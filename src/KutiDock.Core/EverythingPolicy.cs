using System.Globalization;

namespace KutiDock.Core;

/// <summary>Dock'un kendi başlattığı Everything süreci (dock yeniden başlasa da hatırlanır: everything-dock.txt).</summary>
public sealed record OwnedEverything(int Pid, DateTime StartUtc)
{
    public string Serialize() => Pid.ToString(CultureInfo.InvariantCulture) + "|" + StartUtc.Ticks.ToString(CultureInfo.InvariantCulture);

    public static OwnedEverything? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Trim().Split('|');
        if (parts.Length != 2) return null;
        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid) || pid <= 0) return null;
        if (!long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks) || ticks <= 0) return null;
        return new OwnedEverything(pid, new DateTime(ticks, DateTimeKind.Utc));
    }
}

/// <summary>
/// 1.6.14: aramadaki Everything anahtarının kararları. Açıkken Everything pencere açmadan arka planda çalışır (dock
/// açılışında da); veritabanı yüklenene kadar beklenir ve arama kendiliğinden yenilenir. Kapatılınca yalnızca dock'un
/// başlattığı süreç "-exit" ile kapanır; Kutay'ın kendi açtığı ya da penceresi açık Everything'e dokunulmaz.
/// </summary>
public static class EverythingPolicy
{
    public const string OwnerFile = "everything-dock.txt";
    public const int WmUser = 0x400, IpcIsDbLoaded = 401;   // everything_ipc.h: EVERYTHING_IPC_IS_DB_LOADED

    /// <summary>Başlattıktan sonra IPC penceresi için en çok bekleme.</summary>
    public static readonly TimeSpan WindowWait = TimeSpan.FromSeconds(8);
    /// <summary>Veritabanının yüklenmesi için en çok bekleme.</summary>
    public static readonly TimeSpan DbWait = TimeSpan.FromSeconds(20);
    /// <summary>Veritabanı hazır değilken aramanın yeniden deneneceği aralıklar (ms).</summary>
    public static readonly int[] RetryDelaysMs = { 400, 800, 1500, 3000, 5000 };

    /// <summary>Dock açılışında Everything başlatılsın mı: yalnızca anahtar açıksa.</summary>
    public static bool StartWithDock(bool searchWithEverything) => searchWithEverything;

    /// <summary>
    /// Anahtar kapatılınca çalışan Everything kapatılsın mı: dock başlatmış olmalı (aynı PID ve başlangıç zamanı, ±2 sn;
    /// PID yeniden kullanılmışsa başka süreç sayılır) ve Everything penceresi açık olmamalı (Kutay kullanıyor).
    /// </summary>
    public static bool ShouldExit(OwnedEverything? owned, int? runningPid, DateTime? runningStartUtc, bool mainWindowVisible)
    {
        if (owned == null || runningPid == null || runningStartUtc == null) return false;
        if (runningPid.Value != owned.Pid) return false;
        if (Math.Abs((runningStartUtc.Value - owned.StartUtc).TotalSeconds) > 2) return false;
        return !mainWindowVisible;
    }

    /// <summary>Arama sonucu yokken veritabanı yükleniyorsa yeniden denensin mi (deneme sayısı aralık listesini aşmadıkça).</summary>
    public static bool ShouldRetry(bool dbLoaded, int hitCount, int attempt) =>
        !dbLoaded && hitCount == 0 && attempt < RetryDelaysMs.Length;
}
