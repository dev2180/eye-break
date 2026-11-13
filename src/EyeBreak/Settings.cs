using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace EyeBreak;

public sealed class Settings
{
    public int IntervalMinutes { get; set; } = 20;
    public int BreakSeconds { get; set; } = 20;
    public bool HideFromScreenShare { get; set; } = true;
    public string Music { get; set; } = "interstellar";   // a preset key, or "custom"
    public string CustomMusic { get; set; } = "";         // a YouTube link, used when Music is "custom"
    public int SessionMinutes { get; set; } = 0;           // infinite mode timer; 0 = until Esc
    public string Quotes { get; set; } = "calm";          // "calm" (our own list) or "zen" (ZenQuotes, online)

    static string FilePath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EyeBreak", "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch { /* corrupt file: fall back to defaults */ }
        return new Settings();
    }

    public void Save()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}

/// <summary>Whether this copy came from the Microsoft Store (an MSIX package) rather than EyeBreak-Setup.exe.</summary>
public static class StoreApp
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern int GetCurrentPackageFullName(ref int length, System.Text.StringBuilder? name);

    /// <summary>The Store installs, starts at login and updates the app itself, so the app leaves those to it.</summary>
    public static readonly bool IsStore = Check();

    static bool Check()
    {
        int len = 0;
        return GetCurrentPackageFullName(ref len, null) != 15700;   // APPMODEL_ERROR_NO_PACKAGE
    }
}

/// <summary>Start with Windows, via the current user's Run key. Off unless the user turns it on.
/// (The Store version uses the package's startup task instead, managed in Windows Settings.)</summary>
public static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "EyeBreak";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(Name) is string;
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(Name, $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue(Name, false);
    }
}
