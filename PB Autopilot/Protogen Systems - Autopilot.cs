// Protogen Systems - Autopilot v1 - flies the route planned by the SDX2 System Map plugin (Flip and Burn's N list).
//
// SETUP
//  1. Put this script in a programmable block on the ship (SDX2: script access needs a #sigma-support ticket).
//     It adds a [SDX2 Route] section to the PB's Custom Data; that is how the plugin finds it.
//  2. The ship needs a cockpit or remote control, gyros (RCS Control Computer or braced gyros) and its fusion drives
//     (sdx_drive*) all facing one way, as SDX2 requires anyway. Only the drives push the ship along: it turns them
//     the way it needs to go, and leaves RCS thrusters to the turning and the dampeners.
//  3. In game: pick a destination in the N list, click AUTOPILOT under the list (or Ctrl+M), set the route panel
//     (top speed, thrust or signature cap, waypoints) and click ENGAGE.
//  - The route panel's top speed and thrust caps come in the route (maxspeed=, thrust= for burning toward a point,
//    brake= for slowing down): the drives never run above that share of their thrust, which also caps the drive
//    signature. Braking is planned with the brake cap, so a low one starts the flip earlier.
//
// FLYING
//  - It turns the drives toward where it needs to push, burns, flips and brakes, and turns at each detour round a
//    world and each waypoint you added in the route panel, flies by asteroid dodge points without slowing, and stops 3 km short of a station
//    (at a detour or waypoint it slows only enough to turn onto the next leg within the point's reach radius;
//    a parabolic or bent leg comes as "curve" points: it flies through them without stopping, slowing for each bend
//    only as far as a sideways push of 60% of the thrust can turn it)
//    (above it when it's on or near a world), 800 km from Sol, or at the clearance above a planet or moon.
//  - Taking over: touch any movement or turn control in a seat, switch the dampeners on, press STOP AUTOPILOT /
//    Ctrl+M in the plugin, or run this PB with the argument "manual". It lets go of thrusters and gyros at once.
//    FULL STOP in the plugin (or the argument brake) first turns retrograde and burns the ship down to 50 m/s, then
//    switches the dampeners on (they stop the rest) and hands back.
//    The plugin then offers RESUME ROUTE (a pop-up, and a button under the N list).
//  - Hotbar: drag this PB onto the cockpit's toolbar, pick Run, and type the argument  manual  - one key back to
//    manual control. A second slot with  go  resumes the route already sent.
//  - Hotbar turns (no route needed; they take over from a route being flown):
//      prograde    turn the drives' push along your travel (the ship's forward pointing where you're going) and hold
//      retrograde  turn it against your travel (ready to brake) and hold
//      brake       retrograde full stop: turn retrograde, burn at full thrust until under 50 m/s, then dampeners on
//    A hold lasts until you touch the controls or run manual; brake also ends when stopped. Running the same
//    argument again turns it off.
//  - Arguments: manual (or stop) | go (fly the route already in Custom Data) | prograde | retrograde | brake
//  - Optional settings: a [SDX2 Autopilot Config] section in Custom Data, keys below (defaults shown):
//      TurnRate=0.6      max turn rate, rad/s
//      BrakeMargin=0.8   share of max acceleration planned for stops, bends and corners (the rest is a reserve)
//      MaxSpeed=0        m/s, 0 = the route's (Flip and Burn's cap)
//      SpeedBuffer=0.03  at the cap, this share under or over it counts as at speed (no chasing it, no flip to shed it)
//
// SDX2 performance: runs every 10 ticks while flying and every 100 while idle, no block scans per tick.

const string RouteHeader = "[SDX2 Route]", StatusHeader = "[SDX2 Autopilot]", ConfigHeader = "[SDX2 Autopilot Config]";
const int RouteVersion = 2;

readonly List<IMyShipController> controllers = new List<IMyShipController>();
readonly List<IMyGyro> gyros = new List<IMyGyro>();
readonly List<IMyThrust> thrusters = new List<IMyThrust>(), drives = new List<IMyThrust>();
readonly List<Nav.Wp> route = new List<Nav.Wp>();
readonly Nav.VelocityEstimator velocity = new Nav.VelocityEstimator();
readonly Nav.Settings settings = new Nav.Settings();
string target = "", lastCd, state = "idle", msg = "", lastStatus;
int lastId, idx, runs;
double time, routeMax = 50000, startedAt, thrustCap = 1, brakeCap = 1;
// FULL STOP hands over to the dampeners below this speed (m/s)
const double DampenBelow = 50;
int turnTicks; // runs in a row with turn input (a lone mouse jump, as when a menu opens, isn't a takeover)
bool flying;
string mode = "route"; // what Fly() does: route | prograde | retrograde | brake
Nav.Cmd last;

public Program()
{
    if (!Me.CustomData.Contains(RouteHeader))
        Me.CustomData = Me.CustomData.TrimEnd() + (Me.CustomData.Trim().Length > 0 ? "\n\n" : "") + RouteHeader + "\n";
    // resume after a reload or a Nexus sector transfer
    var s = (Storage ?? "").Split('|');
    if (s.Length >= 3)
    {
        int.TryParse(s[0], out lastId);
        int.TryParse(s[2], out idx);
        if (s[1] == "1" && ReadRoute(Me.CustomData, false) && Blocks()) { flying = true; state = "flying"; msg = "Resumed"; startedAt = -10; }
    }
    Runtime.UpdateFrequency = flying ? UpdateFrequency.Update10 : UpdateFrequency.Update100;
}

