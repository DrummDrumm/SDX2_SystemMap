using System;

namespace SDX2_SystemMap;

/// <summary>
/// A tiny immediate-mode UI for the panels beside Flip and Burn's N list: call <see cref="Input"/> once a frame with
/// the mouse, then draw buttons and sliders; each returns what the mouse did to it this frame. Game-free so the tests
/// can render it.
/// </summary>
public sealed class PanelUi
{
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
    }

    public bool Over(Box b) => !float.IsNaN(Mx) && b.Contains(Mx, My);

    /// <summary>A button filling the box; true when clicked. on = drawn as selected.</summary>
    public bool Button(Box b, string text, float px, MapColor accent, bool on = false, bool enabled = true)
    {
        bool hover = enabled && Dragging == null && Over(b);
        var fill = on ? accent.WithAlpha(0.42f) : hover ? accent.WithAlpha(0.25f) : AutopilotPanel.Panel;
        var edge = enabled ? accent : AutopilotPanel.Dim.WithAlpha(0.5f);
        C.Rect(b.X, b.Y, b.W, b.H, fill);
        C.Rect(b.X, b.Y, b.W, 2, edge);
        C.Rect(b.X, b.Bottom - 2, b.W, 2, edge);
        var col = !enabled ? AutopilotPanel.Dim.WithAlpha(0.6f) : on || hover ? MapColor.Rgb(0xFFFFFF) : accent;
        C.Text(text, b.X + b.W / 2, b.Y + (b.H - px) / 2, px, col, TextAnchor.Center);
        return hover && Released && !dropped;
    }

    /// <summary>A horizontal slider across the box; returns the (maybe dragged) value, snapped to step.</summary>
    public double Slider(string id, Box b, double v, double min, double max, double step, MapColor accent, bool enabled = true)
    {
        if (max <= min) max = min + 1;
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
        bool hot = Dragging == id || enabled && Over(b);
        C.Disc(b.X + b.W * t, cy, b.H * (hot ? 0.36f : 0.3f), hot ? MapColor.Rgb(0xFFFFFF) : col);
        return v;
    }
}
