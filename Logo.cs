using System.IO;
using Drawing = System.Drawing;
using D2 = System.Drawing.Drawing2D;

namespace EyeBreak;

/// <summary>
/// The Eye Break logo: a vertical golden third eye with a white star at its centre, on a dark rounded tile.
/// Drawn at 4× and scaled down so it stays crisp at tray size.
/// </summary>
public static class Logo
{
    public static Drawing.Bitmap Render(int size)
    {
        int s = size * 4;
        using var big = new Drawing.Bitmap(s, s);
        using (var g = Drawing.Graphics.FromImage(big))
        {
            g.SmoothingMode = D2.SmoothingMode.AntiAlias;
            g.Clear(Drawing.Color.Transparent);
            float u = s / 32f;

            // dark rounded tile, so the mark reads on light and dark taskbars
            using (var tile = RoundedRect(new Drawing.RectangleF(0.5f * u, 0.5f * u, 31 * u, 31 * u), 8 * u))
            using (var tileBrush = new D2.LinearGradientBrush(new Drawing.PointF(0, 0), new Drawing.PointF(0, s),
                       Drawing.Color.FromArgb(255, 0x24, 0x1B, 0x10), Drawing.Color.FromArgb(255, 0x0B, 0x0A, 0x08)))
                g.FillPath(tileBrush, tile);

            // the vertical eye
            using var eye = new D2.GraphicsPath();
            eye.AddBezier(16 * u, 3 * u, 25 * u, 10 * u, 25 * u, 22 * u, 16 * u, 29 * u);
            eye.AddBezier(16 * u, 29 * u, 7 * u, 22 * u, 7 * u, 10 * u, 16 * u, 3 * u);
            eye.CloseFigure();
            using (var glow = new Drawing.Pen(Drawing.Color.FromArgb(90, 0xF0, 0xB3, 0x5C), 4.5f * u))
                g.DrawPath(glow, eye);
            using (var inner = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 0x0E, 0x0B, 0x07)))
                g.FillPath(inner, eye);
            using (var gold = new D2.LinearGradientBrush(new Drawing.PointF(0, 3 * u), new Drawing.PointF(0, 29 * u),
                       Drawing.Color.FromArgb(255, 0xFF, 0xE3, 0xAE), Drawing.Color.FromArgb(255, 0xE3, 0x92, 0x35)))
            using (var rim = new Drawing.Pen(gold, 2.2f * u))
                g.DrawPath(rim, eye);

            // the white star: soft halo, rays, bright core
            using (var halo = new D2.GraphicsPath())
            {
                halo.AddEllipse(16 * u - 6 * u, 16 * u - 6 * u, 12 * u, 12 * u);
                using var hb = new D2.PathGradientBrush(halo)
                {
                    CenterColor = Drawing.Color.FromArgb(150, 255, 244, 225),
                    SurroundColors = new[] { Drawing.Color.FromArgb(0, 255, 244, 225) },
                };
                g.FillPath(hb, halo);
            }
            using (var ray = new Drawing.Pen(Drawing.Color.FromArgb(200, 255, 255, 255), 0.9f * u))
            {
                g.DrawLine(ray, 16 * u, 10 * u, 16 * u, 22 * u);
                g.DrawLine(ray, 11.5f * u, 16 * u, 20.5f * u, 16 * u);
            }
            using (var core = new Drawing.SolidBrush(Drawing.Color.White))
                g.FillEllipse(core, 16 * u - 2 * u, 16 * u - 2 * u, 4 * u, 4 * u);
        }

        var bmp = new Drawing.Bitmap(size, size);
        using (var g = Drawing.Graphics.FromImage(bmp))
        {
            g.InterpolationMode = D2.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = D2.PixelOffsetMode.HighQuality;
            g.DrawImage(big, 0, 0, size, size);
        }
        return bmp;
    }

    /// <summary>Writes a multi-size .ico (16–256 px, PNG-compressed) for the app file.</summary>
    public static void WriteIco(string path)
    {
        int[] sizes = { 16, 24, 32, 48, 64, 256 };
        var pngs = sizes.Select(sz =>
        {
            using var b = Render(sz);
            using var ms = new MemoryStream();
            b.Save(ms, Drawing.Imaging.ImageFormat.Png);
            return ms.ToArray();
        }).ToArray();

        using var f = new BinaryWriter(File.Create(path));
        f.Write((short)0); f.Write((short)1); f.Write((short)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            f.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); f.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            f.Write((byte)0); f.Write((byte)0); f.Write((short)1); f.Write((short)32);
            f.Write(pngs[i].Length); f.Write(offset);
            offset += pngs[i].Length;
        }
        foreach (var p in pngs) f.Write(p);
    }

    static D2.GraphicsPath RoundedRect(Drawing.RectangleF r, float radius)
    {
        var p = new D2.GraphicsPath();
        float d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
