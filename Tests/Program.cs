using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using SDX2_SystemMap;
using SDX2_SystemMap.Tests;

/// <summary>Offline checks and preview renders. Run: dotnet run --project Tests [-- outDir]</summary>
internal static class Program
{
    private static int fails, checks;

    private static void Check(bool ok, string what)
    {
        checks++;
        if (!ok) { fails++; Console.WriteLine("FAIL  " + what); }
    }

    private static bool Near(double a, double b, double tol) => Math.Abs(a - b) <= tol;

    private static int Main(string[] args)
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string outDir = args.Length > 0 ? args[0] : Path.Combine(root, "previews");
        Directory.CreateDirectory(outDir);

        Sectors();
        Burns();
        Crossings();
        Camera();
        Labels();
        Detours();
        Tracker();
        Export();
        UserWaypoints();
        Limits();
        PanelInput();
        Trajectories();
        Render(outDir, "");
        MapTheme.Current = MapTheme.Tachi;
        Render(outDir, "-hud");
        MapTheme.Current = MapTheme.Unn;
        Render(outDir, "-unn");
        MapTheme.Current = MapTheme.Opa;
        Render(outDir, "-opa");
        MapTheme.Current = MapTheme.Classic;
        Themes();
        HoverOnTop();
        PoiMatching();
        WaypointClicks();
        TurnWithShip();

