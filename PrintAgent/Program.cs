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
            System.Windows.Forms.Application.SetUnhandledExceptionMode(System.Windows.Forms.UnhandledExceptionMode.CatchException);
            System.Windows.Forms.Application.ThreadException += (s, e) => RestartApp();
            AppDomain.CurrentDomain.UnhandledException += (s, e) => RestartApp();

            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

            // Host'u arka planda başlat
            var builder = Host.CreateApplicationBuilder(args);
            
            // Başlangıçta çalışması için Registry ayarı
            bool autoStart = builder.Configuration.GetValue<bool>("AgentSettings:AutoStart", true);
            SetStartup(autoStart);
            
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

            string uiFramework = builder.Configuration.GetValue<string>("AgentSettings:UIFramework", "WinForms") ?? "WinForms";

            if (uiFramework.Equals("WPF", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    // WPF Uygulamasını başlat
                    var app = new System.Windows.Application();
                    var mainWindow = new MainWindow();
                    
                    app.Exit += async (s, e) =>
                    {
                        await host.StopAsync();
                        Log.CloseAndFlush();
                    };

                    app.Run(mainWindow);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("WPF BAŞLATMA HATASI: " + ex.ToString());
                    Log.Error(ex, "WPF Başlatma Hatası");
                    throw;
                }
            }
            else
            {
                var iconConnected = CreateStatusIcon(System.Drawing.Color.LimeGreen);
                var iconDisconnected = CreateStatusIcon(System.Drawing.Color.Red);

                // Sistem tepsisi ikonunu oluştur
                using (var notifyIcon = new System.Windows.Forms.NotifyIcon())
                {
                    notifyIcon.Icon = EventBus.IsConnected ? iconConnected : iconDisconnected;
                    notifyIcon.Text = EventBus.IsConnected ? "PrintAgent - Bağlı" : "PrintAgent - Bağlantı Yok";
                    notifyIcon.Visible = true;

                    // Sağ tık menüsü
                    var contextMenu = new System.Windows.Forms.ContextMenuStrip();
                    var exitMenuItem = new System.Windows.Forms.ToolStripMenuItem("Kapat (Çıkış)");
                    exitMenuItem.Click += async (s, e) =>
                    {
                        notifyIcon.Visible = false;
                        await host.StopAsync();
                        Log.CloseAndFlush();
                        System.Windows.Forms.Application.Exit();
                    };
                    contextMenu.Items.Add(exitMenuItem);

                    notifyIcon.ContextMenuStrip = contextMenu;

                    var showNotifications = builder.Configuration.GetValue<bool>("AgentSettings:ShowNotifications", true);
                    
                    // Senkronizasyon Context'i (Background Thread'den UI Thread'e geçiş için)
                    var syncContext = new System.Windows.Forms.WindowsFormsSynchronizationContext();
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
                                notifyIcon.ShowBalloonTip(3000, title, message, System.Windows.Forms.ToolTipIcon.Info);
                            }, null);
                        }
                    };

                    // RACE CONDITION FIX: State might have changed before we subscribed
                    notifyIcon.Icon = EventBus.IsConnected ? iconConnected : iconDisconnected;
                    notifyIcon.Text = EventBus.IsConnected ? "PrintAgent - Bağlı" : "PrintAgent - Bağlantı Yok";

                    // WinForms mesaj döngüsünü başlat
                    System.Windows.Forms.Application.Run();
                }
            }
        }

        private static System.Drawing.Icon CreateStatusIcon(System.Drawing.Color color)
        {
            using var bmp = new System.Drawing.Bitmap(16, 16);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.Clear(System.Drawing.Color.Transparent);
                g.FillEllipse(new System.Drawing.SolidBrush(color), 2, 2, 12, 12);
            }
            return System.Drawing.Icon.FromHandle(bmp.GetHicon());
        }

        private static void SetStartup(bool enable)
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
                        if (enable)
                        {
                            key.SetValue(appName, "\"" + exePath + "\"");
                        }
                        else
                        {
                            key.DeleteValue(appName, false);
                        }
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
