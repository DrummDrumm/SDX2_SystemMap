using System;
using Sandbox.Graphics;
using Sandbox.Graphics.GUI;
using VRage.Input;
using VRage.Utils;
using VRageMath;

namespace SDX2_SystemMap;

/// <summary>
/// The full-screen interactive map (Shift+M). Drag: orbit. Right-drag: pan. Wheel: zoom. Click a world, station or
/// GPS: focus the view on it. Shift+click: make it Flip and Burn's destination (or a waypoint while ADD WAYPOINT is
/// on), and the autopilot controls open on the left as under the N list: AUTOPILOT (route panel) / MANUAL.
/// Shift+right-click: add / remove a waypoint; Shift+right-drag: add one above or below the plane (tilt the view);
/// drag one of your waypoints: up / down raises or lowers it, sideways moves it along X or Z; Shift+right-drag: anywhere across the plane.
/// S: follow your ship (toggle). F: frame the route. H: whole system. C: copy the flip point as a GPS. N: back to F&amp;B's list.
/// A: autopilot (confirm in the N list, or stop it while it flies). R: re-plan detours from here. V: detours on/off.
/// G/O/X/T/P/L/J: grid, orbits, sectors, stations, GPS, labels, moons. Esc or M: close.
/// Controller: the D-pad or left stick moves between the autopilot controls' buttons and sliders, A presses (on release,
/// like the game's buttons), left / right change a slider, B closes the map like Esc.
/// </summary>
internal sealed class MapScreen : MyGuiScreenBase
{
    private static MapScreen open;
    public static bool IsOpen => open != null;
    /// <summary>The full map is the screen in front (no pop-up over it).</summary>
    public static bool HasFocus => open != null && MyScreenManager.GetScreenWithFocus() == open;

    private readonly MapScene scene = new MapScene();
    private readonly GameCanvas canvas = new GameCanvas();
    private double lastDraw;
    private bool framed;
    private readonly MapMouse mouse = new MapMouse();
    private string toast; private double toastUntil;
    /// <summary>The view stays centred on your ship (S): the same setting as the preview's FOLLOW SHIP.</summary>
    private static bool follow { get => Plugin.Settings.PreviewFollow; set => Plugin.Settings.PreviewFollow = value; }
    /// <summary>The autopilot controls window (opened by Shift+click or A; always shown while the autopilot flies).</summary>
    private bool showControls;
    private readonly PanelUi ui = new PanelUi();
    private readonly HeadingFollow heading = new HeadingFollow();
    private bool Turning => follow && Plugin.Settings.FollowHeading;
    private Box controls;   // where the controls were drawn last frame (the map ignores the mouse there)
    private bool panelGrab; // a press that started on the controls (slider drags)
    private bool bHere;     // B went down while the map had the controller (B closes it when let go)

    public static void Open()
    {
        if (open != null) return;
        if (PlayGuard.Blocked) { SidePreview.Notify(SidePreview.Paused, false); return; }
        open = new MapScreen();
        open.Closed += (s, unloading) => { open = null; Plugin.SaveSettings(); };
        MyGuiSandbox.AddScreen(open);
    }

    public static void Close() { try { open?.CloseScreen(); } catch { } }

    private MapScreen() : base(new Vector2(0.5f, 0.5f), null, Vector2.One, false, null, 0f, 0f)
    {
        EnabledBackgroundFade = false;
        CloseButtonEnabled = false;
        m_closeOnEsc = false; // Esc is handled in HandleInput, before anything else sees it
        DrawMouseCursor = true;
        m_canShareInput = false;
        scene.Opt = Plugin.Settings.Options;
        var c = SidePreview.Camera;
        scene.Cam.Yaw = c.Yaw; scene.Cam.Pitch = c.Pitch;
        mouse.Toast = Toast;
        mouse.Following = () => follow;
        mouse.Unfollow = () => follow = false;
        mouse.Heading = heading;
        mouse.Turning = () => Turning;
        mouse.Destination = _ => showControls = true;
    }

    public override string GetFriendlyName() => "SDX2SystemMap";

    /// <summary>K: the next look (Classic, Tachi, UNN, Belter), saved.</summary>
    public static string NextTheme()
    {
        Plugin.Settings.Theme = MapTheme.Current.Next().Name;
        Plugin.Settings.ApplyLook();
        var t = MapTheme.Current;
        Plugin.SaveSettings();
        return "Map theme: " + t.Name + " (" + t.Blurb + ")";
    }

    private static string On(bool b) => b ? "on" : "off";

