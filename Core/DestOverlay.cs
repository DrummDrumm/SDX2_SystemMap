using System;

namespace SDX2_SystemMap;

/// <summary>The marks drawn over the destination view's picture.</summary>
public enum FeedOverlay
{
    None,
    /// <summary>Tachi: nested lock brackets, tick rulers above and below, a caret over the middle.</summary>
    TachiLock,
    /// <summary>Tachi: curved side arcs with data blocks, a ruler and lock brackets.</summary>
    TachiArcs,
    /// <summary>UNN: a ticked lock ring with gold trim bars on a plotting grid.</summary>
    UnnPlot,
    /// <summary>Belter: a teal mark box with a tag.</summary>
    BelterTag,
    /// <summary>Belter: a rough crosshair, a ring on the target, heavy corner brackets.</summary>
    BelterScrap,
    /// <summary>Protogen: a hexagon lock, a dashed outer hexagon, scan lines and a sweep.</summary>
    ProtoHex,
    /// <summary>Protogen: a turning, segmented lock ring on a dot grid.</summary>
    ProtoRing,
    /// <summary>OPA: a contact box with a callout on a faint grid.</summary>
    OpaContact,
    /// <summary>Classic: corner brackets on the target.</summary>
    ClassicCorners,
    /// <summary>Classic: an edge ruler and a ring on a faint grid.</summary>
    ClassicRulers,
}

/// <summary>
/// The destination view's overlays, game-free (the tests render them): each theme's style of marks over the picture, in
/// the theme's overlay colours, with optional screen noise (grids, scan lines, specks). Also a stand-in picture for the
/// settings preview.
/// </summary>
public static class DestOverlay
{
    /// <summary>Every overlay with its name, in the order the settings list them.</summary>
    public static readonly (FeedOverlay Style, string Name)[] Styles =
    {
        (FeedOverlay.TachiLock, "Tachi nested lock"), (FeedOverlay.TachiArcs, "Tachi side arcs"),
        (FeedOverlay.UnnPlot, "UNN plot ring"),
        (FeedOverlay.BelterTag, "Belter mark box"), (FeedOverlay.BelterScrap, "Belter scrap reticle"),
        (FeedOverlay.ProtoHex, "Protogen hex lock"), (FeedOverlay.ProtoRing, "Protogen lock ring"),
        (FeedOverlay.OpaContact, "OPA contact box"),
        (FeedOverlay.ClassicCorners, "Classic corners"), (FeedOverlay.ClassicRulers, "Classic rulers"),
        (FeedOverlay.None, "None"),
    };

    /// <summary>A theme's own overlay (the first of its styles).</summary>
    public static FeedOverlay Default(ThemeStyle s) => s switch
    {
        ThemeStyle.Tachi => FeedOverlay.TachiLock,
        ThemeStyle.Naval => FeedOverlay.UnnPlot,
        ThemeStyle.Belter => FeedOverlay.BelterTag,
        ThemeStyle.Protogen => FeedOverlay.ProtoHex,
        ThemeStyle.Contact => FeedOverlay.OpaContact,
        _ => FeedOverlay.ClassicCorners,
    };

    public static bool TryParse(string text, out FeedOverlay o) => Enum.TryParse(text?.Trim(), true, out o) && Enum.IsDefined(typeof(FeedOverlay), o);

    public static string NameOf(FeedOverlay o) { foreach (var s in Styles) if (s.Style == o) return s.Name; return o.ToString(); }

