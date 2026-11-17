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
///  - While it flies, a status line shows at the top and STOP AUTOPILOT sits under the list (it asks first; the
///    "System Map: stop autopilot" hotbar action doesn't). Ctrl+M opens the route panel to change the flight: edits
///    are held until APPLY sends them, CANCEL drops them. Touching the controls (or the PB's "manual" hotbar action) lets go at once, with a pop-up
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
    private static bool reframe, navOpen, autoShown;
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
    /// <summary>The route panel opened while flying: the plan and limits as the autopilot has them (CANCEL goes back).</summary>
    private static RouteTracker.State flightEdit;
    private static RouteLimits flightLimits;
    private static int restoredId = -1; // the PB route last taken back (once per route, seat and session)

    private static RouteLimits Limits => Plugin.Settings.Limits;

    public static void Reset()
    {
        Visible = false; autoShown = false; lastTarget = null; prevSel = null; promptUntil = 0; nextRoute = 0; Snap.FnbTargetName = null;
        Tracker.Clear(); Tracker.Hold = Tracker.Adding = false;
        publishedRevision = -1; lastNext = -1;
        routePanel = adding = false; lastState = null; flyingTarget = null; flyingThrust = flyingBrake = 1;
        resumeTarget = null; stoppingSelf = asking = mapPanel = routePin = false; steeredTo = null; restoredId = -1;
        flightEdit = null; flightLimits = null; listHidden = false;
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
        mouse.Reset(scene);
        FlipAndBurnLink.BlockSearch(false);
    }

    private static double dodgeNoteAt;

    /// <summary>Ctrl+Shift+M: send the route with the suggested way round an asteroid (never sent by itself).</summary>
    public static void SteerRound()
    {
        if (!AutopilotLink.Engaged(Plugin.Now) || Path == null || !AsteroidWatch.Changed) { Notify("No asteroid to steer round.", false); return; }
        if (Send("update", quiet: false)) Notify("Autopilot: steering round the asteroid.");
    }

    /// <summary>
    /// Every frame while AFK: the full map and the pinned preview stay up as a read-only view. Your ship, the route
    /// and the autopilot's status keep updating; nothing is written to the PB or Flip and Burn, and no input is taken.
    /// </summary>
    public static void ViewUpdate(double now)
    {
        cursorFree = false;
        mouse.Reset(scene);
        FlipAndBurnLink.BlockSearch(false);
        KeepNavOpen.Intercept = null;
        GameState.Fill(Snap, now);
        AutopilotLink.Update(now);
        if (now >= nextRoute)
        {
            nextRoute = now + 0.5;
            Tracker.Update(Snap.FnbTargetName, Snap.FnbTarget, Snap.HasShip, Snap.ShipPos, Snap.ShipVel, Snap.MaxAccel, Snap.MaxSpeed);
            Project();
        }
        if (Path != null && Snap.HasShip) Snap.RecordTrack(Snap.ShipPos, now, Path.Distance);
    }

    /// <summary>Every frame, from Plugin.Update.</summary>
    public static void Update(double now)
    {
        GameState.Fill(Snap, now);
        var leftWhy = Seat.Update(Snap.InCockpit, Snap.CharacterId, Snap.Dead);
        if (leftWhy != null) { CancelRoute(leftWhy); restoredId = -1; }
        bool was = navOpen;
        navOpen = FlipAndBurnLink.IsNavMenuOpen();
        if (navOpen && !was)
        {
            if (Plugin.Settings.ShowMapWithNav && !Visible) { Visible = true; autoShown = true; reframe = true; }
        }
        else if (!navOpen && was && !listHidden) // (hidden under a pop-up: nothing closes with it)
        {
            // F&B closed the list on a pick (our patch missing or bypassed): open it again
            if (Plugin.Settings.KeepNavOpen && !closingSelf && Snap.FnbTargetName != null && Snap.FnbTargetName != prevSel)
            { FlipAndBurnLink.OpenNavMenu(); navOpen = true; }
            else
            {
                if (autoShown && !Pinned) Visible = false;
                autoShown = false;
                adding = false;
                if (now > panelGrace && !mapPanel) routePanel = false;
            }
        }
        prevSel = Snap.FnbTargetName;
        closingSelf = false;
        KeepNavOpen.Intercept = PickWaypoint;
        if (mapPanel && !MapScreen.IsOpen) { mapPanel = false; if (!navOpen) routePanel = adding = false; }
        if (!navOpen && !mapPanel && now > panelGrace && !listHidden) { routePanel = false; adding = false; }

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
            Tracker.Curved = Limits.Parabolic;
            if (Tracker.TargetName == null && engaged) RestoreRoute();
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
        if (flightEdit != null && (!routePanel || !engaged))
        {
            if (engaged) DiscardFlightEdit(); // the list or map closed: the changes go
            else { flightEdit = null; flightLimits = null; } // the flight ended meanwhile: keep them as a plan
        }
        if (Tracker.Revision != publishedRevision && flightEdit == null)
        {
            publishedRevision = Tracker.Revision;
            FlipAndBurnLink.SetDetours(Tracker.Waypoints);
            if (Path != null && engaged && Tracker.TargetName == flyingTarget) Send("update", quiet: true);
            else if (Path != null && !engaged && Plugin.Settings.AutopilotAutoSend) Send("load", quiet: true);
        }
        AsteroidWatch.Update(now, Snap, Tracker, engaged);
        AutopilotPanel.SteerKey = Binds.Steer.IsNone ? "" : Binds.Steer.Display;
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
        if (routePin && Tracker.TargetName == null) { routePin = false; if (!Plugin.Settings.Pip && !navOpen && !MapScreen.IsOpen) Visible = false; }
        if (resumeTarget != null && Tracker.TargetName != resumeTarget) resumeTarget = null; // picked somewhere else
        if (engaged && flightEdit == null && flyingTarget != null && Tracker.TargetName == flyingTarget) SteerSelection(); else steeredTo = null;

        if (OutOfCockpit) { if (MapScreen.IsOpen) MapScreen.Close(); mouse.Reset(scene); FlipAndBurnLink.BlockSearch(false); return; }
        HandleKeys();
        HoldCursor(now);
        // F&B's search box takes no typing while the mouse is over the map, so the map's keys work there
        cursorFree = InGame && (navOpen || holding || RichHudCursor.Shown);
        FlipAndBurnLink.BlockSearch(navOpen && OverMap());
        if (Visible && cursorFree) { HandleMouse(); if (navOpen) MapKeys(); }
        else mouse.Reset(scene);
        if (pendingAsk != null && InGame) { var a = pendingAsk; pendingAsk = null; a(); }
    }

    private static double noRichHudNote;
    private static bool holding; // the free-mouse key is held (and Rich HUD has its cursor up)

    /// <summary>
    /// The free-mouse key (F2), held: Rich HUD's cursor comes up and takes the mouse off the ship, so you can use the map
    /// while the autopilot flies without turning the ship (which would take the controls from it).
    /// </summary>
    private static void HoldCursor(double now)
    {
        holding = false;
        if (!KeysFree() || !Binds.Cursor.Held()) return;
        if (RichHudCursor.Request()) { holding = true; return; }
        if (now >= noRichHudNote) { Notify("Freeing the mouse needs the Rich HUD Master mod (it's on SDX2; not in this world).", false); noRichHudNote = now + 10; }
    }

    /// <summary>The mouse cursor is free on the game screen: F&amp;B's N list or another Rich HUD menu is open.</summary>
    private static bool cursorFree;

    /// <summary>The game screen has focus (no menu, terminal or pop-up over it).</summary>
    private static bool InGame => MyScreenManager.GetScreenWithFocus() is MyGuiScreenGamePlay;

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
        Add(Binds.Map, "map, full map, off"); Add(Binds.Autopilot, "autopilot"); Add(Binds.Pin, "pin on screen"); Add(Binds.Cursor, "(hold) mouse");
        return sb.ToString();
    }

    /// <summary>The map key in the full map: close it and hide the map window, ending the cycle.</summary>
    public static void CycleOff()
    {
        Visible = false;
        autoShown = false;
    }

    /// <summary>Ctrl+M: open the N list with the route panel (while flying, to change the route: APPLY or CANCEL).</summary>
    public static void AutopilotKey()
    {
        if (AutopilotLink.Engaged(Plugin.Now))
        {
            if (Path == null) { Notify("The autopilot is flying a route the map doesn't have yet: try again in a moment.", false); return; }
            if (!navOpen) FlipAndBurnLink.OpenNavMenu();
            panelGrace = Plugin.Now + 1.5;
            OpenFlightEdit(false);
            return;
        }
        if (Path == null) { Notify("No route: pick a destination in Flip and Burn's list (N) first.", false); return; }
        if (AutopilotLink.Count == 0) { Notify(NoPb, false); return; }
        if (!navOpen) FlipAndBurnLink.OpenNavMenu();
        routePanel = true; adding = false; panelGrace = Plugin.Now + 1.5;
    }

    private const string NoPb = "No autopilot PB on this ship: load the Protogen Systems - Autopilot script into a programmable block.";

    /// <summary>The route panel while flying: edits are held (not sent) until APPLY; CANCEL goes back to these.</summary>
    private static void OpenFlightEdit(bool inMap)
    {
        if (flightEdit == null) { flightEdit = Tracker.Save(); flightLimits = Limits.Clone(); }
        routePanel = true; mapPanel = inMap; adding = false;
    }

    /// <summary>APPLY while flying: the changed route and limits go to the autopilot, which flies on.</summary>
    private static void ApplyFlightEdit()
    {
        flightEdit = null; flightLimits = null;
        routePanel = adding = mapPanel = false;
        Plugin.SaveSettings();
        if (Path == null) return;
        flyingThrust = Limits.ThrustFraction(Snap.DriveWeight, Snap.Signature?.Gain ?? 4);
        flyingBrake = Limits.BrakeFraction(Snap.DriveWeight, Snap.Signature?.Gain ?? 4);
        flyingTarget = Path.TargetName; steeredTo = null;
        publishedRevision = Tracker.Revision;
        FlipAndBurnLink.SetDetours(Tracker.Waypoints);
        if (Send("update", quiet: false)) Notify("Autopilot route updated: flying to " + Path.TargetName + ".");
    }

    /// <summary>CANCEL while flying (or the list closed): back to the route and limits the autopilot is flying.</summary>
    private static void DiscardFlightEdit()
    {
        var st = flightEdit; var lim = flightLimits;
        flightEdit = null; flightLimits = null;
        if (st == null) return;
        bool changed = Tracker.Revision != publishedRevision;
        Tracker.Load(st);
        if (lim != null) Plugin.Settings.Limits = lim;
        publishedRevision = Tracker.Revision;
        FlipAndBurnLink.SetDetours(Tracker.Waypoints);
        steeredTo = null; nextRoute = 0;
        if (changed) Notify("Changes dropped: the autopilot flies on as before.");
    }

    /// <summary>The "System Map: stop autopilot" hotbar action: stops the flight at once (you have the controls).</summary>
    public static void HotbarStop()
    {
        if (PlayGuard.Blocked) { Notify("System Map is paused (" + PlayGuard.Reason + ")", false); return; }
        if (!AutopilotLink.Engaged(Plugin.Now)) { Notify("The autopilot isn't flying.", false); return; }
        if (flightEdit != null) DiscardFlightEdit();
        routePanel = adding = mapPanel = false;
        StopAutopilot();
    }

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

    public static void Engage() { if (DrivesReady(Engage)) EngageNow(); }

    private static readonly List<Sandbox.Game.Entities.MyThrust> drivesOff = new List<Sandbox.Game.Entities.MyThrust>();

    /// <summary>
    /// Before a route starts: if fusion drives are switched off, a pop-up offers to turn them on and go on
    /// (<paramref name="then"/> runs again after). True when they're all on (or there's no ship to check).
    /// </summary>
    private static bool DrivesReady(Action then)
    {
        DriveCheck.Read(DriveCheck.Ship, drivesOff, out int total);
        if (drivesOff.Count == 0) return true;
        if (asking) return false;
        asking = true;
        string n = drivesOff.Count == total ? (total == 1 ? "The fusion drive is" : "All " + total + " fusion drives are") : drivesOff.Count + " of " + total + " fusion drives are";
        Ask("Main drives off", n + " switched off, so the autopilot can't push the ship" + (drivesOff.Count < total ? " at full thrust" : "") + ". Turn them on and start the route?",
            "TURN ON & ENGAGE", "CANCEL", yes =>
            {
                asking = false;
                if (!yes) return;
                int on = DriveCheck.TurnOn(drivesOff);
                Notify(on == 1 ? "Fusion drive switched on" : on + " fusion drives switched on", false);
                DriveCheck.Read(DriveCheck.Ship, drivesOff, out _);
                if (drivesOff.Count == 0) then();
                else Notify("Some drives are still off: switch them on in the terminal", false);
            });
        return false;
    }

    private static void EngageNow()
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

    /// <summary>STOP AUTOPILOT: ask first, since it hands the ship back mid-route.</summary>
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
        if (!DrivesReady(Resume)) return;
        AsteroidWatch.Reset();
        stoppingSelf = false;
        if (!Send("engage", quiet: false)) return;
        flyingTarget = resumeTarget; resumeTarget = null;
        routePanel = adding = mapPanel = false;
        if (navOpen) { closingSelf = true; FlipAndBurnLink.CloseNavMenu(); }
        Notify("Autopilot resumed: flying to " + flyingTarget + ". Move or turn to take over.");
    }

    private static Action pendingAsk;
    private static bool listHidden; // F&B's list closed while a pop-up asks

    /// <summary>A yes/no pop-up (the game's own message box, so it has a mouse cursor).</summary>
    private static void Ask(string caption, string text, string yes, string no, Action<bool> done)
    {
        // a menu or other screen is open (the pause menu, say): asked when you're back in the game
        if (!InGame) { pendingAsk = () => Ask(caption, text, yes, no, done); return; }
        // F&B's list (Rich HUD) still takes clicks under the game's pop-up, so a click on a button would pick in the
        // list too: it closes while asking and comes back after, unless the answer started the autopilot
        bool hid = navOpen;
        if (hid) { listHidden = true; closingSelf = true; FlipAndBurnLink.CloseNavMenu(); }
        void Answer(bool y)
        {
            listHidden = false;
            done(y);
            if (hid && (!y || !AutopilotLink.Engaged(Plugin.Now)) && !FlipAndBurnLink.IsNavMenuOpen())
            { FlipAndBurnLink.OpenNavMenu(); panelGrace = Plugin.Now + 1.5; }
        }
        try
        {
            MyGuiSandbox.AddScreen(MyGuiSandbox.CreateMessageBox(MyMessageBoxStyleEnum.Info, MyMessageBoxButtonsType.YES_NO,
                new System.Text.StringBuilder(text), new System.Text.StringBuilder(caption),
                yesButtonText: VRage.Utils.MyStringId.GetOrCompute(yes), noButtonText: VRage.Utils.MyStringId.GetOrCompute(no),
                callback: r => Answer(r == MyGuiScreenMessageBox.ResultEnum.YES), focusedResult: MyGuiScreenMessageBox.ResultEnum.NO));
        }
        catch (Exception e) { Plugin.LogThrottled("pop-up failed: " + e.Message); Answer(false); }
    }

    /// <summary>While the autopilot flies: Flip and Burn's destination is the point it's flying to now (each detour and
    /// waypoint in turn, then the destination), so F&amp;B's HUD marker and figures follow the route.</summary>
    /// <summary>
    /// The autopilot flies a route the plugin doesn't know (the world was loaded, or you changed sector): show it on the
    /// maps again from the PB's copy, as if it had been sent this session. Nothing is sent back to the PB.
    /// </summary>
    private static void RestoreRoute()
    {
        var r = AutopilotLink.Route; var st = AutopilotLink.Status;
        if (r == null || st == null || !st.Flying || r.Id == restoredId || !Snap.HasShip) return;
        restoredId = r.Id;
        if (!Tracker.Restore(r, st.Wp, Snap.ShipPos, Snap.FnbTargetName, Snap.FnbTarget)) return;
        flyingTarget = r.Target;
        flyingThrust = Math.Max(0.01, Math.Min(1, r.Thrust));
        flyingBrake = r.Brake > 0 ? Math.Max(0.01, Math.Min(1, r.Brake)) : flyingThrust;
        resumeTarget = null; steeredTo = null;
        publishedRevision = Tracker.Revision; // the PB has it already
        FlipAndBurnLink.SetDetours(Tracker.Waypoints);
        Plugin.Log("route taken back from the autopilot PB: " + r.Target + " (" + Tracker.Waypoints.Count + " points, next " + Tracker.Next + ")");
        Notify("Autopilot route back on the map: flying to " + r.Target + ".");
    }

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

    /// <summary>
    /// Dragging a flip point (or a bend) up or down in the full map: the leg ending at <paramref name="leg"/> curves
    /// through the new point without stopping. Back near the straight line, it's straight again.
    /// </summary>
    public static string BendLeg(string leg, V3 pos)
    {
        var p = Path;
        int i = p?.LegTo(leg) ?? -1;
        if (i < 0) return "That leg is gone";
        var r = p.Legs[i];
        double len = V3.Distance(r.Start, r.Target);
        double off = V3.SegmentDistance(r.Start, r.Target, pos, out _);
        nextRoute = 0;
        if (off < Math.Max(1000, len * 0.005)) { Tracker.Bend(leg, null); return "Leg straightened"; }
        Tracker.Bend(leg, pos);
        return "Leg bent " + Fmt.Dist(off) + " off the line: curves through it without stopping";
    }

    /// <summary>Shift+right-click on a bend: the leg is straight again.</summary>
    public static string StraightenLeg(string leg)
    {
        if (leg == null || !Tracker.Bends.ContainsKey(leg)) return "That leg isn't bent";
        Tracker.Bend(leg, null);
        nextRoute = 0;
        return "Leg straightened";
    }

    /// <summary>Where a leg's flip point drag starts from: its bend, if it has one.</summary>
    public static bool LegBend(string leg, out V3 pos) => Tracker.Bends.TryGetValue(leg ?? "", out pos);

    /// <summary>Shift+right-click in the full map: a waypoint into the route where it fits (name null = "Waypoint n").</summary>
    public static string InsertWaypoint(string name, V3 pos)
    {
        if (Tracker.TargetName == null) return "Set a destination first (Shift+click a point)";
        if (Tracker.UserPoints.Count >= 8) return "Up to 8 waypoints";
        name = name ?? Tracker.NextUserName();
        if (!Tracker.InsertUser(name, pos)) return "Can't add " + name + ": it's in the route already";
        nextRoute = 0;
        int i = Tracker.UserPoints.FindIndex(u => u.Name == name);
        return "Waypoint added: " + name + " (point " + (i + 1) + " of " + (Tracker.UserPoints.Count + 1) + ")";
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
        // a curved route is drawn as planned (the autopilot holds to it): the projection steers point to point like
        // flip and burn, so on a curve's many points it overshoots and draws a fan of straight burns
        if (p == null || !Snap.HasShip || p.Legs.Exists(l => l.Curved)) { Snap.Prediction.Clear(); Snap.PredictionPhases.Clear(); Snap.PredictionArrives = true; return; }
        ahead.Clear();
        ahead.AddRange(p.Ahead());
        Snap.PredictionArrives = Trajectory.Predict(Snap.ShipPos, Snap.ShipVel, ahead, p.MaxAccel, p.MaxSpeed, Tracker.ArriveRadius, Snap.Prediction, p.Eta, phases: Snap.PredictionPhases, stops: true);
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
    private static bool InPip => Pinned && !navOpen;
    /// <summary>Pinned by the setting, or for this route only (<see cref="FullMapClosed"/>).</summary>
    private static bool Pinned => Plugin.Settings.Pip || routePin;
    public static bool PinnedNow => Pinned;
    private static bool routePin;

    /// <summary>
    /// The full map closed (Esc, the map key, N): a preview that was pinned stays pinned, and with a route picked it's
    /// pinned for that route (unpinned again when the route is cleared). Otherwise the map key's "off" stands.
    /// </summary>
    public static void FullMapClosed()
    {
        if (!Plugin.Settings.Pip && Tracker.TargetName == null) return;
        if (!Plugin.Settings.Pip) routePin = true;
        Visible = true; autoShown = false; reframe = true;
    }
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
        if (!on) routePin = false;
        if (on) { Visible = true; autoShown = false; }
        else if (!navOpen) Visible = false;
        reframe = true;
        Plugin.SaveSettings();
        return on ? "Map preview pinned on screen (" + CornerName(Plugin.Settings.PipCorner) + ")." + (Binds.Pin.IsNone ? "" : " " + Binds.Pin.Display + " to unpin.")
                  : "Map preview unpinned.";
    }

    public static string TogglePip() => SetPip(!(Pinned && Visible));

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

    /// <summary>The full map's mouse controls, in the preview while the N list is open and the mouse is over it.</summary>
    private static readonly MapMouse mouse = new MapMouse
    {
        Toast = t => Notify(t),
        Following = () => Plugin.Settings.PreviewFollow,
        Unfollow = () => Plugin.Settings.PreviewFollow = false,
        Turning = () => Plugin.Settings.PreviewFollow && Plugin.Settings.FollowHeading,
        Heading = previewHeading,
    };

    /// <summary>The mouse is over the preview's map (not its FOLLOW button or F&amp;B's list), with the cursor free.</summary>
    private static bool OverMap()
    {
        if (!Visible || !cursorFree || MapScreen.IsOpen || OutOfCockpit) return false;
        Mouse(out float mx, out float my);
        var full = MyGuiManager.GetFullscreenRectangle();
        return WindowBox().Contains(mx, my) && !followBox.Contains(mx, my) && !SideLayout.FnbList(full.Width, full.Height).Contains(mx, my);
    }

    private static bool followPress; // the left button went down on FOLLOW SHIP

    private static void HandleMouse()
    {
        var input = MyInput.Static;
        Mouse(out float mx, out float my);
        bool over = OverMap();
        mouse.Handle(scene, input, mx, my, over || mouse.Busy);
        bool onFollow = Visible && followBox.Contains(mx, my);
        if (input.IsNewLeftMousePressed()) followPress = onFollow;
        if (input.IsNewLeftMouseReleased())
        {
            if (followPress && onFollow)
            {
                bool f = Plugin.Settings.PreviewFollow = !Plugin.Settings.PreviewFollow;
                if (f && Snap.HasShip) scene.Cam.FlyTo(Snap.ShipPos, Math.Min(scene.Cam.GoalDistance, MapScene.ShipViewDistance));
                Plugin.SaveSettings();
            }
            followPress = false;
        }
        if (over && input.IsNewMiddleMousePressed()) { reframe = true; Plugin.Settings.PreviewFollow = false; }
    }

    /// <summary>The preview's help with the N list open: the full map's mouse controls and keys (keys while the mouse is over it).</summary>
    private static string PreviewHelp(string keys)
    {
        const string s = MapScene.Sep;
        var o = Plugin.Settings.Options;
        string On(bool b) => b ? "on" : "off";
        return "VIEW|Drag orbit" + s + "Right-drag pan" + s + "Wheel zoom" + s + "Click focus" + s + "Middle-click re-frame" + s + "S follow ship" + s + "U turn with ship: " + On(Plugin.Settings.FollowHeading) + s + "F frame route" + s + "H whole system\n"
            + "ROUTE|Shift+click set destination" + s + "Shift+right-click add / remove waypoint" + s + "Shift+right-drag add above / below" + s + "Drag your waypoint up / down" + s + "Drag a flip point: bend the leg" + s + "Shift+right-click a bend: straighten" + s + "A autopilot" + s + "R re-plan" + s + "V detours: " + On(Plugin.Settings.AvoidBodies) + s + "C copy flip GPS\n"
            + "SHOW|G grid: " + On(o.Grid) + s + "O orbits: " + On(o.Orbits) + s + "X sectors: " + On(o.Sectors) + s + "T stations: " + On(o.Stations) + s + "P GPS: " + On(o.Gps) + s + "L labels: " + On(o.Labels) + s + "J moons: " + On(o.Moons) + s + "K theme: " + MapTheme.Current.Name + s + "Keys work while the mouse is over the map (F&B's search waits)\n"
            + keys;
    }

    /// <summary>The full map's keys, while the mouse is over the preview (F&amp;B's search box is shut meanwhile).</summary>
    private static void MapKeys()
    {
        var input = MyInput.Static;
        if (input == null || !navOpen || !OverMap() || !KeysFree() || input.IsAnyCtrlKeyPressed() || input.IsAnyAltKeyPressed()) return;
        var s = Plugin.Settings; var o = s.Options;
        bool save = true;
        if (input.IsNewKeyPressed(MyKeys.F)) { s.PreviewFollow = false; scene.FrameDefault(false); }
        else if (input.IsNewKeyPressed(MyKeys.H)) { s.PreviewFollow = false; scene.Cam.FlyTo(V3.Zero, 6e7); }
        else if (input.IsNewKeyPressed(MyKeys.S))
        {
            s.PreviewFollow = !s.PreviewFollow;
            if (s.PreviewFollow && Snap.HasShip) scene.Cam.FlyTo(Snap.ShipPos, Math.Min(scene.Cam.GoalDistance, MapScene.ShipViewDistance));
            Notify(!s.PreviewFollow ? "Follow ship: off" : Snap.HasShip ? "Follow ship: on" : "Follow ship: on (no ship yet: sit in a cockpit)");
        }
        else if (input.IsNewKeyPressed(MyKeys.U))
        {
            s.FollowHeading = !s.FollowHeading; previewHeading.Offset = 0;
            Notify(s.FollowHeading ? (s.PreviewFollow ? "Turn with ship: on (your travel points up)" : "Turn with ship: on (when following: S)") : "Turn with ship: off");
        }
        else if (input.IsNewKeyPressed(MyKeys.C)) Notify(MapScreen.CopyFlip());
        else if (input.IsNewKeyPressed(MyKeys.A))
        {
            if (AutopilotLink.Engaged(Plugin.Now)) ConfirmStop();
            else if (Path == null) Notify("No route: Shift+click a destination first.", false);
            else if (AutopilotLink.Count == 0) Notify(NoPb, false);
            else OpenRoutePanel();
        }
        else if (input.IsNewKeyPressed(MyKeys.R)) { Replan(); Notify("Route planned again from here."); }
        else if (input.IsNewKeyPressed(MyKeys.V))
        {
            s.AvoidBodies = !s.AvoidBodies; Replan();
            Notify(s.AvoidBodies ? "Detours round worlds: on" : "Detours off: straight line");
        }
        else if (input.IsNewKeyPressed(MyKeys.G)) o.Grid = !o.Grid;
        else if (input.IsNewKeyPressed(MyKeys.O)) o.Orbits = !o.Orbits;
        else if (input.IsNewKeyPressed(MyKeys.X)) o.Sectors = !o.Sectors;
        else if (input.IsNewKeyPressed(MyKeys.T)) o.Stations = !o.Stations;
        else if (input.IsNewKeyPressed(MyKeys.P)) o.Gps = !o.Gps;
        else if (input.IsNewKeyPressed(MyKeys.L)) o.Labels = !o.Labels;
        else if (input.IsNewKeyPressed(MyKeys.J)) o.Moons = !o.Moons;
        else if (input.IsNewKeyPressed(MyKeys.K)) Notify(MapScreen.NextTheme());
        else save = false;
        if (save) Plugin.SaveSettings();
    }

    /// <summary>OnlyInCockpit is on and you're not in a ship controller: nothing drawn, no keys. Visible and the pin are
    /// kept, so it all comes back when you sit down.</summary>
    public static bool OutOfCockpit => Plugin.Settings.OnlyInCockpit && !Snap.InCockpit;

    /// <summary>Drawn after the game screen (GamePlayDrawPatch), on the main thread. Not over the pause menu, the
    /// terminal or a pop-up. While Rich HUD's cursor shows, a cursor is drawn over the map too (its own goes under).</summary>
    public static void Draw()
    {
        double now = Plugin.Now, dt = Math.Min(0.1, Math.Max(0, now - lastDraw));
        lastDraw = now;
        if (MapScreen.IsOpen || OutOfCockpit || !InGame) return;
        if (PlayGuard.Blocked && !(PlayGuard.ViewOnly && Visible && Pinned)) return; // AFK: only the pinned preview
        panelBox = default;
        DrawAll(now, dt);
        if (!cursorFree || !(RichHudCursor.Shown || holding)) return;
        Mouse(out float cx, out float cy);
        if ((Visible && WindowBox().Contains(cx, cy)) || panelBox.Contains(cx, cy))
            RichHudCursor.Draw(cx, cy, MyGuiManager.GetFullscreenRectangle().Height);
    }

    private static Box panelBox; // the route panel this frame

    private static void DrawAll(double now, double dt)
    {
        var full = MyGuiManager.GetFullscreenRectangle();
        float sw = full.Width, sh = full.Height;
        float px = Math.Max(14f, sh / 52f);
        var path = Path;
        string k = Binds.Map.Display, stop = Binds.Autopilot.Display;
        bool engaged = AutopilotLink.Engaged(now);
        var input = MyInput.Static;
        float mx = float.NaN, my = float.NaN;
        if (cursorFree) Mouse(out mx, out my);
        ui.Input(canvas, mx, my, cursorFree && input.IsLeftMousePressed(), cursorFree && input.IsNewLeftMousePressed(), cursorFree && input.IsNewLeftMouseReleased());

        // HUD: autopilot status and asteroid warning
        float hudY = sh * 0.05f;
        if (engaged) hudY = AutopilotPanel.StatusLine(canvas, sw, hudY, px * 0.9f, flyingTarget ?? NonEmpty(AutopilotLink.Status?.Target) ?? "route", AutopilotLink.Status, stop).Bottom + px * 0.5f;
        if (!double.IsNaN(AsteroidWatch.NearestDistance) && !PlayGuard.Blocked)
            AutopilotPanel.AsteroidWarning(canvas, sw, Math.Max(hudY, sh * 0.12f), px, AsteroidWatch.NearestDistance, Snap.ShipVel.Length,
                AsteroidWatch.NearestOnRoute, AsteroidWatch.NearestDodged, engaged, now, Binds.Steer.Display);

        if (Visible)
        {
            var box = WindowBox();
            bool pip = InPip;
            if (pip != wasPip) { wasPip = pip; reframe = true; }
            scene.Snap = Snap; scene.Path = path; scene.Mini = pip; scene.Bends = Tracker.Bends;
            scene.Cam.Setup(box.X, box.Y, box.W, box.H);
            if (reframe) { scene.FrameDefault(true); reframe = false; }
            bool follow = Plugin.Settings.PreviewFollow;
            if (follow && Snap.HasShip) { scene.Cam.GoalTarget = Snap.ShipPos; scene.Cam.Target = Snap.ShipPos; }
            if (follow && Snap.HasShip && Plugin.Settings.FollowHeading) previewHeading.Update(scene.Cam, Snap.ShipVel, Snap.ShipFwd, dt);
            scene.Cam.Tick(dt);
            string keys = KeysHelp();
            if (pip) { scene.MouseX = mx; scene.MouseY = my; scene.Help = ""; }
            else if (navOpen) { scene.MouseX = mx; scene.MouseY = my; scene.Help = PreviewHelp(keys); }
            else { scene.MouseX = scene.MouseY = float.NaN; scene.Help = keys; }
            scene.Draw(canvas, box);
            // FOLLOW toggle, top right of the preview (clickable while the cursor is free)
            float fpx = px * 0.75f;
            string ft = "FOLLOW SHIP";
            float fw = canvas.TextWidth(ft, fpx) + fpx * 1.6f, fh = fpx * 1.7f;
            followBox = new Box(box.Right - fw - fpx * 0.6f, scene.TitleBottom + fpx * 0.5f, fw, fh);
            if (cursorFree)
            {
                ui.Button(followBox, ft, fpx, MapScene.ShipColor, follow); // clicked in HandleMouse
            }
            else if (follow)
            {
                canvas.Rect(followBox.X, followBox.Y, fw, fh, MapColor.Rgb(0x0A121C, 200));
                canvas.Text(ft, followBox.X + fpx * 0.8f, followBox.Y + fpx * 0.35f, fpx, MapScene.ShipColor);
            }
        }
        else followBox = default;

        if (path == null || PlayGuard.Blocked) return; // AFK: no prompts or buttons
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
            if (!routePanel) return;
            y += px * 2.6f;
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
        var hit = AutopilotPanel.RoutePanel(ui, px0, list.Y, pw, sh, px, path, Limits, Snap.MaxSpeed, Snap.MaxAccel, Snap.Signature, userNames, adding, Binds.Autopilot.Display, now, null, engaged);
        panelBox = hit.Panel;
        RouteHit(hit);
    }

    private static void RouteHit(AutopilotPanel.RouteHit hit)
    {
        if (hit.Changed) nextRoute = 0;
        if (hit.Remove >= 0) { Tracker.RemoveUser(hit.Remove); nextRoute = 0; }
        if (hit.ToggleAdd) adding = !adding;
        if (hit.Cancel)
        {
            if (flightEdit != null) DiscardFlightEdit(); // while flying: drop the changes
            routePanel = adding = mapPanel = false; Plugin.SaveSettings();
        }
        else if (hit.Engage) { if (flightEdit != null) ApplyFlightEdit(); else Engage(); }
    }

    /// <summary>A in the full map: the route panel in the map (while flying, to change the route).</summary>
    public static string MapAutopilotKey()
    {
        if (AutopilotLink.Engaged(Plugin.Now))
        {
            if (Path == null) return "The autopilot is flying a route the map doesn't have yet.";
            OpenFlightEdit(true);
            return null;
        }
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
        if (routePanel && mapPanel)
        {
            userNames.Clear();
            foreach (var u in Tracker.UserPoints) userNames.Add(u.Name);
            var hit = AutopilotPanel.RoutePanel(ui, x, y, pw, sh, px, path, Limits, Snap.MaxSpeed, Snap.MaxAccel, Snap.Signature, userNames, adding,
                Binds.Autopilot.Display, now, "Shift+click a point on the map", engaged);
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
