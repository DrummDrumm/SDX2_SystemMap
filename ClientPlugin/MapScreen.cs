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
/// S: follow your ship (toggle). F: frame the route. H: whole system. C: copy the flip point as a GPS. N: back to F&amp;B's list.
/// A: autopilot (confirm in the N list, or stop it while it flies). R: re-plan detours from here. V: detours on/off.
/// G/O/X/T/P/L/J: grid, orbits, sectors, stations, GPS, labels, moons. Esc or M: close.
/// </summary>
internal sealed class MapScreen : MyGuiScreenBase
{
    private static MapScreen open;
    public static bool IsOpen => open != null;

    private readonly MapScene scene = new MapScene();
    private readonly GameCanvas canvas = new GameCanvas();
    private double lastDraw;
    private bool framed, leftDown, rightDown, moved, rMoved;
    private float lastX, lastY, downX, downY, rDownX, rDownY;
    private string toast; private double toastUntil;
    /// <summary>The view stays centred on your ship (S); kept while the game runs.</summary>
    private static bool follow;
    /// <summary>The autopilot controls window (opened by Shift+click or A; always shown while the autopilot flies).</summary>
    private bool showControls;
    private readonly PanelUi ui = new PanelUi();
    private Box controls;   // where the controls were drawn last frame (the map ignores the mouse there)
    private bool panelGrab; // a press that started on the controls (slider drags)

    public static void Open()
    {
        if (open != null) return;
        if (PlayGuard.Blocked) { SidePreview.Notify(SidePreview.Paused, false); return; }
        follow = true; // the map opens centred on your ship; S, panning, F or H turn it off
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
    }

    public override string GetFriendlyName() => "SDX2SystemMap";

