using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using VRage.Game.ModAPI;

namespace SDX2_SystemMap;

/// <summary>
/// What the route keeps out of besides the worlds: the known stations (the map's list) and the bases owned by you or
/// your faction that your game has loaded. A base is a grid set to station with an Outpost or Installation core
/// (SystemData.IsBaseCore); ships, and grids without one of those cores, aren't obstacles. Only things you already know about (SDX2's plugin rules): no other
/// player's grids, nothing your sensors haven't shown you. Grids near each other are merged into one sphere.
/// Refreshed every few seconds; used when a route is planned.
/// </summary>
internal static class GridObstacles
{
    /// <summary>Kept this far outside a grid's own size.</summary>
    public const double Margin = 2000;
    /// <summary>Kept this far from a known station's centre (as far as a route to one stops short).</summary>
    public const double StationRadius = RouteExport.StationAvoid;
    /// <summary>Smaller grids are ignored (SDX2 deletes them anyway).</summary>
    private const int MinBlocks = 20;
    /// <summary>Whether a grid has a base core, by entity id; looked at again when its block count changes.</summary>
    private static readonly Dictionary<long, (int Blocks, bool Core)> cores = new Dictionary<long, (int, bool)>();
    private const double Interval = 3;

    private static readonly List<SystemData.World> list = new List<SystemData.World>();
    private static double next;

    public static IList<SystemData.World> Current => list;

    public static void Reset() { list.Clear(); cores.Clear(); next = 0; }

    public static void Update(double now)
    {
        if (now < next) return;
        next = now + Interval;
        list.Clear();
        foreach (var s in SystemData.Stations) list.Add(SystemData.Obstacle(s.Name, s.Pos, StationRadius, station: true));
        try { AddGrids(); } catch (Exception e) { Plugin.LogThrottled("grid obstacles: " + e.Message); }
        DetourPlanner.Merge(list);
    }

    private static void AddGrids()
    {
        var session = MySession.Static;
        if (session == null) return;
        long me = session.LocalPlayerId;
        var myFaction = session.Factions?.TryGetPlayerFaction(me);
        var ship = session.ControlledEntity?.Entity?.GetTopMostParent() as MyCubeGrid;
        var seen = new HashSet<long>();
        foreach (var e in MyEntities.GetEntities())
        {
            if (!(e is MyCubeGrid g) || !g.IsStatic || g.Physics == null || g.IsPreview || g.MarkedForClose || g.BlocksCount < MinBlocks) continue;
            if (g == ship) continue; // sitting in the base's own seat: it's not in your way
            if (!Ours(g, me, myFaction, session)) continue;
            seen.Add(g.EntityId);
            if (!HasBaseCore(g)) continue;
            var v = g.PositionComp.WorldVolume;
            list.Add(SystemData.Obstacle(g.DisplayName, new V3(v.Center.X, v.Center.Y, v.Center.Z), v.Radius + Margin));
        }
        if (cores.Count > seen.Count) { var gone = new List<long>(); foreach (var k in cores.Keys) if (!seen.Contains(k)) gone.Add(k); foreach (var k in gone) cores.Remove(k); }
    }

    private static bool HasBaseCore(MyCubeGrid g)
    {
        int n = g.BlocksCount;
        if (cores.TryGetValue(g.EntityId, out var c) && c.Blocks == n) return c.Core;
        bool core = false;
        foreach (var b in g.GetFatBlocks())
            if (SystemData.IsBaseCore(b.BlockDefinition?.Id.SubtypeName)) { core = true; break; }
        cores[g.EntityId] = (n, core);
        return core;
    }

    private static bool Ours(MyCubeGrid g, long me, IMyFaction myFaction, MySession session)
    {
        foreach (var owner in g.BigOwners)
        {
            if (owner == me) return true;
            if (myFaction != null && owner != 0 && session.Factions.TryGetPlayerFaction(owner) == myFaction) return true;
        }
        return false;
    }
}
