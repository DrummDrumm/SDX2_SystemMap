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
    /// <summary>The deceleration the brake leg uses.</summary>
    public double BrakeUsed => B;
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

    /// <summary>Points the leg curves through (none: a straight leg). See <see cref="RouteCurve"/>.</summary>
    public IList<V3> Through;
    /// <summary>The curved leg as a polyline from Start to Target (null when straight); <see cref="Distance"/> is its length.</summary>
    public List<V3> Curve;
    private double[] cum;
    public bool Curved => Curve != null;
    /// <summary>The fastest the bends let the ship fly this leg (infinite when straight).</summary>
    public double CornerSpeed = double.PositiveInfinity;
    /// <summary>Speed to pass the leg's end at (flip and burn: slow only enough to turn there); 0 = stop.</summary>
    public double EndSpeed;
    /// <summary>The speed it really passes the end at: <see cref="EndSpeed"/>, or less if it can't get that fast.</summary>
    public double ExitSpeed;

    public V3 PointAt(double along)
    {
        along = Math.Max(0, Math.Min(Distance, along));
        if (Curve == null) return Start + Dir * along;
        int lo = 0, hi = cum.Length - 1;
        while (hi - lo > 1) { int m = (lo + hi) / 2; if (cum[m] <= along) lo = m; else hi = m; }
        double seg = cum[hi] - cum[lo];
        return seg < 1e-9 ? Curve[lo] : V3.Lerp(Curve[lo], Curve[hi], (along - cum[lo]) / seg);
    }

    /// <summary>The point of the leg nearest p: distance, and how far along the leg.</summary>
    public double Nearest(V3 p, out double along)
    {
        if (Curve == null) { double d0 = V3.SegmentDistance(Start, Target, p, out var t0); along = t0 * Distance; return d0; }
        double best = double.MaxValue; along = 0;
        for (int i = 0; i + 1 < Curve.Count; i++)
        {
            double d = V3.SegmentDistance(Curve[i], Curve[i + 1], p, out var t);
            if (d < best) { best = d; along = cum[i] + t * (cum[i + 1] - cum[i]); }
        }
        return best;
    }
    public V3 FlipPoint => PointAt(FlipAt);
    public V3 CoastPoint => PointAt(CoastAt);

    public static RoutePlan Make(string name, V3 start, V3 target, V3 velocity, double maxAccel, double maxSpeed = DefaultMaxSpeed, double clearance = DefaultClearance, double brakeAccel = 0, IList<V3> through = null, double endSpeed = 0)
    {
        var p = new RoutePlan { TargetName = name, Start = start, Target = target, Velocity = velocity, MaxAccel = maxAccel, BrakeAccel = brakeAccel, MaxSpeed = maxSpeed > 0 ? maxSpeed : DefaultMaxSpeed, Clearance = clearance, Through = through, EndSpeed = endSpeed };
        p.Solve();
        return p;
    }

    public void Solve()
    {
        var d = Target - Start;
        Distance = d.Length;
        Dir = Distance > 1e-6 ? d / Distance : new V3(0, 0, 1);
        Curve = null; CornerSpeed = double.PositiveInfinity;
        if (Through != null && Through.Count > 0 && Distance > 1)
        {
            Curve = RouteCurve.Build(Start, Through, Target);
            cum = new double[Curve.Count];
            for (int i = 1; i < Curve.Count; i++) cum[i] = cum[i - 1] + V3.Distance(Curve[i - 1], Curve[i]);
            Distance = cum[cum.Length - 1];
            Dir = (Curve[1] - Curve[0]).Normalized;
            if (Dir.LengthSq < 0.5) Dir = d.Normalized;
        }
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
        if (Curve != null && MaxAccel > 0.01) LimitCorners();
        double a = MaxAccel, b = B, u = ClosingSpeed, dist = Distance;
        double w = Math.Max(0, Math.Min(EndSpeed, MaxSpeed));
        HasBurnPlan = a > 0.01 && dist > 1;
        TooFastToStop = false;
        StopDistance = b > 0.01 && u > w ? (u * u - w * w) / (2 * b) : 0;
        ExitSpeed = w;
        if (!HasBurnPlan)
        {
            ExitSpeed = Math.Max(0, u);
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
            PeakSpeed = u; BurnTime = 0; CoastTime = 0; BrakeTime = (u - w) / b;
            FlipAt = CoastAt = 0;
            Eta = BrakeTime;
            return;
        }

        double total = dist + extra;
        double reach = Math.Sqrt(u * u + 2 * a * total);
        if (w > 0 && reach <= w)
        {
            // can't get up to the turn speed: burn the whole leg
            PeakSpeed = ExitSpeed = reach;
            BurnTime = (reach - u) / a + turnTime;
            CoastTime = BrakeTime = 0;
            CoastAt = FlipAt = dist;
            Eta = BurnTime;
            return;
        }
        double vp = Math.Sqrt((total + u * u / (2 * a) + w * w / (2 * b)) / (1 / (2 * a) + 1 / (2 * b)));
        if (vp <= MaxSpeed)
        {
            PeakSpeed = vp;
            BurnTime = (vp - u) / a + turnTime;
            CoastTime = 0;
            BrakeTime = (vp - w) / b;
            CoastAt = FlipAt = Math.Max(0, (vp * vp - u * u) / (2 * a) - extra);
        }
        else
        {
            vp = MaxSpeed;
            PeakSpeed = vp;
            double accD = (vp * vp - u * u) / (2 * a), brkD = (vp * vp - w * w) / (2 * b);
            BurnTime = (vp - u) / a + turnTime;
            BrakeTime = (vp - w) / b;
            CoastTime = (total - accD - brkD) / vp;
            CoastAt = Math.Max(0, accD - extra);
            FlipAt = Math.Max(CoastAt, dist - brkD);
        }
        Eta = BurnTime + CoastTime + BrakeTime;
    }

    /// <summary>
    /// A curved leg's speed limit: the slowest bend the plain burn plan would take faster than it can turn
    /// (<see cref="RouteCurve.CornerSpeed"/>). The plan then coasts at that speed (the autopilot itself only slows for
    /// the bend it's at, so it's usually a little quicker).
    /// </summary>
    private void LimitCorners()
    {
        double a = MaxAccel, b = B, u = Math.Max(0, ClosingSpeed), L = Distance, lim = double.PositiveInfinity;
        for (int i = 1; i + 1 < Curve.Count; i++)
        {
            double vc = RouteCurve.CornerSpeed(RouteCurve.Radius(Curve[i - 1], Curve[i], Curve[i + 1]), a);
            double vPlan = Math.Min(Math.Sqrt(u * u + 2 * a * cum[i]), Math.Sqrt(EndSpeed * EndSpeed + 2 * b * (L - cum[i])));
            if (vPlan > vc) lim = Math.Min(lim, vc);
        }
        CornerSpeed = lim;
        if (lim < MaxSpeed) MaxSpeed = Math.Max(lim, 1);
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
        foreach (var w in SystemData.RouteWorlds)
        {
            if (w.IsObstacle)
            {
                // Sol's no-fly sphere: a warning when the line goes into it (not from or to inside it)
                double cz = Nearest(w.Pos, out var az);
                if (cz < w.Radius && V3.Distance(Start, w.Pos) > w.Radius && V3.Distance(Target, w.Pos) > w.Radius)
                    Hazards.Add(new Hazard { World = w, Clearance = cz - w.Radius, At = az, Blocked = true, Closest = PointAt(az) });
                continue;
            }
            if (DetourPlanner.IsCore(w, Start) || DetourPlanner.IsCore(w, Target)) continue; // flying to / from the world itself
            double c = Nearest(w.Pos, out var at);
            double clear = c - w.Radius;
            double low = Math.Min(V3.Distance(Start, w.Pos), V3.Distance(Target, w.Pos));
            bool blocked = c < Math.Min(w.Radius, low * 0.99); // through it (not just down to a point on its ground)
            double floor = DetourPlanner.Floor(w, Start, Target, Clearance);
            if (!blocked && (double.IsNaN(floor) || c >= floor)) continue;
            Hazards.Add(new Hazard { World = w, Clearance = clear, At = at, Blocked = blocked, Closest = PointAt(at) });
        }
        Hazards.Sort((x, y) => x.At.CompareTo(y.At));
    }

    /// <summary>The time from now to reach a distance along the leg, by the burn plan (the inverse of <see cref="AlongAt"/>).</summary>
    public double TimeAt(double along)
    {
        if (double.IsNaN(Eta) || Eta <= 0) return double.NaN;
        if (along <= 0) return 0;
        double lo = 0, hi = Eta;
        for (int i = 0; i < 40; i++) { double m = (lo + hi) / 2; if (AlongAt(m) < along) lo = m; else hi = m; }
        return hi;
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