    /// <summary>K: the next look (Classic, Tachi, UNN, OPA), saved.</summary>
    public static string NextTheme()
    {
        var t = MapTheme.Current.Next();
        MapTheme.Current = t;
        Plugin.Settings.Theme = t.Name;
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
            return "VIEW|Drag orbit" + s + "Right-drag pan" + s + "Wheel zoom" + s + "Click focus" + s + "S follow ship: " + On(follow) + s + "F frame route" + s + "H whole system" + "\n"
                + "ROUTE|Shift+click set destination" + s + "Shift+right-click add / remove waypoint" + s + "A autopilot" + s + "N Flip and Burn list" + s + "R re-plan" + s + "V detours: " + On(Plugin.Settings.AvoidBodies) + s + "C copy flip GPS" + "\n"
                + "SHOW|G grid: " + On(o.Grid) + s + "O orbits: " + On(o.Orbits) + s + "X sectors: " + On(o.Sectors) + s + "T stations: " + On(o.Stations) + s + "P GPS: " + On(o.Gps) + s + "L labels: " + On(o.Labels) + s + "J moons: " + On(o.Moons) + s + "K theme: " + MapTheme.Current.Name + s + "Q pin corner" + s + "Esc back" + (Binds.Map.IsNone ? "" : s + Binds.Map.Display + " off");
        }
    }

    public override void HandleInput(bool receivedFocusInThisUpdate)
    {
        var input = MyInput.Static;
        // Esc first: the base screen's own Esc handling is off, so it can't fall through to the game's main menu
        if (input != null && input.IsNewKeyPressed(MyKeys.Escape) && State == MyGuiScreenState.OPENED) { CloseScreen(); return; }
        base.HandleInput(receivedFocusInThisUpdate);
        if (input == null || receivedFocusInThisUpdate) return;
        if (Binds.Map.Pressed()) { SidePreview.CycleOff(); CloseScreen(); return; } // the map key's last step: off
        if (input.IsNewKeyPressed(MyKeys.N)) { CloseScreen(); FlipAndBurnLink.OpenNavMenu(); return; }
        var o = scene.Opt;
        if (input.IsNewKeyPressed(MyKeys.F)) { follow = false; scene.FrameDefault(false); }
        if (input.IsNewKeyPressed(MyKeys.H)) { follow = false; scene.Cam.FlyTo(V3.Zero, 6e7); }
        if (input.IsNewKeyPressed(MyKeys.S))
        {
            follow = !follow;
            var sn = SidePreview.Snap;
            if (follow && sn.HasShip) scene.Cam.FlyTo(sn.ShipPos, Math.Min(scene.Cam.GoalDistance, MapScene.ShipViewDistance));
            Toast(!follow ? "Follow ship: off" : sn.HasShip ? "Follow ship: on" : "Follow ship: on (no ship yet: sit in a cockpit)");
        }
        if (input.IsNewKeyPressed(MyKeys.C)) CopyFlip();
        if (input.IsNewKeyPressed(MyKeys.A)) { var why = SidePreview.MapAutopilotKey(); if (why != null) Toast(why); else showControls = true; }
        if (input.IsNewKeyPressed(MyKeys.R)) { SidePreview.Replan(); Toast("Route planned again from here."); }
        if (input.IsNewKeyPressed(MyKeys.V))
        {
            Plugin.Settings.AvoidBodies = !Plugin.Settings.AvoidBodies;
            SidePreview.Replan();
            Toast(Plugin.Settings.AvoidBodies ? "Detours round worlds: on" : "Detours off: straight line");
        }
        if (input.IsNewKeyPressed(MyKeys.G)) o.Grid = !o.Grid;
        if (input.IsNewKeyPressed(MyKeys.O)) o.Orbits = !o.Orbits;
        if (input.IsNewKeyPressed(MyKeys.X)) o.Sectors = !o.Sectors;
        if (input.IsNewKeyPressed(MyKeys.T)) o.Stations = !o.Stations;
        if (input.IsNewKeyPressed(MyKeys.P)) o.Gps = !o.Gps;
        if (input.IsNewKeyPressed(MyKeys.L)) o.Labels = !o.Labels;
        if (input.IsNewKeyPressed(MyKeys.J)) o.Moons = !o.Moons;
        if (input.IsNewKeyPressed(MyKeys.K)) Toast(NextTheme());
        if (input.IsNewKeyPressed(MyKeys.Q)) Toast(SidePreview.NextPipCorner());

        var m = MyGuiManager.GetScreenCoordinateFromNormalizedCoordinate(MyGuiManager.MouseCursorPosition);
        float dx = m.X - lastX, dy = m.Y - lastY;
        bool left = input.IsLeftMousePressed(), right = input.IsRightMousePressed();
        bool overPanel = controls.W > 0 && controls.Contains(m.X, m.Y);
        if (input.IsNewLeftMousePressed()) panelGrab = overPanel;
        if (overPanel || panelGrab)
        {
            // the controls window has the mouse: no orbit, pick or zoom underneath
            if (!left) panelGrab = false;
            leftDown = false; rightDown = right && rightDown;
            lastX = m.X; lastY = m.Y;
            return;
        }
        if (left && leftDown) scene.Cam.Orbit(-dx * 0.005, dy * 0.005);
        bool shift = input.IsAnyShiftKeyPressed();
        if (input.IsNewRightMousePressed()) { rDownX = m.X; rDownY = m.Y; rMoved = false; }
        if (right && Math.Abs(m.X - rDownX) + Math.Abs(m.Y - rDownY) > 6) rMoved = true;
        if (input.IsNewRightMouseReleased() && !rMoved && shift) AddWaypointAt(m.X, m.Y);
        if (right && rightDown && !shift && Math.Abs(dx) + Math.Abs(dy) > 0) { scene.Cam.Pan(dx, dy); if (follow) { follow = false; Toast("Follow ship: off (panned)"); } }
        if (input.IsNewLeftMousePressed()) { downX = m.X; downY = m.Y; moved = false; }
        if (left && Math.Abs(m.X - downX) + Math.Abs(m.Y - downY) > 6) moved = true;
        if (input.IsNewLeftMouseReleased() && !moved && scene.Hovered is Pick p)
        {
            if (input.IsAnyShiftKeyPressed()) { Toast(SidePreview.SetDestination(p.Name, p.Kind, p.Pos, p.Detail)); if (p.Kind != "Your ship") showControls = true; }
            else { if (p.Kind != "Your ship") follow = false; scene.Cam.FlyTo(p.Pos, FlyDistance(p)); }
        }
        leftDown = left; rightDown = right;
        lastX = m.X; lastY = m.Y;
        int wheel = input.DeltaMouseScrollWheelValue();
        if (wheel != 0) scene.Cam.Zoom(wheel / 120.0);
    }

    /// <summary>
    /// Shift+right-click: a waypoint at the click (or, on one of your waypoints, removes it). On a world, station or GPS point it's that point; near the route's
    /// line it's on the line; elsewhere it's level with the nearest part of the route. It goes into the leg it's
    /// nearest, so you can build a route of several stages.
    /// </summary>
    private void AddWaypointAt(float x, float y)
    {
        var path = SidePreview.Path;
        if (path == null) { Toast("Set a destination first (Shift+click a point)"); return; }
        if (scene.Hovered is Pick p && p.Kind != "Your ship")
        {
            // one of your waypoints already: take it out again
            Toast(SidePreview.RemoveWaypoint(p.Name, p.Pos) ?? SidePreview.InsertWaypoint(p.Name, p.Pos));
            return;
        }
        if (!RouteClick.Place(scene.Cam, path.Points(), x, y, 12 * scene.Unit, out var pos, out _)) { Toast("Can't place a waypoint there: tilt the view"); return; }
        Toast(SidePreview.InsertWaypoint(null, pos));
    }

    /// <summary>A view that fits the picked thing: worlds by their size, everything else close up.</summary>
    private static double FlyDistance(Pick p)
    {
        var w = SystemData.FindWorld(p.Name);
        return w != null ? Math.Max(w.Radius * 6, 2e5) : 1e5;
    }

    private void CopyFlip()
    {
        var r = SidePreview.Path?.First;
        if (r == null || !r.HasBurnPlan) { Toast("No route to copy: pick a destination in F&B's list (N) first."); return; }
        var f = r.FlipPoint;
        string gps = string.Format(System.Globalization.CultureInfo.InvariantCulture, "GPS:Flip - {0}:{1:0.##}:{2:0.##}:{3:0.##}:#FF7CFC9A:", r.TargetName.Replace(":", " "), f.X, f.Y, f.Z);
        try { MyClipboardHelper.SetClipboard(gps); Toast("Flip point copied. Paste it in the GPS tab (G)."); }
        catch (Exception e) { Toast("Copy failed: " + e.Message); }
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
            scene.Snap = SidePreview.Snap; scene.Path = SidePreview.Path;
            scene.Cam.Setup(box.X, box.Y, box.W, box.H);
            if (!framed) { scene.FrameDefault(true); framed = true; }
            if (follow && scene.Snap.HasShip) { scene.Cam.GoalTarget = scene.Snap.ShipPos; scene.Cam.Target = scene.Snap.ShipPos; }
            scene.Cam.Tick(dt);
            var m = MyGuiManager.GetScreenCoordinateFromNormalizedCoordinate(MyGuiManager.MouseCursorPosition);
            bool overPanel = controls.W > 0 && controls.Contains(m.X, m.Y) || panelGrab;
            scene.MouseX = overPanel ? float.NaN : m.X; scene.MouseY = overPanel ? float.NaN : m.Y;
            scene.Help = Help;
            scene.Draw(canvas, box);
            // the autopilot controls, as under the N list
            var input = MyInput.Static;
            bool flying = AutopilotLink.Engaged(now);
            controls = default;
            if ((showControls || flying) && SidePreview.Path != null && input != null)
            {
                float cpx = Math.Max(14f, full.Height / 52f);
                ui.Input(canvas, m.X, m.Y, input.IsLeftMousePressed(), input.IsNewLeftMousePressed(), input.IsNewLeftMouseReleased());
                // on the right: the route summary is top left
                controls = SidePreview.MapControls(ui, canvas, box.W - cpx * 31.2f, box.H * 0.06f, box.H, cpx, out bool closeMap);
                if (closeMap) { CloseScreen(); return base.Draw(); }
            }
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
        return base.CloseScreen(isUnloading);
    }
}
