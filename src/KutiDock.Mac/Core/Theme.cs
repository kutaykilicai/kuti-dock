// Gravitone kaynak kodu (https://github.com/lupanostefano/Gravitone), Copyright (c) 2026 Stefano Lupano, MIT Lisansı.
// Kuti Dock için uyarlandı (ad, veri klasörü, Türkçe arayüz, Kuti widget entegrasyonu). Lisans: LICENSE-Gravitone.txt
using Microsoft.Win32;

namespace KutiDock.Mac.Core;

internal static class Theme
{
    /// <summary>Follows the Windows "system" colour mode, the one the taskbar uses.</summary>
    public static bool IsDark { get; private set; } = true;

    public static void Refresh()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            IsDark = key?.GetValue("SystemUsesLightTheme") is not int light || light == 0;
        }
        catch (Exception)
        {
            IsDark = true;
        }
    }
}
