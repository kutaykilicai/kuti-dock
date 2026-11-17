using KutiDock.Core;
using Xunit;

public class AppGroupingTests
{
    const string Wa = "hnpfjngllnobngcgfapefoaidbinmjnm";

    [Fact]
    public void CrxId_FromAumidAndArgs()
    {
        Assert.Equal(Wa, AppGrouping.CrxId($"Chrome._crx_{Wa}"));
        Assert.Equal(Wa, AppGrouping.CrxId($"--profile-directory=Default --app-id={Wa.ToUpperInvariant()}"));
        Assert.Null(AppGrouping.CrxId("Chrome"));
        Assert.Null(AppGrouping.CrxId("--app-id=short"));
        Assert.Null(AppGrouping.CrxId(null));
    }

    [Fact]
    public void WhatsAppPwa_IsNotChrome()
    {
        var chrome = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
        var pwa = AppGrouping.WindowKey(chrome, $"Chrome._crx_{Wa}", packaged: false);
        var browser = AppGrouping.WindowKey(chrome, "Chrome", packaged: false);
        Assert.Equal("crx:" + Wa, pwa);
        Assert.Equal(chrome.ToLowerInvariant(), browser);
        Assert.NotEqual(pwa, browser);
        // sabitlenmiş WhatsApp kısayolu pencereyle aynı anahtar, Chrome kısayolu tarayıcıyla aynı
        Assert.Equal(pwa, AppGrouping.PinnedKey(@"C:\x\WhatsApp.lnk", chrome, $"--app-id={Wa}", null));
        Assert.Equal(browser, AppGrouping.PinnedKey(@"C:\x\Chrome.lnk", chrome, "", null));
    }

    [Fact]
    public void Packaged_UsesAumid()
    {
        Assert.Equal("aumid:microsoft.windowscalculator_8wekyb3d8bbwe!app",
            AppGrouping.WindowKey(@"C:\Windows\System32\ApplicationFrameHost.exe", "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", packaged: true));
    }
}

public class AppMenuTests
{
    [Fact]
    public void Desktop_ShowsFinderMenus()
    {
        var m = GenericMenus.For("Gezgin", desktop: true, explorer: false);
        Assert.Equal(new[] { "Dosya", "Git", "Yardım" }, m.Select(x => x.Title));
        Assert.Contains(m[1].Items, e => e.Arg == "shell:Downloads");
        Assert.Contains(m[0].Items, e => e.Arg == "shell:RecycleBinFolder");
    }

    [Fact]
    public void App_HasGenericMenus_ExplorerAddsGo()
    {
        Assert.Equal(new[] { "Dosya", "Düzen", "Görüntü", "Pencere", "Yardım" }, GenericMenus.For("Not Defteri", false, false).Select(x => x.Title));
        Assert.Contains("Git", GenericMenus.For("Gezgin", false, true).Select(x => x.Title));
        var app = GenericMenus.AppMenu("Chrome");
        Assert.Contains(app.Items, e => e.Command == MenuCommand.Quit);
        Assert.Contains(app.Items, e => e.Command == MenuCommand.HideOthers);
    }

    [Fact]
    public void Win32Text_Splits()
    {
        Assert.Equal(("_Aç...", "Ctrl+O"), GenericMenus.SplitWin32("&Aç...\tCtrl+O"));
        Assert.Equal(("Kes & yapıştır", (string?)null), GenericMenus.SplitWin32("Kes && yapıştır"));
        Assert.Equal(("a__b", (string?)null), GenericMenus.SplitWin32("a_b"));
    }

    [Fact]
    public void Shortcuts_ToVirtualKeys()
    {
        Assert.Equal(new ushort[] { 0x11, 0x10, (ushort)'N' }, Shortcut.VirtualKeys("Ctrl+Shift+N"));
        Assert.Equal(new ushort[] { 0x7A }, Shortcut.VirtualKeys("F11"));
        Assert.Equal(new ushort[] { 0x11, 0xBB }, Shortcut.VirtualKeys("Ctrl+Plus"));
        Assert.Null(Shortcut.VirtualKeys("Ctrl+???"));
        Assert.True(Shortcut.Extended(0x25));
        Assert.False(Shortcut.Extended(0x11));
    }
}

