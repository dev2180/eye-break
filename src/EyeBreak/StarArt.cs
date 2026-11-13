using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EyeBreak;

/// <summary>
/// The white star and its aura, rendered once per monitor size with real light falloff (a saturated point,
/// a 1/r² tail, wide glow) and tone-mapped like a camera would, plus the fixed Dhyanalinga outline and the aura
/// of light around it. The overlay only changes brightness; the linga's shape never changes.
/// All images are premultiplied light on black, so drawn over the closed lids they add up like light.
/// </summary>
public sealed record StarArt(BitmapSource Core, BitmapSource Glow, BitmapSource Linga, BitmapSource Ripple, BitmapSource Bloom)
{
    /// <summary>Builds every image for a monitor that is <paramref name="h"/> pixels tall. Safe on any thread.</summary>
    public static StarArt Build(int h)
    {
        double k = h / 1080.0;   // everything is designed at 1080p and scaled
        int side = h;

        var core = Render(side, side, (x, y) =>
        {
            double r = Math.Sqrt(x * x + y * y) / k;
            double point = 60 / (1 + r * r / 16) * Math.Exp(-r / 260);
            // a pure radiant point: no rays or spikes, just light pouring out in every direction
            double i = point + 3.0 * Math.Exp(-r / 9);
            return (i, i * 0.995, i * 0.975);
        });

        var glow = Render(side, side, (x, y) =>
        {
            double r = Math.Sqrt(x * x + y * y) / k;
            double i = 1.7 * Math.Exp(-r / 34) + 0.5 * Math.Exp(-r / 95) + 0.16 * Math.Exp(-r / 260);
            // warmer further out, like light scattering through the lids
            return (i, i * (0.93 + 0.05 * Math.Exp(-r / 60)), i * (0.80 + 0.15 * Math.Exp(-r / 60)));
        });

        // The aura: the outline of the Dhyanalinga (as engraved on its pendant), without the yoni. A tall form
        // about twice as high as it is wide: a full round dome on a straight, upright pillar with a flat base.
        // The star sits at its heart.
        // A bright golden outline, a soft inner glow and a faint pillar of light through the star.
        int aw = (int)(0.8 * h), ah = (int)(1.0 * h);   // room for the aura to fade out fully before the edges
        var sdf = DhyanalingaField(aw, ah, top: -0.22 * h, height: 0.44 * h, halfWidth: 0.118 * h);
        double LingamS(double x, double y)
        {
            int i = Math.Clamp((int)(x + aw / 2.0), 0, aw - 1), j = Math.Clamp((int)(y + ah / 2.0), 0, ah - 1);
            return sdf[j * aw + i];
        }
        double line = 0.012 * h;
        // The linga itself never moves or changes: a crisp golden outline, a soft light inside, the pillar of light.
        var linga = Render(aw, ah, (x, y) =>
        {
            double s = LingamS(x, y);
            double shell = 0.8 * Math.Exp(-Math.Pow(s / line, 2));
            double inner = s < 0 ? 0.16 * (1 - Math.Exp(s / (0.06 * h))) * Math.Exp(-(x * x + y * y) / Math.Pow(0.22 * h, 2)) : 0;
            double pillar = 0.4 * Math.Exp(-Math.Pow(x / (0.03 * h), 2)) * Math.Exp(-Math.Pow(y / (0.26 * h), 2));
            double white = inner + pillar;
            return (shell + white, shell * 0.72 + white * 0.93, shell * 0.30 + white * 0.80);
        });

        // The aura: a ring of light in the linga's own shape. The overlay sends copies of it out from the fixed
        // outline with each pulse of the star, expanding and fading like ripples on water.
        var ripple = Render(aw, ah, (x, y) =>
        {
            double s = LingamS(x, y);
            double i = 0.95 * Math.Exp(-Math.Pow(s / (0.0055 * h), 2)) + 0.18 * Math.Exp(-Math.Pow(s / (0.02 * h), 2));
            return (i, i * 0.80, i * 0.45);
        });

        // A wide soft bloom for the flare, rendered small (it's smooth) and stretched by the overlay.
        int bs = Math.Max(64, h / 4);
        var bloom = Render(bs, bs, (x, y) =>
        {
            double r = Math.Sqrt(x * x + y * y) / (bs / 2.0);
            double i = 0.75 * Math.Exp(-r * r * 9) + 0.25 * Math.Exp(-r * 3.5);
            return (i, i * 0.95, i * 0.86);
        });

        return new StarArt(core, glow, linga, ripple, bloom);
    }

