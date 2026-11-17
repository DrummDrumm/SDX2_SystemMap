using System;
using System.Collections.Generic;
using Sandbox.Game.Gui;
using Sandbox.Graphics;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using VRage.Input;

namespace SDX2_SystemMap;

/// <summary>
/// The map window beside Flip and Burn's N list, the route plan behind it, and the autopilot's buttons and panel.
///  - Opening the N list shows the map window (ShowMapWithNav); Shift+M cycles map window, full map, off. M alone stays the game's symmetry key.
///    The list stays open when you pick a destination (KeepNavOpen, see FlipAndBurnLink).
///  - Once a destination is picked, AUTOPILOT and MANUAL sit under the list. MANUAL closes the list: fly it with
///    Flip and Burn. AUTOPILOT opens the route panel beside the list: top speed, a thrust or signature cap, your own
///    waypoints (picked in the list while adding), then ENGAGE; the PB flies the route.
///  - While it flies, a status line shows at the top and STOP AUTOPILOT sits under the list; it and Ctrl+M ask
///    before they stop it. Touching the controls (or the PB's "manual" hotbar action) lets go at once, with a pop-up
///    offering RESUME ROUTE. Flip and Burn's destination follows the route: each detour and waypoint in turn.
///  - After the route was stopped part-way, RESUME ROUTE sits under the list until you pick something else.
///  - The full map's Shift+click sets Flip and Burn's destination (SetDestination).
///  - Asteroids on the route (Flip and Burn's own scanner) are shown on screen; while the autopilot flies they get
///    fly-by points and the route is re-sent.
///  - Over the map window while the list is open: right-drag orbits, the wheel zooms, a middle click re-frames.
/// </summary>
internal static class SidePreview
{
    public static readonly MapSnapshot Snap = new MapSnapshot();
    public static readonly RouteTracker Tracker = new RouteTracker();
    public static RoutePath Path => Tracker.Path;
    public static bool Visible;

    private static readonly MapScene scene = new MapScene { Compact = true };
    private static readonly GameCanvas canvas = new GameCanvas();
    private static readonly PanelUi ui = new PanelUi();
    private static readonly List<string> userNames = new List<string>();
    private static double nextRoute, promptUntil, lastDraw;
    private static int publishedRevision = -1, lastNext = -1;
    private static string lastTarget, prevSel;
    private static bool reframe, dragging, navOpen, autoShown;
    private static float lastMx, lastMy;
    private static Box followBox; // the preview's FOLLOW button (last frame)
    private static bool routePanel, adding, closingSelf;
    private static double panelGrace; // Ctrl+M opens the N list: give F&B a moment before "list closed" closes the panel
    private static string lastState, flyingTarget;
    private static double flyingThrust = 1, flyingBrake = 1;
    /// <summary>The route the autopilot stopped part-way along (manual control): RESUME ROUTE flies the rest.</summary>
    private static string resumeTarget;
    private static bool stoppingSelf, asking;
    /// <summary>The route panel is open in the full map (not beside the N list): it stays open while the map is.</summary>
    private static bool mapPanel;
    private static string steeredTo; // the route point last made F&B's destination while flying

    private static RouteLimits Limits => Plugin.Settings.Limits;

    public static void Reset()
    {
        Visible = false; autoShown = false; lastTarget = null; prevSel = null; promptUntil = 0; nextRoute = 0; Snap.FnbTargetName = null;
        Tracker.Clear(); Tracker.Hold = Tracker.Adding = false;
        publishedRevision = -1; lastNext = -1;
        routePanel = adding = false; lastState = null; flyingTarget = null; flyingThrust = flyingBrake = 1;
        resumeTarget = null; stoppingSelf = asking = mapPanel = false; steeredTo = null;
        AutopilotLink.Reset(); AsteroidWatch.Reset(); Seat.Reset(); GridObstacles.Reset();
    }

    public static void ApplySettings(MapSettings s)
    {
        scene.Opt = s.Options;
        MapScene.DefaultGridKm = s.DefaultGridKm;
        scene.Cam.Yaw = s.Yaw; scene.Cam.Pitch = s.Pitch;
        Tracker.Avoid = s.AvoidBodies;
        Tracker.Clearance = s.ClearanceKm * 1000;
    }


    /// <summary>
    /// Admin mode or AFK (<see cref="PlayGuard"/>): close the full map, hide the preview and leave F&amp;B's list to itself.
    /// Visible and the route are kept, so it all comes back as it was.
    /// </summary>
    public static void Pause()
    {
        KeepNavOpen.Intercept = null;
        if (MapScreen.IsOpen) MapScreen.Close();
        routePanel = adding = mapPanel = false;
        dragging = false;
    }

    private static double dodgeNoteAt;

    /// <summary>Ctrl+Shift+M: send the route with the suggested way round an asteroid (never sent by itself).</summary>
    public static void SteerRound()
    {
        if (!AutopilotLink.Engaged(Plugin.Now) || Path == null || !AsteroidWatch.Changed) { Notify("No asteroid to steer round.", false); return; }
        if (Send("update", quiet: false)) Notify("Autopilot: steering round the asteroid.");
    }

