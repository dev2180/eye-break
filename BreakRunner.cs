using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Threading;

namespace EyeBreak;

/// <summary>
/// Runs one break on every monitor:
///   1. the eyes settle closed like starting meditation (native eyelids),
///   2. the ray-traced black hole journey with the countdown, ending in darkness and a white star (web view),
///   3. the third eye opens from that star at the centre of the screen (native again).
/// The hand-offs between 1→2 and 2→3 happen in complete darkness / on the same star, so they can't be seen.
/// </summary>
public sealed class BreakRunner
{
    readonly List<OverlayWindow> _windows = new();
    readonly EyeState _s = new();
    BlackHoleWindow? _cosmos;
    bool _journey; // true while the black hole window covers everything

    public bool Running { get; private set; }

    /// <summary>Testing aid: dim the displays even when no exclusive-fullscreen game is detected.</summary>
    public static bool ForceDim { get; set; }

    /// <summary>Testing aid: let screenshots see the overlay even if hiding from screen share is on.</summary>
    public static bool ShowInCapture { get; set; }

    /// <summary>A normal break: the timed black hole journey.</summary>
    public Task RunAsync(int seconds, bool hideFromCapture) => RunCore(hideFromCapture,
        async cosmos =>
        {
            var play = cosmos.PlayAsync(seconds);
            return await Task.WhenAny(play, Task.Delay((seconds + 20) * 1000)) == play;
        },
        () => CountdownFallback(seconds));

    /// <summary>
    /// Infinite mode: float by the black hole with music and quotes until Esc, then fall in, star, third eye.
    /// </summary>
    public async Task RunInfiniteAsync(Task<string> optionsJson, bool hideFromCapture)
    {
        var esc = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        KeyBlocker.OnEscape = () => esc.TrySetResult();
        try
        {
            await RunCore(hideFromCapture,
                async cosmos =>
                {
                    await cosmos.StartInfiniteAsync(await optionsJson);
                    await esc.Task;
                    KeyBlocker.OnEscape = null;
                    var end = cosmos.EndInfiniteAsync();
                    return await Task.WhenAny(end, Task.Delay(30000)) == end;
                },
                () => esc.Task);   // if the black hole can't show, stay quietly closed until Esc
        }
        finally { KeyBlocker.OnEscape = null; }
    }

    async Task RunCore(bool hideFromCapture, Func<BlackHoleWindow, Task<bool>> journey, Func<Task> fallback)
    {
        if (Running) return;
        Running = true;
        bool hide = hideFromCapture && !ShowInCapture;
        var topTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        EventHandler onFrame = (_, _) => Draw();
        try
        {
            // A game in exclusive fullscreen can hide our windows, so also fade the display output itself.
            if (ForceDim || Fullscreen.IsExclusive())
                GammaDimmer.Begin();

            KeyBlocker.Start(); // no Start menu or Alt+Tab pulling things over the break; typing still reaches your app

            foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            {
                var w = new OverlayWindow(screen.Bounds, hide);
                w.Show();
                _windows.Add(w);
            }

            // Load the black hole off-screen while the eyes close, on the main monitor.
            var primary = System.Windows.Forms.Screen.PrimaryScreen ?? System.Windows.Forms.Screen.AllScreens[0];
            _cosmos = new BlackHoleWindow(primary.Bounds, hide);
            _cosmos.Show();
            var loading = _cosmos.LoadAsync();

            topTimer.Tick += (_, _) =>
            {
                // While the black hole is showing, only it is kept on top: re-stacking the eyelid windows
                // under it every tick made the screen hitch.
                if (_cosmos is { Revealed: true }) _cosmos.KeepOnTop();
                else _windows.ForEach(w => w.KeepOnTop());
            };
            topTimer.Start();

            Reset();
            CompositionTarget.Rendering += onFrame;
            await Task.Delay(300);

            // 1. Settling into meditation: two slow, heavy half-closes, then a long gentle close.
            await Lids(0.55, 1400);
            await Lids(0.15, 1600);
            await Task.Delay(1200);
            await Lids(0.75, 1800);
            await Lids(0.40, 1600);
            await Task.Delay(900);
            await Lids(1.0, 3500);

            // 2. The black hole journey. Its page is black until it starts, so revealing it is invisible.
            bool played = false;
            try
            {
                if (await Task.WhenAny(loading, Task.Delay(8000)) == loading && loading.IsCompletedSuccessfully)
                {
                    _cosmos.Reveal();
                    _journey = true;
                    played = await journey(_cosmos);
                }
            }
            catch (Exception ex) { GammaDimmer.Log("black hole failed: " + ex.Message); }

            if (!played)
            {
                // Fallback: if the black hole can't run, a calm countdown on the closed eyelids.
                CloseCosmos();
                await fallback();
                await Tween(t => _s.Star = t, 1500, Sine);
            }

            // Hand-off on the star: a pixel-exact snapshot of the web star goes under the web view, then the web
            // view goes. Same pixels, same place, same brightness, so there's nothing to see.
            if (played && _cosmos != null && await _cosmos.CaptureAsync() is { } snapshot)
                _primary(primary)?.UseStarSnapshot(snapshot);
            _journey = false;
            _s.Star = 1;
            Draw();
            await Task.Delay(80);
            CloseCosmos();

            // 3. The third eye opens from the star: a golden line grows out of it, opens into a vertical eye,
            // then widens to reveal the screen. The star only ever changes brightness, never size.
            _s.Vertical = true;
            await Tween(t => { _s.Len = t; _s.Glow = t; }, 1800, EaseInOut);
            await Task.Delay(500);
            await Tween(t => { _s.Wid = t; _s.Star = 1 - 0.25 * t; }, 2000, EaseInOut);
            await Task.Delay(400);
            await Tween(t => { _s.Grow = t; _s.Glow = 1 - t; _s.Star = 0.75 * (1 - t); }, 2600, EaseInOut);
        }
        finally
        {
            CompositionTarget.Rendering -= onFrame;
            _journey = false;
            KeyBlocker.Stop();
            topTimer.Stop();
            GammaDimmer.End();
            CloseCosmos();
            foreach (var w in _windows) { w.AllowClose = true; w.Close(); }
            _windows.Clear();
            Running = false;
        }
    }