    /// <summary>Half-width of the Dhyanalinga at height t (0 = crown, 1 = base), as a fraction of its widest half.</summary>
    static double Profile(double t)
    {
        const double dome = 0.27;   // the dome takes the top 27% of the height
        if (t < dome)
        {
            double u = (dome - t) / dome;
            return Math.Sqrt(Math.Max(0, 1 - u * u));
        }
        return 1;   // a straight pillar from the dome down to a flat base
    }

    /// <summary>
    /// Signed distance (pixels, negative inside) to the Dhyanalinga outline for every pixel of a w×h image
    /// centred on the star. The outline is traced from the profile as a closed polygon.
    /// </summary>
    static double[] DhyanalingaField(int w, int h, double top, double height, double halfWidth)
    {
        const int n = 120;
        var pts = new List<(double x, double y)>();
        for (int k = 0; k <= n; k++) { double t = (double)k / n; pts.Add((halfWidth * Profile(t), top + t * height)); }
        for (int k = n; k >= 0; k--) { double t = (double)k / n; pts.Add((-halfWidth * Profile(t), top + t * height)); }
        var field = new double[w * h];
        double hx = w / 2.0, hy = h / 2.0;
        Parallel.For(0, h, j =>
        {
            double y = j + 0.5 - hy;
            double t = (y - top) / height;
            for (int i = 0; i < w; i++)
            {
                double x = i + 0.5 - hx;
                double best = double.MaxValue;
                for (int s = 0; s < pts.Count; s++)
                {
                    var p = pts[s]; var q = pts[(s + 1) % pts.Count];
                    double dx = q.x - p.x, dy = q.y - p.y;
                    double u = Math.Clamp(((x - p.x) * dx + (y - p.y) * dy) / (dx * dx + dy * dy + 1e-9), 0, 1);
                    double ex = x - p.x - u * dx, ey = y - p.y - u * dy;
                    best = Math.Min(best, ex * ex + ey * ey);
                }
                bool inside = t >= 0 && t <= 1 && Math.Abs(x) <= halfWidth * Profile(t);
                field[j * w + i] = inside ? -Math.Sqrt(best) : Math.Sqrt(best);
            }
        });
        return field;
    }

    /// <summary>
    /// Renders light (any intensity) into a premultiplied image, tone-mapped with 1 - e^-x, dithered so the
    /// long glows don't band, and faded to nothing at the edges.
    /// </summary>
    static BitmapSource Render(int w, int h, Func<double, double, (double r, double g, double b)> light)
    {
        var px = new byte[w * h * 4];
        var rnd = new Random(7);
        double hx = w / 2.0, hy = h / 2.0;
        for (int j = 0; j < h; j++)
        {
            double y = j + 0.5 - hy;
            for (int i = 0; i < w; i++)
            {
                double x = i + 0.5 - hx;
                var (r, g, b) = light(x, y);
                // Fade out in a soft oval, never along the image's square edges (those read as a box).
                double ex = Math.Sqrt(Math.Pow(x / hx, 2) + Math.Pow(y / hy, 2));
                double u = Math.Clamp((ex - 0.55) / 0.42, 0, 1);
                double edge = 1 - u * u * (3 - 2 * u);
                double n = rnd.NextDouble() - 0.5;
                byte R = Byte((1 - Math.Exp(-r)) * edge, n), G = Byte((1 - Math.Exp(-g)) * edge, n), B = Byte((1 - Math.Exp(-b)) * edge, n);
                int o = (j * w + i) * 4;
                px[o] = B; px[o + 1] = G; px[o + 2] = R; px[o + 3] = Math.Max(R, Math.Max(G, B));
            }
        }
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, px, w * 4);
        bmp.Freeze();
        return bmp;
    }

    static byte Byte(double v, double noise) => (byte)Math.Clamp(Math.Round(v * 255 + noise), 0, 255);
}