    /// <summary>The key help, in groups (one row each in the bottom bar).</summary>
    private string Help
    {
        get
        {
            const string s = MapScene.Sep;
            var o = scene.Opt;
            // the controller was used last: its buttons instead (the game's own switch, as its controller icons)
            if (padMode) return "VIEW|Right stick cursor" + s + "Left stick move map" + s + "RB+LB + right stick turn / tilt" + s + "RB+LB + left stick zoom" + s + "R3 focus" + s + "X follow ship: " + On(follow) + s + "L3 turn with ship: " + On(Plugin.Settings.FollowHeading) + s + "Y frame route" + s + "RB+LB + R3 whole system" + "\n"
                + "ROUTE|RT set destination" + s + "LT add / remove waypoint or detour" + s + "Start route plan" + s + "D-pad buttons" + s + "A press" + s + "LB + Select N list" + s + "RB+LB + Y re-plan" + s + "RB+LB + X clear route" + "\n"
                + "MAP|B back" + s + "RB + Select close" + s + "Grid, orbits, labels, theme and the rest: keyboard";
            return "VIEW|Drag orbit" + s + "Right-drag pan" + s + "Wheel zoom" + s + "Click focus" + s + "S follow ship: " + On(follow) + s + "U turn with ship: " + On(Plugin.Settings.FollowHeading) + s + "F frame route" + s + "H whole system" + "\n"
                + "ROUTE|Shift+click set destination" + s + "Shift+right-click add / remove waypoint or detour" + s + "Shift+right-drag add above / below" + s + "Drag waypoint: up / down (Y) or sideways (X, Z)" + s + "Shift+right-drag waypoint: across" + s + "Right-click destination or Del: clear route" + s + "Drag flip point: bend leg" + s + "Shift+right-click bend: straighten" + s +"A route plan" + s + "N list" + s + "R re-plan" + s + "V detours: " + On(Plugin.Settings.AvoidBodies) + s + "C copy flip GPS" + "\n"
                + "SHOW|G grid: " + On(o.Grid) + s + "O orbits: " + On(o.Orbits) + s + "X sectors: " + On(o.Sectors) + s + "T stations: " + On(o.Stations) + s + "P GPS: " + On(o.Gps) + s + "L labels: " + On(o.Labels) + s + "J moons: " + On(o.Moons) + s + "Y ship routes: " + On(o.Routes) + s + "E copy route share code" + s + "I import share code (Shift+I: clear shared)" + s + "K theme: " + MapTheme.Current.Name + s + "Q pin corner" + (Binds.PanelSide.IsNone ? "" : s + Binds.PanelSide.Display + " panel side") + s + "Esc back" + (Binds.Map.IsNone ? "" : s + Binds.Map.Display + " off");
        }
    }

