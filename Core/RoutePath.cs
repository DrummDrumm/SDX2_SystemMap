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
    public static double Top(SystemData.World w) => w.IsObstacle ? w.Radius * 1.05 : w.Hills > w.Radius ? w.Hills : Math.Max(w.Radius * 1.1, w.TallHills);

    /// <summary>A pick of the world itself (its centre), not of a point on it: a route there stops at the clearance and never goes round it.</summary>
    public static bool IsCore(SystemData.World w, V3 p) => V3.Distance(p, w.Pos) < w.Radius * 0.5 && !AtGroundFacility(p);

    /// <summary>On a ground facility (a station deep in its world, like Hygeia's, is still flown to from above, not as the world itself).</summary>
    public static bool AtGroundFacility(V3 p)
    {
        foreach (var s in SystemData.Stations) if (s.Above > 0 && V3.Distance(s.Pos, p) < 100) return true;
        return false;
    }

    /// <summary>A point on or just above a world's ground (a base, a ship sitting on it), but not the world itself.</summary>
    public static bool OnGround(SystemData.World w, V3 p) => V3.Distance(p, w.Pos) < Top(w) && !IsCore(w, p);

    /// <summary>
    /// How near w's centre the line a→b may come: the world's radius plus the clearance, but never more than the lower
    /// end's own height (a ship in low orbit is routed round near its own height, dipping to 80% of it, not skipped). NaN when an end is on
    /// the ground or in the world (that leg lands on it or leaves it).
    /// </summary>
    public static double Floor(SystemData.World w, V3 a, V3 b, double clearance)
    {
        if (w.IsField) return Math.Min(V3.Distance(a, w.Pos), V3.Distance(b, w.Pos)) < w.Radius + FieldGap ? double.NaN : w.Radius + FieldGap;
        double low = Math.Min(V3.Distance(a, w.Pos), V3.Distance(b, w.Pos));
        if (low < Top(w)) return double.NaN;
        if (w.IsObstacle) return w.Radius;
        // a known gravity well: kept outside it (the corners go WellGap out, so the legs between pass with room)
        double keep = InWell(w) ? w.Radius + Clear(w, clearance) - WellGap * 0.2
            : NoWell(w) ? w.Radius + Clear(w, clearance) - SurfaceGap * 0.2 : w.Radius + clearance;
        return Math.Max(Top(w), Math.Min(keep, w.Radius + (low - w.Radius) * 0.8));
    }

    /// <summary>How far outside an asteroid field (the Trojans', the Greeks' sphere, the Belt's tube) a route keeps.</summary>
    public const double FieldGap = 25e3;

    /// <summary>How far outside a planet's or moon's gravity well (0.05 g edge) a route goes round it.</summary>
    public const double WellGap = 5000;

    /// <summary>
    /// The world has no gravity worth keeping clear of: an asteroid, or a loaded planet or moon whose well doesn't reach
    /// past its hills. Routes go as close as <see cref="SurfaceGap"/> over its hills.
    /// </summary>
    public static bool NoWell(SystemData.World w) => !w.IsObstacle && !w.IsField && !w.IsStar && (w.IsAsteroid || w.Well > 0 && !InWell(w));

    /// <summary>
    /// How far from w's centre the ship must stay to hold itself up with the margin, with no floor at the hills (for a
    /// stop above a base, which may sit lower than them): 0 when no gravity matters (or it holds at the ground), just
    /// outside the well when it can't hold anywhere in it or its thrust isn't known, and above any atmosphere. NaN when
    /// the world's gravity isn't known.
    /// </summary>
    public static double HoldRadius(SystemData.World w, double accel)
    {
        if (NoWell(w)) return 0;
        if (!InWell(w)) return double.NaN;
        double r;
        if (w.SurfaceG <= 0 || w.Hills <= 0 || w.Falloff <= 0 || !(accel > 0)) r = w.Well + RouteExport.WellMargin;
        else
        {
            double need = GravityMargin * w.SurfaceG;
            r = accel >= need ? 0 : w.Hills * Math.Pow(need / accel, 1 / w.Falloff);
            if (r >= w.Well) r = w.Well + RouteExport.WellMargin;
        }
        return w.Atmosphere > 0 ? Math.Max(r, w.Atmosphere + SurfaceGap) : r;
    }

    /// <summary>The world's gravity well is known (read from the game), and reaches past its hills.</summary>
    public static bool InWell(SystemData.World w) => !w.IsObstacle && w.Well > Top(w);

    /// <summary>
    /// How high over w's surface a route going round it (or climbing out of it, or coming down to it) stays, when the
    /// game has told us its well: as low as the ship can safely hold itself up there (<see cref="SafeRadius"/>, down to
    /// <see cref="SurfaceGap"/> over its highest hills), else <see cref="WellGap"/> outside the well (also when the
    /// ship's thrust isn't known). No gravity (an asteroid), or too little to reach past its hills:
    /// <see cref="SurfaceGap"/> over its hills. Gravity not known yet (the planet isn't loaded): <paramref name="clearance"/>
    /// (<see cref="RoutePlan.DefaultClearance"/>), to be safe until it is.
    /// </summary>
    public static double Clear(SystemData.World w, double clearance)
    {
        if (NoWell(w)) return Top(w) + SurfaceGap - w.Radius;
        if (!InWell(w)) return clearance;
        double well = Math.Max(w.Well, HillTop(w)) + WellGap, low = LowestRadius(w, ShipAccel);
        double r = double.IsNaN(low) || low >= w.Well ? well : low;
        // never into its atmosphere, thrust known or not
        if (w.Atmosphere > 0) r = Math.Max(r, w.Atmosphere + SurfaceGap);
        return r - w.Radius;
    }

    /// <summary>
    /// The lowest a route may go round w: where the ship can safely hold itself up (<see cref="SafeRadius"/>) and above
    /// its atmosphere (<see cref="SurfaceGap"/> over the top of it). NaN when the gravity or the ship's thrust isn't known.
    /// </summary>
    public static double LowestRadius(SystemData.World w, double accel)
    {
        double safe = SafeRadius(w, accel);
        return w.Atmosphere > 0 && !double.IsNaN(safe) ? Math.Max(safe, w.Atmosphere + SurfaceGap) : safe;
    }

    /// <summary>
    /// The ship's acceleration the route may count on to hold it up against a world's gravity, m/s²: its thrust at the
    /// autopilot's thrust setting over its current mass. 0 = not known (routes keep outside the wells). Set by the plugin
    /// before a route is planned.
    /// </summary>
    public static double ShipAccel;

    /// <summary>Thrust held in reserve against gravity: the ship needs this many times the pull to count a height as safe.</summary>
    public const double GravityMargin = 1.5;

    /// <summary>Closest a route comes to a world's highest hills when the ship's thrust can hold it there.</summary>
    public const double SurfaceGap = 3000;

    /// <summary>The world's gravity (m/s²) at r from its centre, by the game's formula; NaN when not known.</summary>
    public static double GravityAt(SystemData.World w, double r)
    {
        if (w.SurfaceG <= 0 || w.Hills <= 0) return double.NaN;
        if (w.Well > 0 && r > w.Well) return 0;
        return r <= w.Hills ? w.SurfaceG : w.SurfaceG * Math.Pow(r / w.Hills, -w.Falloff);
    }

    /// <summary>
    /// The lowest a ship with <paramref name="accel"/> (m/s²) may go round w: where its gravity is no more than the
    /// thrust over <see cref="GravityMargin"/>, and never closer than <see cref="SurfaceGap"/> over the highest hills.
    /// NaN when the world's gravity or the ship's thrust isn't known.
    /// </summary>
    public static double SafeRadius(SystemData.World w, double accel)
    {
        if (w.IsObstacle || w.SurfaceG <= 0 || w.Hills <= 0 || w.Falloff <= 0 || !(accel > 0)) return double.NaN;
        double need = GravityMargin * w.SurfaceG;
        double r = accel >= need ? w.Hills : w.Hills * Math.Pow(need / accel, 1 / w.Falloff);
        return Math.Max(r, w.Hills + SurfaceGap);
    }

    /// <summary>The ship can hold itself up at w's surface with the margin (false when either isn't known).</summary>
    public static bool HoldsAtSurface(SystemData.World w, double accel) =>
        w.SurfaceG > 0 && accel > 0 && accel >= GravityMargin * w.SurfaceG;

    /// <summary>Where the corners round w go: a little outside its clearance (an obstacle: outside its sphere; a known
    /// well: <see cref="WellGap"/> outside it, the legs between are tangent to that sphere so they never dip in).</summary>
    private static double CornerRadius(SystemData.World w, double clearance) =>
        w.IsField ? (w.Radius + FieldGap) * 1.01 : w.IsObstacle ? w.Radius * Slack : InWell(w) || NoWell(w) ? w.Radius + Clear(w, clearance) : w.Radius + clearance * Slack;

    /// <summary>Just outside a station's or grid's sphere, straight out from its centre through p (toward the target when p is at the centre).</summary>
    public static V3 Out(SystemData.World w, V3 p, V3 target)
    {
        var d = p - w.Pos;
        if (d.Length < 1) d = target - w.Pos;
        return w.Pos + d.Normalized * (w.Radius * Slack);
    }

    /// <summary>The point straight above p at the clearance: where a route climbs to first (or comes down from).</summary>
    public static V3 Lift(SystemData.World w, V3 p, double clearance) => w.Pos + (p - w.Pos).Normalized * (w.Radius + Clear(w, clearance));

    /// <summary>From p on w's ground the straight line to q (out past the clearance) dips into the hills.</summary>
    private static bool Creeps(SystemData.World w, V3 p, V3 q, double clearance) =>
        V3.Distance(q, w.Pos) > w.Radius + Clear(w, clearance) && V3.SegmentDistance(p, q, w.Pos, out _) < Top(w);

    /// <summary>Steepest a climb out of (or a descent to) a world leans from straight up, toward where the route goes.</summary>
    public const double MaxLean = Math.PI / 3;

    /// <summary>
    /// Where a route taking off from p (on w's ground) reaches its clearance: leaning toward <paramref name="toward"/>, at
    /// the point where the way on from there just skims the clearance sphere (a tangent), not straight up from p. The
    /// lean is held to <see cref="MaxLean"/> from straight up, so the climb still leaves the ground. A landing uses it the
    /// other way round (p the landing spot, toward where the route comes from).
    /// </summary>
    public static V3 Climb(SystemData.World w, V3 p, V3 toward, double clearance)
    {
        double c = w.Radius + Clear(w, clearance), r0 = Math.Max(1, V3.Distance(p, w.Pos));
        var u = (p - w.Pos).Normalized;
        var to = toward - w.Pos;
        double d = to.Length;
        var side = to - u * to.Dot(u);
        if (d <= c || side.Length < 1e-6 * Math.Max(1, d)) return Lift(w, p, clearance);
        var v = side.Normalized;
        // the tangent from 'toward' meets the sphere at angle (alpha - beta) from p's own up
        double alpha = Math.Atan2(to.Dot(v), to.Dot(u)), beta = Math.Acos(c / d), want = Math.Max(0, alpha - beta);
        // the lean limit: the climb's heading from straight up at p
        double Lean(double phi) => Math.Atan2(c * Math.Sin(phi), c * Math.Cos(phi) - r0);
        double lo = 0, hi = want;
        if (Lean(hi) > MaxLean)
            for (int i = 0; i < 30; i++) { double m = (lo + hi) / 2; if (Lean(m) > MaxLean) hi = m; else lo = m; }
        else lo = hi;
        return w.Pos + (u * Math.Cos(lo) + v * Math.Sin(lo)) * c;
    }

    /// <summary>
    /// The detours from start to target round the worlds and, when given, <paramref name="obstacles"/> (your own and
    /// your faction's grids, the known stations). One the route ends at is never gone round; one it starts at (or near) is
    /// left straight outward first, then gone round when the way ahead passes through it.
    /// </summary>
    public static List<Waypoint> Plan(V3 start, V3 target, string targetName, double clearance = RoutePlan.DefaultClearance, IList<SystemData.World> obstacles = null,
        ICollection<string> ignore = null, bool fields = false)
    {
        var all = new List<SystemData.World>(SystemData.RouteWorlds);
        if (obstacles != null) all.AddRange(obstacles);
        if (fields) all.AddRange(SystemData.Fields);
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
            if (ignore != null && w.Name != null && ignore.Contains(w.Name)) { skip.Add(w); continue; } // detours you removed: flown straight past
            if (w == sol) continue;
            // an asteroid field you start or end in can't be kept out of: flown through
            if (w.IsField) { if (V3.Distance(start, w.Pos) < w.Radius + FieldGap || V3.Distance(target, w.Pos) < w.Radius + FieldGap) skip.Add(w); continue; }
            if (w.IsObstacle)
            {
                bool atStart = V3.Distance(start, w.Pos) < Top(w), atEnd = V3.Distance(target, w.Pos) < Top(w);
                // a station or base you start at or near: out of its sphere first, then round it; the one you fly to: not gone round
                if (atStart && !atEnd && outOf == null) { outOf = w; s = Out(w, start, target); continue; }
                if (atStart || atEnd) skip.Add(w);
                continue;
            }
            if (IsCore(w, start) || IsCore(w, target) || OnGround(w, start) && OnGround(w, target)) { skip.Add(w); continue; }
            if (OnGround(w, start)) { upFrom = w; s = Climb(w, start, target, clearance); }
            if (OnGround(w, target)) { downTo = w; e = Climb(w, target, start, clearance); }
        }
        V3 solOut = outOfSol ? Out(sol, s, target) : s;
        Solve(all, solOut, e, clearance, skip, 0, pts);
        if (outOfSol) pts.Insert(0, (solOut, sol));
        // climb / come down straight when the way goes round that world, or the direct line would graze it
        // leaving a station or base: straight out of it first when the way goes round it or the direct line heads in through it
        if (outOf != null && (pts.Exists(q => q.Around == outOf) || V3.SegmentDistance(start, pts.Count > 0 ? pts[0].Pos : e, outOf.Pos, out _) < Math.Min(V3.Distance(start, outOf.Pos), outOf.Radius) * 0.95))
            pts.Insert(0, (s, outOf));
        // (a line that only grazes it is taken over its hills below, not up to the whole clearance)
        // (the climb leans toward the way on, so it's also kept when that way is out past the clearance and the straight
        // line from the ground would only creep up over the hills)
        if (upFrom != null && (pts.Exists(q => q.Around == upFrom) || Creeps(upFrom, start, pts.Count > 0 ? pts[0].Pos : e, clearance))) pts.Insert(0, (s, upFrom));
        if (downTo != null && (pts.Exists(q => q.Around == downTo) || Creeps(downTo, target, pts.Count > 0 ? pts[pts.Count - 1].Pos : s, clearance))) pts.Add((e, downTo));
        if (ignore != null && ignore.Count > 0) pts.RemoveAll(q => q.Around?.Name != null && ignore.Contains(q.Around.Name));
        // across the Belt: over or under its tube, not through it
        if (fields && (ignore == null || !ignore.Contains(SystemData.BeltField.Name))) pts = OverBelt(start, target, pts);
        // low over a world (taking off, landing, a hop between two points by its ground): over its hills, never into them
        pts = OverHills(all, start, target, pts, ignore);
        var list = new List<Waypoint>();
        for (int i = 0; i < pts.Count; i++)
            list.Add(new Waypoint { Pos = pts[i].Pos, Around = pts[i].Around, Name = "Detour " + (i + 1) + " of " + pts.Count + " - " + MapScene.ShortName(targetName) });
        return list;
    }

    /// <summary>How far from a world's centre a leg low over its ground is kept: over its highest hills (<see cref="Top"/>), 5% more.</summary>
    public static double HillTop(SystemData.World w) => !w.IsObstacle && (w.Hills > w.Radius || w.TallHills > w.Radius) ? Top(w) + SurfaceGap : Top(w) * 1.05;

    /// <summary>Largest angle (rad, round the world's centre) between points of a hop over the hills: the legs between stay over Top.</summary>
    private const double HillStep = 0.5;

    /// <summary>
    /// Each leg (start → points → target) that dips into a world's hill band (under <see cref="Top"/>, and lower than
    /// both its ends: not a straight climb or landing) gets points over it instead: straight up to <see cref="HillTop"/>
    /// from a low end, along the arc at that height, and down over the other end. Only the planets' radius was kept
    /// clear before (2% over it), and a hill (up to 6% on SDX2's moons) was flown into.
    /// </summary>
    private static List<(V3 Pos, SystemData.World Around)> OverHills(List<SystemData.World> all, V3 start, V3 target, List<(V3 Pos, SystemData.World Around)> pts, ICollection<string> ignore)
    {
        var res = new List<(V3 Pos, SystemData.World Around)>();
        V3 a = start;
        for (int i = 0; i <= pts.Count; i++)
        {
            V3 b = i < pts.Count ? pts[i].Pos : target;
            foreach (var w in all)
            {
                if (w.IsObstacle || w.IsField || IsCore(w, start) || IsCore(w, target)) continue;
                if (ignore != null && w.Name != null && ignore.Contains(w.Name)) continue; // you removed its detours
                double ra = V3.Distance(a, w.Pos), rb = V3.Distance(b, w.Pos);
                double dip = V3.SegmentDistance(a, b, w.Pos, out _);
                if (dip >= Top(w) || dip >= Math.Min(ra, rb) - 1) continue;
                double H = HillTop(w), ha = Math.Max(ra, H), hb = Math.Max(rb, H);
                var ua = (a - w.Pos).Normalized; var ub = (b - w.Pos).Normalized;
                double ang = Math.Acos(Math.Max(-1, Math.Min(1, ua.Dot(ub))));
                int n = Math.Max(1, (int)Math.Ceiling(ang / HillStep));
                for (int k = 0; k <= n; k++)
                {
                    if (k == 0 && ra >= H || k == n && rb >= H) continue; // that end is already over the hills
                    double t = (double)k / n;
                    res.Add((w.Pos + Arc(ua, ub, t) * (ha + (hb - ha) * t), w));
                }
                break; // one world per leg
            }
            if (i < pts.Count) res.Add(pts[i]);
            a = b;
        }
        return res;
    }

    /// <summary>Each leg (start, points, target) that crosses the Belt's tube goes over or under it instead.</summary>
    private static List<(V3 Pos, SystemData.World Around)> OverBelt(V3 start, V3 target, List<(V3 Pos, SystemData.World Around)> pts)
    {
        var res = new List<(V3 Pos, SystemData.World Around)>();
        V3 a = start;
        for (int i = 0; i <= pts.Count; i++)
        {
            V3 b = i < pts.Count ? pts[i].Pos : target;
            BeltLeg(a, b, 0, res);
            if (i < pts.Count) res.Add(pts[i]);
            a = b;
        }
        return res;
    }

    /// <summary>
    /// If a to b comes within <see cref="FieldGap"/> of the Belt's tube, the corners round it (added to res): where it comes
    /// nearest, the tube is near enough a straight pipe, so the leg is gone round its cross-section there (over or under,
    /// whichever is shorter), then each new leg checked again. Not when an end is in the Belt (it can't be kept out of).
    /// </summary>
    private static void BeltLeg(V3 a, V3 b, int depth, List<(V3 Pos, SystemData.World Around)> res)
    {
        var belt = SystemData.Belt;
        double m = belt.Minor + FieldGap;
        if (depth >= 3 || SystemData.BeltDistance(a) < m || SystemData.BeltDistance(b) < m) return;
        const int n = 128;
        double best = double.MaxValue, bt = 0;
        for (int k = 0; k <= n; k++)
        {
            double t = (double)k / n, d = SystemData.BeltDistance(a + (b - a) * t);
            if (d < best) { best = d; bt = t; }
        }
        double lo = Math.Max(0, bt - 1.0 / n), hi = Math.Min(1, bt + 1.0 / n);
        for (int k = 0; k < 40; k++)
        {
            double t1 = lo + (hi - lo) / 3, t2 = hi - (hi - lo) / 3;
            if (SystemData.BeltDistance(a + (b - a) * t1) < SystemData.BeltDistance(a + (b - a) * t2)) hi = t2; else lo = t1;
        }
        bt = (lo + hi) / 2; best = Math.Min(best, SystemData.BeltDistance(a + (b - a) * bt));
        if (best >= m) return;
        // the tube's centreline point nearest the crossing, and the pipe's direction there
        var up = belt.Normal.Normalized;
        var rel = a + (b - a) * bt - belt.Center;
        var flat = rel - up * rel.Dot(up);
        if (flat.Length < 1) return;
        var radial = flat.Normalized;
        var c0 = belt.Center + radial * belt.Major;
        var along = up.Cross(radial);
        V3 Flat(V3 q) => q - along * (q - c0).Dot(along);
        var corners = Around(Flat(a), Flat(b), c0, m * 1.01);
        if (corners.Count == 0) return;
        var prev = a;
        foreach (var c in corners)
        {
            BeltLeg(prev, c, depth + 1, res);
            res.Add((c, SystemData.BeltField));
            prev = c;
        }
        BeltLeg(prev, b, depth + 1, res);
    }

    /// <summary>The unit vector a share t of the way round from u to v (great circle; any way round when they're opposite).</summary>
    private static V3 Arc(V3 u, V3 v, double t)
    {
        double c = Math.Max(-1, Math.Min(1, u.Dot(v))), th = Math.Acos(c);
        if (th < 1e-9) return u;
        V3 perp = v - u * c;
        if (perp.Length < 1e-9) perp = Math.Abs(u.X) < 0.9 ? u.Cross(new V3(1, 0, 0)) : u.Cross(new V3(0, 1, 0));
        perp = perp.Normalized;
        return u * Math.Cos(th * t) + perp * Math.Sin(th * t);
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
