using System;
using System.Collections.Generic;

namespace SDX2_SystemMap;

/// <summary>
/// Keeps one route plan alive while you fly it. Picking a destination in F&amp;B's list plans detours once and freezes
/// them (so the waypoints you pick in the list don't move under you); picking one of our own detour entries keeps the
/// plan and marks that detour as the next one; reaching a detour moves on to the next. The legs are re-solved from
/// the ship's current position every update. Game-free so it can be tested.
///
/// Your own waypoints (route panel): while <see cref="Adding"/> is set, a pick in F&amp;B's list becomes a waypoint
/// flown before the destination instead of a new destination. They're planned like the destination (detours round
/// worlds on each leg, stopping short of stations and above worlds).
/// </summary>
public sealed class RouteTracker
{
    public RoutePath Path { get; private set; }
    /// <summary>Goes up whenever the waypoints change (publish them to F&amp;B's list and the autopilot again).</summary>
    public int Revision { get; private set; }
    public string TargetName { get; private set; }
    public V3 Target { get; private set; }
    public readonly List<Waypoint> Waypoints = new List<Waypoint>();
    public int Next { get; private set; }
    /// <summary>Your own waypoints, in flight order (name, the point picked).</summary>
    public readonly List<(string Name, V3 Pos)> UserPoints = new List<(string, V3)>();

    public bool Avoid = true;
    /// <summary>Keep out of the asteroid fields (the Belt, the Trojans, the Greeks) when the way can go round them.</summary>
    public bool AvoidFields;
    public double Clearance = RoutePlan.DefaultClearance;
    /// <summary>A detour counts as reached within this distance (less for short legs: <see cref="ReachOf"/>).</summary>
    public double ArriveRadius = 25e3;
    /// <summary>Share of the ship's acceleration the plan uses (autopilot thrust cap); 1 = all of it.</summary>
    public double AccelScale = 1;
    /// <summary>Share of the max acceleration used for braking (route panel's brake cap).</summary>
    public double BrakeScale = 1;
    /// <summary>Top speed for the plan, m/s; 0 = Flip and Burn's cap.</summary>
    public double SpeedCap;
    /// <summary>Keep the route when nothing is picked in F&amp;B's list (the route panel is open).</summary>
    public bool Hold;
    /// <summary>Picks in F&amp;B's list add waypoints instead of changing the destination.</summary>
    public bool Adding;
    /// <summary>Parabolic legs: curve through the points without stopping (false: flip and burn, slowing to turn at each).</summary>
    public bool Curved { get => curved; set { if (curved != value) { curved = value; Revision++; } } }
    private bool curved;
    /// <summary>Legs you bent (dragged their flip point): the point each curves through, by the leg's end name.</summary>
    public readonly Dictionary<string, V3> Bends = new Dictionary<string, V3>();
    /// <summary>Worlds (by name) whose detours you removed: the route isn't planned round them until the destination changes or R re-plans.</summary>
    public readonly HashSet<string> Skipped = new HashSet<string>();

    /// <summary>
    /// Removes the detour at Waypoints[index] (not one of your waypoints): every detour round that world goes, and stays
    /// gone when the route is planned again. Returns the world's name (or the detour's, for one taken back from the PB
    /// with no world on it); null when it isn't a detour.
    /// </summary>
    public string RemoveDetour(int index)
    {
        if (index < 0 || index >= Waypoints.Count || Waypoints[index].User) return null;
        var around = Waypoints[index].Around?.Name;
        if (around == null)
        {
            string n = Waypoints[index].Name;
            Waypoints.RemoveAt(index);
            if (Next > index) Next--;
            Revision++;
            return n;
        }
        Skipped.Add(around);
        if (hasShip) Replan(lastShip);
        else { Waypoints.RemoveAll(w => !w.User && w.Around?.Name == around); Next = Math.Min(Next, Waypoints.Count); Revision++; }
        return around;
    }

    /// <summary>Bends the leg ending at <paramref name="end"/> through <paramref name="at"/> (null: straight again).</summary>
    public void Bend(string end, V3? at)
    {
        if (end == null) return;
        if (at.HasValue) Bends[end] = at.Value;
        else if (!Bends.Remove(end)) return;
        Revision++;
    }

    private string lastSel;
    private V3 lastSelPos, lastShip, planFrom;
    private bool hasShip;

