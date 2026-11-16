using System;
using System.Collections.Generic;

namespace SDX2_SystemMap;

/// <summary>Everything the map shows that comes from the game this frame (the plugin fills it; tests make one up).</summary>
public sealed class MapSnapshot
{
    public bool HasShip;
    public string ShipName = "YOU";
    public V3 ShipPos, ShipVel;
    public bool InCockpit;

    /// <summary>Flip and Burn: found and its API answered.</summary>
    public bool FnbReady;
    /// <summary>Flip and Burn's speed state for this grid ("HighSpeed", "Normal", ...), or "" when unknown.</summary>
    public string FnbState = "";
    /// <summary>The destination picked in F&amp;B's navigation list (null = none).</summary>
    public string FnbTargetName;
    public V3 FnbTarget;
    /// <summary>Max acceleration toward the target in m/s² (0 = unknown).</summary>
    public double MaxAccel;
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
}
