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
    /// Yalnızca dock'un bulunduğu monitörü tamamen kaplayan, büyütülmemiş (maximize olmayan) ve masaüstü/kabuk sınıfı
    /// olmayan ön plan penceresi tam ekran sayılır. Başka monitördeki tam ekran ya da büyütülmüş pencere dock'u gizlemez.
    /// </summary>
    public static bool HidesDock(Box window, Box windowMonitor, Box dockMonitor, bool maximized, string className)
    {
        if (Desktop.Contains(className) || maximized) return false;
        if (windowMonitor != dockMonitor) return false;
        return window.Left <= dockMonitor.Left && window.Top <= dockMonitor.Top && window.Right >= dockMonitor.Right && window.Bottom >= dockMonitor.Bottom;
    }
}

/// <summary>Durum değişikliğini, art arda <c>ticks</c> kez aynı görüldüğünde kabul eder (titreşmeyi önler).</summary>
public sealed class Debounce
{
    readonly int _ticks; int _count; bool _pending;
    public bool State { get; private set; }
    public Debounce(int ticks) { _ticks = Math.Max(1, ticks); }

    /// <summary>Yeni gözlem; kabul edilen durum değiştiyse true döner.</summary>
    public bool Observe(bool value)
    {
        if (value == State) { _count = 0; return false; }
        if (value != _pending) { _pending = value; _count = 0; }
        if (++_count < _ticks) return false;
        State = value; _count = 0;
        return true;
    }
}
