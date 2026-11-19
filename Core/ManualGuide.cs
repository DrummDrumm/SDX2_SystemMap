using System;

namespace SDX2_SystemMap;

/// <summary>
/// The HUD line while you fly a route by hand (MANUAL): which point Flip and Burn's box points at now, how far along
/// the route you are, and a heads-up shortly before a point where the route turns (F&amp;B plans to stop at each point
/// it's given, so you know the next leg is coming).
/// </summary>
public static class ManualGuide
{
    /// <summary>The heads-up shows this long before a turning point (at the speed you close on it).</summary>
    public const double TurnWarn = 30;
    /// <summary>Turns smaller than this (degrees) get no heads-up.</summary>
    public const double MinTurn = 10;

    /// <summary>The angle (degrees) between flying ship → next and next → after.</summary>
    public static double TurnAngle(V3 ship, V3 next, V3 after)
    {
        V3 a = (next - ship).Normalized, b = (after - next).Normalized;
        if (a.LengthSq < 0.5 || b.LengthSq < 0.5) return 0;
        return Math.Acos(Math.Max(-1, Math.Min(1, a.Dot(b)))) * 180 / Math.PI;
    }

    /// <summary>Seconds to the point at the speed you close on it; NaN when you aren't closing on it.</summary>
    public static double TimeTo(V3 ship, V3 vel, V3 point)
    {
        var d = point - ship;
        double dist = d.Length, closing = dist > 0 ? vel.Dot(d) / dist : 0;
        return closing > 1 ? dist / closing : double.NaN;
    }

    /// <summary>
    /// The line's text and sub-line. index / count: the point flown to (0-based) of all points including the
    /// destination; afterName / afterPos: the point after it (null at the last); fnbPick: F&amp;B's current pick.
    /// </summary>
    public static string Text(string target, int index, int count, string nextName, V3 nextPos, string afterName, V3 afterPos,
        V3 ship, V3 vel, double distLeft, double eta, string fnbPick, out string sub)
    {
        string text = "to " + MapScene.ShortName(target);
        if (count > 1) text += "    point " + (index + 1) + " of " + count;
        text += "    " + Fmt.Dist(distLeft);
        double speed = vel.Length;
        if (speed >= 1) text += "    " + Fmt.Speed(speed);
        if (eta > 0 && !double.IsInfinity(eta)) text += "    ETA " + Fmt.Time(eta);

        string next = MapScene.ShortName(nextName);
        if (fnbPick != nextName) { sub = "Pick \"" + next + "\" in N"; return text; }
        double t = TimeTo(ship, vel, nextPos), turn = afterName != null ? TurnAngle(ship, nextPos, afterPos) : 0;
        if (afterName != null && turn >= MinTurn && !double.IsNaN(t) && t <= TurnWarn)
            sub = "Turn " + Fmt.Num(turn, "0") + "° at " + next + " for " + MapScene.ShortName(afterName) + " in " + Fmt.Time(t);
        else
            sub = "Flip and Burn's box: " + next + (afterName != null ? "    then " + MapScene.ShortName(afterName) : "    (the destination)");
        return text;
    }
}
