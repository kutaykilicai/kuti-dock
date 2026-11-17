namespace KutiDock.Core;

/// <summary>
/// 1.6.4: dock panelleri (Cursor/Grok Bot kullanımı, ses, disk, optimizasyon, sağ tık menüleri) dışarı tıklanınca kapanır.
/// Dock odak almadığı için WPF'nin StaysOpen=false'u yalnızca dock içi tıklamaları görüyordu; masaüstü, başka pencere ya da
/// ikinci monitör tıklamaları düşük seviyeli kancayla yakalanır.
/// </summary>
public static class OutsideClick
{
    public const int WM_LBUTTONDOWN = 0x201, WM_RBUTTONDOWN = 0x204, WM_MBUTTONDOWN = 0x207, WM_XBUTTONDOWN = 0x20B, WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104, VK_ESCAPE = 0x1B;

    public static bool IsPress(int msg) => msg is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN or WM_XBUTTONDOWN;

    /// <summary>
    /// Basış panel kapatsın mı: panelin/menünün kendi penceresinde (ya da alt menüsünde) değilse ve dock'un kendisinde
    /// değilse (dock içi tıklamaları WPF zaten kapatır; panelin düğmesine basmak onu aç/kapatır).
    /// </summary>
    public static bool Closes(int msg, bool overOwnPopup, bool overAnchorDock) => IsPress(msg) && !overOwnPopup && !overAnchorDock;

    public static bool IsEscape(int msg, int vk) => (msg is WM_KEYDOWN or WM_SYSKEYDOWN) && vk == VK_ESCAPE;
}