        Console.WriteLine($"{checks - fails}/{checks} checks passed");
        return fails == 0 ? 0 : 1;
    }

    private static void Sectors()
    {
        Check(SystemData.SectorAt(SystemData.FindWorld("Earth").Pos).Dx == "DX4", "Earth is in DX4");
        Check(SystemData.SectorAt(new V3(8.5e6, 0, 0)).Dx == "DX6", "on the Belt's ring: DX6");
        Check(SystemData.SectorAt(new V3(8.5e6, 600e3, 0)).Dx == "DX1", "above the Belt's tube: DX1");
        Check(SystemData.SectorAt(V3.Zero).Dx == "DX1", "Sol: DX1");
        Check(SystemData.SectorAt(SystemData.FindWorld("Ceres").Pos).Dx == "DX5", "Ceres sphere wins over the Belt torus");
        foreach (var s in SystemData.Stations)
            Check(SystemData.SectorAt(s.Pos).Dx != "", "station " + s.Name + " has a sector");
        Check(SystemData.SectorAt(SystemData.Stations[3].Pos).Dx == "DX6", "Tycho Station is in the Belt");
    }

    private static void Burns()
    {
        // 1,000 km from rest at 10 m/s²: peak sqrt(a·d) = 3,162 m/s, 316 s each way, flip at half
        var r = RoutePlan.Make("t", V3.Zero, new V3(1e6, 0, 0), V3.Zero, 10);
        Check(Near(r.PeakSpeed, 3162.28, 0.5), "peak speed from rest " + r.PeakSpeed);
        Check(Near(r.Eta, 632.46, 0.5), "ETA from rest " + r.Eta);
        Check(Near(r.FlipAt, 5e5, 1), "flip halfway from rest " + r.FlipAt);
        Check(Near(r.AlongAt(r.Eta), 1e6, 1) && Near(r.AlongAt(r.BurnTime), r.FlipAt, 1), "AlongAt follows the plan");

        // already closing at 1 km/s: more to brake, so the flip comes before half; (vp² − u²)/2a + vp²/2a = d
        r = RoutePlan.Make("t", V3.Zero, new V3(1e6, 0, 0), new V3(1000, 0, 0), 10);
        Check(r.FlipAt < 5e5 && Near((r.PeakSpeed * r.PeakSpeed - 1e6) / 20 + r.PeakSpeed * r.PeakSpeed / 20, 1e6, 1), "closing: flip before half, distances add up " + r.FlipAt);

        // long haul at 200 m/s²: sqrt(a·d) = 63 km/s, so capped at 50 km/s with a coast
        r = RoutePlan.Make("t", V3.Zero, new V3(2e7, 0, 0), V3.Zero, 200);
        Check(Near(r.PeakSpeed, 50000, 1e-6) && r.CoastTime > 0, "long haul coasts at 50 km/s");
        Check(Near(r.CoastAt, 6.25e6, 1) && Near(r.FlipAt, 1.375e7, 1), "coast and flip points");
        Check(Near(r.Eta, 250 + 150 + 250, 0.5), "long haul ETA = 2 burns + cruise " + r.Eta);
        Check(Near(r.AlongAt(400), 1.375e7, 1) && Near(r.AlongAt(r.Eta), 2e7, 1), "AlongAt through the coast");

        // too fast to stop
        r = RoutePlan.Make("t", V3.Zero, new V3(1e5, 0, 0), new V3(5000, 0, 0), 10);
        Check(r.TooFastToStop, "5 km/s into 100 km at 10 m/s² overshoots");

        // moving away first
        r = RoutePlan.Make("t", V3.Zero, new V3(1e6, 0, 0), new V3(-1000, 0, 0), 10);
        var still = RoutePlan.Make("t", V3.Zero, new V3(1e6, 0, 0), V3.Zero, 10);
        Check(r.Eta > still.Eta + 100, "moving away costs a turnaround");

        // unknown acceleration: ETA from closing speed only
        r = RoutePlan.Make("t", V3.Zero, new V3(1e6, 0, 0), new V3(1000, 0, 0), 0);
        Check(!r.HasBurnPlan && Near(r.Eta, 1000, 0.01), "no accel: d / v");
        r = RoutePlan.Make("t", V3.Zero, new V3(1e6, 0, 0), new V3(0, 0, 100), 0);
        Check(double.IsNaN(r.Eta) && Near(r.LateralSpeed, 100, 1e-6), "no accel and not closing: no ETA, drift shown");
    }

    private static void Crossings()
    {
        var earth = SystemData.FindWorld("Earth").Pos; var mars = SystemData.FindWorld("Mars").Pos;
        var r = RoutePlan.Make("Mars", earth + new V3(0, 300e3, 0), mars + new V3(0, 200e3, 0), V3.Zero, 10);
        Check(r.Crossings.Count >= 2 && r.Crossings[0].From.Dx == "DX4" && r.Crossings[r.Crossings.Count - 1].To.Dx == "DX2",
              "Earth to Mars: leaves DX4, arrives DX2 (" + string.Join(",", r.Crossings.ConvertAll(x => x.To.Dx)) + ")");
        Check(Near(V3.Distance(r.Crossings[0].Pos, SystemData.Sectors[2].Center), 750e3, 2000), "the DX4 border is found at its radius");

        // straight through Earth from one side of its sector to the other
        var through = RoutePlan.Make("x", earth + new V3(-600e3, 0, 0), earth + new V3(600e3, 0, 0), V3.Zero, 10);
        Check(through.Hazards.Exists(h => h.World.Name == "Earth" && h.Blocked), "a line through Earth is blocked");
        var toEarth = RoutePlan.Make("x", earth + new V3(-600e3, 0, 0), earth + new V3(0, 0, 150e3), V3.Zero, 10);
        Check(!toEarth.Hazards.Exists(h => h.World.Name == "Earth"), "flying to Earth isn't a hazard of Earth");
        var tycho = RoutePlan.Make("Tycho", earth, SystemData.Stations[3].Pos, V3.Zero, 10);
        Check(tycho.StationAtTarget?.Name == "Tycho Station" && tycho.TargetSector.Dx == "DX6", "Tycho: station and Belt sector at the end");
    }

    private static void Camera()
    {
        var cam = new OrbitCamera { Target = new V3(1e6, 2e5, -3e6), Distance = 5e5 };
        cam.Setup(0, 0, 1000, 600);
        Check(cam.Project(cam.Target, out var x, out var y, out var d) && Near(x, 500, 0.01) && Near(y, 300, 0.01) && Near(d, 5e5, 1), "target projects to the centre");
        Check(!cam.Project(cam.Eye - cam.Forward * 10, out _, out _, out _), "behind the camera isn't drawn");
        cam.Project(cam.Target + new V3(0, 1e4, 0), out _, out var yUp, out _);
        Check(yUp < 300, "up is up on screen");
        var before = cam.Target; cam.Pan(100, 0);
        cam.Setup(0, 0, 1000, 600);
        cam.Project(before, out var bx, out _, out _);
        Check(Near(bx, 600, 1), "pan by 100 px moves the old centre 100 px " + bx);
        cam.FlyTo(V3.Zero, 1e7);
        for (int i = 0; i < 200; i++) cam.Tick(1 / 60.0);
        Check(Near(cam.Distance, 1e7, 1e4) && cam.Target.Length < 1e3, "fly-to settles");
        cam.Zoom(100);
        for (int i = 0; i < 400; i++) cam.Tick(1 / 60.0);
        Check(cam.Distance >= OrbitCamera.MinDistance - 1, "zoom stops at the minimum");
    }

    private static void Labels()
    {
        Check(Fmt.Dist(850) == "850 m" && Fmt.Dist(12400) == "12.4 km" && Fmt.Dist(1250e3) == "1,250 km" && Fmt.Dist(12.5e6) == "12.50 Mm", "distance text");
        Check(Fmt.Time(45) == "45 s" && Fmt.Time(725) == "12 m 05 s" && Fmt.Time(11520) == "3 h 12 m", "time text");
        Check(Fmt.Speed(950) == "950 m/s" && Fmt.Speed(50000) == "50.00 km/s", "speed text");
    }

    private static bool Clear(RoutePath p)
    {
        foreach (var l in p.Legs) if (l.Hazards.Count > 0) return false;
        return true;
    }

    /// <summary>The nearest the flown line start → waypoints → end comes to w's centre.</summary>
    private static double LowestOver(V3 start, List<Waypoint> wps, V3 end, SystemData.World w)
    {
        var pts = new List<V3> { start }; foreach (var p in wps) pts.Add(p.Pos); pts.Add(end);
        double min = double.MaxValue;
        for (int i = 0; i + 1 < pts.Count; i++) min = Math.Min(min, V3.SegmentDistance(pts[i], pts[i + 1], w.Pos, out _));
        return min;
    }

    private static string Km(double m) => (m / 1000).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " km";

    private static void Detours()
    {
        var earth = SystemData.FindWorld("Earth");
        // straight through Earth (radius 100 km): one bend over its side, every leg clear by 100 km
        var a = earth.Pos + new V3(-600e3, 0, 0); var b = earth.Pos + new V3(600e3, 0, 0);
        var wps = DetourPlanner.Plan(a, b, "B");
        Check(wps.Count >= 1 && wps.TrueForAll(w => w.Around == earth), "through Earth: detours round Earth " + wps.Count);
        var close = DetourPlanner.Plan(earth.Pos + new V3(-300e3, 0, 0), earth.Pos + new V3(300e3, 0, 0), "B");
        Check(close.Count == 2 && Clear(RoutePath.Make("B", earth.Pos + new V3(-300e3, 0, 0), earth.Pos + new V3(300e3, 0, 0), V3.Zero, 10, 50000, close, 0)), "close in: two bends (max 60 degrees each), clear " + close.Count);
        var path = RoutePath.Make("B", a, b, V3.Zero, 10, 50000, wps, 0);
        Check(Clear(path), "detoured legs keep 100 km clear");
        Check(path.Distance > 1.2e6 && path.Distance < 1.4e6, "detour adds a sensible distance " + path.Distance);
        foreach (var w in wps) Check(V3.Distance(w.Pos, earth.Pos) > earth.Radius + 100e3, "detour point outside the clearance");
        // a grazing pass (50 km over the surface) bends only a little
        var g1 = earth.Pos + new V3(-800e3, 150e3, 0); var g2 = earth.Pos + new V3(800e3, 150e3, 0);
        var graze = DetourPlanner.Plan(g1, g2, "G");
        Check(graze.Count == 1 && Clear(RoutePath.Make("G", g1, g2, V3.Zero, 10, 50000, graze, 0)), "grazing pass: one waypoint " + graze.Count);
        // already clear: nothing
        Check(DetourPlanner.Plan(earth.Pos + new V3(-800e3, 400e3, 0), earth.Pos + new V3(800e3, 400e3, 0), "C").Count == 0, "clear line: no detour");
        // flying to Earth or leaving it: never avoided
        Check(DetourPlanner.Plan(earth.Pos + new V3(-600e3, 0, 0), earth.Pos + new V3(0, 0, 130e3), "E").Count == 0, "flying to Earth: no detour");
        // starting close to a world (inside the clearance) used to skip it: the line went straight through
        double R = earth.Radius;
        var lowStart = earth.Pos + new V3(-150e3, 0, 0); var farSide = earth.Pos + new V3(600e3, 0, 0); // 50 km up, target behind Earth
        var low = DetourPlanner.Plan(lowStart, farSide, "F");
        Check(low.Count >= 1 && LowestOver(lowStart, low, farSide, earth) >= R + 40e3 - 1, "low orbit: goes round near its own height, not through (" + Km(LowestOver(lowStart, low, farSide, earth) - R) + " up)");
        Check(RoutePath.Make("F", lowStart, farSide, V3.Zero, 10, 50000, null, 0).First.Hazards.Exists(h => h.World == earth && h.Blocked), "low orbit: the straight line is flagged THROUGH EARTH");
        Check(!RoutePath.Make("F", lowStart, farSide, V3.Zero, 10, 50000, low, 0).Legs.Exists(l => l.Hazards.Exists(h => h.Blocked)), "low orbit: the detoured legs aren't flagged through anything");
        var band = earth.Pos + new V3(-205e3, 0, 0); // 105 km up: the corners at 1.15 x clearance were above the ship
        var bandWps = DetourPlanner.Plan(band, farSide, "F");
        Check(bandWps.Count >= 1 && LowestOver(band, bandWps, farSide, earth) >= R + 80e3, "105 km up: goes round (" + Km(LowestOver(band, bandWps, farSide, earth) - R) + " up)");
        // on the ground: climb first, then round
        var ground = earth.Pos + new V3(-102e3, 0, 0);
        var up = DetourPlanner.Plan(ground, farSide, "F");
        Check(up.Count >= 2 && Math.Abs(V3.Distance(up[0].Pos, earth.Pos) - (R + 100e3)) < 1 && V3.Distance(up[0].Pos, earth.Pos + new V3(-200e3, 0, 0)) < 1, "take-off: climbs straight up to the clearance first");
        Check(LowestOver(up[0].Pos, up.GetRange(1, up.Count - 1), farSide, earth) >= R + 80e3, "take-off: then round, clear");
        // landing on the far side: round, then down from above it
        var base2 = earth.Pos + new V3(101e3, 0, 0);
        var down = DetourPlanner.Plan(lowStart, base2, "B");
        Check(down.Count >= 2 && V3.Distance(down[down.Count - 1].Pos, earth.Pos + new V3(200e3, 0, 0)) < 1, "landing: comes down from straight above the base");
        Check(LowestOver(lowStart, down.GetRange(0, down.Count - 1), down[down.Count - 1].Pos, earth) >= R + 40e3 - 1, "landing: round, clear, before coming down");
        // picking Earth itself from low orbit: stop above you, never at the centre
        var stop = RouteExport.StopNear(lowStart, earth.Pos, 100e3, out var why);
        Check(why == "world" && Math.Abs(V3.Distance(stop, earth.Pos) - 150e3) < 1 && stop.X < earth.Pos.X, "pick Earth from low orbit: stop where you are, not its centre");
        Check(DetourPlanner.Plan(lowStart, earth.Pos, "Earth").Count == 0, "pick Earth: no detours round Earth itself");

        // your own grids and the known stations: gone round outside their sphere, except the one you leave or fly to
        var o1 = earth.Pos + new V3(0, 0, 3e6);                     // well clear of Earth
        var baseObs = SystemData.Obstacle("Home Base", o1, 3000);
        var obs = new List<SystemData.World> { baseObs };
        var oa = o1 + new V3(-50e3, 200, 0); var ob = o1 + new V3(50e3, -200, 0);
        var round = DetourPlanner.Plan(oa, ob, "X", 100e3, obs);
        Check(round.Count >= 1 && round.TrueForAll(w => w.Around == baseObs) && LowestOver(oa, round, ob, baseObs) >= 3000 - 1, "grid in the way: gone round outside its sphere (" + Km(LowestOver(oa, round, ob, baseObs)) + " from its centre)");
        Check(LowestOver(oa, round, ob, baseObs) < 3000 * DetourPlanner.Slack + 100, "grid in the way: no 100 km clearance on top of its margin");
        Check(DetourPlanner.Plan(oa, ob, "X", 100e3).Count == 0, "no obstacles given: straight");
        Check(DetourPlanner.Plan(o1 + new V3(0, 0, 1000), ob, "X", 100e3, obs).Count == 0, "leaving your base, target off to the side: straight away");
        Check(DetourPlanner.Plan(oa, o1 + new V3(0, 1500, 0), "Home", 100e3, obs).Count == 0, "flying to your base: not gone round");
        Check(DetourPlanner.Plan(oa + new V3(0, 0, 20e3), ob + new V3(0, 0, 20e3), "X", 100e3, obs).Count == 0, "base well off the line: straight");
        var tr = new RouteTracker { Avoid = true, Obstacles = obs };
        tr.Update("Far", ob, true, oa, V3.Zero, 10, 0);
        Check(tr.Waypoints.Exists(w => w.Around == baseObs), "tracker: plans round your grids");
        var merged = new List<SystemData.World> { SystemData.Obstacle("A", V3.Zero, 3000), SystemData.Obstacle("B", new V3(4000, 0, 0), 2500), SystemData.Obstacle("C", new V3(50e3, 0, 0), 2000) };
        DetourPlanner.Merge(merged);
        Check(merged.Count == 2 && merged[0].Radius >= (4000 + 2500 + 3000) / 2.0 - 1e-6 && V3.Distance(merged[0].Pos, V3.Zero) + 3000 <= merged[0].Radius + 1e-6 && V3.Distance(merged[0].Pos, new V3(4000, 0, 0)) + 2500 <= merged[0].Radius + 1e-6, "merge: overlapping grids become one sphere holding both, apart ones stay");

        // a station you start at (undocking) or near: straight out of it first, then round it; your own grid: not gone round
        var stn = SystemData.Obstacle("Tycho Station", o1, 8000, station: true);
        var stns = new List<SystemData.World> { stn };
        var docked = o1 + new V3(-2000, 300, 0); var behind = o1 + new V3(60e3, 0, 0);
        var undock = DetourPlanner.Plan(docked, behind, "X", 100e3, stns);
        Check(undock.Count >= 2 && undock[0].Around == stn && Math.Abs(V3.Distance(undock[0].Pos, o1) - 8000 * DetourPlanner.Slack) < 1 && undock[0].Pos.X < o1.X, "undock, target behind: straight out of the station first");
        Check(LowestOver(undock[0].Pos, undock.GetRange(1, undock.Count - 1), behind, stn) >= 8000 - 1, "undock, target behind: then round it, outside its 8 km");
        Check(DetourPlanner.Plan(docked, o1 + new V3(-60e3, 0, 0), "X", 100e3, stns).Count == 0, "undock, target ahead: straight away");
        Check(DetourPlanner.Plan(o1 + new V3(-7000, 0, 0), behind, "X", 100e3, stns).Exists(w => w.Around == stn), "near a station (7 km): gone round too");
        Check(DetourPlanner.Plan(oa, o1 + new V3(0, 1500, 0), "Tycho", 100e3, stns).Count == 0, "flying to a station: not gone round");
        Check(SystemData.IsBaseCore("sdx_shipcoreOutpost") && SystemData.IsBaseCore("sdx_shipcoreInstallation") && !SystemData.IsBaseCore("sdx_shipcoreNoCore") && !SystemData.IsBaseCore("sdx_shipcoreSkiff") && !SystemData.IsBaseCore(null), "base: an Outpost or Installation core, no other core");
        // your own base you start at: the same as a station (out first, then round); flying to it: not gone round
        var home = SystemData.Obstacle("Home Base", o1, 2300);
        var homes = new List<SystemData.World> { home };
        var atHome = o1 + new V3(-400, 100, 0);
        var leave = DetourPlanner.Plan(atHome, behind, "X", 100e3, homes);
        Check(leave.Count >= 2 && leave[0].Around == home && Math.Abs(V3.Distance(leave[0].Pos, o1) - 2300 * DetourPlanner.Slack) < 1 && leave[0].Pos.X < o1.X, "leave your base, target behind: straight out of it first");
        Check(LowestOver(leave[0].Pos, leave.GetRange(1, leave.Count - 1), behind, home) >= 2300 - 1, "leave your base, target behind: then round it");
        Check(DetourPlanner.Plan(atHome, o1 + new V3(-60e3, 0, 0), "X", 100e3, homes).Count == 0, "leave your base, target ahead: straight away");
        Check(DetourPlanner.Plan(behind, o1 + new V3(-300, 0, 0), "Home", 100e3, homes).Count == 0, "fly home: your base isn't gone round");
        var mix = new List<SystemData.World> { SystemData.Obstacle("Home", V3.Zero, 3000), SystemData.Obstacle("Stn", new V3(4000, 0, 0), 8000, station: true), SystemData.Obstacle("Stn2", new V3(5000, 0, 0), 8000, station: true) };
        DetourPlanner.Merge(mix);
        Check(mix.Count == 2 && mix.Exists(w => w.Name == "Home" && !w.IsStation) && mix.Exists(w => w.IsStation), "merge: stations aren't merged into your grids (two stations are)");

        // the geometry: corners of a semicircle around a unit sphere
        var c = DetourPlanner.Around(new V3(-3, 0, 0), new V3(3, 0, 0), V3.Zero, 1);
        Check(c.Count == 1 && c[0].Length > 1 && Math.Abs(c[0].X) < 1e-9, "Around: one corner over the middle, outside the sphere");
        // a path with every planet in the way still ends at the target
        var far = DetourPlanner.Plan(SystemData.FindWorld("Neptune").Pos + new V3(3e6, 0, 0), SystemData.FindWorld("Mercury").Pos + new V3(0, 400e3, 0), "M");
        Check(far.Count <= DetourPlanner.MaxWaypoints, "waypoint count is capped");
    }

    private static void Tracker()
    {
        var earth = SystemData.FindWorld("Earth").Pos;
        var t = new RouteTracker();
        var a = earth + new V3(-300e3, 0, 0); var b = earth + new V3(300e3, 0, 0);
        t.Update("Far side", b, true, a, V3.Zero, 10, 50000);
        int rev = t.Revision, n = t.Waypoints.Count;
        Check(n >= 2 && t.Path.Legs.Count == n + 1, "tracker plans detours on a new pick");
        // picking our own detour 1 in F&B's list keeps the plan
        t.Update(t.Waypoints[0].Name, t.Waypoints[0].Pos, true, a + new V3(1e3, 0, 0), V3.Zero, 10, 50000);
        Check(t.Revision == rev && t.TargetName == "Far side", "picking a detour keeps the plan");
        // reaching detour 1 moves on
        t.Update(t.Waypoints[0].Name, t.Waypoints[0].Pos, true, t.Waypoints[0].Pos, V3.Zero, 10, 50000);
        Check(t.Next == 1 && t.Path.Legs.Count == n, "reaching a detour moves to the next " + t.Next);
        Check(t.NextPick == t.Waypoints[1].Name, "next pick is detour 2");
        // a new destination re-plans
        t.Update("Elsewhere", earth + new V3(0, 900e3, 0), true, a, V3.Zero, 10, 50000);
        Check(t.Revision > rev && t.Next == 0, "new destination re-plans");
        // avoidance off: straight line
        var off = new RouteTracker { Avoid = false };
        off.Update("Far side", b, true, a, V3.Zero, 10, 50000);
        Check(off.Waypoints.Count == 0 && off.Path.Legs.Count == 1 && off.Path.First.Hazards.Count > 0, "avoid off: straight, hazard shown");
        t.Update(null, V3.Zero, true, a, V3.Zero, 10, 50000);
        Check(t.Path == null && t.Waypoints.Count == 0, "no pick: no route");
    }

    private static void Export()
    {
        var earth = SystemData.FindWorld("Earth").Pos;
        var t = new RouteTracker();
        t.Update("Far [side]", earth + new V3(600e3, 0, 0), true, earth + new V3(-600e3, 0, 0), V3.Zero, 12.5, 50000);
        string sec = RouteExport.Build(t.Path, 7);
        string cd = "[Autopilot]\nspeed=500\n\n[SDX2 Route]\nversion=1\nid=1\nwp=0,0,0,0,target,old\n\n[Other]\nx=1\n";
        string merged = RouteExport.Merge(cd, sec);
        Check(merged.Contains("[Autopilot]\nspeed=500") && merged.Contains("[Other]\nx=1") && !merged.Contains(",old"), "merge keeps other sections, replaces ours");
        Check(RouteExport.Parse(merged, out var r) && r.Id == 7 && r.Points.Count == t.Waypoints.Count + 1, "export parses back");
        var last = r.Points[r.Points.Count - 1];
        Check(last.Kind == "target" && last.Name == "Far (side)" && V3.Distance(r.Dest, earth + new V3(600e3, 0, 0)) < 0.01 && r.Command == "load", "last point is the target, dest and command kept");
        var stop = RouteExport.StopPoint(t.Path, out var why);
        Check(V3.Distance(last.Pos, stop) < 0.01 && r.Stop == why, "last point is the stop point");

        // stop points: 8 km short of a station, at the clearance above a world, the point itself in open space
        var tycho = SystemData.Stations[3].Pos;
        var ps = RoutePath.Make("Tycho", tycho + new V3(3e6, 0, 0), tycho, V3.Zero, 10, 50000, new List<Waypoint>(), 0, 100e3);
        var sp = RouteExport.StopPoint(ps, out why);
        Check(why == "station" && Near(V3.Distance(sp, tycho), RouteExport.StationStandoff, 1) && sp.X > tycho.X, "station stop 8 km short, on the near side");
        var mars = SystemData.FindWorld("Mars");
        var pm = RoutePath.Make("Mars", mars.Pos + new V3(0, 0, 2e6), mars.Pos, V3.Zero, 10, 50000, new List<Waypoint>(), 0, 100e3);
        sp = RouteExport.StopPoint(pm, out why);
        Check(why == "world" && Near(V3.Distance(sp, mars.Pos), mars.Radius + 100e3, 1) && sp.Z > mars.Pos.Z, "world stop at the clearance, facing the approach");
        var open = new V3(3e6, 2e6, 1e6);
        var po = RoutePath.Make("Open", open + new V3(1e6, 0, 0), open, V3.Zero, 10, 50000, new List<Waypoint>(), 0, 100e3);
        Check(V3.Distance(RouteExport.StopPoint(po, out why), open) < 0.01 && why == "point", "open space stops on the point");

        // engage with an asteroid dodge in front, stop order, PB status section
        var pass = new V3(1, 2, 3);
        Check(RouteExport.Parse(RouteExport.Build(t.Path, 8, "engage", new[] { pass }), out r) && r.Command == "engage" && r.Points[0].Kind == "pass"
              && V3.Distance(r.Points[0].Pos, pass) < 0.01 && r.Points.Count == t.Waypoints.Count + 2, "engage with a pass point first");
        Check(RouteExport.Parse(RouteExport.BuildStop(9), out r) && r.Command == "stop" && r.Id == 9, "stop order parses");
        string withStatus = RouteExport.Merge(merged, "[SDX2 Autopilot]\nstate=flying\nack=8\nwp=2/3\ndist=123456.5\nspeed=31000\neta=600\nmsg=Flying to Detour 2\n", RouteExport.StatusHeader);
        var st = RouteExport.ParseStatus(withStatus);
        Check(st != null && st.Flying && st.Ack == 8 && st.Wp == 2 && st.Count == 3 && Near(st.Dist, 123456.5, 1e-6) && st.Msg == "Flying to Detour 2"
              && RouteExport.Parse(withStatus, out r) && r.Id == 7, "status section round-trips beside the route");
        Check(RouteExport.ParseStatus(merged) == null, "no status before the PB writes one");

        // asteroid dodge: a rock on the line gets a pass point that keeps both straight lines clear
        var a = new V3(0, 0, 0); var b = new V3(1000e3, 0, 0); var rock = new V3(400e3, 1000, 0);
        var pp = AsteroidDodge.PassPoint(a, b, rock, 3000);
        Check(pp.HasValue && V3.SegmentDistance(a, pp.Value, rock, out _) >= 4999 && V3.SegmentDistance(pp.Value, b, rock, out _) >= 4999 && pp.Value.Y < 0, "dodge passes the rock on the side the line is already on");
        Check(!AsteroidDodge.PassPoint(a, b, new V3(400e3, 50e3, 0), 3000).HasValue, "a rock off the line needs no dodge");
        Check(!AsteroidDodge.PassPoint(a, b, new V3(-50e3, 0, 0), 3000).HasValue && !AsteroidDodge.PassPoint(a, b, b + new V3(1000, 0, 0), 3000).HasValue, "no dodge for a rock behind or at the destination");
        Check(r.Points[0].Kind == "detour" && V3.Distance(r.Points[0].Pos, t.Waypoints[0].Pos) < 0.01 && Near(r.Accel, 12.5, 1e-9), "detours and accel round-trip");
        Check(RouteExport.HasSection(RouteExport.Merge("", sec)) && !RouteExport.HasSection("[Autopilot]"), "section detection");
    }

    private static void UserWaypoints()
    {
        var earth = SystemData.FindWorld("Earth").Pos;
        var t = new RouteTracker();
        var ship = earth + new V3(-3e6, 0, 0);
        var dest = earth + new V3(3e6, 2e6, 0);
        t.Update("Dest", dest, true, ship, V3.Zero, 10, 50000);
        int rev = t.Revision;
        // adding: a pick becomes a waypoint, the destination stays
        t.Hold = t.Adding = true;
        var wpPos = earth + new V3(0, 3e6, 0);
        t.Update("Waypoint A", wpPos, true, ship, V3.Zero, 10, 50000);
        Check(t.TargetName == "Dest" && t.UserPoints.Count == 1 && t.Revision > rev, "adding: a pick becomes a waypoint");
        int ui = t.Waypoints.FindIndex(w => w.User);
        Check(ui >= 0 && t.Waypoints[ui].Name == "Waypoint A" && t.Path.Legs.Count == t.Waypoints.Count + 1 && t.Next == 0, "the waypoint is in the route, flown first");
        // deselecting while the panel holds the route keeps it; picking the same point again adds nothing
        t.Update(null, V3.Zero, true, ship, V3.Zero, 10, 50000);
        Check(t.Path != null && t.TargetName == "Dest", "hold keeps the route with nothing picked");
        t.Update("Waypoint A", wpPos, true, ship, V3.Zero, 10, 50000);
        Check(t.UserPoints.Count == 1, "the same point isn't added twice");
        // a station waypoint stops 8 km short
        var tycho = SystemData.Stations[3];
        t.Update(tycho.Name, tycho.Pos, true, ship, V3.Zero, 10, 50000);
        var sw = t.Waypoints.Find(w => w.User && w.Name == tycho.Name);
        Check(t.UserPoints.Count == 2 && Near(V3.Distance(sw.Pos, tycho.Pos), RouteExport.StationStandoff, 1), "a station waypoint stops 8 km short");
        // the export marks them as waypoints, in order before the target
        RouteExport.Parse(RouteExport.Build(t.Path, 3, "engage", null, 0.6), out var r);
        int ka = r.Points.FindIndex(x => x.Kind == "waypoint" && x.Name == "Waypoint A"), kb = r.Points.FindIndex(x => x.Kind == "waypoint" && x.Name == tycho.Name);
        Check(ka >= 0 && kb > ka && r.Points[r.Points.Count - 1].Kind == "target" && Near(r.Thrust, 0.6, 1e-9), "export: waypoints in order, then the target; thrust cap kept");
        RouteExport.Parse(RouteExport.Build(t.Path, 4, "engage", null, 0.6, 0.25), out var rb);
        Check(Near(rb.Thrust, 0.6, 1e-9) && Near(rb.Brake, 0.25, 1e-9) && Near(r.Brake, 0.6, 1e-9), "export: brake cap kept (defaults to the burn cap)");
        // removing one plans again
        t.RemoveUser(0);
        Check(t.UserPoints.Count == 1 && !t.Waypoints.Exists(w => w.Name == "Waypoint A"), "removing a waypoint");
        // panel closed: the selection (the station) is ours, so the plan stays; a new pick is a new destination
        t.Hold = t.Adding = false;
        t.Update(tycho.Name, tycho.Pos, true, ship, V3.Zero, 10, 50000);
        Check(t.TargetName == "Dest" && t.UserPoints.Count == 1, "closing the panel keeps the route");
        t.Update("Other", earth + new V3(0, -4e6, 0), true, ship, V3.Zero, 10, 50000);
        Check(t.TargetName == "Other" && t.UserPoints.Count == 0, "a new destination drops your waypoints");
        // detour names are numbered over the whole route
        int det = 0; foreach (var w in t.Waypoints) if (!w.User) det++;
        Check(det == 0 || t.Waypoints.TrueForAll(w => w.User || w.Name.Contains(" of " + det + " - Other")), "detours numbered over the whole route");
    }

    private static void Limits()
    {
        Check(DriveSignature.Weight("sdx_driveCivilian5x5") == 2 && DriveSignature.Weight("sdx_driveIndustrial9x9_BS1") == 6
              && Near(DriveSignature.Weight("sdx_driveTorch3x3_small"), 1.0 / 3, 1e-9) && DriveSignature.Weight("sdx_thrusterRCS_Bare") == 0, "drive signal weights");
        Check(Near(DriveSignature.Thermal(2, 1), 24.43 * Math.Sqrt(2), 1e-6) && Near(DriveSignature.Exhaust(2, 1), 97.72 * Math.Sqrt(2), 1e-6), "signature ranges");
        var lim = new RouteLimits { BySignature = true, SignatureKm = DriveSignature.WeakCone(8, 0.25) };
        Check(Near(lim.ThrustFraction(8), 0.25, 1e-6), "signature cap gives the thrust share");
        lim.SignatureKm = 1e6;
        Check(Near(lim.ThrustFraction(8), 1, 1e-9), "a signature cap above full thrust doesn't bite");
        Check(Near(new RouteLimits { ThrustPct = 40 }.ThrustFraction(0), 0.4, 1e-9) && Near(new RouteLimits { BySignature = true, SignatureKm = 50 }.ThrustFraction(0), 1, 1e-9), "thrust %; no drives = no signature cap");
        {
            // Spectrum: range = sqrt(S / threshold / 4pi); one 3x3 (3e9) at full thrust, gain 4
            double R(double st, double thr) => Math.Sqrt(st / thr / 4 / Math.PI) / 1000;
            var one = new SignatureInfo { Weight = 1, Gain = 4 };
            Check(Near(DriveSignature.RangeKm(one.AllRound(1)), R(3e9, 0.4), 1e-6) && Near(DriveSignature.RangeKm(one.AllRound(1)), DriveSignature.Thermal(1, 1), 0.01)
                  && Near(DriveSignature.RangeKm(one.AllRound(1), 0.1), 2 * DriveSignature.RangeKm(one.AllRound(1)), 1e-6), "all-round range matches Spectrum (bright and dim)");
            Check(Near(DriveSignature.RangeKm(one.Cone(1)), R(3e9 * 16, 0.4), 1e-6) && Near(DriveSignature.RangeKm(one.Cone(0.25)), DriveSignature.Exhaust(1, 0.25), 0.01), "exhaust cone = strength x gain^2");
            Check(DriveSignature.Gain("sdx_driveTorch3x3_small") == 2 && DriveSignature.Gain("sdx_driveCivilian5x5") == 4, "torch gain 2, others 4");
            var ant = new SignatureInfo { Weight = 1, Floor = DriveSignature.StrengthForKm(50) };
            Check(Near(DriveSignature.RangeKm(ant.AllRound(0.5)), 50, 1e-6) && Near(DriveSignature.RangeKm(ant.Cone(0.5)), DriveSignature.Exhaust(1, 0.5), 0.01), "a 50 km broadcast sets the all-round range, not the cone");
            double bc = DriveSignature.StrengthForKm(100);
            Check(DriveSignature.Classify(bc, 99) == SignalTier.Strong && DriveSignature.Classify(bc, 101) == SignalTier.Weak
                  && DriveSignature.Classify(bc, 49) == SignalTier.Detailed && DriveSignature.Classify(bc, 199) == SignalTier.Weak
                  && DriveSignature.Classify(bc, 201) == SignalTier.None, "100 km beacon: Detailed 50, Strong 100, Weak 200 km (wiki)");
            Check(DriveSignature.Classify(bc, 199, DriveSignature.Advanced) == SignalTier.Strong && DriveSignature.Classify(bc, 101, DriveSignature.NoSensor) == SignalTier.None
                  && DriveSignature.Classify(bc, 139, DriveSignature.Directional) == SignalTier.Strong, "sensor range multipliers 0.5x / 1.4x / 2.0x");
            Check(DriveSignature.Tiers(bc) == "50/100/200" && DriveSignature.Tiers(DriveSignature.StrengthForKm(8)) == "4.0/8.0/16", "tier ranges text");
        }
        Check(new RouteLimits { MaxSpeed = 20000 }.SpeedCap(50000) == 20000 && new RouteLimits().SpeedCap(50000) == 50000, "speed cap");
        // the tracker plans with the caps: slower, so a longer ETA
        var a = new V3(5e6, 3e6, 1e6); var b = a + new V3(4e7, 0, 0);
        var t = new RouteTracker { Avoid = false };
        t.Update("Far", b, true, a, V3.Zero, 10, 50000);
        double eta = t.Path.Eta;
        t.AccelScale = 0.5; t.BrakeScale = 0.5; t.SpeedCap = 20000;
        t.Update("Far", b, true, a, V3.Zero, 10, 50000);
        Check(t.Path.Eta > eta * 1.3 && Near(t.Path.MaxSpeed, 20000, 1e-9) && Near(t.Path.MaxAccel, 5, 1e-9), "capped plan is slower");
        {
            // separate burn and brake: (vp² − 0)/2a + vp²/2b = d; brake time vp/b; the flip comes where braking at b just stops
            var pl = RoutePlan.Make("x", V3.Zero, new V3(1e6, 0, 0), V3.Zero, 10, 50000, 0, 2.5);
            double vp = Math.Sqrt(1e6 / (1 / 20.0 + 1 / 5.0));
            Check(Near(pl.PeakSpeed, vp, 1e-6) && Near(pl.BrakeTime, vp / 2.5, 1e-6) && Near(pl.FlipAt, vp * vp / 20, 1e-3)
                  && Near(pl.AlongAt(pl.Eta), 1e6, 1), "plan burns at a, brakes at b (" + pl.PeakSpeed.ToString("0") + " m/s peak)");
            var sym = RoutePlan.Make("x", V3.Zero, new V3(1e6, 0, 0), V3.Zero, 10, 50000);
            Check(Near(sym.Eta, 2 * Math.Sqrt(1e6 / 10), 1e-6), "no brake accel given = same as the burn");
            var lim2 = new RouteLimits { ThrustPct = 30, BrakePct = 80 };
            Check(Near(lim2.ThrustFraction(4), 0.3, 1e-9) && Near(lim2.BrakeFraction(4), 0.8, 1e-9), "separate burn / brake thrust caps");
            lim2.BySignature = true; lim2.SignatureKm = DriveSignature.WeakCone(4, 0.25); lim2.BrakeSignatureKm = DriveSignature.WeakCone(4, 0.64);
            Check(Near(lim2.ThrustFraction(4), 0.25, 1e-6) && Near(lim2.BrakeFraction(4), 0.64, 1e-6), "separate burn / brake signature caps");
            var ms = new MapSettings(); ms.Limits.BrakePct = 42; ms.Limits.BrakeSignatureKm = 33;
            var sf = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sdx2map-brake-test.cfg");
            ms.Save(sf);
            var back = MapSettings.Load(sf);
            Check(Near(back.Limits.BrakePct, 42, 1e-9) && Near(back.Limits.BrakeSignatureKm, 33, 1e-9), "brake caps saved in settings");
            System.IO.File.WriteAllText(sf, "ApSignatureKm=40" + Environment.NewLine + "ApBrakeSignatureKm=25" + Environment.NewLine);
            var old = MapSettings.Load(sf);
            Check(Near(old.Limits.SignatureKm, 320, 1e-9) && Near(old.Limits.BrakeSignatureKm, 200, 1e-9), "old Strong all-round caps load as Weak cone km (x8)");
            Check(Near(DriveSignature.RangeKm(new SignatureInfo { Weight = 3 }.Cone(0.4), DriveSignature.WeakThreshold), DriveSignature.WeakCone(3, 0.4), 0.01), "signature cap km = Spectrum's Weak exhaust-cone range");
            var torch = new SignatureInfo { Weight = 1, Gain = 2 };
            var tl = new RouteLimits { BySignature = true, SignatureKm = DriveSignature.RangeKm(torch.Cone(0.5), DriveSignature.WeakThreshold) };
            Check(Near(tl.ThrustFraction(1, 2), 0.5, 1e-4), "Torch cone cap uses its gain 2");
        }
        // settings round-trip
        string f = Path.Combine(Path.GetTempPath(), "sdx2map-test.ini");
        Check(new MapSettings().FollowHeading, "turn with ship: on by default");
        var s = new MapSettings { KeepNavOpen = false, FollowHeading = false };
        s.Limits.BySignature = true; s.Limits.SignatureKm = 77; s.Limits.ThrustPct = 35; s.Limits.MaxSpeed = 12000;
        s.Save(f);
        var l = MapSettings.Load(f);
        Check(!l.KeepNavOpen && l.ShowMapWithNav && l.Limits.BySignature && Near(l.Limits.SignatureKm, 77, 1e-9) && Near(l.Limits.ThrustPct, 35, 1e-9) && Near(l.Limits.MaxSpeed, 12000, 1e-9), "settings keep the limits");
        Check(!l.FollowHeading, "settings keep turn-with-ship off");
        File.Delete(f);
    }

    /// <summary>Drives the route panel with a fake mouse: drag the thrust slider, switch to signature, click ENGAGE.</summary>
    private static void PanelInput()
    {
        var a = new V3(5e6, 3e6, 1e6);
        var t = new RouteTracker { Avoid = false };
        t.Update("Far", a + new V3(4e7, 0, 0), true, a, V3.Zero, 10, 50000);
        var lim = new RouteLimits();
        var ui = new PanelUi();
        var c = new MeasureCanvas();
        var names = new List<string>();
        AutopilotPanel.RouteHit Frame(float mx, float my, bool down, bool pressed, bool released)
        {
            ui.Input(c, mx, my, down, pressed, released);
            return AutopilotPanel.RoutePanel(ui, 0, 0, 600, 1080, 20, t.Path, lim, 50000, 10, new SignatureInfo { Weight = 8 }, names, false, "Ctrl+M");
        }
        Frame(-1, -1, false, false, false);
        var tr = c.Find("Burn", 0);   // the burn slider row label
        float rowY = tr.Y + 9;
        // press on the slider's left end, drag to the middle, release
        float sx0 = tr.X + 200, sxEnd = 600 - 22 - 100;
        Frame(sx0, rowY, true, true, false);
        var h = Frame((sx0 + sxEnd) / 2, rowY, true, false, false);
        Frame((sx0 + sxEnd) / 2, rowY, false, false, true);
        Check(h.Changed && lim.ThrustPct > 20 && lim.ThrustPct < 90, "dragging the burn slider changes the cap " + lim.ThrustPct.ToString("0"));
        {
            double burnBefore = lim.ThrustPct;
            Frame(-1, -1, false, false, false);
            var br = c.Find("Brake", 0);
            float by2 = br.Y + 9;
            Frame(sx0, by2, true, true, false);
            Frame(sx0 + 30, by2, true, false, false);
            Frame(sx0 + 30, by2, false, false, true);
            Check(lim.BrakePct < 60 && Near(lim.ThrustPct, burnBefore, 1e-9), "the brake slider sets only the brake cap " + lim.BrakePct.ToString("0"));
        }
        var sig = c.Find("SIGNATURE", 0);
        Frame(sig.X, sig.Y + 5, true, true, false);
        h = Frame(sig.X, sig.Y + 5, false, false, true);
        Check(lim.BySignature && lim.SignatureKm > 0 && h.Changed, "SIGNATURE switches the cap to a signature");
        var en = c.Find("ENGAGE", 0);
        Frame(en.X, en.Y + 5, true, true, false);
        h = Frame(en.X, en.Y + 5, false, false, true);
        Check(h.Engage && !h.Cancel, "clicking ENGAGE engages");
    }

    /// <summary>A ship leaving Earth's sector for Tycho Station, as the side window (beside F&amp;B's list) and the full map.</summary>
    private static void HoverOnTop()
    {
        // Ganymede Station drawn over Jupiter's disc: hovering the station picks the station, not Jupiter
        var jup = SystemData.FindWorld("Jupiter");
        var scene = new MapScene { Snap = new MapSnapshot() };
        scene.Cam.Yaw = 0.9; scene.Cam.Pitch = 0.62;
        scene.Cam.Setup(0, 0, 1920, 1080);
        scene.Cam.FlyTo(jup.Pos, jup.Radius * 30); scene.Cam.Snap();
        scene.Opt.Stations = true;
        using (var bmp = new Bitmap(1920, 1080))
        using (var g = Graphics.FromImage(bmp))
        {
            scene.Draw(new GdiMapCanvas(g), new Box(0, 0, 1920, 1080));
            var st = scene.Picks.Find(q => q.Kind == "Trade station" && q.Name == "Ganymede Station");
            var pl = scene.Picks.Find(q => q.Name == "Jupiter");
            if (st.Name == null || pl.Name == null) { Check(false, "hover: station and Jupiter on screen"); return; }
            // put the mouse on the station but inside Jupiter's disc if they overlap; else just on the station
            scene.MouseX = st.X; scene.MouseY = st.Y;
            scene.Draw(new GdiMapCanvas(g), new Box(0, 0, 1920, 1080));
            Check(scene.Hovered is Pick h && h.Kind == "Trade station", "hover: a station on top of a planet is picked first");
            scene.MouseX = pl.X; scene.MouseY = pl.Y;
            scene.Draw(new GdiMapCanvas(g), new Box(0, 0, 1920, 1080));
            Check(scene.Hovered is Pick h2 && h2.Name == "Jupiter", "hover: the planet's centre still picks the planet");
        }
    }

    private static void Themes()
    {
        Check(MapTheme.Find("tachi") == MapTheme.Tachi && MapTheme.Find("Solar HUD") == MapTheme.Tachi && MapTheme.Find("nonsense") == MapTheme.Classic, "themes: found by name, unknown = Classic");
        Check(MapTheme.Classic.Next() == MapTheme.Tachi && MapTheme.Tachi.Next() == MapTheme.Unn && MapTheme.Unn.Next() == MapTheme.Opa && MapTheme.Opa.Next() == MapTheme.Classic, "themes: Next cycles through all four");
        Check(MapTheme.Find("unn") == MapTheme.Unn && MapTheme.Find("OPA") == MapTheme.Opa, "themes: UNN and OPA found by name");
        Check(MapTheme.Match("mcrn") == MapTheme.Tachi && MapTheme.Match("navy") == MapTheme.Unn && MapTheme.Match("un") == MapTheme.Unn
            && MapTheme.Match("belter") == MapTheme.Opa && MapTheme.Match("classic") == MapTheme.Classic && MapTheme.Match("purple") == null, "themes: faction words pick a theme");
        Check(MapTheme.Unn.Style == ThemeStyle.Naval && MapTheme.Opa.Style == ThemeStyle.Belter && MapTheme.Tachi.Hud && !MapTheme.Unn.Hud, "themes: each has its own style");
        var dir = Path.Combine(Path.GetTempPath(), "sdx2map-theme-test.ini");
        var st = new MapSettings { Theme = "Tachi" }; st.Save(dir);
        Check(MapSettings.Load(dir).Theme == "Tachi", "themes: saved and loaded");
        // picture-in-picture
        var pst = new MapSettings { Pip = true, PipCorner = "BL", PipSize = 1.4f }; pst.Save(dir);
        var pl = MapSettings.Load(dir);
        Check(pl.Pip && pl.PipCorner == "BL" && Math.Abs(pl.PipSize - 1.4f) < 1e-4, "pip: saved and loaded");
        Check(MapScene.GridStepFor(MapScene.ShipViewDistance) == 2e6, "no route: the view round your ship has a 2000 km grid");
        foreach (var g in MapSettings.GridChoicesKm)
        {
            MapScene.DefaultGridKm = g;
            Check(Math.Abs(MapScene.GridStepFor(MapScene.ShipViewDistance) - g * 1e3) < 1e-3 * g, "default zoom: " + g + " km shows a " + g + " km grid");
        }
        MapScene.DefaultGridKm = MapSettings.DefaultGridDefault;
        Check(MapSettings.NearestGrid(1800) == 2000 && MapSettings.NearestGrid(70) == 50 && MapSettings.NearestGrid(-3) == 2000 && MapSettings.NearestGrid(1e9) == 20000, "default zoom: odd values snap to a choice");
        new MapSettings { DefaultGridKm = 500 }.Save(dir);
        Check(MapSettings.Load(dir).DefaultGridKm == 500, "default zoom: saved and loaded");
        // key bindings
        var kc = KeyCombo.Parse("shift+ctrl+m");
        Check(kc.Ctrl && kc.Shift && !kc.Alt && kc.Key == "m" && kc.ToString() == "Ctrl+Shift+m", "keys: parse any order and case");
        Check(KeyCombo.Parse("Ctrl+Alt+OemQuestion").Display == "Ctrl+Alt+/" && KeyCombo.Parse("Ctrl+Alt+/").Key == "OemQuestion", "keys: / shown and read back");
        Check(KeyCombo.Parse("Alt+5").Key == "D5" && KeyCombo.Parse("Alt+D5").Display == "Alt+5", "keys: digits");
        Check(KeyCombo.Parse("None").IsNone && KeyCombo.Parse("Ctrl+").IsNone && KeyCombo.Parse("").ToString() == "None" && KeyCombo.Parse("None").Display == "unbound", "keys: unbound");
        Check(KeyCombo.Parse("Shift+M").Equals(KeyCombo.Parse("shift+m")) && !KeyCombo.Parse("Shift+M").Equals(KeyCombo.Parse("Ctrl+Shift+M")), "keys: equality is exact on modifiers");
        var kd = new MapSettings();
        Check(kd.KeyMap == "Shift+M" && kd.KeyAutopilot == "Ctrl+M" && kd.KeyPin == "Alt+M" && kd.KeySteer == "Ctrl+Shift+M", "keys: defaults leave M alone to the game");
        var ks = new MapSettings { KeyMap = "F8", KeySteer = "None" }; ks.Save(dir);
        var kl = MapSettings.Load(dir);
        Check(!kd.OnlyInCockpit, "only-in-cockpit: off by default");
        new MapSettings { OnlyInCockpit = true }.Save(dir);
        Check(MapSettings.Load(dir).OnlyInCockpit, "only-in-cockpit: saved and loaded");
        ks.Save(dir); kl = MapSettings.Load(dir);
        Check(kl.KeyMap == "F8" && kl.KeySteer == "None" && kl.KeyPin == "Alt+M", "keys: saved and loaded");
        File.WriteAllText(dir, "PreviewKey=N" + Environment.NewLine);
        var ko = MapSettings.Load(dir);
        Check(ko.KeyMap == "Shift+N" && ko.KeyAutopilot == "Ctrl+N" && ko.KeyPin == "Alt+N" && ko.KeySteer == "Ctrl+Shift+N", "keys: an old PreviewKey carries over");
        Check(SideLayout.Corner("top left") == "TL" && SideLayout.Corner("br") == "BR" && SideLayout.Corner("Bottom-Left") == "BL" && SideLayout.Corner("middle") == null, "pip: corner names");
        Check(SideLayout.NextCorner("TR") == "BR" && SideLayout.NextCorner("BR") == "BL" && SideLayout.NextCorner("BL") == "TL" && SideLayout.NextCorner("TL") == "TR" && SideLayout.NextCorner(null) == "TR", "pip: Q cycles the corners");
        var tr = SideLayout.Pip(1920, 1080, 1, "TR"); var bl = SideLayout.Pip(1920, 1080, 1, "BL"); var big = SideLayout.Pip(1920, 1080, 2, "TR");
        Check(tr.Right <= 1920 && tr.X > 960 && tr.Y > 0 && bl.X < 100 && bl.Bottom < 1080 && bl.Y > tr.Y && big.W > tr.W && big.Bottom <= 1080, "pip: corners on screen, size grows");
        MapTheme.Current = MapTheme.Tachi;
        Check(MapScene.BurnCol.R == MapTheme.Tachi.Burn.R && AutopilotPanel.Panel.A == MapTheme.Tachi.ApPanel.A, "themes: scene and panels read the current theme");
        MapTheme.Current = MapTheme.Classic;
    }

    private static void Trajectories()
    {
        var pts = new List<V3>();
        // at rest, straight to a point 1000 km away at 10 m/s²: arrives there
        bool ok = Trajectory.Predict(V3.Zero, V3.Zero, new[] { new V3(1e6, 0, 0) }, 10, 50000, 25e3, pts);
        Check(ok && V3.Distance(pts[pts.Count - 1], new V3(1e6, 0, 0)) < 100, "projection: reaches the destination from rest");
        double maxOff = 0; foreach (var q in pts) maxOff = Math.Max(maxOff, Math.Abs(q.Y) + Math.Abs(q.Z));
        Check(maxOff < 1, "projection: straight line when already lined up");
        // drifting sideways at 2 km/s: bends back and still arrives, off the straight line on the way
        ok = Trajectory.Predict(V3.Zero, new V3(0, 0, 2000), new[] { new V3(1e6, 0, 0) }, 10, 50000, 25e3, pts);
        maxOff = 0; foreach (var q in pts) maxOff = Math.Max(maxOff, q.Z);
        Check(ok && maxOff > 50e3 && maxOff < 400e3, "projection: sideways drift curves back to the route (" + (maxOff / 1e3).ToString("0") + " km out)");
        // through a waypoint first
        ok = Trajectory.Predict(V3.Zero, V3.Zero, new[] { new V3(5e5, 3e5, 0), new V3(1e6, 0, 0) }, 10, 50000, 25e3, pts);
        double nearWp = double.MaxValue; foreach (var q in pts) nearWp = Math.Min(nearWp, V3.Distance(q, new V3(5e5, 3e5, 0)));
        Check(ok && nearWp < 40e3, "projection: passes the waypoint, then arrives");
        // a sharp corner (the screenshot: past a waypoint, then most of the way back): arcs round it, no long overshoot
        var wpS = new V3(3e6, 0, 0); var destS = new V3(1.2e6, 1.2e6, 0);
        ok = Trajectory.Predict(V3.Zero, V3.Zero, new[] { wpS, destS }, 113, 50000, 25e3, pts);
        double past = 0; foreach (var q in pts) past = Math.Max(past, q.X - wpS.X);
        Check(ok && past < 300e3, "projection: sharp corner arcs round the waypoint (" + (past / 1e3).ToString("0") + " km past it)");
        int sharpTurns = 0;
        for (int i = 2; i < pts.Count; i++)
        {
            var a1 = (pts[i - 1] - pts[i - 2]).Normalized; var b1 = (pts[i] - pts[i - 1]).Normalized;
            if (a1.Length > 0.5 && b1.Length > 0.5 && a1.Dot(b1) < 0.5) sharpTurns++;
        }
        Check(sharpTurns == 0, "projection: no kinks over 60° between samples (" + sharpTurns + ")");
        Check(Math.Abs(Trajectory.SinHalfTurn(V3.Zero, new V3(1, 0, 0), new V3(2, 0, 0))) < 1e-9 && Math.Abs(Trajectory.SinHalfTurn(V3.Zero, new V3(1, 0, 0), V3.Zero) - 1) < 1e-9, "corner: straight 0, U-turn 1");
        Check(Trajectory.CornerSpeed(V3.Zero, new V3(1e6, 0, 0), new V3(2e6, 0, 0), 10, 50000) == 50000 && Trajectory.CornerSpeed(V3.Zero, new V3(1e6, 0, 0), V3.Zero, 10, 50000) < 2000, "corner: full speed straight on, slow for a U-turn");
        // phases: burn first, brake last, one per point
        var ph = new List<byte>();
        ok = Trajectory.Predict(V3.Zero, V3.Zero, new[] { new V3(1e6, 0, 0) }, 10, 50000, 25e3, pts, phases: ph);
        Check(ok && ph.Count == pts.Count && ph[0] == Trajectory.Burn && ph[ph.Count - 2] == Trajectory.Brake, "projection: phases burn → brake, one per point");
        ok = Trajectory.Predict(V3.Zero, V3.Zero, new[] { new V3(4e7, 0, 0) }, 10, 3000, 25e3, pts, phases: ph);
        Check(ok && ph.Contains(Trajectory.Coast), "projection: coasts at the top speed on a long trip");
        // no thrust known: the drift line only
        ok = Trajectory.Predict(V3.Zero, new V3(100, 0, 0), new[] { new V3(1e6, 0, 0) }, 0, 50000, 25e3, pts);
        Check(!ok && pts.Count == 2, "projection: no thrust, drift line, doesn't arrive");

        var snap = new MapSnapshot();
        for (int i = 0; i < 20000; i++) snap.RecordTrack(new V3(i * 1000.0, 0, 0), i * 1.0, 1e6);
        Check(snap.Track.Count <= 3000 && snap.Track[0].X == 0, "track: thinned under 3000 points, start kept");
        snap.Track.Clear();
        snap.RecordTrack(V3.Zero, 0, 1e6); snap.RecordTrack(new V3(50, 0, 0), 5, 1e6); snap.RecordTrack(new V3(5000, 0, 0), 0.2, 1e6);
        Check(snap.Track.Count == 1, "track: skips short moves and too-soon samples");
    }

    private static void TurnWithShip()
    {
        // the ship's travel ends up straight up the screen, whatever the tilt and wherever the view was turned
        var ship = new V3(2e6, 1e5, -4e6);
        foreach (var vel in new[] { new V3(3000, 200, 0), new V3(-500, 0, -2500), new V3(0, -40, 900), new V3(-700, 0, 700) })
        {
            var cam = new OrbitCamera { Target = ship, Distance = 2e5, Pitch = 0.5, Yaw = 2.7 };
            new HeadingFollow().Update(cam, vel, new V3(1, 0, 0), 0);
            cam.Setup(0, 0, 1000, 600);
            cam.Project(ship, out var sx, out var sy, out _);
            cam.Project(ship + new V3(vel.X, 0, vel.Z).Normalized * 5e4, out var ax, out var ay, out _);
            Check(Math.Abs(ax - sx) < 0.5 && ay < sy - 10 && Near(cam.Pitch, 0.5, 1e-12), "turn with ship: travel " + vel + " points up the screen, tilt kept");
        }
        // nearly stopped: the way the ship faces
        var c2 = new OrbitCamera { Target = ship, Distance = 2e5, Pitch = 0.5 };
        new HeadingFollow().Update(c2, new V3(1, 3, 0), new V3(0, 0, -1), 0);
        Check(Near(c2.Yaw, HeadingFollow.YawFor(new V3(0, 0, -1)), 1e-9), "turn with ship: slow, so the ship's facing is used");
        Check(!HeadingFollow.HeadingOf(new V3(0, 400, 0), new V3(0, 1, 0), out _), "turn with ship: straight up or down: no turn");
        // smooth, the short way round, and a drag is kept on top
        var hf = new HeadingFollow();
        var c3 = new OrbitCamera { Yaw = HeadingFollow.YawFor(new V3(1, 0, 0)) - 0.2 };
        hf.Update(c3, new V3(1000, 0, 0), V3.Zero, 0.1);
        double moved = Math.IEEERemainder(c3.Yaw - (HeadingFollow.YawFor(new V3(1, 0, 0)) - 0.2), Math.PI * 2);
        Check(moved > 0.02 && moved < 0.2, "turn with ship: catches up smoothly, the short way");
        hf.Turned(0.5);
        hf.Update(c3, new V3(1000, 0, 0), V3.Zero, 0);
        Check(Near(Math.IEEERemainder(c3.Yaw - HeadingFollow.YawFor(new V3(1, 0, 0)), Math.PI * 2), 0.5, 1e-9), "turn with ship: your own turn stays on top");
    }

    private static void WaypointClicks()
    {
        // the ray is the inverse of Project
        var cam = new OrbitCamera { Target = new V3(1e6, 2e5, -3e6), Distance = 5e5, Pitch = 0.6, Yaw = 0.4 };
        cam.Setup(0, 0, 1000, 600);
        var w = new V3(1.05e6, 2.2e5, -2.97e6);
        cam.Project(w, out var sx, out var sy, out var depth);
        var back = cam.Eye + cam.Ray(sx, sy) * (depth / cam.Ray(sx, sy).Dot(cam.Forward));
        Check(V3.Distance(back, w) < 1, "waypoint: screen ray goes back through the projected point");
        Check(RouteClick.PlaneHit(cam.Eye, cam.Ray(500, 300), cam.Target.Y, out var hit) && V3.Distance(hit, cam.Target) < 1, "waypoint: centre ray meets the target's plane at the target");

        // a click on the route's line lands on it; off it, level with the nearest route point
        var a = new V3(0.9e6, 2e5, -3e6); var b = new V3(1.1e6, 2e5, -3e6);
        var mid = new V3(1.0e6, 2e5, -3e6);
        cam.Project(mid, out var mx, out var my, out _);
        Check(RouteClick.Place(cam, new[] { a, b }, mx + 3, my, 12, out var on, out var onRoute) && onRoute && Math.Abs(on.Y - 2e5) < 1 && Math.Abs(on.Z + 3e6) < 1, "waypoint: click near the line is on it");
        Check(RouteClick.Place(cam, new[] { a, b }, mx, my + 120, 12, out var off, out var onRoute2) && !onRoute2 && Math.Abs(off.Y - 2e5) < 1, "waypoint: click off the line is level with the route");

        // height drag: the mouse above / below the base point on screen lifts / lowers it along the vertical
        var hbase = new V3(1.0e6, 2e5, -3e6);
        var up = new V3(1.0e6, 2.5e5, -3e6); cam.Project(up, out var ux, out var uy, out _);
        Check(RouteClick.HeightOnVertical(cam.Eye, cam.Ray(ux, uy), hbase, out var hup) && Math.Abs(hup.Y - 2.5e5) < 10 && Math.Abs(hup.X - 1e6) < 1e-3 && Math.Abs(hup.Z + 3e6) < 1e-3, "waypoint height: drag up lands 50 km above, same X/Z");
        var down = new V3(1.0e6, 1.2e5, -3e6); cam.Project(down, out var ddx, out var ddy, out _);
        Check(RouteClick.HeightOnVertical(cam.Eye, cam.Ray(ddx, ddy), hbase, out var hdn) && Math.Abs(hdn.Y - 1.2e5) < 10, "waypoint height: drag down lands 80 km below");
        Check(RouteClick.HeightOnVertical(cam.Eye, cam.Ray(ux, uy - 5000), hbase, out var hcap, 1e5) && Math.Abs(hcap.Y - (2e5 + 1e5)) < 1e-3, "waypoint height: capped at the limit");
        Check(!RouteClick.HeightOnVertical(new V3(0, 1e6, 0), new V3(0, -1, 0), new V3(10, 0, 0), out var hflat) && hflat.Y == 0, "waypoint height: straight-down view can't tell height");
        cam.Project(hbase, out var bsx, out var bsy, out _);
        Check(RouteClick.HeightOnVertical(cam.Eye, cam.Ray(bsx, bsy), hbase, out var hsame) && Math.Abs(hsame.Y - 2e5) < 1, "waypoint height: no drag, no change");

        // slot that adds the least distance
        var chain = new List<V3> { new V3(0, 0, 0), new V3(100e3, 0, 0), new V3(200e3, 0, 0) };
        Check(RouteClick.InsertIndex(chain, new V3(50e3, 1e3, 0)) == 0, "waypoint: goes into the first leg");
        Check(RouteClick.InsertIndex(chain, new V3(150e3, -1e3, 0)) == 1, "waypoint: goes into the second leg");

        // the tracker keeps them in route order and names them
        var t = new RouteTracker { Avoid = false };
        t.Update("Dest", new V3(300e3, 0, 0), true, new V3(0, 0, 0), new V3(0, 0, 0), 10, 0);
        Check(t.InsertUser(t.NextUserName(), new V3(200e3, 0, 0)) && t.InsertUser(t.NextUserName(), new V3(100e3, 0, 0)), "waypoint: two added");
        Check(t.UserPoints.Count == 2 && t.UserPoints[0].Name == "Waypoint 2" && t.UserPoints[1].Name == "Waypoint 1", "waypoint: the nearer one is flown first");
        Check(!t.InsertUser("dup", new V3(100e3, 50, 0)), "waypoint: no duplicate within 100 m");
        Check(t.Waypoints.Count == 2 && t.Waypoints[0].User && t.Path != null, "waypoint: route replanned through them");

        // raising one keeps its place and name, and the route goes through the new height
        int rev = t.Revision;
        Check(t.MoveUser(0, new V3(100e3, 40e3, 0)) && t.UserPoints[0].Name == "Waypoint 2" && t.UserPoints[0].Pos.Y == 40e3 && t.Revision > rev, "waypoint height: moved up, same slot, replanned");
        Check(Math.Abs(t.Waypoints[0].Pos.Y - 40e3) < 1, "waypoint height: route goes through the new height");
        Check(!t.MoveUser(0, new V3(200e3, 0, 0)) && !t.MoveUser(5, new V3(0, 1e3, 0)), "waypoint height: can't move onto another waypoint or a missing one");

        // leaving the seat drops the plan, and F&B's same pick doesn't bring it back; a new pick does
        t.Cancel("Dest", new V3(300e3, 0, 0));
        Check(t.TargetName == null && t.UserPoints.Count == 0 && t.Path == null, "cancel: plan and waypoints dropped");
        t.Update("Dest", new V3(300e3, 0, 0), true, new V3(0, 0, 0), new V3(0, 0, 0), 10, 0);
        Check(t.TargetName == null && t.Path == null, "cancel: F&B's unchanged pick isn't planned again");
        t.Update("Other", new V3(0, 0, 400e3), true, new V3(0, 0, 0), new V3(0, 0, 0), 10, 0);
        Check(t.TargetName == "Other" && t.Path != null, "cancel: a new pick plans a route again");

        var seat = new SeatWatch();
        Check(seat.Update(false, 7, false) == null && seat.Update(true, 7, false) == null, "seat: first look and sitting down cancel nothing");
        Check(seat.Update(true, 7, false) == null, "seat: staying seated cancels nothing");
        Check(seat.Update(false, 7, false) == "you left the seat", "seat: getting out cancels");
        Check(seat.Update(false, 7, false) == null, "seat: on foot after that, nothing more");
        Check(seat.Update(false, 7, true) == "you respawned", "seat: dying cancels");
        Check(seat.Update(false, 0, false) == null, "seat: no body while respawning, nothing more");
        Check(seat.Update(true, 9, false) == "you respawned", "seat: a new character (respawn ship) cancels");
        Check(seat.Update(true, 9, false) == null, "seat: then settled");
        seat.Reset();
        Check(seat.Update(false, 3, false) == null, "seat: a new session starts fresh");
    }

    private static void PoiMatching()
    {
        Check(PoiMatch.Rank("Mars", null, "Mars-1234567890d19000", 3000) == 1, "poi: planet entity name 'Mars-123...' matches Mars 3 km off");
        Check(PoiMatch.Rank("Mars", null, "Marshall Depot", 3000) != 1, "poi: 'Marshall' is not Mars");
        Check(PoiMatch.Rank("Tycho Station", "Belt.Tycho.STN", "Belt.Tycho.STN", 200) == 0, "poi: station by its GPS name");
        Check(PoiMatch.Rank("Ganymede Station", null, "JU.Ganymede.STN", 2e4) == 2, "poi: station by key word");
        Check(PoiMatch.Rank("Greek.Achilles.STN", null, "Greek.Achilles.STN", 50) == 0, "poi: exact GPS name");
        Check(PoiMatch.Rank("Vesta Station", "Belt.Vesta.STN", "Belt.Tycho.STN", 2e4) == PoiMatch.NoMatch, "poi: another Belt station isn't a match (belt is a generic word)");
        Check(PoiMatch.Rank("Somewhere", null, "Unrelated", 800) == 3 && PoiMatch.Rank("Mars", null, "Mars", 2e6) == PoiMatch.NoMatch, "poi: near anything within 5 km; names only within 1000 km");
    }

    private static void Render(string outDir, string sfx)
    {
        var earth = SystemData.FindWorld("Earth").Pos;
        var tychoPos = SystemData.Stations[3].Pos;
        var ship = earth - (tychoPos - earth).Normalized * 420e3 + new V3(0, 30e3, 0); // behind Earth: the straight line goes through it
        var snap = new MapSnapshot
        {
            HasShip = true, ShipName = "Rocinante", ShipPos = ship, ShipVel = new V3(900, 40, 2600), FnbReady = true, FnbState = "HighSpeed",
            FnbTargetName = "Belt.Tycho.STN", FnbTarget = SystemData.Stations[3].Pos, MaxAccel = 12.5,
        };
        snap.Gps.Add(new MapSnapshot.Marker { Name = "Ice Field", Pos = earth + new V3(500e3, -80e3, 300e3), Color = MapColor.Rgb(0x75C9FF) });
        snap.Gps.Add(new MapSnapshot.Marker { Name = "Old Wreck", Pos = earth + new V3(-200e3, 60e3, 420e3), Color = MapColor.Rgb(0xFFB060) });
        var tracker = new RouteTracker();
        tracker.Update(snap.FnbTargetName, snap.FnbTarget, true, snap.ShipPos, snap.ShipVel, snap.MaxAccel, 50000);
        var route = tracker.Path;
        Check(route.HasDetours, "preview route bends round Earth");
        int borders = 0; foreach (var l in route.Legs) borders += l.Crossings.Count;
        Check(borders >= 2, "preview route crosses into DX1 and DX6");
        // a flown track that strayed off to the side, and the projection back onto the route
        var start = ship - snap.ShipVel.Normalized * 150e3 + new V3(60e3, 0, -40e3);
        for (int i = 0; i <= 30; i++) { double t = i / 30.0; snap.Track.Add(V3.Lerp(start, ship, t) + new V3(0, 0, Math.Sin(t * Math.PI) * 40e3)); }
        var ahead = new List<V3>(); foreach (var w in route.Waypoints) ahead.Add(w.Pos); ahead.Add(route.Target);
        snap.PredictionArrives = Trajectory.Predict(ship, snap.ShipVel, ahead, 12.5, 50000, 25e3, snap.Prediction, route.Eta, phases: snap.PredictionPhases);
        Check(snap.PredictionArrives, "preview: projected track reaches the destination");

        // 1) full screen
        using (var bmp = new Bitmap(1920, 1080))
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.FromArgb(30, 34, 40));
            var scene = new MapScene { Snap = snap, Path = route, Help = "VIEW|Drag orbit" + MapScene.Sep + "Right-drag pan" + MapScene.Sep + "Wheel zoom" + MapScene.Sep + "Click focus" + MapScene.Sep + "S follow ship: off" + MapScene.Sep + "F frame route" + MapScene.Sep + "H whole system\nROUTE|Shift+click set destination" + MapScene.Sep + "Shift+right-click add / remove waypoint" + MapScene.Sep + "A autopilot" + MapScene.Sep + "N Flip and Burn list" + MapScene.Sep + "R re-plan" + MapScene.Sep + "V detours: on" + MapScene.Sep + "C copy flip GPS\nSHOW|G grid: on" + MapScene.Sep + "O orbits: on" + MapScene.Sep + "X sectors: on" + MapScene.Sep + "T stations: on" + MapScene.Sep + "P GPS: on" + MapScene.Sep + "L labels: on" + MapScene.Sep + "J moons: on" + MapScene.Sep + "K theme: Classic" + MapScene.Sep + "Q pin corner" + MapScene.Sep + "Esc / M close" };
            scene.Cam.Yaw = 0.9; scene.Cam.Pitch = 0.62;
            scene.Cam.Setup(0, 0, 1920, 1080);
            scene.FrameDefault(true);
            scene.MouseX = float.NaN;
            scene.Draw(new GdiMapCanvas(g), new Box(0, 0, 1920, 1080));
            Check(scene.Picks.Exists(p => p.Kind == "F&B destination"), "full map: destination is pickable");
            bmp.Save(Path.Combine(outDir, "full-map" + sfx + ".png"), ImageFormat.Png);

            // the same view while Shift+right-dragging a new waypoint 150 km above the route's first leg
            if (sfx == "")
            {
                g.Clear(Color.FromArgb(30, 34, 40));
                var pts = route.Points();
                scene.GhostBase = V3.Lerp(pts[0], pts[1], 0.5) + new V3(0, 0, 0);
                scene.Ghost = scene.GhostBase + new V3(0, 150e3, 0);
                scene.Draw(new GdiMapCanvas(g), new Box(0, 0, 1920, 1080));
                bmp.Save(Path.Combine(outDir, "full-map-height-drag.png"), ImageFormat.Png);
            }
        }
        // the autopilot PB's script-browser thumbnail (512 x 512): the map on the route, with the autopilot status line
        if (sfx == "-hud")
            using (var bmp = new Bitmap(512, 512))
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Black);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var c = new GdiMapCanvas(g);
                var scene = new MapScene { Snap = snap, Path = route, Compact = true, Mini = true, Help = "" };
                scene.Cam.Yaw = 0.9; scene.Cam.Pitch = 0.62;
                scene.Cam.Setup(0, 0, 512, 512);
                scene.FrameDefault(true);
                scene.MouseX = float.NaN;
                scene.Draw(c, new Box(0, 0, 512, 512));
                var st = new RouteExport.Status { State = "flying", Wp = 0, Count = 2, Dist = route.Distance * 0.9, Speed = 31200, Eta = 1860, Msg = "Flying to " + route.Waypoints[0].Name };
                AutopilotPanel.StatusLine(c, 512, 30, 14, route.TargetName, st, "Ctrl+M");
                bmp.Save(Path.Combine(outDir, "pb-autopilot-thumb.png"), ImageFormat.Png);
            }

        // the whole inner system (the Tachi's reference view)
        using (var bmp = new Bitmap(1920, 1080))
        using (var g = Graphics.FromImage(bmp))
        {
            var scene = new MapScene { Snap = snap, Path = route, Help = "" };
            scene.Cam.Yaw = 0.9; scene.Cam.Pitch = 0.5;
            scene.Cam.Setup(0, 0, 1920, 1080);
            scene.Cam.FlyTo(V3.Zero, 3.2e7); scene.Cam.Snap();
            scene.MouseX = float.NaN;
            scene.Draw(new GdiMapCanvas(g), new Box(0, 0, 1920, 1080));
            bmp.Save(Path.Combine(outDir, "system" + sfx + ".png"), ImageFormat.Png);
        }

        // 2) side window beside a mock of F&B's 600x500 navigation list
        using (var bmp = new Bitmap(1920, 1080))
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.FromArgb(52, 60, 70));
            using (var b = new SolidBrush(Color.FromArgb(230, 34, 48, 52))) g.FillRectangle(b, 660, 290, 600, 500);
            using (var b = new SolidBrush(Color.FromArgb(230, 60, 80, 70))) g.FillRectangle(b, 660, 290, 600, 40);
            using (var f = new Font("Segoe UI", 20, GraphicsUnit.Pixel))
            {
                g.DrawString("Navigation  (Flip and Burn's N list)", f, Brushes.White, 672, 296);
                string[] rows = { "Earth", "  Luna", "Belt.Tycho.STN   ← selected", "Belt.Vesta.STN", "Mars", "Ice Field (GPS)" };
                for (int i = 0; i < rows.Length; i++) g.DrawString(rows[i], f, i == 2 ? Brushes.LightGreen : Brushes.Gainsboro, 690, 350 + i * 34);
            }
            var scene = new MapScene { Snap = snap, Path = route, Compact = true, Help = "VIEW|Right-drag orbit" + MapScene.Sep + "Wheel zoom" + MapScene.Sep + "Middle-click re-frame\nKEYS|Shift+M map, full map, off" + MapScene.Sep + "Ctrl+M autopilot" + MapScene.Sep + "Alt+M pin on screen" };
            var box = SideBox(1920, 1080);
            scene.Cam.Yaw = 0.9; scene.Cam.Pitch = 0.62;
            scene.Cam.Setup(box.X, box.Y, box.W, box.H);
            scene.FrameDefault(true);
            scene.MouseX = scene.Picks.Count > 0 ? scene.Picks[0].X : float.NaN;
            scene.Draw(new GdiMapCanvas(g), box);
            bmp.Save(Path.Combine(outDir, "side-preview" + sfx + ".png"), ImageFormat.Png);
        }

        // 2b) picture-in-picture: the preview pinned top right over a plain game view
        using (var bmp = new Bitmap(1920, 1080))
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.FromArgb(52, 60, 70));
            var scene = new MapScene { Snap = snap, Path = route, Compact = true, Mini = true, Help = "" };
            var box = SideLayout.Pip(1920, 1080, 1, "TR");
            scene.Cam.Yaw = 0.9; scene.Cam.Pitch = 0.62;
            scene.Cam.Setup(box.X, box.Y, box.W, box.H);
            scene.FrameDefault(true);
            scene.MouseX = float.NaN;
            scene.Draw(new GdiMapCanvas(g), box);
            bmp.Save(Path.Combine(outDir, "pip" + sfx + ".png"), ImageFormat.Png);
        }

        // 3) the autopilot: status line, asteroid warning, confirmation under the N list, asteroid on the map
        using (var bmp = new Bitmap(1920, 1080))
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.FromArgb(52, 60, 70));
            using (var b = new SolidBrush(Color.FromArgb(230, 34, 48, 52))) g.FillRectangle(b, 660, 290, 600, 500);
            using (var f = new Font("Segoe UI", 20, GraphicsUnit.Pixel)) g.DrawString("Navigation  (Flip and Burn's N list)", f, Brushes.White, 672, 296);
            var c = new GdiMapCanvas(g);
            var leg = route.Waypoints[0].Pos;
            var rock = V3.Lerp(snap.ShipPos, leg, 0.4) + new V3(0, 1500, 0);
            var dodge = AsteroidDodge.PassPoint(snap.ShipPos, leg, rock, 3000);
            Check(dodge.HasValue, "render: the asteroid on the first leg gets a dodge point");
            snap.Asteroids.Add(new MapSnapshot.Marker { Name = "Asteroid", Pos = rock, Color = MapScene.HazardCol });
            snap.Dodges.Add(dodge.Value);
            var scene = new MapScene { Snap = snap, Path = route, Compact = true, Help = "Shift+M map, full map, off · Ctrl+M autopilot" };
            var box = SideBox(1920, 1080);
            scene.Cam.Yaw = 0.9; scene.Cam.Pitch = 0.62;
            scene.Cam.Setup(box.X, box.Y, box.W, box.H);
            scene.FrameDefault(true);
            scene.MouseX = float.NaN;
            scene.Draw(c, box);
            float px = Math.Max(14f, 1080 / 52f);
            var st = new RouteExport.Status { State = "flying", Wp = 0, Count = 3, Dist = route.Distance * 0.9, Speed = 31200, Eta = 2400, Msg = "Flying to " + route.Waypoints[0].Name };
            var line = AutopilotPanel.StatusLine(c, 1920, 54, px * 0.9f, route.TargetName, st, "Ctrl+M");
            AutopilotPanel.AsteroidWarning(c, 1920, line.Bottom + px * 0.5f, px, 1_250e3, 31200, true, true, true, 0.7);
            var list = SideLayout.FnbList(1920, 1080);
            // the route panel left of the list, with one of your own waypoints and a 60 % thrust cap
            var t2 = new RouteTracker();
            t2.Update(snap.FnbTargetName, snap.FnbTarget, true, snap.ShipPos, snap.ShipVel, snap.MaxAccel, 50000);
            t2.Adding = true;
            var ceres = SystemData.FindWorld("Ceres");
            t2.Update("Ceres", ceres != null ? ceres.Pos : earth + new V3(2e6, 0, 0), true, snap.ShipPos, snap.ShipVel, snap.MaxAccel, 50000);
            var lim = new RouteLimits { MaxSpeed = 30000, ThrustPct = 60 };
            t2.Adding = false; t2.AccelScale = lim.ThrustFraction(14); t2.SpeedCap = lim.MaxSpeed;
            t2.Update("Ceres", ceres != null ? ceres.Pos : earth + new V3(2e6, 0, 0), true, snap.ShipPos, snap.ShipVel, snap.MaxAccel, 50000);
            var ui = new PanelUi();
            ui.Input(c, 500, 640, false, false, false);
            var names = new List<string>(); foreach (var u in t2.UserPoints) names.Add(u.Name);
            float pw = Math.Min(list.X - 1080 * 0.044f, px * 32);
            var hit = AutopilotPanel.RoutePanel(ui, list.X - 1080 * 0.022f - pw, list.Y, pw, 1080, px, t2.Path, lim, 50000, snap.MaxAccel, new SignatureInfo { Weight = 14, Floor = DriveSignature.StrengthForKm(50), FloorWhat = "antenna broadcast", LiveAllRound = DriveSignature.StrengthForKm(50), LiveCone = 0 }, names, false, "Ctrl+M", 0.2);
            Check(hit.Panel.Bottom <= 1080 && hit.Panel.X >= 0 && hit.Panel.Right < list.X, "route panel fits left of the list");
            Check(!hit.Engage && !hit.Cancel && hit.Remove < 0 && !hit.Changed, "no clicks: nothing happens");
            var lines = AutopilotPanel.ConfirmLines(t2.Path, "Ctrl+M");
            Check(lines.Exists(l => l.Text.Contains("short of")) && lines.Exists(l => l.Text.Contains("1 waypoint")), "route lines say where it stops and count your waypoint");
            // under the list on the plain picture: the two choices
            AutopilotPanel.Choice(ui, 960, list.Bottom + px * 0.8f, px, true, out _, out _);
            bmp.Save(Path.Combine(outDir, "autopilot" + sfx + ".png"), ImageFormat.Png);
            snap.Asteroids.Clear(); snap.Dodges.Clear();
        }
        Console.WriteLine("previews written to " + outDir);
    }

    /// <summary>Same layout rule as the plugin's SidePreview: right of centre, clear of F&amp;B's 600 px wide list.</summary>
    private static Box SideBox(float w, float h) => SideLayout.Box(w, h, 1f);
}

