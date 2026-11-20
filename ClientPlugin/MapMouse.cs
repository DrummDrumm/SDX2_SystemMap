using System;
using VRage.Input;

namespace SDX2_SystemMap;

/// <summary>
/// The map's mouse controls, shared by the full map and the preview window (while the N list is open and the mouse
/// is over it):
///  - drag: orbit; right-drag: pan; wheel: zoom; click: focus on a world, station or GPS point;
///  - Shift+click: make it Flip and Burn's destination (a waypoint while ADD WAYPOINT is on);
///  - Shift+right-click: add a waypoint there (on one of yours: remove it; on a detour: remove it, R brings it back;
///    on a bend: straighten the leg; with no route
///    yet it becomes the destination);
///  - Shift+right-drag: add one above or below the plane (tilt the view);
///  - drag one of your waypoints (or your point in space): guide lines show its up / down line and the X and Z axes
///    through it; the drag follows the line it runs closest to (up / down raises or lowers it, X or Z moves it level
///    along that axis), picked once the drag is PickAfter px from the press and picked again whenever it comes back
///    that near. Space while dragging switches between up / down and level and holds it for that drag;
///    Shift+right-drag: anywhere across the level plane;
///  - drag a flip point or a bend: bend that leg up or down.
/// A press only counts when it starts on the map, so dragging in from the N list or a panel does nothing.
/// </summary>
internal sealed class MapMouse
{
    /// <summary>Says what a click did (a toast in the full map, a notification under the preview).</summary>
    public Action<string> Toast = _ => { };
    /// <summary>Following the ship: read / turned off by panning or focusing elsewhere.</summary>
    public Func<bool> Following = () => false;
    public Action Unfollow = () => { };
    /// <summary>Turn with the ship: a sideways drag turns the view it keeps from there.</summary>
    public HeadingFollow Heading;
    /// <summary>The view turns with the ship (following, with turn-with-ship on).</summary>
    public Func<bool> Turning = () => false;
    /// <summary>After a Shift+click set a destination (the full map opens its autopilot controls).</summary>
    public Action<Pick> Destination = _ => { };
    public double OrbitRate = 0.005;
    /// <summary>How far (px, across + down) a press may wander and still count as a click: more for a finger.</summary>
    public float ClickSlop = 6;

    private bool leftDown, rightDown, moved, rMoved;
    private float lastX, lastY, downX, downY, rDownX, rDownY;

    // a height drag: 0 none, 1 placing a new waypoint (Shift+right-drag), 2 raising / lowering one of yours,
    // 3 bending a leg (its flip point or bend dragged up / down), 4 moving the point in space you set as the destination.
    // hFlat: across the level plane (Shift+right-drag) instead of up / down
    private int hMode;
    private bool hRight, hWarned, hFlat;
    /// <summary>A left-drag of your waypoint or point that hasn't picked up / down or an axis yet (hPick: this drag picks).</summary>
    private bool hAuto, hPick;
    /// <summary>Space switched this drag between up / down and level: it stays as set (no picking from the drag's direction).</summary>
    private bool hForced;
    /// <summary>px from the press before a drag of your point picks its line (and inside which it lets go of it again).</summary>
    private float PickAfter => Math.Max(18, ClickSlop * 2.5f);
    /// <summary>A sideways left-drag: along one level axis only, X or Z (whichever runs most along the drag on screen), keeping the rest.</summary>
    private bool hAxisLock, hAlongZ;
    private string hName;
    private V3 hBase, hPos;

    public bool Busy => leftDown || rightDown || hMode != 0;

    /// <summary>Drops any drag (the map closed or lost the mouse).</summary>
    public void Reset(MapScene scene)
    {
        leftDown = rightDown = false;
        if (hMode != 0) { hMode = 0; if (scene != null) { scene.Ghost = null; scene.GhostGuide = 0; } }
    }

    /// <summary>
    /// One frame of input. active = the mouse may act on the map (it's over the map, not over a panel or button; a
    /// drag that started on the map keeps it while held).
    /// </summary>
    public void Handle(MapScene scene, IMyInput input, float mx, float my, bool active) => Handle(scene, MouseInput.From(input), mx, my, active);