    public override void HandleInput(bool receivedFocusInThisUpdate)
    {
        var input = MyInput.Static;
        // Esc first: the base screen's own Esc handling is off, so it can't fall through to the game's main menu
        if (input != null && input.IsNewKeyPressed(MyKeys.Escape) && State == MyGuiScreenState.OPENED) { CloseScreen(); return; }
        // B: the same, when it's let go (as the game's message boxes do), so nothing under the map sees it come up as new
        if (!receivedFocusInThisUpdate && GamePad.BPressed) bHere = true;
        if (bHere && GamePad.BReleased && State == MyGuiScreenState.OPENED) { bHere = false; CloseScreen(); return; }
        if (!GamePad.BDown && !GamePad.BReleased) bHere = false;
        base.HandleInput(receivedFocusInThisUpdate);
        if (input == null || receivedFocusInThisUpdate) return;
        if (Binds.Map.Pressed()) { SidePreview.CycleOff(); CloseScreen(); return; } // the map key's last step: off
        if (input.IsNewKeyPressed(MyKeys.N)) { CloseScreen(); FlipAndBurnLink.OpenNavMenu(); return; }
        // the controller: RB + Select closes the full map again, LB + Select goes to the N list and the Route Plan
        var pad = GamePad.Pressed();
        if (pad == GamePad.Shortcut.FullMap) { CloseScreen(); return; }
        if (pad == GamePad.Shortcut.Autopilot) { CloseScreen(); SidePreview.AutopilotKey(); return; }
        PadInput();
        var o = scene.Opt;
        if (input.IsNewKeyPressed(MyKeys.F)) { follow = false; scene.FrameDefault(false); }
        if (input.IsNewKeyPressed(MyKeys.H)) { follow = false; scene.Cam.FlyTo(V3.Zero, 6e7); }
        if (input.IsNewKeyPressed(MyKeys.S)) ToggleFollow();
        if (input.IsNewKeyPressed(MyKeys.U)) ToggleTurn();
        if (input.IsNewKeyPressed(MyKeys.C)) Toast(CopyFlip());
        if (input.IsNewKeyPressed(MyKeys.Delete)) Toast(SidePreview.ClearRoute());
        if (input.IsNewKeyPressed(MyKeys.A)) { var why = SidePreview.MapAutopilotKey(); if (why != null) Toast(why); else showControls = true; }
        if (input.IsNewKeyPressed(MyKeys.R)) { bool back = SidePreview.Tracker.Skipped.Count > 0; SidePreview.Replan(true); Toast("Route re-planned" + (back ? " (removed detours restored)." : ".")); }
        if (input.IsNewKeyPressed(MyKeys.V))
        {
            Plugin.Settings.AvoidBodies = !Plugin.Settings.AvoidBodies;
            SidePreview.Replan();
            Toast(Plugin.Settings.AvoidBodies ? "Detours: on" : "Detours: off (straight line)");
        }
        if (input.IsNewKeyPressed(MyKeys.G)) o.Grid = !o.Grid;
        if (input.IsNewKeyPressed(MyKeys.O)) o.Orbits = !o.Orbits;
        if (input.IsNewKeyPressed(MyKeys.X)) o.Sectors = !o.Sectors;
        if (input.IsNewKeyPressed(MyKeys.T)) o.Stations = !o.Stations;
        if (input.IsNewKeyPressed(MyKeys.P)) o.Gps = !o.Gps;
        if (input.IsNewKeyPressed(MyKeys.L)) o.Labels = !o.Labels;
        if (input.IsNewKeyPressed(MyKeys.J)) o.Moons = !o.Moons;
        if (input.IsNewKeyPressed(MyKeys.Y)) { o.Routes = !o.Routes; Toast(o.Routes ? "Ship routes: on (where your other ships should be)" : "Ship routes: off"); }
        if (input.IsNewKeyPressed(MyKeys.E)) Toast(FleetRoutes.CopyCode(out _));
        if (input.IsNewKeyPressed(MyKeys.I)) Toast(input.IsAnyShiftKeyPressed() ? FleetRoutes.ClearShared() : FleetRoutes.ImportClipboard(out _));
        if (input.IsNewKeyPressed(MyKeys.K)) Toast(NextTheme());
        if (input.IsNewKeyPressed(MyKeys.Q)) Toast(SidePreview.NextPipCorner());
        if (Binds.PanelSide.Pressed())
        {
            Plugin.Settings.PanelRight = !Plugin.Settings.PanelRight;
            Plugin.SaveSettings();
            Toast(Plugin.Settings.PanelRight ? "Route panel: right" : "Route panel: left");
        }

        var m = MyGuiManager.GetScreenCoordinateFromNormalizedCoordinate(MyGuiManager.MouseCursorPosition);
        bool left = input.IsLeftMousePressed();
        bool overPanel = controls.W > 0 && controls.Contains(m.X, m.Y);
        if (input.IsNewLeftMousePressed()) panelGrab = overPanel;
        if (overPanel || panelGrab)
        {
            // the controls window has the mouse: no orbit, pick or zoom underneath
            if (!left) panelGrab = false;
            mouse.Handle(scene, input, m.X, m.Y, false);
            return;
        }
        mouse.Handle(scene, input, m.X, m.Y, !padMode);
    }

    /// <summary>C: the first leg's flip point to the clipboard as a GPS line. Returns what happened.</summary>
    public static string CopyFlip()
    {
        var r = SidePreview.Path?.First;
        if (r == null || !r.HasBurnPlan) return "No route: pick a destination first.";
        var f = r.FlipPoint;
        string gps = string.Format(System.Globalization.CultureInfo.InvariantCulture, "GPS:Flip - {0}:{1:0.##}:{2:0.##}:{3:0.##}:#FF7CFC9A:", r.TargetName.Replace(":", " "), f.X, f.Y, f.Z);
        try { MyClipboardHelper.SetClipboard(gps); return "Flip point copied: paste it in the GPS tab."; }
        catch (Exception e) { return "Copy failed: " + e.Message; }
    }

    private void ToggleFollow()
    {
        follow = !follow;
        var sn = SidePreview.Snap;
        if (follow && sn.HasShip) scene.Cam.FlyTo(sn.ShipPos, Math.Min(scene.Cam.GoalDistance, MapScene.ShipViewDistance));
        Toast(!follow ? "Follow ship: off" : sn.HasShip ? "Follow ship: on" : "Follow ship: on (sit in a cockpit)");
    }

