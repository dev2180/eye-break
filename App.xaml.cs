using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace EyeBreak;

public partial class App : Application
{
    Mutex? _singleInstance;
    Forms.NotifyIcon? _tray;
    Flyout? _flyout;
    readonly BreakRunner _runner = new();
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };

    DateTime _nextBreak;
    DateTime? _pausedUntil;
    bool _pausedIndefinitely;
    bool _locked;

    public Settings Settings { get; private set; } = new();
    TimeSpan Interval => TimeSpan.FromMinutes(Settings.IntervalMinutes);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Build helper: write the app icon file and exit.
        int ico = Array.IndexOf(e.Args, "--write-icon");
        if (ico >= 0 && ico + 1 < e.Args.Length) { Logo.WriteIco(e.Args[ico + 1]); Shutdown(); return; }

        _singleInstance = new Mutex(true, "EyeBreak.SingleInstance", out bool first);
        if (!first) { Shutdown(); return; }

        // Never leave a screen dark: repair if a previous run was killed mid-break, and restore on any exit or crash.
        GammaDimmer.RecoverIfNeeded();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { GammaDimmer.End(); KeyBlocker.Stop(); };
        AppDomain.CurrentDomain.UnhandledException += (_, _) => GammaDimmer.End();
        // A tray app should never just disappear: log unexpected UI errors and keep running.
        DispatcherUnhandledException += (_, ex) =>
        {
            GammaDimmer.End();
            GammaDimmer.Log("error: " + ex.Exception);
            ex.Handled = true;
        };
        BreakRunner.ForceDim = e.Args.Contains("--force-dim");
        BreakRunner.ShowInCapture = e.Args.Contains("--show-in-capture");

        Settings = Settings.Load();
        _nextBreak = DateTime.Now + Interval;
        BuildTray();

        SystemEvents.SessionSwitch += OnSessionSwitch;
        _tick.Tick += (_, _) => Tick();
        _tick.Start();

        if (e.Args.Contains("--show-panel"))
            Dispatcher.BeginInvoke(ToggleFlyout, DispatcherPriority.ApplicationIdle);
        if (e.Args.Contains("--infinite-now"))
            Dispatcher.BeginInvoke(async () => await StartInfinite(), DispatcherPriority.ApplicationIdle);
        if (e.Args.Contains("--break-now"))
            Dispatcher.BeginInvoke(async () => await TakeBreak(), DispatcherPriority.ApplicationIdle);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    void Tick()
    {
        var now = DateTime.Now;
        if (_pausedUntil is DateTime until && now >= until)
        {
            _pausedUntil = null;
            _nextBreak = now + Interval;
        }

        if (!_runner.Running && !IsPaused && !_locked && now >= _nextBreak)
            _ = TakeBreak();

        UpdateTrayText();
    }

    public async Task TakeBreak()
    {
        if (_runner.Running) return;
        _flyout?.SafeClose();
        await _runner.RunAsync(Settings.BreakSeconds, Settings.HideFromScreenShare);
        _nextBreak = DateTime.Now + Interval;
        UpdateTrayText();
    }

    // Locked screen means you're away, so your eyes are already resting: restart the cycle when you're back.
    void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
            _locked = true;
        else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect)
        {
            _locked = false;
            _nextBreak = DateTime.Now + Interval;
        }
    }

    bool IsPaused => _pausedIndefinitely || _pausedUntil != null;

    public void Pause(TimeSpan? span)
    {
        if (span is TimeSpan s) { _pausedUntil = DateTime.Now + s; _pausedIndefinitely = false; }
        else { _pausedIndefinitely = true; _pausedUntil = null; }
        UpdateTrayText();
    }

    public void Resume()
    {
        _pausedIndefinitely = false;
        _pausedUntil = null;
        _nextBreak = DateTime.Now + Interval;
        UpdateTrayText();
    }

    /// <summary>Infinite mode: music and quotes by the black hole until Esc. Regular breaks wait until it ends.</summary>
    public async Task StartInfinite()
    {
        if (_runner.Running) return;
        _flyout?.SafeClose();
        var options = BuildInfiniteOptions();   // fetches quotes while the eyes close
        await _runner.RunInfiniteAsync(options, Settings.HideFromScreenShare);
        _nextBreak = DateTime.Now + Interval;   // you just rested; start a fresh cycle
        UpdateTrayText();
    }

    async Task<string> BuildInfiniteOptions()
    {
        var (quotes, credit) = await Infinite.GetQuotesAsync(Settings.Quotes);
        var (stations, station) = Infinite.StationsFor(Settings);
        return System.Text.Json.JsonSerializer.Serialize(new { stations, station, quotes, credit });
    }

    public void SetMusic(string key, string? custom = null)
    {
        Settings.Music = key;
        if (custom != null) Settings.CustomMusic = custom;
        Settings.Save();
    }

    /// <summary>Change the break interval or length. The cycle restarts only if the interval changed.</summary>
    public void SetTiming(int minutes, int seconds)
    {
        bool intervalChanged = minutes != Settings.IntervalMinutes;
        Settings.IntervalMinutes = minutes;
        Settings.BreakSeconds = seconds;
        Settings.Save();
        if (intervalChanged) _nextBreak = DateTime.Now + Interval;
        UpdateTrayText();
    }

    public void SetQuotes(string source)
    {
        Settings.Quotes = source;
        Settings.Save();
    }

    public void SetHideFromShare(bool hide)
    {
        Settings.HideFromScreenShare = hide;
        Settings.Save();
    }

    public record Status(string Big, string Unit, string Caption, double Fraction, bool Paused, bool Running);

    /// <summary>What the tray panel shows: the ring, the big number and the line under it.</summary>
    public Status GetStatus()
    {
        var now = DateTime.Now;
        if (_runner.Running) return new("·", "resting", "Eyes closed. Taking a break.", 1, false, true);
        if (_pausedIndefinitely) return new("—", "paused", "Breaks are paused until you resume", 0, true, false);
        if (_pausedUntil is DateTime until)
        {
            var p = until - now;
            return p.TotalMinutes >= 1
                ? new(((int)Math.Ceiling(p.TotalMinutes)).ToString(), "min", $"Paused · breaks resume at {until:t}", 1, true, false)
                : new(((int)Math.Ceiling(p.TotalSeconds)).ToString(), "sec", $"Paused · breaks resume at {until:t}", 1, true, false);
        }
        var left = _nextBreak - now;
        if (left < TimeSpan.Zero) left = TimeSpan.Zero;
        double frac = left.TotalSeconds / Interval.TotalSeconds;
        return left.TotalMinutes >= 1
            ? new(((int)Math.Ceiling(left.TotalMinutes)).ToString(), "min", "until your next break", frac, false, false)
            : new(((int)Math.Ceiling(left.TotalSeconds)).ToString(), "sec", "until your next break", frac, false, false);
    }

    public string StatusText()
    {
        if (_runner.Running) return "Eyes closed. Taking a break.";
        if (_pausedIndefinitely) return "Breaks paused until you resume";
        if (_pausedUntil is DateTime until) return $"Breaks paused until {until:t}";
        var left = _nextBreak - DateTime.Now;
        if (left < TimeSpan.Zero) left = TimeSpan.Zero;
        return left.TotalMinutes >= 1
            ? $"Next break in {(int)Math.Ceiling(left.TotalMinutes)} min"
            : $"Next break in {(int)Math.Ceiling(left.TotalSeconds)} sec";
    }

    void BuildTray()
    {
        _tray = new Forms.NotifyIcon { Icon = MakeIcon(), Visible = true };
        // Either click opens the panel; a click while it's open just closes it.
        _tray.MouseUp += (_, e) =>
        {
            if (e.Button is Forms.MouseButtons.Left or Forms.MouseButtons.Right) ToggleFlyout();
        };
        UpdateTrayText();
    }

    void UpdateTrayText()
    {
        if (_tray == null) return;
        string status = StatusText();
        _tray.Text = "Eye Break · " + status;
    }

    DateTime _flyoutClosedAt;
    void ToggleFlyout()
    {
        if (_runner.Running) return;
        if (_flyout != null) { _flyout.SafeClose(); return; }
        // the click that deactivated (closed) the panel shouldn't immediately reopen it
        if ((DateTime.Now - _flyoutClosedAt).TotalMilliseconds < 300) return;
        _flyout = new Flyout(this);
        _flyout.Closed += (_, _) => { _flyout = null; _flyoutClosedAt = DateTime.Now; };
        _flyout.Show();
        _flyout.Activate();
    }

    /// <summary>The logo, drawn for the tray: a golden vertical third eye with a white star at its centre.</summary>
    internal static Drawing.Icon MakeIcon(int size = 32)
    {
        using var bmp = Logo.Render(size);
        return Drawing.Icon.FromHandle(bmp.GetHicon());
    }
}
