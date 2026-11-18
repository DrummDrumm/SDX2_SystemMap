using System;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Planet;
using Sandbox.Game.GameSystems;
using VRage.Game.ModAPI;
using VRageMath;

namespace SDX2_SystemMap;

/// <summary>
/// Warns before the route takes the ship into a planet's or moon's gravity well (where the drives must also hold the
/// ship up, and a ship left alone falls). Once a second it asks the game's own gravity at points along the route still
/// ahead (ship → points → destination) and at the ship, and puts the result on the snapshot for the map
/// (<see cref="MapSnapshot.GravityEntry"/>, <see cref="MapSnapshot.InGravity"/>). While the autopilot flies it also
/// says so on screen: once when the route is found to enter a well, again about a minute before it gets there, and
/// when the ship goes in.
/// </summary>
internal static class GravityWatch
{
    private const double Interval = 1.0;
    /// <summary>Gravity under this (m/s²) is counted as none (0.05 g, about where the game's wells end).</summary>
    public const double Threshold = 0.49;
    private const int MaxSamples = 400;
    private const double MinStep = 2e3;

    private static double nextScan;
    private static string warnedRoute, warnedSoon;
    private static bool wasIn;

    public static void Reset()
    {
        nextScan = 0; nextWells = 0; warnedRoute = warnedSoon = null; wasIn = false;
        foreach (var w in SystemData.Worlds) { w.Well = w.SurfaceG = w.Falloff = w.Hills = w.Atmosphere = 0; } // another session may have other planets
    }

    private const double WellsInterval = 10;
    private static double nextWells;

    /// <summary>
    /// Each loaded planet's or moon's gravity well edge (the game's own limit, where it falls to 0.05 g) onto the
    /// world it is, so a route to that world stops outside it (<see cref="RouteExport.WellStop"/>).
    /// </summary>
    private static void ReadWells()
    {
        bool changed = false;
        try
        {
            foreach (var planet in MyPlanets.GetPlanets())
            {
                var c = planet.PositionComp.GetPosition();
                var pos = new V3(c.X, c.Y, c.Z);
                var gravity = planet.Components.Get<MyGravityProviderComponent>();
                if (gravity == null) continue;
                foreach (var w in SystemData.Worlds)
                    if (!w.IsObstacle && !w.IsStar && V3.Distance(w.Pos, pos) < w.Radius * 0.5)
                    {
                        double was = w.Well, now = gravity.GravityLimit;
                        w.Well = now;
                        if (Math.Abs(now - was) > 1000) changed = true;
                        // its gravity by the game's formula (for how low the ship can safely go), and its atmosphere
                        if (gravity is IMySphericalNaturalGravityComponent sph)
                        {
                            double g = sph.Intensity * 9.81;
                            if (Math.Abs(g - w.SurfaceG) > 0.01 || Math.Abs(sph.MaxRadius - w.Hills) > 100) changed = true;
                            w.SurfaceG = g; w.Falloff = sph.Falloff; w.Hills = sph.MaxRadius;
                        }
                        double atmo = planet.HasAtmosphere ? planet.AtmosphereRadius : 0;
                        if (Math.Abs(atmo - w.Atmosphere) > 100) changed = true;
                        w.Atmosphere = atmo;
                    }
            }
        }
        catch (Exception e) { Plugin.LogThrottled("gravity wells: " + e.Message); }
        // routes go round worlds just outside their wells: a route planned before a well was known is planned again
        // (not while the autopilot flies it: that one changes only when you re-plan)
        if (changed && SidePreview.Path != null && !AutopilotLink.Engaged(Plugin.Now)) SidePreview.Replan();
    }

    public static void Update(double now, MapSnapshot snap, RoutePath path, bool engaged)
    {
        if (now < nextScan) return;
        nextScan = now + Interval;
        if (now >= nextWells) { nextWells = now + WellsInterval; ReadWells(); }
        snap.GravityEntry = null; snap.GravityName = null; snap.GravityAlong = 0; snap.InGravity = false; snap.GravityG = 0;
        if (!snap.HasShip) { wasIn = false; return; }
        try
        {
            double gHere = G(snap.ShipPos);
            snap.InGravity = gHere >= Threshold;
            snap.GravityG = gHere / 9.81;
            if (snap.InGravity) snap.GravityName = Nearest(snap.ShipPos);
            if (snap.InGravity && !wasIn && engaged)
                SidePreview.Notify("Entered " + snap.GravityName + "'s gravity well (" + Fmt.Num(snap.GravityG, "0.00") + " g): the drives hold the ship up now.", false);
            wasIn = snap.InGravity;
            if (path == null) { warnedRoute = warnedSoon = null; return; }

            // along the route still ahead: the first point in a well (outside one now); in one now, the next well after leaving it
            var pts = path.Points();
            pts[0] = snap.ShipPos;
            double total = 0;
            for (int i = 1; i < pts.Length; i++) total += V3.Distance(pts[i - 1], pts[i]);
            if (total <= 0) return;
            double step = Math.Max(MinStep, total / MaxSamples), along = 0;
            bool inside = snap.InGravity, left = !snap.InGravity;
            for (int i = 1; i < pts.Length; i++)
            {
                var a = pts[i - 1]; var b = pts[i];
                double len = V3.Distance(a, b);
                for (double s = step; s <= len + step * 0.5; s += step)
                {
                    double t = Math.Min(1, s / len);
                    var p = V3.Lerp(a, b, t);
                    bool g = G(p) >= Threshold;
                    if (!g) { left = true; inside = false; continue; }
                    if (inside || !left) continue;
                    snap.GravityEntry = p; snap.GravityAlong = along + t * len;
                    string name = Nearest(p);
                    if (!snap.InGravity) snap.GravityName = name;
                    snap.GravityEntryName = name;
                    Warn(now, snap, path, engaged, name);
                    return;
                }
                along += len;
            }
            warnedSoon = null;
        }
        catch (Exception e) { Plugin.LogThrottled("gravity watch: " + e.Message); }
    }

    private static void Warn(double now, MapSnapshot snap, RoutePath path, bool engaged, string name)
    {
        if (!engaged) return;
        string key = path.TargetName + "|" + name;
        if (warnedRoute != key)
        {
            warnedRoute = key;
            SidePreview.Notify("Route enters " + name + "'s gravity well in " + Fmt.Dist(snap.GravityAlong) + ".", false);
        }
        double speed = snap.ShipVel.Length;
        if (speed > 1 && snap.GravityAlong / speed < 60 && warnedSoon != key)
        {
            warnedSoon = key;
            SidePreview.Notify("Entering " + name + "'s gravity well in about " + Fmt.Time(snap.GravityAlong / speed) + ".", false);
        }
    }

    private static double G(V3 p) => MyGravityProviderSystem.CalculateNaturalGravityInPoint(new Vector3D(p.X, p.Y, p.Z)).Length();

    /// <summary>The planet or moon nearest the point (by distance to its surface).</summary>
    private static string Nearest(V3 p)
    {
        string best = "a planet"; double bd = double.MaxValue;
        foreach (var w in SystemData.Worlds)
        {
            if (w.IsObstacle || w.Name == "Sol" || w.IsAsteroid) continue;
            double d = V3.Distance(p, w.Pos) - w.Radius;
            if (d < bd) { bd = d; best = w.Name; }
        }
        return best;
    }
}
