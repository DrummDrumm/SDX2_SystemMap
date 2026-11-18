using System;
using System.Collections.Generic;

namespace SDX2_SystemMap;

/// <summary>
/// Your ship on the map: a top-down silhouette for its SDX2 core class, turned to the way the ship faces (shortened
/// along its nose as the nose turns toward or away from you), and the trajectory marker each theme draws in its own way
/// along the velocity.
/// </summary>
public static class ShipIcons
{
    /// <summary>A class's silhouette: its outline (nose along +X, from -1 to 1; Y across), its size against a frigate's,
    /// and lines drawn over it (pairs of points: hull details).</summary>
    public sealed class Shape
    {
        public string Class;
        public (float X, float Y)[] Outline;
        public float Size = 1;
        public (float X, float Y)[] Details = new (float, float)[0];
        /// <summary>Drive bells on the stern: where each one's throat sits (X, Y) and its size (1 = a frigate's).</summary>
        public (float X, float Y, float S)[] Bells = new (float, float, float)[0];
        /// <summary>A shape sampled from a real ship (<see cref="ShipSamples"/>): filled row runs (X from, X to, Y), each
        /// <see cref="RowH"/> tall, and its outline as line pairs in <see cref="Details"/> (no <see cref="Outline"/>).</summary>
        public List<(float X0, float X1, float Y)> Runs;
        public float RowH;
        /// <summary>The ship it was taken from.</summary>
        public string From;
    }

    private static Dictionary<string, Shape> sampled;

    /// <summary>The class's icon taken from a real ship (null when no ship of that class was sampled).</summary>
    public static Shape Sampled(string cls)
    {
        if (string.IsNullOrEmpty(cls)) return null;
        if (sampled == null)
        {
            sampled = new Dictionary<string, Shape>(StringComparer.OrdinalIgnoreCase);
            foreach (var (c, from, rows) in ShipSamples.All) sampled[c] = FromMask(c, from, rows);
        }
        return sampled.TryGetValue(cls, out var s) ? s : null;
    }

    /// <summary>The icon to draw: the sampled one when wanted and there is one, else the drawn one.</summary>
    public static Shape Pick(string cls, bool real) => (real ? Sampled(cls) : null) ?? For(cls);

    /// <summary>A mask (rows across the ship, '#' = hull, the nose at column 0) as fill runs and an outline, nose at X = 1.</summary>
    public static Shape FromMask(string cls, string from, string[] rows)
    {
        int H = rows.Length, L = 0;
        foreach (var r in rows) L = Math.Max(L, r.Length);
        float h = 2f / Math.Max(1, L);
        bool On(int j, int i) => j >= 0 && j < H && i >= 0 && i < rows[j].Length && rows[j][i] == '#';
        float X(int i) => 1 - i * h;
        float Y(int j) => (j - (H - 1) / 2f) * h; // row middle
        var runs = new List<(float, float, float)>();
        for (int j = 0; j < H; j++)
            for (int i = 0; i < L;)
            {
                if (!On(j, i)) { i++; continue; }
                int k = i; while (On(j, k)) k++;
                runs.Add((X(i), X(k), Y(j)));
                i = k;
            }
        var edges = new List<(float, float)>();
        // across: between row j-1 and j
        for (int j = 0; j <= H; j++)
            for (int i = 0; i < L;)
            {
                if (On(j - 1, i) == On(j, i)) { i++; continue; }
                int k = i; while (k < L && On(j - 1, k) != On(j, k)) k++;
                float y = Y(j) - h / 2;
                edges.Add((X(i), y)); edges.Add((X(k), y));
                i = k;
            }
        // along: between column i-1 and i
        for (int i = 0; i <= L; i++)
            for (int j = 0; j < H;)
            {
                if (On(j, i - 1) == On(j, i)) { j++; continue; }
                int k = j; while (k < H && On(k, i - 1) != On(k, i)) k++;
                edges.Add((X(i), Y(j) - h / 2)); edges.Add((X(i), Y(k) - h / 2));
                j = k;
            }
        return new Shape { Class = cls, From = from, Runs = runs, RowH = h, Details = edges.ToArray(), Size = For(cls)?.Size ?? 1 };
    }

