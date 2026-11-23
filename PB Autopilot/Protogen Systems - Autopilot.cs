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
//  - One exception: engaged (or re-routed) too fast for the capped brake to stop in the room left, it brakes above
//    the cap, as hard as the stop needs, and says so ("Braking at N%: over the cap to make the stop"). The signature
//    is larger for that burn.
//
// FLYING
//  - It turns the drives toward where it needs to push, burns, flips and brakes, and turns at each detour round a
//    world and each waypoint you added in the route panel, flies by asteroid dodge points without slowing, and stops 3 km short of a station
//    (at a detour or waypoint it slows only enough to turn onto the next leg within the point's reach radius;
//    a parabolic or bent leg comes as "curve" points: it flies through them without stopping, slowing for each bend
//    only as far as a sideways push of 60% of the thrust can turn it)
//    (above it when it's on or near a world), 800 km from Sol, or at the clearance above a planet or moon.
//  - Taking over: touch any movement or turn control in a seat and it lets go of thrusters and gyros at once (you
//    fly it). On a route, input shorter than TakeoverAfter (5 s) is a nudge: when you let go, the autopilot takes
//    the ship back and flies on from there. Longer (or switching the dampeners on) is a takeover: once your input
//    has stopped for 1.5 s (not mid-manoeuvre) it pauses (state "manual"), the route held until the plugin's pop-up
//    is answered: RESUME ROUTE flies on from where you are, CONFIRM MANUAL
//    (or Esc) ends it. The hotbar does the same: go resumes, manual confirms. STOP AUTOPILOT / Ctrl+M in the
//    plugin, or the argument "manual" while flying, ends it at once. A hotbar turn or full stop ends on any input.
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
//  - Hotbar approach (no route needed): lock a ship in WeaponCore, then run
//      approach        fly to 500 m from the locked ship and stop there
//      approach 300    the same, to 300 m (any distance from 100 m up)
//    It is meant for a disabled ship: one lying still, or drifting with its drives dead. It matches the ship's drift
//    (burn, flip, brake against the ship, not against space), stops about the distance (within some 50 m) clear of its hull (its bounding
//    box, so the HUD's range to its centre reads a little more), and hands back: dampeners on if the ship lies
//    still, left off if it drifts (they would stop you dead while it drifts on). It flies at full thrust (the route
//    panel's caps don't apply) and does not steer round anything in between. The lock is read when you run it: a
//    different or dropped lock afterwards doesn't change where it goes (it flies on along the ship's last drift).
//    Touching the controls, the dampeners, manual, or approach again ends it. It needs WeaponCore to know the
//    grid (a WeaponCore weapon on it) and the ship inside its targeting range.
//  - Hotbar match (no route needed): lock a ship in WeaponCore, then run
//      match           match the locked ship's speed and direction and keep the range you are at, never under 3.5 km
//      match 6000      the same, keeping 6 km (anything under 3500 counts as 3500)
//    It flies in the ship's own frame like approach, but doesn't hand back: it holds until you end it. The range
//    kept is a band 600 m deep that starts at the range asked (so never nearer than that, or than 3.5 km clear of
//    the hull): matched inside the band it lets the drives rest, and burns again when the range leaves the band
//    (on the near side a little before, so it has room to turn) or the ship's speed against yours passes 3 m/s. Nearer than the band when you run it, it backs off first. A ship
//    that burns is followed with a lag (each correction is a turn and a burn of the main drives). Dampeners stay
//    off. The lock, the thrust and what ends it are as for approach.
//  - Formation (ships linked by antenna; each needs this script, an antenna switched on and broadcasting, and
//    owners in the same faction): one ship leads and flies the route, the others hold a place beside it.
//      lead            on the leading ship: start (or, run again, end) leading. It then says where it is twice a
//                      second over the antenna, whether it is on a route, flown by hand or lying still
//      lead 3000       the same, with 3 km between ships (2 km when not given; never under 1.2 km, as Flip and Burn
//                      slows ships within 1 km of a friendly one)
//      form            on each other ship: join the nearest leader heard (run again, or touch the controls, to leave)
//    The leader gives each ship that joins a slot: 1 to its right and half a spacing back, 2 the same to the left,
//    3 and 4 two spacings out and one back, and so on (right and left of the way the route runs, level with the
//    system's plane), so every ship has its own lane beside the route. A ship in formation flies in the leader's
//    frame: it burns as the leader burns, turns its drives the way the leader's face (so it flips when the leader
//    flips), and corrects toward its slot. It never uses the route itself, so only the leader needs one.
//    The pace is the slowest ship's: every ship reports its full acceleration, and the leader holds its own burn and
//    brake to 80% of the lowest (the other 20% is what that ship corrects with), and its speed to the lowest MaxSpeed
//    set. The map plans the leader's route with that pace. Join before the leader engages: a ship that joins a
//    leader already burning has only that 20% to catch up with.
//    Arriving: when the leader has arrived, each ship comes to rest in its slot and hands back (dampeners on).
//    When the leader ends the formation, or you leave it, you have the controls with the dampeners off (the ship
//    keeps its speed). If the antenna link drops, the ship flies on along the leader's last course and says so; it
//    does not brake by itself. Followers do not steer round anything: the leader's route is planned for the leader.
//    SDX2: fly this with a pilot aboard each ship; an unpiloted follower counts as a drone under the server rules.
//  - Arguments: manual (or stop) | go (fly the route already in Custom Data) | prograde | retrograde | brake | approach [m] | match [m] | lead [m] | form
//  - Optional settings: a [SDX2 Autopilot Config] section in Custom Data, keys below (defaults shown):
//      TurnRate=0.6      max turn rate, rad/s
//      BrakeMargin=0.8   share of max acceleration planned for stops, bends and corners (the rest is a reserve)
//      MaxSpeed=0        m/s, 0 = the route's (Flip and Burn's cap)
//      SpeedBuffer=0.03  at the cap, this share under or over it counts as at speed (no chasing it, no flip to shed it)
//      FlipBuffer=10     seconds added to the flip time it plans each stop with, so it flips and brakes a little sooner
//      BurnLead=2        seconds the braking burn starts early at speed (fading out slow), so it needs a little less thrust
//      TakeoverAfter=5   seconds of your input on a route before it pauses and asks; shorter, it takes the ship back.
//                        Also held back before each flip, so a short takeover doesn't cost the stop
//      LowWater=1        0 = off. On a route, when the tanks are down to about what stopping from the ship's speed
//                        burns (learned from what the drives use as they burn), it brakes to rest there and then,
//                        rather than fly on and run dry at speed
//      TurnLog=1         0 = off. While flying, a line every 3 s in a [SDX2 Turn Log] section of Custom Data (the last
//                        100 kept): clock time, mode, point, speed, which way the drives face against the travel
//                        (+1 along it, -1 braking), the turn rate asked for, what the gyros are set to, the ship's real
//                        turn rate, throttle, gyros working / on override, pilot seated and turn input, dampeners.
//
// SDX2 performance: runs every 10 ticks while flying and every 100 while idle, no block scans per tick.

const string RouteHeader = "[SDX2 Route]", StatusHeader = "[SDX2 Autopilot]", ConfigHeader = "[SDX2 Autopilot Config]", LogHeader = "[SDX2 Turn Log]";
const int RouteVersion = 2;

readonly List<IMyShipController> controllers = new List<IMyShipController>();
readonly List<IMyGyro> gyros = new List<IMyGyro>();
readonly List<IMyThrust> thrusters = new List<IMyThrust>(), drives = new List<IMyThrust>();
readonly List<Nav.Wp> route = new List<Nav.Wp>();
readonly Nav.VelocityEstimator velocity = new Nav.VelocityEstimator();
// low water: the drives' tanks, what the drives burn (learned as they burn), and when it was last looked at
readonly List<IMyGasTank> tanks = new List<IMyGasTank>();
readonly Nav.WaterMeter water = new Nav.WaterMeter();
double waterAt, flownAt; float lastThr;
readonly Nav.Settings settings = new Nav.Settings();
string target = "", lastCd, state = "idle", msg = "", lastStatus;
bool wasOver, lowWater = true;
int lastId, idx, runs;
double time, routeMax = 50000, startedAt, thrustCap = 1, brakeCap = 1;
// FULL STOP hands over to the dampeners below this speed (m/s)
const double DampenBelow = 50;
int turnTicks; // runs in a row with turn input (a lone mouse jump, as when a menu opens, isn't a takeover)
// the pilot's hands on a route: when the input began (-1: none) and was last seen; held TakeoverAfter s, it pauses
double handStart = -1, lastInput, takeoverAfter = 5;
const double InputGap = 0.5; // s without input that still counts as one go (between key taps)
const double QuietFor = 1.5; // after a takeover: s without input before it pauses and asks (the manoeuvre is done)
bool handHeld; string handWhy = ""; // the input has become a takeover (held TakeoverAfter s, or dampeners on), and why
bool flying;
bool paused; // a route the pilot took over from: let go, but held until manual control is confirmed (or it resumes)
string mode = "route"; // what Fly() does: route | prograde | retrograde | brake | approach | match | form
// formation over the antenna (IGC). The leader broadcasts its state on FormTag + its owner's faction tag; a ship in
// formation answers the leader alone (FormJoin) with its name, full acceleration and speed cap
const string FormTag = "SDX2.Form.", FormJoin = "SDX2.Form.Join";
const double FormSpacing = 2000, FormSpacingMin = 1200; // m between ships (Flip and Burn slows ships within 1 km of a friendly one)
const double LeadShare = 0.8;     // the leader burns at no more than this share of the slowest ship's acceleration
const double LinkLost = 5, MemberTimeout = 15; // s without hearing the leader / a member
const int MaxMembers = 8;
class Member { public string Name = ""; public double Accel, Cap, Seen; public int Slot; }
class Heard { public string Name = ""; public Vector3D Pos; public double Seen; }
IMyBroadcastListener formListen; string formTag = "";
bool leading; double formSpacing = FormSpacing, fleetAccel, fleetCap, fleetShare; int castRun;
readonly Dictionary<long, Member> members = new Dictionary<long, Member>();
readonly Dictionary<long, Heard> heard = new Dictionary<long, Heard>();
readonly List<long> gone = new List<long>();
// the leader's own state as flown this run (curRun), and the way its route runs (the formation's axis)
Vector3D curP, curV, curAcc, curDrive, leadAxis = Vector3D.Forward; int curRun = -1;
// in formation: the leader followed, its state as last heard (moved on by the message's age) and this ship's slot
long leadId; string leadName = ""; Vector3D ldPos, ldVel, ldAcc, ldDrive, ldAxis = Vector3D.Forward;
double ldSeen = -100, ldSpacing = FormSpacing, helloAt = -100; int ldFlag, mySlot; bool ldLost;
// approach: the ship locked in WeaponCore when the command ran (its id), where it was last seen and how it drifts
Func<long, int, MyDetectedEntityInfo> wcFocus;
long apId; Vector3D apPos, apVel; double apSeen, apKeep, apRadius; string apName = ""; bool apLost;
readonly Nav.VelocityEstimator apTrack = new Nav.VelocityEstimator();
readonly List<Nav.Wp> apRoute = new List<Nav.Wp>();
const double ApproachTo = 500; // m clear of the ship's hull, when the argument gives no distance
const double MatchFloor = 3500; // match: never nearer than this clear of the ship's hull
bool apKeeping; // match: matched and inside the band (drives resting)
Nav.Cmd last;
// turn log (TurnLog=1): what the gyros were asked for against what the ship really did, kept in Custom Data
readonly List<string> turnLog = new List<string>();
bool logOn = true;
int logRuns;

