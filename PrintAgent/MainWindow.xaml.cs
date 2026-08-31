using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using System.Diagnostics;

namespace PrintAgent
{
    public class PrinterModel : System.ComponentModel.INotifyPropertyChanged
    {
        public string Name { get; set; } = "";
        private bool _isAllowed;
        public bool IsAllowed
        {
            get => _isAllowed;
            set
            {
                if (_isAllowed != value)
                {
                    _isAllowed = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsAllowed)));
                }
            }
        }
        
        private int _copies = 1;
        public int Copies
        {
            get => _copies;
            set
            {
                if (_copies != value)
                {
                    _copies = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Copies)));
                }
            }
        }
        
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }

    public partial class MainWindow : Window
    {
        private System.Windows.Forms.NotifyIcon _notifyIcon;
        private System.Drawing.Icon _iconConnected;
        private System.Drawing.Icon _iconDisconnected;
        private bool _isClosing = false;
        private bool _minimizeToTray = true;
        private bool _showNotifications = true;
        private bool _autoStart = true;
        private string _appSettingsPath = "appsettings.json";
        private System.Collections.ObjectModel.ObservableCollection<PrinterModel> _printers = new();

        public MainWindow()
        {
            InitializeComponent();
            
            _iconConnected = CreateStatusIcon(System.Drawing.Color.LimeGreen);
            _iconDisconnected = CreateStatusIcon(System.Drawing.Color.Red);

            
            LoadSettings();

            // RACE CONDITION FIX: State might have changed before we subscribed
            StatusIndicator.Fill = EventBus.IsConnected ? System.Windows.Media.Brushes.LimeGreen : System.Windows.Media.Brushes.Red;
            StatusText.Text = EventBus.IsConnected ? "Bağlı" : "Bağlantı Yok";
            BtnForceReconnect.Visibility = EventBus.IsConnected ? Visibility.Collapsed : Visibility.Visible;

            SetupNotifyIcon();
            
            EventBus.ConnectionStateChanged += (isConnected) =>
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    StatusIndicator.Fill = isConnected ? System.Windows.Media.Brushes.LimeGreen : System.Windows.Media.Brushes.Red;
                    StatusText.Text = isConnected ? "Bağlı" : "Bağlantı Yok";
                    BtnForceReconnect.Visibility = isConnected ? Visibility.Collapsed : Visibility.Visible;
                    if (_notifyIcon != null)
                    {
                        _notifyIcon.Icon = isConnected ? _iconConnected : _iconDisconnected;
                        _notifyIcon.Text = isConnected ? "PrintAgent - Bağlı" : "PrintAgent - Bağlantı Yok";
                    }
                }));
            };

            EventBus.ActivityLogged += (title, message) =>
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    LogListBox.Items.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {title}: {message}");
                    if (LogListBox.Items.Count > 100)
                    {
                        LogListBox.Items.RemoveAt(LogListBox.Items.Count - 1);
                    }
                    
                    if (_showNotifications && _notifyIcon != null)
                    {
                        _notifyIcon.ShowBalloonTip(3000, title, message, System.Windows.Forms.ToolTipIcon.Info);
                    }
                }));
            };
        }

        private void SetupNotifyIcon()
        {
            _notifyIcon = new System.Windows.Forms.NotifyIcon();
            _notifyIcon.Icon = EventBus.IsConnected ? _iconConnected : _iconDisconnected;
            _notifyIcon.Text = EventBus.IsConnected ? "PrintAgent - Bağlı" : "PrintAgent - Bağlantı Yok";
            _notifyIcon.Visible = true;
            _notifyIcon.DoubleClick += (s, e) => ShowWindow();

            var contextMenu = new System.Windows.Forms.ContextMenuStrip();
            var openItem = new System.Windows.Forms.ToolStripMenuItem("Göster");
            openItem.Click += (s, e) => ShowWindow();
            
            var refreshItem = new System.Windows.Forms.ToolStripMenuItem("Yazıcıları Yenile");
            refreshItem.Click += (s, e) => LoadPrinters();

            var pauseItem = new System.Windows.Forms.ToolStripMenuItem("Yazdırmayı Duraklat");
            pauseItem.Click += (s, e) => 
            {
                bool newState = !EventBus.IsPaused;
                EventBus.SetPauseState(newState);
                pauseItem.Checked = newState;
                ChkPausePrinting.IsChecked = newState;
            };

            EventBus.PauseStateChanged += (isPaused) => 
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    pauseItem.Checked = isPaused;
                    ChkPausePrinting.IsChecked = isPaused;
                }));
            };
            
            var reconnectItem = new System.Windows.Forms.ToolStripMenuItem("Tekrar Bağlan");
            reconnectItem.Click += (s, e) => EventBus.RequestForceReconnect();
            
            var exitItem = new System.Windows.Forms.ToolStripMenuItem("Kapat (Çıkış)");
            exitItem.Click += (s, e) => 
            {
                _isClosing = true;
                this.Close();
            };

            contextMenu.Items.Add(openItem);
            contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            contextMenu.Items.Add(pauseItem);
            contextMenu.Items.Add(refreshItem);
            contextMenu.Items.Add(reconnectItem);
            contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            contextMenu.Items.Add(exitItem);
            _notifyIcon.ContextMenuStrip = contextMenu;
        }

        private void ShowWindow()
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
        }

        private System.Drawing.Icon CreateStatusIcon(System.Drawing.Color color)
        {
            using var bmp = new System.Drawing.Bitmap(16, 16);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.Clear(System.Drawing.Color.Transparent);
                g.FillEllipse(new System.Drawing.SolidBrush(color), 2, 2, 12, 12);
            }
            return System.Drawing.Icon.FromHandle(bmp.GetHicon());
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(_appSettingsPath))
                {
                    var json = File.ReadAllText(_appSettingsPath);
                    var node = JsonNode.Parse(json);
                    var settings = node?["AgentSettings"];

                    if (settings != null)
                    {
                        if (settings["MinimizeToTray"] != null) _minimizeToTray = settings["MinimizeToTray"].GetValue<bool>();
                        if (settings["ShowNotifications"] != null) _showNotifications = settings["ShowNotifications"].GetValue<bool>();
                        if (settings["AutoStart"] != null) _autoStart = settings["AutoStart"].GetValue<bool>();
                        if (settings["HubUrl"] != null) TxtHubUrl.Text = settings["HubUrl"].GetValue<string>();
                    }
                }
            }
            catch { }

            LoadPrinters();

            ChkMinimizeToTray.IsChecked = _minimizeToTray;
            ChkAutoStart.IsChecked = _autoStart;
            ChkShowNotifications.IsChecked = _showNotifications;
        }

        private void LoadPrinters()
        {
            var allowedPrinters = new System.Collections.Generic.List<string>();
            bool allowAll = true;

            try
            {
                if (File.Exists(_appSettingsPath))
                {
                    var json = File.ReadAllText(_appSettingsPath);
                    var node = JsonNode.Parse(json);
                    var settings = node?["AgentSettings"];

                    if (settings != null)
                    {
                        if (settings["AllowedPrinters"] is JsonArray allowedArr)
                        {
                            foreach (var item in allowedArr)
                            {
                                if (item != null) allowedPrinters.Add(item.ToString());
                            }
                            allowAll = allowedPrinters.Count == 0 && settings["AllowedPrinters"] == null;
                        }
                    }
                }
            }
            catch { }

            _printers.Clear();
            foreach (string printer in System.Drawing.Printing.PrinterSettings.InstalledPrinters)
            {
                _printers.Add(new PrinterModel 
                { 
                    Name = printer, 
                    IsAllowed = allowAll || allowedPrinters.Contains(printer) 
                });
            }
            PrintersListBox.ItemsSource = _printers;
        }

        private void BtnRefreshPrinters_Click(object sender, RoutedEventArgs e)
        {
            LoadPrinters();
        }

        private void BtnForceReconnect_Click(object sender, RoutedEventArgs e)
        {
            BtnForceReconnect.IsEnabled = false;
            EventBus.RequestForceReconnect();
            
            // Re-enable after a short delay to prevent spam
            System.Threading.Tasks.Task.Delay(2000).ContinueWith(_ =>
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    BtnForceReconnect.IsEnabled = true;
                }));
            });
        }

        private void PauseSetting_Changed(object sender, RoutedEventArgs e)
        {
            if (!this.IsLoaded) return;
            EventBus.SetPauseState(ChkPausePrinting.IsChecked == true);
        }

        private void PrinterSetting_Changed_Text(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (!this.IsLoaded) return;
            SavePrintersToSettings();
        }

        private void BtnTestPrint_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            string? printerName = btn?.Tag?.ToString();
            
            if (string.IsNullOrEmpty(printerName) && btn?.DataContext != null)
            {
                // Fallback to dynamic or reflection if type match fails
                dynamic dc = btn.DataContext;
                try { printerName = dc.Name; } catch { }
            }
            
            if (!string.IsNullOrEmpty(printerName))
            {
                try
                {
                    var printDoc = new System.Drawing.Printing.PrintDocument();
                    printDoc.PrinterSettings.PrinterName = printerName;
                    
                    if (!printDoc.PrinterSettings.IsValid)
                    {
                        System.Windows.MessageBox.Show("Yazıcıya ulaşılamıyor: " + printerName, "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    printDoc.PrintPage += (s, ev) =>
                    {
                        var font = new System.Drawing.Font("Arial", 14, System.Drawing.FontStyle.Bold);
                        ev.Graphics?.DrawString("PrintAgent Sınama Sayfası\n\nBaşarıyla yazdırıldı!", font, System.Drawing.Brushes.Black, new System.Drawing.PointF(100, 100));
                        ev.HasMorePages = false;
                    };

                    printDoc.Print();
                    EventBus.NotifyActivity("Test", $"{printerName} için sınama sayfası yazdırıldı.");
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show("Sınama sayfası yazdırılamadı: " + ex.Message, "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                string debugInfo = $"btnIsNull: {btn == null}, dcType: {btn?.DataContext?.GetType().Name}, tagType: {btn?.Tag?.GetType().Name}";
                System.Windows.MessageBox.Show($"Yazıcı adı alınamadı. Debug: {debugInfo}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Setting_Changed(object sender, RoutedEventArgs e)
        {
            if (!this.IsLoaded) return;

            _minimizeToTray = ChkMinimizeToTray.IsChecked == true;
            _showNotifications = ChkShowNotifications.IsChecked == true;
            
            bool oldAutoStart = _autoStart;
            _autoStart = ChkAutoStart.IsChecked == true;

            if (oldAutoStart != _autoStart)
            {
                SetStartup(_autoStart);
            }

            try
            {
                if (File.Exists(_appSettingsPath))
                {
                    var json = File.ReadAllText(_appSettingsPath);
                    var node = JsonNode.Parse(json);
                    var settings = node?["AgentSettings"];
                    if (settings != null)
                    {
                        settings["MinimizeToTray"] = _minimizeToTray;
                        settings["ShowNotifications"] = _showNotifications;
                        settings["AutoStart"] = _autoStart;
                        File.WriteAllText(_appSettingsPath, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                    }
                }
            }
            catch (Exception ex) 
            {
                System.Windows.MessageBox.Show("Ayarlar kaydedilirken hata oluştu: " + ex.Message);
            }
        }

        private void BtnSaveHubUrl_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtHubUrl.Text)) return;
            
            try
            {
                if (File.Exists(_appSettingsPath))
                {
                    var json = File.ReadAllText(_appSettingsPath);
                    var node = JsonNode.Parse(json, null, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
                    var settings = node?["AgentSettings"];
                    if (settings != null)
                    {
                        settings["HubUrl"] = TxtHubUrl.Text.Trim();
                        File.WriteAllText(_appSettingsPath, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                        System.Windows.MessageBox.Show("Hub URL güncellendi.\nDeğişikliklerin etkili olması için uygulamayı yeniden başlatmanız gerekmektedir.", "Bilgi");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Hub URL güncellenirken hata oluştu: " + ex.Message, "Hata");
            }
        }

        private void PrinterSetting_Changed(object sender, RoutedEventArgs e)
        {
            if (!this.IsLoaded) return;
            SavePrintersToSettings();
        }

        private void SavePrintersToSettings()
        {
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    if (File.Exists(_appSettingsPath))
                    {
                        var json = File.ReadAllText(_appSettingsPath);
                        var node = JsonNode.Parse(json, null, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
                        var settings = node?["AgentSettings"];
                        if (settings != null)
                        {
                            var allowedArray = new JsonArray();
                            foreach (var p in _printers)
                            {
                                if (p.IsAllowed) allowedArray.Add(p.Name);
                            }
                            settings["AllowedPrinters"] = allowedArray;
                            File.WriteAllText(_appSettingsPath, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                        }
                    }
                    break;
                }
                catch (IOException)
                {
                    if (i == 2)
                    {
                        System.Windows.MessageBox.Show("Yazıcı ayarları kaydedilirken dosya erişim hatası oluştu. Lütfen tekrar deneyin.");
                    }
                    else
                    {
                        System.Threading.Thread.Sleep(100);
                    }
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show("Yazıcı ayarları kaydedilirken hata oluştu: " + ex.Message);
                    break;
                }
            }
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (this.WindowState == WindowState.Minimized && _minimizeToTray)
            {
                this.Hide();
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_isClosing && _minimizeToTray)
            {
                e.Cancel = true;
                this.Hide();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            base.OnClosed(e);
            System.Windows.Application.Current.Shutdown();
        }

        private void SetStartup(bool enable)
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
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Başlangıç ayarı değiştirilemedi: " + ex.Message);
            }
        }
    }
}
