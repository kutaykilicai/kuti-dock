namespace KutiDock.Core;

/// <summary>Bir süreç: kimlik, ad (uzantısız), tam yol.</summary>
public readonly record struct ProcInfo(int Pid, string Name, string? Path);

/// <summary>"Görevi sonlandır" nasıl uygulanır.</summary>
public enum KillKind
{
    /// <summary>Menüde gösterilmez (çalışmıyor ya da korumalı).</summary>
    None,
    /// <summary>Aynı exe'nin tüm süreçleri, alt süreç ağacıyla zorla kapatılır.</summary>
    KillProcesses,
    /// <summary>Chrome/Edge PWA: yalnızca pencereler WM_CLOSE ile kapatılır (tarayıcının tamamı kapanmasın).</summary>
    ClosePwaWindows,
}

/// <summary>
/// 1.6.13: dock simgesinin sağ tık menüsündeki "Görevi sonlandır" kararları (onay sormaz, donmuş uygulamada da çalışır).
/// Explorer, Kuti Dock'un kendisi, sistem süreçleri, oyunlar, oyun istemcileri ve hile önleyiciler korunur.
/// </summary>
public static class TaskKill
{
    public const string MenuText = "Görevi sonlandır";

    // Kabuk ve sistem süreçleri: kapatılırsa masaüstü/oturum bozulur. (Oyun ve hile önleyiciler OptimizeRules'ta.)
    static readonly HashSet<string> ShellAndSystem = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "kutidock", "applicationframehost", "dwm", "csrss", "winlogon", "wininit", "lsass", "services", "smss",
        "svchost", "sihost", "fontdrvhost", "ctfmon", "conhost", "runtimebroker", "shellexperiencehost", "startmenuexperiencehost",
        "searchhost", "searchapp", "textinputhost", "lockapp", "logonui", "taskhostw", "dllhost", "system", "registry", "idle",
        "securityhealthsystray", "securityhealthservice", "msmpeng", "wudfhost", "spoolsv", "userinit",
    };

    /// <summary>Bu süreç sonlandırılamaz (menüde öğe gösterilmez).</summary>
    public static bool IsProtected(string name, string? path)
    {
        var n = StripExe(name);
        if (ShellAndSystem.Contains(n)) return true;
        if (OptimizeRules.IsProtected(n, path)) return true;
        // Windows klasöründeki her şey (System32, SystemApps, ImmersiveControlPanel...) korunur
        return path != null && path.Replace('/', '\\').Contains(@":\windows\", StringComparison.OrdinalIgnoreCase);
    }

    static string StripExe(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;

    /// <summary>
    /// Simge için ne yapılacağı. <paramref name="key"/> AppGrouping anahtarı ("crx:..." PWA, "aumid:..." paketli uygulama,
    /// diğerleri exe yolu); <paramref name="owners"/> simgenin açık pencerelerinin sahibi süreçler.
    /// </summary>
    public static KillKind KindFor(string key, int windowCount, IReadOnlyCollection<ProcInfo> owners)
    {
        if (windowCount <= 0 || owners.Count == 0) return KillKind.None;
        if (key.StartsWith("crx:", StringComparison.OrdinalIgnoreCase)) return KillKind.ClosePwaWindows;
        if (owners.Any(o => IsProtected(o.Name, o.Path))) return KillKind.None;
        return KillKind.KillProcesses;
    }

    /// <summary>
    /// Sonlandırılacak kök süreçler: pencere sahipleri ve (anahtar bir exe yoluysa) aynı exe'den çalışan tüm süreçler.
    /// Korumalı olanlar hiçbir zaman listeye girmez. Alt süreçler Kill(entireProcessTree) ile gider.
    /// </summary>
    public static List<int> Targets(string key, IEnumerable<ProcInfo> owners, IEnumerable<ProcInfo> all, int selfPid)
    {
        var set = new List<int>();
        void Add(ProcInfo p)
        {
            if (p.Pid <= 4 || p.Pid == selfPid || IsProtected(p.Name, p.Path) || set.Contains(p.Pid)) return;
            set.Add(p.Pid);
        }
        foreach (var o in owners) Add(o);
        bool exeKey = !key.StartsWith("crx:", StringComparison.OrdinalIgnoreCase) && !key.StartsWith("aumid:", StringComparison.OrdinalIgnoreCase);
        if (exeKey)
            foreach (var p in all)
                if (p.Path != null && p.Path.Equals(key, StringComparison.OrdinalIgnoreCase)) Add(p);
        return set;
    }

    /// <summary>
    /// PWA penceresi WM_CLOSE'a yanıt vermediyse: penceresinin süreci yalnızca bu PWA'nın pencerelerini taşıyorsa sonlandırılır.
    /// Süreç başka (tarayıcı ya da başka PWA) pencereleri de taşıyorsa sonlandırılmaz; bütün Chrome kapanırdı.
    /// </summary>
    public static bool CanKillPwaProcess(int otherVisibleWindowsOfProcess) => otherVisibleWindowsOfProcess == 0;

    /// <summary>WM_CLOSE'tan sonra PWA penceresinin kapanması için beklenen süre.</summary>
    public static readonly TimeSpan PwaCloseWait = TimeSpan.FromSeconds(3);

    /// <summary>taskkill argümanları (yetki yetmezse yönetici olarak, pencere açmadan).</summary>
    public static string TaskkillArgs(IEnumerable<int> pids) =>
        "/F /T " + string.Join(" ", pids.Select(p => "/PID " + p));
}
