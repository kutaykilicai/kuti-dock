using KutayDock.Core;
using Xunit;

public class FormatTests
{
    [Theory]
    [InlineData(0, "<1dk")]
    [InlineData(45, "45dk")]
    [InlineData(60, "1s")]
    [InlineData(160, "2s 40dk")]
    [InlineData(60 * 27, "1g 3s")]
    public void Duration(int minutes, string expected) => Assert.Equal(expected, Format.Duration(TimeSpan.FromMinutes(minutes)));

    [Fact]
    public void Rate()
    {
        Assert.Equal("512 B/s", Format.Rate(512));
        Assert.Equal("2 KB/s", Format.Rate(2048));
        Assert.Equal("1,5 MB/s", Format.Rate(1.5 * 1024 * 1024));
    }

    [Fact]
    public void Bytes()
    {
        Assert.Equal("245 GB", Format.Bytes(245L * 1024 * 1024 * 1024));
        Assert.Equal("1,8 TB", Format.Bytes((long)(1.8 * 1024 * 1024 * 1024 * 1024)));
        Assert.Equal("512 MB", Format.Bytes(512L * 1024 * 1024));
    }

    [Fact]
    public void Cpu() => Assert.Equal(25.0, Format.CpuPercent(0, 0, 0, 750, 800, 200));
}

public class LimitsTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.FromHours(3));

    [Fact]
    public void EmptyMeansNoSource()
    {
        Assert.Null(Limits.Fraction(new ManualLimit(), Now));
        Assert.Equal("kaynak yok · elle gir", Limits.Freshness(new ManualLimit(), Now));
        Assert.Equal("", Limits.ResetLabel(new ManualLimit(), Now));
    }

    [Fact]
    public void ManualValueAndReset()
    {
        var l = new ManualLimit { Percent = 49.5, ResetsAt = new DateTimeOffset(2026, 11, 17, 13, 7, 0, TimeSpan.FromHours(3)), UpdatedAt = Now.AddMinutes(-30) };
        Assert.Equal(0.495, Limits.Fraction(l, Now)!.Value, 3);
        Assert.Equal("elle · 30dk önce", Limits.Freshness(l, Now));
        Assert.Equal("Sal 13:07", Limits.ResetLabel(l, Now)); // 1 günden uzak: gün + saat
        Assert.Equal("1s 7dk", Limits.ResetLabel(l, Now.AddHours(24)));
        // sıfırlanma geçince eski yüzde gösterilmez
        Assert.Null(Limits.Fraction(l, Now.AddDays(2)));
        Assert.Equal("sıfırlandı", Limits.ResetLabel(l, Now.AddDays(2)));
    }

    [Fact]
    public void CursorTierOnlyReadsTier()
    {
        var json = "{\"cliVersion\":\"2026.10.22\",\"subscriptionTier\":\"Pro+\",\"userEmail\":\"x@example.com\"}";
        Assert.Equal("Pro+", Limits.ParseCursorTier(json));
        Assert.Equal("Pro+", Limits.ParseCursorTier("uyarı satırı\n" + json));
        Assert.Null(Limits.ParseCursorTier("not json"));
        Assert.Null(Limits.ParseCursorTier("{\"a\":1}"));
    }
}

public class ShareXTests
{
    [Theory]
    [InlineData(ShareXCapture.Bolge, "-RectangleRegion")]
    [InlineData(ShareXCapture.Pencere, "-ActiveWindow")]
    [InlineData(ShareXCapture.TamEkran, "-PrintScreen")]
    [InlineData(ShareXCapture.SonBolge, "-LastRegion")]
    [InlineData(ShareXCapture.Monitor, "-ActiveMonitor")]
    public void Args(ShareXCapture c, string arg) => Assert.Equal(arg, ShareX.Args(c));

    [Fact]
    public void FindPrefersConfiguredThenCandidates()
    {
        var have = new HashSet<string> { @"C:\Program Files\ShareX\ShareX.exe", @"D:\Araclar\ShareX.exe" };
        var cands = new[] { @"C:\Program Files\ShareX\ShareX.exe", null! };
        Assert.Equal(@"D:\Araclar\ShareX.exe", ShareX.Find("\"D:\\Araclar\\ShareX.exe\"", cands, have.Contains));
        Assert.Equal(@"C:\Program Files\ShareX\ShareX.exe", ShareX.Find("", cands, have.Contains));
        Assert.Equal(@"C:\Program Files\ShareX\ShareX.exe", ShareX.Find(@"E:\yok.exe", cands, have.Contains));
        Assert.Null(ShareX.Find(null, new[] { @"Z:\yok.exe" }, have.Contains));
    }
}

public class TaskbarGeometryTests
{
    static readonly Box Primary = new(0, 0, 2560, 1440);
    static readonly Box Left = new(-1920, 272, 0, 1352);

    [Fact]
    public void TopTaskbarTriggersAtTopEdge()
    {
        var s = TaskbarGeometry.TriggerStrip(new Box(0, 0, 2560, 48), Primary);
        Assert.Equal(new Box(0, 0, 2560, 2), s);
        Assert.True(s.Contains(1280, 0)); Assert.False(s.Contains(1280, 10));
    }

