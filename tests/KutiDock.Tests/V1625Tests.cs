using KutiDock.Core;
using Xunit;

/// <summary>
/// 1.6.25: bir uygulamanın birden çok penceresi varken (iki Gezgin klasörü) simgeye tıklamak pencere seçiciyi açar;
/// tek pencerede eski davranış (öndeyse küçült, değilse getir) aynen kalır.
/// </summary>
public class V1625PickerTests
{
    [Theory]
    [InlineData(2, true)]   // biri öndeyken eskiden öndeki küçülüyordu
    [InlineData(2, false)]
    [InlineData(5, true)]
    public void ManyWindows_AlwaysOpenPicker(int n, bool front) => Assert.Equal(AppClick.Picker, WindowPicker.Decide(n, front));

    [Fact]
    public void SingleWindow_Unchanged()
    {
        Assert.Equal(AppClick.MinimizeFront, WindowPicker.Decide(1, true));
        Assert.Equal(AppClick.Restore, WindowPicker.Decide(1, false));
        Assert.Equal(AppClick.Launch, WindowPicker.Decide(0, false));
    }

    [Theory]
    [InlineData("Projeler - Dosya Gezgini", "Projeler")]
    [InlineData("Downloads - File Explorer", "Downloads")]
    [InlineData("İndirilenler", "İndirilenler")]
    [InlineData("Belge - Not Defteri", "Belge - Not Defteri")]
    [InlineData(" - Dosya Gezgini", "- Dosya Gezgini")]
    [InlineData(null, "")]
    public void CardTitle_IsFolderName(string? title, string expected) => Assert.Equal(expected, WindowPicker.CardTitle(title));

    [Fact]
    public void Tooltip_HiddenWithManyWindows()
    {
        Assert.Null(WindowPicker.Tooltip(2, "A - Dosya Gezgini", "Dosya Gezgini"));
        Assert.Equal("A - Dosya Gezgini", WindowPicker.Tooltip(1, "A - Dosya Gezgini", "Dosya Gezgini"));
        Assert.Equal("Dosya Gezgini", WindowPicker.Tooltip(0, null, "Dosya Gezgini"));
    }
}

/// <summary>1.6.25: arayüz bağlantıları (WPF Linux'ta çalışmadığı için kaynak üzerinden).</summary>
public class V1625PickerUiTests
{
    static string Src(string rel)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "KutiDock"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "src", "KutiDock", rel));
    }

    [Fact]
    public void Click_ChecksPickerBeforeMinimizing()
    {
        var apps = Src(Path.Combine("Tiles", "AppsTile.cs"));
        int picker = apps.IndexOf("== AppClick.Picker) { Preview(it, target); return; }", StringComparison.Ordinal);
        int minimize = apps.IndexOf("Native.ShowWindow(fg, Native.SW_MINIMIZE)", StringComparison.Ordinal);
        Assert.True(picker > 0 && picker < minimize);
        Assert.Contains("WindowPicker.Tooltip(", apps);
    }

    [Fact]
    public void Picker_Style()
    {
        var p = Src(Path.Combine("Tiles", "WindowPreview.cs"));
        Assert.Contains("WindowPicker.CardTitle(", p);
        Assert.Contains("new CornerRadius(12)", p);
        Assert.Contains("card.BorderBrush = Theme.Accent", p);
        Assert.Contains("FadeIn()", p);
        Assert.Contains("DwmRegisterThumbnail", p);
    }

    [Fact]
    public void SelfTest_IsAHeadlessArg()
    {
        var app = Src("App.xaml.cs");
        Assert.Contains("case PickerSelfTest.Arg: Shutdown(PickerSelfTest.Run(e.Args)); return;", app);
        Assert.True(app.IndexOf("PickerSelfTest.Arg", StringComparison.Ordinal) < app.IndexOf("KutiDock.TekKopya", StringComparison.Ordinal));
    }
}
