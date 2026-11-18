using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Wakelight
{
    public class MainWindow
    {
        public Window Window { get; private set; }
        readonly Engine engine;
        bool loading;
        bool recordingHotkey;
        string shownPreviewFor;

        T Find<T>(string name) where T : class { return Window.FindName(name) as T; }

        TextBlock Headline, StatusText, WallpaperTitle, TypeChip, IdleText, ShowcaseValue, LockValue, FolderText, SyncStatus, VersionText;
        Ellipse StatusDot, StatusGlow;
        Image PreviewImage, Logo;
        MediaElement PreviewVideo;
        FrameworkElement EmptyState;
        Canvas Timeline;
        Slider ShowcaseSlider, LockSlider;
        CheckBox EnabledSwitch, ClockSwitch, IconsSwitch, FullscreenSwitch, SyncSwitch, StartupSwitch;
        Button HotkeyButton, PauseButton;

        public MainWindow(Engine engine)
        {
            this.engine = engine;
            using (var s = Program.Resource("MainWindow.xaml")) Window = (Window)XamlReader.Load(s);
            Window.Icon = Program.LogoImage;

            Headline = Find<TextBlock>("Headline"); StatusText = Find<TextBlock>("StatusText");
            WallpaperTitle = Find<TextBlock>("WallpaperTitle"); TypeChip = Find<TextBlock>("TypeChip");
            IdleText = Find<TextBlock>("IdleText"); ShowcaseValue = Find<TextBlock>("ShowcaseValue");
            LockValue = Find<TextBlock>("LockValue"); FolderText = Find<TextBlock>("FolderText");
            SyncStatus = Find<TextBlock>("SyncStatus"); VersionText = Find<TextBlock>("VersionText");
            StatusDot = Find<Ellipse>("StatusDot"); StatusGlow = Find<Ellipse>("StatusGlow");
            PreviewImage = Find<Image>("PreviewImage"); Logo = Find<Image>("Logo");
            PreviewVideo = Find<MediaElement>("PreviewVideo"); EmptyState = Find<FrameworkElement>("EmptyState");
            Timeline = Find<Canvas>("Timeline");
            ShowcaseSlider = Find<Slider>("ShowcaseSlider"); LockSlider = Find<Slider>("LockSlider");
            EnabledSwitch = Find<CheckBox>("EnabledSwitch"); ClockSwitch = Find<CheckBox>("ClockSwitch");
            IconsSwitch = Find<CheckBox>("IconsSwitch"); FullscreenSwitch = Find<CheckBox>("FullscreenSwitch");
            SyncSwitch = Find<CheckBox>("SyncSwitch"); StartupSwitch = Find<CheckBox>("StartupSwitch");
            HotkeyButton = Find<Button>("HotkeyButton"); PauseButton = Find<Button>("PauseButton");

            Logo.Source = Program.LogoImage;
            VersionText.Text = Program.Version;

            Window.SourceInitialized += (s, e) => Native.StyleWindow(new WindowInteropHelper(Window).Handle);
            Window.Closing += (s, e) => { e.Cancel = true; Window.Hide(); PreviewVideo.Pause(); };
            Window.IsVisibleChanged += (s, e) => { if (Window.IsVisible) { shownPreviewFor = null; Refresh(); } };
            Window.PreviewKeyDown += OnPreviewKeyDown;
            Timeline.SizeChanged += (s, e) => DrawTimeline();

            Find<Button>("MinButton").Click += (s, e) => Window.WindowState = WindowState.Minimized;
            Find<Button>("CloseButton").Click += (s, e) => Window.Close();
            Find<Button>("ShowcaseButton").Click += (s, e) => engine.ShowNow(false);
            Find<Button>("LockButton").Click += (s, e) => engine.LockNow();
            PauseButton.Click += (s, e) => OpenPauseMenu();
            Find<Button>("RefreshLockButton").Click += (s, e) => engine.RefreshLockScreenNow();
            Find<Button>("FolderButton").Click += (s, e) => ChooseFolder();
            Find<Button>("LogButton").Click += (s, e) => { try { Process.Start("notepad.exe", "\"" + Log.PathName + "\""); } catch { } };
            Find<Button>("QuitButton").Click += (s, e) => Program.Quit();
            HotkeyButton.Click += (s, e) => { recordingHotkey = true; HotkeyButton.Content = "Press keys…"; HotkeyButton.Focus(); };

            PreviewVideo.MediaEnded += (s, e) => { PreviewVideo.Position = TimeSpan.Zero; PreviewVideo.Play(); };

            LoadSettings();
            ShowcaseSlider.ValueChanged += (s, e) => OnSettingChanged();
            LockSlider.ValueChanged += (s, e) => OnSettingChanged();
            foreach (var cb in new[] { EnabledSwitch, ClockSwitch, IconsSwitch, FullscreenSwitch, SyncSwitch, StartupSwitch })
            {
                cb.Checked += (s, e) => OnSettingChanged();
                cb.Unchecked += (s, e) => OnSettingChanged();
            }

            engine.Changed += () => { if (Window.IsVisible) Refresh(); };
            engine.WallpaperChanged += () => { shownPreviewFor = null; if (Window.IsVisible) Refresh(); };
        }

        public void Show()
        {
            if (!Window.IsVisible) Window.Show();
            if (Window.WindowState == WindowState.Minimized) Window.WindowState = WindowState.Normal;
            Window.Activate();
            Window.Topmost = true; Window.Topmost = false;
        }

        // ---------------------------------------------------------- settings
        void LoadSettings()
        {
            loading = true;
            var st = engine.Settings;
            EnabledSwitch.IsChecked = st.Enabled;
            ShowcaseSlider.Value = st.ShowcaseAfterMinutes;
            LockSlider.Value = st.LockAfterMinutes;
            ClockSwitch.IsChecked = st.ShowClock;
            IconsSwitch.IsChecked = st.HideDesktopIcons;
            FullscreenSwitch.IsChecked = st.SkipFullscreenApps;
            SyncSwitch.IsChecked = st.SyncLockScreen;
            StartupSwitch.IsChecked = st.StartWithWindows;
            HotkeyButton.Content = Engine.HotkeyText(st.HotkeyModifiers, st.HotkeyKey);
            loading = false;
            UpdateLabels();
        }

        void OnSettingChanged()
        {
            UpdateLabels();
            if (loading) return;
            var st = engine.Settings;
            st.Enabled = EnabledSwitch.IsChecked == true;
            st.ShowcaseAfterMinutes = ShowcaseSlider.Value;
            st.LockAfterMinutes = LockSlider.Value;
            st.ShowClock = ClockSwitch.IsChecked == true;
            st.HideDesktopIcons = IconsSwitch.IsChecked == true;
            st.SkipFullscreenApps = FullscreenSwitch.IsChecked == true;
            st.SyncLockScreen = SyncSwitch.IsChecked == true;
            st.StartWithWindows = StartupSwitch.IsChecked == true;
            engine.SaveSettings();
        }

        void UpdateLabels()
        {
            ShowcaseValue.Text = Minutes(ShowcaseSlider.Value);
            LockValue.Text = LockSlider.Value <= 0 ? "Never" : Minutes(LockSlider.Value);
            DrawTimeline();
        }

        static string Minutes(double m)
        {
            int v = (int)Math.Round(m);
            if (v < 60) return v + " min";
            return (v / 60) + " h" + (v % 60 == 0 ? "" : " " + (v % 60) + " min");
        }

        void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!recordingHotkey) return;
            e.Handled = true;
            Key k = e.Key == Key.System ? e.SystemKey : e.Key;
            if (k == Key.Escape) { recordingHotkey = false; HotkeyButton.Content = Engine.HotkeyText(engine.Settings.HotkeyModifiers, engine.Settings.HotkeyKey); return; }
            if (k == Key.Back || k == Key.Delete) { Save(0, 0); return; }
            if (k == Key.LeftCtrl || k == Key.RightCtrl || k == Key.LeftAlt || k == Key.RightAlt ||
                k == Key.LeftShift || k == Key.RightShift || k == Key.LWin || k == Key.RWin) return;
            int mods = 0;
            if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) mods |= 1;
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) mods |= 2;
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) mods |= 4;
            if (mods == 0) { HotkeyButton.Content = "Add Ctrl / Alt…"; return; }
            Save(mods, KeyInterop.VirtualKeyFromKey(k));
        }

        void Save(int mods, int vk)
        {
            recordingHotkey = false;
            engine.Settings.HotkeyModifiers = mods;
            engine.Settings.HotkeyKey = vk;
            engine.SaveSettings();
            HotkeyButton.Content = Engine.HotkeyText(mods, vk);
        }

        void OpenPauseMenu()
        {
            var menu = new ContextMenu { PlacementTarget = PauseButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
            Action<string, Action> add = (text, act) =>
            {
                var mi = new MenuItem { Header = text, Style = (Style)Application.Current.Resources["MenuItemStyle"] };
                mi.Click += (s, e) => act();
                menu.Items.Add(mi);
            };
            if (engine.IsPaused) add("Resume now", () => engine.Resume());
            add("Pause for 30 minutes", () => engine.Pause(TimeSpan.FromMinutes(30)));
            add("Pause for 1 hour", () => engine.Pause(TimeSpan.FromHours(1)));
            add("Pause for 3 hours", () => engine.Pause(TimeSpan.FromHours(3)));
            add("Pause until I resume", () => engine.Pause(null));
            menu.IsOpen = true;
        }

        void ChooseFolder()
        {
            using (var d = new System.Windows.Forms.FolderBrowserDialog())
            {
                d.Description = "Select the Wallpaper Engine folder (the one that contains wallpaper64.exe and config.json)";
                if (engine.WeFolder != null) d.SelectedPath = engine.WeFolder;
                if (d.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                if (!WallpaperEngine.IsWeFolder(d.SelectedPath))
                {
                    MessageBox.Show(Window, "That folder doesn't contain Wallpaper Engine (config.json and wallpaper64.exe).", "Wakelight");
                    return;
                }
                engine.Settings.WallpaperEngineDir = d.SelectedPath;
                engine.SaveSettings();
            }
        }

        // ------------------------------------------------------------ status
        void Refresh()
        {
            var st = engine.State;
            Brush dot = (Brush)Application.Current.Resources["Mint"];
            string status, headline;
            switch (st)
            {
                case EngineState.Showing:
                    status = "Showcasing now"; headline = "On display"; dot = (Brush)Application.Current.Resources["Ember"]; break;
                case EngineState.Paused:
                    status = engine.PausedUntil == DateTime.MaxValue ? "Paused" : "Paused until " + engine.PausedUntil.ToString("HH:mm");
                    headline = "Taking a break"; dot = (Brush)Application.Current.Resources["Faint"]; break;
                case EngineState.Disabled:
                    status = "Off"; headline = "Wakelight is off"; dot = (Brush)Application.Current.Resources["Faint"]; break;
                case EngineState.NoWallpaperEngine:
                    status = "Wallpaper Engine not found"; headline = "Waiting for Wallpaper Engine"; dot = (Brush)Application.Current.Resources["Rose"]; break;
                default:
                    double left = engine.SecondsToShowcase;
                    if (st == EngineState.Counting) { status = "Showcase in " + Clock(left); headline = "Showcase in " + Clock(left); dot = (Brush)Application.Current.Resources["Ember"]; }
                    else { status = "Watching"; headline = "Watching quietly"; }
                    break;
            }
            StatusText.Text = status; Headline.Text = headline;
            StatusDot.Fill = dot; StatusGlow.Fill = dot;
            PauseButton.Content = engine.IsPaused ? "Resume" : "Pause";
            IdleText.Text = "Idle " + Clock(engine.IdleSeconds);
            FolderText.Text = engine.WeFolder ?? "Not found — click Change";
            FolderText.ToolTip = engine.WeFolder;

            if (!engine.LockScreenPermission)
            { SyncStatus.Text = "Needs a one-time permission. Re-run the Wakelight installer to grant it."; SyncStatus.Foreground = (Brush)Application.Current.Resources["Rose"]; }
            else
            { SyncStatus.Text = "A sharp still is captured during each showcase."; SyncStatus.Foreground = (Brush)Application.Current.Resources["Sub"]; }

            UpdatePreview();
            DrawTimeline();
        }

        static string Clock(double sec)
        {
            int s = (int)Math.Max(0, sec);
            return s >= 3600 ? (s / 3600) + ":" + (s % 3600 / 60).ToString("00") + ":" + (s % 60).ToString("00")
                             : (s / 60) + ":" + (s % 60).ToString("00");
        }

        void UpdatePreview()
        {
            var w = engine.Wallpaper;
            string key = w == null ? "" : w.File;
            if (key == shownPreviewFor) return;
            shownPreviewFor = key;

            EmptyState.Visibility = w == null ? Visibility.Visible : Visibility.Collapsed;
            WallpaperTitle.Text = w == null ? "—" : w.Title;
            TypeChip.Text = w == null ? "NONE" : w.Type.ToUpperInvariant();

            PreviewVideo.Stop(); PreviewVideo.Source = null; PreviewVideo.Visibility = Visibility.Collapsed;
            PreviewImage.BeginAnimation(Image.SourceProperty, null); PreviewImage.Source = null;
            if (w == null) return;
            if (w.VideoPath != null)
            {
                PreviewVideo.Source = new Uri(w.VideoPath);
                PreviewVideo.Visibility = Visibility.Visible;
                PreviewVideo.Play();
            }
            else if (w.PreviewPath != null) Media.SetAnimatedSource(PreviewImage, w.PreviewPath);
        }

        void DrawTimeline()
        {
            if (Timeline == null || Timeline.ActualWidth <= 0) return;
            Timeline.Children.Clear();
            double W = Timeline.ActualWidth;
            double show = ShowcaseSlider.Value * 60, lck = LockSlider.Value * 60;
            double max = Math.Max(show, lck) * 1.18 + 30;
            double y = 10;
            Func<double, double> X = t => Math.Min(W, t / max * W);

            Timeline.Children.Add(Bar(0, W, y, new SolidColorBrush(Color.FromRgb(0x2A, 0x26, 0x45))));
            double xs = X(show);
            double xl = lck > 0 ? X(lck) : W;
            if (lck <= 0 || lck > show)
            {
                Timeline.Children.Add(Bar(xs, W, y, (Brush)Application.Current.Resources["GlowH"]));
                if (lck > 0) Timeline.Children.Add(Bar(xl, W, y, new SolidColorBrush(Color.FromArgb(200, 0xB8, 0x2E, 0x5C))));
                Timeline.Children.Add(Label(xs, "Showcase · " + Minutes(ShowcaseSlider.Value), (Brush)Application.Current.Resources["Ember"]));
                if (lck > 0) Timeline.Children.Add(Label(xl, "Lock · " + Minutes(LockSlider.Value), (Brush)Application.Current.Resources["Rose"]));
            }
            else
            {
                Timeline.Children.Add(Bar(xl, W, y, new SolidColorBrush(Color.FromArgb(200, 0xB8, 0x2E, 0x5C))));
                Timeline.Children.Add(Label(xl, "Lock · " + Minutes(LockSlider.Value) + " (before showcase)", (Brush)Application.Current.Resources["Rose"]));
            }

            // "now" marker
            double xi = X(Math.Min(engine.IdleSeconds, max));
            var glow = new Ellipse { Width = 18, Height = 18, Fill = (Brush)Application.Current.Resources["Ember"], Opacity = 0.45, Effect = new BlurEffect { Radius = 8 } };
            var dotE = new Ellipse { Width = 12, Height = 12, Fill = new SolidColorBrush(Color.FromRgb(0xF4, 0xEE, 0xE6)) };
            Canvas.SetLeft(glow, xi - 9); Canvas.SetTop(glow, y - 6);
            Canvas.SetLeft(dotE, xi - 6); Canvas.SetTop(dotE, y - 3);
            Timeline.Children.Add(glow); Timeline.Children.Add(dotE);
        }

        static UIElement Bar(double x1, double x2, double y, Brush b)
        {
            var r = new Rectangle { Width = Math.Max(0, x2 - x1), Height = 6, RadiusX = 3, RadiusY = 3, Fill = b };
            Canvas.SetLeft(r, x1); Canvas.SetTop(r, y);
            return r;
        }

        static UIElement Label(double x, string text, Brush color)
        {
            var g = new StackPanel { Orientation = Orientation.Horizontal };
            g.Children.Add(new Rectangle { Width = 2, Height = 12, Fill = color, Margin = new Thickness(0, 0, 6, 0) });
            g.Children.Add(new TextBlock { Text = text, FontSize = 11, Foreground = color, FontWeight = FontWeights.SemiBold });
            Canvas.SetLeft(g, x - 1); Canvas.SetTop(g, 22);
            return g;
        }
    }
}
