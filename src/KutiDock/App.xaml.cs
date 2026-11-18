using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using KutiDock.Core;
using KutiDock.Tiles;
using Forms = System.Windows.Forms;

namespace KutiDock
{
    public partial class App : Application
    {
        static Mutex _single;
        Services _services;
        DockWindow _dock, _dockRight;           // Kuti widget şeridi (Mac dock açıkken altta sol ve sağ grup)
        KutiDock.Mac.UI.DockWindow _mac;         // Gravitone motoru: macOS dock + menü çubuğu
        KutiDock.Mac.Core.DockConfig _macConfig;
        KutiDock.Mac.UI.MenuBars _classicBar;    // klasik modda da üstteki menü çubuğu (Mac dock açıkken onu Mac dock gösterir)
        bool _barEvents;
        Forms.NotifyIcon _tray;
        SettingsWindow _settingsWindow;
        static string SettingsPath => Path.Combine(Services.DataDir, "ayarlar.json");

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            // Yardımcı kipler (pencere açmaz): görev çubuğu bekçisi ve yönetici gerektiren temizlik
            if (e.Args.Length > 0 && e.Args[0] == TaskbarRemover.GuardArg) { Shutdown(TaskbarRemover.RunGuard(e.Args)); return; }
            if (e.Args.Length > 0 && e.Args[0] == Optimizer.AdminArg) { Shutdown(Optimizer.RunAdmin(e.Args)); return; }
            // Gravitone'un "güvenli tasarım" bekçisi ve acil durum anahtarları (pencere açmaz)
            switch (e.Args.FirstOrDefault())
            {
                case KutiDock.Mac.Core.Guard.SpawnArg: Shutdown(KutiDock.Mac.Core.Guard.RunSpawner(e.Args)); return;
                case KutiDock.Mac.Core.Guard.WatchArg: Shutdown(KutiDock.Mac.Core.Guard.RunGuard(e.Args)); return;
                case "--ses-tani": SesTani(); Shutdown(0); return; // 1.6.6: pencere açmadan ses arayüzü denetimi
                case "--everything-tani": EverythingTani(e.Args); return; // 1.6.14: pencere açmadan Everything uçtan uca denetimi
                case "--restore-taskbar": KutiDock.Mac.Core.TaskbarState.Restore(force: true); Shutdown(0); return;
                case KutiDock.Mac.Core.AutoStart.FallbackArg: KutiDock.Mac.Core.TaskbarState.RestoreIfOrphaned(); Shutdown(0); return;
            }
            _single = new Mutex(true, "KutiDock.TekKopya", out bool first);
            if (!first) { Shutdown(); return; }

            DispatcherUnhandledException += (_, ex) => { Log(ex.Exception); ex.Handled = true; };
            AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            {
                Log(ex.ExceptionObject as Exception); AppBar.RestoreTaskbar(); TaskbarRemover.SetEnabled(false);
                try { LaunchGenie.RevealAll(); } catch (Exception) { } // saydam tutulan yeni pencere görünür kalsın
                KutiDock.Mac.Core.TaskbarState.Restore(onlyForPid: Environment.ProcessId);
            };
            AppDomain.CurrentDomain.ProcessExit += (_, _) => KutiDock.Mac.Core.TaskbarState.Restore(onlyForPid: Environment.ProcessId);
            // Oturum açılışında Explorer görev çubuğunu kurmadan başlamış olabilir: tepsi ve app bar'lar onu ister
            if (e.Args.Contains(KutiDock.Mac.Core.AutoStart.AutoStartArg)) WaitForExplorer();

