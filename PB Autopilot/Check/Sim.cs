using System;
using System.Collections.Generic;
using VRageMath;
using Nav = PbCheck.Program.Nav;

namespace PbCheck
{
    /// <summary>
    /// Point-mass flight sims of the autopilot's guidance (Program.Nav), run at the PB's Update10 rate: one drive
    /// direction that has to be turned (with a turn-rate limit and angular acceleration, like gyros), Flip and Burn's
    /// 50 km/s cap, and physics velocity capped at 100 m/s so the position-based speed estimate is what's used.
    /// </summary>
    public static class Sim
    {
        static int fails, checks;

        static void Check(bool ok, string what)
        {
            checks++;
            if (!ok) fails++;
            Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + what);
        }

        class Result { public bool Arrived; public double Time, FinalDist, FinalSpeed, MaxSpeed, ClosestTo, MaxBurn, MaxBrake; public Vector3D End; }

        static Nav.Wp W(Vector3D p, double arrive, string kind) { return new Nav.Wp { Pos = p, Arrive = arrive, Kind = kind, Name = kind }; }

        static Result Fly(List<Nav.Wp> route, Vector3D start, Vector3D startDrive, double aMax, double shipTurnRate, Vector3D g, double limit, Vector3D watch, double burnCap = 1, double brakeCap = 1)
        {
            var s = new Nav.Settings();
            var est = new Nav.VelocityEstimator();
            Vector3D p = start, v = Vector3D.Zero, drive = Vector3D.Normalize(startDrive), w = Vector3D.Zero;
            const double dt = 10.0 / 60.0, alpha = 0.6; // gyro angular acceleration, rad/s²
            int idx = 0; double t = 0;
            var r = new Result { ClosestTo = double.MaxValue };
            Nav.Cmd cmd = new Nav.Cmd();
            while (t < limit)
            {
                var phys = v.Length() > 100 ? Vector3D.Normalize(v) * 100 : v;
                var vEst = est.Update(p, phys, t);
                if (cmd.W.Length() >= s.TurnRate * 0.99) s.Observed = Math.Max(s.Observed, w.Length());
                cmd = Nav.Steer(p, vEst, g, drive, aMax, burnCap, brakeCap, route, idx, 50000 * 0.98, s);
                idx = cmd.Idx;
                if (cmd.Arrived) { r.Arrived = true; break; }
                // gyros: reach the wanted rate at a limited angular acceleration, never past the ship's own limit
                var want = cmd.W;
                if (want.Length() > shipTurnRate) want = Vector3D.Normalize(want) * shipTurnRate;
                var dw = want - w;
                if (dw.Length() > alpha * dt) dw = Vector3D.Normalize(dw) * alpha * dt;
                w += dw;
                if (cmd.Throttle > 0.001)
                {
                    if (Vector3D.Dot(drive, v) < 0) r.MaxBrake = Math.Max(r.MaxBrake, cmd.Throttle);
                    else r.MaxBurn = Math.Max(r.MaxBurn, cmd.Throttle);
                }
                for (int k = 0; k < 10; k++) // 10 physics ticks per PB run
                {
                    double h = dt / 10;
                    if (w.LengthSquared() > 0) drive = Vector3D.Normalize(drive + Vector3D.Cross(w, drive) * h);
                    v += (drive * cmd.Throttle * aMax + g) * h;
                    if (v.Length() > 50000) v = Vector3D.Normalize(v) * 50000;
                    p += v * h;
                    r.ClosestTo = Math.Min(r.ClosestTo, Vector3D.Distance(p, watch));
                }
                r.MaxSpeed = Math.Max(r.MaxSpeed, v.Length());
                t += dt;
            }
            r.Time = t; r.End = p;
            r.FinalDist = Vector3D.Distance(p, route[route.Count - 1].Pos);
            r.FinalSpeed = v.Length();
            return r;
        }

        static void Report(string name, Result r)
        {
            Console.WriteLine(string.Format("{0}: {1} after {2:0} s, {3:0} m from the stop at {4:0.0} m/s, top speed {5:0} m/s",
                name, r.Arrived ? "arrived" : "NOT arrived", r.Time, r.FinalDist, r.FinalSpeed, r.MaxSpeed));
        }

