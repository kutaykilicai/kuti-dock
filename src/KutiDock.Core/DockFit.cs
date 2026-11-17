namespace KutiDock.Core;

/// <summary>Bir widget'ın doğal (küçültülmemiş) genişliği, DIP.</summary>
public sealed record FitItem(string Id, double Width);

/// <summary>Yerleşim kararı: ölçek (1 = olduğu gibi), az önemli widget'lar kompakt grupta mı, taşma menüsüne gidenler.</summary>
public sealed record FitPlan(double Scale, bool Compact, IReadOnlyList<string> Hidden)
{
    public static readonly FitPlan Natural = new(1, false, Array.Empty<string>());
    public bool SameAs(FitPlan o) => Compact == o.Compact && Hidden.SequenceEqual(o.Hidden) && Math.Abs(Scale - o.Scale) < 0.01;
}

/// <summary>
/// 1.6.9: dock bulunduğu ekranın çalışma alanına kenar boşluklarıyla sığar. 1.6.8'de ikinci ekranda (1920 px, %100) doğal
/// genişliği 2015 px olan şerit taşıyordu (ana ekran 2560 px'e sığıyordu). Sıra: (1) simgeler ve aralıklar orantılı küçülür
/// (en az %70); (2) ağ, WARP, disk ve CPU/RAM tek kompakt gruba geçer; (3) fazla öğeler "»" taşma menüsüne alınır.
/// Saat, ses, uygulamalar ve Başlat her zaman görünür. Sığıyorsa hiçbir şey değişmez (ana ekran aynen kalır).
/// </summary>
public static class DockFit
{
    public const double MinScale = 0.70;
    /// <summary>Kompakt grupta her widget bu ölçekte, iki satır hâlinde.</summary>
    public const double CompactScale = 0.5;
    public static readonly string[] CompactIds = ["network", "warp", "storage", "system"];
    public static readonly string[] Essential = ["clock", "volume", "apps", "start"];
    /// <summary>Taşma menüsüne alınma sırası (önce en az önemli); "compact" kompakt grubun tamamı.</summary>
    public static readonly string[] HideOrder = ["screenshot", "battery", "tray", "compact", "ai", "search"];

    /// <summary>Verilen düzende şeridin doğal genişliği (ölçek uygulanmadan).</summary>
    public static double Width(IReadOnlyList<FitItem> items, bool compact, IReadOnlyCollection<string> hidden, double overhead, double overflowButton)
    {
        double w = overhead;
        var group = new List<double>();
        foreach (var it in items)
        {
            if (hidden.Contains(it.Id)) continue;
            if (compact && CompactIds.Contains(it.Id)) { if (!hidden.Contains("compact")) group.Add(it.Width); continue; }
            w += it.Width;
        }
        // Kompakt grup: iki satırlı eşit hücreli ızgara (UniformGrid): sütun sayısı × en geniş widget × yarı ölçek
        if (group.Count > 0) w += (group.Count + 1) / 2 * group.Max() * CompactScale;
        if (hidden.Count > 0) w += overflowButton;
        return w;
    }

    // Aşağı yuvarlanır: yuvarlama yüzünden şerit kenar boşluğuna bir piksel bile taşmasın
    static double Floor3(double v, int _ = 3) => Math.Floor(v * 1000) / 1000;

    public static FitPlan Plan(double available, IReadOnlyList<FitItem> items, double overhead, double overflowButton = 44)
    {
        if (available <= 0 || items.Count == 0) return FitPlan.Natural;
        var none = Array.Empty<string>();
        double natural = Width(items, false, none, overhead, overflowButton);
        if (natural <= available) return FitPlan.Natural;
        double s = available / natural;
        if (s >= MinScale) return new FitPlan(Floor3(s, 3), false, none);
        bool hasCompact = items.Any(i => CompactIds.Contains(i.Id));
        double compactW = hasCompact ? Width(items, true, none, overhead, overflowButton) : natural;
        if (hasCompact && available / compactW >= MinScale) return new FitPlan(Floor3(Math.Min(1, available / compactW), 3), true, none);
        var hidden = new List<string>();
        foreach (var id in HideOrder)
        {
            if (id == "compact" ? !items.Any(i => CompactIds.Contains(i.Id)) : !items.Any(i => i.Id == id)) continue;
            hidden.Add(id);
            double w = Width(items, hasCompact, hidden, overhead, overflowButton);
            if (available / w >= MinScale) return new FitPlan(Floor3(Math.Min(1, available / w), 3), hasCompact, hidden);
        }
        // Yine sığmıyor: zorunlu öğeler kalır, şerit sığacak kadar küçülür
        double last = Width(items, hasCompact, hidden, overhead, overflowButton);
        return new FitPlan(Floor3(Math.Max(0.3, available / last), 3), hasCompact, hidden);
    }
}
