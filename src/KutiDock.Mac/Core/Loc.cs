// Gravitone kaynak kodu (https://github.com/lupanostefano/Gravitone), Copyright (c) 2026 Stefano Lupano, MIT Lisansı.
// Kuti Dock için uyarlandı (ad, veri klasörü, Türkçe arayüz, Kuti widget entegrasyonu). Lisans: LICENSE-Gravitone.txt
using System.Globalization;

namespace KutiDock.Mac.Core;

/// <summary>
/// The interface language. The source text is English; <see cref="T(string)"/> looks a text up in the Italian
/// table when Italian is in use and returns the English text otherwise (so a missing translation is never blank).
/// </summary>
internal static class Loc
{
    // Kuti Dock: İtalyanca tablo yerine Türkçe. Ayar boşsa ve Windows İngilizce değilse Türkçe kullanılır.
    static bool _turkish = true;

    /// <summary>The language codes the settings offer ("" = follow Windows).</summary>
    public static readonly string[] Languages = ["", "en", "tr"];

    /// <param name="setting">"" (the language of Windows), "en" or "tr".</param>
    public static void Init(string? setting)
    {
        string code = string.IsNullOrEmpty(setting) ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName : setting;
        _turkish = !code.Equals("en", StringComparison.OrdinalIgnoreCase);
    }

    public static string T(string english) => _turkish && Tr.TryGetValue(english, out var turkish) ? turkish : english;

    /// <summary>Same, then <see cref="string.Format(string, object[])"/> with the arguments.</summary>
    public static string T(string english, params object[] args) => string.Format(CultureInfo.CurrentCulture, T(english), args);

    static readonly Dictionary<string, string> Tr = new()
    {
        // Dock menus
        ["Open"] = "Aç",
        ["Empty Recycle Bin"] = "Geri Dönüşüm Kutusunu Boşalt",
        ["Recycle Bin"] = "Geri Dönüşüm Kutusu",
        ["Show in File Explorer"] = "Dosya Gezgini'nde göster",
        ["File Explorer"] = "Dosya Gezgini",
        ["Remove from Dock"] = "Dock'tan kaldır",
        ["Remove"] = "Kaldır",
        ["Options"] = "Seçenekler",
        ["Keep in Dock"] = "Dock'ta tut",
        ["Open at Login"] = "Oturum açılınca aç",
        ["Recent"] = "Son kullanılanlar",
        ["New Window"] = "Yeni pencere",
        ["New Window as Administrator"] = "Yönetici olarak yeni pencere",
        ["Open as Administrator"] = "Yönetici olarak aç",
        ["Show All Windows"] = "Tüm pencereleri göster",
        ["Hide"] = "Gizle",
        ["Quit"] = "Çık",
        ["{0} Settings…"] = "{0} ayarları…",
        ["Replace the Windows Taskbar"] = "Windows görev çubuğunun yerine geç",
        ["Hide the Windows Taskbar"] = "Windows görev çubuğunu gizle",
        ["Show the Windows Taskbar"] = "Windows görev çubuğunu göster",
        ["Menu Bar"] = "Üst menü çubuğu",
        ["Start with Windows"] = "Windows ile başlat",
        ["Quit {0}"] = "{0}'tan çık",
        // Menu bar
        ["Wi-Fi: signal {0}%"] = "Wi-Fi: sinyal %{0}",
        ["Ethernet: connected"] = "Ethernet: bağlı",
        ["No Internet connection"] = "İnternet bağlantısı yok",
        ["No audio device"] = "Ses aygıtı yok",
        ["Volume: muted"] = "Ses: kapalı",
        ["Volume: {0}%"] = "Ses: %{0}",
        ["Battery: {0}%"] = "Pil: %{0}",
        ["Battery: {0}% (charging)"] = "Pil: %{0} (şarj oluyor)",
        ["Battery"] = "Pil",
        // Stack
        ["This folder is empty"] = "Bu klasör boş",
        ["Open \"{0}\" in File Explorer"] = "\"{0}\" klasörünü Dosya Gezgini'nde aç",
        // Displays
        ["Display {0} ({1} × {2})"] = "Ekran {0} ({1} × {2})",
        ["Display {0} ({1} × {2}), primary"] = "Ekran {0} ({1} × {2}), birincil",
        // Settings
        ["{0} Settings"] = "{0} ayarları",
        ["Appearance"] = "Görünüm",
        ["Icon size"] = "Simge boyutu",
        ["Magnify icons under the pointer"] = "İmlecin altındaki simgeleri büyüt",
        ["Magnified size"] = "Büyütülmüş boyut",
        ["Genie effect when minimizing and restoring"] = "Küçültürken ve geri açarken Genie efekti",
        ["Position"] = "Konum",
        ["Position on screen"] = "Ekrandaki konum",
        ["Bottom"] = "Alt",
        ["Left"] = "Sol",
        ["Right"] = "Sağ",
        ["Hide automatically"] = "Otomatik gizle",
        ["Dock display"] = "Dock'un ekranı",
        ["Primary"] = "Birincil",
        ["{0} (disconnected)"] = "{0} (bağlı değil)",
        ["Follow the pointer to the bottom edge of another display"] = "İmleç başka bir ekranın alt kenarına itilince oraya geç",
        ["System"] = "Sistem",
        ["Replace the Windows taskbar"] = "Windows görev çubuğunun yerine geç",
        ["Menu bar on top"] = "Üstte menü çubuğu",
        ["Menu bar on every display"] = "Her ekranda menü çubuğu",
        ["Win+1 … Win+9 open the Dock's apps"] = "Win+1 … Win+9 Dock'taki uygulamaları açar",
        ["Show or hide the Windows taskbar: {0}"] = "Windows görev çubuğunu göster/gizle: {0}",
        ["Language"] = "Dil",
        ["Same as Windows"] = "Windows ile aynı",
        ["English"] = "English",
        ["Türkçe"] = "Türkçe",
    };
}
