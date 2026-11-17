using System;
using System.Collections.Generic;

namespace SDX2_SystemMap;

/// <summary>
/// Curved legs. A leg bent through one point is a parabola (a quadratic Bézier with that point at its middle); through
/// several, a smooth (centripetal Catmull-Rom) curve. The ship flies it without stopping at the points: it turns its
/// drives partly sideways (a perpendicular burn) to bend its path, so the corner speed is limited by
/// <see cref="TurnShare"/> of its acceleration over the bend's radius. Game-free so it can be tested.
/// </summary>
public static class RouteCurve
{
    /// <summary>Share of the acceleration a bend may take sideways (the rest is left for speeding up or braking).</summary>
    public const double TurnShare = 0.6;

    /// <summary>The curve from a through the points to b, as a polyline (a and b included).</summary>
    public static List<V3> Build(V3 a, IList<V3> through, V3 b, int perSpan = 32)
    {
        var res = new List<V3>();
        if (through.Count == 1)
        {
            var c = through[0] * 2 - (a + b) * 0.5;
            int n = perSpan * 2;
            for (int i = 0; i <= n; i++)
            {
                double s = i / (double)n;
                res.Add(a * ((1 - s) * (1 - s)) + c * (2 * s * (1 - s)) + b * (s * s));
            }
            return res;
        }
        var pts = new List<V3> { a };
        pts.AddRange(through);
        pts.Add(b);
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            var p1 = pts[i]; var p2 = pts[i + 1];
            var p0 = i > 0 ? pts[i - 1] : p1 * 2 - p2;
            var p3 = i + 2 < pts.Count ? pts[i + 2] : p2 * 2 - p1;
            for (int k = 0; k < perSpan; k++) res.Add(CatmullRom(p0, p1, p2, p3, k / (double)perSpan));
        }
        res.Add(b);
        return res;
    }

    private static V3 CatmullRom(V3 p0, V3 p1, V3 p2, V3 p3, double u)
    {
        double K(V3 x, V3 y) => Math.Max(1e-3, Math.Sqrt(V3.Distance(x, y)));
        double t0 = 0, t1 = t0 + K(p0, p1), t2 = t1 + K(p1, p2), t3 = t2 + K(p2, p3);
        double t = t1 + (t2 - t1) * u;
        var a1 = p0 * ((t1 - t) / (t1 - t0)) + p1 * ((t - t0) / (t1 - t0));
        var a2 = p1 * ((t2 - t) / (t2 - t1)) + p2 * ((t - t1) / (t2 - t1));
        var a3 = p2 * ((t3 - t) / (t3 - t2)) + p3 * ((t - t2) / (t3 - t2));
        var b1 = a1 * ((t2 - t) / (t2 - t0)) + a2 * ((t - t0) / (t2 - t0));
        var b2 = a2 * ((t3 - t) / (t3 - t1)) + a3 * ((t - t1) / (t3 - t1));
        return b1 * ((t2 - t) / (t2 - t1)) + b2 * ((t - t1) / (t2 - t1));
    }

    /// <summary>Radius of the circle through three points (infinite when they're in a line).</summary>
    public static double Radius(V3 p, V3 q, V3 r)
    {
        var a = q - p; var b = r - q; var c = r - p;
        double cross = a.Cross(b).Length;
        return cross < 1e-9 * Math.Max(1, a.Length * b.Length) ? double.PositiveInfinity : a.Length * b.Length * c.Length / (2 * cross);
    }

    /// <summary>The fastest a bend of radius r can be flown with acceleration a (<see cref="TurnShare"/> of it sideways).</summary>
    public static double CornerSpeed(double r, double a) => double.IsInfinity(r) || a <= 0 ? double.PositiveInfinity : Math.Sqrt(TurnShare * a * r);

    /// <summary>
    /// Flip and burn: the fastest the ship can pass corner b of a → b → c and still turn onto the new course fairly
    /// accurately. Swinging the velocity round by burning the change at full thrust cuts the corner by v²·sin²(θ/2)/2a
    /// (θ the turn), which must stay within <paramref name="tol"/> (the point's reach radius), and the turn, starting
    /// v²·sin(θ/2)/a before the point, must fit in half the shorter leg. A straight-on point: no limit.
    /// </summary>
    public static double TurnSpeed(V3 a, V3 b, V3 c, double accel, double tol, double maxSpeed)
    {
        if (accel <= 0) return 0;
        double sh = Trajectory.SinHalfTurn(a, b, c);
        if (sh < 1e-6) return maxSpeed;
        double room = Math.Min(V3.Distance(a, b), V3.Distance(b, c)) / 2;
        return Math.Min(maxSpeed, Math.Min(Math.Sqrt(2 * accel * tol) / sh, Math.Sqrt(accel * room / sh)));
    }

    /// <summary>A subset of the polyline for the autopilot: about every <paramref name="step"/> metres, ends left out.</summary>
    public static List<V3> Thin(IList<V3> curve, double step)
    {
        var res = new List<V3>();
        double run = 0;
        for (int i = 1; i + 1 < curve.Count; i++)
        {
            run += V3.Distance(curve[i - 1], curve[i]);
            if (run >= step) { res.Add(curve[i]); run = 0; }
        }
        return res;
    }
}
