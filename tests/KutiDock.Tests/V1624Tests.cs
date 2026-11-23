using KutiDock.Core;
using Xunit;

/// <summary>
/// 1.6.24: dock'a kullanıcının kendi sabitlemesi. Klasik dock'un sabitlenmiş uygulamaları görev çubuğunun
/// "User Pinned\TaskBar" kısayolları, sıra oluşturulma zamanı. Sağ tıkla sabitle/kaldır, sürükle-bırak, sıralama ve
/// dışarı sürükleyip kaldırma bu klasörü aynı biçimde değiştirir.
/// </summary>
public class V1624PinTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "kd-pin-" + Guid.NewGuid().ToString("N")[..8]);
    readonly Dictionary<string, DateTime> _times = new(StringComparer.OrdinalIgnoreCase);
    DateTime _clock = new(2026, 10, 25, 1, 0, 0, DateTimeKind.Utc);

    string Dir => Path.Combine(_root, "TaskBar");
    string Removed => Path.Combine(_root, "kaldirilan");

    // Linux'ta oluşturulma zamanı yazılamaz: zamanlar bellekte tutulur; yeni dosya "şimdi" oluşturulmuş sayılır.
    PinFolder Pins() => new(Dir, Removed,
        p => _times.TryGetValue(p, out var t) ? t : _times[p] = (_clock = _clock.AddMinutes(1)),
        (p, t) => _times[p] = t);

    string Existing(string name)
    {
        Directory.CreateDirectory(Dir);
        var p = Path.Combine(Dir, name);
        File.WriteAllText(p, "lnk");
        _times[p] = _clock = _clock.AddMinutes(1);
        return p;
    }

    List<string> Names(PinFolder f) => f.Ordered().Select(Path.GetFileName).ToList()!;

    public void Dispose() { try { Directory.Delete(_root, true); } catch (Exception) { } }

    [Fact]
    public void Ordered_FollowsCreationTime_AndIncludesUrl()
    {
        Existing("B.lnk"); Existing("A.lnk"); Existing("Site.url");
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(Dir).FullName, "not.txt"), "x");
        Assert.Equal(new[] { "B.lnk", "A.lnk", "Site.url" }, Names(Pins()));
    }

    [Fact]
    public void Ordered_MissingFolder_IsEmpty() => Assert.Empty(Pins().Ordered());

    [Fact]
    public void AddNew_AtEnd_KeepsExistingTimes()
    {
        var a = Existing("A.lnk"); var b = Existing("B.lnk");
        var ta = _times[a]; var tb = _times[b];
        var f = Pins();
        var c = f.AddNew("iVCam", p => File.WriteAllText(p, "lnk"), int.MaxValue);
        Assert.Equal(Path.Combine(Dir, "iVCam.lnk"), c);
        Assert.Equal(new[] { "A.lnk", "B.lnk", "iVCam.lnk" }, Names(f));
        Assert.Equal(ta, _times[a]); Assert.Equal(tb, _times[b]); // sona ekleme eski kısayollara dokunmaz
    }

    [Fact]
    public void AddNew_AtIndex_InsertsThere()
    {
        Existing("A.lnk"); Existing("B.lnk"); Existing("C.lnk");
        var f = Pins();
        f.AddNew("Yeni", p => File.WriteAllText(p, "lnk"), 1);
        Assert.Equal(new[] { "A.lnk", "Yeni.lnk", "B.lnk", "C.lnk" }, Names(f));
        f.AddNew("Bas", p => File.WriteAllText(p, "lnk"), 0);
        Assert.Equal("Bas.lnk", Names(f)[0]);
    }

    [Fact]
    public void AddNew_WriterFails_ReturnsNull_AndNothingPinned()
    {
        Existing("A.lnk");
        var f = Pins();
        Assert.Null(f.AddNew("Bozuk", _ => { }, 0));
        Assert.Equal(new[] { "A.lnk" }, Names(f));
    }

    [Fact]
    public void AddCopy_CopiesShortcut_WithFreeName_SourceUntouched()
    {
        Existing("Chrome.lnk");
        var src = Path.Combine(Directory.CreateDirectory(Path.Combine(_root, "Desktop")).FullName, "Chrome.lnk");
        File.WriteAllText(src, "kaynak");
        var f = Pins();
        var copy = f.AddCopy(src, int.MaxValue);
        Assert.Equal(Path.Combine(Dir, "Chrome (2).lnk"), copy);
        Assert.Equal("kaynak", File.ReadAllText(copy));
        Assert.True(File.Exists(src));
        Assert.Equal(new[] { "Chrome.lnk", "Chrome (2).lnk" }, Names(f));
    }

    [Fact]
    public void AddCopy_Url_IsPinned()
    {
        var src = Path.Combine(Directory.CreateDirectory(Path.Combine(_root, "Bolmeler")).FullName, "Kutiflix.URL");
        File.WriteAllText(src, "[InternetShortcut]\nURL=https://example.com");
        var f = Pins();
        f.AddCopy(src, 0);
        Assert.Equal(new[] { "Kutiflix.url" }, Names(f));
    }

    [Fact]
    public void Move_Reorders_BothDirections()
    {
        var a = Existing("A.lnk"); Existing("B.lnk"); var c = Existing("C.lnk"); Existing("D.lnk");
        var f = Pins();
        f.Move(a, 2);
        Assert.Equal(new[] { "B.lnk", "C.lnk", "A.lnk", "D.lnk" }, Names(f));
        f.Move(c, 0);
        Assert.Equal(new[] { "C.lnk", "B.lnk", "A.lnk", "D.lnk" }, Names(f));
        f.Move(c, 99); // sınır dışı sıra sona
        Assert.Equal("C.lnk", Names(f)[^1]);
    }

    [Fact]
    public void Move_Persists_ForNewInstance()
    {
        var a = Existing("A.lnk"); Existing("B.lnk");
        Pins().Move(a, 1);
        Assert.Equal(new[] { "B.lnk", "A.lnk" }, Names(Pins())); // yeniden başlatma: yeni nesne aynı sırayı okur
    }

    [Fact]
    public void Remove_MovesToBackup_NotDeleted()
    {
        var a = Existing("A.lnk"); Existing("B.lnk");
        var f = Pins();
        var dest = f.Remove(a);
        Assert.NotNull(dest);
        Assert.False(File.Exists(a));
        Assert.True(File.Exists(dest));
        Assert.StartsWith(Removed, dest);
        Assert.Equal(new[] { "B.lnk" }, Names(f));
        Assert.Null(f.Remove(a)); // ikinci kez: yok
    }

    [Fact]
    public void Remove_SameNameTwice_KeepsBothBackups()
    {
        var f = Pins();
        var d1 = f.Remove(Existing("A.lnk"));
        var d2 = f.Remove(Existing("A.lnk"));
        Assert.NotEqual(d1, d2);
        Assert.True(File.Exists(d1) && File.Exists(d2));
    }

    [Fact]
    public void FreePath_SanitizesName()
    {
        var f = Pins();
        Assert.Equal(Path.Combine(Dir, "a_b.lnk"), f.FreePath("a/b", ".lnk"));
        Assert.Equal(Path.Combine(Dir, "Uygulama.lnk"), f.FreePath("  ", ".lnk"));
    }

    [Fact]
    public void Times_IncreasingKept_OtherwiseRewrittenFromOldest()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var inc = new[] { t0, t0.AddDays(1), t0.AddDays(2) };
        Assert.Equal(inc, PinFolder.Times(inc));
        var mixed = new[] { t0.AddDays(3), t0, t0.AddDays(1) };
        Assert.Equal(new[] { t0, t0.AddSeconds(1), t0.AddSeconds(2) }, PinFolder.Times(mixed));
        var same = new[] { t0, t0 };
        Assert.Equal(new[] { t0, t0.AddSeconds(1) }, PinFolder.Times(same));
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(10, 0)]
    [InlineData(29, 1)]
    [InlineData(55, 1)]
    [InlineData(70, 2)]
    [InlineData(200, 3)]
    public void InsertIndex_ByIconMiddle(double pos, int expected)
    {
        var slots = new List<(double, double)> { (0, 40), (40, 40), (80, 40) };
        Assert.Equal(expected, PinFolder.InsertIndex(slots, pos));
    }

    [Fact]
    public void InsertIndex_Empty_IsZero() => Assert.Equal(0, PinFolder.InsertIndex([], 10));

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0, 1, 0)] // kendi yerinin hemen ardı: yer değişmez
    [InlineData(0, 3, 2)]
    [InlineData(2, 0, 0)]
    [InlineData(2, 3, 2)]
    [InlineData(1, 4, 3)]
    public void MoveTarget_AccountsForOwnSlot(int from, int gap, int expected) =>
        Assert.Equal(expected, PinFolder.MoveTarget(from, gap));

    [Fact]
    public void FindShortcut_MatchesKey_SkipsBroken()
    {
        var links = new[] { "bozuk.lnk", "chrome.lnk", "ivcam.lnk", "ivcam2.lnk" };
        string? KeyOf(string l) => l switch
        {
            "bozuk.lnk" => throw new InvalidOperationException(),
            "chrome.lnk" => @"c:\chrome\chrome.exe",
            _ => @"c:\program files\e2esoft\ivcam\ivcam.exe",
        };
        Assert.Equal("ivcam.lnk", PinFolder.FindShortcut(@"C:\Program Files\e2eSoft\iVCam\iVCam.exe".ToLowerInvariant(), links, KeyOf));
        Assert.Null(PinFolder.FindShortcut(@"c:\yok.exe", links, KeyOf));
    }

    [Fact]
    public void FindShortcut_PwaByShortenedCrxId()
    {
        const string full = "hnpfjngllnobngcgfapefoaidbinmjnm", shortened = "hnpfjngllnfapefoaidbinmjnm";
        var found = PinFolder.FindShortcut("crx:" + shortened, ["WhatsApp.lnk"],
            l => AppGrouping.PinnedKey(l, @"c:\c\chrome_proxy.exe", "--app-id=" + full, null));
        Assert.Equal("WhatsApp.lnk", found);
    }

    [Fact]
    public void Shortcuts_RecursesAndSkipsMissingRoots()
    {
        var sm = Directory.CreateDirectory(Path.Combine(_root, "Start", "Programs", "Alt")).FullName;
        File.WriteAllText(Path.Combine(sm, "x.lnk"), "");
        File.WriteAllText(Path.Combine(sm, "y.txt"), "");
        var all = PinFolder.Shortcuts([Path.Combine(_root, "yok"), Path.Combine(_root, "Start"), ""]).ToList();
        Assert.Single(all);
        Assert.EndsWith("x.lnk", all[0]);
    }

    [Fact]
    public void CanPin_ExeLnkUrlFolderOnly()
    {
        var d = Directory.CreateDirectory(Path.Combine(_root, "dosyalar")).FullName;
        foreach (var n in new[] { "a.exe", "b.LNK", "c.url", "d.txt", "e.png" }) File.WriteAllText(Path.Combine(d, n), "");
        Assert.True(PinFolder.CanPin(Path.Combine(d, "a.exe")));
        Assert.True(PinFolder.CanPin(Path.Combine(d, "b.LNK")));
        Assert.True(PinFolder.CanPin(Path.Combine(d, "c.url")));
        Assert.True(PinFolder.CanPin(d));
        Assert.False(PinFolder.CanPin(Path.Combine(d, "d.txt")));
        Assert.False(PinFolder.CanPin(Path.Combine(d, "e.png")));
        Assert.False(PinFolder.CanPin(Path.Combine(d, "yok.exe")));
        Assert.False(PinFolder.CanPin(null));
    }

    [Fact]
    public void PinnedKey_StoreAppLink_MatchesItsWindow()
    {
        const string calc = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App";
        var pinned = AppGrouping.PinnedKey(@"C:\x\Hesap Makinesi.lnk", null, "", null, calc);
        Assert.Equal("aumid:" + calc.ToLowerInvariant(), pinned);
        Assert.True(AppGrouping.SameKey(pinned, AppGrouping.WindowKey(@"C:\Program Files\WindowsApps\x\CalculatorApp.exe", calc, true)));
    }

    [Fact]
    public void PinnedKey_ExistingBehaviourUnchanged()
    {
        Assert.Equal(@"c:\x\a.exe", AppGrouping.PinnedKey(@"C:\x\A.lnk", @"C:\x\A.exe", "", null, null));
        Assert.Equal(@"c:\x\a.exe", AppGrouping.PinnedKey(@"C:\x\A.lnk", @"C:\x\A.exe", "", null, "Some.App")); // hedef exe öncelikli
        Assert.Equal(@"c:\x\gezgin.lnk", AppGrouping.PinnedKey(@"C:\x\Gezgin.lnk", null, "", null));
        Assert.Equal("crx:hnpfjngllnobngcgfapefoaidbinmjnm", AppGrouping.PinnedKey(@"C:\x\W.lnk", null, "", null, "Chrome._crx_hnpfjngllnobngcgfapefoaidbinmjnm"));
    }
}

