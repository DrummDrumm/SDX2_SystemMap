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

    /// <summary>Above this distance from a world's centre a point is clear of its ground (hills reach a few % of the radius).</summary>
    public static double Top(SystemData.World w) => w.Radius * (w.IsObstacle ? 1.05 : 1.1);

    /// <summary>A pick of the world itself (its centre), not of a point on it: a route there stops at the clearance and never goes round it.</summary>
    public static bool IsCore(SystemData.World w, V3 p) => V3.Distance(p, w.Pos) < w.Radius * 0.5;

    /// <summary>A point on or just above a world's ground (a base, a ship sitting on it), but not the world itself.</summary>
    public static bool OnGround(SystemData.World w, V3 p) => V3.Distance(p, w.Pos) < Top(w) && !IsCore(w, p);

    /// <summary>
    /// How near w's centre the line a→b may come: the world's radius plus the clearance, but never more than the lower
    /// end's own height (a ship in low orbit is routed round near its own height, dipping to 80% of it, not skipped). NaN when an end is on
    /// the ground or in the world (that leg lands on it or leaves it).
    /// </summary>
    public static double Floor(SystemData.World w, V3 a, V3 b, double clearance)
    {
        double low = Math.Min(V3.Distance(a, w.Pos), V3.Distance(b, w.Pos));
        if (low < Top(w)) return double.NaN;
        if (w.IsObstacle) return w.Radius;
        return Math.Min(w.Radius + clearance, w.Radius + (low - w.Radius) * 0.8);
    }

    /// <summary>Where the corners round w go: a little outside its clearance (an obstacle: outside its sphere).</summary>
    private static double CornerRadius(SystemData.World w, double clearance) => w.IsObstacle ? w.Radius * Slack : w.Radius + clearance * Slack;

    /// <summary>Just outside a station's or grid's sphere, straight out from its centre through p (toward the target when p is at the centre).</summary>
    public static V3 Out(SystemData.World w, V3 p, V3 target)
    {
        var d = p - w.Pos;
        if (d.Length < 1) d = target - w.Pos;
        return w.Pos + d.Normalized * (w.Radius * Slack);
    }

    /// <summary>The point straight above p at the clearance: where a route climbs to first (or comes down from).</summary>
    public static V3 Lift(SystemData.World w, V3 p, double clearance) => w.Pos + (p - w.Pos).Normalized * (w.Radius + clearance);

    /// <summary>
    /// The detours from start to target round the worlds and, when given, <paramref name="obstacles"/> (your own and
    /// your faction's grids, the known stations). One the route ends at is never gone round; one it starts at (or near) is
    /// left straight outward first, then gone round when the way ahead passes through it.
    /// </summary>
    public static List<Waypoint> Plan(V3 start, V3 target, string targetName, double clearance = RoutePlan.DefaultClearance, IList<SystemData.World> obstacles = null)
    {
        var all = new List<SystemData.World>(SystemData.RouteWorlds);
        if (obstacles != null) all.AddRange(obstacles);
        var pts = new List<(V3 Pos, SystemData.World Around)>();
        var skip = new HashSet<SystemData.World>();
        // a world picked itself is never gone round; one you take off from or land on is, from a point above the ground
        V3 s = start, e = target; SystemData.World upFrom = null, downTo = null, outOf = null;
        var sol = SystemData.SolZone;
        // inside Sol's no-fly sphere (and not flying to Sol): straight out of it, after any base you start at
        bool outOfSol = V3.Distance(start, sol.Pos) < sol.Radius && V3.Distance(target, sol.Pos) >= sol.Radius;
        if (V3.Distance(target, sol.Pos) < sol.Radius) skip.Add(sol);
        foreach (var w in all)
        {
            if (w == sol) continue;
            if (w.IsObstacle)
            {
                bool atStart = V3.Distance(start, w.Pos) < Top(w), atEnd = V3.Distance(target, w.Pos) < Top(w);
                // a station or base you start at or near: out of its sphere first, then round it; the one you fly to: not gone round
                if (atStart && !atEnd && outOf == null) { outOf = w; s = Out(w, start, target); continue; }
                if (atStart || atEnd) skip.Add(w);
                continue;
            }
            if (IsCore(w, start) || IsCore(w, target) || OnGround(w, start) && OnGround(w, target)) { skip.Add(w); continue; }
            if (OnGround(w, start)) { upFrom = w; s = Lift(w, start, clearance); }
            if (OnGround(w, target)) { downTo = w; e = Lift(w, target, clearance); }
        }
        V3 solOut = outOfSol ? Out(sol, s, target) : s;
        Solve(all, solOut, e, clearance, skip, 0, pts);
        if (outOfSol) pts.Insert(0, (solOut, sol));
        // climb / come down straight when the way goes round that world, or the direct line would graze it
        // leaving a station or base: straight out of it first when the way goes round it or the direct line heads in through it
        if (outOf != null && (pts.Exists(q => q.Around == outOf) || V3.SegmentDistance(start, pts.Count > 0 ? pts[0].Pos : e, outOf.Pos, out _) < Math.Min(V3.Distance(start, outOf.Pos), outOf.Radius) * 0.95))
            pts.Insert(0, (s, outOf));
        if (upFrom != null && (pts.Exists(q => q.Around == upFrom) || V3.SegmentDistance(start, pts.Count > 0 ? pts[0].Pos : e, upFrom.Pos, out _) < upFrom.Radius * 1.02))
            pts.Insert(0, (s, upFrom));
        if (downTo != null && (pts.Exists(q => q.Around == downTo) || V3.SegmentDistance(pts.Count > 0 ? pts[pts.Count - 1].Pos : start, target, downTo.Pos, out _) < downTo.Radius * 1.02))
            pts.Add((e, downTo));
        var list = new List<Waypoint>();
        for (int i = 0; i < pts.Count; i++)
            list.Add(new Waypoint { Pos = pts[i].Pos, Around = pts[i].Around, Name = "Detour " + (i + 1) + " of " + pts.Count + " - " + targetName });
        return list;
    }

    /// <summary>Overlapping spheres become one that holds both (a base of several grids is gone round as one). Stations aren't merged with grids.</summary>
    public static void Merge(List<SystemData.World> s)
    {
        for (bool again = true; again;)
        {
            again = false;
            for (int i = 0; i < s.Count && !again; i++)
                for (int j = i + 1; j < s.Count && !again; j++)
                {
                    var a = s[i]; var b = s[j];
                    double d = V3.Distance(a.Pos, b.Pos);
                    if (d >= a.Radius + b.Radius || a.IsStation != b.IsStation) continue; // a station keeps its own rule
                    SystemData.World m;
                    if (d + b.Radius <= a.Radius) m = a;
                    else if (d + a.Radius <= b.Radius) m = b;
                    else
                    {
                        double r = (d + a.Radius + b.Radius) / 2;
                        var c = a.Pos + (b.Pos - a.Pos) * ((r - a.Radius) / d);
                        m = SystemData.Obstacle(a.Radius >= b.Radius ? a.Name : b.Name, c, r, a.IsStation);
                    }
                    s[i] = m; s.RemoveAt(j); again = true;
                }
        }
    }

    /// <summary>Adds the waypoints between a and b (in order) to output.</summary>
    private static void Solve(List<SystemData.World> all, V3 a, V3 b, double clearance, HashSet<SystemData.World> skip, int depth, List<(V3, SystemData.World)> output)
    {
        if (depth >= MaxDepth || output.Count >= MaxWaypoints) return;
        // the first world along a→b that the line comes too close to
        SystemData.World hit = null;
        double hitT = double.MaxValue;
        foreach (var w in all)
        {
            if (skip.Contains(w)) continue;
            double floor = Floor(w, a, b, clearance);
            if (double.IsNaN(floor)) continue; // an end is on the ground: can't go round
            double c = V3.SegmentDistance(a, b, w.Pos, out var t);
            if (c >= floor) continue;
            if (t < hitT) { hitT = t; hit = w; }
        }
        if (hit == null) return;

        // round it a little outside the clearance, or at the lower end's own height when that's nearer
        double low = Math.Min(V3.Distance(a, hit.Pos), V3.Distance(b, hit.Pos));
        var corners = Around(a, b, hit.Pos, Math.Min(CornerRadius(hit, clearance), low * (1 - 1e-4)));
        if (corners.Count == 0) return;
        var prev = a;
        foreach (var p in corners)
        {
            Solve(all, prev, p, clearance, skip, depth + 1, output);
            if (output.Count >= MaxWaypoints) return;
            output.Add((p, hit));
            prev = p;
        }
        Solve(all, prev, b, clearance, skip, depth + 1, output);
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
/// The whole route: ship → the detour waypoints still ahead → destination. Flip and burn (the default): a leg per
/// point, each ending at the speed the next turn allows (the ship slows at each direction change just enough to turn). Parabolic (<see cref="Curved"/>):
/// one leg that curves through all the points, flown without stopping. Either way a leg can be bent through a point of
/// your own (by dragging its flip point in the full map): bends, keyed by the leg's end (a waypoint's or the
/// destination's name).
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
    /// <summary>Parabolic: the points are flown through on one curved leg, not stopped at.</summary>
    public bool Curved;

    public RoutePlan First => Legs[0];
    public RoutePlan Final => Legs[Legs.Count - 1];
    /// <summary>Points still ahead before the destination (detours and your own waypoints).</summary>
    public int DetoursAhead => Waypoints.Count - Next;
    /// <summary>Your own waypoints still ahead.</summary>
    public int UserAhead { get { int n = 0; for (int i = Next; i < Waypoints.Count; i++) if (Waypoints[i].User) n++; return n; } }
    public bool HasDetours => Waypoints.Count > 0;

    public static RoutePath Make(string name, V3 start, V3 target, V3 velocity, double maxAccel, double maxSpeed,
        IList<Waypoint> waypoints, int next, double clearance = RoutePlan.DefaultClearance, double brakeAccel = 0,
        bool curved = false, IDictionary<string, V3> bends = null)
    {
        var p = new RoutePath { TargetName = name, Start = start, Target = target, Next = Math.Max(0, next), MaxAccel = maxAccel, MaxSpeed = maxSpeed, Clearance = clearance };
        if (waypoints != null) p.Waypoints.AddRange(waypoints);
        p.Next = Math.Min(p.Next, p.Waypoints.Count);
        var from = start;
        var v = velocity;
        if (curved)
        {
            var through = new List<V3>();
            for (int i = p.Next; i < p.Waypoints.Count; i++) through.Add(p.Waypoints[i].Pos);
            if (BendAhead(bends, name, from, target, out var bend))
            {
                var chain = new List<V3> { from }; chain.AddRange(through); chain.Add(target);
                through.Insert(RouteClick.InsertIndex(chain, bend), bend);
            }
            p.Curved = true;
            p.Legs.Add(RoutePlan.Make(name, from, target, v, maxAccel, maxSpeed, clearance, brakeAccel, through));
        }
        else
        {
            for (int i = p.Next; i < p.Waypoints.Count; i++)
            {
                var to = p.Waypoints[i].Pos;
                var after = i + 1 < p.Waypoints.Count ? p.Waypoints[i + 1].Pos : target;
                bool bent = BendAhead(bends, p.Waypoints[i].Name, from, to, out var b);
                // slow only enough to turn onto the next leg there (within the point's reach radius)
                double turn = RouteCurve.TurnSpeed(bent ? b : from, to, after, maxAccel, RouteExport.ArriveFor(from, to, after), maxSpeed);
                var leg = RoutePlan.Make(p.Waypoints[i].Name, from, to, v, maxAccel, maxSpeed, clearance, brakeAccel, bent ? new[] { b } : null, turn);
                p.Legs.Add(leg);
                from = to;
                v = (after - to).Normalized * leg.ExitSpeed;
            }
            p.Legs.Add(RoutePlan.Make(name, from, target, v, maxAccel, maxSpeed, clearance, brakeAccel,
                BendAhead(bends, name, from, target, out var bt) ? new[] { bt } : null));
        }
        double eta = 0;
        foreach (var l in p.Legs) { p.Distance += l.Distance; eta += l.Eta; }
        p.Eta = eta;
        return p;
    }

    /// <summary>The bend of the leg ending at <paramref name="end"/>, while it's still ahead of <paramref name="from"/>.</summary>
    private static bool BendAhead(IDictionary<string, V3> bends, string end, V3 from, V3 to, out V3 bend)
    {
        bend = default;
        if (bends == null || end == null || !bends.TryGetValue(end, out bend)) return false;
        return V3.Distance(from, to) > V3.Distance(bend, to) && V3.Distance(from, bend) > 1;
    }

    /// <summary>The leg ending at <paramref name="end"/> (a waypoint's or the destination's name), or -1.</summary>
    public int LegTo(string end) => Legs.FindIndex(l => l.TargetName == end);

    /// <summary>The points the ship flies through ahead: each leg's curve (when bent) and its end. The last is the destination.</summary>
    public List<V3> Ahead()
    {
        var res = new List<V3>();
        foreach (var l in Legs)
        {
            if (l.Curved) res.AddRange(l.Curve.GetRange(1, l.Curve.Count - 2));
            res.Add(l.Target);
        }
        return res;
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
