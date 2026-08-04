# PrintAgent Örnek Projeleri

Bu klasör, `PrintAgent` projesinin nasıl çalıştığını test etmek ve anlamak için hazırlanmış iki örnek proje içerir:

1. **ExamplePrintHub (SignalR Sunucusu):** PrintAgent'ın bağlanacağı ve Vue istemcisi ile haberleşmeyi sağlayacak merkezi bir .NET Core SignalR sunucusudur.
2. **ExampleVueClient (Vue 3 İstemcisi):** Sunucuya bağlanarak aktif PrintAgent'ları listeleyen, yazıcılarını çeken ve onlara yazdırma komutu gönderen modern bir web arayüzüdür.

## Nasıl Çalıştırılır?

### 1. ExamplePrintHub (SignalR Sunucusu)
Bu proje, ajanın bağlanacağı merkezi sunucuyu temsil eder.
- Terminal'de `ExamplePrintHub` klasörüne gidin.
- `dotnet run` komutu ile projeyi başlatın.
- Sunucu varsayılan olarak `http://localhost:5200` adresinde çalışacak şekilde ayarlanmıştır ve `/printhub` endpoint'inde SignalR bağlantılarını dinler.

### 2. PrintAgent'ı Yapılandırma
Ana `PrintAgent` projesinin SignalR sunucusuna (ExamplePrintHub) bağlanması için `appsettings.json` dosyasını güncellemelisiniz:
```json
{
  "AgentSettings": {
    "HubUrl": "http://localhost:5200/printhub",
    "ShowNotifications": true,
    "AutoStart": false
  }
}
```
Ardından `PrintAgent` uygulamasını başlatın. Windows görev çubuğunda (System Tray) yeşil bir ikon görecek ve bağlantı başarılı balon bildirimini alacaksınız.

### 3. ExampleVueClient (Web Arayüzü)
Bu proje, sistemi yönetmek ve test etmek için bir Vue 3 uygulamasıdır.
- Terminal'de `ExampleVueClient` klasörüne gidin.
- (Eğer yapmadıysanız) `npm install` komutuyla bağımlılıkları yükleyin.
- `npm run dev` komutu ile projeyi başlatın.
- Tarayıcınızda Vue uygulamasını açın (genellikle `http://localhost:5173`).
- Ekranda "Bağlı Ajanlar" altında çalışan PrintAgent'ı göreceksiniz. Ajana tıklayarak yazıcılarını listeleyebilir ve deneme yazdırma komutları (Metin, Base64 dosya veya URL üzerinden PDF vb.) gönderebilirsiniz.
