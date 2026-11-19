using System.Runtime.InteropServices;
using KutiDock.Core;
using Xunit;

/// <summary>1.6.19: çizim motoru çökünce kendini yeniden başlatma sınırları ve HResult tanıma.</summary>
public class RenderRestartPolicyTests
{
    static readonly DateTime T = new(2026, 11, 19, 13, 33, 0, DateTimeKind.Utc);

    [Fact]
    public void Detects_UceAndDxgiHResults()
    {
        Assert.True(RenderRestartPolicy.IsRenderFailure(unchecked((int)0x88980406)));
        Assert.True(RenderRestartPolicy.IsRenderFailure(unchecked((int)0x887A0005)));
        Assert.True(RenderRestartPolicy.IsRenderFailure(unchecked((int)0x887A0007)));
        Assert.False(RenderRestartPolicy.IsRenderFailure(0));
        Assert.False(RenderRestartPolicy.IsRenderFailure(unchecked((int)0x80004005)));
        Assert.True(RenderRestartPolicy.IsRenderFailure(new COMException("uce", unchecked((int)0x88980406))));
        Assert.True(RenderRestartPolicy.IsRenderFailure(new Exception("dis", new COMException("dxgi", unchecked((int)0x887A0005)))));
        Assert.False(RenderRestartPolicy.IsRenderFailure(new InvalidOperationException("x")));
        Assert.False(RenderRestartPolicy.IsRenderFailure((Exception?)null));
    }

    [Fact]
    public void Allows_FirstRestart_ThenRespectsGapAndHourlyCap()
    {
        Assert.True(RenderRestartPolicy.ShouldRestart(Array.Empty<DateTime>(), T));

        var one = RenderRestartPolicy.Record(Array.Empty<DateTime>(), T);
        Assert.False(RenderRestartPolicy.ShouldRestart(one, T.AddMinutes(1)));   // 2 dk dolmadan yok
        Assert.True(RenderRestartPolicy.ShouldRestart(one, T.AddMinutes(2)));

        var three = new[] { T, T.AddMinutes(2), T.AddMinutes(4) };
        Assert.False(RenderRestartPolicy.ShouldRestart(three, T.AddMinutes(6)));  // saatte 3
        Assert.True(RenderRestartPolicy.ShouldRestart(three, T.AddHours(1).AddMinutes(1))); // en eski düştü
    }

    [Fact]
    public void RoundTrip_IgnoresBrokenJson()
    {
        var t = new[] { T, T.AddMinutes(3) };
        var back = RenderRestartPolicy.Parse(RenderRestartPolicy.Serialize(t));
        Assert.Equal(2, back.Count);
        Assert.Equal(T, back[0]);
        Assert.Empty(RenderRestartPolicy.Parse(null));
        Assert.Empty(RenderRestartPolicy.Parse("{bozuk"));
        Assert.Empty(RenderRestartPolicy.Parse("[]"));
    }
}

/// <summary>
/// 1.6.19: ilk açılış Genie'si süreç başına bir kez (Hızlı İndir tepsiye inip yeniden gösterilince ikinci kez
/// oynamasın) ve aynı uygulama birkaç saniye içinde tekrar tetiklenmesin.
/// </summary>
public class LaunchOnceTests
{
    static readonly DateTime T = new(2026, 11, 19, 13, 41, 25, DateTimeKind.Utc);
    const string Hizli = @"c:\users\kutay\appdata\local\programs\hizliindir\hizliindir.exe";

    [Fact]
    public void SameProcess_PlaysOnlyOnce()
    {
        var played = new[] { new LaunchGeniePolicy.LaunchPlay(Hizli, 16548, T) };
        Assert.True(LaunchGeniePolicy.AlreadyPlayed(played, Hizli, 16548, T.AddSeconds(1)));
        Assert.True(LaunchGeniePolicy.AlreadyPlayed(played, Hizli, 16548, T.AddMinutes(30))); // tepsiye inip tekrar açılma
        Assert.False(LaunchGeniePolicy.AlreadyPlayed(played, Hizli, 20000, T.AddMinutes(30))); // yeni süreç
    }

    [Fact]
    public void SameApp_ShortWindow_BlocksSecondWindow()
    {
        var played = new[] { new LaunchGeniePolicy.LaunchPlay(Hizli, 1, T) };
        Assert.True(LaunchGeniePolicy.AlreadyPlayed(played, Hizli, 2, T.AddSeconds(3)));
        Assert.False(LaunchGeniePolicy.AlreadyPlayed(played, Hizli, 2, T.AddSeconds(5)));
        Assert.False(LaunchGeniePolicy.AlreadyPlayed(played, @"c:\c\chrome.exe", 3, T.AddSeconds(1)));
    }
}

/// <summary>
/// 1.6.19: ana ekranda D3D/tam ekran oyun varken SHQueryUserNotificationState sistem genelidir; dock yine de
/// fareyi tam ekran olmayan ikinci monitöre takip etmeli, oyun monitöründe gizlenmeli.
/// </summary>
public class FullscreenFollowTests
{
    const long Main = 1, Side = 2;

    [Fact]
    public void D3dGameOnPrimary_AllowsFollowToSecondMonitor()
    {
        bool fsOnSide = FullscreenRule.Decide(geometry: false, FullscreenRule.QunsD3DFullScreen, foregroundOnDockMonitor: false);
        bool fsOnMain = FullscreenRule.Decide(geometry: false, FullscreenRule.QunsD3DFullScreen, foregroundOnDockMonitor: true);
        Assert.False(fsOnSide);
        Assert.True(fsOnMain);
        Assert.Equal(Side, DockMonitors.Choose(Main, Side, Main, follow: true, primaryOnly: false, cursorAtOuterEdge: true, fullscreenOnCursorMonitor: fsOnSide));
        Assert.Equal(Main, DockMonitors.Choose(Main, Main, Main, true, false, true, fullscreenOnCursorMonitor: fsOnMain));
    }
}