public void Save() { Storage = lastId + "|" + (flying && mode == "route" ? "1" : "0") + "|" + idx; } // hotbar turns aren't resumed

public void Main(string arg, UpdateType source)
{
    time += Runtime.TimeSinceLastRun.TotalSeconds;
    runs++;
    arg = (arg ?? "").Trim().ToLowerInvariant();
    if (arg == "manual") { if (flying) Release("override", "Manual control from the hotbar", false); }
    else if (arg == "stop" || arg == "off") Release("stopped", "Stopped by command", false);
    else if (arg == "go" || arg == "engage") Engage();
    else if (arg == "prograde" || arg == "retrograde" || arg == "brake" || arg == "fullstop" || arg == "full stop")
    {
        var m = arg == "prograde" || arg == "retrograde" ? arg : "brake";
        if (flying && mode == m) Release("override", "Turned off from the hotbar", false); // same key again: off
        else Turn(m);
    }

    var cd = Me.CustomData;
    if (!ReferenceEquals(cd, lastCd) && cd != lastCd) { lastCd = cd; ReadSettings(cd); Orders(cd); }

    if (flying) Fly();
    if (flying && runs % 10 == 0 || lastStatus == null) WriteStatus();
    Echo("Protogen Systems - Autopilot\n" + state.ToUpperInvariant() + (target != "" ? " - " + target : "") + "\n" + msg
         + (flying && mode == "route" ? "\nPoint " + (idx + 1) + " of " + route.Count + "\n" + Nav.Km(last.Dist) + "  " + Nav.Speed(last.SpeedNow) : "")
         + (flying && mode != "route" ? "\n" + Nav.Speed(last.SpeedNow) : "")
         + (thrustCap < 0.995 || brakeCap < 0.995 ? "\nThrust cap " + (thrustCap * 100).ToString("0") + "% burn, " + (brakeCap * 100).ToString("0") + "% brake" : "")
         + "\n\nRun: manual | go | prograde | retrograde | brake\nLast run " + Runtime.LastRunTimeMs.ToString("0.000") + " ms");
}

// ------------------------------------------------------------------ orders from the plugin

void Orders(string cd)
{
    int id; string command;
    if (!PeekOrder(cd, out id, out command) || id == lastId) return;
    lastId = id;
    if (command == "stop") { Release("stopped", "Stopped from the map", false); return; }
    if (command == "brake") { Turn("brake"); if (flying) { msg = "Full stop from the map"; WriteStatus(); } return; } // FULL STOP: brake to rest, then hand back
    bool keepFlying = command == "update" && flying;
    if (!ReadRoute(cd, true)) { if (command == "engage") Release("error", "The route in Custom Data can't be read", true); return; }
    if (command == "engage" || command == "update" && keepFlying) { idx = 0; if (!keepFlying) Engage(); else msg = "Route updated"; }
    else { state = flying ? state : "idle"; msg = "Route to " + target + " loaded: run 'go' or ENGAGE in the map"; }
    WriteStatus();
}

bool PeekOrder(string cd, out int id, out string command)
{
    id = 0; command = "load"; bool inside = false, any = false;
    foreach (var raw in cd.Split('\n'))
    {
        var line = raw.Trim();
        if (line.StartsWith("[")) { inside = line == RouteHeader; continue; }
        if (!inside) continue;
        if (line.StartsWith("id=")) { any = int.TryParse(line.Substring(3), out id); }
        else if (line.StartsWith("command=")) command = line.Substring(8);
    }
    return any;
}

bool ReadRoute(string cd, bool reset)
{
    var pts = new List<Nav.Wp>(); bool inside = false; int version = 0; string name = ""; double maxSpeed = 50000, thrust = 1, brake = 0;
    foreach (var raw in cd.Split('\n'))
    {
        var line = raw.Trim();
        if (line.StartsWith("[")) { inside = line == RouteHeader; continue; }
        int eq = line.IndexOf('=');
        if (!inside || eq < 1) continue;
        string k = line.Substring(0, eq), v = line.Substring(eq + 1);
        if (k == "version") int.TryParse(v, out version);
        else if (k == "target") name = v;
        else if (k == "maxspeed") maxSpeed = Nav.D(v);
        else if (k == "thrust") thrust = Nav.D(v);
        else if (k == "brake") brake = Nav.D(v);
        else if (k == "wp")
        {
            var f = v.Split(new[] { ',' }, 6);
            if (f.Length == 6) pts.Add(new Nav.Wp { Pos = new Vector3D(Nav.D(f[0]), Nav.D(f[1]), Nav.D(f[2])), Arrive = Math.Max(100, Nav.D(f[3])), Kind = f[4], Name = f[5] });
        }
    }
    if (version != RouteVersion || pts.Count == 0) return false;
    route.Clear(); route.AddRange(pts);
    target = name; routeMax = maxSpeed > 0 ? maxSpeed : 50000;
    thrustCap = thrust > 0 ? Math.Min(1, Math.Max(0.01, thrust)) : 1;
    brakeCap = brake > 0 ? Math.Min(1, Math.Max(0.01, brake)) : thrustCap;
    if (reset) idx = 0;
    idx = Math.Min(idx, route.Count - 1);
    return true;
}

