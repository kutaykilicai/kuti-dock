using KutiDock.Core;
using Xunit;

public class TaskKillTests
{
    const string Apk = @"C:\Users\Kutay\AppData\Local\Programs\ApkStart\ApkStart.exe";
    const string Chrome = @"C:\Program Files\Google\Chrome\Application\chrome.exe";

    [Theory]
    [InlineData("explorer", @"C:\Windows\explorer.exe")]
    [InlineData("KutiDock", @"C:\Users\Kutay\Desktop\Ai projeler\KutayDock\KutiDock.exe")]
    [InlineData("csrss.exe", null)]
    [InlineData("winlogon", @"C:\Windows\System32\winlogon.exe")]
    [InlineData("ApplicationFrameHost", @"C:\Windows\System32\ApplicationFrameHost.exe")]
    [InlineData("SystemSettings", @"C:\Windows\ImmersiveControlPanel\SystemSettings.exe")]
    [InlineData("Marvel-Win64-Shipping", @"D:\SteamLibrary\steamapps\common\MarvelRivals\Marvel\Binaries\Win64\Marvel-Win64-Shipping.exe")]
    [InlineData("EasyAntiCheat", @"C:\Program Files (x86)\EasyAntiCheat\EasyAntiCheat.exe")]
    [InlineData("steam", @"C:\Program Files (x86)\Steam\steam.exe")]
    [InlineData("SomeGame", @"E:\SteamLibrary\steamapps\common\SomeGame\game.exe")]
    public void Protected(string name, string? path) => Assert.True(TaskKill.IsProtected(name, path));

    [Theory]
    [InlineData("ApkStart", Apk)]
    [InlineData("chrome", Chrome)]
    [InlineData("notepad++", @"C:\Program Files\Notepad++\notepad++.exe")]
    [InlineData("Grok Bot", @"C:\Users\Kutay\AppData\Local\Programs\Grok Bot\Grok Bot.exe")]
    public void NotProtected(string name, string path) => Assert.False(TaskKill.IsProtected(name, path));

    [Fact]
    public void Kind_OnlyWhenRunning()
    {
        var owners = new[] { new ProcInfo(100, "ApkStart", Apk) };
        Assert.Equal(KillKind.KillProcesses, TaskKill.KindFor(Apk.ToLowerInvariant(), 1, owners));
        Assert.Equal(KillKind.None, TaskKill.KindFor(Apk.ToLowerInvariant(), 0, owners));
        Assert.Equal(KillKind.None, TaskKill.KindFor(Apk.ToLowerInvariant(), 1, Array.Empty<ProcInfo>()));
    }

    [Fact]
    public void Kind_ProtectedAppsHidden()
    {
        Assert.Equal(KillKind.None, TaskKill.KindFor(@"c:\windows\explorer.exe", 2, new[] { new ProcInfo(8, "explorer", @"C:\Windows\explorer.exe") }));
        Assert.Equal(KillKind.None, TaskKill.KindFor("steam", 1, new[] { new ProcInfo(9, "steamwebhelper", @"C:\Program Files (x86)\Steam\bin\cef\steamwebhelper.exe") }));
    }

    [Fact]
    public void Kind_PwaClosesWindowsOnly()
    {
        var owners = new[] { new ProcInfo(50, "chrome", Chrome) };
        Assert.Equal(KillKind.ClosePwaWindows, TaskKill.KindFor("crx:hnpfjngllnobngcgfapefoaidbinmjnm", 1, owners));
    }

    [Fact]
    public void Targets_AllProcessesOfSameExe_NotOthers()
    {
        var all = new[]
        {
            new ProcInfo(100, "ApkStart", Apk), new ProcInfo(101, "ApkStart", Apk), new ProcInfo(200, "chrome", Chrome),
            new ProcInfo(300, "explorer", @"C:\Windows\explorer.exe"), new ProcInfo(400, "KutiDock", @"C:\x\KutiDock.exe"),
        };
        var t = TaskKill.Targets(Apk.ToLowerInvariant(), new[] { all[0] }, all, selfPid: 400);
        Assert.Equal(new[] { 100, 101 }, t);
    }

    [Fact]
    public void Targets_NeverSelfOrProtected()
    {
        var self = new ProcInfo(400, "Foo", @"C:\x\foo.exe");
        var expl = new ProcInfo(300, "explorer", @"C:\Windows\explorer.exe");
        Assert.Empty(TaskKill.Targets(@"c:\x\foo.exe", new[] { self, expl }, new[] { self, expl }, selfPid: 400));
    }

    [Fact]
    public void Targets_PackagedAppUsesWindowOwnersOnly()
    {
        var calc = new ProcInfo(70, "CalculatorApp", @"C:\Program Files\WindowsApps\Microsoft.WindowsCalculator_11\CalculatorApp.exe");
        var other = new ProcInfo(71, "CalculatorApp", calc.Path);
        Assert.Equal(new[] { 70 }, TaskKill.Targets("aumid:microsoft.windowscalculator_8wekyb3d8bbwe!app", new[] { calc }, new[] { calc, other }, 1));
    }

    [Fact]
    public void Pwa_ProcessKilledOnlyIfItHasNoOtherWindows()
    {
        Assert.True(TaskKill.CanKillPwaProcess(0));
        Assert.False(TaskKill.CanKillPwaProcess(3));
    }

    [Fact]
    public void TaskkillArgs_ForceTree()
    {
        Assert.Equal("/F /T /PID 100 /PID 101", TaskKill.TaskkillArgs(new[] { 100, 101 }));
        Assert.Equal("Görevi sonlandır", TaskKill.MenuText);
    }
}
