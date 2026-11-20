using System.Text.RegularExpressions;
using Xunit;

/// <summary>
/// 1.6.23: monitör uyanışında birincil ekran kısa süre değişince menü çubukları yeniden kuruluyor, her seferinde tepsi
/// hizmeti kapatılıp yenisi açılıyordu. ManagedShell kapanışta ve açılışta TaskbarCreated yayınlar; Lively de
/// FindWindow("Shell_TrayWnd") sahibini Explorer/dock arasında değişik görüp 30 sn'de iki "Gezgin çökmesi" sayıyordu
/// (Kasa, 2026-11-20 14:43:14-17, Lively log'unda dört pid mismatch). Tepsi artık MenuBars'ın tek nesnesi; çubuklar
/// yeniden kurulurken yalnızca yeni birincil çubuğa taşınır, kapatılmaz.
/// </summary>
public class V1623TrayLifetimeTests
{
    static string Src(string rel)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "KutiDock.Mac"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "src", "KutiDock.Mac", rel));
    }

    [Fact]
    public void TrayPanel_IsCreatedOnlyByMenuBars_AndReused()
    {
        var bars = Src(Path.Combine("UI", "MenuBars.cs"));
        var bar = Src(Path.Combine("UI", "MenuBarWindow.cs"));
        Assert.DoesNotContain("new TrayIconsPanel", bar);
        Assert.Single(Regex.Matches(bars, @"new TrayIconsPanel"));
        Assert.Contains("_tray ??= new TrayIconsPanel", bars); // var olan panel yeniden kullanılır
    }

    [Fact]
    public void ClosingABar_DetachesTray_DoesNotDisposeIt()
    {
        var bar = Src(Path.Combine("UI", "MenuBarWindow.cs"));
        var onClosed = bar[bar.IndexOf("protected override void OnClosed", StringComparison.Ordinal)..];
        onClosed = onClosed[..onClosed.IndexOf("base.OnClosed", StringComparison.Ordinal)];
        Assert.DoesNotContain("_tray?.Dispose()", onClosed);
        Assert.Contains("Children.Remove(_tray)", onClosed);
    }

    [Fact]
    public void TrayIsDisposed_OnlyWhenMenuBarsGoAway()
    {
        var bars = Src(Path.Combine("UI", "MenuBars.cs"));
        var dispose = bars[bars.IndexOf("public void Dispose()", StringComparison.Ordinal)..];
        Assert.Contains("_tray?.Dispose()", dispose);
        var sync = bars[bars.IndexOf("public void Sync()", StringComparison.Ordinal)..bars.IndexOf("public void ApplyTheme()", StringComparison.Ordinal)];
        Assert.DoesNotContain("Dispose", sync);
    }
}

/// <summary>1.6.23: duvar kağıdı bekçisi uykudan dönüşte ve ekran açılınca da kısa gecikmeyle bakar.</summary>
public class V1623WallpaperGuardWakeTests
{
    [Fact]
    public void Guard_ListensForResumeAndDisplayOn()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "KutiDock"))) dir = dir.Parent;
        var guard = File.ReadAllText(Path.Combine(dir!.FullName, "src", "KutiDock", "WallpaperGuard.cs"));
        var app = File.ReadAllText(Path.Combine(dir.FullName, "src", "KutiDock", "App.xaml.cs"));
        var dock = File.ReadAllText(Path.Combine(dir.FullName, "src", "KutiDock.Mac", "UI", "DockWindow.cs"));
        Assert.Contains("PowerModes.Resume) Wake(", guard);
        Assert.Contains("DisplayOn += () => _wallpaperGuard.Wake(", app);
        Assert.Contains("_pauseReasons.Contains(\"display off\")) { Resume(\"display off\"); DisplayOn?.Invoke(); }", dock);
        Assert.True(KutiDock.Core.WallpaperGuardPolicy.AfterDisplayChange <= TimeSpan.FromSeconds(10));
    }
}
