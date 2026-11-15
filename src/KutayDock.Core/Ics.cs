using System.Globalization;

namespace KutayDock.Core;

public sealed record CalEvent(DateTimeOffset Start, DateTimeOffset End, string Title, bool AllDay);

/// <summary>
/// Salt okunur iCal (.ics) okuyucu. Desteklenen: VEVENT, DTSTART/DTEND (UTC, TZID, yerel, VALUE=DATE),
/// DURATION, RRULE (DAILY/WEEKLY+BYDAY/MONTHLY/YEARLY, INTERVAL, COUNT, UNTIL), EXDATE.
/// Desteklenmeyen: RECURRENCE-ID ile tek tekrar düzenleme, BYSETPOS gibi ileri RRULE kuralları.
/// </summary>
public static class Ics
{
    public static List<CalEvent> Parse(string text, DateTimeOffset from, DateTimeOffset to, TimeZoneInfo? local = null)
    {
        local ??= TimeZoneInfo.Local;
        var result = new List<CalEvent>();
        var lines = Unfold(text);
        Dictionary<string, (string param, string value)>? cur = null;
        List<string> exdates = new();
        foreach (var line in lines)
        {
            if (line == "BEGIN:VEVENT") { cur = new(); exdates = new(); continue; }
            if (line == "END:VEVENT")
            {
                if (cur != null) Expand(cur, exdates, from, to, local, result);
                cur = null; continue;
            }
            if (cur == null) continue;
            int colon = IndexOfValueColon(line);
            if (colon < 0) continue;
            var head = line[..colon]; var value = line[(colon + 1)..];
            int semi = head.IndexOf(';');
            var name = (semi < 0 ? head : head[..semi]).ToUpperInvariant();
            var param = semi < 0 ? "" : head[(semi + 1)..];
            if (name == "EXDATE") { foreach (var v in value.Split(',')) exdates.Add(param + "|" + v); continue; }
            cur.TryAdd(name, (param, value));
        }
        result.Sort((a, b) => a.Start.CompareTo(b.Start));
        return result;
    }

    /// <summary>Şu an süren ya da sıradaki (tüm gün olmayan) etkinlik.</summary>
    public static CalEvent? Next(IEnumerable<CalEvent> events, DateTimeOffset now) =>
        events.Where(e => !e.AllDay && e.End > now).OrderBy(e => e.Start).FirstOrDefault();

