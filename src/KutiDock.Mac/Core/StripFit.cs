// Gravitone kaynak kodu değil; Kuti Dock kodu (MIT, © 2026 Kutay Kılıç).
namespace KutiDock.Mac.Core;

/// <summary>
/// Alt şerit: [sol widget'lar] boşluk [Mac dock] boşluk [sağ widget'lar]. Mac dock'un ihtiyacı (büyütme dahil) önce
/// ayrılır; kalan yer yetmezse widget şeritleri orantılı daralır. Sonuç asla üst üste binmez.
/// </summary>
internal static class StripFit
{
    /// <summary>İzin verilen sol/sağ genişlikler (piksel); 0 = o tarafta şerit yok ya da yer kalmadı.</summary>
    public static (double Left, double Right) Sides(double width, double need, double left, double right, double gap)
    {
        left = Math.Max(0, left); right = Math.Max(0, right);
        double gaps = (left > 0 ? gap : 0) + (right > 0 ? gap : 0);
        double room = width - Math.Max(0, need) - gaps;
        if (left + right <= 0 || room >= left + right) return (left, right);
        if (room <= 0) return (0, 0);
        double f = room / (left + right);
        return (Math.Floor(left * f), Math.Floor(right * f));
    }

    /// <summary>Mac dock penceresinin şerit içindeki aralığı (sol, sağ), verilen yan genişliklere göre.</summary>
    public static (double Start, double End) DockRange(double start, double end, double left, double right, double gap) =>
        (start + (left > 0 ? left + gap : 0), end - (right > 0 ? right + gap : 0));
}
