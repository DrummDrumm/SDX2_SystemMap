using System;
using System.Collections.Generic;

namespace SDX2_SystemMap;

/// <summary>
/// Draws the 3D system map into a box on an IMapCanvas: stars, an ecliptic grid, orbits, the Belt, Nexus sector
/// spheres, worlds at true size, trade stations, GPS markers, the ship with its velocity, and the Flip and Burn route
/// plan (burn / coast / brake legs, flip point, T+ marks, sector borders, close passes). Compact = the side preview
/// window; otherwise the full-screen map with its larger route panel.
/// </summary>
public sealed partial class MapScene
{
    // palette: from the current theme (MapTheme.Current)
    private static MapTheme T => MapTheme.Current;
    private static bool Proto => T.Style == ThemeStyle.Protogen;
    /// <summary>The chart look (hex texture, Sol crosshair, hexagon stations, triangle contacts, boxed labels, chamfered frame): Belter and UNN.</summary>
    private static bool Chart => T.Style == ThemeStyle.Contact || T.Style == ThemeStyle.Naval;
    /// <summary>The nav HUD's lock reticle, destination crosshair, ruler ticks and red Belt arcs: Protogen and UNN.</summary>
    private static bool Lock => T.Style == ThemeStyle.Protogen || T.Style == ThemeStyle.Naval;
    private static bool Naval => T.Style == ThemeStyle.Naval;
    /// <summary>The honeycomb grid: the green Belter chart only (UNN keeps a large square grid).</summary>
    private static bool Hex => T.Style == ThemeStyle.Contact;
    private static MapColor Bg => T.Bg;
    private static MapColor GridCol => T.Grid;
    private static MapColor OrbitCol => T.Orbit;
    private static MapColor TextCol => T.Text;
    private static MapColor DimText => T.Dim;
    private static MapColor PanelBg => T.PanelBg;
    private static MapColor Accent => T.Accent;
    private static MapColor ShipCol => T.Ship;
    public static MapColor ShipColor => T.Ship;
    private static MapColor StationCol => T.Station;
    public static MapColor BurnCol => T.Burn;
    public static MapColor CoastCol => T.Coast;
    public static MapColor BrakeCol => T.Brake;
    private static MapColor BorderCol => T.Border;
    private static MapColor WarnCol => T.Warn;
    public static MapColor DetourCol => T.Detour;
    /// <summary>Your own waypoints (route panel).</summary>
    public static MapColor WaypointCol => T.Waypoint;

    public OrbitCamera Cam = new OrbitCamera();
    public MapSnapshot Snap = new MapSnapshot();
    /// <summary>The planned route: ship → detours → destination (null = none).</summary>
    public RoutePath Path;
    /// <summary>The legs you bent: the point each curves through, by the leg's end name.</summary>
    public IDictionary<string, V3> Bends;
    private RoutePlan Route => Path?.Final;
    public MapOptions Opt = new MapOptions();
    public bool Compact;
    /// <summary>Picture-in-picture (with Compact): a short route summary only, no legend or key help.</summary>
    public bool Mini;
    /// <summary>Mouse position (screen px) for hover highlight and tooltip; NaN = none.</summary>
    public float MouseX = float.NaN, MouseY = float.NaN;
    /// <summary>How near (in UI units) the mouse must be to pick a marker: wider for a finger on a touchscreen.</summary>
    public float HoverReach = 10;
    /// <summary>
    /// Key help for the bottom bar, set by the plugin: one group per line ('\n'), each "LABEL|item · item · ...". Groups
    /// are drawn as their own rows under the legend, wrapped at the " · " between items to fit the width.
    /// </summary>
    public string Help = "";
    /// <summary>The separator between help items.</summary>
    public const string Sep = "  ·  ";

    /// <summary>A waypoint being placed or raised / lowered in the full map (null = none), drawn with a line from GhostBase.</summary>
    public V3? Ghost;
    public V3 GhostBase;
    /// <summary>
    /// While you edit the route the autopilot flies: that route's points as it flies them (start, waypoints, target),
    /// drawn dim and dashed under your edited copy until the changes are sent. Null = no edit open.
    /// </summary>
    public V3[] Original;
    /// <summary>A detour's pick kind starts with this (then the world it goes round).</summary>
    public const string DetourKind = "Detour around ";

    public readonly List<Pick> Picks = new List<Pick>();
    public Pick? Hovered;
    /// <summary>The bottom of the title bar (screen px), set by Draw: room for the plugin's own buttons below it.</summary>
    public float TitleBottom;

    private IMapCanvas c;
    private Box view;
    private Box full;        // the whole window: the map (view) plus, in the side window, the info strip below it
    private float u;         // UI scale: 1 at a 720 px tall map
    public float Unit => u;
    /// <summary>The map's part of the box last drawn: in the side window the info strip below it is left out.</summary>
    public Box MapArea => view;
    private double planeY;   // the grid's height: the ecliptic, or the target's height when far from it
    private readonly List<Box> taken = new List<Box>();
    private readonly List<LabelReq> labels = new List<LabelReq>();
    private Box panelBox;

    private struct LabelReq
    {
        public string Text, Key; public float X, Y, R, Px; public MapColor Col; public int Prio, Order; public bool Boxed, Leader;
    }
    /// <summary>The spot (0 right, 1 left, 2 above, 3 below) each keyed label took last frame: tried first, so it doesn't hop.</summary>
    private readonly Dictionary<string, int> lastSpot = new Dictionary<string, int>();
    private readonly System.Text.StringBuilder widthText = new System.Text.StringBuilder();

    public void Draw(IMapCanvas canvas, Box box)
    {
        c = canvas; view = full = box;
        // the side window: the route info and key bar hang below the map instead of covering its bottom.
        // The window is the map plus last frame's strip (SideLayout.Box adds it), so the scale comes from the map's part
        bool strip = Compact && !Mini;
        SetScale(box.W, strip ? Math.Max(box.H * 0.45f, box.H - Strip) : box.H);
        Strip = strip ? StripHeight(box.W) : 0;
        view = new Box(box.X, box.Y, box.W, Math.Max(box.H * 0.45f, box.H - Strip));
        Cam.Setup(view.X, view.Y, view.W, view.H);
        planeY = Math.Abs(Cam.Target.Y) < Cam.Distance * 0.8 ? 0 : Cam.Target.Y;
        Picks.Clear(); taken.Clear(); labels.Clear(); Hovered = null;

        c.PushClip(view.X, view.Y, view.W, view.H);
        c.Rect(view.X, view.Y, view.W, view.H, Bg);
        Stars();
        if (Opt.Grid) { if (Hex) HexGrid(); else Grid(); }
        if (Lock) ProtoCross();
        if (Chart) ChartBack();
        if (Opt.Orbits) Orbits();
        if (Opt.Sectors) SectorSpheres();
        Worlds();
        AsteroidFields();
        if (Opt.Stations) Stations();
        if (Opt.Gps) GpsMarkers();
        if (Original != null && Original.Length > 1) OriginalRoute();
        if (Path != null) PathLines();
        TrackLines();
        AsteroidMarkers();
        Ship();
        GhostMarker();
        BeltPick();
        FindHover();
        PlaceLabels();
        if (full.H > view.H)
        {
            // the info strip: drawn on its own, so the map's clip doesn't hide it
            c.PopClip();
            c.PushClip(full.X, full.Y, full.W, full.H);
            c.Rect(full.X, view.Bottom, full.W, full.Bottom - view.Bottom, Bg);
        }
        Panels();
        c.PopClip();
        if (Compact) Frame();
    }

    // ------------------------------------------------------------------ helpers

    private bool P(V3 w, out float x, out float y) => Cam.Project(w, out x, out y, out _);

    private bool OnView(float x, float y, float m = 0) => x >= view.X - m && x <= view.Right + m && y >= view.Y - m && y <= view.Bottom + m;

    /// <summary>A world-space segment, clipped to the near plane.</summary>
    private void Seg(V3 a, V3 b, float w, MapColor col)
    {
        double near = Cam.Near * 1.01, da = Cam.Depth(a), db = Cam.Depth(b);
        if (da < near && db < near) return;
        if (da < near) a = V3.Lerp(a, b, (near - da) / (db - da));
        else if (db < near) b = V3.Lerp(b, a, (near - db) / (da - db));
        if (!P(a, out var ax, out var ay) || !P(b, out var bx, out var by)) return;
        if (Math.Max(ax, bx) < view.X || Math.Min(ax, bx) > view.Right || Math.Max(ay, by) < view.Y || Math.Min(ay, by) > view.Bottom) return;
        ScreenLine(ax, ay, bx, by, w, col);
    }

    /// <summary>A screen line, trimmed to a sane length (far-off endpoints would overflow the sprite maths).</summary>
    private void ScreenLine(float ax, float ay, float bx, float by, float w, MapColor col)
    {
        float lim = 4 * Math.Max(view.W, view.H);
        if (Math.Abs(ax - bx) > lim || Math.Abs(ay - by) > lim)
        {
            // pull both ends toward the view centre along the line
            float mx = view.X + view.W / 2, my = view.Y + view.H / 2;
            Clamp(ref ax, ref ay, bx, by, mx, my, lim);
            Clamp(ref bx, ref by, ax, ay, mx, my, lim);
        }
        c.Line(ax, ay, bx, by, w, col);
    }

    private static void Clamp(ref float x, ref float y, float ox, float oy, float mx, float my, float lim)
    {
        float dx = x - mx, dy = y - my;
        if (Math.Abs(dx) <= lim && Math.Abs(dy) <= lim) return;
        float t = 1;
        float ex = x - ox, ey = y - oy;
        // walk from (x,y) toward (ox,oy) until inside the limit box
        for (int i = 0; i < 40 && (Math.Abs(x - mx) > lim || Math.Abs(y - my) > lim); i++) { t *= 0.5f; x = ox + ex * t; y = oy + ey * t; }
    }

    private void Dashed(float ax, float ay, float bx, float by, float w, MapColor col, float dash, float gap)
    {
        float dx = bx - ax, dy = by - ay, len = (float)Math.Sqrt(dx * dx + dy * dy);
        if (len < 1) return;
        if (len > 6000) { ScreenLine(ax, ay, bx, by, w, col.WithAlpha(0.5f)); return; }
        float ux = dx / len, uy = dy / len;
        for (float s = 0; s < len; s += dash + gap)
        {
            float e = Math.Min(len, s + dash);
            c.Line(ax + ux * s, ay + uy * s, ax + ux * e, ay + uy * e, w, col);
        }
    }

    /// <summary>A horizontal circle (orbit, ring) in world space.</summary>
    private void Ring3(V3 centre, double r, int n, float w, MapColor col)
    {
        V3 last = default;
        for (int i = 0; i <= n; i++)
        {
            double a = i * 2 * Math.PI / n;
            var p = centre + new V3(r * Math.Cos(a), 0, r * Math.Sin(a));
            if (i > 0) Seg(last, p, w, col);
            last = p;
        }
    }

    private void ScreenCircle(float x, float y, float r, float w, MapColor col, int n = 48)
    {
        float lx = 0, ly = 0;
        for (int i = 0; i <= n; i++)
        {
            double a = i * 2 * Math.PI / n;
            float qx = x + r * (float)Math.Cos(a), qy = y + r * (float)Math.Sin(a);
            if (i > 0 && (OnView(lx, ly, 2) || OnView(qx, qy, 2))) c.Line(lx, ly, qx, qy, w, col);
            lx = qx; ly = qy;
        }
    }

    private void Diamond(float x, float y, float s, MapColor col) => c.Rect(x - s / 2, y - s / 2, s, s, col, (float)(Math.PI / 4));

    /// <summary>Dashed line from a point down (or up) to the grid plane, with a dot where it lands: shows height.</summary>
    private void Stalk(V3 w, MapColor col)
    {
        if (!Opt.Grid) return;
        var foot = new V3(w.X, planeY, w.Z);
        if (!P(w, out var ax, out var ay) || !P(foot, out var bx, out var by)) return;
        if (Math.Abs(ay - by) < 3 * u) return;
        Dashed(ax, ay, bx, by, Math.Max(1, 0.8f * u), col, 3 * u, 3 * u);
        c.Disc(bx, by, 1.6f * u, col);
    }

