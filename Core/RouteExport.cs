using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SDX2_SystemMap;

/// <summary>
/// The route as text for the programmable-block autopilot (PB Autopilot\Protogen Systems - Autopilot.cs), written into the PB's Custom
/// Data under a [SDX2 Route] section (the rest of the Custom Data is kept). The PB answers in its own [SDX2 Autopilot]
/// section. Format v2, one key=value per line, invariant culture:
/// <code>
/// [SDX2 Route]
/// version=2
/// id=42                         changes on every send: the PB acts once per id
/// command=engage                engage (fly it now) | update (new points, keep flying) | stop | load (store only)
/// target=Tycho Station          the destination picked in F&amp;B's list
/// dest=x,y,z                    where it is
/// stop=station                  why the last wp isn't dest: station (3 km short) | world (clearance above it) | point
/// accel=12.5                    F&amp;B max acceleration of the ship when sent (m/s², 0 = unknown)
/// maxspeed=50000                speed cap (m/s): the route panel's, at most F&amp;B's
/// thrust=1                      share of the drives' thrust the autopilot may use burning (route panel's burn cap)
/// brake=1                       share it may use braking (route panel's brake cap; missing = same as thrust)
/// clearance=100000              wanted clearance above world surfaces (m)
/// eta=5400                      planned total seconds (stopping at each detour)
/// wp=x,y,z,arrive,kind,name     in flight order; kind = pass | detour | waypoint | target; arrive = reach radius (m)
/// border=x,y,z,fromDx,toDx      Nexus borders on the route
/// </code>
/// pass = fly by without slowing (asteroid dodge), detour / waypoint (yours) = stop and turn, target = the final stop (always last).
/// Coordinates are world GPS metres.
/// </summary>
public static class RouteExport
{
    public const string Header = "[SDX2 Route]";
    public const string StatusHeader = "[SDX2 Autopilot]";
    public const int Version = 2;
    /// <summary>A route to a station stops this far short of it; routes past one keep <see cref="StationAvoid"/> from it.</summary>
    public const double StationStandoff = 3000, StationAvoid = 8000, StationRange = 20e3;
    public const double PassArrive = 1000, DetourArrive = 2000, TargetArrive = 500;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Where the autopilot should stop at the end of the route: see <see cref="StopNear"/>.</summary>
    public static V3 StopPoint(RoutePath p, out string why) =>
        StopNear(p.Waypoints.Count > 0 ? p.Waypoints[p.Waypoints.Count - 1].Pos : p.Start, p.Target, p.Clearance, out why);

    /// <summary>Where to stop when flying from <paramref name="from"/> to <paramref name="to"/>: short of a station
    /// (F&amp;B's 5 km exclusion), at the clearance above a world (planet or moon), or at the point itself.</summary>
    public static V3 StopNear(V3 from, V3 to, double clearance, out string why)
    {
        why = "point";
        if (SystemData.StationNear(to, StationRange) != null)
        {
            var d = to - from;
            if (d.Length <= StationStandoff * 1.5) return to;
            why = "station";
            // near or on a world: from the side away from it (above the station), else from your side
            var w = WorldUnder(to, clearance);
            return w != null ? to + (to - w.Pos).Normalized * StationStandoff : to - d.Normalized * StationStandoff;
        }
        var sol = SystemData.SolZone;
        if (V3.Distance(to, sol.Pos) < sol.Radius)
        {
            why = "sol";
            return sol.Pos + (V3.Distance(from, sol.Pos) > 1 ? (from - sol.Pos).Normalized : new V3(1, 0, 0)) * sol.Radius;
        }
        SystemData.World best = null; double bestAlt = double.MaxValue;
        foreach (var w in SystemData.Worlds)
        {
            if (w.IsStar) continue;
            double alt = V3.Distance(to, w.Pos) - w.Radius;
            // a point on a world you're already near is flown to; the world itself (its centre) never is
            if (alt < clearance && alt < bestAlt && (V3.Distance(from, w.Pos) > w.Radius + clearance || DetourPlanner.IsCore(w, to))) { best = w; bestAlt = alt; }
        }
        if (best == null) return to;
        why = "world";
        // at the clearance on your side; already lower than that: where you are, but never below its ground
        double r = Math.Max(DetourPlanner.Top(best), Math.Min(best.Radius + clearance, V3.Distance(from, best.Pos)));
        return best.Pos + (from - best.Pos).Normalized * r;
    }

