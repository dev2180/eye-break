using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace EyeBreak;

/// <summary>The panel that opens from the tray icon: time to next break, pause, settings and quit.</summary>
public partial class Flyout : Window
{
    readonly App _app;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    static readonly int[] Intervals = { 5, 10, 15, 20, 25, 30, 40, 45, 60, 90, 120 };

    public Flyout(App app)
    {
        InitializeComponent();
        _app = app;
        MaxHeight = SystemParameters.WorkArea.Height;   // never taller than the screen; the rest scrolls
        AutostartSwitch.IsChecked = Autostart.IsEnabled;
        if (StoreApp.IsStore)
        {
            // The Store copy starts at login through its package; Windows Settings turns that on or off.
            AutostartSwitch.Visibility = Visibility.Collapsed;
            StartupManageBtn.Visibility = Visibility.Visible;
            CheckBtn.Visibility = Visibility.Collapsed;
        }
        HideSwitch.IsChecked = app.Settings.HideFromScreenShare;
        LinkBox.Text = app.Settings.CustomMusic;
        _loadingMusic = true;
        foreach (RadioButton r in MusicChips.Children)
            r.IsChecked = (string)r.Tag == app.Settings.Music;
        foreach (RadioButton r in QuoteChips.Children)
            r.IsChecked = (string)r.Tag == app.Settings.Quotes;
        _loadingMusic = false;
        LinkBox.Visibility = app.Settings.Music == "custom" ? Visibility.Visible : Visibility.Collapsed;
        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => { PlaceNearTray(); Refresh(); _timer.Start(); };
        SizeChanged += (_, _) => PlaceNearTray();
        // Close when you click elsewhere. Closing also deactivates the window, so guard against closing twice
        // (WPF throws if Close is called while a window is already closing).
        Deactivated += (_, _) => Dispatcher.BeginInvoke(SafeClose);
        Closing += (_, _) => _closing = true;
        Closed += (_, _) => _timer.Stop();
        ShowUpdate();
        Updater.Changed += ShowUpdate;
        Closed += (_, _) => Updater.Changed -= ShowUpdate;
    }

    void ShowUpdate()
    {
        VersionText.Text = StoreApp.IsStore ? $"Version {Updater.Current} · updates via Microsoft Store" : $"Version {Updater.Current}";
        if (Updater.Available is { } u && UpdateBtn.IsEnabled)
        {
            UpdateBtn.Content = $"Update to version {u.Version}";
            UpdateBtn.Visibility = Visibility.Visible;
        }
        else if (Updater.Available == null) UpdateBtn.Visibility = Visibility.Collapsed;
    }

    async void Check_Click(object sender, RoutedEventArgs e)
    {
        CheckBtn.Content = "Checking…";
        bool ok = await Updater.CheckAsync();
        CheckBtn.Content = !ok ? "Couldn't reach GitHub" : Updater.Available != null ? "Update available ↑" : "Up to date ✓";
    }

    async void Update_Click(object sender, RoutedEventArgs e)
    {
        UpdateBtn.IsEnabled = false;
        var progress = new Progress<double>(f => UpdateBtn.Content = $"Downloading update…  {f:P0}");
        try { await _app.InstallUpdate(progress); }
        catch (Exception ex)
        {
            GammaDimmer.Log("update download failed: " + ex.Message);
            UpdateBtn.Content = "Download failed · click to retry";
            UpdateBtn.IsEnabled = true;
        }
    }

    bool _closing;

    /// <summary>Close the panel once, no matter how many things ask for it.</summary>
    public void SafeClose()
    {
        if (_closing) return;
        _closing = true;
        Close();
    }

