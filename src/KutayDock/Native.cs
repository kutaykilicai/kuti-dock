using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace KutayDock
{
    /// <summary>Win32 çağrıları. Hepsi belgelenmiş, genel Windows API'leridir.</summary>
    internal static class Native
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_APPWINDOW = 0x40000;
        public const int SW_RESTORE = 9, SW_MINIMIZE = 6, SW_SHOW = 5;
        public const uint GW_OWNER = 4;
        public const int DWMWA_CLOAKED = 14;
        public const uint WM_CLOSE = 0x0010;

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int idx);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr h, int idx, IntPtr v);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern void SwitchToThisWindow(IntPtr h, bool altTab);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern IntPtr FindWindow(string cls, string title);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int val, int size);
        [DllImport("user32.dll")] public static extern int SetWindowRgn(IntPtr h, IntPtr rgn, bool redraw);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);

        [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder sb, ref int size);
        public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        public const int SW_HIDE = 0, SW_SHOWNA = 8;

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
        [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);

        // Sistem
        [StructLayout(LayoutKind.Sequential)] public struct FILETIME { public uint Low, High; public ulong Value => ((ulong)High << 32) | Low; }
        [DllImport("kernel32.dll")] public static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORYSTATUSEX
        {
            public uint dwLength, dwMemoryLoad; public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }
        [DllImport("kernel32.dll")] public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);

        [StructLayout(LayoutKind.Sequential)]
        public struct SYSTEM_POWER_STATUS { public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public int BatteryLifeTime, BatteryFullLifeTime; }
        [DllImport("kernel32.dll")] public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS s);

        [StructLayout(LayoutKind.Sequential)] public struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
        [DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref LASTINPUTINFO i);
        public static TimeSpan IdleTime()
        {
            var i = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            if (!GetLastInputInfo(ref i)) return TimeSpan.Zero;
            return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - i.dwTime));
        }

        // Klavye (Başlat menüsü, bildirim merkezi)
        [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        public const byte VK_LWIN = 0x5B; const uint KEYUP = 2;
        public static void PressKeys(params byte[] keys)
        {
            foreach (var k in keys) keybd_event(k, 0, 0, UIntPtr.Zero);
            for (int i = keys.Length - 1; i >= 0; i--) keybd_event(keys[i], 0, KEYUP, UIntPtr.Zero);
        }

        // Bulanık arka plan (Windows 10/11 "accent" API)
        [StructLayout(LayoutKind.Sequential)] struct ACCENT_POLICY { public int AccentState, AccentFlags, GradientColor, AnimationId; }
        [StructLayout(LayoutKind.Sequential)] struct WINCOMPATTRDATA { public int Attribute; public IntPtr Data; public int SizeOfData; }
        [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr h, ref WINCOMPATTRDATA d);
        public static void SetBlur(IntPtr hwnd, bool on)
        {
            var accent = new ACCENT_POLICY { AccentState = on ? 3 : 0 }; // 3 = ACCENT_ENABLE_BLURBEHIND
            int size = Marshal.SizeOf(accent);
            IntPtr p = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, p, false);
                var d = new WINCOMPATTRDATA { Attribute = 19, Data = p, SizeOfData = size }; // WCA_ACCENT_POLICY
                SetWindowCompositionAttribute(hwnd, ref d);
            }
            finally { Marshal.FreeHGlobal(p); }
        }

        // AppBar (ekranda alan ayırma, görev çubuğu otomatik gizleme)
        [StructLayout(LayoutKind.Sequential)]
        public struct APPBARDATA { public int cbSize; public IntPtr hWnd; public uint uCallbackMessage; public uint uEdge; public RECT rc; public IntPtr lParam; }
        [DllImport("shell32.dll")] public static extern UIntPtr SHAppBarMessage(uint msg, ref APPBARDATA d);
        public const uint ABM_NEW = 0, ABM_REMOVE = 1, ABM_QUERYPOS = 2, ABM_SETPOS = 3, ABM_GETSTATE = 4, ABM_SETSTATE = 10;
        public const uint ABE_TOP = 1, ABE_BOTTOM = 3;
        public const int ABS_AUTOHIDE = 1, ABS_ALWAYSONTOP = 2;

        public static string WindowTitle(IntPtr h)
        {
            int n = GetWindowTextLength(h);
            if (n <= 0) return "";
            var sb = new StringBuilder(n + 1);
            GetWindowText(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public static string ClassName(IntPtr h)
        {
            var sb = new StringBuilder(256);
            GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public static string ProcessPath(uint pid)
        {
            IntPtr hp = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (hp == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024); int size = sb.Capacity;
                return QueryFullProcessImageName(hp, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally { CloseHandle(hp); }
        }

        static readonly HashSet<string> ShellClasses = new(StringComparer.OrdinalIgnoreCase)
        { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow" };

        /// <summary>Görev çubuğunda görünecek pencereler (Z sırasına göre).</summary>
        public static List<IntPtr> TaskbarWindows(IntPtr exclude)
        {
            var list = new List<IntPtr>();
            EnumWindows((h, _) =>
            {
                if (h == exclude || !IsWindowVisible(h)) return true;
                long ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
                bool appWindow = (ex & WS_EX_APPWINDOW) != 0;
                if (!appWindow && ((ex & WS_EX_TOOLWINDOW) != 0 || GetWindow(h, GW_OWNER) != IntPtr.Zero)) return true;
                if (GetWindowTextLength(h) == 0) return true;
                if (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int cloaked, 4) == 0 && cloaked != 0) return true;
                if (ShellClasses.Contains(ClassName(h))) return true;
                list.Add(h);
                return true;
            }, IntPtr.Zero);
            return list;
        }

        // ---- Paketli (Mağaza/UWP) uygulamalar: AppUserModelID ----
        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPropertyStore
        {
            [PreserveSig] int GetCount(out uint c);
            [PreserveSig] int GetAt(uint i, out PROPERTYKEY k);
            [PreserveSig] int GetValue(ref PROPERTYKEY k, out PROPVARIANT v);
        }
        [StructLayout(LayoutKind.Sequential)] struct PROPERTYKEY { public Guid fmtid; public uint pid; }
        [StructLayout(LayoutKind.Explicit, Size = 24)] struct PROPVARIANT { [FieldOffset(0)] public ushort vt; [FieldOffset(8)] public IntPtr p; }
        [DllImport("shell32.dll")] static extern int SHGetPropertyStoreForWindow(IntPtr h, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore ps);
        [DllImport("ole32.dll")] static extern int PropVariantClear(ref PROPVARIANT v);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern int GetApplicationUserModelId(IntPtr hProcess, ref int len, StringBuilder sb);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);

        /// <summary>Pencerenin AppUserModelID'si: önce pencere özellik deposu, sonra (UWP çerçevesi için iç CoreWindow'un) süreci.</summary>
        public static string AppUserModelId(IntPtr h)
        {
            try
            {
                var iid = typeof(IPropertyStore).GUID;
                if (SHGetPropertyStoreForWindow(h, ref iid, out var ps) == 0 && ps != null)
                {
                    var key = new PROPERTYKEY { fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), pid = 5 };
                    if (ps.GetValue(ref key, out var v) == 0)
                    {
                        string id = v.vt == 31 ? Marshal.PtrToStringUni(v.p) : null; // VT_LPWSTR
                        PropVariantClear(ref v);
                        Marshal.ReleaseComObject(ps);
                        if (!string.IsNullOrEmpty(id)) return id;
                    }
                    else Marshal.ReleaseComObject(ps);
                }
            }
            catch (Exception) { }
            var target = h;
            if (ClassName(h) == "ApplicationFrameWindow")
            {
                var core = FindWindowEx(h, IntPtr.Zero, "Windows.UI.Core.CoreWindow", null);
                if (core != IntPtr.Zero) target = core;
            }
            GetWindowThreadProcessId(target, out uint pid);
            IntPtr hp = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (hp == IntPtr.Zero) return null;
            try
            {
                int len = 256; var sb = new StringBuilder(len);
                return GetApplicationUserModelId(hp, ref len, sb) == 0 ? sb.ToString() : null;
            }
            finally { CloseHandle(hp); }
        }

        public static void Activate(IntPtr h)
        {
            if (IsIconic(h)) ShowWindow(h, SW_RESTORE);
            if (!SetForegroundWindow(h)) SwitchToThisWindow(h, true);
        }

        /// <summary>Ön plandaki pencere bulunduğu monitörü tamamen kaplıyor mu (oyun, tam ekran video)?</summary>
        public static bool ForegroundIsFullscreen(IntPtr self)
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == self) return false;
            var cls = ClassName(fg);
            if (ShellClasses.Contains(cls)) return false;
            if (!GetWindowRect(fg, out var r)) return false;
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(MonitorFromWindow(fg, 2), ref mi)) return false;
            return r.Left <= mi.rcMonitor.Left && r.Top <= mi.rcMonitor.Top && r.Right >= mi.rcMonitor.Right && r.Bottom >= mi.rcMonitor.Bottom;
        }
    }
}
