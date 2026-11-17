// Kuti Dock 1.6.6 (özgün kod, MIT © 2026 Kutay Kılıç): macOS Finder tarzı işlevli üst çubuk menüleri.
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using KutiDock.Core;
using KutiDock.Mac.Core;

namespace KutiDock.Mac.UI;

/// <summary>
/// Öndeki uygulamanın menüleri. Sıra: (1) pencerenin Win32 menüsü (GetMenu; tıklanan öğe WM_COMMAND ile gönderilir),
/// (2) UI Automation ile uygulamanın kendi menü çubuğu (başlığa basınca uygulamanın menüsü açılır),
/// (3) işlevli genel menüler (kısayollar öndeki pencereye gönderilir). Masaüstünde Gezgin (Finder) menüleri.
/// Menü çubuğu etkinleşmeyen bir pencere olduğundan odak uygulamada kalır; komuttan önce pencere yine öne alınır.
/// </summary>
internal sealed class AppMenus
{
    public enum Source { Generic, Win32, Automation }

    /// <summary>Kuti Dock'un menü süslemesi (yay animasyonu, dışarı tıklama/Esc ile kapanma).</summary>
    public static Action<ContextMenu>? Decorate;

    public IntPtr Target { get; }
    public string AppName { get; }
    public Source Kind { get; private set; } = Source.Generic;
    public IReadOnlyList<string> Titles => _titles;
    public event Action? TitlesChanged;

    readonly bool _desktop;
    List<string> _titles = [];
    List<MenuSpec> _generic = [];
    IntPtr _hmenu;
    List<AutomationElement> _uia = [];

    public AppMenus(IntPtr target, string appName, string className, bool desktop)
    {
        Target = target; AppName = appName; _desktop = desktop;
        _generic = GenericMenus.For(appName, desktop, className == "CabinetWClass");
        _titles = _generic.Select(m => m.Title).ToList();
        if (desktop || target == IntPtr.Zero) return;
        _hmenu = GetMenu(target);
        if (_hmenu != IntPtr.Zero && GetMenuItemCount(_hmenu) > 0)
        {
            var t = new List<string>();
            for (int i = 0; i < GetMenuItemCount(_hmenu); i++) t.Add(GenericMenus.SplitWin32(ItemText(_hmenu, i)).Text.Replace("_", ""));
            if (t.Any(s => s.Length > 0)) { Kind = Source.Win32; _titles = t; return; }
            _hmenu = IntPtr.Zero;
        }
        // Chromium/Electron ve UWP pencerelerinde menü çubuğu yok ve ağaç büyük: UI Automation'a hiç girilmez (CPU).
        if (className.StartsWith("Chrome_WidgetWin") || className is "ApplicationFrameWindow" or "CabinetWClass" or "MozillaWindowClass") return;
        var ui = Dispatcher.CurrentDispatcher;
        Task.Run(() =>
        {
            var items = FindAutomationMenu(target);
            if (items.Count == 0) return;
            ui.BeginInvoke(() => { _uia = items.Select(i => i.El).ToList(); _titles = items.Select(i => i.Name).ToList(); Kind = Source.Automation; TitlesChanged?.Invoke(); });
        });
    }

