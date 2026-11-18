using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Wakelight
{
    /// <summary>Read-only view of Wallpaper Engine: where it lives, what's selected, where it renders.</summary>
    public class WallpaperInfo
    {
        public string File;        // as stored in config.json
        public string Folder;
        public string Title;
        public string Type;        // scene / video / web / application
        public string VideoPath;   // for video wallpapers
        public string PreviewPath; // preview.gif / .jpg
    }

    public static class WallpaperEngine
    {
        static readonly string[] VideoExt = { ".mp4", ".webm", ".mkv", ".avi", ".mov", ".wmv", ".m4v" };

        /// <summary>Find the Wallpaper Engine folder: setting, running process, then Steam libraries.</summary>
        public static string FindFolder(string configured)
        {
            if (IsWeFolder(configured)) return configured;

            foreach (var name in new[] { "wallpaper64", "wallpaper32", "ui32" })
            {
                foreach (var p in Process.GetProcessesByName(name))
                {
                    try
                    {
                        string dir = Path.GetDirectoryName(p.MainModule.FileName);
                        if (IsWeFolder(dir)) return dir;
                        string up = Path.GetDirectoryName(dir);
                        if (IsWeFolder(up)) return up;
                    }
                    catch { }
                }
            }

            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    string steam = k == null ? null : k.GetValue("SteamPath") as string;
                    if (steam != null)
                    {
                        steam = steam.Replace('/', '\\');
                        var libs = new System.Collections.Generic.List<string> { steam };
                        string vdf = Path.Combine(steam, @"steamapps\libraryfolders.vdf");
                        if (System.IO.File.Exists(vdf))
                            foreach (Match m in Regex.Matches(System.IO.File.ReadAllText(vdf), "\"path\"\\s*\"([^\"]+)\""))
                                libs.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
                        foreach (var lib in libs)
                        {
                            string d = Path.Combine(lib, @"steamapps\common\wallpaper_engine");
                            if (IsWeFolder(d)) return d;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        public static bool IsWeFolder(string dir)
        {
            return !string.IsNullOrEmpty(dir) && System.IO.File.Exists(Path.Combine(dir, "config.json")) &&
                   (System.IO.File.Exists(Path.Combine(dir, "wallpaper64.exe")) || System.IO.File.Exists(Path.Combine(dir, "wallpaper32.exe")));
        }

        public static bool IsRunning()
        {
            return Process.GetProcessesByName("wallpaper64").Length > 0 || Process.GetProcessesByName("wallpaper32").Length > 0;
        }

        static string ReadShared(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sr = new StreamReader(fs)) return sr.ReadToEnd();
        }

        /// <summary>The wallpaper file selected for the first monitor, as written in config.json.</summary>
        public static string SelectedFile(string weDir)
        {
            try
            {
                if (weDir == null) return null;
                string json = ReadShared(Path.Combine(weDir, "config.json"));
                int i = json.IndexOf("\"selectedwallpapers\"", StringComparison.Ordinal);
                if (i < 0) return null;
                var m = Regex.Match(json.Substring(i), "\"file\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
                return m.Success ? Regex.Unescape(m.Groups[1].Value).Replace('/', '\\') : null;
            }
            catch { return null; }
        }

        static string JsonString(string json, string key)
        {
            var m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            return m.Success ? Regex.Unescape(m.Groups[1].Value) : null;
        }

        public static WallpaperInfo Describe(string file)
        {
            if (string.IsNullOrEmpty(file)) return null;
            var info = new WallpaperInfo();
            info.File = file;
            info.Folder = Directory.Exists(file) ? file : Path.GetDirectoryName(file);
            info.Title = Path.GetFileNameWithoutExtension(file);
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (VideoExt.Contains(ext)) { info.Type = "video"; info.VideoPath = file; }

            try
            {
                string proj = Path.Combine(info.Folder, "project.json");
                if (System.IO.File.Exists(proj))
                {
                    string pj = ReadShared(proj);
                    string t = JsonString(pj, "title"); if (!string.IsNullOrEmpty(t)) info.Title = t;
                    // "type" also appears inside user properties (e.g. "color"), so take the known kinds only
                    foreach (Match tm in Regex.Matches(pj, "\"type\"\\s*:\\s*\"([^\"]+)\""))
                    {
                        string v = tm.Groups[1].Value.ToLowerInvariant();
                        if (v == "scene" || v == "video" || v == "web" || v == "application") { info.Type = v; break; }
                    }
                    string f = JsonString(pj, "file");
                    if (info.Type == "video" && info.VideoPath == null && f != null)
                    {
                        string v = Path.Combine(info.Folder, f);
                        if (System.IO.File.Exists(v)) info.VideoPath = v;
                    }
                    string pv = JsonString(pj, "preview");
                    if (pv != null && System.IO.File.Exists(Path.Combine(info.Folder, pv))) info.PreviewPath = Path.Combine(info.Folder, pv);
                }
            }
            catch { }

            if (info.PreviewPath == null)
                foreach (var n in new[] { "preview.gif", "preview.jpg", "preview.png" })
                    if (System.IO.File.Exists(Path.Combine(info.Folder, n))) { info.PreviewPath = Path.Combine(info.Folder, n); break; }
            if (info.Type == null) info.Type = "scene";
            return info;
        }

        /// <summary>Top-level window that hosts Wallpaper Engine's render (Progman on 24H2+, a WorkerW before).</summary>
        public static IntPtr FindRenderHost()
        {
            IntPtr found = IntPtr.Zero;
            Native.EnumProc child = (h, l) =>
            {
                if (Native.ClassOf(h).StartsWith("WPEDesktop", StringComparison.Ordinal) && Native.IsWindowVisible(h))
                { found = h; return false; }
                return true;
            };
            Native.EnumWindows((h, l) =>
            {
                string c = Native.ClassOf(h);
                if (c == "Progman" || c == "WorkerW") Native.EnumChildWindows(h, child, IntPtr.Zero);
                return found == IntPtr.Zero;
            }, IntPtr.Zero);
            return found == IntPtr.Zero ? IntPtr.Zero : Native.GetAncestor(found, 2);
        }
    }
}
