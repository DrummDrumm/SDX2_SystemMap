using System;
using System.Collections.Generic;

namespace SDX2_SystemMap;

/// <summary>What a tap does on the second screen's map (its TAP button cycles these).</summary>
public enum TapMode { Focus, Destination, Waypoint }

/// <summary>One frame of touch, turned into the map's mouse terms plus the two-finger moves.</summary>
public struct TouchFrame
{
    /// <summary>Where the finger is (NaN = no finger, or two or more).</summary>
    public float X, Y;
    public bool Left, NewLeft, LeftReleased, Right, NewRight, RightReleased, Shift;
    /// <summary>A second finger came down: drop the one-finger press without a click.</summary>
    public bool Cancel;
    /// <summary>The finger has been held still long enough that letting go sets the destination (this frame only).</summary>
    public bool HoldStarted;
    /// <summary>Two fingers: how far their middle moved (px), how much they spread (ratio, 1 = none) and turned (radians).</summary>
    public float PanX, PanY;
    public double Spread, Twist;
    public bool Any => !float.IsNaN(X) || Spread != 1 || PanX != 0 || PanY != 0 || Twist != 0;
}

/// <summary>
/// Turns a touchscreen's fingers into the map's mouse terms, one frame at a time (game-free, so the tests can drive it):
///  - one finger: a left press (drag = orbit, or drag your waypoint as with the mouse); a tap is a click (focus), or with
///    <see cref="TapMode.Destination"/> a Shift+click (set the destination); held still for <see cref="HoldTime"/> then let
///    go, it's a Shift+click too; with <see cref="TapMode.Waypoint"/> it's a Shift+right press instead (tap: add / remove
///    a waypoint; drag: place one above / below);
///  - two fingers: pinch to zoom, move to pan, twist to turn; a one-finger press under way is dropped, and nothing more
///    happens until every finger is up.
/// A press goes out a frame after the finger lands, so the map has found what is under it first (a tap whose finger is
/// already up still presses, then lets go the frame after).
/// </summary>
public sealed class TouchGestures
{
    public const float TapSlop = 14;
    public const double HoldTime = 0.6;
    public TapMode Mode = TapMode.Focus;

    private struct Finger { public float X, Y; }
    private readonly Dictionary<int, Finger> fingers = new Dictionary<int, Finger>();
    private enum St { Idle, Landed, Pending, Pressing, Multi, WaitUp }
    private St st;
    private int primary;
    private float px, py, sx, sy;
    private double downAt;
    private bool primaryUp, moved, held, right;
    // two fingers: the pair last frame
    private int a = -1, b = -1;
    private float ax, ay, bx, by;

    public bool Touching => fingers.Count > 0 || Live;
    private bool Live => st == St.Landed || st == St.Pending || st == St.Pressing;
    /// <summary>Two fingers are on: a pinch, pan or twist is under way.</summary>
    public bool Pinching => st == St.Multi;

    public void Down(int id, float x, float y, double t)
    {
        fingers[id] = new Finger { X = x, Y = y };
        if (st == St.Idle && fingers.Count == 1)
        {
            st = St.Landed; primary = id; px = sx = x; py = sy = y; downAt = t; primaryUp = moved = held = false;
        }
    }

    public void Move(int id, float x, float y)
    {
        if (!fingers.ContainsKey(id)) return;
        fingers[id] = new Finger { X = x, Y = y };
        if (id == primary && Live)
        {
            px = x; py = y;
            if (Math.Abs(x - sx) + Math.Abs(y - sy) > TapSlop) moved = true;
        }
    }

    public void Up(int id, float x, float y)
    {
        if (!fingers.Remove(id)) return;
        if (id == primary && Live) { px = x; py = y; primaryUp = true; }
    }

    /// <summary>Every finger lifted at once (the window lost them).</summary>
    public void Clear()
    {
        fingers.Clear();
        if (Live) primaryUp = true;
        else st = St.Idle;
    }

    public TouchFrame Step(double t)
    {
        var f = new TouchFrame { X = float.NaN, Y = float.NaN, Spread = 1 };
        switch (st)
        {
            case St.Landed:
                // say where the finger is first, so the map finds what is under it before the press
                if (fingers.Count >= 2) { StartMulti(); break; }
                f.X = px; f.Y = py;
                st = St.Pending;
                break;
            case St.Pending:
                if (fingers.Count >= 2) { StartMulti(); break; }
                // the map has seen where the finger is: press now
                right = Mode == TapMode.Waypoint;
                f.X = px; f.Y = py; f.Shift = right;
                if (right) { f.Right = f.NewRight = true; } else { f.Left = f.NewLeft = true; }
                st = St.Pressing;
                break;
            case St.Pressing:
                f.X = px; f.Y = py; f.Shift = right;
                if (fingers.Count >= 2 && !primaryUp) { f.Cancel = true; StartMulti(); f.X = f.Y = float.NaN; break; }
                if (!held && !moved && !right && t - downAt >= HoldTime) { held = true; f.HoldStarted = true; }
                if (primaryUp)
                {
                    if (right) f.RightReleased = true;
                    else { f.LeftReleased = true; f.Shift = !moved && (held || Mode == TapMode.Destination); }
                    st = fingers.Count > 0 ? St.WaitUp : St.Idle;
                }
                else if (right) f.Right = true; else f.Left = true;
                break;
            case St.Multi:
                if (fingers.Count < 2) { st = fingers.Count == 0 ? St.Idle : St.WaitUp; break; }
                if (!fingers.ContainsKey(a) || !fingers.ContainsKey(b)) { Pair(); break; }
                var fa = fingers[a]; var fb = fingers[b];
                f.PanX = (fa.X + fb.X - ax - bx) / 2; f.PanY = (fa.Y + fb.Y - ay - by) / 2;
                double d0 = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay)), d1 = Math.Sqrt((fb.X - fa.X) * (fb.X - fa.X) + (fb.Y - fa.Y) * (fb.Y - fa.Y));
                if (d0 > 20 && d1 > 20) f.Spread = d1 / d0;
                double t0 = Math.Atan2(by - ay, bx - ax), t1 = Math.Atan2(fb.Y - fa.Y, fb.X - fa.X), dt = t1 - t0;
                while (dt > Math.PI) dt -= 2 * Math.PI;
                while (dt < -Math.PI) dt += 2 * Math.PI;
                if (d0 > 20 && d1 > 20) f.Twist = dt;
                ax = fa.X; ay = fa.Y; bx = fb.X; by = fb.Y;
                break;
            case St.WaitUp:
                if (fingers.Count == 0) st = St.Idle;
                break;
            case St.Idle:
                if (fingers.Count >= 2) StartMulti();
                break;
        }
        return f;
    }

    private void StartMulti() { st = St.Multi; Pair(); }

    /// <summary>The two fingers a pinch follows: the first two down still on the glass.</summary>
    private void Pair()
    {
        a = b = -1;
        foreach (var kv in fingers) { if (a < 0) a = kv.Key; else if (b < 0) { b = kv.Key; break; } }
        if (a >= 0) { ax = fingers[a].X; ay = fingers[a].Y; }
        if (b >= 0) { bx = fingers[b].X; by = fingers[b].Y; }
    }
}