/// <summary>A canvas that draws nothing and remembers where text went (for clicking things by their label).</summary>
internal sealed class MeasureCanvas : IMapCanvas
{
    private readonly List<(string Text, float X, float Y)> texts = new List<(string, float, float)>();
    public void Rect(float x, float y, float w, float h, MapColor col, float rotation = 0) { }
    public void Line(float x1, float y1, float x2, float y2, float width, MapColor col) { }
    public void Disc(float cx, float cy, float r, MapColor col) { }
    public void Text(string text, float x, float y, float px, MapColor col, TextAnchor anchor = TextAnchor.Left) => texts.Add((text, x, y));
    public float TextWidth(string text, float px) => text.Length * px * 0.5f;
    public void PushClip(float x, float y, float w, float h) { }
    public void PopClip() { }
    /// <summary>Where the n-th text equal to s was drawn in the last frame (texts reset when one is found again from the start).</summary>
    public (float X, float Y) Find(string s, int n)
    {
        int k = 0;
        for (int i = texts.Count - 1; i >= 0; i--) if (texts[i].Text == "AUTOPILOT  to Far") { texts.RemoveRange(0, i); break; }
        foreach (var t in texts) if (t.Text == s && k++ == n) return (t.X, t.Y);
        return (-1000, -1000);
    }
}
