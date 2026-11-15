using KutayDock.Core;
using Xunit;

public class FormatTests
{
    [Theory]
    [InlineData(0, "<1dk")]
    [InlineData(45, "45dk")]
    [InlineData(60, "1s")]
    [InlineData(160, "2s 40dk")]
    [InlineData(60 * 27, "1g 3s")]
    public void Duration(int minutes, string expected) => Assert.Equal(expected, Format.Duration(TimeSpan.FromMinutes(minutes)));

    [Fact]
    public void Rate()
    {
        Assert.Equal("512 B/s", Format.Rate(512));
        Assert.Equal("2 KB/s", Format.Rate(2048));
        Assert.Equal("1,5 MB/s", Format.Rate(1.5 * 1024 * 1024));
    }

    [Fact]
    public void Tokens() { Assert.Equal("48,1M", Format.Tokens(48_100_000)); Assert.Equal("950", Format.Tokens(950)); }

    [Fact]
    public void Cpu() => Assert.Equal(25.0, Format.CpuPercent(0, 0, 0, 750, 800, 200));
}

public class IcsTests
{
    static readonly TimeZoneInfo Ist = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    const string Sample = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nDTSTART:20261116T113000Z\r\nDTEND:20261116T130000Z\r\nSUMMARY:Calculus\r\n  lecture\r\nEND:VEVENT\r\n" +
        "BEGIN:VEVENT\r\nDTSTART;TZID=Europe/Istanbul:20261102T100000\r\nDTEND;TZID=Europe/Istanbul:20261102T103000\r\nRRULE:FREQ=WEEKLY;BYDAY=MO,WE\r\nEXDATE;TZID=Europe/Istanbul:20261118T100000\r\nSUMMARY:Daily\\, sync\r\nEND:VEVENT\r\n" +
        "BEGIN:VEVENT\r\nDTSTART;VALUE=DATE:20261116\r\nSUMMARY:Tatil\r\nEND:VEVENT\r\n" +
        "BEGIN:VEVENT\r\nDTSTART:20261116T150000Z\r\nDURATION:PT45M\r\nSTATUS:CANCELLED\r\nSUMMARY:İptal\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

    [Fact]
    public void ParsesUnfoldedUtcEvent()
    {
        var from = new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.FromHours(3));
        var ev = Ics.Parse(Sample, from, from.AddDays(1), Ist);
        var c = Assert.Single(ev, e => e.Title == "Calculus lecture");
        Assert.Equal(new DateTimeOffset(2026, 11, 16, 14, 30, 0, TimeSpan.FromHours(3)), c.Start);
        Assert.Equal(TimeSpan.FromMinutes(90), c.End - c.Start);
        Assert.DoesNotContain(ev, e => e.Title == "İptal");
        Assert.Contains(ev, e => e.Title == "Tatil" && e.AllDay);
    }

    [Fact]
    public void ExpandsWeeklyByDayWithExdate()
    {
        var from = new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.FromHours(3));
        var ev = Ics.Parse(Sample, from, from.AddDays(7), Ist).Where(e => e.Title == "Daily, sync").ToList();
        // 16 Kasım Pzt ve 23 Kasım Pzt; 18 Kasım Çar EXDATE ile çıkarıldı (23 Kasım 00:00 sınırının içinde değil).
        Assert.Equal(new[] { 16 }, ev.Select(e => e.Start.Day).ToArray());
        Assert.Equal(10, ev[0].Start.Hour);
        var ev2 = Ics.Parse(Sample, from, from.AddDays(10), Ist).Where(e => e.Title == "Daily, sync").Select(e => e.Start.Day).ToArray();
        Assert.Equal(new[] { 16, 23, 25 }, ev2);
    }

    [Fact]
    public void NextSkipsAllDayAndPast()
    {
        var from = new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.FromHours(3));
        var ev = Ics.Parse(Sample, from, from.AddDays(2), Ist);
        var now = new DateTimeOffset(2026, 11, 16, 13, 3, 0, TimeSpan.FromHours(3));
        Assert.Equal("Calculus lecture", Ics.Next(ev, now)!.Title);
        Assert.Equal(TimeSpan.FromMinutes(87), Ics.Next(ev, now)!.Start - now); // "1s 27dk sonra"
    }

    [Fact]
    public void UntilAndCount()
    {
        var s = "BEGIN:VEVENT\nDTSTART:20261101T090000Z\nDTEND:20261101T093000Z\nRRULE:FREQ=DAILY;COUNT=3\nSUMMARY:A\nEND:VEVENT\n" +
                "BEGIN:VEVENT\nDTSTART;VALUE=DATE:20261101\nRRULE:FREQ=DAILY;UNTIL=20261102\nSUMMARY:B\nEND:VEVENT\n";
        var from = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var ev = Ics.Parse(s, from, from.AddDays(60), Ist);
        Assert.Equal(3, ev.Count(e => e.Title == "A"));
        Assert.Equal(2, ev.Count(e => e.Title == "B"));
    }
}

