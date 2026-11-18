using KutiDock.Core;
using Xunit;

public class GeminiUsageTests
{
    static readonly DateTime Now = new(2026, 11, 18, 13, 55, 0); // Kasa yerel saati (UTC+3)
    const string Page = "Usage limits\nCurrent usage\n0% used\nResets at 2:30 PM\nWeekly limit\n0% used\nResets Nov 18 at 5:30 PM\nUpgrade";

    [Fact]
    public void Parses_ScreenshotCards()
    {
        var p = GeminiUsage.Parse(Page, Now);
        Assert.NotNull(p);
        var (c, w) = p.Value;
        Assert.Equal(0, c.Percent); Assert.Equal(new DateTime(2026, 11, 18, 14, 30, 0), c.ResetsAt);
        Assert.Equal(0, w.Percent); Assert.Equal(new DateTime(2026, 11, 18, 17, 30, 0), w.ResetsAt);
    }

    [Fact]
    public void Parses_NonZero_AndTomorrow()
    {
        var p = GeminiUsage.Parse("Current usage\n37% used\nResets at 1:10 AM\nWeekly limit\n12.5% used\nResets Nov 24 at 5:30 PM", Now)!.Value;
        Assert.Equal(37, p.Current.Percent); Assert.Equal(new DateTime(2026, 11, 19, 1, 10, 0), p.Current.ResetsAt);
        Assert.Equal(12.5, p.Weekly.Percent); Assert.Equal(new DateTime(2026, 11, 24, 17, 30, 0), p.Weekly.ResetsAt);
    }

    [Fact]
    public void Parses_Turkish()
    {
        var p = GeminiUsage.Parse("Güncel kullanım\n%20 kullanıldı\n14:30'da sıfırlanır\nHaftalık sınır\n%5 kullanıldı\n18 Kas 17:30'da sıfırlanır", Now)!.Value;
        Assert.Equal(20, p.Current.Percent); Assert.Equal(new DateTime(2026, 11, 18, 14, 30, 0), p.Current.ResetsAt);
        Assert.Equal(5, p.Weekly.Percent); Assert.Equal(new DateTime(2026, 11, 18, 17, 30, 0), p.Weekly.ResetsAt);
    }

    [Fact]
    public void NoCards_NoData()
    {
        Assert.Null(GeminiUsage.Parse("", Now));
        Assert.Null(GeminiUsage.Parse("Sign in to Gemini", Now));
        Assert.Null(GeminiUsage.Parse("Current usage\nWeekly limit\n", Now)); // yüzde yoksa uydurma yok
    }

    [Fact]
    public void Line_LocalTime()
    {
        var l = new GeminiLimit(0, new DateTime(2026, 11, 18, 14, 30, 0), "Resets at 2:30 PM");
        Assert.Equal("Güncel %0 · sıfırlanma 14:30", GeminiUsage.Line("Güncel", l, Now));
        var w = new GeminiLimit(3, new DateTime(2026, 11, 24, 17, 30, 0), null);
        Assert.Equal("Haftalık %3 · sıfırlanma 24 Kas 17:30", GeminiUsage.Line("Haftalık", w, Now));
    }

    [Fact]
    public void Secret_And_Stale_And_Store()
    {
        var s = GeminiUsage.NewSecret();
        Assert.Equal(64, s.Length);
        Assert.True(GeminiUsage.SecretMatches(s, s));
        Assert.False(GeminiUsage.SecretMatches(s, s[..63] + "x"));
        Assert.False(GeminiUsage.SecretMatches(s, null));
        Assert.False(GeminiUsage.SecretMatches(null, "x"));
        var at = new DateTimeOffset(Now, TimeSpan.FromHours(3));
        var d = new GeminiUsageData(new GeminiLimit(1, null, null), new GeminiLimit(2, null, null), at, "sayfa", false);
        Assert.False(GeminiUsage.IsStale(d, at.AddMinutes(10)));
        Assert.True(GeminiUsage.IsStale(d, at.AddMinutes(31)));
        Assert.True(GeminiUsage.IsStale(null, at));
        Assert.Equal(d.Current, GeminiUsageData.FromJson(d.ToJson())!.Current);
        Assert.Equal(47615, GeminiUsage.Port);
    }
}
