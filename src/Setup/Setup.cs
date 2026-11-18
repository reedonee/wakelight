using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Ellipse = System.Windows.Shapes.Ellipse;
using Path = System.IO.Path;
using Microsoft.Win32;

[assembly: AssemblyTitle("Wakelight Setup")]
[assembly: AssemblyProduct("Wakelight")]
[assembly: AssemblyDescription("Installs Wakelight")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace WakelightSetup
{
    public static class Program
    {
        const string AppName = "Wakelight";
        const string Version = "1.0";
        const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Wakelight";
        const string CspKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\PersonalizationCSP";

        static Window win;
        static bool uninstallMode;
        static string installDir;
        static string userSid;
        static bool done;

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);

        static T F<T>(string n) where T : class { return win.FindName(n) as T; }
        static Stream Res(string n) { return Assembly.GetExecutingAssembly().GetManifestResourceStream(n); }

        [STAThread]
        public static int Main(string[] args)
        {
            uninstallMode = args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase));
            bool silent = args.Any(a => a.Equals("--silent", StringComparison.OrdinalIgnoreCase));
            installDir = ExistingInstallDir() ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppName);
            userSid = InteractiveUserSid();

            if (silent)
            {
                try
                {
                    if (uninstallMode) Uninstall(null, false);
                    else Install(null, true, true, true, true);
                    return 0;
                }
                catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            }

            var app = new Application();
            using (var s = Res("Theme.xaml")) app.Resources = (ResourceDictionary)XamlReader.Load(s);
            using (var s = Res("Setup.xaml")) win = (Window)XamlReader.Load(s);

            var logo = new BitmapImage();
            logo.BeginInit(); logo.StreamSource = Res("wakelight.png"); logo.CacheOption = BitmapCacheOption.OnLoad; logo.EndInit();
            win.Icon = logo;
            F<Image>("BigLogo").Source = logo;
            win.SourceInitialized += (s, e) =>
            {
                IntPtr h = new WindowInteropHelper(win).Handle;
                int on = 1, round = 2, cap = 0x00140A0B, border = 0x00422526;
                DwmSetWindowAttribute(h, 20, ref on, 4); DwmSetWindowAttribute(h, 33, ref round, 4);
                DwmSetWindowAttribute(h, 35, ref cap, 4); DwmSetWindowAttribute(h, 34, ref border, 4);
            };
            win.Loaded += (s, e) => { DrawStars(); Breathe(); };
            win.Closing += (s, e) => { if (F<Grid>("PageProgress").Visibility == Visibility.Visible && !done) e.Cancel = true; };

            F<Button>("CloseButton").Click += (s, e) => win.Close();
            F<TextBlock>("PathText").Text = installDir;
            F<Button>("CustomizeButton").Click += (s, e) => ShowPage("PageOptions");
            F<Button>("BackButton").Click += (s, e) => ShowPage("PageWelcome");
            F<Button>("BrowseButton").Click += (s, e) => Browse();
            F<Button>("InstallButton").Click += (s, e) => { if (uninstallMode) RunUninstall(); else RunInstall(); };
            F<Button>("InstallButton2").Click += (s, e) => RunInstall();
            F<Button>("UninstallButton").Click += (s, e) => RunUninstall();
            F<Button>("FinishButton").Click += (s, e) => Finish();

            bool installed = ExistingInstallDir() != null;
            if (uninstallMode) SetupUninstallWelcome();
            else if (installed)
            {
                F<TextBlock>("WelcomeEyebrow").Text = "ALREADY INSTALLED · VERSION " + Version;
                F<TextBlock>("WelcomeTitle").Text = "Welcome back.";
                F<TextBlock>("WelcomeBody").Text = "Wakelight is already on this PC. Reinstall to repair or update it — your settings stay as they are.";
                F<Button>("InstallButton").Content = "Reinstall";
                F<Button>("UninstallButton").Visibility = Visibility.Visible;
            }

            app.Run(win);
            return 0;
        }

        // ------------------------------------------------------------ UI flow
        static void ShowPage(string name)
        {
            foreach (var p in new[] { "PageWelcome", "PageOptions", "PageProgress", "PageDone" })
            {
                var g = F<Grid>(p);
                if (p == name)
                {
                    g.Visibility = Visibility.Visible;
                    g.Opacity = 0;
                    g.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
                    var tt = new TranslateTransform(0, 12); g.RenderTransform = tt;
                    tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(320))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                }
                else g.Visibility = Visibility.Collapsed;
            }
        }

        static void SetupUninstallWelcome()
        {
            F<TextBlock>("WelcomeEyebrow").Text = "UNINSTALL";
            F<TextBlock>("WelcomeTitle").Text = "Lights out?";
            F<TextBlock>("WelcomeBody").Text = "This removes Wakelight from your PC and gives the lock screen back to Windows. Your wallpapers in Wallpaper Engine are not touched.";
            F<Button>("InstallButton").Content = "Uninstall";
            F<Button>("CustomizeButton").Visibility = Visibility.Collapsed;
        }

        static void Browse()
        {
            using (var d = new System.Windows.Forms.FolderBrowserDialog())
            {
                d.Description = "Choose where to install Wakelight";
                d.SelectedPath = Path.GetDirectoryName(installDir);
                if (d.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    installDir = Path.Combine(d.SelectedPath, AppName);
                    F<TextBlock>("PathText").Text = installDir;
                }
            }
        }

        static async void RunInstall()
        {
            bool startup = F<CheckBox>("OptStartup").IsChecked == true;
            bool lockScreen = F<CheckBox>("OptLockScreen").IsChecked == true;
            bool desktop = F<CheckBox>("OptDesktop").IsChecked == true;
            bool startMenu = F<CheckBox>("OptStartMenu").IsChecked == true;
            ShowPage("PageProgress");
            Action<double, string> report = (p, t) => win.Dispatcher.BeginInvoke(new Action(() => SetProgress(p, t)));
            try
            {
                await Task.Run(() => Install(report, startup, lockScreen, desktop, startMenu));
                await Task.Delay(400);
                done = true;
                ShowPage("PageDone");
            }
            catch (Exception ex) { Fail(ex); }
        }

        static async void RunUninstall()
        {
            uninstallMode = true;
            F<TextBlock>("ProgressEyebrow").Text = "UNINSTALLING";
            F<TextBlock>("ProgressTitle").Text = "Dimming the lights…";
            ShowPage("PageProgress");
            Action<double, string> report = (p, t) => win.Dispatcher.BeginInvoke(new Action(() => SetProgress(p, t)));
            try
            {
                await Task.Run(() => Uninstall(report, true));
                await Task.Delay(400);
                done = true;
                F<TextBlock>("DoneEyebrow").Text = "REMOVED";
                F<TextBlock>("DoneTitle").Text = "Wakelight has been removed.";
                F<TextBlock>("DoneBody").Text = "Your lock screen is back under Windows' control. Thanks for letting Wakelight keep watch.";
                F<CheckBox>("LaunchCheck").Visibility = Visibility.Collapsed;
                ShowPage("PageDone");
            }
            catch (Exception ex) { Fail(ex); }
        }

        static void Fail(Exception ex)
        {
            done = true;
            F<TextBlock>("DoneEyebrow").Text = "SOMETHING WENT WRONG";
            F<TextBlock>("DoneEyebrow").Foreground = (Brush)Application.Current.Resources["Rose"];
            F<TextBlock>("DoneTitle").Text = "Setup couldn't finish.";
            F<TextBlock>("DoneBody").Text = ex.Message;
            F<CheckBox>("LaunchCheck").Visibility = Visibility.Collapsed;
            ShowPage("PageDone");
        }

        static void SetProgress(double p, string text)
        {
            var fill = F<Border>("ProgressFill");
            double target = ((FrameworkElement)fill.Parent).ActualWidth * p;
            fill.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(380))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            F<TextBlock>("ProgressStep").Text = text;
        }

        static void Finish()
        {
            if (!uninstallMode && F<CheckBox>("LaunchCheck").IsChecked == true)
            {
                // explorer.exe starts it as the signed-in user, not elevated like this installer
                try { Process.Start("explorer.exe", "\"" + Path.Combine(installDir, "Wakelight.exe") + "\""); } catch { }
            }
            win.Close();
        }

        static void DrawStars()
        {
            var c = F<Canvas>("Stars");
            var rnd = new Random(7);
            for (int i = 0; i < 46; i++)
            {
                double size = rnd.NextDouble() * 1.8 + 0.6;
                var e = new Ellipse { Width = size, Height = size, Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xF1, 0xDC)), Opacity = rnd.NextDouble() * 0.6 + 0.15 };
                Canvas.SetLeft(e, rnd.NextDouble() * 300); Canvas.SetTop(e, rnd.NextDouble() * 330);
                c.Children.Add(e);
                var tw = new DoubleAnimation(e.Opacity, 0.1, TimeSpan.FromSeconds(1.5 + rnd.NextDouble() * 3))
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(rnd.NextDouble() * 3) };
                e.BeginAnimation(UIElement.OpacityProperty, tw);
            }
        }

        static void Breathe()
        {
            var halo = F<Ellipse>("Halo");
            halo.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.35, 0.75, TimeSpan.FromSeconds(3.2))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
            var st = new ScaleTransform(1, 1, 150, 150); halo.RenderTransform = st;
            var a = new DoubleAnimation(0.92, 1.06, TimeSpan.FromSeconds(3.2)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, a); st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        // ------------------------------------------------------------ install
        static void Step(Action<double, string> r, double p, string t)
        {
            if (r != null) { r(p, t); System.Threading.Thread.Sleep(260); }
        }

        static void Install(Action<double, string> r, bool startup, bool lockScreen, bool desktop, bool startMenu)
        {
            string exe = Path.Combine(installDir, "Wakelight.exe");

            Step(r, 0.08, "Closing running copies");
            StopProcesses();
            CleanupLegacy();

            Step(r, 0.30, "Copying Wakelight");
            Directory.CreateDirectory(installDir);
            using (var src = Res("payload.Wakelight.exe"))
            using (var dst = File.Create(exe)) src.CopyTo(dst);
            string self = Assembly.GetExecutingAssembly().Location;
            string uninst = Path.Combine(installDir, "Uninstall.exe");
            if (!self.Equals(uninst, StringComparison.OrdinalIgnoreCase)) File.Copy(self, uninst, true);

            Step(r, 0.52, "Creating shortcuts");
            string common = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
            string pubDesk = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            if (startMenu) Shortcut(Path.Combine(common, "Wakelight.lnk"), exe, "");
            else TryDelete(Path.Combine(common, "Wakelight.lnk"));
            if (desktop) Shortcut(Path.Combine(pubDesk, "Wakelight.lnk"), exe, "");
            else TryDelete(Path.Combine(pubDesk, "Wakelight.lnk"));

            Step(r, 0.68, "Setting up the lock screen");
            if (lockScreen) GrantLockScreen();

            Step(r, 0.84, "Registering with Windows");
            using (var k = Registry.LocalMachine.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", "Wakelight");
                k.SetValue("DisplayVersion", Version);
                k.SetValue("Publisher", "Wakelight");
                k.SetValue("DisplayIcon", exe);
                k.SetValue("InstallLocation", installDir);
                k.SetValue("UninstallString", "\"" + uninst + "\" --uninstall");
                k.SetValue("QuietUninstallString", "\"" + uninst + "\" --uninstall --silent");
                k.SetValue("EstimatedSize", (int)(new FileInfo(exe).Length / 1024 + 64), RegistryValueKind.DWord);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
            using (var run = UserKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            {
                if (run != null)
                {
                    if (startup) run.SetValue("Wakelight", "\"" + exe + "\" --tray");
                    else run.DeleteValue("Wakelight", false);
                }
            }
            Step(r, 1.0, "Done");
        }

        static void Uninstall(Action<double, string> r, bool deleteFolderLater)
        {
            Step(r, 0.15, "Closing Wakelight");
            StopProcesses();

            Step(r, 0.40, "Restoring your lock screen");
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(CspKey, true))
                    if (k != null)
                        foreach (var v in new[] { "LockScreenImagePath", "LockScreenImageUrl", "LockScreenImageStatus" })
                            k.DeleteValue(v, false);
            }
            catch { }

            Step(r, 0.62, "Removing shortcuts and startup entry");
            TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Wakelight.lnk"));
            TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "Wakelight.lnk"));
            using (var run = UserKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) if (run != null) run.DeleteValue("Wakelight", false);
            try { Registry.LocalMachine.DeleteSubKeyTree(UninstallKey, false); } catch { }

            Step(r, 0.85, "Removing files");
            string dir = installDir;
            TryDelete(Path.Combine(dir, "Wakelight.exe"));
            // Uninstall.exe is running: remove it and the folder once we exit.
            Process.Start(new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"" + dir + "\"")
            { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
            Step(r, 1.0, "Done");
        }

        // ------------------------------------------------------------ helpers
        static string ExistingInstallDir()
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(UninstallKey))
                {
                    string d = k == null ? null : k.GetValue("InstallLocation") as string;
                    return d != null && File.Exists(Path.Combine(d, "Wakelight.exe")) ? d : null;
                }
            }
            catch { return null; }
        }

        static void StopProcesses()
        {
            foreach (var name in new[] { "Wakelight", "LiveLock", "LiveLock.scr" })
                foreach (var p in Process.GetProcessesByName(name))
                    try { p.Kill(); p.WaitForExit(3000); } catch { }
            // Old PowerShell watcher from the script version
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name='powershell.exe'"))
                    foreach (ManagementObject o in s.Get())
                    {
                        string cl = o["CommandLine"] as string;
                        if (cl != null && cl.IndexOf("WallpaperSync.ps1", StringComparison.OrdinalIgnoreCase) >= 0 &&
                            cl.IndexOf("-Watch", StringComparison.OrdinalIgnoreCase) >= 0)
                            try { Process.GetProcessById(Convert.ToInt32(o["ProcessId"])).Kill(); } catch { }
                    }
            }
            catch { }
        }

        /// <summary>Removes leftovers of the earlier LiveLock / WallpaperSync scripts.</summary>
        static void CleanupLegacy()
        {
            try { using (var run = UserKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) if (run != null) run.DeleteValue("LiveLock", false); } catch { }
            try
            {
                var p = Process.Start(new ProcessStartInfo("schtasks.exe", "/Delete /TN WallpaperSync /F")
                { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true });
                p.WaitForExit(5000);
            }
            catch { }
            try
            {
                using (var sf = UserKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders"))
                {
                    string desk = sf == null ? null : sf.GetValue("Desktop") as string;
                    if (desk != null) TryDelete(Path.Combine(desk, "Lock (live wallpaper).lnk"));
                }
            }
            catch { }
        }

        static void GrantLockScreen()
        {
            using (var k = Registry.LocalMachine.CreateSubKey(CspKey))
            {
                var acl = k.GetAccessControl();
                var who = userSid != null ? (IdentityReference)new SecurityIdentifier(userSid) : WindowsIdentity.GetCurrent().User;
                acl.AddAccessRule(new RegistryAccessRule(who, RegistryRights.SetValue | RegistryRights.QueryValues | RegistryRights.Delete,
                    InheritanceFlags.None, PropagationFlags.None, AccessControlType.Allow));
                k.SetAccessControl(acl);
            }
        }

        /// <summary>HKCU of the signed-in user (correct even when elevated with other credentials).</summary>
        static RegistryKey UserKey(string sub)
        {
            try
            {
                if (userSid != null)
                {
                    var k = Registry.Users.OpenSubKey(userSid + "\\" + sub, true) ?? Registry.Users.CreateSubKey(userSid + "\\" + sub);
                    if (k != null) return k;
                }
            }
            catch { }
            return Registry.CurrentUser.CreateSubKey(sub);
        }

        static string InteractiveUserSid()
        {
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT * FROM Win32_Process WHERE Name='explorer.exe'"))
                    foreach (ManagementObject o in s.Get())
                    {
                        var outp = o.InvokeMethod("GetOwnerSid", null, null);
                        if (outp != null && outp["Sid"] != null) return (string)outp["Sid"];
                    }
            }
            catch { }
            var me = WindowsIdentity.GetCurrent().User;
            return me == null ? null : me.Value;
        }

        static void Shortcut(string lnk, string target, string args)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(t);
            object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
            Type st = sc.GetType();
            st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
            st.InvokeMember("Arguments", BindingFlags.SetProperty, null, sc, new object[] { args });
            st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { Path.GetDirectoryName(target) });
            st.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { target + ",0" });
            st.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { "Your live wallpaper, awake while you're away" });
            st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
            Marshal.FinalReleaseComObject(sc); Marshal.FinalReleaseComObject(shell);
        }

        static void TryDelete(string f) { try { if (File.Exists(f)) File.Delete(f); } catch { } }
    }
}
