using System;
using System.Collections.Generic;

namespace SDX2_SystemMap;

/// <summary>
/// Keeps the labels another mod writes beside its HUD markers apart. Flip and Burn writes a destination's name, range,
/// time and retroburn countdown, and its "WARNING" labels (hostile space, asteroid), each to the right of its own
/// point: with two points close together on screen the texts ran through each other (2026-11-19, a detour's label
/// under "WARNING Hostile Space"). The lines of one marker share an anchor and move together; a marker's block that
/// would land on an earlier one's, or on a box of the plugin's own (<c>keep</c>), is moved below it, or above when
/// that is clearly the shorter move. Pixels, y down.
/// </summary>
public static class LabelSpread
{
    private struct Group { public float X, Y, Dy; public Box Block; public List<long> Seen; }
    private static readonly List<Group> groups = new List<Group>();
    private static long frame = -1;

    /// <summary>The block a marker's lines take up, from its anchor (the marker's point, the text starts just right of it).</summary>
    public static Box Block(float x, float y, float sw, float sh) => new Box(x, y - sh * 0.022f, sw * 0.13f, sh * 0.07f);

    /// <summary>
    /// How far down (up when negative) to move the line anchored at (x, y), written <paramref name="lineY"/> off it.
    /// <paramref name="frameNo"/> changes with each frame; the same line coming round again also starts a new one.
    /// </summary>
    public static float Shift(long frameNo, float x, float y, float lineY, float sw, float sh, IList<Box> keep)
    {
        long line = ((long)Math.Round(x * 8) << 32) ^ ((long)Math.Round(lineY * 4096) & 0xFFFFFFFFL);
        if (frameNo != frame) { frame = frameNo; groups.Clear(); }
        for (int i = 0; i < groups.Count; i++)
        {
            var g = groups[i];
            // one marker's lines: the same height, and at most a little further right (a line set beside another)
            if (Math.Abs(g.Y - y) > 0.25f || x < g.X - 0.5f || x - g.X > sw * 0.04f) continue;
            if (g.Seen.Contains(line)) { groups.Clear(); break; } // drawn twice: a new frame
            g.Seen.Add(line);
            return g.Dy;
        }
        var block = Block(x, y, sw, sh);
        float gap = sh * 0.004f;
        float down = Clear(block, keep, gap, sh, 1), up = Clear(block, keep, gap, sh, -1);
        float dy = float.IsNaN(down) ? (float.IsNaN(up) ? 0 : up) : !float.IsNaN(up) && -up < down * 0.6f ? up : down;
        groups.Add(new Group { X = x, Y = y, Dy = dy, Block = new Box(block.X, block.Y + dy, block.W, block.H), Seen = new List<long> { line } });
        return dy;
    }

    /// <summary>The smallest move one way (dir 1 down, -1 up) that clears everything; NaN when it would leave the screen.</summary>
    private static float Clear(Box block, IList<Box> keep, float gap, float sh, int dir)
    {
        float dy = 0;
        for (int pass = 0; pass < 12; pass++)
        {
            var b = new Box(block.X, block.Y + dy, block.W, block.H);
            bool hit = false; Box o = default;
            for (int i = 0; i < groups.Count && !hit; i++) if (groups[i].Block.Overlaps(b)) { hit = true; o = groups[i].Block; }
            for (int i = 0; keep != null && i < keep.Count && !hit; i++) if (keep[i].W > 0 && keep[i].Overlaps(b)) { hit = true; o = keep[i]; }
            if (!hit) return b.Y < 0 || b.Bottom > sh ? float.NaN : dy;
            dy = dir > 0 ? o.Bottom + gap - block.Y : o.Y - gap - block.Bottom;
        }
        return float.NaN;
    }

    public static void Reset() { groups.Clear(); frame = -1; }
}
