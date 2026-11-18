using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace Wakelight
{
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
            return s;
        }

        public static string Folder
        {
            get
            {
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
                        return s;
                    }
            }
            catch (Exception ex) { Log.Write("settings load failed: " + ex.Message); }
            return Defaults();
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