public class LaunchGenieTests
{
    [Fact]
    public void Matching()
    {
        Assert.True(LaunchGeniePolicy.Matches("crx:abc", "CRX:abc"));
        Assert.False(LaunchGeniePolicy.Matches("crx:abc", @"c:\chrome\chrome.exe")); // WhatsApp beklenirken Chrome penceresi değil
        Assert.False(LaunchGeniePolicy.Matches(@"c:\a\app.exe", @"c:\b\other.exe"));
        Assert.True(LaunchGeniePolicy.Matches(@"c:\docs\rapor.pdf", @"c:\reader\reader.exe")); // dosya: ilk yeni ana pencere
        Assert.True(LaunchGeniePolicy.AcceptsAny(@"c:\users\k\desktop\klasör"));
    }

    [Fact]
    public void Skips_FullscreenAndDisabled()
    {
        Assert.False(LaunchGeniePolicy.Watch(animations: true, fullscreen: true));
        Assert.False(LaunchGeniePolicy.Watch(animations: false, fullscreen: false));
        Assert.True(LaunchGeniePolicy.Watch(true, false));
    }

    [Fact]
    public void Candidate_RejectsUacGamesToolsAndTiny()
    {
        Assert.True(LaunchGeniePolicy.Candidate(false, false, true, 800, 600, "Chrome_WidgetWin_1", "chrome", false));
        Assert.False(LaunchGeniePolicy.Candidate(false, false, true, 800, 600, "Credential Dialog Xaml Host", "consent", false));
        Assert.False(LaunchGeniePolicy.Candidate(false, false, true, 800, 600, "X", "consent", false));
        Assert.False(LaunchGeniePolicy.Candidate(false, false, true, 1920, 1080, "UnrealWindow", "Marvel-Win64-Shipping", true));
        Assert.False(LaunchGeniePolicy.Candidate(false, true, true, 800, 600, "X", "x", false));
        Assert.False(LaunchGeniePolicy.Candidate(true, false, true, 800, 600, "X", "KutiDock", false));
        Assert.False(LaunchGeniePolicy.Candidate(false, false, true, 150, 80, "X", "x", false)); // açılış ekranı
    }

    [Fact]
    public void EmptyFrames_AreRejected()
    {
        int w = 100, h = 80;
        var black = new uint[w * h];
        Assert.False(FrameCheck.HasDetail(black, w, h));
        var white = Enumerable.Repeat(0xFFFFFFFFu, w * h).ToArray();
        Assert.False(FrameCheck.HasDetail(white, w, h));
        var content = new uint[w * h];
        for (int i = 0; i < content.Length; i++) content[i] = 0xFF000000u | (uint)((i * 2654435761u) & 0xFFFFFF);
        Assert.True(FrameCheck.HasDetail(content, w, h));
    }
}

public class ComWorkerTests
{
    [Fact]
    public void RunsOnOneDedicatedThread()
    {
        var w = new ComWorker("test");
        int a = w.Run(() => Environment.CurrentManagedThreadId, -1);
        int b = w.Run(() => Environment.CurrentManagedThreadId, -1);
        Assert.Equal(a, b);
        Assert.NotEqual(Environment.CurrentManagedThreadId, a);
        Assert.Equal(7, w.Run(() => w.Run(() => 7, 0), 0)); // iş parçacığı içinden çağrı kilitlenmez
    }

    [Fact]
    public void ErrorGivesFallbackAndReports()
    {
        var w = new ComWorker("test");
        Exception? seen = null;
        Assert.Equal(-1, w.Run<int>(() => throw new System.Runtime.InteropServices.COMException("x", unchecked((int)0x800401F0)), -1, ex => seen = ex));
        Assert.NotNull(seen);
        Assert.Equal(5, w.Run(() => 5, -1)); // hatadan sonra çalışmaya devam eder
    }

    [Fact]
    public void Timeout_GivesFallback()
    {
        var w = new ComWorker("test", timeoutMs: 50);
        Assert.Equal(-1, w.Run(() => { Thread.Sleep(400); return 1; }, -1));
    }
}

