using System;

namespace SDX2_SystemMap;

/// <summary>
/// Theme-styled parts for the info panels round the map (the route panel, the side window's strip, the autopilot
/// panel): a frame and section header in each theme's look, a dial gauge and a burn / coast / brake timeline bar.
/// Game-free, so the tests render them. u = UI scale (1 at a 720 px tall map), px = the panel's text size.
/// </summary>
public static class PanelStyle
{
    private static MapTheme T => MapTheme.Current;

    /// <summary>The header's height when Frame draws a title: the content starts this far below the panel's top.</summary>
    public static float HeaderH(float px) => px * 2.4f;

    /// <summary>
    /// The panel's background and border in the theme's style, and a section header when title isn't null (right = a
    /// short tag at the header's right end). Returns where the content starts.
    /// </summary>
    public static float Frame(IMapCanvas c, Box b, float u, float px, string title, string right = null, MapColor? bg = null)
    {
        var acc = T.Accent;
        float lw = Math.Max(1f, u);
        c.Rect(b.X, b.Y, b.W, b.H, bg ?? T.PanelBg);
        switch (T.Style)
        {
            case ThemeStyle.Tachi:
                c.Rect(b.X, b.Y, b.W, lw, acc.WithAlpha(0.35f));
                Brackets(c, b, 8 * u, Math.Max(1f, 1.5f * u), acc.WithAlpha(0.85f));
                break;
            case ThemeStyle.Naval:
                // the chart's chamfered panel with a gold bar down the left and a gold corner mark
                Chamfer(c, b, 8 * u, 14 * u, lw, acc.WithAlpha(0.7f));
                c.Rect(b.X, b.Y + 8 * u, 3 * u, b.H - 16 * u, T.Trim.WithAlpha(0.9f));
                c.Rect(b.Right - 6 * u, b.Bottom - 6 * u, 4 * u, 4 * u, T.Trim);
                break;
            case ThemeStyle.Belter:
                Brackets(c, b, 7 * u, Math.Max(1f, 1.5f * u), T.Trim.WithAlpha(0.8f));
                HazardStripe(c, b.X, b.Bottom - 4 * u, b.W, 4 * u);
                break;
            case ThemeStyle.Protogen:
                c.Rect(b.X, b.Y, b.W, lw, acc.WithAlpha(0.9f));
                c.Rect(b.X + 2 * u, b.Y + 2 * u, Math.Min(b.W * 0.3f, 60 * u), 2 * u, T.Trim.WithAlpha(0.85f));
                Brackets(c, b, 8 * u, Math.Max(1f, 1.5f * u), acc.WithAlpha(0.8f));
                break;
            case ThemeStyle.Contact:
                Chamfer(c, b, 8 * u, 14 * u, lw, acc.WithAlpha(0.7f));
                c.Rect(b.X, b.Y + 8 * u, 3 * u, b.H - 16 * u, T.Trim.WithAlpha(0.9f));
                break;
            default:
                c.Rect(b.X, b.Y, Math.Max(2f, 2 * u), b.H, acc.WithAlpha(0.8f));
                break;
        }
        if (title == null) return b.Y + px * 0.5f;
        return Header(c, b, u, px, title, right);
    }