    /// <summary>One frame of input from buttons given directly (the second screen's mouse or touch).</summary>
    public void Handle(MapScene scene, MouseInput input, float mx, float my, bool active)
    {
        bool left = input.Left, right = input.Right;
        float dx = mx - lastX, dy = my - lastY;
        if (!active)
        {
            if (hMode != 0 && !(hRight ? right : left)) { hMode = 0; scene.Ghost = null; scene.GhostGuide = 0; } // let go off the map: cancelled
            leftDown = left && leftDown; rightDown = right && rightDown;
            lastX = mx; lastY = my;
            return;
        }
        bool shift = input.Shift;
        bool newLeft = input.NewLeft, newRight = input.NewRight;
        if (newRight) { rDownX = mx; rDownY = my; rMoved = false; }
        if (right && Math.Abs(mx - rDownX) + Math.Abs(my - rDownY) > ClickSlop) rMoved = true;
        if (newLeft) { downX = mx; downY = my; moved = false; }
        if (left && Math.Abs(mx - downX) + Math.Abs(my - downY) > ClickSlop) moved = true;

        // height drags: a press on one of your waypoints (drag, or Shift+right-drag), a flip point or bend (bend the
        // leg), or Shift+right on empty space (a new waypoint)
        // Shift+right-drag on one of your waypoints (or the point in space you set as the destination) moves it across
        // the plane instead
        if (hMode == 0 && newLeft && scene.Hovered is Pick hp && hp.Kind == "Your waypoint" && SidePreview.UserWaypoint(hp.Name, out var hb))
            { StartHeight(2, false, hp.Name, hb); hAuto = hPick = true; }
        else if (hMode == 0 && newLeft && scene.Hovered is Pick dp && dp.Kind == DestKind && SidePreview.OwnDestination(dp.Name))
            { StartHeight(4, false, dp.Name, dp.Pos); hAuto = hPick = true; }
        else if (hMode == 0 && newLeft && scene.Hovered is Pick fp && fp.Leg != null)
            StartHeight(3, false, fp.Leg, SidePreview.LegBend(fp.Leg, out var lb) ? lb : fp.Pos);
        else if (hMode == 0 && newRight && shift)
        {
            if (scene.Hovered is Pick rp && rp.Kind == "Your waypoint" && SidePreview.UserWaypoint(rp.Name, out var rb)) StartHeight(2, true, rp.Name, rb, flat: true);
            else if (scene.Hovered is Pick rd && rd.Kind == DestKind && SidePreview.OwnDestination(rd.Name)) StartHeight(4, true, rd.Name, rd.Pos, flat: true);
            else if (SidePreview.Path is RoutePath path && RouteClick.Place(scene.Cam, path.Points(), mx, my, 12 * scene.Unit, out var nb, out _)) StartHeight(1, true, null, nb);
            else if (SidePreview.Path == null && !(scene.Hovered is Pick) && FirstPlace(scene, mx, my, out var fb)) StartHeight(1, true, null, fb);
        }
        if (hMode != 0)
        {
            bool held = hRight ? right : left, dragging = hRight ? rMoved : moved;
            // your own point: no line picked while the drag is still near the press (the guide shows all three), and
            // coming back there lets go of the line picked, so a wrong pick is undone without letting go of the button
            if (hPick && dragging)
            {
                float px = mx - downX, py = my - downY;
                bool near = px * px + py * py < PickAfter * PickAfter;
                if (input.Toggle)
                {
                    // Space: up / down <-> level, held for the rest of this drag. Level takes the axis the drag runs
                    // closest to now (X when it can't tell)
                    bool toLevel = !hAuto && !hAxisLock;
                    SideDrag(scene, hBase, px, py, out hAlongZ);
                    hForced = true; hAuto = false; hFlat = hAxisLock = toLevel; hWarned = false;
                    Toast(toLevel ? "Drag: level, along " + (hAlongZ ? "Z" : "X") + " (Space: up / down)" : "Drag: up / down (Space: level)");
                }
                else if (hForced) { }
                else if (near) { hAuto = true; hFlat = hAxisLock = false; hPos = hBase; hWarned = false; }
                else if (hAuto) { hAuto = false; hFlat = hAxisLock = SideDrag(scene, hBase, px, py, out hAlongZ); }
            }
            scene.GhostGuide = !hPick || !held ? 0 : hAuto ? 1 : !hAxisLock ? 2 : hAlongZ ? 4 : 3;
            if (dragging && hPick && hAuto) { scene.Ghost = hBase; scene.GhostBase = hBase; }
            else if (dragging)
            {
                if (hAxisLock ? AlongAxis(scene, mx, my, hBase, hAlongZ, out var hp2)
                    : hFlat ? FlatPlace(scene, mx, my, hBase, out hp2) : RouteClick.HeightOnVertical(scene.Cam.Eye, scene.Cam.Ray(mx, my), hBase, out hp2)) hPos = hp2;
                else if (!hWarned) { hWarned = true; Toast(hAxisLock ? "Turn the view to move it along " + (hAlongZ ? "Z" : "X") : hFlat ? "Tilt the view down to move it across" : "Tilt the view to set height"); }
                scene.Ghost = hPos; scene.GhostBase = hBase;
            }
            if (!held)
            {
                scene.GhostGuide = 0;
                if (dragging && !(hPick && hAuto)) // (let go with no line picked: nothing moves)
                {
                    double rise = hPos.Y - hBase.Y;
                    if (hMode == 1 && SidePreview.Path == null) FirstDestination(new Pick { Name = FirstName, Kind = "Waypoint", Pos = hPos });
                    else Toast(hMode == 4 ? SidePreview.MoveDestination(hName, hBase, hPos, hFlat)
                        : hMode == 3 ? SidePreview.BendLeg(hName, hPos)
                        : hMode == 2 ? SidePreview.MoveWaypoint(hName, hPos, rise, hFlat)
                        : SidePreview.InsertWaypoint(null, hPos));
                    hMode = 0; scene.Ghost = null;
                    leftDown = false; rightDown = false; lastX = mx; lastY = my;
                    return; // the release is used up
                }
                hMode = 0; scene.Ghost = null; // a plain click: handled as before below
            }
        }
        bool hDrag = hMode != 0;

        if (left && leftDown && !hDrag) { scene.Cam.Orbit(-dx * OrbitRate, dy * OrbitRate); if (Turning()) Heading?.Turned(-dx * OrbitRate); }
        if (input.RightReleased && !rMoved && shift && rightDown) AddWaypointAt(scene, mx, my);
        else if (input.RightReleased && !rMoved && rightDown && scene.Hovered is Pick dr && dr.Kind == DestKind) Toast(SidePreview.ClearDestination());
        if (right && rightDown && !shift && !hDrag && Math.Abs(dx) + Math.Abs(dy) > 0)
        {
            scene.Cam.Pan(dx, dy);
            if (Following()) { Unfollow(); Toast("Follow ship: off (panned)"); }
        }
        if (input.LeftReleased && !moved && leftDown && scene.Hovered is Pick p)
        {
            if (shift) { Toast(SidePreview.SetDestination(p.Name, p.Kind, p.Pos, p.Detail)); if (p.Kind != "Your ship") Destination(p); }
            else { if (p.Kind != "Your ship") Unfollow(); scene.Cam.FlyTo(p.Pos, MapScene.FocusDistance(p)); }
        }
        // a press counts only when it starts here
        leftDown = left && (leftDown || newLeft);
        rightDown = right && (rightDown || newRight);
        lastX = mx; lastY = my;
        double notches = input.Notches;
        if (notches != 0) scene.Cam.Zoom(notches);
    }

