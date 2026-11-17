// Kuti Dock: Gravitone'un Genie efektini (MIT, © 2026 Stefano Lupano; bkz. LICENSE-Gravitone.txt) klasik dock'un
// uygulama simgelerine bağlar. Mantık UI/DockWindow.cs'deki TryGenieMinimize/TryGenieRestore'dan alındı.
using KutiDock.Mac.Interop;
using static KutiDock.Mac.Interop.NativeMethods;

namespace KutiDock.Mac.Core;

/// <summary>
/// Klasik dock için Genie: pencere simgeye akarak küçülür, küçültülmüşse simgeden büyüyerek geri gelir.
/// Çalışamazsa (ekran kartı, tam ekran, yakalama yok) false döner; çağıran normal küçültme/öne getirmeye düşer.
/// </summary>
internal static class ClassicGenie
{
    static readonly Dictionary<IntPtr, GenieCapture> Cache = [];
    static readonly List<IntPtr> Order = [];
    static GenieAnimation? _running;
    static readonly HashSet<IntPtr> OwnMinimize = [];          // dock tıklamasıyla başlayan küçültmeler (kanca ikinci kez oynatmasın)
    static (IntPtr Hwnd, GenieCapture Capture, DateTime At)? _fresh; // öndeki pencerenin son görüntüsü
    const int CacheMax = 5; // tam ekran yakalama ~8 MB