public Program()
{
    if (!Me.CustomData.Contains(RouteHeader))
        Me.CustomData = Me.CustomData.TrimEnd() + (Me.CustomData.Trim().Length > 0 ? "\n\n" : "") + RouteHeader + "\n";
    // the turn log so far (it carries on across a reload)
    bool inLog = false;
    foreach (var raw in Me.CustomData.Split('\n'))
    {
        var line = raw.Trim();
        if (line.StartsWith("[")) { inLog = line == LogHeader; continue; }
        if (inLog && line.Length > 0) turnLog.Add(line);
    }
    while (turnLog.Count > LogLines) turnLog.RemoveAt(0);
    // resume after a reload or a Nexus sector transfer
    var s = (Storage ?? "").Split('|');
    if (s.Length >= 3)
    {
        int.TryParse(s[0], out lastId);
        int.TryParse(s[2], out idx);
        if (s[1] == "1" && ReadRoute(Me.CustomData, false) && Blocks()) { flying = true; state = "flying"; msg = "Resumed"; startedAt = -10; }
        else if (s[1] == "2" && ReadRoute(Me.CustomData, false)) { paused = true; state = "manual"; msg = "Pilot took the controls"; }
    }
    if (s.Length >= 5 && s[3] == "1") { leading = true; formSpacing = Math.Max(FormSpacingMin, Nav.D(s[4])); }
    try { formTag = FormTag + Me.GetOwnerFactionTag(); formListen = IGC.RegisterBroadcastListener(formTag); } catch { formListen = null; }
    Runtime.UpdateFrequency = flying || leading ? UpdateFrequency.Update10 : UpdateFrequency.Update100;
}

// hotbar turns and a place in a formation aren't resumed; leading is
public void Save() { Storage = lastId + "|" + (flying && mode == "route" ? "1" : paused ? "2" : "0") + "|" + idx + "|" + (leading ? "1" : "0") + "|" + formSpacing.ToString("0"); }

double statusAt = -10, echoAt = -10;
string echoState, echoMsg;

public void Main(string arg, UpdateType source)
{
    time += Runtime.TimeSinceLastRun.TotalSeconds;
    runs++;
    arg = (arg ?? "").Trim().ToLowerInvariant();
    if (arg == "manual") { if (flying || paused) Release("override", paused ? "Manual control confirmed" : "Manual control from the hotbar", false); }
    else if (arg == "stop" || arg == "off") Release("stopped", "Stopped by command", false);
    else if (arg == "go" || arg == "engage") Engage();
    else if (arg == "prograde" || arg == "retrograde" || arg == "brake" || arg == "fullstop" || arg == "full stop")
    {
        var m = arg == "prograde" || arg == "retrograde" ? arg : "brake";
        if (flying && mode == m) Release("override", "Turned off from the hotbar", false); // same key again: off
        else Turn(m);
    }
    else if (arg == "approach" || arg.StartsWith("approach "))
    {
        if (flying && mode == "approach") Release("override", "Approach turned off from the hotbar", false); // same key again: off
        else Approach("approach", arg.Length > 9 ? Nav.D(arg.Substring(9).Trim()) : 0);
    }
    else if (arg == "match" || arg.StartsWith("match "))
    {
        if (flying && mode == "match") Release("override", "Match turned off from the hotbar", false); // same key again: off
        else Approach("match", arg.Length > 6 ? Nav.D(arg.Substring(6).Trim()) : 0);
    }

    else if (arg == "lead" || arg.StartsWith("lead ")) Lead(arg.Length > 5 ? Nav.D(arg.Substring(5).Trim()) : 0);
    else if (arg == "form" || arg == "formation" || arg == "join")
    {
        if (flying && mode == "form") Release("override", "Left the formation: you have the controls", false); // same key again: off
        else Join();
    }

    var cd = Me.CustomData;
    if (!ReferenceEquals(cd, lastCd) && cd != lastCd) { lastCd = cd; ReadSettings(cd); Orders(cd); }

    Hear();
    if (flying) Fly();
    if (leading) Cast();
    // status about every 1.7 s in flight, whatever the run rate (a long coast runs at Update100)
    if (flying && time - statusAt >= 1.6 || lastStatus == null) { statusAt = time; WriteStatus(); }
    // the terminal readout: once a second, or at once when a command came in or the state or message changed
    if (arg == "" && time - echoAt < 1 && state == echoState && msg == echoMsg) return;
    echoAt = time; echoState = state; echoMsg = msg;
    Echo("Protogen Systems - Autopilot\n" + state.ToUpperInvariant() + ((mode == "approach" || mode == "match") && flying ? " - " + apName : mode == "form" && flying ? " - " + leadName : target != "" ? " - " + target : "") + "\n" + msg
         + (leading ? "\nLeading " + members.Count + " ship" + (members.Count == 1 ? "" : "s") + ", " + Nav.Km(formSpacing) + " apart" + (fleetShare > 0 && fleetShare < 0.995 ? ", thrust held to " + (fleetShare * 100).ToString("0") + "%" : "") : "")
         + (flying && mode == "route" ? "\nPoint " + (idx + 1) + " of " + route.Count + "\n" + Nav.Km(last.Dist) + "  " + Nav.Speed(last.SpeedNow) : "")
         + (flying && mode == "approach" ? "\n" + Nav.Km(last.Dist) + " to go  closing " + Nav.Speed(last.SpeedNow) : "")
         + (flying && mode == "match" ? "\nRange " + Nav.Km(last.Dist) + "  speed against it " + Nav.Speed(last.SpeedNow) : "")
         + (flying && mode != "route" && mode != "approach" && mode != "match" ? "\n" + Nav.Speed(last.SpeedNow) : "")
         + (thrustCap < 0.995 || brakeCap < 0.995 ? "\nThrust cap " + (thrustCap * 100).ToString("0") + "% burn, " + (brakeCap * 100).ToString("0") + "% brake" : "")
         + "\n\nRun: manual | go | prograde | retrograde | brake | approach | match | lead | form\nLast run " + Runtime.LastRunTimeMs.ToString("0.000") + " ms");
}

// ------------------------------------------------------------------ orders from the plugin

void Orders(string cd)
{
    int id; string command;
    if (!PeekOrder(cd, out id, out command) || id == lastId) return;
    lastId = id;
    if (command == "stop") { Release("stopped", "Stopped from the map", false); return; }
    if (command == "manual") { if (flying || paused) Release("override", "Manual control confirmed", false); else WriteStatus(); return; }
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
        else if (k == "FlipBuffer") settings.FlipBuffer = Math.Max(0, Math.Min(v, 120));
        else if (k == "TakeoverAfter") settings.Reserve = takeoverAfter = Math.Max(0, Math.Min(v, 60));
        else if (k == "BurnLead") settings.BurnLead = Math.Max(0, Math.Min(v, 30));
        else if (k == "TurnLog") logOn = v != 0;
        else if (k == "LowWater") lowWater = v != 0;
    }
}

// ------------------------------------------------------------------ flying

bool Blocks()
{
    controllers.Clear(); gyros.Clear(); thrusters.Clear(); drives.Clear(); tanks.Clear();
    // the drives' water: SDX2's water tanks, and the vanilla hydrogen tanks it fills with water
    GridTerminalSystem.GetBlocksOfType(tanks, b => b.IsSameConstructAs(Me) && b.IsFunctional && (b.BlockDefinition.SubtypeId.Contains("Water") || b.BlockDefinition.SubtypeId.Contains("Hydrogen")));
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
    // hull size along the drives and across them: which way it should flip faster, until it has seen both
    var o = drives[0].Orientation; var ext = Me.CubeGrid.Max - Me.CubeGrid.Min + Vector3I.One;
    settings.SizeFwd = Size(ext, o.Forward); settings.SizeUp = Size(ext, o.Up); settings.SizeLeft = Size(ext, o.Left);
    return true;
}

double Size(Vector3I ext, Base6Directions.Direction d)
{
    var u = Base6Directions.GetIntVector(d);
    return Math.Abs(ext.X * u.X + ext.Y * u.Y + ext.Z * u.Z) * Me.CubeGrid.GridSize;
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
    flying = true; paused = false; handStart = -1; state = "flying"; msg = Heading(route[idx]); startedAt = time;
    velocity.Reset(); water.Reset(); waterAt = flownAt = time; lastThr = 0;
    settings.Observed = 0; settings.Stuck = 0;
    Runtime.UpdateFrequency = UpdateFrequency.Update10;
    Log("engage route to " + target + ", point " + (idx + 1) + " of " + route.Count, true);
}

