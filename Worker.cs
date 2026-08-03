using Microsoft.AspNetCore.SignalR.Client;
using System.Diagnostics;
using System.Drawing.Printing;

namespace PrintAgent;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private HubConnection? _hubConnection;
    private readonly string _agentName = Environment.MachineName; // Or configure via appsettings.json

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
    }

    protected async override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Merkezi Sunucunun (GenelIslemlerApi01) SignalR Hub adresi
        var hubUrl = "http://localhost:5200/printhub";
        //var hubUrl = "http://localhost:5193/printhub";

        _hubConnection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30) })
            .Build();

        _hubConnection.Closed += async (error) =>
        {
            EventBus.NotifyConnectionState(false);
            _logger.LogWarning(error, "Bağlantı kapandı! Döngü üzerinden yeniden bağlanılmaya çalışılacak...");
            await Task.CompletedTask;
        };

        _hubConnection.Reconnecting += async (error) =>
        {
            EventBus.NotifyConnectionState(false);
            EventBus.NotifyActivity("Bağlantı", "Bağlantı koptu, otomatik yeniden bağlanılıyor...");
            _logger.LogWarning(error, "Bağlantı koptu, otomatik yeniden bağlanılıyor...");
            await Task.CompletedTask;
        };

        _hubConnection.Reconnected += async (connectionId) =>
        {
            EventBus.NotifyConnectionState(true);
            EventBus.NotifyActivity("Bağlantı", "Yeniden bağlandı.");
            _logger.LogInformation("Otomatik yeniden bağlantı başarılı (Reconnected)! Ajan Adı: {AgentName}", _agentName);
            try
            {
                // Yeniden bağlandığında kendini tekrar merkeze kaydettir
                await _hubConnection.SendAsync("RegisterAgent", _agentName, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reconnected sonrası RegisterAgent başarısız oldu.");
            }
        };

        // Merkezi sunucudan gelen "GetPrinters" isteğini dinler
        _hubConnection.On<string>("GetPrinters", async (correlationId) =>
        {
            EventBus.NotifyActivity("Bilgi", "Sunucu yazıcı listesini istedi.");
            _logger.LogInformation("Yazıcı listesi istendi. İstek ID: {CorrelationId}", correlationId);
            
            var printers = new List<string>();
            foreach (string printer in PrinterSettings.InstalledPrinters)
            {
                printers.Add(printer);
            }

            // Listeyi merkeze geri yolla
            if (_hubConnection.State == HubConnectionState.Connected)
            {
                await _hubConnection.SendAsync("SendPrintersList", _agentName, correlationId, printers, stoppingToken);
            }
        });

        // Merkezi sunucudan gelen yazdırma komutunu dinler
        _hubConnection.On<string, string, string, string, string>("PrintCommand", async (logId, callerId, printerName, data, documentName) =>
        {
            EventBus.NotifyActivity("Yazdırma İsteği", $"Belge: {documentName}\nYazıcı: {printerName}");
            _logger.LogInformation("Yazdırma komutu alındı. Hedef Yazıcı: {PrinterName}, Belge: {DocumentName}", printerName, documentName);
            
            try
            {
                await PrintData(printerName, data, documentName);
                if (_hubConnection.State == HubConnectionState.Connected)
                {
                    await _hubConnection.SendAsync("ReportPrintStatus", logId, callerId, true, "Yazdırma işlemi başarılı.", documentName, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Yazdırma sırasında hata oluştu!");
                if (_hubConnection.State == HubConnectionState.Connected)
                {
                    await _hubConnection.SendAsync("ReportPrintStatus", logId, callerId, false, ex.Message, documentName, stoppingToken);
                }
            }
        });

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_hubConnection.State == HubConnectionState.Disconnected)
                {
                    try
                    {
                        _logger.LogInformation("SignalR Hub'a bağlanılıyor...");
                        await _hubConnection.StartAsync(stoppingToken);
                        
                        EventBus.NotifyConnectionState(true);
                        EventBus.NotifyActivity("Bağlantı", "Sunucuya başarıyla bağlanıldı.");
                        _logger.LogInformation("Bağlantı başarılı! Ajan Adı: {AgentName}", _agentName);

                        // Ajan kendini merkeze kaydettirir
                        await _hubConnection.SendAsync("RegisterAgent", _agentName, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        EventBus.NotifyConnectionState(false);
                        _logger.LogError(ex, "Bağlantı hatası. 5 saniye sonra tekrar denenecek...");
                    }
                }

                await Task.Delay(5000, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Döngü içerisinde beklenmeyen hata oluştu. Devam ediliyor...");
                try { await Task.Delay(5000, stoppingToken); } catch { }
            }
        }
    }

    private async Task PrintData(string printerName, string data, string documentName)
    {
        // 1. Gelen data bir URL mi?
        if (data.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || data.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("URL algılandı, dosya indiriliyor: {Url}", data);
            using var httpClient = new HttpClient();
            var fileBytes = await httpClient.GetByteArrayAsync(data);
            await ProcessAndPrintBytesAsync(printerName, fileBytes, documentName);
            return;
        }

        // 2. Gelen data Data URI formatında mı? (örn: data:application/vnd...;base64,...)
        if (data.StartsWith("data:") && data.Contains(";base64,"))
        {
            _logger.LogInformation("Data URI algılandı, işleniyor...");
            var base64Data = data.Substring(data.IndexOf(";base64,") + 8);
            var fileBytes = Convert.FromBase64String(base64Data);
            await ProcessAndPrintBytesAsync(printerName, fileBytes, documentName);
            return;
        }

        // 3. Sadece Base64 mü? 
        // JVBERi0 = %PDF- (PDF)
        // UEsDB = PK.. (DOCX, XLSX, ZIP tabanlı)
        // 0M8R4 = D0 CF 11.. (Eski DOC, XLS)
        if (data.StartsWith("JVBERi0") || data.StartsWith("UEsDB") || data.StartsWith("0M8R4"))
        {
            _logger.LogInformation("Raw Base64 dosya algılandı, işleniyor...");
            var fileBytes = Convert.FromBase64String(data);
            await ProcessAndPrintBytesAsync(printerName, fileBytes, documentName);
            return;
        }

        // 4. Yukarıdakilere uymuyorsa Normal Metin (Standart Yazdırma)
        _logger.LogInformation("Metin verisi algılandı, standart yöntemle yazdırılıyor...");
        PrintPlainText(printerName, data, documentName);
    }

    private async Task ProcessAndPrintBytesAsync(string printerName, byte[] fileBytes, string documentName)
    {
        string extension = Path.GetExtension(documentName)?.ToLowerInvariant();

        // Eğer uzantı yoksa ama data PDF imzası taşıyorsa PDF kabul et
        if (string.IsNullOrEmpty(extension))
        {
            if (fileBytes.Length > 4 && fileBytes[0] == 0x25 && fileBytes[1] == 0x50 && fileBytes[2] == 0x44 && fileBytes[3] == 0x46)
            {
                extension = ".pdf";
                documentName += ".pdf";
            }
        }

        if (extension == ".pdf")
        {
            PrintPdfBytes(printerName, fileBytes, documentName);
            return;
        }

        // PDF değilse (Örn: Word, Excel, TXT, PNG vs.), geçici bir dosyaya kaydet ve Windows'un 
        // varsayılan yazdırma aracıyla (ShellExecute PrintTo) yazdır.
        string tempFileName = Path.GetTempFileName();
        string tempFilePath = Path.ChangeExtension(tempFileName, extension); 
        
        if (tempFilePath != tempFileName)
        {
            File.Move(tempFileName, tempFilePath); // Uzantıyı düzelt
        }

        try
        {
            await File.WriteAllBytesAsync(tempFilePath, fileBytes);
            _logger.LogInformation("Dosya geçici konuma kaydedildi: {Path}, ShellExecute ile yazdırılıyor...", tempFilePath);
            PrintWithShellExecute(printerName, tempFilePath);
        }
        finally
        {
            // Yazdırma işleminin (Örn Word'ün) dosyayı okuması için biraz bekleyip geçici dosyayı siliyoruz
            _ = Task.Run(async () => 
            {
                await Task.Delay(15000); 
                try { File.Delete(tempFilePath); } catch { }
            });
        }
    }

    private void PrintWithShellExecute(string printerName, string filePath)
    {
        var info = new ProcessStartInfo
        {
            FileName = filePath,
            Verb = "PrintTo",
            Arguments = $"\"{printerName}\"",
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            UseShellExecute = true
        };

        try
        {
            using var process = Process.Start(info);
            if (process != null)
            {
                process.WaitForExit(10000); // 10 saniye bekle
            }
            _logger.LogInformation("Genel format yazdırıldı: {FilePath}", filePath);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            if (ex.NativeErrorCode == 1155) // ERROR_NO_ASSOCIATION
            {
                throw new Exception($"'{Path.GetExtension(filePath)}' formatını yazdırmak için sistemde varsayılan bir uygulama bulunamadı (Örn: Word veya Excel yüklü olmayabilir).", ex);
            }
            throw;
        }
    }

    private void PrintPlainText(string printerName, string data, string documentName)
    {
        var printDocument = new PrintDocument();
        printDocument.PrinterSettings.PrinterName = printerName;
        printDocument.DocumentName = documentName;

        if (!printDocument.PrinterSettings.IsValid)
        {
            _logger.LogWarning("Geçersiz yazıcı seçildi: {PrinterName}", printerName);
            throw new Exception("Geçersiz yazıcı veya yazıcıya ulaşılamıyor.");
        }

        printDocument.PrintPage += (sender, e) =>
        {
            var font = new Font("Arial", 12);
            var brush = new SolidBrush(Color.Black);
            e.Graphics?.DrawString(data, font, brush, new PointF(10, 10));
        };

        printDocument.Print();
        _logger.LogInformation("Metin belgesi yazdırıldı: {DocumentName}", documentName);
    }

    private void PrintPdfBytes(string printerName, byte[] pdfBytes, string documentName)
    {
        using var memoryStream = new MemoryStream(pdfBytes);
        using var pdfDocument = PdfiumViewer.PdfDocument.Load(memoryStream);
        
        // Yazıcı kenar boşluklarına göre daraltarak sığdırır (Taşmaları önler)
        using var printDocument = pdfDocument.CreatePrintDocument(PdfiumViewer.PdfPrintMode.ShrinkToMargin);
        printDocument.PrinterSettings.PrinterName = printerName;
        printDocument.DocumentName = documentName;

        if (!printDocument.PrinterSettings.IsValid)
        {
            _logger.LogWarning("Geçersiz yazıcı seçildi: {PrinterName}", printerName);
            throw new Exception("Geçersiz yazıcı veya yazıcıya ulaşılamıyor.");
        }

        printDocument.Print();
        _logger.LogInformation("PDF Belgesi başarıyla yazdırıldı: {DocumentName}", documentName);
    }
}
