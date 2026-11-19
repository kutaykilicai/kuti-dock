using KutiDock.Core;
using Xunit;

/// <summary>
/// 1.6.20: Kasa'nın gerçek düzeni (16:46'da okundu): ana ekran DISPLAY25 0,0-2560x1440, ikinci ekran DISPLAY26 solda
/// -1920,272-0,1352. Zelda (totk, "TotK Recomp") ana ekranda kenarlıksız tam ekran. Dock fareyle ikinci ekrana taşınınca
/// tam ekran denetimi pencerenin eski yerine (ana ekran) bakıyordu ve dock'u yeniden gizliyordu.
/// </summary>
public class V1620FollowFullscreenTests
{
    const long Main = 25, Side = 26;
    static readonly Box MainBox = new(0, 0, 2560, 1440), SideBox = new(-1920, 272, 0, 1352);
    static readonly Box Game = new(0, 0, 2560, 1440);
    static bool OnAny(int x, int y) => MainBox.Contains(x, y) || SideBox.Contains(x, y);

    [Fact]
    public void Probe_UsesDockMonitor_NotStaleWindowMonitor()
    {
        // Taşıma yeni yapıldı: dock mantıksal olarak ikinci ekranda, pencere henüz ana ekranda
        Assert.Equal(Side, DockMonitors.FullscreenProbe(follow: true, dockMonitor: Side, windowMonitor: Main));
        // Takip kapalıysa (Mac şeritleri, sabit dock) pencerenin monitörü
        Assert.Equal(Main, DockMonitors.FullscreenProbe(follow: false, dockMonitor: Side, windowMonitor: Main));
        Assert.Equal(Main, DockMonitors.FullscreenProbe(follow: true, dockMonitor: 0, windowMonitor: Main));
    }

    [Fact]
    public void KasaLayout_GameOnMain_DockRevealsOnSideBottom()
    {
        // İkinci ekranın alt kenarı dış kenar (altında monitör yok), ana ekranın alt kenarı da
        Assert.True(DockMonitors.AtOuterEdge(DockEdge.Alt, -960, 1351, SideBox, OnAny));
        Assert.True(DockMonitors.AtOuterEdge(DockEdge.Alt, -1, 1350, SideBox, OnAny));
        Assert.False(DockMonitors.AtOuterEdge(DockEdge.Alt, -960, 1349, SideBox, OnAny));
        Assert.True(DockMonitors.AtOuterEdge(DockEdge.Alt, 1280, 1439, MainBox, OnAny));

        bool OnMonitor(Box dockMon, long dockId) => FullscreenRule.Decide(
            FullscreenRule.HidesDock(Game, MainBox, dockMon, maximized: false, "SDL_app", hasCaption: false),
            FullscreenRule.QunsD3DFullScreen, foregroundOnDockMonitor: dockId == Main);
        Assert.True(OnMonitor(MainBox, Main));   // oyunun ekranında dock çıkmaz
        Assert.False(OnMonitor(SideBox, Side));  // ikinci ekranda çıkar

        Assert.Equal(Side, DockMonitors.Choose(Main, Side, Main, follow: true, primaryOnly: false, cursorAtOuterEdge: true, fullscreenOnCursorMonitor: OnMonitor(SideBox, Side)));
        // Fare oyunun ekranının altına dönerse dock ikinci ekranda kalır
        Assert.Equal(Side, DockMonitors.Choose(Side, Main, Main, true, false, true, fullscreenOnCursorMonitor: OnMonitor(MainBox, Main)));
    }

    [Fact]
    public void StaleWindowOnGameMonitor_NoLongerHidesDockOnSide()
    {
        // Eski davranış: karar pencerenin monitörüne (ana ekran, oyun) göre verilirdi
        long stale = DockMonitors.FullscreenProbe(follow: false, dockMonitor: Side, windowMonitor: Main);
        long now = DockMonitors.FullscreenProbe(follow: true, dockMonitor: Side, windowMonitor: Main);
        bool Hides(long id) => FullscreenRule.Decide(
            FullscreenRule.HidesDock(Game, MainBox, id == Main ? MainBox : SideBox, false, "SDL_app", false),
            FullscreenRule.QunsD3DFullScreen, id == Main);
        Assert.True(Hides(stale));
        Assert.False(Hides(now));
    }
}