/// <summary>prograde / retrograde hold, or brake (retrograde full stop). Takes over from a route being flown.</summary>
void Turn(string m)
{
    if (flying || paused) Release("override", "", false);
    if (!Blocks()) { Release("error", msg, true); return; }
    var c = Controller();
    if (m == "brake") c.DampenersOverride = false; // the drives do the braking; dampeners come back on when stopped
    mode = m; flying = true; handStart = -1; startedAt = time;
    state = m == "brake" ? "braking" : m;
    msg = m == "brake" ? "Retrograde full stop" : "Holding " + m;
    velocity.Reset();
    settings.Holding = false;
    Runtime.UpdateFrequency = UpdateFrequency.Update10;
    Log("turn " + m, true);
    WriteStatus();
}

/// <summary>The ship locked in WeaponCore from this grid (WeaponCore's programmable block API). False: no lock, the
/// ship is out of WeaponCore's targeting range, or (wcFocus still null) WeaponCore isn't answering.</summary>
bool Focus(out MyDetectedEntityInfo info)
{
    info = new MyDetectedEntityInfo();
    try
    {
        if (wcFocus == null)
        {
            var prop = Me.GetProperty("WcPbAPI");
            var api = prop == null ? null : prop.As<IReadOnlyDictionary<string, Delegate>>().GetValue(Me);
            Delegate d;
            if (api != null && api.TryGetValue("GetAiFocus", out d)) wcFocus = d as Func<long, int, MyDetectedEntityInfo>;
        }
        if (wcFocus == null) return false;
        info = wcFocus(Me.CubeGrid.EntityId, 0);
    }
    catch { return false; }
    return !info.IsEmpty() && info.EntityId != Me.CubeGrid.EntityId;
}

/// <summary>approach [m]: fly to that far (500 m) clear of the ship locked in WeaponCore and stop there, matched to
/// its drift. match [m]: match its speed and direction and keep the range (the one asked, else the one now; never
/// under MatchFloor) until ended. Takes over from a route being flown; with nothing locked it changes nothing.</summary>
void Approach(string m, double keep)
{
    MyDetectedEntityInfo t;
    string what = m == "match" ? "Match" : "Approach";
    if (!Focus(out t))
    {
        msg = wcFocus == null ? what + ": WeaponCore isn't answering (it needs a WeaponCore weapon on this grid)"
            : what + ": no ship locked in WeaponCore (lock it first, inside targeting range)";
        lastStatus = null; WriteStatus();
        return;
    }
    Turn(m);
    if (!flying) return;
    Controller().DampenersOverride = false; // the drives do the stopping; dampeners would brake against space, not the ship
    apId = t.EntityId; apName = t.Name ?? "ship"; apPos = t.Position; apVel = t.Velocity; apSeen = time; apLost = false;
    apRadius = (t.BoundingBox.Max - t.BoundingBox.Min).Length() / 2;
    apKeep = Math.Max(100, keep > 0 ? keep : ApproachTo);
    if (m == "match")
    {
        // the near edge of the band: the range asked, else the range now (less half the band, so it holds where it
        // is), never under the floor
        double now = Vector3D.Distance(Controller().CenterOfMass, apPos) - apRadius;
        apKeep = Math.Max(MatchFloor, keep > 0 ? keep : now - Nav.MatchBand);
    }
    apKeeping = false; settings.Keeping = false;
    apTrack.Reset(); apTrack.Update(apPos, t.Velocity, time);
    settings.Observed = 0; settings.Stuck = 0;
    msg = ApMsg();
    Log(m + " " + apName + " at " + apKeep.ToString("0") + " m, drifting " + apVel.Length().ToString("0") + " m/s", true);
    WriteStatus();
}

// ------------------------------------------------------------------ formation

static string N(double d, string f) { return d.ToString(f, System.Globalization.CultureInfo.InvariantCulture); }
static void Put(StringBuilder sb, Vector3D v, string f) { sb.Append('|').Append(N(v.X, f)).Append('|').Append(N(v.Y, f)).Append('|').Append(N(v.Z, f)); }
static Vector3D Vec(string[] f, int i) { return new Vector3D(Nav.D(f[i]), Nav.D(f[i + 1]), Nav.D(f[i + 2])); }
string ShipName() { return (Me.CubeGrid.CustomName ?? "ship").Replace('|', '/').Replace(',', ' ').Replace('\n', ' '); }

/// <summary>lead [m]: start leading a formation (again with no distance: end it). The ship says where it is over the
/// antenna from then on, on a route or not.</summary>
void Lead(double spacing)
{
    if (formListen == null) { msg = "Formation: this block can't use the antenna link"; lastStatus = null; WriteStatus(); return; }
    if (leading && spacing <= 0)
    {
        Cast(2); // tell the ships in formation it's over
        leading = false; members.Clear(); fleetAccel = fleetCap = fleetShare = 0;
        if (!flying) Runtime.UpdateFrequency = UpdateFrequency.Update100;
        msg = "Formation ended"; Log("formation: ended", true);
        lastStatus = null; WriteStatus();
        return;
    }
    if (flying && mode == "form") Release("override", "", false);
    if (spacing > 0) formSpacing = Math.Max(FormSpacingMin, spacing);
    if (!flying && !Blocks()) { lastStatus = null; WriteStatus(); return; }
    if (!leading) { leading = true; castRun = 0; Log("formation: leading, " + formSpacing.ToString("0") + " m apart", true); }
    msg = "Leading a formation, ships " + Nav.Km(formSpacing) + " apart: the others run 'form'";
    Runtime.UpdateFrequency = UpdateFrequency.Update10;
    lastStatus = null; WriteStatus();
}

/// <summary>form: join the nearest leader heard in the last few seconds and hold the slot it gives.</summary>
void Join()
{
    if (!Blocks()) { state = "error"; lastStatus = null; WriteStatus(); return; }
    var c = Controller(); var p = c.CenterOfMass;
    long best = 0; double bd = double.MaxValue;
    foreach (var kv in heard)
    {
        if (time - kv.Value.Seen > 5) continue;
        double d = Vector3D.Distance(p, kv.Value.Pos);
        if (d < bd) { bd = d; best = kv.Key; }
    }
    if (best == 0)
    {
        msg = "Formation: no leader heard (antennas on and in range, same faction, and the leader running 'lead'?)";
        lastStatus = null; WriteStatus();
        return;
    }
    if (flying || paused) Release("override", "", false);
    if (leading) { Cast(2); leading = false; members.Clear(); fleetAccel = fleetCap = fleetShare = 0; }
    c.DampenersOverride = false; // the drives hold the slot; dampeners would brake against space, not the leader
    mode = "form"; flying = true; handStart = -1; startedAt = time; state = "formation";
    leadId = best; leadName = heard[best].Name; ldSeen = -100; mySlot = 0; ldLost = false; helloAt = -100; ldFlag = 0;
    velocity.Reset();
    settings.Observed = 0; settings.Stuck = 0; settings.Burning = false;
    msg = "Joining " + leadName;
    Runtime.UpdateFrequency = UpdateFrequency.Update10;
    Log("formation: joining " + leadName + ", " + Nav.Km(bd) + " away", true);
    WriteStatus();
}

/// <summary>Every run: what came over the antenna. Leaders heard (for form to pick from), the state of the one this
/// ship follows, and, leading, the ships that have joined or left.</summary>
void Hear()
{
    if (formListen == null) return;
    while (formListen.HasPendingMessage)
    {
        var m = formListen.AcceptMessage();
        var f = (m.Data as string ?? "").Split('|');
        if (f.Length < 21 || f[0] != "L" || m.Source == IGC.Me) continue;
        var pos = Vec(f, 3);
        Heard h;
        if (!heard.TryGetValue(m.Source, out h)) { if (heard.Count >= 16) continue; heard[m.Source] = h = new Heard(); }
        h.Name = f[2]; h.Pos = pos; h.Seen = time;
        if (m.Source != leadId || !flying || mode != "form") continue;
        // moved on by the message's age (the clock both blocks share: the server's), so a leader at 20 km/s isn't
        // placed kilometres behind where it is
        long ticks; long.TryParse(f[1], out ticks);
        double lat = MathHelper.Clamp((DateTime.UtcNow.Ticks - ticks) / 1e7, 0, 2);
        ldVel = Vec(f, 6); ldAcc = Vec(f, 9); ldDrive = Vec(f, 12); ldAxis = Vec(f, 15);
        ldPos = pos + ldVel * lat + ldAcc * (lat * lat / 2); ldVel += ldAcc * lat;
        ldSpacing = Math.Max(FormSpacingMin, Nav.D(f[18]));
        int.TryParse(f[19], out ldFlag);
        leadName = f[2]; ldSeen = time;
        mySlot = 0;
        string me = IGC.Me.ToString() + ":";
        foreach (var e in f[20].Split(',')) if (e.StartsWith(me)) int.TryParse(e.Substring(me.Length), out mySlot);
    }
    var u = IGC.UnicastListener;
    while (u.HasPendingMessage)
    {
        var m = u.AcceptMessage();
        if (!leading || m.Tag != FormJoin) continue;
        var f = (m.Data as string ?? "").Split('|');
        Member mb;
        if (f[0] == "Q")
        {
            if (members.TryGetValue(m.Source, out mb)) { Log("formation: " + mb.Name + " left", true); members.Remove(m.Source); lastStatus = null; }
            continue;
        }
        if (f[0] != "J" || f.Length < 4) continue;
        if (!members.TryGetValue(m.Source, out mb))
        {
            if (members.Count >= MaxMembers) continue;
            // the lowest slot free
            int slot = 1; bool taken = true;
            while (taken) { taken = false; foreach (var x in members.Values) if (x.Slot == slot) { taken = true; slot++; break; } }
            members[m.Source] = mb = new Member { Slot = slot };
            Log("formation: " + f[1] + " joined, slot " + slot, true);
            lastStatus = null;
        }
        mb.Name = f[1]; mb.Accel = Nav.D(f[2]); mb.Cap = Nav.D(f[3]); mb.Seen = time;
    }
}

