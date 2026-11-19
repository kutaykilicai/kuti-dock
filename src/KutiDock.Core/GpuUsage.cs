namespace KutiDock.Core;

/// <summary>
/// 1.6.21: GPU kullanımı "GPU Engine\Utilization Percentage" sayaçlarından, kategori tek seferde okunarak hesaplanır.
/// Kasa'da 176 adet 3D motor örneği var; her biri için ayrı PerformanceCounter.NextValue her okumada ~156 ms CPU
/// harcıyordu (2 sn'de bir, dock gizliyken de), örnek listesi de 15 sn'de bir ~4,5 sn sürüyordu. Sayaç türü
/// Timer100Ns: yüzde = 100 × Δham / Δzaman (100 ns). CounterSample.Calculate ile aynı sonucu verir (Kasa'da ölçüldü).
/// </summary>
public static class GpuUsage
{
    public static bool Is3D(string instance) => instance.EndsWith("engtype_3D", StringComparison.OrdinalIgnoreCase);

    /// <summary>İki okuma arasında 3D motorların toplam kullanımı (0-100). Yalnızca iki okumada da olan örnekler sayılır.</summary>
    public static double Percent(IReadOnlyDictionary<string, (long Raw, long Time)> previous, IReadOnlyDictionary<string, (long Raw, long Time)> current)
    {
        double sum = 0;
        foreach (var (name, c) in current)
        {
            if (!Is3D(name) || !previous.TryGetValue(name, out var p)) continue;
            long dt = c.Time - p.Time, dv = c.Raw - p.Raw;
            if (dt > 0 && dv > 0) sum += 100.0 * dv / dt;
        }
        return Math.Min(100, sum);
    }
}
