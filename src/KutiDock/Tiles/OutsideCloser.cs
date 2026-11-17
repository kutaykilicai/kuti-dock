using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using KutiDock.Core;

namespace KutiDock.Tiles
{
    /// <summary>
    /// Dock'tan açılan panel ve menüleri dışarı tıklanınca ya da Esc'e basılınca kapatır. Dock hiç odak almadığı için
    /// WPF'nin kendi "dışarı tıklayınca kapan"ı masaüstü/başka pencere/ikinci monitör tıklamalarını görmüyordu.
    /// Kancalar yalnızca bir panel açıkken kuruludur; geri çağrı yalnızca tıklama/tuş anında birkaç denetim yapar.
    /// </summary>
    internal static class OutsideCloser
    {
        sealed class Entry { public Action Close; public FrameworkElement Anchor; }
        static readonly List<Entry> Open = new();

        delegate IntPtr HookProc(int code, IntPtr w, IntPtr l);
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr mod, uint tid);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Native.POINT p);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
        static readonly HookProc MouseProc = OnMouse, KeyProc = OnKey; // yerel kod çağırır: canlı tutulmalı
        static IntPtr _mouse, _key;

        public static void Attach(Popup p)
        {
            Entry e = null;
            p.Opened += (_, _) => { Remove(e); e = new Entry { Close = () => p.IsOpen = false, Anchor = p.PlacementTarget as FrameworkElement }; Add(e); };
            p.Closed += (_, _) => Remove(e);
        }

        public static void Attach(ContextMenu m)
        {
            Entry e = null;
            m.Opened += (_, _) => { Remove(e); e = new Entry { Close = () => m.IsOpen = false, Anchor = m.PlacementTarget as FrameworkElement }; Add(e); };
            m.Closed += (_, _) => Remove(e);
        }

        static void Add(Entry e)
        {
            Open.Add(e);
            if (_mouse != IntPtr.Zero) return;
            var mod = GetModuleHandle(null);
            _mouse = SetWindowsHookEx(14 /*WH_MOUSE_LL*/, MouseProc, mod, 0);
            _key = SetWindowsHookEx(13 /*WH_KEYBOARD_LL*/, KeyProc, mod, 0);
        }

        static void Remove(Entry e)
        {
            if (e == null || !Open.Remove(e) || Open.Count > 0) return;
            if (_mouse != IntPtr.Zero) UnhookWindowsHookEx(_mouse);
            if (_key != IntPtr.Zero) UnhookWindowsHookEx(_key);
            _mouse = _key = IntPtr.Zero;
        }

        static IntPtr OnMouse(int code, IntPtr w, IntPtr l)
        {
            try
            {
                if (code >= 0 && OutsideClick.IsPress((int)w) && Open.Count > 0)
                {
                    var pt = new Native.POINT { X = Marshal.ReadInt32(l), Y = Marshal.ReadInt32(l, 4) };
                    var root = GetAncestor(WindowFromPoint(pt), 2 /*GA_ROOT*/);
                    bool popup = false;
                    Native.GetWindowThreadProcessId(root, out uint pid);
                    if (pid == (uint)Environment.ProcessId) popup = HwndSource.FromHwnd(root)?.RootVisual?.GetType().Name == "PopupRoot";
                    foreach (var e in Open.ToArray())
                    {
                        bool dock = e.Anchor != null && PresentationSource.FromVisual(e.Anchor) is HwndSource hs && hs.Handle == root;
                        if (OutsideClick.Closes((int)w, popup, dock)) Later(e.Close);
                    }
                }
            }
            catch (Exception) { }
            return CallNextHookEx(_mouse, code, w, l);
        }

        static IntPtr OnKey(int code, IntPtr w, IntPtr l)
        {
            try
            {
                if (code >= 0 && Open.Count > 0 && OutsideClick.IsEscape((int)w, Marshal.ReadInt32(l)))
                    foreach (var e in Open.ToArray()) Later(e.Close);
            }
            catch (Exception) { }
            return CallNextHookEx(_key, code, w, l);
        }

        // Kancadan hemen dön (Windows yavaş kancayı atar); kapatma arayüz iş parçacığında sonra
        static void Later(Action a) => Dispatcher.CurrentDispatcher.BeginInvoke(a, DispatcherPriority.Input);
    }
}