void Cast() { Cast(-1); }

/// <summary>Leading: the pace (the slowest ship heard from), and twice a second this ship's state over the antenna.
/// flag: -1 = as it is (0 on a route, 1 arrived, 3 anything else), 2 = the formation is over (sent at once).</summary>
void Cast(int flag)
{
    // ships not heard from are dropped; the slowest of the rest sets the pace
    gone.Clear(); fleetAccel = 0; fleetCap = 0;
    foreach (var kv in members)
    {
        if (time - kv.Value.Seen > MemberTimeout) { gone.Add(kv.Key); continue; }
        if (kv.Value.Accel > 0 && (fleetAccel <= 0 || kv.Value.Accel < fleetAccel)) fleetAccel = kv.Value.Accel;
        if (kv.Value.Cap > 0 && (fleetCap <= 0 || kv.Value.Cap < fleetCap)) fleetCap = kv.Value.Cap;
    }
    foreach (var id in gone) { Log("formation: " + members[id].Name + " dropped (not heard for " + MemberTimeout.ToString("0") + " s)", true); members.Remove(id); lastStatus = null; }
    if (flag < 0 && runs - castRun < 3) return;
    castRun = runs;
    var c = Controller();
    if (c == null) { if (!Blocks()) return; c = Controller(); if (c == null) return; }
    Vector3D p, v, a = Vector3D.Zero, d;
    if (flying && curRun == runs) { p = curP; v = curV; a = curAcc; d = curDrive; }
    else
    {
        // not under the autopilot this run (idle, or the pilot flying): as the ship is, no burn told
        p = c.CenterOfMass; v = velocity.Update(p, c.GetShipVelocities().LinearVelocity, time);
        d = drives.Count > 0 ? drives[0].WorldMatrix.Backward : c.WorldMatrix.Forward;
        double mass = c.CalculateShipMass().PhysicalMass, thrust = 0;
        foreach (var t in drives) if (t.IsWorking) thrust += t.MaxEffectiveThrust;
        if (mass > 0 && thrust > 0 && fleetAccel > 0) fleetShare = Math.Min(1, fleetAccel * LeadShare / (thrust / mass));
        else if (fleetAccel <= 0) fleetShare = 0;
    }
    // the formation's axis: the way the route runs from here (kept over the last 5 km), else the way the ship moves
    if (flying && mode == "route" && idx < route.Count) { var to = route[idx].Pos - p; if (to.Length() > 5000) leadAxis = Vector3D.Normalize(to); }
    else if (v.Length() > 50) leadAxis = Vector3D.Normalize(v);
    if (flag < 0) flag = flying && mode == "route" ? 0 : state == "arrived" ? 1 : 3;
    var sb = new StringBuilder("L|").Append(DateTime.UtcNow.Ticks).Append('|').Append(ShipName());
    Put(sb, p, "0"); Put(sb, v, "0.00"); Put(sb, a, "0.000"); Put(sb, d, "0.0000"); Put(sb, leadAxis, "0.0000");
    sb.Append('|').Append(N(formSpacing, "0")).Append('|').Append(flag).Append('|');
    bool first = true;
    foreach (var kv in members) { if (!first) sb.Append(','); first = false; sb.Append(kv.Key).Append(':').Append(kv.Value.Slot); }
    IGC.SendBroadcastMessage(formTag, sb.ToString(), TransmissionDistance.AntennaRelay);
}

string ApMsg()
{
    if (mode == "match") return apKeeping ? "Matched with " + apName + ": holding " + Nav.Km(apKeep) + " or more" : "Matching " + apName + ", range " + Nav.Km(apKeep) + " or more";
    return "Closing to " + apKeep.ToString("0") + " m from " + apName;
}