    /// <summary>Every frame, from Plugin.Update.</summary>
    public static void Update(double now)
    {
        GameState.Fill(Snap, now);
        var leftWhy = Seat.Update(Snap.InCockpit, Snap.CharacterId, Snap.Dead);
        if (leftWhy != null) CancelRoute(leftWhy);
        bool was = navOpen;
        navOpen = FlipAndBurnLink.IsNavMenuOpen();
        if (navOpen && !was)
        {
            if (Plugin.Settings.ShowMapWithNav && !Visible) { Visible = true; autoShown = true; reframe = true; }
        }
        else if (!navOpen && was)
        {
            // F&B closed the list on a pick (our patch missing or bypassed): open it again
            if (Plugin.Settings.KeepNavOpen && !closingSelf && Snap.FnbTargetName != null && Snap.FnbTargetName != prevSel)
            { FlipAndBurnLink.OpenNavMenu(); navOpen = true; }
            else
            {
                if (autoShown && !Plugin.Settings.Pip) Visible = false;
                autoShown = false;
                adding = false;
                if (now > panelGrace && !mapPanel) routePanel = false;
            }
        }
        prevSel = Snap.FnbTargetName;
        closingSelf = false;
        KeepNavOpen.Intercept = PickWaypoint;
        if (mapPanel && !MapScreen.IsOpen) { mapPanel = false; if (!navOpen) routePanel = adding = false; }
        if (!navOpen && !mapPanel && now > panelGrace) { routePanel = false; adding = false; }

        AutopilotLink.Update(now);
        GridObstacles.Update(now);
        Tracker.Obstacles = GridObstacles.Current;
        bool engaged = AutopilotLink.Engaged(now);
        if (now >= nextRoute)
        {
            nextRoute = now + 0.1;
            bool limited = routePanel || engaged && flyingTarget != null;
            Tracker.AccelScale = !limited ? 1 : routePanel ? Limits.ThrustFraction(Snap.DriveWeight, Snap.Signature?.Gain ?? 4) : flyingThrust;
            Tracker.BrakeScale = !limited ? 1 : routePanel ? Limits.BrakeFraction(Snap.DriveWeight, Snap.Signature?.Gain ?? 4) : flyingBrake;
            Tracker.SpeedCap = limited ? Limits.MaxSpeed : 0;
            Tracker.Hold = routePanel || engaged;
            Tracker.Adding = routePanel && adding;
            Tracker.Update(Snap.FnbTargetName, Snap.FnbTarget, Snap.HasShip, Snap.ShipPos, Snap.ShipVel, Snap.MaxAccel, Snap.MaxSpeed);
            Project();
        }

        // a new destination (not one of our own points)
        if (Tracker.TargetName != lastTarget)
        {
            lastTarget = Tracker.TargetName;
            if (lastTarget != null) { promptUntil = now + 8; reframe = true; }
            Snap.Track.Clear(); // a new route: start the flown track again
        }
        if (Path == null) { Snap.Track.Clear(); Snap.Prediction.Clear(); Snap.PredictionPhases.Clear(); }
        else if (Snap.HasShip) Snap.RecordTrack(Snap.ShipPos, now, Path.Distance);
        // the points changed: update F&B's list, and the autopilot (new points while it flies, or on request)
        if (Tracker.Revision != publishedRevision)
        {
            publishedRevision = Tracker.Revision;
            FlipAndBurnLink.SetDetours(Tracker.Waypoints);
            if (Path != null && engaged && Tracker.TargetName == flyingTarget) Send("update", quiet: true);
            else if (Path != null && !engaged && Plugin.Settings.AutopilotAutoSend) Send("load", quiet: true);
        }
        AsteroidWatch.Update(now, Snap, Tracker, engaged);
        if (engaged && AsteroidWatch.Changed && Path != null && now >= dodgeNoteAt)
        {
            if (!Binds.Steer.IsNone) Notify("Asteroid on the route: " + Binds.Steer.Display + " steers round it.", false);
            dodgeNoteAt = now + 15;
        }
        WatchState();
        // reached a detour: say what to pick next
        if (Tracker.Next != lastNext)
        {
            if (lastNext >= 0 && Tracker.Next > lastNext && Path != null) promptUntil = now + 10;
            lastNext = Tracker.Next;
        }
        if (Path == null) { routePanel = false; adding = false; }
        if (resumeTarget != null && Tracker.TargetName != resumeTarget) resumeTarget = null; // picked somewhere else
        if (engaged && flyingTarget != null && Tracker.TargetName == flyingTarget) SteerSelection(); else steeredTo = null;

        if (OutOfCockpit) { if (MapScreen.IsOpen) MapScreen.Close(); dragging = false; return; }
        HandleKeys();
        if (Visible && navOpen) HandleMouse();
        else dragging = false;
    }

    /// <summary>Our keys fire only on the plain game screen, with chat and F&amp;B's search box closed.</summary>
    public static bool KeysFree() =>
        MyScreenManager.GetScreenWithFocus() is MyGuiScreenGamePlay
        && !(MyAPIGateway.Gui?.ChatEntryVisible ?? false)
        && !FlipAndBurnLink.IsSearching();

    private static void HandleKeys()
    {
        var input = MyInput.Static;
        if (input == null || !KeysFree()) return;
        // each binding fires only with exactly its modifiers, so plain M stays the game's symmetry key
        if (Binds.Steer.Pressed()) SteerRound();
        else if (Binds.Map.Pressed()) CycleView();
        else if (Binds.Autopilot.Pressed()) AutopilotKey();
        else if (Binds.Pin.Pressed()) Notify(TogglePip(), true);
    }

