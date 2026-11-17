using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using VRageMath;
using Nav = PbCheck.Program.Nav;

namespace PbCheck
{
    /// <summary>
    /// Server load of the autopilot PB with many players in one sector (dotnet run -- load [players] [seconds] [seed]).
    ///
    /// Each player has a ship running the script. It flies trips of 100-3,000 km (some with a detour round a world or an
    /// asteroid dodge), idles 1-5 minutes between them, and starts at a random time. Every PB run goes through the script's
    /// real guidance (Program.Nav), at its real rates: Update10 while flying, Update100 while idle.
    ///
    /// What it counts is what the server has to do and send:
    ///  - the script's own work per run (guidance, gyro maths, the status text and Echo), timed here. The game adds its
    ///    own block calls on top (velocities, mass, setters), so the in-game "Last run" figure will be higher.
    ///  - synced values the script changes. The game only sends a Sync value when it changes (Sync.SetValue compares):
    ///    a drive's ThrustOverride (one per drive), a gyro's override velocity (one Vector3 per gyro), and GyroOverride /
    ///    GyroPower when they flip on or off.
    ///  - Custom Data writes (the [SDX2 Autopilot] status, about every 1.7 s while flying, only when it changed). The game
    ///    sends the whole Custom Data text each time.
    /// Each of those goes from the server to every client that has the ship loaded: the owner, and in the worst case all
    /// the other players in the sector.
    /// </summary>
    public static class LoadSim
    {
        const double Tick = 1.0 / 60.0;
        static readonly bool Quant = Environment.GetEnvironmentVariable("LOAD_RAW") != "1"; // LOAD_RAW=1: the old unrounded writes
        const int MsgOverhead = 16;
        static readonly int RunTicks = int.Parse(Environment.GetEnvironmentVariable("SIM_RUN_TICKS") ?? "10"); // ticks between flying runs // rough bytes per synced-value message besides the value (entity id, property, framing): an estimate

        class Ship
        {
            public int Id, Drives, Gyros, Phase;
            public double Accel, TurnRate, BurnCap, BrakeCap, NextTrip, TripStart;
            public bool Flying;
            public int Idx, Runs;
            public List<Nav.Wp> Route;
            public Nav.Settings S = new Nav.Settings();
            public Nav.VelocityEstimator Est = new Nav.VelocityEstimator();
            public Vector3D P, V, Drive, Up, W;
            public float LastThrottle = -1;
            public Vector3[] LastGyro;
            public MatrixD[] GyroLocal;
            public string LastStatus, Cd = "";
            public int RouteChars;
            public int Trips, Arrived;
        }

        // totals
        static long runsFlying, runsIdle, thrustMsgs, gyroMsgs, gyroFlagMsgs, cdWrites, cdBytes;
        static readonly List<double> runMs = new List<double>();
        static readonly List<double> tickMs = new List<double>();
        static int maxFlying;

        public static int Run(int players, double seconds, int seed)
        {
            var rnd = new Random(seed);
            var ships = new List<Ship>();
            for (int i = 0; i < players; i++) ships.Add(NewShip(i, rnd));

            int ticks = (int)(seconds / Tick);
            var sw = new Stopwatch();
            var sb = new StringBuilder();
            for (int tick = 0; tick < ticks; tick++)
            {
                double t = tick * Tick, thisTick = 0;
                int flying = 0;
                foreach (var s in ships)
                {
                    if (s.Flying) flying++;
                    int every = s.Flying ? RunTicks : 100;
                    if ((tick + s.Phase) % every != 0) continue;
                    sw.Restart();
                    Step(s, t, rnd, sb);
                    sw.Stop();
                    double ms = sw.Elapsed.TotalMilliseconds;
                    thisTick += ms;
                    if (s.Flying || s.Runs > 0) runMs.Add(ms);
                }
                maxFlying = Math.Max(maxFlying, flying);
                tickMs.Add(thisTick);
            }

            Report(players, seconds, ships);
            return 0;
        }

