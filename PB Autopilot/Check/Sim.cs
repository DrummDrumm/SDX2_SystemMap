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

        /// <summary>Game ticks between PB runs: 10 is Update10 (the script). SIM_RUN_TICKS=60 tries once a second.</summary>
        static readonly int RunTicks = int.Parse(Environment.GetEnvironmentVariable("SIM_RUN_TICKS") ?? "10");

        class Result { public bool Arrived; public double Time, FinalDist, FinalSpeed, MaxSpeed, ClosestTo, MaxBurn, MaxBrake; public Vector3D End; }

        static Nav.Wp W(Vector3D p, double arrive, string kind) { return new Nav.Wp { Pos = p, Arrive = arrive, Kind = kind, Name = kind }; }

        static Result Fly(List<Nav.Wp> route, Vector3D start, Vector3D startDrive, double aMax, double shipTurnRate, Vector3D g, double limit, Vector3D watch, double burnCap = 1, double brakeCap = 1)
        {
            var s = new Nav.Settings();
            var est = new Nav.VelocityEstimator();
            Vector3D p = start, v = Vector3D.Zero, drive = Vector3D.Normalize(startDrive), w = Vector3D.Zero;
            double dt = RunTicks / 60.0; const double alpha = 0.6; // gyro angular acceleration, rad/s²
            int idx = 0; double t = 0;
            var r = new Result { ClosestTo = double.MaxValue };
            Nav.Cmd cmd = new Nav.Cmd();
            while (t < limit)
            {
                var phys = v.Length() > 100 ? Vector3D.Normalize(v) * 100 : v;
                var vEst = est.Update(p, phys, t);
                if (cmd.W.Length() >= s.TurnRate * 0.99) s.Observed = Math.Max(s.Observed, w.Length());
                cmd = Nav.Steer(p, vEst, g, drive, aMax, burnCap, brakeCap, route, idx, 50000 * 0.98, s); cmd = Written(cmd);
                idx = cmd.Idx;
                if (cmd.Arrived) { r.Arrived = true; break; }
                // gyros: reach the wanted rate at a limited angular acceleration, never past the ship's own limit
                var want = cmd.W;
                if (want.Length() > shipTurnRate) want = Vector3D.Normalize(want) * shipTurnRate;
                if (cmd.Throttle > 0.001)
                {
                    if (Vector3D.Dot(drive, v) < 0) r.MaxBrake = Math.Max(r.MaxBrake, cmd.Throttle);
                    else r.MaxBurn = Math.Max(r.MaxBurn, cmd.Throttle);
                }
                for (int k = 0; k < RunTicks; k++) // physics ticks between PB runs
                {
                    double h = 1.0 / 60;
                    var dw = want - w;
                    if (dw.Length() > alpha * h) dw = Vector3D.Normalize(dw) * alpha * h;
                    w += dw;
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

        class TurnResult { public bool Arrived, Reversed; public double Time, AlignedAt = -1, MaxAngleAfter, FinalSpeed, Wobble; }

        /// <summary>A hotbar turn (Nav.Face): the ship moves at v0 with its drives' push along startDrive, no gravity.</summary>
        static TurnResult FlyTurn(string mode, Vector3D v0, Vector3D startDrive, double aMax, double shipTurnRate, double limit, Vector3D g = default(Vector3D))
        {
            var s = new Nav.Settings();
            Vector3D v = v0, drive = Vector3D.Normalize(startDrive), w = Vector3D.Zero;
            double dt = RunTicks / 60.0, t = 0, lastAngle = 0; const double alpha = 0.6;
            var r = new TurnResult();
            var dir0 = Vector3D.Normalize(v0);
            while (t < limit)
            {
                var cmd = Written(Nav.Face(mode, v, g, drive, aMax, s));
                if (cmd.Arrived) { r.Arrived = true; break; }
                var want = cmd.W;
                if (want.Length() > shipTurnRate) want = Vector3D.Normalize(want) * shipTurnRate;
                for (int k = 0; k < RunTicks; k++)
                {
                    double h = 1.0 / 60;
                    var dw = want - w;
                    if (dw.Length() > alpha * h) dw = Vector3D.Normalize(dw) * alpha * h;
                    w += dw;
                    if (w.LengthSquared() > 0) drive = Vector3D.Normalize(drive + Vector3D.Cross(w, drive) * h);
                    v += (drive * cmd.Throttle * aMax + g) * h;
                }
                t += dt;
                if (Vector3D.Dot(v, dir0) < -0.5) r.Reversed = true;
                if (v.Length() > 1e-6)
                {
                    var target = mode == "prograde" ? Vector3D.Normalize(v) : mode == "brake" && g.LengthSquared() > 0 ? Vector3D.Normalize(-v - g) : -Vector3D.Normalize(v);
                    double angle = Math.Acos(MathHelper.Clamp(Vector3D.Dot(drive, target), -1, 1)) * 180 / Math.PI;
                    if (r.AlignedAt < 0 && angle < 2) r.AlignedAt = t;
                    if (r.AlignedAt >= 0)
                    {
                        r.MaxAngleAfter = Math.Max(r.MaxAngleAfter, angle);
                        if (t - r.AlignedAt > 5) r.Wobble = Math.Max(r.Wobble, Math.Abs(angle - lastAngle)); // settled: degrees per run
                    }
                    lastAngle = angle;
                }
            }
            r.Time = t; r.FinalSpeed = v.Length();
            return r;
        }

        static void ReportTurn(string name, TurnResult r)
        {
            Console.WriteLine(string.Format("{0}: {1} after {2:0} s, lined up (2 deg) at {3:0.0} s, worst {4:0.0} deg after, end speed {5:0.0} m/s{6}",
                name, r.Arrived ? "done" : "holding", r.Time, r.AlignedAt, r.MaxAngleAfter, r.FinalSpeed, r.Reversed ? ", REVERSED" : ""));
        }

        static void Report(string name, Result r)
        {
            Console.WriteLine(string.Format("{0}: {1} after {2:0} s, {3:0} m from the stop at {4:0.0} m/s, top speed {5:0} m/s",
                name, r.Arrived ? "arrived" : "NOT arrived", r.Time, r.FinalDist, r.FinalSpeed, r.MaxSpeed));
        }

        /// <summary>The command as the script writes it to the blocks (rounded; per world axis here, per gyro axis in game).</summary>
        static Nav.Cmd Written(Nav.Cmd c)
        {
            c.Throttle = Nav.Throttle(c.Throttle);
            c.W = new Vector3D(Nav.Rate(c.W.X), Nav.Rate(c.W.Y), Nav.Rate(c.W.Z));
            return c;
        }

        /// <summary>The script's types derive from the game's, so load the game DLLs from Bin64 (SE_BIN64 or Steam's default).</summary>
        public static int Main(string[] args)
        {
            var bin64 = Environment.GetEnvironmentVariable("SE_BIN64") ?? @"C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers\Bin64";
            AppDomain.CurrentDomain.AssemblyResolve += (o, e) =>
            {
                var f = System.IO.Path.Combine(bin64, new System.Reflection.AssemblyName(e.Name).Name + ".dll");
                return System.IO.File.Exists(f) ? System.Reflection.Assembly.LoadFrom(f) : null;
            };
            if (args.Length > 0 && args[0] == "load")
                return Load(args.Length > 1 ? int.Parse(args[1]) : 30, args.Length > 2 ? double.Parse(args[2]) : 1800, args.Length > 3 ? int.Parse(args[3]) : 1);
            return Run();
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static int Load(int players, double seconds, int seed) { return LoadSim.Run(players, seconds, seed); }

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
            Check(r.Arrived && r.FinalDist < 500 && r.MaxBurn <= 0.3 + 1e-6 && r.MaxBrake > 0.5, "split caps: burns at most 30%, brakes harder, stops (burn " + r.MaxBurn.ToString("0.00") + ", brake " + r.MaxBrake.ToString("0.00") + ")");
            r = Fly(route, Vector3D.Zero, new Vector3D(1, 0, 0), 10, 0.6, Vector3D.Zero, 6000, none, 1, 0.25);
            Report("burn 100% / brake 25%", r);
            Check(r.Arrived && r.FinalDist < 500 && r.MaxBrake <= 0.25 + 1e-6 && r.MaxBurn > 0.5, "split caps: brakes at most 25%, flips early enough to stop (burn " + r.MaxBurn.ToString("0.00") + ", brake " + r.MaxBrake.ToString("0.00") + ")");

            // 9) hotbar turns: prograde / retrograde holds, brake (retrograde full stop)
            var vx = new Vector3D(3000, 0, 0);
            var tr = FlyTurn("prograde", vx, new Vector3D(0, 1, 0), 10, 0.6, 60);
            ReportTurn("prograde from 90 deg off", tr);
            Check(!tr.Arrived && tr.AlignedAt > 0 && tr.AlignedAt < 12 && tr.MaxAngleAfter < 3 && tr.Wobble < 0.5, "prograde: lines up within 12 s and holds steady");
            tr = FlyTurn("retrograde", vx, new Vector3D(1, 0, 0), 10, 0.6, 60);
            ReportTurn("retrograde from 180 deg off", tr);
            Check(!tr.Arrived && tr.AlignedAt > 0 && tr.AlignedAt < 15 && tr.MaxAngleAfter < 3 && tr.Wobble < 0.5, "retrograde: flips within 15 s and holds steady");
            tr = FlyTurn("retrograde", vx, new Vector3D(1, 0, 0), 10, 0.15, 90);
            ReportTurn("retrograde, slow-turning ship (0.15 rad/s)", tr);
            Check(!tr.Arrived && tr.AlignedAt > 0 && tr.AlignedAt < 35 && tr.MaxAngleAfter < 3, "retrograde: a slow turner still lines up and holds");
            tr = FlyTurn("brake", vx, new Vector3D(1, 0, 0), 10, 0.6, 1000);
            ReportTurn("brake from 3 km/s, facing prograde", tr);
            Check(tr.Arrived && !tr.Reversed && tr.FinalSpeed < 2 && tr.Time < 300 / 1.0 + 30, "brake: flips, stops under 2 m/s without reversing, near v/a + flip (" + tr.Time.ToString("0") + " s, ideal about 310 s)");
            tr = FlyTurn("brake", new Vector3D(50, 0, 0), new Vector3D(-1, 0, 0), 10, 0.6, 100);
            ReportTurn("brake from 50 m/s, already retrograde", tr);
            Check(tr.Arrived && !tr.Reversed && tr.FinalSpeed < 2 && tr.Time < 15, "brake: short stop from 50 m/s without reversing (" + tr.Time.ToString("0.0") + " s)");
            tr = FlyTurn("brake", new Vector3D(0, 0, 49000), new Vector3D(0, 1, 0), 25, 0.6, 3000);
            ReportTurn("brake from 49 km/s at 25 m/s2", tr);
            Check(tr.Arrived && !tr.Reversed && tr.FinalSpeed < 2, "brake: stops from Flip and Burn's top speed");
            tr = FlyTurn("brake", new Vector3D(300, 0, -100), new Vector3D(1, 0, 0), 20, 0.6, 200, new Vector3D(0, 0, -9.81));
            ReportTurn("brake in 1 g from 300 m/s sideways, 100 m/s falling", tr);
            Check(tr.Arrived && tr.FinalSpeed < 2, "brake in gravity: stops instead of settling into a fall (" + tr.Time.ToString("0.0") + " s)");
            tr = FlyTurn("prograde", new Vector3D(0.5, 0, 0), new Vector3D(0, 1, 0), 10, 0.6, 10);
            ReportTurn("prograde at 0.5 m/s", tr);
            Check(tr.Arrived && tr.Time < 0.5, "hold: under 1 m/s at the start says there's no direction to hold");

            Console.WriteLine(string.Format("{0}/{1} sim checks passed", checks - fails, checks));
            return fails == 0 ? 0 : 1;
        }

        static Result FlyMoving(List<Nav.Wp> route, Vector3D v0, Vector3D watch)
        {
            // same as Fly, but the ship starts already moving along +X at v0 and facing +X
            var s = new Nav.Settings();
            var est = new Nav.VelocityEstimator();
            Vector3D p = Vector3D.Zero, v = v0, drive = new Vector3D(1, 0, 0), w = Vector3D.Zero;
            double dt = RunTicks / 60.0; const double alpha = 0.6;
            int idx = 0; double t = 0;
            var r = new Result { ClosestTo = double.MaxValue };
            Nav.Cmd cmd = new Nav.Cmd();
            est.Update(p - v0, Vector3D.Normalize(v0) * 100, -1); // already moving: a second of history
            while (t < 4000)
            {
                var phys = v.Length() > 100 ? Vector3D.Normalize(v) * 100 : v;
                var vEst = est.Update(p, phys, t);
                cmd = Nav.Steer(p, vEst, Vector3D.Zero, drive, 10, route, idx, 49000, s); cmd = Written(cmd);
                idx = cmd.Idx;
                if (cmd.Arrived) { r.Arrived = true; break; }
                var want = cmd.W;
                for (int k = 0; k < RunTicks; k++)
                {
                    double h = 1.0 / 60;
                    var dw = want - w;
                    if (dw.Length() > alpha * h) dw = Vector3D.Normalize(dw) * alpha * h;
                    w += dw;
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
