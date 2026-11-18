using System;

namespace SDX2_SystemMap;

/// <summary>
/// The asteroid fields: DX10 Trojans and DX11 Greeks (Jupiter's Lagrange points), each marked with an icon in the
/// theme's style and filled with a scatter of rocks once the view is close enough to see the field; clicking one
/// flies the view to it. Clicking the Belt flies to the part of it under the mouse.
/// <list type="bullet">
/// <item>Classic: a ring round a cluster of rocks.</item>
/// <item>Tachi: a dashed red ring with corner ticks round red rock shards.</item>
/// <item>UNN: a chart hazard mark: a dotted circle of rocks with gold ticks on the four bearings.</item>
/// <item>OPA: a hazard triangle with rocks in it.</item>
/// <item>Protogen: a cyan diamond in corner brackets, with magenta rocks.</item>
/// <item>Belter: a hexagon with yellow rocks and a chevron over it.</item>
/// </list>
/// </summary>
public sealed partial class MapScene
{
    public const string FieldKind = "Asteroid field", BeltKind = "Belt";

    /// <summary>The rocks of an icon: offsets (in icon sizes) and radii (in u).</summary>
    private static readonly (float X, float Y, float R)[] IconRocks =
    {
        (-0.32f, -0.18f, 1.9f), (0.28f, -0.3f, 1.4f), (0.05f, 0.1f, 2.3f), (-0.2f, 0.38f, 1.3f), (0.38f, 0.3f, 1.6f), (-0.45f, 0.12f, 1.0f),
    };

    private void AsteroidFields()
    {
        foreach (var s in SystemData.Sectors)
        {
            if (!IsField(s)) continue;
            double depth = Cam.Depth(s.Center);
            if (depth < Cam.Near || !P(s.Center, out var x, out var y)) continue;
            float rp = (float)(s.Radius * Cam.PixelsPerMetre(depth));
            var col = T.SectorColor(s.Color);
            if (rp > 40 * u) FieldRocks(s, col, Math.Min(1f, (rp - 40 * u) / (80 * u)));
            if (!OnView(x, y, 20)) continue;
            AddPick(s.Name, FieldKind, s.Dx, s.Center, x, y, 10 * u);
            // the route's destination is on the field: its mark and name stand there instead
            if (Path != null && P(Path.Final.Target, out var fx, out var fy) && Math.Abs(fx - x) < 24 * u && Math.Abs(fy - y) < 24 * u) continue;
            FieldIcon(x, y, 9 * u, col);
            if (Opt.Labels && rp <= 26 * u) Label(s.Dx + " " + s.Name.ToUpperInvariant(), x, y, 12 * u, col.WithAlpha(0.85f), 8, 10 * u);
        }
    }

    private static bool IsField(SystemData.Sector s) => s.Name == "Trojans" || s.Name == "Greeks";

    /// <summary>A fixed scatter of rocks through the field's sphere (denser toward its middle), faded in by alpha.</summary>
    private void FieldRocks(SystemData.Sector s, MapColor col, float alpha)
    {
        static double Frac(double v) => v - Math.Floor(v);
        var rock = T.Style == ThemeStyle.Belter ? Accent : T.Style == ThemeStyle.Protogen ? T.Trim : T.Style == ThemeStyle.Tachi ? Accent : col;
        int seed = s.Name == "Greeks" ? 7 : 0;
        for (int i = 1; i <= 220; i++)
        {
            double a = Frac((i + seed) * 0.6180339887) * 2 * Math.PI, z = Frac((i + seed) * 0.7548776662) * 2 - 1, r = Math.Pow(Frac((i + seed) * 0.5698402910), 0.7);
            double h = Math.Sqrt(1 - z * z);
            // a flattened cloud: the fields lie along Jupiter's orbit, thinner above and below
            var p = s.Center + new V3(h * Math.Cos(a), z * 0.35, h * Math.Sin(a)) * (s.Radius * r);
            if (Cam.Depth(p) < Cam.Near || !P(p, out var x, out var y) || !OnView(x, y)) continue;
            float sz = (1f + (i % 3) * 0.6f) * u;
            c.Disc(x, y, sz, (i % 5 == 0 ? TextCol : rock).WithAlpha(0.55f * alpha));
        }
    }

