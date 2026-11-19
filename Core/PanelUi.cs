using System;
using System.Collections.Generic;

namespace SDX2_SystemMap;

/// <summary>
/// A tiny immediate-mode UI for the panels beside Flip and Burn's N list: call <see cref="Input"/> once a frame with
/// the mouse, then draw buttons and sliders; each returns what the mouse did to it this frame. Game-free so the tests
/// can render it.
/// </summary>
public sealed class PanelUi
{
    /// <summary>A controller direction (D-pad or stick) this frame.</summary>
    public enum PadDir { None, Up, Down, Left, Right }

    public IMapCanvas C;
    public float Mx = float.NaN, My = float.NaN;
    /// <summary>Left button held / pressed this frame / released this frame.</summary>
    public bool Down, Pressed, Released;
    /// <summary>The slider being dragged (null = none).</summary>
    public string Dragging;
    private bool dropped;

    public void Input(IMapCanvas c, float mx, float my, bool down, bool pressed, bool released)
    {
        C = c; Mx = mx; My = my; Down = down; Pressed = pressed; Released = released;
        dropped = Dragging != null && !down;
        if (!down) Dragging = null;
        // last frame's controls are where the controller moves; this frame's are listed as they're drawn
        var swap = prev; prev = items; items = swap; items.Clear();
        padAccept = false; padStep = 0;
    }

    // ---- controller focus: the D-pad / stick moves between the controls drawn last frame, A presses the focused one ----

    /// <summary>A control as drawn: where, whether it can be used, whether it's a slider (left / right change it).</summary>
    public struct Item { public Box Box; public bool Enabled, Slider; }
    private List<Item> items = new List<Item>(), prev = new List<Item>();
    /// <summary>The controller's focus: an index in draw order (-1 = none).</summary>
    public int Focus = -1;
    /// <summary>Show the focus and take A (the controller was used last and these controls have it).</summary>
    public bool PadActive;
    private bool padAccept, aWas, aHere;
    private int padStep;

    /// <summary>The controls drawn last frame.</summary>
    public IReadOnlyList<Item> Drawn => prev;
    /// <summary>A usable control has been drawn since the last <see cref="Input"/>.</summary>
    public bool HasControls => First(items) >= 0;

    /// <summary>
    /// The controller this frame, after <see cref="Input"/>: move = a new D-pad / stick push, aDown = A held. A presses the
    /// focused control when it's let go, and only when it went down while these controls had the controller (so the A
    /// that opened them doesn't press anything), as the game's own buttons do. Left / right on a slider change it.
    /// </summary>
    public void Pad(PadDir move, bool aDown)
    {
        if (!PadActive) { aWas = aDown; aHere = false; return; }
        if (Focus >= prev.Count || Focus >= 0 && !prev[Focus].Enabled) Focus = -1;
        if (move != PadDir.None)
        {
            if (Focus >= 0 && prev[Focus].Slider && (move == PadDir.Left || move == PadDir.Right)) padStep = move == PadDir.Left ? -1 : 1;
            else Focus = Focus < 0 ? First(prev) : Next(prev, Focus, move);
        }
        else if (Focus < 0) Focus = First(prev);
        if (aDown && !aWas) aHere = true;
        if (!aDown && aWas && aHere) padAccept = Focus >= 0;
        if (!aDown) aHere = false;
        aWas = aDown;
    }

    /// <summary>The first control that can be used (-1: none).</summary>
    public static int First(IReadOnlyList<Item> list)
    {
        for (int i = 0; i < list.Count; i++) if (list[i].Enabled) return i;
        return -1;
    }

