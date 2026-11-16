using System;
using System.Collections.Generic;

namespace SDX2_SystemMap;

/// <summary>A point the route goes through: a detour round a world, or a waypoint you added (User).</summary>
public struct Waypoint
{
    public string Name;
    /// <summary>Where the route goes (for your own waypoints: short of a station, or above a world).</summary>
    public V3 Pos;
    public SystemData.World Around;
    /// <summary>Added by you in the route panel (picked in F&amp;B's list), not planned round a world.</summary>
    public bool User;
    /// <summary>For your own waypoints: the point you picked in F&amp;B's list.</summary>
    public V3 Pick;
}

/// <summary>
/// Finds detour waypoints so every leg of a route keeps a clearance above the surface of each world it would pass.
/// Around a world the route follows a polygon drawn around the clearance circle in the plane of the leg and the world's
/// centre: tangent from the start, at most 60° of arc per corner, tangent into the end. Each new leg is checked again
/// (for other worlds), up to a few levels deep.
/// </summary>
public static class DetourPlanner
{
    public const int MaxWaypoints = 8;
    private const int MaxDepth = 4;
    private const double MaxArc = Math.PI / 3;
    /// <summary>Detours sit a bit outside the wanted clearance so the new legs pass the check with room to spare.</summary>
    public const double Slack = 1.15;

    /// <summary>Worlds a route never avoids: one whose clearance zone the destination or the start is inside (landing on it, leaving it).</summary>
    public static bool Skip(SystemData.World w, V3 start, V3 target, double clearance = RoutePlan.DefaultClearance) =>
        V3.Distance(w.Pos, target) < w.Radius + clearance || V3.Distance(w.Pos, start) < w.Radius + clearance;

    public static List<Waypoint> Plan(V3 start, V3 target, string targetName, double clearance = RoutePlan.DefaultClearance)
    {
        var pts = new List<(V3 Pos, SystemData.World Around)>();
        var skip = new HashSet<SystemData.World>();
        foreach (var w in SystemData.Worlds) if (Skip(w, start, target, clearance)) skip.Add(w);
        Solve(start, target, clearance, skip, 0, pts);
        var list = new List<Waypoint>();
        for (int i = 0; i < pts.Count; i++)
            list.Add(new Waypoint { Pos = pts[i].Pos, Around = pts[i].Around, Name = "Detour " + (i + 1) + " of " + pts.Count + " - " + targetName });
        return list;
    }

    /// <summary>Adds the waypoints between a and b (in order) to output.</summary>
    private static void Solve(V3 a, V3 b, double clearance, HashSet<SystemData.World> skip, int depth, List<(V3, SystemData.World)> output)
    {
        if (depth >= MaxDepth || output.Count >= MaxWaypoints) return;
        // the first world along a→b that the line comes too close to
        SystemData.World hit = null;
        double hitT = double.MaxValue;
        foreach (var w in SystemData.Worlds)
        {
            if (skip.Contains(w)) continue;
            double c = V3.SegmentDistance(a, b, w.Pos, out var t);
            if (c - w.Radius >= clearance) continue;
            if (V3.Distance(a, w.Pos) < w.Radius + clearance * 0.99 || V3.Distance(b, w.Pos) < w.Radius + clearance * 0.99) continue; // an end is inside: can't go round
            if (t < hitT) { hitT = t; hit = w; }
        }
        if (hit == null) return;

        var corners = Around(a, b, hit.Pos, hit.Radius + clearance * Slack);
        if (corners.Count == 0) return;
        var prev = a;
        foreach (var p in corners)
        {
            Solve(prev, p, clearance, skip, depth + 1, output);
            if (output.Count >= MaxWaypoints) return;
            output.Add((p, hit));
            prev = p;
        }
        Solve(prev, b, clearance, skip, depth + 1, output);
    }

