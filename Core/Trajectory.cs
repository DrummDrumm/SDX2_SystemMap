using System;
using System.Collections.Generic;

namespace SDX2_SystemMap;

/// <summary>
/// Projects where the ship will actually go from its current position and velocity as it corrects onto the route:
/// a simple steering model (aim the velocity at the next route point, turn it with the ship's acceleration, run at the
/// top speed between points, round each corner on an arc at a speed the turn allows, and brake into the destination). Drawn dashed ahead of the ship, to show the curve it
/// takes back to the plan and where it ends up. Game-free so it can be tested.
/// </summary>
public static class Trajectory
{
    public const byte Burn = 0, Coast = 1, Brake = 2;

    /// <summary>
    /// Fills <paramref name="output"/> with points from <paramref name="pos"/> through <paramref name="points"/> (the
    /// last one is the destination) until the ship stops there, or after <paramref name="maxSteps"/> steps.
    /// accel = m/s² (0 = drift on the current velocity), arrive = distance at which an intermediate point counts as passed.
    /// Returns true if it reaches the destination. <paramref name="phases"/> (optional) gets one entry per output point:
    /// what the ship does from that point on (<see cref="Burn"/>, <see cref="Coast"/>, <see cref="Brake"/>).
    /// </summary>
    public static bool Predict(V3 pos, V3 vel, IList<V3> points, double accel, double maxSpeed, double arrive,
        List<V3> output, double eta = 0, int maxSteps = 1500, List<byte> phases = null, bool stops = false)
    {
        output.Clear();
        phases?.Clear();
        output.Add(pos);
        phases?.Add(Coast);
        int cur = -1;
        void Add(V3 q) { output.Add(q); phases?.Add((byte)Math.Max(0, cur)); }
        if (points == null || points.Count == 0) return false;
        var dest = points[points.Count - 1];
        double total = 0; var from = pos;
        foreach (var q in points) { total += V3.Distance(from, q); from = q; }
        if (accel <= 1e-6)
        {
            // no thrust known: the straight drift on the current velocity, as far as the destination is away
            double s = vel.Length;
            if (s > 1) Add(pos + vel / s * total);
            return false;
        }
        if (maxSpeed <= 0) maxSpeed = RoutePlan.DefaultMaxSpeed;
        // time budget: the plan's ETA (or a rough one), with room for the correction and the extra points
        double rough = total / Math.Max(1, Math.Min(maxSpeed, Math.Sqrt(accel * total))) * 2 + vel.Length / accel * 2;
        double guess = Math.Max(eta, rough);
        double dt = Math.Max(0.25, guess * 2.5 / maxSteps);
        double brakeA = accel * 0.9; // a little margin, as the planner keeps
        int idx = 0;
        var start = pos;
        double sample = Math.Max(dt, guess / 300), since = 0;
        for (int step = 0; step < maxSteps; step++)
        {
            bool final = idx >= points.Count - 1;
            var goal = points[Math.Min(idx, points.Count - 1)];
            var to = goal - pos;
            double d = to.Length;
            double want;
            if (final) want = Math.Min(maxSpeed, Math.Sqrt(2 * brakeA * Math.Max(0, d - 20)));
            else
            {
                // a corner: slow to a speed the turn can take, and start turning early so the ship arcs round it
                // (constant thrust sideways: a parabola) instead of overshooting and doubling back
                var prev = idx == 0 ? start : points[idx - 1];
                var next = points[idx + 1];
                // flip and burn legs: no faster than the ship can turn there and stay within the point's reach radius
                double pass = CornerSpeed(prev, goal, next, accel, maxSpeed);
                if (stops) pass = Math.Min(pass, RouteCurve.TurnSpeed(prev, goal, next, accel, RouteExport.ArriveFor(prev, goal, next), maxSpeed));
                double s = vel.Length, sinHalf = SinHalfTurn(prev, goal, next);
                double lead = Math.Max(stops ? Math.Min(arrive, StopReach) : arrive, s * s * sinHalf / accel);
                if (d < lead) { idx++; continue; }
                want = Math.Min(maxSpeed, Math.Sqrt(pass * pass + 2 * brakeA * Math.Max(0, d - lead)));
            }
            if (final && d < Math.Max(50, vel.Length * dt) && vel.Length < Math.Max(5, brakeA * dt * 2)) { Add(goal); return true; }
            var desired = d > 1e-6 ? to / d * want : V3.Zero;
            // thrust goes on the sideways error first (get back on the line), then on the speed along it
            var dv = desired - vel;
            double can = accel * dt;
            // what the ship is doing now: braking (slowing along the line), burning (any real thrust) or coasting
            double alongSpeed = d > 1e-6 ? vel.Dot(to / d) : 0;
            int ph = want < alongSpeed - 1 && dv.Length > can * 0.05 ? Brake : dv.Length > can * 0.05 ? Burn : Coast;
            if (ph != cur)
            {
                if (cur < 0 && phases != null) phases[0] = (byte)ph;
                else if (cur >= 0) { cur = ph; Add(pos); since = 0; }
                cur = ph;
            }
            if (dv.Length <= can) vel = desired;
            // moving away from the point (just round a corner): thrust straight along the change needed, a parabola,
            // rather than killing the sideways speed first, which stops the ship dead and doubles it back
            else if (alongSpeed < 0) vel = vel + dv / dv.Length * can;
            else
            {
                var dir = d > 1e-6 ? to / d : V3.Zero;
                var along = dir * dv.Dot(dir);
                var side = dv - along;
                double sl = side.Length;
                if (sl >= can) vel = vel + side / sl * can;
                else
                {
                    double rest = Math.Sqrt(can * can - sl * sl), al = along.Length;
                    vel = vel + side + (al > 1e-9 ? along / al * Math.Min(rest, al) : V3.Zero);
                }
            }
            var move = vel * dt;
            // don't step through the destination
            if (final && move.Length > d) { pos = goal; vel = V3.Zero; Add(pos); return true; }
            pos = pos + move;
            since += dt;
            if (since >= sample) { Add(pos); since = 0; }
        }
        Add(pos);
        return false;
    }

