namespace KutiDock.Core;

/// <summary>Yay ayarı: tepki süresi (sn, bir salınımın süresi) ve sönüm oranı (1 = taşmasız, 1'den küçük = hafif taşma).</summary>
public readonly record struct SpringSpec(double Response, double Damping);

/// <summary>
/// 1.6.5: Apple tarzı hareket için yay (spring). Süreye bağlı eğri yerine konum + hız tutulur; hedef yarıda değişince
/// hareket kopmadan yeni hedefe döner (kesilebilir animasyon). Kütle 1, sertlik (2π/tepki)², sönüm 4π·oran/tepki.
/// </summary>
public static class Spring
{
    const double MaxStep = 1.0 / 240; // 60 fps karede 4 alt adım: kararlı ve ucuz

    public static (double X, double V) Step(double x, double v, double target, double dt, SpringSpec s)
    {
        if (dt <= 0) return (x, v);
        double k = Math.Pow(2 * Math.PI / s.Response, 2), c = 4 * Math.PI * s.Damping / s.Response;
        int n = Math.Max(1, (int)Math.Ceiling(dt / MaxStep));
        double h = dt / n;
        for (int i = 0; i < n; i++)
        {
            v += (-k * (x - target) - c * v) * h; // yarı örtük Euler
            x += v * h;
        }
        return (x, v);
    }

    /// <summary>Hedefe oturdu mu (konum ve hız yeterince küçük).</summary>
    public static bool Settled(double x, double v, double target, double eps = 0.001) =>
        Math.Abs(x - target) < eps && Math.Abs(v) < eps * 20;
}

/// <summary>
/// Videolardan çıkarılan Apple hareket ilkelerinin Kuti Dock değerleri: kısa ve yumuşak, abartısız; açılış hafif
/// taşmalı yay, kapanış taşmasız ve daha hızlı; panel kaynağından (dock) küçük, bulanık ve saydam başlayıp netleşir;
/// birden çok öğe varsa küçük gecikmeyle sırayla.
/// </summary>
public static class AppleMotion
{
    public static readonly SpringSpec
        MagnifyGrow = new(0.24, 0.86),   // imleç gelince: canlı, çok az taşma
        MagnifyShrink = new(0.30, 1.0),  // imleç gidince: taşmasız sönme
        PanelOpen = new(0.34, 0.80),     // panel/menü açılışı: hafif taşma
        PanelClose = new(0.20, 1.0),     // kapanış: kısa, taşmasız
        DockShow = new(0.32, 0.88),      // otomatik gizlemeden belirme
        DockHide = new(0.24, 1.0),       // kenara çekilme
        Press = new(0.30, 0.50);         // basınca zıplayıp yerine oturma

    /// <summary>Panel açılırken başlangıç ölçeği ve bulanıklığı (DIP); menülerde daha az.</summary>
    public const double PanelFromScale = 0.86, MenuFromScale = 0.94, PanelBlur = 6;

    /// <summary>Saydamlık ölçekten önce dolar (öğe görünür olunca hâlâ büyüyor olur).</summary>
    public static double Opacity(double progress) => Math.Clamp(progress * 1.6, 0, 1);

    /// <summary>Bulanıklık ilerleme 0'da tam, 0,7'de sıfır.</summary>
    public static double Blur(double progress, double max) => max * Math.Clamp(1 - progress / 0.7, 0, 1);

    /// <summary>
    /// Genie zamanlaması: küçültme yumuşak başlar ve simgeye yumuşakça girer (yarı doğrusal, yarı smoothstep;
    /// huni şekli zaten Genie.Compute'ta);
    /// geri açılma kritik sönümlü yayla hızlı başlar, pencere yerine taşmadan oturur. t, p: 0..1.
    /// </summary>
    public static double Genie(double t, bool expand)
    {
        t = Math.Clamp(t, 0, 1);
        if (!expand) return 0.5 * t + 0.5 * t * t * (3 - 2 * t);
        const double w = 7;
        double raw = 1 - (1 + w * t) * Math.Exp(-w * t), end = 1 - (1 + w) * Math.Exp(-w);
        return raw / end;
    }
}
