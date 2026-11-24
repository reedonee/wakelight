using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace Wakelight
{
    /// <summary>
    /// Demo mode (WAKELIGHT_DEMO=&lt;folder&gt;, see tools\make-demo.ps1): separate settings,
    /// a fake Wallpaper Engine folder, no system changes. Used for screenshots.
    /// </summary>
    public static class Demo
    {
        public static readonly string Folder = Environment.GetEnvironmentVariable("WAKELIGHT_DEMO");
        public static bool On { get { return !string.IsNullOrEmpty(Folder); } }
    }

    [DataContract]
    public class Settings
    {
        [DataMember] public bool Enabled;
        [DataMember] public double ShowcaseAfterMinutes;
        [DataMember] public double LockAfterMinutes;        // 0 = never lock
        [DataMember] public bool ShowClock;
        [DataMember] public bool SyncLockScreen;
        [DataMember] public bool SkipFullscreenApps;
        [DataMember] public bool StartWithWindows;
        [DataMember] public bool HideDesktopIcons;
        [DataMember] public int HotkeyModifiers;            // MOD_ALT=1, CTRL=2, SHIFT=4, WIN=8
        [DataMember] public int HotkeyKey;                  // virtual-key code, 0 = none
        [DataMember] public string WallpaperEngineDir;      // empty = auto-detect
        [DataMember] public bool FirstRunDone;

        // ---- multi-monitor
        [DataMember] public string DisplayMode;             // span | mirror | spotlight | native
        [DataMember] public bool WidgetsOnAllMonitors;

        // ---- widgets
        [DataMember] public string ClockStyle;              // classic | bold | stacked | glow | analog | none
        [DataMember] public bool Clock24h;
        [DataMember] public bool ClockSeconds;
        [DataMember] public string WidgetPosition;          // bottomleft | bottomright | center | topleft | topright
        [DataMember] public double WidgetScale;
        [DataMember] public string Caption;                 // supports {date} {day} {time} {wallpaper} {away}
        [DataMember] public string Accent;                  // ember | rose | ice | mint | white
        [DataMember] public bool ShowAway;
        [DataMember] public bool ShowWallpaperChip;
        [DataMember] public bool ShowBrand;
        [DataMember] public bool ShowWeather;
        [DataMember] public string WeatherCity;
        [DataMember] public bool WeatherFahrenheit;
        [DataMember] public bool WidgetsVersion;            // set once the widget defaults were applied

        // ---- window
        [DataMember] public double WindowWidth;
        [DataMember] public double WindowHeight;
        [DataMember] public bool WindowMaximized;

        /// <summary>Fill in defaults for fields added after 1.0 (old settings files lack them).</summary>
        public void Upgrade()
        {
            if (string.IsNullOrEmpty(DisplayMode)) DisplayMode = "span";
            if (!WidgetsVersion)
            {
                WidgetsVersion = true;
                ClockStyle = "classic"; Clock24h = true; ClockSeconds = false;
                WidgetPosition = "bottomleft"; WidgetScale = 1.0; Caption = "{date}";
                Accent = "ember"; ShowAway = true; ShowWallpaperChip = false; ShowBrand = true;
                ShowWeather = false; WeatherCity = ""; WeatherFahrenheit = false;
            }
            if (WidgetScale <= 0.3) WidgetScale = 1.0;
            if (Caption == null) Caption = "";
            if (WeatherCity == null) WeatherCity = "";
            if (string.IsNullOrEmpty(ClockStyle)) ClockStyle = "classic";
            if (string.IsNullOrEmpty(WidgetPosition)) WidgetPosition = "bottomleft";
            if (string.IsNullOrEmpty(Accent)) Accent = "ember";
        }

        public static Settings Defaults()
        {
            var s = new Settings();
            s.Enabled = true;
            s.ShowcaseAfterMinutes = 5;
            s.LockAfterMinutes = 10;
            s.ShowClock = true;
            s.SyncLockScreen = true;
            s.SkipFullscreenApps = true;
            s.StartWithWindows = true;
            s.HideDesktopIcons = true;
            s.HotkeyModifiers = 2 | 1;      // Ctrl+Alt
            s.HotkeyKey = 0x4C;             // L
            s.WallpaperEngineDir = "";
            s.Upgrade();
            return s;
        }

        public static string Folder
        {
            get
            {
                if (Demo.On) return Path.Combine(Demo.Folder, "data");
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Wakelight");
            }
        }

        static string FilePath { get { return Path.Combine(Folder, "settings.json"); } }

        public static Settings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    using (var fs = File.OpenRead(FilePath))
                    {
                        var s = (Settings)new DataContractJsonSerializer(typeof(Settings)).ReadObject(fs);
                        if (s.ShowcaseAfterMinutes <= 0) s.ShowcaseAfterMinutes = 5;
                        if (s.WallpaperEngineDir == null) s.WallpaperEngineDir = "";
                        s.Upgrade();
                        return s;
                    }
            }
            catch (Exception ex) { Log.Write("settings load failed: " + ex.Message); }
            var d = Defaults();
            if (Demo.On) d.WallpaperEngineDir = Path.Combine(Demo.Folder, "WallpaperEngine");
            return d;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                string tmp = FilePath + ".tmp";
                using (var fs = File.Create(tmp))
                    new DataContractJsonSerializer(typeof(Settings)).WriteObject(fs, this);
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
            }
            catch (Exception ex) { Log.Write("settings save failed: " + ex.Message); }
        }
    }

    public static class Log
    {
        static readonly object gate = new object();
        public static string PathName { get { return System.IO.Path.Combine(Settings.Folder, "wakelight.log"); } }

        public static void Write(string msg)
        {
            lock (gate)
            {
                try
                {
                    Directory.CreateDirectory(Settings.Folder);
                    var fi = new FileInfo(PathName);
                    if (fi.Exists && fi.Length > 1024 * 1024) fi.Delete();
                    File.AppendAllText(PathName, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine);
                }
                catch { }
            }
        }
    }
}
