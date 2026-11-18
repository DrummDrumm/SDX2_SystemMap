using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SDX2_SystemMap;

/// <summary>
/// The full map on a second screen (setting "Map on a second screen", or its key): a window of its own
/// (<see cref="MapWindow"/>) showing the same map as the full map, with its own view, and the same route and autopilot
/// controls. The game thread draws it here (30 frames a second at most) into a <see cref="DrawList"/>; the window plays
/// it back on its own thread.
/// Touch: drag orbit, pinch zoom, two fingers move pan / twist turn, tap focus, hold then let go: set the destination;
/// the TAP button makes a tap set the destination or add / remove waypoints. A mouse on that screen works as in the
/// full map (Shift+click and so on). The buttons down its side do what the full map's keys do.
/// </summary>
internal static class SecondScreen
{
    private const double FrameTime = 1 / 30.0;
    private static MapWindow win;
    private static readonly MapScene scene = new MapScene();
    private static readonly MapMouse mouse = new MapMouse();
    private static readonly TouchGestures touch = new TouchGestures();
    private static readonly PanelUi ui = new PanelUi();
    private static readonly HeadingFollow heading = new HeadingFollow();
    private static GdiCanvas measurer;
    private static Func<string, float, float> measure;
    private static double lastFrame, lastTouch = -10, toastUntil, clearArmedUntil;
    private static bool framed, showControls, layers, hidden, uiGrab, opening, closedHere;
    private static string toast;
    private static Box controls, bar;
    // the window's mouse
    private static float mx = float.NaN, my = float.NaN, px, py;
    private static bool mLeft, mRight;
    private static float pinchPan;

    private static bool follow { get => Plugin.Settings.PreviewFollow; set => Plugin.Settings.PreviewFollow = value; }
    private static bool Turning => follow && Plugin.Settings.FollowHeading;

    public static bool IsOpen => win != null && !win.Gone;

    static SecondScreen()
    {
        mouse.Toast = Toast;
        mouse.Following = () => follow;
        mouse.Unfollow = () => follow = false;
        mouse.Heading = heading;
        mouse.Turning = () => Turning;
        mouse.Destination = _ => showControls = true;
    }

    /// <summary>The setting / key: open or close it (saved, so it comes back with the next world).</summary>
    public static void Set(bool on)
    {
        Plugin.Settings.SecondScreen = on;
        Plugin.SaveSettings();
        if (on) { closedHere = false; Open(); }
        else Close();
    }

    public static void Toggle()
    {
        Set(!IsOpen);
        SidePreview.Notify(IsOpen ? "Map on the second screen" + (win.Full ? "" : " (one screen only: a window)") : Plugin.Settings.SecondScreen ? "Second screen: can't open a window" : "Second screen map closed");
    }

    /// <summary>The monitors, for the setting: "Auto" then each screen.</summary>
    public static List<string> ScreenNames()
    {
        var l = new List<string> { "Auto (first that isn't the main one)" };
        try
        {
            var all = Screen.AllScreens;
            for (int i = 0; i < all.Length; i++) l.Add("Screen " + (i + 1) + ": " + all[i].Bounds.Width + "x" + all[i].Bounds.Height + (all[i].Primary ? " (main)" : ""));
        }
        catch { }
        return l;
    }

    private static void Open()
    {
        if (IsOpen || opening) return;
        opening = true;
        try
        {
            var all = Screen.AllScreens;
            int pick = Plugin.Settings.SecondScreenMonitor;
            Screen s = pick >= 1 && pick <= all.Length ? all[pick - 1] : null;
            if (s == null) foreach (var a in all) if (!a.Primary) { s = a; break; }
            s ??= Screen.PrimaryScreen;
            win = MapWindow.Start(s.Bounds, !s.Primary);
            if (win == null) { Plugin.Log("second screen: the window didn't start"); return; }
            framed = false; showControls = false; lastFrame = 0; touch.Clear(); mouse.Reset(scene);
            scene.Cam.Yaw = SidePreview.Camera.Yaw; scene.Cam.Pitch = SidePreview.Camera.Pitch;
            measurer ??= GdiCanvas.Measurer();
            measure ??= measurer.TextWidth;
        }
        catch (Exception e) { Plugin.Log("second screen failed to open: " + e); win = null; }
        finally { opening = false; }
    }