void ReadSettings(string cd)
{
    bool inside = false;
    foreach (var raw in cd.Split('\n'))
    {
        var line = raw.Trim();
        if (line.StartsWith("[")) { inside = line == ConfigHeader; continue; }
        int eq = line.IndexOf('=');
        if (!inside || eq < 1) continue;
        string k = line.Substring(0, eq).Trim(); double v = Nav.D(line.Substring(eq + 1).Trim());
        if (k == "TurnRate" && v > 0.05) settings.TurnRate = Math.Min(v, 3);
        else if (k == "BrakeMargin" && v > 0.2) settings.BrakeMargin = Math.Min(v, 0.95);
        else if (k == "MaxSpeed") settings.MaxSpeed = Math.Max(0, v);
        else if (k == "SpeedBuffer") settings.SpeedBuffer = Math.Max(0, Math.Min(v, 0.2));
    }
}

// ------------------------------------------------------------------ flying

bool Blocks()
{
    controllers.Clear(); gyros.Clear(); thrusters.Clear(); drives.Clear();
    GridTerminalSystem.GetBlocksOfType(controllers, b => b.IsSameConstructAs(Me) && b.IsFunctional && b.CanControlShip);
    GridTerminalSystem.GetBlocksOfType(gyros, b => b.IsSameConstructAs(Me) && b.IsFunctional);
    GridTerminalSystem.GetBlocksOfType(thrusters, b => b.IsSameConstructAs(Me) && b.IsFunctional);
    if (controllers.Count == 0) { msg = "No cockpit or remote control"; return false; }
    if (gyros.Count == 0) { msg = "No gyros"; return false; }
    // the drive direction: the way the fusion drives (sdx_drive*) push, else the strongest thruster group
    var sum = new Dictionary<Base6Directions.Direction, double>(); bool anyDrive = false;
    foreach (var t in thrusters) if (t.BlockDefinition.SubtypeId.StartsWith("sdx_drive")) anyDrive = true;
    foreach (var t in thrusters)
    {
        if (anyDrive && !t.BlockDefinition.SubtypeId.StartsWith("sdx_drive")) continue;
        double s; sum.TryGetValue(t.Orientation.Forward, out s);
        sum[t.Orientation.Forward] = s + t.MaxThrust;
    }
    if (sum.Count == 0) { msg = "No thrusters"; return false; }
    var best = Base6Directions.Direction.Forward; double bestThrust = -1;
    foreach (var kv in sum) if (kv.Value > bestThrust) { bestThrust = kv.Value; best = kv.Key; }
    // with fusion drives, only they push the ship along (RCS stays free for turning and the dampeners);
    // without any, every thruster pushing the strongest way helps
    foreach (var t in thrusters)
        if (t.Orientation.Forward == best && (!anyDrive || t.BlockDefinition.SubtypeId.StartsWith("sdx_drive"))) drives.Add(t);
        else if (anyDrive && t.Orientation.Forward == best && t.ThrustOverridePercentage > 0) t.ThrustOverridePercentage = 0f; // left by an older version
    return true;
}

IMyShipController Controller()
{
    foreach (var c in controllers) if (c.IsUnderControl && c.IsFunctional) return c;
    foreach (var c in controllers) if (c.IsFunctional) return c;
    return null;
}

void Engage()
{
    if (route.Count == 0 && !ReadRoute(Me.CustomData, true)) { Release("error", "No route: pick one in the N list and ENGAGE", true); return; }
    if (!Blocks()) { Release("error", msg, true); return; }
    var c = Controller();
    c.DampenersOverride = false; // dampeners on = the pilot taking over
    mode = "route";
    flying = true; state = "flying"; msg = Heading(route[idx]); startedAt = time;
    velocity.Reset();
    settings.Observed = 0;
    Runtime.UpdateFrequency = UpdateFrequency.Update10;
}

/// <summary>prograde / retrograde hold, or brake (retrograde full stop). Takes over from a route being flown.</summary>
void Turn(string m)
{
    if (flying) Release("override", "", false);
    if (!Blocks()) { Release("error", msg, true); return; }
    var c = Controller();
    if (m == "brake") c.DampenersOverride = false; // the drives do the braking; dampeners come back on when stopped
    mode = m; flying = true; startedAt = time;
    state = m == "brake" ? "braking" : m;
    msg = m == "brake" ? "Retrograde full stop" : "Holding " + m;
    velocity.Reset();
    settings.Holding = false;
    Runtime.UpdateFrequency = UpdateFrequency.Update10;
    WriteStatus();
}

