using System.Globalization;
using System.Text.Json;

namespace KutayDock.Core;

/// <summary>Dock'taki "Oturum / Haftalık" çubuklarının verisi.</summary>
public sealed record AiUsageSnapshot(
    string Provider,
    double SessionFraction, DateTimeOffset? SessionResetsAt, string SessionDetail,
    double WeeklyFraction, DateTimeOffset? WeeklyResetsAt, string WeeklyDetail,
    string? TopModel);

/// <summary>
/// Claude Code'un yerel kayıtlarından (~/.claude/projects/**/*.jsonl) token sayar.
/// Anthropic'in gerçek limit yüzdesini yayınladığı resmi bir yerel kaynak yok; yüzde,
/// kullanıcının Ayarlar'da girdiği token limitine göre hesaplanır (tahmini).
/// Oturum = 5 saatlik blok: ilk mesajın saat başına yuvarlanmış zamanında başlar.
/// </summary>
public static class ClaudeUsage
{
    public sealed record Entry(DateTimeOffset Time, long Tokens, string? Model, string Key);

    public static IEnumerable<Entry> ParseLines(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            if (line.Length < 20 || !line.Contains("\"usage\"")) continue;
            Entry? e = null;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (!root.TryGetProperty("timestamp", out var ts)) continue;
                if (!root.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object) continue;
                if (!msg.TryGetProperty("usage", out var u)) continue;
                long tok = 0;
                foreach (var k in new[] { "input_tokens", "output_tokens", "cache_creation_input_tokens", "cache_read_input_tokens" })
                    if (u.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number) tok += v.GetInt64();
                var model = msg.TryGetProperty("model", out var m) ? m.GetString() : null;
                var id = msg.TryGetProperty("id", out var mid) ? mid.GetString() : null;
                var req = root.TryGetProperty("requestId", out var r) ? r.GetString() : null;
                var key = (id ?? "") + ":" + (req ?? "");
                if (key == ":") key = ts.GetString() + ":" + tok;
                e = new Entry(DateTimeOffset.Parse(ts.GetString()!, CultureInfo.InvariantCulture), tok, model, key);
            }
            catch (Exception) { }
            if (e != null) yield return e;
        }
    }

    public static DateTimeOffset WeekStart(DateTimeOffset now, DayOfWeek resetDay, int resetHour)
    {
        var local = now.ToLocalTime();
        var d = local.Date.AddHours(resetHour);
        int back = ((int)local.DayOfWeek - (int)resetDay + 7) % 7;
        d = d.AddDays(-back);
        var start = new DateTimeOffset(d, local.Offset);
        if (start > now) start = start.AddDays(-7);
        return start;
    }

    public static AiUsageSnapshot Compute(IEnumerable<Entry> entries, DateTimeOffset now,
        long sessionLimit, long weeklyLimit, DayOfWeek resetDay, int resetHour)
    {
        var seen = new HashSet<string>();
        var list = entries.Where(e => seen.Add(e.Key)).OrderBy(e => e.Time).ToList();

        // 5 saatlik bloklar
        DateTimeOffset? blockStart = null; DateTimeOffset last = DateTimeOffset.MinValue; long blockTokens = 0;
        foreach (var e in list)
        {
            if (blockStart == null || e.Time >= blockStart.Value.AddHours(5) || e.Time - last >= TimeSpan.FromHours(5))
            {
                var t = e.Time.ToUniversalTime();
                blockStart = new DateTimeOffset(t.Year, t.Month, t.Day, t.Hour, 0, 0, TimeSpan.Zero);
                blockTokens = 0;
            }
            blockTokens += e.Tokens; last = e.Time;
        }
        bool active = blockStart != null && now < blockStart.Value.AddHours(5);
        long sessionTokens = active ? blockTokens : 0;
        DateTimeOffset? sessionReset = active ? blockStart!.Value.AddHours(5) : null;

        var ws = WeekStart(now, resetDay, resetHour);
        var week = list.Where(e => e.Time >= ws && e.Time <= now).ToList();
        long weekTokens = week.Sum(e => e.Tokens);
        var top = week.Where(e => !string.IsNullOrEmpty(e.Model) && !e.Model!.StartsWith('<'))
            .GroupBy(e => e.Model).OrderByDescending(g => g.Sum(x => x.Tokens)).Select(g => g.Key).FirstOrDefault();

        return new AiUsageSnapshot("Claude Code",
            sessionLimit > 0 ? Format.Clamp01((double)sessionTokens / sessionLimit) : 0, sessionReset, Format.Tokens(sessionTokens),
            weeklyLimit > 0 ? Format.Clamp01((double)weekTokens / weeklyLimit) : 0, ws.AddDays(7), Format.Tokens(weekTokens),
            PrettyModel(top));
    }

    public static string? PrettyModel(string? m)
    {
        if (string.IsNullOrEmpty(m)) return null;
        // claude-opus-4-5-20251101 -> Opus 4.5
        var parts = m.Split('-');
        if (parts.Length >= 3 && parts[0] == "claude")
        {
            var name = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(parts[1]);
            var ver = string.Join('.', parts.Skip(2).TakeWhile(p => p.Length <= 2 && p.All(char.IsDigit)));
            return ver.Length > 0 ? $"{name} {ver}" : name;
        }
        return m;
    }
}

/// <summary>
/// Codex CLI (ChatGPT planı) kendi oturum kayıtlarına (~/.codex/sessions/**/*.jsonl) gerçek limit
/// yüzdelerini "rate_limits" olarak yazar. En yeni kayıttaki son değeri okuruz; tahmin yok.
/// </summary>
public static class CodexUsage
{
    public static AiUsageSnapshot? FromLines(IEnumerable<string> lines)
    {
        AiUsageSnapshot? last = null;
        foreach (var line in lines)
        {
            if (!line.Contains("\"rate_limits\"")) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                var ts = root.TryGetProperty("timestamp", out var t) && t.ValueKind == JsonValueKind.String
                    ? DateTimeOffset.Parse(t.GetString()!, CultureInfo.InvariantCulture) : DateTimeOffset.UtcNow;
                var rl = Find(root, "rate_limits");
                if (rl == null || rl.Value.ValueKind != JsonValueKind.Object) continue;
                var (p, pr) = Window(rl.Value, "primary", ts);
                var (s, sr) = Window(rl.Value, "secondary", ts);
                last = new AiUsageSnapshot("Codex", p, pr, $"%{p * 100:0}", s, sr, $"%{s * 100:0}", null);
            }
            catch (Exception) { }
        }
        return last;
    }

    static (double, DateTimeOffset?) Window(JsonElement rl, string name, DateTimeOffset ts)
    {
        if (!rl.TryGetProperty(name, out var w) || w.ValueKind != JsonValueKind.Object) return (0, null);
        double used = w.TryGetProperty("used_percent", out var u) && u.ValueKind == JsonValueKind.Number ? u.GetDouble() : 0;
        DateTimeOffset? reset = null;
        if (w.TryGetProperty("resets_at", out var ra) && ra.ValueKind == JsonValueKind.Number) reset = DateTimeOffset.FromUnixTimeSeconds(ra.GetInt64());
        else if (w.TryGetProperty("resets_in_seconds", out var ri) && ri.ValueKind == JsonValueKind.Number) reset = ts.AddSeconds(ri.GetDouble());
        return (Format.Clamp01(used / 100.0), reset);
    }

    static JsonElement? Find(JsonElement e, string name)
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in e.EnumerateObject())
            {
                if (p.Name == name) return p.Value;
                var r = Find(p.Value, name);
                if (r != null) return r;
            }
        }
        return null;
    }
}