    /// <summary>Frame the map window again on its next draw (after the default zoom changes).</summary>
    public static void Reframe() => reframe = true;

    /// <summary>The map key (Shift+M): map window → full map → off (the full map's map key does the last step).</summary>
    private static void CycleView()
    {
        autoShown = false;
        promptUntil = 0;
        if (!Visible) { Visible = true; reframe = true; return; }
        MapScreen.Open();
    }

    /// <summary>The help row under the map: each bound key and what it does.</summary>
    private static string KeysHelp()
    {
        var sb = new System.Text.StringBuilder("KEYS|");
        void Add(KeyCombo b, string what) { if (b.IsNone) return; if (sb.Length > 5) sb.Append(MapScene.Sep); sb.Append(b.Display).Append(' ').Append(what); }
        Add(Binds.Map, "map, full map, off"); Add(Binds.Autopilot, "autopilot"); Add(Binds.Pin, "pin on screen");
        return sb.ToString();
    }

    /// <summary>The map key in the full map: close it and hide the map window, ending the cycle.</summary>
    public static void CycleOff()
    {
        Visible = false;
        autoShown = false;
    }

    /// <summary>Ctrl+M (and A in the full map): stop if flying (after asking), else open the N list with the route panel.</summary>
    public static void AutopilotKey()
    {
        if (AutopilotLink.Engaged(Plugin.Now)) { ConfirmStop(); return; }
        if (Path == null) { Notify("No route: pick a destination in Flip and Burn's list (N) first.", false); return; }
        if (AutopilotLink.Count == 0) { Notify(NoPb, false); return; }
        if (!navOpen) FlipAndBurnLink.OpenNavMenu();
        routePanel = true; adding = false; panelGrace = Plugin.Now + 1.5;
    }

    private const string NoPb = "No autopilot PB on this ship: load the Protogen Systems - Autopilot script into a programmable block.";

    private static void OpenRoutePanel()
    {
        if (AutopilotLink.Count == 0) { Notify(NoPb, false); return; }
        routePanel = true; adding = false;
    }

    /// <summary>A pick in F&amp;B's list while ADD WAYPOINT is on: it becomes a waypoint and F&amp;B's destination stays.</summary>
    private static bool PickWaypoint(string name, VRageMath.Vector3D pos)
    {
        if (!routePanel || !adding || Tracker.TargetName == null) return false;
        var v = new V3(pos.X, pos.Y, pos.Z);
        if (Tracker.AddUser(name, v)) Notify("Waypoint added: " + name, false);
        else Notify(Tracker.UserPoints.Count >= 8 ? "Up to 8 waypoints" : "Already in the route: " + name, false);
        return true;
    }

    /// <summary>MANUAL: close the list and fly the route with Flip and Burn.</summary>
    private static void Manual()
    {
        routePanel = adding = mapPanel = false;
        if (navOpen) { closingSelf = true; FlipAndBurnLink.CloseNavMenu(); }
        var p = Path;
        if (p == null) return;
        Notify(p.DetoursAhead > 0 ? "Manual: pick \"" + Tracker.NextPick + "\" in N first, then each point in turn" : "Manual: fly to " + p.TargetName + " with Flip and Burn");
    }

    public static void Engage()
    {
        AsteroidWatch.Reset();
        Snap.Track.Clear(); // the flown track starts with the flight
        stoppingSelf = false; resumeTarget = null;
        flyingThrust = Limits.ThrustFraction(Snap.DriveWeight, Snap.Signature?.Gain ?? 4);
        flyingBrake = Limits.BrakeFraction(Snap.DriveWeight, Snap.Signature?.Gain ?? 4);
        if (!Send("engage", quiet: false)) return;
        flyingTarget = Path.TargetName;
        routePanel = adding = false;
        Plugin.SaveSettings();
        mapPanel = false;
        if (navOpen) { closingSelf = true; FlipAndBurnLink.CloseNavMenu(); }
        string cap = (Limits.MaxSpeed > 0 ? "  top " + Fmt.Speed(Limits.MaxSpeed) : "") + (flyingThrust < 0.995 || flyingBrake < 0.995 ? "  burn " + Fmt.Num(flyingThrust * 100, "0") + "% / brake " + Fmt.Num(flyingBrake * 100, "0") + "%" : "");
        Notify("Autopilot engaged: flying to " + Path.TargetName + cap + ". Move or turn to take over.");
    }

    public static void StopAutopilot()
    {
        string was = flyingTarget;
        stoppingSelf = true;
        int n = AutopilotLink.Stop(out var error);
        flyingTarget = null;
        if (n > 0) resumeTarget = was;
        Notify(n > 0 ? "Autopilot stopped: you have the controls." + (was != null ? " RESUME ROUTE is under the N list." : "") : error, n > 0);
    }

    /// <summary>STOP AUTOPILOT, Ctrl+M or A: ask first, since it hands the ship back mid-route.</summary>
    public static void ConfirmStop()
    {
        if (asking) return;
        asking = true;
        Ask("Take manual control?", "This stops the autopilot. The route is kept: RESUME ROUTE under the N list flies the rest of it.",
            "TAKE CONTROL", "KEEP FLYING", yes => { asking = false; if (yes) StopAutopilot(); });
    }

