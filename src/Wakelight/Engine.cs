using System;
using System.Diagnostics;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Wakelight
{
    public enum EngineState { Watching, Counting, Showing, Paused, Disabled, NoWallpaperEngine }

    public class Engine
    {
        public Settings Settings { get; private set; }
        public string WeFolder { get; private set; }
        public WallpaperInfo Wallpaper { get; private set; }
        public EngineState State { get; private set; }
        public double IdleSeconds { get; private set; }
        public DateTime PausedUntil { get; private set; }
        public bool LockScreenPermission { get; private set; }

        public event Action Changed;            // state / countdown changed (every tick)
        public event Action WallpaperChanged;

        readonly DispatcherTimer timer;
        Showcase showcase;
        bool lockWhenClosed;
        double idleAtShow;
        DateTime shownAt;
        DateTime lastFolderProbe = DateTime.MinValue;
        string lastSelected;
        bool snapshotForCurrent;
        HwndSource hotkeyWindow;
        const int HotkeyId = 0x5741;

        public Engine()
        {
            Settings = Settings.Load();
            PausedUntil = DateTime.MinValue;
            LockScreenPermission = LockScreen.HasPermission();
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (s, e) => Tick();
        }

        public void Start()
        {
            Showcase.EnsureIconsVisible();
            ApplyStartup();
            RegisterHotkey();
            Tick();
            timer.Start();
            Log.Write("engine started");
        }

        public void Stop()
        {
            timer.Stop();
            if (showcase != null) showcase.Close("exit");
            UnregisterHotkey();
            Showcase.EnsureIconsVisible();
        }

        // ------------------------------------------------------------ actions
        public void SaveSettings()
        {
            Settings.Save();
            ApplyStartup();
            RegisterHotkey();
            lastFolderProbe = DateTime.MinValue;
            Tick();
        }

        public void ShowNow(bool lockOnReturn)
        {
            if (showcase != null) return;
            Open(Native.IdleSeconds(), lockOnReturn);
        }

        public void LockNow()
        {
            if (showcase != null) showcase.Close("lock now");
            Native.LockWorkStation();
        }

        public void Pause(TimeSpan? duration)
        {
            PausedUntil = duration.HasValue ? DateTime.Now + duration.Value : DateTime.MaxValue;
            Log.Write("paused until " + PausedUntil);
            Tick();
        }

        public void Resume() { PausedUntil = DateTime.MinValue; Tick(); }

        public bool IsPaused { get { return DateTime.Now < PausedUntil; } }

        /// <summary>Shows the scene for a moment to grab a sharp lock screen still.</summary>
        public void RefreshLockScreenNow()
        {
            if (showcase != null) return;
            var sc = new Showcase(false, Settings.HideDesktopIcons, LockScreen.NewSnapshotPath());
            sc.SnapshotTaken += p => { LockScreen.Apply(p); snapshotForCurrent = true; sc.Close("snapshot"); };
            sc.Closed += r => { if (showcase == sc) showcase = null; };
            showcase = sc;
            if (!sc.Open(Wallpaper)) sc.Close("no live render");
        }

        // --------------------------------------------------------------- tick
        void Tick()
        {
            IdleSeconds = Native.IdleSeconds();
            TrackWallpaper();

            if (showcase != null) State = EngineState.Showing;
            else if (!Settings.Enabled) State = EngineState.Disabled;
            else if (IsPaused) State = EngineState.Paused;
            else if (WeFolder == null) State = EngineState.NoWallpaperEngine;
            else State = IdleSeconds >= 30 ? EngineState.Counting : EngineState.Watching;

            if ((State == EngineState.Watching || State == EngineState.Counting) && Native.OnDefaultDesktop())
            {
                double showAt = Settings.ShowcaseAfterMinutes * 60;
                double lockAt = Settings.LockAfterMinutes * 60;
                bool blocked = Settings.SkipFullscreenApps && FullscreenAppInFront();
                if (!blocked && IdleSeconds >= showAt && (lockAt <= 0 || lockAt > showAt))
                    Open(IdleSeconds, false);
                else if (!blocked && lockAt > 0 && lockAt <= showAt && IdleSeconds >= lockAt)
                {
                    Log.Write("lock: idle reached lock time before showcase time");
                    Native.LockWorkStation();
                }
            }
            if (Changed != null) Changed();
        }

        /// <summary>Seconds until the showcase appears (or -1).</summary>
        public double SecondsToShowcase
        {
            get
            {
                if (State != EngineState.Watching && State != EngineState.Counting) return -1;
                return Math.Max(0, Settings.ShowcaseAfterMinutes * 60 - IdleSeconds);
            }
        }

        void Open(double idle, bool lockOnReturn)
        {
            lockWhenClosed = lockOnReturn;
            idleAtShow = idle;
            shownAt = DateTime.Now;
            string snap = Settings.SyncLockScreen && LockScreenPermission && !snapshotForCurrent ? LockScreen.NewSnapshotPath() : null;
            var sc = new Showcase(Settings.ShowClock, Settings.HideDesktopIcons, snap);
            sc.SnapshotTaken += p => { if (LockScreen.Apply(p)) snapshotForCurrent = true; };
            sc.Closed += OnShowcaseClosed;
            showcase = sc;
            sc.Open(Wallpaper);
            State = EngineState.Showing;
        }

        void OnShowcaseClosed(string reason)
        {
            showcase = null;
            double totalIdle = idleAtShow + (DateTime.Now - shownAt).TotalSeconds;
            bool doLock = reason != "exit" && (lockWhenClosed ||
                (Settings.LockAfterMinutes > 0 && totalIdle >= Settings.LockAfterMinutes * 60));
            Log.Write("showcase ended (" + reason + "), idle " + totalIdle.ToString("0") + "s, lock=" + doLock);
            if (doLock) Native.LockWorkStation();
            Tick();
        }

        void TrackWallpaper()
        {
            if (WeFolder == null || (DateTime.Now - lastFolderProbe).TotalSeconds > 30)
            {
                lastFolderProbe = DateTime.Now;
                string f = WallpaperEngine.FindFolder(Settings.WallpaperEngineDir);
                if (f != WeFolder) { WeFolder = f; lastSelected = null; Log.Write("Wallpaper Engine folder: " + (f ?? "not found")); }
                LockScreenPermission = LockScreen.HasPermission();
            }
            if (WeFolder == null) { if (Wallpaper != null) { Wallpaper = null; Raise(); } return; }

            string sel = WallpaperEngine.SelectedFile(WeFolder);
            if (sel == null || sel == lastSelected) return;
            lastSelected = sel;
            Wallpaper = WallpaperEngine.Describe(sel);
            snapshotForCurrent = false;
            Log.Write("wallpaper: " + (Wallpaper == null ? "-" : Wallpaper.Title + " (" + Wallpaper.Type + ")"));
            // Until a sharp snapshot is taken during the next showcase, use the preview.
            if (Settings.SyncLockScreen && LockScreenPermission && Wallpaper != null && Wallpaper.PreviewPath != null)
                LockScreen.Apply(Wallpaper.PreviewPath);
            Raise();
        }

        void Raise() { if (WallpaperChanged != null) WallpaperChanged(); }

        static bool FullscreenAppInFront()
        {
            IntPtr fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;
            string c = Native.ClassOf(fg);
            if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd") return false;
            Native.RECT r; Native.GetWindowRect(fg, out r);
            var scr = Forms.Screen.FromHandle(fg).Bounds;
            return r.Left <= scr.Left && r.Top <= scr.Top && r.Right >= scr.Right && r.Bottom >= scr.Bottom;
        }

        // ----------------------------------------------------------- startup
        static string ExePath { get { return Process.GetCurrentProcess().MainModule.FileName; } }

        void ApplyStartup()
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (Settings.StartWithWindows) k.SetValue("Wakelight", "\"" + ExePath + "\" --tray");
                    else k.DeleteValue("Wakelight", false);
                }
            }
            catch (Exception ex) { Log.Write("startup entry failed: " + ex.Message); }
        }

        // ------------------------------------------------------------ hotkey
        void RegisterHotkey()
        {
            if (hotkeyWindow == null)
            {
                var p = new HwndSourceParameters("WakelightHotkey") { Width = 0, Height = 0, WindowStyle = 0 };
                hotkeyWindow = new HwndSource(p);
                hotkeyWindow.AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) =>
                {
                    if (msg == 0x0312 && w.ToInt32() == HotkeyId) { handled = true; ShowNow(true); }
                    return IntPtr.Zero;
                });
            }
            Native.UnregisterHotKey(hotkeyWindow.Handle, HotkeyId);
            if (Settings.HotkeyKey != 0 &&
                !Native.RegisterHotKey(hotkeyWindow.Handle, HotkeyId, (uint)Settings.HotkeyModifiers | 0x4000 /*NOREPEAT*/, (uint)Settings.HotkeyKey))
                Log.Write("hotkey registration failed (in use by another app?)");
        }

        void UnregisterHotkey()
        {
            if (hotkeyWindow != null) Native.UnregisterHotKey(hotkeyWindow.Handle, HotkeyId);
        }

        public static string HotkeyText(int mods, int key)
        {
            if (key == 0) return "None";
            string s = "";
            if ((mods & 2) != 0) s += "Ctrl + ";
            if ((mods & 1) != 0) s += "Alt + ";
            if ((mods & 4) != 0) s += "Shift + ";
            if ((mods & 8) != 0) s += "Win + ";
            var k = System.Windows.Input.KeyInterop.KeyFromVirtualKey(key);
            string ks = k.ToString();
            if (ks.Length == 2 && ks[0] == 'D' && char.IsDigit(ks[1])) ks = ks.Substring(1);
            return s + ks;
        }
    }
}
