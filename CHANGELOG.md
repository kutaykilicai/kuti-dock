# Değişiklik günlüğü

## 1.6.11 (2026-11-18)
- Arama panelinde, kutunun altında küçük bir **Everything** anahtarı (Apple tarzı). Ayarlar'daki "Aramada Everything
  kullan" ile aynı ayar (`SearchWithEverything`), değişince `ayarlar.json`'a yazılır. Açılınca Everything çalışmıyorsa
  dock onu arka planda başlatır (`Everything.exe -startup`, pencere açılmaz) ve dosyaları Everything ile arar; kapatılınca
  Windows aramasına döner ve yalnızca dock'un kendi başlattığı Everything'i `-exit` ile kapatır. Böylece Everything'in
  Windows açılışında başlaması gerekmez.
- WARP: Cloudflare WARP artık Windows açılışında başlamayabilir. Dock'taki WARP'a bağlanmak için tıklanınca arayüzü
  (tepsi) çalışmıyorsa önce o başlatılır, sonra `warp-cli connect`. Bağlantıyı keserken arayüz açılmaz.
- Testler: Everything.exe'nin yeri (Program Files, sonra x86), başlatma/kapatma argümanları, WARP arayüzü yalnızca bağlanırken.

## 1.6.10 (2026-11-17)
- Sıfırdan açılan uygulamalarda Genie çoğu zaman oynamıyordu. Kök neden: hata.log'da "İlk açılış Genie: bulundu
  (Notepad)" ardından "atlandı (Notepad): simge hedefi yok". Simge hedefi `IsVisible` şartı arıyordu; dock genelde
  kenarda saklı (görünmez pencere) olduğu için hedef hiç bulunamıyordu.
- Hedef artık dock saklıyken de hesaplanır (`LaunchTarget`): dock penceresinin ekrandaki yeri + simgenin pencere içindeki
  yeri × o ekranın DPI'si; kayma animasyonunun ötelemesi çıkarılır (simgenin dock açıldığındaki yeri). Simgesi olmayan
  yeni uygulamada hedef, simgenin dock'ta belireceği yer (son simgenin ardı).
- Mac'teki gibi: ilk açılışta saklı dock Genie süresince yay animasyonuyla kısa süre belirir (1,5 sn tutulur, sonra her
  zamanki 0,6 sn gecikmeyle saklanır). Tam ekranda, oyun öndeyken, kullanıcı gizlediyse ya da animasyonlar kapalıysa
  gösterilmez. hata.log: "İlk açılış Genie: dock kısa süre gösterildi".
- Dock araması ("word" yazınca yalnızca .grok\bundled\skills altındaki xsd/tcl/jdk dosyaları): Everything ayarı doğru okunuyordu
  (kapalı; Everything çağrılmıyordu). Kök neden: Windows Search dizini `SCOPE='file:'` ile tüm dizinde aranıyordu (program
  ve araç klasörleri dahil); Kasa'da Microsoft Word kurulu olmadığından Uygulamalar boş kalıyordu. Düzeltme: sıralama
  her iki kipte Uygulamalar → Ayarlar/Denetim Masası → Dosyalar; uygulamalar Uygulamalar klasörü (UWP/Mağaza/Office) +
  Başlat menüsü kısayolları (kullanıcı ve ortak), kelime tam eşleşmesi (90: "word" → "Microsoft Word") ve baş harf
  eşleşmesi; dosyalar yalnızca Masaüstü/Belgeler/İndirilenler/Resimler, AppData/.grok/Tools/dist/node_modules/sistem
  klasörleri hariç (Everything açıkken de). Uygulama bulunamazsa durum satırı söyler. Enter en üstteki öğeyi açar.
- Testler: gizli dock hedefi (kayma çıkarılır), DPI, yeni simge yeri, gösterim koşulları, tutma ve saklanma süresi.

## 1.6.9 (2026-11-17)
- Dock bulunduğu ekrana sığar (`DockFit`): 1.6.8'de ikinci ekranda (1920 px, 96 dpi) 2015 px'lik şerit taşıyordu.
  Kullanılabilir genişlik = çalışma alanı − 2×8 DIP (o ekranın DPI'siyle). Sığmazsa: orantılı ölçek (≥ %70) → ağ/WARP/
  disk/CPU-RAM kompakt grup (2 satır, %50) → taşma menüsü "»" (ekran görüntüsü, pil, tepsi, kompakt grup, AI, arama
  sırasıyla). Saat, ses, uygulamalar, Başlat hep görünür. Sığan ekranda değişiklik yok. Kararlar hata.log'a ("Dock yerleşim").
- Hover büyütme katmanı şeridin ekranına kırpılır, büyüyen simge kenarda içeri itilir.
- Testler: Kasa'nın gerçek iki ekranı ve 2015 px şerit, DPI'ya göre kullanılabilir genişlik, kompakt grup ve taşma.

## 1.6.8 (2026-11-17)
- Dock fareyi takip eder (çoklu ekran): saklı dock, fare hangi monitörün dış dock kenarına giderse orada belirir
  (MonitorFromPoint, GetDpiForMonitor, Per-Monitor V2 piksel yerleşimi). Ekranlar arası ortak kenar tetiklemez
  (`DockMonitors.AtOuterEdge`). Tam ekran/oyun monitör başına (`ForegroundIsFullscreenOn`). Arama paneli ve
  önizleme dock'un ekranında. Ayarlar: "Dock fareyi takip etsin" (varsayılan açık), "Yalnızca ana ekran".
- Testler: Kasa'nın gerçek ekran düzeni, ortak kenar, monitör seçimi, DPI dönüşümü, varsayılan ayarlar.

## 1.6.7 (2026-11-17)
- WhatsApp Web PWA dock'tan küçültülemiyordu. Kök neden: Chrome pencere AUMID'sinde uygulama kimliğini ortadan kısaltıyor
  (`--app-id=hnpfjngllnobngcgfapefoaidbinmjnm` → `Chrome._crx_hnpfjngllnfapefoaidbinmjnm`); anahtar tutmadığı için pencere
  sabitlenmiş simgeye bağlanmıyor, tıklama yeni başlatma sayılıyordu. `AppGrouping.SameCrx/SameKey/Resolve`: kısaltılmış
  kimlik baş+son olarak eşleşir; olmazsa Chromium penceresinin başlığı PWA kısayolunun adıyla birebir aynıysa o simge.
- İlk açılış Genie'si sistem genelinde (EVENT_OBJECT_SHOW + AUMID/exe): dock dışından açılanlar ve 5 sn'den geç pencere
  açan uygulamalar (Grok Bot/Electron) da simgeden açılır; dock'ta olmayan uygulama yeni simgesinden. Kararlar hata.log'a
  ("İlk açılış Genie: bulundu/oynatıldı/zaman aşımı/atlandı ve nedeni").
- Testler: kısaltılmış Chrome kimliği (Kasa'daki gerçek değerler), başlık yedeği, pencere şekli/UAC elemesi.

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
