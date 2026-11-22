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
            s += l.FuelBurn * Math.Min(1, l.MaxAccel / fullAccel) + l.FuelBrake * Math.Min(1, l.BrakeUsed / fullAccel);
        }
        return s * rate;
    }

    /// <summary>
    /// How far the water in the tanks takes the ship, ending at rest: burning up to the speed the water allows, then
    /// braking from it, with no coast (a coast costs no water, so past the speed cap the range has no end: NaN). stored
    /// in litres, rate = the drives' use at 100 % (L/s), fullAccel = the ship's acceleration at 100 %, accel and brake =
    /// what the route burns and brakes with, speed = the ship's speed now (it has to be braked too). 0 when the water
    /// doesn't even stop the ship.
    /// </summary>
    public static double Range(double stored, double rate, double fullAccel, double accel, double brake, double speed, double maxSpeed)
    {
        if (!(stored >= 0) || !(rate > 0) || !(fullAccel > 0) || !(accel > 0)) return double.NaN;
        if (!(brake > 0)) brake = accel;
        // a m/s of speed costs rate / fullAccel litres, gained or lost, whatever the thrust share
        double dv = stored * fullAccel / rate, u = Math.Max(0, speed);
        if (dv < u) return 0;
        double v = (dv + u) / 2;
        if (maxSpeed > 0 && v >= maxSpeed) return double.NaN;
        return (v * v - u * u) / (2 * accel) + v * v / (2 * brake);
    }

    /// <summary>The share of the tanks left after burning used (NaN with no tanks; below 0 = runs dry).</summary>
    public static double LeftAfter(double stored, double capacity, double used) => capacity > 0 ? (stored - used) / capacity : double.NaN;

    /// <summary>Whether to warn before flying: tanks known and less than <see cref="WarnBelow"/> left at the end.</summary>
    public static bool Low(double stored, double capacity, double used) => capacity > 0 && used > 0 && LeftAfter(stored, capacity, used) < WarnBelow;

    /// <summary>Litres as text: L, kL or ML.</summary>
    public static string Volume(double l) =>
        Math.Abs(l) >= 1e6 ? Fmt.Num(l / 1e6, "0.00") + " ML" : Math.Abs(l) >= 1e3 ? Fmt.Num(l / 1e3, "0.0") + " kL" : Fmt.Num(l, "0") + " L";
}