        static Ship NewShip(int id, Random rnd)
        {
            var s = new Ship
            {
                Id = id,
                Drives = 2 + rnd.Next(7),              // 2-8 fusion drives
                Gyros = rnd.Next(3) == 0 ? 1 : 2 + rnd.Next(5), // an RCS Control Computer, or 2-6 braced gyros
                Phase = rnd.Next(100),
                Accel = 5 + rnd.NextDouble() * 20,     // 5-25 m/s²
                TurnRate = 0.15 + rnd.NextDouble() * 0.45,
                NextTrip = rnd.NextDouble() * 600,     // first trip within 10 minutes
            };
            s.LastGyro = new Vector3[s.Gyros];
            s.GyroLocal = new MatrixD[s.Gyros];
            for (int g = 0; g < s.Gyros; g++)
            {
                // gyros mounted in any of the 24 block orientations
                var f = Base6Directions.GetVector((Base6Directions.Direction)rnd.Next(6));
                Vector3 u;
                do u = Base6Directions.GetVector((Base6Directions.Direction)rnd.Next(6)); while (Math.Abs(Vector3.Dot(u, f)) > 0.5f);
                s.GyroLocal[g] = MatrixD.CreateWorld(Vector3D.Zero, f, u);
            }
            s.P = RandomDir(rnd) * (rnd.NextDouble() * 1000e3);
            s.Drive = RandomDir(rnd);
            s.Up = Vector3D.CalculatePerpendicularVector(s.Drive);
            return s;
        }

        static Vector3D RandomDir(Random rnd)
        {
            var v = new Vector3D(rnd.NextDouble() * 2 - 1, rnd.NextDouble() * 2 - 1, rnd.NextDouble() * 2 - 1);
            return v.LengthSquared() < 1e-6 ? Vector3D.Forward : Vector3D.Normalize(v);
        }

        static Nav.Wp W(Vector3D p, double arrive, string kind, string name) { return new Nav.Wp { Pos = p, Arrive = arrive, Kind = kind, Name = name }; }

        static void StartTrip(Ship s, double t, Random rnd)
        {
            double dist = 100e3 + rnd.NextDouble() * 2900e3;
            var dir = RandomDir(rnd);
            var end = s.P + dir * dist;
            var route = new List<Nav.Wp>();
            int kind = rnd.Next(20);
            if (kind < 5) // a detour round a world, then the target
            {
                var side = Vector3D.Normalize(Vector3D.CalculatePerpendicularVector(dir));
                route.Add(W(s.P + dir * dist * 0.5 + side * dist * 0.2, 2000, "detour", "Detour round Vesta"));
            }
            else if (kind < 8) // an asteroid dodge pass point on the way
            {
                var side = Vector3D.Normalize(Vector3D.CalculatePerpendicularVector(dir));
                route.Add(W(s.P + dir * dist * 0.4 + side * 5000, 1000, "pass", "Asteroid dodge"));
            }
            route.Add(W(end, 500, "target", "Tycho Station"));
            s.Route = route;
            s.Idx = 0; s.Flying = true; s.TripStart = t; s.Trips++;
            s.BurnCap = rnd.Next(3) == 0 ? 0.3 + rnd.NextDouble() * 0.7 : 1;
            s.BrakeCap = rnd.Next(4) == 0 ? 0.4 + rnd.NextDouble() * 0.6 : 1;
            s.S = new Nav.Settings { TurnRate = 0.6 };
            s.Est = new Nav.VelocityEstimator();
            s.V = Vector3D.Zero; s.W = Vector3D.Zero;
            // the [SDX2 Route] section the plugin wrote (one wp line per point, about 70 characters each)
            s.RouteChars = 140 + 70 * route.Count;
            gyroFlagMsgs += 2 * s.Gyros; // GyroOverride on, GyroPower 1
        }

        static void EndTrip(Ship s, double t, Random rnd, bool arrived)
        {
            s.Flying = false;
            if (arrived) s.Arrived++;
            // Release: overrides back to 0 / off (sent only if they weren't already)
            for (int i = 0; i < s.Drives; i++) if (s.LastThrottle != 0) thrustMsgs++;
            s.LastThrottle = 0;
            for (int g = 0; g < s.Gyros; g++) { if (s.LastGyro[g] != Vector3.Zero) gyroMsgs++; s.LastGyro[g] = Vector3.Zero; }
            gyroFlagMsgs += s.Gyros; // GyroOverride off
            s.NextTrip = t + 60 + rnd.NextDouble() * 240;
        }

