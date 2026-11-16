namespace KutayDock.Core;

public sealed record Alert(string Key, string Title, string Body);

public sealed record AlertInput(DateTimeOffset Now, ManualLimit? GrokBot, ManualLimit? Cursor, int? BatteryPercent, bool Charging);

/// <summary>
/// "Akıllı" kurallar: limit eşiği ve pil. Aynı uyarıyı iki kez vermez (anahtar hafızası),
/// sessiz saatlerde bekletir ve sessiz saat bitince bekleyenleri bırakır.
/// </summary>
public sealed class AlertEngine
{
    readonly HashSet<string> _fired = new();
    readonly List<Alert> _held = new();

    public static bool InQuietHours(Settings s, DateTimeOffset now)
    {
        if (!s.QuietHoursEnabled || s.QuietStartHour == s.QuietEndHour) return false;
        int h = now.ToLocalTime().Hour;
        return s.QuietStartHour < s.QuietEndHour
            ? h >= s.QuietStartHour && h < s.QuietEndHour
            : h >= s.QuietStartHour || h < s.QuietEndHour;
    }

    public List<Alert> Evaluate(Settings s, AlertInput i)
    {
        var fresh = new List<Alert>();

        if (s.AlertAiLimit)
        {
            Limit(fresh, "Grok Bot", i.GrokBot, s, i.Now);
            Limit(fresh, "Cursor", i.Cursor, s, i.Now);
        }

        if (s.AlertBattery && i.BatteryPercent is { } b && !i.Charging && b <= s.BatteryAlertPercent)
            Fire(fresh, new Alert($"bat:{i.Now.ToLocalTime():yyyyMMdd}:{s.BatteryAlertPercent}", "Pil azaldı", $"%{b} kaldı, şarja tak"));

        if (InQuietHours(s, i.Now)) { _held.AddRange(fresh); return new(); }
        var outList = new List<Alert>(_held);
        _held.Clear();
        outList.AddRange(fresh);
        return outList;
    }

    void Limit(List<Alert> into, string name, ManualLimit? l, Settings s, DateTimeOffset now)
    {
        if (l?.Percent is not { } p || p < s.AiAlertPercent) return;
        if (l.ResetsAt is { } r && r <= now) return; // süre dolmuş, değer bayat
        var key = $"lim:{name}:{l.ResetsAt?.ToUnixTimeSeconds()}:{s.AiAlertPercent}";
        var body = l.ResetsAt is { } rr ? $"{Format.Duration(rr - now)} sonra sıfırlanıyor" : "Sıfırlanma zamanı girilmemiş";
        Fire(into, new Alert(key, $"{name} limiti %{p:0}", body));
    }

    void Fire(List<Alert> into, Alert a)
    {
        if (_fired.Add(a.Key)) into.Add(a);
    }
}
