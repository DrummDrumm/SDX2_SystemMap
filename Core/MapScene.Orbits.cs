using System;

namespace SDX2_SystemMap;

/// <summary>
/// Local orbit lines: each moon round its planet, and each station round the world it sits by (a moon, planet or
/// asteroid within <see cref="StationOrbitReach"/> of its radius). Worlds don't move in SDX2, so the line is the circle
/// round the parent through where the moon or station is, tilted to pass through it (its axis level, at right angles
/// to the line from the parent). Drawn only once the circle is big enough on screen to read. Ground facilities
/// (<see cref="SystemData.Station.Above"/>) get none: they're marked as on the ground instead.
/// </summary>
public sealed partial class MapScene
{
    /// <summary>A station orbits a world when it's within this many of the world's radii of its centre.</summary>
    public const double StationOrbitReach = 3;

    /// <summary>The world a station orbits, or null (it's out in space: Tycho, the Trojans and Greeks stations).</summary>
    public static SystemData.World StationParent(V3 pos)
    {
        SystemData.World best = null; double bestK = StationOrbitReach;
        foreach (var w in SystemData.Worlds)
        {
            if (w.IsStar) continue;
            double k = V3.Distance(pos, w.Pos) / Math.Max(1, w.Radius);
            if (k < bestK) { bestK = k; best = w; }
        }
        return best;
    }

    private void LocalOrbits()
    {
        float w = Math.Max(1f, 0.7f * u);
        bool dashed = Chart || T.Style == ThemeStyle.Belter;
        if (Opt.Moons)
            foreach (var m in SystemData.Worlds)
            {
                if (m.IsPlanet || m.IsAsteroid || m.IsObstacle) continue;
                var parent = SystemData.FindWorld(m.Parent);
                if (parent != null) OrbitThrough(parent.Pos, m.Pos, w, OrbitCol.WithAlpha(0.28f), dashed);
            }
        if (Opt.Stations)
            foreach (var s in SystemData.Stations)
            {
                if (s.Above > 0) continue; // a ground facility sits on its world, it doesn't orbit it
                var parent = StationParent(s.Pos);
                if (parent != null) OrbitThrough(parent.Pos, s.Pos, w, StationCol.WithAlpha(0.5f), true);
            }
    }

    /// <summary>The circle round centre through p (level axis), when it's between 14 px and a few screens across.</summary>
    private void OrbitThrough(V3 centre, V3 p, float w, MapColor col, bool dashed)
    {
        var d = p - centre;
        double r = d.Length;
        if (r < 1) return;
        double depth = Cam.Depth(centre);
        if (depth < Cam.Near) return;
        float px = (float)(r * Cam.PixelsPerMetre(depth));
        if (px < 14 * u || px > 8 * Math.Max(view.W, view.H)) return;
        if (P(centre, out var cx, out var cy) && !OnView(cx, cy, px + 10)) return;
        var a = d / r;
        var e = new V3(0, 1, 0).Cross(a);
        e = e.LengthSq < 1e-6 ? new V3(1, 0, 0) : e.Normalized;
        int n = (px > 300 ? 128 : 64) * Detail;
        V3 last = default;
        for (int i = 0; i <= n; i++)
        {
            double t = i * 2 * Math.PI / n;
            var q = centre + a * (r * Math.Cos(t)) + e * (r * Math.Sin(t));
            if (i > 0 && (!dashed || i % 3 != 0)) Seg(last, q, w, col);
            last = q;
        }
    }
}