void Fly()
{
    var c = Controller();
    if (c == null || drives.Count == 0) { Release("error", "Lost the cockpit or the drives", true); return; }
    // manual override: movement or roll input in a seat, turn input on two runs in a row (a lone mouse jump, as when
    // a menu opens, isn't one), or the dampeners switched on
    if (time - startedAt > 0.5)
    {
        bool turning = false;
        foreach (var s in controllers)
        {
            if (!s.IsUnderControl) continue;
            if (s.MoveIndicator.LengthSquared() > 0.01 || Math.Abs(s.RollIndicator) > 0.01) { Release("override", "Pilot took the controls", false); return; }
            if (s.RotationIndicator.LengthSquared() > 0.01) turning = true;
        }
        turnTicks = turning ? turnTicks + 1 : 0;
        if (turnTicks >= 2) { turnTicks = 0; Release("override", "Pilot took the controls", false); return; }
    }
    else turnTicks = 0;
    if (c.DampenersOverride && time - startedAt > 0.5 && mode != "prograde" && mode != "retrograde") { Release("override", "Dampeners switched on: pilot has the controls", false); return; }

    var p = c.CenterOfMass;
    var v = velocity.Update(p, c.GetShipVelocities().LinearVelocity, time);
    var g = c.GetNaturalGravity();
    double mass = c.CalculateShipMass().PhysicalMass, thrust = 0;
    foreach (var t in drives) if (t.IsWorking) thrust += t.MaxEffectiveThrust;
    if (mass <= 0 || thrust <= 0) { Release("error", "No working drives", true); return; }
    double aFull = thrust / mass; // the route panel's caps are shares of this (burn, brake)
    var drive = drives[0].WorldMatrix.Backward; // thrusters push the grid along their Backward
    // learn how fast the ship really turns (weak RCS gyros), so it flips early enough
    if (last.W.Length() >= settings.TurnRate * 0.99)
        settings.Observed = Math.Max(settings.Observed, c.GetShipVelocities().AngularVelocity.Length());
    settings.Learn(last.W, c.GetShipVelocities().AngularVelocity, time);

    if (mode != "route")
    {
        // FULL STOP: under DampenBelow the dampeners finish the stop (drives off, dampeners on, pilot has the ship)
        if (mode == "brake" && v.Length() < DampenBelow) { Release("stopped", "Under " + DampenBelow.ToString("0") + " m/s: dampeners on", true); return; }
        last = Nav.Face(mode, v, g, drive, aFull, settings);
        if (last.Arrived) { Release("stopped", mode == "brake" ? "Stopped: dampeners on" : "Too slow to tell " + mode + " (under 1 m/s)", mode == "brake"); return; }
    }
    else
    {
        double cap = settings.MaxSpeed > 0 ? Math.Min(settings.MaxSpeed, routeMax) : routeMax;
        last = Nav.Steer(p, v, g, drive, aFull, thrustCap, brakeCap, route, idx, cap * 0.98, settings);
        if (last.Idx != idx) { idx = last.Idx; msg = idx < route.Count ? Heading(route[idx]) : ""; }
        if (last.Arrived) { Release("arrived", "Arrived at " + target, true); return; }
    }

    // Rounded (Nav.Throttle, Nav.Rate) so the values only change, and get synced to every client, when the guidance
    // really changes: unrounded, nearly every run sent one update per drive and per gyro.
    float thr = Nav.Throttle(last.Throttle);
    foreach (var t in drives) t.ThrustOverridePercentage = thr;
    foreach (var gy in gyros)
    {
        var local = Vector3D.TransformNormal(last.W, MatrixD.Transpose(gy.WorldMatrix));
        gy.GyroOverride = true; gy.GyroPower = 1f;
        gy.Pitch = -Nav.Rate(local.X); gy.Yaw = -Nav.Rate(local.Y); gy.Roll = -Nav.Rate(local.Z);
    }
}

/// <summary>Lets go of everything; dampeners back on when it stopped by itself (arrived or an error).</summary>
// what the PB says it's doing ("Curving to X" for a curve point, else "Flying to X")
string Heading(Nav.Wp w) { return w.Kind == "curve" ? w.Name : "Flying to " + w.Name; }

void Release(string newState, string why, bool dampeners)
{
    bool was = flying;
    flying = false; state = newState; msg = why;
    foreach (var t in drives) t.ThrustOverridePercentage = 0f;
    foreach (var gy in gyros) { gy.Pitch = 0; gy.Yaw = 0; gy.Roll = 0; gy.GyroOverride = false; }
    if (dampeners && was) { var c = Controller(); if (c != null) c.DampenersOverride = true; }
    Runtime.UpdateFrequency = UpdateFrequency.Update100;
    WriteStatus();
}

// ------------------------------------------------------------------ status for the plugin

void WriteStatus()
{
    var sb = new StringBuilder();
    sb.Append(StatusHeader).Append("\nstate=").Append(state).Append("\nack=").Append(lastId).Append("\ntarget=").Append(target)
      .Append("\nwp=").Append(idx).Append('/').Append(route.Count)
      .Append("\ndist=").Append(flying ? Math.Round(last.Remaining / 100) * 100 : 0)
      .Append("\nspeed=").Append(flying ? Math.Round(last.SpeedNow) : 0)
      .Append("\neta=").Append(flying ? Math.Round(last.Eta) : 0)
      .Append("\nmsg=").Append(msg.Replace('\n', ' ')).Append('\n');
    var text = sb.ToString();
    if (text == lastStatus) return;
    lastStatus = text;
    Me.CustomData = Merge(Me.CustomData, text, StatusHeader);
    lastCd = Me.CustomData;
}

