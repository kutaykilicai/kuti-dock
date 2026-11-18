using KutiDock.Core;
using Xunit;

public class EverythingPolicyTests
{
    static readonly DateTime T0 = new(2026, 11, 18, 10, 6, 1, DateTimeKind.Utc);

    [Fact]
    public void Owner_RoundTrips()
    {
        var o = new OwnedEverything(6836, T0);
        Assert.Equal(o, OwnedEverything.Parse(o.Serialize()));
        Assert.Null(OwnedEverything.Parse(""));
        Assert.Null(OwnedEverything.Parse("abc"));
        Assert.Null(OwnedEverything.Parse("12|x"));
        Assert.Null(OwnedEverything.Parse("-1|5"));
    }

    [Fact]
    public void Exit_OnlyTheProcessDockStarted()
    {
        var o = new OwnedEverything(6836, T0);
        Assert.True(EverythingPolicy.ShouldExit(o, 6836, T0.AddMilliseconds(400), mainWindowVisible: false));
        // Kutay'ın kendi açtığı (kayıt yok) ya da başka PID
        Assert.False(EverythingPolicy.ShouldExit(null, 6836, T0, false));
        Assert.False(EverythingPolicy.ShouldExit(o, 7000, T0, false));
        // PID yeniden kullanılmış (başlangıç zamanı farklı)
        Assert.False(EverythingPolicy.ShouldExit(o, 6836, T0.AddMinutes(30), false));
        // Everything çalışmıyor
        Assert.False(EverythingPolicy.ShouldExit(o, null, null, false));
    }

    [Fact]
    public void Exit_NotWhileWindowOpen()
    {
        var o = new OwnedEverything(6836, T0);
        Assert.False(EverythingPolicy.ShouldExit(o, 6836, T0, mainWindowVisible: true));
    }

    [Fact]
    public void StartWithDock_FollowsSwitch()
    {
        Assert.True(EverythingPolicy.StartWithDock(true));
        Assert.False(EverythingPolicy.StartWithDock(false));
    }

    [Fact]
    public void Retry_WhileDbLoadingAndEmpty()
    {
        Assert.True(EverythingPolicy.ShouldRetry(dbLoaded: false, hitCount: 0, attempt: 0));
        Assert.False(EverythingPolicy.ShouldRetry(dbLoaded: true, hitCount: 0, attempt: 0));
        Assert.False(EverythingPolicy.ShouldRetry(dbLoaded: false, hitCount: 3, attempt: 0));
        Assert.False(EverythingPolicy.ShouldRetry(false, 0, EverythingPolicy.RetryDelaysMs.Length));
    }

    [Fact]
    public void BackgroundStart_NoWindow()
    {
        // "-startup": Everything arka planda başlar, arama penceresi açılmaz (Kasa'da 1.4.1.1032 ile doğrulandı)
        Assert.Equal("-startup", EverythingIpc.StartArgs);
        Assert.Equal("-exit", EverythingIpc.ExitArgs);
        Assert.Equal(401, EverythingPolicy.IpcIsDbLoaded);
    }

    [Fact]
    public void KasaQuery_UserRoots()
    {
        var q = WinSearch.EverythingQuery("kutay", new[] { @"C:\Users\Kutay\Desktop", @"E:\İndirilenler" });
        Assert.Equal("<\"C:\\Users\\Kutay\\Desktop\\\"|\"E:\\İndirilenler\\\"> kutay", q);
    }
}
