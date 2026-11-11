using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace EyeBreak;

/// <summary>
/// Full-screen, opaque, always-on-top window that plays the ray-traced black hole journey (web/blackhole.html)
/// while the eyes are closed. It loads off-screen during the eye close, and is moved over the monitor only
/// once the lids are fully shut, so the switch happens in complete darkness.
/// </summary>
public sealed class BlackHoleWindow : Window
{
    readonly System.Drawing.Rectangle _bounds;
    readonly bool _hideFromCapture;
    readonly WebView2 _web = new() { DefaultBackgroundColor = System.Drawing.Color.Black };
    readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource? _done;
    IntPtr _hwnd;

    public bool AllowClose { get; set; }
    public bool Revealed { get; private set; }

    public BlackHoleWindow(System.Drawing.Rectangle boundsInPixels, bool hideFromCapture)
    {
        _bounds = boundsInPixels;
        _hideFromCapture = hideFromCapture;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Background = Brushes.Black;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000; Top = -32000; Width = 100; Height = 100;
        Content = _web;

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(_hwnd, GWL_EXSTYLE, GetWindowLong(_hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW);
            if (_hideFromCapture) SetWindowDisplayAffinity(_hwnd, WDA_EXCLUDEFROMCAPTURE);
            // Full monitor size from the start (still off-screen), so the page renders at the right resolution.
            SetWindowPos(_hwnd, HWND_TOPMOST, -32000, -32000, _bounds.Width, _bounds.Height, SWP_NOACTIVATE);
        };
        Closing += (_, e) => { if (!AllowClose) e.Cancel = true; };
    }

    /// <summary>Starts the browser view and loads the page. Completes when the page says it's ready.</summary>
    public async Task LoadAsync()
    {
        var dataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EyeBreak", "WebView2");
        // Music must start without a click (the page is never clicked during a break).
        // And it shouldn't show up in Windows' media controls (no YouTube thumbnail by the volume or lock screen).
        var options = new CoreWebView2EnvironmentOptions(
            "--autoplay-policy=no-user-gesture-required --disable-features=HardwareMediaKeyHandling,MediaSessionService");
        var env = await CoreWebView2Environment.CreateAsync(null, dataFolder, options);
        await _web.EnsureCoreWebView2Async(env);
        var core = _web.CoreWebView2;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.WebMessageReceived += (_, e) =>
        {
            string msg = e.TryGetWebMessageAsString();
            if (msg == "ready") _ready.TrySetResult();
            else if (msg == "done") _done?.TrySetResult();
            else if (msg.StartsWith("music:")) GammaDimmer.Log(msg);   // which song played, or why it couldn't
            else if (msg.StartsWith("search:")) _ = SearchAsync(msg["search:".Length..]);
        };
        // Served from a local https address (not file://) so YouTube's embedded player accepts it.
        core.SetVirtualHostNameToFolderMapping("eyebreak.app", Path.Combine(AppContext.BaseDirectory, "web"),
            CoreWebView2HostResourceAccessKind.Allow);
        core.Navigate("https://eyebreak.app/blackhole.html");
        await _ready.Task;
    }

    /// <summary>Moves the window over its monitor (the page is black until the journey starts).</summary>
    public void Reveal()
    {
        SetWindowPos(_hwnd, HWND_TOPMOST, _bounds.Left, _bounds.Top, _bounds.Width, _bounds.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        Revealed = true;
    }

    /// <summary>Plays the journey and the white star. Completes when the star is at full flare.</summary>
    public async Task PlayAsync(int seconds)
    {
        _done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _web.CoreWebView2.ExecuteScriptAsync($"start({seconds})");
        await _done.Task;
    }

    async Task SearchAsync(string query)
    {
        var results = await YouTubeSearch.SearchAsync(query);
        string json = System.Text.Json.JsonSerializer.Serialize(results);
        try { await _web.CoreWebView2.ExecuteScriptAsync($"searchResults({json})"); } catch { }
    }

    /// <summary>Infinite mode: float by the black hole with music and quotes. Returns at once; ends with EndInfiniteAsync.</summary>
    public Task StartInfiniteAsync(string optionsJson) => _web.CoreWebView2.ExecuteScriptAsync($"infinite({optionsJson})");

    /// <summary>Fades the music, falls into the black hole and births the star. Completes at full flare.</summary>
    public async Task EndInfiniteAsync()
    {
        _done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _web.CoreWebView2.ExecuteScriptAsync("endInfinite()");
        await _done.Task;
    }

    public void KeepOnTop()
    {
        if (_hwnd != IntPtr.Zero && Revealed)
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    const int GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80;
    const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
    const uint WDA_EXCLUDEFROMCAPTURE = 0x11;
    static readonly IntPtr HWND_TOPMOST = new(-1);

    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);
}
