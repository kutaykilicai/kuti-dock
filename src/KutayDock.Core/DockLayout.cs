namespace KutayDock.Core;

/// <summary>Dock'un ekran kenarına göre yerleşimi (WPF birimleri). Üst/Alt yatay, Sol/Sağ dikey.</summary>
public static class DockLayout
{
    public static bool Vertical(DockEdge e) => e is DockEdge.Sol or DockEdge.Sag;

    /// <summary>Dock penceresinin sol-üst köşesi: kenara <paramref name="margin"/> kadar yakın, diğer eksende ortalı.</summary>
    public static (double X, double Y) Position(DockEdge e, double areaX, double areaY, double areaW, double areaH, double w, double h, double margin) => e switch
    {
        DockEdge.Ust => (areaX + (areaW - w) / 2, areaY + margin),
        DockEdge.Sol => (areaX + margin, areaY + (areaH - h) / 2),
        DockEdge.Sag => (areaX + areaW - w - margin, areaY + (areaH - h) / 2),
        _ => (areaX + (areaW - w) / 2, areaY + areaH - h - margin),
    };

    /// <summary>
    /// Dock'tan açılan panelin (pencere önizlemesi vb.) konumu: dock'tan ekranın içine doğru, düğmeye göre ortalı
    /// ve ekran (alan) içinde kalacak şekilde.
    /// </summary>
    public static (double X, double Y) Popup(DockEdge e, double ax, double ay, double aw, double ah, double w, double h, double gap,
        double areaX, double areaY, double areaW, double areaH)
    {
        double x, y;
        switch (e)
        {
            case DockEdge.Ust: x = ax + aw / 2 - w / 2; y = ay + ah + gap; break;
            case DockEdge.Sol: x = ax + aw + gap; y = ay + ah / 2 - h / 2; break;
            case DockEdge.Sag: x = ax - w - gap; y = ay + ah / 2 - h / 2; break;
            default: x = ax + aw / 2 - w / 2; y = ay - h - gap; break;
        }
        if (Vertical(e)) y = Math.Max(areaY + 4, Math.Min(y, areaY + areaH - h - 4));
        else x = Math.Max(areaX + 4, Math.Min(x, areaX + areaW - w - 4));
        return (x, y);
    }

    /// <summary>Simge büyütmesinin merkezi: dock kenarından ekranın içine doğru büyür.</summary>
    public static (double X, double Y) ZoomOrigin(DockEdge e) => e switch
    {
        DockEdge.Ust => (0.5, 0.25),
        DockEdge.Sol => (0.25, 0.5),
        DockEdge.Sag => (0.75, 0.5),
        _ => (0.5, 0.75),
    };
}