    /// <summary>Controller, right trigger: what the cursor is on becomes the destination (as Shift+click).</summary>
    public void PadSelect(MapScene scene)
    {
        if (scene.Hovered is Pick p) { Toast(SidePreview.SetDestination(p.Name, p.Kind, p.Pos, p.Detail)); if (p.Kind != "Your ship") Destination(p); }
        else Toast("Nothing under the cursor (LT places a point in space)");
    }

    /// <summary>Controller, A: the view goes to what the cursor is on (as a click).</summary>
    public void PadFocus(MapScene scene)
    {
        if (!(scene.Hovered is Pick p)) return;
        if (p.Kind != "Your ship") Unfollow();
        scene.Cam.FlyTo(p.Pos, MapScene.FocusDistance(p));
    }

    /// <summary>Controller, left trigger: add / remove a waypoint or detour at the cursor (as Shift+right-click).</summary>
    public void PadWaypoint(MapScene scene, float x, float y) => AddWaypointAt(scene, x, y);

    private void StartHeight(int mode, bool rightButton, string name, V3 basePos, bool flat = false)
    {
        hMode = mode; hRight = rightButton; hName = name; hBase = basePos; hPos = basePos; hWarned = false; hFlat = flat; hAuto = hAxisLock = hPick = hForced = false;
    }

