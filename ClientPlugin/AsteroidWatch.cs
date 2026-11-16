using System;
using System.Collections.Generic;
using VRageMath;

namespace SDX2_SystemMap;

/// <summary>
/// Looks for procedural asteroids with Flip and Burn's own scanner (the one behind its "WARNING: Asteroid"), and no
/// further than F&amp;B itself looks: 30 s at your current speed (SDX2's plugin rules: no information the game wouldn't
/// give you). Once a second it scans that far along the route still ahead (ship → dodge points → the next detour or the
/// stop point) and along your velocity.
///  - Anything found is shown on screen (Warning) and as a red marker on the maps.
///  - While the autopilot flies, an asteroid on the line gets a suggested fly-by point beside it (AsteroidDodge, Changed).
///    Nothing is sent by itself: you press Ctrl+Shift+M to send the route with it (SidePreview.SteerRound).
/// </summary>
internal static class AsteroidWatch
{
    public const int MaxDodges = 4;
    /// <summary>Flip and Burn's own asteroid warning looks 30 s along your velocity; so do we.</summary>
    public const double Lookahead = 30;
    private const double Interval = 1.0;

    private struct Rock { public Vector3D Center; public double Radius; public int Seed; public double Dist; public bool OnRoute; }

    private static readonly List<Rock> rocks = new List<Rock>();
    private static readonly List<V3> dodges = new List<V3>();
    private static readonly List<int> dodgeSeeds = new List<int>();
    private static double nextScan;
    private static int pathRevision = -1, pathNext = -1;

    /// <summary>Dodge points still ahead, in flight order (sent to the PB as pass waypoints).</summary>
    public static IList<V3> Dodges => dodges;
    /// <summary>Set when a way round was suggested and hasn't been sent yet (cleared by TakeChanged once it's sent).</summary>
    public static bool Changed { get; private set; }
    public static bool TakeChanged() { bool c = Changed; Changed = false; return c; }

    /// <summary>The nearest asteroid found (distance in m, NaN if none), and whether it lies on the route or only on the velocity line.</summary>
    public static double NearestDistance { get; private set; } = double.NaN;
    public static bool NearestOnRoute { get; private set; }
    public static bool NearestDodged { get; private set; }

    public static void Reset()
    {
        rocks.Clear(); dodges.Clear(); dodgeSeeds.Clear(); nextScan = 0; pathRevision = -1; pathNext = -1;
        NearestDistance = double.NaN; Changed = false;
    }

    public static void Update(double now, MapSnapshot snap, RouteTracker tracker, bool engaged)
    {
        if (now < nextScan) return;
        nextScan = now + Interval;
        var path = tracker.Path;
        // a new plan or the next leg: the old dodges belonged to the old line
        if (path == null || tracker.Revision != pathRevision || path.Next != pathNext || !engaged)
        {
            if (dodges.Count > 0) { dodges.Clear(); dodgeSeeds.Clear(); }
            pathRevision = tracker.Revision; pathNext = path?.Next ?? -1;
        }
        rocks.Clear();
        if (!snap.HasShip || !FlipAndBurnLink.CanScanAsteroids) { Publish(snap); return; }

        var ship = snap.ShipPos;
        V3? legEnd = null;
        if (path != null) legEnd = path.DetoursAhead > 0 ? path.Waypoints[path.Next].Pos : RouteExport.StopPoint(path, out _);

        // drop dodge points we've gone by
        while (dodges.Count > 0)
        {
            var d = dodges[0];
            var after = dodges.Count > 1 ? dodges[1] : legEnd ?? d;
            if (V3.Distance(d, ship) < RouteExport.PassArrive || (d - ship).Dot(after - ship) <= 0) { dodges.RemoveAt(0); dodgeSeeds.RemoveAt(0); }
            else break;
        }

        int slot = 0;
        double reach = snap.ShipVel.Length * Lookahead; // F&B's own window
        if (legEnd.HasValue && reach >= 100)
        {
            // ship -> dodge 1 -> ... -> leg end
            var a = ship; double along = 0;
            for (int i = 0; i <= dodges.Count && slot < 3 && along < reach; i++)
            {
                var b = i < dodges.Count ? dodges[i] : legEnd.Value;
                double len = Math.Min(V3.Distance(a, b), reach - along);
                if (Scan(a, b - a, len, slot++, out var rock))
                {
                    rock.Dist = along + Vector3D.Distance(ToD(a), rock.Center);
                    rock.OnRoute = true;
                    rocks.Add(rock);
                    if (engaged && dodges.Count < MaxDodges && !dodgeSeeds.Contains(rock.Seed))
                    {
                        var pp = AsteroidDodge.PassPoint(a, b, ToV(rock.Center), rock.Radius);
                        if (pp.HasValue)
                        {
                            dodges.Insert(i, pp.Value); dodgeSeeds.Insert(i, rock.Seed);
                            Changed = true;
                            Plugin.Log("asteroid on the route: way round suggested");
                            break; // the lines changed: scan them again next time
                        }
                    }
                }
                along += V3.Distance(a, b);
                a = b;
            }
        }
        // where we're actually going
        var v = snap.ShipVel;
        if (v.Length > 100 && Scan(ship, v, reach, 3, out var ahead))
        {
            ahead.Dist = Vector3D.Distance(ToD(ship), ahead.Center);
            bool known = false;
            foreach (var r in rocks) if (r.Seed == ahead.Seed) known = true;
            if (!known) rocks.Add(ahead);
        }
        Publish(snap);
    }

    private static bool Scan(V3 from, V3 dir, double length, int slot, out Rock rock)
    {
        rock = default;
        if (length < 100) return false;
        if (!FlipAndBurnLink.FindAsteroid(ToD(from), ToD(dir), length, out var box, out var seed, slot)) return false;
        // F&B finishes the cell it's in, so a hit can lie past the end of the line
        var ray = new RayD(ToD(from), Vector3D.Normalize(ToD(dir)));
        var t = ray.Intersects(box);
        if (!t.HasValue || t.Value > length) return false;
        rock = new Rock { Center = box.Center, Radius = box.HalfExtents.Length(), Seed = seed };
        return true;
    }

    private static void Publish(MapSnapshot snap)
    {
        snap.Asteroids.Clear(); snap.Dodges.Clear();
        NearestDistance = double.NaN; NearestOnRoute = false; NearestDodged = false;
        foreach (var r in rocks)
        {
            snap.Asteroids.Add(new MapSnapshot.Marker { Name = "Asteroid", Pos = ToV(r.Center), Color = MapScene.HazardCol });
            if (double.IsNaN(NearestDistance) || r.Dist < NearestDistance)
            {
                NearestDistance = r.Dist; NearestOnRoute = r.OnRoute; NearestDodged = dodgeSeeds.Contains(r.Seed) && !Changed; // sent to the PB
            }
        }
        snap.Dodges.AddRange(dodges);
    }

    private static Vector3D ToD(V3 v) => new Vector3D(v.X, v.Y, v.Z);
    private static V3 ToV(Vector3D v) => new V3(v.X, v.Y, v.Z);
}
