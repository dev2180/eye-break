using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Windows;
using Microsoft.Win32;

namespace EyeBreakSetup
{
    /// <summary>
    /// Per-user install (no admin): files go to %LOCALAPPDATA%\Programs\EyeBreak, plus a Start menu shortcut,
    /// an optional desktop shortcut, an optional "start with Windows" entry, and an entry in Installed apps.
    /// Setup file layout: [this exe][app zip][zip length: 8 bytes][magic "EYEBRK01"].
    /// </summary>
    static class Installer
    {
        public static readonly string Version = typeof(Installer).Assembly.GetName().Version.ToString(3);   // from Directory.Build.props
        const string Magic = "EYEBRK01";
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\EyeBreak";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static string DefaultDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "EyeBreak");

        /// <summary>Where Eye Break is installed now, if it is (an update keeps it there).</summary>
        public static string InstalledDir
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(UninstallKey))
                    return (k?.GetValue("InstallLocation") as string)?.TrimEnd('\\');
            }
        }
        public static bool HasDesktopShortcut => File.Exists(DesktopLink);
        public static bool StartsWithWindows
        {
            get { using (var run = Registry.CurrentUser.OpenSubKey(RunKey)) return run?.GetValue("EyeBreak") != null; }
        }

        static string SelfPath => Process.GetCurrentProcess().MainModule.FileName;
        static string StartMenuLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Eye Break.lnk");
        static string DesktopLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Eye Break.lnk");

        static void StopRunning()
        {
            foreach (var p in Process.GetProcessesByName("EyeBreak"))
                try { p.Kill(); p.WaitForExit(5000); } catch { }
        }

        public static void Install(string dir, bool desktop, bool startup, IProgress<(double done, string file)> progress)
        {
            StopRunning();   // an older running copy would lock its files
            Directory.CreateDirectory(dir);
            string root = Path.GetFullPath(dir).TrimEnd('\\') + "\\";
            string tmp = Path.Combine(Path.GetTempPath(), "EyeBreakPayload.zip");
            long setupLength;

            using (var self = File.OpenRead(SelfPath))
            {
                var br = new BinaryReader(self);
                if (self.Length < 16) throw Damaged();
                self.Seek(-16, SeekOrigin.End);
                long zipLength = br.ReadInt64();
                if (Encoding.ASCII.GetString(br.ReadBytes(8)) != Magic) throw Damaged();
                setupLength = self.Length - 16 - zipLength;
                if (zipLength <= 0 || setupLength <= 0) throw Damaged();
                self.Seek(setupLength, SeekOrigin.Begin);
                using (var outp = File.Create(tmp)) CopyBytes(self, outp, zipLength);
            }

            try
            {
                using (var zip = ZipFile.OpenRead(tmp))
                {
                    var files = zip.Entries.Where(e => e.Name.Length > 0).ToList();
                    for (int i = 0; i < files.Count; i++)
                    {
                        string target = Path.GetFullPath(Path.Combine(root, files[i].FullName));
                        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;   // never write outside the folder
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        files[i].ExtractToFile(target, true);
                        progress.Report(((i + 1.0) / files.Count, files[i].FullName));
                    }
                }
            }
            finally { File.Delete(tmp); }

            // The uninstaller is this same setup without the app attached.
            using (var self = File.OpenRead(SelfPath))
            using (var u = File.Create(Path.Combine(root, "uninstall.exe")))
                CopyBytes(self, u, setupLength);

            string exe = Path.Combine(root, "EyeBreak.exe");
            Shortcut(StartMenuLink, exe);
            if (desktop) Shortcut(DesktopLink, exe); else File.Delete(DesktopLink);

            using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
                if (startup) run.SetValue("EyeBreak", $"\"{exe}\""); else run.DeleteValue("EyeBreak", false);

            long kb = new DirectoryInfo(root).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) / 1024;
            using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", "Eye Break");
                k.SetValue("DisplayVersion", Version);
                k.SetValue("Publisher", "Eye Break");
                k.SetValue("DisplayIcon", exe);
                k.SetValue("InstallLocation", root);
                k.SetValue("UninstallString", $"\"{root}uninstall.exe\" /uninstall");
                k.SetValue("EstimatedSize", (int)kb, RegistryValueKind.DWord);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }

        public static void Launch(string dir) =>
            Process.Start(new ProcessStartInfo(Path.Combine(dir, "EyeBreak.exe")) { UseShellExecute = true, WorkingDirectory = dir });

        public static void Uninstall()
        {
            string dir;
            using (var k = Registry.CurrentUser.OpenSubKey(UninstallKey))
                dir = k?.GetValue("InstallLocation") as string;
            // Only ever delete a folder that really is an Eye Break install.
            if (string.IsNullOrEmpty(dir) || !File.Exists(Path.Combine(dir, "EyeBreak.exe")))
            {
                MessageBox.Show("Eye Break doesn't seem to be installed.", "Uninstall Eye Break");
                return;
            }
            if (MessageBox.Show("Remove Eye Break from this PC?\n\nYour settings and log in %APPDATA%\\EyeBreak are kept.",
                    "Uninstall Eye Break", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            StopRunning();
            File.Delete(StartMenuLink);
            File.Delete(DesktopLink);
            using (var run = Registry.CurrentUser.OpenSubKey(RunKey, true)) run?.DeleteValue("EyeBreak", false);
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);

            // This exe lives in the folder, so the folder is removed just after it exits.
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"{dir.TrimEnd('\\')}\"")
            { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
            MessageBox.Show("Eye Break was removed.", "Uninstall Eye Break");
        }

        static void Shortcut(string link, string target)
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            var lnk = shell.CreateShortcut(link);
            lnk.TargetPath = target;
            lnk.WorkingDirectory = Path.GetDirectoryName(target);
            lnk.IconLocation = target + ",0";
            lnk.Description = "Eye Break";
            lnk.Save();
        }

        static void CopyBytes(Stream from, Stream to, long count)
        {
            var buf = new byte[1 << 20];
            while (count > 0)
            {
                int n = from.Read(buf, 0, (int)Math.Min(buf.Length, count));
                if (n <= 0) throw Damaged();
                to.Write(buf, 0, n);
                count -= n;
            }
        }

        static Exception Damaged() => new InvalidDataException("This setup file is incomplete. Download it again.");
    }
}
