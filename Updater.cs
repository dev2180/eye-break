using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace EyeBreak;

/// <summary>
/// Updates from the GitHub releases page: compares the latest release's tag with this build's version, and on request
/// downloads its setup and runs it in update mode (it keeps your folder, shortcuts and startup choice, then reopens the app).
/// </summary>
public static class Updater
{
    const string LatestRelease = "https://api.github.com/repos/dev2180/eye-break/releases/latest";

    static readonly HttpClient Http = CreateClient();
    static HttpClient CreateClient()
    {
        var h = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };   // the download can be slow; the check has its own limit
        h.DefaultRequestHeaders.UserAgent.ParseAdd("EyeBreak-Updater");   // GitHub's API requires one
        return h;
    }

    static readonly Version Built = Assembly.GetEntryAssembly()!.GetName().Version!;
    /// <summary>This build's version (testing: --pretend-old makes it 0.0.0, so any release counts as newer).</summary>
    public static Version Current { get; set; } = new(Built.Major, Built.Minor, Math.Max(0, Built.Build));

    public static (Version Version, string Url)? Available { get; private set; }
    public static event Action? Changed;

    /// <summary>Asks GitHub for the latest release. Returns false if it couldn't be reached.</summary>
    public static async Task<bool> CheckAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(LatestRelease, cts.Token));
            string tag = doc.RootElement.GetProperty("tag_name").GetString()!.TrimStart('v', 'V');
            if (!Version.TryParse(tag, out var latest)) return false;
            string? url = null;
            foreach (var a in doc.RootElement.GetProperty("assets").EnumerateArray())
                if (a.GetProperty("name").GetString()!.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    url = a.GetProperty("browser_download_url").GetString();
            Available = latest > Current && url != null ? (latest, url) : null;
            Changed?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            GammaDimmer.Log("update check failed: " + ex.Message);
            return false;
        }
    }

    /// <summary>Downloads the new setup (reporting 0..1) and starts it in update mode. The caller then quits.</summary>
    public static async Task DownloadAndRunAsync(IProgress<double> progress)
    {
        var (version, url) = Available ?? throw new InvalidOperationException("No update available");
        string path = Path.Combine(Path.GetTempPath(), $"EyeBreak-Setup-{version}.exe");
        using (var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
        {
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? 0;
            await using var src = await resp.Content.ReadAsStreamAsync();
            await using var dst = File.Create(path);
            var buf = new byte[1 << 16];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buf)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n));
                done += n;
                if (total > 0) progress.Report((double)done / total);
            }
        }
        GammaDimmer.Log($"updating {Current} -> {version}");
        Process.Start(new ProcessStartInfo(path, "/update") { UseShellExecute = true });
    }
}