    /// <summary>A section header in the theme's style across the top of box b (its frame drawn already); returns where the content starts.</summary>
    public static float Header(IMapCanvas c, Box b, float u, float px, string title, string right = null)
    {
        var acc = T.Accent;
        float lw = Math.Max(1f, u);
        float x = b.X + px, hy = b.Y + px * 0.35f, hh = px * 1.6f, ty = hy + (hh - px) / 2, tx = x, under = hy + hh + px * 0.1f;
        var tcol = acc;
        switch (T.Style)
        {
            case ThemeStyle.Tachi:
                c.Rect(b.X + 3 * u, hy, b.W - 6 * u, hh, acc.WithAlpha(0.2f));
                for (float k = b.X + px; k < b.Right - px; k += 9 * u) c.Rect(k, under, lw, 3 * u, acc.WithAlpha(0.45f));
                tcol = T.Fine;
                break;
            case ThemeStyle.Naval:
                // a tinted header with a gold square, ruler ticks under it
                c.Rect(b.X + 6 * u, hy, b.W - 20 * u, hh, MapColor.Lerp(T.PanelBg, acc, 0.18f));
                c.Rect(x, hy + hh / 2 - 2.5f * u, 5 * u, 5 * u, T.Trim);
                tx = x + 10 * u; tcol = T.Fine;
                for (float k = b.X + px; k < b.Right - px; k += 10 * u) c.Rect(k, under, lw, ((int)((k - b.X - px) / (10 * u)) % 5 == 0 ? 5 : 2) * u, acc.WithAlpha(0.55f));
                break;
            case ThemeStyle.Belter:
                HazardStripe(c, x, hy + hh * 0.2f, 22 * u, hh * 0.6f);
                tx = x + 28 * u; tcol = T.Text;
                c.Rect(x, under, b.W - px * 2, lw, acc.WithAlpha(0.5f));
                break;
            case ThemeStyle.Protogen:
                c.Rect(b.X + 3 * u, hy, b.W - 6 * u, hh, acc.WithAlpha(0.12f));
                c.Text("+", b.Right - px * 0.8f, ty, px, acc, TextAnchor.Right);
                for (float k = b.X + px; k < b.Right - px; k += 10 * u) c.Rect(k, under, lw, ((int)((k - b.X - px) / (10 * u)) % 5 == 0 ? 5 : 2) * u, acc.WithAlpha(0.6f));
                tcol = T.Fine;
                break;
            case ThemeStyle.Contact:
                c.Rect(b.X + 6 * u, hy, b.W - 20 * u, hh, MapColor.Lerp(T.PanelBg, acc, 0.18f));
                c.Rect(x, under, b.W - px * 2, Math.Max(1f, 2 * u), acc.WithAlpha(0.6f));
                tcol = T.Text;
                break;
            default:
                c.Rect(x, under, b.W - px * 2, lw, acc.WithAlpha(0.4f));
                break;
        }
        c.Text(title, tx, ty, px, tcol);
        if (right != null) c.Text(right, b.Right - px * (T.Style == ThemeStyle.Protogen ? 2f : 1f), ty + px * 0.08f, px * 0.9f, T.Trim, TextAnchor.Right);
        return b.Y + HeaderH(px);
    }

