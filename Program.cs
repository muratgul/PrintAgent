using System.Diagnostics;
using Microsoft.Win32;

namespace PrintAgent
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Başlangıçta çalışması için Registry ayarı
            SetStartup();

            // Host'u arka planda başlat
            var builder = Host.CreateApplicationBuilder(args);
            builder.Services.AddHostedService<Worker>();
            var host = builder.Build();

            // Host'u ayrı bir task üzerinde başlatıyoruz (UI'ı bloklamaması için)
            _ = host.RunAsync();

            // Sistem tepsisi ikonunu oluştur
            using (var notifyIcon = new NotifyIcon())
            {
                // Standart bir ikon kullanıyoruz
                notifyIcon.Icon = SystemIcons.Information;
                notifyIcon.Text = "PrintAgent - Yazdırma Servisi";
                notifyIcon.Visible = true;

                // Sağ tık menüsü
                var contextMenu = new ContextMenuStrip();
                var exitMenuItem = new ToolStripMenuItem("Kapat (Çıkış)");
                exitMenuItem.Click += async (s, e) =>
                {
                    notifyIcon.Visible = false;
                    await host.StopAsync();
                    Application.Exit();
                };
                contextMenu.Items.Add(exitMenuItem);

                notifyIcon.ContextMenuStrip = contextMenu;

                // WinForms mesaj döngüsünü başlat
                Application.Run();
            }
        }

        private static void SetStartup()
        {
            try
            {
                string appName = "VuePrintAgent";
                string exePath = Process.GetCurrentProcess().MainModule?.FileName;
                
                if (exePath == null) return;

                using (RegistryKey key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true))
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
    }
}
