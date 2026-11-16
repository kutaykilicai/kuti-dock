# Kuti Dock

Windows 11 için macOS tarzı dock + canlı widget'lar. C# / .NET 8 / WPF. Sürüm 1.6. (Eski adı: Kutay Dock.)

Widget'lar: Başlat, Uygulamalar (Windows 11 tarzı canlı pencere önizlemesi, UWP simgeleri), Limitler (Grok Bot;
tıklayınca Cursor), Ses, Sistem, Ağ, Depolama, Ekran görüntüsü (ShareX), Pil, Gizli simgeler, Saat/hava.
İsteğe bağlı: Windows görev çubuğunu (hangi kenarda olursa olsun, çoklu monitör) otomatik gizleme.

### 1.6 — Kuti Dock
- **Mac dock (Gravitone motoru, MIT):** dalga büyütme (imlecin altındaki simge yerinde kalır), etiketler, çalışan
  uygulama noktaları, açılışta ve dikkat isteyince sıçrama, GPU'da **Genie** küçültme/geri açma (Direct3D 11 +
  DirectComposition), Windows 11 akrilik plaka (açık/koyu tema, uyku/kilit sonrası yeniden uygulanır).
- **Üst menü çubuğu:** aktif uygulama adı, logoda Başlat, tepsi simgeleri (tık, çift tık, menüler uygulamalara gider),
  sinyal gücüyle Wi-Fi, ses, pil, tarih ve saat; her monitörde bir çubuk. Mac dock ayarlarından kapatılabilir.
- **Elle düzenleme:** sürükleyerek sıralama, dışarı sürükleyince duman efektiyle kaldırma, program/kısayol/klasör ve
  Bu Bilgisayar/Denetim Masası bırakıp sabitleme, klasör yığınları, gerçek Geri Dönüşüm Kutusu.
- **Çoklu monitör/DPI:** imleç başka ekranın alt kenarına itilince dock oraya geçer; boyutlar monitör ölçeğine göre;
  uyku, kilit, kullanıcı değişimi ve monitör değişimleri ele alınır.
- **Güvenli tasarım:** görev çubuğu her durumda geri gelir (çıkış, çökme, süreç ağacını öldürme, oturum kapatma,
  elektrik kesintisi sonrası RunOnce, Explorer yeniden başlaması). Ctrl+Alt+Shift+B görev çubuğunu geçici gösterir;
  `KutiDock.exe --restore-taskbar` acil durum anahtarıdır.
- **Birleşim:** Mac dock açıkken uygulamalar Mac dock'ta, Kuti widget'ları alt şeridin iki köşesinde (Uygulamalar'dan
  öncekiler solda, sonrakiler sağda); menü çubuğu açıkken "Gizli simgeler" widget'ı çıkar (tepsi menü çubuğunda).
  Çalışan uygulamanın üzerinde beklenince Kuti'nin canlı pencere önizlemesi açılır. Ayarlar > Mac dock'tan tamamen
  kapatılırsa 1.5'teki klasik dock geri gelir. Diğer tüm özellikler (Grok/Cursor kullanımı, Everything/Windows
  araması, diskler, optimizasyon, ses aygıtı, WARP, tarih, sol/sağ konum, videoda gizlenme…) aynen sürer.
- Ad değişti: exe `KutiDock.exe`, veriler `%APPDATA%\KutiDock`. İlk açılışta `%APPDATA%\KutayDock` içindeki ayarlar
  ve Cursor oturumu (şifreli dosya olduğu gibi) kopyalanır; eskiler silinmez.

### 1.5
- macOS büyütmesi, açılış sıçraması, dock ve panellerin açılma/kapanma animasyonları; Windows görev çubuğunu tamamen
  kaldırma (dock kapanınca/çökünce geri gelir, Explorer yeniden başlatılmaz). Başlat düğmesi yalnızca Başlat menüsünü açar.
