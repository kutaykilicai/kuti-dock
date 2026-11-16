using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using KutayDock.Core;

namespace KutayDock.Tiles
{
    /// <summary>
    /// Everything ile arka planda dosya arama (belgelenmiş IPC, WM_COPYDATA). Everything başlatılmaz, penceresi açılmaz,
    /// ayarlarına dokunulmaz; çalışmıyorsa "Everything çalışmıyor" denir.
    /// </summary>
    internal sealed class EverythingClient : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] struct COPYDATASTRUCT { public IntPtr dwData; public int cbData; public IntPtr lpData; }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string name);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, ref COPYDATASTRUCT l);
        const int WM_COPYDATA = 0x4A;

        readonly HwndSource _sink;   // görünmez, yalnızca ileti alan pencere (HWND_MESSAGE)
        uint _id = 1000;
        public event Action<List<SearchHit>> Results;

        public EverythingClient()
        {
            _sink = new HwndSource(new HwndSourceParameters("KutayDockEverything") { ParentWindow = new IntPtr(-3), WindowStyle = 0 });
            _sink.AddHook(Hook);
        }

        public static bool Running => FindWindow(EverythingIpc.WindowClass, null) != IntPtr.Zero;

        /// <summary>Sorguyu gönderir; sonuç Results olayıyla gelir. Everything yoksa false.</summary>
        public bool Query(string text, int max = 20)
        {
            var ev = FindWindow(EverythingIpc.WindowClass, null);
            if (ev == IntPtr.Zero) return false;
            var data = EverythingIpc.BuildQuery((uint)_sink.Handle.ToInt64(), ++_id, text, max);
            var p = Marshal.AllocHGlobal(data.Length);
            try
            {
                Marshal.Copy(data, 0, p, data.Length);
                var cds = new COPYDATASTRUCT { dwData = new IntPtr(EverythingIpc.CopyDataQueryW), cbData = data.Length, lpData = p };
                return SendMessage(ev, WM_COPYDATA, _sink.Handle, ref cds) != IntPtr.Zero;
            }
            finally { Marshal.FreeHGlobal(p); }
        }

        IntPtr Hook(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg != WM_COPYDATA) return IntPtr.Zero;
            var cds = Marshal.PtrToStructure<COPYDATASTRUCT>(l);
            if ((uint)cds.dwData.ToInt64() != _id) { handled = true; return new IntPtr(1); } // eski sorgunun yanıtı
            var b = new byte[cds.cbData];
            Marshal.Copy(cds.lpData, b, 0, cds.cbData);
            var hits = EverythingIpc.ParseList(b);
            handled = true;
            Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => Results?.Invoke(hits)));
            return new IntPtr(1);
        }

        public void Dispose() => _sink.Dispose();
    }

    /// <summary>Dock'taki büyüteç; tıklayınca arama paneli.</summary>
    internal sealed class SearchTile : Tile
    {
        public override string Id => "search";
        public SearchTile(Services s, Theme t) : base(s, t, card: false)
        {
            Width = 42 * K; Height = 42 * K; CornerRadius = new CornerRadius(10 * K); Background = Brushes.Transparent;
            Child = Ui.Glyph("M9.5,3A6.5,6.5 0 0,1 16,9.5C16,11.11 15.41,12.59 14.44,13.73L14.71,14H15.5L20.5,19L19,20.5L14,15.5V14.71L13.73,14.44C12.59,15.41 11.11,16 9.5,16A6.5,6.5 0 0,1 3,9.5A6.5,6.5 0 0,1 9.5,3M9.5,5C7,5 5,7 5,9.5C5,12 7,14 9.5,14C12,14 14,12 14,9.5C14,7 12,5 9.5,5Z", t.Fg, 20 * K);
            ToolTip = "Ara (Everything)";
            MouseEnter += (_, _) => Background = t.TileHover; MouseLeave += (_, _) => Background = Brushes.Transparent;
            OnClick(() => SearchPanel.Toggle(this, S.Settings.Edge));
            Ui.PressEffect(this);
        }
        public override void Refresh() { }
    }

    /// <summary>Windows arama kutusu gibi küçük koyu panel: yazdıkça ilk 20 sonuç (simge + ad + klasör).</summary>
    internal sealed class SearchPanel : Window
    {
        static SearchPanel _current;
        public static void Toggle(FrameworkElement anchor, DockEdge edge)
        {
            if (_current != null) { _current.Close(); return; }
            _current = new SearchPanel(anchor, edge);
            _current.Show();
            _current.Activate();
            Native.Activate(new WindowInteropHelper(_current).Handle); // dock odak almadığı için klavyeyi panele ver
        }

        readonly FrameworkElement _anchor;
        readonly DockEdge _edge;
        readonly TextBox _box;
        readonly ListBox _list;
        readonly TextBlock _status;
        readonly EverythingClient _ev = new();
        readonly DispatcherTimer _typing = new() { Interval = TimeSpan.FromMilliseconds(120) };
        bool _closing;

        SearchPanel(FrameworkElement anchor, DockEdge edge)
        {
            _anchor = anchor; _edge = edge;
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; Width = 520; SizeToContent = SizeToContent.Height;
            FontFamily = Theme.Font; Left = -32000; Top = -32000;

            _box = new TextBox
            {
                FontSize = 15, Foreground = Brushes.White, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                CaretBrush = Brushes.White, Margin = new Thickness(4, 2, 4, 8), VerticalContentAlignment = VerticalAlignment.Center,
            };
            _status = new TextBlock { Foreground = Theme.B("#9A9AA0"), FontSize = 12, Margin = new Thickness(6, 2, 6, 4), Text = "Yaz: Everything ile ara · Enter aç · Ctrl+Enter klasörde göster · Esc kapat" };
            _list = new ListBox
            {
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Brushes.White, MaxHeight = 460,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
            _list.MouseLeftButtonUp += (_, e) => { if (_list.SelectedItem is ListBoxItem { Tag: SearchHit h }) Open(h, Keyboard.Modifiers.HasFlag(ModifierKeys.Control)); };

            var stack = new StackPanel();
            stack.Children.Add(_box); stack.Children.Add(_status); stack.Children.Add(_list);
            Content = new Border
            {
                Background = Theme.B("#F21C1C1F"), BorderBrush = Theme.B("#33FFFFFF"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12), Padding = new Thickness(10), Child = stack,
            };

            _box.TextChanged += (_, _) => { _typing.Stop(); _typing.Start(); };
            _typing.Tick += (_, _) => { _typing.Stop(); Search(); };
            _ev.Results += Show;
            PreviewKeyDown += Keys;
            Deactivated += (_, _) => { if (!_closing) Close(); };
            ContentRendered += (_, _) => { Place(); _box.Focus(); };
            SizeChanged += (_, _) => Place();
            if (!EverythingClient.Running) _status.Text = "Everything çalışmıyor (Kutay Dock onu başlatmaz).";
        }

        void Search()
        {
            var q = _box.Text.Trim();
            if (q.Length == 0) { _list.Items.Clear(); return; }
            if (!_ev.Query(q, 20)) { _list.Items.Clear(); _status.Text = "Everything çalışmıyor (Kutay Dock onu başlatmaz)."; }
        }

        void Show(List<SearchHit> hits)
        {
            _list.Items.Clear();
            _status.Text = hits.Count == 0 ? "Sonuç yok" : $"{hits.Count} sonuç · Enter aç · Ctrl+Enter klasörde göster";
            foreach (var h in hits)
            {
                var icon = new Image { Width = 20, Height = 20, Margin = new Thickness(0, 0, 10, 0), Source = ShellIcons.Get(h.FullPath, 32) ?? ShellIcons.Generic, VerticalAlignment = VerticalAlignment.Center };
                var text = new StackPanel();
                text.Children.Add(new TextBlock { Text = h.Name, Foreground = Brushes.White, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis });
                text.Children.Add(new TextBlock { Text = h.Folder, Foreground = Theme.B("#9A9AA0"), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
                var row = new DockPanel { Margin = new Thickness(2, 3, 2, 3) };
                DockPanel.SetDock(icon, Dock.Left); row.Children.Add(icon); row.Children.Add(text);
                _list.Items.Add(new ListBoxItem { Content = row, Tag = h, ToolTip = h.FullPath, Cursor = Cursors.Hand });
            }
            if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        }

        void Keys(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape: Close(); e.Handled = true; break;
                case Key.Down: Move(1); e.Handled = true; break;
                case Key.Up: Move(-1); e.Handled = true; break;
                case Key.Enter:
                    if (_list.SelectedItem is ListBoxItem { Tag: SearchHit h }) Open(h, Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
                    e.Handled = true; break;
            }
        }

        void Move(int d)
        {
            if (_list.Items.Count == 0) return;
            _list.SelectedIndex = Math.Clamp(_list.SelectedIndex + d, 0, _list.Items.Count - 1);
            _list.ScrollIntoView(_list.SelectedItem);
        }

        void Open(SearchHit h, bool inFolder)
        {
            try
            {
                var psi = inFolder
                    ? new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{h.FullPath}\"")
                    : new System.Diagnostics.ProcessStartInfo(h.FullPath);
                psi.UseShellExecute = true;
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception) { }
            Close();
        }

        void Place()
        {
            if (!_anchor.IsVisible) return;
            var a = _anchor.PointToScreen(new Point(0, 0));
            var b = _anchor.PointToScreen(new Point(_anchor.ActualWidth, _anchor.ActualHeight));
            var dpi = VisualTreeHelper.GetDpi(this);
            var wa = SystemParameters.WorkArea;
            (Left, Top) = DockLayout.Popup(_edge, a.X / dpi.DpiScaleX, a.Y / dpi.DpiScaleY, (b.X - a.X) / dpi.DpiScaleX, (b.Y - a.Y) / dpi.DpiScaleY,
                ActualWidth, ActualHeight, 10, wa.X, wa.Y, wa.Width, wa.Height);
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e) { _closing = true; base.OnClosing(e); }

        protected override void OnClosed(EventArgs e)
        {
            _typing.Stop(); _ev.Dispose();
            if (_current == this) _current = null;
            base.OnClosed(e);
        }
    }
}