    [Fact]
    public void SecondaryMonitorTopTaskbar() =>
        Assert.Equal(new Box(-1920, 272, 0, 274), TaskbarGeometry.TriggerStrip(new Box(-1920, 272, 0, 320), Left));

    [Fact]
    public void BottomLeftRightEdges()
    {
        Assert.Equal(new Box(0, 1438, 2560, 1440), TaskbarGeometry.TriggerStrip(new Box(0, 1392, 2560, 1440), Primary));
        Assert.Equal(new Box(0, 0, 2, 1440), TaskbarGeometry.TriggerStrip(new Box(0, 0, 60, 1440), Primary));
        Assert.Equal(new Box(2558, 0, 2560, 1440), TaskbarGeometry.TriggerStrip(new Box(2500, 0, 2560, 1440), Primary));
    }
}

public class CursorApiTests
{
    // Not: gerçek yanıt yapısı bilinmiyor; alan adları Nowdex 1.4.1 dizelerinden. Ayrıştırıcı alanları her derinlikte arar.
    [Fact]
    public void SandUsagePercentAndResetFlatOrNested()
    {
        var flat = CursorApi.ParseSand("{\"isEnabled\":true,\"hasNonZeroIncludedLimit\":true,\"usagePercent\":49.5,\"nextResetTimestampUtc\":\"2026-11-17T10:07:00Z\"}")!;
        Assert.True(flat.Enabled); Assert.Equal(49.5, flat.Percent);
        Assert.Equal(new DateTimeOffset(2026, 11, 17, 13, 7, 0, TimeSpan.FromHours(3)), flat.ResetsAt); // UTC+3'te Sal 13:07
        var nested = CursorApi.ParseSand("{\"status\":{\"planUsage\":{\"totalPercentUsed\":\"12\",\"nextResetTimestampUtc\":\"1795428420000\"}}}")!;
        Assert.Equal(12, nested.Percent);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1795428420000), nested.ResetsAt);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1795428420), CursorApi.ParseSand("{\"nextResetTimestampUtc\":1795428420}")!.ResetsAt);
    }

    [Fact]
    public void SandDisabledAndGarbage()
    {
        Assert.False(CursorApi.ParseSand("{\"isEnabled\":true,\"includedLimitZero\":true}")!.Enabled);
        Assert.False(CursorApi.ParseSand("{\"isEnabled\":false}")!.Enabled);
        Assert.Null(CursorApi.ParseSand("<html>")); Assert.Null(CursorApi.ParseSand("{}")!.Percent);
        Assert.Equal(100, CursorApi.ParseSand("{\"usagePercent\":130}")!.Percent);
    }

    [Fact]
    public void UsageSummaryCentsToDollars()
    {
        var json = "{\"billingCycleEnd\":\"2026-12-01T00:00:00.000Z\",\"membershipType\":\"pro_plus\",\"individualUsage\":{\"plan\":{\"enabled\":true,\"used\":1500,\"limit\":6000,\"autoPercentUsed\":10,\"apiPercentUsed\":20},\"onDemand\":{\"enabled\":true,\"used\":250,\"limit\":2000}}}";
        var u = CursorApi.ParseSummary(json)!;
        Assert.Equal("pro_plus", u.Membership);
        Assert.Equal(25, u.TotalPercent); // totalPercentUsed yoksa used/limit
        Assert.Equal(10, u.AutoPercent); Assert.Equal(20, u.ApiPercent);
        Assert.Equal(15, u.UsedDollars); Assert.Equal(60, u.LimitDollars);
        Assert.True(u.OnDemandEnabled); Assert.Equal(2.5, u.OnDemandUsedDollars); Assert.Equal(20, u.OnDemandLimitDollars);
        Assert.Equal(new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero), u.CycleEnd);
        Assert.Equal(33, CursorApi.ParseSummary("{\"individualUsage\":{\"plan\":{\"totalPercentUsed\":33}}}")!.TotalPercent);
    }

    [Fact]
    public void LoginUrlsAndPkce()
    {
        var v = CursorApi.NewVerifier();
        Assert.Equal(43, v.Length); Assert.DoesNotContain("=", v);
        // RFC 7636 Ek B örneği
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", CursorApi.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");
        Assert.Equal("https://cursor.com/loginDeepControl?challenge=abc&uuid=11111111-2222-3333-4444-555555555555&mode=login", CursorApi.LoginUrl("abc", id));
        Assert.Equal("https://api2.cursor.sh/auth/poll?uuid=11111111-2222-3333-4444-555555555555&verifier=x-y_z", CursorApi.PollUrl(id, "x-y_z"));
        Assert.Equal("tok", CursorApi.ParsePoll("{\"accessToken\":\"tok\",\"refreshToken\":\"r\"}"));
        Assert.Null(CursorApi.ParsePoll("{}")); Assert.Null(CursorApi.ParsePoll("bekle"));
    }

    [Fact]
    public void SessionCookieFromJwtSub()
    {
        static string B(string s) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var jwt = B("{\"alg\":\"none\"}") + "." + B("{\"sub\":\"auth0|user_ABC123\",\"exp\":1}") + ".imza";
        Assert.Equal("WorkosCursorSessionToken=user_ABC123%3A%3A" + jwt, CursorApi.SessionCookie(jwt));
        Assert.Null(CursorApi.SessionCookie("duz-metin"));
        Assert.Null(CursorApi.SessionCookie("a." + B("{\"x\":1}") + ".b"));
    }

    [Fact]
    public void LiveFreshnessLabel() =>
        Assert.Equal("Cursor · 5dk önce", Limits.LiveFreshness(DateTimeOffset.Now.AddMinutes(-5), DateTimeOffset.Now));
}

