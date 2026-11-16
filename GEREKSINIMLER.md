# Gereksinim defteri ve kapsam (Kutay Dock 1.1)

Varsayımlar (karpathy): hedef "aynı his ve düzen", ticari ürünün birebir kodu değil. En basit çalışan çözüm:
tek pencereli WPF dock + saniyelik zamanlayıcı + saf mantık katmanı. Ölçütler aşağıda; ✔ = karşılandı,
◐ = kısmen, ✘ = yok, ? = Windows'ta doğrulanmadı.

| # | Gereksinim (kaynak) | Durum | Doğrulama |
|---|---|---|---|
| 1 | Ekranın altında yüzen yuvarlak dock, 54 px, köşe 16 (DOM) | ✔ ? | Kod + HTML maketi aynı ölçü; maket–site karşılaştırması |
| 2 | Kart düzeni 48 px, siyah %18, beyaz %12 kenar (DOM) | ✔ ? | Maket karşılaştırması |
| 3 | Sabitlenen + açık uygulamalar, tıkla aç/öne getir/küçült; çoklu pencerede seçim listesi; UWP simgeleri (SSS + Kutay 1.1) | ✔ | Win32 EnumWindows; AUMID test PC'sinde doğrulandı (Ayarlar, Not Defteri) |
| 4 | Hover büyütme (site/video) | ✔ ? | ScaleTransform 1.22 animasyonu |
| 5 | Grok Bot limiti, tıklayınca Cursor limitleri (Kutay 1.1) | ◐ | Belgelenmiş kaynak yok → elle; Cursor planı `agent about`; 3 birim testi |
| 6 | Takvim, medya, ekran süresi, Claude/Codex kaldırıldı (Kutay 1.1) | ✔ | Ayar taşıma testi (2) |
| 7 | Limit eşiği uyarısı, pil, sessiz saat (SSS) | ✔ | 3 birim testi |
| 8 | Tam ekranda dock çekilir (README) | ✔ ? | Monitör/ön plan pencere karşılaştırması |
| 9 | Görev çubuğunu otomatik gizle (her kenar, çoklu monitör), çıkışta geri getir (SSS + Kutay 1.1) | ✔ ? | ABM_SETSTATE test PC'sinde (25H2) etkisiz → gizle/kenarda göster yedeği; kenar geometrisi 3 birim testi |
| 10 | Üst/alt kenar, tam genişlik, stiller, boyut (site) | ◐ | 4 stil, sürükle-yapıştır yok |
| 11 | Sistem: CPU/RAM/GPU, ağ, pil, ses, saat, hava, depolama, ekran görüntüsü (ShareX) | ✔ ? | CPU/hava/bayt biçimi/ShareX argümanları testli |
| 12 | Ayarlar penceresi + tepsi simgesi (görev) | ✔ ? | Derleme |
| 13 | Hesap yok, telemetri yok (site) | ✔ | Ağ: yalnızca open-meteo |
| 14 | 100+ widget | ✘ | Bilerek şişirilmedi; 11 çalışan widget |
| 15 | Liquid Glass | ✘ | Yerine Windows bulanıklığı |
| 16 | Widget sürükle-bırak düzenleyici | ✘ | Ayarlar'da sıralama |

Görsel ölçüt: maket ile sitenin aynı 1440×813 karesinde değişen piksel oranı eşik 32'de %3,16
(değişim yalnızca dock şeridinde; farklar: gerçek uygulama ikonları yerine yer tutucular, Türkçe metin,
uygulama grubu kartı yok, cam efekti yok).