    /// <summary>The upper half (Y ≥ 0, nose to tail) mirrored into a closed outline.</summary>
    private static (float, float)[] Mirror(params float[] xy)
    {
        var res = new List<(float, float)>();
        for (int i = 0; i < xy.Length; i += 2) res.Add((xy[i], xy[i + 1]));
        for (int i = xy.Length - 2; i >= 0; i -= 2) if (xy[i + 1] != 0) res.Add((xy[i], -xy[i + 1]));
        return res.ToArray();
    }

    /// <summary>Drive bells from (x, y, size) triples; one off the keel (y not 0) gets its mirror too.</summary>
    private static (float, float, float)[] B(params float[] xys)
    {
        var res = new List<(float, float, float)>();
        for (int i = 0; i + 2 < xys.Length; i += 3)
        {
            res.Add((xys[i], xys[i + 1], xys[i + 2]));
            if (xys[i + 1] != 0) res.Add((xys[i], -xys[i + 1], xys[i + 2]));
        }
        return res.ToArray();
    }

    /// <summary>A drive bell's throat and mouth half-widths and its length, at size 1 (icon units).</summary>
    public const float BellThroat = 0.055f, BellMouth = 0.11f, BellLength = 0.2f;

    private static (float, float)[] P(params float[] xy)
    {
        var res = new (float, float)[xy.Length / 2];
        for (int i = 0; i < res.Length; i++) res[i] = (xy[2 * i], xy[2 * i + 1]);
        return res;
    }

    private static (float, float)[] Ring(int n, float r, float turn = 0)
    {
        var res = new (float, float)[n];
        for (int i = 0; i < n; i++) { double a = turn + i * 2 * Math.PI / n; res[i] = ((float)(Math.Cos(a) * r), (float)(Math.Sin(a) * r)); }
        return res;
    }

