# Değişiklik günlüğü

## 1.6.6 (2026-11-17)
- Ses bölümü onarıldı. Kök neden: Mac menü çubuğu (Gravitone) aynı CLSID için kendi `[ComImport] MMDeviceEnumerator`
  sınıfını tanımlıyor ve açılışta önce oluşturuyordu; .NET bu COM nesnesini ilk sınıf türüyle eşlediği için dock'un
  `new MMDeviceEnumerator()` çağrısı InvalidCastException veriyor, hata yutulduğu için aygıt listesi boş, kaydırıcı
  etkisiz kalıyordu. Nesneler artık CoCreateInstance ile doğrudan arayüz olarak alınır; çağrılar adanmış MTA iş
  parçacığında (`ComWorker`), hatalar hata.log'a yazılır.
  Çıkış/giriş aygıtları, varsayılan seçimi (IPolicyConfig; olmazsa ayarlar), gerçek ses/sessiz, mikrofon sessiz,
  klavye ses tuşlarının yansıması.
- Finder tarzı menü çubuğu: Win32 menüsü (WM_COMMAND) → UI Automation menü çubuğu → işlevli genel menüler;
  uygulama menüsü (hakkında, gizle, diğerlerini gizle, çık); masaüstünde Gezgin menüleri (Git, Geri Dönüşüm Kutusu).
- Chrome PWA'ları (WhatsApp Web vb.) AppUserModelID ile ayrı gruplanır; Chrome'a tıklamak PWA penceresini etkilemez.
- İlk açılışta Genie: dock'tan başlatılan uygulama/PWA/dosya/klasörün ilk penceresi simgeden büyüyerek açılır.
- Testler: gruplama, menüler, kısayollar, ilk açılış Genie kuralları, boş kare denetimi, ses iş parçacığı.

## 1.6.5 (2026-11-17)
- Apple tarzı hareket (yay/spring, kesilebilir): hover büyütme, kullanım/ses/disk/sistem panelleri, sağ tık menüleri,
  otomatik gizlenip belirme ve Başlat zıplaması. Paneller dock'tan küçük, bulanık ve saydam başlayıp hafif taşmayla
  oturur; kapanış kısa ve taşmasız. Genie'de küçültme yumuşak başlar, geri açılma hızlı başlayıp yumuşak oturur.
- İlkeler ve değerler: docs/APPLE-HAREKET.md.

## 1.6.4 (2026-11-17)
- Pencerenin kendi küçültme düğmesi ya da Win+Aşağı ile küçültmede de Genie oynar; dock'tan geri açılış Genie ile büyür.
  Öndeki pencerenin görüntüsü arka planda alınır; Windows'un küçültme animasyonu yalnızca o pencere için kapatılır.
  Tam ekranda ve oyunlarda devre dışı.
- Kullanım, ses, disk ve sistem panelleri ile sağ tık menüleri masaüstüne, başka pencereye ya da ikinci monitöre
  tıklayınca ve Esc ile kapanır.

## 1.6.3 (2026-11-17)
- Windows görev çubuğunu kaldırma varsayılan olarak açık; Explorer'ın çubuğu ayrı bir iş parçacığında anında gizlenir,
  tam ekrandan çıkışta kısa süreli görünme giderildi.
- Dock otomatik gizlenir; imleç alt kenara gelince kayarak çıkar.
- Genie, basış anındaki ön pencereyi hedefler.

## 1.6.2 (2026-11-17)
- Klasik dock yeniden varsayılan. Uygulama simgelerinde Mac dalga büyütmesi, açılış sıçraması ve Genie.
- Üst menü çubuğu klasik düzende de açık.

## 1.6.1 (2026-11-17)
- Mac dock ile widget şeritleri tek şeritte, üst üste binmez (daralma, çoklu monitör, DPI).

## 1.6 (2026-11-16)
- Ad Kuti Dock oldu; ayarlar ve Cursor oturumu `%APPDATA%\KutayDock`'tan taşınır.
- Gravitone (MIT) tabanlı Mac dock motoru: dalga büyütme, sıçrama, GPU Genie, akrilik plaka, üst menü çubuğu,
  klasör yığınları, Geri Dönüşüm Kutusu, sürükleyerek sıralama, çoklu monitör, bekçi süreci.

## 1.5 (2026-11-16)
- Büyütme ve sıçrama animasyonları, görev çubuğunu kaldırma, klasör boyutları, Windows optimizasyonu paneli,
  ses çıkış aygıtı seçimi, Cloudflare WARP, tam tarih, Windows benzeri arama.

## 1.4 ve 1.4.1 (2026-11-16)
- Tam ekran videoda gizlenme, sol ve sağ kenarda dikey dock, Everything araması.

## 1.3 – 1.3.2 (2026-11-16)
- İsteğe bağlı Cursor girişi (DPAPI), Grok Bot ve Cursor kullanımı, yer ayırmayan yüzen dock.

## 1.0 – 1.2 (2026-11-16)
- İlk sürüm: dock, widget'lar, ayarlar, tepsi; ardından limitler, depolama, ShareX ve canlı pencere önizlemesi.
