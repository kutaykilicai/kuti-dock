using KutiDock.Core;
using Xunit;

public class WallpaperGuardPolicyTests
{
    static readonly DateTime T = new(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void LayoutCount_ReadsArray()
    {
        Assert.Equal(0, WallpaperGuardPolicy.LayoutCount("[]"));
        Assert.Equal(2, WallpaperGuardPolicy.LayoutCount("[{\"a\":1},{\"b\":2}]"));
        Assert.Null(WallpaperGuardPolicy.LayoutCount("{bozuk"));
        Assert.Null(WallpaperGuardPolicy.LayoutCount(null));
    }

    [Fact]
    public void Missing_Cases()
    {
        Assert.True(WallpaperGuardPolicy.Missing(0, 0));   // Kasa 14:01: Lively düzeni boşalttı
        Assert.True(WallpaperGuardPolicy.Missing(2, 0));
        Assert.True(WallpaperGuardPolicy.Missing(2, 1));
        Assert.False(WallpaperGuardPolicy.Missing(2, 2));
        Assert.True(WallpaperGuardPolicy.Missing(null, 0));
        Assert.False(WallpaperGuardPolicy.Missing(null, 2));
    }

    [Fact]
    public void NeverDuringFullscreen_OrWithoutLively_OrTooOften()
    {
        Assert.True(WallpaperGuardPolicy.ShouldRestore(true, 0, 0, false, true, null, T));
        Assert.False(WallpaperGuardPolicy.ShouldRestore(true, 0, 0, true, true, null, T));   // tam ekran oyun
        Assert.False(WallpaperGuardPolicy.ShouldRestore(false, 0, 0, false, true, null, T)); // Lively kapalı
        Assert.False(WallpaperGuardPolicy.ShouldRestore(true, 0, 0, false, false, null, T)); // betik yok
        Assert.False(WallpaperGuardPolicy.ShouldRestore(true, 2, 2, false, true, null, T));  // her şey yolunda
        Assert.False(WallpaperGuardPolicy.ShouldRestore(true, 0, 0, false, true, T.AddSeconds(-60), T));
        Assert.True(WallpaperGuardPolicy.ShouldRestore(true, 0, 0, false, true, T.AddMinutes(-3), T));
    }

    [Fact]
    public void Delays()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), WallpaperGuardPolicy.AfterDisplayChange);
        Assert.True(WallpaperGuardPolicy.AfterDockStart > TimeSpan.FromSeconds(30)); // Lively'nin 30 sn penceresinden sonra
    }
}