    /// <summary>The field's icon at (x, y), s across its middle.</summary>
    private void FieldIcon(float x, float y, float s, MapColor col)
    {
        float lw = Math.Max(1f, 1.2f * u);
        MapColor rock = col;
        switch (T.Style)
        {
            case ThemeStyle.Tachi:
                for (int k = 0; k < 6; k++) Arc(x, y, s * 1.15f, k * 60 + 8, k * 60 + 42, lw, Accent, 5);
                foreach (var (dx, dy) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
                {
                    float cx = x + dx * s * 1.45f, cy = y + dy * s * 1.45f;
                    c.Line(cx, cy, cx - dx * s * 0.35f, cy, lw, T.Fine.WithAlpha(0.8f));
                    c.Line(cx, cy, cx, cy - dy * s * 0.35f, lw, T.Fine.WithAlpha(0.8f));
                }
                rock = Accent;
                break;
            case ThemeStyle.Naval:
                DashedCircle(x, y, s * 1.2f, lw, OrbitCol.WithAlpha(0.8f), 24);
                for (int k = 0; k < 4; k++) Spoke(x, y, k * 90, s * 1.2f, s * 1.6f, lw, T.Trim);
                rock = MapColor.Lerp(col, TextCol, 0.3f);
                break;
            case ThemeStyle.Belter:
            {
                // a hazard triangle, point up
                float h = s * 1.5f;
                float ax = x, ay = y - h, bx = x + h * 0.95f, by = y + h * 0.62f, cx2 = x - h * 0.95f;
                float w2 = Math.Max(2f, 2.2f * u);
                c.Line(ax, ay, bx, by, w2, Accent); c.Line(bx, by, cx2, by, w2, Accent); c.Line(cx2, by, ax, ay, w2, Accent);
                rock = T.Ship;
                y += s * 0.15f;
                break;
            }
            case ThemeStyle.Protogen:
            {
                float d = s * 1.2f;
                c.Line(x, y - d, x + d, y, lw, Accent); c.Line(x + d, y, x, y + d, lw, Accent);
                c.Line(x, y + d, x - d, y, lw, Accent); c.Line(x - d, y, x, y - d, lw, Accent);
                float b = s * 1.6f;
                Brackets(new Box(x - b, y - b, 2 * b, 2 * b), s * 0.45f, lw, T.Fine.WithAlpha(0.6f));
                rock = T.Trim;
                break;
            }
            case ThemeStyle.Contact:
                Hexagon(x, y, s * 1.3f, lw, Accent.WithAlpha(0.85f));
                Chevron(x, y - s * 1.9f, s * 0.4f, 0, 1, lw, T.Trim);
                rock = T.Trim;
                break;
            default:
                ScreenCircle(x, y, s * 1.2f, lw, col.WithAlpha(0.85f), 28);
                break;
        }
        foreach (var (rx, ry, rr) in IconRocks) c.Disc(x + rx * s * 1.4f, y + ry * s * 1.4f, Math.Max(1f, rr * u * 0.9f), rock);
    }

    /// <summary>
    /// The Belt under the mouse, as a pick: where the mouse's ray meets the Belt's plane inside the tube, the point on
    /// the tube's middle line at that bearing. Ranks behind everything else under the mouse.
    /// </summary>
    private void BeltPick()
    {
        if (float.IsNaN(MouseX) || !view.Contains(MouseX, MouseY)) return;
        var b = SystemData.Belt;
        var n = b.Normal.Normalized;
        var ray = Cam.Ray(MouseX, MouseY);
        double dn = ray.Dot(n);
        if (Math.Abs(dn) < 1e-6) return;
        double t = (b.Center - Cam.Eye).Dot(n) / dn;
        if (t <= 0) return;
        var hit = Cam.Eye + ray * t;
        var radial = hit - b.Center;
        radial -= n * radial.Dot(n);
        double r = radial.Length;
        if (r < 1 || Math.Abs(r - b.Major) > b.Minor) return;
        var pos = b.Center + radial * (b.Major / r);
        Picks.Add(new Pick { Name = "The Belt", Kind = BeltKind, Detail = "DX6", Pos = pos, X = MouseX, Y = MouseY, R = 3 * u });
    }

    /// <summary>How far back a click on a pick puts the view: worlds by their size, fields and the Belt so they fill
    /// the view, everything else a little way out.</summary>
    public static double FocusDistance(Pick p)
    {
        if (p.Kind == FieldKind)
            foreach (var s in SystemData.Sectors)
                if (s.Name == p.Name) return s.Radius * 3.2;
        if (p.Kind == BeltKind) return SystemData.Belt.Minor * 8;
        var w = SystemData.FindWorld(p.Name);
        return w != null ? Math.Max(w.Radius * 18, 6e5) : 3e5;
    }
}