    /// <summary>Flies the rest of a route that was stopped part-way, from where the ship is now.</summary>
    public static void Resume()
    {
        if (resumeTarget == null || Path == null || Path.TargetName != resumeTarget) { Notify("Nothing to resume: the route has changed.", false); resumeTarget = null; return; }
        if (AutopilotLink.Count == 0) { Notify(NoPb, false); return; }
        AsteroidWatch.Reset();
        stoppingSelf = false;
        if (!Send("engage", quiet: false)) return;
        flyingTarget = resumeTarget; resumeTarget = null;
        routePanel = adding = mapPanel = false;
        if (navOpen) { closingSelf = true; FlipAndBurnLink.CloseNavMenu(); }
        Notify("Autopilot resumed: flying to " + flyingTarget + ". Move or turn to take over.");
    }

    /// <summary>A yes/no pop-up (the game's own message box, so it has a mouse cursor).</summary>
    private static void Ask(string caption, string text, string yes, string no, Action<bool> done)
    {
        try
        {
            MyGuiSandbox.AddScreen(MyGuiSandbox.CreateMessageBox(MyMessageBoxStyleEnum.Info, MyMessageBoxButtonsType.YES_NO,
                new System.Text.StringBuilder(text), new System.Text.StringBuilder(caption),
                yesButtonText: VRage.Utils.MyStringId.GetOrCompute(yes), noButtonText: VRage.Utils.MyStringId.GetOrCompute(no),
                callback: r => done(r == MyGuiScreenMessageBox.ResultEnum.YES), focusedResult: MyGuiScreenMessageBox.ResultEnum.NO));
        }
        catch (Exception e) { Plugin.LogThrottled("pop-up failed: " + e.Message); done(false); }
    }

    /// <summary>While the autopilot flies: Flip and Burn's destination is the point it's flying to now (each detour and
    /// waypoint in turn, then the destination), so F&amp;B's HUD marker and figures follow the route.</summary>
    private static void SteerSelection()
    {
        int next = Tracker.Next;
        bool final = next >= Tracker.Waypoints.Count;
        string name = final ? Tracker.TargetName : Tracker.Waypoints[next].Name;
        var p = final ? Tracker.Target : Tracker.Waypoints[next].User ? Tracker.Waypoints[next].Pick : Tracker.Waypoints[next].Pos;
        string key = next + "|" + name;
        if (key == steeredTo) return;
        steeredTo = key;
        if (Snap.FnbTargetName == name) return;
        if (!FlipAndBurnLink.SelectPoint(name, new VRageMath.Vector3D(p.X, p.Y, p.Z), out var error)) Plugin.LogThrottled(error);
    }

    /// <summary>Shift+right-click on one of your waypoints in the full map: removes it. Null when it isn't one.</summary>
    public static string RemoveWaypoint(string name, V3 pos)
    {
        int i = Tracker.UserPoints.FindIndex(u => u.Name == name);
        if (i < 0) i = Tracker.UserPoints.FindIndex(u => V3.Distance(u.Pos, pos) < 100);
        if (i < 0) return null;
        string n = Tracker.UserPoints[i].Name;
        Tracker.RemoveUser(i);
        nextRoute = 0;
        return "Waypoint removed: " + n + (Tracker.UserPoints.Count > 0 ? " (" + Tracker.UserPoints.Count + " left)" : "");
    }

    /// <summary>Where one of your waypoints is (the point you placed, not the stop short of a station). False when it isn't one.</summary>
    public static bool UserWaypoint(string name, out V3 pos)
    {
        int i = Tracker.UserPoints.FindIndex(u => u.Name == name);
        pos = i >= 0 ? Tracker.UserPoints[i].Pos : default;
        return i >= 0;
    }

    /// <summary>Dragging one of your waypoints up or down in the full map: moves it and plans the route again.</summary>
    public static string MoveWaypoint(string name, V3 pos, double rise)
    {
        int i = Tracker.UserPoints.FindIndex(u => u.Name == name);
        if (i < 0) return "That waypoint is gone";
        if (!Tracker.MoveUser(i, pos)) return "Can't move " + name + " onto another point of the route";
        nextRoute = 0;
        return name + " moved " + Fmt.Dist(Math.Abs(rise)) + (rise >= 0 ? " up" : " down");
    }

    /// <summary>Shift+right-click in the full map: a waypoint into the route where it fits (name null = "Waypoint n").</summary>
    public static string InsertWaypoint(string name, V3 pos)
    {
        if (Tracker.TargetName == null) return "Set a destination first (Shift+click a point)";
        if (Tracker.UserPoints.Count >= 8) return "Up to 8 waypoints";
        name = name ?? Tracker.NextUserName();
        if (!Tracker.InsertUser(name, pos)) return "Can't add " + name + ": it's in the route already";
        nextRoute = 0;
        int i = Tracker.UserPoints.FindIndex(u => u.Name == name);
        return "Waypoint added: " + name + " (stop " + (i + 1) + " of " + (Tracker.UserPoints.Count + 1) + ")";
    }

