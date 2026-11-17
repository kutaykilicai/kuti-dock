namespace KutiDock.Core;

/// <summary>
/// 1.6.8: dock fareyi takip eder. Saklı dock, fare hangi monitörün dock kenarına (gerçek dış kenar) giderse o monitörde
/// belirir. İki ekran arasındaki ortak kenar dış kenar sayılmaz (fare öbür ekrana geçiyordur, yanlış tetiklenmez).
/// Tam ekran/oyun olan monitörde tetiklenmez; diğer monitörde tetiklenebilir.
/// </summary>
public static class DockMonitors
{
    /// <summary>
    /// Fare monitörün dock kenarındaki şeritte mi ve o kenar dış kenar mı (kenarın hemen ötesinde başka monitör yok).
    /// <paramref name="onAnyMonitor"/>: verilen piksel herhangi bir monitörde mi (MonitorFromPoint DEFAULTTONULL).
    /// </summary>
    public static bool AtOuterEdge(DockEdge edge, int x, int y, Box monitor, Func<int, int, bool> onAnyMonitor, int band = 2)
    {
        if (!AutoHide.AtEdge(edge, x, y, monitor, band)) return false;
        var (bx, by) = edge switch
        {
            DockEdge.Ust => (x, monitor.Top - 1),
            DockEdge.Sol => (monitor.Left - 1, y),
            DockEdge.Sag => (monitor.Right, y),
            _ => (x, monitor.Bottom),
        };
        return !onAnyMonitor(bx, by);
    }

    /// <summary>Dock hangi monitörde olmalı (monitör kimlikleri: HMONITOR değerleri).</summary>
    public static long Choose(long current, long cursorMonitor, long primary, bool follow, bool primaryOnly,
        bool cursorAtOuterEdge, bool fullscreenOnCursorMonitor)
    {
        if (primaryOnly) return primary;
        if (!follow) return current;
        if (cursorMonitor != 0 && cursorMonitor != current && cursorAtOuterEdge && !fullscreenOnCursorMonitor) return cursorMonitor;
        return current;
    }

    /// <summary>Per-Monitor V2: DIP boyutunun hedef monitördeki piksel karşılığı.</summary>
    public static int ToPixels(double dip, uint monitorDpi) => (int)Math.Round(dip * monitorDpi / 96.0);
}