/// <summary>1.6.24: arayüz bağlantıları (WPF Linux'ta çalışmadığı için kaynak üzerinden).</summary>
public class V1624PinUiTests
{
    static string Src(string rel)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "KutiDock"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "src", "KutiDock", rel));
    }

    [Fact]
    public void Menu_HasPinAndUnpin()
    {
        var apps = Src(Path.Combine("Tiles", "AppsTile.cs"));
        Assert.Contains("\"Dock'a sabitle\"", apps);
        Assert.Contains("\"Dock'tan kaldır\"", apps);
        Assert.Contains("Görevi sonlandır", Src("TaskKiller.cs")); // eski menü öğeleri yerinde
        Assert.Contains("Pencereyi kapat", apps);
    }

    [Fact]
    public void Drop_DragAndPersistence_AreWired()
    {
        var apps = Src(Path.Combine("Tiles", "AppsTile.cs"));
        Assert.Contains("AllowDrop = true", apps);
        Assert.Contains("Drop += OnDrop", apps);
        Assert.Contains("Pins.Ordered()", apps);      // sıra PinFolder'dan
        Assert.Contains("Pins.Move(", apps);
        Assert.Contains("Pins.Remove(", apps);
        Assert.Contains("if (Dragging) return;", apps); // sürüklerken yeniden kurulmaz
        Assert.Contains("Tiles.AppsTile.Dragging", Src("DockWindow.cs")); // sürüklerken dock saklanmaz
    }

    [Fact]
    public void SelfTest_IsAHeadlessArg()
    {
        var app = Src("App.xaml.cs");
        Assert.Contains("case PinSelfTest.Arg: Shutdown(PinSelfTest.Run(e.Args)); return;", app);
        int selfTest = app.IndexOf("PinSelfTest.Arg", StringComparison.Ordinal), mutex = app.IndexOf("KutiDock.TekKopya", StringComparison.Ordinal);
        Assert.True(selfTest > 0 && selfTest < mutex); // tek kopya kilidinden önce: çalışan dock'u etkilemez
    }
}
