using System;

namespace SDX2_SystemMap;

/// <summary>
/// The destination view's rules, game-free (so the tests can check them): when it shows, where its window goes and how
/// far its lens may zoom.
/// </summary>
public static class DestView
{
    /// <summary>It comes up within this of the route's destination (m).</summary>
    public const double ShowWithin = 30e3;
    /// <summary>A camera block's lens limits (the game's camera blocks: MinFov 0.1, MaxFov 1.0472 rad): the view never
    /// zooms in further than a camera block on your hull could.</summary>
    public const double MinFov = 0.1, MaxFov = 1.04719755;
    /// <summary>The lens with nothing found to frame (the destination is a point, or its grid isn't loaded here).</summary>
    public const double PointFov = 0.5;
    /// <summary>Room round the destination in the picture.</summary>
    public const double Margin = 1.4;

    /// <summary>The range setting's limits (km).</summary>
    public const double MinRangeKm = 1, MaxRangeKm = ShowWithin / 1000;

    public static double ClampRangeKm(double km) => double.IsNaN(km) ? MaxRangeKm : Math.Max(MinRangeKm, Math.Min(MaxRangeKm, km));

    /// <summary>Up within <paramref name="range"/> of the destination (m), never past 30 km.</summary>
    public static bool Shown(bool enabled, bool routeActive, double distance, double range = ShowWithin) =>
        enabled && routeActive && !double.IsNaN(distance) && distance >= 0 && distance <= Math.Min(ShowWithin, double.IsNaN(range) ? ShowWithin : range);

    /// <summary>
    /// How near the ship is to the destination (m), for when the view comes up and the distance it shows: to the point,
    /// or a world's surface (<paramref name="worldRadius"/>, 0 for none), or to where the route stops, whichever is
    /// nearer. A route to a world stops outside its gravity well, often more than 10 km from its centre (Deimos).
    /// </summary>
    public static double Nearness(double toTarget, double toStop, double worldRadius = 0)
    {
        double d = double.IsNaN(toTarget) ? double.MaxValue : Math.Max(0, toTarget - Math.Max(0, worldRadius));
        if (!double.IsNaN(toStop) && toStop >= 0) d = Math.Min(d, toStop);
        return d == double.MaxValue ? double.NaN : d;
    }

    /// <summary>The vertical field of view (rad) that frames something of radius r at distance d, within a camera block's lens.</summary>
    public static double FitFov(double radius, double distance)
    {
        if (double.IsNaN(radius) || radius <= 0 || double.IsNaN(distance) || distance <= 0) return PointFov;
        if (radius * Margin >= distance) return MaxFov;
        double fov = 2 * Math.Atan(radius * Margin / distance);
        return Math.Max(MinFov, Math.Min(MaxFov, fov));
    }

    /// <summary>
    /// The window: 16:9, a corner of the screen, never on the pinned map's corner (it takes the other side's).
    /// </summary>
    public static Box Window(float screenW, float screenH, float scale, string corner, string mapCorner, bool mapPinned)
    {
        string c = SideLayout.Corner(corner) ?? "TL";
        if (mapPinned && c == SideLayout.Corner(mapCorner)) c = c[0] + (c[1] == 'L' ? "R" : "L");
        float s = Math.Max(0.5f, Math.Min(2f, scale));
        float w = Math.Min(screenW * 0.45f, screenH * 0.36f * s), h = w * 9f / 16f;
        float margin = screenH * 0.02f;
        bool left = c[1] == 'L', bottom = c[0] == 'B';
        float x = left ? margin : screenW - w - margin;
        float y = bottom ? screenH - h - screenH * 0.14f : screenH * 0.1f;
        return new Box(x, y, w, h);
    }

    /// <summary>The frame's size unit for a window this tall (the map window's u at the default size).</summary>
    public static float Unit(Box frame) => Math.Max(1f, frame.H / 220f);

    /// <summary>The title bar's height (the map window's: 12u text, 1.35 line, 1.5 lines).</summary>
    public static float TitleH(float u) => 12 * u * 1.35f * 1.5f;

    /// <summary>
    /// Where the picture goes inside the themed frame: under the title bar, inset from the sides and bottom, far enough in
    /// that its square corners stay inside the cut corners (<paramref name="big"/> the top-right's, <paramref name="small"/>
    /// the others'; 0 for a square frame).
    /// </summary>
    public static Box Picture(Box frame, float u, float small, float big)
    {
        float top = TitleH(u);
        float side = Math.Max(Math.Max(2f, 2 * u), Math.Max(big - top, small / 2) + u);
        return new Box(frame.X + side, frame.Y + top, Math.Max(0, frame.W - 2 * side), Math.Max(0, frame.H - top - side));
    }

    /// <summary>
    /// The window round the picture, in the theme's style: the title bar (name and distance), the margins, the frame, and
    /// over the picture the overlay (when <paramref name="picture"/>) or why there is none. With <paramref name="standIn"/>
    /// it draws a stand-in picture first (the settings preview). Nothing is filled over the picture itself.
    /// </summary>
    public static void DrawWindow(IMapCanvas c, Box box, MapTheme th, string name, double dist, FeedOverlay overlay, bool noise, double time,
        bool picture, string why, bool contact, bool standIn = false)
    {
        float u = Unit(box), cutS = MapScene.CutSmall(u), cutB = MapScene.CutBig(u);
        var pic = Picture(box, u, cutS, cutB);
        float px = 12 * u, top = pic.Y - box.Y;
        if (standIn) DestOverlay.Feed(c, pic);
        var bg = th.PanelBg;
        void Fill(Box r, MapColor col) { if (r.W > 0 && r.H > 0) PanelStyle.FillInside(c, r, box, cutS, cutB, col); }
        Fill(new Box(box.X, box.Y, box.W, top), bg);                                   // title bar
        Fill(new Box(box.X, pic.Y, pic.X - box.X, pic.H), bg);                          // left
        Fill(new Box(pic.Right, pic.Y, box.Right - pic.Right, pic.H), bg);              // right
        Fill(new Box(box.X, pic.Bottom, box.W, box.Bottom - pic.Bottom), bg);           // bottom
        Fill(new Box(box.X, pic.Y - Math.Max(1f, u), box.W, Math.Max(1f, u)), th.Accent.WithAlpha(0.6f)); // under the title
        if (picture) DestOverlay.Draw(c, pic, overlay, th, u, name, dist, contact, noise, time);
        else
        {
            c.Rect(pic.X, pic.Y, pic.W, pic.H, new MapColor(0, 0, 0, 200));
            c.Text(why ?? "CONNECTING...", pic.X + pic.W / 2, pic.Y + pic.H / 2 - px / 2, px, th.Dim, TextAnchor.Center);
        }
        MapScene.WindowFrame(c, box, u);
        string title = string.IsNullOrEmpty(name) ? "DESTINATION" : name;
        float ty = box.Y + (top - px) / 2 - u, inset = Math.Max(cutS, 6 * u) + 2 * u;
        c.Text(title, box.X + inset, ty, px, th.Text);
        c.Text(Fmt.Dist(dist), box.Right - Math.Max(cutB * 0.75f, 6 * u) - 2 * u, ty, px, th.Accent, TextAnchor.Right);
        if (picture && !contact)
            c.Text("NO CONTACT", pic.X + pic.W / 2, pic.Y + 16 * u, px * 0.85f, th.Dim, TextAnchor.Center);
    }
}