    /// <summary>The world (not Sol) a point is on or within the clearance of, nearest the ground; null in open space.</summary>
    public static SystemData.World WorldUnder(V3 p, double clearance)
    {
        SystemData.World best = null; double bestAlt = clearance;
        foreach (var w in SystemData.Worlds)
        {
            if (w.IsStar) continue;
            double alt = V3.Distance(p, w.Pos) - w.Radius;
            if (alt < bestAlt) { best = w; bestAlt = alt; }
        }
        return best;
    }

    /// <summary>For a stop above a station on a world: a point off to your side at the stop's height, flown to first
    /// when the straight line to the stop would pass the station closer than <see cref="StationStandoff"/>
    /// (coming up from the world's side). Null when the straight line is clear.</summary>
    public static V3? ApproachPoint(V3 from, V3 station, V3 stop)
    {
        var up = stop - station; double h = up.Length; if (h < 1) return null;
        up = up / h;
        if (V3.SegmentDistance(from, stop, station, out _) >= StationStandoff * 0.9) return null;
        var rel = from - station;
        var side = rel - up * rel.Dot(up);
        if (side.Length < 1) side = up.Cross(Math.Abs(up.Y) < 0.9 ? new V3(0, 1, 0) : new V3(1, 0, 0));
        return station + side.Normalized * StationAvoid + up * h;
    }

    /// <summary>The route section. <paramref name="passes"/> are dodge points flown by before the next waypoint.</summary>
    public static string Build(RoutePath p, int id, string command = "load", IList<V3> passes = null, double thrust = 1, double brake = -1)
    {
        var sb = new StringBuilder();
        sb.Append(Header).Append('\n');
        sb.Append("version=").Append(Version).Append('\n');
        sb.Append("id=").Append(id).Append('\n');
        sb.Append("command=").Append(command).Append('\n');
        sb.Append("target=").Append(Clean(p.TargetName)).Append('\n');
        sb.Append("dest=").Append(Xyz(p.Target)).Append('\n');
        var stop = StopPoint(p, out var why);
        sb.Append("stop=").Append(why).Append('\n');
        sb.Append("accel=").Append(N(p.MaxAccel)).Append('\n');
        sb.Append("maxspeed=").Append(N(p.MaxSpeed)).Append('\n');
        sb.Append("thrust=").Append(Math.Round(Math.Max(0.01, Math.Min(1, thrust)), 3).ToString("0.###", Inv)).Append('\n');
        sb.Append("brake=").Append(Math.Round(Math.Max(0.01, Math.Min(1, brake > 0 ? brake : thrust)), 3).ToString("0.###", Inv)).Append('\n');
        sb.Append("clearance=").Append(N(p.Clearance)).Append('\n');
        sb.Append("eta=").Append(double.IsNaN(p.Eta) ? "0" : N(Math.Round(p.Eta))).Append('\n');
        if (passes != null) for (int i = 0; i < passes.Count; i++) Wp(sb, passes[i], PassArrive, "pass", "Asteroid dodge " + (i + 1));
        if (p.Legs.Count == 1 && p.Final.Curved)
            Curve(sb, p.Final, stop, p.TargetName); // parabolic: one curve through all the points
        else
            for (int i = p.Next; i < p.Waypoints.Count; i++)
            {
                var prev = i > 0 ? p.Waypoints[i - 1].Pos : p.Start;
                var next = i + 1 < p.Waypoints.Count ? p.Waypoints[i + 1].Pos : stop;
                int leg = i - p.Next;
                if (leg < p.Legs.Count && p.Legs[leg].Curved) Curve(sb, p.Legs[leg], p.Waypoints[i].Pos, p.Waypoints[i].Name);
                Wp(sb, p.Waypoints[i].Pos, ArriveFor(prev, p.Waypoints[i].Pos, next), p.Waypoints[i].User ? "waypoint" : "detour", p.Waypoints[i].Name);
            }
        if (p.Legs.Count > 1 && p.Final.Curved) Curve(sb, p.Final, stop, p.TargetName);
        if (why == "station")
        {
            var last = p.Waypoints.Count > 0 ? p.Waypoints[p.Waypoints.Count - 1].Pos : p.Start;
            var ap = ApproachPoint(last, p.Target, stop);
            if (ap.HasValue) Wp(sb, ap.Value, ArriveFor(last, ap.Value, stop), p.Curved ? "curve" : "detour", "Approach " + p.TargetName);
        }
        Wp(sb, stop, TargetArrive, "target", p.TargetName);
        foreach (var leg in p.Legs)
            foreach (var x in leg.Crossings)
                sb.Append("border=").Append(Xyz(x.Pos)).Append(',').Append(x.From.Dx).Append(',').Append(x.To.Dx).Append('\n');
        return sb.ToString();
    }