    /// <summary>
    /// Draws <paramref name="style"/> over the picture <paramref name="p"/>. <paramref name="contact"/>: a grid was found to
    /// frame (the marks are dimmer without one). <paramref name="time"/> (s) moves the sweeps and the noise.
    /// </summary>
    public static void Draw(IMapCanvas c, Box p, FeedOverlay style, MapTheme th, float u, string name, double dist, bool contact, bool noise, double time)
    {
        if (style == FeedOverlay.None || p.W < 20 || p.H < 20) return;
        float k = contact ? 1f : 0.55f;
        var line = th.OverlayLine.WithAlpha(k); var trim = th.OverlayTrim.WithAlpha(k); var text = th.OverlayText; var dim = th.OverlayDim;
        float cx = p.X + p.W / 2, cy = p.Y + p.H / 2, s = Math.Min(p.H * 0.34f, p.W * 0.3f);
        var lk = new Box(cx - s, cy - s, 2 * s, 2 * s);
        float w = Math.Max(1f, 1.2f * u), w2 = Math.Max(1.5f, 2f * u), px = Math.Max(11f, 10f * u);
        string d = Fmt.Dist(dist).ToUpperInvariant();
        name = string.IsNullOrEmpty(name) ? "DESTINATION" : name.ToUpperInvariant();
        if (name.Length > 18) name = name.Substring(0, 17) + "~";
        c.PushClip(p.X, p.Y, p.W, p.H);
        try
        {
            switch (style)
            {
                case FeedOverlay.TachiLock:
                {
                    if (noise) { ScanLines(c, p, 3 * u, line.WithAlpha(0.07f)); }
                    float y0 = p.Y + 9 * u, y1 = p.Bottom - 9 * u, x0 = p.X + p.W * 0.2f, x1 = p.Right - p.W * 0.2f;
                    Ruler(c, x0, x1, y0, 40, 1, u, line.WithAlpha(0.85f));
                    Ruler(c, x0, x1, y1, 40, -1, u, line.WithAlpha(0.85f));
                    c.Line(cx - 4 * u, p.Y + 2 * u, cx, p.Y + 6 * u, w, line); c.Line(cx, p.Y + 6 * u, cx + 4 * u, p.Y + 2 * u, w, line);
                    PanelStyle.Brackets(c, lk.Inflate(6 * u), s * 0.3f, w2, line);
                    var inr = lk.Inflate(-3 * u);
                    c.Line(inr.X, inr.Y, inr.Right, inr.Y, w, line.WithAlpha(0.7f)); c.Line(inr.Right, inr.Y, inr.Right, inr.Bottom, w, line.WithAlpha(0.7f));
                    c.Line(inr.X, inr.Bottom, inr.X + s * 0.4f, inr.Bottom, w, line.WithAlpha(0.7f)); c.Line(inr.X, inr.Bottom, inr.X, inr.Bottom - s * 0.4f, w, line.WithAlpha(0.7f));
                    c.Text("[DEST]", p.X + 5 * u, p.Y + 4 * u, px * 0.85f, line);
                    c.Text("[" + d + "]", p.X + 5 * u, p.Y + 4 * u + px, px * 0.85f, line);
                    break;
                }
                case FeedOverlay.TachiArcs:
                {
                    if (noise) { ScanLines(c, p, 3 * u, line.WithAlpha(0.07f)); Specks(c, p, 24, time, u, line.WithAlpha(0.35f)); }
                    float ax = p.X + p.W * 0.1f, bx = p.Right - p.W * 0.1f, ya = p.Y + p.H * 0.14f, yb = p.Bottom - p.H * 0.14f, bow = p.W * 0.05f;
                    Curve(c, ax, ya, ax - bow, cy, ax, yb, w2, line); Curve(c, ax + 5 * u, ya + 6 * u, ax + 5 * u - bow * 0.7f, cy, ax + 5 * u, yb - 6 * u, w, line.WithAlpha(0.6f));
                    Curve(c, bx, ya, bx + bow, cy, bx, yb, w2, line); Curve(c, bx - 5 * u, ya + 6 * u, bx - 5 * u + bow * 0.7f, cy, bx - 5 * u, yb - 6 * u, w, line.WithAlpha(0.6f));
                    Ruler(c, p.X + p.W * 0.22f, p.Right - p.W * 0.22f, p.Y + 8 * u, 30, 1, u, line.WithAlpha(0.85f));
                    PanelStyle.Brackets(c, lk.Inflate(2 * u), s * 0.28f, w2, line);
                    float dx = ax + 12 * u, ex = bx - 12 * u, ty = cy - px * 1.6f;
                    c.Text("RNG", dx, ty, px * 0.8f, dim); c.Text(d, dx, ty + px, px * 0.85f, text);
                    c.Text("TGT", ex, ty, px * 0.8f, dim, TextAnchor.Right); c.Text(contact ? "LOCK" : "----", ex, ty + px, px * 0.85f, text, TextAnchor.Right);
                    for (int i = 0; i < 4; i++)
                    {
                        float bw = (8 + (i * 7) % 12) * u;
                        c.Rect(dx, ty + px * 2.3f + i * 4 * u, bw, Math.Max(1, 1.6f * u), line.WithAlpha(0.7f));
                        c.Rect(ex - bw, ty + px * 2.3f + i * 4 * u, bw, Math.Max(1, 1.6f * u), line.WithAlpha(0.7f));
                    }
                    c.Text(name, cx, p.Bottom - px * 1.5f, px * 0.9f, text, TextAnchor.Center);
                    break;
                }
                case FeedOverlay.UnnPlot:
                {
                    if (noise) { Grid(c, p, p.H / 8, line.WithAlpha(0.08f)); }
                    c.Line(p.X, cy, p.Right, cy, Math.Max(1, 0.6f * u), dim.WithAlpha(0.3f)); c.Line(cx, p.Y, cx, p.Bottom, Math.Max(1, 0.6f * u), dim.WithAlpha(0.3f));
                    Ring(c, cx, cy, s * 0.62f, w, line);
                    for (int i = 0; i < 4; i++)
                    {
                        double a = i * Math.PI / 2; float ca = (float)Math.Cos(a), sa = (float)Math.Sin(a);
                        c.Line(cx + ca * s * 0.8f, cy + sa * s * 0.8f, cx + ca * s * 1.05f, cy + sa * s * 1.05f, w2, line);
                    }
                    for (int i = -3; i <= 3; i++) if (i != 0) c.Disc(cx + i * s * 0.2f, cy, Math.Max(1.2f, 1.3f * u), line.WithAlpha(0.8f));
                    float bw = Math.Max(2, 2.5f * u), bh = Math.Min(p.H * 0.18f, 50 * u);
                    c.Rect(p.X, p.Y + p.H * 0.3f, bw, bh, trim); c.Rect(p.Right - bw, p.Y + p.H * 0.7f - bh, bw, bh, trim);
                    c.Rect(p.X + 14 * u, p.Y + 3 * u, Math.Min(p.W * 0.25f, 60 * u), Math.Max(2, 2f * u), trim);
                    c.Text("RNG " + d, p.X + 8 * u, p.Bottom - px * 1.5f, px * 0.85f, text);
                    c.Text(contact ? "TRACK" : "NO TRACK", p.Right - 8 * u, p.Bottom - px * 1.5f, px * 0.85f, trim, TextAnchor.Right);
                    break;
                }
                case FeedOverlay.BelterTag:
                {
                    if (noise) Specks(c, p, 30, time, u, text.WithAlpha(0.3f));
                    var bx = lk.Inflate(-s * 0.05f);
                    Outline(c, bx, w2, trim);
                    PanelStyle.Brackets(c, bx.Inflate(4 * u), 8 * u, w2, line);
                    string tag = "MARK 01";
                    float tw = c.TextWidth(tag, px * 0.85f) + 8 * u, th2 = px + 2 * u;
                    c.Rect(bx.X, bx.Y - th2 - 2 * u, tw, th2, trim);
                    c.Text(tag, bx.X + 4 * u, bx.Y - th2 - 1 * u, px * 0.85f, th.PanelBg.WithAlpha(1f));
                    c.Text(d, bx.X + tw + 5 * u, bx.Y - th2 - 1 * u, px * 0.85f, line);
                    break;
                }
                case FeedOverlay.BelterScrap:
                {
                    if (noise)
                    {
                        Specks(c, p, 40, time, u, text.WithAlpha(0.35f));
                        int f = (int)(time * 8);
                        if (Hash(f, 99) % 9 == 0) c.Rect(p.X, p.Y + (Hash(f, 7) % 1000) / 1000f * p.H, p.W, Math.Max(2, 3 * u), text.WithAlpha(0.08f));
                    }
                    float g = s * 0.3f, a = s * 1.3f;
                    c.Line(cx - a, cy, cx - g, cy, w2, line); c.Line(cx + g, cy, cx + a, cy, w2, line);
                    c.Line(cx, cy - a * 0.85f, cx, cy - g, w2, line); c.Line(cx, cy + g, cx, cy + a * 0.85f, w2, line);
                    Ring(c, cx, cy, g * 0.8f, w, trim);
                    c.Text(d, cx + g + 3 * u, cy - g - px, px * 0.9f, text);
                    PanelStyle.Brackets(c, p.Inflate(-4 * u), 14 * u, Math.Max(2, 2.5f * u), line);
                    c.Text(dist < 2000 ? "OYE, DOCK HER SLOW" : "OYE, SHE CLOSE NOW", p.X + 10 * u, p.Bottom - px * 1.6f, px * 0.85f, dim);
                    break;
                }
                case FeedOverlay.ProtoHex:
                {
                    if (noise)
                    {
                        ScanLines(c, p, 3 * u, line.WithAlpha(0.06f));
                        float sy = p.Y + (float)(time * 0.25 % 1.0) * p.H;
                        c.Rect(p.X, sy - 8 * u, p.W, 8 * u, line.WithAlpha(0.05f)); c.Rect(p.X, sy, p.W, Math.Max(1, 1.5f * u), line.WithAlpha(0.35f));
                    }
                    Hex(c, cx, cy, s * 0.95f, w, line, false);
                    Hex(c, cx, cy, s * 1.1f, w, trim.WithAlpha(0.65f), true);
                    PanelStyle.Brackets(c, p.Inflate(-3 * u), 14 * u, w2, line);
                    c.Text(contact ? "LOCK" : "SEEK", p.X + 8 * u, p.Bottom - px * 1.6f, px * 0.85f, trim);
                    c.Text(d, p.Right - 8 * u, p.Bottom - px * 1.6f, px * 0.85f, text, TextAnchor.Right);
                    break;
                }
                case FeedOverlay.ProtoRing:
                {
                    if (noise) Dots(c, p, p.H / 7, Math.Max(1f, 0.9f * u), line.WithAlpha(0.18f));
                    float r = s * 0.75f, turn = (float)(time * 0.3 % (Math.PI * 2));
                    for (int i = 0; i < 4; i++) { float a0 = turn + i * (float)Math.PI / 2 + 0.25f; Arc(c, cx, cy, r, a0, a0 + 1.07f, Math.Max(2, 2.5f * u), i == 0 ? trim : line); }
                    Ring(c, cx, cy, 4 * u, w, line);
                    PanelStyle.Brackets(c, p.Inflate(-3 * u), 14 * u, w2, line);
                    c.Text(d, p.Right - 8 * u, p.Bottom - px * 1.6f, px * 0.85f, text, TextAnchor.Right);
                    break;
                }
                case FeedOverlay.OpaContact:
                {
                    if (noise) Grid(c, p, p.H / 8, line.WithAlpha(0.08f));
                    var bx = lk.Inflate(-s * 0.08f);
                    Outline(c, bx, Math.Max(1, 1.3f * u), trim);
                    float lx = Math.Min(p.Right - 8 * u, bx.Right + p.W * 0.08f), ly = Math.Max(p.Y + px + 6 * u, bx.Y - p.H * 0.1f);
                    c.Line(bx.Right, bx.Y, lx, ly, w, trim);
                    string label = "C-01 " + name;
                    float lw = c.TextWidth(label, px * 0.85f);
                    float tx = Math.Min(lx + 2 * u, p.Right - lw - 4 * u);
                    c.Text(label, tx, ly - px - 1 * u, px * 0.85f, trim);
                    c.Text(d, tx, ly + 1 * u, px * 0.8f, text);
                    break;
                }
                case FeedOverlay.ClassicCorners:
                    PanelStyle.Brackets(c, lk, s * 0.25f, w, line);
                    break;
                case FeedOverlay.ClassicRulers:
                {
                    if (noise) Grid(c, p, p.H / 8, line.WithAlpha(0.07f));
                    Ruler(c, p.X + 10 * u, p.Right - 10 * u, p.Bottom - 5 * u, 28, 1, u, line.WithAlpha(0.85f));
                    Ring(c, cx, cy, s * 0.55f, w, line.WithAlpha(0.6f));
                    break;
                }
            }
        }
        finally { c.PopClip(); }
    }

