using System;
using System.Runtime.InteropServices;

namespace KutayDock
{
    /// <summary>Varsayılan hoparlörün ses düzeyi (Windows Core Audio, IAudioEndpointVolume).</summary>
    internal sealed class CoreAudio
    {
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumerator { }
        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int flow, int mask, out IMMDeviceCollection devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
        }
        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceCollection
        {
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int Item(uint i, out IMMDevice device);
        }
        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int ctx, IntPtr p, [MarshalAs(UnmanagedType.IUnknown)] out object o);
            [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
            [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        }
        [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPropertyStore
        {
            [PreserveSig] int GetCount(out uint c);
            [PreserveSig] int GetAt(uint i, out PROPERTYKEY k);
            [PreserveSig] int GetValue(ref PROPERTYKEY k, out PROPVARIANT v);
        }
        [StructLayout(LayoutKind.Sequential)] struct PROPERTYKEY { public Guid fmtid; public int pid; }
        [StructLayout(LayoutKind.Explicit)] struct PROPVARIANT { [FieldOffset(0)] public ushort vt; [FieldOffset(8)] public IntPtr p; [FieldOffset(16)] public IntPtr pad; }

        // Varsayılan çıkış aygıtını değiştirmek için Windows'un kendi ses ayarlarının kullandığı arayüz (IPolicyConfig).
        // Belgelenmemiş ama Windows 7–11'de değişmeden kalan, yaygın kullanılan COM arayüzü.
        [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")] class PolicyConfigClient { }
        [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPolicyConfig
        {
            [PreserveSig] int GetMixFormat(string id, IntPtr f); [PreserveSig] int GetDeviceFormat(string id, bool def, IntPtr f);
            [PreserveSig] int ResetDeviceFormat(string id); [PreserveSig] int SetDeviceFormat(string id, IntPtr f, IntPtr m);
            [PreserveSig] int GetProcessingPeriod(string id, bool def, IntPtr a, IntPtr b); [PreserveSig] int SetProcessingPeriod(string id, IntPtr p);
            [PreserveSig] int GetShareMode(string id, IntPtr m); [PreserveSig] int SetShareMode(string id, IntPtr m);
            [PreserveSig] int GetPropertyValue(string id, bool fx, IntPtr k, IntPtr v); [PreserveSig] int SetPropertyValue(string id, bool fx, IntPtr k, IntPtr v);
            [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
            [PreserveSig] int SetEndpointVisibility(string id, bool visible);
        }
        [DllImport("ole32.dll")] static extern int PropVariantClear(ref PROPVARIANT v);
        static PROPERTYKEY FriendlyName = new() { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };

        public sealed record Device(string Id, string Name, bool IsDefault);

        /// <summary>Etkin ses çıkış aygıtları; varsayılan işaretli.</summary>
        public System.Collections.Generic.List<Device> Outputs()
        {
            var list = new System.Collections.Generic.List<Device>();
            try
            {
                var en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                string def = null;
                if (en.GetDefaultAudioEndpoint(0, 1, out var d) == 0) d.GetId(out def);
                if (en.EnumAudioEndpoints(0 /*eRender*/, 1 /*ACTIVE*/, out var col) != 0) return list;
                col.GetCount(out uint n);
                for (uint i = 0; i < n; i++)
                {
                    if (col.Item(i, out var dev) != 0) continue;
                    dev.GetId(out var id);
                    string name = id;
                    if (dev.OpenPropertyStore(0, out var ps) == 0 && ps.GetValue(ref FriendlyName, out var v) == 0)
                    {
                        if (v.vt == 31 /*VT_LPWSTR*/) name = Marshal.PtrToStringUni(v.p) ?? id;
                        PropVariantClear(ref v);
                    }
                    list.Add(new Device(id, name, id == def));
                }
            }
            catch (Exception) { }
            return list;
        }

        /// <summary>Varsayılan çıkışı değiştirir (konsol, multimedya ve iletişim rolleri).</summary>
        public bool SetDefault(string id)
        {
            try
            {
                var pc = (IPolicyConfig)new PolicyConfigClient();
                bool ok = true;
                for (int role = 0; role < 3; role++) ok &= pc.SetDefaultEndpoint(id, role) == 0;
                return ok;
            }
            catch (Exception) { return false; }
        }
        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioEndpointVolume
        {
            int RegisterControlChangeNotify(IntPtr n); int UnregisterControlChangeNotify(IntPtr n);
            int GetChannelCount(out uint c);
            int SetMasterVolumeLevel(float db, ref Guid ctx);
            int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
            int GetMasterVolumeLevel(out float db);
            int GetMasterVolumeLevelScalar(out float level);
            int SetChannelVolumeLevel(uint ch, float db, ref Guid ctx); int SetChannelVolumeLevelScalar(uint ch, float l, ref Guid ctx);
            int GetChannelVolumeLevel(uint ch, out float db); int GetChannelVolumeLevelScalar(uint ch, out float l);
            int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
            int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }

        IAudioEndpointVolume Endpoint()
        {
            // Varsayılan cihaz değişebildiği için her seferinde tazelenir (ucuz bir çağrı).
            var en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            if (en.GetDefaultAudioEndpoint(0 /*eRender*/, 1 /*eMultimedia*/, out var dev) != 0) return null;
            var iid = typeof(IAudioEndpointVolume).GUID;
            return dev.Activate(ref iid, 23 /*CLSCTX_ALL*/, IntPtr.Zero, out var o) == 0 ? (IAudioEndpointVolume)o : null;
        }

        public (float level, bool muted)? Read()
        {
            try
            {
                var ep = Endpoint(); if (ep == null) return null;
                ep.GetMasterVolumeLevelScalar(out float l); ep.GetMute(out bool m);
                return (l, m);
            }
            catch (Exception) { return null; }
        }

        public void SetLevel(float level)
        {
            try { var g = Guid.Empty; Endpoint()?.SetMasterVolumeLevelScalar(Math.Clamp(level, 0f, 1f), ref g); } catch (Exception) { }
        }

        public void ToggleMute()
        {
            try { var ep = Endpoint(); if (ep == null) return; ep.GetMute(out bool m); var g = Guid.Empty; ep.SetMute(!m, ref g); } catch (Exception) { }
        }
    }
}
