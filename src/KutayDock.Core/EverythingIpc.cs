using System.Text;

namespace KutayDock.Core;

/// <summary>Everything arama sonucu.</summary>
public sealed record SearchHit(string Name, string Folder, bool IsFolder)
{
    public string FullPath => Folder.Length == 0 ? Name : Folder.TrimEnd('\\') + "\\" + Name;
}

/// <summary>
/// Everything 1.4'ün belgelenmiş IPC'si (SDK'daki everything_ipc.h; WM_COPYDATA). DLL gerekmez: sorgu Everything'in
/// gizli "EVERYTHING_TASKBAR_NOTIFICATION" penceresine gönderilir, yanıt kendi pencerimize WM_COPYDATA ile gelir.
/// Everything'in arayüzü açılmaz, ayarlarına dokunulmaz.
/// </summary>
public static class EverythingIpc
{
    public const string WindowClass = "EVERYTHING_TASKBAR_NOTIFICATION";
    public const int CopyDataQueryW = 2;          // EVERYTHING_IPC_COPYDATAQUERYW
    const int ItemFolder = 0x1, ItemDrive = 0x2;  // EVERYTHING_IPC_FOLDER / _DRIVE

    /// <summary>EVERYTHING_IPC_QUERYW: 5 DWORD (yanıt penceresi, yanıt kimliği, bayraklar, ofset, en çok sonuç) + UTF-16 arama metni + 0.</summary>
    public static byte[] BuildQuery(uint replyHwnd, uint replyId, string search, int maxResults, uint flags = 0)
    {
        var text = Encoding.Unicode.GetBytes(search + "\0");
        var b = new byte[20 + text.Length];
        BitConverter.GetBytes(replyHwnd).CopyTo(b, 0);
        BitConverter.GetBytes(replyId).CopyTo(b, 4);
        BitConverter.GetBytes(flags).CopyTo(b, 8);
        BitConverter.GetBytes(0u).CopyTo(b, 12);
        BitConverter.GetBytes((uint)maxResults).CopyTo(b, 16);
        text.CopyTo(b, 20);
        return b;
    }

    /// <summary>
    /// EVERYTHING_IPC_LISTW: 7 DWORD başlık (toplam klasör/dosya/öğe, dönen klasör/dosya/öğe, ofset), ardından öğe başına
    /// 3 DWORD (bayrak, ad ofseti, yol ofseti). Ofsetler listenin başından bayt cinsindendir. Bozuk veride boş liste.
    /// </summary>
    public static List<SearchHit> ParseList(byte[] data)
    {
        var list = new List<SearchHit>();
        if (data.Length < 28) return list;
        uint n = BitConverter.ToUInt32(data, 20);
        for (uint i = 0; i < n; i++)
        {
            int at = 28 + (int)i * 12;
            if (at + 12 > data.Length) break;
            uint flags = BitConverter.ToUInt32(data, at);
            var name = Str(data, BitConverter.ToUInt32(data, at + 4));
            var folder = Str(data, BitConverter.ToUInt32(data, at + 8));
            if (name == null || folder == null) continue;
            list.Add(new SearchHit(name, folder, (flags & (ItemFolder | ItemDrive)) != 0));
        }
        return list;
    }

    static string? Str(byte[] d, uint offset)
    {
        if (offset >= d.Length) return null;
        int end = (int)offset;
        while (end + 1 < d.Length && (d[end] != 0 || d[end + 1] != 0)) end += 2;
        return Encoding.Unicode.GetString(d, (int)offset, end - (int)offset);
    }
}