    static int IndexOfValueColon(string line)
    {
        bool q = false;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') q = !q;
            else if (line[i] == ':' && !q) return i;
        }
        return -1;
    }

    static List<string> Unfold(string text)
    {
        var raw = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var list = new List<string>();
        foreach (var l in raw)
        {
            if ((l.StartsWith(' ') || l.StartsWith('\t')) && list.Count > 0) list[^1] += l[1..];
            else if (l.Length > 0) list.Add(l);
        }
        return list;
    }

    static string Unescape(string s) =>
        s.Replace("\\n", " ").Replace("\\N", " ").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\").Trim();

    public static (DateTimeOffset when, bool allDay) ParseDate(string param, string value, TimeZoneInfo local)
    {
        value = value.Trim();
        bool isDate = param.Contains("VALUE=DATE", StringComparison.OrdinalIgnoreCase) && !param.Contains("DATE-TIME", StringComparison.OrdinalIgnoreCase)
                      || value.Length == 8;
        if (isDate)
        {
            var d = DateTime.ParseExact(value[..8], "yyyyMMdd", CultureInfo.InvariantCulture);
            return (new DateTimeOffset(d, local.GetUtcOffset(d)), true);
        }
        bool utc = value.EndsWith('Z');
        var dt = DateTime.ParseExact(value.TrimEnd('Z')[..15], "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture);
        if (utc) return (new DateTimeOffset(dt, TimeSpan.Zero), false);
        var tz = local;
        var tzid = GetParam(param, "TZID");
        if (tzid != null && TryZone(tzid, out var z)) tz = z;
        return (new DateTimeOffset(dt, tz.GetUtcOffset(dt)), false);
    }

    static string? GetParam(string param, string key)
    {
        foreach (var p in param.Split(';'))
        {
            var kv = p.Split('=', 2);
            if (kv.Length == 2 && kv[0].Equals(key, StringComparison.OrdinalIgnoreCase)) return kv[1].Trim('"');
        }
        return null;
    }

    static bool TryZone(string id, out TimeZoneInfo tz)
    {
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(id); return true; } catch { }
        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var w))
            try { tz = TimeZoneInfo.FindSystemTimeZoneById(w); return true; } catch { }
        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var i))
            try { tz = TimeZoneInfo.FindSystemTimeZoneById(i); return true; } catch { }
        tz = TimeZoneInfo.Utc; return false;
    }

    static TimeSpan ParseDuration(string v)
    {
        // P1D, PT1H30M, -PT15M, P1W
        bool neg = v.StartsWith('-'); v = v.TrimStart('-', '+');
        TimeSpan t = TimeSpan.Zero; int num = 0; bool inTime = false;
        foreach (var c in v)
        {
            if (char.IsDigit(c)) { num = num * 10 + (c - '0'); continue; }
            switch (c)
            {
                case 'T': inTime = true; break;
                case 'W': t += TimeSpan.FromDays(7 * num); break;
                case 'D': t += TimeSpan.FromDays(num); break;
                case 'H': t += TimeSpan.FromHours(num); break;
                case 'M': t += inTime ? TimeSpan.FromMinutes(num) : TimeSpan.Zero; break;
                case 'S': t += TimeSpan.FromSeconds(num); break;
            }
            if (c != 'P' && c != 'T') num = 0;
        }
        return neg ? -t : t;
    }

    static void Expand(Dictionary<string, (string param, string value)> ev, List<string> exdates,
        DateTimeOffset from, DateTimeOffset to, TimeZoneInfo local, List<CalEvent> output)
    {
        if (!ev.TryGetValue("DTSTART", out var ds)) return;
        if (ev.TryGetValue("STATUS", out var st) && st.value.Trim().Equals("CANCELLED", StringComparison.OrdinalIgnoreCase)) return;
        var (start, allDay) = ParseDate(ds.param, ds.value, local);
        TimeSpan length;
        if (ev.TryGetValue("DTEND", out var de)) length = ParseDate(de.param, de.value, local).when - start;
        else if (ev.TryGetValue("DURATION", out var du)) length = ParseDuration(du.value.Trim());
        else length = allDay ? TimeSpan.FromDays(1) : TimeSpan.Zero;
        var title = ev.TryGetValue("SUMMARY", out var s) ? Unescape(s.value) : "(Başlıksız)";

        var ex = new HashSet<DateTimeOffset>();
        foreach (var e in exdates)
        {
            var parts = e.Split('|', 2);
            try { ex.Add(ParseDate(parts[0], parts[1], local).when); } catch { }
        }

        void Add(DateTimeOffset st0)
        {
            if (ex.Contains(st0)) return;
            var en = st0 + length;
            if (en > from && st0 < to) output.Add(new CalEvent(st0, en, title, allDay));
        }

        if (!ev.TryGetValue("RRULE", out var rr)) { Add(start); return; }

        var rule = rr.value.Split(';').Select(p => p.Split('=', 2)).Where(p => p.Length == 2)
            .ToDictionary(p => p[0].ToUpperInvariant(), p => p[1]);
        var freq = rule.GetValueOrDefault("FREQ", "");
        int interval = int.TryParse(rule.GetValueOrDefault("INTERVAL"), out var iv) && iv > 0 ? iv : 1;
        int count = int.TryParse(rule.GetValueOrDefault("COUNT"), out var cn) ? cn : int.MaxValue;
        DateTimeOffset until = DateTimeOffset.MaxValue;
        if (rule.TryGetValue("UNTIL", out var u)) until = ParseDate("", u, local).when + (u.Trim().Length == 8 ? TimeSpan.FromDays(1) - TimeSpan.FromTicks(1) : TimeSpan.Zero);
        var byDay = rule.TryGetValue("BYDAY", out var bd)
            ? bd.Split(',').Select(x => DayCode(x.Trim()[^2..])).Where(x => x != null).Select(x => x!.Value).ToHashSet()
            : null;

        // Yerel saat (duvar saati) üzerinden ilerle, DST'de saat kaymasın.
        var tz = local;
        var tzid = GetParam(ds.param, "TZID");
        if (tzid != null && TryZone(tzid, out var z)) tz = z;
        var wall = ds.value.EndsWith('Z') ? TimeZoneInfo.ConvertTime(start, tz).DateTime : start.DateTime;
        DateTimeOffset At(DateTime w) => ds.value.EndsWith('Z') || tzid != null
            ? new DateTimeOffset(w, tz.GetUtcOffset(w)) : new DateTimeOffset(w, local.GetUtcOffset(w));

        int produced = 0;
        for (int step = 0; step < 5000 && produced < count; step++)
        {
            IEnumerable<DateTime> candidates;
            switch (freq)
            {
                case "DAILY": candidates = new[] { wall.AddDays(step * interval) }; break;
                case "WEEKLY":
                    var weekStart = wall.AddDays(7 * step * interval);
                    if (byDay == null || byDay.Count == 0) candidates = new[] { weekStart };
                    else
                    {
                        var monday = weekStart.AddDays(-(((int)weekStart.DayOfWeek + 6) % 7));
                        candidates = Enumerable.Range(0, 7).Select(i => monday.AddDays(i))
                            .Where(d => byDay.Contains(d.DayOfWeek) && d >= wall);
                    }
                    break;
                case "MONTHLY": candidates = new[] { wall.AddMonths(step * interval) }; break;
                case "YEARLY": candidates = new[] { wall.AddYears(step * interval) }; break;
                default: Add(start); return;
            }
            foreach (var c in candidates)
            {
                var at = At(c);
                if (at > until || produced >= count) return;
                produced++;
                if (at > to) return;
                Add(at);
            }
        }
    }

    static DayOfWeek? DayCode(string c) => c.ToUpperInvariant() switch
    {
        "MO" => DayOfWeek.Monday, "TU" => DayOfWeek.Tuesday, "WE" => DayOfWeek.Wednesday,
        "TH" => DayOfWeek.Thursday, "FR" => DayOfWeek.Friday, "SA" => DayOfWeek.Saturday,
        "SU" => DayOfWeek.Sunday, _ => null,
    };
}
