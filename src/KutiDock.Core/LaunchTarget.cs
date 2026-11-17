namespace KutiDock.Core;

/// <summary>
/// 1.6.10: ilk açılış Genie'sinin hedefi dock otomatik gizliyken de bulunur.
/// Hedef, dock açıldığında simgenin duracağı yer: kayma animasyonu hesaba katılmaz, dock'un ekrandaki konumu ve DPI'si kullanılır.
/// </summary>
public static class LaunchTarget
{
    /// <summary>Genie sırasında saklı dock'un görünür kalacağı süre (sonra her zamanki 0,6 sn gecikmeyle saklanır).</summary>
    public const int PeekMs = 1500;

    /// <summary>Saklı dock ilk açılışta kısa süre gösterilsin mi: yalnızca otomatik gizlemede, kullanıcı gizlemediyse, tam ekran ya da oyun yokken.</summary>
    public static bool ShouldPeek(bool animations, bool autoHideOn, bool autoHidden, bool userHidden, bool hiddenForFullscreen, bool fullscreenOnMonitor, bool gameForeground) =>
        animations && autoHideOn && autoHidden && !userHidden && !hiddenForFullscreen && !fullscreenOnMonitor && !gameForeground;

    /// <summary>Pencere içi DIP dikdörtgeni → ekran pikseli. <paramref name="slideX"/>/<paramref name="slideY"/>: kayma animasyonunun o anki ötelemesi (çıkarılır).</summary>
    public static Box ToScreen(int windowLeft, int windowTop, double x, double y, double width, double height, double scale, double slideX = 0, double slideY = 0)
    {
        double l = windowLeft + (x - slideX) * scale, t = windowTop + (y - slideY) * scale;
        return new Box((int)Math.Round(l), (int)Math.Round(t), (int)Math.Round(l + width * scale), (int)Math.Round(t + height * scale));
    }

    /// <summary>
    /// Simgesi henüz olmayan uygulama: yeni simge son simgenin ardına eklenir (yatayda sağına, dikeyde altına).
    /// Hiç simge yoksa şeridin ortasında <paramref name="size"/> boyutlu kare.
    /// </summary>
    public static (double X, double Y, double W, double H) NextSlot((double X, double Y, double W, double H)? last, bool vertical, double centerX, double centerY, double size)
    {
        if (last is { } l) return vertical ? (l.X, l.Y + l.H, l.W, l.H) : (l.X + l.W, l.Y, l.W, l.H);
        return (centerX - size / 2, centerY - size / 2, size, size);
    }
}
