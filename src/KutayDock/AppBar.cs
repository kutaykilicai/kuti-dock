using System;
using System.Runtime.InteropServices;
using System.Windows;

namespace KutayDock
{
    /// <summary>
    /// Kabuk "AppBar" kaydı: ekranın kenarında dock için yer ayırır; büyütülen pencereler dock'un altına girmez.
    /// Ayrıca Windows görev çubuğunu (isteğe bağlı) otomatik gizler ve çıkışta eski hâline döndürür.
    /// </summary>
    internal sealed class AppBar
    {
        readonly IntPtr _hwnd; bool _registered; Native.RECT _rc;
        public AppBar(IntPtr hwnd) { _hwnd = hwnd; }

        Native.APPBARDATA Data() => new() { cbSize = Marshal.SizeOf<Native.APPBARDATA>(), hWnd = _hwnd };

        public void Reserve(bool top, int heightPx)
        {
            var d = Data();
            if (!_registered) { d.uCallbackMessage = 0x0400 + 77; Native.SHAppBarMessage(Native.ABM_NEW, ref d); _registered = true; }
            int sw = (int)Math.Round(SystemParameters.PrimaryScreenWidth * Dpi().x), sh = (int)Math.Round(SystemParameters.PrimaryScreenHeight * Dpi().y);
            d.uEdge = top ? Native.ABE_TOP : Native.ABE_BOTTOM;
            d.rc = new Native.RECT { Left = 0, Right = sw, Top = top ? 0 : sh - heightPx, Bottom = top ? heightPx : sh };
            Native.SHAppBarMessage(Native.ABM_QUERYPOS, ref d);
            if (top) d.rc.Bottom = d.rc.Top + heightPx; else d.rc.Top = d.rc.Bottom - heightPx;
            Native.SHAppBarMessage(Native.ABM_SETPOS, ref d);
            _rc = d.rc;
        }

        (double x, double y) Dpi()
        {
            var src = System.Windows.Interop.HwndSource.FromHwnd(_hwnd);
            var m = src?.CompositionTarget?.TransformToDevice;
            return m.HasValue ? (m.Value.M11, m.Value.M22) : (1, 1);
        }

        /// <summary>Ayrılan alan (WPF birimleri).</summary>
        public Rect? ReservedRect(double sx, double sy) =>
            _registered ? new Rect(_rc.Left / sx, _rc.Top / sy, (_rc.Right - _rc.Left) / sx, (_rc.Bottom - _rc.Top) / sy) : null;

        public void Remove()
        {
            if (!_registered) return;
            var d = Data(); Native.SHAppBarMessage(Native.ABM_REMOVE, ref d); _registered = false;
        }

        // ---- Windows görev çubuğu ----
        static int? _originalState;

        public static void SetTaskbarAutoHide(bool on)
        {
            var d = new Native.APPBARDATA { cbSize = Marshal.SizeOf<Native.APPBARDATA>(), hWnd = Native.FindWindow("Shell_TrayWnd", null) };
            int state = (int)Native.SHAppBarMessage(Native.ABM_GETSTATE, ref d).ToUInt32();
            _originalState ??= state;
            int want = on ? (state | Native.ABS_AUTOHIDE) : (_originalState.Value & Native.ABS_AUTOHIDE) != 0 ? state | Native.ABS_AUTOHIDE : state & ~Native.ABS_AUTOHIDE;
            if (want == state) return;
            d.lParam = new IntPtr(want);
            Native.SHAppBarMessage(Native.ABM_SETSTATE, ref d);
        }

        /// <summary>Kutay Dock kapanırken görev çubuğunu açılıştaki hâline döndürür.</summary>
        public static void RestoreTaskbar()
        {
            if (_originalState == null) return;
            var d = new Native.APPBARDATA { cbSize = Marshal.SizeOf<Native.APPBARDATA>(), hWnd = Native.FindWindow("Shell_TrayWnd", null), lParam = new IntPtr(_originalState.Value) };
            Native.SHAppBarMessage(Native.ABM_SETSTATE, ref d);
        }
    }
}
