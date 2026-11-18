using System;
using System.Collections.Generic;

namespace SDX2_SystemMap;

/// <summary>Everything the map shows that comes from the game this frame (the plugin fills it; tests make one up).</summary>
public sealed class MapSnapshot
{
    public bool HasShip;
    /// <summary>
    /// Gravity (set by the plugin once a second): InGravity = the ship is in a planet's or moon's well (GravityG, in g;
    /// GravityName = whose); GravityEntry = where the route ahead first goes into a well (null = it doesn't), GravityAlong
    /// metres along the route from the ship, GravityEntryName = whose.
    /// </summary>
    public bool InGravity;
    public double GravityG, GravityAlong;
    public string GravityName, GravityEntryName;
    public V3? GravityEntry;
    public string ShipName = "YOU";
    public V3 ShipPos, ShipVel;
    /// <summary>The way your cockpit (or you) face.</summary>
    public V3 ShipFwd;
    public bool InCockpit;
    /// <summary>Your faction's tag ("" = none) and the ship's SDX2 core class (Skiff with no core; "" = not a grid): the map's short name.</summary>
    public string FactionTag = "", ShipClass = "";

    /// <summary>Longest ship name the map labels; a longer one is shown as faction tag and core class.</summary>
    public const int MaxShipName = 20;
    /// <summary>On SDX2 a ship without a core is classed (and called) a Skiff.</summary>
    public const string Skiff = "Skiff";

    /// <summary>The ship's name for the map's label: as it is up to <see cref="MaxShipName"/> characters, else the faction
    /// tag and core class ("OPA Hauler", "OPA Skiff"); with no class (not a ship), the name cut short with "...".</summary>
    public static string ShortShipName(string name, string tag, string cls, int max = MaxShipName)
    {
        name = (name ?? "").Trim();
        if (name.Length <= max) return name;
        string s = ((tag ?? "").Trim() + " " + (cls ?? "").Trim()).Trim();
        if (s.Length > 0 && (cls ?? "").Trim().Length > 0) return s.Length <= max ? s : s.Substring(0, max - 3).TrimEnd() + "...";
        return name.Substring(0, max - 3).TrimEnd() + "...";
    }

    /// <summary>The class an SDX2 core block gives ("sdx_shipcoreHauler" → "Hauler", "…StationAdmin" → "Station Admin");
    /// the NoCore block is a Skiff; "" for a block that isn't a core.</summary>
    public static string CoreClass(string subtype)
    {
        const string p = "sdx_shipcore";
        if (subtype == null || !subtype.StartsWith(p, StringComparison.OrdinalIgnoreCase) || subtype.Length == p.Length) return "";
        var raw = subtype.Substring(p.Length);
        if (raw.Equals("NoCore", StringComparison.OrdinalIgnoreCase)) return Skiff;
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < raw.Length; i++) { if (i > 0 && char.IsUpper(raw[i]) && char.IsLower(raw[i - 1])) sb.Append(' '); sb.Append(raw[i]); }
        return sb.ToString();
    }
    /// <summary>Your character's entity id (0 = none) and whether it's dead: a change means you respawned.</summary>
    public long CharacterId;
    public bool Dead;

    /// <summary>Flip and Burn: found and its API answered.</summary>
    public bool FnbReady;
    /// <summary>Flip and Burn's speed state for this grid ("HighSpeed", "Normal", ...), or "" when unknown.</summary>
    public string FnbState = "";
    /// <summary>The destination picked in F&amp;B's navigation list (null = none).</summary>
    public string FnbTargetName;
    public V3 FnbTarget;
    /// <summary>Max acceleration toward the target in m/s² (0 = unknown).</summary>
    public double MaxAccel;
    /// <summary>Water in the tanks and their capacity (L), and the fusion drives' water use at full thrust (L/s); 0 = unknown.</summary>
    public double Water, WaterCap, WaterRate;
    /// <summary>The route's water (L) at its thrust and brake caps, and the thrust share it was worked out at; NaN = not worked out (the map works it from the route as drawn).</summary>
    public double WaterUse = double.NaN, WaterThrust = double.NaN;
    public double MaxSpeed = RoutePlan.DefaultMaxSpeed;
    /// <summary>Summed signal weight of the ship's fusion drives (Spectrum signature), 0 = none.</summary>
    public double DriveWeight;
    /// <summary>The ship's Spectrum signature parts (drives, other emitters, Spectrum's own figures).</summary>
    public readonly SignatureInfo Signature = new SignatureInfo();

    public readonly List<Marker> Gps = new List<Marker>();
    /// <summary>Asteroids found on the route by Flip and Burn's scanner, and the fly-by points the autopilot uses round them.</summary>
    public readonly List<Marker> Asteroids = new List<Marker>();
    public readonly List<V3> Dodges = new List<V3>();
    /// <summary>Where the ship has actually flown since the route was set (oldest first): drawn dashed against the plan.</summary>
    public readonly List<V3> Track = new List<V3>();

    /// <summary>Where the ship is projected to go from here as it corrects onto the route, to the destination (<see cref="Trajectory"/>).</summary>
    public readonly List<V3> Prediction = new List<V3>();
    public bool PredictionArrives;
    /// <summary>Per <see cref="Prediction"/> point: burn / coast / brake from there on (<see cref="Trajectory"/>'s phases).</summary>
    public readonly List<byte> PredictionPhases = new List<byte>();

    private double trackTime;

    /// <summary>Records the ship's position (about every half second once it has moved 1/400 of the way left, min 100 m).
    /// Keeps at most 3000 points by thinning the older half.</summary>
    public void RecordTrack(V3 pos, double now, double remaining)
    {
        if (Track.Count > 0)
        {
            double step = Math.Max(100, remaining / 400);
            if (now - trackTime < 0.5 || V3.Distance(Track[Track.Count - 1], pos) < step) return;
        }
        trackTime = now;
        Track.Add(pos);
        if (Track.Count > 3000)
            for (int i = Track.Count / 2; i >= 1; i -= 2) Track.RemoveAt(i); // thin the older half (keep the start)
    }

    public struct Marker
    {
        public string Name; public V3 Pos; public MapColor Color;
    }
}

/// <summary>What can be shown or hidden (keys in the full-screen map, saved in the settings).</summary>
public sealed class MapOptions
{
    public bool Grid = true, Orbits = true, Sectors = true, Stations = true, Gps = true, Labels = true, Moons = true;
}

/// <summary>Something on screen the mouse can point at.</summary>
public struct Pick
{
    public string Name, Kind, Detail; public V3 Pos; public float X, Y, R;
    /// <summary>For a flip point or a bend: the leg's end name (which leg it belongs to).</summary>
    public string Leg;
}
