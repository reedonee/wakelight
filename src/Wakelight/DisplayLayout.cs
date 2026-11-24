using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Forms = System.Windows.Forms;

namespace Wakelight
{
    /// <summary>
    /// How the live scene (rendered on the primary monitor) is spread over all monitors.
    ///   span      - one continuous image across the whole desktop (each monitor shows its slice)
    ///   mirror    - every monitor shows the full scene
    ///   spotlight - primary full brightness, others show a dimmed mirror
    ///   native    - each monitor shows whatever Wallpaper Engine draws there
    /// </summary>
    public static class DisplayLayout
    {
        public static readonly string[] Modes = { "span", "mirror", "spotlight", "native" };

        public class ScreenInfo
        {
            public System.Drawing.Rectangle Bounds;
            public bool Primary;
        }

        public static List<ScreenInfo> Screens()
        {
            return Forms.Screen.AllScreens
                .Select(s => new ScreenInfo { Bounds = s.Bounds, Primary = s.Primary })
                .OrderBy(s => s.Bounds.Left).ThenBy(s => s.Bounds.Top).ToList();
        }

        public static System.Drawing.Rectangle Union(IEnumerable<ScreenInfo> screens)
        {
            var list = screens.ToList();
            int l = list.Min(s => s.Bounds.Left), t = list.Min(s => s.Bounds.Top);
            int r = list.Max(s => s.Bounds.Right), b = list.Max(s => s.Bounds.Bottom);
            return new System.Drawing.Rectangle(l, t, r - l, b - t);
        }

        /// <summary>
        /// Part of the source picture (normalized 0..1, aspect = sourceAspect) that a screen should show.
        /// Null for "native" (screen shows its own region of the desktop render).
        /// </summary>
        public static Rect? SourceFor(string mode, ScreenInfo screen, System.Drawing.Rectangle virt, double sourceAspect)
        {
            var b = screen.Bounds;
            switch (mode)
            {
                case "native":
                    return null;
                case "span":
                {
                    // cover the whole virtual desktop with the picture (UniformToFill), take this screen's slice
                    double vw = virt.Width, vh = virt.Height;
                    double pw = sourceAspect, ph = 1;                       // picture in "aspect units"
                    double scale = Math.Max(vw / pw, vh / ph);              // pixels per unit
                    double visW = vw / scale, visH = vh / scale;            // units visible
                    double x0 = (pw - visW) / 2, y0 = (ph - visH) / 2;
                    double x = x0 + (b.Left - virt.Left) / scale, y = y0 + (b.Top - virt.Top) / scale;
                    return new Rect(x / pw, y / ph, (b.Width / scale) / pw, (b.Height / scale) / ph);
                }
                default:
                    return Cover(sourceAspect, (double)b.Width / b.Height);
            }
        }

        /// <summary>Centered crop of a picture (aspect a) that fills a target of aspect t.</summary>
        public static Rect Cover(double a, double t)
        {
            if (t > a) { double h = a / t; return new Rect(0, (1 - h) / 2, 1, h); }
            double w = t / a; return new Rect((1 - w) / 2, 0, w, 1);
        }

        public static byte OpacityFor(string mode, ScreenInfo screen)
        {
            return mode == "spotlight" && !screen.Primary ? (byte)70 : (byte)255;
        }

        public static string Title(string mode)
        {
            switch (mode)
            {
                case "span": return "Span";
                case "mirror": return "Mirror";
                case "spotlight": return "Spotlight";
                default: return "Native";
            }
        }

        public static string Description(string mode)
        {
            switch (mode)
            {
                case "span": return "One scene stretched across all your monitors, like a single giant screen.";
                case "mirror": return "Every monitor shows the full scene.";
                case "spotlight": return "Main monitor in full glow, the others show a dimmed echo of the scene.";
                default: return "Each monitor shows what Wallpaper Engine draws on it (needs a wallpaper on every monitor).";
            }
        }
    }
}
