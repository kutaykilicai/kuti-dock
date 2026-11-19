namespace KutiDock.Core;

/// <summary>
/// 1.6.22: hata.log sınırsız büyüyordu (Kasa'da 596 KB, bir ekran kartı sıfırlanmasında tek başına 400 yığın satırı).
/// Dosya ~1 MB'ı geçince hata.log.1 olur (tek yedek, eskisinin üstüne) ve yeni satırlar boş hata.log'a yazılır.
/// </summary>
public static class RotatingLog
{
    public const long MaxBytes = 1024 * 1024;

    public static void Append(string path, string text, long maxBytes = MaxBytes)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Exists && info.Length >= maxBytes) File.Move(path, path + ".1", overwrite: true);
        }
        catch (Exception) { } // başka süreç aynı anda döndürdüyse ya da dosya kilitliyse satır yine yazılır
        File.AppendAllText(path, text);
    }
}