    /// <summary>The waypoint being dragged: a ring where it'll go, a line up / down from where it started, its height change.</summary>
    private void GhostMarker()
    {
        if (!Ghost.HasValue) return;
        var g = Ghost.Value; var col = WaypointCol;
        Seg(GhostBase, g, Math.Max(1, 1.4f * u), col.WithAlpha(0.85f));
        if (P(GhostBase, out var bx, out var by)) c.Disc(bx, by, 2.2f * u, col.WithAlpha(0.7f));
        Stalk(g, col.WithAlpha(0.5f));
        if (!P(g, out var x, out var y)) return;
        ScreenCircle(x, y, 9 * u, 2 * u, col, 24);
        c.Disc(x, y, 3 * u, col);
        double h = g.Y - GhostBase.Y;
        double across = Math.Sqrt((g.X - GhostBase.X) * (g.X - GhostBase.X) + (g.Z - GhostBase.Z) * (g.Z - GhostBase.Z));
        string text = across > Math.Abs(h) ? Fmt.Dist(across) + " across" : Math.Abs(h) < 1 ? "level" : Fmt.Dist(Math.Abs(h)) + (h > 0 ? " up" : " down");
        Label(text, x, y, 11 * u, col, 0, 11 * u, true);
    }

    private void Label(string text, float x, float y, float r, MapColor col, int prio, float px = 0, bool boxed = false, string key = null, bool leader = false) =>
        labels.Add(new LabelReq { Text = text, Key = key, X = x, Y = y, R = r, Col = col, Prio = prio, Order = labels.Count, Px = px > 0 ? px : 11 * u, Boxed = boxed, Leader = leader });

    /// <summary>The text's width with every digit as 0, so a label with a changing number (speed, distance) keeps its size
    /// and doesn't shift about as the digits change.</summary>
    private float StableWidth(string text, float px)
    {
        widthText.Clear();
        foreach (char ch in text) widthText.Append(ch >= '0' && ch <= '9' ? '0' : ch);
        return Math.Max(c.TextWidth(text, px), c.TextWidth(widthText.ToString(), px));
    }

    private void AddPick(string name, string kind, string detail, V3 pos, float x, float y, float r, string leg = null) =>
        Picks.Add(new Pick { Name = name, Kind = kind, Detail = detail, Pos = pos, X = x, Y = y, R = r, Leg = leg });

    // ------------------------------------------------------------------ layers

    private void Stars()
    {
        var rnd = new Random(1977);
        for (int i = 0; i < 260; i++)
        {
            double z = rnd.NextDouble() * 2 - 1, a = rnd.NextDouble() * Math.PI * 2, s = Math.Sqrt(1 - z * z);
            var dir = new V3(s * Math.Cos(a), z, s * Math.Sin(a));
            float bright = (float)(0.25 + 0.6 * rnd.NextDouble());
            if (dir.Dot(Cam.Forward) < 0.2) continue;
            if (!P(Cam.Eye + dir * (Cam.Distance * 1e4), out var x, out var y) || !OnView(x, y)) continue;
            c.Disc(x, y, (0.6f + bright) * u * 0.8f, MapColor.White.WithAlpha(bright * 0.7f * T.Stars));
        }
    }

    private static double Nice(double v)
    {
        double e = Math.Pow(10, Math.Floor(Math.Log10(v))), f = v / e;
        return (f < 1.5 ? 1 : f < 3.5 ? 2 : f < 7.5 ? 5 : 10) * e;
    }

    private double gridStep;
    /// <summary>The bottom bar's top last frame (the chart's 180 bearing sits above it).</summary>
    private float barTop;

    private void Grid()
    {
        gridStep = GridStepFor(Cam.Distance);
        if (Naval) { LargeGrid(); return; }
        const int n = 10;
        double cx = Math.Round(Cam.Target.X / gridStep) * gridStep, cz = Math.Round(Cam.Target.Z / gridStep) * gridStep;
        float w = Math.Max(1f, 0.7f * u);
        for (int i = -n; i <= n; i++)
        {
            bool major = Math.Abs(Math.Round((cx / gridStep + i)) % 5) < 0.5;
            float fade = 1 - Math.Abs(i) / (float)(n + 1);
            var colX = GridCol.WithAlpha((major ? 0.42f : 0.2f) * fade);
            Seg(new V3(cx + i * gridStep, planeY, cz - n * gridStep), new V3(cx + i * gridStep, planeY, cz + n * gridStep), w, colX);
            bool majorZ = Math.Abs(Math.Round((cz / gridStep + i)) % 5) < 0.5;
            var colZ = GridCol.WithAlpha((majorZ ? 0.42f : 0.2f) * fade);
            Seg(new V3(cx - n * gridStep, planeY, cz + i * gridStep), new V3(cx + n * gridStep, planeY, cz + i * gridStep), w, colZ);
        }
    }

    /// <summary>
    /// UNN plotting chart: a large square grid, each square two grid steps across, drawn as bright chart lines with a
    /// faint line halfway between them, fading toward its edges.
    /// </summary>
    private void LargeGrid()
    {
        gridStep = Nice(gridStep * 2);
        const int n = 8;
        double cx = Math.Round(Cam.Target.X / gridStep) * gridStep, cz = Math.Round(Cam.Target.Z / gridStep) * gridStep, ext = (n + 0.5) * gridStep;
        float w = Math.Max(1f, 1.1f * u), wf = Math.Max(1f, 0.7f * u);
        for (int i = -n; i <= n; i++)
        {
            float fade = 1 - Math.Abs(i) / (float)(n + 1.5f);
            var col = MapColor.Lerp(GridCol, OrbitCol, 0.45f).WithAlpha(0.6f * fade);
            Seg(new V3(cx + i * gridStep, planeY, cz - ext), new V3(cx + i * gridStep, planeY, cz + ext), w, col);
            Seg(new V3(cx - ext, planeY, cz + i * gridStep), new V3(cx + ext, planeY, cz + i * gridStep), w, col);
            if (i == n) continue;
            var half = GridCol.WithAlpha(0.22f * fade);
            double h = (i + 0.5) * gridStep;
            Seg(new V3(cx + h, planeY, cz - ext), new V3(cx + h, planeY, cz + ext), wf, half);
            Seg(new V3(cx - ext, planeY, cz + h), new V3(cx + ext, planeY, cz + h), wf, half);
        }
    }

    /// <summary>The contact chart's hex size across the flat sides: half a grid step.</summary>
    private double HexWidth => gridStep / 2;

    /// <summary>
    /// Contact chart: the grid as a honeycomb on the grid plane (the theme mockup's hex pattern), each hex half a grid step
    /// across its flat sides, centred on the camera's target and fading toward its edges like the square grid.
    /// </summary>
    private void HexGrid()
    {
        gridStep = GridStepFor(Cam.Distance);
        const int n = 14;
        double wd = HexWidth, r = wd / Math.Sqrt(3), dz = r * 1.5;
        int row0 = (int)Math.Round(Cam.Target.Z / dz), col0 = (int)Math.Round(Cam.Target.X / wd);
        float w = Math.Max(1f, 0.9f * u);
        // pointy hexes: corners at 30 + 60k degrees; each hex draws three edges in a row, its neighbours the other three
        var cs = new V3[7];
        for (int k = 0; k < 7; k++) { double a = (30 + 60 * k) * Math.PI / 180; cs[k] = new V3(r * Math.Cos(a), 0, r * Math.Sin(a)); }
        for (int j = -n; j <= n; j++)
        {
            int row = row0 + j;
            double cz = row * dz, off = (row & 1) != 0 ? wd / 2 : 0;
            for (int i = -n; i <= n; i++)
            {
                var centre = new V3((col0 + i) * wd + off, planeY, cz);
                float d = Math.Max(Math.Abs(i), Math.Abs(j)) / (float)(n + 1), fade = 1 - d * d;
                var col = OrbitCol.WithAlpha(0.34f * fade);
                for (int k = 0; k < 3; k++) Seg(centre + cs[k], centre + cs[k + 1], w, col);
            }
        }
    }

    private void Orbits()
    {
        float w = Math.Max(1f, 0.8f * u);
        int oi = 0;
        foreach (var r in SystemData.OrbitRadii())
        {
            if (T.Style == ThemeStyle.Belter) DashedRing3(V3.Zero, r, 240, w, OrbitCol.WithAlpha(0.45f), 5, 2);
            else if (Chart && oi % 2 == 0) DashedRing3(V3.Zero, r, 240, w, OrbitCol.WithAlpha(0.5f), 4, 3);
            else if (Chart) Ring3(V3.Zero, r, 160, Math.Max(1f, 1.1f * u), OrbitCol.WithAlpha(0.6f));
            else Ring3(V3.Zero, r, 160, w, OrbitCol.WithAlpha(T.Hud ? 0.5f : T.Style == ThemeStyle.Naval || Proto ? 0.42f : 0.32f));
            if (T.Hud) HudRing(r, oi);
            else if (T.Style == ThemeStyle.Naval) NavalRing(r);
            else if (T.Style == ThemeStyle.Belter) BelterRing(r, oi);
            oi++;
        }
        if (T.Style == ThemeStyle.Naval) BearingSpokes();
        LocalOrbits();
        var belt = SystemData.Sectors[SystemData.Sectors.Length - 2];
        var bc = T.SectorColor(belt.Color);
        BeltTorus3D(bc);
        if (Lock) BeltArcs(belt.Radius);
        if (Opt.Labels && Cam.Distance > 4e6 && P(new V3(belt.Radius, 0, 0).Flat + V3.Zero, out var x, out var y) && OnView(x, y))
            Label("DX6 THE BELT", x, y, 2, bc.WithAlpha(0.8f), 9, 10 * u);
    }

    private void SectorSpheres()
    {
        foreach (var s in SystemData.Sectors)
        {
            if (s.Shape != SystemData.SectorShape.Sphere) continue;
            double depth = Cam.Depth(s.Center);
            if (depth < Cam.Near) continue;
            if (!P(s.Center, out var x, out var y)) continue;
            float r = (float)(s.Radius * Cam.PixelsPerMetre(depth));
            if (r < 6 || r > 6 * Math.Max(view.W, view.H)) continue;
            if (x + r < view.X || x - r > view.Right || y + r < view.Y || y - r > view.Bottom) continue;
            var col = T.SectorColor(s.Color);
            if (r < 3000) c.Disc(x, y, r, col.WithAlpha(0.045f));
            ScreenCircle(x, y, r, Math.Max(1f, 1f * u), col.WithAlpha(0.5f), r > 400 ? 96 : 48);
            Ring3(s.Center, s.Radius, 64, Math.Max(1f, 0.7f * u), col.WithAlpha(0.16f));
            if (Opt.Labels && r > 26 * u) Label(s.Dx + " " + s.Name.ToUpperInvariant(), x, y - r, 2, col.WithAlpha(0.85f), 8, 10 * u);
        }
    }