    static void SetTransitions(IntPtr hwnd, bool disabled)
    {
        int value = disabled ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, ref value, sizeof(int));
    }

    /// <summary>Pencere dock'tan Genie ile küçültüldü mü (geri gelişi de Genie ile oynar).</summary>
    public static bool HasCapture(IntPtr hwnd) => Cache.ContainsKey(hwnd);

    static bool Ready => _running is null && GenieRenderer.Instance.Available;

    /// <summary>Pencereyi Genie ile simgeye küçültür. <paramref name="icon"/>: simgenin ekran dikdörtgeni (piksel).</summary>
    public static bool Minimize(IntPtr hwnd, RECT icon, DockEdge edge, IntPtr dock)
    {
        try
        {
            Prune();
            if (!Ready) return Skip("minimize", _running is not null ? "another animation is running" : "graphics card not usable");
            if (IsIconic(hwnd) || !IsWindowVisible(hwnd) || IsCloaked(hwnd)) return Skip("minimize", "window already minimized, hidden or on another desktop");
            var capture = GenieCapture.From(hwnd);
            if (capture is null) return Skip("minimize", GenieCapture.LastFailure);
            Remember(hwnd, capture);
            SetTransitions(hwnd, true); // Windows'un kendi küçültmesi altta oynamasın
            OwnMinimize.Add(hwnd);
            var a = new GenieAnimation(capture, icon, edge, expand: false,
                started: () => WindowActions.Minimize(hwnd), finished: () => { }, durationMs: 520, lingerMs: 0);
            a.Ended += () => { SetTransitions(hwnd, false); _running = null; OwnMinimize.Remove(hwnd); if (!IsIconic(hwnd)) Cache.Remove(hwnd); };
            _running = a;
            if (a.Start(dock)) return true;
            _running = null; SetTransitions(hwnd, false);
            return Skip("minimize", "overlay could not start");
        }
        catch (Exception ex) { _running = null; Log.Error(ex, "Classic genie"); }
        return false;
    }

    /// <summary>Küçültülmüş pencereyi simgeden büyüterek geri getirir (yalnızca dock'tan Genie ile küçültüldüyse).</summary>
    public static bool Restore(IntPtr hwnd, RECT icon, DockEdge edge, IntPtr dock)
    {
        try
        {
            if (!Ready) return Skip("restore", _running is not null ? "another animation is running" : "graphics card not usable");
            if (!IsIconic(hwnd)) return false;
            if (!Cache.TryGetValue(hwnd, out var capture)) return Skip("restore", "no captured image: the window was not minimized from the Dock");
            SetTransitions(hwnd, true);
            var a = new GenieAnimation(capture, icon, edge, expand: true,
                started: null, finished: () => WindowActions.Activate(hwnd), durationMs: 460, lingerMs: 140);
            a.Ended += () => { SetTransitions(hwnd, false); Cache.Remove(hwnd); Order.Remove(hwnd); _running = null; };
            _running = a;
            if (a.Start(dock)) return true;
            _running = null; SetTransitions(hwnd, false);
            return Skip("restore", "overlay could not start");
        }
        catch (Exception ex) { _running = null; Log.Error(ex, "Classic genie"); }
        return false;
    }

    /// <summary>
    /// 1.6.6: dock'tan ilk kez açılan uygulamanın penceresi simgeden büyüyerek gelir. Pencere bu sırada saydam tutulur;
    /// son karede <paramref name="reveal"/> çağrılır (pencere görünür olur). Oynamazsa false: çağıran hemen gösterir.
    /// </summary>
    public static bool PlayLaunch(IntPtr hwnd, GenieCapture capture, RECT icon, DockEdge edge, IntPtr dock, Action reveal)
    {
        try
        {
            if (!Ready) return Skip("launch", _running is not null ? "another animation is running" : "graphics card not usable");
            SetTransitions(hwnd, true);
            var a = new GenieAnimation(capture, icon, edge, expand: true,
                started: null, finished: reveal, durationMs: 460, lingerMs: 120);
            a.Ended += () => { SetTransitions(hwnd, false); _running = null; };
            _running = a;
            if (a.Start(dock)) return true;
            _running = null; SetTransitions(hwnd, false);
            return Skip("launch", "overlay could not start");
        }
        catch (Exception ex) { _running = null; Log.Error(ex, "Classic genie"); }
        return false;
    }

    // ── 1.6.4: pencerenin kendi küçültme düğmesi / Win+Aşağı ──
    // Öndeki uygulama penceresinin görüntüsü düşük maliyetle (ön plan değişince ve birkaç saniyede bir) önbelleğe alınır
    // ve yalnızca o pencerenin Windows küçültme animasyonu kapatılır. Küçültme başlayınca Genie bu görüntüyle oynar;
    // görüntü dock'tan geri açılışta da kullanılır.

    /// <summary>Öndeki pencere: Windows'un kendi küçültme animasyonu yalnızca bu pencere için kapatılır.</summary>
    public static void Hold(IntPtr hwnd) => SetTransitions(hwnd, true);

    /// <summary>Pencere artık önde değil: animasyonu geri açılır (dock'tan Genie ile geri gelmeyi bekleyen hariç).</summary>
    public static void Release(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return;
        if (IsIconic(hwnd) && Cache.ContainsKey(hwnd)) return;
        SetTransitions(hwnd, false);
    }

    /// <summary>Öndeki pencerenin son görüntüsü (arka planda alınır).</summary>
    public static void SetFresh(IntPtr hwnd, GenieCapture capture) => _fresh = (hwnd, capture, DateTime.UtcNow);

    /// <summary>Arka plan iş parçacığında görüntü; başarısızsa null.</summary>
    public static GenieCapture? Capture(IntPtr hwnd)
    {
        try { return IsWindow(hwnd) && !IsIconic(hwnd) && IsWindowVisible(hwnd) ? GenieCapture.From(hwnd) : null; }
        catch (Exception) { return null; }
    }

    /// <summary>Pencere kendi düğmesiyle küçülmeye başladı: saklanan görüntüyle simgeye Genie.</summary>
    public static bool PlayMinimizing(IntPtr hwnd, RECT icon, DockEdge edge, IntPtr dock)
    {
        try
        {
            if (OwnMinimize.Contains(hwnd)) return false; // dock tıklaması zaten oynatıyor
            if (_fresh is not { } f || f.Hwnd != hwnd || DateTime.UtcNow - f.At > TimeSpan.FromSeconds(30) /* MinimizeGeniePolicy.MaxAge */)
                return Skip("minimize button", "no recent image of the window");
            if (!Ready) return Skip("minimize button", _running is not null ? "another animation is running" : "graphics card not usable");
            _fresh = null;
            Remember(hwnd, f.Capture);
            var a = new GenieAnimation(f.Capture, icon, edge, expand: false, started: null, finished: () => { }, durationMs: 520, lingerMs: 0);
            a.Ended += () => { _running = null; if (!IsIconic(hwnd)) { Cache.Remove(hwnd); SetTransitions(hwnd, false); } };
            _running = a;
            if (a.Start(dock)) return true;
            _running = null;
            return Skip("minimize button", "overlay could not start");
        }
        catch (Exception ex) { _running = null; Log.Error(ex, "Classic genie"); return false; }
    }

    static bool Skip(string action, string why)
    {
        Log.Info($"Classic genie skipped ({action}): {why}");
        return false;
    }

    static void Remember(IntPtr hwnd, GenieCapture c)
    {
        Cache[hwnd] = c; Order.Remove(hwnd); Order.Add(hwnd);
        while (Order.Count > CacheMax) { Cache.Remove(Order[0]); Order.RemoveAt(0); }
    }

    static void Prune()
    {
        foreach (var h in Order.Where(h => !IsWindow(h) || !IsIconic(h)).ToList()) { Cache.Remove(h); Order.Remove(h); }
    }
}
