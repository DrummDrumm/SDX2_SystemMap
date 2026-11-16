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
    public double Clearance = RoutePlan.DefaultClearance;
    /// <summary>A detour counts as reached within this distance.</summary>
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

    private string lastSel;
    private V3 lastSelPos, lastShip;
    private bool hasShip;

    public void Clear()
    {
        lastSel = null;
        if (TargetName == null && Waypoints.Count == 0 && UserPoints.Count == 0) { Path = null; return; }
        TargetName = null; Waypoints.Clear(); UserPoints.Clear(); Next = 0; Path = null; Revision++;
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

    public void RemoveUser(int index)
    {
        if (index < 0 || index >= UserPoints.Count) return;
        UserPoints.RemoveAt(index);
        if (hasShip) Replan(lastShip); else Revision++;
    }

    /// <summary>Plans the route again from where the ship is now: detours on each leg, through your own waypoints.</summary>
    public void Replan(V3 ship)
    {
        if (TargetName == null) return;
        Waypoints.Clear();
        var from = ship;
        foreach (var u in UserPoints)
        {
            var det = Avoid ? DetourPlanner.Plan(from, u.Pos, u.Name, Clearance) : new List<Waypoint>();
            Waypoints.AddRange(det);
            var last = det.Count > 0 ? det[det.Count - 1].Pos : from;
            var stop = RouteExport.StopNear(last, u.Pos, Clearance, out _);
            Waypoints.Add(new Waypoint { Name = u.Name, Pos = stop, Pick = u.Pos, User = true });
            from = stop;
        }
        if (Avoid) Waypoints.AddRange(DetourPlanner.Plan(from, Target, TargetName, Clearance));
        // number the detours across the whole route
        int total = 0, k = 0;
        foreach (var w in Waypoints) if (!w.User) total++;
        for (int i = 0; i < Waypoints.Count; i++)
        {
            if (Waypoints[i].User) continue;
            var w = Waypoints[i]; w.Name = "Detour " + (++k) + " of " + total + " - " + TargetName; Waypoints[i] = w;
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

        if (selectedName == null) { if (!Hold && TargetName != null) { Clear(); return; } }
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
                UserPoints.Clear();
                Replan(ship);
            }
        }
        if (TargetName == null) { Path = null; return; }

        while (Next < Waypoints.Count && V3.Distance(ship, Waypoints[Next].Pos) < ArriveRadius) Next++;
        double cap = SpeedCap > 0 && (maxSpeed <= 0 || SpeedCap < maxSpeed) ? SpeedCap : maxSpeed;
        Path = RoutePath.Make(TargetName, ship, Target, velocity, accel * AccelScale, cap, Waypoints, Next, Clearance, accel * BrakeScale);
    }

    /// <summary>What to pick in F&amp;B's list next: the next detour or waypoint, or the destination.</summary>
    public string NextPick => Next < Waypoints.Count ? Waypoints[Next].Name : TargetName;
}
