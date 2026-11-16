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
        List<V3> output, double eta = 0, int maxSteps = 1500, List<byte> phases = null)
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
                double pass = CornerSpeed(prev, goal, next, accel, maxSpeed);
                double s = vel.Length, sinHalf = SinHalfTurn(prev, goal, next);
                double lead = Math.Max(arrive, s * s * sinHalf / accel);
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

    /// <summary>sin(half the turn) at corner b of a → b → c: 0 straight on, 1 for a U-turn.</summary>
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
