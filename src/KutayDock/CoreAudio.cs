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
            int EnumAudioEndpoints(int flow, int mask, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
        }
        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int ctx, IntPtr p, [MarshalAs(UnmanagedType.IUnknown)] out object o);
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
