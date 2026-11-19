namespace KutiDock.Core;

/// <summary>
/// 1.6.22: Genie için alınan pencere görüntülerinin piksel dizileri yeniden kullanılır. Öndeki pencere 2,5 sn'de bir tam
/// çözünürlükte yakalanıyor ve her seferinde yeni bir dizi ayrılıyordu (2560x1440 pencerede ~14 MB, büyük nesne yığını);
/// eski diziler ancak tam GC'de bırakıldığı için dock'un özel belleği 380 MB'tan 550 MB'a çıkıyordu.
/// Artık kullanılmadan eskiyen görüntünün dizisi yedek olarak tutulur ve bir sonraki yakalama, boyut aynıysa ona yazar.
/// Kullanılan (Genie'ye ya da geri dönüş önbelleğine giden) dizi yedeğe hiç girmez, üstüne yazılmaz.
/// </summary>
public sealed class CaptureBuffer
{
    uint[]? _spare;

    /// <summary>Yedek diziyi alır (yakalama bitene kadar yalnızca alan kullanır).</summary>
    public uint[]? Take()
    {
        var s = _spare;
        _spare = null;
        return s;
    }

    /// <summary>Artık kimsenin kullanmadığı diziyi yedeğe koyar (tek yedek).</summary>
    public void Give(uint[]? pixels)
    {
        if (pixels != null) _spare = pixels;
    }

    /// <summary>Boyu tutan dizi yeniden kullanılır, tutmayan bırakılır ve yenisi ayrılır.</summary>
    public static uint[] Fit(uint[]? buffer, int length) => buffer != null && buffer.Length == length ? buffer : new uint[length];
}
