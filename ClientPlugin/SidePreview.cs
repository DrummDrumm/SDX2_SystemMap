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
    private static string manualTarget; // MANUAL was picked for the route to this: F&B's pick follows the route
    private static double panelGrace; // Ctrl+M opens the N list: give F&B a moment before "list closed" closes the panel
    private static string lastState, flyingTarget;
    private static double flyingThrust = 1, flyingBrake = 1;
    /// <summary>The route the autopilot stopped part-way along (manual control): RESUME ROUTE flies the rest.</summary>
    private static string resumeTarget;
    private static bool stoppingSelf, asking;
    /// <summary>A change to the route the autopilot flies is waiting for its CHANGE ROUTE / KEEP FLYING pop-up.</summary>
    private static bool confirming;
    /// <summary>The route the autopilot flies, as it was when the edit opened (drawn under the edited copy).</summary>
    private static V3[] flownPoints;
    /// <summary>What has been changed in the open edit (for the SEND ROUTE pop-up), and what puts back anything outside the plan.</summary>
    private static readonly List<string> draftChanges = new List<string>();
    private static readonly List<Action> draftUndo = new List<Action>();
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
        resumeTarget = null; stoppingSelf = asking = confirming = mapPanel = routePin = false; steeredTo = null; restoredId = -1; manualTarget = null;
        flightEdit = null; flightLimits = null; listHidden = false;
        padCaptured = false; ui.Focus = -1;
        AutopilotLink.Reset(); FleetRoutes.Reset(); AsteroidWatch.Reset(); Seat.Reset(); GridObstacles.Reset();
    }

    /// <summary>Drawing quality, everywhere it applies: the sprite canvas, the map's curves, the destination view.</summary>
    public static void ApplyQuality(bool ultra)
    {
        GameCanvas.Ultra = ultra;
        MapScene.Detail = ultra ? 2 : 1;
        DestinationView.Ultra = ultra;
    }

    /// <summary>Another set of settings came into use (a different ship's): the map takes them up.</summary>
    public static void SettingsSwapped()
    {
        var s = Plugin.Settings;
        ApplySettings(s);
        reframe = true;
        if (s.Pip) { Visible = true; autoShown = false; }
        else if (!navOpen && !routePin && !MapScreen.IsOpen) Visible = false;
        OwnShipIcon.Refresh();
        try { if (Sandbox.Game.World.MySession.Static != null) Replan(); } catch (Exception e) { Plugin.LogThrottled("re-plan for the ship's settings failed: " + e.Message); }
    }

    public static void ApplySettings(MapSettings s)
    {
        scene.Opt = s.Options;
        MapScene.DefaultGridKm = s.DefaultGridKm;
        MapScene.ShipIconFromShip = s.ShipIconFromShip;
        ApplyQuality(s.Ultra);
        scene.Cam.Yaw = s.Yaw; scene.Cam.Pitch = s.Pitch;
        Tracker.Avoid = s.AvoidBodies;
        Tracker.AvoidFields = s.AvoidFields;
        RouteExport.Offset = s.OffsetKm * 1000;
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
        FleetRoutes.Update(now, Snap, Plugin.Settings.Options.Routes, AutopilotLink.FlownShip, AutopilotLink.FlownIdle);
        if (now >= nextRoute)
        {
            nextRoute = now + 0.1; // as often as when you're here: the figures on show stay current
            Tracker.Update(Snap.FnbTargetName, Snap.FnbTarget, Snap.HasShip, Snap.ShipPos, Snap.ShipVel, Snap.MaxAccel, Snap.MaxSpeed);
            Project();
        }
        if (Path != null && Snap.HasShip) Snap.RecordTrack(Snap.ShipPos, now, Path.Distance);
        AsteroidWatch.Update(now, Snap, Tracker, AutopilotLink.Engaged(now)); // a scan only: the warning stays current
    }

    /// <summary>Every frame, from Plugin.Update.</summary>
    public static void Update(double now)
    {
        GameState.Fill(Snap, now);
        var leftWhy = Seat.Update(Snap.InCockpit, Snap.CharacterId, Snap.Dead);
        // the route stays until you clear it or arrive (getting out of the seat, or respawning, doesn't drop it)
        if (leftWhy != null) restoredId = -1;
        bool was = navOpen;
        navOpen = FlipAndBurnLink.IsNavMenuOpen();
        // the full map is up: F&B's list (Rich HUD) would stay drawn under it and take the map's clicks as picks, so
        // it closes (N in the full map brings it back)
        if (navOpen && MapScreen.IsOpen) { closingSelf = true; FlipAndBurnLink.CloseNavMenu(); navOpen = false; }
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
        if (!navOpen && !listHidden) panelDismissed = false;
        KeepNavOpen.Intercept = PickWaypoint;
        // the full map's route editor closes with the full map; the second screen's map has it too (its ROUTE PLAN button)
        if (mapPanel && !MapScreen.IsOpen && !SecondScreen.IsOpen) { mapPanel = false; if (!navOpen) routePanel = adding = false; }
        if (!navOpen && !mapPanel && now > panelGrace && !listHidden) { routePanel = false; adding = false; }

        AutopilotLink.Update(now);
        FleetRoutes.Update(now, Snap, Plugin.Settings.Options.Routes, AutopilotLink.FlownShip, AutopilotLink.FlownIdle);
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
            // leading a formation: the autopilot holds this ship to the pace of the slowest ship in it, so the route is
            // planned (burn, flip, ETA) at that pace
            var fleet = AutopilotLink.Status;
            if (fleet != null && fleet.Leading && fleet.FleetShare > 0)
            {
                Tracker.AccelScale = Math.Min(Tracker.AccelScale, fleet.FleetShare);
                Tracker.BrakeScale = Math.Min(Tracker.BrakeScale, fleet.FleetShare);
            }
            if (fleet != null && fleet.Leading && fleet.FleetCap > 0) Tracker.SpeedCap = Tracker.SpeedCap > 0 ? Math.Min(Tracker.SpeedCap, fleet.FleetCap) : fleet.FleetCap;
            // what the ship can hold itself up with near a planet: its thrust at the autopilot's setting over its mass now
            // (how low routes go round, onto and off worlds). A route not yet flown is planned again when that changes a lot.
            double lift = Snap.MaxAccel * (engaged && flyingTarget != null ? flyingThrust : Limits.ThrustFraction(Snap.DriveWeight, Snap.Signature?.Gain ?? 4));
            DetourPlanner.ShipAccel = lift;
            if (!engaged && Path != null && GravityKnown() && (plannedLift > 0 ? Math.Abs(lift - plannedLift) > plannedLift * 0.1 : lift > 0) && now >= nextLiftReplan)
            {
                nextLiftReplan = now + 2;
                plannedLift = lift;
                Replan();
            }
            else if (plannedLift <= 0 || engaged) plannedLift = lift;
            Tracker.Hold = routePanel || engaged;
            Tracker.Adding = routePanel && adding;
            Tracker.Curved = Limits.Parabolic;
            // the PB flies a route this session didn't send (the world was loaded, or the sector changed): take it back,
            // even when F&B's remembered pick has already given the map a destination of its own
            // ... and when the map's copy was cleared while it still flies (destination removed, route cancelled)
            if (engaged && AutopilotLink.Route is { } held)
            {
                bool lostRoute = Tracker.TargetName == null && now >= nextRestore;
                // (not while an order we just wrote waits for the PB: its copy may still be the one before)
                bool foreign = held.Id != restoredId && !AutopilotLink.SentHere(held.Id) && !(AutopilotLink.Pending && now - AutopilotLink.SentAt < 10);
                // the map already flies this route (taken back before, or sent here): keep its copy, with any bends
                // and edits on it, instead of the PB's (which has none of the map's curve)
                if (foreign && !lostRoute && flyingTarget != null && flyingTarget == held.Target && Tracker.TargetName == held.Target) { restoredId = held.Id; foreign = false; }
                if (foreign || lostRoute) RestoreRoute(now, lostRoute ? "the map had no route" : "the PB's route id " + held.Id + " wasn't sent from here");
            }
            // the PB is past points the map hasn't counted (flown by at speed, not close): keep the plan with it
            if (engaged && flyingTarget != null && Tracker.TargetName == flyingTarget)
                Tracker.PassedTo(AutopilotLink.PassedOnRoute(Tracker.Waypoints.Count, Tracker.Revision));
            Tracker.Update(Snap.FnbTargetName, Snap.FnbTarget, Snap.HasShip, Snap.ShipPos, Snap.ShipVel, Snap.MaxAccel, Snap.MaxSpeed);
            UpdateWater(engaged);
            Project();
        }

        // a new destination (not one of our own points)
        if (Tracker.TargetName != lastTarget)
        {
            lastTarget = Tracker.TargetName;
            if (lastTarget != null) { promptUntil = now + 8; reframe = true; }
            panelDismissed = false;
            Snap.Track.Clear(); // a new route: start the flown track again
        }
        if (Path == null) { Snap.Track.Clear(); Snap.Prediction.Clear(); Snap.PredictionPhases.Clear(); }
        else if (Snap.HasShip) Snap.RecordTrack(Snap.ShipPos, now, Path.Distance);
        CheckArrived(engaged);
        ToolbarActions.ReleaseBurn(Path != null, Snap.InCockpit, engaged); // a character on foot counts as a "ship" in the snapshot: the seat is what matters
        // the points changed: update F&B's list, and the autopilot (new points while it flies, or on request)
        if (flightEdit != null && !confirming && (!routePanel || !engaged))
        {
            if (!engaged) { flightEdit = null; flightLimits = null; } // the flight ended meanwhile: keep them as a plan
            else if (!DraftChanged()) DiscardFlightEdit(); // the list or map closed with nothing changed
            else ConfirmSend(true); // the list or map closed: send the changes, or drop them?
        }
        if (Tracker.Revision != publishedRevision && flightEdit == null)
        {
            publishedRevision = Tracker.Revision;
            FlipAndBurnLink.SetDetours(Tracker.Waypoints);
            if (Path != null && engaged && Tracker.TargetName == flyingTarget) Send("update", quiet: true);
            else if (Path != null && !engaged && AutopilotLink.Status != null && Plugin.Settings.AutopilotAutoSend) Send("load", quiet: true); // (not before the PB has said it is idle)
        }
        AsteroidWatch.Update(now, Snap, Tracker, engaged);
        GravityWatch.Update(now, Snap, Path, engaged);
        AutopilotPanel.SteerKey = Binds.Steer.IsNone ? "" : Binds.Steer.Display;
        if (engaged && AsteroidWatch.Changed && Path != null && now >= dodgeNoteAt)
        {
            if (!Binds.Steer.IsNone) Notify("Asteroid on the route: " + Binds.Steer.Display + " steers round it.", false);
            dodgeNoteAt = now + 15;
        }
        WatchState();
        // reached a detour: say what comes next
        if (Tracker.Next != lastNext)
        {
            if (lastNext >= 0 && Tracker.Next > lastNext && Path != null) promptUntil = now + 10;
            lastNext = Tracker.Next;
        }
        if (Path == null) { routePanel = false; adding = false; }
        if (routePin && Tracker.TargetName == null) { routePin = false; if (!Plugin.Settings.Pip && !navOpen && !MapScreen.IsOpen) Visible = false; }
        if (resumeTarget != null && Tracker.TargetName != resumeTarget) resumeTarget = null; // picked somewhere else
        if (manualTarget != null && (engaged || Tracker.TargetName != manualTarget)) manualTarget = null; // engaged, arrived, or another destination
        if (engaged && flightEdit == null && flyingTarget != null && Tracker.TargetName == flyingTarget) SteerSelection();
        else if (ManualGuiding) SteerSelection();
        else steeredTo = null;

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
        if (now >= noRichHudNote) { Notify("Free mouse needs the Rich HUD Master mod (not in this world).", false); noRichHudNote = now + 10; }
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
        else switch (GamePad.Pressed())
        {
            case GamePad.Shortcut.FullMap: autoShown = false; promptUntil = 0; MapScreen.Open(); break;
            // again with the list up: close it (as N does)
            case GamePad.Shortcut.Autopilot: if (navOpen) { closingSelf = true; FlipAndBurnLink.CloseNavMenu(); } else AutopilotKey(); break;
        }
    }

    /// <summary>
    /// Esc (or the controller's B) on the game screen: closes what we have up (the N list with its Route Plan, else the map window unless it is
    /// pinned) and returns true, so the game's pause menu stays shut. False with nothing to close: Esc is the game's.
    /// </summary>
    public static bool EscapeClose()
    {
        if (!WorldGate.Active || (PlayGuard.Blocked && !PlayGuard.ViewOnly)) return false;
        if (!(MyScreenManager.GetScreenWithFocus() is MyGuiScreenGamePlay) || (MyAPIGateway.Gui?.ChatEntryVisible ?? false)) return false;
        if (navOpen || FlipAndBurnLink.IsNavMenuOpen())
        {
            closingSelf = true; FlipAndBurnLink.CloseNavMenu();
            if (!mapPanel) routePanel = false;
            adding = false; panelGrace = 0;
            if (autoShown && !Pinned) { Visible = false; autoShown = false; }
            return true;
        }
        if (Visible && !Pinned) { Visible = false; autoShown = false; promptUntil = 0; return true; }
        return false;
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
        Add(Binds.Map, "map, full map, off"); Add(Binds.Autopilot, "route plan"); Add(Binds.Pin, "pin"); Add(Binds.Cursor, "(hold) free mouse");
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
            if (Path == null) { Notify("Autopilot route not on the map yet: try again in a moment.", false); return; }
            if (!navOpen) FlipAndBurnLink.OpenNavMenu();
            panelGrace = Plugin.Now + 1.5;
            OpenFlightEdit(false);
            return;
        }
        // no route yet: the list to pick from, with the Route Plan panel and the map preview beside it
        if (!Visible) { Visible = true; autoShown = true; reframe = true; }
        panelDismissed = false;
        if (Path == null) { if (!navOpen) FlipAndBurnLink.OpenNavMenu(); return; }
        if (AutopilotLink.Count == 0) Notify(NoPb, false); // the plan still opens: FLY MANUAL needs no PB
        if (!navOpen) FlipAndBurnLink.OpenNavMenu();
        routePanel = true; adding = false; panelGrace = Plugin.Now + 1.5;
    }

    private const string NoPb = "No autopilot PB on this ship: load the Protogen Systems - Autopilot script into a programmable block.";

    /// <summary>The route panel while flying: edits are held (not sent) until APPLY; CANCEL goes back to these.</summary>
    private static void OpenFlightEdit(bool inMap)
    {
        if (flightEdit == null) StartDraft();
        routePanel = true; mapPanel = inMap; adding = false;
    }

    /// <summary>An edit of the flown route opens: the route the autopilot has is kept (and flown) until SEND ROUTE.</summary>
    private static void StartDraft()
    {
        flightEdit = Tracker.Save(); flightLimits = Limits.Clone();
        flownPoints = Path?.Points();
        draftChanges.Clear(); draftUndo.Clear();
    }

    /// <summary>The open edit differs from what the autopilot flies.</summary>
    private static bool DraftChanged() => flightEdit != null && (Tracker.Revision != publishedRevision || draftChanges.Count > 0
        || flightLimits != null && !flightLimits.SameAs(Limits));

    /// <summary>
    /// The one pop-up that sends an edit of the flown route: APPLY (or the list / map closing with changes) asks, with
    /// the changes listed. SEND ROUTE sends them and the autopilot flies on from here; otherwise it flies on as before.
    /// </summary>
    private static void ConfirmSend(bool closing)
    {
        if (asking) return;
        asking = true; confirming = true;
        var sb = new System.Text.StringBuilder();
        int shown = 0;
        foreach (var c in draftChanges) { if (shown++ == 6) { sb.Append("\n- and " + (draftChanges.Count - 6) + " more"); break; } sb.Append("\n- ").Append(c); }
        if (draftChanges.Count == 0) sb.Append("\n- the limits");
        string to = MapScene.ShortName(Tracker.TargetName ?? "the destination");
        Ask("Send the changed route?", "Your changes:" + sb + "\n\nThe autopilot is still flying the old route. SEND ROUTE: fly the new one (to " + to
            + ") from here. " + (closing ? "DROP CHANGES: keep the old route." : "KEEP EDITING: back to the editor."),
            "SEND ROUTE", closing ? "DROP CHANGES" : "KEEP EDITING", yes =>
            {
                asking = false; confirming = false;
                if (flightEdit == null) return;
                if (yes) ApplyFlightEdit();
                else if (closing) DiscardFlightEdit();
            });
    }

    /// <summary>APPLY while flying: the changed route and limits go to the autopilot, which flies on.</summary>
    private static void ApplyFlightEdit()
    {
        flightEdit = null; flightLimits = null; draftChanges.Clear(); draftUndo.Clear();
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
        bool changed = DraftChanged();
        flightEdit = null; flightLimits = null;
        var undo = new List<Action>(draftUndo); draftChanges.Clear(); draftUndo.Clear();
        if (st == null) return;
        for (int i = undo.Count - 1; i >= 0; i--) undo[i]();
        Tracker.Load(st);
        if (lim != null) Plugin.Settings.Limits = lim;
        publishedRevision = Tracker.Revision;
        FlipAndBurnLink.SetDetours(Tracker.Waypoints);
        steeredTo = null; nextRoute = 0;
        if (changed) Notify("Changes dropped: autopilot unchanged.");
    }

    /// <summary>The "System Map: stop autopilot" hotbar action: stops the flight at once (you have the controls).</summary>
    public static void HotbarStop()
    {
        if (PlayGuard.Blocked) { Notify(Paused, false); return; }
        if (!AutopilotLink.Engaged(Plugin.Now)) { Notify("The autopilot isn't flying.", false); return; }
        if (flightEdit != null) DiscardFlightEdit();
        routePanel = adding = mapPanel = false;
        StopAutopilot();
    }

    /// <summary>
    /// FULL STOP: the PB turns retrograde and burns the ship to a standstill, then switches the dampeners on and hands
    /// back. The route is kept (RESUME ROUTE). Works while it flies a route, or to stop a drifting ship.
    /// </summary>
    public static void FullStop()
    {
        if (PlayGuard.Blocked) { Notify(Paused, false); return; }
        if (AutopilotLink.Count == 0) { Notify(NoPb, false); return; }
        if (AutopilotLink.Braking(Plugin.Now)) { Notify("Already braking to a stop.", false); return; }
        if (flightEdit != null) DiscardFlightEdit();
        routePanel = adding = mapPanel = false;
        string was = flyingTarget;
        stoppingSelf = true;
        int n = AutopilotLink.FullStop(out var error);
        if (n <= 0) { stoppingSelf = false; Notify(error, false); return; }
        if (was != null) resumeTarget = was;
        flyingTarget = null; fullStopWarned = false;
        Notify("Full stop: braking retrograde. Dampeners take over under 50 m/s.");
    }
    private static bool fullStopWarned;

    private static bool panelDismissed; // CANCEL on the Route Plan beside the N list: not brought up again by itself

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
        else Notify(Tracker.UserPoints.Count >= 8 ? "8 waypoints at most" : "Already in the route: " + name, false);
        return true;
    }

    /// <summary>MANUAL: close the list and fly the route with Flip and Burn.</summary>
    private static void Manual()
    {
        routePanel = adding = mapPanel = false;
        if (navOpen) { closingSelf = true; FlipAndBurnLink.CloseNavMenu(); }
        var p = Path;
        if (p == null) return;
        GuideManual(p.TargetName);
        Notify(p.DetoursAhead > 0 ? "Manual: Flip and Burn points at \"" + Tracker.NextPick + "\", then each point in turn." : "Manual: fly to " + p.TargetName + " with Flip and Burn.");
    }

    public static void Engage() { if (DrivesReady(Engage) && FuelReady(Engage)) EngageNow(); }

    private static bool fuelOk; // ENGAGE ANYWAY was picked: the next start doesn't ask again

    /// <summary>
    /// Before a route starts: if flying it would leave less than 10 % water in the tanks (or run them dry), a pop-up
    /// says so and asks (<paramref name="then"/> runs again on ENGAGE ANYWAY). True when there's enough, or no tanks
    /// or drives to read.
    /// </summary>
    /// <summary>
    /// The route's water at the thrust and brake caps it's flown with (the route panel's limits; while flying, the ones
    /// sent), whether or not the map is drawing the route at those caps.
    /// </summary>
    private static void UpdateWater(bool engaged) => Snap.WaterUse = RouteWater(engaged, Snap.WaterRate, out Snap.WaterThrust);

    private static double RouteWater(bool engaged, double rate, out double thrust)
    {
        bool flown = engaged && flyingTarget != null;
        thrust = flown ? flyingThrust : Limits.ThrustFraction(Snap.DriveWeight, Snap.Signature?.Gain ?? 4);
        double brake = flown ? flyingBrake : Limits.BrakeFraction(Snap.DriveWeight, Snap.Signature?.Gain ?? 4);
        if (!(rate > 0)) return double.NaN;
        var p = Tracker.PathWith(thrust, brake, Limits.MaxSpeed, Snap.ShipVel, Snap.MaxAccel, Snap.MaxSpeed);
        return p == null ? double.NaN : RouteFuel.Litres(p, Snap.MaxAccel, rate);
    }

    private static bool FuelReady(Action then)
    {
        if (fuelOk) { fuelOk = false; return true; }
        double stored, cap, rate;
        try { DriveCheck.Water(DriveCheck.Ship, out stored, out cap, out rate); }
        catch (Exception e) { Plugin.LogThrottled("can't read the ship's water: " + e.Message); return true; }
        double used = RouteWater(false, rate, out _);
        if (double.IsNaN(used)) used = RouteFuel.Litres(Path, Snap.MaxAccel, rate);
        if (!RouteFuel.Low(stored, cap, used)) return true;
        if (asking) { Notify("Answer the pop-up first.", false); return false; }
        asking = true;
        double left = RouteFuel.LeftAfter(stored, cap, used);
        string end = left <= 0 ? "runs the tanks dry " + RouteFuel.Volume(used - stored) + " short"
            : "leaves " + Fmt.Num(left * 100, "0.0") + " % (" + RouteFuel.Volume(stored - used) + ") in the tanks";
        Ask("Low water", "This route burns about " + RouteFuel.Volume(used) + " of water (you have " + RouteFuel.Volume(stored) + ", " + Fmt.Num(stored / cap * 100, "0") + " %) and "
            + end + ", below " + Fmt.Num(RouteFuel.WarnBelow * 100, "0") + " %. Engage anyway?",
            "ENGAGE ANYWAY", "CANCEL", yes =>
            {
                asking = false;
                if (!yes) return;
                fuelOk = true;
                then();
                fuelOk = false;
            });
        return false;
    }

    private static readonly List<Sandbox.Game.Entities.MyThrust> drivesOff = new List<Sandbox.Game.Entities.MyThrust>();

    /// <summary>
    /// Before a route starts: if fusion drives are switched off, a pop-up offers to turn them on and go on
    /// (<paramref name="then"/> runs again after). True when they're all on (or there's no ship to check).
    /// </summary>
    private static bool DrivesReady(Action then)
    {
        DriveCheck.Read(DriveCheck.Ship, drivesOff, out int total);
        if (drivesOff.Count == 0) return true;
        if (asking) { Notify("Answer the pop-up first.", false); return false; }
        asking = true;
        string n = drivesOff.Count == total ? (total == 1 ? "The fusion drive is" : "All " + total + " fusion drives are") : drivesOff.Count + " of " + total + " fusion drives are";
        Ask("Fusion drives off", n + " switched off, so the autopilot can't push the ship" + (drivesOff.Count < total ? " at full thrust" : "") + ". Turn them on and start the route?",
            "TURN ON & ENGAGE", "CANCEL", yes =>
            {
                asking = false;
                if (!yes) return;
                int on = DriveCheck.TurnOn(drivesOff);
                Notify(on == 1 ? "Fusion drive switched on" : on + " fusion drives switched on", false);
                DriveCheck.Read(DriveCheck.Ship, drivesOff, out _);
                if (drivesOff.Count == 0) then();
                else Notify("Some fusion drives are still off: switch them on in the terminal.", false);
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
        flyingTarget = Path.TargetName; manualTarget = null;
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
        if (n > 0) { resumeTarget = was; GuideManual(was); }
        Notify(n > 0 ? "Autopilot stopped. You have control." + (was != null ? " RESUME ROUTE is under the N list." : "") : error, n > 0);
    }

    /// <summary>
    /// FLY MANUAL while the autopilot flies: it lets go at once (no pop-up, you picked it) and the route stays, flown
    /// by hand with Flip and Burn's pick and the burn guide. RESUME ROUTE hands it back.
    /// </summary>
    public static void FlyManual()
    {
        if (PlayGuard.Blocked) { Notify(Paused, false); return; }
        if (flightEdit != null) DiscardFlightEdit();
        routePanel = adding = mapPanel = false;
        if (navOpen) { closingSelf = true; FlipAndBurnLink.CloseNavMenu(); }
        string was = flyingTarget ?? Path?.TargetName;
        stoppingSelf = true;
        int n = AutopilotLink.Stop(out var error);
        flyingTarget = null;
        if (n <= 0) { stoppingSelf = false; Notify(error, false); return; }
        resumeTarget = was;
        if (was != null) GuideManual(was);
        Notify("Manual: you have control." + (was != null ? " Route to " + MapScene.ShortName(was) + " kept: follow the burn guide, or RESUME ROUTE." : ""));
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
        if (!DrivesReady(Resume) || !FuelReady(Resume)) return;
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
        // a click on the second screen asks there (you're looking at it, and the game window may be behind)
        if (SecondScreen.Ask(caption, text, yes, no, done)) { Plugin.Log("asking on the second screen: " + caption); return; }
        if (!InGame && !MapScreen.HasFocus) { pendingAsk = () => Ask(caption, text, yes, no, done); return; }
        Plugin.Log("asking: " + caption);
        // F&B's list (Rich HUD) still takes clicks under the game's pop-up, so a click on a button would pick in the
        // list too: it closes while asking and comes back after, unless the answer started the autopilot
        bool hid = navOpen;
        if (hid) { listHidden = true; closingSelf = true; FlipAndBurnLink.CloseNavMenu(); }
        var token = new object();
        askToken = token;
        void Answer(bool y)
        {
            listHidden = false;
            if (askToken != token) return; // dropped: answered another way (the PB's hotbar)
            askToken = null;
            done(y);
            if (hid && (!y || !AutopilotLink.Engaged(Plugin.Now)) && !MapScreen.IsOpen && !FlipAndBurnLink.IsNavMenuOpen())
            { FlipAndBurnLink.OpenNavMenu(); panelGrace = Plugin.Now + 1.5; }
        }
        try
        {
            askBox = MyGuiSandbox.CreateMessageBox(MyMessageBoxStyleEnum.Info, MyMessageBoxButtonsType.YES_NO,
                new System.Text.StringBuilder(text), new System.Text.StringBuilder(caption),
                yesButtonText: VRage.Utils.MyStringId.GetOrCompute(yes), noButtonText: VRage.Utils.MyStringId.GetOrCompute(no),
                callback: r => Answer(r == MyGuiScreenMessageBox.ResultEnum.YES), focusedResult: MyGuiScreenMessageBox.ResultEnum.NO);
            MyGuiSandbox.AddScreen(askBox);
        }
        catch (Exception e) { Plugin.LogThrottled("pop-up failed: " + e.Message); Answer(false); }
    }

    private static object askToken;
    private static MyGuiScreenMessageBox askBox;

    /// <summary>Takes down the open pop-up (or one waiting to ask) without its answer: settled another way.</summary>
    private static void DropAsk()
    {
        pendingAsk = null;
        askToken = null;
        try { if (askBox != null && askBox.State != MyGuiScreenState.CLOSED) askBox.CloseScreen(); }
        catch (Exception e) { Plugin.LogThrottled("closing the pop-up failed: " + e.Message); }
        askBox = null;
        if (listHidden && !MapScreen.IsOpen && !FlipAndBurnLink.IsNavMenuOpen()) { FlipAndBurnLink.OpenNavMenu(); panelGrace = Plugin.Now + 1.5; }
        listHidden = false;
    }

    /// <summary>While the autopilot flies: Flip and Burn's destination is the point it's flying to now (each detour and
    /// waypoint in turn, then the destination), so F&amp;B's HUD marker and figures follow the route.</summary>
    /// <summary>
    /// The autopilot flies a route the plugin doesn't know (the world was loaded, or you changed sector): show it on the
    /// maps again from the PB's copy, as if it had been sent this session. Nothing is sent back to the PB.
    /// </summary>
    private static double nextRestore;
    private static void RestoreRoute(double now, string why = "asked")
    {
        var r = AutopilotLink.Route; var st = AutopilotLink.Status;
        bool lost = Tracker.TargetName == null; // the map has no route at all: take it back even if it went from here
        if (r == null || st == null || !st.Flying || r.Id == restoredId && !lost || !Snap.HasShip) return;
        nextRestore = now + 2; // (a PB copy that can't be taken back isn't tried every frame)
        restoredId = r.Id;
        if (!Tracker.Restore(r, st.Wp, Snap.ShipPos, Snap.FnbTargetName, Snap.FnbTarget))
        { Plugin.Log("autopilot route not taken back: the PB holds no destination (" + r.Command + ")"); return; }
        flyingTarget = r.Target;
        flyingThrust = Math.Max(0.01, Math.Min(1, r.Thrust));
        flyingBrake = r.Brake > 0 ? Math.Max(0.01, Math.Min(1, r.Brake)) : flyingThrust;
        resumeTarget = null; steeredTo = null;
        publishedRevision = Tracker.Revision; // the PB has it already
        FlipAndBurnLink.SetDetours(Tracker.Waypoints);
        Plugin.Log("route taken back from the autopilot PB: " + r.Target + " (" + Tracker.Waypoints.Count + " points, next " + Tracker.Next + "; " + why + ")");
        Notify("Autopilot route restored: flying to " + r.Target + ".");
    }

    /// <summary>
    /// You fly the route to <paramref name="target"/> by hand (MANUAL, or you took over from the autopilot): F&amp;B's
    /// pick follows the route and the MANUAL line shows on the HUD. Nothing if the plan isn't that route any more.
    /// </summary>
    private static void GuideManual(string target)
    {
        if (target == null || Tracker.TargetName != target) return;
        manualTarget = target; steeredTo = null;
        Plugin.Log("manual guidance on: " + target + " (" + Tracker.Waypoints.Count + " points, next " + Tracker.Next + ")");
    }

    /// <summary>Flying the route by hand after MANUAL: F&amp;B's pick moves to each route point as you reach the one before.</summary>
    private static bool ManualGuiding => manualTarget != null && Tracker.TargetName == manualTarget && !adding && flightEdit == null;

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
        else if (manualTarget != null) Plugin.Log("manual guidance: picked " + name + " (point " + (next + 1) + " of " + (Tracker.Waypoints.Count + 1) + ")");
    }

    /// <summary>
    /// A change to the route while the autopilot flies it (from the map, the preview or the editor): made on an edit
    /// copy, which opens the route editor if it isn't open. The autopilot flies on the route as it was (drawn dashed
    /// under the copy) until APPLY, where one pop-up lists the changes and SEND ROUTE sends them; CANCEL drops them all.
    /// changed: whether the edit did anything (default: the plan's revision moved); undo: puts back anything outside the
    /// plan (Flip and Burn's point) if the changes are dropped.
    /// </summary>
    private static string FlightChange(string what, Func<string> edit, Func<bool> changed = null, Action undo = null)
    {
        if (!AutopilotLink.Engaged(Plugin.Now)) return edit();
        if (confirming) return "Answer the pop-up first.";
        bool fresh = flightEdit == null;
        if (fresh) StartDraft();
        int rev = Tracker.Revision;
        string msg = edit();
        if (!(changed?.Invoke() ?? Tracker.Revision != rev))
        {
            if (fresh) { flightEdit = null; flightLimits = null; draftChanges.Clear(); }
            return msg;
        }
        draftChanges.Add(what.TrimEnd('.'));
        if (undo != null) draftUndo.Add(undo);
        nextRoute = 0; steeredTo = null;
        if (!routePanel) OpenFlightEdit(MapScreen.IsOpen);
        return (msg ?? what) + (fresh ? "  (not sent: APPLY or CANCEL)" : "");
    }

    /// <summary>Shift+right-click on one of your waypoints in the full map: removes it. Null when it isn't one.</summary>
    public static string RemoveWaypoint(string name, V3 pos) => FlightChange("Remove " + name + " from the route.", () => RemoveWaypointNow(name, pos));
    private static string RemoveWaypointNow(string name, V3 pos)
    {
        int i = Tracker.UserPoints.FindIndex(u => u.Name == name);
        if (i < 0) i = Tracker.UserPoints.FindIndex(u => V3.Distance(u.Pos, pos) < 100);
        if (i < 0) return null;
        string n = Tracker.UserPoints[i].Name;
        Tracker.RemoveUser(i);
        nextRoute = 0;
        return "Waypoint removed: " + n + (Tracker.UserPoints.Count > 0 ? " (" + Tracker.UserPoints.Count + " left)" : "");
    }

    /// <summary>
    /// Shift+right-click on a detour in the map: the route goes straight past that world (every detour round it), also
    /// after it's planned again; R in the full map, or a new destination, brings the detours back. While flying it's an
    /// edit like any other (sent with APPLY → SEND ROUTE).
    /// </summary>
    public static string RemoveDetour(string name, V3 pos)
    {
        int i = Tracker.Waypoints.FindIndex(w => !w.User && w.Name == name);
        if (i < 0) i = Tracker.Waypoints.FindIndex(w => !w.User && V3.Distance(w.Pos, pos) < 100);
        if (i < 0) return "That detour is gone";
        string around = Tracker.Waypoints[i].Around?.Name;
        return FlightChange("Remove the detour round " + (around ?? MapScene.ShortName(name)) + ".", () =>
        {
            string w = Tracker.RemoveDetour(i);
            if (w == null) return "That isn't a detour";
            nextRoute = 0;
            return around != null ? "Detour removed: straight past " + w + " (R re-plans)" : "Detour removed: " + MapScene.ShortName(w);
        });
    }

    /// <summary>Where one of your waypoints is (the point you placed, not the stop short of a station). False when it isn't one.</summary>
    public static bool UserWaypoint(string name, out V3 pos)
    {
        int i = Tracker.UserPoints.FindIndex(u => u.Name == name);
        pos = i >= 0 ? Tracker.UserPoints[i].Pos : default;
        return i >= 0;
    }

    /// <summary>Dragging one of your waypoints up or down in the full map: moves it and plans the route again.</summary>
    public static string MoveWaypoint(string name, V3 pos, double rise, bool flat = false) => FlightChange("Move " + name + ".", () => MoveWaypointNow(name, pos, rise, flat));
    private static string MoveWaypointNow(string name, V3 pos, double rise, bool flat)
    {
        int i = Tracker.UserPoints.FindIndex(u => u.Name == name);
        if (i < 0) return "That waypoint is gone";
        var from = Tracker.UserPoints[i].Pos;
        if (!Tracker.MoveUser(i, pos)) return "Can't move " + name + " onto another point of the route";
        nextRoute = 0;
        if (flat) return name + " moved " + Fmt.Dist(V3.Distance(from, pos)) + " across";
        return name + " moved " + Fmt.Dist(Math.Abs(rise)) + (rise >= 0 ? " up" : " down");
    }

    /// <summary>
    /// Dragging a flip point (or a bend) up or down in the full map: the leg ending at <paramref name="leg"/> curves
    /// through the new point without stopping. Back near the straight line, it's straight again.
    /// </summary>
    public static string BendLeg(string leg, V3 pos) => FlightChange("Bend the leg to " + MapScene.ShortName(leg) + ".", () => BendLegNow(leg, pos));
    private static string BendLegNow(string leg, V3 pos)
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
        return "Leg bent " + Fmt.Dist(off) + " off the line (flown without stopping)";
    }

    /// <summary>Shift+right-click on a bend: the leg is straight again.</summary>
    public static string StraightenLeg(string leg) => FlightChange("Straighten the leg to " + MapScene.ShortName(leg) + ".", () => StraightenLegNow(leg));
    private static string StraightenLegNow(string leg)
    {
        if (leg == null || !Tracker.Bends.ContainsKey(leg)) return "That leg isn't bent";
        Tracker.Bend(leg, null);
        nextRoute = 0;
        return "Leg straightened";
    }

    /// <summary>Where a leg's flip point drag starts from: its bend, if it has one.</summary>
    public static bool LegBend(string leg, out V3 pos) => Tracker.Bends.TryGetValue(leg ?? "", out pos);

    /// <summary>Shift+right-click in the full map: a waypoint into the route where it fits (name null = "Waypoint n").</summary>
    public static string InsertWaypoint(string name, V3 pos) => FlightChange("Add " + (name ?? Tracker.NextUserName()) + " to the route.", () => InsertWaypointNow(name, pos));
    private static string InsertWaypointNow(string name, V3 pos)
    {
        if (Tracker.TargetName == null) return "Set a destination first (Shift+click)";
        if (Tracker.UserPoints.Count >= 8) return "8 waypoints at most";
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
        if (!FlipAndBurnLink.Found) return "Flip and Burn isn't loaded.";
        if (name == Tracker.TargetName && V3.Distance(pos, Tracker.Target) < 100) return name + " is the destination already.";
        bool picked = false;
        return FlightChange("Fly to " + MapScene.ShortName(name) + " instead of " + MapScene.ShortName(Tracker.TargetName ?? "the destination") + ".",
            () => { var m = SelectDestination(name, kind, pos, detail, out picked); if (picked) Tracker.Update(name, pos, Snap.HasShip, Snap.ShipPos, Snap.ShipVel, Snap.MaxAccel, Snap.MaxSpeed); return m; },
            () => picked);
    }

    private static string SelectDestination(string name, string kind, V3 pos, string detail, out bool ok)
    {
        // a station's F&B entry goes by its GPS name (JU.Ganymede.STN), shown as the pick's detail
        string alt = kind == "Trade station" ? detail
            : kind == "Ground facility" ? System.Array.Find(SystemData.Stations, s => s.Name == name)?.Gps : null;
        ok = FlipAndBurnLink.SelectPoint(name, new VRageMath.Vector3D(pos.X, pos.Y, pos.Z), out var error, alt);
        if (ok) ownTarget = kind == "Waypoint" ? name : null;
        return ok ? "Destination: " + name + ". Press N for AUTOPILOT or MANUAL." : error;
    }

    /// <summary>The destination is a point in space set from the map (so it can be dragged about).</summary>
    private static string ownTarget;
    public static bool OwnDestination(string name) => name != null && name == ownTarget && Tracker.TargetName == name;

    /// <summary>Dragging the destination you set in space: moves it (up / down, or across the plane when flat).</summary>
    public static string MoveDestination(string name, V3 from, V3 to, bool flat)
    {
        if (!OwnDestination(name)) return "Only a destination you placed can be moved";
        bool moved = false;
        return FlightChange("Move the destination " + name + ".", () =>
        {
            if (!FlipAndBurnLink.MovePoint(name, new VRageMath.Vector3D(to.X, to.Y, to.Z), out var error)) return error;
            moved = true; nextRoute = 0;
            Tracker.Update(name, to, Snap.HasShip, Snap.ShipPos, Snap.ShipVel, Snap.MaxAccel, Snap.MaxSpeed);
            double d = flat ? V3.Distance(from, to) : Math.Abs(to.Y - from.Y);
            return name + " moved " + Fmt.Dist(d) + (flat ? "" : to.Y >= from.Y ? " up" : " down");
        }, () => moved, () => { FlipAndBurnLink.MovePoint(name, new VRageMath.Vector3D(from.X, from.Y, from.Z), out _); ownTarget = name; });
    }

    /// <summary>Right-click on the destination in the full map: Flip and Burn has none now (your waypoints are kept).</summary>
    public static string ClearDestination() => ClearRoute();

    /// <summary>Sends the route (with any asteroid dodges and the thrust cap) to the autopilot PBs.</summary>
    public static bool Send(string command, bool quiet)
    {
        double thrust = command == "load" ? Limits.ThrustFraction(Snap.DriveWeight, Snap.Signature?.Gain ?? 4) : flyingThrust;
        double brake = command == "load" ? Limits.BrakeFraction(Snap.DriveWeight, Snap.Signature?.Gain ?? 4) : flyingBrake;
        int n = AutopilotLink.Send(Path, command, command == "load" ? null : AsteroidWatch.Dodges, thrust, brake, out var error);
        if (n > 0) AutopilotLink.SentRevision = Tracker.Revision;
        if (n > 0 && command != "load") AsteroidWatch.TakeChanged(); // the suggested way round went with it
        if (n <= 0 && !quiet) Notify(error, false);
        else if (n > 0 && command == "load" && !quiet) Notify("Route to " + Path.TargetName + " loaded into the autopilot.");
        return n > 0;
    }

    /// <summary>Says so when the PB stops flying by itself (arrived, you took over, or a problem).</summary>
    private static void WatchState()
    {
        var st = AutopilotLink.Status;
        string state = st?.State;
        // an older PB script doesn't know FULL STOP: it never answers the order
        if (!fullStopWarned && AutopilotLink.LastCommand == "brake" && AutopilotLink.Pending && Plugin.Now - AutopilotLink.SentAt > 6)
        {
            fullStopWarned = true; stoppingSelf = false;
            Notify("Autopilot script out of date (no FULL STOP): load the latest Protogen Systems - Autopilot script.", false);
        }
        if (state == lastState) return;
        if (lastState == "braking" && !AutopilotLink.Pending)
        {
            string more = resumeTarget != null ? " RESUME ROUTE flies the rest to " + resumeTarget + "." : "";
            switch (state)
            {
                case "stopped": Notify("Full stop: under 50 m/s, dampeners on. You have control." + more); break;
                case "override": Notify("Full stop cancelled: you took control." + more, false); break;
                case "error": Notify("Full stop failed: " + st.Msg, false); break;
            }
            stoppingSelf = false;
            lastState = state;
            return;
        }
        // resumed from the PB's own "go" (hotbar): the stopped route is flying again
        if (state == "flying" && flyingTarget == null && resumeTarget != null && !AutopilotLink.Pending)
        { flyingTarget = resumeTarget; resumeTarget = null; Notify("Autopilot resumed: flying to " + flyingTarget + "."); }
        // the PB's own "manual" (hotbar) while the route was paused: manual control, the route kept, no pop-up
        if (lastState == "manual" && state == "override" && resumeTarget != null && manualTarget != resumeTarget)
        {
            DropAsk();
            GuideManual(resumeTarget);
            Notify("Manual: you have control. Route to " + MapScene.ShortName(resumeTarget) + " kept: Flip and Burn points along it, RESUME ROUTE is under the N list.");
            lastState = state;
            return;
        }
        if (lastState == "flying" && !AutopilotLink.Pending)
        {
            string was = flyingTarget;
            switch (state)
            {
                case "arrived":
                    bool damp = DampenersOn();
                    Notify("Arrived" + (flyingTarget != null ? " at " + MapScene.ShortName(flyingTarget) : "") + (damp ? ", dampeners on" : "") + ". You have control.");
                    flyingTarget = null; DropRoute();
                    break;
                case "error": Notify("Autopilot stopped: " + st.Msg, false); if (was != null) resumeTarget = was; break;
                case "manual":
                    // you moved or turned: the PB has let go but holds the route until this is answered. Esc (B)
                    // answers CONFIRM MANUAL, so backing out never hands the ship back to the autopilot
                    resumeTarget = was ?? st.Target;
                    Ask("Manual control", (string.IsNullOrEmpty(st.Msg) ? "You took control" : st.Msg)
                        + ".\nAutopilot paused; route to " + resumeTarget
                        + " kept. Resume from here, or stay manual?",
                        "RESUME ROUTE", "STAY MANUAL", yes =>
                        {
                            if (yes) { Resume(); return; }
                            int n = AutopilotLink.ConfirmManual(out var err);
                            if (n > 0) GuideManual(resumeTarget);
                            Notify(n > 0 ? "Manual: you have control." + (resumeTarget != null ? " Flip and Burn points along the route. RESUME ROUTE is under the N list." : "") : err, n > 0);
                        });
                    break;
                case "override":
                case "stopped":
                    if (state == "stopped" && stoppingSelf) break; // our own STOP, already asked
                    if (was == null) { Notify("Autopilot off: you took control."); break; }
                    resumeTarget = was;
                    if (state == "override" && (st.Msg ?? "").IndexOf("hotbar", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        // the PB put in manual on purpose: keep the route and fly it by hand, no pop-up
                        GuideManual(was);
                        Notify("Manual: you have control. Route to " + MapScene.ShortName(was) + " kept: Flip and Burn points along it, RESUME ROUTE is under the N list.");
                        break;
                    }
                    Ask("Manual control", "Autopilot stopped: " + (string.IsNullOrEmpty(st.Msg) ? "you took control" : st.Msg)
                        + ".\nYou have control. Resume the route to " + was + " from here, or stay manual?",
                        "RESUME ROUTE", "STAY MANUAL", yes => { if (yes) Resume(); else GuideManual(was); });
                    break;
                case "braking": // FULL STOP (ours, or brake from the hotbar): the route is kept for later
                    if (was != null) resumeTarget = was;
                    if (!stoppingSelf) Notify("Autopilot: full stop, braking.");
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
    /// <summary>
    /// CLEAR ROUTE (the map card, Delete in the full map, the hotbar action, right-click on the destination): drops the
    /// route, your waypoints and Flip and Burn's pick. Not while the autopilot flies it (stop it first).
    /// </summary>
    public static string ClearRoute()
    {
        double now = Plugin.Now;
        if (Tracker.TargetName == null && Tracker.UserPoints.Count == 0) return "No route to clear";
        if (AutopilotLink.Braking(now)) return "Autopilot is braking: clear the route once stopped.";
        if (AutopilotLink.Engaged(now))
        {
            if (asking || confirming) return "Answer the pop-up first.";
            asking = true;
            Ask("Clear the route?", "The autopilot is flying this route. CLEAR ROUTE stops it and drops the route and your waypoints. You get control.",
                "CLEAR ROUTE", "KEEP FLYING", yes =>
                {
                    asking = false;
                    if (!yes) return;
                    string was = Tracker.TargetName;
                    StopAutopilot();
                    DropRoute();
                    Notify("Autopilot stopped and route cleared" + (was != null ? ": " + MapScene.ShortName(was) : "") + ".");
                });
            return null;
        }
        string name = Tracker.TargetName;
        DropRoute();
        return "Route cleared" + (name != null ? ": " + name : "");
    }

    /// <summary>The route is gone: cleared, or flown to the end.</summary>
    private static void DropRoute()
    {
        if (flightEdit != null) { flightEdit = null; flightLimits = null; }
        FlipAndBurnLink.ClearSelection(out _);
        Tracker.Cancel(Snap.FnbTargetName, Snap.FnbTarget);
        routePanel = adding = mapPanel = false;
        resumeTarget = null; steeredTo = null; ownTarget = null; awayFromTarget = false;
        nextRoute = 0;
    }

    /// <summary>The ship has been well away from the destination since the route was set (so being there now is arriving).</summary>
    private static bool awayFromTarget;

    /// <summary>Flown by hand: at the destination (inside the arrive radius, nearly stopped) after being away from it.</summary>
    private static void CheckArrived(bool engaged)
    {
        if (Path == null || !Snap.HasShip) { awayFromTarget = false; return; }
        // at where the route stops (above a world, short of a station), or on the point itself
        double d = Math.Min(V3.Distance(Snap.ShipPos, Tracker.Target), V3.Distance(Snap.ShipPos, RouteExport.StopPoint(Path, out _)));
        if (d > Tracker.ArriveRadius * 2) { awayFromTarget = true; return; }
        if (engaged || !awayFromTarget || Tracker.Next < Tracker.Waypoints.Count) return;
        if (d < Tracker.ArriveRadius && Snap.ShipVel.Length < 100)
        {
            string name = Tracker.TargetName;
            DropRoute();
            Notify("Arrived at " + name + ".");
        }
    }

    /// <summary>
    /// Switches the inertial dampeners of the ship you control on (the end of a route). True when they're on after.
    /// The PB does the same as it lets go; this covers the case where its write didn't take.
    /// </summary>
    private static bool DampenersOn()
    {
        try
        {
            if (!(Sandbox.Game.World.MySession.Static?.ControlledEntity is Sandbox.ModAPI.Ingame.IMyShipController c)) return false;
            if (!c.DampenersOverride) c.DampenersOverride = true;
            return c.DampenersOverride;
        }
        catch (Exception e) { Plugin.LogThrottled("dampeners on: " + e.Message); return false; }
    }

    public static void Notify(string text, bool ok = true)
    {
        if (string.IsNullOrEmpty(text)) return;
        try { MyAPIGateway.Utilities.ShowNotification(text, 4000, ok ? "White" : "Red"); } catch { }
        SecondScreen.Echo(text, ok); // a click there gets its answer there too (the HUD may be on another screen)
        if (!ok) Plugin.Log("notice: " + text);
    }

    /// <summary>Picks the route again from where the ship is now (new detours).</summary>
    /// <param name="detoursBack">Plan round the worlds whose detours you removed again (R in the full map).</param>
    public static void Replan(bool detoursBack = false)
    {
        if (detoursBack) Tracker.Skipped.Clear();
        Tracker.Avoid = Plugin.Settings.AvoidBodies;
        Tracker.AvoidFields = Plugin.Settings.AvoidFields;
        RouteExport.Offset = Plugin.Settings.OffsetKm * 1000;
        if (Snap.HasShip) Tracker.Replan(Snap.ShipPos);
        Snap.Track.Clear();
        nextRoute = 0;
    }

    private static readonly List<V3> ahead = new List<V3>();

    /// <summary>The ship's lift (<see cref="DetourPlanner.ShipAccel"/>) the route was last planned again for.</summary>
    private static double plannedLift, nextLiftReplan;

    private static bool GravityKnown()
    {
        foreach (var w in SystemData.Worlds) if (w.SurfaceG > 0) return true;
        return false;
    }

    /// <summary>Where the ship will go from here as it corrects onto the route, to the destination (drawn dashed).</summary>
    private static void Project()
    {
        var p = Path;
        if (p == null || !Snap.HasShip) { Snap.Prediction.Clear(); Snap.PredictionPhases.Clear(); Snap.PredictionArrives = true; return; }
        // a curved route: the point-to-point projection would overshoot a curve's many points, so instead the arc the
        // ship flies from its velocity now onto the curve, then the curve (nothing to correct: drawn as planned)
        if (p.Legs.Exists(l => l.Curved))
        {
            Trajectory.Join(Snap.ShipPos, Snap.ShipVel, p.Legs.Find(l => l.Curved), p.MaxAccel, p.MaxSpeed, Snap.Prediction, Snap.PredictionPhases);
            Snap.PredictionArrives = true;
            return;
        }
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
    /// <summary>The pinned map is in its corner on screen now.</summary>
    public static bool PipOnScreen => InPip && Visible;
    public static bool InGameScreen => InGame;
    /// <summary>The preview's window this frame when it gave its place to the destination view (null when it didn't).</summary>
    public static Box? GivenUp { get; private set; }
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
            : SideLayout.Box(full.Width, full.Height, s.PreviewSize, s.PreviewLeft, scene.Strip);
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
        return on ? "Map pinned (" + CornerName(Plugin.Settings.PipCorner) + ")." + (Binds.Pin.IsNone ? "" : " " + Binds.Pin.Display + " to unpin.")
                  : "Map unpinned.";
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
        if (!Visible || !cursorFree || MapScreen.IsOpen || OutOfCockpit || GivenUp != null) return false;
        Mouse(out float mx, out float my);
        var full = MyGuiManager.GetFullscreenRectangle();
        return WindowBox().Contains(mx, my) && !followBox.Contains(mx, my) && !SideLayout.FnbList(full.Width, full.Height).Contains(mx, my);
    }

    private static bool followPress; // the left button went down on FOLLOW SHIP

    private static void ToggleFollow()
    {
        bool f = Plugin.Settings.PreviewFollow = !Plugin.Settings.PreviewFollow;
        if (f && Snap.HasShip) scene.Cam.FlyTo(Snap.ShipPos, Math.Min(scene.Cam.GoalDistance, MapScene.ShipViewDistance));
        Plugin.SaveSettings();
    }

    // ---- the controller on the buttons under the N list ----
    // The N list (Rich HUD) has its own controller cursor (right stick, LB clicks). These buttons take the D-pad instead:
    // a D-pad push while they show gives them the controller, and from then the game screen's input is held (GamePlayInputPatch)
    // so A, the D-pad and the sticks don't fly the ship or use the toolbar. B closes the list and the Route Plan (as Esc does) and
    // the controller goes back to the ship. Keyboard or mouse use, or the buttons going away, gives it back too.
    private static bool padCaptured;
    private static double panelsDrawn = -1; // when DrawAll last drew buttons under the list

    /// <summary>The D-pad has the buttons under the N list (the game's input is held meanwhile).</summary>
    public static bool PadCaptured => padCaptured;

    /// <summary>
    /// From the game screen's input (each update): whether the buttons have the controller now, so the game screen
    /// skips its input. The D-pad push that takes them and the B that closes the list go no further either.
    /// </summary>
    public static bool PadInput()
    {
        bool up = navOpen && !MapScreen.IsOpen && !OutOfCockpit && !PlayGuard.Blocked && GamePad.Active;
        bool shown = up && Plugin.Now - panelsDrawn < 0.25 && ui.HasControls;
        // the map window beside the list: the full map's controller buttons, while the cursor is over it
        bool mapUsed = up && PadMap();
        if (!shown) { padCaptured = false; return mapUsed; }
        if (!padCaptured)
        {
            if (GamePad.Dpad() == PanelUi.PadDir.None) return mapUsed;
            padCaptured = true; ui.Focus = -1; // the first push lands on the first button
            return true;
        }
        // B closes the list and the Route Plan, as Esc does
        if (GamePad.BPressed || GamePad.BackPressed) { padCaptured = false; ui.Focus = -1; EscapeClose(); }
        return true;
    }

    private const double PadTurnRate = 1.8, PadZoomRate = 6, PadPanRate = 0.7; // as the full map's
    private static double lastPadMap;

    /// <summary>
    /// The controller on the map window while the N list is open and the cursor (Rich HUD's: right stick, LB clicks)
    /// is over the map: RT sets the destination at the cursor, LT adds / removes a waypoint, the left stick moves the
    /// map, R3 focuses, X / Y / L3 follow, frame and turn with ship; with RB + LB held the right stick turns and
    /// tilts, the left stick zooms, Y re-plans, X clears the route, R3 shows the whole system. True when something
    /// was used this update, so the game screen doesn't act on the same press or stick.
    /// </summary>
    private static bool PadMap()
    {
        double now = Plugin.Now, dt = Math.Min(0.1, now - lastPadMap);
        lastPadMap = now;
        if (!GamePad.ReadMap(out var p) || dt <= 0 || !Visible || !OverMap()) return false;
        var s = Plugin.Settings;
        bool used = false;
        bool x = GamePad.New(MyJoystickButtonsEnum.J03), y = GamePad.New(MyJoystickButtonsEnum.J04), r3 = GamePad.New(MyJoystickButtonsEnum.J10);
        if (p.Bumpers)
        {
            if (p.Rx != 0 || p.Ry != 0)
            {
                double yaw = -p.Rx * PadTurnRate * dt;
                scene.Cam.Orbit(yaw, p.Ry * PadTurnRate * dt);
                if (s.PreviewFollow && s.FollowHeading) previewHeading.Turned(yaw);
                used = true;
            }
            if (p.Ly != 0) { scene.Cam.Zoom(-p.Ly * PadZoomRate * dt); used = true; }
            if (y) { bool back = Tracker.Skipped.Count > 0; Replan(true); Notify("Route re-planned" + (back ? " (removed detours restored)." : ".")); }
            if (x) Notify(ClearRoute());
            if (r3) { s.PreviewFollow = false; scene.Cam.FlyTo(V3.Zero, 6e7); }
        }
        else
        {
            if (p.Lx != 0 || p.Ly != 0)
            {
                float k = (float)(MyGuiManager.GetFullscreenRectangle().Height * PadPanRate * dt);
                scene.Cam.Pan(-p.Lx * k, -p.Ly * k);
                if (s.PreviewFollow) { s.PreviewFollow = false; Notify("Follow ship: off (moved the map)"); }
                used = true;
            }
            Mouse(out float mx, out float my);
            if (p.RtNew) mouse.PadSelect(scene);
            if (p.LtNew) mouse.PadWaypoint(scene, mx, my);
            if (r3) mouse.PadFocus(scene);
            if (x) { ToggleFollow(); Notify(!s.PreviewFollow ? "Follow ship: off" : Snap.HasShip ? "Follow ship: on" : "Follow ship: on (sit in a cockpit)"); }
            if (y) { s.PreviewFollow = false; scene.FrameDefault(false); }
            if (GamePad.New(MyJoystickButtonsEnum.J09))
            {
                s.FollowHeading = !s.FollowHeading; previewHeading.Offset = 0; Plugin.SaveSettings();
                Notify(s.FollowHeading ? (s.PreviewFollow ? "Turn with ship: on (travel points up)" : "Turn with ship: on (when following: X)") : "Turn with ship: off");
                used = true;
            }
        }
        return used || x || y || r3 || p.RtNew || p.LtNew;
    }

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
            if (followPress && onFollow) ToggleFollow();
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
        // the controller was used last: its buttons instead
        if (GamePad.Active) return "VIEW|Right stick cursor (LB clicks)" + s + "Left stick move map" + s + "RB+LB + right stick turn / tilt" + s + "RB+LB + left stick zoom" + s + "R3 focus" + s + "X follow ship" + s + "L3 turn with ship: " + On(Plugin.Settings.FollowHeading) + s + "Y frame route" + s + "RB+LB + R3 whole system" + "\n"
            + "ROUTE|RT set destination" + s + "LT add / remove waypoint or detour" + s + "D-pad buttons" + s + "A press" + s + "RB+LB + Y re-plan" + s + "RB+LB + X clear route" + s + "B close" + "\n"
            + "SHOW|These work with the cursor over the map" + s + "Grid, orbits, labels, theme and the rest: keyboard" + "\n"
            + keys;
        return "VIEW|Drag orbit" + s + "Right-drag pan" + s + "Wheel zoom" + s + "Click focus" + s + "Middle-click re-frame" + s + "S follow ship" + s + "U turn with ship: " + On(Plugin.Settings.FollowHeading) + s + "F frame route" + s + "H whole system\n"
            + "ROUTE|Shift+click set destination" + s + "Shift+right-click add / remove waypoint or detour" + s + "Shift+right-drag add above / below" + s + "Drag waypoint: up / down (Y) or sideways (X, Z)" + s + "Shift+right-drag waypoint: across" + s + "Right-click destination or Del: clear route" + s + "Drag flip point: bend leg" + s + "Shift+right-click bend: straighten" + s + "A route plan" + s + "R re-plan" + s + "V detours: " + On(Plugin.Settings.AvoidBodies) + s + "C copy flip GPS\n"
            + "SHOW|G grid: " + On(o.Grid) + s + "O orbits: " + On(o.Orbits) + s + "X sectors: " + On(o.Sectors) + s + "T stations: " + On(o.Stations) + s + "P GPS: " + On(o.Gps) + s + "L labels: " + On(o.Labels) + s + "J moons: " + On(o.Moons) + s + "Y ship routes: " + On(o.Routes) + s + "E copy route share code" + s + "I import share code (Shift+I: clear shared)" + s + "K theme: " + MapTheme.Current.Name + s + "Keys work with the mouse over the map\n"
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
            Notify(!s.PreviewFollow ? "Follow ship: off" : Snap.HasShip ? "Follow ship: on" : "Follow ship: on (sit in a cockpit)");
        }
        else if (input.IsNewKeyPressed(MyKeys.U))
        {
            s.FollowHeading = !s.FollowHeading; previewHeading.Offset = 0;
            Notify(s.FollowHeading ? (s.PreviewFollow ? "Turn with ship: on (travel points up)" : "Turn with ship: on (when following: S)") : "Turn with ship: off");
        }
        else if (input.IsNewKeyPressed(MyKeys.C)) Notify(MapScreen.CopyFlip());
        else if (input.IsNewKeyPressed(MyKeys.Delete)) Notify(ClearRoute());
        else if (input.IsNewKeyPressed(MyKeys.A))
        {
            if (AutopilotLink.Engaged(Plugin.Now)) ConfirmStop();
            else if (Path == null) Notify("No route: pick a destination first.", false);
            else if (AutopilotLink.Count == 0) Notify(NoPb, false);
            else OpenRoutePanel();
        }
        else if (input.IsNewKeyPressed(MyKeys.R)) { bool back = Tracker.Skipped.Count > 0; Replan(true); Notify("Route re-planned" + (back ? " (removed detours restored)." : ".")); }
        else if (input.IsNewKeyPressed(MyKeys.V))
        {
            s.AvoidBodies = !s.AvoidBodies; Replan();
            Notify(s.AvoidBodies ? "Detours: on" : "Detours: off (straight line)");
        }
        else if (input.IsNewKeyPressed(MyKeys.G)) o.Grid = !o.Grid;
        else if (input.IsNewKeyPressed(MyKeys.O)) o.Orbits = !o.Orbits;
        else if (input.IsNewKeyPressed(MyKeys.X)) o.Sectors = !o.Sectors;
        else if (input.IsNewKeyPressed(MyKeys.T)) o.Stations = !o.Stations;
        else if (input.IsNewKeyPressed(MyKeys.P)) o.Gps = !o.Gps;
        else if (input.IsNewKeyPressed(MyKeys.L)) o.Labels = !o.Labels;
        else if (input.IsNewKeyPressed(MyKeys.J)) o.Moons = !o.Moons;
        else if (input.IsNewKeyPressed(MyKeys.Y)) { o.Routes = !o.Routes; Notify(o.Routes ? "Ship routes: on (where your other ships should be)" : "Ship routes: off"); }
        else if (input.IsNewKeyPressed(MyKeys.E)) { string m = FleetRoutes.CopyCode(out bool ok); Notify(m, ok); }
        else if (input.IsNewKeyPressed(MyKeys.I)) { bool ok = true; string m = input.IsAnyShiftKeyPressed() ? FleetRoutes.ClearShared() : FleetRoutes.ImportClipboard(out ok); Notify(m, ok); }
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
        GivenUp = null;
        if (MapScreen.IsOpen || OutOfCockpit || !InGame) return;
        if (PlayGuard.Blocked && !PlayGuard.ViewOnly) return; // AFK: everything is still shown, read-only (no keys, mouse, prompts or writes)
        panelBox = default;
        DrawAll(now, dt);
        if (!cursorFree || !(RichHudCursor.Shown || holding)) return;
        Mouse(out float cx, out float cy);
        if ((Visible && GivenUp == null && WindowBox().Contains(cx, cy)) || panelBox.Contains(cx, cy))
            RichHudCursor.Draw(cx, cy, MyGuiManager.GetFullscreenRectangle().Height);
    }

    private static Box panelBox; // the route panel this frame

    /// <summary>
    /// The burn guide's thrust for the route now (the "match route burn" drive action): the phase and the share of full
    /// thrust (%): the plan's share while burning, what stops on the point while braking (over 100 when it can't), 0
    /// while coasting. NaN with no route to fly by hand, the reason in <paramref name="why"/>.
    /// </summary>
    public static double RouteBurnPct(out BurnGuide.Phase phase, out string why)
    {
        phase = BurnGuide.Phase.None; why = null;
        var path = Path;
        if (path == null || path.Legs.Count == 0 || !Snap.HasShip) { why = "No route: pick a destination first."; return double.NaN; }
        if (AutopilotLink.Engaged(Plugin.Now)) { why = "The autopilot is flying the route"; return double.NaN; }
        if (Snap.MaxAccel <= 0.01) { why = "No drive thrust read yet"; return double.NaN; }
        var real = BurnGuide.HandLeg(path); // ends where the route stops, not on a world's centre
        var leg = BurnGuide.Padded(real, Snap.ShipPos, Snap.ShipVel);
        phase = BurnGuide.PhaseOf(leg);
        if (phase == BurnGuide.Phase.Coast) return 0;
        var toPoint = real.Target - Snap.ShipPos;
        double dist = toPoint.Length, closing = dist > 1 ? Snap.ShipVel.Dot(toPoint) / dist : 0;
        double pct = BurnGuide.ThrustPct(phase, leg.MaxAccel, Snap.MaxAccel, dist, closing, real.EndSpeed);
        if (double.IsNaN(pct)) { why = "Nothing to burn for: the ship isn't closing on the point"; return double.NaN; }
        return pct;
    }

    /// <summary>
    /// Flying by hand: your nose's marker and where to point it to burn onto the route's line (or brake along it, or
    /// correct a drift while coasting), projected through the game's camera.
    /// </summary>
    private static bool DrawBurnGuide(RoutePath path, float sw, float sh, float px)
    {
        var cam = Sandbox.Game.World.MySector.MainCamera;
        if (cam == null || path.Legs.Count == 0) return false;
        var real = BurnGuide.HandLeg(path); // ends where the route stops, not on a world's centre
        var leg = BurnGuide.Padded(real, Snap.ShipPos, Snap.ShipVel); // with a pilot's slack
        var phase = BurnGuide.PhaseOf(leg);
        int n = path.Next;
        var from = n > 0 && n - 1 < path.Waypoints.Count ? path.Waypoints[n - 1].Pos : Tracker.PlanFrom;
        var to = n < path.Waypoints.Count ? path.Waypoints[n].Pos : path.Target;
        double accel = phase == BurnGuide.Phase.Brake && leg.BrakeAccel > 0 ? leg.BrakeAccel : leg.MaxAccel;
        var aim = BurnGuide.Aim(from, to, Snap.ShipPos, Snap.ShipVel, accel, phase);
        if (aim.LengthSq < 0.5) return false;
        var proj = cam.ProjectionMatrix;
        float margin = px * 2.5f, foot = sh * 0.21f; // edge marks stay above the game's toolbar and gauges
        BurnGuide.Project(ToCam(cam, Snap.ShipFwd), proj.M11, proj.M22, sw, sh, margin, out float nx, out float ny, out bool noseOn);
        BurnGuide.Project(ToCam(cam, aim), proj.M11, proj.M22, sw, sh, margin, foot, out float ax, out float ay, out bool aimOn);
        string after = n < path.Waypoints.Count ? (n + 1 < path.Waypoints.Count ? path.Waypoints[n + 1].Name : path.TargetName) : null;
        // how hard to thrust, and where the ship stops against the point at the slowing it has now
        var toPoint = real.Target - Snap.ShipPos;
        double dist = toPoint.Length, closing = dist > 1 ? Snap.ShipVel.Dot(toPoint) / dist : 0;
        double decel = Decel(closing);
        double pct = BurnGuide.ThrustPct(phase, leg.MaxAccel, Snap.MaxAccel, dist, closing, real.EndSpeed);
        double err = BurnGuide.StopError(phase, dist, closing, real.EndSpeed, decel, Snap.MaxAccel);
        string stop = BurnGuide.StopText(phase, err, dist);
        bool bad = !double.IsNaN(err) && (err > 0 || phase != BurnGuide.Phase.Brake) && stop != null && !stop.StartsWith("stops");
        // where the nose goes at the next change (the flip, or the turn onto the next leg)
        V3? afterPos = n < path.Waypoints.Count ? (n + 1 < path.Waypoints.Count ? path.Waypoints[n + 1].Pos : path.Target) : (V3?)null;
        var nextAim = BurnGuide.NextAim(leg, phase, from, to, afterPos, out string nextLabel);
        if (nextLabel != null && nextAim.LengthSq > 0.5)
        {
            double inT = !leg.HasBurnPlan ? double.NaN : nextLabel == "TURN" ? leg.Eta
                : phase == BurnGuide.Phase.Coast ? leg.CoastTime : leg.BurnTime + leg.CoastTime;
            BurnGuide.Project(ToCam(cam, nextAim), proj.M11, proj.M22, sw, sh, margin, foot, out float qx, out float qy, out bool qOn);
            BurnGuide.DrawNext(canvas, px, qx, qy, qOn, nx, ny, nextLabel, double.IsNaN(inT) || leg.TooFastToStop ? null : Fmt.Time(inT));
        }
        BurnGuide.Draw(canvas, px, nx, ny, noseOn, ax, ay, aimOn, phase, BurnGuide.Angle(Snap.ShipFwd, aim), BurnGuide.Next(leg, phase, after), pct, stop, bad);
        return noseOn;
    }

    private static double lastClosing = double.NaN, lastClosingAt, decelSmooth;

    /// <summary>How hard the ship slows toward the point now (m/s², + slowing), smoothed over about a second.</summary>
    private static double Decel(double closing)
    {
        double now = Plugin.Now, dt = now - lastClosingAt;
        if (double.IsNaN(lastClosing) || dt > 2 || dt < 0) { lastClosing = closing; lastClosingAt = now; decelSmooth = 0; return 0; }
        if (dt < 0.2) return decelSmooth;
        double d = (lastClosing - closing) / dt;
        decelSmooth += (d - decelSmooth) * Math.Min(1, dt / 1.0);
        lastClosing = closing; lastClosingAt = now;
        return decelSmooth;
    }

    /// <summary>A world direction in the camera's frame (x right, y up, looking down -z).</summary>
    private static V3 ToCam(VRage.Game.Utils.MyCamera cam, V3 d)
    {
        var v = VRageMath.Vector3D.TransformNormal(new VRageMath.Vector3D(d.X, d.Y, d.Z), cam.ViewMatrix);
        return new V3(v.X, v.Y, v.Z);
    }

    /// <summary>The nose and aim markers show on the plain game screen (and under this plugin's settings window, to
    /// see a change), not over the terminal, the full map or another menu.</summary>
    private static bool MarkersShown => !MapScreen.IsOpen && (InGame || SettingsScreen.IsOpen);

    /// <summary>The game's crosshair is hidden for the nose marker (the setting, in a seat).</summary>
    public static bool CrosshairHidden => Plugin.Settings.HideCrosshair && Snap.InCockpit && Snap.HasShip && !(PlayGuard.Blocked && !PlayGuard.ViewOnly) && WorldGate.Active;

    /// <summary>The nose marker on its own, where the crosshair was hidden (no route flown by hand).</summary>
    private static void DrawNose(float sw, float sh, float px)
    {
        var cam = Sandbox.Game.World.MySector.MainCamera;
        if (cam == null) return;
        var proj = cam.ProjectionMatrix;
        BurnGuide.Project(ToCam(cam, Snap.ShipFwd), proj.M11, proj.M22, sw, sh, px * 2.5f, out float nx, out float ny, out bool on);
        if (on) BurnGuide.Nose(canvas, px, nx, ny);
    }

    /// <summary>The plugin's own boxes on the flight HUD this frame, which other mods' marker labels are moved off
    /// (<see cref="ModLabels"/>); <see cref="KeepClearAt"/> is when they were set.</summary>
    public static readonly List<Box> KeepClear = new List<Box>();
    public static double KeepClearAt;

    private static void DrawAll(double now, double dt)
    {
        KeepClear.Clear(); KeepClearAt = now;
        if (DestinationView.Shown.W > 0) KeepClear.Add(DestinationView.Shown);
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
        var padMove = GamePad.Take();
        ui.PadActive = padCaptured;
        ui.Pad(padCaptured ? padMove : PanelUi.PadDir.None, padCaptured && GamePad.ADown);
        if (navOpen) panelsDrawn = now;

        // HUD: autopilot status and asteroid warning
        var hs = Plugin.Settings;
        bool noseDrawn = false;
        float hudY = sh * 0.05f;
        if (engaged || AutopilotLink.Braking(now))
        {
            var line = AutopilotPanel.StatusLine(canvas, sw, sh * hs.HudY, px * 0.9f * hs.HudSize, flyingTarget ?? NonEmpty(AutopilotLink.Status?.Target) ?? "route", AutopilotLink.Status, stop, sw * hs.HudX, sh);
            // the asteroid warning goes under the line when the line sits near the top, else in its usual place
            if (line.Y < sh * 0.2f) hudY = line.Bottom + px * 0.5f;
            KeepClear.Add(line);
        }
        else if (ManualGuiding && path != null && Snap.HasShip)
        {
            int n = path.Next;
            bool last = n >= path.Waypoints.Count;
            string nextName = last ? path.TargetName : path.Waypoints[n].Name;
            var nextPos = last ? path.Target : path.Waypoints[n].User ? path.Waypoints[n].Pick : path.Waypoints[n].Pos;
            string afterName = last ? null : n + 1 < path.Waypoints.Count ? path.Waypoints[n + 1].Name : path.TargetName;
            var afterPos = last ? path.Target : n + 1 < path.Waypoints.Count ? path.Waypoints[n + 1].Pos : path.Target;
            string text = ManualGuide.Text(path.TargetName, n, path.Waypoints.Count + 1, nextName, nextPos, afterName, afterPos,
                Snap.ShipPos, Snap.ShipVel, path.Distance, path.Eta, Snap.FnbTargetName, out string sub);
            var line = AutopilotPanel.ManualLine(canvas, sw, sh * hs.HudY, px * 0.9f * hs.HudSize, text, sub, sw * hs.HudX, sh);
            if (line.Y < sh * 0.2f) hudY = line.Bottom + px * 0.5f;
            KeepClear.Add(line);
            BurnGuide.LastTab = default;
            if (hs.BurnGuide && MarkersShown) noseDrawn = DrawBurnGuide(path, sw, sh, px * hs.HudSize);
            if (BurnGuide.LastTab.W > 0) KeepClear.Add(BurnGuide.LastTab);
        }
        if (!noseDrawn && CrosshairHidden && MarkersShown) DrawNose(sw, sh, px * hs.HudSize);
        if (!double.IsNaN(AsteroidWatch.NearestDistance))
            AutopilotPanel.AsteroidWarning(canvas, sw, Math.Max(hudY, sh * 0.12f), px, AsteroidWatch.NearestDistance, Snap.ShipVel.Length,
                AsteroidWatch.NearestOnRoute, AsteroidWatch.NearestDodged, engaged, now, Binds.Steer.Display);

        // the destination view may take the preview's place (the setting; not while F&B's list is open to pick from)
        if (Visible && !navOpen && hs.DestViewReplacesMap && DestinationView.Wanted(InGame)) { GivenUp = WindowBox(); followBox = default; }
        else if (Visible)
        {
            var box = WindowBox();
            bool pip = InPip;
            if (pip != wasPip) { wasPip = pip; reframe = true; }
            scene.Snap = Snap; scene.Path = path; scene.Mini = pip; scene.Bends = Tracker.Bends; scene.Original = FlownRoute;
            // the camera covers the map only (last frame's), not the info strip that hangs below it
            var area = scene.MapArea;
            if (area.X != box.X || area.Y != box.Y || area.W != box.W || area.H > box.H || area.H <= 0) area = box;
            scene.Cam.Setup(area.X, area.Y, area.W, area.H);
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
            KeepClear.Add(box);
            // FOLLOW toggle, top right of the preview (clickable while the cursor is free)
            float fpx = px * 0.75f;
            string ft = "FOLLOW SHIP";
            float fw = canvas.TextWidth(ft, fpx) + fpx * 1.6f, fh = fpx * 1.7f;
            followBox = new Box(box.Right - fw - fpx * 0.6f, scene.TitleBottom + fpx * 0.5f, fw, fh);
            if (cursorFree)
            {
                // a click is handled in HandleMouse; A on it (the controller's focus) here
                if (ui.Button(followBox, ft, fpx, MapScene.ShipColor, follow) && !ui.Released) ToggleFollow();
            }
            else if (follow)
            {
                canvas.Rect(followBox.X, followBox.Y, fw, fh, MapColor.Rgb(0x0A121C, 200));
                canvas.Text(ft, followBox.X + fpx * 0.8f, followBox.Y + fpx * 0.35f, fpx, MapScene.ShipColor);
            }
        }
        else followBox = default;

        if (PlayGuard.Blocked) return; // AFK: no prompts or buttons
        var list = SideLayout.FnbList(sw, sh);
        if (path == null)
        {
            // no destination yet: the Route Plan panel is there all the same, saying what to do
            if (navOpen) { PlanPlace(list, sw, sh, px, out float ex, out float ew); panelBox = AutopilotPanel.EmptyPlan(ui, ex, list.Y, ew, sh, px, k); }
            return;
        }
        float y = navOpen ? list.Bottom + px * 0.8f : sh * 0.72f;

        // the prompt: after a pick or reaching a detour while the list is closed, or with the map hidden
        if (!Visible && !routePanel && (now < promptUntil && !ManualGuiding || navOpen))
        {
            string text = path.DetoursAhead > 0
                ? "Route to " + MapScene.ShortName(path.TargetName) + " goes via \"" + path.Waypoints[path.Next].Name + "\" first    preview [" + k + "]"
                : "Preview route to " + path.TargetName + "   [" + k + "]";
            if (engaged) text = "Autopilot is flying the route    preview [" + k + "]";
            float w = canvas.TextWidth(text, px) + px * 2, h = px * 1.8f;
            var promptBox = new Box((sw - w) / 2, y, w, h);
            float fade = navOpen ? 1 : (float)Math.Min(1, (promptUntil - now) / 1.5);
            var th = MapTheme.Current;
            var accent = path.DetoursAhead > 0 && !engaged ? th.Detour : th.Accent;
            canvas.Rect(promptBox.X, promptBox.Y, w, h, th.ApPanel.WithAlpha(fade));
            canvas.Rect(promptBox.X, promptBox.Y, Math.Max(2f, px / 7f), h, th.Trim.WithAlpha(fade));
            canvas.Rect(promptBox.X, promptBox.Bottom - 2, w, 2, accent.WithAlpha(fade));
            canvas.Text(text, promptBox.X + px, promptBox.Y + px * 0.38f, px, th.ApText.WithAlpha(fade));
            if (ui.Released && promptBox.Contains(mx, my)) { Visible = true; reframe = true; }
            y = promptBox.Bottom + px * 0.6f;
        }
        if (!navOpen) return;

        // under the list: STOP while flying, else (RESUME ROUTE and) AUTOPILOT / MANUAL
        if (engaged)
        {
            AutopilotPanel.StopPair(ui, sw / 2, y, px * 34, px * 1.9f, px, out bool stopHit, out bool manualHit, out bool fullHit);
            if (stopHit) ConfirmStop();
            else if (manualHit) { FlyManual(); return; }
            else if (fullHit) { FullStop(); return; }
            if (!routePanel) return;
            y += px * 2.6f;
        }
        else if (AutopilotLink.Braking(now))
        {
            float w = canvas.TextWidth("STOP AUTOPILOT", px) + px * 2.4f;
            if (ui.Button(new Box(sw / 2 - w / 2, y, w, px * 1.9f), "STOP AUTOPILOT", px, AutopilotPanel.Stop)) StopAutopilot();
            return;
        }
        if (resumeTarget != null && path.TargetName == resumeTarget)
        {
            string rt = "RESUME ROUTE  to " + resumeTarget;
            float w = canvas.TextWidth(rt, px) + px * 2.4f;
            if (ui.Button(new Box(sw / 2 - w / 2, y, w, px * 1.9f), rt, px, AutopilotPanel.Go, true, AutopilotLink.Count > 0)) Resume();
            y += px * 2.6f;
        }
        if (!routePanel)
        {
            // the Route Plan comes up with the list every time (its ENGAGE / FLY MANUAL / CANCEL are the choice);
            // after CANCEL it stays away until the list opens again or the destination changes
            if (panelDismissed) return;
            routePanel = true; adding = false;
        }

        // the route panel, on the other side of the list from the map
        PlanPlace(list, sw, sh, px, out float px0, out float pw);
        userNames.Clear();
        foreach (var u in Tracker.UserPoints) userNames.Add(u.Name);
        var hit = AutopilotPanel.RoutePanel(ui, px0, list.Y, pw, sh, px, path, Limits, Snap.MaxSpeed, Snap.MaxAccel, Snap.Signature, userNames, adding, Binds.Autopilot.Display, now, null, engaged);
        panelBox = hit.Panel;
        RouteHit(hit);
    }

    /// <summary>Where the Route Plan panel goes: on the other side of F&amp;B's list from the map.</summary>
    private static void PlanPlace(Box list, float sw, float sh, float px, out float x, out float w)
    {
        float margin = sh * 0.022f;
        bool left = !Plugin.Settings.PreviewLeft;
        float room = left ? list.X - margin * 2 : sw - list.Right - margin * 2;
        w = Math.Max(px * 18, Math.Min(room, px * 32));
        x = left ? list.X - margin - w : list.Right + margin;
    }

    private static void RouteHit(AutopilotPanel.RouteHit hit)
    {
        if (hit.Changed) nextRoute = 0;
        if (hit.Remove >= 0) { Tracker.RemoveUser(hit.Remove); nextRoute = 0; }
        if (hit.ToggleAdd) adding = !adding;
        if (hit.Cancel)
        {
            if (flightEdit != null) DiscardFlightEdit(); // while flying: drop the changes
            routePanel = adding = mapPanel = false; panelDismissed = true; Plugin.SaveSettings();
        }
        else if (hit.Manual && flightEdit == null) { Plugin.SaveSettings(); Manual(); }
        else if (hit.Engage) { if (flightEdit == null) Engage(); else if (!DraftChanged()) { DiscardFlightEdit(); routePanel = adding = mapPanel = false; } else ConfirmSend(false); }
    }

    /// <summary>A in the full map: the route panel in the map (while flying, to change the route).</summary>
    public static string MapAutopilotKey()
    {
        if (AutopilotLink.Engaged(Plugin.Now))
        {
            if (Path == null)
            {
                RestoreRoute(Plugin.Now, "the autopilot key");
                Tracker.Update(Snap.FnbTargetName, Snap.FnbTarget, Snap.HasShip, Snap.ShipPos, Snap.ShipVel, Snap.MaxAccel, Snap.MaxSpeed);
                Project();
            }
            if (Path == null) return "Autopilot route not on the map yet.";
            OpenFlightEdit(true);
            return null;
        }
        if (Path == null) return "No route: pick a destination first.";
        if (AutopilotLink.Count == 0) return NoPb;
        routePanel = mapPanel = true; adding = false;
        return null;
    }

    /// <summary>The full map's route editor (A / AUTOPILOT) is open: it stands on its own on the side away from the route panel.</summary>
    public static bool MapEditorOpen => routePanel && mapPanel && Path != null;
    /// <summary>ADD WAYPOINT is on in the route editor: a pick adds a waypoint.</summary>
    public static bool AddingWaypoint => routePanel && adding && Tracker.TargetName != null;
    /// <summary>The route the autopilot flies while you edit a copy of it (null when no edit has changed anything).</summary>
    public static V3[] FlownRoute => flightEdit != null && Tracker.Revision != publishedRevision ? flownPoints : null;
    /// <summary>A stopped route can be flown on (RESUME ROUTE).</summary>
    public static bool CanResume(double now) => !AutopilotLink.Engaged(now) && resumeTarget != null && Path != null && Path.TargetName == resumeTarget;

    /// <summary>The full map's route editor at (x, y), px text; returns its box.</summary>
    public static Box MapEditor(PanelUi ui, float x, float y, float sh, float px)
    {
        var path = Path;
        if (path == null) return new Box(x, y, 0, 0);
        double now = Plugin.Now;
        userNames.Clear();
        foreach (var u in Tracker.UserPoints) userNames.Add(u.Name);
        var hit = AutopilotPanel.RoutePanel(ui, x, y, px * 30, sh, px, path, Limits, Snap.MaxSpeed, Snap.MaxAccel, Snap.Signature, userNames, adding,
            Binds.Autopilot.Display, now, "Shift+click a point on the map", AutopilotLink.Engaged(now));
        RouteHit(hit);
        return hit.Panel;
    }

    /// <summary>The full map's autopilot card in the route panel's slot b (px text); closeMap = MANUAL was picked.</summary>
    public static void MapCard(PanelUi ui, Box b, float px, out bool closeMap)
    {
        closeMap = false;
        var path = Path;
        double now = Plugin.Now;
        bool braking = AutopilotLink.Braking(now);
        if (path == null && !braking || b.W <= 0) return;
        bool engaged = AutopilotLink.Engaged(now), resume = CanResume(now);
        var st = AutopilotLink.Status;
        string hint = braking ? (st != null && st.Braking ? Fmt.Speed(st.Speed) + "  stops in " + Fmt.Dist(st.Dist) : "Turning retrograde...") + "  dampeners on under 50 m/s"
            : engaged ? "Move or turn to take over" : path.DetoursAhead > 0 ? path.DetoursAhead + " detour(s) on the way" : Fmt.Dist(path.Distance) + "  ETA " + Fmt.Time(path.Eta);
        var hit = AutopilotPanel.MapCard(ui, b, px, path?.TargetName ?? "", engaged, resume, AutopilotLink.Count > 0, hint, braking);
        if (hit.Stop) { if (braking) StopAutopilot(); else ConfirmStop(); }
        else if (hit.Manual && engaged) { FlyManual(); closeMap = true; }
        else if (hit.FullStop) FullStop();
        else if (hit.Resume) Resume();
        else if (hit.Autopilot) { if (AutopilotLink.Count == 0) Notify(NoPb, false); else { routePanel = mapPanel = true; adding = false; } }
        else if (hit.Manual) { Manual(); closeMap = true; }
        else if (hit.Clear) Notify(ClearRoute());
    }

    private static string NonEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;

    /// <summary>The full map picks its camera up from the preview's.</summary>
    public static OrbitCamera Camera => scene.Cam;
}