    public static readonly Shape[] All =
    {
        // civilian: a small pod, a boxy barge, a cab pulling a cargo spine
        new Shape { Class = "Skiff", Size = 0.75f, Outline = Mirror(1, 0, 0.45f, 0.38f, -0.7f, 0.45f, -1, 0.25f, -1, 0),
            Details = P(0.35f, 0.2f, 0.35f, -0.2f), Bells = B(-1, 0, 1.3f) },
        // the barge and the hauler lead with their square cargo blocks; the drives sit on the narrower stern
        new Shape { Class = "Barge", Size = 1.05f, Outline = Mirror(1, 0, 1, 0.55f, -0.82f, 0.55f, -1, 0.3f, -1, 0),
            Details = P(-0.45f, 0.55f, -0.45f, -0.55f, 0.05f, 0.55f, 0.05f, -0.55f, 0.55f, 0.55f, 0.55f, -0.55f), Bells = B(-1, 0, 1.1f, -1, 0.2f, 1.1f) },
        new Shape { Class = "Hauler", Size = 1.15f, Outline = Mirror(1, 0, 1, 0.42f, 0.92f, 0.5f, -0.5f, 0.5f, -0.55f, 0.3f, -1, 0.3f, -1, 0),
            Details = P(0.9f, 0, -0.5f, 0, 0.42f, 0.5f, 0.42f, -0.5f, -0.05f, 0.5f, -0.05f, -0.5f, -0.55f, 0.3f, -0.55f, -0.3f), Bells = B(-1, 0.16f, 1.25f) },
        // combat, smallest to largest
        new Shape { Class = "Picket", Size = 0.8f, Outline = Mirror(1, 0, -0.45f, 0.26f, -1, 0.34f, -0.78f, 0), Bells = B(-0.78f, 0, 1.2f) },
        new Shape { Class = "Corvette", Size = 0.9f, Outline = Mirror(1, 0, 0.25f, 0.2f, -0.3f, 0.2f, -0.62f, 0.52f, -1, 0.52f, -0.88f, 0.18f, -1, 0.14f, -1, 0),
            Bells = B(-1, 0, 1.3f, -0.98f, 0.42f, 0.85f) },
        new Shape { Class = "Frigate", Size = 1, Outline = Mirror(1, 0, 0.6f, 0.14f, 0.1f, 0.17f, 0, 0.42f, -0.42f, 0.42f, -0.5f, 0.2f, -1, 0.22f, -1, 0),
            Details = P(0.6f, 0, -0.85f, 0), Bells = B(-1, 0.11f, 1) },
        new Shape { Class = "Cruiser", Size = 1.12f, Outline = Mirror(1, 0, 0.72f, 0.1f, 0.4f, 0.27f, -0.2f, 0.3f, -0.42f, 0.46f, -0.82f, 0.46f, -1, 0.3f, -1, 0),
            Details = P(0.4f, 0.27f, 0.4f, -0.27f, -0.42f, 0.3f, -0.42f, -0.3f), Bells = B(-1, 0, 1.1f, -1, 0.2f, 1) },
        new Shape { Class = "Battleship", Size = 1.28f, Outline = Mirror(1, 0.08f, 0.8f, 0.2f, 0.3f, 0.42f, -0.6f, 0.5f, -1, 0.38f, -1, 0),
            Details = P(0.55f, 0, -0.85f, 0, 0.2f, 0.2f, 0.2f, -0.2f, -0.3f, 0.25f, -0.3f, -0.25f), Bells = B(-1, 0, 1.2f, -1, 0.25f, 1.1f) },
        new Shape { Class = "Carrier", Size = 1.3f, Outline = Mirror(1, 0.14f, 0.85f, 0.42f, -0.9f, 0.42f, -1, 0.25f, -1, 0),
            Details = P(0.8f, 0.05f, -0.85f, 0.05f, 0.8f, -0.05f, -0.85f, -0.05f), Bells = B(-1, 0.14f, 1.1f) },
        // a forked nose round its spinal gun
        new Shape { Class = "Dreadnought", Size = 1.42f, Outline = Mirror(1, 0.26f, 0.7f, 0.26f, 0.6f, 0.11f, 0.45f, 0.11f, 0.38f, 0.5f, -0.7f, 0.56f, -1, 0.4f, -1, 0),
            Details = P(0.45f, 0, -0.85f, 0, -0.1f, 0.5f, -0.1f, -0.5f), Bells = B(-1, 0, 1.4f, -1, 0.27f, 1.1f) },
        // bases (turned like ships, though they don't move)
        new Shape { Class = "Outpost", Size = 1.1f, Outline = Ring(6, 1f), Details = P(-0.5f, 0, 0.5f, 0, 0, -0.5f, 0, 0.5f) },
        new Shape { Class = "Installation", Size = 1.25f, Outline = Ring(8, 1f, (float)(Math.PI / 8)), Details = P(-0.6f, -0.6f, 0.6f, 0.6f, -0.6f, 0.6f, 0.6f, -0.6f) },
        new Shape { Class = "Station Admin", Size = 1.3f, Outline = Star() },
    };

    private static (float, float)[] Star()
    {
        var res = new (float, float)[10];
        for (int i = 0; i < 10; i++) { double a = i * Math.PI / 5; float r = i % 2 == 0 ? 1f : 0.45f; res[i] = ((float)(Math.Cos(a) * r), (float)(Math.Sin(a) * r)); }
        return res;
    }

    /// <summary>The silhouette for a core class ("" or unknown: null, the plain marker).</summary>
    public static Shape For(string cls)
    {
        if (string.IsNullOrEmpty(cls)) return null;
        foreach (var s in All) if (string.Equals(s.Class, cls, StringComparison.OrdinalIgnoreCase)) return s;
        return null;
    }

    /// <summary>
    /// Where along a row across the outline (at Y = y) it's inside: the X spans, in pairs, for filling it a row at a time.
    /// </summary>
    public static List<float> Row((float X, float Y)[] o, float y)
    {
        var xs = new List<float>();
        for (int i = 0; i < o.Length; i++)
        {
            var a = o[i]; var b = o[(i + 1) % o.Length];
            if (a.Y == b.Y) continue;
            if (y < Math.Min(a.Y, b.Y) || y >= Math.Max(a.Y, b.Y)) continue;
            xs.Add(a.X + (y - a.Y) / (b.Y - a.Y) * (b.X - a.X));
        }
        xs.Sort();
        return xs;
    }
}