    /// <summary>
    /// The nearest usable control in that direction from control i (by centres; one well in line beats a nearer one off
    /// to the side). None that way: i stays.
    /// </summary>
    public static int Next(IReadOnlyList<Item> list, int i, PadDir dir)
    {
        if (i < 0 || i >= list.Count) return First(list);
        var a = list[i].Box;
        float ax = a.X + a.W / 2, ay = a.Y + a.H / 2;
        int best = i; float bestScore = float.MaxValue;
        for (int j = 0; j < list.Count; j++)
        {
            if (j == i || !list[j].Enabled) continue;
            var b = list[j].Box;
            float dx = b.X + b.W / 2 - ax, dy = b.Y + b.H / 2 - ay;
            float along = dir == PadDir.Up ? -dy : dir == PadDir.Down ? dy : dir == PadDir.Left ? -dx : dir == PadDir.Right ? dx : 0;
            float across = dir == PadDir.Up || dir == PadDir.Down ? Math.Abs(dx) : Math.Abs(dy);
            // in that direction: past the edge of this control (a control in the same row isn't "below" it)
            float edge = dir == PadDir.Up || dir == PadDir.Down ? Math.Min(a.H, b.H) * 0.5f : Math.Min(a.W, b.W) * 0.25f;
            if (along <= edge) continue;
            float score = along + across * 2;
            if (score < bestScore) { bestScore = score; best = j; }
        }
        return best;
    }

    /// <summary>Lists a control as drawn; true when it has the controller's focus.</summary>
    private bool Register(Box b, bool enabled, bool slider)
    {
        items.Add(new Item { Box = b, Enabled = enabled, Slider = slider });
        return PadActive && Focus == items.Count - 1;
    }

    /// <summary>The focus ring: a frame just outside the box.</summary>
    private void Ring(Box b, MapColor col)
    {
        float t = Math.Max(2f, b.H * 0.07f), g = t;
        C.Rect(b.X - g - t, b.Y - g - t, b.W + 2 * (g + t), t, col);
        C.Rect(b.X - g - t, b.Bottom + g, b.W + 2 * (g + t), t, col);
        C.Rect(b.X - g - t, b.Y - g, t, b.H + 2 * g, col);
        C.Rect(b.Right + g, b.Y - g, t, b.H + 2 * g, col);
    }

    public bool Over(Box b) => !float.IsNaN(Mx) && b.Contains(Mx, My);

    /// <summary>A button filling the box; true when clicked. on = drawn as selected.</summary>
    public bool Button(Box b, string text, float px, MapColor accent, bool on = false, bool enabled = true)
    {
        bool focused = Register(b, enabled, false);
        bool hover = enabled && Dragging == null && Over(b);
        PanelStyle.Button(C, b, px, text, accent, hover || focused && enabled, on, enabled);
        if (focused) Ring(b, MapColor.Rgb(0xFFFFFF));
        return hover && Released && !dropped || focused && enabled && padAccept;
    }

    /// <summary>A horizontal slider across the box; returns the (maybe dragged) value, snapped to step.</summary>
    public double Slider(string id, Box b, double v, double min, double max, double step, MapColor accent, bool enabled = true)
    {
        if (max <= min) max = min + 1;
        bool focused = Register(b, enabled, true);
        if (focused && enabled && padStep != 0)
        {
            double st = step > 0 ? Math.Max(step, (max - min) / 40) : (max - min) / 20;
            if (step > 0) st = Math.Round(st / step) * step;
            v = Math.Max(min, Math.Min(max, v + padStep * st));
        }
        if (enabled && Pressed && Over(b.Inflate(b.H * 0.3f))) Dragging = id;
        if (enabled && Dragging == id && !float.IsNaN(Mx))
        {
            double t0 = Math.Max(0, Math.Min(1, (Mx - b.X) / b.W));
            v = min + t0 * (max - min);
            if (step > 0) v = Math.Round(v / step) * step;
        }
        v = Math.Max(min, Math.Min(max, v));
        float t = (float)((v - min) / (max - min));
        float cy = b.Y + b.H / 2, th = Math.Max(3, b.H * 0.16f);
        var col = enabled ? accent : AutopilotPanel.Dim;
        C.Rect(b.X, cy - th / 2, b.W, th, AutopilotPanel.Dim.WithAlpha(0.35f));
        C.Rect(b.X, cy - th / 2, b.W * t, th, col);
        bool hot = Dragging == id || enabled && Over(b) || focused;
        if (focused) Ring(b.Inflate(b.H * 0.15f), MapColor.Rgb(0xFFFFFF));
        C.Disc(b.X + b.W * t, cy, b.H * (hot ? 0.36f : 0.3f), hot ? MapColor.Rgb(0xFFFFFF) : col);
        return v;
    }
}