    /// <summary>
    /// A curved route from where the ship is, with the velocity it already has: the arc it flies to get onto the
    /// planned curve (steering at a point a little ahead on it, as the autopilot does), then the rest of the curve as
    /// planned. Phases: the correction burns (or brakes, when it has to slow), the rest follows the plan's burn,
    /// coast and brake. False (output empty) when there's nothing to correct: the ship is near enough stopped, or
    /// already on the curve and heading along it.
    /// </summary>
    public static bool Join(V3 pos, V3 vel, RoutePlan leg, double accel, double maxSpeed, List<V3> output, List<byte> phases, int maxSteps = 1500)
    {
        output.Clear(); phases?.Clear();
        if (leg == null || leg.Distance < 1 || accel <= 1e-6) return false;
        if (maxSpeed <= 0) maxSpeed = RoutePlan.DefaultMaxSpeed;
        const int N = 256;
        var s = new V3[N + 1];
        for (int k = 0; k <= N; k++) s[k] = leg.PointAt(leg.Distance * k / N);
        double step = leg.Distance / N;
        int at = 0;
        double Near(V3 p, out double along, out V3 tan)
        {
            // the sample nearest p, searched forward from the last one (the ship moves along the curve)
            double best = double.MaxValue; int bi = at; double bt = 0;
            for (int k = Math.Max(0, at - 4); k < N && k < at + 48; k++)
            {
                double d = V3.SegmentDistance(s[k], s[k + 1], p, out var t);
                if (d < best) { best = d; bi = k; bt = t; }
            }
            at = bi;
            along = (bi + bt) * step;
            tan = (s[bi + 1] - s[bi]).Normalized;
            return best;
        }
        double speed = vel.Length;
        if (speed < 5) return false;
        // "on it": within a ship's few seconds of travel of the line, and heading along it
        double onTol = Math.Max(150, speed * 2);
        // a global look first: the ship may be well along a curve it's already flying
        {
            double best = double.MaxValue;
            for (int k = 0; k < N; k++) { double d = V3.SegmentDistance(s[k], s[k + 1], pos, out _); if (d < best) { best = d; at = k; } }
        }
        double d0 = Near(pos, out double a0, out var t0);
        if (d0 < onTol && vel.Dot(t0) > speed * Math.Cos(3 * Math.PI / 180)) return false;

        double brakeA = accel * 0.9;
        // time to swing the velocity round, the step from it
        double turn = (vel - t0 * Math.Max(speed, 1)).Length / accel;
        double dt = Math.Max(0.1, (turn * 3 + 30) / maxSteps);
        double sample = Math.Max(dt, (turn * 3 + 30) / 200), since = 0;
        int cur = -1;
        output.Add(pos); phases?.Add(Burn);
        void Add(V3 q) { output.Add(q); phases?.Add((byte)Math.Max(0, cur)); }
        for (int i = 0; i < maxSteps; i++)
        {
            double dd = Near(pos, out double along, out var tan);
            double rest = leg.Distance - along;
            speed = vel.Length;
            if (dd < onTol && vel.Dot(tan) > speed * Math.Cos(2 * Math.PI / 180) || rest < step)
            {
                // on the curve: the plan from here
                if (output.Count == 1) Add(pos);
                if (phases != null) phases[phases.Count - 1] = PlanPhase(leg, along);
                for (int k = at + 1; k <= N; k++) { output.Add(s[k]); phases?.Add(PlanPhase(leg, k * step)); }
                return true;
            }
            // aim a little ahead on the curve: far enough to swing onto it smoothly, nearer when close
            double lead = Math.Max(step * 2, Math.Min(Math.Max(dd * 1.5, speed * speed / accel * 0.5), rest));
            var aim = leg.PointAt(Math.Min(leg.Distance, along + lead));
            var to = aim - pos; double d = to.Length;
            double want = Math.Min(maxSpeed, Math.Sqrt(2 * brakeA * Math.Max(0, rest)));
            want = Math.Max(want, Math.Min(speed, maxSpeed) * 0.5); // swing round at speed, not stopping first
            var desired = d > 1e-6 ? to / d * want : V3.Zero;
            var dv = desired - vel;
            double can = accel * dt;
            double alongSpeed = d > 1e-6 ? vel.Dot(to / d) : 0;
            int ph = want < alongSpeed - 1 ? Brake : Burn;
            if (ph != cur) { if (cur >= 0) { cur = ph; Add(pos); since = 0; } cur = ph; if (output.Count == 1 && phases != null) phases[0] = (byte)ph; }
            vel = dv.Length <= can ? desired : vel + dv / dv.Length * can;
            pos = pos + vel * dt;
            since += dt;
            if (since >= sample) { Add(pos); since = 0; }
        }
        Add(pos);
        return output.Count > 1;
    }