public class AiUsageTests
{
    static string Line(string ts, string id, long input, long output, string model = "claude-opus-4-5-20251101") =>
        $"{{\"type\":\"assistant\",\"requestId\":\"r{id}\",\"timestamp\":\"{ts}\",\"message\":{{\"id\":\"{id}\",\"model\":\"{model}\",\"usage\":{{\"input_tokens\":{input},\"output_tokens\":{output},\"cache_read_input_tokens\":0}}}}}}";

    [Fact]
    public void SessionBlockAndWeekly()
    {
        var lines = new[]
        {
            Line("2026-11-15T08:10:00Z", "a", 100, 50),                 // eski blok
            Line("2026-11-16T07:20:00Z", "b", 1000, 500),               // yeni blok 07:00Z'de başlar
            Line("2026-11-16T07:20:00Z", "b", 1000, 500),               // kopya: sayılmaz
            Line("2026-11-16T09:00:00Z", "c", 2000, 500, "claude-sonnet-4-5-20250929"),
            "bozuk satır {",
        };
        var entries = ClaudeUsage.ParseLines(lines).ToList();
        Assert.Equal(4, entries.Count);
        var now = new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);
        var snap = ClaudeUsage.Compute(entries, now, 10_000, 100_000, DayOfWeek.Monday, 0);
        Assert.Equal(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero), snap.SessionResetsAt);
        Assert.Equal(0.4, snap.SessionFraction, 3); // 4000/10000
        Assert.Equal("Sonnet 4.5", snap.TopModel);
        // Haftalık: Pazartesi 00:00 yerel saatten itibaren
        var ws = ClaudeUsage.WeekStart(now, DayOfWeek.Monday, 0);
        Assert.Equal(DayOfWeek.Monday, ws.ToLocalTime().DayOfWeek);
        Assert.True(ws <= now && now - ws < TimeSpan.FromDays(7));
    }

    [Fact]
    public void NoActiveSession()
    {
        var e = ClaudeUsage.ParseLines(new[] { Line("2026-11-16T01:00:00Z", "x", 10, 10) });
        var snap = ClaudeUsage.Compute(e, new DateTimeOffset(2026, 11, 16, 9, 0, 0, TimeSpan.Zero), 100, 1000, DayOfWeek.Monday, 0);
        Assert.Null(snap.SessionResetsAt);
        Assert.Equal(0, snap.SessionFraction);
    }

    [Fact]
    public void PrettyModel() => Assert.Equal("Opus 4.5", ClaudeUsage.PrettyModel("claude-opus-4-5-20251101"));

    [Fact]
    public void CodexRateLimits()
    {
        var l = "{\"timestamp\":\"2026-11-16T10:00:00Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"rate_limits\":{\"primary\":{\"used_percent\":41.0,\"window_minutes\":300,\"resets_in_seconds\":9600},\"secondary\":{\"used_percent\":64.0,\"window_minutes\":10080,\"resets_at\":1795000000}}}}";
        var s = CodexUsage.FromLines(new[] { "{}", l })!;
        Assert.Equal(0.41, s.SessionFraction, 3);
        Assert.Equal(0.64, s.WeeklyFraction, 3);
        Assert.Equal(new DateTimeOffset(2026, 11, 16, 12, 40, 0, TimeSpan.Zero), s.SessionResetsAt);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1795000000), s.WeeklyResetsAt);
    }
}

public class AlertTests
{
    static readonly DateTimeOffset Noon = new(2026, 11, 16, 12, 0, 0, TimeSpan.FromHours(3));

    [Fact]
    public void MeetingAlertPausesMediaOnce()
    {
        var s = new Settings { QuietHoursEnabled = false };
        var eng = new AlertEngine();
        var ev = new CalEvent(Noon.AddMinutes(4), Noon.AddMinutes(34), "Toplantı", false);
        var a = eng.Evaluate(s, new AlertInput(Noon, ev, true, null, null, false));
        var al = Assert.Single(a);
        Assert.Equal(AlertAction.PauseMedia, al.Action);
        Assert.Empty(eng.Evaluate(s, new AlertInput(Noon.AddMinutes(1), ev, true, null, null, false)));
    }