    /// <summary>
    /// A 240-degree dial: a track, the value's arc (segmented in the HUD themes), ticks, a pointer, a mark (the plan's
    /// peak, NaN = none), the value in the middle and a label under it. frac and mark are 0..1 of the scale.
    /// </summary>
    public static void Dial(IMapCanvas c, float cx, float cy, float r, float u, float px, float frac, float mark, string value, string label, MapColor col)
    {
        frac = Math.Max(0, Math.Min(1, float.IsNaN(frac) ? 0 : frac));
        const float a0 = -120, a1 = 120;
        float tw = Math.Max(2f, r * 0.13f);
        bool segs = T.Style == ThemeStyle.Tachi || T.Style == ThemeStyle.Belter || T.Style == ThemeStyle.Contact;
        Arc(c, cx, cy, r, a0, a1, tw, T.Dim.WithAlpha(0.22f), 40);
        float af = a0 + (a1 - a0) * frac;
        if (segs)
        {
            // blocks of 8 degrees with 3-degree gaps
            for (float a = a0; a < af; a += 11) Arc(c, cx, cy, r, a, Math.Min(a + 8, af), tw, col, 3);
        }
        else if (frac > 0) Arc(c, cx, cy, r, a0, af, tw, col, Math.Max(2, (int)(40 * frac)));
        // ticks every 30 degrees outside the arc
        var tick = T.Style == ThemeStyle.Naval ? T.Trim : T.Dim;
        for (int k = 0; k <= 8; k++)
        {
            float a = a0 + k * 30, len = (k % 4 == 0 ? 5 : 3) * u;
            Ray(c, cx, cy, a, r + tw * 0.7f, r + tw * 0.7f + len, Math.Max(1f, u), tick.WithAlpha(k % 4 == 0 ? 0.9f : 0.55f));
        }
        if (T.Style == ThemeStyle.Protogen || T.Style == ThemeStyle.Naval) DashedArc(c, cx, cy, r * 1.25f, -150, 150, Math.Max(1f, 0.8f * u), T.Accent.WithAlpha(0.4f), 30);
        if (T.Style == ThemeStyle.Contact) Arc(c, cx, cy, r * 0.62f, 0, 360, Math.Max(1f, 0.8f * u), T.Orbit.WithAlpha(0.35f), 32);
        if (!float.IsNaN(mark))
        {
            float am = a0 + (a1 - a0) * Math.Max(0, Math.Min(1, mark));
            Ray(c, cx, cy, am, r - tw * 1.2f, r + tw * 1.2f, Math.Max(1.5f, 2 * u), T.Trim);
        }
        // the pointer: a bar from inside the value text's ring out to the arc
        Ray(c, cx, cy, af, r * 0.74f, r - tw * 0.6f, Math.Max(1.5f, 2 * u), T.Fine);
        float vp = Math.Min(px, r * 1.25f / Math.Max(1f, c.TextWidth(value, 1)));
        c.Text(value, cx, cy - vp * 0.55f, vp, T.Text, TextAnchor.Center);
        c.Text(label, cx, cy + r * 0.62f, px * 0.8f, T.Dim, TextAnchor.Center);
    }

    /// <summary>
    /// The burn / coast / brake timeline across the box: T+0 at the left, the flip mark and its time, the ETA at the
    /// right, then the bar split by the time spent in each (segmented in the HUD themes).
    /// </summary>
    public static void TimeBar(IMapCanvas c, Box b, float u, float px, double burn, double coast, double brake, string end = "ETA ")
    {
        double total = burn + coast + brake;
        if (!(total > 0)) return;
        float sp = px * 0.8f, bh = Math.Max(4f, px * 0.55f), by = b.Y + sp * 1.35f;
        float fb = (float)(burn / total) * b.W, fc = (float)(coast / total) * b.W, fx = b.X + fb + fc;
        // the labels: T+0, FLIP T+..., ETA ...
        string l = "T+0", r = end + Fmt.Time(total), f = "FLIP T+" + Fmt.Time(burn + coast);
        float lw = c.TextWidth(l, sp), rw = c.TextWidth(r, sp), fw = c.TextWidth(f, sp);
        c.Text(l, b.X, b.Y, sp, T.Dim);
        c.Text(r, b.Right, b.Y, sp, T.Brake, TextAnchor.Right);
        float flx = Math.Max(b.X + lw + px * 0.6f, Math.Min(b.Right - rw - px * 0.6f - fw, fx - fw / 2));
        if (flx + fw <= b.Right - rw - px * 0.5f && flx >= b.X + lw + px * 0.5f) c.Text(f, flx, b.Y, sp, T.Trim);
        // the bar
        c.Rect(b.X, by, b.W, bh, T.Dim.WithAlpha(0.18f));
        c.Rect(b.X, by, fb, bh, T.Burn);
        c.Rect(b.X + fb, by, fc, bh, T.Coast.WithAlpha(0.75f));
        c.Rect(fx, by, b.Right - fx, bh, T.Brake);
        var gap = T.PanelBg;
        switch (T.Style)
        {
            case ThemeStyle.Tachi:
            case ThemeStyle.Belter:
            case ThemeStyle.Contact:
                for (float x = b.X + 7 * u; x < b.Right; x += 9 * u) c.Rect(x, by, 2 * u, bh, gap);
                break;
            case ThemeStyle.Naval:
                c.Rect(b.X, by - u, b.W, Math.Max(1f, 0.8f * u), T.Trim.WithAlpha(0.6f));
                c.Rect(b.X, by + bh, b.W, Math.Max(1f, 0.8f * u), T.Trim.WithAlpha(0.6f));
                for (int k = 0; k <= 20; k++) c.Rect(b.X + b.W * k / 20f, by + bh + 2 * u, Math.Max(1f, u), (k % 5 == 0 ? 4 : 2) * u, T.Accent.WithAlpha(0.5f));
                break;
            case ThemeStyle.Protogen:
                for (int k = 0; k <= 20; k++) c.Rect(b.X + b.W * k / 20f, by + bh + u, Math.Max(1f, u), (k % 5 == 0 ? 4 : 2) * u, T.Accent.WithAlpha(0.6f));
                break;
        }
        // the flip
        c.Rect(fx - Math.Max(1f, u), by - 3 * u, Math.Max(2f, 2 * u), bh + 6 * u, T.Trim);
    }

