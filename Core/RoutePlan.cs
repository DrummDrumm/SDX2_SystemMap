using System;
using System.Collections.Generic;

namespace SDX2_SystemMap;

/// <summary>
/// A straight-line Flip and Burn route from the ship to the destination picked in F&amp;B's navigation list:
/// burn toward it, coast at F&amp;B's speed cap if reached, flip, brake to a stop. Also lists the Nexus sector borders the
/// line crosses (each one is a server transfer) and the worlds it passes close to or through.
/// Game-free: the plugin fills in the ship state, the tests fill in anything they like.
/// </summary>
public sealed class RoutePlan
{
    public const double DefaultMaxSpeed = 50000; // F&B's Settings.MaxSpeed on SDX2

    public sealed class Crossing { public double At; public SystemData.Sector From, To; public V3 Pos; }
    public sealed class Hazard { public SystemData.World World; public double Clearance, At; public bool Blocked; public V3 Closest; }

    // inputs
    public string TargetName;
    public V3 Start, Target, Velocity;
    public double MaxAccel;            // m/s², 0 = unknown (no burn plan, ETA from current speed only)
    /// <summary>Deceleration for the brake burn, m/s²; 0 = same as <see cref="MaxAccel"/>.</summary>
    public double BrakeAccel;
    private double B => BrakeAccel > 0.01 ? BrakeAccel : MaxAccel;
    public double MaxSpeed = DefaultMaxSpeed;
    /// <summary>Wanted clearance above a world's surface; closer passes are hazards.</summary>
    public double Clearance = DefaultClearance;
    public const double DefaultClearance = 100e3;

    // results
    public double Distance, ClosingSpeed, LateralSpeed;
    public bool HasBurnPlan, TooFastToStop;
    public double PeakSpeed, BurnTime, CoastTime, BrakeTime, Eta;
    public double FlipAt, CoastAt;     // metres along the line where the coast starts and the flip (brake) starts
    public double StopDistance;        // metres needed to stop from the current closing speed
    public V3 Dir;
    public readonly List<Crossing> Crossings = new List<Crossing>();
    public readonly List<Hazard> Hazards = new List<Hazard>();
    public SystemData.Sector StartSector, TargetSector;
    public SystemData.Station StationAtTarget;

    public V3 PointAt(double along) => Start + Dir * Math.Max(0, Math.Min(Distance, along));
    public V3 FlipPoint => PointAt(FlipAt);
    public V3 CoastPoint => PointAt(CoastAt);

    public static RoutePlan Make(string name, V3 start, V3 target, V3 velocity, double maxAccel, double maxSpeed = DefaultMaxSpeed, double clearance = DefaultClearance, double brakeAccel = 0)
    {
        var p = new RoutePlan { TargetName = name, Start = start, Target = target, Velocity = velocity, MaxAccel = maxAccel, BrakeAccel = brakeAccel, MaxSpeed = maxSpeed > 0 ? maxSpeed : DefaultMaxSpeed, Clearance = clearance };
        p.Solve();
        return p;
    }

    public void Solve()
    {
        var d = Target - Start;
        Distance = d.Length;
        Dir = Distance > 1e-6 ? d / Distance : new V3(0, 0, 1);
        ClosingSpeed = Velocity.Dot(Dir);
        LateralSpeed = (Velocity - Dir * ClosingSpeed).Length;
        StartSector = SystemData.SectorAt(Start);
        TargetSector = SystemData.SectorAt(Target);
        StationAtTarget = SystemData.StationNear(Target, 200e3);
        SolveBurn();
        FindCrossings();
        FindHazards();
    }

