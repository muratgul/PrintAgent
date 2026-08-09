# PrintAgent & Merkezi Yönetim Sistemi (Central Management System)

PrintAgent, merkezi bir sunucudan (SignalR aracılığıyla) gelen yazdırma komutlarını dinleyen ve istenilen belgeyi doğrudan hedef bilgisayarın yazıcılarına gönderen, 7/24 çalışacak şekilde tasarlanmış (Always-On) bir arka plan Windows servis uygulamasıdır. 

Bu depo (repository) sadece **PrintAgent** istemcisini değil, aynı zamanda sistemin nasıl çalıştığını tam olarak test edebilmeniz için **Merkezi SignalR Sunucusu (.NET Core)** ve **Yönetim Paneli (Vue 3)** örnek projelerini de barındırmaktadır.

---

## 🇹🇷 Türkçe 

### 🏗️ Sistem Mimarisi

Sistem 3 ana bileşenden oluşur:

1. **PrintAgent (Windows Client):** Yazdırma işlemini yapacak olan bilgisayara kurulan, WinForms (Tray Icon) veya WPF (Pencere) modunda çalışabilen servis.
2. **ExamplePrintHub (SignalR Server):** Tüm PrintAgent'ların bağlandığı, onları dinleyen ve arayüzlerden gelen komutları ilgili ajanlara yönlendiren köprü (Hub) sunucu.
3. **Yönetim İstemcileri (Web veya Masaüstü):**
   - **ExampleVueClient (Web UI):** Tüm sisteme hakim olan web tabanlı kontrol paneli. 
   - **ExampleWinFormsClient (Masaüstü UI):** Web paneline alternatif olarak işlemleri masaüstü uygulamasından (Windows Forms) tetiklemek ve yönetmek isteyenler için örnek istemci uygulaması.

### 🚀 Yeni Eklenen Özellikler & Güncellemeler (v2.0)

- **WPF Arayüz Desteği:** İstenildiğinde sadece Sistem Tepsisinde (Tray) çalışmak yerine, ayarların (`appsettings.json`'da `UIFramework` ayarını `WPF` yaparak) modern bir pencere üzerinden yönetilebilmesi sağlandı.
- **Kullanıcı Dostu Ayar Kontrolleri (WPF):** 
  - **Tepsiye Küçült (Minimize to Tray):** Pencere kapatıldığında uygulamanın sistem tepsisine gizlenmesini sağlar.
  - **Otomatik Başlat (Auto Start):** İşletim sistemi başladığında uygulamanın da arka planda çalışmasını sağlar (Registry üzerinden yönetilir).
  - **Bildirimleri Göster (Show Notifications):** Bağlantı kopmaları ve yeniden bağlanma gibi durumları sağ alt köşede Windows bildirim balonu olarak gösterir.
- **Büyük Boyutlu Dosya Aktarımı Desteği:** Hub Server (`ExamplePrintHub`) üzerinde yer alan `MaxRequestBodySize` ve SignalR `MaximumReceiveMessageSize` limitleri **500 MB**'a çıkartıldı.
- **Timeout (Zaman Aşımı) ve Yarış Durumu (Race Condition) İyileştirmeleri:** Client bağlantılarında 5 dakikalık tolerans (ServerTimeout) eklendi. Ayrıca uygulamanın açılışı esnasında hızlıca sunucuya bağlandığında arayüz ikonunun kırmızı (bağlı değil) kalmasına neden olan yarış durumu (Race Condition) giderildi.
- **WPF UI Kilitlenme (Deadlock) ve Kaynak Sızıntısı (Memory Leak) Düzeltmesi:** Arka planda SignalR üzerinden bağlantı durumu değiştiğinde WPF arayüzünün güncellenememesi ve kilitlenmesi (Dispatcher.Invoke) sorunu `BeginInvoke` kullanılarak çözüldü. Ayrıca GDI kaynaklarını (Handle) tüketen ikon oluşturma mantığı cache (önbellek) yapısına geçirilerek performans/bellek sızıntısı iyileştirildi.

---

## 🇬🇧 English

### 🏗️ System Architecture

The system consists of 3 main components:

1. **PrintAgent (Windows Client):** A service installed on the target machine that performs the printing operations. It can run in WinForms (Tray Icon) or WPF (Window) mode.
2. **ExamplePrintHub (SignalR Server):** The bridge (Hub) server that all PrintAgents connect to. It listens for commands from clients and routes them to the appropriate agents.
3. **Management Clients (Web or Desktop):**
   - **ExampleVueClient (Web UI):** A comprehensive web-based control panel to monitor the system.
   - **ExampleWinFormsClient (Desktop UI):** An alternative desktop application (Windows Forms) to trigger and manage operations.

### 🚀 New Features & Updates (v2.0)

- **WPF UI Support:** In addition to the System Tray mode, you can now manage settings via a modern window by setting `UIFramework` to `WPF` in `appsettings.json`.
- **User-Friendly Settings Controls (WPF):** 
  - **Minimize to Tray:** Hides the application to the system tray when the window is closed or minimized.
  - **Auto Start:** Allows the application to automatically start in the background when the OS boots up (managed via Registry).
  - **Show Notifications:** Displays Windows balloon notifications in the system tray for events like disconnections and reconnections.
- **Large File Transfer Support:** The `MaxRequestBodySize` and SignalR `MaximumReceiveMessageSize` limits on the Hub Server (`ExamplePrintHub`) have been increased to **500 MB** to allow printing of very large files.
- **Timeout and Race Condition Improvements:** A 5-minute tolerance (`ServerTimeout`) was added to client connections to prevent timeout issues when transferring large files. Additionally, a race condition bug where the UI icon would remain red (disconnected) during an extremely fast connection on startup has been fixed.
- **WPF UI Deadlock and Memory Leak Fix:** Resolved an issue where the WPF interface would fail to update or lock up (due to Dispatcher.Invoke) when the background SignalR connection state changed, by switching to `BeginInvoke`. Additionally, improved performance and prevented memory/handle leaks by caching the unmanaged GDI icons instead of recreating them on every state change.

---

## 🛠️ Kurulum ve Test / Setup & Testing

**1. ExamplePrintHub**
```bash
cd ExamplePrintHub
dotnet run
```
**2. PrintAgent**
Check `appsettings.json` for `HubUrl` and `UIFramework` settings, then run:
```bash
cd PrintAgent
dotnet run
```
**3. ExampleVueClient**
```bash
cd ExampleVueClient
npm install
npm run dev
```

---

_Not: Proje içerisindeki örnek klasörleri (.NET derlemesinde çakışma olmaması için) ana `PrintAgent.csproj` içerisinden `<DefaultItemExcludes>` kullanılarak hariç tutulmuştur._
_Note: Sample folders within the project are excluded from the main `PrintAgent.csproj` using `<DefaultItemExcludes>` to prevent build conflicts._
