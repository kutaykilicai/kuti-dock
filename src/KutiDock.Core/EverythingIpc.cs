using System.Text;

namespace KutiDock.Core;

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

    // ---- QUERY2 (boyut ve sıralama) ----
    public const int CopyDataQuery2W = 18;                 // EVERYTHING_IPC_COPYDATA_QUERY2W
    public const uint RequestName = 0x1, RequestPath = 0x2, RequestSize = 0x10;
    public const uint SortSizeDescending = 6;

    /// <summary>EVERYTHING_IPC_QUERY2: 7 DWORD (yanıt penceresi, kimlik, bayraklar, ofset, en çok, istenen alanlar, sıralama) + UTF-16 metin + 0.</summary>
    public static byte[] BuildQuery2(uint replyHwnd, uint replyId, string search, int maxResults, uint request, uint sort)
    {
        var text = Encoding.Unicode.GetBytes(search + "\0");
        var b = new byte[28 + text.Length];
        BitConverter.GetBytes(replyHwnd).CopyTo(b, 0);
        BitConverter.GetBytes(replyId).CopyTo(b, 4);
        BitConverter.GetBytes((uint)maxResults).CopyTo(b, 16);
        BitConverter.GetBytes(request).CopyTo(b, 20);
        BitConverter.GetBytes(sort).CopyTo(b, 24);
        text.CopyTo(b, 28);
        return b;
    }

    /// <summary>
    /// EVERYTHING_IPC_LIST2: 5 DWORD başlık (toplam, dönen, ofset, istenen alanlar, sıralama), öğe başına 2 DWORD
    /// (bayrak, veri ofseti). Veri, istenen alanların sırasıyla: metin = DWORD uzunluk + (uzunluk+1) UTF-16; boyut = 8 bayt.
    /// (Kasa'daki Everything 1.4.1.1032 ile doğrulandı.)
    /// </summary>
    public static List<(string Name, string Folder, long Size, bool IsFolder)> ParseList2(byte[] d)
    {
        var list = new List<(string, string, long, bool)>();
        if (d.Length < 20) return list;
        uint n = BitConverter.ToUInt32(d, 4), req = BitConverter.ToUInt32(d, 12);
        for (uint i = 0; i < n; i++)
        {
            int at = 20 + (int)i * 8;
            if (at + 8 > d.Length) break;
            uint flags = BitConverter.ToUInt32(d, at);
            int p = (int)BitConverter.ToUInt32(d, at + 4);
            string name = "", folder = ""; long size = -1;
            try
            {
                if ((req & RequestName) != 0) name = Text(d, ref p);
                if ((req & RequestPath) != 0) folder = Text(d, ref p);
                if ((req & RequestSize) != 0) { size = BitConverter.ToInt64(d, p); p += 8; }
            }
            catch (ArgumentException) { continue; }
            list.Add((name, folder, size, (flags & (ItemFolder | ItemDrive)) != 0));
        }
        return list;
    }

    static string Text(byte[] d, ref int p)
    {
        if (p < 0 || p + 4 > d.Length) throw new ArgumentException("ofset");
        int len = (int)BitConverter.ToUInt32(d, p);
        if (len < 0 || p + 4 + len * 2 > d.Length) throw new ArgumentException("uzunluk");
        var s = Encoding.Unicode.GetString(d, p + 4, len * 2);
        p += 4 + (len + 1) * 2;
        return s;
    }

    static string? Str(byte[] d, uint offset)
    {
        if (offset >= d.Length) return null;
        int end = (int)offset;
        while (end + 1 < d.Length && (d[end] != 0 || d[end + 1] != 0)) end += 2;
        return Encoding.Unicode.GetString(d, (int)offset, end - (int)offset);
    }
}
