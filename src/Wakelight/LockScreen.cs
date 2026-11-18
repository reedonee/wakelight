using System;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace Wakelight
{
    /// <summary>
    /// Sets the Windows lock screen image through the PersonalizationCSP policy key.
    /// The installer grants the user write access to that one key (needs admin once).
    /// </summary>
    public static class LockScreen
    {
        const string CspKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\PersonalizationCSP";

        public static string Folder
        {
            get
            {
                if (Demo.On) return Path.Combine(Demo.Folder, "data", "lockscreen");
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wakelight", "lockscreen");
            }
        }

        public static string NewSnapshotPath()
        {
            Directory.CreateDirectory(Folder);
            return Path.Combine(Folder, "lock_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".jpg");
        }

        public static bool HasPermission()
        {
            if (Demo.On) return true;
            try
            {
                using (var b = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var k = b.OpenSubKey(CspKey, RegistryKeyPermissionCheck.ReadWriteSubTree,
                           System.Security.AccessControl.RegistryRights.SetValue | System.Security.AccessControl.RegistryRights.QueryValues))
                    return k != null;
            }
            catch { return false; }
        }

        /// <summary>Apply an image (jpg/png/gif - gifs use their first frame).</summary>
        public static bool Apply(string image)
        {
            if (Demo.On) { Log.Write("demo: lock screen not changed"); return true; }
            try
            {
                string path = image;
                if (!path.StartsWith(Folder, StringComparison.OrdinalIgnoreCase) ||
                    !path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                {
                    // copy/convert into our folder under a unique name (Windows caches by path)
                    path = NewSnapshotPath();
                    using (var img = System.Drawing.Image.FromFile(image))
                        img.Save(path, System.Drawing.Imaging.ImageFormat.Jpeg);
                }
                using (var b = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var k = b.OpenSubKey(CspKey, RegistryKeyPermissionCheck.ReadWriteSubTree,
                           System.Security.AccessControl.RegistryRights.SetValue | System.Security.AccessControl.RegistryRights.QueryValues))
                {
                    if (k == null) { Log.Write("lock screen: key missing"); return false; }
                    k.SetValue("LockScreenImagePath", path, RegistryValueKind.String);
                    k.SetValue("LockScreenImageUrl", path, RegistryValueKind.String);
                    k.SetValue("LockScreenImageStatus", 1, RegistryValueKind.DWord);
                }
                Prune(path);
                Log.Write("lock screen set: " + path);
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("lock screen failed: " + ex.Message);
                return false;
            }
        }

        static void Prune(string keep)
        {
            try
            {
                foreach (var f in new DirectoryInfo(Folder).GetFiles("lock_*.jpg").OrderByDescending(f => f.LastWriteTime).Skip(3))
                    if (!f.FullName.Equals(keep, StringComparison.OrdinalIgnoreCase)) f.Delete();
            }
            catch { }
        }
    }
}