            // Eski adın (Kutay Dock) ayarları ve Cursor oturumu yeni klasöre bir kez kopyalanır
            try { DataMigration.Run(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KutayDock"), Services.DataDir); } catch (Exception) { }
            TaskbarRemover.RestoreOrphaned();
            AppBar.ShowAllTaskbars();
            var settings = Settings.Load(SettingsPath);
            if (!File.Exists(SettingsPath)) { try { settings.Save(SettingsPath); } catch (Exception) { } }
            CoreAudio.Error = (w, ex) => Log(new Exception("Ses: " + w, ex));
            _services = new Services(settings);
            KutiDock.Mac.UI.AppMenus.Decorate = m => OutsideCloser.Attach(m, DockEdge.Ust); // 1.6.6: üst çubuk menüleri yayla açılır, dışarı tıklama/Esc ile kapanır
            if (settings.MacDock) StartMac();
            else StartClassicMenuBar();
            BuildStrips();
            if (_mac == null)
            {
                if (settings.RemoveWindowsTaskbar) TaskbarRemover.SetEnabled(true);
                else if (settings.AutoHideWindowsTaskbar) AppBar.SetTaskbarAutoHide(true);
            }
            CreateTray();
            // 1.6.14: aramadaki Everything anahtarı açıksa Everything dock'la birlikte arka planda (penceresiz) başlar;
            // açılışı yavaşlatmasın diye 8 sn sonra. Anahtar kapalıysa başlatılmaz.
            if (EverythingPolicy.StartWithDock(settings.SearchWithEverything))
                _ = System.Threading.Tasks.Task.Delay(8000).ContinueWith(_ => Dispatcher.BeginInvoke(new Action(async () =>
                {
                    if (settings.SearchWithEverything) await KutiDock.Tiles.EverythingLauncher.EnsureRunning(waitForDb: false);
                })));
        }

        /// <summary>Mac dock (Gravitone): akrilik plaka, dalga büyütme, Genie, menü çubuğu, yığınlar, Geri Dönüşüm Kutusu, bekçi.</summary>
        void StartMac()
        {
            try
            {
                KutiDock.Mac.Core.Theme.Refresh();
                _macConfig = KutiDock.Mac.Core.DockConfig.LoadOrCreate();
                // Windows ile başlat kaydı varsa Mac dock ayarı da onu göstersin (ayar değişince kayıt silinmesin)
                if (KutiDock.Mac.Core.AutoStart.IsEnabled && !_macConfig.Settings.StartWithWindows) { _macConfig.Settings.StartWithWindows = true; _macConfig.Save(); }
                KutiDock.Mac.Core.Loc.Init(_macConfig.Settings.Language);
                KutiDock.Mac.UI.DockWindow.WindowPreviewRequested = (wins, icon, anchor, edge) =>
                    WindowPreview.Open(wins, wins, icon ?? ShellIcons.Generic, anchor, edge switch
                    {
                        KutiDock.Mac.Core.DockEdge.Left => DockEdge.Sol, KutiDock.Mac.Core.DockEdge.Right => DockEdge.Sag, _ => DockEdge.Alt,
                    });
                KutiDock.Mac.UI.DockWindow.WindowPreviewDismissed = WindowPreview.CloseCurrent;
                KutiDock.Mac.UI.DockWindow.KutiSettingsRequested = OpenSettings;
                // Birleşik alt şerit: Mac dock widget şeritlerine yer bırakır ve onları kendi monitöründe yerleştirir
                KutiDock.Mac.UI.DockWindow.SideRequest = scale => (_dock?.NaturalPx(scale) ?? 0, _dockRight?.NaturalPx(scale) ?? 0);
                KutiDock.Mac.UI.DockWindow.SidesPlaced = (bounds, l, r, mid, bottom, scale) =>
                {
                    _dock?.PlaceAt(bounds.Left, bounds.Right, l, mid, bottom, scale);
                    _dockRight?.PlaceAt(bounds.Left, bounds.Right, r, mid, bottom, scale);
                };
                var backdrop = new KutiDock.Mac.UI.BackdropWindow();
                backdrop.Show();
                _mac = new KutiDock.Mac.UI.DockWindow(_macConfig, backdrop);
                _mac.Show();
            }
            catch (Exception ex) { Log(ex); _mac = null; }
        }

        /// <summary>
        /// Kuti widget şeridi. Mac dock açıkken uygulamalar Mac dock'ta olduğundan "apps" çıkar; menü çubuğu açıksa gizli
        /// simgeler (tray) de çıkar (menü çubuğu tepsiyi gösterir). Altta: "apps"tan öncekiler sol köşede, sonrakiler sağ köşede.
        /// </summary>
        void BuildStrips()
        {
            _dock?.Close(); _dockRight?.Close(); _dock = _dockRight = null;
            var s = _services.Settings;
            if (_mac == null)
            {
                // Klasik dock (1.5 düzeni). Menü çubuğu tepsiyi gösterdiği için "Gizli simgeler" widget'ı o varken çıkar.
                var only = _classicBar != null ? s.Widgets.Where(id => id != "tray").ToList() : null;
                _dock = only == null ? new DockWindow(_services, OpenSettings, ExitApp, Notify) : new DockWindow(_services, OpenSettings, ExitApp, Notify, only, StripAnchor.Kenar);
                _dock.Show();
                return;
            }
            bool menuBar = _macConfig.Settings.MenuBar;
            bool Keep(string id) => id != "apps" && !(menuBar && id == "tray");
            if (s.Edge == DockEdge.Alt && _macConfig.Settings.Edge == KutiDock.Mac.Core.DockEdge.Bottom)
            {
                int split = s.Widgets.IndexOf("apps");
                var left = split < 0 ? new List<string>() : s.Widgets.Take(split).Where(Keep).ToList();
                var right = (split < 0 ? s.Widgets : s.Widgets.Skip(split + 1)).Where(Keep).ToList();
                if (left.Count > 0) { _dock = new DockWindow(_services, OpenSettings, ExitApp, Notify, left, StripAnchor.SolAlt); _dock.Show(); }
                if (right.Count > 0) { _dockRight = new DockWindow(_services, OpenSettings, ExitApp, Notify, right, StripAnchor.SagAlt); _dockRight.Show(); }
                foreach (var d in new[] { _dock, _dockRight }) if (d != null) d.NaturalChanged = () => _mac?.RequestPlace();
                // Şeritler ölçüldükten sonra Mac dock yerleşimi (ve şeritlerin yeri) tazelenir
                Dispatcher.BeginInvoke(new Action(() => _mac?.RequestPlace()), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            else
            {
                _dock = new DockWindow(_services, OpenSettings, ExitApp, Notify, s.Widgets.Where(Keep).ToList(), StripAnchor.Kenar);
                _dock.Show();
            }
        }

        /// <summary>Klasik modda menü çubuğu: Mac dock ayarlarındaki "Menü çubuğu" açıksa (varsayılan açık).</summary>
        void StartClassicMenuBar()
        {
            if (_mac != null || _classicBar != null) return;
            try
            {
                KutiDock.Mac.Core.Theme.Refresh();
                _macConfig ??= KutiDock.Mac.Core.DockConfig.LoadOrCreate();
                KutiDock.Mac.Core.Loc.Init(_macConfig.Settings.Language);
                if (!_macConfig.Settings.MenuBar) return;
                _classicBar = new KutiDock.Mac.UI.MenuBars(_ => null, _macConfig.Settings.MenuBarOnAllDisplays);
                if (_barEvents) return;
                _barEvents = true;
                // Mac dock'un yaptığı gibi: ekran değişimi, uyku/kilit dönüşü, saat ve tema değişimi
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.BeginInvoke(new Action(() => _classicBar?.Sync()));
                Microsoft.Win32.SystemEvents.PowerModeChanged += (_, ev) => { if (ev.Mode == Microsoft.Win32.PowerModes.Resume) Dispatcher.BeginInvoke(new Action(() => _classicBar?.Refresh())); };
                Microsoft.Win32.SystemEvents.SessionSwitch += (_, ev) => Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (ev.Reason is Microsoft.Win32.SessionSwitchReason.SessionLock) _classicBar?.SetPaused(true);
                    else if (ev.Reason is Microsoft.Win32.SessionSwitchReason.SessionUnlock or Microsoft.Win32.SessionSwitchReason.ConsoleConnect) { _classicBar?.SetPaused(false); _classicBar?.Refresh(); }
                }));
                Microsoft.Win32.SystemEvents.TimeChanged += (_, _) => Dispatcher.BeginInvoke(new Action(() => _classicBar?.UpdateClock()));
                Microsoft.Win32.SystemEvents.UserPreferenceChanged += (_, _) => Dispatcher.BeginInvoke(new Action(() => { KutiDock.Mac.Core.Theme.Refresh(); _classicBar?.ApplyTheme(); }));
            }
            catch (Exception ex) { Log(ex); _classicBar = null; }
        }