    private void ToggleTurn()
    {
        Plugin.Settings.FollowHeading = !Plugin.Settings.FollowHeading; heading.Offset = 0; Plugin.SaveSettings();
        string key = padMode ? "X" : "S";
        Toast(Plugin.Settings.FollowHeading ? (follow ? "Turn with ship: on (travel points up)" : "Turn with ship: on (when following: " + key + ")") : "Turn with ship: off");
    }

    // at full stick: radians, wheel notches and screen heights a second
    private const double PadTurnRate = 1.8, PadZoomRate = 6, PadCursorRate = 0.75, PadPanRate = 0.7;
    private double lastPadInput;
    /// <summary>The controller was used last: its cursor and button help are shown instead of the mouse's.</summary>
    private bool padMode;
    private float padX, padY;

    /// <summary>
    /// The controller in the full map. Right stick: the select cursor; left stick: moves the map; RT: destination at
    /// the cursor; LT: waypoint there; R3 (or A with no buttons up): focus; X / Y / L3 / Start: follow, frame, turn with ship,
    /// the route plan. With RB + LB held the right stick turns and tilts, the left stick zooms, Y re-plans, X clears, R3 shows the whole system.
    /// </summary>
    private void PadInput()
    {
        double now = Plugin.Now, dt = Math.Min(0.1, now - lastPadInput);
        lastPadInput = now;
        bool read = GamePad.ReadMap(out var p), was = padMode;
        padMode = read && GamePad.Active;
        if (!padMode || dt <= 0) return;
        var full = MyGuiManager.GetFullscreenRectangle();
        if (!was)
        {
            // the cursor starts where the mouse is
            var m = MyGuiManager.GetScreenCoordinateFromNormalizedCoordinate(MyGuiManager.MouseCursorPosition);
            padX = m.X; padY = m.Y;
            mouse.Reset(scene);
        }
        if (p.Bumpers)
        {
            // the same way round as a mouse drag: stick right is a drag to the right, stick up a drag up
            if (p.Rx != 0 || p.Ry != 0)
            {
                double yaw = -p.Rx * PadTurnRate * dt;
                scene.Cam.Orbit(yaw, p.Ry * PadTurnRate * dt);
                if (Turning) heading.Turned(yaw);
            }
            if (p.Ly != 0) scene.Cam.Zoom(-p.Ly * PadZoomRate * dt);
            if (GamePad.New(MyJoystickButtonsEnum.J04)) { bool back = SidePreview.Tracker.Skipped.Count > 0; SidePreview.Replan(true); Toast("Route re-planned" + (back ? " (removed detours restored)." : ".")); }
            if (GamePad.New(MyJoystickButtonsEnum.J03)) Toast(SidePreview.ClearRoute());
            if (GamePad.New(MyJoystickButtonsEnum.J10)) { follow = false; scene.Cam.FlyTo(V3.Zero, 6e7); }
        }
        else
        {
            // the cursor: slow near the centre of the stick for small moves
            double c = full.Height * PadCursorRate * dt;
            padX += (float)(p.Rx * Math.Abs(p.Rx) * c); padY += (float)(p.Ry * Math.Abs(p.Ry) * c);
            if (p.Lx != 0 || p.Ly != 0)
            {
                float k = (float)(full.Height * PadPanRate * dt);
                scene.Cam.Pan(-p.Lx * k, -p.Ly * k); // the view goes the way the stick is pushed
                if (follow) { follow = false; Toast("Follow ship: off (moved the map)"); }
            }
            if (p.RtNew) mouse.PadSelect(scene);
            if (p.LtNew) mouse.PadWaypoint(scene, padX, padY);
            // A presses the Route controls' focused button while they are up; otherwise it focuses like R3
            if (GamePad.New(MyJoystickButtonsEnum.J10) || GamePad.New(MyJoystickButtonsEnum.J01) && controls.W <= 0) mouse.PadFocus(scene);
            if (GamePad.New(MyJoystickButtonsEnum.J03)) ToggleFollow();
            if (GamePad.New(MyJoystickButtonsEnum.J04)) { follow = false; scene.FrameDefault(false); }
            if (GamePad.New(MyJoystickButtonsEnum.J09)) ToggleTurn();
            if (GamePad.New(MyJoystickButtonsEnum.J08)) { var why = SidePreview.MapAutopilotKey(); if (why != null) Toast(why); else showControls = true; }
        }
        padX = Math.Max(0, Math.Min(full.Width - 1, padX)); padY = Math.Max(0, Math.Min(full.Height - 1, padY));
    }