    /// <summary>Most curve points sent per leg: the PB steers from one to the next.</summary>
    public const int CurvePoints = 40;

    /// <summary>
    /// A curved leg as "curve" points the autopilot flies through without stopping (turning its drives partly sideways
    /// to bend its path), ending short of where it stops (<paramref name="end"/>), so none lies beyond the stop.
    /// </summary>
    private static void Curve(StringBuilder sb, RoutePlan leg, V3 end, string name)
    {
        double step = Math.Max(500, leg.Distance / CurvePoints), keep = V3.Distance(end, leg.Target) * 1.5 + 1000;
        var pts = RouteCurve.Thin(leg.Curve, step);
        for (int i = 0; i < pts.Count; i++)
            if (V3.Distance(pts[i], leg.Target) > keep && V3.Distance(pts[i], end) > step * 0.5)
                Wp(sb, pts[i], Math.Max(100, Math.Min(DetourArrive, step * 0.25)), "curve", "Curving to " + name);
    }

    /// <summary>A waypoint's reach radius: <see cref="DetourArrive"/>, but at most a quarter of the shorter leg either side
    /// (min 100 m), so a short detour (leaving a station or base) is flown, not counted as reached where you start.</summary>
    public static double ArriveFor(V3 prev, V3 at, V3 next) =>
        Math.Max(100, Math.Min(DetourArrive, 0.25 * Math.Min(V3.Distance(prev, at), V3.Distance(at, next))));

    /// <summary>A stop order with no route (the PB lets go of the controls).</summary>
    public static string BuildStop(int id) => Header + "\nversion=" + Version + "\nid=" + id + "\ncommand=stop\n";

    private static void Wp(StringBuilder sb, V3 p, double arrive, string kind, string name) =>
        sb.Append("wp=").Append(Xyz(p)).Append(',').Append(N(arrive)).Append(',').Append(kind).Append(',').Append(Clean(name)).Append('\n');

    private static string Xyz(V3 p) => N(p.X) + "," + N(p.Y) + "," + N(p.Z);
    private static string N(double v) => Math.Round(v, 2).ToString("0.##", Inv);
    private static string Clean(string s) => (s ?? "").Replace('\n', ' ').Replace('\r', ' ').Replace('[', '(').Replace(']', ')');

    /// <summary>Replaces (or appends) a section in a block's Custom Data, keeping everything else.</summary>
    public static string Merge(string customData, string section, string header = Header)
    {
        var lines = (customData ?? "").Replace("\r\n", "\n").Split('\n');
        var outp = new StringBuilder();
        bool inside = false, written = false;
        foreach (var line in lines)
        {
            var t = line.Trim();
            if (t.StartsWith("[") && t.EndsWith("]"))
            {
                inside = t == header;
                if (inside) { outp.Append(section); if (!section.EndsWith("\n")) outp.Append('\n'); outp.Append('\n'); written = true; continue; }
            }
            if (!inside) outp.Append(line).Append('\n');
        }
        var res = outp.ToString().TrimEnd('\n');
        if (!written) res = (res.Length > 0 ? res + "\n\n" : "") + section;
        return res.TrimEnd('\n') + "\n";
    }

    /// <summary>True if the Custom Data has a [SDX2 Route] section (the autopilot script makes one).</summary>
    public static bool HasSection(string customData)
    {
        foreach (var line in (customData ?? "").Split('\n')) if (line.Trim() == Header) return true;
        return false;
    }

