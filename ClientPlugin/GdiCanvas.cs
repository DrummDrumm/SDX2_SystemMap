using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace SDX2_SystemMap;

/// <summary>
/// Draws the map with GDI+ on the second screen's window (and measures text for it on the game thread: one instance per
/// thread). Lines are clipped to the window first: GDI+ chokes on the far-off ends a zoomed-in map produces.
/// </summary>
internal sealed class GdiCanvas : IMapCanvas, IDisposable
{
    private Graphics g;
    private readonly Stack<GraphicsState> clips = new Stack<GraphicsState>();
    private readonly Dictionary<int, Font> fonts = new Dictionary<int, Font>();
    private readonly SolidBrush brush = new SolidBrush(Color.White);
    private readonly Pen pen = new Pen(Color.White, 1);
    private readonly StringFormat format;
    private float w = 4000, h = 4000;
    private Bitmap measureBitmap;

    public GdiCanvas()
    {
        format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
    }

    /// <summary>A canvas that only measures text (the game thread's, for <see cref="DrawList.Measure"/>).</summary>
    public static GdiCanvas Measurer()
    {
        var c = new GdiCanvas { measureBitmap = new Bitmap(1, 1) };
        c.Bind(Graphics.FromImage(c.measureBitmap), 1, 1);
        return c;
    }

    public void Bind(Graphics graphics, float width, float height)
    {
        g = graphics; w = width; h = height; clips.Clear();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.PixelOffsetMode = PixelOffsetMode.HighSpeed;
    }

    private static Color C(MapColor c) => Color.FromArgb(c.A, c.R, c.G, c.B);

    private Font F(float px)
    {
        int k = (int)Math.Round(px * 4);
        if (!fonts.TryGetValue(k, out var f)) fonts[k] = f = new Font("Segoe UI", Math.Max(1, k / 4f * 0.78f), GraphicsUnit.Pixel);
        return f;
    }

    private bool Off(float x, float y, float rw, float rh) => x > w + 50 || y > h + 50 || x + rw < -50 || y + rh < -50;

    public void Rect(float x, float y, float rw, float rh, MapColor col, float rotation = 0)
    {
        if (col.A == 0 || rw <= 0 || rh <= 0 || float.IsNaN(x + y + rw + rh)) return;
        if (rotation == 0)
        {
            if (Off(x, y, rw, rh)) return;
            // keep a huge rectangle inside reach
            float x0 = Math.Max(x, -100), y0 = Math.Max(y, -100), x1 = Math.Min(x + rw, w + 100), y1 = Math.Min(y + rh, h + 100);
            brush.Color = C(col);
            g.FillRectangle(brush, x0, y0, x1 - x0, y1 - y0);
            return;
        }
        // a rotated rectangle is a thick line through its middle
        float cx = x + rw / 2, cy = y + rh / 2, dx = (float)Math.Cos(rotation) * rw / 2, dy = (float)Math.Sin(rotation) * rw / 2;
        Line(cx - dx, cy - dy, cx + dx, cy + dy, rh, col, LineCap.Flat);
    }

    public void Line(float x1, float y1, float x2, float y2, float width, MapColor col) => Line(x1, y1, x2, y2, width, col, LineCap.Flat);

    private void Line(float x1, float y1, float x2, float y2, float width, MapColor col, LineCap cap)
    {
        if (col.A == 0 || width <= 0 || float.IsNaN(x1 + y1 + x2 + y2)) return;
        if (!Clip(ref x1, ref y1, ref x2, ref y2, -50, -50, w + 50, h + 50)) return;
        pen.Color = C(col); pen.Width = width; pen.StartCap = pen.EndCap = cap;
        g.DrawLine(pen, x1, y1, x2, y2);
    }

    /// <summary>Liang-Barsky: cuts the segment to the box; false when none of it is inside.</summary>
    private static bool Clip(ref float x1, ref float y1, ref float x2, ref float y2, float xmin, float ymin, float xmax, float ymax)
    {
        double dx = (double)x2 - x1, dy = (double)y2 - y1, t0 = 0, t1 = 1;
        double[] p = { -dx, dx, -dy, dy }, q = { x1 - xmin, xmax - x1, y1 - ymin, ymax - y1 };
        for (int i = 0; i < 4; i++)
        {
            if (p[i] == 0) { if (q[i] < 0) return false; continue; }
            double r = q[i] / p[i];
            if (p[i] < 0) { if (r > t1) return false; if (r > t0) t0 = r; }
            else { if (r < t0) return false; if (r < t1) t1 = r; }
        }
        double ox = x1, oy = y1;
        x1 = (float)(ox + t0 * dx); y1 = (float)(oy + t0 * dy);
        x2 = (float)(ox + t1 * dx); y2 = (float)(oy + t1 * dy);
        return true;
    }

    public void Disc(float cx, float cy, float r, MapColor col)
    {
        if (col.A == 0 || r <= 0 || r > 20000 || float.IsNaN(cx + cy + r) || Off(cx - r, cy - r, 2 * r, 2 * r)) return;
        brush.Color = C(col);
        g.FillEllipse(brush, cx - r, cy - r, 2 * r, 2 * r);
    }

    public void Text(string text, float x, float y, float px, MapColor col, TextAnchor anchor = TextAnchor.Left)
    {
        if (string.IsNullOrEmpty(text) || col.A == 0 || px < 1 || float.IsNaN(x + y)) return;
        if (anchor != TextAnchor.Left) { float tw = TextWidth(text, px); x -= anchor == TextAnchor.Center ? tw / 2 : tw; }
        if (Off(x, y, px * text.Length, px)) return;
        var f = F(px);
        // a soft shadow, as the game's text has
        brush.Color = Color.FromArgb((int)(col.A * 0.6f), 0, 0, 0);
        g.DrawString(text, f, brush, x + 1, y + px * 0.08f + 1, format);
        brush.Color = C(col);
        g.DrawString(text, f, brush, x, y + px * 0.08f, format);
    }

    public float TextWidth(string text, float px) => string.IsNullOrEmpty(text) || px < 1 ? 0 : g.MeasureString(text, F(px), 100000, format).Width;

    public void PushClip(float x, float y, float cw, float ch)
    {
        clips.Push(g.Save());
        g.SetClip(new RectangleF(x, y, Math.Max(0, cw), Math.Max(0, ch)), CombineMode.Intersect);
    }

    public void PopClip() { if (clips.Count > 0) g.Restore(clips.Pop()); }

    public void Dispose()
    {
        foreach (var f in fonts.Values) f.Dispose();
        fonts.Clear();
        brush.Dispose(); pen.Dispose(); format.Dispose();
        if (measureBitmap != null) { g?.Dispose(); measureBitmap.Dispose(); }
    }
}