    /// <summary>The controller's select cursor: a small cross with a gap in the middle.</summary>
    private void DrawPadCursor(float unit)
    {
        float r = unit * 1.1f, g = unit * 0.35f, w = Math.Max(2f, unit * 0.14f);
        var dark = MapColor.Rgb(0x05090E, 200); var col = MapColor.Rgb(0xFFFFFF);
        for (int pass = 0; pass < 2; pass++)
        {
            float lw = pass == 0 ? w * 2.2f : w; var cc = pass == 0 ? dark : col;
            canvas.Line(padX - r, padY, padX - g, padY, lw, cc); canvas.Line(padX + g, padY, padX + r, padY, lw, cc);
            canvas.Line(padX, padY - r, padX, padY - g, lw, cc); canvas.Line(padX, padY + g, padX, padY + r, lw, cc);
        }
    }

    private void Toast(string text) { toast = text; toastUntil = Plugin.Now + 4; }

    public override bool Draw()
    {
        try
        {
            double now = Plugin.Now, dt = lastDraw == 0 ? 0 : Math.Min(0.1, now - lastDraw);
            lastDraw = now;
            var full = MyGuiManager.GetFullscreenRectangle();
            var box = new Box(0, 0, full.Width, full.Height);
            scene.Snap = SidePreview.Snap; scene.Path = SidePreview.Path; scene.Bends = SidePreview.Tracker.Bends; scene.Original = SidePreview.FlownRoute;
            scene.Cam.Setup(box.X, box.Y, box.W, box.H);
            if (!framed) { scene.FrameDefault(true); framed = true; }
            if (follow && scene.Snap.HasShip) { scene.Cam.GoalTarget = scene.Snap.ShipPos; scene.Cam.Target = scene.Snap.ShipPos; }
            if (Turning && scene.Snap.HasShip) heading.Update(scene.Cam, scene.Snap.ShipVel, scene.Snap.ShipFwd, dt);
            scene.Cam.Tick(dt);
            var m = MyGuiManager.GetScreenCoordinateFromNormalizedCoordinate(MyGuiManager.MouseCursorPosition);
            // the controller's cursor points at the map instead of the mouse while the controller is in use
            float hx = padMode ? padX : m.X, hy = padMode ? padY : m.Y;
            bool overPanel = controls.W > 0 && controls.Contains(hx, hy) || !padMode && panelGrab;
            scene.MouseX = overPanel ? float.NaN : hx; scene.MouseY = overPanel ? float.NaN : hy;
            scene.Help = Help;
            var input = MyInput.Static;
            bool wanted = MapControls.Prepare(scene, showControls, now, out bool card) && input != null;
            if (input == null) scene.ControlsHeight = null;
            scene.Draw(canvas, box);
            // the autopilot controls: the card in the route panel's top slot, the route editor on the other side
            controls = default;
            if (!wanted) GamePad.Take(); // no buttons: a push now doesn't move the focus later
            if (wanted)
            {
                // a pop-up over the map takes the clicks: the buttons under it don't
                bool focus = HasFocus;
                ui.Input(canvas, m.X, m.Y, focus && input.IsLeftMousePressed(), focus && input.IsNewLeftMousePressed(), focus && input.IsNewLeftMouseReleased());
                ui.PadActive = focus && GamePad.Active;
                ui.Pad(focus ? GamePad.Take() : PanelUi.PadDir.None, focus && GamePad.ADown);
                controls = MapControls.Draw(ui, scene, box, card, now, out bool closeMap);
                if (closeMap) { CloseScreen(); return base.Draw(); }
            }
            if (padMode) DrawPadCursor(Math.Max(14f, full.Height / 50f));
            if (toast != null && now < toastUntil)
            {
                float px = Math.Max(14f, full.Height / 50f), w = canvas.TextWidth(toast, px) + px * 2;
                canvas.Rect((box.W - w) / 2, box.H * 0.12f, w, px * 1.8f, MapColor.Rgb(0x0A121C, 230));
                canvas.Text(toast, box.W / 2, box.H * 0.12f + px * 0.38f, px, MapColor.Rgb(0xD8E6F0), TextAnchor.Center);
            }
        }
        catch (Exception e) { Plugin.LogThrottled("map draw failed: " + e); }
        return base.Draw();
    }

    public override bool CloseScreen(bool isUnloading = false)
    {
        Plugin.Settings.Yaw = scene.Cam.Yaw; Plugin.Settings.Pitch = scene.Cam.Pitch;
        if (!isUnloading) SidePreview.FullMapClosed();
        return base.CloseScreen(isUnloading);
    }
}
