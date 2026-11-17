using System;
using System.Runtime.InteropServices;

namespace KutiDock
{
    /// <summary>Varsayılan hoparlörün ses düzeyi (Windows Core Audio, IAudioEndpointVolume).</summary>
    internal sealed class CoreAudio
    {
        /// <summary>1.6.6: hatalar yutulmaz; her işlem için ilk hata hata.log'a yazılır (aygıt adı/kimliği yazılmaz).</summary>
        public static Action<string, Exception> Error = (_, _) => { };
        static readonly System.Collections.Generic.HashSet<string> Reported = new();
        static void Report(string what, Exception ex) { lock (Reported) if (!Reported.Add(what + ex.GetType().Name)) return; Error(what, ex); }

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

        // ── 1.6.6: tüm Core Audio çağrıları tek bir MTA iş parçacığında ──
        // Arayüz iş parçacığından bağımsız; hatalar artık yutulmaz, hata.log'a yazılır. Core Audio serbest iş parçacıklıdır;
        // Microsoft MTA önerir. (Asıl kök neden aşağıda, En() üstünde.)
        static readonly KutiDock.Core.ComWorker Worker = new("Kuti Dock ses");
        static IMMDeviceEnumerator _en;

        /// <summary>İşi ses iş parçacığında çalıştırır; 1,5 sn'de bitmezse ya da hata verirse yedek değer.</summary>
        static T Run<T>(string what, Func<T> f, T fallback) =>
            Worker.Run(f, fallback, ex => { _en = null; Report(what, ex); }); // bozuk sayıcı bir sonraki çağrıda yenilenir

        // KÖK NEDEN (1.6.6, Kasa'da hata.log ile doğrulandı): Mac menü çubuğu (Gravitone, StatusInterop) aynı CLSID için kendi
        // [ComImport] sınıfını tanımlıyor ve açılışta önce o oluşturuluyor. .NET aynı COM nesnesi için ilk sınıf türünü
        // önbelleğe aldığından "new MMDeviceEnumerator()" InvalidCastException veriyor, eski kod da hatayı yutuyordu: liste
        // boş, kaydırıcı etkisiz. Nesne artık CoCreateInstance ile doğrudan arayüz olarak alınır (sınıf türü yok).
        static readonly Guid ClsidEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
        static readonly Guid ClsidPolicyConfig = new("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9");
        [DllImport("ole32.dll")]
        static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, int context, ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object obj);

        static T Create<T>(Guid clsid)
        {
            var iid = typeof(T).GUID;
            Check(CoCreateInstance(ref clsid, IntPtr.Zero, 1 /*CLSCTX_INPROC_SERVER*/, ref iid, out var o), "create");
            return (T)o;
        }

        static IMMDeviceEnumerator En() => _en ??= Create<IMMDeviceEnumerator>(ClsidEnumerator);

        static void Check(int hr, string what) { if (hr != 0) Marshal.ThrowExceptionForHR(hr); }

        /// <summary>Etkin ses çıkış aygıtları; varsayılan işaretli.</summary>
        public System.Collections.Generic.List<Device> Outputs() => List(0 /*eRender*/);

        /// <summary>Etkin ses giriş (mikrofon) aygıtları; varsayılan işaretli.</summary>
        public System.Collections.Generic.List<Device> Inputs() => List(1 /*eCapture*/);

        static DateTime _probed;
        /// <summary>Kanıt için: çağıran (arayüz) iş parçacığında eski yol hâlâ çalışıyor mu; yalnızca başarısızlık loglanır, 10 dk'da bir.</summary>
        static void ProbeCaller()
        {
            if (DateTime.UtcNow - _probed < TimeSpan.FromMinutes(10)) return;
            _probed = DateTime.UtcNow;
            try { var en = Create<IMMDeviceEnumerator>(ClsidEnumerator); Marshal.ReleaseComObject(en); }
            catch (Exception ex) { Report("UI thread (" + System.Threading.Thread.CurrentThread.GetApartmentState() + ")", ex); }
        }

