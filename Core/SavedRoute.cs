using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace SDX2_SystemMap;

/// <summary>
/// A route as it was planned when a ship was sent off on autopilot: where the plan has the ship at each moment (the
/// burn, coast and brake of every leg, sampled), stamped with the time it started. The map's "ship routes" layer
/// shows your other ships from these alone: no ship is looked at after the route is sent, so the marker is where
/// the ship should be by its plan, not where it is.
/// </summary>
public sealed class SavedRoute
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    /// <summary>Samples a leg (its burn, coast and brake are each a curve in time).</summary>
    public const int PerLeg = 24;

    public long ShipId;
    public string Ship = "", Target = "", World = "";
    /// <summary>The route as a share code (ShareCode), made when it was sent; "" for none.</summary>
    public string Code = "";
    /// <summary>Who shared it with you (a player's name; "" = one of your own ships, or not known).</summary>
    public string By = "";
    /// <summary>Read from someone's share code, not sent by you.</summary>
    public bool Shared => ShipId < 0;
    /// <summary>When the route was sent, on the world's clock (seconds; see the plugin's stamp).</summary>
    public double Start;
    /// <summary>Seconds after Start and where the plan has the ship then; the first is where it set off, the last the stop.</summary>
    public readonly List<(double T, V3 P)> Track = new List<(double, V3)>();

    /// <summary>The plan's flight time (s).</summary>
    public double Eta => Track.Count > 0 ? Track[Track.Count - 1].T : 0;
    public V3 End => Track.Count > 0 ? Track[Track.Count - 1].P : default(V3);

    /// <summary>The plan of a route being sent now. Null when it has no time plan (no drives seen, so no ETA).</summary>
    public static SavedRoute From(RoutePath path, long shipId, string ship, string world, double stamp)
    {
        if (path == null || path.Legs.Count == 0) return null;
        var r = new SavedRoute { ShipId = shipId, Ship = Clean(ship), Target = Clean(path.TargetName), World = Clean(world), Start = stamp };
        double t0 = 0;
        r.Track.Add((0, path.Legs[0].Start));
        foreach (var leg in path.Legs)
        {
            double eta = leg.Eta;
            if (double.IsNaN(eta) || double.IsInfinity(eta) || eta <= 0) return null;
            for (int k = 1; k < PerLeg; k++)
            {
                double t = eta * k / PerLeg;
                r.Track.Add((t0 + t, leg.PointAt(leg.AlongAt(t))));
            }
            t0 += eta;
            r.Track.Add((t0, leg.Target));
        }
        return r;
    }

    /// <summary>Where the plan has the ship this long after it set off (at the stop once the time is up); i = the
    /// first track point still ahead (Track.Count when arrived).</summary>
    public V3 At(double elapsed, out int i)
    {
        i = Track.Count;
        if (Track.Count == 0) return default(V3);
        if (elapsed <= 0) { i = Math.Min(1, Track.Count); return Track[0].P; }
        for (int k = 1; k < Track.Count; k++)
        {
            if (elapsed >= Track[k].T) continue;
            double span = Track[k].T - Track[k - 1].T;
            i = k;
            return span <= 0 ? Track[k].P : V3.Lerp(Track[k - 1].P, Track[k].P, (elapsed - Track[k - 1].T) / span);
        }
        return End;
    }

    public static string Clean(string s) => (s ?? "").Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ').Trim();
    private static string N(double d) => d.ToString("R", Inv);
    private static double D(string s) => double.TryParse(s, NumberStyles.Float, Inv, out var d) ? d : double.NaN;

    /// <summary>One line: ship id | start | world | ship | target | t,x,y,z;... | share code | shared by</summary>
    public string ToLine()
    {
        var sb = new StringBuilder();
        sb.Append(ShipId.ToString(Inv)).Append('|').Append(N(Start)).Append('|').Append(Clean(World)).Append('|').Append(Clean(Ship)).Append('|').Append(Clean(Target)).Append('|');
        for (int i = 0; i < Track.Count; i++)
        {
            if (i > 0) sb.Append(';');
            var (t, p) = Track[i];
            sb.Append(Math.Round(t, 3).ToString(Inv)).Append(',').Append(Math.Round(p.X).ToString(Inv)).Append(',').Append(Math.Round(p.Y).ToString(Inv)).Append(',').Append(Math.Round(p.Z).ToString(Inv));
        }
        sb.Append('|').Append(Clean(Code)).Append('|').Append(Clean(By));
        return sb.ToString();
    }

    public static SavedRoute Parse(string line)
    {
        var f = (line ?? "").Split('|');
        if (f.Length < 6 || f.Length > 8 || !long.TryParse(f[0], NumberStyles.Integer, Inv, out long id)) return null;
        var r = new SavedRoute { ShipId = id, Start = D(f[1]), World = f[2], Ship = f[3], Target = f[4] };
        if (double.IsNaN(r.Start)) return null;
        if (f.Length > 6) r.Code = f[6];
        if (f.Length > 7) r.By = f[7];
        foreach (var s in f[5].Split(';'))
        {
            var q = s.Split(',');
            if (q.Length != 4) return null;
            double t = D(q[0]), x = D(q[1]), y = D(q[2]), z = D(q[3]);
            if (double.IsNaN(t) || double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(z)) return null;
            r.Track.Add((t, new V3(x, y, z)));
        }
        return r.Track.Count >= 2 ? r : null;
    }

    public static List<SavedRoute> Load(string path)
    {
        var res = new List<SavedRoute>();
        try
        {
            if (File.Exists(path))
                foreach (var line in File.ReadAllLines(path)) { var r = Parse(line.Trim()); if (r != null) res.Add(r); }
        }
        catch { }
        return res;
    }

    public static void Save(string path, IEnumerable<SavedRoute> routes)
    {
        var sb = new StringBuilder();
        foreach (var r in routes) sb.Append(r.ToLine()).Append('\n');
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, sb.ToString());
    }
}
