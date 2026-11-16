# Üçüncü taraf bildirimleri

## Gravitone — Mac dock motoru

- Kaynak: https://github.com/lupanostefano/Gravitone (commit 93e9d18)
- Lisans: MIT, Copyright (c) 2026 Stefano Lupano (tam metin aşağıda ve `src/KutiDock.Mac/LICENSE-Gravitone.txt`)
- 1.6'dan itibaren Gravitone'un kaynak kodu (`Core/`, `Interop/`, `UI/`) `src/KutiDock.Mac/` altında **doğrudan** kullanılır.
  Her dosyanın başında kaynak ve lisans notu vardır. Gravitone'dan gelenler:
  dalga büyütme (imlecin altındaki simge yerinde kalır), etiketler, çalışan uygulama noktaları, açılış ve dikkat sıçraması,
  GPU'da Genie küçültme/geri açma (Direct3D 11 + DirectComposition), Windows 11 akrilik plaka (DWM sistem arka planı),
  üst menü çubuğu (aktif uygulama, Başlat, tepsi simgeleri, Wi-Fi, ses, pil, tarih/saat; her monitörde),
  sürükleyerek sıralama, dışarı sürükleyince duman, bırakarak sabitleme, klasör yığınları, Geri Dönüşüm Kutusu,
  çoklu monitör/DPI ve imleci izleme, uyku/kilit/oturum değişimi, görev çubuğunu değiştirme ve çökme bekçisi.
- Kuti Dock değişiklikleri: ad ve veri klasörü (%APPDATA%\KutiDock), İtalyanca yerine Türkçe arayüz, .NET 8,
  ilk açılışta Windows görev çubuğu sabitlemelerinin alınması, "Windows ile başlat" varsayılanı kapalı,
  Kuti'nin canlı pencere önizlemesi ve ayar menüsü bağlantıları.
- Alınmayanlar: Gravitone adı, logosu, simgesi, görselleri ve kurulum programı.
- Ayrıca 1.5'te `src/KutiDock.Core/Motion.cs` ve `src/KutiDock/TaskbarRemover.cs` Gravitone'dan uyarlanmıştı (klasik kip).

```
MIT License

Copyright (c) 2026 Stefano Lupano

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Gravitone'un bağımlılıkları (Kuti Dock exe'sinin içinde)

| Bileşen | Kullanım | Lisans |
| --- | --- | --- |
| [ManagedShell](https://github.com/cairoshell/ManagedShell) 0.0.374 | Menü çubuğundaki tepsi simgeleri | Apache-2.0 |
| [Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows) 3.8.3 (Direct3D11, DirectComposition, D3DCompiler, DXGI) | GPU'da Genie efekti | MIT |
| [SharpGen.Runtime](https://github.com/SharpGenTools/SharpGenTools) | Vortice'in COM bağlantısı | MIT |

macOS ve Dock, Apple Inc.'in ticari markalarıdır; Windows, Microsoft'un ticari markasıdır. Kuti Dock bu şirketlerle bağlantılı değildir.