    [Fact]
    public void NoAlertTooEarly()
    {
        var s = new Settings { QuietHoursEnabled = false };
        var ev = new CalEvent(Noon.AddMinutes(30), Noon.AddMinutes(60), "X", false);
        Assert.Empty(new AlertEngine().Evaluate(s, new AlertInput(Noon, ev, false, null, null, false)));
    }

    [Fact]
    public void AiLimitAndBattery()
    {
        var s = new Settings { QuietHoursEnabled = false, AiAlertPercent = 80, BatteryAlertPercent = 15 };
        var ai = new AiUsageSnapshot("Claude Code", 0.9, Noon.AddHours(1), "", 0.5, Noon.AddDays(3), "", null);
        var a = new AlertEngine().Evaluate(s, new AlertInput(Noon, null, false, ai, 12, false));
        Assert.Equal(2, a.Count);
        Assert.Contains(a, x => x.Title.Contains("oturum"));
        Assert.Contains(a, x => x.Title == "Pil azaldı");
        Assert.Empty(new AlertEngine().Evaluate(s, new AlertInput(Noon, null, false, null, 12, true)));
    }

    [Fact]
    public void QuietHoursHoldThenRelease()
    {
        var s = new Settings { QuietStartHour = 23, QuietEndHour = 8, AiAlertPercent = 80 };
        var night = new DateTimeOffset(2026, 11, 16, 2, 0, 0, TimeSpan.FromHours(3)).ToLocalTime();
        Assert.True(AlertEngine.InQuietHours(s, night));
        Assert.False(AlertEngine.InQuietHours(s, Noon));
        var eng = new AlertEngine();
        var ai = new AiUsageSnapshot("Codex", 0.95, night.AddHours(2), "", 0, null, "", null);
        Assert.Empty(eng.Evaluate(s, new AlertInput(night, null, false, ai, null, false)));
        var morning = eng.Evaluate(s, new AlertInput(Noon, null, false, ai, null, false));
        Assert.Single(morning);
    }
}

public class ScreenTimeTests
{
    [Fact]
    public void SplitsAcrossHoursAndDays()
    {
        var st = new ScreenTimeStore();
        st.Add("code", new DateTime(2026, 11, 16, 10, 0, 30), 60);
        var d = st.Day(new DateTime(2026, 11, 16));
        Assert.Equal(60, d.TotalSeconds);
        Assert.Equal(30, d.Hours[9]); Assert.Equal(30, d.Hours[10]);
        st.Add("chrome", new DateTime(2026, 11, 17, 0, 0, 10), 20);
        Assert.Equal(10, st.Day(new DateTime(2026, 11, 16)).Apps["chrome"]);
        Assert.Equal(10, st.Day(new DateTime(2026, 11, 17)).Apps["chrome"]);
        Assert.Equal("code", st.Top(new DateTime(2026, 11, 16), 1).First().App);
        st.Prune(new DateTime(2027, 1, 1));
        Assert.Empty(st.Days);
    }
}

public class WeatherTests
{
    [Fact]
    public void ParsesOpenMeteo()
    {
        var json = "{\"current\":{\"temperature_2m\":24.3,\"weather_code\":0,\"is_day\":1},\"daily\":{\"temperature_2m_max\":[26.1],\"temperature_2m_min\":[15.0]}}";
        var w = Weather.Parse(json);
        Assert.Equal(24.3, w.TempC); Assert.True(w.IsDay);
        Assert.Equal("Açık", Weather.Describe(w.Code, w.IsDay).Text);
        Assert.Contains("latitude=41.01", Weather.Url(41.01, 28.97));
    }

    [Fact]
    public void SettingsRoundTrip()
    {
        var p = Path.Combine(Path.GetTempPath(), "kd-test-" + Guid.NewGuid() + ".json");
        var s = new Settings { Style = DockStyle.Gece, AutoHideWindowsTaskbar = false };
        s.Save(p);
        var l = Settings.Load(p);
        Assert.Equal(DockStyle.Gece, l.Style);
        Assert.False(new Settings().AutoHideWindowsTaskbar); // varsayılan KAPALI
        File.Delete(p);
        Assert.Equal(DockStyle.Cam, Settings.Load(p).Style);
    }
}

public class SettingsListTests
{
    [Fact]
    public void WidgetListIsReplacedNotAppended()
    {
        var s = new Settings { Widgets = new() { "clock", "apps" } };
        var copy = Settings.Deserialize<Settings>(Settings.Serialize(s))!;
        Assert.Equal(new[] { "clock", "apps" }, copy.Widgets);
    }
}