public class FullscreenRuleTests
{
    // Kasa düzeni: orta/ana 2560x1440, solda 1920x1080 (y=272); üçüncü ekran senaryosu için sağda 1920x1080.
    static readonly Box Main = new(0, 0, 2560, 1440), Left = new(-1920, 272, 0, 1352), Right = new(2560, 0, 4480, 1080);

    [Fact]
    public void FullscreenOnDockMonitorHides() =>
        Assert.True(FullscreenRule.HidesDock(Main, Main, Main, false, "UnityWndClass"));

    [Fact]
    public void FullscreenOnOtherMonitorsDoesNotHide()
    {
        Assert.False(FullscreenRule.HidesDock(Left, Left, Main, false, "Chrome_WidgetWin_1"));   // sol ekranda tam ekran video
        Assert.False(FullscreenRule.HidesDock(Right, Right, Main, false, "UnityWndClass"));      // sağ ekranda oyun
    }

    [Fact]
    public void MaximizedWindowIsNotFullscreen()
    {
        // Görev çubuğu gizliyken büyütülmüş pencere monitörü 8 px taşarak kaplar; bu tam ekran değildir.
        var maximized = new Box(-8, -8, 2568, 1448);
        Assert.False(FullscreenRule.HidesDock(maximized, Main, Main, true, "Chrome_WidgetWin_1"));
        Assert.True(FullscreenRule.HidesDock(maximized, Main, Main, false, "Chrome_WidgetWin_1")); // F11 / çerçevesiz tam ekran
    }