    /// <summary>
    /// A button in the theme's look (text size px, the button's colour accent): Classic bars above and below; Tachi a
    /// cut-corner outline with a lit edge; UNN a chamfered plate with a gold tab; OPA corner brackets over a hazard
    /// foot; Protogen brackets with a lit top; Belter a chamfered tag with a trim bar.
    /// </summary>
    public static void Button(IMapCanvas c, Box b, float px, string text, MapColor accent, bool hover, bool on, bool enabled)
    {
        float u = px / 12f, lw = Math.Max(1f, u), lw2 = Math.Max(2f, 2 * u), k = Math.Min(b.H * 0.35f, 7 * u);
        var dim = T.ApDim;
        var edge = enabled ? accent : dim.WithAlpha(0.5f);
        var fill = on ? accent.WithAlpha(0.42f) : hover ? accent.WithAlpha(0.25f) : T.ApPanel;
        c.Rect(b.X, b.Y, b.W, b.H, fill);
        switch (T.Style)
        {
            case ThemeStyle.Tachi:
                Chamfer(c, b, 0.01f, k, lw, edge.WithAlpha(0.85f));
                c.Rect(b.X, b.Y, lw2, b.H, edge);
                break;
            case ThemeStyle.Naval:
                Chamfer(c, b, k * 0.5f, k, lw, edge.WithAlpha(0.8f));
                c.Rect(b.X + lw, b.Y + k * 0.5f, 3 * u, b.H - k, enabled ? T.Trim : dim.WithAlpha(0.5f));
                break;
            case ThemeStyle.Belter:
                Brackets(c, b, Math.Min(6 * u, b.H * 0.4f), lw2, edge);
                if (enabled && (hover || on)) HazardStripe(c, b.X + lw2, b.Bottom - 3 * u, b.W - 2 * lw2, 3 * u);
                else c.Rect(b.X + lw2, b.Bottom - lw, b.W - 2 * lw2, lw, edge.WithAlpha(0.5f));
                break;
            case ThemeStyle.Protogen:
                c.Rect(b.X, b.Y, b.W, lw, edge);
                Brackets(c, b, Math.Min(6 * u, b.H * 0.4f), lw, edge.WithAlpha(0.8f));
                c.Rect(b.X + 2 * u, b.Y + 2 * u, Math.Min(b.W * 0.25f, 18 * u), lw2, enabled ? T.Trim.WithAlpha(0.85f) : dim.WithAlpha(0.4f));
                break;
            case ThemeStyle.Contact:
                Chamfer(c, b, k * 0.5f, k, lw, edge.WithAlpha(0.8f));
                c.Rect(b.X + lw, b.Y + k * 0.5f, lw2, b.H - k, enabled ? T.Trim : dim.WithAlpha(0.5f));
                break;
            default:
                c.Rect(b.X, b.Y, b.W, 2, edge);
                c.Rect(b.X, b.Bottom - 2, b.W, 2, edge);
                break;
        }
        var col = !enabled ? dim.WithAlpha(0.6f) : on || hover ? MapColor.Rgb(0xFFFFFF) : accent;
        c.Text(text, b.X + b.W / 2, b.Y + (b.H - px) / 2, px, col, TextAnchor.Center);
    }