    private void Worlds()
    {
        var items = new List<(double Depth, SystemData.World W, float X, float Y, float R)>();
        foreach (var w in SystemData.Worlds)
        {
            double depth = Cam.Depth(w.Pos);
            if (depth < Cam.Near || !P(w.Pos, out var x, out var y)) continue;
            float tr = (float)(w.Radius * Cam.PixelsPerMetre(depth));
            float r = Math.Max(w.IsPlanet ? (w.Name == "Sol" ? 7 : 4.5f) * u : 2.2f * u, tr);
            if (!OnView(x, y, r + 40)) continue;
            if (!w.IsPlanet && !w.IsAsteroid)
            {
                if (!Opt.Moons) continue;
                var parent = SystemData.FindWorld(w.Parent);
                if (parent != null && P(parent.Pos, out var px, out var py))
                {
                    float pr = Math.Max(4.5f * u, (float)(parent.Radius * Cam.PixelsPerMetre(Cam.Depth(parent.Pos))));
                    float sep = (float)Math.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
                    if (sep < pr + 7 * u) continue;
                }
            }
            if (w.IsAsteroid && Cam.Distance > 3.5e7) continue;
            items.Add((depth, w, x, y, r));
        }
        items.Sort((a, b) => b.Depth.CompareTo(a.Depth)); // far first
        P(new V3(-19187, -19187, -19187), out var sx, out var sy);
        foreach (var it in items)
        {
            var w = it.W;
            if (w.IsPlanet && w.Name != "Sol") Stalk(w.Pos, w.Color.WithAlpha(0.35f));
            if (w.Name == "Sol")
            {
                if (T.Hud)
                {
                    // a hot glow and rings round it
                    c.Disc(it.X, it.Y, it.R * 7f, MapColor.Rgb(0xFF6A00).WithAlpha(0.05f));
                    c.Disc(it.X, it.Y, it.R * 4.5f, MapColor.Rgb(0xFF8A10).WithAlpha(0.09f));
                    c.Disc(it.X, it.Y, it.R * 2.6f, MapColor.Rgb(0xFFB030).WithAlpha(0.2f));
                    ScreenCircle(it.X, it.Y, it.R * 3.4f, Math.Max(1f, 1.2f * u), Accent.WithAlpha(0.7f), 64);
                    ScreenCircle(it.X, it.Y, it.R * 3.9f, Math.Max(1f, 0.8f * u), T.Fine.WithAlpha(0.35f), 64);
                }
                if (Naval)
                {
                    // the chart's thick centre ring with a fine one outside, gold plotting ticks on it
                    c.Disc(it.X, it.Y, it.R * 3.4f, OrbitCol.WithAlpha(0.06f));
                    ScreenCircle(it.X, it.Y, it.R * 2.6f, Math.Max(2f, 3.5f * u), OrbitCol.WithAlpha(0.9f), 64);
                    ScreenCircle(it.X, it.Y, it.R * 3.4f, Math.Max(1f, 1f * u), OrbitCol.WithAlpha(0.55f), 64);
                    Reticle(it.X, it.Y, it.R * 3.4f, T.Trim.WithAlpha(0.85f));
                }
                else if (Proto)
                {
                    // a magenta glow inside a cyan ring and a fine dashed one
                    c.Disc(it.X, it.Y, it.R * 4.5f, T.Trim.WithAlpha(0.06f));
                    c.Disc(it.X, it.Y, it.R * 2.6f, T.Trim.WithAlpha(0.14f));
                    ScreenCircle(it.X, it.Y, it.R * 3f, Math.Max(1f, 1.2f * u), Accent.WithAlpha(0.6f), 64);
                    DashedCircle(it.X, it.Y, it.R * 3.6f, Math.Max(1f, 0.8f * u), T.Fine.WithAlpha(0.35f), 48);
                }
                else if (Chart)
                {
                    // the contact plot's centre mark: a thick ring and a fine one outside it
                    c.Disc(it.X, it.Y, it.R * 3.4f, OrbitCol.WithAlpha(0.06f));
                    ScreenCircle(it.X, it.Y, it.R * 2.6f, Math.Max(2f, 3.5f * u), OrbitCol.WithAlpha(0.9f), 64);
                    ScreenCircle(it.X, it.Y, it.R * 3.4f, Math.Max(1f, 1f * u), OrbitCol.WithAlpha(0.55f), 64);
                }
                else if (T.Style == ThemeStyle.Belter)
                {
                    ScreenCircle(it.X, it.Y, it.R * 2.4f, Math.Max(1.5f, 2f * u), Accent.WithAlpha(0.85f), 6);
                    ScreenCircle(it.X, it.Y, it.R * 3.1f, Math.Max(1f, 0.9f * u), T.Trim.WithAlpha(0.5f), 6);
                }
                c.Disc(it.X, it.Y, it.R * 3.2f, w.Color.WithAlpha(0.08f));
                c.Disc(it.X, it.Y, it.R * 1.8f, w.Color.WithAlpha(0.18f));
                c.Disc(it.X, it.Y, it.R, w.Color);
            }
            else if (T.Hud && w.IsPlanet)
            {
                // bright, in their own colours: a shaded night side and a lit day side toward Sol
                float r = Math.Max(it.R, 6 * u);
                c.Disc(it.X, it.Y, r * 1.5f, w.Color.WithAlpha(0.12f));
                c.Disc(it.X, it.Y, r, MapColor.Lerp(w.Color, MapColor.Black, 0.45f));
                float dx = sx - it.X, dy = sy - it.Y, d = (float)Math.Sqrt(dx * dx + dy * dy);
                if (d > 1 && !float.IsNaN(d)) c.Disc(it.X + dx / d * r * 0.18f, it.Y + dy / d * r * 0.18f, r * 0.8f, w.Color);
                else c.Disc(it.X, it.Y, r * 0.9f, w.Color);
                c.Disc(it.X + dx / Math.Max(d, 1) * r * 0.4f, it.Y + dy / Math.Max(d, 1) * r * 0.4f, r * 0.25f, MapColor.Lerp(w.Color, MapColor.White, 0.5f).WithAlpha(0.7f));
            }
            else if (it.R > 4 * u)
            {
                c.Disc(it.X, it.Y, it.R, w.Color.WithAlpha(0.95f));
                c.Disc(it.X, it.Y, it.R * 0.9f, MapColor.Lerp(Bg, w.Color, 0.22f));
                // the day side: a brighter disc nudged toward Sol
                float dx = sx - it.X, dy = sy - it.Y, d = (float)Math.Sqrt(dx * dx + dy * dy);
                if (d > 1 && !float.IsNaN(d)) c.Disc(it.X + dx / d * it.R * 0.16f, it.Y + dy / d * it.R * 0.16f, it.R * 0.74f, MapColor.Lerp(Bg, w.Color, 0.5f));
            }
            else c.Disc(it.X, it.Y, it.R, w.Color);
            if ((T.Hud || Proto) && w.IsPlanet && w.Name != "Sol") ScreenCircle(it.X, it.Y, Math.Max(it.R, 6 * u) + 4.5f * u, Math.Max(1f, 1f * u), Accent.WithAlpha(0.6f), 32);
            if (Chart && w.IsPlanet && w.Name != "Sol")
            {
                // a ring and a dashed sphere of influence round each planet (UNN: gold plotting ticks on the ring)
                float rr = Math.Max(it.R, 4.5f * u);
                ScreenCircle(it.X, it.Y, rr + 5 * u, Math.Max(1f, 1f * u), OrbitCol.WithAlpha(0.8f), 32);
                DashedCircle(it.X, it.Y, rr + 15 * u, Math.Max(1f, 0.9f * u), OrbitCol.WithAlpha(0.5f), 40);
                if (Naval) Reticle(it.X, it.Y, rr + 5 * u, T.Trim.WithAlpha(0.85f));
            }
            if (T.Style == ThemeStyle.Belter && (w.IsPlanet && w.Name != "Sol" || w.IsAsteroid && it.R > 3 * u))
                ScreenCircle(it.X, it.Y, Math.Max(it.R, 4.5f * u) + 5 * u, Math.Max(1f, 1.3f * u), (w.IsPlanet ? T.Trim : Accent).WithAlpha(0.75f), 6);
            string kind = w.IsPlanet ? (w.Name == "Sol" ? "Star" : "Planet") : w.IsAsteroid ? "Asteroid" : "Moon of " + w.Parent;
            AddPick(w.Name, kind, SystemData.SectorAt(w.Pos).Dx, w.Pos, it.X, it.Y, it.R);
            if (Opt.Labels) Label(w.Name, it.X, it.Y, it.R, w.IsPlanet ? MapColor.Lerp(w.Color, TextCol, 0.4f) : DimText, w.IsPlanet ? 5 : 11, (w.IsPlanet ? 12 : 10) * u);
        }
    }

    private void Stations()
    {
        foreach (var s in SystemData.Stations)
        {
            bool dest = Route != null && V3.Distance(Route.Target, s.Pos) < 50e3;
            if (!dest && Cam.Distance > 6e7) continue;
            if (!P(s.Pos, out var x, out var y) || !OnView(x, y, 10)) continue;
            var col = dest ? BrakeCol : StationCol;
            if (s.Above > 0) { GroundFacility(s, x, y, col, dest); continue; }
            if (Chart) { c.Disc(x, y, 6 * u, Bg); Hexagon(x, y, 7 * u, Math.Max(1f, 1.5f * u), col); }
            else
            {
                Diamond(x, y, 6.5f * u, col);
                Diamond(x, y, 3f * u, Bg);
            }
            AddPick(s.Name, "Trade station", s.Gps, s.Pos, x, y, 5 * u);
            if (Opt.Labels && (dest || Cam.Distance < 6e6)) Label(s.Name, x, y, 5 * u, col, dest ? 2 : 7, 10 * u);
        }
    }

    /// <summary>
    /// A station on a world's ground: a square pad on a ground bar (level on screen), and a dashed line straight up to
    /// its approach point (where routes to it stop), marked with a ring.
    /// </summary>
    private void GroundFacility(SystemData.Station s, float x, float y, MapColor col, bool dest)
    {
        var world = RouteExport.WorldUnder(s.Pos, 100e3);
        float px = 6.5f * u;
        c.Rect(x - px / 2, y - px / 2, px, px, col, 0);
        c.Rect(x - px * 0.22f, y - px * 0.22f, px * 0.44f, px * 0.44f, Bg, 0);
        c.Rect(x - px * 1.1f, y + px * 0.75f, px * 2.2f, Math.Max(1.2f, 1.4f * u), col, 0);
        if (world != null)
        {
            var top = s.Pos + (s.Pos - world.Pos).Normalized * s.Above;
            if (P(top, out var tx, out var ty) && Math.Abs(tx - x) + Math.Abs(ty - y) > 10 * u)
            {
                Dashed(x, y, tx, ty, Math.Max(1f, 1f * u), col.WithAlpha(0.6f), 4 * u, 3 * u);
                ScreenCircle(tx, ty, 3.5f * u, Math.Max(1f, 1.2f * u), col);
                AddPick(s.Name + " approach", "Approach", "Routes to " + s.Name + " stop here, " + Fmt.Dist(s.Above) + " straight above it", top, tx, ty, 4 * u);
            }
        }
        AddPick(s.Name, "Ground facility", s.Gps + (world != null ? "  on " + world.Name + "'s ground" : "") + "  (routes stop " + Fmt.Dist(s.Above) + " above)", s.Pos, x, y, 5 * u);
        if (Opt.Labels && (dest || Cam.Distance < 6e6)) Label(s.Name, x, y, 5 * u, col, dest ? 2 : 7, 10 * u);
    }

    private void GpsMarkers()
    {
        foreach (var m in Snap.Gps)
        {
            if (!P(m.Pos, out var x, out var y) || !OnView(x, y, 10)) continue;
            Stalk(m.Pos, m.Color.WithAlpha(0.3f));
            if (Chart) Triangle(x, y, 6 * u, 0, -1, m.Color);
            else
            {
                c.Disc(x, y, 3.6f * u, m.Color);
                c.Disc(x, y, 1.8f * u, Bg);
            }
            AddPick(m.Name, "GPS", "", m.Pos, x, y, 4 * u);
            if (Opt.Labels && Cam.Distance < 3e6) Label(m.Name, x, y, 4 * u, m.Color, 12, 10 * u);
        }
    }

    public static MapColor HazardCol => T.Hazard;

    private void AsteroidMarkers()
    {
        foreach (var m in Snap.Asteroids)
        {
            if (!P(m.Pos, out var x, out var y) || !OnView(x, y, 10)) continue;
            Diamond(x, y, 9 * u, HazardCol);
            Diamond(x, y, 4.5f * u, Bg);
            AddPick(m.Name, "Asteroid", "on the route", m.Pos, x, y, 6 * u);
            Label(m.Name, x, y, 6 * u, HazardCol, 30, 11 * u);
        }
        V3 prev = Snap.ShipPos;
        foreach (var d in Snap.Dodges)
        {
            if (Snap.HasShip) Seg(prev, d, 1.4f * u, HazardCol.WithAlpha(0.7f));
            prev = d;
            if (P(d, out var x, out var y) && OnView(x, y, 10)) ScreenCircle(x, y, 4 * u, 1.4f * u, HazardCol);
        }
    }

    private void Ship()
    {
        if (!Snap.HasShip || !P(Snap.ShipPos, out var x, out var y)) return;
        Stalk(Snap.ShipPos, ShipCol.WithAlpha(0.6f));
        double speed = Snap.ShipVel.Length;
        var shape = ShipIcons.Pick(Snap.ShipClass, RealShipIcons);
        float r = shape != null ? 13 * u * shape.Size : 9 * u;
        if (speed > 1)
        {
            // where it's going: the theme's trajectory marker along the velocity on screen, out from the icon
            double m = 10 * u / Cam.PixelsPerMetre(Cam.Depth(Snap.ShipPos));
            if (P(Snap.ShipPos + Snap.ShipVel / speed * m, out var vx, out var vy))
            {
                float dx = vx - x, dy = vy - y, l = (float)Math.Sqrt(dx * dx + dy * dy);
                // shorter as the velocity turns toward or away from the view
                if (l > 0.5f) CourseMarker(x, y, dx / l, dy / l, r + 3 * u, 40 * u * Math.Max(0.35f, Math.Min(1, l / (10 * u))), ShipCol);
            }
        }
        var (nx, ny, along) = ShipNose(x, y);
        if (shape != null)
        {
            // the class's silhouette, turned to the way the ship faces
            c.Disc(x, y, r * 1.05f, ShipCol.WithAlpha(0.1f));
            ShipShape(shape, x, y, r, nx, ny, along, ShipCol.WithAlpha(0.5f), ShipCol);
        }
        else if (Chart)
        {
            // a contact triangle pointing the way the ship faces
            c.Disc(x, y, 11 * u, ShipCol.WithAlpha(0.14f));
            Triangle(x, y, 9 * u, nx, ny, ShipCol);
        }
        else
        {
            c.Disc(x, y, 9 * u, ShipCol.WithAlpha(0.18f));
            ScreenCircle(x, y, 7 * u, Math.Max(1f, 1.4f * u), ShipCol, 24);
            c.Disc(x, y, 3 * u, ShipCol);
        }
        AddPick(Snap.ShipName, "Your ship", SystemData.SectorAt(Snap.ShipPos).Dx, Snap.ShipPos, x, y, 8 * u);
        Label(MapSnapshot.ShortShipName(Snap.ShipName, Snap.FactionTag, Snap.ShipClass) + (speed > 1 ? "  " + Fmt.Speed(speed) : ""), x, y, 9 * u, ShipCol, 1, 11 * u, true, "ship");
    }

