using KutiDock.Core;
using Xunit;

/// <summary>
/// 1.6.21: Kasa hata.log, 17:28:09: Marvel Rivals ana ekranda tam ekran (QUNS_BUSY sistem genelinde 2), dock fareyle
/// ikinci ekrana taşınmış, ön planda ikinci ekranda büyütülmüş Chrome (başlıklı). Dock "tamEkranGizli=True" ile
/// gizli kalıyordu: QUNS sinyali öndeki Chrome'a yükleniyordu.
/// </summary>
public class V1621SideMonitorTests
{
    const long Main = 25, Side = 26;
    static readonly Box MainBox = new(0, 0, 2560, 1440), SideBox = new(-1920, 272, 0, 1352);
    static readonly Box ChromeMaximized = new(-1928, 290, 8, 1360);

    [Fact]
    public void GameBusyOnMain_CaptionedWindowOnSide_DoesNotHideSideDock()
    {
        bool geometry = FullscreenRule.HidesDock(ChromeMaximized, SideBox, SideBox, maximized: true, "Chrome_WidgetWin_1", hasCaption: true);
        Assert.False(geometry);
        bool fs = FullscreenRule.Decide(geometry, FullscreenRule.QunsBusy, foregroundOnDockMonitor: true, foregroundHasCaption: true);
        Assert.False(fs);
        Assert.Equal(Side, DockMonitors.Choose(Main, Side, Main, follow: true, primaryOnly: false, cursorAtOuterEdge: true, fullscreenOnCursorMonitor: fs));
    }

    [Fact]
    public void BorderlessOrExclusiveGame_StillHidesItsOwnMonitor()
    {
        // Oyunun kendi ekranında başlıksız pencere: QUNS yine gizler (geometri tutmasa bile)
        Assert.True(FullscreenRule.Decide(false, FullscreenRule.QunsD3DFullScreen, foregroundOnDockMonitor: true, foregroundHasCaption: false));
        Assert.True(FullscreenRule.Decide(false, FullscreenRule.QunsBusy, true, false));
        // Marvel kenarlıksız tam ekran: geometri tek başına yeter
        Assert.True(FullscreenRule.Decide(FullscreenRule.HidesDock(MainBox, MainBox, MainBox, false, "UnrealWindow", false), FullscreenRule.QunsBusy, true, false));
        // Başlıklı pencere de geometriyle tam ekransa gizler (geometri her zaman yeter)
        Assert.True(FullscreenRule.Decide(true, 5, true, foregroundHasCaption: true));
    }
}

/// <summary>
/// 1.6.21: ilk açılış Genie'sinde asıl pencere gizlenmiyordu. DWMWA_CLOAK başka sürecin penceresinde E_ACCESSDENIED
/// (0x80070005) döner (Kasa'da ölçüldü); WPF penceresi eklenen WS_EX_LAYERED'ı geri alır. Gizlenemeyen pencerede Genie oynamaz.
/// </summary>
public class V1621GenieHideTests
{
    const int S_OK = 0, E_ACCESSDENIED = unchecked((int)0x80070005);

    [Fact]
    public void Cloak_CountsOnlyWhenItSucceeds()
    {
        Assert.Equal(LaunchGeniePolicy.HideHow.Cloak, LaunchGeniePolicy.Hide(S_OK, false, false));
        Assert.Equal(LaunchGeniePolicy.HideHow.Alpha, LaunchGeniePolicy.Hide(E_ACCESSDENIED, layeredStuck: true, alphaApplied: true));
    }

    [Fact]
    public void NothingStuck_NoGenie()
    {
        // WPF (Hızlı İndir): stil geri alındı, SetLayeredWindowAttributes ERROR_INVALID_PARAMETER
        Assert.Equal(LaunchGeniePolicy.HideHow.None, LaunchGeniePolicy.Hide(E_ACCESSDENIED, layeredStuck: false, alphaApplied: false));
        Assert.Equal(LaunchGeniePolicy.HideHow.None, LaunchGeniePolicy.Hide(E_ACCESSDENIED, layeredStuck: true, alphaApplied: false));
    }
}

/// <summary>1.6.21: GPU yüzdesi kategori tek okumasından (Timer100Ns: 100 × Δham / Δzaman), yalnızca 3D motorlar.</summary>
public class V1621GpuUsageTests
{
    [Fact]
    public void Percent_SumsOnly3DEnginesPresentInBothReads()
    {
        var a = new Dictionary<string, (long Raw, long Time)>
        {
            ["pid_1_luid_0x1_phys_0_eng_0_engtype_3D"] = (1_000, 10_000_000),
            ["pid_2_luid_0x1_phys_0_eng_0_engtype_3D"] = (0, 10_000_000),
            ["pid_1_luid_0x1_phys_0_eng_1_engtype_Copy"] = (0, 10_000_000),
        };
        var b = new Dictionary<string, (long Raw, long Time)>
        {
            ["pid_1_luid_0x1_phys_0_eng_0_engtype_3D"] = (1_000 + 2_000_000, 30_000_000), // 2 sn'nin %10'u
            ["pid_2_luid_0x1_phys_0_eng_0_engtype_3D"] = (1_000_000, 30_000_000),         // %5
            ["pid_1_luid_0x1_phys_0_eng_1_engtype_Copy"] = (20_000_000, 30_000_000),      // 3D değil
            ["pid_3_luid_0x1_phys_0_eng_0_engtype_3D"] = (5_000_000, 30_000_000),         // ilk okumada yoktu
        };
        Assert.Equal(15, GpuUsage.Percent(a, b), 6);
    }

    [Fact]
    public void Percent_ClampsAndIgnoresCounterResets()
    {
        var a = new Dictionary<string, (long Raw, long Time)> { ["x_engtype_3D"] = (0, 0), ["y_engtype_3D"] = (9_000, 0) };
        var b = new Dictionary<string, (long Raw, long Time)> { ["x_engtype_3D"] = (30_000_000, 20_000_000), ["y_engtype_3D"] = (0, 20_000_000) };
        Assert.Equal(100, GpuUsage.Percent(a, b));
        Assert.Equal(0, GpuUsage.Percent(b, b));
    }
}
