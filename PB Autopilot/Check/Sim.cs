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

        class Result { public bool Arrived; public int Kicks, Reversals, Toggles, Flips; public double OverTime, FlipTime, MaxFace = -1, CruiseSwing, Time, FinalDist, FinalSpeed, MaxSpeed, ClosestTo, SpeedAtClosest, MaxBurn, MaxBrake; public Vector3D End; }

        /// <summary>Seconds the pilot holds the controls once the flip to brake starts (0 = never); when it began.</summary>
        static double HandFor, handFrom = -1;

        static Nav.Wp W(Vector3D p, double arrive, string kind) { return new Nav.Wp { Pos = p, Arrive = arrive, Kind = kind, Name = kind }; }

        static Result Fly(List<Nav.Wp> route, Vector3D start, Vector3D startDrive, double aMax, double shipTurnRate, Vector3D g, double limit, Vector3D watch, double burnCap = 1, double brakeCap = 1, Vector3D v0 = default(Vector3D), double alpha = 0.6, double alphaRoll = 0, double alphaYaw = 0, double sizeUp = 0, double sizeLeft = 0, double observed = 0)
        {
            var s = new Nav.Settings(); s.Observed = observed; // observed: a turn rate already seen this flight
            var est = new Nav.VelocityEstimator();
            Vector3D p = start, v = v0, drive = Vector3D.Normalize(startDrive), w = Vector3D.Zero, wLast = Vector3D.Zero;
            double dt = RunTicks / 60.0; // alpha: gyro angular acceleration, rad/s²
            // alphaRoll > 0: Flip and Burn's turn handling above 100 m/s (damping 0.5, a turn under 0.001 rad/s on every
            // world axis zeroed each tick) and the game's gyro torque, one clamp on its length shared by all axes:
            // alphaRoll about the drive axis, alpha across it
            // alphaYaw: about up (0 = alpha too); sizeUp / sizeLeft: the hull (100 m long) for the flip axis guess
            Vector3D up = Vector3D.CalculatePerpendicularVector(drive);
            if (alphaYaw <= 0) alphaYaw = alpha;
            s.SizeFwd = sizeUp > 0 ? 100 : 0; s.SizeUp = sizeUp; s.SizeLeft = sizeLeft;
            double flipStart = -1;
            bool thrOn = false, flipped = false;
            if (v0.LengthSquared() > 0) est.Update(p - v0, Vector3D.Normalize(v0) * 100, -1); // already moving: a second of history
            int idx = 0; double t = 0;
            var r = new Result { ClosestTo = double.MaxValue };
            Nav.Cmd cmd = new Nav.Cmd();
            while (t < limit)
            {
                var phys = v.Length() > 100 ? Vector3D.Normalize(v) * 100 : v;
                var vEst = est.Update(p, phys, t);
                s.PhysMax = Math.Max(s.PhysMax, phys.Length());
                if (cmd.W.Length() >= s.TurnRate * 0.99 && !s.Kicking) s.Observed = Math.Max(s.Observed, w.Length());
                s.AxisUp = up; s.AxisLeft = Vector3D.Cross(up, drive);
                s.Learn(cmd.W, w, t);
                cmd = Nav.Steer(p, vEst, g, drive, aMax, burnCap, brakeCap, route, idx, 50000 * 0.98, s);
                if (Environment.GetEnvironmentVariable("SIM_NO_KICK") == null) cmd.W = s.Unstick(cmd.W, w, drive, up);
                if (s.Kicking) r.Kicks++;
                cmd = Written(cmd);
                // a short takeover: the pilot holds the controls HandFor s from when the flip to brake starts (no turn, no burn)
                if (HandFor > 0 && v.Length() > 1000)
                {
                    double fc0 = Vector3D.Dot(drive, v) / v.Length();
                    if (handFrom < 0 && fc0 < 0.9 && cmd.W.Length() > 0.03) handFrom = t;
                    if (handFrom >= 0 && t < handFrom + HandFor) { cmd.W = Vector3D.Zero; cmd.Throttle = 0; }
                }
                idx = cmd.Idx;
                if (Environment.GetEnvironmentVariable("SIM_TRACE") != null && Vector3D.Distance(p, watch) < 40e3) Console.WriteLine("  t " + t.ToString("0") + " d " + Vector3D.Distance(p, watch).ToString("0") + " v " + v.Length().ToString("0") + " idx " + idx + " thr " + cmd.Throttle.ToString("0.00") + " drive.v " + (v.Length() > 1 ? Vector3D.Dot(drive, v) / v.Length() : 0).ToString("0.00"));
                if (Environment.GetEnvironmentVariable("SIM_LONG_TRACE") != null && (int)(t / dt) % (int)(10 / dt) == 0) Console.WriteLine("  t " + t.ToString("0") + " rem " + (cmd.Remaining / 1000).ToString("0") + " km to " + (Vector3D.Distance(p, watch) / 1000).ToString("0") + " km v " + v.Length().ToString("0") + " idx " + idx + " thr " + cmd.Throttle.ToString("0.00") + " drive.v " + (v.Length() > 1 ? Vector3D.Dot(drive, v) / v.Length() : 0).ToString("0.00"));
                if (cmd.Over > 0) r.OverTime += dt;
                if (cmd.Arrived) { r.Arrived = true; break; }
                // wobble: the turn reversing, the throttle switching off and on
                if (w.Length() > 0.03 && wLast.Length() > 0.03 && Vector3D.Dot(w, wLast) < 0) { r.Reversals++; if (Environment.GetEnvironmentVariable("SIM_REV_TRACE") != null && aMax > 300) Console.WriteLine("  rev t " + t.ToString("0") + " v " + v.Length().ToString("0") + " rem " + (cmd.Remaining / 1000).ToString("0") + " km thr " + cmd.Throttle.ToString("0.00")); }
                if (w.Length() > 0.03) wLast = w;
                if (!thrOn && cmd.Throttle > 0.3) { thrOn = true; r.Toggles++; } else if (thrOn && cmd.Throttle < 0.05) thrOn = false;
                // cruising (fast, far from the stop): how far the drives swing off the velocity, and turning to face back
                if (v.Length() > 40000 && cmd.Remaining > v.LengthSquared() / aMax + 500e3) // well before the flip to brake
                {
                    double off = Math.Acos(MathHelper.Clamp(Vector3D.Dot(drive, Vector3D.Normalize(v)), -1, 1)) * 180 / Math.PI;
                    if (t > 20) r.CruiseSwing = Math.Max(r.CruiseSwing, off);
                    if (Environment.GetEnvironmentVariable("SIM_CRUISE_TRACE") != null && aMax > 300 && t > 60 && t < 120)
                        Console.WriteLine("  t " + t.ToString("0") + " off " + off.ToString("0.0") + " w " + w.Length().ToString("0.000") + " W " + cmd.W.Length().ToString("0.000") + " thr " + cmd.Throttle.ToString("0.00") + " burn " + s.Burning);
                    if (off > 120 && !flipped) { r.Flips++; flipped = true; } else if (off < 60) flipped = false;
                }
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
                    if (alphaRoll > 0)
                    {
                        var dr = drive * Vector3D.Dot(dw, drive); var du = up * Vector3D.Dot(dw, up); var dp = dw - dr - du;
                        var tau = dr / alphaRoll + du / alphaYaw + dp / alpha; // torque to close the gap in a tick, per tick-squared
                        double n = tau.Length() / h;
                        if (n > 1) { dr /= n; du /= n; dp /= n; }
                        w += dr + du + dp;
                        if (v.Length() > 100)
                        {
                            w *= 1 - Nav.Settings.FbDamping * h;
                            if (Math.Abs(w.X) < 0.001 && Math.Abs(w.Y) < 0.001 && Math.Abs(w.Z) < 0.001) w = Vector3D.Zero;
                        }
                    }
                    else
                    {
                        if (dw.Length() > alpha * h) dw = Vector3D.Normalize(dw) * alpha * h;
                        w += dw;
                    }
                    if (w.LengthSquared() > 0)
                    {
                        var rot = MatrixD.CreateFromAxisAngle(Vector3D.Normalize(w), w.Length() * h);
                        drive = Vector3D.Normalize(Vector3D.TransformNormal(drive, rot));
                        up = Vector3D.Normalize(Vector3D.TransformNormal(up, rot));
                    }
                    v += (drive * cmd.Throttle * aMax + g) * h;
                    if (v.Length() > 50000) v = Vector3D.Normalize(v) * 50000;
                    p += v * h;
                    { double dc = Vector3D.Distance(p, watch); if (dc < r.ClosestTo) { r.ClosestTo = dc; r.SpeedAtClosest = v.Length(); } }
                }
                r.MaxSpeed = Math.Max(r.MaxSpeed, v.Length());
                if (t > 2 && v.Length() > 1) r.MaxFace = Math.Max(r.MaxFace, Vector3D.Dot(drive, v) / v.Length());
                if (v.Length() > 1000) // the flip to brake: from leaving prograde to lined up retrograde
                {
                    double fc = Vector3D.Dot(drive, v) / v.Length();
                    if (flipStart < 0 && r.FlipTime == 0 && fc < 0.9 && cmd.W.Length() > 0.03) flipStart = t;
                    if (flipStart >= 0 && fc < -0.98) { r.FlipTime = t - flipStart; flipStart = -1; }
                }
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
            Check(r.Arrived && r.ClosestTo < 2500 && r.SpeedAtClosest > 250, "detour: slows only to its turn speed at the detour point (" + r.SpeedAtClosest.ToString("0") + " m/s within " + r.ClosestTo.ToString("0") + " m), turns and arrives");

            // 3b) undocking from a base 350 m ahead (82 m across, the destination beyond it): the plugin's detours lead
            //     straight out 2 km, then round. The ship, at rest, must fly the first one, not count it as gone by.
            var baseC = new Vector3D(346, 0, 0);
            var outPt = baseC + new Vector3D(-2394, 0, 0);
            var d2 = baseC + Vector3D.Normalize(new Vector3D(-0.3, 1.1, 0)) * 2400;
            var d3 = baseC + Vector3D.Normalize(new Vector3D(0.8, 0.9, 0)) * 2400;
            var dest = new Vector3D(400e3, 0, 0);
            Func<Vector3D, Vector3D, Vector3D, double> arr = (a, b, c) => Math.Max(100, Math.Min(2000, 0.25 * Math.Min(Vector3D.Distance(a, b), Vector3D.Distance(b, c))));
            route = new List<Nav.Wp> { W(outPt, arr(Vector3D.Zero, outPt, d2), "detour"), W(d2, arr(outPt, d2, d3), "detour"), W(d3, arr(d2, d3, dest), "detour"), W(dest, 500, "target") };
            r = Fly(route, Vector3D.Zero, new Vector3D(1, 0, 0), 10, 0.6, Vector3D.Zero, 3000, baseC);
            Report("undock from a base", r);
            Check(r.Arrived && r.ClosestTo > 300, "undock: flies out first and round the base, never within 300 m of its centre (closest " + r.ClosestTo.ToString("0") + " m)");
            route = new List<Nav.Wp> { W(outPt, 2000, "detour"), W(d2, 2000, "detour"), W(d3, 2000, "detour"), W(dest, 500, "target") };
            var c0 = Nav.Steer(Vector3D.Zero, Vector3D.Zero, Vector3D.Zero, new Vector3D(1, 0, 0), 10, route, 0, 49000, new Nav.Settings());
            Check(c0.Idx == 0, "undock: at rest by the base, the first detour isn't skipped as gone by (2 km reach radius)");

            // 3c) a parabolic leg: 2,000 km with its middle bent 300 km sideways, sent as "curve" points like the plugin's
            //     (a quadratic Bezier through the bend): flown without stopping at the bend, ending at the target
            {
                Vector3D a0 = Vector3D.Zero, b0 = new Vector3D(2000e3, 0, 0), bend = new Vector3D(1000e3, 300e3, 0);
                var ctl = 2 * bend - (a0 + b0) / 2;
                route = new List<Nav.Wp>();
                for (int i = 1; i < 40; i++) { double u = i / 40.0; if (u > 0.93) break; route.Add(W((1 - u) * (1 - u) * a0 + 2 * u * (1 - u) * ctl + u * u * b0, 500, "curve")); }
                route.Add(W(b0, 500, "target"));
                r = Fly(route, a0, Vector3D.Normalize(ctl - a0), 10, 0.6, Vector3D.Zero, 6000, bend);
                Report("parabolic leg", r);
                var fb = Fly(new List<Nav.Wp> { W(bend, 2000, "detour"), W(b0, 500, "target") }, a0, Vector3D.Normalize(bend), 10, 0.6, Vector3D.Zero, 6000, bend);
                Report("same bend, flip and burn", fb);
                Check(r.Arrived && r.FinalDist < 500 && r.ClosestTo < 15e3 && r.SpeedAtClosest > 1000,
                    "parabolic: curves past the bend (within " + (r.ClosestTo / 1000).ToString("0.0") + " km) at speed (" + r.SpeedAtClosest.ToString("0") + " m/s, no stop) and stops at the target");
                Check(r.Time < fb.Time * 0.9, "parabolic: quicker than stopping at the bend (" + r.Time.ToString("0") + " s vs " + fb.Time.ToString("0") + " s)");
            }

            // 3d) running in far too fast on a curve (the recording near Saturn): 15 km/s into a 3,000 km radius bend
            //     of curve points every 20 km, weak RCS gyros (0.1 rad/s²). Brakes onto the curve without wobbling.
            {
                double R = 3000e3; var c = new Vector3D(0, R, 0);
                route = new List<Nav.Wp>();
                for (int i = 1; i <= 40; i++) { double th = i * 20e3 / R; route.Add(W(c + new Vector3D(Math.Sin(th), -Math.Cos(th), 0) * R, 500, "curve")); }
                double te = 41 * 20e3 / R;
                route.Add(W(c + new Vector3D(Math.Sin(te), -Math.Cos(te), 0) * R + new Vector3D(Math.Cos(te), Math.Sin(te), 0) * 600e3, 500, "target"));
                foreach (var alphaG in new[] { 0.6, 0.1, 0.04 })
                {
                    r = Fly(route, Vector3D.Zero, new Vector3D(1, 0.1, 0), 10, 0.6, Vector3D.Zero, 6000, route[20].Pos, 1, 1, new Vector3D(15000, 0, 0), alphaG);
                    Report("15 km/s into a curve, gyros " + alphaG + " rad/s2", r);
                    Console.WriteLine("    turn reversals " + r.Reversals + ", throttle restarts " + r.Toggles);
                    Check(r.Arrived && r.Reversals < 40 && r.Toggles < 15, "overspeed curve (" + alphaG + " rad/s2): arrives without wobbling (" + r.Reversals + " turn reversals, " + r.Toggles + " throttle restarts)");
                }
            }

            // 3e) cruising at the cap on a nearly straight parabolic route (the recording to Pallas): 27 curve points
            //     over 18,800 km, bent 300 km, 35 g drives, 49 km/s already. Holds its attitude: no flips, no wobble.
            {
                Vector3D a0 = Vector3D.Zero, b0 = new Vector3D(18800e3, 0, 0), bend = new Vector3D(9400e3, 300e3, 0);
                var ctl = 2 * bend - (a0 + b0) / 2;
                route = new List<Nav.Wp>();
                for (int i = 1; i < 28; i++) { double u = i / 28.0; route.Add(W((1 - u) * (1 - u) * a0 + 2 * u * (1 - u) * ctl + u * u * b0, 500, "curve")); }
                route.Add(W(b0, 500, "target"));
                var v0 = Vector3D.Normalize(ctl - a0) * 49000;
                foreach (var alphaG in new[] { 0.6, 0.15, 0.04 })
                {
                    r = Fly(route, a0, Vector3D.Normalize(ctl - a0), 350, 0.6, Vector3D.Zero, 2000, b0, 1, 1, v0, alphaG);
                    Report("cruise at the cap, gyros " + alphaG + " rad/s2", r);
                    Console.WriteLine("    turn reversals " + r.Reversals + ", throttle restarts " + r.Toggles + ", cruise flips " + r.Flips + ", worst cruise swing " + r.CruiseSwing.ToString("0") + " deg");
                    Check(r.Arrived && r.FinalDist < 500 && r.Flips == 0 && r.Reversals < 20 && r.Toggles < 25 && r.CruiseSwing < 100,
                        "cruise at the cap (" + alphaG + " rad/s2): steady (" + r.Reversals + " reversals, " + r.Toggles + " restarts, " + r.Flips + " flips, swing " + r.CruiseSwing.ToString("0") + " deg)");
                }
            }

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
            // engaged too fast for the capped brake (2026-11-19: 24.7 km/s, 2,700 km from Deimos, brake cap 54%, 230 km past)
            route = new List<Nav.Wp> { W(new Vector3D(2700e3, 0, 0), 500, "target") };
            r = Fly(route, Vector3D.Zero, new Vector3D(1, 0, 0), 171, 0.6, Vector3D.Zero, 3000, none, 0.539, 0.539, new Vector3D(24700, 0, 0));
            Report("engaged at 24.7 km/s, brake cap 54%", r);
            Check(r.Arrived && r.FinalDist < 500 && r.MaxBrake > 0.6 && r.MaxBrake < 0.95 && r.OverTime > 5, "engaged too fast for the brake cap: brakes above it, only as hard as needed, and stops (brake " + r.MaxBrake.ToString("0.00") + ", " + r.OverTime.ToString("0") + " s over the cap)");
            // too fast even for the whole thrust: all of it
            route = new List<Nav.Wp> { W(new Vector3D(1000e3, 0, 0), 500, "target") };
            r = Fly(route, Vector3D.Zero, new Vector3D(1, 0, 0), 171, 0.6, Vector3D.Zero, 200, none, 0.539, 0.539, new Vector3D(24700, 0, 0));
            Check(r.MaxBrake > 0.98, "too fast for any brake: brakes at 100% (brake " + r.MaxBrake.ToString("0.00") + ")");

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

            // 10) the overshoot of 2026-11-18: Naust Station to Andromeda Station (5,895 km), 138.7 m/s², a curve point 8 km short
            {
                var naust = new Vector3D(-3639808.44, 0, -1284705.62);
                var andro = new Vector3D(0, 0, 0);
                route = new List<Nav.Wp> { W(new Vector3D(0, 0, 0), 2000, "curve"), W(andro, 500, "target") };
                foreach (var turn in new[] { 0.6, 0.15, 0.05 })
                {
                    r = Fly(route, naust, andro - naust, 138.7, turn, Vector3D.Zero, 1500, andro);
                    Report("Naust to Andromeda at 138.7 m/s2, turns " + turn + " rad/s", r);
                    Check(r.Arrived && r.FinalDist < 500, "long fast leg with a curve point: stops at the station (" + (r.FinalDist / 1000).ToString("0") + " km off, closest " + (r.ClosestTo / 1000).ToString("0") + " km at " + r.SpeedAtClosest.ToString("0") + " m/s)");
                }
                // the 2026-11-18 15:34 flight: the Iris hauler's gyros (0.047 rad/s² across, ~0.3 about its length)
                // under Flip and Burn's clamp at 26 km/s: asked to flip, it sat still a minute and overshot 1,300 km
                // pitch 0.047, yaw 0.062: the hull guess right (taller than wide: yaw faster), wrong, and none
                foreach (var hull in new[] { new[] { 40.0, 20.0 }, new[] { 20.0, 40.0 }, new[] { 0.0, 0.0 } })
                {
                    r = Fly(route, naust, andro - naust, 138.7, 0.6, Vector3D.Zero, 1500, andro, 1, 1, default(Vector3D), 0.047, 0.3, 0.062, hull[0], hull[1]);
                    string name = hull[0] > hull[1] ? "hull says yaw" : hull[0] < hull[1] ? "hull says pitch" : "no hull guess";
                    Report("Naust to Andromeda, hauler under Flip and Burn, " + name + ", flip " + r.FlipTime.ToString("0.0") + " s", r);
                    Check(r.Arrived && r.FinalDist < 500 && r.ClosestTo < 1000, "hauler under Flip and Burn's turn clamp (" + name + "): flips and stops at the station (" + (r.FinalDist / 1000).ToString("0") + " km off, closest " + (r.ClosestTo / 1000).ToString("0") + " km at " + r.SpeedAtClosest.ToString("0") + " m/s, " + r.Kicks + " kick runs)");
                    if (hull[0] > hull[1]) Check(r.FlipTime > 0 && r.FlipTime < 30, "hauler flips about its faster axis (" + r.FlipTime.ToString("0.0") + " s)");
                }
            }

            // 12) the 2026-11-19 07:50 flight to 2 Pallas Station: the route was changed mid-flight at 24 km/s, so much of
            // the velocity was across the new line. Thrust went on cancelling that while the stop was planned as if all
            // of it could brake along the line: it braked about 1.5 km/s short and stopped 94 km past the station
            {
                var pallas = new Vector3D(0, 0, 0);
                var path = new[] { new Vector3D(11342873.83, -157987.76, 13924813.76), new Vector3D(10341877, -179461.21, 12429349.29),
                    new Vector3D(9228868.07, -200884.41, 10762795.62), new Vector3D(8123860.01, -222311.2, 9108462.6),
                    new Vector3D(7146865.78, -243795.42, 7649660.11), new Vector3D(6672041.27, -256735.93, 6944490.18),
                    new Vector3D(6417898.34, -265390.93, 6569698.02), new Vector3D(6222641.78, -274072.35, 6284849.9) };
                route = new List<Nav.Wp>();
                foreach (var q in path) route.Add(W(q, 2000, "curve"));
                route.Add(W(pallas, 500, "target"));
                var full = route;
                var line = Vector3D.Normalize(path[1] - path[0]);
                var side = Vector3D.Normalize(Vector3D.Cross(line, Vector3D.Up));
                foreach (var c in new[] { new[] { 20.0, 0 }, new[] { 40.0, 0 }, new[] { 60.0, 2 }, new[] { 75.0, 3 }, new[] { 40.0, 4 }, new[] { 60.0, 4 } })
                {
                    double off = c[0], rad = off * Math.PI / 180;
                    var v0 = (line * Math.Cos(rad) + side * Math.Sin(rad)) * 24000;
                    var start = path[(int)c[1]] - v0 * 2; // a little before that point, already moving
                    route = full.GetRange((int)c[1], full.Count - (int)c[1]);
                    r = Fly(route, start, v0, 161.58, 0.6, Vector3D.Zero, 1500, pallas, 1, 1, v0, 0.047, 0.3, 0.062, 40, 20);
                    Report("Pallas, route changed at 24 km/s, " + off + " deg off the line at point " + (c[1] + 1) + " (hauler)", r);
                    Check(r.Arrived && r.FinalDist < 500 && r.ClosestTo < 3000, "course change at speed, then a long stop: no overshoot (" + (r.FinalDist / 1000).ToString("0") + " km off, closest " + (r.ClosestTo / 1000).ToString("0") + " km at " + r.SpeedAtClosest.ToString("0") + " m/s, top " + r.MaxSpeed.ToString("0") + " m/s)");
                }
            }

            // 13) a short takeover right as the flip to brake starts (under TakeoverAfter, so the autopilot takes the
            // ship back): the reserve held before each flip covers it, with no overshoot
            {
                var naust = new Vector3D(-3639808.44, 0, -1284705.62);
                var andro = new Vector3D(0, 0, 0);
                route = new List<Nav.Wp> { W(new Vector3D(0, 0, 0), 2000, "curve"), W(andro, 500, "target") };
                HandFor = 5; handFrom = -1;
                r = Fly(route, naust, andro - naust, 138.7, 0.6, Vector3D.Zero, 1500, andro, 1, 1, default(Vector3D), 0.047, 0.3, 0.062, 40, 20);
                HandFor = 0; handFrom = -1;
                Report("Naust to Andromeda, hauler, the pilot holds the controls 5 s as the flip starts", r);
                Check(r.Arrived && r.FinalDist < 500 && r.ClosestTo < 1000 && r.MaxBrake < 0.999, "5 s takeover at the flip: still stops at the station, with thrust to spare (" + (r.ClosestTo / 1000).ToString("0") + " km closest, braking at " + (r.MaxBrake * 100).ToString("0") + "% at most)");
            }

            // 11) Andromeda, 2026-11-18 16:14: braking, Flip and Burn drops the speed to 90 m/s a few km short; it
            // turned round to speed up and back again to brake (a minute lost for a few m/s). Near: coast on. Far: burn.
            {
                var stop = new Vector3D(5000, 0, 0);
                route = new List<Nav.Wp> { W(stop, 500, "target") };
                r = Fly(route, Vector3D.Zero, new Vector3D(-1, 0, 0), 138.7, 0.6, Vector3D.Zero, 600, stop, 1, 1, new Vector3D(90, 0, 0));
                Report("90 m/s facing back, 5 km short", r);
                Check(r.Arrived && r.MaxFace < 0, "near a stop and slow, facing back: coasts and brakes, no turn round (drives faced " + r.MaxFace.ToString("+0.00;-0.00") + " along the travel at most, " + r.Time.ToString("0") + " s)");
                // 17:25 at CE.Aventine: 86 m/s facing back nearer 8 km out, top speed 90: coast, don't turn for 4 m/s
                stop = new Vector3D(7500, 0, 0);
                route = new List<Nav.Wp> { W(stop, 500, "target") };
                r = Fly(route, Vector3D.Zero, new Vector3D(-1, 0, 0), 138.7, 0.6, Vector3D.Zero, 600, stop, 1, 1, new Vector3D(86, 0, 0));
                Report("86 m/s facing back, 7.5 km short", r);
                Check(r.Arrived && r.MaxFace < 0, "near a station at the game's top speed, facing back: no turn round (drives faced " + r.MaxFace.ToString("+0.00;-0.00") + " along the travel at most)");
                // learning: Flip and Burn zeroing a turn isn't the gyros' speed
                var ls = new Nav.Settings();
                ls.Learn(Vector3D.Zero, Vector3D.Zero, 0);
                ls.Learn(new Vector3D(0, 0.3, 0), new Vector3D(0, 0.012, 0), 1 / 6.0);    // 0.07 rad/s2
                ls.Learn(new Vector3D(0, 0.3, 0), new Vector3D(0, 0.08, 0), 2 / 6.0);     // 0.41: a glitch
                ls.Learn(new Vector3D(0, -0.3, 0), new Vector3D(0, 0, 0), 3 / 6.0);       // zeroed: not toward the ask
                Check(ls.Accel > 0.05 && ls.Accel < 0.25, "turn learning: a glitch and a zeroed turn don't take over (" + ls.Accel.ToString("0.000") + " rad/s2)");
                stop = new Vector3D(200000, 0, 0);
                route = new List<Nav.Wp> { W(stop, 500, "target") };
                r = Fly(route, Vector3D.Zero, new Vector3D(-1, 0, 0), 138.7, 0.6, Vector3D.Zero, 900, stop, 1, 1, new Vector3D(90, 0, 0));
                Report("90 m/s facing back, 200 km short", r);
                Check(r.Arrived && r.MaxFace > 0.9 && r.MaxSpeed > 1000, "far from the stop and slow, facing back: turns round and speeds up (top " + r.MaxSpeed.ToString("0") + " m/s)");
            }

            // 12) Hygeia, 2026-11-18 17:50: flipped and braking 110 km out at 894 m/s, a slow-turning hauler. The plan
            // kept a whole flip in hand after the flip, so it held the speed near distance / flip time and crept in
            // (894 to 79 m/s in 5 minutes, still km short). Facing back: coast on and brake at the end.
            {
                var stop = new Vector3D(110000, 0, 0);
                route = new List<Nav.Wp> { W(stop, 500, "target") };
                r = Fly(route, Vector3D.Zero, new Vector3D(-1, 0, 0), 148.4, 0.03, Vector3D.Zero, 900, stop, 1, 1, new Vector3D(894, 0, 0), 0.6, 0, 0, 0, 0, 0.03);
                Report("894 m/s facing back, 110 km short, slow turner", r);
                Check(r.Arrived && r.Time < 250 && r.MaxFace < 0, "already flipped: no creeping in, brakes at the end (" + r.Time.ToString("0") + " s, drives faced " + r.MaxFace.ToString("+0.00;-0.00") + " at most)");
            }

            // 13) Andromeda, 2026-11-18 19:15: lined up and braking, a small aim correction (0.03 rad/s asked) that Flip
            // and Burn zeroes: no kicks (they rolled the ship every few seconds). A real turn still kicks.
            {
                var ks = new Nav.Settings();
                Vector3D roll = new Vector3D(1, 0, 0), kup = new Vector3D(0, 1, 0), small = new Vector3D(0, 0.03, 0), big = new Vector3D(0, 0.6, 0);
                bool kicked = false;
                for (int i = 0; i < 10; i++) { ks.Unstick(small, Vector3D.Zero, roll, kup); kicked |= ks.Kicking; }
                Check(!kicked, "small aim correction the ship ignores: no kick");
                for (int i = 0; i < 10; i++) { ks.Unstick(big, Vector3D.Zero, roll, kup); kicked |= ks.Kicking; }
                Check(kicked, "real turn the ship ignores: kicks");
            }

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
                if (cmd.Over > 0) r.OverTime += dt;
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
                    { double dc = Vector3D.Distance(p, watch); if (dc < r.ClosestTo) { r.ClosestTo = dc; r.SpeedAtClosest = v.Length(); } }
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
