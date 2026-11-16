namespace KutayDock.Core;

/// <summary>"Windows optimizasyonu" kararları: oyun algılama ve dokunulmayacak süreçler.</summary>
public static class OptimizeRules
{
    // Oyunlar, oyun istemcileri, hile önleyiciler ve GPU sürücüsü süreçleri: RAM temizliği bunlara asla dokunmaz.
    static readonly string[] ProtectedPrefixes =
    {
        "marvel", "steam", "easyanticheat", "eac", "beservice", "battleye", "vgc", "vgtray", "faceit", "eaanticheat",
        "nvcontainer", "nvdisplay", "nvidia", "nvsphelper", "amdrsserv", "amdow", "radeonsoftware", "atiesrxx", "atieclxx",
        "igfx", "intelgraphics", "epicgameslauncher", "battle.net", "riotclient", "eadesktop", "ubisoftconnect", "gog",
        "audiodg", "csrss", "lsass", "smss", "wininit", "winlogon", "services", "system", "registry", "memcompression", "dwm",
    };

    static readonly string[] GameFolders = { @"\steamapps\common\", @"\epic games\", @"\riot games\", @"\xboxgames\", @"\ubisoft game launcher\games\" };

    /// <summary>Oyun süreci mi (Marvel Rivals, Steam/Epic/Riot kütüphanesindeki oyunlar).</summary>
    public static bool IsGame(string processName, string? path)
    {
        if (processName.StartsWith("marvel", StringComparison.OrdinalIgnoreCase)) return true;
        return path != null && GameFolders.Any(f => path.Contains(f, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>RAM temizliğinin dokunmayacağı süreç (oyun, istemci, hile önleyici, GPU sürücüsü, sistem).</summary>
    public static bool IsProtected(string processName, string? path) =>
        IsGame(processName, path) || ProtectedPrefixes.Any(p => processName.StartsWith(p, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Cloudflare WARP durumu (warp-cli status çıktısı).</summary>
public enum WarpState { Unknown, Connected, Connecting, Disconnected, NotInstalled }

public static class Warp
{
    /// <summary>"Status update: Connected" / "Disconnected" / "Connecting" satırlarını okur.</summary>
    public static WarpState Parse(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return WarpState.Unknown;
        foreach (var line in output.Split('\n'))
        {
            var l = line.Trim();
            if (!l.StartsWith("Status update:", StringComparison.OrdinalIgnoreCase)) continue;
            var v = l["Status update:".Length..].Trim();
            if (v.StartsWith("Connected", StringComparison.OrdinalIgnoreCase)) return WarpState.Connected;
            if (v.StartsWith("Connecting", StringComparison.OrdinalIgnoreCase)) return WarpState.Connecting;
            if (v.StartsWith("Disconnected", StringComparison.OrdinalIgnoreCase)) return WarpState.Disconnected;
        }
        return WarpState.Unknown;
    }

    public static string Label(WarpState s) => s switch
    {
        WarpState.Connected => "Bağlı",
        WarpState.Connecting => "Bağlanıyor…",
        WarpState.Disconnected => "Bağlı değil",
        WarpState.NotInstalled => "Kurulu değil",
        _ => "Bilinmiyor",
    };
}
