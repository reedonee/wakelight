using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Wakelight
{
    /// <summary>
    /// Full-screen mirror of Wallpaper Engine's live render (DWM thumbnail) on every
    /// monitor, with an optional clock. Any input closes it.
    /// </summary>
    public class Showcase
    {
        public event Action<string> Closed;            // reason
        public event Action<string> SnapshotTaken;     // jpg path

        readonly bool showClock;
        readonly bool hideIcons;
        readonly string snapshotPath;
        readonly List<Window> windows = new List<Window>();
        readonly List<IntPtr> thumbs = new List<IntPtr>();
        DateTime shownAt;
        Point? firstMouse;
        bool closing;
        IntPtr iconsHwnd = IntPtr.Zero;
        DispatcherTimer clockTimer;

        public bool IsOpen { get { return windows.Count > 0 && !closing; } }

        public Showcase(bool showClock, bool hideIcons, string snapshotPath)
        {
            this.showClock = showClock;
            this.hideIcons = hideIcons;
            this.snapshotPath = snapshotPath;
        }

        public bool Open(WallpaperInfo fallback)
        {
            shownAt = DateTime.Now;
            IntPtr host = WallpaperEngine.FindRenderHost();
            Log.Write("showcase open, render host=" + host);
            if (host != IntPtr.Zero && hideIcons) HideIcons(host);

            double scale = Dpi();
            foreach (var screen in Forms.Screen.AllScreens)
            {
                var b = screen.Bounds;
                var w = new Window
                {
                    WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.NoResize,
                    Topmost = true,
                    ShowInTaskbar = false,
                    Background = Brushes.Black,
                    Cursor = Cursors.None,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = b.Left / scale, Top = b.Top / scale, Width = b.Width / scale, Height = b.Height / scale,
                    Title = "Wakelight Showcase"
                };
                var bounds = b;
                if (host != IntPtr.Zero)
                {
                    w.Content = new Grid();
                    w.ContentRendered += (s, e) => Attach((Window)s, host, bounds);
                }
                else w.Content = FallbackContent(fallback);
                w.SourceInitialized += (s, e) => ((Window)s).WindowState = WindowState.Maximized;
                w.KeyDown += (s, e) => { if (Armed()) Close("key"); };
                w.MouseDown += (s, e) => { if (Armed()) Close("click"); };
                w.MouseWheel += (s, e) => { if (Armed()) Close("wheel"); };
                w.MouseMove += (s, e) =>
                {
                    var p = ((Window)s).PointToScreen(e.GetPosition((Window)s));
                    if (firstMouse == null || !Armed()) { firstMouse = p; return; }
                    if (Math.Abs(p.X - firstMouse.Value.X) > 14 || Math.Abs(p.Y - firstMouse.Value.Y) > 14) Close("mouse");
                };
                w.Deactivated += (s, e) => { if (Armed() && windows.Count == 1) ((Window)s).Activate(); };
                windows.Add(w);
                w.Show();
            }
            if (windows.Count > 0) windows[0].Activate();

            // Take a sharp still for the lock screen while nothing covers the scene, then add the clock.
            After(900, () =>
            {
                if (closing) return;
                if (snapshotPath != null && host != IntPtr.Zero)
                {
                    try { Capture(snapshotPath); if (SnapshotTaken != null) SnapshotTaken(snapshotPath); }
                    catch (Exception ex) { Log.Write("snapshot failed: " + ex.Message); }
                }
                if (showClock) foreach (var w in windows.ToList()) AddClock(w);
            });
            return host != IntPtr.Zero;
        }

        bool Armed() { return (DateTime.Now - shownAt).TotalMilliseconds > 1800; }

        public void Close(string reason)
        {
            if (closing) return;
            closing = true;
            Log.Write("showcase close: " + reason + " after " + (DateTime.Now - shownAt).TotalSeconds.ToString("0") + "s");
            if (clockTimer != null) clockTimer.Stop();
            foreach (var t in thumbs) Native.DwmUnregisterThumbnail(t);
            thumbs.Clear();
            foreach (var w in windows.ToList()) { try { w.Close(); } catch { } }
            windows.Clear();
            RestoreIcons();
            if (Closed != null) Closed(reason);
        }

        // ------------------------------------------------------------------
        void Attach(Window w, IntPtr host, System.Drawing.Rectangle screen)
        {
            IntPtr hwnd = new WindowInteropHelper(w).Handle;
            IntPtr thumb;
            if (Native.DwmRegisterThumbnail(hwnd, host, out thumb) != 0) { Log.Write("thumbnail failed"); return; }
            thumbs.Add(thumb);
            Native.RECT src; Native.GetWindowRect(host, out src);
            Native.RECT client; Native.GetClientRect(hwnd, out client);
            var p = new Native.DWM_THUMBNAIL_PROPERTIES();
            p.dwFlags = Native.DWM_TNP_RECTDESTINATION | Native.DWM_TNP_RECTSOURCE | Native.DWM_TNP_OPACITY |
                        Native.DWM_TNP_VISIBLE | Native.DWM_TNP_SOURCECLIENTAREAONLY;
            p.rcDestination = client;
            p.rcSource = new Native.RECT
            {
                Left = screen.Left - src.Left, Top = screen.Top - src.Top,
                Right = screen.Right - src.Left, Bottom = screen.Bottom - src.Top
            };
            p.opacity = 255; p.fVisible = 1; p.fSourceClientAreaOnly = 0;
            Native.DwmUpdateThumbnailProperties(thumb, ref p);
        }

        void AddClock(Window owner)
        {
            var time = new TextBlock
            {
                FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI Light"),
                FontWeight = FontWeights.Light, FontSize = 128, Foreground = Brushes.White
            };
            var date = new TextBlock
            {
                FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
                FontSize = 30, Foreground = new SolidColorBrush(Color.FromArgb(230, 255, 241, 226)),
                Margin = new Thickness(6, -8, 0, 0)
            };
            var mark = new TextBlock
            {
                Text = "WAKELIGHT", FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
                FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 18, 0, 0),
                Foreground = new SolidColorBrush(Color.FromArgb(150, 255, 196, 107))
            };
            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(72, 0, 0, 64),
                Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 0, Opacity = 0.55, Color = Colors.Black }
            };
            stack.Children.Add(time); stack.Children.Add(date); stack.Children.Add(mark);

            Action tick = () =>
            {
                time.Text = DateTime.Now.ToString("HH:mm");
                date.Text = DateTime.Now.ToString("dddd, d MMMM");
            };
            tick();
            if (clockTimer == null)
            {
                clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                clockTimer.Start();
            }
            clockTimer.Tick += (s, e) => tick();

            var overlay = new Window
            {
                Owner = owner, WindowStyle = WindowStyle.None, AllowsTransparency = true,
                Background = Brushes.Transparent, ShowInTaskbar = false, Topmost = true, ShowActivated = false,
                ResizeMode = ResizeMode.NoResize, IsHitTestVisible = false, Focusable = false,
                Left = owner.Left, Top = owner.Top, Width = owner.ActualWidth, Height = owner.ActualHeight,
                Content = stack, Opacity = 0, Cursor = Cursors.None
            };
            overlay.Show();
            overlay.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(900)));
            windows.Add(overlay);
        }

        static UIElement FallbackContent(WallpaperInfo info)
        {
            if (info != null && info.VideoPath != null)
            {
                var me = new MediaElement
                {
                    Source = new Uri(info.VideoPath), LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual,
                    IsMuted = true, Stretch = Stretch.UniformToFill
                };
                me.MediaEnded += (s, e) => { me.Position = TimeSpan.Zero; me.Play(); };
                me.Loaded += (s, e) => me.Play();
                return me;
            }
            var img = new Image { Stretch = Stretch.UniformToFill };
            if (info != null && info.PreviewPath != null) Media.SetAnimatedSource(img, info.PreviewPath);
            return img;
        }

        // Desktop icons are drawn above the render inside Progman: hide them while showing.
        void HideIcons(IntPtr host)
        {
            if (Native.ClassOf(host) != "Progman") return;
            IntPtr dv = Native.FindWindowEx(host, IntPtr.Zero, "SHELLDLL_DefView", null);
            IntPtr lv = dv == IntPtr.Zero ? IntPtr.Zero : Native.FindWindowEx(dv, IntPtr.Zero, "SysListView32", null);
            if (lv == IntPtr.Zero || !Native.IsWindowVisible(lv)) return;
            iconsHwnd = lv;
            Native.ShowWindow(lv, 0);
        }

        void RestoreIcons()
        {
            if (iconsHwnd != IntPtr.Zero) { Native.ShowWindow(iconsHwnd, 5); iconsHwnd = IntPtr.Zero; }
        }

        /// <summary>Safety net at startup / exit: make sure desktop icons are visible.</summary>
        public static void EnsureIconsVisible()
        {
            IntPtr prog = Native.FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Progman", null);
            IntPtr dv = Native.FindWindowEx(prog, IntPtr.Zero, "SHELLDLL_DefView", null);
            IntPtr lv = Native.FindWindowEx(dv, IntPtr.Zero, "SysListView32", null);
            if (lv != IntPtr.Zero && !Native.IsWindowVisible(lv)) Native.ShowWindow(lv, 5);
        }

        static void Capture(string path)
        {
            var b = Forms.Screen.PrimaryScreen.Bounds;
            using (var bmp = new System.Drawing.Bitmap(b.Width, b.Height))
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(b.Location, System.Drawing.Point.Empty, b.Size);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var enc = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders().First(e => e.MimeType == "image/jpeg");
                var ps = new System.Drawing.Imaging.EncoderParameters(1);
                ps.Param[0] = new System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 92L);
                bmp.Save(path, enc, ps);
            }
        }

        static void After(int ms, Action a)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            t.Tick += (s, e) => { t.Stop(); a(); };
            t.Start();
        }

        static double Dpi()
        {
            using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero)) return g.DpiX / 96.0;
        }
    }

    /// <summary>Animated GIF / still image helper.</summary>
    public static class Media
    {
        public static void SetAnimatedSource(Image img, string path)
        {
            try
            {
                if (path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
                {
                    var dec = new System.Windows.Media.Imaging.GifBitmapDecoder(new Uri(path),
                        System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                    if (dec.Frames.Count > 1)
                    {
                        var anim = new ObjectAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
                        var t = TimeSpan.Zero;
                        foreach (var f in Composite(dec))
                        {
                            anim.KeyFrames.Add(new DiscreteObjectKeyFrame(f.Item1, KeyTime.FromTimeSpan(t)));
                            t += TimeSpan.FromMilliseconds(f.Item2);
                        }
                        anim.Duration = t;
                        img.BeginAnimation(Image.SourceProperty, anim);
                        return;
                    }
                    img.Source = dec.Frames[0];
                    return;
                }
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit(); bmp.UriSource = new Uri(path); bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; bmp.EndInit();
                img.BeginAnimation(Image.SourceProperty, null);
                img.Source = bmp;
            }
            catch (Exception ex) { Log.Write("image load failed: " + ex.Message); }
        }

        // GIF frames are partial updates: draw each onto the running canvas at its
        // offset, honouring the "restore to background" disposal method.
        static List<Tuple<ImageSource, int>> Composite(System.Windows.Media.Imaging.GifBitmapDecoder dec)
        {
            var result = new List<Tuple<ImageSource, int>>();
            int w = dec.Frames[0].PixelWidth, h = dec.Frames[0].PixelHeight;
            try
            {
                var md0 = dec.Metadata;
                if (md0 != null)
                {
                    object lw = md0.GetQuery("/logscrdesc/Width"), lh = md0.GetQuery("/logscrdesc/Height");
                    if (lw != null && lh != null) { w = Convert.ToInt32(lw); h = Convert.ToInt32(lh); }
                }
            }
            catch { }
            int maxFrames = Math.Min(dec.Frames.Count, 240);
            System.Windows.Media.Imaging.BitmapSource canvas = null;
            for (int i = 0; i < maxFrames; i++)
            {
                var f = dec.Frames[i];
                int left = 0, top = 0, disposal = 0;
                var md = f.Metadata as System.Windows.Media.Imaging.BitmapMetadata;
                try
                {
                    if (md != null)
                    {
                        object l = md.GetQuery("/imgdesc/Left"), tp = md.GetQuery("/imgdesc/Top"), d = md.GetQuery("/grctlext/Disposal");
                        if (l != null) left = Convert.ToInt32(l);
                        if (tp != null) top = Convert.ToInt32(tp);
                        if (d != null) disposal = Convert.ToInt32(d);
                    }
                }
                catch { }
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    if (canvas != null) dc.DrawImage(canvas, new Rect(0, 0, w, h));
                    dc.DrawImage(f, new Rect(left, top, f.PixelWidth, f.PixelHeight));
                }
                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv); rtb.Freeze();
                result.Add(Tuple.Create((ImageSource)rtb, FrameDelay(f)));
                if (disposal == 2)
                {
                    var clear = new DrawingVisual();
                    using (var dc = clear.RenderOpen())
                    {
                        dc.PushClip(new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, w, h)),
                            new RectangleGeometry(new Rect(left, top, f.PixelWidth, f.PixelHeight))));
                        dc.DrawImage(rtb, new Rect(0, 0, w, h));
                        dc.Pop();
                    }
                    var c2 = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                    c2.Render(clear); c2.Freeze(); canvas = c2;
                }
                else if (disposal != 3) canvas = rtb;
            }
            return result;
        }

        static int FrameDelay(System.Windows.Media.Imaging.BitmapFrame f)
        {
            try
            {
                var md = f.Metadata as System.Windows.Media.Imaging.BitmapMetadata;
                if (md != null)
                {
                    object v = md.GetQuery("/grctlext/Delay");
                    if (v != null) { int d = Convert.ToInt32(v) * 10; return d < 20 ? 100 : d; }
                }
            }
            catch { }
            return 60;
        }
    }
}