        /// <summary>The script's types derive from the game's, so load the game DLLs from Bin64 (SE_BIN64 or Steam's default).</summary>
        public static int Main()
        {
            var bin64 = Environment.GetEnvironmentVariable("SE_BIN64") ?? @"C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers\Bin64";
            AppDomain.CurrentDomain.AssemblyResolve += (o, e) =>
            {
                var f = System.IO.Path.Combine(bin64, new System.Reflection.AssemblyName(e.Name).Name + ".dll");
                return System.IO.File.Exists(f) ? System.Reflection.Assembly.LoadFrom(f) : null;
            };
            return Run();
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static int Run()
        {
            var none = new Vector3D(1e12, 0, 0);

            // 1) 2,000 km from rest, facing the wrong way: turn, burn, flip, brake, stop
            var route = new List<Nav.Wp> { W(new Vector3D(2000e3, 0, 0), 500, "target") };
            var r = Fly(route, Vector3D.Zero, new Vector3D(0, 0, 1), 10, 0.6, Vector3D.Zero, 4000, none);
            Report("straight 2,000 km", r);
            double ideal = 2 * Math.Sqrt(2000e3 / 10);
            Check(r.Arrived && r.FinalDist < 500, "straight: stops within 500 m");
            Check(r.Time < ideal * 1.6, "straight: within 1.6x the ideal " + ideal.ToString("0") + " s");

            // 2) long haul that hits the 50 km/s cap
            route = new List<Nav.Wp> { W(new Vector3D(0, 0, 200000e3), 500, "target") };
            r = Fly(route, Vector3D.Zero, new Vector3D(0, 0, 1), 20, 0.6, Vector3D.Zero, 12000, none);
            Report("200,000 km", r);
            Check(r.Arrived && r.MaxSpeed > 45000, "long haul: reaches the speed cap and stops");

            // 3) detour then target (60 degree bend): stops at the detour, turns, goes on
            var detour = new Vector3D(500e3, 0, 0);
            route = new List<Nav.Wp> { W(detour, 2000, "detour"), W(detour + new Vector3D(250e3, 433e3, 0), 500, "target") };
            r = Fly(route, Vector3D.Zero, new Vector3D(1, 0, 0), 10, 0.6, Vector3D.Zero, 6000, detour);
            Report("detour + target", r);
            Check(r.Arrived && r.ClosestTo < 2000, "detour: passes within 2 km of the detour point and arrives");

            // 4) asteroid dodge: a pass point 5 km beside a rock on the line, 600 km out, flown at speed
            var rock = new Vector3D(600e3, 0, 0);
            var pass = rock + new Vector3D(0, 5000, 0);
            route = new List<Nav.Wp> { W(pass, 1000, "pass"), W(new Vector3D(1500e3, 0, 0), 500, "target") };
            r = Fly(route, Vector3D.Zero, new Vector3D(1, 0, 0), 10, 0.6, Vector3D.Zero, 4000, rock);
            Report("asteroid dodge", r);
            Check(r.Arrived && r.ClosestTo > 3200, "dodge: keeps more than 3.2 km from the rock (5 km pass point, 3 km rock + margin) and arrives (closest " + r.ClosestTo.ToString("0") + " m)");

            // 5) a dodge found late: rock 60 km ahead while already doing 2 km/s
            route = new List<Nav.Wp> { W(new Vector3D(60e3, 5000, 0), 1000, "pass"), W(new Vector3D(400e3, 0, 0), 500, "target") };
            r = FlyMoving(route, new Vector3D(2000, 0, 0), new Vector3D(60e3, 0, 0));
            Report("late dodge at 2 km/s", r);
            Check(r.Arrived && r.ClosestTo > 2500, "late dodge: holds speed and pushes sideways (closest " + r.ClosestTo.ToString("0") + " m)");

            // 6) a ship that turns much slower than asked (weak RCS gyros): learns it and still stops
            route = new List<Nav.Wp> { W(new Vector3D(800e3, 0, 0), 500, "target") };
            r = Fly(route, Vector3D.Zero, new Vector3D(-1, 0, 0), 10, 0.12, Vector3D.Zero, 6000, none);
            Report("slow-turning ship", r);
            Check(r.Arrived && r.FinalDist < 500, "slow turner: still stops at the point");

            // 7) in gravity: climb 60 km straight up against 1 g
            var g = new Vector3D(0, -9.81, 0);
            route = new List<Nav.Wp> { W(new Vector3D(0, 60e3, 0), 500, "target") };
            r = Fly(route, Vector3D.Zero, new Vector3D(0, 1, 0), 25, 0.6, g, 3000, none);
            Report("climb in 1 g", r);
            Check(r.Arrived && r.FinalDist < 500, "gravity: climbs and holds at the point");

            // 8) separate caps: gentle burn (30%), full brake; then full burn, gentle brake (25%)
            route = new List<Nav.Wp> { W(new Vector3D(1000e3, 0, 0), 500, "target") };
            r = Fly(route, Vector3D.Zero, new Vector3D(1, 0, 0), 10, 0.6, Vector3D.Zero, 6000, none, 0.3, 1);
            Report("burn 30% / brake 100%", r);
            Check(r.Arrived && r.FinalDist < 500 && r.MaxBurn <= 0.3 + 1e-9 && r.MaxBrake > 0.5, "split caps: burns at most 30%, brakes harder, stops (burn " + r.MaxBurn.ToString("0.00") + ", brake " + r.MaxBrake.ToString("0.00") + ")");
            r = Fly(route, Vector3D.Zero, new Vector3D(1, 0, 0), 10, 0.6, Vector3D.Zero, 6000, none, 1, 0.25);
            Report("burn 100% / brake 25%", r);
            Check(r.Arrived && r.FinalDist < 500 && r.MaxBrake <= 0.25 + 1e-9 && r.MaxBurn > 0.5, "split caps: brakes at most 25%, flips early enough to stop (burn " + r.MaxBurn.ToString("0.00") + ", brake " + r.MaxBrake.ToString("0.00") + ")");

            Console.WriteLine(string.Format("{0}/{1} sim checks passed", checks - fails, checks));
            return fails == 0 ? 0 : 1;
        }

        static Result FlyMoving(List<Nav.Wp> route, Vector3D v0, Vector3D watch)
        {
            // same as Fly, but the ship starts already moving along +X at v0 and facing +X
            var s = new Nav.Settings();
            var est = new Nav.VelocityEstimator();
            Vector3D p = Vector3D.Zero, v = v0, drive = new Vector3D(1, 0, 0), w = Vector3D.Zero;
            const double dt = 10.0 / 60.0, alpha = 0.6;
            int idx = 0; double t = 0;
            var r = new Result { ClosestTo = double.MaxValue };
            Nav.Cmd cmd = new Nav.Cmd();
            est.Update(p - v0, Vector3D.Normalize(v0) * 100, -1); // already moving: a second of history
            while (t < 4000)
            {
                var phys = v.Length() > 100 ? Vector3D.Normalize(v) * 100 : v;
                var vEst = est.Update(p, phys, t);
                cmd = Nav.Steer(p, vEst, Vector3D.Zero, drive, 10, route, idx, 49000, s);
                idx = cmd.Idx;
                if (cmd.Arrived) { r.Arrived = true; break; }
                var want = cmd.W; var dw = want - w;
                if (dw.Length() > alpha * dt) dw = Vector3D.Normalize(dw) * alpha * dt;
                w += dw;
                for (int k = 0; k < 10; k++)
                {
                    double h = dt / 10;
                    if (w.LengthSquared() > 0) drive = Vector3D.Normalize(drive + Vector3D.Cross(w, drive) * h);
                    v += drive * cmd.Throttle * 10 * h;
                    p += v * h;
                    r.ClosestTo = Math.Min(r.ClosestTo, Vector3D.Distance(p, watch));
                }
                r.MaxSpeed = Math.Max(r.MaxSpeed, v.Length());
                t += dt;
            }
            r.Time = t; r.End = p;
            r.FinalDist = Vector3D.Distance(p, route[route.Count - 1].Pos);
            r.FinalSpeed = v.Length();
            return r;
        }
    }
}