        void StopClassicMenuBar()
        {
            try { _classicBar?.Dispose(); } catch (Exception ex) { Log(ex); }
            _classicBar = null;
        }

        static void WaitForExplorer()
        {
            var deadline = DateTime.UtcNow.AddSeconds(45);
            while (KutiDock.Mac.Core.TaskbarState.FindExplorerTray() == IntPtr.Zero && DateTime.UtcNow < deadline) Thread.Sleep(200);
            Thread.Sleep(500);
        }

        void CreateTray()
        {
            System.Drawing.Icon icon;
            try
            {
                var s = GetResourceStream(new Uri("pack://application:,,,/KutiDock;component/Assets/app.ico")).Stream;
                icon = new System.Drawing.Icon(s);
            }
            catch (Exception) { icon = System.Drawing.SystemIcons.Application; }
            _tray = new Forms.NotifyIcon { Icon = icon, Text = "Kuti Dock", Visible = true };
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Ayarlar…", null, (_, _) => OpenSettings());
            menu.Items.Add("Widget şeridini gizle / göster", null, (_, _) => { _dock?.ToggleUserHidden(); _dockRight?.ToggleUserHidden(); });
            menu.Items.Add("Ayar klasörünü aç", null, (_, _) => { Directory.CreateDirectory(Services.DataDir); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Services.DataDir) { UseShellExecute = true }); });
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Çıkış", null, (_, _) => ExitApp());
            _tray.ContextMenuStrip = menu;
            _tray.MouseClick += (_, ev) => { if (ev.Button == Forms.MouseButtons.Left) OpenSettings(); };
        }

        void Notify(string title, string body)
        {
            try { _tray?.ShowBalloonTip(6000, title, string.IsNullOrEmpty(body) ? " " : body, Forms.ToolTipIcon.None); } catch (Exception) { }
        }

        void OpenSettings()
        {
            if (_settingsWindow != null) { _settingsWindow.Activate(); return; }
            _settingsWindow = new SettingsWindow(_services.Settings, Apply, _services.Cursor, () => OpenMacSettings());
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
            _settingsWindow.Activate();
        }

        void Apply(Settings s)
        {
            bool hideChanged = s.AutoHideWindowsTaskbar != _services.Settings.AutoHideWindowsTaskbar
                || s.RemoveWindowsTaskbar != _services.Settings.RemoveWindowsTaskbar;
            bool macChanged = s.MacDock != _services.Settings.MacDock;
            try { s.Save(SettingsPath); } catch (Exception ex) { Log(ex); }
            _services.Settings = s;
            _services.InvalidateData();
            if (macChanged)
            {
                if (s.MacDock) { TaskbarRemover.SetEnabled(false); AppBar.SetTaskbarAutoHide(false); StopClassicMenuBar(); StartMac(); }
                else { StopMac(); hideChanged = true; StartClassicMenuBar(); }
            }
            if (hideChanged && _mac == null)
            {
                TaskbarRemover.SetEnabled(false);
                AppBar.SetTaskbarAutoHide(false);
                if (s.RemoveWindowsTaskbar) TaskbarRemover.SetEnabled(true);
                else if (s.AutoHideWindowsTaskbar) AppBar.SetTaskbarAutoHide(true);
            }
            BuildStrips();
        }

        void StopMac()
        {
            if (_mac == null) return;
            try { _mac.ReleaseShell(); _mac.Close(); } catch (Exception ex) { Log(ex); }
            _mac = null;
        }

        /// <summary>Kuti ayarlarından Mac dock ayarlarını (büyütme, Genie, menü çubuğu, ekran…) açar.</summary>
        void OpenMacSettings() { if (_mac != null) _mac.OpenSettings(); }

        void ExitApp()
        {
            try { _services?.Dispose(); } catch (Exception) { }
            AppBar.RestoreTaskbar();
            TaskbarRemover.SetEnabled(false);
            StopMac();
            StopClassicMenuBar();
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            _dock?.Close(); _dockRight?.Close();
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try { _mac?.ReleaseShell(); } catch (Exception) { } // Mac dock menüsünden "Çık" da buraya gelir
            StopClassicMenuBar(); // ayrılan üst şerit ve tepsi geri verilir
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
            AppBar.RestoreTaskbar();
            TaskbarRemover.SetEnabled(false);
            base.OnExit(e);
        }

        /// <summary>
        /// 1.6.14: dock'un gerçek Everything yolu, pencere açmadan: "ac" (anahtar açık: başlat + veritabanı + IPC arama),
        /// "kapat" (anahtar kapalı: yalnızca dock'un başlattığını kapat). Sonuç %TEMP%\kutidock-everything-tani.txt.
        /// </summary>
        async void EverythingTani(string[] args)
        {
            var sb = new System.Text.StringBuilder();
            string mode = args.Length > 1 ? args[1] : "ac", q = args.Length > 2 ? args[2] : "kutay";
            try
            {
                if (mode == "kapat") { KutiDock.Tiles.EverythingLauncher.StopIfStartedByDock(); sb.AppendLine("kapat: istendi"); }
                else
                {
                    sb.AppendLine("calisiyordu: " + KutiDock.Tiles.EverythingClient.Running);
                    var t = System.Diagnostics.Stopwatch.StartNew();
                    bool ok = await KutiDock.Tiles.EverythingLauncher.EnsureRunning();
                    sb.AppendLine($"hazir: {ok} ({t.ElapsedMilliseconds} ms), db: {KutiDock.Tiles.EverythingLauncher.DbLoaded()}, pencere acik: {KutiDock.Tiles.EverythingLauncher.MainWindowVisible()}");
                    using var c = new KutiDock.Tiles.EverythingClient();
                    var roots = KutiDock.Tiles.WindowsSearch.UserRoots();
                    var hits = await c.Search(WinSearch.EverythingQuery(q, roots), 40);
                    var files = hits?.Where(h => WinSearch.AllowedFile(h.FullPath, roots)).Take(8).ToList();
                    sb.AppendLine($"arama '{q}': {(hits == null ? "yanit yok" : hits.Count + " sonuc, gosterilecek " + files.Count)}");
                }
            }
            catch (Exception ex) { sb.AppendLine("HATA: " + ex.GetType().Name + " " + ex.Message); }
            try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "kutidock-everything-tani.txt"), sb.ToString()); } catch (Exception) { }
            Shutdown(0);
        }

        static void SesTani()
        {
            var sb = new System.Text.StringBuilder();
            CoreAudio.Error = (w, ex) => sb.AppendLine($"{w} HATA: {ex.GetType().Name} {ex.Message}");
            var a = new CoreAudio();
            sb.AppendLine("apartment: " + Thread.CurrentThread.GetApartmentState());
            var l = a.Outputs(); sb.AppendLine("aygit: " + l.Count + ", varsayilan: " + l.Count(d => d.IsDefault));
            var r = a.Read(); sb.AppendLine("okuma: " + (r == null ? "yok" : $"{r.Value.level:0.00} sessiz={r.Value.muted}"));
            try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "kutidock-ses-tani.txt"), sb.ToString()); } catch (Exception) { }
        }

        static void Log(Exception ex)
        {
            if (ex == null) return;
            try
            {
                Directory.CreateDirectory(Services.DataDir);
                File.AppendAllText(Path.Combine(Services.DataDir, "hata.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {ex}\n\n");
            }
            catch (Exception) { }
        }
    }
}
