namespace KutayDock.Core;

/// <summary>Piksel dikdörtgeni (Win32 RECT gibi: sağ/alt hariç).</summary>
public readonly record struct Box(int Left, int Top, int Right, int Bottom)
{
    public bool Contains(int x, int y, int pad = 0) => x >= Left - pad && x < Right + pad && y >= Top - pad && y < Bottom + pad;
}

public static class TaskbarGeometry
{
    /// <summary>
    /// Görev çubuğunun yapıştığı ekran kenarı boyunca 2 piksellik tetik şeridi (çubuğun kendi monitöründe).
    /// Çubuk üstte, altta, solda ya da sağda olabilir; fare bu şeride gelince çubuk gösterilir.
    /// </summary>
    public static Box TriggerStrip(Box bar, Box mon)
    {
        bool horizontal = bar.Right - bar.Left >= bar.Bottom - bar.Top;
        if (horizontal)
        {
            bool top = Math.Abs(bar.Top - mon.Top) <= Math.Abs(mon.Bottom - bar.Bottom);
            return top ? new Box(bar.Left, mon.Top, bar.Right, mon.Top + 2) : new Box(bar.Left, mon.Bottom - 2, bar.Right, mon.Bottom);
        }
        bool left = Math.Abs(bar.Left - mon.Left) <= Math.Abs(mon.Right - bar.Right);
        return left ? new Box(mon.Left, bar.Top, mon.Left + 2, bar.Bottom) : new Box(mon.Right - 2, bar.Top, mon.Right, bar.Bottom);
    }
}

public static class WorkArea
{
    /// <summary>
    /// <paramref name="area"/>'dan, monitörün bir kenarına yapışık <paramref name="bar"/> şeridini çıkarır
    /// (görev çubuğu ya da dock'un ayırdığı alan). Kesişmiyorsa alan değişmez.
    /// </summary>
    public static Box Subtract(Box area, Box bar, Box mon)
    {
        if (bar.Right <= area.Left || bar.Left >= area.Right || bar.Bottom <= area.Top || bar.Top >= area.Bottom) return area;
        bool horizontal = bar.Right - bar.Left >= bar.Bottom - bar.Top;
        if (horizontal)
            return Math.Abs(bar.Top - mon.Top) <= Math.Abs(mon.Bottom - bar.Bottom)
                ? area with { Top = Math.Max(area.Top, bar.Bottom) }
                : area with { Bottom = Math.Min(area.Bottom, bar.Top) };
        return Math.Abs(bar.Left - mon.Left) <= Math.Abs(mon.Right - bar.Right)
            ? area with { Left = Math.Max(area.Left, bar.Right) }
            : area with { Right = Math.Min(area.Right, bar.Left) };
    }

    /// <summary>Görev çubuğu gizliyken olması gereken çalışma alanı: monitörün tamamı, varsa yalnızca dock'un ayırdığı alan çıkar.</summary>
    public static Box WhileTaskbarHidden(Box mon, Box? dockReserve) =>
        dockReserve is { } d ? Subtract(mon, d, mon) : mon;
}

public static class Thumb
{
    /// <summary>Kaynak en-boy oranını koruyarak kutuya sığdır ve ortala (x, y, genişlik, yükseklik).</summary>
    public static (double X, double Y, double W, double H) Fit(double boxX, double boxY, double boxW, double boxH, double srcW, double srcH)
    {
        if (srcW <= 0 || srcH <= 0) return (boxX, boxY, boxW, boxH);
        double k = Math.Min(boxW / srcW, boxH / srcH);
        double w = srcW * k, h = srcH * k;
        return (boxX + (boxW - w) / 2, boxY + (boxH - h) / 2, w, h);
    }
}

/// <summary>Dock'u gizleyecek tam ekran kararı (oyun, tam ekran video).</summary>
public static class FullscreenRule
{
    static readonly HashSet<string> Desktop = new(StringComparer.OrdinalIgnoreCase)
    { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow" };

    /// <summary>
    /// Yalnızca dock'un bulunduğu monitörü tamamen kaplayan ve masaüstü/kabuk sınıfı olmayan ön plan penceresi tam ekran
    /// sayılır. Başlık çubuklu büyütülmüş (maximize) pencere tam ekran değildir; büyütülmüş olsa da başlıksızsa
    /// (Chrome/YouTube HTML5 tam ekranı, oynatıcılar) tam ekrandır. Başka monitördeki pencere dock'u gizlemez.
    /// </summary>
    public static bool HidesDock(Box window, Box windowMonitor, Box dockMonitor, bool maximized, string className, bool hasCaption = true)
    {
        if (Desktop.Contains(className)) return false;
        if (windowMonitor != dockMonitor) return false;
        if (maximized && hasCaption) return false;
        return window.Left <= dockMonitor.Left && window.Top <= dockMonitor.Top && window.Right >= dockMonitor.Right && window.Bottom >= dockMonitor.Bottom;
    }

    // SHQueryUserNotificationState değerleri (belgelenmiş shell32 API'si)
    public const int QunsBusy = 2, QunsD3DFullScreen = 3, QunsPresentation = 4;

    /// <summary>
    /// Geometri kuralına Windows'un kendi "tam ekran" sinyali eklenir: D3D tam ekran (oyun/video) ya da sunum modu
    /// her zaman gizler; QUNS_BUSY (tam ekran uygulama) yalnızca ön plan penceresi dock'un monitöründeyse gizler.
    /// </summary>
    public static bool Decide(bool geometry, int quns, bool foregroundOnDockMonitor) =>
        geometry || quns is QunsD3DFullScreen or QunsPresentation || (quns == QunsBusy && foregroundOnDockMonitor);
}

/// <summary>
/// Durum değişikliğini art arda yeterince kez aynı görüldüğünde kabul eder (titreşmeyi önler).
/// true'ya geçiş <c>onTicks</c>, false'a dönüş <c>offTicks</c> gözlem ister.
/// </summary>
public sealed class Debounce
{
    readonly int _on, _off; int _count; bool _pending;
    public bool State { get; private set; }
    public Debounce(int ticks) : this(ticks, ticks) { }
    public Debounce(int onTicks, int offTicks) { _on = Math.Max(1, onTicks); _off = Math.Max(1, offTicks); }

    /// <summary>Yeni gözlem; kabul edilen durum değiştiyse true döner.</summary>
    public bool Observe(bool value)
    {
        if (value == State) { _count = 0; return false; }
        if (value != _pending) { _pending = value; _count = 0; }
        if (++_count < (value ? _on : _off)) return false;
        State = value; _count = 0;
        return true;
    }
}