    private static void Close()
    {
        Answer(false); // a question left open here would hold the autopilot's pop-ups shut
        win?.CloseSoon();
        win = null;
    }

    /// <summary>The world is closing: the window goes, the setting stays (it opens again with the next world).</summary>
    public static void Reset() { Close(); closedHere = false; }

    private static void Toast(string text) { toast = text; toastUntil = Plugin.Now + 4; }

    private sealed class Prompt { public string Caption, Text, Yes, No; public Action<bool> Done; }
    private static Prompt prompt;
    private static bool inFrame;

    /// <summary>
    /// A yes/no question from a click on this screen (engage with the drives off, low water, take control...): asked
    /// here, over the map, instead of in the game window. False when the click didn't come from here (or it's closed).
    /// </summary>
    public static bool Ask(string caption, string text, string yes, string no, Action<bool> done)
    {
        if (!inFrame || !IsOpen) return false;
        Answer(false); // (one at a time: an older one is answered no)
        prompt = new Prompt { Caption = caption, Text = text, Yes = yes, No = no, Done = done };
        return true;
    }

    private static void Answer(bool yes)
    {
        var p = prompt;
        prompt = null;
        if (p == null) return;
        try { p.Done(yes); } catch (Exception e) { Plugin.LogThrottled("second screen answer failed: " + e); }
    }

    /// <summary>The question over the map; its two buttons answer it.</summary>
    private static void DrawPrompt(DrawList c, Box box)
    {
        var p = prompt;
        float px = Math.Max(15f, box.H / 46f), pad = px * 1.1f, w = Math.Min(box.W * 0.86f, px * 34);
        var lines = WrapText(c, p.Text, px * 0.9f, w - pad * 2);
        float lh = px * 1.25f, bh = px * 2.1f;
        float h = PanelStyle.HeaderH(px) + pad * 0.6f + lines.Count * lh + pad + bh + pad;
        var b = new Box(box.X + (box.W - w) / 2, box.Y + Math.Max(px, (box.H - h) / 2), w, h);
        c.Rect(box.X, box.Y, box.W, box.H, MapColor.Rgb(0x000000, 140));
        PanelStyle.Frame(c, b, px / 12f, px, p.Caption.ToUpperInvariant(), null, AutopilotPanel.Panel);
        float y = b.Y + PanelStyle.HeaderH(px) + pad * 0.6f;
        foreach (var l in lines) { c.Text(l, b.X + pad, y, px * 0.9f, AutopilotPanel.Text); y += lh; }
        y += pad;
        float bw = (w - pad * 2 - px) / 2;
        bool yes = ui.Button(new Box(b.X + pad, y, bw, bh), p.Yes, px, AutopilotPanel.Go);
        bool no = ui.Button(new Box(b.X + pad + bw + px, y, bw, bh), p.No, px, AutopilotPanel.Dim);
        if (yes || no) Answer(yes);
    }

