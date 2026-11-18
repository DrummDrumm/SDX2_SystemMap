using System;
using System.Collections.Generic;

namespace SDX2_SystemMap;

/// <summary>
/// SDX2's fixed geography: the Nexus sectors (each a separate server), the planets, moons and named asteroids at their
/// true positions and radii, and the trade stations. Sector centres/radii are the wiki NavMarkers list; worlds are the
/// community map (maxpowa.github.io/sdx-map, 2026-11-10); stations are the server's station GPS list (2026-11-10).
/// </summary>
public static class SystemData
{
    public enum SectorShape { Sphere, Torus, Outer }

    public sealed class Sector
    {
        public string Dx, Name; public V3 Center; public double Radius, HalfWidth; public SectorShape Shape; public MapColor Color;
    }

    public sealed class World
    {
        public string Name, Parent; public V3 Pos; public double Radius; public MapColor Color;
        /// <summary>
        /// Where its gravity well ends (metres from the centre, the game's own limit, set by the plugin from the loaded
        /// planet); 0 = not known. A route to the world itself stops outside it (<see cref="RouteExport.StopNear"/>).
        /// </summary>
        public double Well;
        /// <summary>
        /// The game's gravity numbers for the loaded planet (set by the plugin; 0 = not known): its surface gravity in
        /// m/s² (the same from the ground up to <see cref="Hills"/>), how fast it falls off past that
        /// (g = surface × (r / Hills)^-falloff), and the radius its highest hills reach (the gravity's full-strength radius).
        /// </summary>
        public double SurfaceG, Falloff, Hills;
        /// <summary>Where its atmosphere ends (metres from the centre, set by the plugin); 0 = none, or not known. Routes keep above it.</summary>
        public double Atmosphere;
        public bool IsPlanet => Parent == "";
        public bool IsAsteroid => Parent == "Belt";
        /// <summary>Not a world: a grid or station the route keeps out of (Radius already has the margin; no clearance on top).</summary>
        public bool IsObstacle => Parent == ObstacleParent || Parent == StationParent;
        /// <summary>A known station (kept apart from your grids when nearby spheres are merged).</summary>
        public bool IsStation => Parent == StationParent;
        /// <summary>An asteroid field a route may keep out of (the Trojans, the Greeks, the Belt): not a world, no clearance or ground.</summary>
        public bool IsField => Parent == FieldParent;
        /// <summary>The star. Routes don't use its own clearance: it's the fixed no-fly sphere <see cref="SolZone"/>.</summary>
        public bool IsStar => Name == "Sol" && Parent == "";
    }

    public sealed class Station
    {
        public string Gps, Name; public V3 Pos;
        /// <summary>
        /// A ground facility: the built-in approach stops this far straight above it (away from its world's centre),
        /// instead of the usual <see cref="RouteExport.StationStandoff"/>. 0 = an ordinary station.
        /// </summary>
        public double Above;
    }

    private static Sector S(string dx, string name, double x, double y, double z, double r, int rgb, SectorShape shape = SectorShape.Sphere, double half = 0) =>
        new Sector { Dx = dx, Name = name, Center = new V3(x, y, z), Radius = r, HalfWidth = half, Shape = shape, Color = MapColor.Rgb(rgb) };

    /// <summary>
    /// The Belt's asteroid torus as the Skylark Belt plugin draws it in game (its SkylarkBelt.cfg): centred on Sol, the
    /// tube's centreline 8,500 km out, the tube 500 km in radius, its axis straight up (flat to the ecliptic). The same
    /// torus as DX6's sector below.
    /// </summary>
    public static readonly BeltTorus Belt = new BeltTorus { Center = V3.Zero, Normal = new V3(0, 1, 0), Major = 8500e3, Minor = 500e3 };

    public sealed class BeltTorus
    {
        public V3 Center, Normal;
        /// <summary>Radius of the tube's centreline, and of the tube itself (m).</summary>
        public double Major, Minor;
    }

