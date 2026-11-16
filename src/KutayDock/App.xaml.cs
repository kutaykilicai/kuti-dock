using System;
using System.IO;
using System.Threading;
using System.Windows;
using KutayDock.Core;
using Forms = System.Windows.Forms;

namespace KutayDock
{
    public partial class App : Application
    {
        static Mutex _single;
        Services _services;
        DockWindow _dock;
        Forms.NotifyIcon _tray;
        SettingsWindow _settingsWindow;
        static string SettingsPath => Path.Combine(Services.DataDir, "ayarlar.json");

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            _single = new Mutex(true, "KutayDock.TekKopya", out bool first);
            if (!first) { Shutdown(); return; }

            DispatcherUnhandledException += (_, ex) => { Log(ex.Exception); ex.Handled = true; };
            AppDomain.CurrentDomain.UnhandledException += (_, ex) => { Log(ex.ExceptionObject as Exception); AppBar.RestoreTaskbar(); };

            AppBar.ShowAllTaskbars();
            var settings = Settings.Load(SettingsPath);
            if (!File.Exists(SettingsPath)) { try { settings.Save(SettingsPath); } catch (Exception) { } }
            _services = new Services(settings);
            _dock = new DockWindow(_services, OpenSettings, ExitApp, Notify);
            _dock.Show();
            if (settings.AutoHideWindowsTaskbar) AppBar.SetTaskbarAutoHide(true);
            CreateTray();
        }

        void CreateTray()
        {
            System.Drawing.Icon icon;
            try
            {
                var s = GetResourceStream(new Uri("pack://application:,,,/KutayDock;component/Assets/app.ico")).Stream;
                icon = new System.Drawing.Icon(s);
            }
            catch (Exception) { icon = System.Drawing.SystemIcons.Application; }
            _tray = new Forms.NotifyIcon { Icon = icon, Text = "Kutay Dock", Visible = true };
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Ayarlar…", null, (_, _) => OpenSettings());
            menu.Items.Add("Dock'u gizle / göster", null, (_, _) => _dock.ToggleUserHidden());
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
            _settingsWindow = new SettingsWindow(_services.Settings, Apply);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
            _settingsWindow.Activate();
        }

        void Apply(Settings s)
        {
            bool hideChanged = s.AutoHideWindowsTaskbar != _services.Settings.AutoHideWindowsTaskbar;
            try { s.Save(SettingsPath); } catch (Exception ex) { Log(ex); }
            _services.Settings = s;
            _services.InvalidateData();
            if (hideChanged) AppBar.SetTaskbarAutoHide(s.AutoHideWindowsTaskbar);
            _dock.Build();
        }

        void ExitApp()
        {
            try { _services?.Dispose(); } catch (Exception) { }
            AppBar.RestoreTaskbar();
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            _dock?.Close();
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            AppBar.RestoreTaskbar();
            base.OnExit(e);
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
