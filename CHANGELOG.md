# Değişiklik günlüğü

## 1.6.19 (2026-11-19)
- Ekran kartı kısa süre sıfırlanınca (Kasa, 13:33, DXGI_ERROR_DEVICE_REMOVED) WPF çizim motoru
  UCEERR_RENDERTHREADFAILURE (0x88980406) ile çöküyor, hata.log'a yazılıp Handled sayılıyordu; dock kesik ve
  mor ışıntılı kalıyordu, yalnızca elle yeniden başlatınca düzeliyordu. Artık bu hata ya da Genie tarafında DXGI
  aygıt kaybı görünce dock kendini bir kez yeniden başlatır (`--autostart`): tek kopya mutex'i bırakılır, görev
  çubuğu bekçisi yeni sürece devredilir, çubuk geri getirilmez. En fazla 2 dakikada bir ve saatte üç kez
  (`cizim-yeniden.json`); hata.log: "Çizim motoru çöktü, dock yeniden başlatılıyor". Genie aygıtı da kayıptan
  sonra yeniden kurulur.
- Tam ekran oyun açıkken (Kasa, ana ekran) dock fareyi ikinci monitöre takip etmiyor, yalnızca ana ekranda açılıyordu.
  Kök neden: `SHQueryUserNotificationState` D3D tam ekran ve sunum sinyali sistem genelidir; `FullscreenRule.Decide`
  bunu monitörden bağımsız "her zaman gizle" sayıyordu. Fare ikinci ekranın dış kenarındayken `ForegroundIsFullscreenOn`
  yine true dönüyor, `DockMonitors.Choose` taşımıyordu; dock ana ekranda `_hiddenForFullscreen` ile gizli kalıyordu.
  Menü çubuğu 1.6.18'den beri monitör başına (log: yalnızca DISPLAY25 için "hidden (full-screen app)"). Artık D3D/sunum/
  QUNS_BUSY de yalnızca ön plan o monitördeyse gizler; oyunun olduğu ekranda dock çıkmaz, diğer ekranın kenarında açılır.
- İlk açılış Genie'si Hızlı İndir'de üst üste iki katman gibi duruyordu. Kök neden (hata.log): süreç 13:10'da
  bir kez açılmıştı, Genie o anda bir kez oynadı; aynı süreç 13:41 ve 13:42'de pencere yeniden gösterilince
  (tepsi ya da gizleme) yine "sistem geneli ilk pencere" sanılıp tekrar oynatıldı. Üstüne WPF penceresinde
  WS_EX_LAYERED + alpha 0 DWM yüzeyini kapatmadığı için asıl pencere görünür kalıyor, Genie kopyası üstünde
  oynuyordu. Artık pencere animasyon boyunca DWMWA_CLOAK ile gizlenir, yalnızca sonda açılır; bir süreç
  açılışında ve aynı uygulamada birkaç saniye içinde en fazla bir kez oynar. Aynı yol diğer uygulamalarda da
  kullanılır: hata.log'da steamwebhelper (üç saniye arayla iki oynatma) ve Lively için de ikinci tetik görünür.
- Testler: HResult tanıma (UCE/DXGI, iç istisna), 2 dk ve saatte 3 sınırı, kayıt yuvarlak turu; aynı süreçte
  ve birkaç saniyelik pencerede ikinci oynatmanın reddi.

## 1.6.18 (2026-11-19)
- Üstteki menü çubuğu tam ekran oyunun (Marvel Rivals) üstünde kalmaz. Kök neden: çubuk tam ekranı yalnızca Explorer'ın
  `ABN_FULLSCREENAPP` bildiriminden öğreniyordu. Kasa'da iki monitör olduğu için bildirim, ön plan penceresi çubuğun
  monitöründe değilse yok sayılıyordu. Oyun açılırken bildirim, oyun penceresi henüz öne ya da tam ekrana geçmeden
  geliyor, yok sayılıyor ve Explorer tekrar göndermiyordu; çubuk topmost olduğu için oyunun üstünde kalıyordu.
  Alt+Enter yeni bir bildirim ürettiği için düzeliyordu. Klasik dock 250 ms'lik kendi yoklamasıyla zaten gizleniyordu.
- Çubuk artık tam ekranı kendisi de izler: ön plan değişince, ön plan penceresi boyut/konum değiştirince (yalnızca o
  sürecin `EVENT_OBJECT_LOCATIONCHANGE` olayı), ekran ayarı değişince ve Explorer bildirimi gelince bakar; bunların her
  birinden sonra 10 sn boyunca 500 ms'de bir kısa kontrol yapar, sonra durur. Sürekli yoklama yok.
