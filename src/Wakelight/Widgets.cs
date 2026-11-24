using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Wakelight
{
    /// <summary>
    /// The showcase overlay: clock (several styles), caption, chips (away timer,
    /// weather, wallpaper title) and the brand mark. Laid out on a 1920x1080
    /// design canvas by the caller (Viewbox), so it looks the same on any screen
    /// and in the in-app preview.
    /// </summary>
    public class WidgetLayer : Grid
    {
        readonly Settings s;
        readonly WallpaperInfo wallpaper;
        readonly Func<double> awaySeconds;
        readonly DispatcherTimer timer;
        readonly HorizontalAlignment align;
        Action tick = () => { };

        public WidgetLayer(Settings settings, WallpaperInfo wallpaper, Func<double> awaySeconds)
        {
            s = settings; this.wallpaper = wallpaper; this.awaySeconds = awaySeconds;
            Width = 1920; Height = 1080;
            IsHitTestVisible = false;
            // The app window uses Display text mode, which drops very large glyphs when scaled down in the preview.
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);

            align = s.WidgetPosition.EndsWith("right") ? HorizontalAlignment.Right
                  : s.WidgetPosition == "center" ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            var stack = new StackPanel
            {
                HorizontalAlignment = align,
                VerticalAlignment = s.WidgetPosition.StartsWith("top") ? VerticalAlignment.Top
                                  : s.WidgetPosition == "center" ? VerticalAlignment.Center : VerticalAlignment.Bottom,
                Margin = new Thickness(80, 70, 80, 70),
                LayoutTransform = new ScaleTransform(s.WidgetScale, s.WidgetScale),
                Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 0, Opacity = 0.55, Color = Colors.Black }
            };
            Children.Add(stack);

            BuildClock(stack);
            BuildCaption(stack);
            BuildChips(stack);
            if (s.ShowBrand)
                stack.Children.Add(new TextBlock
                {
                    Text = "WAKELIGHT", FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"), FontSize = 12,
                    FontWeight = FontWeights.SemiBold, Margin = new Thickness(6, 20, 6, 0), HorizontalAlignment = align,
                    Foreground = new SolidColorBrush(Color.FromArgb(160, AccentA.R, AccentA.G, AccentA.B))
                });

            tick();
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (o, e) => tick();
            Loaded += (o, e) => timer.Start();
            Unloaded += (o, e) => timer.Stop();
            Weather.Updated += OnWeather;
            Unloaded += (o, e) => Weather.Updated -= OnWeather;
        }

        public void Stop() { timer.Stop(); Weather.Updated -= OnWeather; }

        Action weatherRefresh = () => { };
        void OnWeather() { weatherRefresh(); }

        // ------------------------------------------------------------- accent
        Color AccentA { get { return Accents(s.Accent)[0]; } }
        Color AccentB { get { return Accents(s.Accent)[1]; } }

        public static Color[] Accents(string name)
        {
            switch (name)
            {
                case "rose": return new[] { Color.FromRgb(0xFF, 0x7A, 0xA8), Color.FromRgb(0xB0, 0x5C, 0xFF) };
                case "ice": return new[] { Color.FromRgb(0x9F, 0xE2, 0xFF), Color.FromRgb(0x7A, 0x8C, 0xFF) };
                case "mint": return new[] { Color.FromRgb(0x9B, 0xF2, 0xC4), Color.FromRgb(0x38, 0xC6, 0xD0) };
                case "white": return new[] { Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xE8, 0xE2, 0xF0) };
                default: return new[] { Color.FromRgb(0xFF, 0xC4, 0x6B), Color.FromRgb(0xFF, 0x5C, 0x8A) };
            }
        }

        Brush AccentBrush()
        {
            return new LinearGradientBrush(AccentA, AccentB, new Point(0, 0), new Point(1, 1));
        }

        static readonly FontFamily Display = new FontFamily("Segoe UI Variable Display, Segoe UI");
        static readonly FontFamily Body = new FontFamily("Segoe UI Variable Text, Segoe UI");
        static readonly FontFamily Icons = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

        string TimeText()
        {
            var n = DateTime.Now;
            string f = s.Clock24h ? "HH:mm" : "h:mm";
            if (s.ClockSeconds) f += ":ss";
            return n.ToString(f);
        }

        string AmPm() { return s.Clock24h ? "" : DateTime.Now.ToString("tt"); }

        // -------------------------------------------------------------- clock
        void BuildClock(StackPanel stack)
        {
            switch (s.ClockStyle)
            {
                case "none": return;
                case "analog": BuildAnalog(stack); return;
                case "stacked": BuildStacked(stack); return;
            }

            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = align };
            var t = new TextBlock { FontFamily = Display, Foreground = Brushes.White };
            var ampm = new TextBlock { FontFamily = Display, FontSize = 34, Margin = new Thickness(10, 0, 0, 22), VerticalAlignment = VerticalAlignment.Bottom, Foreground = Brushes.White, Opacity = 0.85 };
            switch (s.ClockStyle)
            {
                case "bold":
                    t.FontSize = 140; t.FontWeight = FontWeights.Black;
                    break;
                case "glow":
                    t.FontSize = 140; t.FontWeight = FontWeights.Light; t.Foreground = AccentBrush();
                    t.Effect = new DropShadowEffect { BlurRadius = 46, ShadowDepth = 0, Color = AccentB, Opacity = 0.9 };
                    ampm.Foreground = AccentBrush();
                    break;
                default:
                    t.FontSize = 132; t.FontWeight = FontWeights.Light;
                    break;
            }
            row.Children.Add(t); row.Children.Add(ampm);
            stack.Children.Add(row);
            Chain(() => { t.Text = TimeText(); ampm.Text = AmPm(); });
        }

        void BuildStacked(StackPanel stack)
        {
            var h = new TextBlock { FontFamily = Display, FontSize = 168, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, HorizontalAlignment = align, LineHeight = 150, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
            var m = new TextBlock { FontFamily = Display, FontSize = 168, FontWeight = FontWeights.SemiBold, Foreground = AccentBrush(), HorizontalAlignment = align, LineHeight = 150, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, Margin = new Thickness(0, -6, 0, 0) };
            stack.Children.Add(h); stack.Children.Add(m);
            Chain(() =>
            {
                var n = DateTime.Now;
                h.Text = n.ToString(s.Clock24h ? "HH" : "hh");
                m.Text = n.ToString("mm") + (s.ClockSeconds ? ":" + n.ToString("ss") : "");
            });
        }

        void BuildAnalog(StackPanel stack)
        {
            const double D = 260, R = D / 2;
            var c = new Canvas { Width = D, Height = D, HorizontalAlignment = align, Margin = new Thickness(0, 0, 0, 18) };
            c.Children.Add(new Ellipse { Width = D, Height = D, Fill = new SolidColorBrush(Color.FromArgb(70, 10, 8, 20)), Stroke = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)), StrokeThickness = 2 });
            for (int i = 0; i < 60; i++)
            {
                bool major = i % 5 == 0;
                double a = i * 6 * Math.PI / 180, r1 = R - (major ? 22 : 12), r2 = R - 8;
                c.Children.Add(new Line
                {
                    X1 = R + r1 * Math.Sin(a), Y1 = R - r1 * Math.Cos(a), X2 = R + r2 * Math.Sin(a), Y2 = R - r2 * Math.Cos(a),
                    Stroke = new SolidColorBrush(Color.FromArgb(major ? (byte)230 : (byte)90, 255, 255, 255)),
                    StrokeThickness = major ? 3 : 1.4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
                });
            }
            Func<double, double, Brush, Line> hand = (len, w, b) =>
            {
                var l = new Line { X1 = R, Y1 = R + 14, X2 = R, Y2 = R - len, Stroke = b, StrokeThickness = w, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                c.Children.Add(l); return l;
            };
            var hh = hand(R * 0.5, 7, Brushes.White);
            var mm = hand(R * 0.75, 4.5, Brushes.White);
            var ss = hand(R * 0.82, 2, new SolidColorBrush(AccentB));
            ss.Visibility = s.ClockSeconds ? Visibility.Visible : Visibility.Collapsed;
            var hub = new Ellipse { Width = 14, Height = 14, Fill = AccentBrush() };
            Canvas.SetLeft(hub, R - 7); Canvas.SetTop(hub, R - 7); c.Children.Add(hub);
            stack.Children.Add(c);
            var rh = new RotateTransform(0, R, R); var rm = new RotateTransform(0, R, R); var rs = new RotateTransform(0, R, R);
            hh.RenderTransform = rh; mm.RenderTransform = rm; ss.RenderTransform = rs;
            Chain(() =>
            {
                var n = DateTime.Now;
                rs.Angle = n.Second * 6;
                rm.Angle = n.Minute * 6 + n.Second * 0.1;
                rh.Angle = (n.Hour % 12) * 30 + n.Minute * 0.5;
            });
        }

        // ------------------------------------------------------------ caption
        void BuildCaption(StackPanel stack)
        {
            if (string.IsNullOrWhiteSpace(s.Caption)) return;
            var t = new TextBlock
            {
                FontFamily = Display, FontSize = 32, Foreground = new SolidColorBrush(Color.FromArgb(235, 255, 244, 232)),
                Margin = new Thickness(6, s.ClockStyle == "analog" ? 0 : -6, 6, 0), HorizontalAlignment = align,
                TextAlignment = align == HorizontalAlignment.Right ? TextAlignment.Right : align == HorizontalAlignment.Center ? TextAlignment.Center : TextAlignment.Left,
                TextWrapping = TextWrapping.Wrap, MaxWidth = 1100
            };
            stack.Children.Add(t);
            Chain(() => t.Text = Expand(s.Caption));
        }

        public string Expand(string text)
        {
            var n = DateTime.Now;
            return text.Replace("{date}", n.ToString("dddd, d MMMM"))
                       .Replace("{day}", n.ToString("dddd"))
                       .Replace("{time}", n.ToString(s.Clock24h ? "HH:mm" : "h:mm tt"))
                       .Replace("{wallpaper}", wallpaper == null ? "" : wallpaper.Title)
                       .Replace("{away}", AwayText())
                       .Replace("\\n", "\n");
        }

        string AwayText()
        {
            int m = (int)(awaySeconds() / 60);
            if (m < 1) return "Just stepped away";
            if (m < 60) return "Away " + m + " min";
            return "Away " + (m / 60) + " h " + (m % 60) + " min";
        }

        // -------------------------------------------------------------- chips
        void BuildChips(StackPanel stack)
        {
            var row = new WrapPanel { HorizontalAlignment = align, Margin = new Thickness(0, 18, 0, 0) };
            if (s.ShowAway)
            {
                TextBlock txt;
                row.Children.Add(Chip("", out txt));        // stopwatch
                Chain(() => txt.Text = AwayText());
            }
            if (s.ShowWeather)
            {
                TextBlock txt; TextBlock icon;
                var chip = Chip("", out txt, out icon);
                row.Children.Add(chip);
                weatherRefresh = () =>
                {
                    var w = Weather.Current;
                    chip.Visibility = w == null ? Visibility.Collapsed : Visibility.Visible;
                    if (w == null) return;
                    icon.Text = w.Glyph;
                    txt.Text = w.Temperature.ToString("0") + "°  " + w.Summary + " · " + w.Place;
                };
                weatherRefresh();
            }
            if (s.ShowWallpaperChip && wallpaper != null)
            {
                TextBlock txt;
                row.Children.Add(Chip("", out txt));        // photo
                txt.Text = wallpaper.Title;
            }
            if (row.Children.Count > 0) stack.Children.Add(row);
        }

        Border Chip(string glyph, out TextBlock text)
        {
            TextBlock icon;
            return Chip(glyph, out text, out icon);
        }

        Border Chip(string glyph, out TextBlock text, out TextBlock icon)
        {
            icon = new TextBlock { Text = glyph, FontFamily = Icons, FontSize = 18, Foreground = AccentBrush(), VerticalAlignment = VerticalAlignment.Center };
            text = new TextBlock { FontFamily = Body, FontSize = 19, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Margin = new Thickness(10, 0, 0, 1), VerticalAlignment = VerticalAlignment.Center };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(icon); sp.Children.Add(text);
            return new Border
            {
                Child = sp, CornerRadius = new CornerRadius(22), Padding = new Thickness(16, 9, 18, 9), Margin = new Thickness(6, 0, 6, 8),
                Background = new SolidColorBrush(Color.FromArgb(110, 12, 10, 24)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)), BorderThickness = new Thickness(1)
            };
        }

        void Chain(Action a) { var prev = tick; tick = () => { prev(); a(); }; }
    }
}