    public static MapColor TrackCol => T.Track;
    public static MapColor PlanCol => T.Plan;

    /// <summary>
    /// The ship's actual track since the route was set, dashed, ending at the ship; under it a thin line for the route
    /// as set (start → every detour and waypoint, passed ones too → where it's heading now), so you can see how far the
    /// ship strayed and how it corrects back.
    /// </summary>
    private void TrackLines()
    {
        if (!Snap.HasShip) return;
        var pr = Snap.Prediction;
        if (pr.Count > 1)
        {
            // projected: from the ship to where it ends up (dashed when there's no route curve drawn for it)
            if (!(Path != null && curved)) DashedTrack(pr, pr[pr.Count - 1], Math.Max(1f, 1.5f * u), TrackCol.WithAlpha(0.55f), 4 * u, 6 * u);
            if (P(pr[pr.Count - 1], out var ex, out var ey) && OnView(ex, ey, 6))
            {
                var ec = Snap.PredictionArrives ? TrackCol : WarnCol;
                ScreenCircle(ex, ey, 4 * u, Math.Max(1f, 1.3f * u), ec.WithAlpha(0.85f), 16);
                if (!Snap.PredictionArrives) Label("projected end", ex, ey, 4 * u, WarnCol, 40, 9 * u);
            }
        }
        var t = Snap.Track;
        if (t.Count == 0) return;
        if (Path != null)
        {
            V3 prev = t[0];
            int upto = Math.Min(Path.Next, Path.Waypoints.Count);
            for (int i = 0; i <= upto; i++)
            {
                var q = i < Path.Waypoints.Count ? Path.Waypoints[i].Pos : Path.Target;
                SplitSeg(prev, q, Math.Max(1f, 1.1f * u), PlanCol.WithAlpha(0.5f));
                prev = q;
            }
        }
        DashedTrack(t, Snap.ShipPos, Math.Max(1.2f, 1.8f * u), TrackCol.WithAlpha(0.9f), 6 * u, 4 * u);
        if (P(t[0], out var sx, out var sy) && OnView(sx, sy, 4)) ScreenCircle(sx, sy, 3 * u, Math.Max(1f, 1.2f * u), TrackCol.WithAlpha(0.8f), 16);
    }

    /// <summary>A long world segment drawn in pieces (keeps the near-plane clip and perspective right).</summary>
    private void SplitSeg(V3 a, V3 b, float w, MapColor col)
    {
        const int n = 16;
        for (int i = 0; i < n; i++) Seg(V3.Lerp(a, b, i / (double)n), V3.Lerp(a, b, (i + 1) / (double)n), w, col);
    }