        System.Collections.Generic.List<Device> List(int flow) { ProbeCaller(); return ListOnWorker(flow); }

        System.Collections.Generic.List<Device> ListOnWorker(int flow) => Run(flow == 0 ? "Outputs" : "Inputs", () =>
        {
            var list = new System.Collections.Generic.List<Device>();
            var en = En();
            string def = null;
            if (en.GetDefaultAudioEndpoint(flow, 1, out var d) == 0) d.GetId(out def);
            Check(en.EnumAudioEndpoints(flow, 1 /*DEVICE_STATE_ACTIVE*/, out var col), "enum");
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
            return list;
        }, new System.Collections.Generic.List<Device>());

        /// <summary>Varsayılan aygıtı değiştirir (konsol, multimedya ve iletişim rolleri); çıkış ya da giriş.</summary>
        public bool SetDefault(string id) => Run("SetDefault", () =>
        {
            var pc = Create<IPolicyConfig>(ClsidPolicyConfig);
            bool ok = true;
            for (int role = 0; role < 3; role++) ok &= pc.SetDefaultEndpoint(id, role) == 0;
            return ok;
        }, false);
        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioEndpointVolume
        {
            [PreserveSig] int RegisterControlChangeNotify(IntPtr n); [PreserveSig] int UnregisterControlChangeNotify(IntPtr n);
            [PreserveSig] int GetChannelCount(out uint c);
            [PreserveSig] int SetMasterVolumeLevel(float db, ref Guid ctx);
            [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
            [PreserveSig] int GetMasterVolumeLevel(out float db);
            [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
            [PreserveSig] int SetChannelVolumeLevel(uint ch, float db, ref Guid ctx); [PreserveSig] int SetChannelVolumeLevelScalar(uint ch, float l, ref Guid ctx);
            [PreserveSig] int GetChannelVolumeLevel(uint ch, out float db); [PreserveSig] int GetChannelVolumeLevelScalar(uint ch, out float l);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }

        static IAudioEndpointVolume Endpoint(int flow)
        {
            // Varsayılan cihaz değişebildiği için her seferinde bakılır (ucuz).
            if (En().GetDefaultAudioEndpoint(flow, 1 /*eMultimedia*/, out var dev) != 0) return null;
            var iid = typeof(IAudioEndpointVolume).GUID;
            return dev.Activate(ref iid, 23 /*CLSCTX_ALL*/, IntPtr.Zero, out var o) == 0 ? (IAudioEndpointVolume)o : null;
        }

        /// <summary>Varsayılan çıkışın (ya da mikrofonun) düzeyi ve sessizliği; aygıt yoksa null.</summary>
        public (float level, bool muted)? Read(bool input = false) => Run<(float, bool)?>("Read", () =>
        {
            var ep = Endpoint(input ? 1 : 0); if (ep == null) return null;
            Check(ep.GetMasterVolumeLevelScalar(out float l), "level"); Check(ep.GetMute(out bool m), "mute");
            return (l, m);
        }, null);

        public bool SetLevel(float level, bool input = false) => Run("SetLevel", () =>
        {
            var ep = Endpoint(input ? 1 : 0); if (ep == null) return false;
            var g = Guid.Empty; Check(ep.SetMasterVolumeLevelScalar(Math.Clamp(level, 0f, 1f), ref g), "set");
            return true;
        }, false);

        public bool SetMute(bool mute, bool input = false) => Run("SetMute", () =>
        {
            var ep = Endpoint(input ? 1 : 0); if (ep == null) return false;
            var g = Guid.Empty; Check(ep.SetMute(mute, ref g), "mute");
            return true;
        }, false);

        public void ToggleMute(bool input = false)
        {
            if (Read(input) is { } v) SetMute(!v.muted, input);
        }
    }
}