    /// <summary>A stand-in picture for the settings preview: space, a few stars and a rock with a little station.</summary>
    public static void Feed(IMapCanvas c, Box p)
    {
        c.Rect(p.X, p.Y, p.W, p.H, MapColor.Rgb(0x0B1626));
        for (int i = 0; i < 18; i++)
            c.Disc(p.X + (Hash(i, 3) % 1000) / 1000f * p.W, p.Y + (Hash(i, 5) % 1000) / 1000f * p.H, 0.8f + (i % 3) * 0.3f, MapColor.Rgb(0xFFFFFF, 140 + (i * 37) % 100));
        float cx = p.X + p.W / 2, cy = p.Y + p.H / 2, s = Math.Min(p.H * 0.34f, p.W * 0.3f);
        var rock = MapColor.Rgb(0x3A4250); var lit = MapColor.Rgb(0x59606C);
        c.Disc(cx, cy, s * 0.55f, rock); c.Disc(cx + s * 0.25f, cy - s * 0.2f, s * 0.42f, rock); c.Disc(cx - s * 0.3f, cy + s * 0.15f, s * 0.38f, rock);
        c.Disc(cx + s * 0.1f, cy - s * 0.12f, s * 0.24f, lit); c.Disc(cx + s * 0.3f, cy - s * 0.3f, s * 0.16f, lit);
        c.Rect(cx - s * 0.06f, cy - s * 0.02f, s * 0.14f, s * 0.08f, MapColor.Rgb(0xC8D0DC));
        c.Rect(cx - s * 0.02f, cy - s * 0.14f, s * 0.03f, s * 0.12f, MapColor.Rgb(0x9AA4B0));
    }

