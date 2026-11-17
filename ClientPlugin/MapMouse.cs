using System;
using VRage.Input;

namespace SDX2_SystemMap;

/// <summary>
/// The map's mouse controls, shared by the full map and the preview window (while the N list is open and the mouse
/// is over it):
///  - drag: orbit; right-drag: pan; wheel: zoom; click: focus on a world, station or GPS point;
///  - Shift+click: make it Flip and Burn's destination (a waypoint while ADD WAYPOINT is on);
///  - Shift+right-click: add a waypoint there (on one of yours: remove it; on a bend: straighten the leg);
///  - Shift+right-drag: add one above or below the plane (tilt the view);
///  - drag one of your waypoints (left, or Shift+right): raise or lower it;
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

    private bool leftDown, rightDown, moved, rMoved;
    private float lastX, lastY, downX, downY, rDownX, rDownY;

    // a height drag: 0 none, 1 placing a new waypoint (Shift+right-drag), 2 raising / lowering one of yours,
    // 3 bending a leg (its flip point or bend dragged up / down)
    private int hMode;
    private bool hRight, hWarned;
    private string hName;
    private V3 hBase, hPos;

    public bool Busy => leftDown || rightDown || hMode != 0;

    /// <summary>Drops any drag (the map closed or lost the mouse).</summary>
    public void Reset(MapScene scene)
    {
        leftDown = rightDown = false;
        if (hMode != 0) { hMode = 0; if (scene != null) scene.Ghost = null; }
    }

    /// <summary>
    /// One frame of input. active = the mouse may act on the map (it's over the map, not over a panel or button; a
    /// drag that started on the map keeps it while held).
    /// </summary>
    public void Handle(MapScene scene, IMyInput input, float mx, float my, bool active)
    {
        bool left = input.IsLeftMousePressed(), right = input.IsRightMousePressed();
        float dx = mx - lastX, dy = my - lastY;
        if (!active)
        {
            if (hMode != 0 && !(hRight ? right : left)) { hMode = 0; scene.Ghost = null; } // let go off the map: cancelled
            leftDown = left && leftDown; rightDown = right && rightDown;
            lastX = mx; lastY = my;
            return;
        }
        bool shift = input.IsAnyShiftKeyPressed();
        bool newLeft = input.IsNewLeftMousePressed(), newRight = input.IsNewRightMousePressed();
        if (newRight) { rDownX = mx; rDownY = my; rMoved = false; }
        if (right && Math.Abs(mx - rDownX) + Math.Abs(my - rDownY) > 6) rMoved = true;
        if (newLeft) { downX = mx; downY = my; moved = false; }
        if (left && Math.Abs(mx - downX) + Math.Abs(my - downY) > 6) moved = true;

        // height drags: a press on one of your waypoints (drag, or Shift+right-drag), a flip point or bend (bend the
        // leg), or Shift+right on empty space (a new waypoint)
        if (hMode == 0 && newLeft && scene.Hovered is Pick hp && hp.Kind == "Your waypoint" && SidePreview.UserWaypoint(hp.Name, out var hb))
            StartHeight(2, false, hp.Name, hb);
        else if (hMode == 0 && newLeft && scene.Hovered is Pick fp && fp.Leg != null)
            StartHeight(3, false, fp.Leg, SidePreview.LegBend(fp.Leg, out var lb) ? lb : fp.Pos);
        else if (hMode == 0 && newRight && shift)
        {
            if (scene.Hovered is Pick rp && rp.Kind == "Your waypoint" && SidePreview.UserWaypoint(rp.Name, out var rb)) StartHeight(2, true, rp.Name, rb);
            else if (SidePreview.Path is RoutePath path && RouteClick.Place(scene.Cam, path.Points(), mx, my, 12 * scene.Unit, out var nb, out _)) StartHeight(1, true, null, nb);
        }
        if (hMode != 0)
        {
            bool held = hRight ? right : left, dragging = hRight ? rMoved : moved;
            if (dragging)
            {
                if (RouteClick.HeightOnVertical(scene.Cam.Eye, scene.Cam.Ray(mx, my), hBase, out var hp2)) hPos = hp2;
                else if (!hWarned) { hWarned = true; Toast("Tilt the view to set height"); }
                scene.Ghost = hPos; scene.GhostBase = hBase;
            }
            if (!held)
            {
                if (dragging)
                {
                    double rise = hPos.Y - hBase.Y;
                    Toast(hMode == 3 ? SidePreview.BendLeg(hName, hPos) : hMode == 2 ? SidePreview.MoveWaypoint(hName, hPos, rise) : SidePreview.InsertWaypoint(null, hPos));
                    hMode = 0; scene.Ghost = null;
                    leftDown = false; rightDown = false; lastX = mx; lastY = my;
                    return; // the release is used up
                }
                hMode = 0; scene.Ghost = null; // a plain click: handled as before below
            }
        }
        bool hDrag = hMode != 0;

        if (left && leftDown && !hDrag) { scene.Cam.Orbit(-dx * OrbitRate, dy * OrbitRate); if (Turning()) Heading?.Turned(-dx * OrbitRate); }
        if (input.IsNewRightMouseReleased() && !rMoved && shift && rightDown) AddWaypointAt(scene, mx, my);
        if (right && rightDown && !shift && !hDrag && Math.Abs(dx) + Math.Abs(dy) > 0)
        {
            scene.Cam.Pan(dx, dy);
            if (Following()) { Unfollow(); Toast("Follow ship: off (panned)"); }
        }
        if (input.IsNewLeftMouseReleased() && !moved && leftDown && scene.Hovered is Pick p)
        {
            if (shift) { Toast(SidePreview.SetDestination(p.Name, p.Kind, p.Pos, p.Detail)); if (p.Kind != "Your ship") Destination(p); }
            else { if (p.Kind != "Your ship") Unfollow(); scene.Cam.FlyTo(p.Pos, FlyDistance(p)); }
        }
        // a press counts only when it starts here
        leftDown = left && (leftDown || newLeft);
        rightDown = right && (rightDown || newRight);
        lastX = mx; lastY = my;
        int wheel = input.DeltaMouseScrollWheelValue();
        if (wheel != 0) scene.Cam.Zoom(wheel / 120.0);
    }

    private void StartHeight(int mode, bool rightButton, string name, V3 basePos)
    {
        hMode = mode; hRight = rightButton; hName = name; hBase = basePos; hPos = basePos; hWarned = false;
    }

    /// <summary>
    /// Shift+right-click: a waypoint at the click (or, on one of your waypoints, removes it; on a bend, straightens
    /// the leg). On a world, station or GPS point it's that point; near the route's line it's on the line; elsewhere
    /// it's level with the nearest part of the route. It goes into the leg it's nearest.
    /// </summary>
    private void AddWaypointAt(MapScene scene, float x, float y)
    {
        var path = SidePreview.Path;
        if (path == null) { Toast("Set a destination first (Shift+click a point)"); return; }
        if (scene.Hovered is Pick bp && bp.Kind == "Bend") { Toast(SidePreview.StraightenLeg(bp.Leg)); return; }
        if (scene.Hovered is Pick p && p.Kind != "Your ship")
        {
            Toast(SidePreview.RemoveWaypoint(p.Name, p.Pos) ?? SidePreview.InsertWaypoint(p.Name, p.Pos));
            return;
        }
        if (!RouteClick.Place(scene.Cam, path.Points(), x, y, 12 * scene.Unit, out var pos, out _)) { Toast("Can't place a waypoint there: tilt the view"); return; }
        Toast(SidePreview.InsertWaypoint(null, pos));
    }

    /// <summary>A view that fits the picked thing: worlds by their size, everything else close up.</summary>
    public static double FlyDistance(Pick p)
    {
        var w = SystemData.FindWorld(p.Name);
        return w != null ? Math.Max(w.Radius * 6, 2e5) : 1e5;
    }
}