    /// <summary>Order matters for SectorAt: spheres first, then the Belt torus, then Sol (everything else).</summary>
    public static readonly Sector[] Sectors = {
        S("DX3", "Mercury", -835637.09, 208377.81, 835637.09, 250e3, 0xA9A9A9),
        S("DX3", "Venus", 2228365.57, -555674.17, -2228365.57, 500e3, 0xE6C88A),
        S("DX4", "Earth", -2490486.75, 435778.71, -4313649.58, 750e3, 0x4682B4),
        S("DX2", "Mars", -3486681.44, -610090.2, 6039109.41, 750e3, 0xCD5C5C),
        S("DX5", "Ceres", 9447958.01, 993020.4, 0, 750e3, 0xFFDAB9),
        S("DX7", "Jupiter", -13594616.81, 0, -6339273.93, 2500e3, 0xDEB887),
        S("DX10", "Trojans", -6339273.93, 0, -13594616.81, 1500e3, 0x00FF7F),
        S("DX11", "Greeks", -14488887.39, 0, 3882285.68, 1500e3, 0x00FF7F),
        S("DX8", "Saturn", 11471528.73, 0, 16383040.89, 2500e3, 0xF4A460),
        S("DX9", "Uranus", 17677669.53, 0, -17677669.53, 1500e3, 0xAFEEEE),
        S("DX9", "Neptune", -20075175.27, 0, 28670321.55, 1500e3, 0x4169E1),
        S("DX3", "Pluto", -27701614.66, 12940952.26, -39562005.76, 300e3, 0xD3D3D3),
        S("DX6", "The Belt", 0, 0, 0, 8500e3, 0x00FFCC, SectorShape.Torus, 500e3),
        S("DX1", "Sol", 0, 0, 0, 0, 0xFFD700, SectorShape.Outer),
    };

    public static Sector SectorAt(V3 p)
    {
        foreach (var s in Sectors)
        {
            if (s.Shape == SectorShape.Sphere && V3.Distance(p, s.Center) <= s.Radius) return s;
            if (s.Shape == SectorShape.Torus)
            {
                double rxz = Math.Sqrt(p.X * p.X + p.Z * p.Z), dr = rxz - s.Radius;
                if (dr * dr + p.Y * p.Y <= s.HalfWidth * s.HalfWidth) return s;
            }
        }
        return Sectors[Sectors.Length - 1];
    }

    private static World W(string n, string parent, double x, double y, double z, double r, int rgb = 0x9AA4AE) =>
        new World { Name = n, Parent = parent, Pos = new V3(x, y, z), Radius = r, Color = MapColor.Rgb(rgb) };

    public const string ObstacleParent = "(obstacle)", StationParent = "(station)", FieldParent = "(field)";

    /// <summary>The Trojans and the Greeks as spheres (their sectors), for routes that keep out of the asteroid fields.</summary>
    public static World[] Fields => fields ?? (fields = Array.ConvertAll(Array.FindAll(Sectors, s => s.Name == "Trojans" || s.Name == "Greeks"),
        s => new World { Name = s.Name, Parent = FieldParent, Pos = s.Center, Radius = s.Radius }));
    private static World[] fields;

    /// <summary>The Belt's torus, as what its detours go round (its Pos is Sol's centre, Radius the tube's).</summary>
    public static World BeltField => beltField ?? (beltField = new World { Name = "The Belt", Parent = FieldParent, Pos = Belt.Center, Radius = Belt.Minor });
    private static World beltField;

    /// <summary>How far p is from the Belt tube's centreline (under <see cref="BeltTorus.Minor"/>: in the Belt).</summary>
    public static double BeltDistance(V3 p)
    {
        var rel = p - Belt.Center; var n = Belt.Normal.Normalized;
        double y = rel.Dot(n), rho = (rel - n * y).Length - Belt.Major;
        return Math.Sqrt(rho * rho + y * y);
    }

    /// <summary>
    /// SDX2's base cores (SDX2: Core, Data/Cores/StationCores): sdx_shipcoreOutpost and sdx_shipcoreInstallation. A grid
    /// set to station with one of these is a base.
    /// </summary>
    public static bool IsBaseCore(string subtype) =>
        subtype != null && (subtype.IndexOf("shipcoreOutpost", StringComparison.OrdinalIgnoreCase) >= 0 || subtype.IndexOf("shipcoreInstallation", StringComparison.OrdinalIgnoreCase) >= 0);