public sealed partial class MapScene
{
    private float shipNoseX = 0, shipNoseY = -1;

    /// <summary>
    /// The ship's on-screen nose: unit direction and how much of a nose-on-the-screen it is (1 = across the screen,
    /// toward 0 = pointing at you or away). Holds the last direction while the nose points straight at the view.
    /// </summary>
    private (float X, float Y, float Along) ShipNose(float x, float y)
    {
        var f = Snap.ShipFwd;
        if (f.Length < 1e-6) return (shipNoseX, shipNoseY, 1);
        double ppm = Cam.PixelsPerMetre(Cam.Depth(Snap.ShipPos));
        float L = 20 * u;
        if (ppm <= 0 || !P(Snap.ShipPos + f.Normalized * (L / ppm), out var fx, out var fy)) return (shipNoseX, shipNoseY, 1);
        float dx = fx - x, dy = fy - y, l = (float)Math.Sqrt(dx * dx + dy * dy);
        if (l > 0.08f * L) { shipNoseX = dx / l; shipNoseY = dy / l; }
        return (shipNoseX, shipNoseY, Math.Max(0.35f, Math.Min(1, l / L)));
    }

    /// <summary>The class silhouette at (x, y), r from the centre to the nose, filled a row at a time and outlined.</summary>
    private void ShipShape(ShipIcons.Shape s, float x, float y, float r, float nx, float ny, float along, MapColor fill, MapColor edge)
    {
        float px = -ny, py = nx, ra = r * along; // across the ship on screen
        (float, float) At(float ix, float iy) => (x + nx * ix * ra + px * iy * r, y + ny * ix * ra + py * iy * r);
        if (s.Runs != null)
        {
            // sampled from a real ship: its rows, then its outline
            float rw = s.RowH * r + 0.7f;
            foreach (var (x0, x1, iy) in s.Runs)
            {
                var (ax, ay) = At(x0 + s.RowH * 0.15f, iy); var (bx, by) = At(x1 - s.RowH * 0.15f, iy);
                c.Line(ax, ay, bx, by, rw, fill);
            }
            float ew = Math.Max(1f, 1.1f * u);
            for (int i = 0; i + 1 < s.Details.Length; i += 2)
            {
                var (ax, ay) = At(s.Details[i].X, s.Details[i].Y); var (bx, by) = At(s.Details[i + 1].X, s.Details[i + 1].Y);
                c.Line(ax, ay, bx, by, ew, edge);
            }
            return;
        }
        // drive bells on the stern, under the hull: flaring back from the throat
        foreach (var (bx0, by0, bs) in s.Bells)
        {
            float t = ShipIcons.BellThroat * bs, m = ShipIcons.BellMouth * bs, bl = ShipIcons.BellLength * bs, x1 = bx0 + 0.03f, x2 = bx0 - bl;
            var (tax, tay) = At(x1, by0 - t); var (tbx, tby) = At(x1, by0 + t);
            var (max, may) = At(x2, by0 - m); var (mbx, mby) = At(x2, by0 + m);
            float bw = Math.Max(1f, (t + m) * r * 0.5f);
            for (int k = 0; k <= 3; k++)
            {
                float f = k / 3f;
                c.Line(tax + (tbx - tax) * f, tay + (tby - tay) * f, max + (mbx - max) * f, may + (mby - may) * f, bw, fill);
            }
            float bew = Math.Max(1f, 1f * u);
            c.Line(tax, tay, max, may, bew, edge); c.Line(tbx, tby, mbx, mby, bew, edge); c.Line(max, may, mbx, mby, bew, edge);
        }
        var o = s.Outline;
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (var q in o) { minY = Math.Min(minY, q.Y); maxY = Math.Max(maxY, q.Y); }
        int rows = Math.Max(5, Math.Min(18, (int)((maxY - minY) * r / (1.4f * u))));
        float step = (maxY - minY) / rows, w = step * r + 0.7f;
        for (int i = 0; i < rows; i++)
        {
            float iy = minY + (i + 0.5f) * step;
            var xs = ShipIcons.Row(o, iy);
            for (int k = 0; k + 1 < xs.Count; k += 2)
            {
                var (ax, ay) = At(xs[k], iy); var (bx, by) = At(xs[k + 1], iy);
                c.Line(ax, ay, bx, by, w, fill);
            }
        }
        float ow = Math.Max(1f, 1.2f * u);
        for (int i = 0; i < o.Length; i++)
        {
            var (ax, ay) = At(o[i].X, o[i].Y); var (bx, by) = At(o[(i + 1) % o.Length].X, o[(i + 1) % o.Length].Y);
            c.Line(ax, ay, bx, by, ow, edge);
        }
        var d = s.Details;
        for (int i = 0; i + 1 < d.Length; i += 2)
        {
            var (ax, ay) = At(d[i].X, d[i].Y); var (bx, by) = At(d[i + 1].X, d[i + 1].Y);
            c.Line(ax, ay, bx, by, Math.Max(1f, 0.8f * u), edge.WithAlpha(0.7f));
        }
    }