    /// <summary>
    /// Burn / coast / brake times along the line from the current closing speed u, burning at a and braking at b.
    /// Moving away first costs a stop (u²/2a further to go). Peak speed vp solves (vp² − u²)/2a + vp²/2b = d, capped at
    /// MaxSpeed with a coast between.
    /// </summary>
    private void SolveBurn()
    {
        double a = MaxAccel, b = B, u = ClosingSpeed, dist = Distance;
        HasBurnPlan = a > 0.01 && dist > 1;
        StopDistance = b > 0.01 && u > 0 ? u * u / (2 * b) : 0;
        if (!HasBurnPlan)
        {
            PeakSpeed = Math.Max(0, u);
            Eta = u > 0.5 ? dist / u : double.NaN;
            FlipAt = CoastAt = dist;
            return;
        }

        double extra = 0, turnTime = 0;
        if (u < 0) { turnTime = -u / a; extra = u * u / (2 * a); u = 0; }

        if (StopDistance >= dist)
        {
            // already too fast: brake now and overshoot
            TooFastToStop = true;
            PeakSpeed = u; BurnTime = 0; CoastTime = 0; BrakeTime = u / b;
            FlipAt = CoastAt = 0;
            Eta = BrakeTime;
            return;
        }

        double total = dist + extra;
        double vp = Math.Sqrt((total + u * u / (2 * a)) / (1 / (2 * a) + 1 / (2 * b)));
        if (vp <= MaxSpeed)
        {
            PeakSpeed = vp;
            BurnTime = (vp - u) / a + turnTime;
            CoastTime = 0;
            BrakeTime = vp / b;
            CoastAt = FlipAt = Math.Max(0, (vp * vp - u * u) / (2 * a) - extra);
        }
        else
        {
            vp = MaxSpeed;
            PeakSpeed = vp;
            double accD = (vp * vp - u * u) / (2 * a), brkD = vp * vp / (2 * b);
            BurnTime = (vp - u) / a + turnTime;
            BrakeTime = vp / b;
            CoastTime = (total - accD - brkD) / vp;
            CoastAt = Math.Max(0, accD - extra);
            FlipAt = Math.Max(CoastAt, dist - brkD);
        }
        Eta = BurnTime + CoastTime + BrakeTime;
    }

    /// <summary>Sector changes along the line, sampled every ~1/600 of its length (and at least every 25 km).</summary>
    private void FindCrossings()
    {
        Crossings.Clear();
        if (Distance < 1) return;
        int n = (int)Math.Min(4000, Math.Max(600, Distance / 25e3));
        var last = StartSector;
        for (int i = 1; i <= n; i++)
        {
            double along = Distance * i / n;
            var s = SystemData.SectorAt(PointAt(along));
            if (s == last) continue;
            // refine the border by bisection
            double lo = Distance * (i - 1) / n, hi = along;
            for (int k = 0; k < 20; k++)
            {
                double mid = (lo + hi) / 2;
                if (SystemData.SectorAt(PointAt(mid)) == last) lo = mid; else hi = mid;
            }
            Crossings.Add(new Crossing { At = hi, From = last, To = s, Pos = PointAt(hi) });
            last = s;
        }
    }

    /// <summary>Worlds whose surface the line passes within Clearance (100 km) of (or through). The destination's own world is skipped.</summary>
    private void FindHazards()
    {
        Hazards.Clear();
        foreach (var w in SystemData.Worlds)
        {
            if (DetourPlanner.Skip(w, Start, Target, Clearance)) continue;
            double c = V3.SegmentDistance(Start, Target, w.Pos, out var t);
            double clear = c - w.Radius;
            if (clear > Clearance) continue;
            Hazards.Add(new Hazard { World = w, Clearance = clear, At = t * Distance, Blocked = clear < 0, Closest = PointAt(t * Distance) });
        }
        Hazards.Sort((x, y) => x.At.CompareTo(y.At));
    }

    /// <summary>Position along the route at time t from now (for T+ marks), by the burn plan.</summary>
    public double AlongAt(double t)
    {
        if (!HasBurnPlan) return Math.Min(Distance, Math.Max(0, ClosingSpeed) * t);
        double a = MaxAccel, b = B, u = Math.Max(0, ClosingSpeed);
        if (TooFastToStop) return Math.Min(Distance, u * t - b * t * t / 2);
        if (t <= BurnTime) return Math.Min(FlipAt, u * t + a * t * t / 2);
        t -= BurnTime;
        if (t <= CoastTime) return Math.Min(FlipAt, CoastAt + PeakSpeed * t);
        t -= CoastTime;
        t = Math.Min(t, BrakeTime);
        return Math.Min(Distance, FlipAt + PeakSpeed * t - b * t * t / 2);
    }
}