    /// <summary>Routes keep this far from Sol's centre (a fixed no-fly sphere, whatever the clearance).</summary>
    public const double SolNoFly = 800e3;

    /// <summary>Sol's no-fly sphere, gone round like a station: left straight outward first when you start inside it.</summary>
    public static World SolZone => solZone ?? (solZone = Obstacle("Sol", Worlds[0].Pos, SolNoFly));
    private static World solZone;

    /// <summary>What routes go round: the worlds, with Sol as its fixed no-fly sphere.</summary>
    public static World[] RouteWorlds => routeWorlds ?? (routeWorlds = BuildRouteWorlds());
    private static World[] routeWorlds;

    private static World[] BuildRouteWorlds()
    {
        var r = (World[])Worlds.Clone();
        for (int i = 0; i < r.Length; i++) if (r[i].IsStar) r[i] = SolZone;
        return r;
    }

    /// <summary>A grid or station for the route to keep out of: a sphere of radius r round pos.</summary>
    public static World Obstacle(string name, V3 pos, double r, bool station = false) =>
        new World { Name = name, Parent = station ? StationParent : ObstacleParent, Pos = pos, Radius = r };

    public static readonly World[] Worlds = {
        W("Sol", "", -19187, -19187, -19187, 9500, 0xFFD24A),
        W("Mercury", "", -835636.5, 208377.5, 835637.5, 30000, 0xA9A9A9),
        W("Venus", "", 2228365.5, -555673.5, -2228364.5, 90000, 0xE6C88A),
        W("Earth", "", -2490485.5, 435778.5, -4313648.5, 100000, 0x4682B4),
        W("Luna", "Earth", -2755392.5, 333172.5, -4410066.5, 27000),
        W("Mars", "", -3486680.5, -610089.5, 6039109.5, 60000, 0xCD5C5C),
        W("Phobos", "Mars", -3512331.5, -584041.5, 5893632.5, 4000),
        W("Deimos", "Mars", -3291698.5, -658710.5, 6234091.5, 3000),
        W("Ceres", "", 9447958.5, 993020.5, 0.5, 15000, 0xFFDAB9),
        W("Vesta", "Belt", -8183475.5, -152009.5, -2978540.5, 6000, 0x7FD8C4),
        W("Pallas", "Belt", 6035013.5, -298040.5, 6035013.5, 5000, 0x7FD8C4),
        W("Hygeia", "Belt", 5875162.5, 145029.5, -5875161.5, 4000, 0x7FD8C4),
        W("Eros", "Belt", 0.5, 300136.5, 8594761.5, 1000, 0x7FD8C4),
        W("Jupiter", "", -13594615.5, 0.5, -6339272.5, 400000, 0xDEB887),
        W("Io", "Jupiter", -12794902.5, -85412.5, -6899239.5, 24000),
        W("Europa", "Jupiter", -13689031.5, 191013.5, -7418439.5, 28000),
        W("Ganymede", "Jupiter", -12746088.5, 0.5, -5490745.5, 40000),
        W("Callisto", "Jupiter", -14933424.5, -260471.5, -5714976.5, 35000),
        W("Saturn", "", 11471528.5, 0.5, 16383040.5, 300000, 0xF4A460),
        W("Mimas", "Saturn", 10828007.5, -327889.5, 16446228.5, 4000),
        W("Enceladus", "Saturn", 11092926.5, -222870.5, 17009310.5, 5000),
        W("Tethys", "Saturn", 12173318.5, 389502.5, 16524477.5, 9000),
        W("Dione", "Saturn", 11940653.5, 155397.5, 15685331.5, 9000),
        W("Rhea", "Saturn", 11435324.5, 71055.5, 17294559.5, 12000),
        W("Titan", "Saturn", 10717711.5, -341785.5, 15689253.5, 40000),
        W("Iapetus", "Saturn", 11908753.5, -150487.5, 15184119.5, 12000),
        W("Phoebe", "Saturn", 11030881.5, 864819.5, 17539768.5, 3000),
        W("Uranus", "", 17677669.5, 0.5, -17677668.5, 200000, 0xAFEEEE),
        W("Miranda", "Uranus", 17555810.5, 318074.5, -17399138.5, 4000),
        W("Ariel", "Uranus", 17700536.5, -488349.5, -17500494.5, 9000),
        W("Umbriel", "Uranus", 17716253.5, 113728.5, -17069411.5, 9000),
        W("Titania", "Uranus", 17709405.5, -814474.5, -18145880.5, 12000),
        W("Oberon", "Uranus", 17662446.5, 587391.5, -18167529.5, 12000),
        W("Neptune", "", -20075174.5, 0.5, 28670321.5, 200000, 0x4169E1),
        W("Triton", "Neptune", -20589404.5, 612835.5, 28670321.5, 21000),
        W("Pluto", "", -27701613.5, 12940952.5, -39562004.5, 30000, 0xD3D3D3),
        W("Charon", "Pluto", -27846502.5, 12902129.5, -39562004.5, 15000),
    };

