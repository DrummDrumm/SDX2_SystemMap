using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace SDX2_SystemMap;

/// <summary>
/// A debugging tool: measures what the pinned map costs a frame in game. It runs once per world load when the file
/// <c>Storage\SDX2_SystemMap.perf</c> exists in the game's user folder (delete the file to switch it off), starting
/// <see cref="StartAfter"/> seconds after the first frame. It cycles two states, <see cref="Rounds"/> times each for
/// <see cref="PhaseSeconds"/> seconds: map not drawn, map drawn. The averages go to the game's log. Times are those of the game thread: what the
/// render thread spends on the sprites shows only as a longer frame.
/// </summary>
internal static class Perf
{
    private const double StartAfter = 12, PhaseSeconds = 6, Settle = 1;
    private const int Rounds = 3, States = 2;
    private static readonly string[] Names = { "map not drawn", "map drawn" };

    private struct Sum { public long Frames, DrawTicks, FrameTicks, MaxDraw, MaxFrame, Sprites, Bytes, UpdateTicks; }

    private static bool? wanted;
    private static bool done;
    private static double firstFrame = -1, phaseStart;
    private static int phase = -1; // -1 waiting, 0..Rounds*States-1 running
    private static readonly Sum[] sums = new Sum[States];
    private static long drawStart, lastFrame, bytesStart, updateStart, updateTicks;
    private static bool counting;

    private static bool Wanted
    {
        get
        {
            if (wanted == null)
            {
                try { wanted = File.Exists(Path.Combine(VRage.FileSystem.MyFileSystem.UserDataPath ?? "", "Storage", "SDX2_SystemMap.perf")); }
                catch { wanted = false; }
            }
            return wanted.Value;
        }
    }

    /// <summary>A world closed: the next one measures again.</summary>
    public static void Reset() { done = false; firstFrame = -1; phase = -1; wanted = null; }

    public static void BeginUpdate() { if (phase >= 0) updateStart = Stopwatch.GetTimestamp(); }
    public static void EndUpdate() { if (phase >= 0) updateTicks += Stopwatch.GetTimestamp() - updateStart; }

    /// <summary>Before the pinned map is drawn. False: leave it out this frame (the "not drawn" state).</summary>
    public static bool BeginDraw()
    {
        if (done || !Wanted) return true;
        double now = Plugin.Now;
        if (firstFrame < 0) firstFrame = now;
        if (phase < 0)
        {
            if (now - firstFrame < StartAfter) return true;
            phase = 0; phaseStart = now; lastFrame = 0; updateTicks = 0;
            Plugin.Log("perf: measuring the pinned map for " + (Rounds * States * PhaseSeconds).ToString("0") + " s");
        }
        else if (now - phaseStart >= PhaseSeconds)
        {
            phase++; phaseStart = now; lastFrame = 0; updateTicks = 0;
            if (phase >= Rounds * States) { Finish(); return true; }
        }
        int k = phase % States;
        counting = now - phaseStart >= Settle;
        long t = Stopwatch.GetTimestamp();
        if (counting && lastFrame != 0)
        {
            long f = t - lastFrame;
            sums[k].Frames++; sums[k].FrameTicks += f; if (f > sums[k].MaxFrame) sums[k].MaxFrame = f;
            sums[k].UpdateTicks += updateTicks;
        }
        updateTicks = 0;
        lastFrame = t;
        GameCanvas.Sprites = 0;
        bytesStart = GC.GetAllocatedBytesForCurrentThread();
        drawStart = Stopwatch.GetTimestamp();
        return k != 0;
    }

    public static void EndDraw()
    {
        if (phase < 0 || done || !counting) return;
        long d = Stopwatch.GetTimestamp() - drawStart;
        int k = phase % States;
        sums[k].DrawTicks += d; if (d > sums[k].MaxDraw) sums[k].MaxDraw = d;
        sums[k].Sprites += GameCanvas.Sprites;
        sums[k].Bytes += GC.GetAllocatedBytesForCurrentThread() - bytesStart;
    }

    private static void Finish()
    {
        done = true; phase = -1;
        double ms = 1000.0 / Stopwatch.Frequency;
        var sb = new StringBuilder("perf: pinned map, per frame (game thread)");
        for (int k = 0; k < States; k++)
        {
            var s = sums[k]; double n = Math.Max(1, s.Frames);
            sb.Append("\n  ").Append(Names[k].PadRight(14))
              .Append(" frames ").Append(s.Frames)
              .Append("  frame ").Append((s.FrameTicks * ms / n).ToString("0.000")).Append(" ms (").Append((n * 1000 / Math.Max(1e-9, s.FrameTicks * ms)).ToString("0.0")).Append(" fps, worst ").Append((s.MaxFrame * ms).ToString("0.0")).Append(" ms)")
              .Append("  draw ").Append((s.DrawTicks * ms / n).ToString("0.000")).Append(" ms (worst ").Append((s.MaxDraw * ms).ToString("0.00")).Append(")")
              .Append("  update ").Append((s.UpdateTicks * ms / n).ToString("0.000")).Append(" ms")
              .Append("  sprites ").Append((s.Sprites / n).ToString("0"))
              .Append("  alloc ").Append((s.Bytes / n / 1024).ToString("0.0")).Append(" KB");
            sums[k] = default;
        }
        Plugin.Log(sb.ToString());
    }
}