void Fly()
{
    var c = Controller();
    if (c == null || drives.Count == 0) { Release("error", "Lost the cockpit or the drives", true); return; }
    // manual override: movement or roll input in a seat, turn input on two runs in a row (a lone mouse jump, as when
    // a menu opens, isn't one; once the pilot has the ship, any), or the dampeners switched on
    bool input = false;
    if (time - startedAt > 0.5)
    {
        bool turning = false;
        foreach (var s in controllers)
        {
            if (!s.IsUnderControl) continue;
            if (s.MoveIndicator.LengthSquared() > 0.01 || Math.Abs(s.RollIndicator) > 0.01) input = true;
            if (s.RotationIndicator.LengthSquared() > 0.01) turning = true;
        }
        turnTicks = turning ? turnTicks + 1 : 0;
        // on a slow coast the runs are 1.7 s apart: turn input seen once is the pilot's
        input |= turnTicks >= 2 || turning && (handStart >= 0 || Runtime.UpdateFrequency == UpdateFrequency.Update100);
    }
    else turnTicks = 0;
    bool damp = c.DampenersOverride && time - startedAt > 0.5 && mode != "prograde" && mode != "retrograde";
    if (input && mode != "route") { turnTicks = 0; Release("override", "Pilot took the controls", false); return; }
    if (damp && mode != "route") { Release("override", "Dampeners switched on: pilot has the controls", false); return; }
    if (input || damp || handStart >= 0) Pace(false);
    if (input || damp)
    {
        // a route: let go while the pilot flies. Held TakeoverAfter s (or the dampeners switched on) it's a takeover,
        // but it pauses and asks only once the pilot's input has stopped (no pop-up in the middle of a manoeuvre)
        if (input || handStart < 0) lastInput = time;
        if (handStart < 0)
        {
            handStart = time; handHeld = false;
            foreach (var t in drives) t.ThrustOverridePercentage = 0f;
            foreach (var gy in gyros) { gy.Pitch = 0; gy.Yaw = 0; gy.Roll = 0; gy.GyroOverride = false; }
            msg = "Pilot flying: the autopilot takes back when you let go";
            Log("pilot input: let go", true);
            WriteStatus();
        }
        if (!handHeld && (damp || time - handStart >= takeoverAfter))
        {
            handHeld = true; handWhy = damp ? "Dampeners switched on: pilot has the controls" : "Pilot took the controls";
            msg = "Pilot flying: the autopilot asks once you let go";
            Log("pilot input: taken over, asking once it stops", true);
            WriteStatus();
        }
        if (input) return;
    }
    if (handStart >= 0)
    {
        if (time - lastInput < (handHeld ? QuietFor : InputGap)) return; // between key taps or moves: still the pilot's
        if (handHeld) { handStart = -1; handHeld = false; turnTicks = 0; TakeOver(handWhy); return; }
        Log("pilot input ended after " + (lastInput - handStart).ToString("0.0") + " s: autopilot back", true);
        handStart = -1;
        velocity.Reset();
        msg = "Back on the route";
    }

    var p = c.CenterOfMass;
    var v = velocity.Update(p, c.GetShipVelocities().LinearVelocity, time);
    settings.PhysMax = Math.Max(settings.PhysMax, c.GetShipVelocities().LinearVelocity.Length());
    var g = c.GetNaturalGravity();
    double mass = c.CalculateShipMass().PhysicalMass, thrust = 0;
    foreach (var t in drives) if (t.IsWorking) thrust += t.MaxEffectiveThrust;
    if (mass <= 0 || thrust <= 0) { Release("error", "No working drives", true); return; }
    double aFull = thrust / mass; // the route panel's caps are shares of this (burn, brake)
    // low water (a route only; LowWater=0 turns it off): every 2 s, what the tanks hold against what stopping from this
    // speed burns. Down to that with a little over, it brakes to rest now, while it still can
    water.Burnt(lastThr, time - flownAt); flownAt = time;
    if (mode == "route" && lowWater && tanks.Count > 0 && time - waterAt >= 2)
    {
        waterAt = time;
        double litres = 0;
        foreach (var k in tanks) litres += k.FilledRatio * k.Capacity;
        if (water.Low(litres, v.Length(), aFull))
        {
            string why = "Low water: " + Nav.Litres(litres) + " left, about what stopping from " + Nav.Kms(v.Length()) + " burns. Braking to a stop";
            Turn("brake");
            if (flying) { msg = why; Log(why, true); lastStatus = null; WriteStatus(); }
            return;
        }
    }
    var drive = drives[0].WorldMatrix.Backward; // thrusters push the grid along their Backward
    // learn how fast the ship really turns (weak RCS gyros), so it flips early enough
    if (last.W.Length() >= settings.TurnRate * 0.99 && !settings.Kicking)
        settings.Observed = Math.Max(settings.Observed, c.GetShipVelocities().AngularVelocity.Length());
    settings.AxisUp = drives[0].WorldMatrix.Up; settings.AxisLeft = drives[0].WorldMatrix.Left;
    settings.Learn(last.W, c.GetShipVelocities().AngularVelocity, time);

    if (mode == "approach" || mode == "match")
    {
        // where the ship is now: as WeaponCore sees it while the lock holds, else along its last drift
        MyDetectedEntityInfo t;
        bool seen = Focus(out t) && t.EntityId == apId;
        if (seen)
        {
            apPos = t.Position; apVel = apTrack.Update(apPos, t.Velocity, time); apSeen = time;
            apRadius = (t.BoundingBox.Max - t.BoundingBox.Min).Length() / 2;
        }
        if (seen == apLost)
        {
            apLost = !seen;
            msg = seen ? ApMsg() : "Lock on " + apName + " lost: flying on along its last drift";
            Log(msg, true);
        }
        var tp = seen ? apPos : apPos + apVel * (time - apSeen);
        double cap = settings.MaxSpeed > 0 ? Math.Min(settings.MaxSpeed, routeMax) : routeMax;
        if (mode == "match")
        {
            last = Nav.Match(p, v, g, drive, aFull, tp, apVel, apKeep + apRadius, cap * 0.98, apRoute, settings);
            last.Dist = Vector3D.Distance(p, tp) - apRadius; // shown: the range to its hull
            if (settings.Keeping != apKeeping) { apKeeping = settings.Keeping; if (!apLost) msg = ApMsg(); Log(apKeeping ? "matched at " + last.Dist.ToString("0") + " m" : "correcting at " + last.Dist.ToString("0") + " m, " + last.SpeedNow.ToString("0.0") + " m/s against it", true); }
        }
        else last = Nav.Approach(p, v, g, drive, aFull, tp, apVel, apKeep + apRadius, cap * 0.98, apRoute, settings);
        if (last.Arrived)
        {
            // a ship lying still: dampeners hold you there. One drifting: they'd stop you dead while it drifts on
            bool still = apVel.Length() < 2;
            Release("arrived", (Vector3D.Distance(p, tp) - apRadius).ToString("0") + " m from " + apName
                + (still ? ": dampeners on" : ", matched to its " + Nav.Speed(apVel.Length()) + " drift: dampeners off"), still);
            return;
        }
    }
    else if (mode == "form")
    {
        // the leader hears from this ship every 2 s: its name, what its drives can do, its speed cap
        if (time - helloAt > 2) { helloAt = time; IGC.SendUnicastMessage(leadId, FormJoin, "J|" + ShipName() + "|" + N(aFull, "0.000") + "|" + N(settings.MaxSpeed, "0")); }
        double age = time - ldSeen;
        if (ldFlag == 2 && age < LinkLost) { Release("override", leadName + " ended the formation: you have the controls", false); return; }
        if (mySlot == 0 || ldSeen < 0)
        {
            // no slot yet: wait for the leader's answer
            last = new Nav.Cmd { SpeedNow = v.Length() };
            if (time - startedAt > 10 && !ldLost) { ldLost = true; msg = "No answer from " + leadName + ": is the antenna link up?"; Log(msg, true); }
        }
        else
        {
            bool lost = age > LinkLost;
            if (lost != ldLost) { ldLost = lost; msg = lost ? "Link to " + leadName + " lost: flying on along its last course" : "Link to " + leadName + " back"; Log(msg, true); }
            // where the leader is now: on from what it last said, its burn kept up for 3 s at most
            double te = Math.Min(age, 3);
            var tv = ldVel + ldAcc * te;
            var tp = ldPos + ldVel * age + ldAcc * (te * age - te * te / 2) + Nav.Slot(ldAxis, mySlot, ldSpacing);
            double cap = settings.MaxSpeed > 0 ? Math.Min(settings.MaxSpeed, routeMax) : routeMax;
            last = Nav.Formation(p, v, g, drive, aFull, tp, tv, age < 3 ? ldAcc : Vector3D.Zero, ldDrive, cap * 0.98, apRoute, settings);
            double off = Vector3D.Distance(p, tp);
            last.Dist = last.Remaining = off;
            if (ldFlag == 1 && !lost && off < Nav.FormReach && last.SpeedNow < 2)
            {
                bool still = tv.Length() < 2;
                Release("arrived", "In slot " + mySlot + " beside " + leadName + " at its stop" + (still ? ": dampeners on" : ""), still);
                return;
            }
            if (!lost) msg = "Slot " + mySlot + " on " + leadName + (off > Nav.FormReach ? ": " + Nav.Km(Math.Round(off / 100) * 100) + " off station" : ": on station");
        }
    }
    else if (mode != "route")
    {
        // FULL STOP: under DampenBelow the dampeners finish the stop (drives off, dampeners on, pilot has the ship)
        if (mode == "brake" && v.Length() < DampenBelow) { Release("stopped", "Under " + DampenBelow.ToString("0") + " m/s: dampeners on", true); return; }
        last = Nav.Face(mode, v, g, drive, aFull, settings);
        if (last.Arrived) { Release("stopped", mode == "brake" ? "Stopped: dampeners on" : "Too slow to tell " + mode + " (under 1 m/s)", mode == "brake"); return; }
    }
    else
    {
        double cap = settings.MaxSpeed > 0 ? Math.Min(settings.MaxSpeed, routeMax) : routeMax;
        KeepOffGround(c, g);
        // leading a formation: no harder than LeadShare of what the slowest ship's drives can do (it keeps the rest
        // to correct with), and no faster than the lowest speed cap
        double burnCap = thrustCap, stopCap = brakeCap;
        fleetShare = leading && fleetAccel > 0 ? Math.Max(0.02, Math.Min(1, fleetAccel * LeadShare / aFull)) : 0;
        if (fleetShare > 0) { burnCap = Math.Min(burnCap, fleetShare); stopCap = Math.Min(stopCap, fleetShare); }
        if (leading && fleetCap > 0) cap = Math.Min(cap, fleetCap);
        last = Nav.Steer(p, v, g, drive, aFull, burnCap, stopCap, route, idx, cap * 0.98, settings);
        if (last.Idx != idx) { idx = last.Idx; msg = idx < route.Count ? Heading(route[idx]) : ""; Log("point " + (idx + 1) + " of " + route.Count + ": " + msg, false); }
        if (last.Arrived) { Release("arrived", "Arrived at " + target, true); return; }
        // braking above the cap to make the stop: say so while it lasts (and when even all of it won't do)
        bool over = last.Over > 0;
        if (over)
        {
            string m = last.Over > 0.995 ? "Too fast to stop in time: braking at 100%, will overshoot" : "Braking at " + Math.Ceiling(last.Over * 20) * 5 + "%: over the " + (brakeCap * 100).ToString("0") + "% cap to make the stop";
            if (m != msg) { if (!wasOver || m.StartsWith("Too") != msg.StartsWith("Too")) Log("over cap: " + m + " at " + v.Length().ToString("0") + " m/s", true); msg = m; }
        }
        else if (wasOver) { msg = idx < route.Count ? Heading(route[idx]) : ""; Log("back within the brake cap at " + v.Length().ToString("0") + " m/s", true); }
        wasOver = over;
    }

    // stuck under Flip and Burn's turn clamp: kick it (Settings.Unstick)
    last.W = settings.Unstick(last.W, c.GetShipVelocities().AngularVelocity, drive, drives[0].WorldMatrix.Up);
    // every axis tried twice and still dead still: no gyro override can turn it at this speed (a pilot's own turn input can)
    if (settings.Stuck == 15) { msg = "Not turning: gyros too weak for Flip and Burn's turn clamp. Add gyros, or turn it by hand"; Log(msg, true); }
    if (settings.Kicking && settings.Stuck == 3) Log("stuck: asked " + last.W.Length().ToString("0.00") + " rad/s, not turning; kick " + "RUL"[settings.Kicks % 3], true);

    // Rounded (Nav.Throttle, Nav.Rate) so the values only change, and get synced to every client, when the guidance
    // really changes: unrounded, nearly every run sent one update per drive and per gyro.
    float thr = Nav.Throttle(last.Throttle);
    foreach (var t in drives) t.ThrustOverridePercentage = thr;
    lastThr = thr;
    curP = p; curV = v; curDrive = drive; curAcc = drive * (thr * aFull); curRun = runs; // what a formation is told
    foreach (var gy in gyros)
    {
        var local = Vector3D.TransformNormal(last.W, MatrixD.Transpose(gy.WorldMatrix));
        gy.GyroOverride = true; gy.GyroPower = 1f;
        gy.Pitch = -Nav.Rate(local.X); gy.Yaw = -Nav.Rate(local.Y); gy.Roll = -Nav.Rate(local.Z);
    }
    if (++logRuns >= LogEvery) { logRuns = 0; Sample(c, v, drive, thr); }
    // a long coast on a route: nothing to steer or burn and nothing due for CoastQuiet s, so run at Update100 until
    // then. Not while leading (the formation is told where this ship is every few runs) or in gravity
    Pace(mode == "route" && !leading && thr == 0 && last.W == Vector3D.Zero && !settings.Kicking
        && last.Quiet > CoastQuiet && g.LengthSquared() < 1e-6);
}

const double CoastQuiet = 30; // s: back to Update10 this long before the flip, a bend or the next point

/// <summary>Flying: every 100 ticks on a long coast (slow), else every 10.</summary>
void Pace(bool slow)
{
    var f = slow ? UpdateFrequency.Update100 : UpdateFrequency.Update10;
    if (Runtime.UpdateFrequency == f) return;
    Runtime.UpdateFrequency = f;
    Log(slow ? "coast: Update100, " + last.Quiet.ToString("0") + " s clear" : "coast over: Update10", false);
}

// ------------------------------------------------------------------ turn log

const int LogLines = 100, LogEvery = 18; // 18 runs at Update10 = 3 s

/// <summary>One line of the turn log: what was asked of the gyros against what the ship really did.</summary>
void Sample(IMyShipController c, Vector3D v, Vector3D drive, float thr)
{
    if (!logOn) return;
    var w = c.GetShipVelocities().AngularVelocity;
    int working = 0, over = 0; double set = 0;
    foreach (var gy in gyros) { if (gy.IsWorking) working++; if (gy.GyroOverride) over++; }
    if (gyros.Count > 0) { var g0 = gyros[0]; set = Math.Sqrt(g0.Pitch * g0.Pitch + g0.Yaw * g0.Yaw + g0.Roll * g0.Roll); }
    bool seated = false; double rot = 0;
    foreach (var s in controllers) if (s.IsUnderControl) { seated = true; rot = Math.Max(rot, s.RotationIndicator.Length()); }
    double sp = v.Length();
    Log(mode + " pt=" + (idx + 1) + " v=" + sp.ToString("0")
        + " face=" + (sp > 1 ? Vector3D.Dot(drive, v / sp).ToString("+0.00;-0.00") : "-")
        + " ask=" + last.W.Length().ToString("0.00") + " set=" + set.ToString("0.00") + " turn=" + w.Length().ToString("0.0000")
        + " thr=" + thr.ToString("0.00") + " gyros=" + working + "/" + gyros.Count + " ov=" + over
        + " seat=" + (seated ? "y" : "n") + " rot=" + rot.ToString("0.00") + " damp=" + (c.DampenersOverride ? "on" : "off")
        + " obs=" + settings.Observed.ToString("0.000") + " acc=" + settings.Accel.ToString("0.000") + " up=" + settings.AccelUp.ToString("0.000") + " left=" + settings.AccelLeft.ToString("0.000") + " fast=" + "L-U"[settings.FastAxis() + 1] + " stuck=" + settings.Stuck + " ms=" + Runtime.LastRunTimeMs.ToString("0.00"), true);
}