static string Merge(string cd, string section, string header)
{
    var sb = new StringBuilder(); bool inside = false, written = false;
    foreach (var line in cd.Replace("\r\n", "\n").Split('\n'))
    {
        var t = line.Trim();
        if (t.StartsWith("[") && t.EndsWith("]"))
        {
            inside = t == header;
            if (inside) { sb.Append(section).Append('\n'); written = true; continue; }
        }
        if (!inside) sb.Append(line).Append('\n');
    }
    var res = sb.ToString().TrimEnd('\n');
    if (!written) res = (res.Length > 0 ? res + "\n\n" : "") + section;
    return res.TrimEnd('\n') + "\n";
}

// ------------------------------------------------------------------ guidance (no game calls: tested off-line)

public static class Nav
{
    public class Settings
    {
        public double TurnRate = 0.6, BrakeMargin = 0.8, MaxSpeed;
        // at the speed cap, this share under (or over) it counts as at speed: no burn to chase the last bit (the game
        // doesn't always let a ship reach its set speed) and no flip to shed a few m/s over it
        public double SpeedBuffer = 0.03;
        public bool Burning;    // correcting now (the dead band is narrower until it's done, so it doesn't stutter)
        public double Observed; // fastest turn seen while asking for full rate (0 = not yet)
        public bool Holding;    // a hotbar turn has had a direction to hold
        public double FlipRate { get { return Observed > 0.02 ? Math.Min(TurnRate, Observed) : TurnRate; } }
        // how fast the gyros really speed up / slow the turn (rad/s², 0 = not seen yet), learnt while they're
        // still short of the asked rate; until then a guess on the weak side (RCS gyros)
        public double Accel;
        public double TurnAccel { get { return Accel > 0 ? Accel : 0.15; } }
        Vector3D wPrev; double tPrev = -1;
        /// <summary>Learn the gyros' angular acceleration from the rate asked last run (asked) and the rate now.</summary>
        public void Learn(Vector3D asked, Vector3D wNow, double time)
        {
            double dt = time - tPrev;
            if (tPrev >= 0 && dt > 0.05 && dt < 1 && (asked - wPrev).Length() > 0.1)
            {
                double seen = (wNow - wPrev).Length() / dt;
                if (seen > 0.01) Accel = Math.Min(5, Math.Max(Accel, seen));
            }
            wPrev = wNow; tPrev = time;
        }
        /// <summary>
        /// The turn rate to ask for, angle (rad) off: never faster than the gyros can still slow it to nothing by the
        /// time it's lined up (with half their acceleration to spare), so the turn doesn't swing past and back.
        /// </summary>
        public double TurnCmd(double angle)
        {
            return Math.Min(TurnRate, Math.Min(angle * 1.5, Math.Sqrt(TurnAccel * angle)));
        }
    }

    /// <summary>Throttle as written to the drives: whole percent, rounded down so it never goes over a burn or brake cap.</summary>
    public static float Throttle(double t) { return (float)(Math.Floor(MathHelper.Clamp(t, 0, 1) * 100 + 1e-6) / 100); }
    /// <summary>A gyro rate as written to the gyros: to 0.01 rad/s.</summary>
    public static float Rate(double w) { return (float)(Math.Round(w * 100) / 100); }

    public struct Wp { public Vector3D Pos; public double Arrive; public string Kind, Name; }

    public struct Cmd
    {
        public Vector3D W;      // wanted world angular velocity (rad/s)
        public double Throttle; // 0..1 on the drives
        public int Idx;         // waypoint being flown to
        public bool Arrived;
        public double Dist, Remaining, SpeedNow, Eta;
    }

    /// <summary>
    /// One guidance step. Wanted velocity = toward the waypoint at the most speed that still lets the ship flip and
    /// stop at the next stopping point (detour or target); pass points are flown by. The wanted acceleration
    /// (velocity error over 3 s, plus gravity) sets where the drives point and how hard they burn: no burn until
    /// they point within 15 degrees of it.
    /// </summary>
    public static Cmd Steer(Vector3D p, Vector3D v, Vector3D g, Vector3D drive, double aMax, List<Wp> route, int idx, double cap, Settings s)
    {
        return Steer(p, v, g, drive, aMax, 1, 1, route, idx, cap, s);
    }

