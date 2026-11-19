using System;
using System.Collections.Generic;

namespace SDX2_SystemMap;

/// <summary>
/// A ship's silhouette from its blocks, as <see cref="ShipIcons.FromMask"/> takes it: rows across the ship ('#' = hull),
/// the nose at column 0. The same sampling as Tools/ship-silhouettes.py, done on the ship you fly, seen from one of four
/// sides round its nose-to-tail line.
/// </summary>
public static class ShipMask
{
    /// <summary>Mask cells along the ship's longer dimension.</summary>
    public const int Length = 36;
    /// <summary>The side that suits the ship: from above or from the right, whichever looks down its thinner dimension.</summary>
    public const int Auto = -1;
    public static readonly string[] SideNames = { "Top", "Right side", "Bottom", "Left side" };

    public static string SideName(int side) => side >= 0 && side < SideNames.Length ? SideNames[side] : "Auto";

    /// <summary>A block's cells: its lowest and highest corner (grid cells, both included).</summary>
    public struct Cells
    {
        public int X0, Y0, Z0, X1, Y1, Z1;
        public Cells(int x0, int y0, int z0, int x1, int y1, int z1) { X0 = x0; Y0 = y0; Z0 = z0; X1 = x1; Y1 = y1; Z1 = z1; }
    }

    private static (int, int, int) Cross((int X, int Y, int Z) a, (int X, int Y, int Z) b) => (a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    private static (int, int, int) Neg((int X, int Y, int Z) a) => (-a.X, -a.Y, -a.Z);

    private static void Span(Cells c, (int X, int Y, int Z) axis, out int lo, out int hi)
    {
        int a = c.X0 * axis.X + c.Y0 * axis.Y + c.Z0 * axis.Z, b = c.X1 * axis.X + c.Y1 * axis.Y + c.Z1 * axis.Z;
        lo = Math.Min(a, b); hi = Math.Max(a, b) + 1;
    }

    /// <summary>The side <see cref="Auto"/> stands for on this ship.</summary>
    public static int AutoSide(IReadOnlyList<Cells> blocks, (int X, int Y, int Z) fwd, (int X, int Y, int Z) up)
    {
        if (blocks == null || blocks.Count == 0) return 0;
        var right = Cross(fwd, up);
        int u0 = int.MaxValue, u1 = int.MinValue, r0 = int.MaxValue, r1 = int.MinValue;
        foreach (var b in blocks)
        {
            Span(b, up, out int a, out int c); u0 = Math.Min(u0, a); u1 = Math.Max(u1, c);
            Span(b, right, out a, out c); r0 = Math.Min(r0, a); r1 = Math.Max(r1, c);
        }
        return u1 - u0 <= r1 - r0 ? 0 : 1;
    }

    /// <summary>
    /// The mask of <paramref name="blocks"/> seen from <paramref name="side"/> (0 top, 1 right, 2 bottom, 3 left, or
    /// <see cref="Auto"/>), with the nose (<paramref name="fwd"/>) at column 0 and the rows running to the viewer's
    /// right when the nose points up. <paramref name="fwd"/> and <paramref name="up"/> are grid axes (one of them ±1).
    /// Null when there are no blocks.
    /// </summary>
    public static string[] Build(IReadOnlyList<Cells> blocks, (int X, int Y, int Z) fwd, (int X, int Y, int Z) up, int side)
    {
        if (blocks == null || blocks.Count == 0) return null;
        if (side < 0 || side > 3) side = AutoSide(blocks, fwd, up);
        var right = Cross(fwd, up);
        var toViewer = side == 0 ? up : side == 1 ? right : side == 2 ? Neg(up) : Neg(right);
        var across = Cross(fwd, toViewer);

        int f0 = int.MaxValue, f1 = int.MinValue, a0 = int.MaxValue, a1 = int.MinValue;
        foreach (var b in blocks)
        {
            Span(b, fwd, out int lo, out int hi); f0 = Math.Min(f0, lo); f1 = Math.Max(f1, hi);
            Span(b, across, out lo, out hi); a0 = Math.Min(a0, lo); a1 = Math.Max(a1, hi);
        }
        double cell = Math.Max(f1 - f0, a1 - a0) / (double)Length;
        int L = Math.Max(1, Math.Min(Length, (int)Math.Ceiling((f1 - f0) / cell - 1e-9)));
        int used = Math.Max(1, Math.Min(Length, (int)Math.Ceiling((a1 - a0) / cell - 1e-9)));
        int W = used;
        var grid = new bool[W, L];
        foreach (var b in blocks)
        {
            Span(b, fwd, out int lo, out int hi);
            Span(b, across, out int alo, out int ahi);
            // every mask cell the block overlaps (column 0 = nose)
            int i0 = (int)((f1 - hi) / cell + 1e-9), i1 = Math.Min(L, (int)Math.Ceiling((f1 - lo) / cell - 1e-9));
            int j0 = (int)((alo - a0) / cell + 1e-9), j1 = Math.Min(used, (int)Math.Ceiling((ahi - a0) / cell - 1e-9));
            for (int j = j0; j < j1; j++)
                for (int i = i0; i < i1; i++) grid[j, i] = true;
        }
        // close 1-cell gaps (big blocks, open rooms)
        bool Get(int j, int i) => j >= 0 && j < W && i >= 0 && i < L && grid[j, i];
        var rows = new string[W];
        var row = new char[L];
        for (int j = 0; j < W; j++)
        {
            for (int i = 0; i < L; i++)
                row[i] = grid[j, i] || (Get(j, i - 1) && Get(j, i + 1)) || (Get(j - 1, i) && Get(j + 1, i)) ? '#' : '.';
            rows[j] = new string(row);
        }
        return rows;
    }
}