    /// <summary>The eyelid window on the given monitor.</summary>
    OverlayWindow? _primary(System.Windows.Forms.Screen screen)
    {
        var all = System.Windows.Forms.Screen.AllScreens;
        int i = Array.FindIndex(all, s => s.DeviceName == screen.DeviceName);
        return i >= 0 && i < _windows.Count ? _windows[i] : null;
    }

    void CloseCosmos()
    {
        if (_cosmos == null) return;
        _cosmos.AllowClose = true;
        _cosmos.Close();
        _cosmos = null;
    }

    async Task CountdownFallback(int seconds)
    {
        _ = Tween(t => _s.MessageOpacity = t, 1200, Sine);
        for (int i = seconds; i > 0; i--)
        {
            _s.Countdown = i;
            await Task.Delay(1000);
        }
        await Tween(t => _s.MessageOpacity = 1 - t, 800, Sine);
    }

    void Reset()
    {
        _s.Vertical = false; _s.P = 0; _s.Len = 0; _s.Wid = 0; _s.Grow = 0; _s.Glow = 0;
        _s.Star = 0; _s.Flash = 0; _s.MessageOpacity = 0; _s.Countdown = 0;
    }

    void Draw()
    {
        // Hidden under the black hole, the eyelid windows don't need redrawing (saves CPU for a smooth journey).
        if (_journey) return;
        foreach (var w in _windows) w.Render(_s);

        // Display brightness follows the eyelids: dark when shut, brightening as the third eye opens.
        if (GammaDimmer.Active)
            GammaDimmer.Set(_s.Vertical ? 0.25 * _s.Len + 0.75 * _s.Grow : 1 - _s.P);
    }

    Task Lids(double to, double ms)
    {
        double from = _s.P;
        return Tween(t => _s.P = from + (to - from) * t, ms, Sine);
    }

    /// <summary>Changes a value smoothly, once per rendered frame. Drawing happens in the frame loop.</summary>
    static Task Tween(Action<double> set, double ms, Func<double, double> ease)
    {
        var done = new TaskCompletionSource();
        var clock = Stopwatch.StartNew();
        EventHandler? onFrame = null;
        onFrame = (_, _) =>
        {
            double t = Math.Min(1, clock.Elapsed.TotalMilliseconds / ms);
            set(ease(t));
            if (t >= 1)
            {
                CompositionTarget.Rendering -= onFrame;
                done.TrySetResult();
            }
        };
        CompositionTarget.Rendering += onFrame;
        return done.Task;
    }

    static double Sine(double t) => -(Math.Cos(Math.PI * t) - 1) / 2;
    static double EaseInOut(double t) => t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;
}