    void PlaceNearTray()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - ActualWidth + 4;
        Top = wa.Bottom - ActualHeight + 4;
    }

    void Refresh()
    {
        var s = _app.GetStatus();
        BigNumber.Text = s.Big;
        Unit.Text = s.Unit;
        Caption.Text = s.Caption;
        ResumeBtn.Visibility = s.Paused ? Visibility.Visible : Visibility.Collapsed;
        BreakNow.IsEnabled = !s.Running;
        Arc.Opacity = s.Paused ? 0.35 : 1;
        Arc.Data = ArcGeometry(s.Fraction);
        IntervalText.Text = $"{_app.Settings.IntervalMinutes} min";
        SecondsText.Text = $"{_app.Settings.BreakSeconds} sec";
        TimerText.Text = _app.Settings.SessionMinutes == 0 ? "Off" : $"{_app.Settings.SessionMinutes} min";
    }

    /// <summary>A clockwise arc starting at 12 o'clock, covering `fraction` of the ring.</summary>
    static Geometry ArcGeometry(double fraction)
    {
        const double cx = 84, cy = 84, r = 78;
        fraction = Math.Clamp(fraction, 0, 0.9999);
        if (fraction <= 0.001) return Geometry.Empty;
        double a = fraction * 2 * Math.PI;
        var end = new Point(cx + r * Math.Sin(a), cy - r * Math.Cos(a));
        var fig = new PathFigure { StartPoint = new Point(cx, cy - r) };
        fig.Segments.Add(new ArcSegment(end, new Size(r, r), 0, fraction > 0.5, SweepDirection.Clockwise, true));
        return new PathGeometry(new[] { fig });
    }

    async void BreakNow_Click(object sender, RoutedEventArgs e) { SafeClose(); await _app.TakeBreak(); }
    void Resume_Click(object sender, RoutedEventArgs e) { _app.Resume(); Refresh(); }

    void Pause_Click(object sender, RoutedEventArgs e)
    {
        int minutes = int.Parse((string)((Button)sender).Tag);
        _app.Pause(minutes == 0 ? null : TimeSpan.FromMinutes(minutes));
        Refresh();
    }

    void IntervalDown(object sender, RoutedEventArgs e) => StepInterval(-1);
    void IntervalUp(object sender, RoutedEventArgs e) => StepInterval(+1);
    void StepInterval(int dir)
    {
        int cur = _app.Settings.IntervalMinutes;
        int i = dir > 0
            ? Array.FindIndex(Intervals, v => v > cur)       // next step up
            : Array.FindLastIndex(Intervals, v => v < cur);  // next step down
        if (i < 0) return;                                   // already at the end of the range
        _app.SetTiming(Intervals[i], _app.Settings.BreakSeconds);
        Refresh();
    }

    void SecondsDown(object sender, RoutedEventArgs e) { _app.SetTiming(_app.Settings.IntervalMinutes, Math.Max(10, _app.Settings.BreakSeconds - 5)); Refresh(); }
    void SecondsUp(object sender, RoutedEventArgs e) { _app.SetTiming(_app.Settings.IntervalMinutes, Math.Min(120, _app.Settings.BreakSeconds + 5)); Refresh(); }

    void TimerDown(object sender, RoutedEventArgs e) { _app.SetSessionMinutes(_app.Settings.SessionMinutes - 5); Refresh(); }
    void TimerUp(object sender, RoutedEventArgs e) { _app.SetSessionMinutes(_app.Settings.SessionMinutes + 5); Refresh(); }

    bool _loadingMusic;

    void SettingsToggle_Click(object sender, RoutedEventArgs e)
    {
        bool show = SettingsPanel.Visibility != Visibility.Visible;
        SettingsPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        SettingsToggle.Content = show ? "Settings ▴" : "Settings ▾";
    }

    void Music_Checked(object sender, RoutedEventArgs e)
    {
        if (_loadingMusic) return;
        string key = (string)((RadioButton)sender).Tag;
        LinkBox.Visibility = key == "custom" ? Visibility.Visible : Visibility.Collapsed;
        LinkError.Visibility = Visibility.Collapsed;
        _app.SetMusic(key);
        if (key == "custom") LinkBox.Focus();
    }

    void Quotes_Checked(object sender, RoutedEventArgs e)
    {
        if (_loadingMusic) return;
        _app.SetQuotes((string)((RadioButton)sender).Tag);
    }

    void LinkBox_KeyUp(object sender, System.Windows.Input.KeyEventArgs e) { if (e.Key == System.Windows.Input.Key.Enter) LinkBox_Changed(sender, e); }
    void LinkBox_Changed(object sender, RoutedEventArgs e)
    {
        bool ok = LinkBox.Text.Trim() == "" || Infinite.ParseYouTube(LinkBox.Text) != null;
        LinkError.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
        if (ok) _app.SetMusic("custom", LinkBox.Text.Trim());
    }

    async void Infinite_Click(object sender, RoutedEventArgs e)
    {
        if (_app.Settings.Music == "custom")
        {
            LinkBox_Changed(sender, e);
            if (Infinite.ParseYouTube(LinkBox.Text) == null) { LinkError.Visibility = Visibility.Visible; LinkBox.Focus(); return; }
        }
        SafeClose();
        await _app.StartInfinite();
    }

    void Autostart_Click(object sender, RoutedEventArgs e) => Autostart.Set(AutostartSwitch.IsChecked == true);
    void StartupManage_Click(object sender, RoutedEventArgs e) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true });
    void Hide_Click(object sender, RoutedEventArgs e) => _app.SetHideFromShare(HideSwitch.IsChecked == true);
    void Report_Click(object sender, RoutedEventArgs e)
    {
        // ponytail: a marker in the local log is the whole report; Claude reads %APPDATA%\EyeBreak\log.txt
        GammaDimmer.Log("=== REPORTED by user ===");
        System.Diagnostics.Process.Start("notepad.exe", GammaDimmer.LogFile);
    }
    void Quit_Click(object sender, RoutedEventArgs e) { SafeClose(); _app.Shutdown(); }
}
