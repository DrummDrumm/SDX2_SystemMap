using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using VRage.Game.ModAPI;

namespace SDX2_SystemMap;

/// <summary>
/// Before the autopilot starts a route: are the ship's fusion drives (sdx_drive*, the only thrusters it pushes with)
/// switched on? The ones switched off can be turned on from a pop-up.
/// </summary>
internal static class DriveCheck
{
    private static readonly List<IMyCubeGrid> grids = new List<IMyCubeGrid>();

    /// <summary>The ship you're controlling (null when you're not in a seat or remote control).</summary>
    public static MyCubeGrid Ship => MySession.Static?.ControlledEntity?.Entity?.GetTopMostParent() as MyCubeGrid;

    /// <summary>The ship's fusion drives (with its subgrids'), and those of them switched off that you can switch on.</summary>
    public static void Read(MyCubeGrid ship, List<MyThrust> off, out int total)
    {
        off.Clear(); total = 0;
        if (ship == null) return;
        grids.Clear();
        try { ((IMyCubeGrid)ship).GetGridGroup(GridLinkTypeEnum.Mechanical)?.GetGrids(grids); } catch (Exception) { }
        if (grids.Count == 0) grids.Add(ship);
        long me = MySession.Static?.LocalPlayerId ?? 0;
        foreach (var g in grids)
            if (g is MyCubeGrid cg)
                foreach (var b in cg.GetFatBlocks())
                {
                    if (!(b is MyThrust t) || !t.BlockDefinition.Id.SubtypeName.StartsWith("sdx_drive", StringComparison.Ordinal)) continue;
                    total++;
                    if (!t.Enabled && t.IsFunctional && t.HasPlayerAccess(me)) off.Add(t);
                }
    }

    /// <summary>Switches the drives on (synced to the server like the terminal's On/Off).</summary>
    public static int TurnOn(List<MyThrust> off)
    {
        int n = 0;
        foreach (var t in off)
        {
            if (t.Closed || t.Enabled) continue;
            ((Sandbox.ModAPI.IMyFunctionalBlock)t).Enabled = true;
            n++;
        }
        return n;
    }

    /// <summary>
    /// The ship's water: litres in its tanks of the drives' fuel (all sdx_tankWater* on the grid group), their capacity,
    /// and the litres a second its working fusion drives burn together at 100 % thrust (the definitions' power over
    /// efficiency times the gas's energy density: 55.8 L/s for a Canterbury, as on the SDX2 sheet). 0s when unknown.
    /// </summary>
    public static void Water(MyCubeGrid ship, out double stored, out double capacity, out double rate)
    {
        stored = capacity = rate = 0;
        if (ship == null) return;
        grids.Clear();
        try { ((IMyCubeGrid)ship).GetGridGroup(GridLinkTypeEnum.Mechanical)?.GetGrids(grids); } catch (Exception) { }
        if (grids.Count == 0) grids.Add(ship);
        VRage.Game.MyDefinitionId? fuel = null;
        foreach (var g in grids)
            if (g is MyCubeGrid cg)
                foreach (var b in cg.GetFatBlocks())
                {
                    if (!(b is MyThrust t) || !t.IsWorking || !t.BlockDefinition.Id.SubtypeName.StartsWith("sdx_drive", StringComparison.Ordinal)) continue;
                    var gas = t.FuelDefinition;
                    double eff = t.BlockDefinition.FuelConverter?.Efficiency ?? 1;
                    if (gas == null || !(gas.EnergyDensity > 0) || !(eff > 0) || gas.Id.SubtypeName == "Electricity") continue;
                    rate += t.MaxPowerConsumption / (eff * gas.EnergyDensity);
                    fuel ??= gas.Id;
                }
        if (fuel == null) return;
        foreach (var g in grids)
            if (g is MyCubeGrid cg)
                foreach (var b in cg.GetFatBlocks())
                {
                    if (!(b is Sandbox.Game.Entities.Blocks.MyGasTank k) || !k.IsFunctional || k.BlockDefinition.StoredGasId != fuel.Value) continue;
                    capacity += k.Capacity;
                    stored += k.FilledRatio * k.Capacity;
                }
    }
}
