using System.Drawing.Printing;
using Microsoft.AspNetCore.SignalR.Client;
using System.Drawing;

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
            .WithAutomaticReconnect()
            .Build();

        // Merkezi sunucudan gelen "GetPrinters" isteğini dinler
        _hubConnection.On<string>("GetPrinters", async (correlationId) =>
        {
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
            if (_hubConnection.State == HubConnectionState.Disconnected)
            {
                try
                {
                    _logger.LogInformation("SignalR Hub'a bağlanılıyor...");
                    await _hubConnection.StartAsync(stoppingToken);
                    _logger.LogInformation("Bağlantı başarılı! Ajan Adı: {AgentName}", _agentName);

                    // Ajan kendini merkeze kaydettirir
                    await _hubConnection.SendAsync("RegisterAgent", _agentName, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Bağlantı hatası. 5 saniye sonra tekrar denenecek...");
                }
            }

            await Task.Delay(5000, stoppingToken);
        }
    }

    private async Task PrintData(string printerName, string data, string documentName)
    {
        // 1. Gelen data bir PDF URL'si mi?
        if (data.StartsWith("http://") || data.StartsWith("https://"))
        {
            _logger.LogInformation("URL algılandı, PDF indiriliyor: {Url}", data);
            using var httpClient = new HttpClient();
            var pdfBytes = await httpClient.GetByteArrayAsync(data);
            PrintPdfBytes(printerName, pdfBytes, documentName);
            return;
        }

        // 2. Gelen data Base64 formatında bir PDF mi? (PDF Base64 formati JVBERi0 ile baslar)
        if (data.StartsWith("JVBERi0") || data.StartsWith("data:application/pdf;base64,"))
        {
            _logger.LogInformation("Base64 PDF algılandı, işleniyor...");
            var base64String = data;
            if (data.StartsWith("data:application/pdf;base64,"))
            {
                base64String = data.Substring("data:application/pdf;base64,".Length);
            }
            
            var pdfBytes = Convert.FromBase64String(base64String);
            PrintPdfBytes(printerName, pdfBytes, documentName);
            return;
        }

        // 3. Normal Metin (Standart Yazdırma)
        _logger.LogInformation("Metin verisi algılandı, standart yöntemle yazdırılıyor...");
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