        /// <summary>One PB run: the script's work and what it writes.</summary>
        static void Step(Ship s, double t, Random rnd, StringBuilder sb)
        {
            s.Runs++;
            if (!s.Flying)
            {
                runsIdle++;
                if (t >= s.NextTrip) StartTrip(s, t, rnd); // the player clicked ENGAGE
                else { Echo(s, sb); return; }
            }
            runsFlying++;

            // what the script reads each run (here from the point-mass physics; physics velocity capped at 100 m/s as in game)
            var phys = s.V.Length() > 100 ? Vector3D.Normalize(s.V) * 100 : s.V;
            var vEst = s.Est.Update(s.P, phys, t);
            var cmd = Nav.Steer(s.P, vEst, Vector3D.Zero, s.Drive, s.Accel, s.BurnCap, s.BrakeCap, s.Route, s.Idx, 50000 * 0.98, s.S);
            s.Idx = cmd.Idx;
            if (cmd.Arrived || t - s.TripStart > 3000) { WriteStatus(s, cmd, sb, true); EndTrip(s, t, rnd, cmd.Arrived); Echo(s, sb); return; }

            // drives: one ThrustOverride per drive, sent when it changes
            float thr = (float)(MathHelper.Clamp(cmd.Throttle, 0, 1) * 100.0);
            if (Quant) thr = Nav.Throttle(cmd.Throttle) * 100;
            if (thr != s.LastThrottle) { thrustMsgs += s.Drives; s.LastThrottle = thr; }
            // gyros: the wanted world rate in each gyro's own axes (the script's TransformNormal), sent when it changes
            var ship = MatrixD.CreateWorld(Vector3D.Zero, s.Drive, s.Up);
            for (int g = 0; g < s.Gyros; g++)
            {
                var world = s.GyroLocal[g] * ship;
                var local = Vector3D.TransformNormal(cmd.W, MatrixD.Transpose(world));
                var val = new Vector3(-(float)local.X, -(float)local.Y, -(float)local.Z);
                if (Quant) val = new Vector3(-Nav.Rate(local.X), -Nav.Rate(local.Y), -Nav.Rate(local.Z));
                if (val != s.LastGyro[g]) { gyroMsgs++; s.LastGyro[g] = val; }
            }
            if (s.Runs % Math.Max(1, (100 + RunTicks / 2) / RunTicks) == 0) WriteStatus(s, cmd, sb, false); // about every 1.7 s
            Echo(s, sb);

            // 10 physics ticks to the next run: the gyros turn the ship toward the wanted rate, the drives push
            var want = cmd.W;
            if (want.Length() > s.TurnRate) want = Vector3D.Normalize(want) * s.TurnRate;
            const double alpha = 0.6;
            for (int k = 0; k < RunTicks; k++)
            {
                var dw = want - s.W;
                if (dw.Length() > alpha * Tick) dw = Vector3D.Normalize(dw) * alpha * Tick;
                s.W += dw;
                if (s.W.LengthSquared() > 0)
                {
                    s.Drive = Vector3D.Normalize(s.Drive + Vector3D.Cross(s.W, s.Drive) * Tick);
                    s.Up = Vector3D.Normalize(s.Up + Vector3D.Cross(s.W, s.Up) * Tick);
                    s.Up = Vector3D.Normalize(s.Up - s.Drive * Vector3D.Dot(s.Up, s.Drive));
                }
                s.V += s.Drive * cmd.Throttle * s.Accel * Tick;
                if (s.V.Length() > 50000) s.V = Vector3D.Normalize(s.V) * 50000;
                s.P += s.V * Tick;
            }
        }

        /// <summary>The script's WriteStatus: the same text, written only when it changed.</summary>
        static void WriteStatus(Ship s, Nav.Cmd c, StringBuilder sb, bool final)
        {
            bool flying = !final;
            sb.Clear();
            sb.Append("[SDX2 Autopilot]").Append("\nstate=").Append(flying ? "flying" : "arrived").Append("\nack=").Append(123456789)
              .Append("\ntarget=").Append("Tycho Station").Append("\nwp=").Append(s.Idx).Append('/').Append(s.Route.Count)
              .Append("\ndist=").Append(flying ? Math.Round(c.Remaining / 100) * 100 : 0)
              .Append("\nspeed=").Append(flying ? Math.Round(c.SpeedNow) : 0)
              .Append("\neta=").Append(flying ? Math.Round(c.Eta) : 0)
              .Append("\nmsg=").Append(flying ? "Flying to " + s.Route[Math.Min(s.Idx, s.Route.Count - 1)].Name : "Arrived").Append('\n');
            var text = sb.ToString();
            if (text == s.LastStatus) return;
            s.LastStatus = text;
            cdWrites++;
            cdBytes += s.RouteChars + text.Length + 2; // the whole Custom Data goes (route section + status), ASCII
        }

