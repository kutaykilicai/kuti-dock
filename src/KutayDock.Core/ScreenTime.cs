namespace KutayDock.Core;

/// <summary>Ön plandaki uygulamaya göre günlük ekran süresi. Yalnızca bu PC'de, JSON dosyasında tutulur.</summary>
public sealed class ScreenTimeDay
{
    public string Date { get; set; } = "";
    public Dictionary<string, double> Apps { get; set; } = new();
    public double[] Hours { get; set; } = new double[24];
    public double TotalSeconds => Apps.Values.Sum();
}

public sealed class ScreenTimeStore
{
    public Dictionary<string, ScreenTimeDay> Days { get; set; } = new();

    public static string Key(DateTime localDate) => localDate.ToString("yyyy-MM-dd");

    public ScreenTimeDay Day(DateTime local)
    {
        var k = Key(local.Date);
        if (!Days.TryGetValue(k, out var d)) Days[k] = d = new ScreenTimeDay { Date = k };
        return d;
    }

    /// <summary>Yerel zamanda "local" anında biten "seconds" süreyi uygulamaya yaz; saat sınırını böler.</summary>
    public void Add(string app, DateTime localEnd, double seconds)
    {
        if (seconds <= 0 || string.IsNullOrWhiteSpace(app)) return;
        var start = localEnd.AddSeconds(-seconds);
        var t = start;
        while (t < localEnd)
        {
            var hourEnd = t.Date.AddHours(t.Hour + 1);
            var segEnd = hourEnd < localEnd ? hourEnd : localEnd;
            var sec = (segEnd - t).TotalSeconds;
            var d = Day(t);
            d.Apps[app] = d.Apps.GetValueOrDefault(app) + sec;
            d.Hours[t.Hour] += sec;
            t = segEnd;
        }
    }

    public void Prune(DateTime today, int keepDays = 35)
    {
        var min = Key(today.Date.AddDays(-keepDays));
        foreach (var k in Days.Keys.Where(k => string.CompareOrdinal(k, min) < 0).ToList()) Days.Remove(k);
    }

    public IEnumerable<(string App, double Seconds)> Top(DateTime local, int n) =>
        Day(local).Apps.OrderByDescending(kv => kv.Value).Take(n).Select(kv => (kv.Key, kv.Value));
}
