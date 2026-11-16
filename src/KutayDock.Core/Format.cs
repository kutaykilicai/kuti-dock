using System.Globalization;

namespace KutayDock.Core;

public static class Format
{
    public static readonly CultureInfo Tr = new("tr-TR");

    /// <summary>"2s 40dk", "45dk", "3g 4s", "<1dk".</summary>
    public static string Duration(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        if (t.TotalMinutes < 1) return "<1dk";
        if (t.TotalHours < 1) return $"{(int)t.TotalMinutes}dk";
        if (t.TotalDays < 1) return t.Minutes == 0 ? $"{(int)t.TotalHours}s" : $"{(int)t.TotalHours}s {t.Minutes}dk";
        return t.Hours == 0 ? $"{(int)t.TotalDays}g" : $"{(int)t.TotalDays}g {t.Hours}s";
    }

    /// <summary>Saniyedeki bayt → "820 KB/s", "1,2 MB/s".</summary>
    public static string Rate(double bytesPerSec)
    {
        if (bytesPerSec < 0) bytesPerSec = 0;
        if (bytesPerSec < 1024) return $"{bytesPerSec:0} B/s";
        if (bytesPerSec < 1024 * 1024) return (bytesPerSec / 1024).ToString("0", Tr) + " KB/s";
        if (bytesPerSec < 1024d * 1024 * 1024) return (bytesPerSec / 1024 / 1024).ToString("0.0", Tr) + " MB/s";
        return (bytesPerSec / 1024 / 1024 / 1024).ToString("0.00", Tr) + " GB/s";
    }

    /// <summary>Bayt → "245 GB", "1,8 TB".</summary>
    public static string Bytes(long b)
    {
        double v = b; string[] u = { "B", "KB", "MB", "GB", "TB" }; int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return (i >= 3 && v < 10 ? v.ToString("0.0", Tr) : v.ToString("0", Tr)) + " " + u[i];
    }

    public static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

    /// <summary>Kernel/user/idle tik farklarından CPU yüzdesi (kernel idle'ı içerir).</summary>
    public static double CpuPercent(ulong idle0, ulong kernel0, ulong user0, ulong idle1, ulong kernel1, ulong user1)
    {
        ulong idle = idle1 - idle0, total = (kernel1 - kernel0) + (user1 - user0);
        if (total == 0) return 0;
        return Math.Round(100.0 * (total - Math.Min(idle, total)) / total, 1);
    }
}