    // aFull = the drives' whole acceleration; burnCap / brakeCap = the shares of it allowed when speeding up / slowing
    // down. Throttle comes out as a share of the whole thrust, never above the cap for what it's doing.
    public static Cmd Steer(Vector3D p, Vector3D v, Vector3D g, Vector3D drive, double aFull, double burnCap, double brakeCap, List<Wp> route, int idx, double cap, Settings s)
    {
        double aMax = aFull * burnCap, aBrake = aFull * brakeCap;
        // planning share: every stop, bend and corner is planned on BrakeMargin of the thrust, so the rest is kept back
        // to catch up when the ship runs ahead of the plan (the throttle itself can still use all of it)
        double aPlan = aMax * s.BrakeMargin;
        var cmd = new Cmd { Idx = idx, SpeedNow = v.Length() };
        // move on from points reached or gone by
        while (cmd.Idx < route.Count - 1)
        {
            var w = route[cmd.Idx];
            double dw = Vector3D.Distance(p, w.Pos);
            var outDir = Vector3D.Normalize(route[cmd.Idx + 1].Pos - w.Pos);
            // gone by: past it toward the next point, and moving away from it (so not where you start, e.g. undocking)
            bool beyond = Vector3D.Dot(p - w.Pos, outDir) > 0 && Vector3D.Dot(p - w.Pos, v) > 0;
            bool through = w.Kind == "pass" || w.Kind == "curve";
            // a corner: start turning for the next leg early enough to swing the drives round and burn the
            // velocity change, centred on the point
            double lead = 0;
            if (Corner(w))
            {
                var prev = Before(route, cmd.Idx, p);
                double sh = SinHalf(prev, w.Pos, route[cmd.Idx + 1].Pos), sp = cmd.SpeedNow;
                lead = sp * sp * sh / aMax + sp * TurnLag(sh, s);
            }
            if (dw < w.Arrive || dw < lead || through && beyond || !through && beyond && dw < w.Arrive * 10) cmd.Idx++;
            else break;
        }
        var wp = route[cmd.Idx];
        var to = wp.Pos - p;
        double d = to.Length();
        var dir = d > 1e-3 ? to / d : Vector3D.Zero;
        cmd.Dist = d;

        // distance to the next point we must stop at, through pass points
        double stopDist = d; int k = cmd.Idx;
        while ((route[k].Kind == "pass" || route[k].Kind == "curve" || Corner(route[k])) && k < route.Count - 1) { stopDist += Vector3D.Distance(route[k].Pos, route[k + 1].Pos); k++; }
        var stopWp = route[k];
        double remaining = stopDist;
        for (int i = k; i < route.Count - 1; i++) remaining += Vector3D.Distance(route[i].Pos, route[i + 1].Pos);
        cmd.Remaining = remaining;

        bool last = cmd.Idx == route.Count - 1;
        if (last && d < wp.Arrive && cmd.SpeedNow < 10) { cmd.Arrived = true; return cmd; } // dampeners take the rest

        // speed we can still stop from: v*tFlip + v^2/(2a) = s
        double a = aBrake * s.BrakeMargin - Math.Max(0, Vector3D.Dot(g, dir));
        a = Math.Max(a, aBrake * 0.1);
        double tFlip = Math.PI / s.FlipRate + 3;
        double sAvail = Math.Max(0, stopDist - stopWp.Arrive * 0.4);
        double vStop = a * (-tFlip + Math.Sqrt(tFlip * tFlip + 2 * sAvail / a));
        double speed = Math.Min(cap, vStop);
        // a curved leg: no faster at each bend ahead than a sideways burn of TurnShare of the thrust can turn it
        // (v^2 / r), and slowing for one only as much as it needs (flip and brake only when it's a sharp one)
        double along2 = d;
        for (int j = cmd.Idx; j < k && j < cmd.Idx + 60; j++)
        {
            if (j > cmd.Idx) along2 += Vector3D.Distance(route[j - 1].Pos, route[j].Pos);
            bool corner = Corner(route[j]);
            if (route[j].Kind != "curve" && !corner) continue;
            var q = j > 0 ? route[j - 1].Pos : p;
            double vL, D = along2;
            if (corner) q = Before(route, j, p);
            if (corner)
            {
                // flip and burn: slow only enough to turn onto the next leg within the point's reach radius, by
                // the time the turn starts
                double sh = SinHalf(q, route[j].Pos, route[j + 1].Pos);
                vL = TurnSpeed(q, route[j].Pos, route[j + 1].Pos, aPlan, route[j].Arrive, cap);
                D = Math.Max(0, along2 - (vL * vL * sh / aMax + vL * TurnLag(sh, s)));
            }
            else vL = Math.Sqrt(TurnShare * aPlan * Radius(q, route[j].Pos, route[j + 1].Pos));
            double allow = Math.Max(vL, a * (-tFlip + Math.Sqrt(tFlip * tFlip + (2 * D + vL * vL / a) / a)));
            speed = Math.Min(speed, allow);
        }
        if (wp.Kind == "pass" && cmd.SpeedNow > 1)
        {
            // short of time for the sideways move to the pass point: don't speed up or brake, put all thrust sideways
            double lat = Vector3D.Reject(to, v / cmd.SpeedNow).Length();
            double tNeed = Math.Sqrt(2 * lat / (aPlan * 0.7)) + Math.PI / 2 / s.FlipRate + 2;
            double along = Math.Max(1, Vector3D.Dot(v, dir));
            if (d / along < tNeed * 1.5) speed = Math.Min(speed, along);
        }
        if (stopWp.Kind != "pass" && stopWp.Kind != "curve" && stopDist < stopWp.Arrive * 0.4) speed = 0;
        // on a curve: aim at a point some seconds ahead along it, not at the next point (aiming at a point close by
        // swings the heading as it's reached, and jumps it when the next one takes over)
        var aimDir = wp.Kind == "curve" ? LookAhead(route, cmd.Idx, k, p, Math.Max(cmd.SpeedNow * LookTime, wp.Arrive * 4)) : dir;
        double vAlong = Vector3D.Dot(v, aimDir), speedSet = speed;
        // cruising at the cap: anywhere in the buffer round it is at speed (no chasing it, no flip to shed a few m/s)
        if (speed >= cap * 0.999 && vAlong > cap * (1 - s.SpeedBuffer) && vAlong < cap * (1 + s.SpeedBuffer)) speedSet = vAlong;
        var vWant = aimDir * speedSet;
        var acc = (vWant - v) / 3.0 - g;
        double am = acc.Length();

        // where the drives should point: along acc; hold still when there's nothing to do. Fast, small errors
        // (under 0.4 % of the speed) are left until they grow, and a correction runs on until it's a third of that
        bool braking = cmd.SpeedNow > 1 && Vector3D.Dot(acc, v) < 0;
        double capNow = braking ? brakeCap : burnCap;
        double band = Math.Max(aFull * capNow * 0.01, cmd.SpeedNow * 0.004 / 3.0) * (s.Burning ? 0.33 : 1);
        if (am < band) { cmd.W = Vector3D.Zero; cmd.Throttle = 0; s.Burning = false; }
        else
        {
            s.Burning = true;
            var want = acc / am;
            double cos = MathHelper.Clamp(Vector3D.Dot(drive, want), -1, 1), angle = Math.Acos(cos);
            var axis = Vector3D.Cross(drive, want);
            if (axis.LengthSquared() < 1e-10) axis = Vector3D.CalculatePerpendicularVector(drive);
            cmd.W = Vector3D.Normalize(axis) * s.TurnCmd(angle);
            double aim = angle < 0.05 ? 1 : angle < 0.26 ? (0.26 - angle) / 0.21 : 0;
            cmd.Throttle = MathHelper.Clamp(am / aFull, 0, capNow) * aim * Math.Max(0, cos);
        }

        // ETA: this stretch from the present speed, the rest from rest
        double u = Math.Max(0, Vector3D.Dot(v, dir)), eta = 0;
        double stopping = u * u / (2 * a);
        eta += u > 1 && stopDist > stopping ? (stopDist - stopping) / Math.Max(u, Math.Min(cap, vStop)) + u / a : Trapezoid(stopDist, aMax * s.BrakeMargin, a, cap);
        for (int i = k; i < route.Count - 1; i++) eta += Trapezoid(Vector3D.Distance(route[i].Pos, route[i + 1].Pos), aMax * s.BrakeMargin, a, cap) + tFlip;
        cmd.Eta = eta;
        return cmd;
    }

