using System;

namespace SDX2_SystemMap;

/// <summary>
/// Where the side preview window goes: beside Flip and Burn's navigation list, which is a fixed 600 x 500 window in
/// the screen centre (RichHud "high DPI" units: 1 unit = 1 px up to 1080p, scaled up above that).
/// </summary>
public static class SideLayout
{
    public const float FnbListW = 600, FnbListH = 500;

    /// <summary>RichHud's high-DPI scale for a screen height.</summary>
    public static float HudScale(float screenH) => Math.Max(1f, screenH / 1080f);

    /// <summary>The F&amp;B list's screen rectangle.</summary>
    public static Box FnbList(float screenW, float screenH)
    {
        float s = HudScale(screenH);
        return new Box(screenW / 2 - FnbListW * s / 2, screenH / 2 - FnbListH * s / 2, FnbListW * s, FnbListH * s);
    }

    /// <summary>A corner code (TR, TL, BR, BL) from a name like "top right", "tr" or "bottom-left"; null if it isn't one.</summary>
    /// <summary>The next corner clockwise: TR → BR → BL → TL → TR.</summary>
    public static string NextCorner(string c) => c == "TR" ? "BR" : c == "BR" ? "BL" : c == "BL" ? "TL" : "TR";

    public static string Corner(string name)
    {
        var n = (name ?? "").Trim().ToLowerInvariant().Replace("-", " ").Replace("_", " ");
        switch (n)
        {
            case "tr": case "top right": case "topright": case "right top": return "TR";
            case "tl": case "top left": case "topleft": case "left top": return "TL";
            case "br": case "bottom right": case "bottomright": case "right bottom": return "BR";
            case "bl": case "bottom left": case "bottomleft": case "left bottom": return "BL";
        }
        return null;
    }

    /// <summary>
    /// The picture-in-picture window in a corner (TR/TL/BR/BL). scale = size factor (0.5 to 2). The top corners sit below
    /// the autopilot status line; the bottom corners sit above the toolbar.
    /// </summary>
    public static Box Pip(float screenW, float screenH, float scale, string corner)
    {
        float s = Math.Max(0.5f, Math.Min(2f, scale));
        float w = Math.Min(screenW * 0.45f, screenH * 0.36f * s), h = Math.Min(screenH * 0.6f, w * 0.85f);
        float margin = screenH * 0.02f;
        bool left = corner == "TL" || corner == "BL", bottom = corner == "BR" || corner == "BL";
        float x = left ? margin : screenW - w - margin;
        float y = bottom ? screenH - h - screenH * 0.14f : screenH * 0.1f;
        return new Box(x, y, w, h);
    }

    /// <summary>The side window: right of the list (or left with left = true), 70% of the screen tall. scale = user size factor.</summary>
    public static Box Box(float screenW, float screenH, float scale, bool left = false)
    {
        var list = FnbList(screenW, screenH);
        float margin = screenH * 0.022f, gap = screenH * 0.022f;
        float room = left ? list.X - gap - margin : screenW - list.Right - gap - margin;
        float w = Math.Max(240, Math.Min(room, screenH * 0.75f) * Math.Max(0.5f, Math.Min(1.5f, scale)));
        w = Math.Min(w, Math.Max(240, room));
        float h = Math.Min(screenH * 0.7f, w * 1.15f);
        float x = left ? list.X - gap - w : list.Right + gap;
        return new Box(x, (screenH - h) / 2, w, h);
    }
}