/// <summary>Adds a line (clock time first, to match the game log) and, if <paramref name="write"/>, saves the log to Custom Data.</summary>
void Log(string line, bool write)
{
    if (!logOn) return;
    turnLog.Add(DateTime.Now.ToString("HH:mm:ss") + " " + line);
    while (turnLog.Count > LogLines) turnLog.RemoveAt(0);
    if (!write) return;
    var sb = new StringBuilder(LogHeader).Append('\n');
    foreach (var l in turnLog) sb.Append(l).Append('\n');
    Me.CustomData = Merge(Me.CustomData, sb.ToString(), LogHeader);
    lastCd = Me.CustomData;
}

/// <summary>Lets go of everything; dampeners back on when it stopped by itself (arrived or an error).</summary>
// what the PB says it's doing ("Curving to X" for a curve point, else "Flying to X")
/// <summary>
/// In a gravity well: the stop at the end of the route kept GroundClear over the ground (its height from the ship's own
/// surface reading), lifted straight up when it's lower. Only ever raised. A route that stopped on a station in a
/// world's hills (an older plugin's, or a GPS) flew a hauler into Pallas's ground on 2026-11-19.
/// </summary>
void KeepOffGround(IMyShipController c, Vector3D g)
{
    if (route.Count == 0 || g.LengthSquared() < 0.0025) return;
    var end = route[route.Count - 1];
    Vector3D centre; double elev;
    if (!c.TryGetPlanetPosition(out centre) || !c.TryGetPlanetElevation(MyPlanetElevation.Surface, out elev)) return;
    double ground = Vector3D.Distance(c.CenterOfMass, centre) - elev, at = Vector3D.Distance(end.Pos, centre);
    if (ground <= 0 || at >= ground + GroundClear - 100) return; // (100 m of play: not a log line each run as the ground under the ship changes)
    var up = Vector3D.Normalize(end.Pos - centre);
    end.Pos = centre + up * (ground + GroundClear);
    route[route.Count - 1] = end;
    Log("stop raised " + (ground + GroundClear - at).ToString("0") + " m: it was " + (at - ground).ToString("0") + " m over the ground", true);
}

/// <summary>Least height of the route's stop over a world's ground (m).</summary>
const double GroundClear = 1500;

string Heading(Nav.Wp w) { return w.Kind == "curve" ? w.Name : "Flying to " + w.Name; }

/// <summary>The pilot's input: a route pauses (let go, route held until manual control is confirmed or it resumes),
/// a hotbar turn or full stop ends.</summary>
void TakeOver(string why)
{
    if (mode != "route") { Release("override", why, false); return; }
    Release("manual", why, false);
    paused = true;
    Save();
}