public class V167Tests
{
    // Kasa'da okunan gerçek değerler: kısayol --app-id ve pencerenin PKEY_AppUserModel_ID'si
    const string Full = "hnpfjngllnobngcgfapefoaidbinmjnm", WindowAumid = "Chrome._crx_hnpfjngllnfapefoaidbinmjnm";

    [Fact]
    public void ShortenedChromeAumid_MatchesPinnedWhatsApp()
    {
        Assert.True(AppGrouping.SameCrx(Full, AppGrouping.CrxId(WindowAumid)));
        var pinned = AppGrouping.PinnedKey(@"C:\x\WhatsApp Web.lnk", @"C:\c\chrome_proxy.exe", $"--profile-directory=Default --app-id={Full}", null);
        var win = AppGrouping.WindowKey(@"C:\c\chrome.exe", WindowAumid, false);
        Assert.NotEqual(pinned, win);                 // 1.6.6'daki hata: anahtarlar tutmuyordu
        Assert.True(AppGrouping.SameKey(pinned, win)); // 1.6.7
        var list = new[] { (@"c:\c\chrome.exe", "Google Chrome"), (pinned, "WhatsApp Web") };
        Assert.Equal(pinned, AppGrouping.Resolve(win, "WhatsApp Web", true, list));
        Assert.True(LaunchGeniePolicy.Matches(pinned, win));
    }

    [Fact]
    public void DifferentCrxIds_DoNotMatch()
    {
        Assert.False(AppGrouping.SameCrx(Full, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"));
        Assert.False(AppGrouping.SameCrx(Full, "hnpfjnglln"));                   // çok kısa
        Assert.False(AppGrouping.SameKey("crx:" + Full, @"c:\c\chrome.exe"));
    }

    [Fact]
    public void TitleFallback_OnlyForPwaShortcuts()
    {
        var pinned = "crx:" + Full;
        var list = new[] { (@"c:\c\chrome.exe", "Google Chrome"), (pinned, "WhatsApp Web") };
        // AUMID okunamadı (düz Chrome) ama başlık kısayol adıyla birebir aynı: PWA penceresi
        Assert.Equal(pinned, AppGrouping.Resolve(@"c:\c\chrome.exe", "WhatsApp Web", true, list));
        // tarayıcı sekmesi başlığı: Chrome'da kalır
        Assert.Equal(@"c:\c\chrome.exe", AppGrouping.Resolve(@"c:\c\chrome.exe", "WhatsApp Web - Google Chrome", true, list));
        // Chromium olmayan pencere başlıkla bağlanmaz
        Assert.Equal(@"c:\x\notepad.exe", AppGrouping.Resolve(@"c:\x\notepad.exe", "WhatsApp Web", false, list));
        // Electron uygulaması (Grok Bot) kendi exe anahtarıyla sabitlenmiş simgesine
        var grok = @"c:\users\k\appdata\local\programs\grok bot\grok bot.exe";
        Assert.Equal(grok, AppGrouping.Resolve(@"C:\Users\K\AppData\Local\Programs\Grok Bot\Grok Bot.exe".ToLowerInvariant(), "Grok Bot", true,
            new[] { (grok, "Grok Bot") }));
    }

    [Fact]
    public void MainWindowShape_ExcludesTooltipsMenusPopups()
    {
        Assert.True(LaunchGeniePolicy.MainWindowShape(false, true, 1280, 800, "Chrome_WidgetWin_1"));
        Assert.False(LaunchGeniePolicy.MainWindowShape(false, false, 300, 200, "tooltips_class32"));
        Assert.False(LaunchGeniePolicy.MainWindowShape(false, true, 300, 200, "#32768"));
        Assert.False(LaunchGeniePolicy.MainWindowShape(true, true, 600, 400, "#32770"));   // sahipli iletişim kutusu
        Assert.False(LaunchGeniePolicy.MainWindowShape(false, false, 600, 400, "Popup"));  // çerçevesiz açılır pencere
        Assert.True(LaunchGeniePolicy.IsUac("Credential Dialog Xaml Host", "x"));
        Assert.True(LaunchGeniePolicy.IsUac("X", "consent"));
        Assert.False(LaunchGeniePolicy.IsUac("Chrome_WidgetWin_1", "Grok Bot"));
    }
}
