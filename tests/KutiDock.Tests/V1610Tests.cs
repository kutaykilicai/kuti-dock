using KutiDock.Core;
using Xunit;

/// <summary>1.6.10: dock otomatik gizliyken ilk açılış Genie'sinin hedefi ve kısa gösterim.</summary>
public class LaunchTargetTests
{
    [Fact]
    public void HiddenDock_TargetIsWhereIconWillBe_SlideRemoved()
    {
        // Kasa ana ekranı (96 dpi): dock penceresi 247,1370; simge pencere içinde 120,8 (42x46 DIP).
        // Saklıyken gövde 70 DIP aşağı kaymış durumda: hedef yine açık dock'taki yer.
        var open = LaunchTarget.ToScreen(247, 1370, 120, 8, 42, 46, 1.0);
        var hidden = LaunchTarget.ToScreen(247, 1370, 120, 8 + 70, 42, 46, 1.0, slideY: 70);
        Assert.Equal(new Box(367, 1378, 409, 1424), open);
        Assert.Equal(open, hidden);
    }

    [Fact]
    public void Target_UsesMonitorDpi()
    {
        // %150 ölçekli ekran: DIP → piksel 1,5 kat
        var r = LaunchTarget.ToScreen(-1912, 1282, 100, 10, 40, 40, 1.5);
        Assert.Equal(new Box(-1762, 1297, -1702, 1357), r);
    }

    [Fact]
    public void NewApp_TargetIsNextSlot()
    {
        var last = (X: 500.0, Y: 1380.0, W: 50.0, H: 48.0);
        Assert.Equal((550.0, 1380.0, 50.0, 48.0), LaunchTarget.NextSlot(last, vertical: false, 0, 0, 42));
        Assert.Equal((500.0, 1428.0, 50.0, 48.0), LaunchTarget.NextSlot(last, vertical: true, 0, 0, 42));
        // hiç simge yok: şeridin ortası
        Assert.Equal((979.0, 1379.0, 42.0, 42.0), LaunchTarget.NextSlot(null, false, 1000, 1400, 42));
    }

    [Theory]
    [InlineData(true, true, true, false, false, false, false, true)]   // saklı dock, normal masaüstü: göster
    [InlineData(true, true, false, false, false, false, false, false)] // zaten açık
    [InlineData(true, false, true, false, false, false, false, false)] // otomatik gizleme kapalı
    [InlineData(true, true, true, true, false, false, false, false)]   // kullanıcı gizledi
    [InlineData(true, true, true, false, true, false, false, false)]   // tam ekran için saklı
    [InlineData(true, true, true, false, false, true, false, false)]   // o ekranda tam ekran uygulama
    [InlineData(true, true, true, false, false, false, true, false)]   // oyun önde
    [InlineData(false, true, true, false, false, false, false, false)] // animasyonlar kapalı
    public void Peek_OnlyWhenSafe(bool anim, bool autoOn, bool autoHidden, bool user, bool fsHidden, bool fs, bool game, bool expected) =>
        Assert.Equal(expected, LaunchTarget.ShouldPeek(anim, autoOn, autoHidden, user, fsHidden, fs, game));

    [Fact]
    public void AutoHide_PeekHoldsThenHidesWithUsualDelay()
    {
        var a = new AutoHide(); a.Reset();
        var t0 = new DateTime(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc);
        a.Peek(t0.AddMilliseconds(LaunchTarget.PeekMs));
        Assert.True(a.Shown);
        var delay = TimeSpan.FromMilliseconds(600);
        Assert.False(a.Update(false, false, false, t0.AddMilliseconds(1000), delay)); // tutma süresi içinde
        Assert.False(a.Update(false, false, false, t0.AddMilliseconds(1600), delay)); // süre bitti, gecikme başladı
        Assert.True(a.Update(false, false, false, t0.AddMilliseconds(2300), delay));  // 0,6 sn sonra saklandı
        Assert.False(a.Shown);
    }

    [Fact]
    public void AutoHide_PeekThenMouseOnDock_StaysOpen()
    {
        var a = new AutoHide(); a.Reset();
        var t0 = new DateTime(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc);
        a.Peek(t0.AddMilliseconds(LaunchTarget.PeekMs));
        Assert.False(a.Update(false, true, false, t0.AddSeconds(5), TimeSpan.FromMilliseconds(600)));
        Assert.True(a.Shown);
        a.Reset(); // tam ekran: hemen saklanır, tutma süresi silinir
        Assert.False(a.Shown);
    }
}
