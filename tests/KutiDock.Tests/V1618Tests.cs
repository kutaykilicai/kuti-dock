using KutiDock.Core;
using Xunit;

/// <summary>1.6.18: menü çubuğunun tam ekran kararı (Marvel Rivals'ta çubuğun oyunun üstünde kalması).</summary>
public class MenuBarFullscreenTests
{
    static readonly Box Main = new(0, 0, 2560, 1440);        // Kasa: birincil
    static readonly Box Left = new(-1920, 272, 0, 1352);     // Kasa: sol monitör

    static bool Covers(Box window, Box barMonitor, bool maximized = false, string cls = "UnrealWindow", bool caption = false) =>
        FullscreenRule.HidesDock(window, barMonitor, barMonitor, maximized, cls, caption);

    [Fact]
    public void MissedNotification_GeometryStillHides()
    {
        // Explorer'ın bildirimi oyun öne geçmeden gelip kaçırıldı (explorerFullscreen=false sayılır): çerçevesiz tam ekran yine gizler.
        Assert.True(MenuBarFullscreen.Hides(false, singleMonitor: false, Covers(Main, Main), foregroundIsDesktop: false));
        Assert.True(MenuBarFullscreen.Hides(true, singleMonitor: false, Covers(Main, Main), false));
    }

    [Fact]
    public void PerDisplay_OnlyTheGamesMonitorHides()
    {
        // Oyun birincil monitörde: sol monitörün çubuğu görünür kalır, Explorer bildirimi gelse bile.
        Assert.True(MenuBarFullscreen.Hides(true, false, Covers(Main, Main), false));
        Assert.False(MenuBarFullscreen.Hides(true, false, Covers(Main, Left), false));
        // Oyun sol monitörde: yalnızca sol çubuk gizlenir.
        Assert.True(MenuBarFullscreen.Hides(true, false, Covers(Left, Left), false));
        Assert.False(MenuBarFullscreen.Hides(true, false, Covers(Left, Main), false));
    }

    [Fact]
    public void StaleNotification_DoesNotHideOnOtherMonitor()
    {
        // Oyun sol monitörde tam ekranken Kutay birincil monitörde normal pencereye geçti: birincil çubuk görünür.
        var window = new Box(100, 100, 1200, 900);
        Assert.False(MenuBarFullscreen.Hides(true, false, Covers(window, Main, cls: "Notepad", caption: true), false));
    }

    [Fact]
    public void SingleMonitor_TrustsExplorer_ButNotForDesktop()
    {
        Assert.True(MenuBarFullscreen.Hides(true, singleMonitor: true, false, false));   // oyun henüz önde değil
        Assert.False(MenuBarFullscreen.Hides(false, singleMonitor: true, false, false));
        Assert.False(MenuBarFullscreen.Hides(true, singleMonitor: true, false, foregroundIsDesktop: true));
        Assert.False(MenuBarFullscreen.Hides(true, singleMonitor: true, true, foregroundIsDesktop: true));
    }

    [Fact]
    public void MaximizedWindowWithTitleBar_IsNotFullscreen()
    {
        var maximized = new Box(-8, -8, 2568, 1448);
        Assert.False(MenuBarFullscreen.Hides(false, false, Covers(maximized, Main, maximized: true, cls: "Chrome_WidgetWin_1", caption: true), false));
        Assert.True(MenuBarFullscreen.Hides(false, false, Covers(maximized, Main, maximized: true, cls: "Chrome_WidgetWin_1", caption: false), false));
    }

    [Fact]
    public void Burst_IsShortAndLight()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), MenuBarFullscreen.BurstLength);
        Assert.Equal(TimeSpan.FromMilliseconds(500), MenuBarFullscreen.BurstInterval);
    }
}