    public void Clear()
    {
        lastSel = null;
        if (TargetName == null && Waypoints.Count == 0 && UserPoints.Count == 0) { Path = null; return; }
        TargetName = null; Waypoints.Clear(); UserPoints.Clear(); Bends.Clear(); Skipped.Clear(); Next = 0; Path = null; Revision++;
    }

    /// <summary>
    /// Drops the plan (left the seat, respawned) and ignores F&amp;B's current pick (selectedName / selectedPos) until it
    /// changes, so the same destination isn't planned again straight away. Picking a point again plans a new route.
    /// </summary>
    public void Cancel(string selectedName, V3 selectedPos)
    {
        Clear();
        lastSel = selectedName; lastSelPos = selectedPos;
    }

    /// <summary>
    /// Takes the route back from what the autopilot PB holds (after a world load or a Nexus sector change, when the
    /// plugin has forgotten it but the PB still flies it): the destination, your waypoints and the detours as sent,
    /// with <paramref name="reached"/> (the PB's point index) marking those already flown. F&amp;B's current pick is
    /// ignored until it changes. False when the PB holds no route.
    /// </summary>
    public bool Restore(RouteExport.Parsed r, int reached, V3 ship, string selectedName, V3 selectedPos)
    {
        if (r == null || string.IsNullOrEmpty(r.Target) || r.Command == "stop") return false;
        Clear();
        TargetName = r.Target; Target = r.Dest; planFrom = ship;
        int next = 0;
        for (int i = 0; i < r.Points.Count; i++)
        {
            var pt = r.Points[i];
            bool user = pt.Kind == "waypoint";
            if (!user && (pt.Kind != "detour" || pt.Name.StartsWith("Approach "))) continue; // passes, curve points, the stop
            if (user) UserPoints.Add((pt.Name, pt.Pos));
            Waypoints.Add(new Waypoint { Name = pt.Name, Pos = pt.Pos, Pick = pt.Pos, User = user });
            if (i < reached) next = Waypoints.Count;
        }
        Next = next;
        lastSel = selectedName; lastSelPos = selectedPos;
        // a parabolic route goes to the PB as one curve (its detours only as curve points): plan the detours again
        // from here, so the map shows the way round worlds and Sol's no-fly instead of a line through them
        bool curve = r.Points.Exists(pt => pt.Kind == "curve");
        if (curve && Avoid && !Waypoints.Exists(w => !w.User)) { lastShip = ship; Replan(ship); }
        Revision++;
        return true;
    }

    /// <summary>A copy of the plan (destination, points, your waypoints, bends, progress), for <see cref="Load"/>.</summary>
    public sealed class State
    {
        internal string TargetName;
        internal V3 Target;
        internal int Next;
        internal readonly List<Waypoint> Waypoints = new List<Waypoint>();
        internal readonly List<(string Name, V3 Pos)> UserPoints = new List<(string, V3)>();
        internal readonly Dictionary<string, V3> Bends = new Dictionary<string, V3>();
        internal readonly HashSet<string> Skipped = new HashSet<string>();
    }

    public State Save()
    {
        var st = new State { TargetName = TargetName, Target = Target, Next = Next };
        st.Waypoints.AddRange(Waypoints); st.UserPoints.AddRange(UserPoints);
        foreach (var kv in Bends) st.Bends[kv.Key] = kv.Value;
        st.Skipped.UnionWith(Skipped);
        return st;
    }

    /// <summary>Back to a saved plan (edits made since are dropped). F&amp;B's current pick stays ignored until it changes.</summary>
    public void Load(State st)
    {
        if (st == null) return;
        TargetName = st.TargetName; Target = st.Target;
        Waypoints.Clear(); Waypoints.AddRange(st.Waypoints);
        UserPoints.Clear(); UserPoints.AddRange(st.UserPoints);
        Bends.Clear(); foreach (var kv in st.Bends) Bends[kv.Key] = kv.Value;
        Skipped.Clear(); Skipped.UnionWith(st.Skipped);
        Next = Math.Min(st.Next, Waypoints.Count);
        if (TargetName == null) Path = null;
        Revision++;
    }

