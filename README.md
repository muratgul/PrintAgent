# PrintAgent

PrintAgent, merkezi bir sunucudan (SignalR aracılığıyla) gelen yazdırma komutlarını dinleyen ve istenilen belgeyi doğrudan hedef bilgisayarın yazıcılarına gönderen, 7/24 çalışacak şekilde tasarlanmış (Always-On) bir arka plan Windows servis uygulamasıdır. 

## Özellikler

- **Gelişmiş Format Desteği:** PDF (PdfiumViewer ile native yazdırılır), Word (.doc, .docx), Excel (.xls, .xlsx), Metin Dosyaları (.txt) ve diğer genel Windows dosya formatlarını destekler. Gelen verinin `Base64`, `URL` veya `Data URI` olmasından bağımsız olarak sihirli byte'larına (Magic Numbers) bakarak otomatik algılar ve Windows `ShellExecute` (PrintTo) özelliği ile arka planda sessizce yazdırır.
- **Kopmaz Bağlantı (Auto-Reconnect):** İnternet veya sunucu kesintilerinde SignalR üzerinden otomatik yeniden bağlanma stratejisi uygular. Çok uzun süreli (saatlerce süren) kopmalarda dahi bağlantıyı yeniden sağlamak için sonsuz bir manuel tetikleme döngüsü (fallback) içerir.
- **Tekli Çalışma (Single Instance):** Mutex yapısı ile uygulamanın aynı bilgisayarda yanlışlıkla birden fazla kez açılması ve çakışması önlenir.
- **Hata Toleransı ve Auto-Restart:** İşletim sistemi veya uygulama düzeyinde oluşabilecek beklenmedik kritik hatalarda (`UnhandledException`) uygulamanın sessizce kapanıp yok olması yerine otomatik olarak kendi kendini yeniden başlatmasını sağlayan global hata yakalayıcılara sahiptir.
- **Windows Bildirimleri & Görsel Durum:** Görev çubuğunda (System Tray) uygulamanın anlık bağlantı durumunu görsel renklerle (Bağlıysa Yeşil, Değilse Kırmızı ikon) gösterir. Ayrıca gelen yazdırma komutlarını Windows Balon Bildirimleri (Toast Notifications) olarak ekranın sağ alt köşesinde belirtir.
- **Fiziksel Loglama (Serilog):** Uygulama faaliyetleri disk üzerinde günlük periyotlarla `Logs/printagent-{date}.txt` formatında saklanır. Disk şişmesini önlemek için 7 günden eski loglar otomatik temizlenir.
- **Otomatik Başlangıç:** Windows Registry (Kayıt Defteri) üzerine kendini kaydederek bilgisayar (oturum) açıldığında müdahaleye gerek kalmadan otomatik başlar.

## Yapılandırma (`appsettings.json`)

Ayarlar dosyası üzerinden loglama seviyesini ve bildirimleri özelleştirebilirsiniz:

```json
{
  "Serilog": {
    "MinimumLevel": "Information",
    "WriteTo": [
      {
        "Name": "Console"
      },
      {
        "Name": "File",
        "Args": {
          "path": "Logs/printagent-.txt",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 7
        }
      }
    ]
  },
  "AgentSettings": {
    "ShowNotifications": true 
  }
}
```
*Gelen yazdırma bildirimlerini kapatmak isterseniz `ShowNotifications` değerini `false` yapabilirsiniz.*

## Teknik Altyapı
- **.NET 10.0** (Worker Service & Windows Forms altyapısı melez olarak kullanılmıştır)
- **SignalR Client** (Merkezi sunucu ile Real-Time çift yönlü haberleşme)
- **Serilog** (Gelişmiş asenkron loglama)
- **PdfiumViewer** (Bağımsız PDF işleme ve render motoru)