void Release(string newState, string why, bool dampeners)
{
    bool was = flying || paused;
    if (flying && mode == "form" && leadId != 0) { try { IGC.SendUnicastMessage(leadId, FormJoin, "Q"); } catch { } } // the slot is free again
    flying = false; paused = false; handStart = -1; state = newState; msg = why; wasOver = false;
    foreach (var t in drives) t.ThrustOverridePercentage = 0f;
    foreach (var gy in gyros) { gy.Pitch = 0; gy.Yaw = 0; gy.Roll = 0; gy.GyroOverride = false; }
    if (dampeners && was) { var c = Controller(); if (c != null) c.DampenersOverride = true; }
    Runtime.UpdateFrequency = leading ? UpdateFrequency.Update10 : UpdateFrequency.Update100;
    if (was) Log(newState + ": " + why, true);
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
    // leading a formation: the ships in it, and the share of this ship's thrust the slowest holds it to (0 = none)
    if (leading) sb.Append("fleet=").Append(members.Count).Append(',').Append(N(fleetShare, "0.00")).Append(',').Append(N(fleetCap, "0")).Append('\n');
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
        public double FlipBuffer = 10; // s added to the planned flip time at each stop (flip and brake a little sooner)
        // s the braking burn starts early at speed; s held back before each flip for a short takeover (TakeoverAfter)
        public double BurnLead = 2, Reserve = 5;
        // the game's own top speed (m/s, 0 = not seen yet): the fastest physics speed seen, which Flip and Burn holds
        // the physics at while it moves the ship faster itself
        public double PhysMax;
        public bool Burning;    // correcting now (the dead band is narrower until it's done, so it doesn't stutter)
        public double Observed; // fastest turn seen while asking for full rate (0 = not yet)
        public bool Holding;    // a hotbar turn has had a direction to hold
        public double ArriveSpeed = 10; // m/s: inside the last point's reach and under this, it has arrived
        public bool Keeping;    // match: matched and inside the band, the drives resting
        // Flip and Burn, above physics speed with nobody turning the ship from a seat, damps any turn (angular damping
        // 0.5, so a turn tops out at Accel / 0.5) and zeroes a turn under 0.001 rad/s every tick
        public const double FbDamping = 0.5;
        public double FlipRate
        {
            get
            {
                double r = Observed > 0.02 ? Math.Min(TurnRate, Observed) : TurnRate;
                return Math.Min(r, TurnAccel / FbDamping);
            }
        }
        // That zeroing traps a heavy ship whose gyros can't add 0.001 rad/s in one tick about the axis asked (under
        // about 0.06 rad/s²): asked to turn, it never starts. Stuck counts runs asking for a turn with the ship dead
        // still; Unstick then kicks it about one axis at a time, two runs each (roll first: a long ship turns fastest
        // about its length, the gyros' torque being the same whichever way), and once it's turning at all the turn
        // asked for takes over. Kicks stays on the axis that worked last.
        public int Stuck, Kicks;
        public bool Kicking;    // this run's turn is a kick (not learnt from: it's another axis)
        public const double KickFrom = 0.1; // rad/s asked: below this, no kicks
        public Vector3D Unstick(Vector3D w, Vector3D wNow, Vector3D roll, Vector3D up)
        {
            bool still = Math.Abs(wNow.X) < 0.0015 && Math.Abs(wNow.Y) < 0.0015 && Math.Abs(wNow.Z) < 0.0015;
            Kicking = false;
            // only a real turn (a flip, a new leg): a small aim correction (under ~5 deg, asking under 0.1 rad/s)
            // that Flip and Burn zeroes costs next to nothing, and kicking for it rolled the ship every few seconds
            // while lined up and braking (2026-11-18 19:15 Andromeda log)
            if (!still || w.Length() < KickFrom) { Stuck = 0; return w; }
            if (++Stuck < 3) return w;
            if (Stuck > 3 && Stuck % 2 == 1) Kicks++;
            Kicking = true;
            int k = Kicks % 3;
            var axis = k == 0 ? roll : k == 1 ? up : Vector3D.Cross(roll, up);
            return Vector3D.Normalize(axis) * TurnRate;
        }
        // how fast the gyros really speed up / slow the turn (rad/s², 0 = not seen yet), learnt while they're
        // still short of the asked rate; until then a guess on the weak side (RCS gyros)
        public double Accel;
        public double TurnAccel { get { return Accel > 0 ? Accel : 0.15; } }
        Vector3D wPrev; double tPrev = -1;
        /// <summary>A new reading into a learnt acceleration: the highest seen, but one over 3 times what's been
        /// seen so far only moves it a third of the way (a glitch doesn't take over).</summary>
        static double Fold(double was, double seen)
        {
            seen = Math.Min(5, seen);
            if (was <= 0) return seen;
            return seen > was * 3 ? was + (seen - was) / 3 : Math.Max(was, seen);
        }
        /// <summary>Learn the gyros' angular acceleration from the rate asked last run (asked) and the rate now.</summary>
        public void Learn(Vector3D asked, Vector3D wNow, double time)
        {
            if (Kicking) { tPrev = -1; return; }
            double dt = time - tPrev;
            // speeding a turn up only: slowing one, Flip and Burn's damping and zeroing help the gyros
            if (tPrev >= 0 && dt > 0.05 && dt < 1 && (asked - wPrev).Length() > 0.1
                && asked.Length() > wPrev.Length() && Vector3D.Dot(asked, wPrev) >= 0)
            {
                // only the change toward the rate asked: Flip and Burn zeroing or damping a turn is a fast change too,
                // and once read as the gyros' speed it made every turn and flip plan far too quick (0.52 rad/s² seen
                // on a hauler that turns at 0.06-0.10)
                var ask = Vector3D.Normalize(asked - wPrev);
                double seen = Vector3D.Dot(wNow - wPrev, ask) / dt;
                if (seen > 0.01)
                {
                    Accel = Fold(Accel, seen);
                    // a turn about (nearly) one of the flip axes: that axis's own speed
                    if (Math.Abs(Vector3D.Dot(ask, AxisUp)) > 0.9) AccelUp = Fold(AccelUp, seen);
                    else if (Math.Abs(Vector3D.Dot(ask, AxisLeft)) > 0.9) AccelLeft = Fold(AccelLeft, seen);
                }
            }
            wPrev = wNow; tPrev = time;
        }
        // A flip (the drives a long way round, as from prograde to retrograde) can go about any axis across the drives,
        // and a ship turns faster about one than the other (the gyros' torque is the same, the hull's inertia isn't):
        // AxisUp / AxisLeft are the drives' Up and Left (world, set each run), AccelUp / AccelLeft how fast it's been
        // seen to turn about each (0 = not yet), and until both are seen the hull's size says which should be faster
        // (about Up it swings its length and width, about Left its length and height).
        public Vector3D AxisUp, AxisLeft;
        public double AccelUp, AccelLeft, SizeFwd, SizeUp, SizeLeft;
        const double FlipFrom = 2.7; // rad (155 deg): past this the turn is a flip and takes the faster axis
        /// <summary>+1 Up is the faster flip axis, -1 Left, 0 no telling.</summary>
        public int FastAxis()
        {
            if (AccelUp > 0 && AccelLeft > 0) return AccelUp > AccelLeft * 1.1 ? 1 : AccelLeft > AccelUp * 1.1 ? -1 : 0;
            double iUp = SizeFwd * SizeFwd + SizeLeft * SizeLeft, iLeft = SizeFwd * SizeFwd + SizeUp * SizeUp;
            if (iUp <= 0 || iLeft <= 0) return 0;
            return iUp < iLeft * 0.9 ? 1 : iLeft < iUp * 0.9 ? -1 : 0;
        }
        /// <summary>The axis to turn the drives from drive toward want (angle apart) about: the shortest way, or for
        /// a flip the faster axis, turned the way that brings them round toward want.</summary>
        public Vector3D TurnAxis(Vector3D drive, Vector3D want, double angle)
        {
            int fast = angle > FlipFrom && AxisUp.LengthSquared() > 0.5 ? FastAxis() : 0;
            if (fast != 0)
            {
                var a = fast > 0 ? AxisUp : AxisLeft;
                a -= drive * Vector3D.Dot(a, drive);
                if (a.LengthSquared() > 0.25)
                {
                    a = Vector3D.Normalize(a);
                    return Vector3D.Dot(Vector3D.Cross(a, drive), want) >= 0 ? a : -a;
                }
            }
            var axis = Vector3D.Cross(drive, want);
            if (axis.LengthSquared() < 1e-10) axis = Vector3D.CalculatePerpendicularVector(drive);
            return Vector3D.Normalize(axis);
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
        public double Quiet;    // coasting with nothing to do: seconds until the flip, a bend or the next point is due (0 = busy)
        public double Over;     // braking above the route's brake cap to make the stop: the share in use (0 = within the cap)
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
    // down. Throttle comes out as a share of the whole thrust, never above the cap for what it's doing, but for a
    // stop the capped brake can't make (Cmd.Over).
    public const double OverAt = 1.03; // this far over the speed the capped brake can stop from, it brakes above the cap
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
        if (last && d < wp.Arrive && cmd.SpeedNow < s.ArriveSpeed) { cmd.Arrived = true; return cmd; } // dampeners take the rest

        // speed we can still stop from: v*tFlip + v^2/(2a) = s
        double a = aBrake * s.BrakeMargin - Math.Max(0, Vector3D.Dot(g, dir));
        a = Math.Max(a, aBrake * 0.1);
        // speed across the line has to go too: braking it all at once takes |v| / a, and only the along share of the
        // thrust slows the ship toward the stop, so the stop is planned on that share (a route changed at speed left
        // 8 km/s across the line at 21 km/s on the 2026-11-19 Pallas approach, and it stopped 94 km past)
        double vIn = Math.Max(0, Vector3D.Dot(v, dir)), vAcross = Vector3D.Reject(v, dir).Length();
        double aStop = vIn > 1 && vAcross > 1 ? a * Math.Max(0.3, vIn / Math.Sqrt(vIn * vIn + vAcross * vAcross)) : a;
        // the time to turn the drives from where they point now to braking (against dir) and start the burn: a whole
        // flip plus FlipBuffer when facing ahead, next to nothing once already facing back. Kept at a whole flip
        // after the flip, it held the speed at about distance / flip time, so it crept in, losing the same share
        // of its speed every second (the 2026-11-18 17:50 Hygeia approach: 894 to 79 m/s over 5 minutes)
        double back = Math.Acos(Math.Max(-1, Math.Min(1, -Vector3D.Dot(drive, dir))));
        // before the flip, room for a short takeover too (Reserve: the pilot can hold the controls that long before it
        // pauses) so a few seconds by hand don't eat into the stop. The burn starts BurnLead s early at speed, fading
        // to nothing slow (a lead held to the end makes it creep in), so the stop needs a little less than the plan
        double early = s.BurnLead * Math.Min(1, cmd.SpeedNow / 1000);
        double tFull = Math.PI / s.FlipRate + 3 + s.FlipBuffer + s.Reserve + early;
        double tFlip = back / s.FlipRate + 2 + early + Math.Min(1, back / 0.5) * (1 + s.FlipBuffer + s.Reserve);
        double sAvail = Math.Max(0, stopDist - stopWp.Arrive * 0.4);
        double vStop = aStop * (-tFlip + Math.Sqrt(tFlip * tFlip + 2 * sAvail / aStop));
        double vFlipped = aStop * (-tFull + Math.Sqrt(tFull * tFull + 2 * sAvail / aStop)); // after turning round to face ahead
        double speed = Math.Min(cap, vStop);
        // how long a coast at this speed stays one: to the next point, to where the stop's flip is due
        double quiet = vIn > 1 ? Math.Min(d / cmd.SpeedNow, (sAvail - vIn * tFlip - vIn * vIn / (2 * aStop)) / vIn) : 0;
        // Faster than the capped brake can stop from (engaged at speed, or the route changed): brake above the cap,
        // as hard as the stop needs and no harder, and back under it once the speed is down to the plan. Kept to the
        // cap, a route engaged at 24.7 km/s 2,700 km from Deimos braked at 54%, and went 230 km past (2026-11-19).
        double brakeNow = brakeCap;
        if (brakeCap < 1 && vIn > vStop * OverAt && vIn > 100)
        {
            double room = sAvail - vIn * tFlip;
            double need = room > 1 ? vIn * vIn / (2 * room) / aStop : 1e9; // against the planned slowing
            if (need > 1) { brakeNow = Math.Min(1, brakeCap * need); cmd.Over = brakeNow; }
        }
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
            if (vIn > 1) quiet = Math.Min(quiet, (D - vIn * tFlip - Math.Max(0, vIn * vIn - vL * vL) / (2 * a)) / vIn); // to slowing or turning for it
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
        // facing back (braking) and slower than the plan, as when Flip and Burn drops the speed to the game's own
        // limit near a stop: turn round to speed up only if that saves more time than the two flips it costs
        // (round and back again to brake), else coast on at this speed and brake when the plan comes down to it
        else if (speed > vAlong && vAlong > 1 && Vector3D.Dot(drive, aimDir) < 0)
        {
            // Flip and Burn won't go over the game's own top speed (PhysMax) within 5 km of a station or any other
            // static grid, and a stop at a station is 3 km short of it: near a stop, that's as fast as it can go
            // once turned round, the plan holds a whole flip back again: no faster than vFlipped
            double lim = Math.Min(speed, vFlipped);
            if (s.PhysMax > 1 && stopDist < FbZone) lim = Math.Min(lim, s.PhysMax);
            bool atLimit = s.PhysMax > 1 && stopDist < FbZone && vAlong > s.PhysMax * 0.85;
            double flip = Math.PI / s.FlipRate + 3;
            // coasting on, then braking at the end, against two flips (coasting through them) and a burn up and back
            double tSlow = stopDist / vAlong + vAlong / (2 * a);
            double D2 = Math.Max(0, stopDist - vAlong * 2 * flip);
            double vp = Math.Min(lim, Math.Sqrt((2 * aPlan * a * D2 + a * vAlong * vAlong) / (aPlan + a)));
            double tFast = 2 * flip + (vp > vAlong
                ? (vp - vAlong) / aPlan + vp / a + Math.Max(0, D2 - (vp * vp - vAlong * vAlong) / (2 * aPlan) - vp * vp / (2 * a)) / vp
                : D2 / vAlong + vAlong / a);
            // worth it only for a clear gain (the flip times are estimates); already near the game's top speed inside
            // the zone, never (the 2026-11-18 17:25 approach turned round at 86 m/s for the last few m/s)
            if (atLimit || vFlipped < vAlong * 1.2 || tFast > tSlow - Math.Max(10, tSlow * 0.2)) speedSet = vAlong;
        }
        var vWant = aimDir * speedSet;
        var acc = (vWant - v) / 3.0 - g;
        double am = acc.Length();

        // where the drives should point: along acc; hold still when there's nothing to do. Fast, small errors
        // (under 0.4 % of the speed) are left until they grow, and a correction runs on until it's a third of that
        bool braking = cmd.SpeedNow > 1 && Vector3D.Dot(acc, v) < 0;
        double capNow = braking ? brakeNow : burnCap;
        double band = Math.Max(aFull * capNow * 0.01, cmd.SpeedNow * 0.004 / 3.0) * (s.Burning ? 0.33 : 1);
        if (am < band) { cmd.W = Vector3D.Zero; cmd.Throttle = 0; s.Burning = false; cmd.Quiet = Math.Max(0, quiet); }
        else
        {
            s.Burning = true;
            var want = acc / am;
            double cos = MathHelper.Clamp(Vector3D.Dot(drive, want), -1, 1), angle = Math.Acos(cos);
            cmd.W = s.TurnAxis(drive, want, angle) * s.TurnCmd(angle);
            // burn while the drives swing onto a new direction, fading out by about 35 degrees off: cutting at 15 lost
            // 12 s of braking at 21 km/s as the push swung from sideways to back (2026-11-19 Pallas, 94 km over)
            double aim = angle < 0.05 ? 1 : angle < AimFade ? (AimFade - angle) / (AimFade - 0.05) : 0;
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
        cmd.W = angle < 0.002 ? Vector3D.Zero : s.TurnAxis(drive, want, angle) * s.TurnCmd(angle);
        if (brake)
        {
            double aim = angle < 0.05 ? 1 : angle < 0.26 ? (0.26 - angle) / 0.21 : 0;
            cmd.Throttle = MathHelper.Clamp(acc.Length() / aFull, 0, 1) * aim * Math.Max(0, cos);
            cmd.Remaining = cmd.SpeedNow * cmd.SpeedNow / (2 * aFull);
            cmd.Eta = cmd.SpeedNow / aFull + angle / s.FlipRate;
        }
        return cmd;
    }

    /// <summary>
    /// approach: one guidance step toward a ship at tp drifting at tv, to stop keep metres from its centre on the side
    /// the ship comes from. It is Steer flown in the target's own frame (the velocity against the target's), so the
    /// flip and the braking burn end at rest beside it, whatever it drifts at. Arrived within the reach of that
    /// point and under ApproachSpeed against the target: slow enough to hand back without drifting into it.
    /// SpeedNow is the closing speed. one: a list kept by the caller for the single point.
    /// </summary>
    public static Cmd Approach(Vector3D p, Vector3D v, Vector3D g, Vector3D drive, double aFull, Vector3D tp, Vector3D tv, double keep, double cap, List<Wp> one, Settings s)
    {
        var off = p - tp; double d = off.Length();
        var dir = d > 1 ? off / d : -drive;
        // Steer plans its stop 0.4 of the reach short of the point and runs on a few tens of metres past that plan
        // as it sheds the last of its speed: with the point at keep, it ends within about 50 m of keep either way
        one.Clear();
        one.Add(new Wp { Pos = tp + dir * keep, Arrive = ApproachReach, Kind = "target", Name = "" });
        s.ArriveSpeed = ApproachSpeed;
        var cmd = Steer(p, v - tv, g, drive, aFull, 1, 1, one, 0, Math.Max(100, cap - tv.Length()), s);
        s.ArriveSpeed = 10;
        return cmd;
    }
    public const double ApproachReach = 100, ApproachSpeed = 1;

    /// <summary>
    /// match: keep station on a ship at tp moving at tv, in a band of ranges from near (from its centre) to near +
    /// 2 x MatchBand. It is Approach to the middle of the band, never done: once at rest against the ship there
    /// (Keeping) the drives rest, until the range is out of the band or the speed against the ship passes MatchLoose,
    /// and then it flies to the middle again. Never Arrived. SpeedNow is the speed against the ship.
    /// </summary>
    public static Cmd Match(Vector3D p, Vector3D v, Vector3D g, Vector3D drive, double aFull, Vector3D tp, Vector3D tv, double near, double cap, List<Wp> one, Settings s)
    {
        double mid = near + MatchBand, range = Vector3D.Distance(p, tp), rel = (v - tv).Length();
        // the near edge is a floor: it corrects while there is still room to turn and burn before reaching it
        // (50 m and half a minute of the closing speed)
        double closing = range > 1 ? Vector3D.Dot(v - tv, (tp - p) / range) : 0;
        if (s.Keeping && (range - mid > MatchBand || range - near < 50 + Math.Max(0, closing) * 30 || rel > MatchLoose)) s.Keeping = false;
        if (!s.Keeping)
        {
            var cmd = Approach(p, v, g, drive, aFull, tp, tv, mid, cap, one, s);
            if (!cmd.Arrived) return cmd;
            s.Keeping = true; s.Burning = false;
        }
        return new Cmd { SpeedNow = rel, Dist = range, Remaining = Math.Abs(range - mid) };
    }
    public const double MatchBand = 300, MatchLoose = 3;

    /// <summary>
    /// Formation: one guidance step holding a slot at tp that moves at tv and speeds up at ta (the leader's burn),
    /// tdrive the way the leader's drives push. It is Steer flown in the slot's own frame, with the leader's burn as
    /// a pull the other way (as gravity is), so a ship on station burns as the leader does and one off it corrects on
    /// top of that. Never Arrived. With nothing to burn for, the drives turn the way the leader's face, so the ship
    /// flips when the leader flips and is lined up for its next burn. Within 0.4 of FormReach of the slot it only
    /// matches velocity. SpeedNow is the speed against the slot.
    /// </summary>
    public static Cmd Formation(Vector3D p, Vector3D v, Vector3D g, Vector3D drive, double aFull, Vector3D tp, Vector3D tv, Vector3D ta, Vector3D tdrive, double cap, List<Wp> one, Settings s)
    {
        one.Clear();
        one.Add(new Wp { Pos = tp, Arrive = FormReach, Kind = "target", Name = "" });
        s.ArriveSpeed = -1; // a slot is never arrived at
        var cmd = Steer(p, v - tv, g - ta, drive, aFull, 1, 1, one, 0, Math.Max(100, cap - tv.Length()), s);
        s.ArriveSpeed = 10;
        if (cmd.Throttle <= 0 && cmd.W.LengthSquared() == 0 && tdrive.LengthSquared() > 0.5)
        {
            var want = Vector3D.Normalize(tdrive);
            double angle = Math.Acos(MathHelper.Clamp(Vector3D.Dot(drive, want), -1, 1));
            if (angle > 0.02) cmd.W = s.TurnAxis(drive, want, angle) * s.TurnCmd(angle);
        }
        return cmd;
    }
    public const double FormReach = 250;

    /// <summary>
    /// Where a formation slot lies from the leader: axis = the way the route runs. Slots 1, 2 are one spacing to the
    /// right and left and half a spacing back, 3, 4 two out and one back, and so on (right = across the route, level
    /// with the system's plane), so no two ships share a lane along the route.
    /// </summary>
    public static Vector3D Slot(Vector3D axis, int slot, double spacing)
    {
        if (slot < 1) slot = 1;
        axis = axis.LengthSquared() > 0.5 ? Vector3D.Normalize(axis) : Vector3D.Forward;
        var right = Vector3D.Cross(axis, Vector3D.Up);
        if (right.LengthSquared() < 0.01) right = Vector3D.Cross(axis, Vector3D.Right);
        right = Vector3D.Normalize(right);
        int n = (slot + 1) / 2;
        return right * ((slot % 2 == 1 ? 1 : -1) * n * spacing) - axis * (n * spacing * 0.5);
    }

    /// <summary>Seconds ahead along a curve the ship aims.</summary>
    public const double LookTime = 15;
    /// <summary>The drives' angle off the wanted push (rad) where the throttle has faded to nothing.</summary>
    public const double AimFade = 0.6;

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
    // within this far of a stop (Flip and Burn's 5 km round a static grid + the 3 km a station stop is short of it)
    // the ship can't go over the game's own top speed
    public const double FbZone = 8000;

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
    public static string Litres(double l) { return l >= 1000 ? (l / 1000).ToString("0.0") + " kL" : l.ToString("0") + " L"; }
    public static string Kms(double v) { return v >= 1000 ? (v / 1000).ToString("0.0") + " km/s" : v.ToString("0") + " m/s"; }

    /// <summary>
    /// What the drives burn, learned in flight: the litres the tanks lose against the seconds of full thrust flown
    /// (Burnt each run, Low every couple of seconds with the tanks' litres). A m/s of speed costs Rate / aFull litres,
    /// so stopping from v takes v / aFull * Rate. Low: the tanks hold no more than that and Reserve over.
    /// </summary>
    public class WaterMeter
    {
        /// <summary>Kept over what the stop needs (the flip burns nothing, but the stop is never exact).</summary>
        public const double Reserve = 1.1;
        /// <summary>Seconds of full thrust, and litres, seen before the rate is trusted.</summary>
        public const double MinSeconds = 5, MinLitres = 1;
        /// <summary>Slower than this (m/s) it never brakes for water: the dampeners can stop that.</summary>
        public const double MinSpeed = 100;
        double last = -1, pending, secs, used;
        /// <summary>Litres a second at full thrust (0 = not known yet).</summary>
        public double Rate { get { return secs >= MinSeconds && used >= MinLitres ? used / secs : 0; } }
        public void Reset() { last = -1; pending = secs = used = 0; }
        /// <summary>The drives ran at this throttle (0..1) for dt seconds.</summary>
        public void Burnt(double throttle, double dt) { if (throttle > 0 && dt > 0 && dt < 5) pending += throttle * dt; }
        /// <summary>The tanks hold this now; whether that is down to what stopping from speed burns.</summary>
        public bool Low(double litres, double speed, double aFull)
        {
            // tanks filled since (docked, or a transfer): that stretch says nothing about the drives
            if (last >= 0 && litres <= last) { secs += pending; used += last - litres; }
            pending = 0; last = litres;
            double rate = Rate;
            if (rate <= 0 || aFull <= 0 || speed < MinSpeed) return false;
            return litres <= speed / aFull * rate * Reserve;
        }
    }

    public class VelocityEstimator
    {
        readonly List<Vector3D> pos = new List<Vector3D>();
        readonly List<double> at = new List<double>();
        public void Reset() { pos.Clear(); at.Clear(); }
        public Vector3D Update(Vector3D p, Vector3D physics, double t)
        {
            pos.Add(p); at.Add(t);
            // (the next one must span 0.4 s too: runs 1.7 s apart on a coast, then 0.17 s)
            while (at.Count > 2 && t - at[0] > 2.0 && t - at[1] >= 0.4) { pos.RemoveAt(0); at.RemoveAt(0); }
            double dt = t - at[0];
            if (physics.Length() < 90 || dt < 0.4) return physics;
            var fromPos = (p - pos[0]) / dt;
            return fromPos.Length() > physics.Length() ? fromPos : physics;
        }
    }

    public static double D(string s) { double d; return double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d) ? d : 0; }
    public static string Km(double m) { return m < 10000 ? m.ToString("0") + " m" : (m / 1000).ToString("#,0") + " km"; }
    public static string Speed(double ms) { return ms < 1000 ? ms.ToString("0") + " m/s" : (ms / 1000).ToString("0.0") + " km/s"; }
}