- Karar monitör başına: ön plan penceresi çubuğun monitörünü tamamen kaplıyorsa (görünür, gizlenmemiş/cloaked değil,
  başlık çubuklu büyütülmüş pencere değil, masaüstü değil) yalnızca o monitörün çubuğu gizlenir ve topmost'tan çekilir;
  diğer monitördeki çubuk görünür kalır. Tek monitörde Explorer bildirimine eskisi gibi güvenilir. Gizlenme/görünme
  `log.txt`'ye yazılır ("Menu bar \\.\DISPLAY1: hidden (full-screen app)").
- Testler: kaçırılan bildirimde geometriyle gizleme, monitör başına karar, eski bildirimin diğer monitörü gizlememesi,
  tek monitör ve masaüstü, başlıklı büyütülmüş pencere, kısa yoklama süresi.

## 1.6.17 (2026-11-18)
- Lively duvar kağıtları kaybolunca kendiliğinden geri gelir. Kök neden (Kasa, Lively günlüğü): Kuti Dock Windows
  görev çubuğunun yerini aldığı için `Shell_TrayWnd` penceresinin sahibi dock'tur. Dock her yeniden başladığında Lively
  "Explorer çöktü (pid uyuşmazlığı)" sanıp duvar kağıtlarını yeniden kurar; 30 sn içinde ikinci kez olunca ("Explorer
  restarted multiple times in the last 30s") vazgeçer, oynatıcıları kapatır ve `WallpaperLayout.json`'u boşaltır.
  18 Kasım 14:01'de 28 sn arayla iki dock yeniden başlatması bunu tetikledi; inFamous 2 değildi.
- Yeni duvar kağıdı bekçisi: dock açılışından 40 sn sonra, her ekran ayarı değişikliğinden (çözünürlük, monitör,
  oyunun mod değiştirmesi) 5 sn sonra ve 2 dakikada bir hafif bir kontrol yapar: Lively çalışıyor mu, düzen boş mu,
  düzendeki kadar Lively oynatıcısı (mpv) var mı. Eksikse `LivelyGeriYukle.ps1 -Mod Kontrol` pencere açmadan çalışır
  (en sık 2 dakikada bir). Ön planda tam ekran oyun/uygulama varsa hiçbir şey yapılmaz; Lively kapalıysa dokunulmaz.
  Kararlar hata.log'a ("Duvar kağıdı bekçisi: ...").
- Testler: düzen sayısı okuma, eksiklik kararı, tam ekranda/Lively kapalıyken/betik yokken çalışmama, en sık 2 dakikada bir, gecikmeler.

## 1.6.16 (2026-11-18)
- Limit panelindeki Gemini satırı artık gemini.google.com/usage sayfasındaki iki kartı gösterir: **Güncel** (yüzde,
  çubuk ve sıfırlanma saati) ve **Haftalık** (yüzde, çubuk ve sıfırlanma günü/saati), altında "güncellendi SS:dd ·
  sayfa/arka plan". Veri 30 dakikadan eskiyse turuncu "son güncelleme", hiç veri yoksa "Veri yok" yazar; uydurma
  değer gösterilmez. Saatler sayfada zaten tarayıcının yerel saatiyle (Kasa: Europe/Istanbul) yazıldığı için çevrilmez.
- Kaynak: Kutay'ın Chrome'una bir kez yüklenen özgün MV3 eklentisi (`extensions/gemini-usage`). Eklentinin izinleri
  yalnızca `https://gemini.google.com/usage*` ve `http://127.0.0.1:47615/*`. Sayfa açıkken içerik betiği iki kartın
  metnini okur (e-posta/ad gibi sayfanın geri kalanı gönderilmez) ve yalnızca dock'a iletir. Çerez, oturum belirteci ya
  da Chrome profili okunmaz; dock Google'a hiç bağlanmaz.
- Arka plan: eklenti 10 dakikada bir sayfayı Chrome'un normal isteğiyle ister ve görünür metinde kartları arar.
  Sayfa kartları JavaScript ile çiziyorsa bu bulunamaz (Gemini `X-Frame-Options: DENY` gönderdiği için gizli iframe
  de mümkün değil); o durumda değerler Kutay sayfayı açtığında güncellenir.
- Gemini satırına tıklamak: eklenti arka planda okuyabiliyorsa yenileme ister (eklenti 1 dk içinde alır), okuyamıyorsa
  Gemini kullanım sayfasını açar. "Sayfayı aç" bağlantısı da duruyor.
- Dock tarafı: yalnızca 127.0.0.1:47615'i dinleyen küçük yerel uç nokta (`POST /gemini`, `GET /gemini/istek`). Her istek
  dock'un ürettiği 64 karakterlik gizli değeri (`X-KutiDock-Gizli`) taşımalı; web sayfası kaynaklı istekler reddedilir.
  Gizli değer `%APPDATA%\KutiDock\gemini-gizli.txt`'de durur, dock onu eklenti klasörüne `dock-gizli.json` olarak yazar
  (repoda yok). Değerler `gemini-kullanim.json`'da saklanır. hata.log'a yalnızca "Gemini: güncellendi (sayfa)" gibi satırlar.
- Testler: ekran görüntüsündeki kartlar, sıfır olmayan değerler ve ertesi gün sıfırlanma, Türkçe arayüz, kart yoksa
  veri yok, satır biçimi, gizli değer karşılaştırma, eskime ve saklama.

## 1.6.15 (2026-11-18)
- Limit kutucuğu kapalıyken yine yalnızca Grok Bot haftalık limitini gösterir. Tıklayınca açılan panelde Cursor ve
  Grok Bot satırının altına Apple tarzı gruplu bir "Diğer yapay zekâ" listesi eklendi: AllModels, OpenRouter,
  Kimi (Moonshot) ve Gemini. Her satırda bakiye, son güncelleme saati ve sağlayıcının kendi sayfasını açan
  "Sayfayı aç" bağlantısı var.
- Veriler yalnızca resmi API'lerden ve Kutay'ın API anahtarlarıyla okunur; tarayıcı çerezi, oturum belirteci ya da
  tarayıcı veritabanı okunmaz, sayfa kazınmaz.
  - Kimi: `GET https://api.moonshot.ai/v1/users/me/balance` → `available_balance` (yalnızca bakiye sorgusu, kota harcamaz).
  - AllModels: `GET https://api.allmodels.io/account/balance` → `paid_balance_usd`.
  - OpenRouter: `GET /api/v1/credits` (total_credits − total_usage, sayfadaki "Total available"; yönetim anahtarı
    ister). Normal anahtarla 403 dönerse `/api/v1/key`'den yalnızca o anahtarın kalan limiti gösterilir ve bu belirtilir.
  - Gemini (Pro uygulama limiti): resmi API yok. Satırda yalnızca plan ve "5 saatte bir yenilenir, haftalık limit var"
    bilgisi ile sayfa bağlantısı var; uydurma yüzde gösterilmez.
- Anahtarlar `%APPDATA%\KutiDock\api-anahtarlari.dat`'ta Windows DPAPI (CurrentUser) ile şifreli saklanır; ekrana,
  log'a ve repoya yazılmaz. Ayarlar > Limitler'e anahtar alanları eklendi (boş = değişmez, "-" = sil). Aynı klasöre
  düz metin `api-anahtarlari.yeni.json` konursa dock açılışta onu şifreli dosyaya katar ve düz metni siler.
- Yenileme: panel açılınca (en çok 30 sn'de bir) ve 10 dakikada bir. Hata olursa satırda sessizce "güncellenemedi"
  yazar, son bilinen değer kalır. hata.log'a yalnızca "AI bakiye: <sağlayıcı>: güncellendi / güncellenemedi (HTTP n)".
- Testler: Kimi/AllModels/OpenRouter yanıt ayrıştırma, sıra ve bağlantılar, anahtar dosyası süzme ve birleştirme, yenileme aralıkları.

## 1.6.14 (2026-11-18)
- "Aramada Everything kullan" anahtarı artık Everything'i kendisi yönetir; Everything'i elle açmak gerekmez.
  Önceki sorunlar: Everything yalnızca arama paneli açılınca başlatılıyordu; veritabanı yüklenmeden yapılan
  aramalar boş dönüyor ve yeniden denenmiyordu; ayarlardaki anahtar Everything'i başlatıp kapatmıyordu; dock'un
  başlattığı Everything yalnızca bellekte tutulduğu için dock yeniden başlayınca anahtar kapatılsa da açık kalıyordu.
- Anahtar açılınca Everything arka planda, pencere açmadan (`-startup`) başlar. Anahtar açıkken dock her açılışta
  (8 sn sonra) Everything'i yine başlatır; kapalıyken başlatmaz. Arama sırası değişmedi: uygulamalar, ayarlar,
  Everything'den dosyalar.
- Everything çalışmıyorsa arama onu başlatıp IPC penceresini (en çok 8 sn) ve veritabanını (en çok 20 sn) bekler.
  Veritabanı hazır değilse "Everything veritabanı yükleniyor…" yazar ve aramayı 0,4 / 0,8 / 1,5 / 3 / 5 sn sonra
  kendiliğinden yeniler.
- Anahtar kapatılınca yalnızca dock'un başlattığı Everything kapatılır (`Everything.exe -exit`). Kimin başlattığı
  `%APPDATA%\KutiDock\everything-dock.txt` dosyasında (süreç no + başlama zamanı) tutulur; dock yeniden başlasa
  da geçerlidir. Elle açılan Everything ya da ana penceresi açık olan Everything kapatılmaz. Everything hizmeti
  (servis) hiç ellenmez.
- Tanı modu: `KutiDock.exe --everything-tani ac|kapat` (pencere açmaz, sonucu `%TEMP%\kutidock-everything-tani.txt`'e
  yazar). İşlemler hata.log'a ("Everything: ...").
- Testler: dock açılışında başlatma kararı, sahiplik dosyası, yalnızca dock'un başlattığı süreci kapatma, yeniden deneme kararı.

## 1.6.13 (2026-11-18)
- Çalışan bir uygulamanın simgesine sağ tıklayınca menünün en altında kırmızı **Görevi sonlandır**. Onay sormaz;
  donmuş uygulamada da çalışır, Görev Yöneticisi'ni açmak gerekmez. Simgenin pencerelerinin sahibi süreçler ve aynı
  exe'den çalışan tüm süreçler alt süreç ağacıyla zorla kapatılır (`Process.Kill(entireProcessTree: true)`). Yönetici
  olarak çalışan bir süreç kapatılamazsa `taskkill /F /T` yönetici izniyle, pencere açmadan denenir.
- Paketli (Mağaza) uygulamalarda paylaşılan `ApplicationFrameHost` değil, penceresinin içindeki asıl süreç kapatılır.
- Chrome/Edge PWA'larında (ör. WhatsApp Web) Chrome'un tamamı kapanmasın diye yalnızca pencereye kapat komutu
  (`WM_CLOSE`) gider. 3 sn içinde kapanmazsa penceresinin süreci yalnızca bu PWA'nın pencerelerini taşıyorsa
  sonlandırılır; tarayıcının başka pencerelerini de taşıyorsa sonlandırılmaz ve bu hata.log'a yazılır.
- Öğe yalnızca uygulama çalışırken görünür. Explorer, Kuti Dock'un kendisi, Windows klasöründeki ve sistem süreçleri
  (csrss, winlogon, svchost...), oyunlar, Steam ve hile önleyiciler (Marvel Rivals, EasyAntiCheat...) için gösterilmez.
  Her işlem hata.log'a ("Görevi sonlandır ...").
- Testler: korumalı ve korumasız süreçler (Kasa'daki gerçek yollar), yalnızca çalışırken gösterim, aynı exe'nin tüm
  süreçleri, Kuti Dock'un kendini hiç hedeflememesi, paketli uygulamada yalnızca pencere sahipleri, PWA kararı, taskkill argümanları.

## 1.6.12 (2026-11-18)
- TV Aç-Kapat simgesi artık TV'nin durumunu gösterir. Dock, sabitlenmiş kısayolun hedefi `TvEkranGecis.exe` olunca
  simgeyi TV düğmesi sayar. TV açıkken simge normal renkte ve sağ üstünde yeşil bir nokta var; kapalıyken simge gri ve
  soluk, altında küçük gri "kapalı" rozeti. Üzerine gelince "TV: Açık" ya da "TV: Kapalı" yazar.
- Durum CCD API'siyle okunur (`QueryDisplayConfig`, tüm yollar): TV'nin etkin bir yolu varsa açık, yalnızca etkin olmayan
  yolu varsa kapalı. TV, TvEkranGecis'in `ayarlar.json`'undaki EDID anahtarı (`GSM0001`) ve adıyla (`LG TV`) tanınır;
  dosya yoksa adında "TV" geçen tek ekran (birden çoksa HDMI olan) alınır. Windows ekran ayarı değişince
  (`DisplaySettingsChanged`) hemen, ayrıca en geç 3 sn'de bir ve tıklamadan 0,7 / 1,8 / 4 sn sonra yeniden okunur.
  Durum değişimleri hata.log'a ("TV durumu: ...").
- TV simgesine tıklamak sıçrama ve Genie başlatmaz (araç pencere açmaz).
- Testler: kısayol hedefinin tanınması, ayarlar.json ipucu, Kasa'daki gerçek üç ekranla açık/kapalı/bilinmiyor, iki TV'de HDMI tercihi, Türkçe metinler.

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