- Diskler panelinde klasör boyutları (Everything, TreeSize benzeri, içine gir/geri), Windows optimizasyonu paneli
  (RAM, geçici dosyalar MB önizleme + onay, DNS; oyun açıkken pasif), ses çıkış aygıtı listesi, Cloudflare WARP
  bağlan/kes, saatin altında tam tarih, varsayılan arama Windows araması gibi (uygulamalar, ayarlar, dosyalar);
  Everything isteğe bağlı.

### Gravitone'dan alınanlar (MIT)
[Gravitone](https://github.com/lupanostefano/Gravitone), Copyright (c) 2026 Stefano Lupano, MIT lisanslı. Büyütme
(kosinüs tümseği, tau değerleri), sıçrama (parabol ve süreler) ve görev çubuğunu kaldırma yöntemi (otomatik gizleme +
gizleme + olay kancası + bekçi süreci) uyarlandı. Logo, ad, simgeler ve Genie efekti (Direct3D) alınmadı. Lisans metni
ve ayrıntılar: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

### Limit verisinin kaynağı
- **İsteğe bağlı Cursor girişi (1.3):** Ayarlar'daki düğmeyle Kuti Dock kendi Cursor oturumunu açar (tarayıcıda onay,
  belirteç DPAPI ile şifreli). Grok Bot yüzdesi/sıfırlanması ve Cursor kullanım özeti Cursor'un **belgelenmemiş** dahili
  uç noktalarından okunur; Cursor değiştirirse çalışmayabilir, belirteç tüm hesaba erişir. Giriş yoksa aşağıdaki elle yol.
- **Grok Bot:** belgelenmiş kullanım API'si yok → değerler Ayarlar'dan elle girilir ("kaynak yok · elle gir").
- **Cursor:** plan adı Cursor CLI'nin belgelenmiş `agent about --format json` komutundan (yalnızca
  `subscriptionTier`); yüzde elle girilir, panelden cursor.com kullanım sayfası açılır. Cursor uygulamasının
  token/çerez/yerel veritabanı okunmaz, belgelenmemiş API kullanılmaz.

lumabar.xyz'deki dock'un **herkese açık** sayfası, dokümanları, değişiklik günlüğü ve
tanıtım görselleri REA ile incelenerek davranışı ve düzeni **özgün kodla** yeniden yazıldı.
lumabar'ın kodu kapalıdır; kodu, logosu, ikonları, görselleri, adı ve lisans kontrolü
kullanılmadı. Ürün satın alınmadı, ticari MSI indirilmedi/incelenmedi.

`github.com/Acadbek/lumabar` deposu lumabar'ın resmî sürüm (release) deposudur; içinde
yalnızca README ve tanıtım görselleri var, **lisans dosyası yok** ve README "kaynak kodu
herkese açık değil, ticari yazılım" diyor. Bu yüzden oradan **hiçbir dosya alınmadı**;
yalnızca README metni ve değişiklik notları özellik listesi için okundu.

## Yapı

```
src/KutiDock.Mac    Mac dock motoru: Gravitone kaynak kodu (MIT, Stefano Lupano)
src/KutiDock.Core   platformdan bağımsız mantık (limitler, ShareX, uyarılar, ayar taşıma, görev çubuğu geometrisi, hava)
src/KutiDock        WPF uygulaması (dock penceresi, widget'lar, ayarlar, tepsi)
tests/KutiDock.Tests xUnit testleri (Core)
mock/                dock düzeninin HTML maketi (görsel karşılaştırma için)
rea/BULGULAR.md      REA analizi ve özellik listesi
GEREKSINIMLER.md     gereksinim (yükümlülük) defteri ve kapsam
```

## Derleme

```
dotnet test tests/KutiDock.Tests
dotnet publish src/KutiDock -c Release -o publish   # tek dosya, win-x64, self-contained
```

Linux'ta derlemek için `EnableWindowsTargeting` açık. Kullanım için `OKUBENI.txt`.

Lisans: MIT © 2026 Kutay Kılıç
