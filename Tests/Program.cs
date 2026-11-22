using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
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
        else if (Environment.GetEnvironmentVariable("SHOWALL") == "1") Console.WriteLine("ok    " + what);
    }

    private static bool Near(double a, double b, double tol) => Math.Abs(a - b) <= tol;

    /// <summary>
    /// Made-up station GPS points for the checks and previews (the plugin knows only the stations in a player's own GPS
    /// list). Placed by rule from the worlds, not at the server's real spots: on a world's ground, just off it, or out
    /// in a field.
    /// </summary>
    private static List<KeyValuePair<string, V3>> TestStations()
    {
        var list = new List<KeyValuePair<string, V3>>();
        void At(string gps, V3 pos) => list.Add(new KeyValuePair<string, V3>(gps, pos));
        void On(string gps, string world, V3 dir, double height) { var w = SystemData.FindWorld(world); At(gps, w.Pos + dir.Normalized * (w.Radius + height)); }
        On("Belt.2Pallas.STN", "Pallas", new V3(1, 0.4, -0.3), 0);
        On("Belt.Eros.STN", "Eros", new V3(0.2, 1, 0.3), 2500);
        On("Belt.Hygeia.STN", "Hygeia", new V3(-0.5, 0.3, 1), -2000); // inside it
        At("Belt.Tycho.STN", new V3(-6010e3, -100e3, 6010e3));        // out in the Belt
        On("Belt.Vesta.STN", "Vesta", new V3(1, 0.2, 0.6), 2000);
        On("Ve.Andromeda.STN", "Venus", new V3(-1, -0.1, -0.8), 60e3);
        On("JU.Ganymede.STN", "Ganymede", new V3(0.6, -0.3, -1), 0);
        On("JU.IoHeliumRef.STN", "Io", new V3(0.3, 0.9, 0.4), 0);
        On("SA.Enceladus.STN", "Enceladus", new V3(-0.2, 1, 0.1), 0);
        On("SA.Iapetus.STN", "Iapetus", new V3(-0.4, 0.2, -1), 5000);
        On("SA.Mimas.STN", "Mimas", new V3(1, -0.1, -0.5), 4000);
        On("SA.Rhea.STN", "Rhea", new V3(-0.5, -0.9, -0.2), 0);
        On("NE.TriDALab.STN", "Triton", new V3(-1, 0.3, 0.2), 0);
        At("Greek.Achilles.STN", new V3(-14488887 + 500e3, -20e3, 3882285 - 150e3));
        At("Tro.Paris.STN", new V3(-6339273 - 200e3, 40e3, -13594616 + 600e3));
        At("CE.Aventine.STN", SystemData.FindWorld("Ceres").Pos + new V3(-400e3, 250e3, -200e3));
        return list;
    }

    private static int Main(string[] args)
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string outDir = args.Length > 0 ? args[0] : Path.Combine(root, "previews");
        Directory.CreateDirectory(outDir);
        SystemData.SetKnownStations(TestStations()); // the plugin has none built in: these stand in for a player's GPS points

        Sectors();
        Burns();
        Crossings();
        Camera();
        Labels();
        Detours();
        Fields();
        Tracker();
        Export();
        UserWaypoints();
        Limits();
        PanelInput();
        PadFocus();
        Trajectories();
        Render(outDir, "");
        MapTheme.Current = MapTheme.Tachi;
        Render(outDir, "-hud");
        MapTheme.Current = MapTheme.Unn;
        Render(outDir, "-unn");
        MapTheme.Current = MapTheme.Belter;
        Render(outDir, "-belter");
        MapTheme.Current = MapTheme.Protogen;
        Render(outDir, "-protogen");
        MapTheme.Current = MapTheme.Opa;
        Render(outDir, "-opa");
        ShipIconSheet(outDir);
        FeedSheet(outDir);
        MapTheme.Current = MapTheme.Classic;
        Themes();
        HoverOnTop();
        PoiMatching();
        WaypointClicks();
        TurnWithShip();
        ShipLabelSteady();
        Curves();
        Touch();
        Recorded(outDir);

        Console.WriteLine($"{checks - fails}/{checks} checks passed");
        return fails == 0 ? 0 : 1;
    }

    private static void Sectors()
    {
        Check(SystemData.SectorAt(SystemData.FindWorld("Earth").Pos).Dx == "DX4", "Earth is in DX4");
        Check(SystemData.SectorAt(new V3(8.5e6, 0, 0)).Dx == "DX6", "on the Belt's ring: DX6");
        Check(SystemData.SectorAt(new V3(8.5e6, 600e3, 0)).Dx == "DX1", "above the Belt's tube: DX1");
        Check(SystemData.SectorAt(V3.Zero).Dx == "DX1", "Sol: DX1");
        var beltSector = Array.Find(SystemData.Sectors, x => x.Dx == "DX6");
        Check(SystemData.Belt.Major == beltSector.Radius && SystemData.Belt.Minor == beltSector.HalfWidth, "Skylark's Belt torus matches DX6's sector");
        Check(SystemData.SectorAt(SystemData.Belt.Center + new V3(SystemData.Belt.Major, SystemData.Belt.Minor * 0.9, 0)).Dx == "DX6", "inside the Skylark tube: DX6");
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
        // the wheel: the game's total reset to 0 on focus loss (Windows key) isn't a huge zoom either way
        Check(OrbitCamera.WheelNotches(0, 6000) == 0 && OrbitCamera.WheelNotches(6000, 0) == 0, "wheel: the total's reset on focus loss is ignored");
        Check(OrbitCamera.WheelNotches(6120, 6000) == 1 && OrbitCamera.WheelNotches(120, 0) == 1 && OrbitCamera.WheelNotches(-120, 0) == -1, "wheel: a notch zooms one step");
        Check(OrbitCamera.WheelNotches(9000, 1200) == OrbitCamera.MaxWheelNotches, "wheel: one frame zooms at most a few notches");
        Check(MapScene.FocusDistance(new Pick { Name = "Earth", Kind = "Planet" }) > SystemData.FindWorld("Earth").Radius * 15
            && MapScene.FocusDistance(new Pick { Name = "Trojans", Kind = MapScene.FieldKind }) > 1500e3 * 3, "click focus stands back far enough to see a world or field whole");
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

    /// <summary>Nearest a route (start, points, end) comes to the Belt's tube centreline (sampled).</summary>
    private static double BeltLowest(V3 start, List<Waypoint> wps, V3 end)
    {
        var pts = new List<V3> { start }; foreach (var p in wps) pts.Add(p.Pos); pts.Add(end);
        double min = double.MaxValue;
        for (int i = 0; i + 1 < pts.Count; i++)
            for (int k = 0; k <= 2000; k++) min = Math.Min(min, SystemData.BeltDistance(pts[i] + (pts[i + 1] - pts[i]) * (k / 2000.0)));
        return min;
    }

    private static double RouteLength(V3 start, List<Waypoint> wps, V3 end)
    {
        double len = 0; V3 a = start;
        foreach (var p in wps) { len += V3.Distance(a, p.Pos); a = p.Pos; }
        return len + V3.Distance(a, end);
    }

    private static void Fields()
    {
        var earth = SystemData.FindWorld("Earth"); var jupiter = SystemData.FindWorld("Jupiter");
        V3 from = earth.Pos + new V3(1000e3, 0, 0), to = jupiter.Pos + new V3(0, 0, 3000e3);
        double m = SystemData.Belt.Minor + DetourPlanner.FieldGap;
        var plain = DetourPlanner.Plan(from, to, "J");
        var keep = DetourPlanner.Plan(from, to, "J", fields: true);
        double inPlain = BeltLowest(from, plain, to), inKeep = BeltLowest(from, keep, to);
        Check(inPlain < SystemData.Belt.Minor, "belt: without the option, Earth to Jupiter goes through the Belt (" + Km(inPlain) + " from its middle)");
        Check(inKeep >= SystemData.Belt.Minor + DetourPlanner.FieldGap * 0.9 && keep.Exists(w => w.Around == SystemData.BeltField),
              "belt: with it, over or under the tube (" + Km(inKeep - SystemData.Belt.Minor) + " outside, " + keep.Count + " points)");
        double extra = RouteLength(from, keep, to) - RouteLength(from, plain, to);
        Check(extra < 600e3, "belt: going over it costs little (" + Km(extra) + " more)");
        // in the plane, straight across: over (or under) the tube
        V3 inner = new V3(6000e3, 0, 0), outer = new V3(11000e3, 0, 0);
        var flatCross = DetourPlanner.Plan(inner, outer, "O", fields: true);
        Check(BeltLowest(inner, flatCross, outer) >= m * 0.9 && flatCross.Count >= 1, "belt: a crossing in its plane goes over it (" + flatCross.Count + " points)");
        // starting in the Belt (Tycho Station): can't keep out of it, flown as before
        var tycho = Array.Find(SystemData.Stations, x => x.Name == "Tycho Station");
        var fromTycho = DetourPlanner.Plan(tycho.Pos + new V3(0, 0, 10e3), to, "J", fields: true);
        Check(!fromTycho.Exists(w => w.Around == SystemData.BeltField), "belt: starting in it, no detour round it");
        // through the Trojans: round the field's sphere
        var tro = Array.Find(SystemData.Fields, f => f.Name == "Trojans");
        V3 ta = tro.Pos + new V3(-4000e3, 0, 0), tb = tro.Pos + new V3(4000e3, 0, 0);
        var round = DetourPlanner.Plan(ta, tb, "T", fields: true);
        double trLow = LowestOver(ta, round, tb, tro);
        Check(trLow >= tro.Radius + DetourPlanner.FieldGap - 1 && trLow < tro.Radius + DetourPlanner.FieldGap * 4, "trojans: gone round (" + Km(trLow - tro.Radius) + " outside)");
        Check(DetourPlanner.Plan(ta, tb, "T").Count == 0, "trojans: without the option, straight through");
        // to a station in the Greeks: flown into
        var achilles = Array.Find(SystemData.Stations, x => x.Name == "Achilles Station");
        var toGreek = DetourPlanner.Plan(achilles.Pos + new V3(0, 0, 4000e3), achilles.Pos, "A", fields: true);
        Check(!toGreek.Exists(w => w.Around != null && w.Around.Name == "Greeks"), "greeks: flying to a station in them, not gone round");
        // a removed detour (Shift+right-click) stays removed
        var ign = DetourPlanner.Plan(from, to, "J", ignore: new HashSet<string> { "The Belt" }, fields: true);
        Check(!ign.Exists(w => w.Around == SystemData.BeltField), "belt: its detour removed, flown through");
    }

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
        var sol = SystemData.Worlds[0];
        var acrossSol = DetourPlanner.Plan(sol.Pos + new V3(-1.5e6, 30e3, 0), sol.Pos + new V3(1.5e6, -20e3, 0), "X");
        double solLow = LowestOver(sol.Pos + new V3(-1.5e6, 30e3, 0), acrossSol, sol.Pos + new V3(1.5e6, -20e3, 0), sol);
        Check(sol.IsStar && acrossSol.Exists(w => w.Around == SystemData.SolZone) && solLow >= SystemData.SolNoFly - 1 && solLow < SystemData.SolNoFly * 1.3,
              "Sol: gone round outside its 800 km no-fly sphere (" + Km(solLow) + " from its centre)");
        var inSol = sol.Pos + new V3(20e3, 20e3, 20e3); var pastSol = sol.Pos + new V3(-3e6, 0, 0);
        var outSol = DetourPlanner.Plan(inSol, pastSol, "X");
        Check(outSol.Count >= 1 && V3.Distance(outSol[0].Pos, sol.Pos) >= SystemData.SolNoFly - 1
              && LowestOver(outSol[0].Pos, outSol.GetRange(1, outSol.Count - 1), pastSol, sol) >= SystemData.SolNoFly - 1,
              "starting inside Sol's no-fly: out first, then round it (" + outSol.Count + " points)");
        var solBase = SystemData.Obstacle("Base", inSol + new V3(300, 0, 0), 500);
        var fromBase = DetourPlanner.Plan(inSol, pastSol, "X", 100e3, new List<SystemData.World> { solBase });
        Check(fromBase.Count >= 2 && fromBase[0].Around == solBase && fromBase[1].Around == SystemData.SolZone,
              "base inside Sol's no-fly: out of the base, then out of Sol (" + string.Join(", ", fromBase.ConvertAll(w => w.Around?.Name)) + ")");
        Check(DetourPlanner.Plan(sol.Pos + new V3(0, 0, 3e6), sol.Pos + new V3(0, 0, 100e3), "Sol").TrueForAll(w => w.Around != SystemData.SolZone), "flying to Sol: not gone round");
        var solStop = RouteExport.StopNear(sol.Pos + new V3(0, 0, 3e6), sol.Pos, 100e3, out var solWhy);
        Check(solWhy == "sol" && Near(V3.Distance(solStop, sol.Pos), SystemData.SolNoFly, 1) && solStop.Z > sol.Pos.Z, "a route to Sol stops at 800 km, your side");
        var venus = Array.Find(SystemData.Worlds, w => w.Name == "Venus");
        var acrossVenus = DetourPlanner.Plan(venus.Pos + new V3(-1.5e6, 0, 0), venus.Pos + new V3(1.5e6, 0, 0), "X");
        double venusLow = LowestOver(venus.Pos + new V3(-1.5e6, 0, 0), acrossVenus, venus.Pos + new V3(1.5e6, 0, 0), venus);
        Check(!venus.IsStar && venusLow >= venus.Radius + 100e3 - 1 && venusLow < venus.Radius + 150e3, "planets: still the usual clearance (" + Km(venusLow - venus.Radius) + " above Venus)");
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
        // taking off for somewhere off to the side: the climb leans toward it and meets the way on at a tangent,
        // skimming the clearance instead of going straight up and then round with room to spare
        {
            var side = earth.Pos + new V3(300e3, 0, 600e3); // past the horizon, off to the side
            var lean = DetourPlanner.Plan(ground, side, "S");
            // (first straight up over the hills, then the climb)
            int ci = lean.FindIndex(q => Math.Abs(V3.Distance(q.Pos, earth.Pos) - (R + 100e3)) < 1);
            double leanDeg = ci >= 0 ? Math.Acos((lean[ci].Pos - earth.Pos).Normalized.Dot((ground - earth.Pos).Normalized)) * 180 / Math.PI : 0;
            Check(ci >= 0 && leanDeg > 5 && (lean[ci].Pos - ground).Normalized.Dot((side - ground).Normalized) > 0,
                  "take-off to the side: climbs leaning toward it (" + leanDeg.ToString("0") + " deg round), to the clearance");
            double lowLean = ci < 0 ? 0 : LowestOver(lean[ci].Pos, lean.GetRange(ci + 1, lean.Count - ci - 1), side, earth);
            Check(lowLean >= R + 80e3 && lowLean < R + 116e3, "take-off to the side: then skims the clearance (" + Km(lowLean - R) + " up)");
        }
        // a known gravity well: worlds are gone round about 5 km outside it, not by the clearance setting
        {
            double wasWell = earth.Well;
            earth.Well = R * 1.6;
            var across = DetourPlanner.Plan(earth.Pos + new V3(-900e3, 0, 0), earth.Pos + new V3(900e3, 0, 0), "X");
            double lowW = LowestOver(earth.Pos + new V3(-900e3, 0, 0), across, earth.Pos + new V3(900e3, 0, 0), earth);
            double corner = 0; foreach (var wp in across) corner = Math.Max(corner, V3.Distance(wp.Pos, earth.Pos));
            Check(across.Count >= 1 && lowW >= earth.Well && lowW <= earth.Well + DetourPlanner.WellGap * 1.2,
                  "gravity well: the route stays outside it (" + Km(lowW - earth.Well) + " outside)");
            Check(across.Count >= 1 && corner < earth.Well + DetourPlanner.WellGap * 2, "gravity well: corners a few km outside it (" + Km(corner - earth.Well) + ")");
            var wellUp = DetourPlanner.Plan(ground, farSide, "F");
            Check(wellUp.Count >= 2 && Math.Abs(V3.Distance(wellUp[0].Pos, earth.Pos) - (earth.Well + DetourPlanner.WellGap)) < 1, "gravity well: take-off climbs to 5 km outside it");
            earth.Well = wasWell;
        }
        // the ship's thrust against the world's gravity (1.5x margin): as low as 3 km over the hills when it holds,
        // higher where it doesn't, outside the well when it can't hold anywhere in it; above any atmosphere
        try
        {
            earth.Well = R * 1.6; earth.Hills = R * 1.04; earth.SurfaceG = 9.81; earth.Falloff = 7;
            var ea = earth.Pos + new V3(-900e3, 0, 0); var eb = earth.Pos + new V3(900e3, 0, 0);
            double Lowest(double accel) { DetourPlanner.ShipAccel = accel; var wp = DetourPlanner.Plan(ea, eb, "X"); return wp.Count == 0 ? 0 : LowestOver(ea, wp, eb, earth); }
            double strong = Lowest(20), want = earth.Hills + DetourPlanner.SurfaceGap;
            Check(strong >= want - 1 && strong < want + 3e3, "thrust holds (20 m/s² vs 1 g): round Earth 3 km over its hills (" + Km(strong - earth.Hills) + " over them)");
            double weakR = DetourPlanner.SafeRadius(earth, 5), weak = Lowest(5);
            Check(Near(weakR, earth.Hills * Math.Pow(1.5 * 9.81 / 5, 1 / 7.0), 1) && weak >= weakR - 1 && weak < weakR + 3e3
                  && DetourPlanner.GravityAt(earth, weakR) * DetourPlanner.GravityMargin <= 5 + 1e-6,
                  "weak thrust (5 m/s²): only as low as 1.5x the pull is held (" + Km(weak - R) + " up, " + (DetourPlanner.GravityAt(earth, weak) / 9.81).ToString("0.00") + " g)");
            double none = Lowest(0.5);
            Check(none >= earth.Well && none <= earth.Well + DetourPlanner.WellGap * 1.2, "too weak to hold anywhere in the well: outside it (" + Km(none - earth.Well) + " outside)");
            double unknown = Lowest(0);
            Check(unknown >= earth.Well, "thrust not known: outside the well");
            DetourPlanner.ShipAccel = 20;
            var tUp = DetourPlanner.Plan(ground, farSide, "F");
            Check(tUp.Count >= 1 && Near(V3.Distance(tUp[0].Pos, earth.Pos), want, 1), "thrust holds: take-off climbs to 3 km over the hills (" + Km(V3.Distance(tUp[0].Pos, earth.Pos) - R) + " up)");
            var tDown = DetourPlanner.Plan(lowStart, earth.Pos + new V3(101e3, 0, 0), "B");
            Check(tDown.Count >= 1 && Near(V3.Distance(tDown[tDown.Count - 1].Pos, earth.Pos), want, 1), "thrust holds: landing comes down from 3 km over the hills");
            var gStop = RouteExport.StopNear(earth.Pos + new V3(0, 0, 2e6), earth.Pos, 100e3, out var gWhy);
            Check(gWhy == "well" && Near(V3.Distance(gStop, earth.Pos), earth.Well + RouteExport.WellMargin, 1), "pick Earth, thrust holds: still stops just outside its well (" + gWhy + ")");
            // a base on Earth's ground: straight above it, 3 km up when the thrust holds there
            var eBase = earth.Pos + new V3(0, R * 1.01, 0);
            var bStop = RouteExport.StopNear(earth.Pos + new V3(0, 0, 2e6), eBase, 100e3, out var bWhy);
            Check(bWhy == "surface" && Near(V3.Distance(bStop, eBase), DetourPlanner.SurfaceGap, 1) && (bStop - eBase).Normalized.Dot((eBase - earth.Pos).Normalized) > 0.9999,
                  "base on Earth, thrust holds: stops 3 km straight above it (" + Km(V3.Distance(bStop, eBase)) + ")");
            var oStop = RouteExport.StopNear(earth.Pos + new V3(0, 0, 2e6), eBase, 100e3, out _, 7000);
            Check(Near(V3.Distance(oStop, eBase), 7000, 1), "base on Earth, offset 7 km: stops 7 km above it");
            var o2Stop = RouteExport.StopNear(earth.Pos + new V3(0, 0, 2e6), eBase, 100e3, out _, 500);
            Check(Near(V3.Distance(o2Stop, eBase), DetourPlanner.SurfaceGap, 1), "base on Earth, offset 0.5 km: still 3 km above it");
            DetourPlanner.ShipAccel = 5;
            var wkStop = RouteExport.StopNear(earth.Pos + new V3(0, 0, 2e6), eBase, 100e3, out _);
            Check(Near(V3.Distance(wkStop, earth.Pos), earth.Hills * Math.Pow(1.5 * 9.81 / 5, 1 / 7.0), 1), "base on Earth, weak thrust: stops where 1.5x the pull is held (" + Km(V3.Distance(wkStop, eBase)) + " up)");
            DetourPlanner.ShipAccel = 20;
            DetourPlanner.ShipAccel = 0.5;
            var wStop2 = RouteExport.StopNear(earth.Pos + new V3(0, 0, 2e6), earth.Pos, 100e3, out _);
            Check(V3.Distance(wStop2, earth.Pos) > earth.Well, "pick Earth, too weak: stops outside the well");
            Check(DetourPlanner.HoldsAtSurface(earth, 14.8) && !DetourPlanner.HoldsAtSurface(earth, 14.6) && !DetourPlanner.HoldsAtSurface(earth, 0), "holds at the surface: 1.5 g of thrust for 1 g");
            // an atmosphere: kept above it, whatever the thrust
            earth.Atmosphere = R * 1.3;
            double atmo = Lowest(20);
            Check(atmo >= earth.Atmosphere + DetourPlanner.SurfaceGap - 1 && atmo < earth.Atmosphere + 6e3, "atmosphere: route stays above it (" + Km(atmo - earth.Atmosphere) + " over its top)");
            DetourPlanner.ShipAccel = 20;
            var aUp = DetourPlanner.Plan(ground, farSide, "F");
            Check(aUp.Count >= 1 && Near(V3.Distance(aUp[0].Pos, earth.Pos), earth.Atmosphere + DetourPlanner.SurfaceGap, 1), "atmosphere: take-off climbs out of it first");
            earth.Atmosphere = R * 1.7; // past the well: outside the well, and the atmosphere's top is the limit still
            double thick = Lowest(20);
            Check(thick >= earth.Atmosphere + DetourPlanner.SurfaceGap - 1, "atmosphere past the well: still above the atmosphere (" + Km(thick - R) + " up)");
        }
        finally { earth.Well = 0; earth.Hills = earth.SurfaceG = earth.Falloff = earth.Atmosphere = 0; DetourPlanner.ShipAccel = 0; }
        // a hop by a small moon's ground (the Rhea crash): from low over it to 3 km above a station 50 degrees round.
        // The straight line stayed over its radius but went through a hill (hills reach 6% up): now over the hills
        {
            var rhea = SystemData.FindWorld("Rhea"); double rr = rhea.Radius;
            V3 Dir(double deg) => new V3(Math.Cos(deg * Math.PI / 180), 0, Math.Sin(deg * Math.PI / 180));
            var lowShip = rhea.Pos + Dir(0) * (rr * 1.04); var aboveStn = rhea.Pos + Dir(50) * (rr * 1.28);
            var hop = DetourPlanner.Plan(lowShip, aboveStn, "Rhea Station");
            Check(hop.Count >= 1 && V3.Distance(hop[0].Pos, rhea.Pos + Dir(0) * DetourPlanner.HillTop(rhea)) < 1, "moon hop: climbs straight up over the hills first (" + hop.Count + " points)");
            Check(hop.Count >= 1 && LowestOver(hop[0].Pos, hop.GetRange(1, hop.Count - 1), aboveStn, rhea) >= DetourPlanner.Top(rhea) - 1,
                  "moon hop: then stays over the hills (" + Km(LowestOver(hop[0].Pos, hop.GetRange(1, hop.Count - 1), aboveStn, rhea) - rr) + " up, hills to " + Km(rr * 0.06) + ")");
            // two bases on its ground, opposite sides: over it, not through it
            var b1 = rhea.Pos + Dir(0) * (rr * 1.01); var b2 = rhea.Pos + Dir(180) * (rr * 1.01);
            var across = DetourPlanner.Plan(b1, b2, "B2");
            Check(across.Count >= 3 && LowestOver(across[0].Pos, across.GetRange(1, across.Count - 2), across[across.Count - 1].Pos, rhea) >= DetourPlanner.Top(rhea) - 1
                  && V3.Distance(across[across.Count - 1].Pos, rhea.Pos + Dir(180) * DetourPlanner.HillTop(rhea)) < 1,
                  "base to base across a moon: up, over the hills, down from above (" + across.Count + " points)");
            // Rhea Station is a ground facility: routes to it stop 8 km straight above it, and it gets no orbit line
            var rs = Array.Find(SystemData.Stations, x => x.Name == "Rhea Station");
            var rsStop = RouteExport.StopNear(rhea.Pos + Dir(90) * 60e3, rs.Pos, 100e3, out var rsWhy);
            Check(rs.Above == 8000 && rsWhy == "station" && Near(V3.Distance(rsStop, rs.Pos), 8000, 1) && (rsStop - rs.Pos).Normalized.Dot((rs.Pos - rhea.Pos).Normalized) > 0.9999,
                  "Rhea Station: stops 8 km straight above it (" + Km(V3.Distance(rsStop, rs.Pos)) + ")");
            // every station within 5% of a world's radius from its centre (on its ground) is one; the rest aren't
            foreach (var st in SystemData.Stations)
            {
                bool onGround = st.Name == "Hygeia Station" || Array.Exists(SystemData.Worlds, w => !w.IsStar && V3.Distance(st.Pos, w.Pos) > w.Radius * 0.9 && V3.Distance(st.Pos, w.Pos) < Math.Max(w.Radius * 1.05, w.TallHills)); // Hygeia's is inside it: treated as on it for now
                var under = RouteExport.WorldUnder(st.Pos, 100e3);
                var gStop = RouteExport.StopNear(st.Pos + new V3(0, 0, 500e3), st.Pos, 100e3, out _);
                // (on an asteroid: no gravity, so 3 km above it; elsewhere, gravity not read here: its own 8 km)
                double wantUp = under != null && under.IsAsteroid && V3.Distance(st.Pos, under.Pos) >= under.Radius * 0.99 ? DetourPlanner.SurfaceGap : 8000; // (buried, like Hygeia's: 8 km)
                Check(onGround == (st.Above == 8000) && (!onGround || under != null && Near(V3.Distance(gStop, st.Pos), wantUp, 1) && (under.IsAsteroid || V3.Distance(gStop, under.Pos) > DetourPlanner.Top(under))),
                      st.Name + ": " + (onGround ? "ground facility, stops " + Km(wantUp) + " straight above (on " + under?.Name + ")" : "ordinary station"));
            }
            // Vesta's hills reach twice its radius, known before the planet is loaded: a station among them is a ground
            // facility, and a route from far off comes down to it from over the hills (a frigate flew into them, 2026-11-20)
            {
                var vesta = SystemData.FindWorld("Vesta");
                var vs = Array.Find(SystemData.Stations, x => x.Name == "Vesta Station");
                V3 vfar = vesta.Pos + new V3(-300e3, 40e3, -7000e3);
                var vdet = DetourPlanner.Plan(vfar, vs.Pos, vs.Name);
                var vp = RoutePath.Make(vs.Name, vfar, vs.Pos, V3.Zero, 100, 50000, vdet, 0, 100e3, 100);
                var vstop = RouteExport.StopPoint(vp, out var vwhy);
                Check(vesta.Hills == 0 && DetourPlanner.Top(vesta) == 12000 && DetourPlanner.HillTop(vesta) == 15000 && vs.Above > 0 && DetourPlanner.OnGround(vesta, vs.Pos),
                      "Vesta: its hills reach 12 km before it's loaded; a station among them is a ground facility");
                Check(vwhy == "station" && Near(V3.Distance(vstop, vs.Pos), DetourPlanner.SurfaceGap, 1) && (vstop - vs.Pos).Normalized.Dot((vs.Pos - vesta.Pos).Normalized) > 0.9999,
                      "Vesta Station: stops 3 km straight above it (" + Km(V3.Distance(vstop, vs.Pos)) + ")");
                double lowest = double.MaxValue; V3 at = vfar;
                foreach (var wp in vdet) { lowest = Math.Min(lowest, V3.SegmentDistance(at, wp.Pos, vesta.Pos, out _)); at = wp.Pos; }
                Check(vdet.Count >= 1 && lowest >= 12000 && V3.Distance(at, vesta.Pos) >= 15000 - 1
                      && (at - vesta.Pos).Normalized.Dot((vs.Pos - vesta.Pos).Normalized) > Math.Cos(DetourPlanner.MaxLean),
                      "Vesta Station from far off: over the hills all the way (lowest " + Km(lowest) + "), then down from above it");
            }
            // two ships sent to one place don't get one stop: the second and third stop beside it (three flew into each
            // other at one station's stop, 2026-11-20)
            {
                var tycho = Array.Find(SystemData.Stations, x => x.Name == "Tycho Station");
                V3 sFrom = tycho.Pos + new V3(400e3, 20e3, -90e3);
                var sPath = RoutePath.Make(tycho.Name, sFrom, tycho.Pos, V3.Zero, 10, 50000, new List<Waypoint>(), 0, 100e3, 10);
                var sHad = RouteExport.Taken;
                try
                {
                    RouteExport.Taken = null;
                    var s0 = RouteExport.StopPoint(sPath, out _, out double a0);
                    RouteExport.Taken = new List<(V3, double)> { (s0 + new V3(900, 0, 0), 0) };
                    Check(a0 == 0 && V3.Distance(RouteExport.StopPoint(sPath, out _), s0) < 1e-6, "stops: nobody else there, or only 900 m off: the stop itself");
                    RouteExport.Taken = new List<(V3, double)> { (s0 + new V3(30, -20, 10), 0) };
                    var s1 = RouteExport.StopPoint(sPath, out var w1, out double a1);
                    Check(w1 == "station" && Near(a1, RouteExport.StopSpacing, 1e-6) && Math.Abs((s1 - s0).Dot((s0 - tycho.Pos).Normalized)) < 1e-6 && V3.Distance(s1, tycho.Pos) > RouteExport.Offset,
                          "stops: another ship's route stops there: 500 m to the side, no nearer the station");
                    RouteExport.Taken = new List<(V3, double)> { (s0, 0), (s1, 0) };
                    var s2 = RouteExport.StopPoint(sPath, out _);
                    Check(V3.Distance(s2, s0) >= RouteExport.StopSpacing - 1e-6 && V3.Distance(s2, s1) >= RouteExport.StopSpacing * 0.8, "stops: a third ship takes the next place");
                    Check(RouteExport.Build(sPath, 5).Contains("wp=" + s2.X.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)), "stops: the place beside is what the autopilot is sent");
                    RouteExport.Taken = new List<(V3, double)> { (s0 + new V3(0, 100, 0), 600) };
                    var s3 = RouteExport.StopPoint(sPath, out _);
                    Check(V3.Distance(s3, s0 + new V3(0, 100, 0)) >= 600 + RouteExport.StopSpacing * 0.8, "stops: a big ship at rest there is kept clear of by its size");
                    Check(AutopilotPanel.ConfirmLines(sPath, "X").Exists(l => l.Text.StartsWith("Another ship stops there")), "stops: the confirmation says the stop was moved");
                    // a saved route keeps where it was sent to stop; one without (a share code, an older file) is worked out
                    var sRoute = SavedRoute.From(sPath, 7, "A", "sp:T", 0); sRoute.Stop = s1;
                    var sBack = SavedRoute.Parse(sRoute.ToLine());
                    string sOld = sRoute.ToLine().Substring(0, sRoute.ToLine().LastIndexOf('|'));
                    RouteExport.Taken = null;
                    Check(sBack != null && sBack.Stop.HasValue && V3.Distance(sBack.StopAt, s1) < 1 && SavedRoute.Parse(sOld) is { } o && !o.Stop.HasValue && V3.Distance(o.StopAt, s0) < 5,
                          "stops: kept with a saved route; an older line reads, its stop worked out");
                }
                finally { RouteExport.Taken = sHad; }
            }
            // Hygeia Station sits inside Hygeia (under its listed radius): from the far side, round over it, down from above
            {
                var hy = SystemData.FindWorld("Hygeia"); var hs = Array.Find(SystemData.Stations, x => x.Name == "Hygeia Station");
                var hup = (hs.Pos - hy.Pos).Normalized; var hFar = hy.Pos - hup * 60e3;
                var hw = DetourPlanner.Plan(hFar, hs.Pos, hs.Name);
                Check(hw.Count >= 2 && LowestOver(hFar, hw.GetRange(0, hw.Count - 1), hw[hw.Count - 1].Pos, hy) >= DetourPlanner.Top(hy) - 1 && (hw[hw.Count - 1].Pos - hy.Pos).Normalized.Dot(hup) > 0.999,
                      "Hygeia Station from the hFar side: round the asteroid, then down from straight above (" + hw.Count + " points)");
            }
            // well clear of it: nothing added
            Check(DetourPlanner.Plan(rhea.Pos + Dir(0) * (rr * 1.5), rhea.Pos + Dir(30) * (rr * 1.5), "C").Count == 0, "moon hop high over it: no points");
        }
        // landing on the far side: round, then down from above it
        var base2 = earth.Pos + new V3(101e3, 0, 0);
        var down = DetourPlanner.Plan(lowStart, base2, "B");
        Check(down.Count >= 2 && V3.Distance(down[down.Count - 1].Pos, earth.Pos + new V3(200e3, 0, 0)) < 1, "landing: comes down from straight above the base");
        Check(LowestOver(lowStart, down.GetRange(0, down.Count - 1), down[down.Count - 1].Pos, earth) >= R + 40e3 - 1, "landing: round, clear, before coming down");
        // picking Earth itself from low orbit: stop above you, never at the centre
        var stop = RouteExport.StopNear(lowStart, earth.Pos, 100e3, out var why);
        Check(why == "world" && Near(V3.Distance(stop, earth.Pos), R + 100e3, 1) && stop.X < earth.Pos.X, "pick Earth, gravity not read: the fallback height on your side, not its centre");
        Check(DetourPlanner.Plan(lowStart, earth.Pos, "Earth").Count == 0, "pick Earth: no detours round Earth itself");
        // with its gravity well known: picking Earth itself stops outside the well, from far off or from inside it
        earth.Well = 250e3; // past the 100 km clearance stop (the guard only ever moves a stop out)
        try
        {
            var farStart = earth.Pos + new V3(0, 0, 2e6);
            var wStop = RouteExport.StopNear(farStart, earth.Pos, 100e3, out var wWhy);
            Check(wWhy == "well" && Near(V3.Distance(wStop, earth.Pos), 252e3, 1) && wStop.Z > earth.Pos.Z, "pick Earth: stops 2 km outside its gravity well, on your side");
            var inStop = RouteExport.StopNear(lowStart, earth.Pos, 100e3, out var inWhy);
            Check(inWhy == "well" && Near(V3.Distance(inStop, earth.Pos), 252e3, 1), "pick Earth from inside its well: climbs out of it");
            var spot = earth.Pos + new V3(0, 101e3, 0);
            var sStop = RouteExport.StopNear(farStart, spot, 100e3, out var sWhy);
            Check(sWhy == "surface" && V3.Distance(sStop, earth.Pos) > earth.Well, "a point on Earth, thrust not known: straight above it, outside the well");
            // no well worth the name (too weak to reach past the hills): 3 km over them
            earth.Well = R * 1.05;
            var nStop = RouteExport.StopNear(farStart, earth.Pos, 100e3, out var nWhy);
            Check(nWhy == "world" && Near(V3.Distance(nStop, earth.Pos), DetourPlanner.Top(earth) + DetourPlanner.SurfaceGap, 1), "pick a world with no well past its hills: 3 km over them");
            var nWp = DetourPlanner.Plan(earth.Pos + new V3(-900e3, 0, 0), earth.Pos + new V3(900e3, 0, 0), "X");
            double nLow = nWp.Count == 0 ? 0 : LowestOver(earth.Pos + new V3(-900e3, 0, 0), nWp, earth.Pos + new V3(900e3, 0, 0), earth);
            Check(nLow >= DetourPlanner.Top(earth) + DetourPlanner.SurfaceGap * 0.79 && nLow < DetourPlanner.Top(earth) + 6e3, "no well: round it 3 km over the hills (" + Km(nLow - DetourPlanner.Top(earth)) + " over them)");
            earth.Well = 250e3;
            var ePath = RoutePath.Make("Earth", farStart, earth.Pos, V3.Zero, 20, 50e3, new List<Waypoint>(), 0, 100e3);
            Check(Near(V3.Distance(RouteExport.StopPoint(ePath, out _), earth.Pos), 252e3, 1), "the exported route to Earth stops outside the well");
        }
        finally { earth.Well = 0; }

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
        var lt = new RouteTracker { Avoid = true, Obstacles = homes };
        lt.Update("X", behind, true, atHome, V3.Zero, 10, 0);
        Check(RouteExport.Parse(RouteExport.Build(lt.Path, 11), out var lr) && lr.Points.Count >= 3 && lr.Points[0].Kind == "detour"
              && lr.Points.TrueForAll(q => q.Kind == "target" || q.Arrive <= 2000)
              && lr.Points[0].Arrive < V3.Distance(atHome, lr.Points[0].Pos) * 0.3,
              "leave your base: the short detours' reach radius is well inside the 2 km legs (not reached where you start)");
        Check(Math.Abs(RouteExport.ArriveFor(V3.Zero, new V3(1e6, 0, 0), new V3(2e6, 0, 0)) - RouteExport.DetourArrive) < 1e-9
              && Math.Abs(RouteExport.ArriveFor(V3.Zero, new V3(2000, 0, 0), new V3(2000, 3000, 0)) - 500) < 1e-9
              && RouteExport.ArriveFor(V3.Zero, new V3(10, 0, 0), new V3(20, 0, 0)) == 100, "reach radius: 2 km on long legs, a quarter of a short one, at least 100 m");
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
        // the route sent to the PB says, per point, which detour it's on the way to; the PB's progress moves the plan on
        {
            var at = new RouteTracker();
            at.Update("Far side", b, true, a, V3.Zero, 10, 50000);
            int nw = at.Waypoints.Count;
            string text = RouteExport.Build(at.Path, 1, out var marks, "engage");
            int lines = 0; foreach (var l in text.Split('\n')) if (l.StartsWith("wp=")) lines++;
            bool order = true; for (int i = 1; i < marks.Count; i++) order &= marks[i] >= marks[i - 1];
            int firstD = text.Split('\n').Where(l => l.StartsWith("wp=")).ToList().FindIndex(l => l.Contains(",detour,"));
            Check(marks.Count == lines && order && marks[marks.Count - 1] == nw && firstD >= 0 && marks[firstD] == 0,
                "export marks: one per point sent, in order, the detours' own index, the stop after all of them (" + string.Join(",", marks) + ")");
            int r0 = at.Revision;
            Check(at.PassedTo(2) && at.Next == 2 && at.Revision == r0 && !at.PassedTo(1) && at.Next == 2 && !at.PassedTo(nw + 1),
                "PB progress: the plan moves on to where the PB is (never back, no re-send)");
            // flown by fast without coming near: the next update still holds the PB's progress
            at.Update("Far side", b, true, a + new V3(0, 1e6, 0), V3.Zero, 10, 50000);
            Check(at.Next == 2 && RouteExport.Build(at.Path, 2, out var m2, "update").Split('\n').Count(l => l.Contains(",detour,")) == nw - 2,
                "PB progress: an update sends only the points still ahead (no flying back to detour 1)");
        }
        // removing a detour: straight past that world, also after a re-plan; Load puts it back
        {
            var d = new RouteTracker();
            d.Update("Far side", b, true, a, V3.Zero, 10, 50000);
            var saved = d.Save();
            string w = d.RemoveDetour(0);
            Check(w == "Earth" && d.Waypoints.Count == 0 && d.Skipped.Contains("Earth"), "remove detour: every detour round Earth goes (" + w + ", " + d.Waypoints.Count + " left)");
            d.Replan(a);
            Check(d.Waypoints.Count == 0, "remove detour: stays gone after a re-plan");
            d.Load(saved); d.Replan(a);
            Check(d.Waypoints.Count >= 2 && d.Skipped.Count == 0, "remove detour: undone by loading the saved plan");
            d.RemoveDetour(0); d.Update("Elsewhere", earth + new V3(0, 900e3, 0), true, a, V3.Zero, 10, 50000);
            Check(d.Skipped.Count == 0, "remove detour: a new destination brings detours back");
            Check(d.RemoveDetour(99) == null, "remove detour: out of range is nothing");
        }
        // a new destination re-plans
        t.Update("Elsewhere", earth + new V3(0, 900e3, 0), true, a, V3.Zero, 10, 50000);
        Check(t.Revision > rev && t.Next == 0, "new destination re-plans");
        // avoidance off: straight line
        var off = new RouteTracker { Avoid = false };
        off.Update("Far side", b, true, a, V3.Zero, 10, 50000);
        Check(off.Waypoints.Count == 0 && off.Path.Legs.Count == 1 && off.Path.First.Hazards.Count > 0, "avoid off: straight, hazard shown");
        t.Update(null, V3.Zero, true, a, V3.Zero, 10, 50000);
        Check(t.Path != null && t.TargetName == "Elsewhere", "no pick: the route stays until cleared");
        t.Cancel(null, V3.Zero); t.Update(null, V3.Zero, true, a, V3.Zero, 10, 50000);
        Check(t.Path == null && t.Waypoints.Count == 0, "cleared: no route");
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
        // a curved route starting next to a moon (the 19 Nov crash: stopped 3.3 km over Phobos, route on to Tycho): the
        // corners round it are a few km apart on a 4,000 km route, and the points sent to the PB must keep them
        {
            var phobos = SystemData.FindWorld("Phobos");
            double wWell = phobos.Well, wG = phobos.SurfaceG, wF = phobos.Falloff, wH = phobos.Hills;
            try
            {
                phobos.Well = 4400; phobos.Hills = 4400; phobos.SurfaceG = 0.05 * 9.81; phobos.Falloff = 7; // the game's numbers
                var ship = new V3(-3506274.91, -585056.39, 5897633.21); var tychoAt = new V3(-6517288.60, -351506.97, 4537578.92); // a point out past Phobos (no station)
                var ct = new RouteTracker { Avoid = true, Curved = true };
                ct.Update("Tycho Station", tychoAt, true, ship, V3.Zero, 169.26, 50000);
                Check(ct.Waypoints.Exists(w => w.Around == phobos) && ct.Path.Curved, "by Phobos: the route goes round it, curved");
                Check(RouteExport.Parse(RouteExport.Build(ct.Path, 3), out var cr), "by Phobos: export parses");
                double lowest = double.MaxValue; V3 pa = ship;
                foreach (var q in cr.Points) { lowest = Math.Min(lowest, V3.SegmentDistance(pa, q.Pos, phobos.Pos, out _)); pa = q.Pos; }
                Check(lowest > DetourPlanner.Top(phobos) + 1000, "by Phobos: the points sent to the PB stay clear of it (" + Km(lowest) + " from its centre)");
                Check(cr.Points.Count <= RouteExport.CurvePoints * 2 + 4, "by Phobos: no flood of points (" + cr.Points.Count + ")");
                Check(cr.Points[0].Arrive <= V3.Distance(ship, cr.Points[0].Pos) * 0.25 + 1 || cr.Points[0].Arrive <= 100, "by Phobos: the first point isn't reached where the ship starts");
            }
            finally { phobos.Well = wWell; phobos.SurfaceG = wG; phobos.Falloff = wF; phobos.Hills = wH; }
        }
        // world load: the route comes back from the PB copy, with the points already flown passed
        var back = new RouteTracker();
        Check(back.Restore(r, 1, earth + new V3(-600e3, 0, 0), null, V3.Zero) && back.TargetName == "Far (side)" && back.Waypoints.Count == t.Waypoints.Count
            && back.Next == Math.Min(1, t.Waypoints.Count) && V3.Distance(back.Target, r.Dest) < 0.01, "restore: route taken back from the PB");
        back.Hold = true; back.Update(null, V3.Zero, true, earth + new V3(-600e3, 0, 0), V3.Zero, 12.5, 50000);
        Check(back.Path != null && back.TargetName == "Far (side)", "restore: kept with nothing picked while flying");
        // editing in flight: CANCEL goes back to the saved plan
        var saved = back.Save(); int wps = back.Waypoints.Count, rev = back.Revision;
        back.AddUser("Extra", earth + new V3(0, 900e3, 0)); back.Bend("Far (side)", earth + new V3(0, 0, 700e3));
        back.Load(saved);
        Check(back.UserPoints.Count == 0 && back.Waypoints.Count == wps && back.Bends.Count == 0 && back.TargetName == "Far (side)" && back.Revision > rev, "flight edit: cancel goes back");
        RouteExport.Parse(RouteExport.BuildStop(8), out var sr);
        Check(!new RouteTracker().Restore(sr, 0, V3.Zero, null, V3.Zero), "restore: a stop order isn't a route");

        // flying by hand: the HUD line and the heads-up before a turn
        Check(Math.Abs(ManualGuide.TurnAngle(V3.Zero, new V3(100e3, 0, 0), new V3(100e3, 100e3, 0)) - 90) < 0.01, "manual: turn angle");
        Check(double.IsNaN(ManualGuide.TimeTo(V3.Zero, new V3(-500, 0, 0), new V3(100e3, 0, 0))), "manual: no time when moving away");
        // flown by hand to a moon (the 19 Nov crash: the brake share was for a stop on Deimos's centre, and the hauler
        // reached the surface at 824 m/s): the guide's leg ends where the autopilot stops, above the surface
        {
            var deimos = SystemData.FindWorld("Deimos");
            var from = new V3(-7033734.69, -315354.85, 4319765.74);
            var mt2 = new RouteTracker { Avoid = true, Curved = true };
            mt2.Update("Deimos", deimos.Pos, true, from, V3.Zero, 170.27, 50000);
            var hand = BurnGuide.HandLeg(mt2.Path);
            double up = V3.Distance(hand.Target, deimos.Pos) - deimos.Radius;
            Check(mt2.Path.Legs.Count == 1 && V3.Distance(hand.Target, RouteExport.StopPoint(mt2.Path, out _)) < 0.01 && up > 2000,
                "manual: the hand-flown leg to a moon ends above its surface (" + Km(up) + " up), where the autopilot stops");
            // braking to stop there from 126.9 km out at 6,305 m/s (the crash's last brake setting) leaves no speed at the surface
            var ship2 = hand.Target + (from - hand.Target).Normalized * 126900;
            double pct2 = BurnGuide.ThrustPct(BurnGuide.Phase.Brake, 170.27, 170.27, V3.Distance(ship2, hand.Target), 6305, hand.EndSpeed);
            double old2 = BurnGuide.ThrustPct(BurnGuide.Phase.Brake, 170.27, 170.27, V3.Distance(ship2, deimos.Pos), 6305, 0);
            Check(pct2 > old2 && pct2 <= 100 && Math.Abs(pct2 - 6305.0 * 6305 / (2 * 126900) / 170.27 * 100) < 0.5, "manual: the brake share stops the ship at that point (" + pct2.ToString("0") + "%, was " + old2.ToString("0") + "% for the centre)");
            var straight = new RouteTracker { Avoid = false };
            straight.Update("Deimos", deimos.Pos, true, from, V3.Zero, 170.27, 50000);
            straight.AddUser("Mid", (from + deimos.Pos) * 0.5 + new V3(0, 200e3, 0));
            straight.Update("Deimos", deimos.Pos, true, from, V3.Zero, 170.27, 50000);
            Check(straight.Path.Legs.Count > 1 && BurnGuide.HandLeg(straight.Path) == straight.Path.First, "manual: a leg to a route point still ends on the point (" + straight.Path.Legs.Count + " legs, " + straight.UserPoints.Count + " points)");
        }
        string mt = ManualGuide.Text("Hygeia", 1, 3, "Waypoint 2", new V3(100e3, 0, 0), "Hygeia", new V3(100e3, 900e3, 0),
            new V3(90e3, 0, 0), new V3(500, 0, 0), 910e3, 1800, "Waypoint 2", out string ms);
        Check(mt.Contains("point 2 of 3") && ms.StartsWith("Turn 90") && ms.Contains("in 20 s"), "manual: turn heads-up 20 s out (" + ms + ")");
        ManualGuide.Text("Hygeia", 1, 3, "Waypoint 2", new V3(100e3, 0, 0), "Hygeia", new V3(100e3, 900e3, 0),
            new V3(0, 0, 0), new V3(500, 0, 0), 1000e3, 1800, "Waypoint 2", out ms);
        Check(ms.StartsWith("Flip and Burn's box: Waypoint 2"), "manual: no heads-up far out (" + ms + ")");
        ManualGuide.Text("Hygeia", 1, 3, "Waypoint 2", new V3(100e3, 0, 0), "Hygeia", new V3(100e3, 900e3, 0),
            new V3(90e3, 0, 0), new V3(500, 0, 0), 910e3, 1800, "Somewhere", out ms);
        Check(ms == "Pick \"Waypoint 2\" in N", "manual: asks for the pick when F&B points elsewhere");
        {
            // other mods' marker labels: two markers close on screen (F&B's destination and its hostile space warning)
            LabelSpread.Reset();
            var none = new System.Collections.Generic.List<Box>();
            float a1 = LabelSpread.Shift(1, 870, 300, 0.0225f, 1920, 1080, none), a2 = LabelSpread.Shift(1, 870, 300, -0.01f, 1920, 1080, none);
            float a3 = LabelSpread.Shift(1, 870 + 60, 300, -0.01f, 1920, 1080, none); // a line set beside another
            float b1 = LabelSpread.Shift(1, 850, 290, 0.03f, 1920, 1080, none), b2 = LabelSpread.Shift(1, 850, 290, 0f, 1920, 1080, none);
            Check(a1 == 0 && a2 == 0 && a3 == 0, "mod labels: the first marker's lines stay where they are");
            Check(b1 != 0 && b1 == b2 && !LabelSpread.Block(850, 290 + b1, 1920, 1080).Overlaps(LabelSpread.Block(870, 300, 1920, 1080)),
                "mod labels: a second marker's lines move together, clear of the first (" + b1.ToString("0") + " px)");
            float c1 = LabelSpread.Shift(1, 300, 700, 0.0225f, 1920, 1080, none);
            Check(c1 == 0, "mod labels: a marker elsewhere is left alone");
            // the next frame (the same line again, even with the same frame number) starts afresh
            Check(LabelSpread.Shift(1, 870, 300, 0.0225f, 1920, 1080, none) == 0 && LabelSpread.Shift(1, 850, 290, 0.03f, 1920, 1080, none) == b1, "mod labels: the same each frame");
            // one of the plugin's own boxes: moved off it, the shorter way
            var keep = new System.Collections.Generic.List<Box> { new Box(800, 200, 300, 110) };
            float k = LabelSpread.Shift(2, 870, 300, 0.0225f, 1920, 1080, keep);
            Check(k > 0 && !LabelSpread.Block(870, 300 + k, 1920, 1080).Overlaps(keep[0]), "mod labels: moved below the plugin's own box (" + k.ToString("0") + " px)");
            float e = LabelSpread.Shift(3, 870, 1070, 0.0225f, 1920, 1080, new System.Collections.Generic.List<Box> { new Box(800, 1040, 300, 30) });
            Check(e < 0, "mod labels: up when there is no room below");
            LabelSpread.Reset();
        }

        // burn guide: on the line, burn along it; off it, tilt back toward it; brake points back along it
        {
            var a0 = BurnGuide.Aim(V3.Zero, new V3(1e9, 0, 0), new V3(1e6, 0, 0), new V3(1000, 0, 0), 10, BurnGuide.Phase.Burn);
            Check(BurnGuide.Angle(a0, new V3(1, 0, 0)) < 0.01, "burn guide: on the line, along it");
            var a1 = BurnGuide.Aim(V3.Zero, new V3(1e9, 0, 0), new V3(1e6, 50e3, 0), new V3(1000, 0, 0), 10, BurnGuide.Phase.Burn);
            Check(a1.Y < 0 && BurnGuide.Angle(a1, new V3(1, 0, 0)) <= 45.01, "burn guide: off the line, tilted back (" + Fmt.Num(BurnGuide.Angle(a1, new V3(1, 0, 0)), "0.0") + ")");
            var a2 = BurnGuide.Aim(V3.Zero, new V3(1e9, 0, 0), new V3(1e6, 0, 0), new V3(1000, 300, 0), 10, BurnGuide.Phase.Burn);
            Check(a2.Y < 0, "burn guide: a drift across is taken out");
            var a3 = BurnGuide.Aim(V3.Zero, new V3(1e9, 0, 0), new V3(1e6, 0, 0), new V3(1000, 0, 0), 10, BurnGuide.Phase.Brake);
            Check(BurnGuide.Angle(a3, new V3(-1, 0, 0)) < 0.01, "burn guide: brake points back");
            Check(BurnGuide.Aim(V3.Zero, new V3(1e9, 0, 0), new V3(1e6, 0, 0), new V3(1000, 0, 0), 10, BurnGuide.Phase.Coast).LengthSq < 0.5, "burn guide: on the line coasting, nothing to do");
            BurnGuide.Project(new V3(0, 0, -1), 1, 1.7, 1920, 1080, 40, out float gx, out float gy, out bool gon);
            Check(gon && Math.Abs(gx - 960) < 0.5 && Math.Abs(gy - 540) < 0.5, "burn guide: ahead is the screen's centre");
            BurnGuide.Project(new V3(1, 0, 1), 1, 1.7, 1920, 1080, 40, out gx, out gy, out gon);
            Check(!gon && Math.Abs(gx - 1880) < 0.5, "burn guide: behind-right goes to the right edge");
            BurnGuide.Draw(new MeasureCanvas(), 20, 960, 540, true, 1200, 400, true, BurnGuide.Phase.Burn, 12, "FLIP in 45 s");
            var gl = RoutePlan.Make("Far", V3.Zero, new V3(1e9, 0, 0), V3.Zero, 10, 50000);
            string gn = BurnGuide.Next(gl, BurnGuide.PhaseOf(gl), null);
            Check(BurnGuide.PhaseOf(gl) == BurnGuide.Phase.Burn && gn.StartsWith("COAST in"), "burn guide: countdown to the coast (" + gn + ")");
            Check(BurnGuide.Next(gl, BurnGuide.Phase.Brake, "Hygeia").StartsWith("TURN for Hygeia in"), "burn guide: braking counts down to the turn");
            // a pilot's slack: at speed the hand-flown plan flips earlier than the autopilot's
            var fast = RoutePlan.Make("Far", V3.Zero, new V3(2e8, 0, 0), new V3(20000, 0, 0), 10, 50000);
            var padded = BurnGuide.Padded(fast, V3.Zero, new V3(20000, 0, 0));
            Check(padded.FlipAt < fast.FlipAt - 20000 * BurnGuide.ReactTime * 0.9, "burn guide: flips earlier by hand (" + Fmt.Dist(fast.FlipAt - padded.FlipAt) + ")");
            var still = BurnGuide.Padded(gl, V3.Zero, V3.Zero);
            Check(Math.Abs(still.Distance - gl.Distance) < 1, "burn guide: no slack taken from a standstill");
            // thrust hints: BURN at the plan's share, BRAKE at what stops on the point
            Check(Near(BurnGuide.ThrustPct(BurnGuide.Phase.Burn, 6, 10, 1e6, 0, 0), 60, 1e-9), "burn guide: BURN at the plan's thrust share");
            Check(Near(BurnGuide.ThrustPct(BurnGuide.Phase.Brake, 6, 10, 50000, 1000, 0), 100, 1e-6), "burn guide: BRAKE share stops on the point");
            Check(BurnGuide.ThrustPct(BurnGuide.Phase.Brake, 6, 10, 100000, 1000, 0) < 51, "burn guide: half the brake with twice the room");
            Check(double.IsNaN(BurnGuide.ThrustPct(BurnGuide.Phase.Coast, 6, 10, 1e5, 100, 0)), "burn guide: no thrust hint while coasting");
            // overshoot / short: 1000 m/s, 50 km out; slowing at 5 m/s² stops at 100 km (50 km over), at 20 m/s² at 25 km (25 km short)
            Check(Near(BurnGuide.StopError(BurnGuide.Phase.Brake, 50000, 1000, 0, 5, 10), 50000, 1), "burn guide: overshoot at the slowing it has");
            Check(BurnGuide.StopText(BurnGuide.Phase.Brake, BurnGuide.StopError(BurnGuide.Phase.Brake, 50000, 1000, 0, 20, 30), 50000) == "short 25.0 km", "burn guide: short with the distance");
            Check(BurnGuide.StopText(BurnGuide.Phase.Brake, BurnGuide.StopError(BurnGuide.Phase.Brake, 50000, 1000, 0, 10, 10), 50000) == "stops on the mark", "burn guide: on the mark");
            Check(BurnGuide.StopText(BurnGuide.Phase.Burn, BurnGuide.StopError(BurnGuide.Phase.Burn, 40000, 1000, 0, 0, 10), 40000) == "overshoot 10.0 km", "burn guide: overshoot even at full brake");
            Check(BurnGuide.StopText(BurnGuide.Phase.Burn, BurnGuide.StopError(BurnGuide.Phase.Burn, 1e6, 1000, 0, 0, 10), 1e6) == null, "burn guide: nothing while there's room");
            // the next change: FLIP back along the line, TURN onto the next leg, nothing when arriving
            var nf = BurnGuide.NextAim(gl, BurnGuide.Phase.Burn, V3.Zero, new V3(1e6, 0, 0), null, out string nl1);
            Check(nl1 == "FLIP" && nf.X < -0.99, "burn guide: next is the flip, pointing back");
            var nt = BurnGuide.NextAim(gl, BurnGuide.Phase.Brake, V3.Zero, new V3(1e6, 0, 0), new V3(1e6, 1e6, 0), out string nl2);
            Check(nl2 == "TURN" && nt.Y > 0.99, "burn guide: braking for a point, next is the turn onto the next leg");
            BurnGuide.NextAim(gl, BurnGuide.Phase.Brake, V3.Zero, new V3(1e6, 0, 0), null, out string nl3);
            Check(nl3 == null, "burn guide: nothing next when arriving");
            var nc = new PropCanvas();
            BurnGuide.DrawNext(nc, 20, 1900, 500, false, 960, 540, "FLIP", "1 m 20 s");
            Check(nc.Texts.Exists(x => x.Text == "NEXT FLIP 1 m 20 s"), "burn guide: next change labelled with its time");
        }

        // stop points: 3 km short of a station, at the clearance above a world, the point itself in open space
        var tycho = SystemData.Stations[3].Pos;
        var ps = RoutePath.Make("Tycho", tycho + new V3(3e6, 0, 0), tycho, V3.Zero, 10, 50000, new List<Waypoint>(), 0, 100e3);
        var sp = RouteExport.StopPoint(ps, out why);
        Check(why == "station" && Near(V3.Distance(sp, tycho), RouteExport.StationStandoff, 1) && sp.X > tycho.X, "station stop 3 km short, on the near side");
        // a station near or on a world: stop above it (away from the world), round to it if you come from the world's side
        var low = Array.Find(SystemData.Stations, x => x.Above == 0 && RouteExport.WorldUnder(x.Pos, 100e3) != null);
        Check(low != null, "a station sits near a world (" + (low?.Name ?? "none") + ")");
        if (low != null)
        {
            var lw = RouteExport.WorldUnder(low.Pos, 100e3); var lup = (low.Pos - lw.Pos).Normalized;
            var under = low.Pos - lup * 50e3 + (lup.Cross(new V3(0, 1, 0)).Length > 0.1 ? lup.Cross(new V3(0, 1, 0)).Normalized : new V3(1, 0, 0)) * 1000;
            var pl = RoutePath.Make(low.Name, under, low.Pos, V3.Zero, 10, 50000, new List<Waypoint>(), 0, 100e3);
            var lsp = RouteExport.StopPoint(pl, out why);
            Check(why == "station" && Near(V3.Distance(lsp, low.Pos), RouteExport.StationStandoff, 1) && (lsp - low.Pos).Normalized.Dot(lup) > 0.999,
                  "station by a world: stop 3 km above it, away from " + lw.Name);
            Check(RouteExport.Parse(RouteExport.Build(pl, 9), out var lr) && lr.Points.Count == 2 && lr.Points[0].Name.StartsWith("Approach")
                  && LowestOver(under, new List<Waypoint> { new Waypoint { Pos = lr.Points[0].Pos } }, lsp, SystemData.Obstacle("s", low.Pos, 1)) >= RouteExport.StationStandoff * 0.9,
                  "coming up from the world's side: round the station to the stop, not through it");
            var above = low.Pos + lup * 400e3;
            Check(RouteExport.Parse(RouteExport.Build(RoutePath.Make(low.Name, above, low.Pos, V3.Zero, 10, 50000, new List<Waypoint>(), 0, 100e3), 9), out lr) && lr.Points.Count == 1,
                  "coming from above: straight to the stop");
        }
        // a ground facility (2 Pallas Station, in Pallas's hills): the planner's descent detour ends ~3.7 km from it, and
        // the stop must still be above it, never on it (2026-11-19: stop=point on the station flew a hauler into the ground)
        {
            var pal = Array.Find(SystemData.Stations, x => x.Name == "2Pallas Station");
            var pw = SystemData.FindWorld("Pallas");
            foreach (var from in new[] { new V3(11342873.83, -157987.76, 13924813.76), new V3(6222641.78, -274072.35, 6284849.9) })
                foreach (bool curved in new[] { false, true })
                {
                    var dets = DetourPlanner.Plan(from, pal.Pos, pal.Name, 100e3);
                    var palPath = RoutePath.Make(pal.Name, from, pal.Pos, V3.Zero, 161.58, 50000, dets, 0, 100e3, 0, curved);
                    var stopP = RouteExport.StopPoint(palPath, out why);
                    double over = V3.Distance(stopP, pw.Pos) - V3.Distance(pal.Pos, pw.Pos);
                    Check(why == "station" && over >= DetourPlanner.SurfaceGap - 1 && (stopP - pal.Pos).Normalized.Dot((pal.Pos - pw.Pos).Normalized) > 0.999
                          && RouteExport.Parse(RouteExport.Build(palPath, 9), out var palRoute) && V3.Distance(palRoute.Points[palRoute.Points.Count - 1].Pos, stopP) < 1,
                          "Pallas station (" + (curved ? "curved" : "straight") + ", " + dets.Count + " detour): stop " + over.ToString("0") + " m straight above it, not on it");
                }
        }
        var mars = SystemData.FindWorld("Mars");
        var pm = RoutePath.Make("Mars", mars.Pos + new V3(0, 0, 2e6), mars.Pos, V3.Zero, 10, 50000, new List<Waypoint>(), 0, 100e3);
        sp = RouteExport.StopPoint(pm, out why);
        Check(why == "world" && Near(V3.Distance(sp, mars.Pos), mars.Radius + 100e3, 1) && sp.Z > mars.Pos.Z, "world stop at the clearance, facing the approach");
        var open = new V3(3e6, 2e6, 1e6);
        var po = RoutePath.Make("Open", open + new V3(1e6, 0, 0), open, V3.Zero, 10, 50000, new List<Waypoint>(), 0, 100e3);
        double keepOffset = RouteExport.Offset;
        try
        {
            RouteExport.Offset = 0;
            var op = RouteExport.StopPoint(po, out why);
            Check(Near(V3.Distance(op, open), RouteExport.MinOffset, 1) && why == "offset", "offset never under 0.5 km");
            RouteExport.Offset = 4000;
            op = RouteExport.StopPoint(po, out why);
            Check(Near(V3.Distance(op, open), 4000, 1) && op.X > open.X && why == "offset", "open space: stops the offset short, on your side");
            Check(V3.Distance(RouteExport.StopNear(open + new V3(1e6, 0, 0), open, 100e3, out why), open) < 0.01 && why == "point", "a waypoint on the way: on the point");
        }
        finally { RouteExport.Offset = keepOffset; }

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
        {
            var fst = RouteExport.ParseStatus(RouteExport.Merge(merged, "[SDX2 Autopilot]\nstate=flying\nack=11\nmsg=x\nfleet=2,0.64,0\n", RouteExport.StatusHeader));
            Check(fst != null && fst.Leading && fst.Fleet == 2 && Math.Abs(fst.FleetShare - 0.64) < 1e-9 && fst.FleetCap == 0 && !fst.InFormation, "status: leading a formation, the slowest ship's share read");
        }
        {
            Check(RouteExport.Parse(RouteExport.BuildCommand(11, "brake"), out var br) && br.Command == "brake" && br.Id == 11, "full stop order parses");
            var bst = RouteExport.ParseStatus(RouteExport.Merge(merged, "[SDX2 Autopilot]\nstate=braking\nack=11\nspeed=2400\ndist=120000\neta=95\nmsg=Full stop from the map\n", RouteExport.StatusHeader));
            Check(bst != null && bst.Braking && !bst.Flying && Near(bst.Speed, 2400, 1e-6), "full stop: the PB's braking status reads back");
            Check(RouteExport.Parse(RouteExport.BuildCommand(12, "manual"), out var mo) && mo.Command == "manual" && mo.Id == 12, "confirm manual order parses");
            var mst = RouteExport.ParseStatus(RouteExport.Merge(merged, "[SDX2 Autopilot]\nstate=manual\nack=11\ntarget=Hygeia\nmsg=Pilot took the controls\n", RouteExport.StatusHeader));
            Check(mst != null && mst.Manual && !mst.Flying && mst.Target == "Hygeia", "taken over: the PB's paused status reads back");
        }

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
        // a station waypoint stops 3 km short
        var tycho = SystemData.Stations[3];
        t.Update(tycho.Name, tycho.Pos, true, ship, V3.Zero, 10, 50000);
        var sw = t.Waypoints.Find(w => w.User && w.Name == tycho.Name);
        Check(t.UserPoints.Count == 2 && Near(V3.Distance(sw.Pos, tycho.Pos), RouteExport.StationStandoff, 1), "a station waypoint stops 3 km short");
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
        var en = c.Find("ENGAGE AUTOPILOT", 0);
        Frame(en.X, en.Y + 5, true, true, false);
        h = Frame(en.X, en.Y + 5, false, false, true);
        Check(h.Engage && !h.Cancel, "clicking ENGAGE engages");
    }

    /// <summary>The second screen's list of places: its rows, its pages and a pick.</summary>
    private static void DestinationListChecks()
    {
        // no station is built in: they are the station GPS points you have, and nothing else
        {
            var had = new List<KeyValuePair<string, V3>>();
            foreach (var s in SystemData.Stations) had.Add(new KeyValuePair<string, V3>(s.Gps, s.Pos));
            SystemData.SetKnownStations(null);
            var none = new MapSnapshot();
            none.Gps.Add(new MapSnapshot.Marker { Name = "My base", Pos = new V3(1, 2, 3) });
            Check(SystemData.Stations.Length == 0 && SystemData.StationNear(had[3].Value, 1e9) == null && !DestinationList.Build(none).Exists(e => e.Name == "STATIONS" || e.Kind == "Trade station"),
                  "stations: none known without their GPS points (no list entry, no heading)");
            var rhea = SystemData.FindWorld("Rhea");
            SystemData.SetKnownStations(new[] {
                new KeyValuePair<string, V3>("SA.Rhea.STN", rhea.Pos + new V3(0, rhea.Radius, 0)), new KeyValuePair<string, V3>("Belt.Tycho.STN", had[3].Value),
                new KeyValuePair<string, V3>("My base", new V3(1, 2, 3)), new KeyValuePair<string, V3>("Belt.Tycho.STN", new V3(9, 9, 9)) });
            Check(SystemData.Stations.Length == 2 && SystemData.Stations[0].Name == "Rhea Station" && SystemData.Stations[0].Above == SystemData.GroundAbove
                  && SystemData.Stations[1].Name == "Tycho Station" && SystemData.Stations[1].Above == 0 && SystemData.Stations[1].Pos.X == had[3].Value.X,
                  "stations: from region.place.STN GPS points only, once each; on a world's ground = ground facility");
            Check(!SystemData.IsStationGps("Tycho") && !SystemData.IsStationGps("a.b.c") && !SystemData.IsStationGps("Belt..STN") && SystemData.IsStationGps(" belt.Tycho.stn "), "stations: only names of the form region.place.STN count");
            var one = new MapSnapshot();
            one.Gps.Add(new MapSnapshot.Marker { Name = "Belt.Tycho.STN", Pos = had[3].Value });
            var l1 = DestinationList.Build(one);
            Check(l1.FindAll(e => e.Kind == "Trade station").Count == 2 && !l1.Exists(e => e.Kind == "GPS"), "destination list: a station's GPS point is listed as the station, not again under GPS");
            SystemData.SetKnownStations(had);
        }
        var snap = new MapSnapshot();
        snap.Gps.Add(new MapSnapshot.Marker { Name = "My base", Pos = new V3(1, 2, 3) });
        var items = DestinationList.Build(snap);
        int mars = items.FindIndex(e => e.Name == "Mars"), deimos = items.FindIndex(e => e.Name == "Deimos");
        Check(items[0].Head && mars > 0 && !items[mars].Moon && deimos > mars && items[deimos].Moon, "destination list: worlds first, moons set in under their planet");
        Check(!items.Exists(e => e.Name == "Sol") && items.Exists(e => e.Kind == "Trade station" && !string.IsNullOrEmpty(e.Detail)) && items[items.Count - 1].Name == "My base" && items[items.Count - 1].Kind == "GPS",
            "destination list: no sun, stations with their GPS name, your GPS points last");
        var ui = new PanelUi();
        var c = new MeasureCanvas();
        int page = 0;
        DestinationList.Hit Frame(float mx, float my, bool down, bool pressed, bool released)
        {
            ui.Input(c, mx, my, down, pressed, released);
            return DestinationList.Draw(ui, items, 0, 0, 400, 900, 20, snap, "Mars", false, ref page);
        }
        var h0 = Frame(-1, -1, false, false, false);
        Check(h0.Picked < 0 && h0.Panel.H <= 900 && page == 0, "destination list: fits its height, nothing picked");
        var m = c.Find("Mars", 0);
        Frame(m.X + 5, m.Y + 5, true, true, false);
        var h1 = Frame(m.X + 5, m.Y + 5, false, false, true);
        Check(h1.Picked == mars, "destination list: a click on Mars picks Mars");
        var nx = c.Find("NEXT", 0);
        Frame(nx.X + 2, nx.Y + 5, true, true, false);
        Frame(nx.X + 2, nx.Y + 5, false, false, true);
        Check(page == 1, "destination list: NEXT turns the page");
        page = 99; Frame(-1, -1, false, false, false);
        Check(page == (items.Count - 1) / DestinationList.Rows(900, 20), "destination list: the page stays in range");
    }

    /// <summary>The controller on the route panel: D-pad focus, A on release, left / right on a slider, and the A that opened it.</summary>
    private static void PadFocus()
    {
        // the focus moves by direction between boxes: a 2 x 2 grid
        var grid = new List<PanelUi.Item>
        {
            new PanelUi.Item { Box = new Box(0, 0, 100, 30), Enabled = true },
            new PanelUi.Item { Box = new Box(120, 0, 100, 30), Enabled = true },
            new PanelUi.Item { Box = new Box(0, 50, 100, 30), Enabled = true },
            new PanelUi.Item { Box = new Box(120, 50, 100, 30), Enabled = false },
        };
        Check(PanelUi.Next(grid, 0, PanelUi.PadDir.Right) == 1 && PanelUi.Next(grid, 0, PanelUi.PadDir.Down) == 2, "pad: right and down go to the neighbours");
        Check(PanelUi.Next(grid, 1, PanelUi.PadDir.Down) == 2, "pad: down skips a disabled button and takes the nearest one below");
        Check(PanelUi.Next(grid, 0, PanelUi.PadDir.Up) == 0 && PanelUi.Next(grid, 0, PanelUi.PadDir.Left) == 0, "pad: nothing that way: the focus stays");

        var a = new V3(5e6, 3e6, 1e6);
        var t = new RouteTracker { Avoid = false };
        t.Update("Far", a + new V3(4e7, 0, 0), true, a, V3.Zero, 10, 50000);
        var lim = new RouteLimits();
        var ui = new PanelUi { PadActive = true };
        var c = new MeasureCanvas();
        var names = new List<string>();
        AutopilotPanel.RouteHit Frame(PanelUi.PadDir move, bool aDown)
        {
            ui.Input(c, float.NaN, float.NaN, false, false, false);
            ui.Pad(move, aDown);
            return AutopilotPanel.RoutePanel(ui, 0, 0, 600, 1080, 20, t.Path, lim, 50000, 10, new SignatureInfo { Weight = 8 }, names, false, "Ctrl+M");
        }
        // A held as the panel opens (the A that opened it): letting go presses nothing
        var h = Frame(PanelUi.PadDir.None, true);
        h = Frame(PanelUi.PadDir.None, false);
        Check(ui.Focus == 0 && !h.Engage && !h.Cancel && !h.Changed, "pad: the panel opens focused on its first control, and the A that opened it presses nothing");
        // left / right on the top speed slider change it
        int slider = -1;
        for (int i = 0; i < ui.Drawn.Count; i++) if (ui.Drawn[i].Slider) { slider = i; break; }
        ui.Focus = slider;
        Frame(PanelUi.PadDir.None, false);
        h = Frame(PanelUi.PadDir.Left, false);
        Check(slider >= 0 && h.Changed && lim.MaxSpeed > 0 && lim.MaxSpeed < 50000, "pad: left on the speed slider lowers the cap " + lim.MaxSpeed.ToString("0"));
        // down to ENGAGE, then A down and up presses it
        var en = c.Find("ENGAGE AUTOPILOT", 0);
        bool OnEngage() => ui.Focus >= 0 && ui.Focus < ui.Drawn.Count && ui.Drawn[ui.Focus].Box.Contains(en.X + 2, en.Y + 5);
        for (int i = 0; i < 40 && !OnEngage(); i++)
        {
            Frame(PanelUi.PadDir.Down, false);
            Frame(PanelUi.PadDir.None, false);
            if (!OnEngage() && ui.Focus >= 0 && ui.Drawn[ui.Focus].Box.Bottom > en.Y) { Frame(PanelUi.PadDir.Left, false); Frame(PanelUi.PadDir.None, false); }
        }
        Check(OnEngage(), "pad: down reaches ENGAGE");
        h = Frame(PanelUi.PadDir.None, true);
        Check(!h.Engage, "pad: A pressing down doesn't press yet");
        h = Frame(PanelUi.PadDir.None, false);
        Check(h.Engage && !h.Cancel, "pad: letting A go presses ENGAGE");
        // the focus is only on screen while the controller has the panel
        ui.PadActive = false;
        h = Frame(PanelUi.PadDir.Down, true); h = Frame(PanelUi.PadDir.None, false);
        Check(!h.Engage, "pad: with the controller elsewhere, A presses nothing");
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
            var st = scene.Picks.Find(q => q.Name == "Ganymede Station" && (q.Kind == "Trade station" || q.Kind == "Ground facility"));
            var pl = scene.Picks.Find(q => q.Name == "Jupiter");
            if (st.Name == null || pl.Name == null) { Check(false, "hover: station and Jupiter on screen"); return; }
            // put the mouse on the station but inside Jupiter's disc if they overlap; else just on the station
            scene.MouseX = st.X; scene.MouseY = st.Y;
            scene.Draw(new GdiMapCanvas(g), new Box(0, 0, 1920, 1080));
            Check(scene.Hovered is Pick h && h.Name == "Ganymede Station", "hover: a station on top of a planet is picked first");
            scene.MouseX = pl.X; scene.MouseY = pl.Y;
            scene.Draw(new GdiMapCanvas(g), new Box(0, 0, 1920, 1080));
            Check(scene.Hovered is Pick h2 && h2.Name == "Jupiter", "hover: the planet's centre still picks the planet");
            // the ship routes layer: another ship of yours on autopilot is on the map and pickable, and goes with the layer
            var other = new MapSnapshot.OtherRoute { Ship = "Hauler 2", Target = "Somewhere", Pos = jup.Pos + new V3(jup.Radius * 4, 0, 0), Dist = 5e6 };
            other.Points.Add(jup.Pos + new V3(jup.Radius * 8, 0, jup.Radius * 3));
            scene.Snap.Others.Add(other);
            scene.Draw(new GdiMapCanvas(g), new Box(0, 0, 1920, 1080));
            Check(scene.Picks.Exists(q => q.Name == "Hauler 2" && q.Kind == "Ship" && q.Detail.Contains("Somewhere")), "ship routes: another ship on autopilot is drawn and pickable");
            // the plan kept when a route is sent: the ship starts at the start, ends at the stop, never goes back, and
            // half way through the time of a burn-and-brake leg it is half way along; the file line reads back the same
            {
                V3 ra = new V3(1e6, 2e5, -3e6), rb = new V3(9e6, -1e5, 4e6);
                var rp = RoutePath.Make("Far Point", ra, rb, V3.Zero, 10, 50000, new List<Waypoint>(), 0);
                var sr = SavedRoute.From(rp, 42, "Hauler|2", "sp:Test", 1000);
                bool okPlan = sr != null && sr.Eta > 0 && V3.Distance(sr.At(0, out _), ra) < 1 && V3.Distance(sr.At(sr.Eta + 5, out int done), rb) < 1 && done == sr.Track.Count;
                Check(okPlan, "saved route: starts at the start and ends at the stop");
                if (sr != null)
                {
                    bool fwd = true; double lastD = -1;
                    for (int k = 0; k <= 50; k++) { double dd = V3.Distance(sr.At(sr.Eta * k / 50, out _), ra); if (dd < lastD - 1) fwd = false; lastD = dd; }
                    Check(fwd, "saved route: the ship only moves on along it");
                    if (rp.Legs.Count == 1) Check(Math.Abs(V3.Distance(sr.At(sr.Eta / 2, out _), ra) / V3.Distance(ra, rb) - 0.5) < 0.03, "saved route: half way in time is half way along a burn-and-brake leg");
                    // the share code: fits a chat message, and the route read from it has the ship where the plan does
                    string code = ShareCode.Make(rp, 42, "Hauler 2", 5000);
                    var got = ShareCode.Read("look: " + code + " thanks");
                    Check(code != null && code.Length <= ShareCode.ChatMax && got != null && got.Shared && got.Ship == "Hauler 2" && got.Target == "Far Point" && got.Start == 5000, "share code: fits chat and reads back");
                    if (got != null)
                    {
                        double worst = 0;
                        for (int k = 0; k <= 40; k++) worst = Math.Max(worst, V3.Distance(got.At(sr.Eta * k / 40, out _), sr.At(sr.Eta * k / 40, out _)));
                        Check(Math.Abs(got.Eta - sr.Eta) < 3 && worst < V3.Distance(ra, rb) * 0.01, "share code: the ship is where the plan has it (worst " + Fmt.Dist(worst) + ")");
                        sr.Code = code; got.By = "Someone";
                        var again = SavedRoute.Parse(got.ToLine());
                        Check(again != null && again.Shared && again.By == "Someone" && again.Code == code && SavedRoute.Parse(sr.ToLine())?.Code == code, "share code: kept in the saved line");
                    }
                    Check(ShareCode.Read("SDX2R:notacode") == null && ShareCode.Read("hello") == null && ShareCode.Find("x SDX2R:AAAA") == null, "share code: junk is refused");
                    var back = SavedRoute.Parse(sr.ToLine());
                    Check(back != null && back.ShipId == 42 && back.Ship == "Hauler/2" && back.Target == "Far Point" && back.Start == 1000 && back.Track.Count == sr.Track.Count
                        && V3.Distance(back.At(sr.Eta * 0.3, out _), sr.At(sr.Eta * 0.3, out _)) < 50, "saved route: reads back from its line");
                }
            }
            scene.Opt.Routes = false;
            scene.Draw(new GdiMapCanvas(g), new Box(0, 0, 1920, 1080));
            Check(!scene.Picks.Exists(q => q.Name == "Hauler 2"), "ship routes: off hides it");
            scene.Opt.Routes = true;
            // the waypoint drag guide draws in every state
            scene.GhostBase = other.Pos; scene.Ghost = other.Pos;
            bool guideOk = true;
            try { for (int gg = 1; gg <= 4; gg++) { scene.GhostGuide = gg; scene.Draw(new GdiMapCanvas(g), new Box(0, 0, 1920, 1080)); } } catch { guideOk = false; }
            Check(guideOk, "drag guide: draws with no line picked and with each line picked");
            scene.Ghost = null; scene.GhostGuide = 0;
            var ms2 = new MapSettings(); ms2.Options.Routes = false;
            string sf2 = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sdx2map-routes-test.cfg");
            ms2.Save(sf2);
            Check(MapSettings.Load(sf2).Options.Routes == false, "settings: the ship routes layer is kept");
            try { System.IO.File.Delete(sf2); } catch { }
        }
    }

    private static void Themes()
    {
        Check(MapTheme.Find("tachi") == MapTheme.Tachi && MapTheme.Find("Solar HUD") == MapTheme.Tachi && MapTheme.Find("nonsense") == MapTheme.Classic, "themes: found by name, unknown = Classic");
        {
            // palettes: a theme's shapes with another's colours, the player's colours over them, built-ins untouched
            var t = MapTheme.Make(MapTheme.Tachi, MapTheme.Unn, new Dictionary<string, int> { ["Burn"] = 0x123456 });
            Check(t.Style == ThemeStyle.Tachi && t.Name == "Tachi" && t.Base == MapTheme.Tachi, "palette: keeps the theme's name and style");
            Check(t.Accent.R == MapTheme.Unn.Accent.R && t.Burn.R == 0x12 && t.Burn.B == 0x56 && MapTheme.Tachi.Burn.R != 0x12, "palette: other theme's colours, own colour on top, built-in unchanged");
            Check(t.Bg.A == MapTheme.Unn.Bg.A && t.Next() == MapTheme.Unn, "palette: opacity kept, K moves to the next built-in");
            bool slots = true; foreach (var (k, _) in MapTheme.Slots) slots &= t.Set(k, t.Get(k));
            Check(slots && MapTheme.ParseHex("#abc", out int h3) && h3 == 0xAABBCC && !MapTheme.ParseHex("zz", out _), "palette: every slot set and read, hex parsed");
            var ms = new MapSettings { Theme = "Protogen", NoseStyle = "UNN" };
            ms.Palettes["Protogen"] = "Belter"; ms.ColorsOf("Protogen")["Ship"] = 0xFF00FF;
            var f = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sdx2map-palette-test.cfg");
            ms.Save(f);
            var back = MapSettings.Load(f);
            var look = back.Look("Protogen");
            Check(back.NoseOverride == ThemeStyle.Naval && look.Style == ThemeStyle.Protogen && look.Accent.R == MapTheme.Belter.Accent.R && look.Ship.R == 255 && look.Ship.G == 0,
                "palette: nose style, palette and colours saved per theme");
            Check(back.Look("Tachi").Accent.R == MapTheme.Tachi.Accent.R && new MapSettings().NoseOverride == null, "palette: other themes keep their own");
            // a file saved before OPA and Belter changed names keeps the looks it chose
            System.IO.File.WriteAllText(f, "Theme=OPA\nNoseStyle=Belter\nPalette.Tachi=Belter\nOverlay.OPA=OpaScrap\nOverlay.Belter=BelterContact\nColor.OPA.Ship=#FF00FF\n");
            var old = MapSettings.Load(f);
            Check(old.Theme == "Belter" && old.NoseOverride == ThemeStyle.Contact && old.Palettes["Tachi"] == "OPA" && old.OverlayOf(MapTheme.Belter) == FeedOverlay.BelterScrap
                && old.OverlayOf(MapTheme.Opa) == FeedOverlay.OpaContact && old.ColorsOf("Belter")["Ship"] == 0xFF00FF, "themes: an older file's OPA and Belter choices follow the looks");
            old.Save(f); var again = MapSettings.Load(f);
            Check(again.Theme == "Belter" && again.Palettes["Tachi"] == "OPA" && again.OverlayOf(MapTheme.Belter) == FeedOverlay.BelterScrap, "themes: a file saved since is read as written");
        }
        {
            var leg = RoutePath.Make("T", V3.Zero, new V3(4e9, 0, 0), V3.Zero, 30, 50000, null, 0).First;
            bool inv = true;
            foreach (double f in new[] { 0.05, 0.3, 0.5, 0.8, 0.99 }) { double t = leg.Eta * f; inv &= Math.Abs(leg.TimeAt(leg.AlongAt(t)) - t) < 1; }
            Check(inv, "route: TimeAt is the inverse of AlongAt (for the pinned T+ marks)");
        }
        {
            // water: Canterbury 0.87 MW / (efficiency 10 x water 0.00156) = 55.8 L/s at 100 %, burning and braking only
            double rate = 0.87 / (10 * 0.00156);
            Check(Math.Abs(rate - 55.77) < 0.05, "fuel: Canterbury burns 55.8 L/s (the SDX2 sheet)");
            var fp = RoutePath.Make("T", V3.Zero, new V3(4e9, 0, 0), V3.Zero, 30, 50000, null, 0);
            var fl = fp.First;
            double full = RouteFuel.Litres(fp, 30, rate), half = RouteFuel.Litres(fp, 60, rate);
            Check(Math.Abs(full - (fl.BurnTime + fl.BrakeTime) * rate) < 1 && fl.CoastTime > 0, "fuel: litres = burn + brake time x rate (none on the coast)");
            Check(Math.Abs(half - full / 2) < 1, "fuel: half the thrust share burns half the water");
            Check(RouteFuel.Low(1000, 10000, 950) && !RouteFuel.Low(5000, 10000, 1000) && !RouteFuel.Low(1000, 0, 500) && RouteFuel.Low(100, 10000, 500),
                "fuel: warns below 10 % left (and when it runs dry), not without tanks");
            Check(RouteFuel.Volume(512) == "512 L" && RouteFuel.Volume(12_300) == "12.3 kL" && RouteFuel.Volume(2_500_000) == "2.50 ML", "fuel: volume text");
        }
        Check(MapTheme.Classic.Next() == MapTheme.Tachi && MapTheme.Tachi.Next() == MapTheme.Unn && MapTheme.Unn.Next() == MapTheme.Belter && MapTheme.Belter.Next() == MapTheme.Protogen && MapTheme.Protogen.Next() == MapTheme.Opa && MapTheme.Opa.Next() == MapTheme.Classic, "themes: Next cycles through all six");
        Check(MapTheme.Find("opa") == MapTheme.Opa && MapTheme.Match("green") == MapTheme.Opa && MapTheme.Opa.Style == ThemeStyle.Contact && !MapTheme.Opa.Hud, "themes: OPA found by name and colour, the contact chart style");
        Check(MapTheme.Find("protogen") == MapTheme.Protogen && MapTheme.Match("cyan") == MapTheme.Protogen && MapTheme.Protogen.Style == ThemeStyle.Protogen && !MapTheme.Protogen.Hud, "themes: Protogen found by name and colour, its own style");
        Check(MapTheme.Find("unn") == MapTheme.Unn && MapTheme.Find("Belter") == MapTheme.Belter, "themes: UNN and Belter found by name");
        Check(MapTheme.Match("mcrn") == MapTheme.Tachi && MapTheme.Match("navy") == MapTheme.Unn && MapTheme.Match("un") == MapTheme.Unn
            && MapTheme.Match("belt") == MapTheme.Belter && MapTheme.Match("ceres") == MapTheme.Opa && MapTheme.Match("salvage") == MapTheme.Belter && MapTheme.Match("classic") == MapTheme.Classic && MapTheme.Match("purple") == null, "themes: faction words pick a theme");
        Check(MapTheme.Unn.Style == ThemeStyle.Naval && MapTheme.Belter.Style == ThemeStyle.Belter && MapTheme.Tachi.Hud && !MapTheme.Unn.Hud, "themes: each has its own style");
        var dir = Path.Combine(Path.GetTempPath(), "sdx2map-theme-test.ini");
        var st = new MapSettings { Theme = "Tachi" }; st.Save(dir);
        Check(MapSettings.Load(dir).Theme == "Tachi", "themes: saved and loaded");
        // picture-in-picture
        var pst = new MapSettings { Pip = true, PipCorner = "BL", PipSize = 1.4f }; pst.Save(dir);
        var pl = MapSettings.Load(dir);
        Check(pl.Pip && pl.PipCorner == "BL" && Math.Abs(pl.PipSize - 1.4f) < 1e-4, "pip: saved and loaded");
        { var hst = new MapSettings { HudX = 0.3f, HudY = 0.5f, HudSize = 1.4f }; hst.Save(dir); var hl = MapSettings.Load(dir);
          Check(Math.Abs(hl.HudX - 0.3f) < 1e-4 && Math.Abs(hl.HudY - 0.5f) < 1e-4 && Math.Abs(hl.HudSize - 1.4f) < 1e-4, "autopilot line: place and size saved and loaded"); }
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
        Check(KeyCombo.Parse("ExtraButton1").Display == "Mouse 4" && KeyCombo.Parse("mouse5").Key == "ExtraButton2" && KeyCombo.Parse("Shift+Mouse 4").ToString() == "Shift+ExtraButton1", "keys: side mouse buttons");
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
        {
            // a background inside a cut-corner frame: nothing in the cut corners, all of the middle filled
            var rec = new RecordCanvas();
            var fb = new Box(100, 50, 400, 200);
            PanelStyle.FillInside(rec, fb, fb, 14, 34, new MapColor(0, 0, 0, 200));
            bool inside = true; double area = 0;
            foreach (var r in rec.Rects)
            {
                area += r.W * r.H;
                foreach (var (px2, py2) in new[] { (r.X, r.Y), (r.X + r.W, r.Y), (r.X, r.Y + r.H), (r.X + r.W, r.Y + r.H) })
                {
                    float dl = px2 - fb.X, dr = fb.Right - px2, dt = py2 - fb.Y, db = fb.Bottom - py2;
                    if (dl + dt < 14 - 0.01f || dr + dt < 34 - 0.01f || dr + db < 14 - 0.01f || dl + db < 14 - 0.01f) inside = false;
                }
            }
            double want = 400.0 * 200 - 3 * 14 * 14 / 2.0 - 34 * 34 / 2.0;
            Check(inside && Math.Abs(area - want) / want < 0.02 && rec.Rects.Count < 80, "panels: a background stays inside its cut-corner frame");
            rec.Rects.Clear();
            PanelStyle.FillInside(rec, new Box(100, 50, 400, 20), fb, 14, 34, new MapColor(0, 0, 0, 200));
            bool clipped = true; foreach (var r in rec.Rects) if (r.Y < 50 || r.Y + r.H > 70.01f || r.X + r.W > fb.Right - (34 - (r.Y - 50)) + 0.01f) clipped = false;
            Check(clipped && rec.Rects.Count > 0, "panels: a title bar in the frame's top keeps inside its cut corners");
        }
        // the destination view: within the set range (30 km at most) of an active route's destination, a camera block's lens, its own corner
        Check(DestView.Shown(true, true, 9e3) && DestView.Shown(true, true, 30e3), "destination view: shown within 30 km");
        Check(!DestView.Shown(true, true, 30.1e3) && !DestView.Shown(false, true, 5e3) && !DestView.Shown(true, false, 5e3) && !DestView.Shown(true, true, double.NaN), "destination view: hidden past 30 km, switched off, with no route or no distance");
        Check(DestView.Shown(true, true, 2.9e3, 3e3) && !DestView.Shown(true, true, 3.1e3, 3e3) && !DestView.Shown(true, true, 30.5e3, 50e3), "destination view: the range setting, never past 30 km");
        Check(DestView.Nearness(20e3, 4e3, 3e3) == 4e3 && DestView.Nearness(12e3, double.NaN, 3e3) == 9e3 && DestView.Nearness(1e3, 2e3) == 1e3 && DestView.Nearness(1e3, 5e3, 3e3) == 0, "destination view: a world's surface or the route's stop (outside its well), whichever is nearer");
        Check(DestView.ClampRangeKm(0.2) == 1 && DestView.ClampRangeKm(45) == 30 && DestView.ClampRangeKm(4.5) == 4.5 && DestView.ClampRangeKm(double.NaN) == 30, "destination view: range kept to 1-30 km");
        {
            var ms = new MapSettings { DestViewRangeKm = 4, DestViewReplacesMap = true };
            string ini = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sdx2map_destview_test.ini");
            ms.Save(ini); var back = MapSettings.Load(ini); System.IO.File.Delete(ini);
            Check(back.DestViewRangeKm == 4 && back.DestViewReplacesMap && !new MapSettings().DestViewReplacesMap && new MapSettings().DestViewRangeKm == 10, "destination view: range and map-place settings saved and loaded (off and 10 km by default)");
        }
        Check(Math.Abs(DestView.FitFov(100, 10e3) - DestView.MinFov) < 1e-9, "destination view: a small station 10 km off zooms no further than a camera block (0.1 rad)");
        { double f = DestView.FitFov(500, 3e3); Check(f > DestView.MinFov && f < DestView.MaxFov && Math.Abs(Math.Tan(f / 2) * 3e3 - 500 * DestView.Margin) < 1, "destination view: a big station near frames it with room"); }
        Check(DestView.FitFov(2e3, 1e3) == DestView.MaxFov && DestView.FitFov(0, 5e3) == DestView.PointFov && DestView.FitFov(100, 0) == DestView.PointFov, "destination view: right on it = widest lens; nothing to frame = the default lens");
        {
            var dv = DestView.Window(1920, 1080, 1, "TL", "TR", true);
            Check(dv.X < 960 && dv.Y >= 0 && dv.Right <= 1920 && dv.Bottom <= 1080 && Math.Abs(dv.W / dv.H - 16f / 9f) < 0.01f, "destination view: 16:9 in its corner, on screen");
            var moved = DestView.Window(1920, 1080, 1, "TR", "TR", true);
            Check(moved.X < 960 && Math.Abs(moved.Y - dv.Y) < 0.5f, "destination view: the pinned map's corner taken = the other side");
            var kept = DestView.Window(1920, 1080, 1, "TR", "TR", false);
            Check(kept.Right > 960, "destination view: the map not pinned = its own corner");
            var dvBig = DestView.Window(1920, 1080, 2, "BL", "TR", true); var dvSmall = DestView.Window(1920, 1080, 0.5f, "BL", "TR", true);
            Check(dvBig.W > dvSmall.W && dvBig.Bottom <= 1080 && dvBig.W <= 1920 * 0.45f + 0.5f, "destination view: size factor, still on screen");
            foreach (var fr in new[] { dv, dvBig, dvSmall })
            {
                float fu = DestView.Unit(fr), cs = 14 * fu, cb = 34 * fu;
                var pc = DestView.Picture(fr, fu, cs, cb); var sq = DestView.Picture(fr, fu, 0, 0);
                bool inside = (fr.Right - pc.Right) + (pc.Y - fr.Y) >= cb - 0.01f && (pc.X - fr.X) + (pc.Y - fr.Y) >= cs && 2 * (fr.Bottom - pc.Bottom) >= cs
                    && pc.Y - fr.Y >= DestView.TitleH(fu) - 0.01f && pc.W > fr.W * 0.8f && pc.H > fr.H * 0.6f;
                Check(inside && sq.W >= pc.W && sq.X > fr.X, "destination view: the picture sits under the title, its corners inside the frame's cut corners");
            }
        }
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
        var wpS0 = new V3(5e5, 3e5, 0);
        ok = Trajectory.Predict(V3.Zero, V3.Zero, new[] { new V3(5e5, 3e5, 0), new V3(1e6, 0, 0) }, 10, 50000, 25e3, pts);
        double nearWp = double.MaxValue; foreach (var q in pts) nearWp = Math.Min(nearWp, V3.Distance(q, new V3(5e5, 3e5, 0)));
        Check(ok && nearWp < 40e3, "projection: passes the waypoint, then arrives");
        // flip and burn: stops at the waypoint (within the 2 km reach), then a straight leg on
        ok = Trajectory.Predict(V3.Zero, V3.Zero, new[] { wpS0, new V3(1e6, 0, 0) }, 10, 50000, 25e3, pts, phases: new List<byte>(), stops: true);
        nearWp = double.MaxValue; foreach (var q in pts) nearWp = Math.Min(nearWp, V3.Distance(q, wpS0));
        double off2 = 0; foreach (var q in pts) if (q.X > wpS0.X + 1e3) off2 = Math.Max(off2, V3.SegmentDistance(wpS0, new V3(1e6, 0, 0), q, out _));
        Check(ok && nearWp < 2500 && off2 < 5e3, "projection, flip and burn: stops at the waypoint (" + nearWp.ToString("0") + " m), then flies straight on (" + off2.ToString("0") + " m off)");
        // the route screen's camera: straight down, north (-Z) up, east (+X) right, the points framed inside the view
        var td = new OrbitCamera { TopDown = true };
        td.Setup(0, 0, 400, 300);
        var tdPts = new List<V3> { new V3(-5e5, 0, 0), new V3(5e5, 2e4, 0), new V3(0, 0, -3e5) };
        td.FrameFlat(tdPts, 20e3);
        td.Setup(0, 0, 400, 300);
        td.Project(new V3(0, 0, -3e5), out _, out var nY, out _); td.Project(new V3(5e5, 0, 0), out var eX, out _, out _); td.Project(new V3(-5e5, 0, 0), out var wX, out _, out _);
        Check(nY < 150 && eX > 200 && wX < 200 && wX > 0 && eX < 400, "route screen camera: north up, east right, all in view");
        // a curved route, the ship moving across it: an arc from its velocity onto the curve, then the curve
        var cleg = RoutePlan.Make("C", V3.Zero, new V3(2e6, 0, 0), V3.Zero, 10, 50000, through: new[] { new V3(1e6, 3e5, 0) });
        var jph = new List<byte>();
        ok = Trajectory.Join(V3.Zero, new V3(0, -3000, 0), cleg, 10, 50000, pts, jph);
        double minY = 0; foreach (var q in pts) minY = Math.Min(minY, q.Y);
        Check(ok && pts.Count == jph.Count && minY < -50e3 && V3.Distance(pts[pts.Count - 1], new V3(2e6, 0, 0)) < 1e3 && jph.Contains(Trajectory.Brake),
            "curve join: swings round from the drift (" + (minY / 1e3).ToString("0") + " km out), then the curve to its end");
        Check(!Trajectory.Join(V3.Zero, cleg.Dir * 3000, cleg, 10, 50000, pts, jph) && pts.Count == 0, "curve join: nothing to correct when already heading along it");
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

    /// <summary>The ship's label in the small pinned map: drawn every frame, in the same spot while the speed digits change.</summary>
    private static void Curves()
    {
        var O = new V3(5e6, 0, 0);
        var a = O; var b = O + new V3(2000e3, 0, 0); var bend = O + new V3(1000e3, 300e3, 0);

        // one bend point: a parabola through it
        var cv = RouteCurve.Build(a, new[] { bend }, b);
        double near = double.MaxValue; foreach (var q in cv) near = Math.Min(near, V3.Distance(q, bend));
        Check(V3.Distance(cv[0], a) < 1 && V3.Distance(cv[cv.Count - 1], b) < 1 && near < 1, "curve: starts, ends and passes through the bend (" + near.ToString("0") + " m)");

        // a detour close to the start (Sol's on the way, the ship a fifth of the way there): the curve goes through it
        // without swinging out behind the start first, so it isn't held to a U-turn's crawl
        var far = O + new V3(18000e3, 0, 0); var side = O + new V3(3600e3, 900e3, 0);
        var dc = RouteCurve.Build(a, new[] { side }, far);
        double back = 0; foreach (var q in dc) back = Math.Min(back, (q - a).Dot(new V3(1, 0, 0)));
        double nearSide = double.MaxValue; foreach (var q in dc) nearSide = Math.Min(nearSide, V3.Distance(q, side));
        var dp = RoutePlan.Make("D", a, far, V3.Zero, 107, 50000, RoutePlan.DefaultClearance, 0, new[] { side });
        Check(back > -50e3 && nearSide < 1 && dp.CornerSpeed > 20000 && dp.Eta < 3600,
            "curve: a detour near the start doesn't swing back (" + (back / 1000).ToString("0") + " km behind, corner " + (dp.CornerSpeed / 1000).ToString("0.0") + " km/s, ETA " + (dp.Eta / 60).ToString("0") + " min)");

        // a bent leg: longer than the chord, planned along the arc, no faster than its corner speed
        var r = RoutePlan.Make("B", a, b, V3.Zero, 10, 50000, RoutePlan.DefaultClearance, 0, new[] { bend });
        double arc = 0; for (int i = 1; i < r.Curve.Count; i++) arc += V3.Distance(r.Curve[i - 1], r.Curve[i]);
        Check(r.Curved && Math.Abs(r.Distance - arc) < 1 && r.Distance > 2000e3 * 1.05, "curve: distance is the arc length (" + (r.Distance / 1000).ToString("0") + " km)");
        double rPlain = RoutePlan.Make("B", a, a + new V3(r.Distance, 0, 0), V3.Zero, 10, 50000).Eta, rHeld = r.Distance / r.CornerSpeed + r.CornerSpeed / 10;
        Check(!double.IsInfinity(r.CornerSpeed) && r.MaxSpeed == 50000 && r.Eta > rPlain + 1 && r.Eta <= rHeld + 1 && Math.Abs(r.BurnTime + r.CoastTime + r.BrakeTime - r.Eta) < 0.01,
            "curve: slowed for the bend (" + r.CornerSpeed.ToString("0") + " m/s there), not held to it all the way (" + r.Eta.ToString("0") + " s; " + rPlain.ToString("0") + " straight, " + rHeld.ToString("0") + " held)");
        // the water: a bent leg burns and brakes at every bend too, so more than its first burn and last brake; a straight one just those
        {
            var straight = RoutePlan.Make("B", a, b, V3.Zero, 10, 50000);
            Check(straight.FuelBurn == straight.BurnTime && straight.FuelBrake == straight.BrakeTime, "fuel: a straight leg burns for its burn and brake times");
            Check(r.FuelBurn + r.FuelBrake > r.BurnTime + r.BrakeTime + 1 && r.FuelBurn + r.FuelBrake <= r.Eta + 0.01 && r.FuelBurn > 0 && r.FuelBrake > 0,
                "fuel: a bent leg counts the slowing and speeding up at its bend (" + (r.FuelBurn + r.FuelBrake).ToString("0") + " s of thrust; " + (r.BurnTime + r.BrakeTime).ToString("0") + " s first burn and last brake)");
            // range on the water in the tanks: 1000 L at 10 L/s and 20 m/s2 is 2000 m/s of speed in all: up to 1000 m/s and back down
            double rg = RouteFuel.Range(1000, 10, 20, 20, 20, 0, 50000);
            Check(Math.Abs(rg - 2 * 1000.0 * 1000 / (2 * 20)) < 1, "fuel range: burn to the speed the water allows, then brake (" + (rg / 1000).ToString("0") + " km)");
            Check(Math.Abs(RouteFuel.Range(1000, 10, 20, 10, 10, 0, 50000) - 2 * rg) < 1, "fuel range: half the thrust goes twice as far on the same water");
            Check(RouteFuel.Range(1000, 10, 20, 20, 20, 3000, 50000) == 0 && RouteFuel.Range(1000, 10, 20, 20, 20, 1000, 50000) > rg,
                "fuel range: the speed you have must be braked too (no water left over for it: 0)");
            Check(double.IsNaN(RouteFuel.Range(1e9, 10, 20, 20, 20, 0, 50000)) && double.IsNaN(RouteFuel.Range(1000, 0, 20, 20, 20, 0, 50000)), "fuel range: no ring when the water reaches the speed cap, or the drives' use isn't known");
            // a route as long as the range just empties the tanks
            var rp = RoutePath.Make("T", V3.Zero, new V3(rg, 0, 0), V3.Zero, 20, 50000, null, 0);
            Check(Math.Abs(RouteFuel.Litres(rp, 20, 10) - 1000) < 5, "fuel range: a route to the ring burns what's in the tanks (" + RouteFuel.Litres(rp, 20, 10).ToString("0") + " L of 1000)");
        }
        Check(Math.Abs(r.AlongAt(r.Eta) - r.Distance) < 1 && r.AlongAt(r.Eta / 2) > r.AlongAt(r.Eta / 4) && Math.Abs(r.AlongAt(r.TimeAt(r.Distance * 0.3)) - r.Distance * 0.3) < 50, "curve: place by time follows the slowed plan");
        // a tight bend beside the dock on a very long flight: slow there only (it was 52 hours for a 20 minute flight)
        {
            var lFar = a + new V3(34000e3, 0, 0);
            var lp = RoutePlan.Make("L", a, lFar, V3.Zero, 62, 50000, RoutePlan.DefaultClearance, 0, new[] { a + new V3(1500, 4000, 0), a + new V3(9000, 6000, 0) });
            double lPlain = RoutePlan.Make("L", a, lFar, V3.Zero, 62, 50000).Eta;
            Check(!double.IsInfinity(lp.CornerSpeed) && lp.CornerSpeed < 2000 && lp.Eta > lPlain && lp.Eta < lPlain + 300 && lp.PeakSpeed > 40000,
                "curve: a tight bend at the start costs minutes, not days (bend " + lp.CornerSpeed.ToString("0") + " m/s, " + (lp.Eta / 60).ToString("0") + " min; " + (lPlain / 60).ToString("0") + " straight)");
        }
        Check(V3.Distance(r.PointAt(0), a) < 1 && V3.Distance(r.PointAt(r.Distance), b) < 1, "curve: PointAt follows the arc end to end");

        // flip and burn: a leg per waypoint; parabolic: one leg through them all
        var wps = new List<Waypoint> { new Waypoint { Name = "W1", Pos = O + new V3(700e3, 200e3, 0), User = true }, new Waypoint { Name = "W2", Pos = O + new V3(1400e3, 100e3, 0), User = true } };
        var fb = RoutePath.Make("Dest", a, b, V3.Zero, 10, 50000, wps, 0);
        var pa = RoutePath.Make("Dest", a, b, V3.Zero, 10, 50000, wps, 0, RoutePlan.DefaultClearance, 0, true);
        Check(fb.Legs.Count == 3 && !fb.Curved && pa.Legs.Count == 1 && pa.Curved && pa.Final.Curved, "legs: flip and burn stops at each point, parabolic is one leg");
        Check(fb.Legs[0].ExitSpeed > 100 && fb.Legs[0].ExitSpeed < fb.Legs[0].PeakSpeed && fb.Legs[1].ClosingSpeed > 100 && fb.Final.ExitSpeed == 0,
            "legs: flip and burn slows to a turn speed at each point (" + fb.Legs[0].ExitSpeed.ToString("0") + " m/s), not a stop, and stops at the end");
        double tv = RouteCurve.TurnSpeed(a, O + new V3(700e3, 200e3, 0), O + new V3(1400e3, 100e3, 0), 10, 2000, 50000);
        double sh0 = Trajectory.SinHalfTurn(a, O + new V3(700e3, 200e3, 0), O + new V3(1400e3, 100e3, 0));
        Check(tv * tv * sh0 * sh0 / 20 <= 2000 + 1, "legs: the turn cuts the corner by no more than the 2 km reach (" + (tv * tv * sh0 * sh0 / 20).ToString("0") + " m)");
        Check(RouteCurve.TurnSpeed(a, O + new V3(1e6, 0, 0), O + new V3(2e6, 0, 0), 10, 2000, 50000) == 50000 && RouteCurve.TurnSpeed(a, O + new V3(1e6, 0, 0), a, 10, 2000, 50000) < 250,
            "legs: no slowing for a straight-on point, nearly a stop to double back");
        Check(pa.Eta < fb.Eta, "legs: parabolic is quicker than stopping at each point (" + pa.Eta.ToString("0") + " s vs " + fb.Eta.ToString("0") + " s)");
        Check(RouteExport.Parse(RouteExport.Build(pa, 5), out var ex) && ex.Points.Count > 10 && ex.Points.TrueForAll(q => q.Kind == "curve" || q.Equals(ex.Points[ex.Points.Count - 1])),
            "legs: parabolic export is curve points, then the stop (" + ex.Points.Count + ")");
        bool beyond = false; var stop = ex.Points[ex.Points.Count - 1].Pos;
        for (int i = 0; i + 1 < ex.Points.Count; i++) if (V3.Distance(ex.Points[i].Pos, b) < V3.Distance(stop, b)) beyond = true;
        Check(!beyond, "legs: no curve point lies beyond the stop");

        // a bent leg in flip and burn mode
        var bends = new Dictionary<string, V3> { ["Dest"] = bend };
        var bp = RoutePath.Make("Dest", a, b, V3.Zero, 10, 50000, null, 0, RoutePlan.DefaultClearance, 0, false, bends);
        Check(bp.Legs.Count == 1 && bp.Final.Curved, "bend: the dragged leg curves");
        Check(RouteExport.Parse(RouteExport.Build(bp, 6), out ex) && ex.Points.Exists(q => q.Kind == "curve"), "bend: exported as curve points");
        var past = RoutePath.Make("Dest", O + new V3(1500e3, 0, 0), b, V3.Zero, 10, 50000, null, 0, RoutePlan.DefaultClearance, 0, false, bends);
        Check(!past.Final.Curved, "bend: dropped once the ship is past it");

        // the tracker keeps bends across a replan and drops them on a new destination
        var t = new RouteTracker { Avoid = false };
        t.Update("Dest", b, true, a, V3.Zero, 10, 0);
        t.Bend("Dest", bend);
        t.Update("Dest", b, true, a, V3.Zero, 10, 0);
        Check(t.Path != null && t.Path.Final.Curved, "bend: the tracker plans through it");
        t.Update("Dest", b, true, a + new V3(1000, 0, 0), V3.Zero, 10, 0);
        Check(t.Bends.ContainsKey("Dest"), "bend: kept as the ship moves");
        t.Update("Other", O + new V3(0, 0, 900e3), true, a, V3.Zero, 10, 0);
        Check(t.Bends.Count == 0, "bend: dropped on a new destination");
        int rev = t.Revision; t.Curved = true;
        Check(t.Revision > rev, "legs: switching mode replans");
    }

    private static void ShipLabelSteady()
    {
        Check(MapSnapshot.ShortShipName("Rocinante", "OPA", "Corvette") == "Rocinante"
              && MapSnapshot.ShortShipName("SDX2- Comet Shuttle_testing", "OPA", "Hauler") == "OPA Hauler"
              && MapSnapshot.ShortShipName("SDX2- Comet Shuttle_testing", "", "Hauler") == "Hauler"
              && MapSnapshot.ShortShipName("SDX2- Comet Shuttle_testing", "OPA", MapSnapshot.Skiff) == "OPA Skiff"
              && MapSnapshot.ShortShipName("SDX2- Comet Shuttle_testing", "OPA", "") == "SDX2- Comet Shutt..."
              && MapSnapshot.ShortShipName("SDX2- Comet Shuttle_testing", "", "").Length <= MapSnapshot.MaxShipName,
              "ship label: names up to 20 characters as they are; longer: faction tag + core class, or cut short when there's no class (not a ship)");
        Check(MapSnapshot.CoreClass("sdx_shipcoreHauler") == "Hauler" && MapSnapshot.CoreClass("sdx_shipcoreStationAdmin") == "Station Admin"
              && MapSnapshot.CoreClass("sdx_shipcoreNoCore") == "Skiff" && MapSnapshot.CoreClass("LargeBlockGyro") == "" && MapSnapshot.CoreClass(null) == "",
              "ship class: from the SDX2 core block's subtype (NoCore: Skiff; not a core block: none)");
        var snap = new MapSnapshot { HasShip = true, ShipName = "SDX2- Comet Shuttle_testing", FactionTag = "OPA", ShipClass = "Hauler", ShipPos = new V3(-1.2e6, 0, 9e6) };
        var scene = new MapScene { Snap = snap, Compact = true, Mini = true, Help = "" };
        scene.Cam.Setup(0, 0, 300, 260);
        scene.Cam.Target = scene.Cam.GoalTarget = snap.ShipPos; scene.Cam.Distance = scene.Cam.GoalDistance = 2e6;
        scene.MouseX = float.NaN;
        var spots = new List<(float X, float Y)>();
        double[] speeds = { 44690, 44740, 45110, 41111, 48888, 45110, 39999 };
        foreach (var sp in speeds)
        {
            snap.ShipVel = new V3(0, 0, -sp);
            var c = new PropCanvas();
            scene.Draw(c, new Box(0, 0, 300, 260));
            var t = c.Texts.Find(x => x.Text.StartsWith("OPA Hauler"));
            spots.Add(t.Text == null ? (float.NaN, float.NaN) : (t.X, t.Y));
        }
        Check(spots.TrueForAll(q => !float.IsNaN(q.X)) && spots.TrueForAll(q => Math.Abs(q.X - spots[0].X) < 0.01f && Math.Abs(q.Y - spots[0].Y) < 0.01f),
              "pinned map: the ship's name is drawn every frame in the same spot as the speed changes (" + string.Join(" ", spots.ConvertAll(q => q.X.ToString("0.0"))) + ")");
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
        var O = new V3(5e6, 0, 0);
        var t = new RouteTracker { Avoid = false };
        t.Update("Dest", O + new V3(300e3, 0, 0), true, O, new V3(0, 0, 0), 10, 0);
        Check(t.InsertUser(t.NextUserName(), O + new V3(200e3, 0, 0)) && t.InsertUser(t.NextUserName(), O + new V3(100e3, 0, 0)), "waypoint: two added");
        Check(t.UserPoints.Count == 2 && t.UserPoints[0].Name == "Waypoint 2" && t.UserPoints[1].Name == "Waypoint 1", "waypoint: the nearer one is flown first");
        Check(!t.InsertUser("dup", O + new V3(100e3, 50, 0)), "waypoint: no duplicate within 100 m");
        Check(t.Waypoints.Count == 2 && t.Waypoints[0].User && t.Path != null, "waypoint: route replanned through them");

        // raising one keeps its place and name, and the route goes through the new height
        int rev = t.Revision;
        Check(t.MoveUser(0, O + new V3(100e3, 40e3, 0)) && t.UserPoints[0].Name == "Waypoint 2" && t.UserPoints[0].Pos.Y == 40e3 && t.Revision > rev, "waypoint height: moved up, same slot, replanned");
        Check(Math.Abs(t.Waypoints[0].Pos.Y - 40e3) < 1, "waypoint height: route goes through the new height");
        Check(!t.MoveUser(0, O + new V3(200e3, 0, 0)) && !t.MoveUser(5, O + new V3(0, 1e3, 0)), "waypoint height: can't move onto another waypoint or a missing one");

        // leaving the seat drops the plan, and F&B's same pick doesn't bring it back; a new pick does
        t.Cancel("Dest", O + new V3(300e3, 0, 0));
        Check(t.TargetName == null && t.UserPoints.Count == 0 && t.Path == null, "cancel: plan and waypoints dropped");
        t.Update("Dest", O + new V3(300e3, 0, 0), true, O, new V3(0, 0, 0), 10, 0);
        Check(t.TargetName == null && t.Path == null, "cancel: F&B's unchanged pick isn't planned again");
        t.Update("Other", O + new V3(0, 0, 400e3), true, O, new V3(0, 0, 0), 10, 0);
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
        Check(PoiMatch.Rank("Enceladus Station", null, "Enceladus", 5190) == PoiMatch.NoMatch
              && PoiMatch.Rank("Enceladus Station", null, "Enceladus-1234567890d5000", 5190) == PoiMatch.NoMatch
              && PoiMatch.Rank("Enceladus Station", null, "SA.Enceladus.STN", 30) == 2, "poi: a ground facility doesn't match its moon");
        Check(PoiMatch.Rank("Vesta Station", "Belt.Vesta.STN", "Belt.Tycho.STN", 2e4) == PoiMatch.NoMatch, "poi: another Belt station isn't a match (belt is a generic word)");
        Check(PoiMatch.Rank("Somewhere", null, "Unrelated", 800) == 3 && PoiMatch.Rank("Mars", null, "Mars", 2e6) == PoiMatch.NoMatch, "poi: near anything within 5 km; names only within 1000 km");
    }

    /// <summary>The second screen's touch gestures, in the map's mouse terms.</summary>
    private static void Touch()
    {
        var g = new TouchGestures();
        g.Down(1, 100, 100, 0);
        var f = g.Step(0.01);
        Check(f.X == 100 && !f.Left && !f.NewLeft, "touch: the first frame only says where the finger is");
        f = g.Step(0.04);
        Check(f.Left && f.NewLeft && !f.Shift, "touch: then it presses");
        g.Up(1, 102, 101);
        f = g.Step(0.07);
        Check(f.LeftReleased && !f.Shift && !f.Left, "touch: a tap lets go as a plain click (focus)");
        Check(!g.Touching, "touch: idle after the tap");

        g.Down(2, 50, 50, 1); g.Up(2, 50, 50);
        var a = g.Step(1.01); var b = g.Step(1.04); var c = g.Step(1.07);
        Check(!a.NewLeft && b.NewLeft && c.LeftReleased, "touch: a tap within one frame still presses, then lets go");

        g.Down(3, 200, 200, 2); g.Step(2.01); g.Step(2.04);
        f = g.Step(2.7);
        Check(f.HoldStarted && f.Left, "touch: held still, it says so");
        g.Up(3, 201, 200);
        f = g.Step(2.75);
        Check(f.LeftReleased && f.Shift, "touch: let go after a hold: Shift+click (set destination)");

        g.Down(4, 200, 200, 3); g.Step(3.01); g.Step(3.04);
        g.Move(4, 260, 200); f = g.Step(3.07);
        Check(f.Left && f.X == 260, "touch: a drag holds the press");
        g.Up(4, 260, 200); f = g.Step(3.8);
        Check(f.LeftReleased && !f.Shift, "touch: a drag never sets the destination");

        g.Mode = TapMode.Destination;
        g.Down(5, 10, 10, 4); g.Step(4.01); g.Step(4.04); g.Up(5, 10, 10); f = g.Step(4.07);
        Check(f.LeftReleased && f.Shift, "touch: TAP DESTINATION makes a tap a Shift+click");

        g.Mode = TapMode.Waypoint;
        g.Down(6, 10, 10, 5); g.Step(5.01); f = g.Step(5.04);
        Check(f.NewRight && f.Right && f.Shift && !f.Left, "touch: TAP WAYPOINTS presses Shift+right");
        g.Up(6, 10, 10); f = g.Step(5.07);
        Check(f.RightReleased && !f.Right, "touch: and lets go of it");
        g.Mode = TapMode.Focus;

        g.Down(7, 100, 300, 6); g.Step(6.01); g.Step(6.04);
        g.Down(8, 300, 300, 6.05); f = g.Step(6.07);
        Check(f.Cancel && g.Pinching && !f.LeftReleased, "touch: a second finger drops the press without a click");
        g.Move(7, 0, 300); g.Move(8, 400, 300); f = g.Step(6.1);
        Check(Near(f.Spread, 2, 1e-6) && Near(f.PanX, 0, 1e-6) && Near(f.Twist, 0, 1e-6), "touch: spreading two fingers zooms by their spread " + f.Spread);
        g.Move(7, 50, 350); g.Move(8, 450, 350); f = g.Step(6.13);
        Check(Near(f.PanX, 50, 1e-4) && Near(f.PanY, 50, 1e-4) && Near(f.Spread, 1, 1e-6), "touch: moving both pans by their middle");
        g.Move(8, 50, 750); f = g.Step(6.16);
        Check(Near(f.Twist, Math.PI / 2, 1e-6), "touch: turning one round the other twists " + f.Twist);
        g.Up(8, 50, 750); f = g.Step(6.2);
        Check(!g.Pinching && !f.NewLeft && !f.Left, "touch: one finger left after a pinch does nothing");
        g.Move(7, 90, 350); f = g.Step(6.23);
        Check(!f.Left && float.IsNaN(f.X), "touch: still nothing until every finger is up");
        g.Up(7, 90, 350); g.Step(6.26);
        g.Down(9, 5, 5, 7); g.Step(7.01); f = g.Step(7.04);
        Check(f.NewLeft, "touch: a fresh finger presses again");
    }

    /// <summary>The second screen records the map on the game thread and plays it back in its window: the same picture.</summary>
    private static void Recorded(string outDir)
    {
        var snap = new MapSnapshot { HasShip = true, ShipName = "Rocinante", ShipPos = SystemData.FindWorld("Ceres").Pos + new V3(300e3, 0, 0), ShipVel = new V3(500, 0, 0), MaxAccel = 10 };
        MapScene Scene()
        {
            var sc = new MapScene { Snap = snap, Help = "TOUCH|Drag orbit" + MapScene.Sep + "Pinch zoom" };
            sc.Cam.Yaw = 0.9; sc.Cam.Pitch = 0.62; sc.Cam.Setup(0, 0, 1280, 800); sc.FrameDefault(true); sc.MouseX = float.NaN;
            return sc;
        }
        using var direct = new Bitmap(1280, 800);
        using (var g = Graphics.FromImage(direct)) { g.Clear(Color.Black); Scene().Draw(new GdiMapCanvas(g), new Box(0, 0, 1280, 800)); }
        using var played = new Bitmap(1280, 800);
        using var mbmp = new Bitmap(1, 1);
        using var mg = Graphics.FromImage(mbmp);
        var measure = new GdiMapCanvas(mg);
        var list = new DrawList { Measure = measure.TextWidth };
        list.Clear(1280, 800);
        Scene().Draw(list, new Box(0, 0, 1280, 800));
        Check(list.Count > 100, "recorded frame has the map in it (" + list.Count + " calls)");
        using (var g = Graphics.FromImage(played)) { g.Clear(Color.Black); list.Replay(new GdiMapCanvas(g)); }
        int diff = 0;
        for (int y = 0; y < 800; y += 2) for (int x = 0; x < 1280; x += 2) if (direct.GetPixel(x, y) != played.GetPixel(x, y)) diff++;
        Check(diff < 50, "played back frame matches the direct one (" + diff + " pixels differ)");
        played.Save(Path.Combine(outDir, "second-screen.png"), ImageFormat.Png);
    }

    /// <summary>Every destination-view overlay in the theme it belongs to, on the stand-in picture (previews/feed-overlays.png).</summary>
    private static void FeedSheet(string outDir)
    {
        var rows = new (FeedOverlay O, MapTheme T)[]
        {
            (FeedOverlay.TachiLock, MapTheme.Tachi), (FeedOverlay.TachiArcs, MapTheme.Tachi), (FeedOverlay.UnnPlot, MapTheme.Unn),
            (FeedOverlay.BelterTag, MapTheme.Belter), (FeedOverlay.BelterScrap, MapTheme.Belter), (FeedOverlay.ProtoHex, MapTheme.Protogen),
            (FeedOverlay.ProtoRing, MapTheme.Protogen), (FeedOverlay.OpaContact, MapTheme.Opa), (FeedOverlay.ClassicCorners, MapTheme.Classic),
            (FeedOverlay.ClassicRulers, MapTheme.Classic),
        };
        float bw = 560, bh = 315, gap = 20;
        using var bmp = new Bitmap((int)(2 * bw + 3 * gap), (int)(5 * (bh + gap) + gap));
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(System.Drawing.Color.FromArgb(40, 44, 52));
        var c = new GdiMapCanvas(g);
        var was = MapTheme.Current;
        for (int i = 0; i < rows.Length; i++)
        {
            MapTheme.Current = rows[i].T;
            var box = new Box(gap + (i % 2) * (bw + gap), gap + (i / 2) * (bh + gap), bw, bh);
            DestView.DrawWindow(c, box, rows[i].T, "Pallas Station", 4820, rows[i].O, true, 1.3, true, null, true, standIn: true);
        }
        MapTheme.Current = was;
        bmp.Save(Path.Combine(outDir, "feed-overlays.png"), ImageFormat.Png);
        foreach (var t in MapTheme.All) Check(DestOverlay.Default(t.Style) != FeedOverlay.None, "feed overlay: " + t.Name + " has one of its own");
        var ms = new MapSettings { FeedNoise = false };
        ms.Overlays["Tachi"] = FeedOverlay.TachiArcs; ms.Overlays["Classic"] = FeedOverlay.None; ms.ColorsOf("Unn")["OvLine"] = 0x123456;
        string ini = Path.Combine(Path.GetTempPath(), "sdx2map_overlay_test.ini");
        ms.Save(ini); var back = MapSettings.Load(ini); File.Delete(ini);
        Check(!back.FeedNoise && back.OverlayOf(MapTheme.Tachi) == FeedOverlay.TachiArcs && back.OverlayOf(MapTheme.Classic) == FeedOverlay.None
            && back.OverlayOf(MapTheme.Belter) == FeedOverlay.BelterTag && new MapSettings().FeedNoise, "feed overlay: choice per theme and noise saved and loaded (theme's own by default)");
        var unn = back.Look("UNN");
        Check(unn.OverlayLine.R == 0x12 && unn.OverlayLine.B == 0x56 && unn.OverlayTrim.R == MapTheme.Unn.Trim.R && MapTheme.Tachi.OverlayLine.R == MapTheme.Tachi.Accent.R,
            "feed overlay: colours default to the theme's accent and trim, and take the player's own");
        var rec = new RecordCanvas();
        foreach (var (o, _) in DestOverlay.Styles)
        {
            int before = rec.Count;
            DestOverlay.Draw(rec, new Box(0, 0, 400, 225), o, MapTheme.Tachi, 1, "X", 1000, true, true, 0);
            Check(o == FeedOverlay.None ? rec.Count == before : rec.Count > before, "feed overlay draws: " + o);
        }
    }

    /// <summary>Every class's ship icon in every theme, with each theme's trajectory marker (previews/ship-icons.png).</summary>
    private static void ShipIconSheet(string outDir)
    {
        var themes = new[] { MapTheme.Classic, MapTheme.Tachi, MapTheme.Unn, MapTheme.Belter, MapTheme.Protogen, MapTheme.Opa };
        float rowH = 220, w = 1800;
        using var bmp = new Bitmap((int)w, (int)(rowH * (themes.Length + 2)));
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var c = new GdiMapCanvas(g);
        var was = MapTheme.Current;
        for (int i = 0; i < themes.Length; i++)
        {
            MapTheme.Current = themes[i];
            new MapScene().ShipSheet(c, 0, i * rowH, w, rowH);
        }
        // the icons taken from real ships, in two themes
        MapTheme.Current = MapTheme.Classic; new MapScene().ShipSheet(c, 0, themes.Length * rowH, w, rowH, true);
        MapTheme.Current = MapTheme.Protogen; new MapScene().ShipSheet(c, 0, (themes.Length + 1) * rowH, w, rowH, true);
        MapTheme.Current = was;
        bmp.Save(Path.Combine(outDir, "ship-icons.png"), ImageFormat.Png);
        foreach (var cls in new[] { "Skiff", "Barge", "Hauler", "Picket", "Corvette", "Frigate", "Cruiser", "Battleship", "Carrier", "Dreadnought", "Outpost", "Installation", "Station Admin" })
            Check(ShipIcons.For(cls) != null, "ship icon for " + cls);
        foreach (var cls in new[] { "Skiff", "Barge", "Hauler", "Picket", "Corvette", "Frigate", "Installation" })
        {
            var sm = ShipIcons.Sampled(cls);
            Check(sm != null && sm.Runs.Count > 5 && sm.Details.Length >= 8 && sm.From != null, "real ship icon for " + cls + " (" + sm?.From + ")");
        }
        Check(ShipIcons.Pick("Cruiser", true) == ShipIcons.For("Cruiser"), "no real Cruiser sampled: the drawn icon");
        var sq2 = ShipIcons.FromMask("T", "t", new[] { "##", "##" });
        Check(sq2.Runs.Count == 2 && sq2.Details.Length == 8, "real ship icon: a 2x2 mask is two runs and a 4-sided outline");
        Check(ShipIcons.For("") == null && ShipIcons.For("Nonsense") == null, "no class: the plain marker");
        DestinationListChecks();
        // the icon from the ship you fly: a flat hull (nose along -Z) with a pod on its right and a fin on top at the tail
        var hull = new List<ShipMask.Cells>
        {
            new ShipMask.Cells(-4, 0, 0, 4, 1, 29), new ShipMask.Cells(-1, 0, -6, 1, 1, -1),
            new ShipMask.Cells(5, 0, 10, 6, 1, 15), new ShipMask.Cells(0, 2, 24, 0, 5, 29),
        };
        var fwdAxis = (0, 0, -1); var upAxis = (0, 1, 0);
        Check(ShipMask.AutoSide(hull, fwdAxis, upAxis) == 0, "own ship icon: a flat ship is shown from the top");
        var topM = ShipMask.Build(hull, fwdAxis, upAxis, 0); var botM = ShipMask.Build(hull, fwdAxis, upAxis, 2);
        var rightM = ShipMask.Build(hull, fwdAxis, upAxis, 1); var leftM = ShipMask.Build(hull, fwdAxis, upAxis, 3);
        Check(topM[0].Length == ShipMask.Length && topM.Length == 11, "own ship icon: " + ShipMask.Length + " cells nose to tail, 11 across");
        int Hull(string r) { int n = 0; foreach (char ch in r) if (ch == '#') n++; return n; }
        int noseCol = 0, tailCol = 0; foreach (var r in topM) { if (r[0] == '#') noseCol++; if (r[r.Length - 1] == '#') tailCol++; }
        Check(noseCol > 0 && noseCol < tailCol, "own ship icon: the narrow nose is at column 0 (" + noseCol + " rows against " + tailCol + " at the tail)");
        bool mirrored = topM.Length == botM.Length; for (int i = 0; mirrored && i < topM.Length; i++) mirrored = topM[i] == botM[topM.Length - 1 - i];
        Check(mirrored, "own ship icon: the bottom is the top mirrored");
        Check(Hull(topM[topM.Length - 1]) + Hull(topM[topM.Length - 2]) < Hull(topM[topM.Length / 2]) && Hull(topM[0]) >= Hull(topM[topM.Length - 1]), "own ship icon: from the top the pod is on the right (the last rows)");
        mirrored = rightM.Length == leftM.Length; for (int i = 0; mirrored && i < rightM.Length; i++) mirrored = rightM[i] == leftM[rightM.Length - 1 - i];
        Check(mirrored && string.Join("", rightM) != string.Join("", topM), "own ship icon: the left side is the right side mirrored, and not the top");
        Check(ShipMask.Build(hull, fwdAxis, upAxis, ShipMask.Auto)[3] == topM[3] && ShipMask.Build(new List<ShipMask.Cells>(), fwdAxis, upAxis, 0) == null, "own ship icon: auto is the top here; no blocks, no icon");
        var wide = ShipMask.Build(new List<ShipMask.Cells> { new ShipMask.Cells(0, 0, 0, 39, 0, 9) }, fwdAxis, upAxis, 0);
        Check(wide.Length == ShipMask.Length && wide[0].Length == 9, "own ship icon: a ship wider than long keeps its proportions (" + wide.Length + " x " + wide[0].Length + ")");
        var wideShape = ShipIcons.FromMask("T", "t", wide);
        float maxX = 0, maxY = 0; foreach (var (x0, x1, yy) in wideShape.Runs) { maxX = Math.Max(maxX, Math.Max(Math.Abs(x0), Math.Abs(x1))); maxY = Math.Max(maxY, Math.Abs(yy)); }
        Check(maxX < 0.3f && maxY > 0.9f && maxY <= 1.01f, "own ship icon: the wide ship's icon spans its beam, not its length");
        {
            string sf = Path.Combine(Path.GetTempPath(), "sdx2map-ship.ini");
            new MapSettings { ShipIconFromShip = true, ShipIconSide = 2, PerShip = false }.Save(sf);
            var back = MapSettings.Load(sf);
            Check(back.ShipIconFromShip && back.ShipIconSide == 2 && !back.PerShip, "settings keep the ship icon's side and the all-ships switch");
            File.WriteAllText(sf, "RealShipIcons=True\nShipIconSide=9\n");
            back = MapSettings.Load(sf);
            Check(back.ShipIconFromShip && back.ShipIconSide == ShipMask.Auto && back.PerShip, "an older file's real-ship icons become the current ship's; a bad side is auto; each ship its own by default");
            File.Delete(sf);
        }
        {
            using var sb = new Bitmap(1200, 330);
            using var sg = Graphics.FromImage(sb);
            sg.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var sc = new GdiMapCanvas(sg);
            var masks = new[] { topM, rightM, botM, leftM };
            for (int i = 0; i < 4; i++)
                new MapScene().ShipPreview(sc, ShipIcons.FromMask("Hauler", "Test ship", masks[i]), i * 300, 0, 296, 330, "Test ship: " + ShipMask.SideNames[i].ToLowerInvariant(), null);
            sb.Save(Path.Combine(outDir, "ship-icon-sides.png"), ImageFormat.Png);
        }
        // the fill rows: a square from -1 to 1 is 2 wide on every row
        var sq = new (float X, float Y)[] { (-1, -1), (1, -1), (1, 1), (-1, 1) };
        var row = ShipIcons.Row(sq, 0.3f);
        Check(row.Count == 2 && Math.Abs(row[0] + 1) < 1e-5 && Math.Abs(row[1] - 1) < 1e-5, "ship icon fill: a row across a square");
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
            Water = 400e3, WaterCap = 1e6, WaterRate = 55.8,
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
            var scene = new MapScene { Snap = snap, Path = route, Help = "VIEW|Drag orbit" + MapScene.Sep + "Right-drag pan" + MapScene.Sep + "Wheel zoom" + MapScene.Sep + "Click focus" + MapScene.Sep + "S follow ship: off" + MapScene.Sep + "F frame route" + MapScene.Sep + "H whole system\nROUTE|Shift+click set destination" + MapScene.Sep + "Shift+right-click add / remove waypoint" + MapScene.Sep + "A route plan" + MapScene.Sep + "N Flip and Burn list" + MapScene.Sep + "R re-plan" + MapScene.Sep + "V detours: on" + MapScene.Sep + "C copy flip GPS\nSHOW|G grid: on" + MapScene.Sep + "O orbits: on" + MapScene.Sep + "X sectors: on" + MapScene.Sep + "T stations: on" + MapScene.Sep + "P GPS: on" + MapScene.Sep + "L labels: on" + MapScene.Sep + "J moons: on" + MapScene.Sep + "K theme: Classic" + MapScene.Sep + "Q pin corner" + MapScene.Sep + "Esc / M close" };
            scene.Cam.Yaw = 0.9; scene.Cam.Pitch = 0.62;
            scene.Cam.Setup(0, 0, 1920, 1080);
            scene.FrameDefault(true);
            scene.MouseX = float.NaN;
            scene.Draw(new GdiMapCanvas(g), new Box(0, 0, 1920, 1080));
            Check(scene.Picks.Exists(p => p.Kind == "Destination"), "full map: destination is pickable");
            bmp.Save(Path.Combine(outDir, "full-map" + sfx + ".png"), ImageFormat.Png);

            // the autopilot card sits in the route panel's top slot; the panel key mirrors the whole panel to the right
            {
                var left = scene.PanelBox;
                bool noSlot = scene.ControlsSlot.W <= 0;
                Check(noSlot, "panel" + sfx + ": no autopilot slot without the card");
                scene.ControlsHeight = p => AutopilotPanel.MapCardHeight(p, false);
                var ui = new PanelUi();
                void Shot(bool right, string name)
                {
                    scene.PanelOnRight = right;
                    g.Clear(Color.FromArgb(30, 34, 40));
                    var gc = new GdiMapCanvas(g);
                    scene.Draw(gc, new Box(0, 0, 1920, 1080));
                    ui.Input(gc, float.NaN, float.NaN, false, false, false);
                    AutopilotPanel.MapCard(ui, scene.ControlsSlot, scene.PanelPx, "Ceres Station", false, false, true, "1.2 Mm  ETA 2 h 10 min");
                    bmp.Save(Path.Combine(outDir, name + sfx + ".png"), ImageFormat.Png);
                }
                Shot(false, "full-map-card");
                if (sfx == "")
                {
                    // the card while flying (STOP AUTOPILOT / FULL STOP) and while braking to a standstill
                    foreach (var (engagedNow, brakingNow, name) in new[] { (true, false, "full-map-card-flying"), (false, true, "full-map-card-braking") })
                    {
                        g.Clear(Color.FromArgb(30, 34, 40));
                        var gc = new GdiMapCanvas(g);
                        scene.PanelOnRight = false;
                        scene.Draw(gc, new Box(0, 0, 1920, 1080));
                        ui.Input(gc, float.NaN, float.NaN, false, false, false);
                        AutopilotPanel.MapCard(ui, scene.ControlsSlot, scene.PanelPx, "Ceres Station", engagedNow, false, true,
                            brakingNow ? "2.40 km/s  stops in 120 km  dampeners on under 50 m/s" : "Moving the controls also hands the ship back", brakingNow);
                        bmp.Save(Path.Combine(outDir, name + ".png"), ImageFormat.Png);
                    }
                    var probeUi = new PanelUi(); var mc = new MeasureCanvas();
                    probeUi.Input(mc, float.NaN, float.NaN, false, false, false);
                    Check(AutopilotPanel.MapCard(probeUi, new Box(0, 0, 300, 200), 14, "X", true, false, true, "h").Equals(default(AutopilotPanel.CardHit)), "card: no click, no action");
                }
                var withCard = scene.PanelBox; var slotL = scene.ControlsSlot;
                Check(slotL.W > 0 && slotL.H > 0, "panel" + sfx + ": the autopilot card has a slot");
                Check(slotL.X >= withCard.X - 1 && slotL.Right <= withCard.Right + 1 && slotL.Y >= withCard.Y - 1 && slotL.Y < withCard.Y + withCard.H * 0.5f, "panel" + sfx + ": the card's slot is at the top of the panel");
                Check(Math.Abs(withCard.X - left.X) < 1, "panel" + sfx + ": the card doesn't move the panel sideways");
                Shot(true, "full-map-panel-right");
                var right = scene.PanelBox; var slotR = scene.ControlsSlot;
                Check(Math.Abs((1920 - right.Right) - withCard.X) < 1 && Math.Abs(right.W - withCard.W) < 1, "panel" + sfx + ": on the right it keeps the same gap from the edge");
                Check(Math.Abs((1920 - slotR.Right) - slotL.X) < 1 && Math.Abs(slotR.Y - slotL.Y) < 1, "panel" + sfx + ": the card's slot is mirrored");
                scene.PanelOnRight = false; scene.ControlsHeight = null;
            }

            // the same view while Shift+right-dragging a new waypoint 150 km above the route's first leg
            if (sfx == "")
            {
                g.Clear(Color.FromArgb(30, 34, 40));
                var pts = route.Points();
                scene.GhostBase = V3.Lerp(pts[0], pts[1], 0.5) + new V3(0, 0, 0);
                scene.Ghost = scene.GhostBase + new V3(0, 150e3, 0);
                scene.Draw(new GdiMapCanvas(g), new Box(0, 0, 1920, 1080));
                bmp.Save(Path.Combine(outDir, "full-map-height-drag.png"), ImageFormat.Png);
                scene.Ghost = null;

                // the route's water: burn and brake at the thrust used, none on the coast
                var wl = scene.RouteLinesText().Find(x => x.Text.StartsWith("Water"));
                double wantL = RouteFuel.Litres(route, snap.MaxAccel, snap.WaterRate);
                Check(wl.Text != null && wantL > 0 && wl.Text.StartsWith("Water  now 40 % → " + Fmt.Num(RouteFuel.LeftAfter(snap.Water, snap.WaterCap, wantL) * 100, "0") + " % @ ") && wl.Text.EndsWith(", max 100 % thrust"), "fuel: the route panel shows the water it burns (" + wl.Text + ")");
                snap.Water = wantL * 0.5;
                Check(scene.RouteLinesText().Find(x => x.Text.StartsWith("Water")).Text.Contains("→ dry before "), "fuel: short of water, it says so");
                // and the map rings how far that water goes
                g.Clear(Color.FromArgb(30, 34, 40));
                scene.Draw(new GdiMapCanvas(g), new Box(0, 0, 1920, 1080));
                bmp.Save(Path.Combine(outDir, "full-map-water-range.png"), ImageFormat.Png);
                double ringR = RouteFuel.Range(snap.Water, snap.WaterRate, snap.MaxAccel, route.MaxAccel, route.Final.BrakeUsed, snap.ShipVel.Length, route.MaxSpeed);
                Check(ringR > 0 && ringR < route.Distance, "fuel: short of water, the range ring falls short of the destination (" + Fmt.Dist(ringR) + " of " + Fmt.Dist(route.Distance) + ")");
                snap.Water = 400e3;
                // at the route's thrust cap: the same route planned at 60 % thrust and brake
                var capped = tracker.PathWith(0.6, 0.6, 0, snap.ShipVel, snap.MaxAccel, 50000);
                double cappedL = RouteFuel.Litres(capped, snap.MaxAccel, snap.WaterRate);
                Check(capped != null && Near(capped.First.MaxAccel, snap.MaxAccel * 0.6, 1e-6) && cappedL > 0, "fuel: the route planned at its thrust cap (" + RouteFuel.Volume(cappedL) + " at 60 %, " + RouteFuel.Volume(wantL) + " at 100 %)");
                snap.WaterUse = cappedL; snap.WaterThrust = 0.6;
                var wl2 = scene.RouteLinesText().Find(x => x.Text.StartsWith("Water")).Text;
                Check(wl2.Contains("→ " + Fmt.Num(RouteFuel.LeftAfter(snap.Water, snap.WaterCap, cappedL) * 100, "0") + " % @ ") && wl2.EndsWith(", max 60 % thrust"), "fuel: the panel shows the capped estimate and the thrust (" + wl2 + ")");
                snap.WaterUse = snap.WaterThrust = double.NaN;
                Check(wl.Text.Contains(" % @ Tycho, max"), "fuel: the destination's short name (" + wl.Text + ")");
                Check(MapScene.ShortName("Tycho Station") == "Tycho" && MapScene.ShortName("Andromeda Station") == "Andromeda" && MapScene.ShortName("Belt.Tycho.STN") == "Tycho"
                      && MapScene.ShortName("JU.Ganymede.STN") == "Ganymede" && MapScene.ShortName("Waypoint 1") == "Waypoint 1" && MapScene.ShortName("Station") == "Station", "fuel: short destination names");
                // gravity: no row without a well; the route going into one, and the ship in one
                Check(scene.GravityLine() == null && !scene.RouteLinesText().Exists(x => x.Text.StartsWith("GRAVITY")), "gravity: no row with no well");
                snap.GravityEntry = route.Final.Target; snap.GravityEntryName = "Mars"; snap.GravityAlong = 1.2e6;
                Check(scene.RouteLinesText().Exists(x => x.Text == "GRAVITY  route enters Mars's well in " + Fmt.Dist(1.2e6)), "gravity: the route enters a well (" + scene.GravityLine() + ")");
                snap.GravityEntry = null; snap.InGravity = true; snap.GravityName = "Earth"; snap.GravityG = 0.42;
                Check(scene.GravityLine() == "GRAVITY  in Earth's well, 0.42 g", "gravity: the ship in a well (" + scene.GravityLine() + ")");
                snap.InGravity = false; snap.GravityName = null; snap.GravityG = 0;
                // local orbits: moons round Saturn, stations round their moons
                Check(MapScene.StationParent(Array.Find(SystemData.Stations, s => s.Name == "Andromeda Station").Pos)?.Name == "Venus", "orbits: Andromeda Station orbits Venus");
                Check(MapScene.StationParent(Array.Find(SystemData.Stations, s => s.Name == "Ganymede Station").Pos)?.Name == "Ganymede", "orbits: Ganymede Station orbits Ganymede");
                Check(MapScene.StationParent(Array.Find(SystemData.Stations, s => s.Name == "Tycho Station").Pos) == null, "orbits: Tycho Station is out in the Belt");
                var saturn = SystemData.FindWorld("Saturn").Pos;
                var keepT = scene.Cam.Target; double keepD = scene.Cam.Distance;
                scene.Cam.Target = scene.Cam.GoalTarget = saturn; scene.Cam.Distance = scene.Cam.GoalDistance = 3e6;
                g.Clear(Color.FromArgb(30, 34, 40));
                scene.Draw(new GdiMapCanvas(g), new Box(0, 0, 1920, 1080));
                bmp.Save(Path.Combine(outDir, "full-map-orbits-saturn.png"), ImageFormat.Png);
                var enc = SystemData.FindWorld("Enceladus").Pos;
                scene.Cam.Target = scene.Cam.GoalTarget = enc; scene.Cam.Distance = scene.Cam.GoalDistance = 60e3;
                g.Clear(Color.FromArgb(30, 34, 40));
                scene.Draw(new GdiMapCanvas(g), new Box(0, 0, 1920, 1080));
                bmp.Save(Path.Combine(outDir, "full-map-orbits-station.png"), ImageFormat.Png);
                scene.Cam.Target = scene.Cam.GoalTarget = keepT; scene.Cam.Distance = scene.Cam.GoalDistance = keepD;
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
            Check(scene.Picks.Exists(p => p.Kind == MapScene.FieldKind && p.Name == "Trojans") && scene.Picks.Exists(p => p.Kind == MapScene.FieldKind && p.Name == "Greeks"), "system" + sfx + ": the Trojans and Greeks fields are pickable");
            // the mouse on the Belt picks the spot on its middle line under it
            var spot = new V3(Math.Cos(2.0), 0, Math.Sin(2.0)) * SystemData.Belt.Major;
            scene.Cam.Project(spot, out var bx, out var by, out _);
            scene.MouseX = bx; scene.MouseY = by;
            scene.Draw(new MeasureCanvas(), new Box(0, 0, 1920, 1080));
            Check(scene.Hovered is Pick bp && bp.Kind == MapScene.BeltKind && V3.Distance(bp.Pos, spot) < SystemData.Belt.Minor, "system" + sfx + ": the Belt under the mouse is picked at that spot");
            scene.MouseX = float.NaN;
            var pc = new PropCanvas();
            scene.Draw(pc, new Box(0, 0, 1920, 1080));
            Check(pc.Texts.Exists(t => t.Text == MapScene.ShortName(route.Final.TargetName)), "system" + sfx + ": the destination's short name is drawn by its mark (" + MapScene.ShortName(route.Final.TargetName) + ")");
        }
        // a field close up: its rocks fill the view
        if (sfx == "" || sfx == "-opa" || sfx == "-protogen")
        using (var bmp = new Bitmap(1920, 1080))
        using (var g = Graphics.FromImage(bmp))
        {
            var scene = new MapScene { Snap = snap, Path = route, Help = "" };
            scene.Cam.Yaw = 0.9; scene.Cam.Pitch = 0.5;
            scene.Cam.Setup(0, 0, 1920, 1080);
            var tro = Array.Find(SystemData.Sectors, s => s.Name == "Trojans");
            scene.Cam.FlyTo(tro.Center, MapScene.FocusDistance(new Pick { Name = "Trojans", Kind = MapScene.FieldKind })); scene.Cam.Snap();
            scene.MouseX = float.NaN;
            scene.Draw(new GdiMapCanvas(g), new Box(0, 0, 1920, 1080));
            bmp.Save(Path.Combine(outDir, "field" + sfx + ".png"), ImageFormat.Png);
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
            var scene = new MapScene { Snap = snap, Path = route, Compact = true, Help = "VIEW|Right-drag orbit" + MapScene.Sep + "Wheel zoom" + MapScene.Sep + "Middle-click re-frame\nKEYS|Shift+M map, full map, off" + MapScene.Sep + "Ctrl+M route plan" + MapScene.Sep + "Alt+M pin on screen" };
            var box = SideBox(1920, 1080, scene, new GdiMapCanvas(g));
            scene.Cam.Yaw = 0.9; scene.Cam.Pitch = 0.62;
            scene.Cam.Setup(box.X, box.Y, box.W, box.H - scene.Strip);
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
            var scene = new MapScene { Snap = snap, Path = route, Compact = true, Help = "Shift+M map, full map, off · Ctrl+M route plan" };
            var box = SideBox(1920, 1080, scene, new GdiMapCanvas(g));
            scene.Cam.Yaw = 0.9; scene.Cam.Pitch = 0.62;
            scene.Cam.Setup(box.X, box.Y, box.W, box.H - scene.Strip);
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
            bmp.Save(Path.Combine(outDir, "autopilot" + sfx + ".png"), ImageFormat.Png);
            snap.Asteroids.Clear(); snap.Dodges.Clear();
        }
        Console.WriteLine("previews written to " + outDir);
    }

    /// <summary>Same layout rule as the plugin's SidePreview: right of centre, clear of F&amp;B's 600 px wide list.</summary>
    /// <summary>The side window as the plugin lays it out: the map box, grown by the info strip that hangs below it.</summary>
    private static Box SideBox(float w, float h, MapScene scene, IMapCanvas c) => SideLayout.Box(w, h, 1f, false, scene.MeasureStrip(c, SideLayout.Box(w, h, 1f)));
}

