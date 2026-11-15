# Gereksinim defteri ve kapsam (Kutay Dock 1.0)

Varsayımlar (karpathy): hedef "aynı his ve düzen", ticari ürünün birebir kodu değil. En basit çalışan çözüm:
tek pencereli WPF dock + saniyelik zamanlayıcı + saf mantık katmanı. Ölçütler aşağıda; ✔ = karşılandı,
◐ = kısmen, ✘ = yok, ? = Windows'ta doğrulanmadı.

| # | Gereksinim (kaynak) | Durum | Doğrulama |
|---|---|---|---|
| 1 | Ekranın altında yüzen yuvarlak dock, 54 px, köşe 16 (DOM) | ✔ ? | Kod + HTML maketi aynı ölçü; maket–site karşılaştırması |
| 2 | Kart düzeni 48 px, siyah %18, beyaz %12 kenar (DOM) | ✔ ? | Maket karşılaştırması |
| 3 | Sabitlenen + açık uygulamalar, tıkla aç/öne getir/küçült (SSS) | ✔ ? | Win32 EnumWindows; Windows'ta denenmedi |
| 4 | Hover büyütme (site/video) | ✔ ? | ScaleTransform 1.22 animasyonu |
| 5 | Oturum/Haftalık AI çubukları (ekran görüntüsü) | ◐ | Codex birebir, Claude tahmin; 5 birim testi |
| 6 | Takvim, iCal 15 dk, salt okunur (SSS) | ✔ | 4 birim testi (UTC/TZID/RRULE/EXDATE/COUNT/UNTIL) |
| 7 | Toplantı öncesi müziği duraklat, AI limit uyarısı, sessiz saat (SSS) | ✔ | 4 birim testi |
| 8 | Tam ekranda dock çekilir (README) | ✔ ? | Monitör/ön plan pencere karşılaştırması |
| 9 | Görev çubuğunu otomatik gizle, çıkışta geri getir (SSS) | ✔ ? | Varsayılan KAPALI (birim testi) |
| 10 | Üst/alt kenar, tam genişlik, stiller, boyut (site) | ◐ | 4 stil, sürükle-yapıştır yok |
| 11 | Sistem: CPU/RAM/GPU, ağ, pil, ses, medya, saat, hava, ekran süresi | ✔ ? | CPU/ekran süresi/hava mantığı testli |
| 12 | Ayarlar penceresi + tepsi simgesi (görev) | ✔ ? | Derleme |
| 13 | Hesap yok, telemetri yok (site) | ✔ | Ağ: yalnızca open-meteo + kullanıcı iCal |
| 14 | 100+ widget | ✘ | Bilerek şişirilmedi; 12 çalışan widget |
| 15 | Liquid Glass | ✘ | Yerine Windows bulanıklığı |
| 16 | Widget sürükle-bırak düzenleyici | ✘ | Ayarlar'da sıralama |

Görsel ölçüt: maket ile sitenin aynı 1440×813 karesinde değişen piksel oranı eşik 32'de %3,16
(değişim yalnızca dock şeridinde; farklar: gerçek uygulama ikonları yerine yer tutucular, Türkçe metin,
uygulama grubu kartı yok, cam efekti yok).
