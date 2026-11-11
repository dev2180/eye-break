using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace EyeBreak;

/// <summary>Detects a game that owns the display in exclusive fullscreen.</summary>
public static class Fullscreen
{
    const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;

    [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);

    public static bool IsExclusive() =>
        SHQueryUserNotificationState(out int state) == 0 && state == QUNS_RUNNING_D3D_FULL_SCREEN;
}

/// <summary>
/// Fades every display to black at the graphics-card output (gamma ramp). This works even when a game
/// owns the screen and no window can be drawn over it. Originals are always restored, and a marker file
/// lets the next launch repair the screen if the app was killed mid-break.
/// </summary>
public static class GammaDimmer
{
    sealed class Display
    {
        public IntPtr Dc;
        public ushort[] Original = new ushort[768];
        public double Floor = 1; // darkest level Windows accepted
    }

    static readonly List<Display> _displays = new();
    static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EyeBreak");
    static readonly string Marker = Path.Combine(Folder, "dimmed.flag");
    static readonly string LogFile = Path.Combine(Folder, "log.txt");

    public static bool Active => _displays.Count > 0;

    /// <summary>True when Windows allows the gamma ramp to go fully black (registry GdiIcmGammaRange = 256).</summary>
    public static bool FullRangeUnlocked
    {
        get
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM");
            return key?.GetValue("GdiIcmGammaRange") is int v && v >= 256;
        }
    }

    public static void Begin()
    {
        if (Active) return;
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Marker, DateTime.Now.ToString("o"));

        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            IntPtr dc = CreateDC(null, screen.DeviceName, null, IntPtr.Zero);
            if (dc == IntPtr.Zero) continue;
            var d = new Display { Dc = dc };
            if (!GetDeviceGammaRamp(dc, d.Original)) d.Original = Identity();
            d.Floor = FindFloor(d);
            _displays.Add(d);
            Log($"dim begin {screen.DeviceName}: darkest accepted level {d.Floor:0.00}");
        }
    }

    /// <summary>1 = normal brightness, 0 = black (or as dark as Windows allows).</summary>
    public static void Set(double level)
    {
        level = Math.Clamp(level, 0, 1);
        foreach (var d in _displays)
            Apply(d, Math.Max(level, d.Floor));
    }

    public static void End()
    {
        foreach (var d in _displays)
        {
            if (!SetDeviceGammaRamp(d.Dc, d.Original)) SetDeviceGammaRamp(d.Dc, Identity());
            DeleteDC(d.Dc);
        }
        if (_displays.Count > 0) Log("dim end: restored");
        _displays.Clear();
        try { File.Delete(Marker); } catch { }
    }

    /// <summary>Called at startup: if a previous run was killed while dimmed, put every display back to normal.</summary>
    public static void RecoverIfNeeded()
    {
        if (!File.Exists(Marker)) return;
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            IntPtr dc = CreateDC(null, screen.DeviceName, null, IntPtr.Zero);
            if (dc == IntPtr.Zero) continue;
            SetDeviceGammaRamp(dc, Identity());
            DeleteDC(dc);
        }
        Log("recovered displays left dimmed by a previous run");
        try { File.Delete(Marker); } catch { }
    }

    static double FindFloor(Display d)
    {
        // Windows rejects ramps too far from normal unless the range is unlocked. Find the darkest it accepts.
        for (double level = 0; level < 1; level += 0.05)
            if (Apply(d, level)) { Apply(d, 1); return level; }
        return 1;
    }

    static bool Apply(Display d, double level)
    {
        var ramp = new ushort[768];
        for (int i = 0; i < 768; i++) ramp[i] = (ushort)(d.Original[i] * level);
        return SetDeviceGammaRamp(d.Dc, ramp);
    }

    static ushort[] Identity()
    {
        var ramp = new ushort[768];
        for (int i = 0; i < 256; i++)
        {
            ushort v = (ushort)(i * 257);
            ramp[i] = ramp[256 + i] = ramp[512 + i] = v;
        }
        return ramp;
    }

    public static void Log(string line)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.AppendAllText(LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}\n");
        }
        catch { }
    }

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateDC(string? driver, string device, string? output, IntPtr init);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool GetDeviceGammaRamp(IntPtr dc, [Out] ushort[] ramp);
    [DllImport("gdi32.dll")] static extern bool SetDeviceGammaRamp(IntPtr dc, [In] ushort[] ramp);
}
