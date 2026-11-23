namespace KutiDock.Core;

/// <summary>Dock simgesine tıklanınca ne olacağı.</summary>
public enum AppClick { Launch, Picker, MinimizeFront, Restore }

/// <summary>
/// 1.6.25: bir uygulamanın birden çok penceresi varken (iki Gezgin klasörü gibi) simgeye tıklamak pencere seçiciyi açar.
/// Önceden pencerelerden biri öndeyse tıklama onu küçültüyor, dock'tan küçültülmüş biri varsa onu geri getiriyordu; istenen
/// pencereye ancak üzerinde bekleyip önizlemeyi yakalayarak ulaşılıyordu. Tek pencerede davranış aynı.
/// </summary>
public static class WindowPicker
{
    /// <summary>Tıklama kararı: görünür pencere sayısı ve bunlardan birinin önde olup olmadığı.</summary>
    public static AppClick Decide(int visibleWindows, bool oneIsInFront) =>
        visibleWindows <= 0 ? AppClick.Launch
        : visibleWindows > 1 ? AppClick.Picker
        : oneIsInFront ? AppClick.MinimizeFront
        : AppClick.Restore;

    static readonly string[] Suffixes = { " - Dosya Gezgini", " - Windows Gezgini", " - File Explorer" };

    /// <summary>Seçicideki kart başlığı: Gezgin penceresinde yalnızca klasör adı.</summary>
    public static string CardTitle(string? title)
    {
        var t = (title ?? "").Trim();
        foreach (var s in Suffixes)
            if (t.Length > s.Length && t.EndsWith(s, StringComparison.OrdinalIgnoreCase)) return t[..^s.Length].TrimEnd();
        return t;
    }

    /// <summary>Simge ipucu: birden çok pencerede yok (seçici başlıkları gösterir, ipucu seçicinin üstüne binmesin).</summary>
    public static string? Tooltip(int windows, string? firstTitle, string? name) =>
        windows > 1 ? null : windows == 1 ? firstTitle : name;
}
