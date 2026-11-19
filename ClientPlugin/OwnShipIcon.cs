using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using VRageMath;

namespace SDX2_SystemMap;

/// <summary>
/// The map icon made from the ship you fly (setting "Ship icon: From Current Ship"): its blocks as a silhouette seen
/// from the side chosen in the settings. Rebuilt when the ship, its block count, its class or the side changes; checked
/// once a second from <see cref="GameState"/>.
/// </summary>
internal static class OwnShipIcon
{
    private static MyCubeGrid lastGrid;
    private static MyShipController lastSeat;
    private static long gridId;
    private static int blockCount = -1, side = int.MinValue;
    private static string cls;

    /// <summary>The side the icon shows (0 top, 1 right, 2 bottom, 3 left): the chosen one, or the one Auto picked.</summary>
    public static int SideUsed { get; private set; }

    public static void Reset()
    {
        lastGrid = null; lastSeat = null; gridId = 0; blockCount = -1; side = int.MinValue; cls = null;
        MapScene.OwnShip = null;
    }

    /// <summary>Makes the icon again now (the side was changed in the settings).</summary>
    public static void Refresh()
    {
        blockCount = -1;
        if (lastGrid != null && !lastGrid.MarkedForClose) Update(lastGrid, lastSeat, cls ?? "");
    }

    public static void Update(MyCubeGrid grid, MyShipController seat, string shipClass)
    {
        try
        {
            if (grid == null || grid.MarkedForClose) { Reset(); return; }
            int want = Plugin.Settings.ShipIconSide;
            if (grid.EntityId == gridId && grid.BlocksCount == blockCount && want == side && shipClass == cls) return;
            lastGrid = grid; lastSeat = seat; gridId = grid.EntityId; blockCount = grid.BlocksCount; side = want; cls = shipClass;

            var cells = new List<ShipMask.Cells>(blockCount);
            foreach (var b in grid.GetBlocks())
            {
                Vector3I lo = b.Min, hi = b.Max;
                cells.Add(new ShipMask.Cells(lo.X, lo.Y, lo.Z, hi.X, hi.Y, hi.Z));
            }
            Axes(grid, seat, out var fwd, out var up);
            SideUsed = want >= 0 && want <= 3 ? want : ShipMask.AutoSide(cells, fwd, up);
            var rows = ShipMask.Build(cells, fwd, up, SideUsed);
            MapScene.OwnShip = rows == null ? null : ShipIcons.FromMask(shipClass, grid.DisplayName, rows);
        }
        catch (Exception e) { MapScene.OwnShip = null; Plugin.LogThrottled("ship icon not made: " + e.Message); }
    }

    /// <summary>
    /// The ship's nose and top as grid axes: the Ship Core's; with no core, the nose is against the first fusion drive's
    /// flame; with neither, the seat's.
    /// </summary>
    private static void Axes(MyCubeGrid grid, MyShipController seat, out (int, int, int) fwd, out (int, int, int) up)
    {
        MyCubeBlock core = null, drive = null;
        foreach (var b in grid.GetFatBlocks())
        {
            string sub = b.BlockDefinition?.Id.SubtypeName ?? "";
            if (sub.StartsWith("sdx_shipcore", StringComparison.OrdinalIgnoreCase)) { core = b; break; }
            if (drive == null && sub.StartsWith("sdx_drive", StringComparison.OrdinalIgnoreCase)) drive = b;
        }
        var from = core ?? drive ?? seat;
        Vector3I f = Vector3I.Forward, u = Vector3I.Up;
        if (from != null)
        {
            f = Base6Directions.GetIntVector(from.Orientation.Forward);
            u = Base6Directions.GetIntVector(from.Orientation.Up);
            if (core == null && drive != null) f = -f;
        }
        fwd = (f.X, f.Y, f.Z); up = (u.X, u.Y, u.Z);
    }
}
