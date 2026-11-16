# Kutay Dock

Windows 10/11 için ekranın altında yüzen, canlı widget'lı akıllı dock. C# / .NET 8 / WPF. Sürüm 1.2.

Widget'lar: Başlat, Uygulamalar (Windows 11 tarzı canlı pencere önizlemesi, UWP simgeleri), Limitler (Grok Bot;
tıklayınca Cursor), Ses, Sistem, Ağ, Depolama, Ekran görüntüsü (ShareX), Pil, Gizli simgeler, Saat/hava.
İsteğe bağlı: Windows görev çubuğunu (hangi kenarda olursa olsun, çoklu monitör) otomatik gizleme.

### Limit verisinin kaynağı
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
src/KutayDock.Core   platformdan bağımsız mantık (limitler, ShareX, uyarılar, ayar taşıma, görev çubuğu geometrisi, hava)
src/KutayDock        WPF uygulaması (dock penceresi, widget'lar, ayarlar, tepsi)
tests/KutayDock.Tests xUnit testleri (Core)
mock/                dock düzeninin HTML maketi (görsel karşılaştırma için)
rea/BULGULAR.md      REA analizi ve özellik listesi
GEREKSINIMLER.md     gereksinim (yükümlülük) defteri ve kapsam
```

## Derleme

```
dotnet test tests/KutayDock.Tests
dotnet publish src/KutayDock -c Release -o publish   # tek dosya, win-x64, self-contained
```

Linux'ta derlemek için `EnableWindowsTargeting` açık. Kullanım için `OKUBENI.txt`.

Lisans: MIT © 2026 Kutay Kılıç