    /// <summary>Shift+click in the full map: make the point Flip and Burn's destination (a waypoint while adding).</summary>
    public static string SetDestination(string name, string kind, V3 pos, string detail = null)
    {
        if (kind == "Your ship") return "That's your ship.";
        if (routePanel && adding && Tracker.TargetName != null)
            return Tracker.AddUser(name, pos) ? "Waypoint added: " + name : "Can't add " + name + " (already in the route, or 8 waypoints)";
        if (AutopilotLink.Engaged(Plugin.Now)) return "The autopilot is flying: stop it (" + Binds.Autopilot.Display + ") before you change the destination.";
        if (!FlipAndBurnLink.Found) return "Flip and Burn isn't loaded.";
        // a station's F&B entry goes by its GPS name (JU.Ganymede.STN), shown as the pick's detail
        string alt = kind == "Trade station" ? detail : null;
        return FlipAndBurnLink.SelectPoint(name, new VRageMath.Vector3D(pos.X, pos.Y, pos.Z), out var error, alt)
            ? "Destination: " + name + ". Press N for AUTOPILOT or MANUAL." : error;
    }

    /// <summary>Sends the route (with any asteroid dodges and the thrust cap) to the autopilot PBs.</summary>
    public static bool Send(string command, bool quiet)
    {
        double thrust = command == "load" ? Limits.ThrustFraction(Snap.DriveWeight, Snap.Signature?.Gain ?? 4) : flyingThrust;
        double brake = command == "load" ? Limits.BrakeFraction(Snap.DriveWeight, Snap.Signature?.Gain ?? 4) : flyingBrake;
        int n = AutopilotLink.Send(Path, command, command == "load" ? null : AsteroidWatch.Dodges, thrust, brake, out var error);
        if (n > 0 && command != "load") AsteroidWatch.TakeChanged(); // the suggested way round went with it
        if (n <= 0 && !quiet) Notify(error, false);
        else if (n > 0 && command == "load" && !quiet) Notify("Route to " + Path.TargetName + " loaded into the autopilot");
        return n > 0;
    }

    /// <summary>Says so when the PB stops flying by itself (arrived, you took over, or a problem).</summary>
    private static void WatchState()
    {
        var st = AutopilotLink.Status;
        string state = st?.State;
        if (state == lastState) return;
        // resumed from the PB's own "go" (hotbar): the stopped route is flying again
        if (state == "flying" && flyingTarget == null && resumeTarget != null && !AutopilotLink.Pending)
        { flyingTarget = resumeTarget; resumeTarget = null; Notify("Autopilot resumed: flying to " + flyingTarget); }
        if (lastState == "flying" && !AutopilotLink.Pending)
        {
            string was = flyingTarget;
            switch (state)
            {
                case "arrived": Notify("Autopilot: arrived" + (flyingTarget != null ? " at " + flyingTarget : "") + ". You have the controls."); break;
                case "error": Notify("Autopilot stopped: " + st.Msg, false); if (was != null) resumeTarget = was; break;
                case "override":
                case "stopped":
                    if (state == "stopped" && stoppingSelf) break; // our own STOP, already asked
                    if (was == null) { Notify("Autopilot off: you took the controls"); break; }
                    resumeTarget = was;
                    Ask("Manual control", "Autopilot stopped: " + (string.IsNullOrEmpty(st.Msg) ? "you took the controls" : st.Msg)
                        + ".\nYou have the ship. Resume the route to " + was + " from here, or keep flying it yourself?",
                        "RESUME ROUTE", "STAY MANUAL", yes => { if (yes) Resume(); });
                    break;
                default: if (state != null) Notify("Autopilot: " + state + (string.IsNullOrEmpty(st.Msg) ? "" : " - " + st.Msg)); break;
            }
            if (state != null) flyingTarget = null;
            stoppingSelf = false;
        }
        lastState = state;
    }

    private static readonly SeatWatch Seat = new SeatWatch();
    private static readonly HeadingFollow previewHeading = new HeadingFollow();

    /// <summary>
    /// You got out of the seat or respawned: the planned route (and your waypoints) is dropped. F&amp;B's own pick stays,
    /// but isn't planned again until you pick a point. An autopilot already flying isn't stopped here.
    /// </summary>
    private static void CancelRoute(string why)
    {
        if (Tracker.TargetName == null && Tracker.UserPoints.Count == 0) return;
        Tracker.Cancel(Snap.FnbTargetName, Snap.FnbTarget);
        routePanel = adding = mapPanel = false;
        resumeTarget = null; steeredTo = null;
        Notify("Route cancelled: " + why, false);
    }

    public static void Notify(string text, bool ok = true)
    {
        try { MyAPIGateway.Utilities.ShowNotification(text, 4000, ok ? "White" : "Red"); } catch { }
    }

    /// <summary>Picks the route again from where the ship is now (new detours).</summary>
    public static void Replan()
    {
        Tracker.Avoid = Plugin.Settings.AvoidBodies;
        Tracker.Clearance = Plugin.Settings.ClearanceKm * 1000;
        if (Snap.HasShip) Tracker.Replan(Snap.ShipPos);
        Snap.Track.Clear();
        nextRoute = 0;
    }

    private static readonly List<V3> ahead = new List<V3>();

    /// <summary>Where the ship will go from here as it corrects onto the route, to the destination (drawn dashed).</summary>
    private static void Project()
    {
        var p = Path;
        if (p == null || !Snap.HasShip) { Snap.Prediction.Clear(); Snap.PredictionPhases.Clear(); return; }
        ahead.Clear();
        for (int i = p.Next; i < p.Waypoints.Count; i++) ahead.Add(p.Waypoints[i].Pos);
        ahead.Add(p.Target);
        Snap.PredictionArrives = Trajectory.Predict(Snap.ShipPos, Snap.ShipVel, ahead, p.MaxAccel, p.MaxSpeed, Tracker.ArriveRadius, Snap.Prediction, p.Eta, phases: Snap.PredictionPhases);
    }