    private static List<string> WrapText(DrawList c, string text, float px, float width)
    {
        var lines = new List<string>();
        foreach (var para in (text ?? "").Split('\n'))
        {
            string line = "";
            foreach (var word in para.Split(' '))
            {
                string t = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && c.TextWidth(t, px) > width) { lines.Add(line); line = word; }
                else line = t;
            }
            lines.Add(line);
        }
        return lines;
    }

    /// <summary>The game's HUD notices (autopilot engaged, errors, pop-ups to answer) shown on this screen too while it's open.</summary>
    public static void Echo(string text, bool ok)
    {
        if (!IsOpen || string.IsNullOrEmpty(text)) return;
        toast = text; toastUntil = Plugin.Now + (ok ? 4 : 7);
    }

    /// <summary>Every frame, from Plugin.Update (also while paused: it then says so).</summary>
    public static void Update(double now)
    {
        if (!PlayGuard.Blocked && Binds.SecondScreen.Pressed()) Toggle();
        if (win == null)
        {
            if (Plugin.Settings.SecondScreen && !closedHere && WorldGate.Active) { Open(); if (win == null) closedHere = true; }
            return;
        }
        if (win.Gone)
        {
            // closed with its own close button (or Alt+F4): the setting goes off
            win = null; closedHere = true;
            Plugin.Settings.SecondScreen = false; Plugin.SaveSettings();
            return;
        }
        if (now - lastFrame < FrameTime) return;
        if (PlayGuard.ViewOnly && !win.Inputs.IsEmpty)
        {
            PlayGuard.NoteInput(now); // a touch here is input too: back from AFK, and the queue is read next frame
            return;
        }
        var list = win.TakeFree();
        if (list == null) return; // the window is still drawing the last one
        double dt = lastFrame == 0 ? 0 : Math.Min(0.2, now - lastFrame);
        lastFrame = now;
        int w = win.ClientW, h = win.ClientH;
        list.Clear(w, h); list.Measure = measure;
        try
        {
            if (PlayGuard.Blocked && !PlayGuard.ViewOnly || SidePreview.OutOfCockpit)
            {
                while (win.Inputs.TryDequeue(out _)) { }
                touch.Clear(); mouse.Reset(scene); mLeft = mRight = false;
                if (SidePreview.OutOfCockpit) Answer(false); // out of the seat: nothing it asked about can go ahead
                Message(list, w, h, SidePreview.OutOfCockpit ? "The map shows while you sit in a cockpit, seat or remote control." : SidePreview.Paused);
            }
            else
            {
                inFrame = true;
                try { Frame(list, new Box(0, 0, w, h), now, dt, !PlayGuard.ViewOnly); }
                finally { inFrame = false; }
            }
        }
        catch (Exception e) { Plugin.LogThrottled("second screen draw failed: " + e); }
        win.Publish(list);
    }

    private static void Message(DrawList c, int w, int h, string text)
    {
        float t = Math.Max(16f, h / 40f);
        c.Rect(0, 0, w, h, MapColor.Rgb(0x05080C));
        c.Text(text, w / 2f, h / 2f - t / 2, t, MapColor.Rgb(0xB8C6D0), TextAnchor.Center);
    }

    private static void Frame(DrawList c, Box box, double now, double dt, bool live)
    {
        scene.Snap = SidePreview.Snap; scene.Path = SidePreview.Path; scene.Bends = SidePreview.Tracker.Bends; scene.Original = SidePreview.FlownRoute;
        scene.Opt = Plugin.Settings.Options;
        scene.Cam.Setup(box.X, box.Y, box.W, box.H);
        if (!framed) { scene.FrameDefault(true); framed = true; }

        // ---- input
        var m = new MouseInput();
        bool sawLeft = false, sawRight = false, any = false;
        if (live)
            while (win.Inputs.TryPeek(out var e))
            {
                // one press or release per button per frame: a fast click's release waits for the next one
                if (e.Kind == WinInput.K.MouseDown || e.Kind == WinInput.K.MouseUp)
                {
                    if (e.Id == 0 ? sawLeft : sawRight) break;
                    if (e.Id == 0) sawLeft = true; else sawRight = true;
                }
                win.Inputs.TryDequeue(out e);
                any = true;
                switch (e.Kind)
                {
                    case WinInput.K.TouchDown: touch.Down(e.Id, e.X, e.Y, now); lastTouch = now; break;
                    case WinInput.K.TouchMove: touch.Move(e.Id, e.X, e.Y); lastTouch = now; break;
                    case WinInput.K.TouchUp: case WinInput.K.TouchLost: touch.Up(e.Id, e.X, e.Y); lastTouch = now; break;
                    case WinInput.K.MouseMove: mx = e.X; my = e.Y; break;
                    case WinInput.K.MouseDown:
                        mx = e.X; my = e.Y;
                        if (e.Id == 0) { mLeft = m.NewLeft = true; } else { mRight = m.NewRight = true; }
                        break;
                    case WinInput.K.MouseUp:
                        mx = e.X; my = e.Y;
                        if (e.Id == 0) { mLeft = false; m.LeftReleased = true; } else { mRight = false; m.RightReleased = true; }
                        break;
                    case WinInput.K.Wheel: m.Notches += e.Delta / 120.0; break;
                }
            }
        if (any) PlayGuard.NoteInput(now);
        var tf = live ? touch.Step(now) : new TouchFrame { X = float.NaN, Y = float.NaN, Spread = 1 };
        bool fingers = touch.Touching || now - lastTouch < 0.3;
        float x, y;
        if (fingers)
        {
            x = tf.X; y = tf.Y;
            m = new MouseInput { Left = tf.Left, NewLeft = tf.NewLeft, LeftReleased = tf.LeftReleased, Right = tf.Right, NewRight = tf.NewRight, RightReleased = tf.RightReleased, Shift = tf.Shift };
        }
        else
        {
            x = mx; y = my;
            m.Left = mLeft; m.Right = mRight; m.Shift = MapWindow.ShiftHeld;
        }
        scene.HoverReach = now - lastTouch < 5 ? 24 : 10;
        mouse.ClickSlop = fingers ? TouchGestures.TapSlop : 6;
        if (!float.IsNaN(x)) { px = x; py = y; }
        if (tf.Cancel) mouse.Reset(scene);

        // two fingers: pinch, pan, twist
        if (tf.Spread != 1 && tf.Spread > 0)
        {
            scene.Cam.Zoom(Math.Log(tf.Spread) / -Math.Log(0.82));
            scene.Cam.Distance = scene.Cam.GoalDistance;
        }
        if (touch.Pinching)
        {
            pinchPan += Math.Abs(tf.PanX) + Math.Abs(tf.PanY);
            if (pinchPan > 24 && (tf.PanX != 0 || tf.PanY != 0))
            {
                scene.Cam.Pan(tf.PanX, tf.PanY);
                if (follow) { follow = false; Toast("Follow ship: off (panned)"); }
            }
            if (tf.Twist != 0) { scene.Cam.Orbit(tf.Twist, 0); if (Turning) heading.Turned(tf.Twist); }
        }
        else pinchPan = 0;

        // a press on the buttons or the autopilot controls stays with them until let go
        bool down = m.Left || m.Right, pressed = m.NewLeft || m.NewRight, released = m.LeftReleased || m.RightReleased;
        bool overUi = prompt != null || !float.IsNaN(x) && (controls.W > 0 && controls.Contains(x, y) || bar.W > 0 && bar.Contains(x, y));
        if (pressed) uiGrab = overUi;
        bool toUi = uiGrab || overUi && !down && !released;
        if (live && (uiGrab || overUi)) mouse.Handle(scene, new MouseInput(), px, py, false);
        else if (live) mouse.Handle(scene, m, px, py, !float.IsNaN(x) || mouse.Busy);
        if (tf.HoldStarted)
            Toast(scene.Hovered is Pick hp && hp.Kind != "Your ship" ? "Let go to make " + hp.Name + " the destination" : "Hold on a world, station or GPS point to make it the destination");

        if (follow && scene.Snap.HasShip) { scene.Cam.GoalTarget = scene.Snap.ShipPos; scene.Cam.Target = scene.Snap.ShipPos; }
        if (Turning && scene.Snap.HasShip) heading.Update(scene.Cam, scene.Snap.ShipVel, scene.Snap.ShipFwd, dt);
        scene.Cam.Tick(dt);
        scene.MouseX = toUi || float.IsNaN(x) ? float.NaN : x; scene.MouseY = toUi || float.IsNaN(x) ? float.NaN : y;
        scene.Help = Help;

        // ---- draw
        bool wanted = MapControls.Prepare(scene, showControls, now, out bool card) && live;
        if (!live) scene.ControlsHeight = null;
        scene.Draw(c, box);
        ui.Input(c, toUi ? x : float.NaN, toUi ? y : float.NaN, toUi && down, toUi && pressed, toUi && released);
        if (released) uiGrab = false;
        controls = default;
        if (prompt != null) { bar = default; DrawPrompt(c, box); }
        else if (wanted)
        {
            controls = MapControls.Draw(ui, scene, box, card, now, out bool manual);
            if (manual) showControls = false;
        }
        if (prompt == null) bar = live ? Toolbar(box, now) : default;
        if (toast != null && now < toastUntil)
        {
            float tpx = Math.Max(16f, box.H / 45f);
            tpx = Math.Min(tpx, box.W * 0.9f / Math.Max(1f, c.TextWidth(toast, 1) + 2)); // a long notice fits the width
            float tw = c.TextWidth(toast, tpx) + tpx * 2;
            c.Rect((box.W - tw) / 2, box.H * 0.12f, tw, tpx * 1.8f, MapColor.Rgb(0x0A121C, 230));
            c.Text(toast, box.W / 2, box.H * 0.12f + tpx * 0.38f, tpx, MapColor.Rgb(0xD8E6F0), TextAnchor.Center);
        }
    }

    private static string On(bool b) => b ? "on" : "off";

    private static string Help
    {
        get
        {
            const string s = MapScene.Sep;
            return "TOUCH|Drag orbit" + s + "Pinch zoom" + s + "Two fingers: move to pan, twist to turn" + s + "Tap focus" + s + "Hold, then let go: set destination" + s
                    + "TAP button: what a tap does (" + TapName + ")" + s + "Drag your waypoint to move it" + "\n"
                + "MOUSE|Drag orbit" + s + "Right-drag pan" + s + "Wheel zoom" + s + "Click focus" + s + "Shift+click set destination" + s + "Shift+right-click add / remove waypoint" + s + "Right-click the destination: clear route";
        }
    }

    private static string TapName => touch.Mode == TapMode.Focus ? "focus" : touch.Mode == TapMode.Destination ? "set destination" : "add / remove waypoint";

    /// <summary>The buttons down the side away from the route panel (and clear of the route editor); returns their box.</summary>
    private static Box Toolbar(Box box, double now)
    {
        var o = Plugin.Settings.Options;
        float bpx = Math.Max(15f, box.H / 48f), bh = bpx * 2.3f, gap = bpx * 0.45f;
        var main = new List<(string Text, bool On, Action Act)>();
        if (hidden) main.Add(("TOOLS", false, () => hidden = false));
        else
        {
            main.Add(("TAP: " + (touch.Mode == TapMode.Focus ? "FOCUS" : touch.Mode == TapMode.Destination ? "DESTINATION" : "WAYPOINTS"), touch.Mode != TapMode.Focus, () =>
            {
                touch.Mode = (TapMode)(((int)touch.Mode + 1) % 3);
                Toast(touch.Mode == TapMode.Focus ? "Tap: focus on it (hold, then let go: set destination)"
                    : touch.Mode == TapMode.Destination ? "Tap: set it as the destination"
                    : "Tap: add a waypoint (on one of yours: remove it); drag: place one above or below");
            }));
            main.Add(("FOLLOW SHIP", follow, () =>
            {
                follow = !follow;
                var sn = SidePreview.Snap;
                if (follow && sn.HasShip) scene.Cam.FlyTo(sn.ShipPos, Math.Min(scene.Cam.GoalDistance, MapScene.ShipViewDistance));
                Toast(!follow ? "Follow ship: off" : sn.HasShip ? "Follow ship: on" : "Follow ship: on (no ship yet: sit in a cockpit)");
            }));
            main.Add(("TURN WITH SHIP", Plugin.Settings.FollowHeading, () =>
            {
                Plugin.Settings.FollowHeading = !Plugin.Settings.FollowHeading; heading.Offset = 0; Plugin.SaveSettings();
                Toast(Plugin.Settings.FollowHeading ? "Turn with ship: on (when following)" : "Turn with ship: off");
            }));
            main.Add(("FRAME ROUTE", false, () => { follow = false; scene.FrameDefault(false); }));
            main.Add(("WHOLE SYSTEM", false, () => { follow = false; scene.Cam.FlyTo(V3.Zero, 6e7); }));
            main.Add(("AUTOPILOT", showControls && SidePreview.Path != null, () => { var why = SidePreview.MapAutopilotKey(); if (why != null) Toast(why); else showControls = true; }));
            main.Add(("RE-PLAN", false, () =>
            {
                bool back = SidePreview.Tracker.Skipped.Count > 0; SidePreview.Replan(true);
                Toast("Route planned again from here" + (back ? ", with the detours you removed." : "."));
            }));
            main.Add(("DETOURS", Plugin.Settings.AvoidBodies, () =>
            {
                Plugin.Settings.AvoidBodies = !Plugin.Settings.AvoidBodies; SidePreview.Replan();
                Toast(Plugin.Settings.AvoidBodies ? "Detours round worlds: on" : "Detours off: straight line");
            }));
            bool armed = now < clearArmedUntil;
            main.Add((armed ? "TAP AGAIN: CLEAR" : "CLEAR ROUTE", armed, () =>
            {
                if (now < clearArmedUntil) { clearArmedUntil = 0; Toast(SidePreview.ClearRoute()); }
                else clearArmedUntil = now + 3;
            }));
            main.Add(("LAYERS", layers, () => layers = !layers));
            main.Add((win.Full ? "WINDOW" : "FULL SCREEN", false, () => win.SetFull(!win.Full)));
            main.Add(("HIDE", false, () => { hidden = true; layers = false; }));
        }
        var side = new List<(string Text, bool On, Action Act)>();
        if (layers && !hidden)
        {
            side.Add(("GRID", o.Grid, () => o.Grid = !o.Grid));
            side.Add(("ORBITS", o.Orbits, () => o.Orbits = !o.Orbits));
            side.Add(("SECTORS", o.Sectors, () => o.Sectors = !o.Sectors));
            side.Add(("STATIONS", o.Stations, () => o.Stations = !o.Stations));
            side.Add(("GPS", o.Gps, () => o.Gps = !o.Gps));
            side.Add(("LABELS", o.Labels, () => o.Labels = !o.Labels));
            side.Add(("MOONS", o.Moons, () => o.Moons = !o.Moons));
            side.Add(("THEME: " + MapTheme.Current.Name.ToUpperInvariant(), false, () => Toast(MapScreen.NextTheme())));
        }
        float colW = 0;
        foreach (var b in main) colW = Math.Max(colW, ui.C.TextWidth(b.Text, bpx));
        foreach (var b in side) colW = Math.Max(colW, ui.C.TextWidth(b.Text, bpx));
        colW += bpx * 2.4f;
        // the side away from the route panel; beside the route editor when that stands there
        bool right = !scene.PanelOnRight;
        float top = Math.Max(scene.TitleBottom, box.Y) + gap * 2, edge = gap * 2;
        float x = right ? box.Right - edge - colW : box.X + edge;
        if (controls.W > 0 && controls.X < x + colW && controls.Right > x) x = right ? controls.X - gap - colW : controls.Right + gap;
        float sx = right ? x - gap - colW : x + colW + gap;
        var all = new Box(x, top, colW, 0);
        Action act = null;
        float yy = top;
        foreach (var b in main)
        {
            if (ui.Button(new Box(x, yy, colW, bh), b.Text, bpx, AutopilotPanel.Go, b.On)) act = b.Act;
            yy += bh + gap;
        }
        all.H = yy - top;
        yy = top;
        foreach (var b in side)
        {
            if (ui.Button(new Box(sx, yy, colW, bh), b.Text, bpx, AutopilotPanel.Go, b.On)) act = b.Act;
            yy += bh + gap;
        }
        if (side.Count > 0) { float l = Math.Min(x, sx); all = new Box(l, top, colW * 2 + gap, Math.Max(all.H, yy - top)); }
        act?.Invoke();
        return all;
    }
}