    /// <summary>The index of our point with this name and position (as picked in F&amp;B's list), or -1.</summary>
    public int IndexOf(string name, V3 pos)
    {
        for (int i = 0; i < Waypoints.Count; i++)
            if (Waypoints[i].Name == name && (V3.Distance(Waypoints[i].Pos, pos) < 100 || Waypoints[i].User && V3.Distance(Waypoints[i].Pick, pos) < 100)) return i;
        return -1;
    }

    /// <summary>Adds one of your own waypoints before the destination and plans the route again.</summary>
    public bool AddUser(string name, V3 pos)
    {
        if (TargetName == null || UserPoints.Count >= 8 || V3.Distance(pos, Target) < 100) return false;
        foreach (var u in UserPoints) if (V3.Distance(u.Pos, pos) < 100) return false;
        UserPoints.Add((name, pos));
        if (hasShip) Replan(lastShip);
        return true;
    }

    /// <summary>
    /// Adds one of your own waypoints where it fits best in the route: in the slot (ship → your waypoints → destination)
    /// that adds the least distance, so a point clicked on a leg lands in that leg. Plans the route again.
    /// </summary>
    public bool InsertUser(string name, V3 pos)
    {
        if (TargetName == null || UserPoints.Count >= 8 || V3.Distance(pos, Target) < 100) return false;
        foreach (var u in UserPoints) if (V3.Distance(u.Pos, pos) < 100) return false;
        var chain = new List<V3> { lastShip };
        foreach (var u in UserPoints) chain.Add(u.Pos);
        chain.Add(Target);
        UserPoints.Insert(RouteClick.InsertIndex(chain, pos), (name, pos));
        if (hasShip) Replan(lastShip); else Revision++;
        return true;
    }

    /// <summary>"Waypoint n" with the first n not in use.</summary>
    public string NextUserName()
    {
        for (int n = 1; ; n++)
        {
            string name = "Waypoint " + n; bool used = false;
            foreach (var u in UserPoints) if (u.Name == name) used = true;
            if (!used) return name;
        }
    }

    /// <summary>Moves one of your waypoints (keeping its place in the route) and plans the route again. False if it would sit on another point.</summary>
    public bool MoveUser(int index, V3 pos)
    {
        if (index < 0 || index >= UserPoints.Count || V3.Distance(pos, Target) < 100) return false;
        for (int i = 0; i < UserPoints.Count; i++) if (i != index && V3.Distance(UserPoints[i].Pos, pos) < 100) return false;
        UserPoints[index] = (UserPoints[index].Name, pos);
        if (hasShip) Replan(lastShip); else Revision++;
        return true;
    }

    public void RemoveUser(int index)
    {
        if (index < 0 || index >= UserPoints.Count) return;
        UserPoints.RemoveAt(index);
        if (hasShip) Replan(lastShip); else Revision++;
    }

    /// <summary>Plans the route again from where the ship is now: detours on each leg, through your own waypoints.</summary>
    /// <summary>Grids and stations to keep out of (set by the plugin; used when the route is planned).</summary>
    public IList<SystemData.World> Obstacles;

    public void Replan(V3 ship)
    {
        if (TargetName == null) return;
        Waypoints.Clear();
        planFrom = ship;
        var from = ship;
        foreach (var u in UserPoints)
        {
            var det = Avoid ? DetourPlanner.Plan(from, u.Pos, u.Name, Clearance, Obstacles, Skipped, AvoidFields) : new List<Waypoint>();
            Waypoints.AddRange(det);
            var last = det.Count > 0 ? det[det.Count - 1].Pos : from;
            var stop = RouteExport.StopNear(last, u.Pos, Clearance, out _);
            Waypoints.Add(new Waypoint { Name = u.Name, Pos = stop, Pick = u.Pos, User = true });
            from = stop;
        }
        if (Avoid) Waypoints.AddRange(DetourPlanner.Plan(from, Target, TargetName, Clearance, Obstacles, Skipped, AvoidFields));
        // number the detours across the whole route
        int total = 0, k = 0;
        foreach (var w in Waypoints) if (!w.User) total++;
        for (int i = 0; i < Waypoints.Count; i++)
        {
            if (Waypoints[i].User) continue;
            var w = Waypoints[i]; w.Name = "Detour " + (++k) + " of " + total + " - " + MapScene.ShortName(TargetName); Waypoints[i] = w;
        }
        Next = 0;
        Revision++;
    }

