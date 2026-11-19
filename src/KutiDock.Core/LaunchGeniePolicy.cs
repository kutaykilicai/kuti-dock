namespace KutiDock.Core;

/// <summary>
/// 1.6.6: dock'tan ilk kez başlatılan uygulamanın (PWA, normal uygulama, dosya, klasör) ilk penceresi de Genie ile dock
/// simgesinden büyüyerek açılır. Karar kuralları burada (test edilebilir); pencere kancası KutiDock'taki LaunchGenie'de.
/// </summary>
public static class LaunchGeniePolicy
{
    /// <summary>Başlatmadan sonra ilk pencere bu sürede görünmezse normal açılışa bırakılır.</summary>
    public const int WindowTimeoutMs = 5000;
    /// <summary>Pencere bulunduktan sonra ilk dolu karenin beklenmesi için üst sınır; aşılırsa pencere normal gösterilir.</summary>
    public const int PaintTimeoutMs = 2000;
    public const int PollMs = 70;

    static readonly HashSet<string> Blocked = new(StringComparer.OrdinalIgnoreCase)
    {
        // kabuk, UAC ve güvenli masaüstü pencereleri
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow",
        "XamlExplorerHostIslandWindow", "Credential Dialog Xaml Host", "$$$Secure UAP Dummy Window Class For Interim Dialog",
        "#32768", "tooltips_class32", "Chrome_WidgetWin_2",
    };

    static readonly HashSet<string> BlockedProcesses = new(StringComparer.OrdinalIgnoreCase)
    { "consent", "credentialuibroker", "LogonUI", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "LockApp" };

    /// <summary>İzleme başlasın mı: animasyonlar açık, ön planda tam ekran (oyun/video) yok.</summary>
    public static bool Watch(bool animations, bool fullscreen) => animations && !fullscreen;

    /// <summary>Anahtar bir exe ya da uygulama kimliği değilse (dosya/klasör) ilk yeni ana pencere kabul edilir.</summary>
    public static bool AcceptsAny(string expectedKey) =>
        !(expectedKey.StartsWith("crx:", StringComparison.Ordinal) || expectedKey.StartsWith("aumid:", StringComparison.Ordinal)
          || expectedKey.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

    public static bool Matches(string expectedKey, string windowKey) =>
        AcceptsAny(expectedKey) || AppGrouping.SameKey(expectedKey, windowKey);

    static readonly HashSet<string> Uac = new(StringComparer.OrdinalIgnoreCase)
    { "Credential Dialog Xaml Host", "$$$Secure UAP Dummy Window Class For Interim Dialog" };

    public static bool IsUac(string className, string processName) =>
        Uac.Contains(className) || processName.Equals("consent", StringComparison.OrdinalIgnoreCase)
        || processName.Equals("credentialuibroker", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 1.6.7: sistem geneli kancada ucuz ilk eleme (süreç sorgusu yok): araç ipucu, açılır menü (#32768), sahipli iletişim
    /// kutusu, çerçevesiz ya da küçük pencere ana pencere sayılmaz.
    /// </summary>
    public static bool MainWindowShape(bool toolOrOwned, bool hasFrame, int width, int height, string className) =>
        !toolOrOwned && hasFrame && width >= 200 && height >= 120 && !Blocked.Contains(className);

    /// <summary>Yeni görünen pencere bir uygulamanın ana penceresi olabilir mi (araç/sahipli/küçük/UAC/oyun değil).</summary>
    public static bool Candidate(bool ownProcess, bool toolOrOwned, bool hasFrame, int width, int height,
        string className, string processName, bool game) =>
        !ownProcess && !toolOrOwned && hasFrame && !game && width >= 200 && height >= 120
        && !Blocked.Contains(className) && !BlockedProcesses.Contains(processName);

    /// <summary>1.6.19: bir süreç açılışında ya da aynı uygulamanın birkaç saniye içindeki ikinci penceresinde tekrar oynatma.</summary>
    public const int PerAppCooldownMs = 4000;
    public const int PerProcessMs = 12 * 60 * 60 * 1000; // aynı süreç (tepsiye inip saatler sonra açılma); pid bu sürede yenilenmez

    public readonly record struct LaunchPlay(string Key, int ProcessId, DateTime AtUtc);

    public static bool AlreadyPlayed(IEnumerable<LaunchPlay> recent, string key, int processId, DateTime nowUtc)
    {
        foreach (var p in recent)
        {
            if (p.ProcessId == processId && nowUtc - p.AtUtc < TimeSpan.FromMilliseconds(PerProcessMs)) return true;
            if (AppGrouping.SameKey(p.Key, key) && nowUtc - p.AtUtc < TimeSpan.FromMilliseconds(PerAppCooldownMs)) return true;
        }
        return false;
    }
}

/// <summary>Yakalanan karede gerçek içerik var mı: tamamen siyah ya da tek renk (henüz çizilmemiş) kare Genie'ye verilmez.</summary>
public static class FrameCheck
{
    public static bool HasDetail(uint[] pixels, int width, int height, int minColors = 6)
    {
        if (width <= 0 || height <= 0 || pixels.Length < width * height) return false;
        var seen = new HashSet<uint>();
        const int grid = 24;
        for (int gy = 0; gy < grid; gy++)
        for (int gx = 0; gx < grid; gx++)
        {
            int x = (int)((gx + 0.5) * width / grid), y = (int)((gy + 0.5) * height / grid);
            uint p = pixels[y * width + x] & 0x00FFFFFF;
            // 8 düzeyli kova: yumuşak geçişli tek renk arka plan "içerik" sayılmasın
            seen.Add(p & 0x00E0E0E0);
            if (seen.Count >= minColors) return true;
        }
        return false;
    }
}