    // ---- drawing helpers

    private static void Ruler(IMapCanvas c, float x0, float x1, float y, int n, int dir, float u, MapColor col)
    {
        float w = Math.Max(1, u);
        c.Line(x0, y, x1, y, w, col);
        for (int i = 0; i <= n; i++)
        {
            float x = x0 + (x1 - x0) * i / n, h = (i % 5 == 0 ? 5 : 2.5f) * u;
            c.Line(x, y, x, y + h * dir, w, col);
        }
    }

    private static void Outline(IMapCanvas c, Box b, float w, MapColor col)
    {
        c.Rect(b.X, b.Y, b.W, w, col); c.Rect(b.X, b.Bottom - w, b.W, w, col);
        c.Rect(b.X, b.Y, w, b.H, col); c.Rect(b.Right - w, b.Y, w, b.H, col);
    }

    private static void Arc(IMapCanvas c, float cx, float cy, float r, float a0, float a1, float w, MapColor col)
    {
        int n = Math.Max(4, (int)(Math.Abs(a1 - a0) * r / 4));
        float px = cx + r * (float)Math.Cos(a0), py = cy + r * (float)Math.Sin(a0);
        for (int i = 1; i <= n; i++)
        {
            float a = a0 + (a1 - a0) * i / n, x = cx + r * (float)Math.Cos(a), y = cy + r * (float)Math.Sin(a);
            c.Line(px, py, x, y, w, col); px = x; py = y;
        }
    }

