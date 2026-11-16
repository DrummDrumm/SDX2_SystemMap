using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace SDX2_SystemMap.Tests;

/// <summary>Draws the map with GDI+ for previews. Fonts are close to, not the same as, the game's.</summary>
internal sealed class GdiMapCanvas : IMapCanvas
{
    private readonly Graphics g;
    private readonly Stack<GraphicsState> clips = new Stack<GraphicsState>();
    private readonly Dictionary<int, Font> fonts = new Dictionary<int, Font>();
    private static readonly StringFormat Fmt0 = (StringFormat)StringFormat.GenericTypographic.Clone();

    public GdiMapCanvas(Graphics g)
    {
        this.g = g;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        Fmt0.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
    }

    private static Color C(MapColor c) => Color.FromArgb(c.A, c.R, c.G, c.B);

    private Font F(float px)
    {
        int k = (int)Math.Round(px * 4);
        if (!fonts.TryGetValue(k, out var f)) fonts[k] = f = new Font("Segoe UI", Math.Max(1, px * 0.78f), GraphicsUnit.Pixel);
        return f;
    }

    public void Rect(float x, float y, float w, float h, MapColor col, float rotation = 0)
    {
        using var b = new SolidBrush(C(col));
        if (rotation == 0) { g.FillRectangle(b, x, y, w, h); return; }
        var st = g.Save();
        g.TranslateTransform(x + w / 2, y + h / 2);
        g.RotateTransform(rotation * 180f / (float)Math.PI);
        g.FillRectangle(b, -w / 2, -h / 2, w, h);
        g.Restore(st);
    }

    public void Line(float x1, float y1, float x2, float y2, float width, MapColor col)
    {
        using var p = new Pen(C(col), width);
        g.DrawLine(p, x1, y1, x2, y2);
    }

    public void Disc(float cx, float cy, float r, MapColor col)
    {
        if (r > 20000) return;
        using var b = new SolidBrush(C(col));
        g.FillEllipse(b, cx - r, cy - r, 2 * r, 2 * r);
    }

    public void Text(string text, float x, float y, float px, MapColor col, TextAnchor anchor = TextAnchor.Left)
    {
        if (anchor != TextAnchor.Left) { float w = TextWidth(text, px); x -= anchor == TextAnchor.Center ? w / 2 : w; }
        using var b = new SolidBrush(C(col));
        g.DrawString(text, F(px), b, x, y + px * 0.08f, Fmt0);
    }

    public float TextWidth(string text, float px) => g.MeasureString(text, F(px), 10000, Fmt0).Width;

    public void PushClip(float x, float y, float w, float h)
    {
        clips.Push(g.Save());
        g.SetClip(new RectangleF(x, y, w, h), CombineMode.Intersect);
    }

    public void PopClip() { if (clips.Count > 0) g.Restore(clips.Pop()); }
}