        /// <summary>The script's Echo text (built every run).</summary>
        static void Echo(Ship s, StringBuilder sb)
        {
            sb.Clear();
            sb.Append("Protogen Systems - Autopilot\n").Append(s.Flying ? "FLYING" : "IDLE").Append(" - Tycho Station\n")
              .Append(s.Flying ? "Point " + (s.Idx + 1) + " of " + s.Route.Count : "").Append("\n\nRun: manual | go\nLast run 0.000 ms");
            GC.KeepAlive(sb.ToString());
        }

        static double Pct(List<double> v, double p)
        {
            if (v.Count == 0) return 0;
            var a = v.ToArray(); Array.Sort(a);
            return a[Math.Min(a.Length - 1, (int)(p * (a.Length - 1)))];
        }

        static void Report(int players, double seconds, List<Ship> ships)
        {
            int trips = 0, arrived = 0;
            foreach (var s in ships) { trips += s.Trips; arrived += s.Arrived; }
            double flyingShare = (double)runsFlying * RunTicks / (seconds * 60 * players);
            long msgs = thrustMsgs + gyroMsgs + gyroFlagMsgs;
            double msgsPerSec = msgs / seconds, cdPerSec = cdWrites / seconds;
            double valueBytes = thrustMsgs * 4 + gyroMsgs * 12 + gyroFlagMsgs * 4;
            double bytesPerSecToOne = (valueBytes + msgs * MsgOverhead + cdBytes + cdWrites * MsgOverhead) / seconds;
            double avgTick = 0; foreach (var x in tickMs) avgTick += x; avgTick /= Math.Max(1, tickMs.Count);

            Console.WriteLine();
            Console.WriteLine(Quant ? "(writes rounded as the script does: throttle to 1%, gyro rates to 0.01 rad/s)" : "(LOAD_RAW: unrounded writes, the script before rounding)");
            Console.WriteLine(string.Format("=== {0} players, one sector, {1:0} min simulated ===", players, seconds / 60));
            Console.WriteLine(string.Format("Trips: {0} started, {1} arrived. Flying {2:0}% of the time on average, at most {3} ships at once.",
                trips, arrived, flyingShare * 100, maxFlying));
            Console.WriteLine();
            Console.WriteLine("Server CPU, the script's own work (the game's block calls come on top):");
            Console.WriteLine(string.Format("  per PB run:  median {0:0.0000} ms, 99th percentile {1:0.0000} ms, max {2:0.0000} ms",
                Pct(runMs, 0.5), Pct(runMs, 0.99), Pct(runMs, 1)));
            Console.WriteLine(string.Format("  all {0} PBs per game tick: mean {1:0.0000} ms, 99th percentile {2:0.0000} ms, max {3:0.0000} ms (a tick is 16.7 ms)",
                players, avgTick, Pct(tickMs, 0.99), Pct(tickMs, 1)));
            Console.WriteLine(string.Format("  PB runs: {0:0.0} per second in all ({1} flying, {2} idle)", (runsFlying + runsIdle) / seconds, runsFlying, runsIdle));
            Console.WriteLine();
            Console.WriteLine("Synced from the server (sent only when a value changes):");
            Console.WriteLine(string.Format("  thrust override: {0:0.0}/s   gyro override vector: {1:0.0}/s   gyro on/off + power: {2:0.00}/s",
                thrustMsgs / seconds, gyroMsgs / seconds, gyroFlagMsgs / seconds));
            Console.WriteLine(string.Format("  Custom Data status writes: {0:0.00}/s, {1:0} bytes each on average",
                cdPerSec, cdWrites > 0 ? (double)cdBytes / cdWrites : 0));
            double perFlying = runsFlying > 0 ? (thrustMsgs + gyroMsgs) / (runsFlying * RunTicks * Tick) : 0;
            Console.WriteLine(string.Format("  per flying ship: {0:0.0} value changes per second", perFlying));
            Console.WriteLine();
            Console.WriteLine(string.Format("Network, all {0} ships (bytes include an estimated {1} bytes of framing per message):", players, MsgOverhead));
            Console.WriteLine(string.Format("  to each client that has every ship loaded: {0:0.0} messages/s, {1:0.0} KB/s",
                msgsPerSec + cdPerSec, bytesPerSecToOne / 1024));
            Console.WriteLine(string.Format("  server upload if all {0} clients have every ship loaded: {1:0.0} KB/s",
                players, bytesPerSecToOne * players / 1024));
        }
    }
}
