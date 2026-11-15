namespace KutayDock.Core;

public enum AlertAction { None, PauseMedia }

public sealed record Alert(string Key, string Title, string Body, AlertAction Action = AlertAction.None);

public sealed record AlertInput(
    DateTimeOffset Now,
    CalEvent? NextEvent,
    bool MediaPlaying,
    AiUsageSnapshot? Ai,
    int? BatteryPercent, bool Charging);

/// <summary>
/// Widget'ların bildiklerini birleştiren "akıllı" kurallar. Saf mantık: aynı uyarıyı iki kez vermez
/// (anahtar hafızası), sessiz saatlerde bekletir ve sessiz saat bitince bekleyenleri bırakır.
/// </summary>
public sealed class AlertEngine
{
    readonly HashSet<string> _fired = new();
    readonly List<Alert> _held = new();

    public IReadOnlyCollection<string> Fired => _fired;

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

        if (s.AlertMeeting && i.NextEvent is { } ev)
        {
            var until = ev.Start - i.Now;
            if (until > TimeSpan.Zero && until <= TimeSpan.FromMinutes(s.MeetingLeadMinutes))
            {
                var key = $"meet:{ev.Start.ToUnixTimeSeconds()}:{ev.Title}";
                var action = s.PauseMediaBeforeMeeting && i.MediaPlaying ? AlertAction.PauseMedia : AlertAction.None;
                var body = $"{Format.Duration(until)} sonra başlıyor" + (action == AlertAction.PauseMedia ? " · müzik duraklatıldı" : "");
                Fire(fresh, new Alert(key, ev.Title, body, action));
            }
        }

        if (s.AlertAiLimit && i.Ai is { } ai)
        {
            double th = s.AiAlertPercent / 100.0;
            if (ai.SessionFraction >= th && ai.SessionResetsAt is { } sr)
                Fire(fresh, new Alert($"ai-s:{ai.Provider}:{sr.ToUnixTimeSeconds()}",
                    $"{ai.Provider} oturum limiti %{ai.SessionFraction * 100:0}",
                    $"{Format.Duration(sr - i.Now)} sonra sıfırlanıyor"));
            if (ai.WeeklyFraction >= th && ai.WeeklyResetsAt is { } wr)
                Fire(fresh, new Alert($"ai-w:{ai.Provider}:{wr.ToUnixTimeSeconds()}",
                    $"{ai.Provider} haftalık limit %{ai.WeeklyFraction * 100:0}",
                    $"{Format.Duration(wr - i.Now)} sonra sıfırlanıyor"));
        }

        if (s.AlertBattery && i.BatteryPercent is { } b && !i.Charging && b <= s.BatteryAlertPercent)
            Fire(fresh, new Alert($"bat:{i.Now.ToLocalTime():yyyyMMdd}:{s.BatteryAlertPercent}", "Pil azaldı", $"%{b} kaldı, şarja tak"));

        if (InQuietHours(s, i.Now))
        {
            // Toplantı için müzik duraklatma sessiz saatte de çalışır, ama bildirim bekletilir.
            _held.AddRange(fresh);
            return fresh.Where(a => a.Action != AlertAction.None)
                .Select(a => a with { Title = "", Body = "" }).ToList();
        }
        var outList = new List<Alert>(_held.Where(h => h.Action == AlertAction.None)); // bekleyenler (eylemsiz)
        _held.Clear();
        outList.AddRange(fresh);
        return outList;
    }

    void Fire(List<Alert> into, Alert a)
    {
        if (_fired.Add(a.Key)) into.Add(a);
    }
}