/// <summary>A canvas that draws nothing, with proportional digits ('1' narrow), remembering where text went.</summary>
internal sealed class PropCanvas : IMapCanvas
{
    public readonly List<(string Text, float X, float Y)> Texts = new List<(string, float, float)>();
    public void Rect(float x, float y, float w, float h, MapColor col, float rotation = 0) { }
    public void Line(float x1, float y1, float x2, float y2, float width, MapColor col) { }
    public void Disc(float cx, float cy, float r, MapColor col) { }
    public void Text(string text, float x, float y, float px, MapColor col, TextAnchor anchor = TextAnchor.Left) => Texts.Add((text, x, y));
    public float TextWidth(string text, float px) { float w = 0; foreach (char ch in text) w += (ch == '1' ? 0.3f : ch == '0' ? 0.6f : 0.55f) * px; return w; }
    public void PushClip(float x, float y, float w, float h) { }
    public void PopClip() { }
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
        for (int i = texts.Count - 1; i >= 0; i--) if (texts[i].Text == "ROUTE PLAN  to Far") { texts.RemoveRange(0, i); break; }
        foreach (var t in texts) if (t.Text == s && k++ == n) return (t.X, t.Y);
        return (-1000, -1000);
    }
}

/// <summary>A canvas that remembers its rects (for checking where backgrounds go).</summary>
internal sealed class RecordCanvas : IMapCanvas
{
    public readonly List<Box> Rects = new List<Box>();
    public void Rect(float x, float y, float w, float h, MapColor col, float rotation = 0) { Rects.Add(new Box(x, y, w, h)); Count++; }
    public int Count;
    public void Line(float x1, float y1, float x2, float y2, float width, MapColor col) => Count++;
    public void Disc(float cx, float cy, float r, MapColor col) => Count++;
    public void Text(string text, float x, float y, float px, MapColor col, TextAnchor anchor = TextAnchor.Left) => Count++;
    public float TextWidth(string text, float px) => text.Length * px * 0.5f;
    public void PushClip(float x, float y, float w, float h) { }
    public void PopClip() { }
}