    /// <summary>
    /// Where the ship is going: from the icon's edge out along the velocity on screen (dx, dy unit), len long, in the theme's
    /// own way:
    /// <list type="bullet">
    /// <item>Classic: a plain arrow.</item>
    /// <item>Tachi: a segmented red line ending in a double chevron.</item>
    /// <item>UNN: a plotting vector with ruler ticks and a gold diamond at its end.</item>
    /// <item>OPA: a run of hazard chevrons, brighter toward the front.</item>
    /// <item>Protogen: a thin line to a small reticle (a ring with crosshair ticks) where the ship is headed.</item>
    /// <item>Belter: a dotted trail ending in a filled triangle.</item>
    /// </list>
    /// </summary>
    private void CourseMarker(float x, float y, float dx, float dy, float start, float len, MapColor col)
    {
        float px = -dy, py = dx;
        float sx = x + dx * start, sy = y + dy * start, ex = x + dx * (start + len), ey = y + dy * (start + len);
        float w = Math.Max(1f, 1.6f * u);
        void Chevron(float cx, float cy, float h, float lw, MapColor cc)
        {
            c.Line(cx, cy, cx - dx * h + px * h * 0.7f, cy - dy * h + py * h * 0.7f, lw, cc);
            c.Line(cx, cy, cx - dx * h - px * h * 0.7f, cy - dy * h - py * h * 0.7f, lw, cc);
        }
        switch (T.Style)
        {
            case ThemeStyle.Tachi:
            {
                // segmented line, then a double chevron
                int n = 4; float seg = (len - 8 * u) / n;
                for (int i = 0; i < n; i++)
                {
                    float a = start + i * seg, b = a + seg * 0.62f;
                    c.Line(x + dx * a, y + dy * a, x + dx * b, y + dy * b, w, col.WithAlpha(0.55f + 0.1f * i));
                }
                Chevron(ex, ey, 6 * u, w, col);
                Chevron(ex - dx * 4.5f * u, ey - dy * 4.5f * u, 6 * u, w, col.WithAlpha(0.6f));
                break;
            }
            case ThemeStyle.Naval:
            {
                // plotting vector: ruler ticks across it, a gold diamond at the end
                c.Line(sx, sy, ex, ey, w, col.WithAlpha(0.85f));
                for (int i = 1; i <= 3; i++)
                {
                    float a = start + len * i / 4f, h = (i == 2 ? 4.5f : 3f) * u;
                    float tx = x + dx * a, ty = y + dy * a;
                    c.Line(tx - px * h, ty - py * h, tx + px * h, ty + py * h, Math.Max(1f, u), col.WithAlpha(0.7f));
                }
                c.Rect(ex - 4 * u, ey - 4 * u, 8 * u, 8 * u, T.Trim, (float)(Math.PI / 4));
                c.Rect(ex - 2 * u, ey - 2 * u, 4 * u, 4 * u, Bg, (float)(Math.PI / 4));
                break;
            }
            case ThemeStyle.Belter:
            {
                // hazard chevrons, brighter toward the front
                int n = 4;
                for (int i = 0; i < n; i++)
                {
                    float a = start + 6 * u + (len - 6 * u) * (i + 1) / n;
                    Chevron(x + dx * a, y + dy * a, 5.5f * u, Math.Max(1.5f, 2.2f * u), (i % 2 == 0 ? Accent : col).WithAlpha(0.45f + 0.55f * (i + 1) / n));
                }
                break;
            }
            case ThemeStyle.Protogen:
            {
                // thin line to a small reticle where it's headed
                float rr = 4.5f * u;
                c.Line(sx, sy, ex - dx * rr, ey - dy * rr, Math.Max(1f, 1.1f * u), col.WithAlpha(0.8f));
                ScreenCircle(ex, ey, rr, Math.Max(1f, 1.1f * u), Accent, 20);
                float t0 = rr * 1.4f, t1 = rr * 2.1f;
                c.Line(ex + dx * t0, ey + dy * t0, ex + dx * t1, ey + dy * t1, Math.Max(1f, 1.1f * u), Accent);
                c.Line(ex + px * t0, ey + py * t0, ex + px * t1, ey + py * t1, Math.Max(1f, 1.1f * u), Accent.WithAlpha(0.6f));
                c.Line(ex - px * t0, ey - py * t0, ex - px * t1, ey - py * t1, Math.Max(1f, 1.1f * u), Accent.WithAlpha(0.6f));
                c.Disc(ex, ey, 1.2f * u, Accent);
                break;
            }
            case ThemeStyle.Contact:
            {
                // dotted trail, a filled triangle at the end
                int n = 6;
                for (int i = 0; i < n; i++)
                {
                    float a = start + (len - 8 * u) * (i + 0.5f) / n;
                    c.Disc(x + dx * a, y + dy * a, 1.3f * u, col.WithAlpha(0.4f + 0.5f * i / n));
                }
                Triangle(ex - dx * 3 * u, ey - dy * 3 * u, 5 * u, dx, dy, col);
                break;
            }
            default:
            {
                c.Line(sx, sy, ex, ey, 2 * u, col.WithAlpha(0.9f));
                Chevron(ex, ey, 6 * u, 2 * u, col);
                break;
            }
        }
    }