    /// <summary>The corners of the shortest path from a to b around a sphere (centre c, radius r), in the a-c-b plane.</summary>
    public static List<V3> Around(V3 a, V3 b, V3 c, double r)
    {
        var res = new List<V3>();
        V3 ca = a - c, cb = b - c;
        double da = ca.Length, db = cb.Length;
        if (da <= r || db <= r) return res;
        var e1 = ca / da;
        var n = ca.Cross(cb);
        if (n.Length < 1e-6 * da * db)
        {
            // a, c and b in a line: go round over the top (or sideways when the line is vertical)
            n = e1.Cross(new V3(0, 1, 0));
            if (n.Length < 1e-6) n = e1.Cross(new V3(1, 0, 0));
        }
        n = n.Normalized;
        var e2 = n.Cross(e1);
        double thetaB = Math.Atan2(cb.Dot(e2), cb.Dot(e1));          // 0..π by the choice of n
        if (thetaB < 0) thetaB += 2 * Math.PI;
        double betaA = Math.Acos(r / da), betaB = thetaB - Math.Acos(r / db);
        double gap = betaB - betaA;
        if (gap <= 1e-9) return res;                                  // the straight line already clears it
        int k = (int)Math.Ceiling(gap / MaxArc);
        double step = gap / k, dist = r / Math.Cos(step / 2);
        for (int i = 0; i < k; i++)
        {
            double ang = betaA + (i + 0.5) * step;
            res.Add(c + (e1 * Math.Cos(ang) + e2 * Math.Sin(ang)) * dist);
        }
        return res;
    }
}

/// <summary>
/// The whole route: ship → the detour waypoints still ahead → destination, as Flip and Burn legs. Each leg after the
/// first starts from rest (F&amp;B stops at each point you pick; an autopilot may do better).
/// </summary>
public sealed class RoutePath
{
    public string TargetName;
    public V3 Start, Target;
    /// <summary>All detours of the plan (passed ones too); Next = the first still ahead.</summary>
    public readonly List<Waypoint> Waypoints = new List<Waypoint>();
    public int Next;
    public readonly List<RoutePlan> Legs = new List<RoutePlan>();
    public double Distance, Eta, MaxAccel, MaxSpeed, Clearance;

    public RoutePlan First => Legs[0];
    public RoutePlan Final => Legs[Legs.Count - 1];
    /// <summary>Points still ahead before the destination (detours and your own waypoints).</summary>
    public int DetoursAhead => Waypoints.Count - Next;
    /// <summary>Your own waypoints still ahead.</summary>
    public int UserAhead { get { int n = 0; for (int i = Next; i < Waypoints.Count; i++) if (Waypoints[i].User) n++; return n; } }
    public bool HasDetours => Waypoints.Count > 0;

    public static RoutePath Make(string name, V3 start, V3 target, V3 velocity, double maxAccel, double maxSpeed,
        IList<Waypoint> waypoints, int next, double clearance = RoutePlan.DefaultClearance, double brakeAccel = 0)
    {
        var p = new RoutePath { TargetName = name, Start = start, Target = target, Next = Math.Max(0, next), MaxAccel = maxAccel, MaxSpeed = maxSpeed, Clearance = clearance };
        if (waypoints != null) p.Waypoints.AddRange(waypoints);
        p.Next = Math.Min(p.Next, p.Waypoints.Count);
        var from = start;
        var v = velocity;
        for (int i = p.Next; i < p.Waypoints.Count; i++)
        {
            p.Legs.Add(RoutePlan.Make(p.Waypoints[i].Name, from, p.Waypoints[i].Pos, v, maxAccel, maxSpeed, clearance, brakeAccel));
            from = p.Waypoints[i].Pos;
            v = V3.Zero;
        }
        p.Legs.Add(RoutePlan.Make(name, from, target, v, maxAccel, maxSpeed, clearance, brakeAccel));
        double eta = 0;
        foreach (var l in p.Legs) { p.Distance += l.Distance; eta += l.Eta; }
        p.Eta = eta;
        return p;
    }

    /// <summary>The leg start times from now (seconds), for T+ marks along later legs.</summary>
    public double LegStartTime(int leg)
    {
        double t = 0;
        for (int i = 0; i < leg && i < Legs.Count; i++) t += double.IsNaN(Legs[i].Eta) ? 0 : Legs[i].Eta;
        return t;
    }

    /// <summary>All points of the route, for framing the camera.</summary>
    public V3[] Points()
    {
        var pts = new List<V3> { Start };
        for (int i = Next; i < Waypoints.Count; i++) pts.Add(Waypoints[i].Pos);
        pts.Add(Target);
        return pts.ToArray();
    }
}
