using System;
using System.Collections.Generic;

namespace SDX2_SystemMap;

/// <summary>Turning a click in the full map into a point for a waypoint (game-free so it can be tested).</summary>
public static class RouteClick
{
    /// <summary>
    /// Where in a chain (ship, waypoints…, destination) a new point adds the least distance. Returns the index into the
    /// waypoints between the ends: 0 = first after the ship, chain.Count-2 = just before the destination.
    /// </summary>
    public static int InsertIndex(IList<V3> chain, V3 p)
    {
        int best = 0; double bestAdd = double.MaxValue;
        for (int i = 0; i + 1 < chain.Count; i++)
        {
            double add = V3.Distance(chain[i], p) + V3.Distance(p, chain[i + 1]) - V3.Distance(chain[i], chain[i + 1]);
            if (add < bestAdd - 1e-6) { bestAdd = add; best = i; }
        }
        return best;
    }

    /// <summary>Where a ray from <paramref name="eye"/> meets the horizontal plane at height y (false if it doesn't, ahead).</summary>
    public static bool PlaneHit(V3 eye, V3 dir, double y, out V3 hit)
    {
        hit = default;
        if (Math.Abs(dir.Y) < 1e-6) return false;
        double t = (y - eye.Y) / dir.Y;
        if (t <= 0) return false;
        hit = eye + dir * t;
        return true;
    }

    /// <summary>The point of segment ab nearest the ray (eye, unit dir).</summary>
    public static V3 NearestOnSegment(V3 eye, V3 dir, V3 a, V3 b)
    {
        var u = b - a; var w = a - eye;
        double uu = u.Dot(u), ud = u.Dot(dir), uw = u.Dot(w), dw = dir.Dot(w);
        double den = uu - ud * ud;
        double s = den < 1e-12 * Math.Max(1, uu) ? 0 : (ud * dw - uw) / den;
        s = Math.Max(0, Math.Min(1, s));
        return a + u * s;
    }

    /// <summary>
    /// The world point for a click at (sx, sy): on the route's line when the click is within tolPx pixels of it,
    /// otherwise on the horizontal plane at the height of the nearest route point. False when neither works.
    /// </summary>
    public static bool Place(OrbitCamera cam, V3[] route, float sx, float sy, float tolPx, out V3 pos, out bool onRoute)
    {
        pos = default; onRoute = false;
        if (route == null || route.Length < 2) return false;
        var eye = cam.Eye; var dir = cam.Ray(sx, sy);
        double bestPx = double.MaxValue; V3 best = default; bool any = false;
        for (int i = 0; i + 1 < route.Length; i++)
        {
            var q = NearestOnSegment(eye, dir, route[i], route[i + 1]);
            if (!cam.Project(q, out float qx, out float qy, out _)) continue;
            double px = Math.Sqrt((qx - sx) * (qx - sx) + (qy - sy) * (qy - sy));
            if (px < bestPx) { bestPx = px; best = q; any = true; }
        }
        if (!any) return false;
        if (bestPx <= tolPx) { pos = best; onRoute = true; return true; }
        // a nearly flat view meets the plane far away: keep it within reach of the route
        return PlaneHit(eye, dir, best.Y, out pos) && V3.Distance(eye, pos) < 20 * Math.Max(1, V3.Distance(eye, best));
    }
}