    /// <summary>
    /// Hotbar turns. prograde / retrograde: point the drives' push along / against the velocity and hold (no thrust).
    /// brake: push to cancel the velocity in about a second, plus hold against gravity (so in space it points straight
    /// retrograde, and near a planet it doesn't settle into a fall at g m/s): full thrust once lined up, easing off over
    /// the last second's worth of speed so it doesn't overshoot; Arrived under 2 m/s (the dampeners take the rest).
    /// A hold reports Arrived only if there's no travel direction at the start (under 1 m/s); later it keeps its
    /// attitude below that.
    /// </summary>
    public static Cmd Face(string mode, Vector3D v, Vector3D g, Vector3D drive, double aFull, Settings s)
    {
        var cmd = new Cmd { SpeedNow = v.Length() };
        bool brake = mode == "brake";
        aFull = Math.Max(aFull, 0.01);
        if (brake ? cmd.SpeedNow < 2 : cmd.SpeedNow < 1) { cmd.Arrived = brake || !s.Holding; return cmd; }
        s.Holding = true;
        var want = (mode == "prograde" ? v : -v) / cmd.SpeedNow;
        var acc = -v - g; // brake: lose the speed in about 1 s and hold against gravity
        if (brake) want = Vector3D.Normalize(acc);
        double cos = MathHelper.Clamp(Vector3D.Dot(drive, want), -1, 1), angle = Math.Acos(cos);
        var axis = Vector3D.Cross(drive, want);
        if (axis.LengthSquared() < 1e-10) axis = Vector3D.CalculatePerpendicularVector(drive);
        cmd.W = angle < 0.002 ? Vector3D.Zero : Vector3D.Normalize(axis) * s.TurnCmd(angle);
        if (brake)
        {
            double aim = angle < 0.05 ? 1 : angle < 0.26 ? (0.26 - angle) / 0.21 : 0;
            cmd.Throttle = MathHelper.Clamp(acc.Length() / aFull, 0, 1) * aim * Math.Max(0, cos);
            cmd.Remaining = cmd.SpeedNow * cmd.SpeedNow / (2 * aFull);
            cmd.Eta = cmd.SpeedNow / aFull + angle / s.FlipRate;
        }
        return cmd;
    }

    /// <summary>Seconds ahead along a curve the ship aims.</summary>
    public const double LookTime = 15;

    /// <summary>
    /// The direction to a point L metres ahead along the route from the ship's place on the leg to route[idx], no
    /// further than route[stop] (the next point it must stop or turn at).
    /// </summary>
    static Vector3D LookAhead(List<Wp> route, int idx, int stop, Vector3D p, double L)
    {
        var a = idx > 0 ? route[idx - 1].Pos : p;
        var b = route[idx].Pos;
        var seg = b - a; double len = seg.Length();
        double s0 = len > 1 ? MathHelper.Clamp(Vector3D.Dot(p - a, seg / len), 0, len) : len;
        var look = b; double left = L - (len - s0);
        if (left <= 0) look = a + seg / Math.Max(len, 1e-9) * (s0 + L);
        else
        {
            for (int j = idx; j < stop && left > 0; j++)
            {
                var u = route[j + 1].Pos - route[j].Pos; double ul = u.Length();
                if (ul >= left) { look = route[j].Pos + u / ul * left; left = 0; }
                else { left -= ul; look = route[j + 1].Pos; }
            }
        }
        var to = look - p;
        return to.LengthSquared() > 1 ? Vector3D.Normalize(to) : Vector3D.Normalize(b - p + new Vector3D(1e-9, 0, 0));
    }

