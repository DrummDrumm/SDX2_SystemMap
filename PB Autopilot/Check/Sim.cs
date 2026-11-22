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
                var naust = new Vector3D(-3328808.44, -72000.00, -1472705.62);
                var andro = new Vector3D(2443065.43, -634043.44, -2528128.79);
                route = new List<Nav.Wp> { W(new Vector3D(2437029.92, -632964.46, -2522989.87), 2000, "curve"), W(andro, 500, "target") };
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
                var pallas = new Vector3D(5772894.75, -236290.12, 6229136.08);
                var path = new[] { new Vector3D(11078873.83, -99987.76, 14121813.76), new Vector3D(10077877.00, -121461.21, 12626349.29),
                    new Vector3D(8964868.07, -142884.41, 10959795.62), new Vector3D(7859860.01, -164311.20, 9305462.60),
                    new Vector3D(6882865.78, -185795.42, 7846660.11), new Vector3D(6408041.27, -198735.93, 7141490.18),
                    new Vector3D(6153898.34, -207390.93, 6766698.02), new Vector3D(5958641.78, -216072.35, 6481849.90) };
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
                var naust = new Vector3D(-3328808.44, -72000.00, -1472705.62);
                var andro = new Vector3D(2443065.43, -634043.44, -2528128.79);
                route = new List<Nav.Wp> { W(new Vector3D(2437029.92, -632964.46, -2522989.87), 2000, "curve"), W(andro, 500, "target") };
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

            // approach: a ship lying still 20 km off, one drifting across at 300 m/s, and one at 2 km/s (over the
            // game's own speed, so its drift comes from its position) with the lock lost half way
            {
                var a1 = FlyApproach(new Vector3D(20e3, 0, 0), Vector3D.Zero, 500, -1);
                Console.WriteLine("approach still: " + (a1.Arrived ? "arrived" : "NOT arrived") + " t=" + a1.Time.ToString("0") + " s, " + a1.FinalDist.ToString("0") + " m off, closing " + a1.FinalSpeed.ToString("0.00") + " m/s, closest " + a1.ClosestTo.ToString("0") + " m");
                Check(a1.Arrived && a1.FinalDist > 450 && a1.FinalDist < 560, "approach, ship lying still: stops 450-560 m off");
                Check(a1.ClosestTo > 440, "approach, ship lying still: never nearer than 440 m");
                var a2 = FlyApproach(new Vector3D(20e3, 5e3, 0), new Vector3D(0, 0, 300), 500, -1);
                Console.WriteLine("approach drifting 300: " + (a2.Arrived ? "arrived" : "NOT arrived") + " t=" + a2.Time.ToString("0") + " s, " + a2.FinalDist.ToString("0") + " m off, closing " + a2.FinalSpeed.ToString("0.00") + " m/s, closest " + a2.ClosestTo.ToString("0") + " m");
                Check(a2.Arrived && a2.FinalDist > 450 && a2.FinalDist < 560, "approach, ship drifting at 300 m/s: stops 450-560 m off");
                Check(a2.FinalSpeed < 1.5 && a2.ClosestTo > 440, "approach, drifting: matched within 1.5 m/s, never nearer than 440 m");
                var a3 = FlyApproach(new Vector3D(-30e3, 0, 8e3), new Vector3D(2000, 0, 0), 300, 40);
                Console.WriteLine("approach drifting 2 km/s, lock lost at 40 s: " + (a3.Arrived ? "arrived" : "NOT arrived") + " t=" + a3.Time.ToString("0") + " s, " + a3.FinalDist.ToString("0") + " m off, closing " + a3.FinalSpeed.ToString("0.00") + " m/s, closest " + a3.ClosestTo.ToString("0") + " m");
                Check(a3.Arrived && a3.FinalDist > 250 && a3.FinalDist < 400, "approach to 300 m, 2 km/s drift, lock lost: stops 250-400 m off");
                Check(a3.FinalSpeed < 1.5 && a3.ClosestTo > 240, "approach, lock lost: matched within 1.5 m/s, never nearer than 240 m");
            }

            // match. An hour from 20 km behind a ship drifting at 300 m/s, from 2 km (inside the 3.5 km floor)
            // beside one at 2 km/s, and from 8 km with the ship burning at 2 m/s2 for a minute from 200 s
            {
                var m1 = FlyApproach(new Vector3D(20e3, 5e3, 0), new Vector3D(0, 0, 300), 3500, -1, 3600);
                Console.WriteLine("match from 20 km, 300 m/s drift: range " + m1.FinalDist.ToString("0") + " m, " + m1.FinalSpeed.ToString("0.00") + " m/s against it, closest " + m1.ClosestTo.ToString("0") + " m, burning " + m1.OverTime.ToString("0") + " s of 3600");
                Check(m1.ClosestTo >= 3500 && m1.FinalDist <= 4100, "match from 20 km: never nearer than 3.5 km, ends inside the band");
                Check(m1.FinalSpeed < 3, "match from 20 km: within 3 m/s of the ship's velocity");
                var m2 = FlyApproach(new Vector3D(0, 2e3, 0), new Vector3D(2000, 0, 0), 3500, -1, 600);
                Console.WriteLine("match from 2 km, 2 km/s drift: range " + m2.FinalDist.ToString("0") + " m, " + m2.FinalSpeed.ToString("0.00") + " m/s against it, closest " + m2.ClosestTo.ToString("0") + " m, burning " + m2.OverTime.ToString("0") + " s of 600");
                Check(m2.FinalDist >= 3500 && m2.FinalDist <= 4100 && m2.ClosestTo > 1900, "match from 2 km: backs off into the band, never nearer than it began");
                Check(m2.FinalSpeed < 3, "match from 2 km: within 3 m/s of the ship's velocity");
                var m3 = FlyApproach(new Vector3D(8e3, 0, 0), new Vector3D(0, 300, 0), 7700, -1, 900, new Vector3D(-2, 0, 0));
                Console.WriteLine("match at 8 km, ship burns 2 m/s2 toward it for 60 s: range " + m3.FinalDist.ToString("0") + " m, " + m3.FinalSpeed.ToString("0.00") + " m/s against it, closest " + m3.ClosestTo.ToString("0") + " m, burning " + m3.OverTime.ToString("0") + " s of 900");
                Check(m3.FinalDist >= 7700 && m3.FinalDist <= 8300 && m3.FinalSpeed < 3, "match, ship burns: back in the band and matched afterwards");
            }

            // formation. A leader with 6 m/s2 drives and one follower with 5 (so the leader is held to 4): the
            // follower joins from 10 km off while the leader lies still, then the leader flies 2,000 km and stops
            {
                var f1 = FlyFormation(2000e3, 6, 5, new Vector3D(-6e3, 3e3, 8e3), 2000);
                Console.WriteLine("formation, 2,000 km: on station after " + f1.FlipTime.ToString("0") + " s, worst " + f1.MaxBurn.ToString("0") + " m off its slot under way (peak " + f1.MaxSpeed.ToString("0") + " m/s), ships never nearer than " + f1.ClosestTo.ToString("0") + " m, ends " + f1.FinalDist.ToString("0") + " m off its slot at " + f1.FinalSpeed.ToString("0.00") + " m/s after " + f1.Time.ToString("0") + " s");
                Check(f1.Arrived, "formation: the leader arrives and the follower comes to rest in its slot");
                Check(f1.FlipTime > 0 && f1.FlipTime < 400, "formation: joins from 10 km within 400 s");
                Check(f1.MaxBurn < 600, "formation: within 600 m of its slot all the way");
                Check(f1.ClosestTo > 1200, "formation: ships never within 1.2 km of each other");
                Check(f1.FinalDist < 250 && f1.FinalSpeed < 2, "formation: ends in its slot, at rest against the leader");
                // slot 4 (two spacings to the left), 3 km spacing, a 600 km hop with a weak follower (3 m/s2 against 8)
                var f2 = FlyFormation(600e3, 8, 3, new Vector3D(4e3, -2e3, -9e3), 3000, 4);
                Console.WriteLine("formation, slot 4, 600 km: on station after " + f2.FlipTime.ToString("0") + " s, worst " + f2.MaxBurn.ToString("0") + " m off, nearest " + f2.ClosestTo.ToString("0") + " m, ends " + f2.FinalDist.ToString("0") + " m off at " + f2.FinalSpeed.ToString("0.00") + " m/s");
                Check(f2.Arrived && f2.MaxBurn < 600 && f2.ClosestTo > 1200, "formation, slot 4 with a weak follower: holds its slot and arrives");
                var o1 = Nav.Slot(new Vector3D(1, 0, 0), 1, 2000); var o2 = Nav.Slot(new Vector3D(1, 0, 0), 2, 2000); var o3 = Nav.Slot(new Vector3D(1, 0, 0), 3, 2000);
                Check(o1.Length() > 2000 && (o1 - o2).Length() > 3999 && (o1 - o3).Length() > 2000 && Math.Abs(Vector3D.Reject(o1 - o3, new Vector3D(1, 0, 0)).Length() - 2000) < 1, "formation: slots are a spacing or more apart, each in its own lane");
            }

            {
                // low water: a ship of 20 m/s2 burning 10 L/s at full thrust; a m/s costs half a litre
                Func<double, double, bool, double[]> fly = (tank, upTo, refill) =>
                {
                    var m = new Nav.WaterMeter(); double v = 0, w = tank, braked = -1;
                    for (int i = 0; i < 60000 && braked < 0; i++)
                    {
                        double thr = v < upTo ? 1 : 0, dt = 1 / 6.0;
                        m.Burnt(thr, dt); v += 20 * thr * dt; w = Math.Max(0, w - 10 * thr * dt);
                        if (refill && i == 300) w += 5000;
                        if (i % 12 == 11 && m.Low(w, v, 20)) braked = v;
                    }
                    return new[] { braked, w, m.Rate };
                };
                var ok = fly(10000, 5000, false);   // 5 km/s costs 2500 L up and 2500 L down of 10 kL
                Check(ok[0] < 0 && Math.Abs(ok[2] - 10) < 0.2, "low water: enough in the tanks, flies on (rate learned: " + ok[2].ToString("0.0") + " L/s)");
                var dry = fly(4000, 5000, false);   // 4 kL can't take it to 5 km/s and back: brakes near 4 km/s
                Check(dry[0] > 3300 && dry[0] < 4000 && dry[1] >= dry[0] / 20 * 10, "low water: brakes while the tanks still hold the stop (at " + dry[0].ToString("0") + " m/s with " + dry[1].ToString("0") + " L; the stop burns " + (dry[0] / 20 * 10).ToString("0") + " L)");
                var fill = fly(4000, 2500, true);   // topped up on the way: the refill isn't taken for burn, and it flies on
                Check(fill[0] < 0 && Math.Abs(fill[2] - 10) < 0.5, "low water: a refill in flight doesn't upset the rate");
                var slow = new Nav.WaterMeter(); slow.Low(100, 0, 20); slow.Burnt(1, 3); slow.Low(70, 50, 20); slow.Burnt(1, 3);
                Check(!slow.Low(40, 50, 20) && slow.Rate > 0, "low water: under 100 m/s it leaves the stop to the dampeners");
                var full = new Nav.WaterMeter(); full.Low(0, 0, 20); full.Burnt(1, 4); full.Low(0, 2000, 20); full.Burnt(1, 4);
                Check(!full.Low(0, 5000, 20), "low water: tanks that never drop (creative, or none read) never brake it");
            }
            Console.WriteLine(string.Format("{0}/{1} sim checks passed", checks - fails, checks));
            return fails == 0 ? 0 : 1;
        }

        /// <summary>
        /// Formation: a leader (drives aLead m/s2) lies still at the origin for 200 s, then flies dist along +X and
        /// stops, held to 80% of the follower's acceleration as the script holds it; a follower (aFollow) starts at
        /// fStart at rest and holds the slot. The leader's state reaches the follower every third run, a run late,
        /// and is flown on from as the script does. FlipTime = when first on station (within 150 m), MaxBurn = the
        /// worst distance off the slot from then until the leader arrives, ClosestTo = between the ships, Arrived =
        /// the leader arrived and the follower came to rest in its slot; FinalDist / FinalSpeed against the slot.
        /// </summary>
        static Result FlyFormation(double dist, double aLead, double aFollow, Vector3D fStart, double spacing, int slot = 1)
        {
            var sL = new Nav.Settings(); var sF = new Nav.Settings();
            var estL = new Nav.VelocityEstimator(); var estF = new Nav.VelocityEstimator();
            var one = new List<Nav.Wp>();
            var route = new List<Nav.Wp> { W(new Vector3D(dist, 0, 0), 3000, "target") };
            Vector3D pL = Vector3D.Zero, vL = Vector3D.Zero, dL = new Vector3D(0, 1, 0), wL = Vector3D.Zero;
            Vector3D pF = fStart, vF = Vector3D.Zero, dF = new Vector3D(0, 0, 1), wF = Vector3D.Zero;
            var axis = new Vector3D(1, 0, 0);
            double share = Math.Min(1, aFollow * 0.8 / aLead);
            double dt = RunTicks / 60.0, alpha = 0.6, t = 0;
            // the state as sent (sT) and the one waiting a run to be heard
            Vector3D mP = pL, mV = vL, mA = Vector3D.Zero, mD = dL; double mT = -1; bool mDone = false;
            Vector3D qP = pL, qV = vL, qA = Vector3D.Zero, qD = dL; double qT = -1; bool qDone = false, queued = false;
            int run = 0; bool leadDone = false;
            var r = new Result { ClosestTo = double.MaxValue, FlipTime = -1 };
            Nav.Cmd cL = new Nav.Cmd(), cF = new Nav.Cmd();
            while (t < 6000)
            {
                var physL = vL.Length() > 100 ? Vector3D.Normalize(vL) * 100 : vL;
                var veL = estL.Update(pL, physL, t);
                sL.PhysMax = Math.Max(sL.PhysMax, physL.Length());
                cL = new Nav.Cmd();
                if (t >= 200 && !leadDone)
                {
                    cL = Written(Nav.Steer(pL, veL, Vector3D.Zero, dL, aLead, share, share, route, 0, 49000, sL));
                    if (cL.Arrived) { leadDone = true; cL = new Nav.Cmd(); vL = Vector3D.Zero; } // (the dampeners stop the rest)
                }
                if (queued) { mP = qP; mV = qV; mA = qA; mD = qD; mT = qT; mDone = qDone; queued = false; }
                if (run % 3 == 0) { qP = pL; qV = veL; qA = dL * (cL.Throttle * aLead); qD = dL; qT = t; qDone = leadDone; queued = true; }
                run++;

                var physF = vF.Length() > 100 ? Vector3D.Normalize(vF) * 100 : vF;
                var veF = estF.Update(pF, physF, t);
                sF.PhysMax = Math.Max(sF.PhysMax, physF.Length());
                cF = new Nav.Cmd();
                var slotNow = pL + Nav.Slot(axis, slot, spacing);
                double offNow = Vector3D.Distance(pF, slotNow);
                if (mT >= 0)
                {
                    double age = t - mT, te = Math.Min(age, 3);
                    var tv = mV + mA * te;
                    var tp = mP + mV * age + mA * (te * age - te * te / 2) + Nav.Slot(axis, slot, spacing);
                    cF = Written(Nav.Formation(pF, veF, Vector3D.Zero, dF, aFollow, tp, tv, mA, mD, 49000, one, sF));
                    if (mDone && Vector3D.Distance(pF, tp) < Nav.FormReach && cF.SpeedNow < 2) { r.Arrived = true; break; }
                }
                if (r.FlipTime < 0 && offNow < 150) r.FlipTime = t;
                if (r.FlipTime >= 0 && !leadDone && offNow > r.MaxBurn) r.MaxBurn = offNow;
                r.MaxSpeed = Math.Max(r.MaxSpeed, vL.Length());
                for (int k = 0; k < RunTicks; k++)
                {
                    double h = 1.0 / 60;
                    var dw = cL.W - wL;
                    if (dw.Length() > alpha * h) dw = Vector3D.Normalize(dw) * alpha * h;
                    wL += dw;
                    if (wL.LengthSquared() > 0) dL = Vector3D.Normalize(dL + Vector3D.Cross(wL, dL) * h);
                    vL += dL * cL.Throttle * aLead * h;
                    pL += vL * h;
                    dw = cF.W - wF;
                    if (dw.Length() > alpha * h) dw = Vector3D.Normalize(dw) * alpha * h;
                    wF += dw;
                    if (wF.LengthSquared() > 0) dF = Vector3D.Normalize(dF + Vector3D.Cross(wF, dF) * h);
                    vF += dF * cF.Throttle * aFollow * h;
                    pF += vF * h;
                    double dc = Vector3D.Distance(pL, pF); if (dc < r.ClosestTo) r.ClosestTo = dc;
                }
                t += dt;
            }
            r.Time = t;
            r.FinalDist = Vector3D.Distance(pF, pL + Nav.Slot(axis, slot, spacing));
            r.FinalSpeed = (vF - vL).Length();
            return r;
        }

        /// <summary>
        /// The approach command against a ship at tp0 drifting at tv (the flying ship starts at rest at the origin,
        /// drive facing +Y): the target's place and physics velocity (capped at 100 m/s) read each run as the script
        /// does, until lostAt s (-1: never), then flown on along its last drift. FinalDist / FinalSpeed / ClosestTo are
        /// against the target.
        /// </summary>
        // matchFor > 0: the match command instead, flown that many seconds (keep = the band's near edge); OverTime is
        // then the time the drives burned. burn: the target's acceleration from 200 s to 260 s.
        static Result FlyApproach(Vector3D tp0, Vector3D tv, double keep, double lostAt, double matchFor = 0, Vector3D burn = default(Vector3D))
        {
            var s = new Nav.Settings();
            var est = new Nav.VelocityEstimator(); var track = new Nav.VelocityEstimator();
            var one = new List<Nav.Wp>();
            Vector3D p = Vector3D.Zero, v = Vector3D.Zero, drive = new Vector3D(0, 1, 0), w = Vector3D.Zero, tp = tp0;
            Vector3D seenPos = tp0, seenVel = tv.Length() > 100 ? Vector3D.Normalize(tv) * 100 : tv; double seenAt = 0;
            double dt = RunTicks / 60.0, alpha = 0.6, t = 0;
            var r = new Result { ClosestTo = double.MaxValue };
            Nav.Cmd cmd = new Nav.Cmd();
            while (t < (matchFor > 0 ? matchFor : 4000))
            {
                var phys = v.Length() > 100 ? Vector3D.Normalize(v) * 100 : v;
                var vEst = est.Update(p, phys, t);
                s.PhysMax = Math.Max(s.PhysMax, phys.Length());
                if (lostAt < 0 || t < lostAt)
                {
                    seenPos = tp; seenAt = t;
                    seenVel = track.Update(tp, tv.Length() > 100 ? Vector3D.Normalize(tv) * 100 : tv, t);
                }
                var at = seenPos + seenVel * (t - seenAt);
                cmd = Written(matchFor > 0 ? Nav.Match(p, vEst, Vector3D.Zero, drive, 10, at, seenVel, keep, 49000, one, s)
                    : Nav.Approach(p, vEst, Vector3D.Zero, drive, 10, at, seenVel, keep, 49000, one, s));
                if (cmd.Arrived) { r.Arrived = true; break; }
                if (cmd.Throttle > 0) r.OverTime += dt;
                bool burning = t >= 200 && t < 260;
                var want = cmd.W;
                for (int k = 0; k < RunTicks; k++)
                {
                    double h = 1.0 / 60;
                    var dw = want - w;
                    if (dw.Length() > alpha * h) dw = Vector3D.Normalize(dw) * alpha * h;
                    w += dw;
                    if (w.LengthSquared() > 0) drive = Vector3D.Normalize(drive + Vector3D.Cross(w, drive) * h);
                    v += drive * cmd.Throttle * 10 * h;
                    if (burning) tv += burn * h;
                    p += v * h; tp += tv * h;
                    double dc = Vector3D.Distance(p, tp); if (dc < r.ClosestTo) r.ClosestTo = dc;
                }
                t += dt;
            }
            r.Time = t; r.End = p;
            r.FinalDist = Vector3D.Distance(p, tp);
            r.FinalSpeed = (v - tv).Length();
            return r;
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