    /// <summary>Mouse in screen pixels (works under RichHud's cursor, where the GUI cursor isn't updated).</summary>
    public static void Mouse(out float x, out float y)
    {
        var input = MyInput.Static;
        var full = MyGuiManager.GetFullscreenRectangle();
        var area = input.GetMouseAreaSize();
        var p = input.GetMousePosition();
        x = area.X > 0 ? p.X / area.X * full.Width : p.X;
        y = area.Y > 0 ? p.Y / area.Y * full.Height : p.Y;
    }

    /// <summary>Picture-in-picture: the preview in a screen corner while F&amp;B's list is closed.</summary>
    private static bool InPip => Plugin.Settings.Pip && !navOpen;
    private static bool wasPip;

    private static Box WindowBox()
    {
        var full = MyGuiManager.GetFullscreenRectangle();
        var s = Plugin.Settings;
        return InPip ? SideLayout.Pip(full.Width, full.Height, s.PipSize, s.PipCorner)
            : SideLayout.Box(full.Width, full.Height, s.PreviewSize, s.PreviewLeft);
    }

    /// <summary>Picture-in-picture on or off (Alt+key, the cockpit toolbar action). Returns the message to show.</summary>
    public static string SetPip(bool on)
    {
        if (PlayGuard.Blocked) return Paused;
        Plugin.Settings.Pip = on;
        if (on) { Visible = true; autoShown = false; }
        else if (!navOpen) Visible = false;
        reframe = true;
        Plugin.SaveSettings();
        return on ? "Map preview pinned on screen (" + CornerName(Plugin.Settings.PipCorner) + ")." + (Binds.Pin.IsNone ? "" : " " + Binds.Pin.Display + " to unpin.")
                  : "Map preview unpinned.";
    }

    public static string TogglePip() => SetPip(!(Plugin.Settings.Pip && Visible));

    public static string Paused => "System Map is paused (" + PlayGuard.Reason + ").";

    /// <summary>The next corner for the pinned preview (Q in the full map): top right, bottom right, bottom left, top left.</summary>
    public static string NextPipCorner()
    {
        var s = Plugin.Settings;
        s.PipCorner = SideLayout.NextCorner(s.PipCorner);
        return SetPip(true);
    }

    public static string CornerName(string c) => c == "TL" ? "top left" : c == "BR" ? "bottom right" : c == "BL" ? "bottom left" : "top right";

    /// <summary>Show the pinned preview when a world loads with PiP on.</summary>
    public static void Start()
    {
        Plugin.Settings.PreviewFollow = true; // each session starts centred on your ship
        if (Plugin.Settings.Pip) { Visible = true; reframe = true; }
    }

    private static void HandleMouse()
    {
        var input = MyInput.Static;
        Mouse(out float mx, out float my);
        var box = WindowBox();
        bool over = box.Contains(mx, my);
        bool right = input.IsRightMousePressed();
        if (right && dragging)
        {
            scene.Cam.Orbit(-(mx - lastMx) * 0.006, (my - lastMy) * 0.006);
            if (Plugin.Settings.PreviewFollow && Plugin.Settings.FollowHeading) previewHeading.Turned(-(mx - lastMx) * 0.006);
        }
        dragging = right && (dragging || over);
        lastMx = mx; lastMy = my;
        if (!over) return;
        int wheel = input.DeltaMouseScrollWheelValue();
        if (wheel != 0) scene.Cam.Zoom(wheel / 120.0);
        if (input.IsNewMiddleMousePressed()) { reframe = true; Plugin.Settings.PreviewFollow = false; }
    }

    /// <summary>OnlyInCockpit is on and you're not in a ship controller: nothing drawn, no keys. Visible and the pin are
    /// kept, so it all comes back when you sit down.</summary>
    public static bool OutOfCockpit => Plugin.Settings.OnlyInCockpit && !Snap.InCockpit;

