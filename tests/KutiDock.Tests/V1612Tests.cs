using KutiDock.Core;
using Xunit;

public class TvStatusTests
{
    // Kasa'daki gerçek ekranlar (CCD): LG ULTRAGEAR (DP), ASUS VP228 (DVI), LG TV (HDMI, GSM0001)
    static readonly TvPath Lg = new("LG ULTRAGEAR", @"\\?\DISPLAY#GSM778B#7&153e4896&0&UID256#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}", false, true);
    static readonly TvPath Asus = new("ASUS VP228", @"\\?\DISPLAY#ACI22C3#7&153e4896&0&UID260#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}", false, true);
    static TvPath Tv(bool active) => new("LG TV", @"\\?\DISPLAY#GSM0001#7&153e4896&0&UID268#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}", true, active);

    const string KasaAyarlar = """
        {
          "friendlyName": "LG TV",
          "devicePathContains": "GSM0001",
          "edidManufactureId": 27934,
          "edidProductCodeId": 1,
          "outputTechnology": "HDMI",
          "primaryFriendlyName": "LG ULTRAGEAR"
        }
        """;

    [Fact]
    public void IsTvTool_RecognizesShortcutTarget()
    {
        Assert.True(TvStatus.IsTvTool(@"C:\Users\Kutay\Desktop\Ai projeler\TvEkranGecis\TvEkranGecis.exe"));
        Assert.True(TvStatus.IsTvTool("\"C:\\x\\tvekrangecis.EXE\""));
        Assert.False(TvStatus.IsTvTool(@"C:\Program Files\Google\Chrome\Application\chrome.exe"));
        Assert.False(TvStatus.IsTvTool(null));
        Assert.False(TvStatus.IsTvTool(""));
    }

    [Fact]
    public void Hint_ParsedFromTvEkranGecisSettings()
    {
        var h = TvHint.Parse(KasaAyarlar);
        Assert.Equal("LG TV", h.FriendlyName);
        Assert.Equal("GSM0001", h.DevicePathContains);
        Assert.Equal(TvHint.None, TvHint.Parse("{bozuk"));
        Assert.Equal(TvHint.None, TvHint.Parse(null));
    }

    [Fact]
    public void On_WhenTvPathActive()
    {
        var hint = TvHint.Parse(KasaAyarlar);
        Assert.True(TvStatus.IsOn(new[] { Lg, Asus, Tv(true) }, hint));
        // tüm yollar sorgusunda TV'nin etkin olmayan kopyaları da gelir; etkin olan varsa açık
        Assert.True(TvStatus.IsOn(new[] { Lg, Asus, Tv(false), Tv(true), Tv(false) }, hint));
    }

    [Fact]
    public void Off_WhenTvOnlyInInactivePaths()
    {
        var hint = TvHint.Parse(KasaAyarlar);
        Assert.False(TvStatus.IsOn(new[] { Lg, Asus, Tv(false) }, hint));
    }

    [Fact]
    public void WithoutSettings_FallsBackToTvName()
    {
        Assert.True(TvStatus.IsOn(new[] { Lg, Asus, Tv(true) }, TvHint.None));
        Assert.False(TvStatus.IsOn(new[] { Lg, Asus, Tv(false) }, TvHint.None));
    }

    [Fact]
    public void Unknown_WhenNoTvAtAll()
    {
        Assert.Null(TvStatus.IsOn(new[] { Lg, Asus }, TvHint.None));
        Assert.Null(TvStatus.IsOn(Array.Empty<TvPath>(), TvHint.Parse(KasaAyarlar)));
    }

    [Fact]
    public void TwoTvNames_PrefersHdmi()
    {
        var dpTv = new TvPath("Samsung TV", @"\\?\DISPLAY#SAM0001#x", false, true);
        Assert.False(TvStatus.IsOn(new[] { dpTv, Tv(false) }, TvHint.None));
    }

    [Fact]
    public void Texts_AreTurkish()
    {
        Assert.Equal("TV: Açık", TvStatus.Tooltip(true));
        Assert.Equal("TV: Kapalı", TvStatus.Tooltip(false));
        Assert.Equal("kapalı", TvStatus.Badge(false));
        Assert.Null(TvStatus.Badge(true));
        Assert.Null(TvStatus.Badge(null));
    }
}