    private static IEnumerable<(string K, string V)> Section(string customData, string header)
    {
        bool inside = false;
        foreach (var raw in (customData ?? "").Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("[") && line.EndsWith("]")) { inside = line == header; continue; }
            int eq = line.IndexOf('=');
            if (inside && eq > 0) yield return (line.Substring(0, eq), line.Substring(eq + 1));
        }
    }

    /// <summary>Reads a route section back (the same parsing the autopilot script does). False if missing or another version.</summary>
    public static bool Parse(string customData, out Parsed r)
    {
        r = new Parsed();
        bool any = false;
        foreach (var (k, v) in Section(customData, Header))
        {
            any = true;
            switch (k)
            {
                case "version": r.Version = (int)D(v); break;
                case "id": r.Id = (int)D(v); break;
                case "command": r.Command = v; break;
                case "target": r.Target = v; break;
                case "dest": { var f = v.Split(','); if (f.Length == 3) r.Dest = new V3(D(f[0]), D(f[1]), D(f[2])); break; }
                case "stop": r.Stop = v; break;
                case "accel": r.Accel = D(v); break;
                case "maxspeed": r.MaxSpeed = D(v); break;
                case "thrust": r.Thrust = D(v); break;
                case "brake": r.Brake = D(v); break;
                case "clearance": r.Clearance = D(v); break;
                case "eta": r.Eta = D(v); break;
                case "wp":
                {
                    var f = v.Split(new[] { ',' }, 6);
                    if (f.Length == 6) r.Points.Add((new V3(D(f[0]), D(f[1]), D(f[2])), D(f[3]), f[4], f[5]));
                    break;
                }
                case "border":
                {
                    var f = v.Split(',');
                    if (f.Length == 5) r.Borders.Add((new V3(D(f[0]), D(f[1]), D(f[2])), f[3], f[4]));
                    break;
                }
            }
        }
        return any && r.Version == Version && (r.Points.Count > 0 || r.Command == "stop");
    }

    /// <summary>Reads the autopilot's own status section; null if there's none.</summary>
    public static Status ParseStatus(string customData)
    {
        Status s = null;
        foreach (var (k, v) in Section(customData, StatusHeader))
        {
            s ??= new Status();
            switch (k)
            {
                case "state": s.State = v; break;
                case "ack": s.Ack = (int)D(v); break;
                case "wp": { var f = v.Split('/'); if (f.Length == 2) { s.Wp = (int)D(f[0]); s.Count = (int)D(f[1]); } break; }
                case "dist": s.Dist = D(v); break;
                case "speed": s.Speed = D(v); break;
                case "eta": s.Eta = D(v); break;
                case "msg": s.Msg = v; break;
                case "target": s.Target = v; break;
            }
        }
        return s;
    }

    private static double D(string s) => double.TryParse(s, NumberStyles.Float, Inv, out var d) ? d : 0;

    public sealed class Parsed
    {
        public int Version, Id;
        public string Command = "load", Target = "", Stop = "point";
        public V3 Dest;
        public double Accel, MaxSpeed, Clearance, Eta, Thrust = 1, Brake = -1;
        public readonly List<(V3 Pos, double Arrive, string Kind, string Name)> Points = new List<(V3, double, string, string)>();
        public readonly List<(V3 Pos, string From, string To)> Borders = new List<(V3, string, string)>();
    }

    /// <summary>What the PB reports: state = idle | flying | arrived | override | stopped | error.</summary>
    public sealed class Status
    {
        public string State = "idle", Msg = "", Target = "";
        public int Ack, Wp, Count;
        public double Dist, Speed, Eta;
        public bool Flying => State == "flying";
    }
}

/// <summary>
/// Dodging an asteroid that lies on the leg ahead: one fly-by point beside it, level with its centre, far enough out that
/// the straight lines in and out keep <c>radius + margin</c> clear.
/// </summary>
public static class AsteroidDodge
{
    public const double Margin = 2000;

    /// <summary>The pass point, or null when the leg misses it anyway, the asteroid is behind, or the leg ends at it
    /// (you're flying to the asteroid: the autopilot stops short instead).</summary>
    public static V3? PassPoint(V3 ship, V3 legEnd, V3 center, double radius, double margin = Margin)
    {
        double clear = radius + margin;
        if (V3.Distance(legEnd, center) < clear || V3.Distance(ship, center) < clear) return null;
        double miss = V3.SegmentDistance(ship, legEnd, center, out double t);
        if (miss >= clear || t <= 0 || t >= 1) return null;
        var dir = (legEnd - ship).Normalized;
        var closest = ship + (legEnd - ship) * t;
        var side = closest - center;
        if (side.Length < 1) side = dir.Cross(Math.Abs(dir.Y) < 0.9 ? new V3(0, 1, 0) : new V3(1, 0, 0));
        side = side.Normalized;
        // pass point beside the centre; the lines ship-P and P-end stay clear when P is out at clear/cos(half the bend)
        double da = V3.Distance(ship, center), db = V3.Distance(legEnd, center);
        double bend = Math.Max(Math.Asin(Math.Min(1, clear / da)), Math.Asin(Math.Min(1, clear / db)));
        return center + side * (clear / Math.Cos(Math.Min(bend, 1.2)));
    }
}
