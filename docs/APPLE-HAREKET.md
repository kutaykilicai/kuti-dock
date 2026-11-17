# Apple tarzı hareket: notlar (1.6.5)

Kaynak: Kutay'ın gönderdiği üç video. "Oddly satisfying Mac Dock part 2" kısa videosu 30 fps'te kare kare,
"Apple-style motion graphics" (After Effects, ~20 dk) ve "Apple style visuals" videoları altyazılarıyla incelendi.
Videolardan kod, görsel ya da ses alınmadı; yalnızca ilkeler çıkarıldı ve değerler Kuti Dock için yeniden seçildi.

## Çıkarılan ilkeler
- **Akıcılık:** hareket 60 fps'te oynar; "tereyağı gibi" görünümün ilk şartı kare atlamamak.
- **Kısa ve abartısız:** "subtle, smooth, quick; nothing flashy". Açılışlar yaklaşık 0,3 sn, kapanışlar daha kısa.
- **Yay ve hafif sekme:** anahtar karelerin sonuna "bounce/inertia" ifadesi eklenir; öğe hedefini çok az geçip yerine
  oturur. Kapanışta sekme yok.
- **Kaynağından büyüme:** kısa videoda panel, dock'taki öğesinden küçük, bulanık ve saydam başlayıp ~10 karede
  (≈0,33 sn) büyüyor ve netleşiyor; dönüşüm noktası dock'a bakan kenar.
- **Saydamlık ölçekten önce dolar:** öğe görünür hale geldiğinde hâlâ büyümektedir.
- **Kademeli gecikme:** birlikte hareket eden parçalar ~0,1 sn arayla başlar ("time minus 0.1").
- **Dönüşüm noktası ortada / kaynakta:** ölçek yanlış köşeden büyümez.
- **Yuvarlak köşeler, cam (blur) ve çok hafif gölge:** hareketin dışındaki görsel dil; Kuti Dock'ta zaten var.

## Kuti Dock'ta karşılıkları
| Yer | Önce | 1.6.5 |
|---|---|---|
| Hover büyütme | üstel yaklaşma (tau 0,045/0,075) | yay: büyürken tepki 0,24 sn sönüm 0,86, sönerken 0,30/1,0; hız korunur |
| Paneller (kullanım, ses, disk, sistem) | 190 ms ölçek 0,92→1 + saydamlık | yay 0,34/0,80; ölçek 0,86→1, saydamlık önde, bulanıklık 6→0; kapanış yay 0,20/1,0 |
| Sağ tık menüleri | Windows'un düz solması | aynı yay, ölçek 0,94→1, bulanıklık 3→0; dışarı tıklama/Esc ile kapanışta geri küçülme |
| Otomatik gizleme | 220/150 ms kübik | yay: belirme 0,32/0,88, çekilme 0,24/1,0; yarıda tersine dönebilir |
| Başlat zıplaması | 150 ms git-gel | yay dürtüsü (0,30/0,50): sıçrar, bir kez sallanıp oturur |
| Genie | doğrusal zaman | küçültme yumuşak başlar ve yumuşak girer; geri açılma hızlı başlar, taşmadan oturur |
| Uygulama zıplaması | yerçekimi parabolü | değişmedi (macOS'taki gibi fiziksel zıplama zaten doğru) |
| Üst çubuk | tepsi menüleri uygulamaların kendi menüleri | değişmedi (Windows'un/uygulamanın menüsü, Kuti Dock çizmiyor) |

Bütün yaylar yalnızca hareket sürerken ekran yenilemesine bağlanır; bulanıklık efekti hareket bitince kaldırılır.
Değerlerin testleri: `tests/KutiDock.Tests/CoreTests.cs` (`V165AppleMotionTests`).