    /// <summary>A detour or waypoint: flown as a turn onto the next leg (the last point is the stop).</summary>
    static bool Corner(Wp w) { return w.Kind == "detour" || w.Kind == "waypoint"; }

    /// <summary>
    /// The point before route[j]: the one before it, or for the first, a point back toward the ship as far out as the
    /// next (so the turn's room doesn't shrink as the ship closes in).
    /// </summary>
    static Vector3D Before(List<Wp> route, int j, Vector3D p)
    {
        if (j > 0) return route[j - 1].Pos;
        var w = route[j].Pos; var back = p - w;
        double far = j + 1 < route.Count ? Vector3D.Distance(w, route[j + 1].Pos) : 1;
        return back.LengthSquared() < 1 ? p : w + Vector3D.Normalize(back) * Math.Max(back.Length(), far);
    }

    /// <summary>sin of half the turn at b, going a → b → c.</summary>
    public static double SinHalf(Vector3D a, Vector3D b, Vector3D c)
    {
        var u = b - a; var w = c - b;
        if (u.LengthSquared() < 1e-6 || w.LengthSquared() < 1e-6) return 1;
        return Math.Sqrt(Math.Max(0, (1 - Vector3D.Dot(Vector3D.Normalize(u), Vector3D.Normalize(w))) / 2));
    }

    /// <summary>
    /// The fastest a turn at b can be flown: burning the velocity change at full thrust cuts the corner by
    /// v^2 sin^2(t/2) / 2a, kept within tol (the point's reach radius), and starts v^2 sin(t/2) / a before the point,
    /// kept within half the shorter leg. The plugin plans with the same.
    /// </summary>
    public static double TurnSpeed(Vector3D a, Vector3D b, Vector3D c, double acc, double tol, double cap)
    {
        if (acc <= 0) return 0;
        double sh = SinHalf(a, b, c);
        if (sh < 1e-6) return cap;
        double room = Math.Min(Vector3D.Distance(a, b), Vector3D.Distance(b, c)) / 2;
        return Math.Min(cap, Math.Min(Math.Sqrt(2 * acc * tol) / sh, Math.Sqrt(acc * room / sh)));
    }

    /// <summary>Seconds to swing the drives from braking to the turn's push (90 degrees less half the turn), plus a second.</summary>
    static double TurnLag(double sh, Settings s) { return (Math.PI / 2 - Math.Asin(Math.Min(1, sh))) / s.FlipRate + 1; }

    /// <summary>Share of the thrust a bend may take sideways (the plugin plans with the same).</summary>
    public const double TurnShare = 0.6;

    /// <summary>Radius of the circle through three points (very large when they're in a line).</summary>
    public static double Radius(Vector3D a, Vector3D b, Vector3D c)
    {
        var u = b - a; var w = c - b;
        double cross = Vector3D.Cross(u, w).Length();
        return cross < 1e-9 * Math.Max(1, u.Length() * w.Length()) ? 1e12 : u.Length() * w.Length() * (c - a).Length() / (2 * cross);
    }

    // time to go dist from rest to rest: speed up at a, slow down at b, no faster than vmax
    static double Trapezoid(double dist, double a, double b, double vmax)
    {
        if (a <= 0 || b <= 0) return 0;
        double vp = Math.Sqrt(2 * dist * a * b / (a + b));
        return vp <= vmax ? vp / a + vp / b : dist / vmax + vmax / (2 * a) + vmax / (2 * b);
    }

    /// <summary>
    /// True speed: above the vanilla limit Flip and Burn moves the grid itself (physics velocity stays capped), so it
    /// comes from position over the last ~2 s; below it, from physics.
    /// </summary>
    public class VelocityEstimator
    {
        readonly Queue<Vector3D> pos = new Queue<Vector3D>();
        readonly Queue<double> at = new Queue<double>();
        public void Reset() { pos.Clear(); at.Clear(); }
        public Vector3D Update(Vector3D p, Vector3D physics, double t)
        {
            pos.Enqueue(p); at.Enqueue(t);
            while (at.Count > 2 && t - at.Peek() > 2.0) { pos.Dequeue(); at.Dequeue(); }
            double dt = t - at.Peek();
            if (physics.Length() < 90 || dt < 0.4) return physics;
            var fromPos = (p - pos.Peek()) / dt;
            return fromPos.Length() > physics.Length() ? fromPos : physics;
        }
    }

    public static double D(string s) { double d; return double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d) ? d : 0; }
    public static string Km(double m) { return m < 10000 ? m.ToString("0") + " m" : (m / 1000).ToString("#,0") + " km"; }
    public static string Speed(double ms) { return ms < 1000 ? ms.ToString("0") + " m/s" : (ms / 1000).ToString("0.0") + " km/s"; }
}
