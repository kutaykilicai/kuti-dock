using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KutayDock
{
    /// <summary>Kabuk simgeleri (yüksek kalite, alfa korunur) ve .lnk hedefi çözümleme.</summary>
    internal static class ShellIcons
    {
        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItemImageFactory { [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm); }
        [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);
        [DllImport("gdi32.dll")] static extern int GetObject(IntPtr h, int size, ref BITMAP bmp);
        [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER bi, uint usage);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [StructLayout(LayoutKind.Sequential)] struct BITMAPINFOHEADER { public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }
        [StructLayout(LayoutKind.Sequential)] struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }

        static readonly Dictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);

        public static ImageSource Get(string path, int px = 64)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (Cache.TryGetValue(path, out var c)) return c;
            ImageSource img = null;
            try
            {
                SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out var f);
                // 0x1 = SIIGBF_BIGGERSIZEOK, 0x10 = SIIGBF_ICONONLY
                if (f.GetImage(new SIZE { cx = px, cy = px }, 0x11, out IntPtr hbm) == 0 && hbm != IntPtr.Zero)
                {
                    try { img = FromHBitmap(hbm); } finally { DeleteObject(hbm); }
                }
                Marshal.ReleaseComObject(f);
            }
            catch (Exception) { }
            if (img == null)
            {
                try
                {
                    using var ico = System.Drawing.Icon.ExtractAssociatedIcon(path);
                    img = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                }
                catch (Exception) { }
            }
            img?.Freeze();
            Cache[path] = img;
            return img;
        }

        static BitmapSource FromHBitmap(IntPtr hbm)
        {
            var bmp = new BITMAP();
            GetObject(hbm, Marshal.SizeOf<BITMAP>(), ref bmp);
            if (bmp.bmBitsPixel != 32) return System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(hbm, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            int w = bmp.bmWidth, h = bmp.bmHeight, stride = w * 4;
            var bits = new byte[stride * h];
            // Negatif yükseklik = yukarıdan aşağı satırlar; görüntü ters dönmez.
            var bi = new BITMAPINFOHEADER { biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            IntPtr dc = GetDC(IntPtr.Zero);
            try { GetDIBits(dc, hbm, 0, (uint)h, bits, ref bi, 0); } finally { ReleaseDC(IntPtr.Zero, dc); }
            // Kabuk önceden çarpılmış alfa (PBGRA) döndürür.
            return BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, bits, stride);
        }

        // ---- .lnk çözümleme (IShellLinkW) ----
        [ComImport, Guid("00021401-0000-0000-C000-000000000046")] class CShellLink { }
        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int cch, IntPtr fd, uint flags);
            void GetIDList(out IntPtr ppidl); void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int cch); void SetDescription(string name);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int cch); void SetWorkingDirectory(string dir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int cch); void SetArguments(string args);
            void GetHotkey(out short hk); void SetHotkey(short hk);
            void GetShowCmd(out int cmd); void SetShowCmd(int cmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int cch, out int idx); void SetIconLocation(string path, int idx);
            void SetRelativePath(string path, uint res); void Resolve(IntPtr hwnd, uint flags); void SetPath(string file);
        }
        [ComImport, Guid("0000010b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPersistFile
        {
            void GetClassID(out Guid id); [PreserveSig] int IsDirty(); void Load([MarshalAs(UnmanagedType.LPWStr)] string file, uint mode);
            void Save(string file, bool remember); void SaveCompleted(string file); void GetCurFile(out IntPtr file);
        }

        /// <summary>Kısayolun hedef exe yolu (Mağaza uygulamalarında boş döner).</summary>
        public static string LinkTarget(string lnk)
        {
            try
            {
                var link = (IShellLinkW)new CShellLink();
                ((IPersistFile)link).Load(lnk, 0);
                var sb = new StringBuilder(1024);
                link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0x4 /* SLGP_RAWPATH yok: genişletilmiş */);
                Marshal.ReleaseComObject(link);
                var p = Environment.ExpandEnvironmentVariables(sb.ToString());
                return File.Exists(p) ? p : null;
            }
            catch (Exception) { return null; }
        }
    }
}
