using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Arrakis.Core
{
    public class DockSettings
    {
        public bool Enabled { get; set; } = true;
        public string Style { get; set; } = "Floating";   // Floating, Pill, Edge
        public bool HideTaskbar { get; set; } = false;
        public bool AutoHideWhenMaximised { get; set; } = true;
        public bool Magnify { get; set; } = true;
        public int IconSize { get; set; } = 48;
        public bool ShowLiveActivities { get; set; } = true;
        public List<string> Pinned { get; set; } = new List<string>();
        public bool PinnedSeeded { get; set; } = false;
        public List<string> Folders { get; set; } = new List<string>();
        public bool FoldersSeeded { get; set; } = false;
        public bool Previews { get; set; } = true;
        public bool WindowCounts { get; set; } = true;
        public bool AllMonitors { get; set; } = false;
    }

    public class WidgetInstance
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Kind { get; set; } = "Clock";
        public double Left { get; set; } = double.NaN;
        public double Top { get; set; } = double.NaN;
        public string Text { get; set; } = "";
        public DateTime? Date { get; set; }
        public string Size { get; set; } = "M";          // S, M, L
        public List<string> List { get; set; } = new List<string>();
        public string Source { get; set; } = "";
    }

    public class WidgetSettings
    {
        public bool Enabled { get; set; } = true;
        public string Style { get; set; } = "Glass";      // Glass, Solid, Outline
        public bool Locked { get; set; } = false;
        public string City { get; set; } = "";
        public double Latitude { get; set; } = double.NaN;
        public double Longitude { get; set; } = double.NaN;
        public bool Fahrenheit { get; set; } = false;
        public List<WidgetInstance> Items { get; set; } = new List<WidgetInstance>();
        public bool Seeded { get; set; } = false;
        public bool SnapToGrid { get; set; } = true;
        public bool MeetingAlerts { get; set; } = true;
        public Dictionary<string, List<WidgetInstance>> Layouts { get; set; } = new Dictionary<string, List<WidgetInstance>>();
    }

    public class WallpaperSettings
    {
        public bool Enabled { get; set; } = false;
        public string Mode { get; set; } = "Auto";        // Auto, Dawn, Day, Sunset, Night, Photo (Bing), Folder, ScenePhotos
        public string OriginalWallpaper { get; set; } = "";
        public bool MatchWeather { get; set; } = true;
        public bool MovingSun { get; set; } = true;
        public string PhotoFolder { get; set; } = "";
        public int PhotoMinutes { get; set; } = 30;
        public Dictionary<string, string> ScenePhotos { get; set; } = new Dictionary<string, string>();
    }

    public class ClipboardSettings
    {
        public bool Enabled { get; set; } = true;
        public int MaxItems { get; set; } = 200;
        public bool SaveImages { get; set; } = true;
        public string Hotkey { get; set; } = "Ctrl+Alt+V";
    }

    public class CaptureSettings
    {
        public bool Enabled { get; set; } = true;
        public string Hotkey { get; set; } = "Ctrl+Alt+S";
        public bool UsePrintScreen { get; set; } = false;
        public bool AutoBlurEmails { get; set; } = true;
        public string Frame { get; set; } = "Sunset";     // None, Sunset, Ocean, Meadow, Light, Dark
        public int Padding { get; set; } = 48;
        public int Corners { get; set; } = 12;
        public bool Shadow { get; set; } = true;
        public string SaveFolder { get; set; } = "";

        // Capture modes and shortcuts
        public string WindowHotkey { get; set; } = "Ctrl+Alt+D";
        public string FullHotkey { get; set; } = "Ctrl+Alt+A";
        public bool IncludeCursor { get; set; } = false;
        public bool ShowMagnifier { get; set; } = true;
        public int LastX { get; set; }
        public int LastY { get; set; }
        public int LastW { get; set; }
        public int LastH { get; set; }

        // What happens after a capture: Editor, Clipboard, Save, ClipboardAndSave
        public string AfterCapture { get; set; } = "Editor";

        // Saving
        public string FilePattern { get; set; } = "Screenshot {yyyy-MM-dd HH.mm.ss}";
        public string Format { get; set; } = "png";      // png, jpg, bmp, gif, tiff
        public int JpegQuality { get; set; } = 90;

        // Editor defaults
        public string Colour { get; set; } = "#E2585F";
        public string Fill { get; set; } = "None";       // None, Soft, Solid
        public int Thickness { get; set; } = 3;
        public int FontSize { get; set; } = 4;
        public bool AnnotationShadow { get; set; } = true;
    }

    public class FocusSettings
    {
        public bool Enabled { get; set; } = true;
        public int FocusMinutes { get; set; } = 25;
        public int BreakMinutes { get; set; } = 5;
        public int LongBreakMinutes { get; set; } = 15;
        public int SessionsPerRound { get; set; } = 4;
        public bool SilenceNotifications { get; set; } = true;
        public bool HideDock { get; set; } = true;
        public bool DimWidgets { get; set; } = true;
        public string Sound { get; set; } = "Off";        // Off, Rain, Ocean, Brown noise, White noise
        public double Volume { get; set; } = 0.4;
        public string Hotkey { get; set; } = "Ctrl+Alt+F";
        public string Label { get; set; } = "Deep work";
    }

    public class BatterySettings
    {
        public bool Enabled { get; set; } = true;
        public bool UnplugReminder { get; set; } = true;
        public int ReminderPercent { get; set; } = 80;
    }

    public class AppSettings
    {
        public int Version { get; set; } = 1;
        public bool FirstRun { get; set; } = true;
        public bool StartWithWindows { get; set; } = true;
        public bool CheckForUpdates { get; set; } = true;
        public bool AutoUpdate { get; set; } = true;
        public string Theme { get; set; } = "Dark";          // Dark, Light, System
        public string Accent { get; set; } = "Amber";
        public bool CommandBar { get; set; } = true;
        public string CommandBarHotkey { get; set; } = "Alt+Space";
        public string CalendarUrl { get; set; } = "";       // an .ics link, used by the calendar widget and Focus
        public DockSettings Dock { get; set; } = new DockSettings();
        public WidgetSettings Widgets { get; set; } = new WidgetSettings();
        public WallpaperSettings Wallpaper { get; set; } = new WallpaperSettings();
        public ClipboardSettings Clipboard { get; set; } = new ClipboardSettings();
        public CaptureSettings Capture { get; set; } = new CaptureSettings();
        public FocusSettings Focus { get; set; } = new FocusSettings();
        public BatterySettings Battery { get; set; } = new BatterySettings();
    }

    /// <summary>Loads and saves settings to %AppData%\Arrakis\settings.json.</summary>
    public static class Settings
    {
        public static AppSettings Current { get; private set; } = new AppSettings();

        public static string Folder { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Arrakis");

        public static string FilePath => Path.Combine(Folder, "settings.json");

        public static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            WriteIndented = true,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
        };

        public static event Action Changed;

        public static void Load()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                if (File.Exists(FilePath))
                {
                    var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json);
                    if (loaded != null) Current = loaded;
                }
            }
            catch (Exception ex)
            {
                Log.Write("Settings load failed: " + ex.Message);
                Current = new AppSettings();
            }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(Current, Json));
                File.Copy(tmp, FilePath, true);
                File.Delete(tmp);
            }
            catch (Exception ex)
            {
                Log.Write("Settings save failed: " + ex.Message);
            }
            Changed?.Invoke();
        }

        public static T LoadFile<T>(string name) where T : new()
        {
            try
            {
                string path = Path.Combine(Folder, name);
                if (File.Exists(path))
                {
                    var v = JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json);
                    if (v != null) return v;
                }
            }
            catch (Exception ex) { Log.Write("Load " + name + " failed: " + ex.Message); }
            return new T();
        }

        public static void SaveFile<T>(string name, T value)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(Path.Combine(Folder, name), JsonSerializer.Serialize(value, Json));
            }
            catch (Exception ex) { Log.Write("Save " + name + " failed: " + ex.Message); }
        }
    }

    public static class Log
    {
        private static readonly object Gate = new object();

        public static void Write(string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Settings.Folder);
                    string path = Path.Combine(Settings.Folder, "arrakis.log");
                    if (File.Exists(path) && new FileInfo(path).Length > 1_000_000) File.Delete(path);
                    File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}
