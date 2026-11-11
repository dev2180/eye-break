using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace EyeBreak;

/// <summary>Everything the overlays need to draw one frame. Shared by every monitor.</summary>
public sealed class EyeState
{
    public bool Vertical;          // false = closing eyelids, true = third eye opening
    public double P;               // eyelid closure 0 (open) .. 1 (shut)
    public double Len;             // third eye: length of the golden line 0..1
    public double Wid;             // third eye: how wide the vertical eye has opened 0..1
    public double Grow;            // third eye: expansion until the screen is uncovered 0..1
    public double Glow;            // third eye: golden edge opacity 0..1
    public double Star;            // the white star at the centre 0..1
    public double Flash;           // the star's flare (a wide bloom) 0..1
    public double Aura;            // the pulsing lingam-shaped aura around the star 0..1
    public double Birth;           // the star's birth: 0 = a single dot of light, 1 = its full light spread out
    public double Time;            // seconds, drives the aura's breathing
    public double MessageOpacity;  // fallback countdown, only if the black hole can't run
    public int Countdown;
}

/// <summary>A borderless, transparent, always-on-top window covering one monitor: eyelids, star and third eye.</summary>
public sealed class OverlayWindow : Window
{
    static readonly Color LidColor = Colors.Black;
    static readonly Color RimColor = Color.FromRgb(0x2A, 0x2A, 0x2A);
    static readonly Color Gold = Color.FromRgb(0xEF, 0x9F, 0x27);

    readonly System.Drawing.Rectangle _bounds;
    readonly Path _lid, _rim, _gold;
    readonly Grid _starLayer = new() { IsHitTestVisible = false };
    readonly Image _bloom = Sprite(), _linga = Sprite(), _halo = Sprite(), _haloFar = Sprite(), _glow = Sprite(), _core = Sprite();
    readonly ScaleTransform _coreScale = new(0.02, 0.02), _glowScale = new(0.02, 0.02);
    readonly TaskCompletionSource _art = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes once the star and aura images for this monitor are rendered.</summary>
    public Task ArtReady => _art.Task;

    static Image Sprite() => new()
    {
        Stretch = Stretch.Fill, Opacity = 0, IsHitTestVisible = false,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        RenderTransformOrigin = new Point(0.5, 0.5),
    };
    readonly StackPanel _msg;
    readonly TextBlock _count;
    IntPtr _hwnd;

    public bool AllowClose { get; set; }

    public OverlayWindow(System.Drawing.Rectangle boundsInPixels, bool hideFromCapture)
    {
        _bounds = boundsInPixels;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Left = 0; Top = 0; Width = 100; Height = 100; // real size is set in pixels once the window exists
        // No mouse pointer over the closed eyes or the star. Where the third eye has opened, the window is
        // transparent, so the pointer belongs to the app underneath again and comes back by itself.
        Cursor = System.Windows.Input.Cursors.None;
        ForceCursor = true;

        _lid = new Path { Fill = new SolidColorBrush(LidColor) };
        _rim = new Path { Stroke = new SolidColorBrush(RimColor), StrokeThickness = 3 };
        _gold = new Path
        {
            Stroke = new SolidColorBrush(Gold),
            StrokeThickness = 3,
            StrokeLineJoin = PenLineJoin.Round,
            Effect = new DropShadowEffect { Color = Gold, BlurRadius = 28, ShadowDepth = 0, Opacity = 1 },
            Opacity = 0,
        };
        _core.RenderTransform = _coreScale;
        _glow.RenderTransform = _glowScale;

        _count = new TextBlock { FontSize = 64, FontWeight = FontWeights.Light, HorizontalAlignment = HorizontalAlignment.Center };
        _msg = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0, IsHitTestVisible = false };
        var text = new SolidColorBrush(Color.FromRgb(0xD3, 0xD1, 0xC7));
        _msg.Children.Add(new TextBlock { Text = "Look at something 20 feet away", FontSize = 28, FontWeight = FontWeights.Light, Foreground = text, HorizontalAlignment = HorizontalAlignment.Center });
        _count.Foreground = text;
        _count.Margin = new Thickness(0, 12, 0, 12);
        _msg.Children.Add(_count);

