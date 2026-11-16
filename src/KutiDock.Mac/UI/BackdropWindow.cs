// Gravitone kaynak kodu (https://github.com/lupanostefano/Gravitone), Copyright (c) 2026 Stefano Lupano, MIT Lisansı.
// Kuti Dock için uyarlandı (ad, veri klasörü, Türkçe arayüz, Kuti widget entegrasyonu). Lisans: LICENSE-Gravitone.txt
using System.Windows.Controls;
using System.Windows.Media;
using KutiDock.Mac.Core;
using KutiDock.Mac.Interop;
using static KutiDock.Mac.Interop.NativeMethods;

namespace KutiDock.Mac.UI;

/// <summary>
/// The frosted-glass plate behind the icons. It is a separate, non-layered window because only
/// those get the real Windows 11 acrylic backdrop and antialiased rounded corners from DWM.
/// It is resized every frame while the dock magnifies.
/// </summary>
internal sealed class BackdropWindow : AcrylicWindow
{
    readonly Border _tint = new();
    bool _visible = true;

    public BackdropWindow() : base(rounded: true)
    {
        Title = "Kuti Dock Backdrop";
        Content = _tint;
    }

    public override void ApplyTheme()
    {
        // COLORREF is 0x00BBGGRR: a faint rim like the Dock's hairline border.
        ApplyGlassTheme(Theme.IsDark ? 0x00545454 : 0x00D6D6D6);
        _tint.Background = new SolidColorBrush(Theme.IsDark
            ? Color.FromArgb(0x26, 0x10, 0x10, 0x10)
            : Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
    }

    public void SetVisible(bool visible)
    {
        if (Handle == IntPtr.Zero || visible == _visible) return;
        _visible = visible;
        ShowWindow(Handle, visible ? SW_SHOWNOACTIVATE : SW_HIDE);
    }
}
