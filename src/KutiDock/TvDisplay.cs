using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using KutiDock.Core;

namespace KutiDock
{
    /// <summary>
    /// 1.6.12: TV'nin Windows ekran düzeninde açık mı kapalı mı olduğunu CCD API'siyle okur
    /// (QueryDisplayConfig QDC_ALL_PATHS: etkin ve etkin olmayan yollar; etkin yol varsa TV açık).
    /// Ekran ayarı değişince (SystemEvents.DisplaySettingsChanged = WM_DISPLAYCHANGE) önbellek düşer.
    /// </summary>
    internal static class TvDisplay
    {
        static bool? _state;
        static DateTime _readAt = DateTime.MinValue;
        static bool _hooked;
        static string _hintDir;
        static TvHint _hint = TvHint.None;
        static DateTime _hintAt = DateTime.MinValue;

        /// <summary>TV'nin durumu; en fazla 3 sn eski (ekran değişikliğinde hemen tazelenir).</summary>
        public static bool? State(string toolPath)
        {
            Hook();
            if ((DateTime.UtcNow - _readAt).TotalSeconds < 3) return _state;
            return Read(toolPath);
        }

        /// <summary>Hemen yeniden oku (tıklamadan sonra).</summary>
        public static bool? Read(string toolPath = null)
        {
            if (toolPath != null) _hintDir = Path.GetDirectoryName(toolPath);
            bool? s;
            try { s = TvStatus.IsOn(QueryPaths(), Hint()); }
            catch (Exception ex) { Trace("TV durumu okunamadı: " + ex.Message); s = null; }
            _readAt = DateTime.UtcNow;
            if (s != _state)
            {
                _state = s;
                Trace("TV durumu: " + TvStatus.Tooltip(s));
            }
            return s;
        }

        static void Trace(string line)
        {
            try { File.AppendAllText(Path.Combine(Services.DataDir, "hata.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}\n"); }
            catch (Exception) { }
        }

        static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) => _readAt = DateTime.MinValue;
        }

        static TvHint Hint()
        {
            if (_hintDir == null) return TvHint.None;
            if ((DateTime.UtcNow - _hintAt).TotalSeconds < 60) return _hint;
            _hintAt = DateTime.UtcNow;
            try
            {
                var f = Path.Combine(_hintDir, "ayarlar.json");
                _hint = File.Exists(f) ? TvHint.Parse(File.ReadAllText(f)) : TvHint.None;
            }
            catch (Exception) { _hint = TvHint.None; }
            return _hint;
        }

        static List<TvPath> QueryPaths()
        {
            var list = new List<TvPath>();
            if (GetDisplayConfigBufferSizes(QDC_ALL_PATHS, out var pc, out var mc) != 0) return list;
            var paths = new PATH_INFO[pc];
            var modes = new MODE_INFO[mc];
            if (QueryDisplayConfig(QDC_ALL_PATHS, ref pc, paths, ref mc, modes, IntPtr.Zero) != 0) return list;
            for (int i = 0; i < pc; i++)
            {
                var p = paths[i];
                var t = new TARGET_DEVICE_NAME();
                t.header.type = 2; // DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME
                t.header.size = Marshal.SizeOf<TARGET_DEVICE_NAME>();
                t.header.adapterId = p.targetInfo.adapterId;
                t.header.id = p.targetInfo.id;
                if (DisplayConfigGetDeviceInfo(ref t) != 0) continue;
                list.Add(new TvPath(t.monitorFriendlyDeviceName ?? "", t.monitorDevicePath ?? "",
                    p.targetInfo.outputTechnology == 5 /* HDMI */, (p.flags & 1) != 0));
            }
            return list;
        }

        const uint QDC_ALL_PATHS = 1;

        [StructLayout(LayoutKind.Sequential)] struct LUID { public uint LowPart; public int HighPart; }
        [StructLayout(LayoutKind.Sequential)] struct RATIONAL { public uint Numerator, Denominator; }
        [StructLayout(LayoutKind.Sequential)] struct PATH_SOURCE_INFO { public LUID adapterId; public uint id, modeInfoIdx, statusFlags; }
        [StructLayout(LayoutKind.Sequential)]
        struct PATH_TARGET_INFO
        {
            public LUID adapterId; public uint id, modeInfoIdx, outputTechnology, rotation, scaling;
            public RATIONAL refreshRate; public uint scanLineOrdering; public int targetAvailable; public uint statusFlags;
        }
        [StructLayout(LayoutKind.Sequential)] struct PATH_INFO { public PATH_SOURCE_INFO sourceInfo; public PATH_TARGET_INFO targetInfo; public uint flags; }
        // DISPLAYCONFIG_MODE_INFO: 64 bayt (tür, kimlik, bağdaştırıcı + 48 baytlık birleşim); içeriği kullanılmaz.
        [StructLayout(LayoutKind.Sequential, Size = 64)] struct MODE_INFO { public uint infoType, id; public LUID adapterId; }
        [StructLayout(LayoutKind.Sequential)] struct DEVICE_INFO_HEADER { public int type, size; public LUID adapterId; public uint id; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct TARGET_DEVICE_NAME
        {
            public DEVICE_INFO_HEADER header; public uint flags, outputTechnology;
            public ushort edidManufactureId, edidProductCodeId; public uint connectorInstance;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
        }

        [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
        [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint numPaths, [Out] PATH_INFO[] paths, ref uint numModes, [Out] MODE_INFO[] modes, IntPtr topology);
        [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref TARGET_DEVICE_NAME packet);
    }
}