        var root = new Grid { Background = Brushes.Transparent };
        root.Children.Add(_lid);
        root.Children.Add(_rim);
        foreach (var img in new[] { _bloom, _haloFar, _halo, _linga, _glow, _core })
            _starLayer.Children.Add(img);
        root.Children.Add(_starLayer);
        root.Children.Add(_gold);
        root.Children.Add(_msg);
        Content = root;

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
            SetWindowLong(_hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW); // hide from Alt+Tab
            // Screen sharing and recorders skip this window, so viewers keep seeing your normal screen.
            if (hideFromCapture) SetWindowDisplayAffinity(_hwnd, WDA_EXCLUDEFROMCAPTURE);
            // Move onto the target monitor first, then size in that monitor's DPI so WPF agrees with the pixels.
            SetWindowPos(_hwnd, HWND_TOPMOST, _bounds.Left, _bounds.Top, _bounds.Width, _bounds.Height, SWP_NOACTIVATE);
            double scale = GetDpiForWindow(_hwnd) / 96.0;
            Width = _bounds.Width / scale;
            Height = _bounds.Height / scale;
            SetWindowPos(_hwnd, HWND_TOPMOST, _bounds.Left, _bounds.Top, _bounds.Width, _bounds.Height, SWP_NOACTIVATE);
            _ = LoadArtAsync(scale);
        };
        Closing += (_, e) => { if (!AllowClose) e.Cancel = true; }; // blocks Alt+F4
    }

    /// <summary>Renders the star and aura for this monitor's size off the UI thread, then shows them pixel-exact.</summary>
    async Task LoadArtAsync(double scale)
    {
        try
        {
            var art = await Task.Run(() => StarArt.Build(_bounds.Height));
            void Set(Image img, System.Windows.Media.Imaging.BitmapSource b, double sizeK = 1)
            {
                img.Source = b;
                img.Width = b.PixelWidth * sizeK / scale;
                img.Height = b.PixelHeight * sizeK / scale;
            }
            Set(_core, art.Core); Set(_glow, art.Glow); Set(_linga, art.Linga);
            Set(_halo, art.Halo); Set(_haloFar, art.HaloFar);
            Set(_bloom, art.Bloom, 1.6 * _bounds.Height / art.Bloom.PixelHeight);
            foreach (var img in new[] { _core, _glow, _linga, _halo, _haloFar, _bloom })
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        }
        catch (Exception ex) { GammaDimmer.Log("star art failed: " + ex.Message); }
        _art.TrySetResult();
    }

    /// <summary>
    /// Windows only re-picks the pointer shape when the mouse moves. A one-pixel nudge and back makes it ask
    /// the window now under it (ours), so the pointer disappears without the user touching the mouse.
    /// </summary>
    public static void RefreshCursor()
    {
        if (!GetCursorPos(out var p)) return;
        SetCursorPos(p.X + 1, p.Y);
        SetCursorPos(p.X, p.Y);
    }

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);

    /// <summary>Re-asserts top-most so other "always on top" windows can't climb above us.</summary>
    public void KeepOnTop()
    {
        if (_hwnd != IntPtr.Zero)
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    public void Render(EyeState s)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        double cx = w / 2, cy = h / 2, r = Math.Sqrt(w * w + h * h) / 2;

        PathFigure eye;
        if (!s.Vertical)
        {
            // Horizontal almond: upper lid sweeps down, lower lid rises a little less.
            double o = 1 - s.P;
            double peak = o * h * 0.55 + Math.Pow(o, 5) * 2 * r;
            double hw = w * 0.52 + o * o * 2.5 * r, c = peak / 0.75, k = hw * 0.5;
            Point a = new(cx - hw, cy), b = new(cx + hw, cy);
            eye = Figure(a, true,
                new BezierSegment(new(cx - k, cy - c), new(cx + k, cy - c), b, true),
                new BezierSegment(new(cx + k, cy + c * 0.85), new(cx - k, cy + c * 0.85), a, true));

            _rim.Data = s.P > 0.02 && s.P < 0.999
                ? new PathGeometry(new[] { Figure(a, false, new BezierSegment(new(cx - k, cy - c), new(cx + k, cy - c), b, true)) })
                : null;
            _rim.Opacity = Math.Min(1, s.P * 1.5);
            _gold.Data = null;
        }
        else
        {
            // Vertical third eye, opening from the star at the centre of the screen.
            double hv = s.Len * h * 0.42 + s.Grow * s.Grow * 2.5 * r;
            double c = s.Wid * 0.3 * Math.Min(w, h) + s.Grow * 2.2 * r, k = hv * 0.5;
            Point top = new(cx, cy - hv), bottom = new(cx, cy + hv);
            eye = Figure(top, true,
                new BezierSegment(new(cx - c, cy - k), new(cx - c, cy + k), bottom, true),
                new BezierSegment(new(cx + c, cy + k), new(cx + c, cy - k), top, true));

            _rim.Data = null;
            _gold.Data = new PathGeometry(new[] { eye.Clone() });
            _gold.Opacity = s.Glow;
        }

        var lid = new PathGeometry { FillRule = FillRule.EvenOdd };
        var screen = new PathFigure { StartPoint = new(-50, -50), IsClosed = true };
        screen.Segments.Add(new PolyLineSegment(new[] { new Point(w + 50, -50), new Point(w + 50, h + 50), new Point(-50, h + 50) }, true));
        lid.Figures.Add(screen);
        lid.Figures.Add(eye);
        _lid.Data = lid;

        // The star sits on the darkness, never inside the opening eye.
        _starLayer.Clip = lid;
        // The star only ever changes brightness: the point lights first, then its glow spreads.
        // It breathes with the aura: a slow inhale/exhale of about 3.4 s.
        const double Breath = 3.4;
        double br = Math.Sin(2 * Math.PI * s.Time / Breath);
        // Born from a single dot: the light starts as one bright point and spreads outward to its full reach.
        // Once born, the size stays fixed and only the brightness changes.
        double born = 1 - Math.Pow(1 - Math.Clamp(s.Birth, 0, 1), 3);
        _coreScale.ScaleX = _coreScale.ScaleY = 0.02 + 0.98 * born;
        _glowScale.ScaleX = _glowScale.ScaleY = 0.02 + 0.98 * Math.Pow(born, 1.4);
        _core.Opacity = Math.Clamp(s.Star * 4, 0, 1);
        _glow.Opacity = Math.Clamp((s.Star - 0.15) / 0.85, 0, 1) * (0.86 + 0.14 * br * s.Aura);
        _bloom.Opacity = Math.Clamp(s.Flash, 0, 1) * (0.9 + 0.1 * br);

        // The linga stays perfectly still. Its aura breathes: the close glow brightens on the in-breath, and the
        // far glow follows a moment later, so the light seems to pour outward and settle back.
        _linga.Opacity = s.Aura;
        double near = 0.5 + 0.5 * br;
        double far = 0.5 + 0.5 * Math.Sin(2 * Math.PI * s.Time / Breath - 0.9);
        // Each breath the aura blooms out fully and fades away completely, then blooms again.
        _halo.Opacity = s.Aura * near * near;
        _haloFar.Opacity = s.Aura * far * far;

        _msg.Opacity = s.MessageOpacity;
        _count.Text = s.Countdown.ToString();
    }

    static PathFigure Figure(Point start, bool closed, params PathSegment[] segs)
    {
        var f = new PathFigure { StartPoint = start, IsClosed = closed };
        foreach (var seg in segs) f.Segments.Add(seg);
        return f;
    }

    const int GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80;
    const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;
    const uint WDA_EXCLUDEFROMCAPTURE = 0x11;
    static readonly IntPtr HWND_TOPMOST = new(-1);

    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
