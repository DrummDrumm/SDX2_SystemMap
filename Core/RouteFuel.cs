using System;

namespace SDX2_SystemMap;

/// <summary>
/// The water a route burns: fusion drives use water in proportion to their thrust (the SDX2 sheet: burn L/s at 100 %
/// times thrust % times time), and only while burning or braking, not on the coast.
/// </summary>
public static class RouteFuel
{
    /// <summary>A route that would leave less than this share of the tanks asks before the autopilot starts.</summary>
    public const double WarnBelow = 0.10;

    /// <summary>Litres of water for the whole route: each leg's burn and brake, at the share of full thrust it uses.
    /// fullAccel = the ship's acceleration at 100 % thrust, rate = its drives' water use at 100 % (L/s).</summary>
    public static double Litres(RoutePath p, double fullAccel, double rate)
    {
        if (p == null || !(fullAccel > 0) || !(rate > 0)) return 0;
        double s = 0;
        foreach (var l in p.Legs)
        {
            if (!l.HasBurnPlan || double.IsNaN(l.Eta)) continue;
            s += l.BurnTime * Math.Min(1, l.MaxAccel / fullAccel) + l.BrakeTime * Math.Min(1, l.BrakeUsed / fullAccel);
        }
        return s * rate;
    }

    /// <summary>The share of the tanks left after burning used (NaN with no tanks; below 0 = runs dry).</summary>
    public static double LeftAfter(double stored, double capacity, double used) => capacity > 0 ? (stored - used) / capacity : double.NaN;

    /// <summary>Whether to warn before flying: tanks known and less than <see cref="WarnBelow"/> left at the end.</summary>
    public static bool Low(double stored, double capacity, double used) => capacity > 0 && used > 0 && LeftAfter(stored, capacity, used) < WarnBelow;

    /// <summary>Litres as text: L, kL or ML.</summary>
    public static string Volume(double l) =>
        Math.Abs(l) >= 1e6 ? Fmt.Num(l / 1e6, "0.00") + " ML" : Math.Abs(l) >= 1e3 ? Fmt.Num(l / 1e3, "0.0") + " kL" : Fmt.Num(l, "0") + " L";
}
