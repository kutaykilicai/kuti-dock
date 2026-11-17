using System;

namespace KutiDock.Core;

/// <summary>
/// 1.6.4: pencerenin kendi küçültme düğmesinde Genie. Hangi ön pencere izlenir (görüntüsü alınır, Windows küçültme
/// animasyonu yalnızca onun için kapatılır) ve ne sıklıkla görüntü tazelenir.
/// </summary>
public static class MinimizeGeniePolicy
{
    /// <summary>Ön plan değişince ilk görüntü (pencere çizilsin diye kısa gecikme) ve sonraki tazeleme aralığı.</summary>
    public static readonly TimeSpan FirstCapture = TimeSpan.FromMilliseconds(300), Refresh = TimeSpan.FromSeconds(2.5);

    /// <summary>Küçültmede kullanılabilecek en eski görüntü.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(30);

    /// <summary>
    /// İzlenir mi: animasyonlar açık, dock'un kendi penceresi değil, görev çubuğu türünde uygulama penceresi,
    /// tam ekran değil (video/oyun) ve oyun çalışmıyor.
    /// </summary>
    public static bool Track(bool enabled, bool ownWindow, bool appWindow, bool fullscreen, bool gameRunning) =>
        enabled && !ownWindow && appWindow && !fullscreen && !gameRunning;

    public static bool Usable(DateTime capturedAt, DateTime now) => now - capturedAt <= MaxAge && now >= capturedAt;
}