    /// <summary>The timeline's height for text size px.</summary>
    public static float TimeBarH(float px) => px * 0.8f * 1.35f + Math.Max(4f, px * 0.55f) + px * 0.5f;

    // ------------------------------------------------------------------ pieces

    public static void Brackets(IMapCanvas c, Box b, float len, float w, MapColor col)
    {
        c.Rect(b.X, b.Y, len, w, col); c.Rect(b.X, b.Y, w, len, col);
        c.Rect(b.Right - len, b.Y, len, w, col); c.Rect(b.Right - w, b.Y, w, len, col);
        c.Rect(b.X, b.Bottom - w, len, w, col); c.Rect(b.X, b.Bottom - len, w, len, col);
        c.Rect(b.Right - len, b.Bottom - w, len, w, col); c.Rect(b.Right - w, b.Bottom - len, w, len, col);
    }

    /// <summary>OPA: a strip of diagonal orange-and-black hazard stripes.</summary>
    public static void HazardStripe(IMapCanvas c, float x, float y, float w, float h)
    {
        c.PushClip(x, y, w, h);
        c.Rect(x, y, w, h, MapColor.Rgb(0x15130F));
        float step = h * 2.2f, lw = h * 0.9f;
        for (float sx = x - h; sx < x + w + h; sx += step) c.Line(sx, y + h, sx + h, y, lw, T.Accent.WithAlpha(0.9f));
        c.PopClip();
    }

    /// <summary>A box outline with its corners cut: the top-right one by big, the others by small.</summary>
    public static void Chamfer(IMapCanvas c, Box b, float small, float big, float w, MapColor col)
    {
        float l = b.X, t = b.Y, r = b.Right, bt = b.Bottom;
        var pts = new[] { (l + small, t), (r - big, t), (r, t + big), (r, bt - small), (r - small, bt), (l + small, bt), (l, bt - small), (l, t + small) };
        for (int i = 0; i < pts.Length; i++)
        {
            var (ax, ay) = pts[i]; var (bx, by) = pts[(i + 1) % pts.Length];
            c.Line(ax, ay, bx, by, w, col);
        }
    }

    /// <summary>A screen arc; angles in degrees clockwise from straight up.</summary>
    public static void Arc(IMapCanvas c, float x, float y, float r, float a0, float a1, float w, MapColor col, int n)
    {
        float lx = 0, ly = 0;
        for (int i = 0; i <= n; i++)
        {
            double a = (a0 + (a1 - a0) * i / n) * Math.PI / 180;
            float qx = x + r * (float)Math.Sin(a), qy = y - r * (float)Math.Cos(a);
            if (i > 0) c.Line(lx, ly, qx, qy, w, col);
            lx = qx; ly = qy;
        }
    }

    public static void DashedArc(IMapCanvas c, float x, float y, float r, float a0, float a1, float w, MapColor col, int n)
    {
        for (int i = 0; i < n; i += 2) Arc(c, x, y, r, a0 + (a1 - a0) * i / n, a0 + (a1 - a0) * (i + 1) / n, w, col, 1);
    }

    private static void Ray(IMapCanvas c, float x, float y, float a, float r0, float r1, float w, MapColor col)
    {
        double t = a * Math.PI / 180;
        float s = (float)Math.Sin(t), k = (float)Math.Cos(t);
        c.Line(x + s * r0, y - k * r0, x + s * r1, y - k * r1, w, col);
    }
}