    /// <summary>Drawn after the game screen (GamePlayDrawPatch), on the main thread.</summary>
    public static void Draw()
    {
        double now = Plugin.Now, dt = Math.Min(0.1, Math.Max(0, now - lastDraw));
        lastDraw = now;
        if (MapScreen.IsOpen || PlayGuard.Blocked || OutOfCockpit) return;
        var full = MyGuiManager.GetFullscreenRectangle();
        float sw = full.Width, sh = full.Height;
        float px = Math.Max(14f, sh / 52f);
        var path = Path;
        string k = Binds.Map.Display, stop = Binds.Autopilot.Display;
        bool engaged = AutopilotLink.Engaged(now);
        var input = MyInput.Static;
        float mx = float.NaN, my = float.NaN;
        if (navOpen) Mouse(out mx, out my);
        ui.Input(canvas, mx, my, navOpen && input.IsLeftMousePressed(), navOpen && input.IsNewLeftMousePressed(), navOpen && input.IsNewLeftMouseReleased());

        // HUD: autopilot status and asteroid warning
        float hudY = sh * 0.05f;
        if (engaged) hudY = AutopilotPanel.StatusLine(canvas, sw, hudY, px * 0.9f, flyingTarget ?? NonEmpty(AutopilotLink.Status?.Target) ?? "route", AutopilotLink.Status, stop).Bottom + px * 0.5f;
        if (!double.IsNaN(AsteroidWatch.NearestDistance))
            AutopilotPanel.AsteroidWarning(canvas, sw, Math.Max(hudY, sh * 0.12f), px, AsteroidWatch.NearestDistance, Snap.ShipVel.Length,
                AsteroidWatch.NearestOnRoute, AsteroidWatch.NearestDodged, engaged, now, Binds.Steer.Display);

        if (Visible)
        {
            var box = WindowBox();
            bool pip = InPip;
            if (pip != wasPip) { wasPip = pip; reframe = true; }
            scene.Snap = Snap; scene.Path = path; scene.Mini = pip;
            scene.Cam.Setup(box.X, box.Y, box.W, box.H);
            if (reframe) { scene.FrameDefault(true); reframe = false; }
            bool follow = Plugin.Settings.PreviewFollow;
            if (follow && Snap.HasShip) { scene.Cam.GoalTarget = Snap.ShipPos; scene.Cam.Target = Snap.ShipPos; }
            if (follow && Snap.HasShip && Plugin.Settings.FollowHeading) previewHeading.Update(scene.Cam, Snap.ShipVel, Snap.ShipFwd, dt);
            scene.Cam.Tick(dt);
            string keys = KeysHelp();
            if (pip) { scene.MouseX = scene.MouseY = float.NaN; scene.Help = ""; }
            else if (navOpen) { scene.MouseX = mx; scene.MouseY = my; scene.Help = "VIEW|Right-drag orbit" + MapScene.Sep + "Wheel zoom" + MapScene.Sep + "Middle-click re-frame\n" + keys; }
            else { scene.MouseX = scene.MouseY = float.NaN; scene.Help = keys; }
            scene.Draw(canvas, box);
            // FOLLOW toggle, top right of the preview (clickable while the N list is open)
            float fpx = px * 0.75f;
            string ft = "FOLLOW SHIP";
            float fw = canvas.TextWidth(ft, fpx) + fpx * 1.6f, fh = fpx * 1.7f;
            followBox = new Box(box.Right - fw - fpx * 0.6f, scene.TitleBottom + fpx * 0.5f, fw, fh);
            if (navOpen)
            {
                if (ui.Button(followBox, ft, fpx, MapScene.ShipColor, follow))
                {
                    Plugin.Settings.PreviewFollow = !follow;
                    if (!follow && Snap.HasShip) scene.Cam.FlyTo(Snap.ShipPos, Math.Min(scene.Cam.GoalDistance, MapScene.ShipViewDistance));
                    Plugin.SaveSettings();
                }
            }
            else if (follow)
            {
                canvas.Rect(followBox.X, followBox.Y, fw, fh, MapColor.Rgb(0x0A121C, 200));
                canvas.Text(ft, followBox.X + fpx * 0.8f, followBox.Y + fpx * 0.35f, fpx, MapScene.ShipColor);
            }
        }
        else followBox = default;

        if (path == null) return;
        var list = SideLayout.FnbList(sw, sh);
        float y = navOpen ? list.Bottom + px * 0.8f : sh * 0.72f;

        // the prompt: after a pick or reaching a detour while the list is closed, or with the map hidden
        if (!Visible && !routePanel && (now < promptUntil || navOpen))
        {
            string text = path.DetoursAhead > 0
                ? "Route goes via \"" + path.Waypoints[path.Next].Name + "\": pick it in N first    preview [" + k + "]"
                : "Preview route to " + path.TargetName + "   [" + k + "]";
            if (engaged) text = "Autopilot is flying the route    preview [" + k + "]";
            float w = canvas.TextWidth(text, px) + px * 2, h = px * 1.8f;
            var promptBox = new Box((sw - w) / 2, y, w, h);
            float fade = navOpen ? 1 : (float)Math.Min(1, (promptUntil - now) / 1.5);
            var accent = path.DetoursAhead > 0 && !engaged ? MapScene.DetourCol : MapColor.Rgb(0x4FC3F7);
            canvas.Rect(promptBox.X, promptBox.Y, w, h, MapColor.Rgb(0x0A121C, (int)(225 * fade)));
            canvas.Rect(promptBox.X, promptBox.Bottom - 2, w, 2, accent.WithAlpha(fade));
            canvas.Text(text, promptBox.X + px, promptBox.Y + px * 0.38f, px, MapColor.Rgb(0xD8E6F0, (int)(255 * fade)));
            if (ui.Released && promptBox.Contains(mx, my)) { Visible = true; reframe = true; }
            y = promptBox.Bottom + px * 0.6f;
        }
        if (!navOpen) return;

        // under the list: STOP while flying, else (RESUME ROUTE and) AUTOPILOT / MANUAL
        if (engaged)
        {
            float w = canvas.TextWidth("STOP AUTOPILOT", px) + px * 2.4f;
            if (ui.Button(new Box(sw / 2 - w / 2, y, w, px * 1.9f), "STOP AUTOPILOT", px, AutopilotPanel.Stop)) ConfirmStop();
            return;
        }
        if (!routePanel && resumeTarget != null && path.TargetName == resumeTarget)
        {
            string rt = "RESUME ROUTE  to " + resumeTarget;
            float w = canvas.TextWidth(rt, px) + px * 2.4f;
            if (ui.Button(new Box(sw / 2 - w / 2, y, w, px * 1.9f), rt, px, AutopilotPanel.Go, true, AutopilotLink.Count > 0)) Resume();
            y += px * 2.6f;
        }
        if (!routePanel)
        {
            AutopilotPanel.Choice(ui, sw / 2, y, px, AutopilotLink.Count > 0, out bool auto, out bool manual);
            if (auto) OpenRoutePanel();
            else if (manual) Manual();
            return;
        }

        // the route panel, on the other side of the list from the map
        float margin = sh * 0.022f;
        bool left = !Plugin.Settings.PreviewLeft;
        float room = left ? list.X - margin * 2 : sw - list.Right - margin * 2;
        float pw = Math.Max(px * 18, Math.Min(room, px * 32));
        float px0 = left ? list.X - margin - pw : list.Right + margin;
        userNames.Clear();
        foreach (var u in Tracker.UserPoints) userNames.Add(u.Name);
        var hit = AutopilotPanel.RoutePanel(ui, px0, list.Y, pw, sh, px, path, Limits, Snap.MaxSpeed, Snap.MaxAccel, Snap.Signature, userNames, adding, Binds.Autopilot.Display, now);
        RouteHit(hit);
    }