    /// <summary>What the plan does at a point along the leg: burn, coast or brake.</summary>
    public static byte PlanPhase(RoutePlan leg, double along)
    {
        if (!leg.HasBurnPlan || leg.TooFastToStop) return Burn;
        return along < leg.CoastAt ? Burn : along < leg.FlipAt ? Coast : Brake;
    }

    /// <summary>sin(half the turn) at corner b of a → b → c: 0 straight on, 1 for a U-turn.</summary>
    /// <summary>How near a flip-and-burn stop counts as reached (the autopilot's detour reach radius).</summary>
    public const double StopReach = 2000;

    public static double SinHalfTurn(V3 a, V3 b, V3 c)
    {
        var u = (b - a).Normalized; var w = (c - b).Normalized;
        if (u.Length < 0.5 || w.Length < 0.5) return 1;
        return Math.Sqrt(Math.Max(0, (1 - u.Dot(w)) / 2));
    }

    /// <summary>
    /// The fastest the ship can pass corner b of a → b → c and finish turning within half the shorter leg: the turn
    /// changes the velocity by 2v·sin(θ/2), which takes 2v²·sin(θ/2)/accel of travel.
    /// </summary>
    public static double CornerSpeed(V3 a, V3 b, V3 c, double accel, double maxSpeed)
    {
        double sh = SinHalfTurn(a, b, c);
        if (sh < 1e-6) return maxSpeed;
        double room = Math.Min(V3.Distance(a, b), V3.Distance(b, c)) / 2;
        return Math.Min(maxSpeed, Math.Sqrt(accel * room / (2 * sh)));
    }
}
