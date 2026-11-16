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
