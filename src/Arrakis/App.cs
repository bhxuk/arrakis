using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Arrakis.Core;
using Arrakis.Modules;

namespace Arrakis
{
    public static class Program
    {
        private static Mutex _mutex;

        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Any(a => a.Equals("--restore-taskbar", StringComparison.OrdinalIgnoreCase)))
            {
                TaskbarManager.EmergencyRestore();
                return 0;
            }

            _mutex = new Mutex(true, "Arrakis.SingleInstance.bhxuk", out bool isNew);
            if (!isNew)
            {
                // Already running: ask the running copy to open its window.
                try
                {
                    using var ev = EventWaitHandle.OpenExisting("Arrakis.Show.bhxuk");
                    ev.Set();
                }
                catch { }
                return 0;
            }

            var app = new App { Background = args.Any(a => a.Equals("--background", StringComparison.OrdinalIgnoreCase)) };
            int code = app.Run();
            GC.KeepAlive(_mutex);
            return code;
        }
    }

    public class App : Application
    {
        public bool Background { get; set; }
        public static App Instance => (App)Current;

        public Tray Tray { get; private set; }
        public HubWindow Hub { get; private set; }

        public DockModule Dock { get; } = new DockModule();
        public WidgetsModule Widgets { get; } = new WidgetsModule();
        public WallpaperModule Wallpaper { get; } = new WallpaperModule();
        public ClipboardModule Clipboard { get; } = new ClipboardModule();
        public CaptureModule Capture { get; } = new CaptureModule();
        public FocusModule Focus { get; } = new FocusModule();
        public BatteryModule Battery { get; } = new BatteryModule();

        private EventWaitHandle _showEvent;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            DispatcherUnhandledException += (s, ex) =>
            {
                Log.Write("Unhandled: " + ex.Exception);
                ex.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
            {
                Log.Write("Fatal: " + ex.ExceptionObject);
                TaskbarManager.Restore();
            };
            TaskScheduler.UnobservedTaskException += (s, ex) => { Log.Write("Task: " + ex.Exception); ex.SetObserved(); };

            Settings.Load();
            AutoStart.Apply(Settings.Current.StartWithWindows);
            Hotkeys.Init();
            MediaService.Instance.Start();

            Tray = new Tray();
            StartModules();

            // Let a second copy of Arrakis bring this one's window forward.
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Arrakis.Show.bhxuk");
            var t = new Thread(() =>
            {
                while (true)
                {
                    _showEvent.WaitOne();
                    Dispatcher.BeginInvoke(new Action(() => ShowHub()));
                }
            }) { IsBackground = true };
            t.Start();

            if (Settings.Current.FirstRun || !Background) ShowHub();
            if (Settings.Current.FirstRun)
            {
                Settings.Current.FirstRun = false;
                Settings.Save();
            }

            if (Settings.Current.CheckForUpdates)
            {
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
                timer.Tick += async (s, ev) =>
                {
                    timer.Stop();
                    if (await Updates.Check())
                        Tray.Notify("Arrakis " + Updates.LatestVersion + " is available", "Open Arrakis > Settings to download it.");
                };
                timer.Start();
            }
        }

        public IModule[] Modules => new IModule[] { Dock, Widgets, Wallpaper, Clipboard, Capture, Focus, Battery };

        private void StartModules()
        {
            foreach (var m in Modules)
            {
                if (!m.IsEnabled) continue;
                try { m.Start(); }
                catch (Exception ex) { Log.Write(m.Name + " failed to start: " + ex); }
            }
        }

        public void SetModuleEnabled(IModule m, bool on)
        {
            m.IsEnabled = on;
            Settings.Save();
            try
            {
                if (on) m.Start(); else m.Stop();
            }
            catch (Exception ex) { Log.Write(m.Name + " toggle failed: " + ex); }
        }

        public void ShowHub(string page = null)
        {
            if (Hub == null)
            {
                Hub = new HubWindow();
                Hub.Closed += (s, e) => Hub = null;
            }
            if (page != null) Hub.Navigate(page);
            Hub.Show();
            if (Hub.WindowState == WindowState.Minimized) Hub.WindowState = WindowState.Normal;
            Hub.Activate();
        }

        public void Quit()
        {
            foreach (var m in Modules)
            {
                try { if (m.IsRunning) m.Stop(); } catch { }
            }
            TaskbarManager.Restore();
            Hotkeys.UnregisterAll();
            Tray?.Dispose();
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            TaskbarManager.Restore();
            base.OnExit(e);
        }
    }
}