    /// <summary>
    /// A drag (dx, dy screen px) of the point at p: which of the three lines through p it runs closest to on screen,
    /// up / down (false) or a level axis (true; alongZ says which). An axis seen nearly end-on isn't a choice, and
    /// looking (nearly) straight down, where height hardly shows, any drag is level.
    /// </summary>
    internal static bool SideDrag(MapScene scene, V3 p, float dx, float dy, out bool alongZ)
    {
        alongZ = false;
        var cam = scene.Cam;
        double d = Math.Max(1, V3.Distance(cam.Eye, p)) * 0.1;
        if (!cam.Project(p, out var bx, out var by, out _) || !cam.Project(p + new V3(0, d, 0), out var ux, out var uy, out _)) return false;
        double vx = ux - bx, vy = uy - by, vl = Math.Sqrt(vx * vx + vy * vy), ml = Math.Sqrt(dx * dx + dy * dy);
        // looking (nearly) straight down: height hardly shows, so any drag moves it across
        double h = 0, xl = 0, zl = 0, xCos = 0, zCos = 0;
        if (cam.Project(p + new V3(d, 0, 0), out var ax, out var ay, out _)) { xl = Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by)); if (xl > 0 && ml > 0) xCos = Math.Abs((ax - bx) * dx + (ay - by) * dy) / (xl * ml); }
        if (cam.Project(p + new V3(0, 0, d), out var cx, out var cy, out _)) { zl = Math.Sqrt((cx - bx) * (cx - bx) + (cy - by) * (cy - by)); if (zl > 0 && ml > 0) zCos = Math.Abs((cx - bx) * dx + (cy - by) * dy) / (zl * ml); }
        h = Math.Max(xl, zl);
        // the level axis that runs most along the drag on screen (one seen end-on can't be dragged along)
        alongZ = zl > 0.15 * h && (zCos > xCos || xl < 0.15 * h);
        if (ml < 1 || vl < 0.2 * h) return true;
        double vCos = Math.Abs(vx * dx + vy * dy) / (vl * ml);
        double level = Math.Max(xl > 0.15 * h ? xCos : 0, zl > 0.15 * h ? zCos : 0);
        return vCos < level;
    }

    private const string DestKind = "Destination";

    /// <summary>
    /// On the X (or Z) axis line through basePos, at the same height: the point on it nearest the mouse ray; false
    /// when the view looks along that axis (the line is end-on) or the point is far off.
    /// </summary>
    private static bool AlongAxis(MapScene scene, float x, float y, V3 basePos, bool z, out V3 pos)
    {
        pos = basePos;
        var eye = scene.Cam.Eye; var r = scene.Cam.Ray(x, y);
        var w0 = basePos - eye;
        double b = z ? r.Z : r.X, c = r.Dot(r), d = z ? w0.Z : w0.X, e = r.Dot(w0), den = c - b * b;
        if (den < 0.02 * c) return false;
        double t = (b * e - c * d) / den;
        pos = z ? new V3(basePos.X, basePos.Y, basePos.Z + t) : new V3(basePos.X + t, basePos.Y, basePos.Z);
        return V3.Distance(eye, pos) < 20 * Math.Max(1, V3.Distance(eye, basePos));
    }

    /// <summary>Across the level plane through basePos (a drag side to side); false when the view is too flat.</summary>
    private static bool FlatPlace(MapScene scene, float x, float y, V3 basePos, out V3 pos)
    {
        var eye = scene.Cam.Eye;
        return RouteClick.PlaneHit(eye, scene.Cam.Ray(x, y), basePos.Y, out pos)
            && V3.Distance(eye, pos) < 20 * Math.Max(1, V3.Distance(eye, basePos));
    }

    /// <summary>
    /// Shift+right-click: a waypoint at the click (or, on one of your waypoints, removes it; on a bend, straightens
    /// the leg). On a world, station or GPS point it's that point; near the route's line it's on the line; elsewhere
    /// it's level with the nearest part of the route. It goes into the leg it's nearest.
    /// </summary>
    private void AddWaypointAt(MapScene scene, float x, float y)
    {
        var path = SidePreview.Path;
        if (path == null)
        {
            // no route yet: the first one is the destination (a world, station or GPS point, or a point in space)
            if (scene.Hovered is Pick fp && fp.Kind != "Bend" && fp.Leg == null) { FirstDestination(fp); return; }
            if (!FirstPlace(scene, x, y, out var first)) { Toast("Can't place a destination there: tilt the view"); return; }
            FirstDestination(new Pick { Name = FirstName, Kind = "Waypoint", Pos = first });
            return;
        }
        if (scene.Hovered is Pick bp && bp.Kind == "Bend") { Toast(SidePreview.StraightenLeg(bp.Leg)); return; }
        if (scene.Hovered is Pick dp && dp.Kind == DestKind) { Toast(SidePreview.ClearDestination()); return; }
        if (scene.Hovered is Pick tp && tp.Kind != null && tp.Kind.StartsWith(MapScene.DetourKind)) { Toast(SidePreview.RemoveDetour(tp.Name, tp.Pos)); return; }
        if (scene.Hovered is Pick p && p.Kind != "Your ship")
        {
            Toast(SidePreview.RemoveWaypoint(p.Name, p.Pos) ?? SidePreview.InsertWaypoint(p.Name, p.Pos));
            return;
        }
        if (!RouteClick.Place(scene.Cam, path.Points(), x, y, 12 * scene.Unit, out var pos, out _)) { Toast("Can't place a waypoint there: tilt the view"); return; }
        Toast(SidePreview.InsertWaypoint(null, pos));
    }

    private const string FirstName = "Waypoint 1";

    /// <summary>Shift+right-click with no route: the point becomes the destination, as Shift+click does.</summary>
    private void FirstDestination(Pick p)
    {
        Toast(SidePreview.SetDestination(p.Name, p.Kind, p.Pos, p.Detail));
        if (p.Kind != "Your ship") Destination(p);
    }

    /// <summary>A point in space for the first click: on the level plane through your ship (or the view's centre).</summary>
    private static bool FirstPlace(MapScene scene, float x, float y, out V3 pos)
    {
        var centre = scene.Snap != null && scene.Snap.HasShip ? scene.Snap.ShipPos : scene.Cam.Target;
        var eye = scene.Cam.Eye;
        // a nearly flat view meets the plane far away: keep it within reach
        return RouteClick.PlaneHit(eye, scene.Cam.Ray(x, y), centre.Y, out pos)
            && V3.Distance(eye, pos) < 20 * Math.Max(1, V3.Distance(eye, centre));
    }

}

/// <summary>The mouse buttons and wheel for one frame of <see cref="MapMouse.Handle(MapScene, MouseInput, float, float, bool)"/>.</summary>
internal struct MouseInput
{
    public bool Left, Right, NewLeft, NewRight, LeftReleased, RightReleased, Shift;
    /// <summary>Space went down: while one of your points is dragged, switches between up / down and level.</summary>
    public bool Toggle;
    /// <summary>Wheel notches this frame (positive: closer).</summary>
    public double Notches;

    public static MouseInput From(IMyInput i) => new MouseInput
    {
        Left = i.IsLeftMousePressed(), Right = i.IsRightMousePressed(), NewLeft = i.IsNewLeftMousePressed(), NewRight = i.IsNewRightMousePressed(),
        LeftReleased = i.IsNewLeftMouseReleased(), RightReleased = i.IsNewRightMouseReleased(), Shift = i.IsAnyShiftKeyPressed(), Toggle = i.IsNewKeyPressed(MyKeys.Space),
        Notches = OrbitCamera.WheelNotches(i.MouseScrollWheelValue(), i.PreviousMouseScrollWheelValue()),
    };
}