    /// <summary>
    /// Call often. selectedName / selectedPos = F&amp;B's current pick (null = none).
    /// </summary>
    public void Update(string selectedName, V3 selectedPos, bool hasShip, V3 ship, V3 velocity, double accel, double maxSpeed)
    {
        this.hasShip = hasShip;
        if (!hasShip) { Path = null; return; }
        lastShip = ship;
        bool changed = selectedName != lastSel || selectedName != null && V3.Distance(selectedPos, lastSelPos) > 100;
        lastSel = selectedName; lastSelPos = selectedPos;

        // no pick in Flip and Burn: the route stays until it's cleared (Clear / Cancel) or flown
        if (selectedName == null) { }
        else if (changed)
        {
            int ours = IndexOf(selectedName, selectedPos);
            bool isTarget = selectedName == TargetName && V3.Distance(selectedPos, Target) <= 100;
            if (ours >= 0) { if (!Hold && ours > Next) Next = ours; }  // flying a detour leg: keep the plan
            else if (isTarget) { }
            else if (Adding && TargetName != null) AddUser(selectedName, selectedPos);
            else
            {
                TargetName = selectedName; Target = selectedPos;
                UserPoints.Clear(); Bends.Clear(); Skipped.Clear();
                Replan(ship);
            }
        }
        if (TargetName == null) { Path = null; return; }

        while (Next < Waypoints.Count && (V3.Distance(ship, Waypoints[Next].Pos) < ReachOf(Next) || Curved && GoneBy(Next, ship, velocity))) Next++;
        double cap = SpeedCap > 0 && (maxSpeed <= 0 || SpeedCap < maxSpeed) ? SpeedCap : maxSpeed;
        Path = RoutePath.Make(TargetName, ship, Target, velocity, accel * AccelScale, cap, Waypoints, Next, Clearance, accel * BrakeScale, Curved, Bends);
    }

    /// <summary>
    /// The same route planned at other thrust and brake shares and speed cap (what the autopilot would fly with the
    /// route's limits), from the ship's last position: for the water estimate. Null with no route.
    /// </summary>
    public RoutePath PathWith(double accelScale, double brakeScale, double speedCap, V3 velocity, double accel, double maxSpeed)
    {
        if (TargetName == null || !hasShip) return null;
        double cap = speedCap > 0 && (maxSpeed <= 0 || speedCap < maxSpeed) ? speedCap : maxSpeed;
        return RoutePath.Make(TargetName, lastShip, Target, velocity, accel * accelScale, cap, Waypoints, Next, Clearance, accel * brakeScale, Curved, Bends);
    }

    /// <summary>
    /// The distance within which waypoint i counts as reached: <see cref="ArriveRadius"/>, but at most a quarter of the
    /// shorter leg either side, so the short detours out of a station or base you start at aren't passed where you are.
    /// </summary>
    public double ReachOf(int i)
    {
        var prev = i > 0 ? Waypoints[i - 1].Pos : planFrom;
        var next = i + 1 < Waypoints.Count ? Waypoints[i + 1].Pos : Target;
        var at = Waypoints[i].Pos;
        return Math.Min(ArriveRadius, 0.25 * Math.Min(V3.Distance(prev, at), V3.Distance(at, next)));
    }

    /// <summary>
    /// The autopilot says it has flown past the first <paramref name="next"/> waypoints (it flies toward that one now):
    /// the plan moves on with it, so a later update doesn't send the ship back to points it went by without coming
    /// near enough to count them here. Never goes back.
    /// </summary>
    public bool PassedTo(int next)
    {
        if (next <= Next || next > Waypoints.Count) return false;
        Next = next;
        return true;
    }

    /// <summary>Parabolic legs fly through a point at speed: it's passed once the ship is beyond it, heading on.</summary>
    private bool GoneBy(int i, V3 ship, V3 velocity)
    {
        var at = Waypoints[i].Pos;
        var next = i + 1 < Waypoints.Count ? Waypoints[i + 1].Pos : Target;
        var outDir = next - at;
        return velocity.LengthSq > 1 && (ship - at).Dot(outDir) > 0 && velocity.Dot(outDir) > 0
            && V3.Distance(ship, at) < 0.5 * V3.Distance(at, next);
    }

    /// <summary>What to pick in F&amp;B's list next: the next detour or waypoint, or the destination.</summary>
    public string NextPick => Next < Waypoints.Count ? Waypoints[Next].Name : TargetName;
}