    /// <summary>A dashed world polyline whose dash pattern runs on across its points (so short steps don't turn solid).</summary>
    private void DashedTrack(List<V3> pts, V3 end, float w, MapColor col, float dash, float gap)
    {
        float period = dash + gap, phase = 0, lx = 0, ly = 0;
        bool have = false;
        double near = Cam.Near * 1.01;
        float lim = 4 * Math.Max(view.W, view.H);
        for (int i = 0; i <= pts.Count; i++)
        {
            var q = i < pts.Count ? pts[i] : end;
            if (Cam.Depth(q) < near || !P(q, out var x, out var y)) { have = false; continue; }
            if (!have) { lx = x; ly = y; have = true; continue; }
            float dx = x - lx, dy = y - ly, len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 1.5f && i < pts.Count) continue; // merge tiny steps
            bool visible = !(Math.Max(lx, x) < view.X || Math.Min(lx, x) > view.Right || Math.Max(ly, y) < view.Y || Math.Min(ly, y) > view.Bottom);
            if (visible && len < lim)
            {
                float ux = dx / len, uy = dy / len;
                for (float s = -phase; s < len; s += period)
                {
                    float a = Math.Max(0, s), e = Math.Min(len, s + dash);
                    if (e > a) c.Line(lx + ux * a, ly + uy * a, lx + ux * e, ly + uy * e, w, col);
                }
            }
            else if (visible) ScreenLine(lx, ly, x, y, w, col.WithAlpha(0.5f));
            phase = (phase + len) % period;
            lx = x; ly = y;
        }
    }

    /// <summary>The route is drawn along the projected curve (the path the ship really takes as it corrects); the
    /// straight plan stays underneath, thin and dim, with its flip point and marks.</summary>
    private bool curved;

    /// <summary>The route the autopilot still flies while you edit a copy: dim, dashed, labelled FLYING.</summary>
    private void OriginalRoute()
    {
        var pts = new List<V3> { Snap.HasShip ? Snap.ShipPos : Original[0] };
        for (int i = 1; i < Original.Length - 1; i++) pts.Add(Original[i]);
        var end = Original[Original.Length - 1];
        var col = PlanCol.WithAlpha(0.75f);
        DashedTrack(pts, end, Math.Max(1.2f, 1.6f * u), col, 7 * u, 5 * u);
        for (int i = 1; i < Original.Length; i++)
            if (P(Original[i], out var x, out var y) && OnView(x, y, 4)) ScreenCircle(x, y, 3.5f * u, Math.Max(1f, 1.2f * u), col, 14);
        if (!Compact && P(end, out var ex, out var ey) && OnView(ex, ey, 4)) Label("FLYING (until you send)", ex, ey + 14 * u, 2, col, 6, 9 * u);
    }

    private void PathLines()
    {
        curved = Snap.HasShip && Snap.Prediction.Count > 1 && Snap.PredictionPhases.Count == Snap.Prediction.Count;
        AnchorMarks();
        for (int i = 0; i < Path.Legs.Count; i++) RouteLines(Path.Legs[i], Path.LegStartTime(i), i == Path.Legs.Count - 1, i);
        // parabolic: one leg through all the points, so their markers aren't leg ends
        if (Path.Legs.Count == 1) for (int n = Path.Next; n < Path.Waypoints.Count; n++) WaypointMark(n, n == Path.Next);
        DrawMarks();
        if (curved) CurvedRoute();
        if (Snap.GravityEntry is V3 ge && P(ge, out var gx, out var gy) && OnView(gx, gy, 4))
        {
            ScreenCircle(gx, gy, 6 * u, Math.Max(1.2f, 1.6f * u), WarnCol, 20);
            if (!Compact) Label("GRAVITY: " + (Snap.GravityEntryName ?? "well"), gx, gy, 6 * u, WarnCol, 3, 10 * u);
            AddPick("Gravity well", "Gravity", (Snap.GravityEntryName ?? "a planet") + "'s well starts here, " + Fmt.Dist(Snap.GravityAlong) + " along the route", ge, gx, gy, 7 * u);
        }
    }

    private void CurvedRoute()
    {
        var pts = Snap.Prediction; var ph = Snap.PredictionPhases;
        float w = Math.Max(1.5f, 2.6f * u);
        for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                var col = ph[i] == Trajectory.Burn ? BurnCol : ph[i] == Trajectory.Brake ? BrakeCol : CoastCol;
                if (pass == 0) Seg(pts[i], pts[i + 1], w + 2.5f * u, MapColor.Black.WithAlpha(0.55f));
                else Seg(pts[i], pts[i + 1], w, col);
            }
        // where the projected ship turns to brake
        for (int i = 1; i < pts.Count; i++)
            if (ph[i] == Trajectory.Brake && ph[i - 1] != Trajectory.Brake && P(pts[i], out var fx, out var fy) && OnView(fx, fy, 8))
            {
                Diamond(fx, fy, 8 * u, BrakeCol); Diamond(fx, fy, 4 * u, Bg);
                break;
            }
    }

    // T+ marks pinned to the route: when a route is set, a mark is put every step of its time (a round figure, about
    // a sixth of the trip) and kept as a point in space. Each frame the mark is drawn there, labelled with the time to
    // reach it on the plan now, and dropped once the ship has passed it. The set is made again for a new route, when
    // the trip has grown well past the time it was set for, when the route has moved away from the marks, or when all
    // of them are passed.
    private string markKey;
    private double markEta0, markStep;
    private readonly List<V3> marks = new List<V3>();
    private readonly List<double> markTimes = new List<double>(); // time to each mark now; NaN = passed

    private void AnchorMarks()
    {
        double eta = Path.Eta;
        if (double.IsNaN(eta) || eta <= 0) { markKey = null; marks.Clear(); markTimes.Clear(); return; }
        string key = Path.Final.TargetName + "|" + Path.Waypoints.Count;
        bool again = key != markKey || eta > markEta0 * 1.6;
        if (!again) again = !MarkTimes();
        if (!again) return;
        markKey = key; markEta0 = eta; markStep = NiceTime(eta / 6);
        marks.Clear();
        for (int k = 1; k * markStep < eta - markStep * 0.3; k++)
            if (PointAtTime(k * markStep, out var p)) marks.Add(p);
        MarkTimes();
    }

    /// <summary>The time to each mark on the plan now (NaN once passed); false when none is left ahead, or one ahead
    /// is off the route (it was replanned or the ship drifted).</summary>
    private bool MarkTimes()
    {
        markTimes.Clear();
        double tol = Math.Max(Path.Distance * 0.04, 5e3);
        bool ahead = false, stray = false;
        foreach (var m in marks)
        {
            double best = double.MaxValue, t = double.NaN;
            for (int i = 0; i < Path.Legs.Count; i++)
            {
                var leg = Path.Legs[i];
                double d = leg.Nearest(m, out var along);
                if (d >= best) continue;
                best = d;
                t = i == 0 && along <= 1 ? double.NaN : Path.LegStartTime(i) + leg.TimeAt(along);
            }
            if (!double.IsNaN(t) && best > tol) stray = true;
            if (!double.IsNaN(t)) ahead = true;
            markTimes.Add(t);
        }
        return ahead && !stray;
    }

    /// <summary>Where the plan has the ship at time t from now.</summary>
    private bool PointAtTime(double t, out V3 p)
    {
        p = default;
        for (int i = 0; i < Path.Legs.Count; i++)
        {
            var leg = Path.Legs[i];
            double t0 = Path.LegStartTime(i);
            if (!leg.HasBurnPlan || double.IsNaN(leg.Eta) || t < t0 || t >= t0 + leg.Eta) continue;
            p = leg.PointAt(leg.AlongAt(t - t0));
            return true;
        }
        return false;
    }

    private void DrawMarks()
    {
        for (int i = 0; i < marks.Count && i < markTimes.Count; i++)
        {
            double t = markTimes[i];
            if (double.IsNaN(t) || t <= 0) continue;
            if (!P(marks[i], out var tx, out var ty) || !OnView(tx, ty)) continue;
            c.Disc(tx, ty, 2.4f * u, TextCol);
            if (Opt.Labels && !Compact) Label("T+" + Fmt.Time(t), tx, ty, 3 * u, DimText, 10, 9 * u);
        }
    }

    private void RouteLines(RoutePlan r, double t0, bool final, int legIndex)
    {
        float w = Math.Max(1.5f, 2.6f * u);
        void Leg(double a, double b, MapColor col)
        {
            if (b - a < 1) return;
            // split long legs so the near-plane clip and perspective stay right (and a bent leg follows its curve)
            int n = r.Curved ? 64 : 24;
            for (int i = 0; i < n; i++)
            {
                var q0 = r.PointAt(a + (b - a) * i / n); var q1 = r.PointAt(a + (b - a) * (i + 1) / n);
                if (curved) { Seg(q0, q1, Math.Max(1f, 1.1f * u), col.WithAlpha(0.35f)); continue; }
                Seg(q0, q1, w + 2.5f * u, MapColor.Black.WithAlpha(0.55f));
                Seg(q0, q1, w, col);
            }
        }
        if (r.HasBurnPlan && !r.TooFastToStop)
        {
            Leg(0, r.CoastAt, BurnCol);
            Leg(r.CoastAt, r.FlipAt, CoastCol);
            Leg(r.FlipAt, r.Distance, BrakeCol);
        }
        else Leg(0, r.Distance, r.TooFastToStop ? WarnCol : CoastCol);

        // flip point
        if (r.HasBurnPlan && !r.TooFastToStop && P(r.FlipPoint, out var fx, out var fy))
        {
            Diamond(fx, fy, 10 * u, BrakeCol);
            Diamond(fx, fy, 5 * u, Bg);
            AddPick("Flip point", "Route", "T+" + Fmt.Time(t0 + r.BurnTime + r.CoastTime) + "  drag: bend the leg up / down", r.FlipPoint, fx, fy, 6 * u, r.TargetName);
            Label("FLIP  T+" + Fmt.Time(t0 + r.BurnTime + r.CoastTime), fx, fy, 7 * u, BrakeCol, 3, 10.5f * u, true);
        }
        // the point a bent leg curves through
        if (Bends != null && Bends.TryGetValue(r.TargetName, out var bend) && r.Curved && P(bend, out var bx0, out var by0))
        {
            var chordPt = V3.Lerp(r.Start, r.Target, Math.Max(0, Math.Min(1, (bend - r.Start).Dot(r.Target - r.Start) / Math.Max(1, (r.Target - r.Start).LengthSq))));
            Stalk(bend, WaypointCol.WithAlpha(0.5f));
            Diamond(bx0, by0, 7 * u, WaypointCol); Diamond(bx0, by0, 3.5f * u, Bg);
            AddPick("Bend", "Bend", Fmt.Dist(V3.Distance(bend, chordPt)) + " off the line  drag: move up / down, Shift+right-click: straighten", bend, bx0, by0, 7 * u, r.TargetName);
            Label("BEND", bx0, by0, 8 * u, WaypointCol, 1, 9.5f * u);
        }
        if (r.HasBurnPlan && r.CoastTime > 1 && P(r.CoastPoint, out var cx2, out var cy2))
        {
            c.Disc(cx2, cy2, 4 * u, CoastCol);
            Label("COAST " + Fmt.Speed(r.PeakSpeed), cx2, cy2, 5 * u, CoastCol, 4, 10 * u);
        }

        // sector borders
        foreach (var x in r.Crossings)
        {
            if (!P(x.Pos, out var bx, out var by)) continue;
            ScreenCircle(bx, by, 6 * u, Math.Max(1f, 1.6f * u), BorderCol, 20);
            AddPick("Border: " + x.To.Dx + " " + x.To.Name, "Nexus transfer", Fmt.Dist(x.At) + " along", x.Pos, bx, by, 6 * u);
            Label("→ " + x.To.Dx + " " + x.To.Name, bx, by, 7 * u, BorderCol, 4, 10 * u);
        }

        // close passes
        foreach (var h in r.Hazards)
        {
            if (!P(h.Closest, out var hx, out var hy)) continue;
            float s = 6 * u;
            c.Line(hx - s, hy - s, hx + s, hy + s, 2.2f * u, WarnCol);
            c.Line(hx - s, hy + s, hx + s, hy - s, 2.2f * u, WarnCol);
            Label(h.Blocked ? "THROUGH " + h.World.Name.ToUpperInvariant() : h.World.Name + " " + Fmt.Dist(h.Clearance), hx, hy, s + 2, WarnCol, 3, 10 * u, true);
        }

        // a detour waypoint
        if (!final) WaypointMark(Path.Next + legIndex, legIndex == 0);

        // destination
        if (final && P(r.Target, out var dx, out var dy))
        {
            Stalk(r.Target, BrakeCol.WithAlpha(0.6f));
            float lo = DestinationMark(dx, dy);
            AddPick(r.TargetName, "F&B destination", r.TargetSector.Dx + "  right-click: remove", r.Target, dx, dy, 10 * u);
            Label(ShortName(r.TargetName), dx, dy, lo, Chart ? T.Trim : BrakeCol, 0, 12 * u, true, "dest", true);
        }
    }

    private void WaypointMark(int n, bool nextPick)
    {
        var wp = Path.Waypoints[n];
        if (!P(wp.Pos, out var wx, out var wy)) return;
        var wc = wp.User ? WaypointCol : DetourCol;
        ScreenCircle(wx, wy, 7 * u, 2 * u, wc, 20);
        c.Disc(wx, wy, 2.5f * u, wc);
        AddPick(wp.Name, wp.User ? "Your waypoint" : DetourKind + (wp.Around?.Name ?? "?"), wp.User ? "drag: up / down or sideways, Shift+right-click: remove" : "Shift+right-click: remove (fly straight past)", wp.Pos, wx, wy, 8 * u);
        Label((wp.User ? ShortName(wp.Name) : "D" + (n + 1)) + (nextPick ? "  next pick" : ""), wx, wy, 9 * u, wc, nextPick ? 1 : 3, 10.5f * u, true);
    }

    private static double NiceTime(double s)
    {
        foreach (var t in new double[] { 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600, 7200, 10800, 21600 }) if (t >= s) return t;
        return 43200;
    }

    // ------------------------------------------------------------------ hover, labels, panels

    private void FindHover()
    {
        if (float.IsNaN(MouseX) || !view.Contains(MouseX, MouseY)) return;
        // small markers (stations, GPS, moons) sit on top of big discs (planets, Sol): inside a disc counts as distance 0,
        // and among the things under the mouse the smallest wins, so a station over Jupiter can still be picked
        float best = float.MaxValue;
        foreach (var p in Picks)
        {
            float dx = p.X - MouseX, dy = p.Y - MouseY, d = (float)Math.Sqrt(dx * dx + dy * dy) - p.R;
            if (d >= HoverReach * u) continue;
            bool world = p.Kind == "Planet" || p.Kind == "Star" || p.Kind == FieldKind || p.Kind == BeltKind || p.Kind != null && p.Kind.StartsWith("Moon of");
            // a marker the mouse is on comes before any world; one only near the mouse ranks with the worlds
            float score = Math.Max(0, d) * 4 + p.R + (world || d > 3 * u ? 1000 : 0);
            if (score < best) { best = score; Hovered = p; }
        }
        if (Hovered is Pick h) ScreenCircle(h.X, h.Y, h.R + 5 * u, 1.5f * u, MapColor.White.WithAlpha(0.85f), 28);
    }

    private void PlaceLabels()
    {
        // by priority, then in the order asked (a stable order: equal ones don't swap from frame to frame)
        labels.Sort((a, b) => a.Prio != b.Prio ? a.Prio.CompareTo(b.Prio) : a.Order.CompareTo(b.Order));
        var tries = new Box[4];
        foreach (var l in labels)
        {
            float tw = StableWidth(l.Text, l.Px), th = l.Px, pad = 2 * u, gap = l.R + 3 * u;
            tries[0] = new Box(l.X + gap, l.Y - th / 2, tw, th); tries[1] = new Box(l.X - gap - tw, l.Y - th / 2, tw, th);
            tries[2] = new Box(l.X - tw / 2, l.Y - gap - th, tw, th); tries[3] = new Box(l.X - tw / 2, l.Y + gap, tw, th);
            int first = l.Key != null && lastSpot.TryGetValue(l.Key, out var ls) ? ls : 0, placed = -1;
            bool Try(Box t)
            {
                var tb = t.Inflate(pad);
                if (!view.Contains(tb.X, tb.Y) || !view.Contains(tb.Right, tb.Bottom)) return false;
                foreach (var o in taken) if (o.Overlaps(tb)) return false;
                taken.Add(tb);
                if (l.Boxed && Chart)
                {
                    // a tinted info box with a bar in the label's colour down its left side
                    c.Rect(tb.X - 3 * u, tb.Y, tb.W + 3 * u, tb.H, MapColor.Lerp(Bg, Accent, 0.28f).WithAlpha(0.8f));
                    c.Rect(tb.X - 3 * u, tb.Y, Math.Max(1.5f, 2f * u), tb.H, l.Col);
                }
                else if (l.Boxed) c.Rect(tb.X, tb.Y, tb.W, tb.H, MapColor.Black.WithAlpha(0.6f));
                c.Text(l.Text, t.X, t.Y, l.Px, l.Col);
                return true;
            }
            // each spot as it is, then (for a label on the view) pushed in from the view's edge, last spot first
            for (int pass = 0; pass < 2 && placed < 0; pass++)
                for (int k = 0; k < 4 && placed < 0; k++)
                {
                    int i = (first + k) % 4;
                    var t = tries[i];
                    if (pass == 1)
                    {
                        if (!view.Contains(l.X, l.Y)) break;
                        t = new Box(Math.Max(view.X + pad, Math.Min(view.Right - pad - t.W, t.X)), Math.Max(view.Y + pad, Math.Min(view.Bottom - pad - t.H, t.Y)), t.W, t.H);
                    }
                    if (Try(t)) placed = i;
                }
            // a label that matters (the destination) and found no room beside its mark: set it further out on a
            // diagonal, with a leader line from the mark's edge to it. Spots 4.. (the corner, then the distance out)
            if (placed < 0 && l.Leader && view.Contains(l.X, l.Y))
            {
                int lf = first >= 4 ? first - 4 : 0;
                for (int k = 0; k < 12 && placed < 0; k++)
                {
                    int i = (lf + k) % 12, corner = i % 4, step = i / 4;
                    float sx = corner == 0 || corner == 3 ? 1 : -1, sy = corner < 2 ? -1 : 1;
                    float d = gap + (10 + step * 14) * u, ex = l.X + sx * d * 0.7071f, ey = l.Y + sy * d * 0.7071f;
                    float stub = 6 * u, bx = sx > 0 ? ex + stub + 2 * u : ex - stub - 2 * u - tw;
                    if (!Try(new Box(bx, ey - th / 2, tw, th))) continue;
                    var lc = l.Col.WithAlpha(0.8f); float lw = Math.Max(1f, 1.2f * u);
                    c.Line(l.X + sx * l.R * 0.7071f, l.Y + sy * l.R * 0.7071f, ex, ey, lw, lc);
                    c.Line(ex, ey, ex + sx * stub, ey, lw, lc);
                    c.Disc(l.X + sx * l.R * 0.7071f, l.Y + sy * l.R * 0.7071f, Math.Max(1f, 1.4f * u), lc);
                    placed = 4 + i;
                }
            }
            if (l.Key != null) { if (placed >= 0) lastSpot[l.Key] = placed; else lastSpot.Remove(l.Key); }
        }
    }

    /// <summary>Height of the info strip that hangs below the side window's map (0 in picture-in-picture), as last drawn.</summary>
    public float Strip { get; private set; }

    /// <summary>Measures the info strip for a side window whose map fills mapBox, before the first Draw: SideLayout.Box then adds it below.</summary>
    public float MeasureStrip(IMapCanvas canvas, Box mapBox)
    {
        c = canvas;
        SetScale(mapBox.W, mapBox.H);
        return Strip = Compact && !Mini ? StripHeight(mapBox.W) : 0;
    }

    private void SetScale(float w, float h)
    {
        u = Math.Max(0.55f, Math.Min(2.2f, Math.Min(w * 1.35f, h) / 720f));
        if (Compact) u = Math.Max(u * 1.25f, 1f); // the side window is small: keep its text readable
    }

    /// <summary>Height of the side window's info strip: the route panel (two columns) and the key bar, as Panels lays them out.</summary>
    private float StripHeight(float width)
    {
        float px = 12 * u, lh = px * 1.35f;
        float h = HelpRows(px * 0.85f, width - px * 2).Count * lh * 1.05f + lh * 1.05f + px * 0.35f;
        int n = RouteLinesText().Count;
        if (n > 0) h += (n + 1) / 2 * lh + px;
        if (n > 0 && Path.First.HasBurnPlan) h += PanelStyle.TimeBarH(px * 0.95f);
        return h;
    }

    private void Panels()
    {
        float px = 12 * u, lh = px * 1.35f;
        // title bar
        string title = Mini ? "MAP" : Compact ? "ROUTE PREVIEW" : "SDX2 SYSTEM MAP";
        string sub = Route != null ? ShortName(Route.TargetName) : Snap.FnbReady ? "No Flip and Burn destination — press N and pick one" : "Flip and Burn not detected";
        c.Rect(view.X, view.Y, view.W, lh * 1.5f, PanelBg);
        TitleBottom = view.Y + lh * 1.5f;
        c.Rect(view.X, view.Y + lh * 1.5f - 2 * u, view.W, 2 * u, Accent.WithAlpha(0.8f));
        if (T.Hud) for (float tx = view.X + px; tx < view.Right - px; tx += 9 * u) c.Rect(tx, view.Y + lh * 1.5f - 6 * u, 1, 4 * u, Accent.WithAlpha(0.45f));
        if (T.Style == ThemeStyle.Naval) c.Rect(view.X, view.Y + lh * 1.5f, view.W, Math.Max(1f, 1f * u), T.Trim.WithAlpha(0.7f));
        if (T.Style == ThemeStyle.Belter) HazardStripe(view.X, view.Y + lh * 1.5f, view.W, 5 * u);
        if (Lock) RulerTicks(view.X + px, view.Right - px, view.Y + lh * 1.5f + (Naval ? Math.Max(1f, u) : 0), 1);
        if (Chart) c.Rect(view.X + view.W * 0.22f, view.Y + lh * 1.5f, view.W * 0.33f, Math.Max(2f, 3f * u), Accent.WithAlpha(0.55f));
        c.Text(title, view.X + px, view.Y + lh * 0.3f, px * 1.05f, Accent);
        float tw = c.TextWidth(title, px * 1.05f);
        c.Text(sub, view.X + px * 2 + tw, view.Y + lh * 0.3f, px, Route != null ? BrakeCol : DimText);
        if (Snap.HasShip && !Compact)
        {
            string here = "You: " + SystemData.SectorAt(Snap.ShipPos).Dx + " " + SystemData.SectorAt(Snap.ShipPos).Name + (Snap.FnbState.Length > 0 ? "   F&B: " + Snap.FnbState : "");
            c.Text(here, view.Right - px, view.Y + lh * 0.3f, px * 0.95f, DimText, TextAnchor.Right);
        }

        // the bottom bar's rows (legend, then the help groups), measured first so the panels above can sit on it
        float hpx = px * 0.85f, rowH = lh * 1.05f;
        var rows = Mini ? new List<(string Label, string Text)>() : HelpRows(hpx, view.W - px * 2);
        float barH = Mini ? 0 : rowH * (1 + rows.Count) + px * 0.35f;

        // route panel (picture-in-picture: distance, ETA, flip and what to pick next only)
        var lines = RouteLinesText();
        if (Mini) lines = lines.FindAll(l => l.Text.StartsWith("Distance") || l.Text.StartsWith("ETA") || l.Text.StartsWith("Flip in") || l.Text.StartsWith("Pick next"));
        if (lines.Count == 0) panelBox = default;
        if (lines.Count == 0 || Compact) ControlsSlotClear();
        if (lines.Count > 0)
        {
            var plan = Path.First;
            bool timeline = !Mini && plan.HasBurnPlan;
            int half = (lines.Count + 1) / 2;               // compact: two columns
            if (Compact)
            {
                float h = half * lh + px + (timeline ? PanelStyle.TimeBarH(px * 0.95f) : 0);
                float x0 = view.X, y0 = full.Bottom - h - barH, w = view.W;
                panelBox = new Box(x0, y0, w, h);
                float y = PanelStyle.Frame(c, panelBox, u, px, null);
                if (timeline)
                {
                    PanelStyle.TimeBar(c, new Box(x0 + px, y, w - px * 2, 0), u, px * 0.95f, plan.BurnTime, plan.CoastTime, plan.BrakeTime, Path.Legs.Count > 1 ? "LEG 1 " : "ETA ");
                    y += PanelStyle.TimeBarH(px * 0.95f);
                }
                for (int i = 0; i < lines.Count; i++)
                {
                    float cx = x0 + px + (i < half ? 0 : w / 2), cy = y + (i < half ? i : i - half) * lh;
                    c.Text(lines[i].Text, cx, cy, px * 0.95f, lines[i].Col);
                }
            }
            else SidedRoutePanel(lines, px, lh, plan, timeline, full.Bottom - barH);
        }

        // bottom bar: the legend and scale on the first row, then one row per help group (label in the accent colour)
        float by = full.Bottom - barH;
        barTop = by;
        if (!Mini) { c.Rect(view.X, by, view.W, barH, PanelBg); c.Rect(view.X, by, view.W, 1, Accent.WithAlpha(0.35f)); }
        if (!Mini && Lock) RulerTicks(view.X + px, view.Right - px, by, -1);
        float ry = by + px * 0.25f, lx = view.X + px, labelW = 0;
        foreach (var r in rows) if (r.Label != null) labelW = Math.Max(labelW, c.TextWidth(r.Label, hpx));
        if (!Mini && Path != null && Path.First.HasBurnPlan)
        {
            foreach (var (name, col) in new[] { ("burn", BurnCol), ("coast", CoastCol), ("brake", BrakeCol) })
            {
                c.Rect(lx, ry + rowH * 0.42f, 14 * u, 3 * u, col);
                c.Text(name, lx + 18 * u, ry + rowH * 0.1f, hpx, DimText);
                lx += 18 * u + c.TextWidth(name, hpx) + 12 * u;
            }
        }
        if (!Mini && Snap.Track.Count > 0)
        {
            Dashed(lx, ry + rowH * 0.48f, lx + 16 * u, ry + rowH * 0.48f, Math.Max(1f, 1.5f * u), TrackCol, 4 * u, 3 * u);
            c.Text("flown", lx + 20 * u, ry + rowH * 0.1f, hpx, DimText);
            lx += 20 * u + c.TextWidth("flown", hpx) + 12 * u;
        }
        if (!Mini && Opt.Grid && gridStep > 0) c.Text(Hex ? "hex " + Fmt.Dist(HexWidth) : "grid " + Fmt.Dist(gridStep), view.Right - px, ry + rowH * 0.1f, hpx, DimText, TextAnchor.Right);
        for (int i = 0; i < rows.Count; i++)
        {
            float y = ry + rowH * (i + 1) + rowH * 0.1f;
            if (rows[i].Label != null) c.Text(rows[i].Label, view.X + px, y, hpx, Accent.WithAlpha(0.9f));
            c.Text(rows[i].Text, view.X + px + (labelW > 0 ? labelW + px : 0), y, hpx, DimText);
        }

        // tooltip
        if (Hovered is Pick hp)
        {
            var tip = new List<string> { hp.Name, hp.Kind + (string.IsNullOrEmpty(hp.Detail) ? "" : "  ·  " + hp.Detail) };
            if (Snap.HasShip) tip.Add("from you " + Fmt.Dist(V3.Distance(Snap.ShipPos, hp.Pos)));
            if (!Compact) tip.Add(hp.Kind == "Your ship" ? "click: focus" : "click: focus   shift+click: set as destination");
            float tpx = 11 * u, w = 0;
            foreach (var t in tip) w = Math.Max(w, c.TextWidth(t, tpx));
            w += tpx * 1.6f;
            float h = tip.Count * tpx * 1.3f + tpx * 0.6f;
            float tx = Math.Min(view.Right - w - 4, MouseX + 16 * u), ty = Math.Min(by - h - 4 * u, MouseY + 12 * u);
            c.Rect(tx, ty, w, h, PanelBg);
            c.Rect(tx, ty, w, 1.5f * u, Accent);
            for (int i = 0; i < tip.Count; i++) c.Text(tip[i], tx + tpx * 0.8f, ty + tpx * 0.3f + i * tpx * 1.3f, tpx, i == 0 ? TextCol : DimText);
        }
    }

    /// <summary>The help groups as rows: label (only on a group's first row) and items, wrapped to fit the width.</summary>
    private List<(string Label, string Text)> HelpRows(float hpx, float width)
    {
        var rows = new List<(string Label, string Text)>();
        if (string.IsNullOrEmpty(Help)) return rows;
        var groups = Help.Split('\n');
        float labelW = 0;
        foreach (var g in groups) { int bar = g.IndexOf('|'); if (bar > 0) labelW = Math.Max(labelW, c.TextWidth(g.Substring(0, bar), hpx)); }
        float room = width - (labelW > 0 ? labelW + 12 * u : 0);
        foreach (var g in groups)
        {
            int bar = g.IndexOf('|');
            string label = bar > 0 ? g.Substring(0, bar) : null, body = bar > 0 ? g.Substring(bar + 1) : g;
            string line = "";
            foreach (var item in body.Split(new[] { Sep }, StringSplitOptions.RemoveEmptyEntries))
            {
                string next = line.Length == 0 ? item : line + Sep + item;
                if (line.Length > 0 && c.TextWidth(next, hpx) > room) { rows.Add((label, line)); label = null; line = item; }
                else line = next;
            }
            if (line.Length > 0) rows.Add((label, line));
        }
        return rows;
    }

    /// <summary>The route plan as panel lines (text, colour).</summary>
    /// <summary>
    /// The water the route will burn (every leg's burn and brake at the thrust it uses, none on the coast), with what's
    /// left at the end: "Water  12.4 kL  38 % left". Warning colour below <see cref="RouteFuel.WarnBelow"/>. Null when the
    /// drives' use isn't known.
    /// </summary>
    private (string, MapColor)? WaterLine()
    {
        if (!Snap.HasShip || !(Snap.WaterRate > 0)) return null;
        double used = double.IsNaN(Snap.WaterUse) ? RouteFuel.Litres(Path, Snap.MaxAccel, Snap.WaterRate) : Snap.WaterUse;
        if (!(used > 0)) return null;
        // now - on arrival, at the thrust the route uses: "now 64 % → 47 % @ Ceres, max 100 % thrust"
        if (!(Snap.WaterCap > 0)) return ("Water  no tanks", WarnCol);
        double share = double.IsNaN(Snap.WaterThrust) ? 1 : Snap.WaterThrust;
        double left = RouteFuel.LeftAfter(Snap.Water, Snap.WaterCap, used);
        string Text(string dest) => "now " + Fmt.Num(Snap.Water / Snap.WaterCap * 100, "0") + " % → "
            + (left <= 0 ? "dry before " : Fmt.Num(left * 100, "0") + " % @ ") + dest
            + ", max " + Fmt.Num(share * 100, "0") + " % thrust";
        string name = ShortName(Path.Final.TargetName ?? "destination");
        // still too wide for the full map's value column: cut short ("Ganymede."; at least 8 letters, then the panel
        // draws the row smaller)
        if (c != null && !Compact)
        {
            float px = 12 * u, room = px * ValueCols;
            while (name.Length > 9 && c.TextWidth(Text(name), px) > room)
                name = name.Substring(0, name.Length - (name.EndsWith(".") ? 2 : 1)).TrimEnd(' ', '.') + ".";
        }
        string text = "Water  " + Text(name);
        return (text, RouteFuel.Low(Snap.Water, Snap.WaterCap, used) ? WarnCol : CoastCol);
    }

    /// <summary>
    /// A destination's short name for labels, panels and the autopilot card: "Tycho Station" → "Tycho", "Belt.Tycho.STN" → "Tycho" (a GPS name's
    /// middle part, without its region and STN), anything else as it is.
    /// </summary>
    public static string ShortName(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        string s = name.Trim();
        var parts = s.Split('.');
        if (parts.Length == 3 && parts[2].Trim().Equals("STN", StringComparison.OrdinalIgnoreCase) && parts[1].Trim().Length > 0) s = parts[1].Trim();
        foreach (var tail in new[] { " Station", " Stn", " STN" })
            if (s.Length > tail.Length && s.EndsWith(tail, StringComparison.OrdinalIgnoreCase)) { s = s.Substring(0, s.Length - tail.Length).TrimEnd(); break; }
        return s.Length > 0 ? s : name;
    }

    /// <summary>The gravity warning row (null = none): the ship in a well, or the route going into one.</summary>
    public string GravityLine()
    {
        if (Snap.GravityEntry.HasValue)
            return "GRAVITY  route enters " + (Snap.GravityEntryName ?? "a planet") + "'s well in " + Fmt.Dist(Snap.GravityAlong)
                + (Snap.InGravity ? "  (in " + (Snap.GravityName ?? "a") + "'s now, " + Fmt.Num(Snap.GravityG, "0.00") + " g)" : "");
        if (Snap.InGravity) return "GRAVITY  in " + (Snap.GravityName ?? "a planet") + "'s well, " + Fmt.Num(Snap.GravityG, "0.00") + " g";
        return null;
    }

    /// <summary>
    /// A warning when the route takes off from or lands on a world whose surface gravity the ship can't hold with the
    /// margin (<see cref="DetourPlanner.GravityMargin"/>) at the autopilot's thrust setting; null otherwise.
    /// </summary>
    public string HeavyLine()
    {
        double a = DetourPlanner.ShipAccel;
        if (Path == null || !(a > 0)) return null;
        foreach (var w in SystemData.Worlds)
        {
            if (w.SurfaceG <= 0 || DetourPlanner.HoldsAtSurface(w, a)) continue;
            bool from = DetourPlanner.OnGround(w, Path.Start), to = DetourPlanner.OnGround(w, Path.Target);
            if (!from && !to) continue;
            return "TOO HEAVY  " + (to ? "landing on " : "leaving ") + w.Name + ": " + Fmt.Num(a / 9.81, "0.00") + " g of thrust, "
                + Fmt.Num(DetourPlanner.GravityMargin * w.SurfaceG / 9.81, "0.00") + " g needed";
        }
        return null;
    }

    public List<(string Text, MapColor Col)> RouteLinesText()
    {
        var l = new List<(string, MapColor)>();
        if (Path == null) return l;
        var r = Path.First; var fin = Path.Final;
        int ahead = Path.DetoursAhead, user = Path.UserAhead, det = ahead - user;
        l.Add(("Distance  " + Fmt.Dist(Path.Distance) + (det > 0 ? "  via " + det + (det == 1 ? " detour" : " detours") : "") + (user > 0 ? "  " + user + (user == 1 ? " waypoint" : " waypoints") : ""), TextCol));
        l.Add(("To  " + fin.TargetSector.Dx + " " + fin.TargetSector.Name + (fin.StationAtTarget != null ? "  (" + ShortName(fin.StationAtTarget.Name) + ")" : ""), TextCol));
        if (GravityLine() is { } gl) l.Add((gl, WarnCol));
        if (HeavyLine() is { } hl) l.Add((hl, WarnCol));
        if (ahead > 0)
        {
            l.Add(("Pick next in N:  " + Path.Waypoints[Path.Next].Name, DetourCol));
            var names = new List<string>();
            bool wells = false, other = false, low = false, bare = false;
            for (int i = Path.Next; i < Path.Waypoints.Count; i++)
            {
                var w = Path.Waypoints[i].Around; if (w == null) continue;
                if (DetourPlanner.InWell(w)) { wells = true; low |= DetourPlanner.Clear(w, Path.Clearance) + w.Radius < w.Well; }
                else if (DetourPlanner.NoWell(w)) bare = true;
                else if (!w.IsObstacle && !w.IsField) other = true;
                var n = w == SystemData.SolZone ? "Sol " + Fmt.Dist(SystemData.SolNoFly) + " no-fly" : w.Name;
                if (!names.Contains(n)) names.Add(n);
            }
            string inWell = low ? "as low as thrust holds" : Fmt.Dist(DetourPlanner.WellGap) + " outside gravity";
            var hows = new List<string>();
            if (wells) hows.Add(inWell);
            if (bare) hows.Add(Fmt.Dist(DetourPlanner.SurfaceGap) + " over the hills");
            if (other) hows.Add(Fmt.Dist(Path.Clearance) + " clear (gravity not known)");
            string how = string.Join(", ", hows);
            if (names.Count > 0) l.Add(("Avoids  " + string.Join(", ", names) + "  (" + how + ")", DetourCol));
        }
        if (r.HasBurnPlan)
        {
            bool over = false; foreach (var g in Path.Legs) over |= g.TooFastToStop;
            l.Add(("ETA  " + Fmt.Time(Path.Eta) + (over ? "  (overshoot)" : "") + (Path.Legs.Count > 1 ? "  (slows to turn at each point)" : Path.Curved && Path.DetoursAhead > 0 ? "  (curves through the points)" : ""), over ? WarnCol : BrakeCol));
            string leg = Path.Legs.Count > 1 ? "Leg 1:  " : "";
            l.Add((leg + "Peak  " + Fmt.Speed(r.PeakSpeed) + (r.CoastTime > 1 ? "  coast " + Fmt.Time(r.CoastTime) : ""), CoastCol));
            l.Add(("Burn  " + Fmt.Time(r.BurnTime) + "   brake " + Fmt.Time(r.BrakeTime), BurnCol));
            if (WaterLine() is { } wl) l.Add(wl);
            l.Add(("Flip in  " + Fmt.Dist(r.FlipAt) + "  (T+" + Fmt.Time(r.BurnTime + r.CoastTime) + ")", BrakeCol));
            l.Add(("Accel  " + Fmt.Num(r.MaxAccel, "0.0") + " m/s²  (" + Fmt.Num(r.MaxAccel / 9.81, "0.00") + " g)", DimText));
        }
        else l.Add(("ETA  " + (double.IsNaN(r.Eta) ? "-- (not closing)" : Fmt.Time(r.Eta) + " at current speed"), DimText));
        l.Add(("Closing  " + Fmt.Speed(r.ClosingSpeed) + "   drift " + Fmt.Speed(r.LateralSpeed), r.LateralSpeed > Math.Max(50, Math.Abs(r.ClosingSpeed) * 0.1) ? BorderCol : DimText));
        int borders = 0;
        double along = 0;
        foreach (var g in Path.Legs)
        {
            foreach (var x in g.Crossings) { borders++; l.Add(("Border " + x.From.Dx + " → " + x.To.Dx + " " + x.To.Name + " @ " + Fmt.Dist(along + x.At), BorderCol)); }
            foreach (var h in g.Hazards) l.Add(((h.Blocked ? "BLOCKED by " + h.World.Name : "Passes " + h.World.Name + " @ " + Fmt.Dist(h.Clearance)) + " (" + Fmt.Dist(along + h.At) + " along)", WarnCol));
            along += g.Distance;
        }
        if (borders == 0) l.Insert(Math.Min(l.Count, 2), ("Stays in " + r.StartSector.Dx + " (no Nexus transfer)", DimText));
        if (Compact && l.Count > 12) l.RemoveRange(12, l.Count - 12);
        return l;
    }

    /// <summary>HUD corner brackets round a box.</summary>
    private void Brackets(Box b, float len, float w, MapColor col)
    {
        c.Rect(b.X, b.Y, len, w, col); c.Rect(b.X, b.Y, w, len, col);
        c.Rect(b.Right - len, b.Y, len, w, col); c.Rect(b.Right - w, b.Y, w, len, col);
        c.Rect(b.X, b.Bottom - w, len, w, col); c.Rect(b.X, b.Bottom - len, w, len, col);
        c.Rect(b.Right - len, b.Bottom - w, len, w, col); c.Rect(b.Right - w, b.Bottom - len, w, len, col);
    }

    /// <summary>A horizontal circle drawn as dashes: on segments, then off segments.</summary>
    private void DashedRing3(V3 centre, double r, int n, float w, MapColor col, int on, int off)
    {
        for (int i = 0; i < n; i++)
        {
            if (i % (on + off) >= on) continue;
            double a = i * 2 * Math.PI / n, b = (i + 1) * 2 * Math.PI / n;
            Seg(centre + new V3(r * Math.Cos(a), 0, r * Math.Sin(a)), centre + new V3(r * Math.Cos(b), 0, r * Math.Sin(b)), w, col);
        }
    }

    /// <summary>UNN: degree ticks round an orbit, every 10 degrees, longer every 30.</summary>
    private void NavalRing(double r)
    {
        for (int k = 0; k < 36; k++)
        {
            double a = k * Math.PI / 18, len = k % 3 == 0 ? 0.045 : 0.02;
            var p = new V3(r * Math.Cos(a), 0, r * Math.Sin(a));
            Seg(p * (1 - len), p * (1 + len * 0.4), Math.Max(1f, 0.9f * u), OrbitCol.WithAlpha(k % 3 == 0 ? 0.65f : 0.35f));
        }
    }

    /// <summary>UNN: faint bearing spokes from Sol every 30 degrees, labelled 000 to 330 at the outer orbit.</summary>
    private void BearingSpokes()
    {
        double outer = 0;
        foreach (var r in SystemData.OrbitRadii()) outer = Math.Max(outer, r);
        if (outer <= 0) return;
        double inner = outer * 0.02;
        for (int k = 0; k < 12; k++)
        {
            double a = k * Math.PI / 6;
            var d = new V3(Math.Cos(a), 0, Math.Sin(a));
            Seg(d * inner, d * outer, Math.Max(1f, 0.7f * u), T.Trim.WithAlpha(k % 3 == 0 ? 0.22f : 0.12f));
            if (Opt.Labels && P(d * outer * 1.03, out var x, out var y) && OnView(x, y))
                c.Text((k * 30).ToString("000"), x, y - 5 * u, 9 * u, T.Trim.WithAlpha(0.75f), TextAnchor.Center);
        }
    }

    /// <summary>UNN: four short ticks outside a ring (a plotting reticle).</summary>
    private void Reticle(float x, float y, float r, MapColor col)
    {
        float t = 4 * u, w = Math.Max(1f, 1.2f * u);
        c.Line(x + r, y, x + r + t, y, w, col); c.Line(x - r, y, x - r - t, y, w, col);
        c.Line(x, y + r, x, y + r + t, w, col); c.Line(x, y - r, x, y - r - t, w, col);
    }

    /// <summary>OPA: a few short thick orange hazard marks on an orbit (three per orbit, each orbit turned).</summary>
    private void BelterRing(double r, int i)
    {
        V3 At(double a) => new V3(r * Math.Cos(a), 0, r * Math.Sin(a));
        for (int m = 0; m < 3; m++)
        {
            double a0 = i * 1.3 + m * 2 * Math.PI / 3;
            for (int k = 0; k < 3; k++)
                Seg(At(a0 + k * 0.03), At(a0 + k * 0.03 + 0.02), Math.Max(2f, 3.2f * u), (k == 1 ? T.Trim : Accent).WithAlpha(0.85f));
        }
    }

    /// <summary>OPA: a strip of diagonal orange-and-black hazard stripes.</summary>
    private void HazardStripe(float x, float y, float w, float h)
    {
        c.PushClip(x, y, w, h);
        c.Rect(x, y, w, h, MapColor.Rgb(0x15130F));
        float step = h * 2.2f, lw = h * 0.9f;
        for (float sx = x - h; sx < x + w + h; sx += step) c.Line(sx, y + h, sx + h, y, lw, Accent.WithAlpha(0.9f));
        c.PopClip();
    }

    /// <summary>The Tachi's extra rings on an orbit: segmented thick arcs, fine dashes or a tick scale, in turn.</summary>
    private void HudRing(double r, int i)
    {
        const int n = 180;
        V3 At(double a) => new V3(r * Math.Cos(a), 0, r * Math.Sin(a));
        double step = 2 * Math.PI / n, turn = i * 0.9; // each orbit's pattern starts somewhere else
        switch (i % 3)
        {
            case 0: // thick red arcs with gaps
                for (int k = 0; k < n; k++)
                {
                    int m = k % 60;
                    if (m < 22 || m >= 30 && m < 36) Seg(At(turn + k * step), At(turn + (k + 1) * step), Math.Max(2f, 3.4f * u), Accent.WithAlpha(0.8f));
                }
                break;
            case 1: // fine bright dashes just outside the orbit
                for (int k = 0; k < n; k += 2)
                    Seg(At(turn + k * step) * 1.03, At(turn + (k + 1) * step) * 1.03, Math.Max(1f, 0.9f * u), T.Fine.WithAlpha(0.4f));
                break;
            default: // a tick scale: short radial marks, longer every tenth
                for (int k = 0; k < 120; k++)
                {
                    double a = turn + k * Math.PI / 60, len = k % 10 == 0 ? 0.06 : 0.025;
                    Seg(At(a), At(a) * (1 - len), Math.Max(1f, 0.9f * u), OrbitCol.WithAlpha(k % 10 == 0 ? 0.7f : 0.4f));
                }
                break;
        }
    }

    /// <summary>A screen circle drawn as n dashes (every other segment).</summary>
    private void DashedCircle(float x, float y, float r, float w, MapColor col, int n)
    {
        for (int i = 0; i < n; i += 2)
        {
            double a = i * 2 * Math.PI / n, b = (i + 1) * 2 * Math.PI / n;
            float ax = x + r * (float)Math.Cos(a), ay = y + r * (float)Math.Sin(a), bx = x + r * (float)Math.Cos(b), by = y + r * (float)Math.Sin(b);
            if (OnView(ax, ay, 2) || OnView(bx, by, 2)) c.Line(ax, ay, bx, by, w, col);
        }
    }

    /// <summary>A screen arc; angles in degrees clockwise from straight up.</summary>
    private void Arc(float x, float y, float r, float a0, float a1, float w, MapColor col, int n)
    {
        float lx = 0, ly = 0;
        for (int i = 0; i <= n; i++)
        {
            double a = (a0 + (a1 - a0) * i / n) * Math.PI / 180;
            float qx = x + r * (float)Math.Sin(a), qy = y - r * (float)Math.Cos(a);
            if (i > 0) c.Line(lx, ly, qx, qy, w, col);
            lx = qx; ly = qy;
        }
    }

    /// <summary>A chevron centred on (x, y) pointing along (dx, dy), arms s long.</summary>
    private void Chevron(float x, float y, float s, float dx, float dy, float w, MapColor col)
    {
        float tx = x + dx * s * 0.5f, ty = y + dy * s * 0.5f, bx = x - dx * s * 0.5f, by = y - dy * s * 0.5f;
        c.Line(tx, ty, bx - dy * s, by + dx * s, w, col);
        c.Line(tx, ty, bx + dy * s, by - dx * s, w, col);
    }

    /// <summary>Contact chart: a faint hex texture, plus marks on a square grid, and a crosshair through Sol with bearings at the edges.</summary>
    private void ChartBack()
    {
        float lw = Math.Max(1f, 0.8f * u);
        // the crosshair and bearings, through Sol when it's on the map (the hex grid is HexGrid, drawn as the map's grid)
        if (!P(V3.Zero, out var sx, out var sy) || !OnView(sx, sy)) return;
        var cc = Accent.WithAlpha(0.4f);
        c.Rect(view.X, sy, view.W, lw, cc);
        c.Rect(sx, view.Y, lw, view.H, cc);
        if (Compact) return;
        float bp = 10 * u, top = view.Y + 34 * u;
        foreach (var (text, bx, by) in new[] { ("000", sx, top), ("180", sx, (barTop > view.Y ? barTop : view.Bottom) - bp * 1.8f), ("270", view.X + 18 * u, sy - bp * 0.6f), ("090", view.Right - 18 * u, sy - bp * 0.6f) })
        {
            float w = c.TextWidth(text, bp) + 6 * u;
            c.Rect(bx - w / 2, by - u, w, bp * 1.25f, Bg);
            c.Text(text, bx, by, bp, TextCol.WithAlpha(0.85f), TextAnchor.Center);
        }
    }

    /// <summary>Contact chart: a pointy-top hexagon outline.</summary>
    private void Hexagon(float x, float y, float r, float w, MapColor col)
    {
        float lx = 0, ly = 0;
        for (int i = 0; i <= 6; i++)
        {
            double a = (i * 60 - 90) * Math.PI / 180;
            float qx = x + r * (float)Math.Cos(a), qy = y + r * (float)Math.Sin(a);
            if (i > 0) c.Line(lx, ly, qx, qy, w, col);
            lx = qx; ly = qy;
        }
    }

    /// <summary>Contact chart: a filled triangle centred on (x, y) pointing along (dx, dy), s from centre to tip.</summary>
    private void Triangle(float x, float y, float s, float dx, float dy, MapColor col)
    {
        float tx = x + dx * s, ty = y + dy * s, bx = x - dx * s * 0.55f, by = y - dy * s * 0.55f, hw = s * 0.75f;
        float ax = bx - dy * hw, ay = by + dx * hw, cx = bx + dy * hw, cy = by - dx * hw;
        // filled with lines from the tip across the base, then outlined
        float lw = Math.Max(1.5f, s * 0.28f);
        for (int i = 0; i <= 4; i++)
        {
            float f = i / 4f;
            c.Line(tx, ty, ax + (cx - ax) * f, ay + (cy - ay) * f, lw, col);
        }
        float ow = Math.Max(1f, 1.2f * u);
        c.Line(tx, ty, ax, ay, ow, col); c.Line(ax, ay, cx, cy, ow, col); c.Line(cx, cy, tx, ty, ow, col);
    }

    /// <summary>Contact chart: a box outline with its corners cut, the top-right one by big, the others by small.</summary>
    private void Chamfer(Box b, float small, float big, float w, MapColor col)
    {
        float l = b.X, t = b.Y, r = b.Right, bt = b.Bottom;
        var pts = new[] { (l + small, t), (r - big, t), (r, t + big), (r, bt - small), (r - small, bt), (l + small, bt), (l, bt - small), (l, t + small) };
        for (int i = 0; i < pts.Length; i++)
        {
            var (ax, ay) = pts[i]; var (bx, by) = pts[(i + 1) % pts.Length];
            c.Line(ax, ay, bx, by, w, col);
        }
    }

    /// <summary>Protogen: a ruler of ticks along a line, hanging down (dir 1) or standing up (dir -1), longer every fifth.</summary>
    private void RulerTicks(float x0, float x1, float y, int dir)
    {
        float step = 10 * u, w = Math.Max(1f, 1f * u);
        int k = 0;
        for (float x = x0; x <= x1; x += step, k++)
        {
            bool major = k % 5 == 0;
            float len = (major ? 7 : 3) * u;
            c.Rect(x, dir > 0 ? y : y - len, w, len, Accent.WithAlpha(major ? 0.85f : 0.45f));
        }
    }

    /// <summary>Protogen: a faint dashed crosshair across the map through the destination (or the view's centre).</summary>
    private void ProtoCross()
    {
        float x = view.X + view.W / 2, y = view.Y + view.H / 2;
        if (Route != null && P(Route.Target, out var tx, out var ty) && OnView(tx, ty)) { x = tx; y = ty; }
        var col = Accent.WithAlpha(0.2f);
        float w = Math.Max(1f, 0.8f * u), d = 6 * u;
        Dashed(view.X, y, view.Right, y, w, col, d, d);
        Dashed(x, view.Y, x, view.Bottom, w, col, d, d);
        float len = view.W + view.H, dx = (float)Math.Cos(0.61) * len, dy = (float)Math.Sin(0.61) * len;
        var faint = Accent.WithAlpha(0.1f);
        Dashed(x - dx, y - dy, x + dx, y + dy, w, faint, 4 * u, 8 * u);
        Dashed(x - dx, y + dy, x + dx, y - dy, w, faint, 4 * u, 8 * u);
    }

    /// <summary>Protogen: a solid red arc and a dashed one on the Belt.</summary>
    private void BeltArcs(double r)
    {
        V3 At(double a) => new V3(r * Math.Cos(a), 0, r * Math.Sin(a));
        const double step = 0.02;
        float w = Math.Max(2f, 2.6f * u);
        for (double a = 2.2; a < 3.9; a += step) Seg(At(a), At(a + step), w, WarnCol.WithAlpha(0.85f));
        int k = 0;
        for (double a = 5.2; a < 6.6; a += step, k++) if (k % 3 != 2) Seg(At(a), At(a + step), w, WarnCol.WithAlpha(0.55f));
    }

    /// <summary>Protogen: the lock reticle on the destination: range rings, a magenta wedge, a thick lock arc, chevrons and the TRAJECTORY VECTOR tag.</summary>
    private void LockReticle(float x, float y)
    {
        float lw = Math.Max(1f, 1f * u), mw = Math.Max(1f, 1.3f * u);
        DashedCircle(x, y, 106 * u, lw, Accent.WithAlpha(0.3f), 96);
        DashedCircle(x, y, 68 * u, mw, WarnCol.WithAlpha(0.8f), 72);
        DashedCircle(x, y, 48 * u, lw, Accent.WithAlpha(0.55f), 56);
        Arc(x, y, 41 * u, -35, 35, 11 * u, T.Trim.WithAlpha(0.35f), 14);
        Arc(x, y, 31 * u, 15, 300, Math.Max(2f, 4f * u), Accent, 48);
        Arc(x, y, 31 * u, 300, 375, Math.Max(2f, 4f * u), Accent.WithAlpha(0.15f), 12);
        ScreenCircle(x, y, 25 * u, lw, T.Trim.WithAlpha(0.8f), 40);
        ScreenCircle(x, y, 17 * u, mw, Accent, 40);
        c.Line(x - 5 * u, y, x + 5 * u, y, mw, T.Fine);
        c.Line(x, y - 5 * u, x, y + 5 * u, mw, T.Fine);
        Chevron(x, y - 11 * u, 4 * u, 0, -1, Math.Max(1.5f, 2f * u), Accent);
        for (int i = 0; i < 3; i++) Chevron(x, y - (100 - 9 * i) * u, 7 * u, 0, 1, Math.Max(1.5f, 2.4f * u), Accent.WithAlpha(0.4f + 0.3f * i));
        const string tag = "TRAJECTORY VECTOR";
        float px = 9.5f * u, tw = c.TextWidth(tag, px), ty = y - 76 * u;
        c.Text(tag, x, ty, px, T.Fine, TextAnchor.Center);
        Chevron(x - tw / 2 - 10 * u, ty + px * 0.55f, 5 * u, 1, 0, Math.Max(1.5f, 2.2f * u), Accent);
        Chevron(x + tw / 2 + 10 * u, ty + px * 0.55f, 5 * u, -1, 0, Math.Max(1.5f, 2.2f * u), Accent);
        taken.Add(new Box(x - tw / 2 - 16 * u, ty, tw + 32 * u, px * 1.2f));
        taken.Add(new Box(x - 32 * u, y - 32 * u, 64 * u, 64 * u));
    }

    private void Frame()
    {
        // the border goes round the whole window; a line splits the map from the info strip below it
        var map = view;
        view = full;
        FrameBorder();
        view = map;
        if (full.H > view.H) c.Rect(view.X, view.Bottom - Math.Max(1f, u) / 2, view.W, Math.Max(1f, u), Accent.WithAlpha(0.7f));
    }

    private void FrameBorder()
    {
        float w = Math.Max(1f, 1.5f * u);
        if (Naval)
        {
            // the chart's chamfered bezel with a gold inner line, gold side bars and a gold strip along the top
            Chamfer(view, 14 * u, 34 * u, w, Accent.WithAlpha(0.9f));
            Chamfer(view.Inflate(-4 * u), 12 * u, 30 * u, Math.Max(1f, 0.8f * u), T.Trim.WithAlpha(0.55f));
            float bw = Math.Max(2f, 2.5f * u), bh = Math.Min(view.H * 0.18f, 60 * u);
            c.Rect(view.X, view.Y + view.H * 0.3f, bw, bh, T.Trim.WithAlpha(0.8f));
            c.Rect(view.X, view.Y + view.H * 0.7f - bh, bw, bh, T.Trim.WithAlpha(0.8f));
            c.Rect(view.X + 22 * u, view.Y, Math.Min(view.W * 0.25f, 70 * u), Math.Max(2f, 2.2f * u), T.Trim);
            return;
        }
        if (T.Style == ThemeStyle.Belter)
        {
            // a heavy orange border with teal notched corners
            float b = Math.Max(2f, 2.4f * u);
            c.Rect(view.X, view.Y, view.W, b, Accent); c.Rect(view.X, view.Bottom - b, view.W, b, Accent);
            c.Rect(view.X, view.Y, b, view.H, Accent); c.Rect(view.Right - b, view.Y, b, view.H, Accent);
            Brackets(new Box(view.X - b, view.Y - b, view.W + 2 * b, view.H + 2 * b), 16 * u, Math.Max(2f, 2.6f * u), T.Trim);
            return;
        }
        if (Chart)
        {
            // a chamfered bezel: a bright inner outline and a dim outer one, the top-right corner cut deepest
            Chamfer(view, 14 * u, 34 * u, w, Accent.WithAlpha(0.9f));
            Chamfer(view.Inflate(-4 * u), 12 * u, 30 * u, Math.Max(1f, 0.8f * u), Accent.WithAlpha(0.35f));
            return;
        }
        if (Proto)
        {
            // a thin cyan border, corner brackets and a tick ruler along the top
            var dim = Accent.WithAlpha(0.45f);
            c.Rect(view.X, view.Y, view.W, 1, dim); c.Rect(view.X, view.Bottom - 1, view.W, 1, dim);
            c.Rect(view.X, view.Y, 1, view.H, dim); c.Rect(view.Right - 1, view.Y, 1, view.H, dim);
            Brackets(view, 16 * u, Math.Max(2f, 2.2f * u), Accent);
            c.Rect(view.X + 22 * u, view.Y, Math.Min(view.W * 0.25f, 70 * u), Math.Max(2f, 2.2f * u), T.Trim);
            return;
        }
        if (T.Hud)
        {
            // a thin dim border with bright corner brackets
            var dim = Accent.WithAlpha(0.35f);
            c.Rect(view.X, view.Y, view.W, 1, dim); c.Rect(view.X, view.Bottom - 1, view.W, 1, dim);
            c.Rect(view.X, view.Y, 1, view.H, dim); c.Rect(view.Right - 1, view.Y, 1, view.H, dim);
            Brackets(view, 18 * u, Math.Max(2f, 2.4f * u), Accent);
            return;
        }
        c.Rect(view.X, view.Y, view.W, w, Accent);
        c.Rect(view.X, view.Bottom - w, view.W, w, Accent);
        c.Rect(view.X, view.Y, w, view.H, Accent);
        c.Rect(view.Right - w, view.Y, w, view.H, Accent);
    }

    /// <summary>Frames the camera on the route (ship and destination), or on the ship, or on the whole system.</summary>
    /// <summary>The grid size of the view round your ship with no route (setting DefaultGridKm).</summary>
    public static double DefaultGridKm = MapSettings.DefaultGridDefault;
    /// <summary>Your ship's icon from a real ship of its class where one was sampled (setting RealShipIcons).</summary>
    public static bool RealShipIcons;
    /// <summary>Camera distance for that view: the grid is a nice step near distance / 3.5.</summary>
    public static double ShipViewDistance => DefaultGridKm * 1e3 * 3.5;

    /// <summary>Distance → grid step, as the grid draws it.</summary>
    public static double GridStepFor(double distance) => Nice(distance / 3.5);

    public void FrameDefault(bool snap)
    {
        if (Path != null) Cam.Frame(Path.Points(), 20e3);
        else if (Snap.HasShip) Cam.FlyTo(Snap.ShipPos, ShipViewDistance);
        else Cam.FlyTo(V3.Zero, 6e7);
        if (snap) Cam.Snap();
    }
}