    /// <summary>
    /// A preview strip for the tests: every class's icon in the current theme, each turned a different way, with the
    /// theme's trajectory marker off to one side (as when the ship drifts sideways), in the box at (x0, y0).
    /// </summary>
    public void ShipSheet(IMapCanvas canvas, float x0, float y0, float w, float h, bool real = false)
    {
        c = canvas; u = h / 160f;
        c.Rect(x0, y0, w, h, Bg);
        c.Text(T.Name + (real ? "  (from real ships)" : ""), x0 + 8 * u, y0 + 6 * u, 12 * u, Accent);
        int n = ShipIcons.All.Length;
        float cw = w / n;
        for (int i = 0; i < n; i++)
        {
            var s = ShipIcons.Pick(ShipIcons.All[i].Class, real);
            float cx = x0 + cw * (i + 0.5f), cy = y0 + h * 0.5f;
            double face = -Math.PI / 2 + i * 0.45, go = face + 0.6;
            float r = 13 * u * s.Size;
            CourseMarker(cx, cy, (float)Math.Cos(go), (float)Math.Sin(go), r + 3 * u, 40 * u, ShipCol);
            c.Disc(cx, cy, r * 1.05f, ShipCol.WithAlpha(0.1f));
            ShipShape(s, cx, cy, r, (float)Math.Cos(face), (float)Math.Sin(face), 1, ShipCol.WithAlpha(0.5f), ShipCol);
            c.Text(s.Class, cx, y0 + h - 16 * u, 9 * u, ShipCol, TextAnchor.Center);
            if (s.From != null) c.Text(s.From.Length > 22 ? s.From.Substring(0, 21) + "…" : s.From, cx, y0 + h - 8 * u, 6 * u, ShipCol.WithAlpha(0.6f), TextAnchor.Center);
        }
    }
}