    [Fact]
    public void DesktopAndShellClassesIgnored()
    {
        foreach (var c in new[] { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" })
            Assert.False(FullscreenRule.HidesDock(Main, Main, Main, false, c));
    }

    [Fact]
    public void PartialWindowDoesNotHide() =>
        Assert.False(FullscreenRule.HidesDock(new Box(0, 70, 2560, 1440), Main, Main, false, "Notepad"));

    [Fact]
    public void DebounceNeedsStableTicks()
    {
        var d = new Debounce(2);
        Assert.False(d.Observe(true)); Assert.False(d.State);      // 1. tık: henüz değil
        Assert.True(d.Observe(true)); Assert.True(d.State);        // 2. tık: gizle
        Assert.False(d.Observe(false)); Assert.False(d.Observe(true)); Assert.True(d.State); // kısa odak değişimi: titreme yok
        Assert.False(d.Observe(false)); Assert.True(d.Observe(false)); Assert.False(d.State); // göster
    }
}

public class CursorShapeTests
{
    [Fact]
    public void ShapeHasNamesAndTypesButNoValues()
    {
        var json = """{"accessToken":"eyJsecret","usagePercent":46.5,"isEnabled":true,"plan":{"used":120,"items":[{"email":"k@x.com"}]},"x":null}""";
        var shape = CursorApi.Shape(json);
        Assert.Equal("{accessToken:str,usagePercent:num,isEnabled:bool,plan:{used:num,items:[{email:str}]},x:null}", shape);
        Assert.DoesNotContain("secret", shape);
        Assert.DoesNotContain("k@x.com", shape);
        Assert.DoesNotContain("46", shape);
        Assert.Equal("json değil", CursorApi.Shape("Not found"));
    }
}

public class WorkAreaTests
{
    static readonly Box Main = new(0, 0, 2560, 1440), Left = new(-1920, 272, 0, 1352);

    [Fact]
    public void HiddenTaskbarLeavesNoStrip()
    {
        Assert.Equal(Main, WorkArea.WhileTaskbarHidden(Main, null));   // Kasa: alttaki 48 px geri gelir
        Assert.Equal(Left, WorkArea.WhileTaskbarHidden(Left, null));
    }

    [Fact]
    public void DockReserveIsKeptWhenEnabled() =>
        Assert.Equal(new Box(0, 70, 2560, 1440), WorkArea.WhileTaskbarHidden(Main, new Box(0, 0, 2560, 70)));

    [Fact]
    public void SubtractBarOnAnyEdge()
    {
        Assert.Equal(new Box(0, 0, 2560, 1392), WorkArea.Subtract(Main, new Box(0, 1392, 2560, 1440), Main)); // alt
        Assert.Equal(new Box(0, 48, 2560, 1440), WorkArea.Subtract(Main, new Box(0, 0, 2560, 48), Main));     // üst
        Assert.Equal(new Box(48, 0, 2560, 1440), WorkArea.Subtract(Main, new Box(0, 0, 48, 1440), Main));     // sol
        Assert.Equal(new Box(-1920, 272, 0, 1304), WorkArea.Subtract(Left, new Box(-1920, 1304, 0, 1352), Left)); // ikincil
        Assert.Equal(Main, WorkArea.Subtract(Main, new Box(-1920, 1304, 0, 1352), Main)); // başka monitördeki çubuk
    }
}

public class FloatingDefaultsTests
{
    [Fact]
    public void NewSettingsFloat()
    {
        var s = new Settings().Migrate();
        Assert.False(s.ReserveSpace); Assert.False(s.FullWidth); Assert.Equal(5, s.Version);
        Assert.Equal(1, s.Widgets.IndexOf("search"));
    }

    [Fact]
    public void ExistingSettingsMigratedToFloating()
    {
        var s = new Settings { ReserveSpace = true, FullWidth = true, Version = 2 };
        var w = s.Widgets.ToList();
        s.Migrate();
        Assert.False(s.ReserveSpace); Assert.False(s.FullWidth);
        Assert.Equal(w, s.Widgets);
        s.ReserveSpace = true; s.Migrate();          // kullanıcı sonradan açarsa kalıcı
        Assert.True(s.ReserveSpace);
    }
}

public class VideoFullscreenTests
{
    static readonly Box Main = new(0, 0, 2560, 1440), Left = new(-1920, 272, 0, 1352);

    [Fact]
    public void ChromeHtml5FullscreenHides()
    {
        // Chrome/YouTube tam ekranı: başlık ve kenarlık kalkar, pencere monitörü tam kaplar (büyütülmüş durumdan girse de)
        Assert.True(FullscreenRule.HidesDock(Main, Main, Main, false, "Chrome_WidgetWin_1", hasCaption: false));
        Assert.True(FullscreenRule.HidesDock(Main, Main, Main, true, "Chrome_WidgetWin_1", hasCaption: false));
    }

    [Fact]
    public void MaximizedChromeWithCaptionDoesNotHide() =>
        // Kasa'da ölçülen: büyütülmüş Chrome -8,-8,2568,1448, başlık stili var
        Assert.False(FullscreenRule.HidesDock(new Box(-8, -8, 2568, 1448), Main, Main, true, "Chrome_WidgetWin_1", hasCaption: true));

    [Fact]
    public void WindowsSignals()
    {
        Assert.True(FullscreenRule.Decide(false, FullscreenRule.QunsD3DFullScreen, false));  // D3D tam ekran video/oyun
        Assert.True(FullscreenRule.Decide(false, FullscreenRule.QunsPresentation, false));   // sunum modu
        Assert.True(FullscreenRule.Decide(false, FullscreenRule.QunsBusy, true));            // tam ekran uygulama, dock ekranında
        Assert.False(FullscreenRule.Decide(false, FullscreenRule.QunsBusy, false));          // başka ekranda: gizleme
        Assert.False(FullscreenRule.Decide(false, 5, true));                                 // QUNS_ACCEPTS_NOTIFICATIONS (normal)
        Assert.True(FullscreenRule.Decide(true, 5, true));
    }

    [Fact]
    public void FastHideSlowShow()
    {
        var d = new Debounce(2, 8); // 250 ms tık: 0,5 sn'de gizle, 2 sn'de göster
        Assert.False(d.Observe(true)); Assert.True(d.Observe(true));
        for (int i = 0; i < 7; i++) Assert.False(d.Observe(false));
        Assert.True(d.Observe(false)); Assert.False(d.State);
    }
}

public class DockLayoutTests
{
    // Kasa ana ekranı (WPF birimi = piksel, ölçek 1)
    const double X = 0, Y = 0, W = 2560, H = 1440;

    [Fact]
    public void EdgesPlaceDock()
    {
        Assert.Equal((1030.0, 1378.0), DockLayout.Position(DockEdge.Alt, X, Y, W, H, 500, 54, 8));
        Assert.Equal((1030.0, 8.0), DockLayout.Position(DockEdge.Ust, X, Y, W, H, 500, 54, 8));
        Assert.Equal((8.0, 470.0), DockLayout.Position(DockEdge.Sol, X, Y, W, H, 58, 500, 8));   // dikey ortalı
        Assert.Equal((2494.0, 470.0), DockLayout.Position(DockEdge.Sag, X, Y, W, H, 58, 500, 8));
        Assert.True(DockLayout.Vertical(DockEdge.Sol) && DockLayout.Vertical(DockEdge.Sag));
        Assert.False(DockLayout.Vertical(DockEdge.Alt) || DockLayout.Vertical(DockEdge.Ust));
    }

    [Fact]
    public void PopupOpensInward()
    {
        // Düğme 50x46; panel 480x170, aralık 10
        Assert.Equal((775.0, 830.0), DockLayout.Popup(DockEdge.Alt, 990, 1010, 50, 46, 480, 170, 10, X, Y, W, H));
        Assert.Equal((775.0, 1066.0), DockLayout.Popup(DockEdge.Ust, 990, 1010, 50, 46, 480, 170, 10, X, Y, W, H));
        Assert.Equal((70.0, 599.0), DockLayout.Popup(DockEdge.Sol, 10, 661, 50, 46, 480, 170, 10, X, Y, W, H));   // sağa açılır
        Assert.Equal((2010.0, 599.0), DockLayout.Popup(DockEdge.Sag, 2500, 661, 50, 46, 480, 170, 10, X, Y, W, H)); // sola açılır
    }

    [Fact]
    public void PopupStaysOnScreen()
    {
        Assert.Equal(4.0, DockLayout.Popup(DockEdge.Alt, 0, 1380, 50, 46, 480, 170, 10, X, Y, W, H).X);
        Assert.Equal(H - 170 - 4, DockLayout.Popup(DockEdge.Sol, 10, 1400, 50, 30, 480, 170, 10, X, Y, W, H).Y);
    }

    [Fact]
    public void ZoomGrowsInward()
    {
        Assert.Equal((0.5, 0.75), DockLayout.ZoomOrigin(DockEdge.Alt));
        Assert.Equal((0.25, 0.5), DockLayout.ZoomOrigin(DockEdge.Sol));
        Assert.Equal((0.75, 0.5), DockLayout.ZoomOrigin(DockEdge.Sag));
    }
}

public class EverythingIpcTests
{
    [Fact]
    public void SearchAddedOnceAndRemovalKept()
    {
        var s = new Settings { Version = 3, Widgets = new() { "start", "apps", "clock" } };
        s.Migrate();
        Assert.Equal(new[] { "start", "search", "apps", "warp", "clock" }, s.Widgets);
        s.Widgets.Remove("search"); s.Migrate();   // kullanıcı kapatırsa geri gelmez
        Assert.DoesNotContain("search", s.Widgets);
    }

    [Fact]
    public void QueryLayout()
    {
        var b = EverythingIpc.BuildQuery(0x1234, 7, "ab", 20);
        Assert.Equal(20 + 6, b.Length);
        Assert.Equal(0x1234u, BitConverter.ToUInt32(b, 0));
        Assert.Equal(7u, BitConverter.ToUInt32(b, 4));
        Assert.Equal(20u, BitConverter.ToUInt32(b, 16));
        Assert.Equal("ab\0", System.Text.Encoding.Unicode.GetString(b, 20, 6));
    }

    // Everything'in döndüreceği EVERYTHING_IPC_LISTW yanıtını taklit eder
    static byte[] List(params (int flags, string name, string path)[] items)
    {
        var strings = new List<byte>();
        int baseOff = 28 + items.Length * 12;
        var head = new List<byte>();
        foreach (var v in new uint[] { 1, 1, 2, 1, 1, (uint)items.Length, 0 }) head.AddRange(BitConverter.GetBytes(v));
        foreach (var (f, n, p) in items)
        {
            uint no = (uint)(baseOff + strings.Count); strings.AddRange(System.Text.Encoding.Unicode.GetBytes(n + "\0"));
            uint po = (uint)(baseOff + strings.Count); strings.AddRange(System.Text.Encoding.Unicode.GetBytes(p + "\0"));
            head.AddRange(BitConverter.GetBytes((uint)f)); head.AddRange(BitConverter.GetBytes(no)); head.AddRange(BitConverter.GetBytes(po));
        }
        head.AddRange(strings);
        return head.ToArray();
    }

    [Fact]
    public void ParsesFilesAndFolders()
    {
        var hits = EverythingIpc.ParseList(List((0, "rapor.xlsx", @"C:\Users\Kutay\Desktop"), (1, "Ai projeler", @"C:\Users\Kutay\Desktop")));
        Assert.Equal(2, hits.Count);
        Assert.Equal(@"C:\Users\Kutay\Desktop\rapor.xlsx", hits[0].FullPath);
        Assert.False(hits[0].IsFolder);
        Assert.True(hits[1].IsFolder);
    }

    [Fact]
    public void BrokenDataIsEmpty()
    {
        Assert.Empty(EverythingIpc.ParseList(new byte[5]));
        var bad = List((0, "x", "y")); BitConverter.GetBytes(9999u).CopyTo(bad, 32); // ad ofseti dışarıda
        Assert.Empty(EverythingIpc.ParseList(bad));
    }
}

public class ThumbTests
{
    [Fact]
    public void FitKeepsAspectAndCenters()
    {
        Assert.Equal((10.0, 9.75, 200.0, 112.5), Thumb.Fit(10, 0, 200, 132, 2560, 1440)); // yatay pencere
        var t = Thumb.Fit(0, 0, 200, 132, 1000, 2000); // dikey pencere
        Assert.Equal(66, t.W); Assert.Equal(132, t.H); Assert.Equal(67, t.X);
        Assert.Equal((0.0, 0.0, 200.0, 132.0), Thumb.Fit(0, 0, 200, 132, 0, 0));
    }
}

public class AlertTests
{
    static readonly DateTimeOffset Noon = new(2026, 11, 16, 12, 0, 0, TimeSpan.FromHours(3));
    static ManualLimit L(double p) => new() { Percent = p, ResetsAt = Noon.AddDays(1) };

    [Fact]
    public void LimitAndBatteryOnce()
    {
        var s = new Settings { QuietHoursEnabled = false, AiAlertPercent = 80, BatteryAlertPercent = 15 };
        var eng = new AlertEngine();
        var a = eng.Evaluate(s, new AlertInput(Noon, L(90), L(50), 12, false));
        Assert.Equal(2, a.Count);
        Assert.Contains(a, x => x.Title.Contains("Grok Bot"));
        Assert.Contains(a, x => x.Title == "Pil azaldı");
        Assert.Empty(eng.Evaluate(s, new AlertInput(Noon.AddMinutes(1), L(91), L(50), 12, false)));
        Assert.Empty(new AlertEngine().Evaluate(s, new AlertInput(Noon, null, null, 12, true)));
    }

    [Fact]
    public void NoAlertWithoutSourceOrAfterReset()
    {
        var s = new Settings { QuietHoursEnabled = false };
        Assert.Empty(new AlertEngine().Evaluate(s, new AlertInput(Noon, new ManualLimit(), new ManualLimit(), null, false)));
        var old = new ManualLimit { Percent = 99, ResetsAt = Noon.AddHours(-1) };
        Assert.Empty(new AlertEngine().Evaluate(s, new AlertInput(Noon, old, null, null, false)));
    }

    [Fact]
    public void QuietHoursHoldThenRelease()
    {
        var s = new Settings { QuietStartHour = 23, QuietEndHour = 8, AiAlertPercent = 80 };
        var night = new DateTimeOffset(2026, 11, 16, 2, 0, 0, TimeSpan.FromHours(3)).ToLocalTime();
        Assert.True(AlertEngine.InQuietHours(s, night));
        Assert.False(AlertEngine.InQuietHours(s, Noon));
        var eng = new AlertEngine();
        Assert.Empty(eng.Evaluate(s, new AlertInput(night, null, L(95), null, false)));
        var morning = eng.Evaluate(s, new AlertInput(Noon, null, L(95), null, false));
        Assert.Contains("Cursor", Assert.Single(morning).Title);
    }
}

public class MigrationTests
{
    [Fact]
    public void OldSettingsLoseRemovedWidgetsAndGainNewOnes()
    {
        var p = Path.Combine(Path.GetTempPath(), "kd-mig-" + Guid.NewGuid() + ".json");
        File.WriteAllText(p, "{\"Style\":\"Siyah\",\"AiSource\":\"ClaudeCode\",\"IcsUrls\":[\"x\"],\"Widgets\":[\"start\",\"apps\",\"ai\",\"media\",\"volume\",\"screentime\",\"calendar\",\"battery\",\"clock\"]}");
        var s = Settings.Load(p);
        Assert.Equal(DockStyle.Siyah, s.Style);
        Assert.Equal(new[] { "start", "search", "apps", "ai", "volume", "storage", "screenshot", "battery", "warp", "clock" }, s.Widgets);
        Assert.Equal(5, s.Version);
        // 1.1 dosyası tekrar taşınmaz: kullanıcı Depolama'yı kapattıysa geri gelmez
        s.Widgets.Remove("storage"); s.Save(p);
        Assert.DoesNotContain("storage", Settings.Load(p).Widgets);
        File.Delete(p);
    }

    [Fact]
    public void FreshDefaultsHaveNewWidgets()
    {
        var s = Settings.Load(Path.Combine(Path.GetTempPath(), "kd-yok-" + Guid.NewGuid() + ".json"));
        Assert.Contains("storage", s.Widgets); Assert.Contains("screenshot", s.Widgets);
        Assert.DoesNotContain("media", s.Widgets); Assert.DoesNotContain("calendar", s.Widgets); Assert.DoesNotContain("screentime", s.Widgets);
    }
}

public class WeatherTests
{
    [Fact]
    public void ParsesOpenMeteo()
    {
        var json = "{\"current\":{\"temperature_2m\":24.3,\"weather_code\":0,\"is_day\":1},\"daily\":{\"temperature_2m_max\":[26.1],\"temperature_2m_min\":[15.0]}}";
        var w = Weather.Parse(json);
        Assert.Equal(24.3, w.TempC); Assert.True(w.IsDay);
        Assert.Equal("Açık", Weather.Describe(w.Code, w.IsDay).Text);
        Assert.Contains("latitude=41.01", Weather.Url(41.01, 28.97));
    }

    [Fact]
    public void SettingsRoundTrip()
    {
        var p = Path.Combine(Path.GetTempPath(), "kd-test-" + Guid.NewGuid() + ".json");
        var s = new Settings { Style = DockStyle.Gece, AutoHideWindowsTaskbar = false };
        s.Save(p);
        var l = Settings.Load(p);
        Assert.Equal(DockStyle.Gece, l.Style);
        Assert.False(new Settings().AutoHideWindowsTaskbar); // varsayılan KAPALI
        File.Delete(p);
        Assert.Equal(DockStyle.Cam, Settings.Load(p).Style);
    }
}

public class SettingsListTests
{
    [Fact]
    public void WidgetListIsReplacedNotAppended()
    {
        var s = new Settings { Widgets = new() { "clock", "apps" } };
        var copy = Settings.Deserialize<Settings>(Settings.Serialize(s))!;
        Assert.Equal(new[] { "clock", "apps" }, copy.Widgets);
    }
}

public class V15Tests
{
    [Fact]
    public void MagnifyCosineBump()
    {
        Assert.Equal(40.0 / 28, Magnify.Scale(0, 28, 40, 1), 6);
        Assert.Equal(1, Magnify.Scale(Magnify.Range(28), 28, 40, 1));
        Assert.Equal(1, Magnify.Scale(5, 28, 40, 0));
        double mid = Magnify.Scale(Magnify.Range(28) / 2, 28, 40, 1);
        Assert.Equal(1 + (40.0 / 28 - 1) / 2, mid, 6);
        Assert.True(Magnify.Scale(10, 28, 40, 1) > Magnify.Scale(20, 28, 40, 1));
    }

    [Fact]
    public void ApproachConvergesAndSnaps()
    {
        double v = 1;
        for (int i = 0; i < 200; i++) v = Magnify.Approach(v, 1.4, 1 / 60.0, Magnify.TauGrow);
        Assert.Equal(1.4, v);
        Assert.True(Magnify.Approach(1, 2, 0.01, 0.045) < 2);
    }

    [Fact]
    public void LaunchBounceIsParabolicAndStopsAfterLanding()
    {
        var b = new Bouncer();
        b.Start(BounceKind.Launch, 0);
        Assert.Equal(0, b.Offset(0), 6);
        Assert.Equal(0.5, b.Offset(0.31), 3);          // tepe: periyodun yarısı, yükseklik 0,5
        b.Stop(0.4);                                    // o anki sıçrama iner, sonra durur
        Assert.True(b.IsActive);
        Assert.True(b.Offset(0.5) > 0);
        Assert.Equal(0, b.Offset(0.7));
        Assert.False(b.IsActive);
    }

    [Fact]
    public void LaunchBounceTimesOut()
    {
        var b = new Bouncer();
        b.Start(BounceKind.Launch, 0);
        b.Offset(12.5); b.Offset(13.2);
        Assert.False(b.IsActive);
    }

    [Fact]
    public void FolderAggregateGroupsByChild()
    {
        var files = new[]
        {
            (@"C:\Games\A\bin", "x.pak", 300L), (@"C:\Games\A", "y.pak", 200L), (@"C:\Games\B", "z", 100L),
            (@"C:\Games", "kok.iso", 50L), (@"C:\Other", "w", 999L), (@"C:\Games\B", "neg", -1L),
        };
        var r = FolderSizes.Aggregate(@"C:\Games\", files);
        Assert.Equal(new[] { "A", "B", "kok.iso" }, r.Select(e => e.Name));
        Assert.Equal(500, r[0].Bytes); Assert.Equal(2, r[0].Files); Assert.True(r[0].IsFolder);
        Assert.Equal(@"C:\Games\A", r[0].FullPath);
        Assert.False(r[2].IsFolder);
    }

    [Fact]
    public void FolderQueryAndParent()
    {
        Assert.Equal("file: \"C:\\\" size:>1024", FolderSizes.Query(@"C:\", 1024));
        Assert.Equal(@"C:\", FolderSizes.Parent(@"C:\Users"));
        Assert.Equal(@"C:\Users", FolderSizes.Parent(@"C:\Users\Kutay\"));
        Assert.Null(FolderSizes.Parent(@"C:\"));
    }

    static byte[] List2(params (uint flags, string name, string path, long size)[] items)
    {
        var data = new List<byte>();
        var offs = new List<int>();
        int header = 20 + items.Length * 8;
        foreach (var it in items)
        {
            offs.Add(header + data.Count);
            foreach (var t in new[] { it.name, it.path })
            {
                data.AddRange(BitConverter.GetBytes((uint)t.Length));
                data.AddRange(System.Text.Encoding.Unicode.GetBytes(t + "\0"));
            }
            data.AddRange(BitConverter.GetBytes(it.size));
        }
        var b = new List<byte>();
        foreach (var v in new uint[] { (uint)items.Length, (uint)items.Length, 0, EverythingIpc.RequestName | EverythingIpc.RequestPath | EverythingIpc.RequestSize, EverythingIpc.SortSizeDescending })
            b.AddRange(BitConverter.GetBytes(v));
        for (int i = 0; i < items.Length; i++) { b.AddRange(BitConverter.GetBytes(items[i].flags)); b.AddRange(BitConverter.GetBytes((uint)offs[i])); }
        b.AddRange(data);
        return b.ToArray();
    }

    [Fact]
    public void ParsesList2WithSizes()
    {
        var r = EverythingIpc.ParseList2(List2((1, "Games", @"D:\", 5_000_000_000), (0, "a.iso", @"D:\Games", 123)));
        Assert.Equal(2, r.Count);
        Assert.Equal(("Games", @"D:\", 5_000_000_000L, true), r[0]);
        Assert.Equal(("a.iso", @"D:\Games", 123L, false), r[1]);
        Assert.Empty(EverythingIpc.ParseList2(new byte[3]));
        var broken = List2((0, "x", "y", 1)); Array.Resize(ref broken, broken.Length - 10);
        Assert.Empty(EverythingIpc.ParseList2(broken));
    }

    [Fact]
    public void BuildsQuery2Header()
    {
        var b = EverythingIpc.BuildQuery2(0x1234, 77, "abc", 500, 0x13, 6);
        Assert.Equal(0x1234u, BitConverter.ToUInt32(b, 0));
        Assert.Equal(77u, BitConverter.ToUInt32(b, 4));
        Assert.Equal(500u, BitConverter.ToUInt32(b, 16));
        Assert.Equal(0x13u, BitConverter.ToUInt32(b, 20));
        Assert.Equal(6u, BitConverter.ToUInt32(b, 24));
        Assert.Equal("abc\0", System.Text.Encoding.Unicode.GetString(b, 28, b.Length - 28));
    }

    [Fact]
    public void OptimizeNeverTouchesGamesDriversOrSystem()
    {
        Assert.True(OptimizeRules.IsGame("Marvel-Win64-Shipping", null));
        Assert.True(OptimizeRules.IsGame("game", @"D:\SteamLibrary\steamapps\common\X\game.exe"));
        Assert.False(OptimizeRules.IsGame("chrome", @"C:\Program Files\Google\Chrome\chrome.exe"));
        foreach (var p in new[] { "steam", "steamwebhelper", "EasyAntiCheat", "BEService", "vgc", "nvcontainer", "NVDisplay.Container", "amdrsserv", "csrss", "dwm", "audiodg" })
            Assert.True(OptimizeRules.IsProtected(p, null), p);
        Assert.False(OptimizeRules.IsProtected("chrome", null));
        Assert.False(OptimizeRules.IsProtected("Discord", null));
    }

    [Fact]
    public void ParsesWarpStatus()
    {
        Assert.Equal(WarpState.Connected, Warp.Parse("Status update: Connected\nNetwork: healthy"));
        Assert.Equal(WarpState.Disconnected, Warp.Parse("Status update: Disconnected\nReason: Manual Disconnection"));
        Assert.Equal(WarpState.Connecting, Warp.Parse("Status update: Connecting"));
        Assert.Equal(WarpState.Unknown, Warp.Parse(null));
        Assert.Equal(WarpState.Unknown, Warp.Parse("Error: daemon not running"));
        Assert.Equal("Bağlı", Warp.Label(WarpState.Connected));
    }

    [Fact]
    public void WindowsLikeSearchRanking()
    {
        Assert.Equal(100, WinSearch.Score("Discord", "discord"));
        Assert.Equal(80, WinSearch.Score("Discord", "dis"));
        Assert.Equal(60, WinSearch.Score("Visual Studio Code", "stu"));
        Assert.Equal(40, WinSearch.Score("Notepad", "pad"));
        Assert.Equal(0, WinSearch.Score("Notepad", "xyz"));
        var r = WinSearch.Rank(new[] { "Spotify", "Steam", "Ses", "Paint" }, x => x, "s", 3);
        Assert.Equal(new[] { "Ses", "Steam", "Spotify" }, r);
        Assert.Contains(WinSearch.Rank(WinSearch.SettingsPages, p => p.Name, "bluetooth", 1), p => p.Uri.StartsWith("ms-settings:"));
    }

    [Fact]
    public void IndexSqlIsEscaped()
    {
        var sql = WinSearch.IndexSql("a'b%c", 8);
        Assert.Contains("TOP 8", sql);
        Assert.Contains("LIKE '%a''bc%'", sql);
        Assert.DoesNotContain("a'b", sql.Replace("a''b", ""));
    }

    [Fact]
    public void V5MigrationAddsWarpAndTaskbarRemoval()
    {
        var s = new Settings { Version = 4, AutoHideWindowsTaskbar = true, Widgets = new() { "start", "search", "network", "clock" } };
        s.Migrate();
        Assert.Equal(new[] { "start", "search", "network", "warp", "clock" }, s.Widgets);
        Assert.True(s.RemoveWindowsTaskbar);
        s.Widgets.Remove("warp"); s.RemoveWindowsTaskbar = false; s.Migrate();
        Assert.DoesNotContain("warp", s.Widgets); Assert.False(s.RemoveWindowsTaskbar);
        var f = new Settings().Migrate();
        Assert.Contains("warp", f.Widgets); Assert.False(f.RemoveWindowsTaskbar);
        Assert.True(f.Magnification); Assert.True(f.Animations); Assert.False(f.SearchWithEverything);
    }
}