    private static void Ring(IMapCanvas c, float cx, float cy, float r, float w, MapColor col) => Arc(c, cx, cy, r, 0, (float)(Math.PI * 2), w, col);

    /// <summary>A quadratic curve from (x0,y0) through the pull of (x1,y1) to (x2,y2).</summary>
    private static void Curve(IMapCanvas c, float x0, float y0, float x1, float y1, float x2, float y2, float w, MapColor col)
    {
        float px = x0, py = y0;
        for (int i = 1; i <= 16; i++)
        {
            float t = i / 16f, m = 1 - t;
            float x = m * m * x0 + 2 * m * t * x1 + t * t * x2, y = m * m * y0 + 2 * m * t * y1 + t * t * y2;
            c.Line(px, py, x, y, w, col); px = x; py = y;
        }
    }

    private static void Hex(IMapCanvas c, float cx, float cy, float r, float w, MapColor col, bool dashed)
    {
        for (int i = 0; i < 6; i++)
        {
            double a0 = -Math.PI / 2 + i * Math.PI / 3, a1 = a0 + Math.PI / 3;
            float x0 = cx + r * (float)Math.Cos(a0), y0 = cy + r * (float)Math.Sin(a0), x1 = cx + r * (float)Math.Cos(a1), y1 = cy + r * (float)Math.Sin(a1);
            if (!dashed) { c.Line(x0, y0, x1, y1, w, col); continue; }
            for (int k = 0; k < 4; k++)
            {
                float t0 = k / 4f, t1 = t0 + 0.14f;
                c.Line(x0 + (x1 - x0) * t0, y0 + (y1 - y0) * t0, x0 + (x1 - x0) * t1, y0 + (y1 - y0) * t1, w, col);
            }
        }
    }

