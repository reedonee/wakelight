using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

[assembly: AssemblyTitle("Wakelight")]
[assembly: AssemblyProduct("Wakelight")]
[assembly: AssemblyDescription("Your live wallpaper, awake while you're away")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

namespace Wakelight
{
    public static class Program
    {
        public const string Version = "1.1";
        public static BitmapImage LogoImage;
        static Engine engine;
        static MainWindow main;
        static Forms.NotifyIcon tray;
        static Mutex single;
        static EventWaitHandle showSignal;

        [STAThread]
        public static int Main(string[] args)
        {
            bool trayOnly = Array.Exists(args, a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));

            bool created;
            string suffix = Demo.On ? ".Demo" : "";
            single = new Mutex(true, "Local\\Wakelight.App" + suffix, out created);
            showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\Wakelight.Show" + suffix);
            if (!created)
            {
                if (!trayOnly) showSignal.Set();   // bring the running instance forward
                return 0;
            }

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.DispatcherUnhandledException += (s, e) => { Log.Write("error: " + e.Exception); e.Handled = true; };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => { Log.Write("fatal: " + e.ExceptionObject); Showcase.EnsureIconsVisible(); };

            using (var s = Resource("Theme.xaml")) app.Resources = (ResourceDictionary)XamlReader.Load(s);
            LogoImage = LoadPng("wakelight.png");

            engine = new Engine();
            main = new MainWindow(engine);
            CreateTray();
            engine.Start();

            // Second launches signal us to show the window.
            var t = new Thread(() =>
            {
                while (true)
                {
                    showSignal.WaitOne();
                    app.Dispatcher.BeginInvoke(new Action(() => main.Show()));
                }
            }) { IsBackground = true };
            t.Start();

            if (!trayOnly || !engine.Settings.FirstRunDone)
            {
                main.Show();
                if (!engine.Settings.FirstRunDone)
                {
                    engine.Settings.FirstRunDone = true;
                    engine.Settings.Save();
                }
            }
            app.Run();
            return 0;
        }

        public static void Quit()
        {
            engine.Stop();
            tray.Visible = false;
            tray.Dispose();
            Log.Write("quit");
            Application.Current.Shutdown();
        }

        public static Stream Resource(string name)
        {
            return Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        }

        static BitmapImage LoadPng(string name)
        {
            var b = new BitmapImage();
            b.BeginInit(); b.StreamSource = Resource(name); b.CacheOption = BitmapCacheOption.OnLoad; b.EndInit();
            b.Freeze();
            return b;
        }

        // ------------------------------------------------------------------ tray
        static void CreateTray()
        {
            tray = new Forms.NotifyIcon();
            using (var s = Resource("wakelight.ico")) tray.Icon = new Icon(s, 32, 32);
            tray.Text = "Wakelight";
            tray.Visible = true;
            tray.MouseClick += (s, e) => { if (e.Button == Forms.MouseButtons.Left) main.Show(); };

            var menu = new Forms.ContextMenuStrip { Renderer = new DarkRenderer(), ShowImageMargin = false, Font = new Font("Segoe UI", 9.5f) };
            menu.BackColor = Color.FromArgb(0x16, 0x13, 0x2A);
            menu.ForeColor = Color.FromArgb(0xF4, 0xEE, 0xE6);
            var open = menu.Items.Add("Open Wakelight", null, (s, e) => main.Show());
            open.Font = new Font("Segoe UI Semibold", 9.5f);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Showcase now", null, (s, e) => engine.ShowNow(false));
            menu.Items.Add("Showcase && lock", null, (s, e) => engine.ShowNow(true));
            menu.Items.Add("Lock now", null, (s, e) => engine.LockNow());
            menu.Items.Add(new Forms.ToolStripSeparator());
            var pause = menu.Items.Add("Pause for 1 hour", null, (s, e) => { if (engine.IsPaused) engine.Resume(); else engine.Pause(TimeSpan.FromHours(1)); });
            menu.Opening += (s, e) => pause.Text = engine.IsPaused ? "Resume" : "Pause for 1 hour";
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Quit", null, (s, e) => Quit());
            foreach (Forms.ToolStripItem i in menu.Items) { i.ForeColor = menu.ForeColor; i.Padding = new Forms.Padding(6, 5, 6, 5); }
            tray.ContextMenuStrip = menu;
        }

        class DarkRenderer : Forms.ToolStripProfessionalRenderer
        {
            public DarkRenderer() : base(new DarkColors()) { RoundedEdges = true; }
            protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = Color.FromArgb(0xF4, 0xEE, 0xE6);
                base.OnRenderItemText(e);
            }
        }

        class DarkColors : Forms.ProfessionalColorTable
        {
            static readonly Color Bg = Color.FromArgb(0x16, 0x13, 0x2A), Hi = Color.FromArgb(0x2A, 0x24, 0x48), Ln = Color.FromArgb(0x30, 0x2A, 0x4C);
            public override Color ToolStripDropDownBackground { get { return Bg; } }
            public override Color MenuItemSelected { get { return Hi; } }
            public override Color MenuItemBorder { get { return Hi; } }
            public override Color MenuBorder { get { return Ln; } }
            public override Color SeparatorDark { get { return Ln; } }
            public override Color SeparatorLight { get { return Bg; } }
            public override Color ImageMarginGradientBegin { get { return Bg; } }
            public override Color ImageMarginGradientMiddle { get { return Bg; } }
            public override Color ImageMarginGradientEnd { get { return Bg; } }
        }
    }
}
