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
    const int CacheMax = 5; // tam ekran yakalama ~8 MB

    static void SetTransitions(IntPtr hwnd, bool disabled)
    {
        int value = disabled ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, ref value, sizeof(int));
    }

    static bool Ready => _running is null && GenieRenderer.Instance.Available;

    /// <summary>Pencereyi Genie ile simgeye küçültür. <paramref name="icon"/>: simgenin ekran dikdörtgeni (piksel).</summary>
    public static bool Minimize(IntPtr hwnd, RECT icon, DockEdge edge, IntPtr dock)
    {
        try
        {
            Prune();
            if (!Ready || IsIconic(hwnd) || !IsWindowVisible(hwnd) || IsCloaked(hwnd)) return false;
            var capture = GenieCapture.From(hwnd);
            if (capture is null) return false;
            Remember(hwnd, capture);
            SetTransitions(hwnd, true); // Windows'un kendi küçültmesi altta oynamasın
            var a = new GenieAnimation(capture, icon, edge, expand: false,
                started: () => WindowActions.Minimize(hwnd), finished: () => { }, durationMs: 520, lingerMs: 0);
            a.Ended += () => { SetTransitions(hwnd, false); _running = null; if (!IsIconic(hwnd)) Cache.Remove(hwnd); };
            _running = a;
            if (a.Start(dock)) return true;
            _running = null; SetTransitions(hwnd, false);
        }
        catch (Exception) { _running = null; }
        return false;
    }

    /// <summary>Küçültülmüş pencereyi simgeden büyüterek geri getirir (yalnızca dock'tan Genie ile küçültüldüyse).</summary>
    public static bool Restore(IntPtr hwnd, RECT icon, DockEdge edge, IntPtr dock)
    {
        try
        {
            if (!Ready || !IsIconic(hwnd) || !Cache.TryGetValue(hwnd, out var capture)) return false;
            SetTransitions(hwnd, true);
            var a = new GenieAnimation(capture, icon, edge, expand: true,
                started: null, finished: () => WindowActions.Activate(hwnd), durationMs: 460, lingerMs: 140);
            a.Ended += () => { SetTransitions(hwnd, false); Cache.Remove(hwnd); Order.Remove(hwnd); _running = null; };
            _running = a;
            if (a.Start(dock)) return true;
            _running = null; SetTransitions(hwnd, false);
        }
        catch (Exception) { _running = null; }
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
