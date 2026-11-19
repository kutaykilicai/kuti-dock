using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace KutiDock.Core;

/// <summary>
/// 1.6.19: WPF çizim motoru (UCEERR_RENDERTHREADFAILURE, 0x88980406) veya DXGI aygıt kaybı sonrası
/// dock'un kendini yeniden başlatması. Kayıt süreçler arası dosyada tutulur: en fazla 2 dakikada bir
/// ve saatte üç kez, böylece döngü oluşmaz.
/// </summary>
public static class RenderRestartPolicy
{
    public const int UceRenderThreadFailure = unchecked((int)0x88980406);
    public const int DxgiDeviceRemoved = unchecked((int)0x887A0005);
    public const int DxgiDeviceReset = unchecked((int)0x887A0007);

    public static readonly TimeSpan MinGap = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan Window = TimeSpan.FromHours(1);
    public const int MaxPerHour = 3;

    public static bool IsRenderFailure(int hresult) =>
        hresult == UceRenderThreadFailure || hresult == DxgiDeviceRemoved || hresult == DxgiDeviceReset;

    public static bool IsRenderFailure(Exception? ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
            if (IsRenderFailure(e.HResult)) return true;
        return false;
    }

    public static IReadOnlyList<DateTime> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<DateTime>();
        try
        {
            var raw = JsonSerializer.Deserialize<string[]>(json);
            if (raw == null) return Array.Empty<DateTime>();
            var list = new List<DateTime>();
            foreach (var s in raw)
                if (DateTime.TryParse(s, null, DateTimeStyles.RoundtripKind, out var t))
                    list.Add(t.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(t, DateTimeKind.Utc) : t.ToUniversalTime());
            return list;
        }
        catch (JsonException) { return Array.Empty<DateTime>(); }
    }

    public static string Serialize(IReadOnlyList<DateTime> times) =>
        JsonSerializer.Serialize(times.Select(t => t.ToUniversalTime().ToString("o")).ToArray());

    public static List<DateTime> Prune(IEnumerable<DateTime> previous, DateTime nowUtc) =>
        previous.Select(t => t.ToUniversalTime()).Where(t => nowUtc - t < Window).ToList();

    public static bool ShouldRestart(IEnumerable<DateTime> previous, DateTime nowUtc)
    {
        var recent = Prune(previous, nowUtc);
        if (recent.Count >= MaxPerHour) return false;
        if (recent.Count > 0 && nowUtc - recent.Max() < MinGap) return false;
        return true;
    }

    public static List<DateTime> Record(IEnumerable<DateTime> previous, DateTime nowUtc)
    {
        var next = Prune(previous, nowUtc);
        next.Add(nowUtc.ToUniversalTime());
        return next;
    }
}