    static List<(AutomationElement El, string Name)> FindAutomationMenu(IntPtr hwnd)
    {
        var found = new List<(AutomationElement, string)>();
        try
        {
            var root = AutomationElement.FromHandle(hwnd);
            var isBar = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuBar);
            // Yalnızca iki kat derine bakılır (menü çubuğu pencerenin hemen altında olur): ucuz ve sınırlı
            var bar = root.FindFirst(TreeScope.Children, isBar);
            if (bar == null)
                foreach (AutomationElement child in root.FindAll(TreeScope.Children, System.Windows.Automation.Condition.TrueCondition))
                    if ((bar = child.FindFirst(TreeScope.Children, isBar)) != null) break;
            if (bar == null) return found;
            foreach (AutomationElement item in bar.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem)))
            {
                var name = item.Current.Name;
                if (!string.IsNullOrWhiteSpace(name) && !item.Current.IsOffscreen) found.Add((item, name));
            }
        }
        catch (Exception) { found.Clear(); }
        return found;
    }

    // ───────────── Açma ─────────────

    /// <summary>Uygulama adı menüsü (Hakkında, Gizle, Diğerlerini gizle, Çık).</summary>
    public void OpenAppMenu(FrameworkElement anchor) => Show(anchor, Build(GenericMenus.AppMenu(AppName).Items));

    public void Open(int index, FrameworkElement anchor)
    {
        if (index < 0 || index >= _titles.Count) return;
        switch (Kind)
        {
            case Source.Win32:
                var sub = GetSubMenu(_hmenu, index);
                if (sub == IntPtr.Zero) { Post(GetMenuItemID(_hmenu, index)); return; }
                // Uygulama öğelerin durumunu (gri/işaretli) güncellesin
                SendMessageTimeout(Target, WM_INITMENUPOPUP, sub, (IntPtr)index, SMTO_ABORTIFHUNG, 150, out _);
                Show(anchor, Win32Items(sub, 0));
                break;
            case Source.Automation:
                var el = _uia[index];
                Activate();
                Task.Run(() =>
                {
                    try
                    {
                        if (el.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var p)) ((ExpandCollapsePattern)p).Expand();
                        else if (el.TryGetCurrentPattern(InvokePattern.Pattern, out var q)) ((InvokePattern)q).Invoke();
                    }
                    catch (Exception) { }
                });
                break;
            default:
                Show(anchor, Build(_generic[index].Items));
                break;
        }
    }

    static void Show(FrameworkElement anchor, IEnumerable<object> items)
    {
        var m = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        foreach (var i in items) m.Items.Add(i);
        if (m.Items.Count == 0) return;
        Decorate?.Invoke(m);
        m.IsOpen = true;
    }

    List<object> Build(IReadOnlyList<MenuEntry> entries)
    {
        var list = new List<object>();
        foreach (var e in entries)
        {
            if (e.IsSeparator) { list.Add(new Separator()); continue; }
            var mi = new MenuItem { Header = e.Text, InputGestureText = e.Shortcut ?? "" };
            var entry = e;
            mi.Click += (_, _) => Later(() => Run(entry));
            list.Add(mi);
        }
        return list;
    }

    List<object> Win32Items(IntPtr menu, int depth)
    {
        var list = new List<object>();
        int n = GetMenuItemCount(menu);
        for (int i = 0; i < n && depth < 4; i++)
        {
            var info = new MENUITEMINFO { cbSize = (uint)Marshal.SizeOf<MENUITEMINFO>(), fMask = MIIM_FTYPE | MIIM_STATE | MIIM_ID | MIIM_SUBMENU };
            if (!GetMenuItemInfo(menu, (uint)i, true, ref info)) continue;
            if ((info.fType & MFT_SEPARATOR) != 0) { list.Add(new Separator()); continue; }
            var (text, shortcut) = GenericMenus.SplitWin32(ItemText(menu, i));
            if (text.Length == 0) continue; // sahibi çizen (owner-draw) metinsiz öğe
            var mi = new MenuItem
            {
                Header = text, InputGestureText = shortcut ?? "",
                IsEnabled = (info.fState & (MFS_DISABLED | MFS_GRAYED)) == 0,
                IsChecked = (info.fState & MFS_CHECKED) != 0,
            };
            if (info.hSubMenu != IntPtr.Zero)
            {
                SendMessageTimeout(Target, WM_INITMENUPOPUP, info.hSubMenu, (IntPtr)i, SMTO_ABORTIFHUNG, 100, out _);
                foreach (var c in Win32Items(info.hSubMenu, depth + 1)) mi.Items.Add(c);
            }
            else
            {
                uint id = info.wID;
                mi.Click += (_, _) => Later(() => Post(id));
            }
            list.Add(mi);
        }
        return list;
    }

    static string ItemText(IntPtr menu, int index)
    {
        var sb = new StringBuilder(256);
        return GetMenuString(menu, (uint)index, sb, sb.Capacity, MF_BYPOSITION) > 0 ? sb.ToString() : "";
    }

    // ───────────── Komutlar ─────────────

    /// <summary>Menü kapanıp odak oturduktan sonra (kısayol öndeki pencereye gitsin).</summary>
    static void Later(Action a)
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
        t.Tick += (_, _) => { t.Stop(); try { a(); } catch (Exception ex) { Log.Error(ex, "Menu command"); } };
        t.Start();
    }

    void Activate() { if (Target != IntPtr.Zero && IsWindow(Target)) WindowActions.Activate(Target); }

    void Post(uint id) { if (IsWindow(Target)) { Activate(); PostMessage(Target, WM_COMMAND, (IntPtr)id, IntPtr.Zero); } }

    void Run(MenuEntry e)
    {
        switch (e.Command)
        {
            case MenuCommand.Keys:
                if (!_desktop) Activate();
                SendKeys(e.Arg);
                break;
            case MenuCommand.Open: Shell(e.Arg == "explorer" ? "explorer.exe" : e.Arg!); break;
            case MenuCommand.About:
                var exe = AppIdentity.FromWindow(Target, ClassOf(Target)).ExePath;
                if (exe != null) Shell("explorer.exe", $"/select,\"{exe}\"");
                break;
            case MenuCommand.Hide: foreach (var w in AppWindows(same: true)) WindowActions.Minimize(w); break;
            case MenuCommand.HideOthers: foreach (var w in AppWindows(same: false)) WindowActions.Minimize(w); break;
            case MenuCommand.Quit: foreach (var w in AppWindows(same: true)) WindowActions.Close(w); break;
            case MenuCommand.CloseWindow: if (IsWindow(Target)) WindowActions.Close(Target); break;
            case MenuCommand.Minimize: if (IsWindow(Target)) WindowActions.Minimize(Target); break;
            case MenuCommand.Zoom:
                if (IsWindow(Target)) PostMessage(Target, WM_SYSCOMMAND, (IntPtr)(IsZoomed(Target) ? SC_RESTORE : SC_MAXIMIZE), IntPtr.Zero);
                break;
            case MenuCommand.BringAllToFront:
                foreach (var w in AppWindows(same: true).AsEnumerable().Reverse()) WindowActions.Activate(w);
                Activate();
                break;
        }
    }

    static void Shell(string file, string? args = null)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file, args ?? "") { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error(ex, "Menu open"); }
    }

    /// <summary>Hedef uygulamanın (aynı AUMID/exe) ya da diğer uygulamaların görünür ana pencereleri, Z sırasıyla.</summary>
    List<IntPtr> AppWindows(bool same)
    {
        var list = new List<IntPtr>();
        if (!IsWindow(Target)) return list;
        var me = AppIdentity.FromWindow(Target, ClassOf(Target));
        uint own = (uint)Environment.ProcessId;
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h) || GetWindow(h, 4 /*GW_OWNER*/) != IntPtr.Zero || GetWindowTextLength(h) == 0) return true;
            if (((long)GetWindowLongPtr(h, -20) & 0x80 /*WS_EX_TOOLWINDOW*/) != 0) return true;
            GetWindowThreadProcessId(h, out uint pid);
            if (pid == own) return true;
            var cls = ClassOf(h);
            if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return true;
            if (AppIdentity.FromWindow(h, cls).Matches(me) == same) list.Add(h);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    static string ClassOf(IntPtr h) { var sb = new StringBuilder(256); GetClassName(h, sb, sb.Capacity); return sb.ToString(); }

    /// <summary>Kısayolu SendInput ile basar ve bırakır (öndeki pencereye gider).</summary>
    public static bool SendKeys(string? shortcut)
    {
        var vks = Shortcut.VirtualKeys(shortcut);
        if (vks == null) return false;
        var inputs = new INPUT[vks.Length * 2];
        for (int i = 0; i < vks.Length; i++)
        {
            inputs[i] = Key(vks[i], false);
            inputs[vks.Length * 2 - 1 - i] = Key(vks[i], true);
        }
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == inputs.Length;
    }

    static INPUT Key(ushort vk, bool up) => new()
    {
        type = 1,
        u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = (up ? 2u : 0u) | (Shortcut.Extended(vk) ? 1u : 0u) } },
    };

    // ───────────── Win32 ─────────────
    const uint WM_COMMAND = 0x0111, WM_SYSCOMMAND = 0x0112, WM_INITMENUPOPUP = 0x0117, SMTO_ABORTIFHUNG = 2;
    const uint SC_MAXIMIZE = 0xF030, SC_RESTORE = 0xF120, MF_BYPOSITION = 0x400;
    const uint MIIM_STATE = 1, MIIM_ID = 2, MIIM_SUBMENU = 4, MIIM_FTYPE = 0x100, MFT_SEPARATOR = 0x800;
    const uint MFS_GRAYED = 3, MFS_DISABLED = 3, MFS_CHECKED = 8;

    [StructLayout(LayoutKind.Sequential)]
    struct MENUITEMINFO
    {
        public uint cbSize, fMask, fType, fState, wID;
        public IntPtr hSubMenu, hbmpChecked, hbmpUnchecked, dwItemData, dwTypeData;
        public uint cch;
        public IntPtr hbmpItem;
    }
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit)] struct InputUnion { [FieldOffset(0)] public KEYBDINPUT ki; [FieldOffset(0)] public MOUSEINPUT mi; }
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public InputUnion u; }
    delegate bool EnumProc(IntPtr h, IntPtr l);

    [DllImport("user32.dll")] static extern IntPtr GetMenu(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int GetMenuItemCount(IntPtr menu);
    [DllImport("user32.dll")] static extern IntPtr GetSubMenu(IntPtr menu, int pos);
    [DllImport("user32.dll")] static extern uint GetMenuItemID(IntPtr menu, int pos);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetMenuString(IntPtr menu, uint item, StringBuilder sb, int max, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMenuItemInfo(IntPtr menu, uint item, bool byPos, ref MENUITEMINFO info);
    [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);
}
