# Üçüncü taraf bildirimleri

## Gravitone

- Kaynak: https://github.com/lupanostefano/Gravitone
- Lisans: MIT, Copyright (c) 2026 Stefano Lupano
- Uyarlanan kısımlar (kod yeniden yazıldı, yöntem ve parametreler alındı):
  - Büyütme: kosinüs tümseği, etki mesafesi = simge boyu × 3,2, üstel yaklaşma
    (büyürken tau 0,045 sn, sönerken 0,075 sn, gizle/göster 0,065 sn). Kaynak: `Core/DockLayout.cs`, `UI/DockWindow.cs`.
    Kutay Dock'taki yeri: `src/KutayDock.Core/Motion.cs` (Magnify), `src/KutayDock/Tiles/AppsTile.cs`.
  - Sıçrama: parabol 4p(1−p); açılışta periyot 0,62 sn, yükseklik 0,5, 12 sn sınır; dikkat sıçraması 3 × 0,5 sn,
    yükseklik 0,65, ara 1,1 sn. Kaynak: `Core/Bouncer.cs`. Kutay Dock'taki yeri: `src/KutayDock.Core/Motion.cs` (Bouncer).
  - Görev çubuğunu kaldırma: otomatik gizleme + tüm görev çubuğu pencerelerini gizleme, EVENT_OBJECT_SHOW kancası ve
    1 sn'lik denetim, işaret dosyasında asıl durum, StuckRects3 kalıcılığı, çökmeye karşı ayrı bekçi süreci.
    Kaynak: `Core/TaskbarReplacer.cs`, `Core/TaskbarState.cs`, `Core/Guard.cs`. Kutay Dock'taki yeri: `src/KutayDock/TaskbarRemover.cs`.
- Alınmayanlar: Gravitone adı, logosu, simgeleri ve diğer marka varlıkları; Genie efekti (Direct3D/Vortice gerektirir);
  ManagedShell ve diğer bağımlılıklar.

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