    private static void RouteHit(AutopilotPanel.RouteHit hit)
    {
        if (hit.Changed) nextRoute = 0;
        if (hit.Remove >= 0) { Tracker.RemoveUser(hit.Remove); nextRoute = 0; }
        if (hit.ToggleAdd) adding = !adding;
        if (hit.Cancel) { routePanel = adding = mapPanel = false; Plugin.SaveSettings(); }
        else if (hit.Engage) Engage();
    }

    /// <summary>A in the full map: the route panel in the map (or, while flying, ask to stop).</summary>
    public static string MapAutopilotKey()
    {
        if (AutopilotLink.Engaged(Plugin.Now)) { ConfirmStop(); return null; }
        if (Path == null) return "No route: Shift+click a destination first.";
        if (AutopilotLink.Count == 0) return NoPb;
        routePanel = mapPanel = true; adding = false;
        return null;
    }

    /// <summary>
    /// The autopilot controls in the full map, as under the N list: the destination with AUTOPILOT / MANUAL (and
    /// RESUME ROUTE after a stop), the route panel, or STOP AUTOPILOT while it flies. x, y = top-left. Returns the
    /// area it covers (the map ignores the mouse there); closeMap = MANUAL was clicked (fly it yourself).
    /// </summary>
    public static Box MapControls(PanelUi ui, IMapCanvas c, float x, float y, float sh, float px, out bool closeMap)
    {
        closeMap = false;
        var path = Path;
        if (path == null) return new Box(x, y, 0, 0);
        double now = Plugin.Now;
        bool engaged = AutopilotLink.Engaged(now);
        float pw = px * 30;
        if (routePanel && mapPanel && !engaged)
        {
            userNames.Clear();
            foreach (var u in Tracker.UserPoints) userNames.Add(u.Name);
            var hit = AutopilotPanel.RoutePanel(ui, x, y, pw, sh, px, path, Limits, Snap.MaxSpeed, Snap.MaxAccel, Snap.Signature, userNames, adding,
                Binds.Autopilot.Display, now, "Shift+click a point on the map");
            RouteHit(hit);
            return hit.Panel;
        }
        bool resume = !engaged && resumeTarget != null && path.TargetName == resumeTarget;
        float rowH = px * 1.9f, h = px * 2.4f + rowH + (resume ? rowH + px * 0.7f : 0) + px * 1.0f + px * 1.3f;
        c.Rect(x, y, pw, h, AutopilotPanel.Panel);
        c.Rect(x, y, pw, 2, engaged ? AutopilotPanel.Stop : AutopilotPanel.Go);
        string title = (engaged ? "AUTOPILOT FLYING  to " : "DESTINATION  ") + path.TargetName;
        c.Text(title, x + px, y + px * 0.6f, px, AutopilotPanel.Text);
        float by = y + px * 2.4f;
        if (engaged)
        {
            float w = c.TextWidth("STOP AUTOPILOT", px) + px * 2.4f;
            if (ui.Button(new Box(x + pw / 2 - w / 2, by, w, rowH), "STOP AUTOPILOT", px, AutopilotPanel.Stop)) ConfirmStop();
        }
        else
        {
            if (resume)
            {
                string rt = "RESUME ROUTE";
                float w = c.TextWidth(rt, px) + px * 2.4f;
                if (ui.Button(new Box(x + pw / 2 - w / 2, by, w, rowH), rt, px, AutopilotPanel.Go, true, AutopilotLink.Count > 0)) Resume();
                by += rowH + px * 0.7f;
            }
            AutopilotPanel.Choice(ui, x + pw / 2, by, px, AutopilotLink.Count > 0, out bool auto, out bool manual);
            if (auto) { if (AutopilotLink.Count == 0) Notify(NoPb, false); else { routePanel = mapPanel = true; adding = false; } }
            else if (manual) { Manual(); closeMap = true; }
        }
        by += rowH + px * 0.5f;
        string hint = engaged ? "Moving the controls also hands the ship back" : path.DetoursAhead > 0 ? path.DetoursAhead + " detour(s) on the way" : Fmt.Dist(path.Distance) + "  ETA " + Fmt.Time(path.Eta);
        c.Text(hint, x + px, by, px * 0.85f, AutopilotPanel.Dim);
        return new Box(x, y, pw, h);
    }

    private static string NonEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;

    /// <summary>The full map picks its camera up from the preview's.</summary>
    public static OrbitCamera Camera => scene.Cam;
}
