using System.Diagnostics;
using Microsoft.Win32;
using Serilog;

namespace PrintAgent
{
    static class Program
    {
        private static Mutex? _mutex;

        [STAThread]
        static void Main(string[] args)
        {
            _mutex = new Mutex(true, "PrintAgentSingleInstanceMutex", out bool createdNew);
            if (!createdNew)
            {
                // Zaten çalışıyor, çıkış yap.
                return;
            }

            // Global Hata Yakalama ve Yeniden Başlatma
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => RestartApp();
            AppDomain.CurrentDomain.UnhandledException += (s, e) => RestartApp();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Başlangıçta çalışması için Registry ayarı
            SetStartup();

            // Host'u arka planda başlat
            var builder = Host.CreateApplicationBuilder(args);
            
            // Serilog konfigürasyonu
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(builder.Configuration)
                .CreateLogger();
            builder.Logging.ClearProviders();
            builder.Logging.AddSerilog(Log.Logger);

            builder.Services.AddHostedService<Worker>();
            var host = builder.Build();

            // Host'u ayrı bir task üzerinde başlatıyoruz (UI'ı bloklamaması için)
            _ = host.RunAsync();

            var iconConnected = CreateStatusIcon(Color.LimeGreen);
            var iconDisconnected = CreateStatusIcon(Color.Red);

            // Sistem tepsisi ikonunu oluştur
            using (var notifyIcon = new NotifyIcon())
            {
                notifyIcon.Icon = iconDisconnected;
                notifyIcon.Text = "PrintAgent - Bağlantı Yok";
                notifyIcon.Visible = true;

                // Sağ tık menüsü
                var contextMenu = new ContextMenuStrip();
                var exitMenuItem = new ToolStripMenuItem("Kapat (Çıkış)");
                exitMenuItem.Click += async (s, e) =>
                {
                    notifyIcon.Visible = false;
                    await host.StopAsync();
                    Log.CloseAndFlush();
                    Application.Exit();
                };
                contextMenu.Items.Add(exitMenuItem);

                notifyIcon.ContextMenuStrip = contextMenu;

                var showNotifications = builder.Configuration.GetValue<bool>("AgentSettings:ShowNotifications", true);
                
                // Senkronizasyon Context'i (Background Thread'den UI Thread'e geçiş için)
                var syncContext = new WindowsFormsSynchronizationContext();
                SynchronizationContext.SetSynchronizationContext(syncContext);

                EventBus.ConnectionStateChanged += (isConnected) =>
                {
                    syncContext.Post(_ => 
                    {
                        notifyIcon.Icon = isConnected ? iconConnected : iconDisconnected;
                        notifyIcon.Text = isConnected ? "PrintAgent - Bağlı" : "PrintAgent - Bağlantı Yok";
                    }, null);
                };

                EventBus.ActivityLogged += (title, message) =>
                {
                    if (showNotifications)
                    {
                        syncContext.Post(_ => 
                        {
                            notifyIcon.ShowBalloonTip(3000, title, message, ToolTipIcon.Info);
                        }, null);
                    }
                };

                // WinForms mesaj döngüsünü başlat
                Application.Run();
            }
        }

        private static Icon CreateStatusIcon(Color color)
        {
            using var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.FillEllipse(new SolidBrush(color), 2, 2, 12, 12);
            }
            return Icon.FromHandle(bmp.GetHicon());
        }

        private static void SetStartup()
        {
            try
            {
                string appName = "VuePrintAgent";
                string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
                
                if (exePath == null) return;

                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true))
                {
                    if (key != null)
                    {
                        key.SetValue(appName, "\"" + exePath + "\"");
                    }
                }
            }
            catch
            {
                // İzin veya başka bir hata olursa yoksay
            }
        }

        private static void RestartApp()
        {
            try
            {
                string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (exePath != null)
                {
                    Process.Start(exePath);
                }
            }
            catch
            {
            }
            finally
            {
                Environment.Exit(-1);
            }
        }
    }
}
