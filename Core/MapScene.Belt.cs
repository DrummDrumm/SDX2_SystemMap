using System;

namespace SDX2_SystemMap;

/// <summary>
/// The Belt as a 3D torus (<see cref="SystemData.Belt"/>, from the Skylark Belt plugin), drawn as rings round the tube
/// and cross-sections through it, in each theme's own way. The far side of the tube is dimmer than the near side.
/// <list type="bullet">
/// <item>Classic: as the Skylark Belt plugin draws it in game: 6 rings round the tube and 36 round cross-sections.</item>
/// <item>Tachi: 4 segmented rings and a cross-section every 30 degrees in the HUD red.</item>
/// <item>UNN: the chart's tube: its inner and outer edges, top and bottom, and a cross-section on every 30-degree bearing, gold at 000 / 090 / 180 / 270.</item>
/// <item>Belter: dashed edges, hazard-dashed cross-sections every 45 degrees and a scatter of rocks through the tube.</item>
/// <item>Protogen: a fine wireframe lattice (8 rings, 48 cross-sections) with a bright ring along its top.</item>
/// <item>OPA: a hexagonal tube: 6 rings on its corners and a hexagon cross-section every 15 degrees.</item>
/// </list>
/// </summary>
public sealed partial class MapScene
{
    private V3 beltE1, beltE2, beltN;
    private double beltDepth;

    private void BeltTorus3D(MapColor bc)
    {
        var b = SystemData.Belt;
        beltN = b.Normal.Normalized;
        var a = Math.Abs(beltN.Y) < 0.9 ? new V3(0, 1, 0) : new V3(1, 0, 0);
        beltE1 = (a - beltN * a.Dot(beltN)).Normalized;
        beltE2 = beltE1.Cross(beltN);
        beltDepth = Cam.Depth(b.Center);
        float w = Math.Max(1f, 0.8f * u);
        const double deg = Math.PI / 180;
        switch (T.Style)
        {
            case ThemeStyle.Tachi:
                foreach (int ph in new[] { 0, 90, 180, 270 }) TorusRing(ph * deg, 180, w, bc, ph % 180 == 0 ? 0.5f : 0.35f, 6, 2);
                for (int k = 0; k < 12; k++) TorusRib(k * 30 * deg, 20, Math.Max(1f, 1.2f * u), Accent, 0.55f);
                break;
            case ThemeStyle.Naval:
                TorusRing(0, 180, w, bc, 0.5f);
                TorusRing(180 * deg, 180, w, bc, 0.5f);
                TorusRing(90 * deg, 180, w, bc, 0.3f);
                TorusRing(270 * deg, 180, w, bc, 0.3f);
                for (int k = 0; k < 12; k++) TorusRib(k * 30 * deg, 24, w, k % 3 == 0 ? T.Trim : bc, k % 3 == 0 ? 0.75f : 0.35f);
                break;
            case ThemeStyle.Belter:
                TorusRing(0, 240, w, bc, 0.45f, 5, 2);
                TorusRing(180 * deg, 240, w, bc, 0.45f, 5, 2);
                for (int k = 0; k < 8; k++) TorusRib(k * 45 * deg, 24, Math.Max(1f, 1.3f * u), k % 2 == 0 ? Accent : T.Ship, 0.6f, 2, 1);
                BeltRocks(260, Accent);
                break;
            case ThemeStyle.Protogen:
                for (int k = 0; k < 8; k++) TorusRing(k * 45 * deg, 160, w, bc, 0.25f);
                for (int k = 0; k < 48; k++) TorusRib(k * 7.5 * deg, 18, w, bc, 0.18f);
                TorusRing(90 * deg, 200, Math.Max(1f, 1.2f * u), Accent, 0.55f);
                break;
            case ThemeStyle.Contact:
                for (int k = 0; k < 6; k++) TorusRing((30 + k * 60) * deg, 180, w, bc, k == 1 || k == 4 ? 0.55f : 0.36f);
                for (int k = 0; k < 24; k++) TorusRib(k * 15 * deg, 6, w, bc, 0.42f, 0, 0, 30 * deg);
                break;
            default:
                for (int k = 0; k < 6; k++) TorusRing(k * 60 * deg, 180, w, bc, 0.34f);
                for (int k = 0; k < 36; k++) TorusRib(k * 10 * deg, 24, w, bc, 0.2f);
                break;
        }
    }

    /// <summary>A point on the torus: theta round the Belt from its first axis, phi round the tube from its outer edge (radius 0..1 of the tube).</summary>
    private V3 TorusAt(double theta, double phi, double radius = 1)
    {
        var b = SystemData.Belt;
        double ring = b.Major + b.Minor * radius * Math.Cos(phi);
        return b.Center + (beltE1 * Math.Cos(theta) + beltE2 * Math.Sin(theta)) * ring + beltN * (b.Minor * radius * Math.Sin(phi));
    }

    /// <summary>1 on the tube's near side, down to 0.35 on its far side.</summary>
    private float BeltFade(V3 p) => (float)Math.Max(0.35, Math.Min(1, 0.65 + 0.35 * (beltDepth - Cam.Depth(p)) / SystemData.Belt.Major));

    /// <summary>A ring round the Belt at angle phi round the tube, in n segments (dashed: on segments then off).</summary>
    private void TorusRing(double phi, int n, float w, MapColor col, float alpha, int on = 0, int off = 0)
    {
        var last = TorusAt(0, phi);
        for (int i = 1; i <= n; i++)
        {
            var p = TorusAt(i * 2 * Math.PI / n, phi);
            if (on == 0 || (i - 1) % (on + off) < on) Seg(last, p, w, col.WithAlpha(alpha * BeltFade(p)));
            last = p;
        }
    }

    /// <summary>A cross-section of the tube at angle theta round the Belt: n sides (6 = a hexagon) from phi0, dashed if on > 0.</summary>
    private void TorusRib(double theta, int n, float w, MapColor col, float alpha, int on = 0, int off = 0, double phi0 = 0)
    {
        var last = TorusAt(theta, phi0);
        float a = alpha * BeltFade(TorusAt(theta, 0));
        for (int i = 1; i <= n; i++)
        {
            var p = TorusAt(theta, phi0 + i * 2 * Math.PI / n);
            if (on == 0 || (i - 1) % (on + off) < on) Seg(last, p, w, col.WithAlpha(a));
            last = p;
        }
    }

    /// <summary>Belter: a fixed scatter of rocks through the tube (the same every frame).</summary>
    private void BeltRocks(int count, MapColor col)
    {
        static double Frac(double v) => v - Math.Floor(v);
        for (int i = 1; i <= count; i++)
        {
            double th = Frac(i * 0.6180339887) * 2 * Math.PI, ph = Frac(i * 0.7548776662) * 2 * Math.PI, r = Math.Sqrt(Frac(i * 0.5698402910));
            var p = TorusAt(th, ph, r);
            if (Cam.Depth(p) < Cam.Near || !P(p, out var x, out var y) || !OnView(x, y)) continue;
            float s = (1f + (i % 3) * 0.6f) * u;
            c.Disc(x, y, s, (i % 5 == 0 ? T.Ship : col).WithAlpha(0.55f * BeltFade(p)));
        }
    }
}