    private static Station Ground(Station s, double above) { s.Above = above; return s; }

    private static Station St(string gps, string name, double x, double y, double z) => new Station { Gps = gps, Name = name, Pos = new V3(x, y, z) };

    public static readonly Station[] Stations = {
        Ground(St("Belt.2Pallas.STN", "2 Pallas Station", 0, 0, 0), 8000), // on Pallas's ground
        St("Belt.Eros.STN", "Eros Station", 0, 0, 0),
        Ground(St("Belt.Hygeia.STN", "Hygeia Station", 0, 0, 0), 8000), // in Hygeia (2 km under its listed radius): treated as on its ground for now
        St("Belt.Tycho.STN", "Tycho Station", 0, 0, 0),
        St("Belt.Vesta.STN", "Vesta Station", 0, 0, 0),
        St("Ve.Andromeda.STN", "Andromeda Station", 0, 0, 0),
        Ground(St("JU.Ganymede.STN", "Ganymede Station", 0, 0, 0), 8000), // on Ganymede's ground
        Ground(St("JU.IoHeliumRef.STN", "Io Helium Refinery", 0, 0, 0), 8000), // on Io's ground
        Ground(St("SA.Enceladus.STN", "Enceladus Station", 0, 0, 0), 8000), // on Enceladus's ground
        St("SA.Iapetus.STN", "Iapetus Station", 0, 0, 0),
        St("SA.Mimas.STN", "Mimas Station", 0, 0, 0),
        Ground(St("SA.Rhea.STN", "Rhea Station", 0, 0, 0), 8000), // on Rhea's ground
        Ground(St("NE.TriDALab.STN", "TriDA Lab", 0, 0, 0), 8000), // on Triton's ground
        St("Greek.Achilles.STN", "Achilles Station", 0, 0, 0),
        St("Tro.Paris.STN", "Paris Station", 0, 0, 0),
        St("CE.Aventine.STN", "Aventine Station", 0, 0, 0),
    };

    public static World FindWorld(string name)
    {
        foreach (var w in Worlds) if (string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase)) return w;
        return null;
    }

    /// <summary>The station nearest to p within maxDist, or null.</summary>
    public static Station StationNear(V3 p, double maxDist)
    {
        Station best = null; double bd = maxDist;
        foreach (var s in Stations) { double d = V3.Distance(s.Pos, p); if (d <= bd) { bd = d; best = s; } }
        return best;
    }

    /// <summary>Distinct orbit radii (in the XZ plane) of the planets, for drawing orbits.</summary>
    public static IEnumerable<double> OrbitRadii()
    {
        var seen = new HashSet<long>();
        foreach (var w in Worlds)
        {
            if (!w.IsPlanet || w.Name == "Sol") continue;
            double r = w.Pos.Flat.Length;
            if (seen.Add((long)(r / 1e5))) yield return r;
        }
    }
}