    // ---- screen noise

    private static void ScanLines(IMapCanvas c, Box p, float step, MapColor col)
    {
        step = Math.Max(2, step);
        for (float y = p.Y + step / 2; y < p.Bottom; y += step) c.Rect(p.X, y, p.W, 1, col);
    }

    private static void Grid(IMapCanvas c, Box p, float step, MapColor col)
    {
        step = Math.Max(8, step);
        float cx = p.X + p.W / 2, cy = p.Y + p.H / 2;
        for (float x = cx - (float)Math.Floor((cx - p.X) / step) * step; x <= p.Right; x += step) c.Rect(x, p.Y, 1, p.H, col);
        for (float y = cy - (float)Math.Floor((cy - p.Y) / step) * step; y <= p.Bottom; y += step) c.Rect(p.X, y, p.W, 1, col);
    }

    private static void Dots(IMapCanvas c, Box p, float step, float r, MapColor col)
    {
        step = Math.Max(8, step);
        float cx = p.X + p.W / 2, cy = p.Y + p.H / 2;
        for (float x = cx - (float)Math.Floor((cx - p.X) / step) * step; x <= p.Right; x += step)
            for (float y = cy - (float)Math.Floor((cy - p.Y) / step) * step; y <= p.Bottom; y += step) c.Disc(x, y, r, col);
    }

    /// <summary>Little flecks of static, moved about ten times a second.</summary>
    private static void Specks(IMapCanvas c, Box p, int n, double time, float u, MapColor col)
    {
        int f = (int)(time * 10);
        for (int i = 0; i < n; i++)
        {
            float x = p.X + (Hash(f, i * 2) % 1000) / 1000f * p.W, y = p.Y + (Hash(f, i * 2 + 1) % 1000) / 1000f * p.H;
            float len = (1 + Hash(f, i + 500) % 4) * u;
            c.Rect(x, y, len, Math.Max(1, 0.8f * u), col.WithAlpha(0.4f + (Hash(f, i + 900) % 60) / 100f));
        }
    }

    private static int Hash(int a, int b)
    {
        unchecked
        {
            uint h = (uint)a * 374761393u + (uint)b * 668265263u;
            h = (h ^ (h >> 13)) * 1274126177u;
            return (int)((h ^ (h >> 16)) & 0x7FFFFFFF);
        }
    }
}
