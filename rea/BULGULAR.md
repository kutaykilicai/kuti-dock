# REA bulguları — lumabar.xyz (16 Kasım 2026, 01:05 TSİ)

Kaynaklar (yalnızca herkese açık): lumabar.xyz ana sayfa, /widgets, SSS; github.com/Acadbek/lumabar
README + sürüm notları (v1.2.17–v1.2.19). Ürün satın alınmadı; ticari MSI indirilmedi.

## REA kayıtları
| Araç | Sonuç |
|---|---|
| capture_web_screenshot (lumabar.xyz) | ev_9c8f3c3a…, artifact sha256 3782d4a0… (1440×813) |
| inspect_web_page (/widgets, erişilebilirlik metni) | ev_7809abe2… — widget kataloğu ve açıklamalar |
| capture_web_screenshot (Kutay Dock HTML maketi) | ev_d252f03e… |
| compare_web_screenshots (eşik 0) | ev_70d4e05f… — değişen piksel %5,98 (tamamı dock şeridinde) |
| compare_web_screenshots (eşik 32) | ev_8ca36a5c… — değişen piksel %3,16 |
| analyze_web_bundle (ana sayfa) | ev_882bcad4… — 18 betik ayrıştırıldı (kesik), site Vite + React; kod kopyalanmadı |
| record_unknown | unk_2a000b16… (Liquid Glass), unk_cde043da… (AI limit kaynakları), unk_884e7f1b… (çatı/otomasyon) |

Not: Kayıtlı REA MCP sunucusunda tarayıcı gözlemi yönetici politikasıyla kapalıydı
(`permission_required`). Aynı REA sürümü (rea-agents 3.1.0) box'ta yalnızca
`https://lumabar.xyz` ve yerel maket kökenine izinli, yerel bir başsız Chrome'a bağlanarak çalıştırıldı.
`build_reconstruction_obligation_ledger` ve `verify_reconstruction` uygulanamadı: ilki imzalı bir Evidence v2 paketi +
yeniden yapım manifesti, ikincisi süreç/protokol yakalama karşılaştırması ister; hedef kapalı kaynaklı bir Windows
uygulaması olduğu ve box'ta çalıştırılamadığı için defter GEREKSINIMLER.md'de elle tutuldu.

## Dock ölçüleri (sitenin canlı dock'u, DOM'dan)
- Dock: 54 px yükseklik, ~16 px köşe, arka plan rgba(20,20,20,.4) + canvas cam efekti,
  iç gölge `inset 0 -10px 24px rgba(0,0,0,.28)`.
- Kart (section): 48 px, köşe ~17.6 px, arka plan siyah %18, kenar beyaz %12 (1.33 px).
- Uygulama düğmesi 42 px (köşe 10), simge 28 px; kartlı uygulama düğmesi 39 px (köşe 13), kart 50 px.
- Yazı tipi yığını: "lumabar UI", "Segoe UI Variable", …

## Özellik listesi (siteden)
- Görev çubuğu yerine dock; Windows görev çubuğunu otomatik gizler ya da tamamen gizler, kapanınca geri getirir.
- Üst/alt kenar, hap ya da tam genişlik; 5–6 stil (Black, White, Liquid Glass, iOS Glass, Midnight), 3 boyut.
- Uygulamalar: görev çubuğuna sabitlenenler + açık pencereler, gruplar, canlı önizleme, okunmamış rozetleri.
- Tam ekran oyun/video varken dock çekilir. Sağ tık > Widgets… ile sürükle-bırak düzen; yapılandırma dosyası yok.
- Akıllı otomasyonlar: toplantıdan önce müziği duraklatma, AI limiti dolmadan uyarı, yenileme öncesi sorma; sessiz saatler.
- Takvim: Google/Outlook/iCloud gizli iCal adresi, 15 dk'da bir, salt okunur.
- Gizlilik: hesap yok, sunucu yok, telemetri yok; yalnızca lisans kontrolü + açılan widget'lar dışarı çıkar.
- Widget kataloğu (13 grup, 100+): AI 11 (Claude/ChatGPT/Gemini/Grok/DeepSeek/GLM/OpenCode/Cursor…),
  Productivity 10 (Day Timeline, Pomodoro, Timer, Stopwatch, Todos, Notes, Calendar, WhatsApp, Currency…),
  Finance 15 (Stripe/Polar/Shopify/Lemon Squeezy/Paddle/Gumroad/Dodo + grafikler), System 23 (Top CPU/RAM, Tray,
  Language, CPU, RAM, Network, 5 pil stili, Quick Toggles, Brightness, Theme, DND, Notifications, Show Desktop,
  Keyboard, Day/Month/Year Progress, Audio Output…), Media & Audio 6, Analytics 4, Developer 3, Online & Mail 6,
  Social 4, Apps & Files 7, Weather 6, Stocks 3, Clocks 11.

## Bilinmeyenler
1. Liquid Glass kırılma efektinin uygulanışı (kapalı kaynak).
2. AI sağlayıcılarının limitlerinin hangi uç noktalardan okunduğu ("kendi oturumundan, salt okunur").
3. Uygulama çatısı (MSI + latest.json + imzalı güncelleyici → Tauri olabilir) ve otomasyon motorunun iç yapısı.
