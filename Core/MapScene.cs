using System;
using System.Collections.Generic;

namespace SDX2_SystemMap;

/// <summary>
/// Draws the 3D system map into a box on an IMapCanvas: stars, an ecliptic grid, orbits, the Belt, Nexus sector
/// spheres, worlds at true size, trade stations, GPS markers, the ship with its velocity, and the Flip and Burn route
/// plan (burn / coast / brake legs, flip point, T+ marks, sector borders, close passes). Compact = the side preview
/// window; otherwise the full-screen map with its larger route panel.
/// </summary>
public sealed class MapScene
{
    // palette: from the current theme (MapTheme.Current)
    private static MapTheme T => MapTheme.Current;
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
    private RoutePlan Route => Path?.Final;
    public MapOptions Opt = new MapOptions();
    public bool Compact;
    /// <summary>Picture-in-picture (with Compact): a short route summary only, no legend or key help.</summary>
    public bool Mini;
    /// <summary>Mouse position (screen px) for hover highlight and tooltip; NaN = none.</summary>
    public float MouseX = float.NaN, MouseY = float.NaN;
    /// <summary>
    /// Key help for the bottom bar, set by the plugin: one group per line ('\n'), each "LABEL|item · item · ...". Groups
    /// are drawn as their own rows under the legend, wrapped at the " · " between items to fit the width.
    /// </summary>
    public string Help = "";
    /// <summary>The separator between help items.</summary>
    public const string Sep = "  ·  ";

    public readonly List<Pick> Picks = new List<Pick>();
    public Pick? Hovered;
    /// <summary>The bottom of the title bar (screen px), set by Draw: room for the plugin's own buttons below it.</summary>
    public float TitleBottom;

    private IMapCanvas c;
    private Box view;
    private float u;         // UI scale: 1 at a 720 px tall map
    public float Unit => u;
    private double planeY;   // the grid's height: the ecliptic, or the target's height when far from it
    private readonly List<Box> taken = new List<Box>();
    private readonly List<LabelReq> labels = new List<LabelReq>();
    private Box panelBox;

    private struct LabelReq
    {
        public string Text; public float X, Y, R, Px; public MapColor Col; public int Prio; public bool Boxed;
    }

    public void Draw(IMapCanvas canvas, Box box)
    {
        c = canvas; view = box;
        u = Math.Max(0.55f, Math.Min(2.2f, Math.Min(box.W * 1.35f, box.H) / 720f));
        if (Compact) u = Math.Max(u * 1.25f, 1f); // the side window is small: keep its text readable
        Cam.Setup(box.X, box.Y, box.W, box.H);
        planeY = Math.Abs(Cam.Target.Y) < Cam.Distance * 0.8 ? 0 : Cam.Target.Y;
        Picks.Clear(); taken.Clear(); labels.Clear(); Hovered = null;

        c.PushClip(box.X, box.Y, box.W, box.H);
        c.Rect(box.X, box.Y, box.W, box.H, Bg);
        Stars();
        if (Opt.Grid) Grid();
        if (Opt.Orbits) Orbits();
        if (Opt.Sectors) SectorSpheres();
        Worlds();
        if (Opt.Stations) Stations();
        if (Opt.Gps) GpsMarkers();
        if (Path != null) PathLines();
        TrackLines();
        AsteroidMarkers();
        Ship();
        FindHover();
        PlaceLabels();
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

    private void Label(string text, float x, float y, float r, MapColor col, int prio, float px = 0, bool boxed = false) =>
        labels.Add(new LabelReq { Text = text, X = x, Y = y, R = r, Col = col, Prio = prio, Px = px > 0 ? px : 11 * u, Boxed = boxed });

    private void AddPick(string name, string kind, string detail, V3 pos, float x, float y, float r) =>
        Picks.Add(new Pick { Name = name, Kind = kind, Detail = detail, Pos = pos, X = x, Y = y, R = r });

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

    private void Grid()
    {
        gridStep = GridStepFor(Cam.Distance);
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

    private void Orbits()
    {
        float w = Math.Max(1f, 0.8f * u);
        int oi = 0;
        foreach (var r in SystemData.OrbitRadii())
        {
            if (T.Style == ThemeStyle.Belter) DashedRing3(V3.Zero, r, 240, w, OrbitCol.WithAlpha(0.45f), 5, 2);
            else Ring3(V3.Zero, r, 160, w, OrbitCol.WithAlpha(T.Hud ? 0.5f : T.Style == ThemeStyle.Naval ? 0.42f : 0.32f));
            if (T.Hud) HudRing(r, oi);
            else if (T.Style == ThemeStyle.Naval) NavalRing(r);
            else if (T.Style == ThemeStyle.Belter) BelterRing(r, oi);
            oi++;
        }
        if (T.Style == ThemeStyle.Naval) BearingSpokes();
        var belt = SystemData.Sectors[SystemData.Sectors.Length - 2];
        var bc = T.SectorColor(belt.Color);
        Ring3(V3.Zero, belt.Radius - belt.HalfWidth, 180, w, bc.WithAlpha(0.3f));
        Ring3(V3.Zero, belt.Radius + belt.HalfWidth, 180, w, bc.WithAlpha(0.3f));
        Ring3(V3.Zero, belt.Radius, 180, Math.Max(1f, 4f * u), bc.WithAlpha(0.06f));
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
                if (T.Style == ThemeStyle.Naval)
                {
                    ScreenCircle(it.X, it.Y, it.R * 2.6f, Math.Max(1f, 1.2f * u), T.Trim.WithAlpha(0.8f), 64);
                    Reticle(it.X, it.Y, it.R * 2.6f, T.Trim.WithAlpha(0.8f));
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
            if (T.Hud && w.IsPlanet && w.Name != "Sol") ScreenCircle(it.X, it.Y, Math.Max(it.R, 6 * u) + 4.5f * u, Math.Max(1f, 1f * u), Accent.WithAlpha(0.6f), 32);
            if (T.Style == ThemeStyle.Naval && w.IsPlanet && w.Name != "Sol")
            {
                float rr = Math.Max(it.R, 4.5f * u) + 4 * u;
                ScreenCircle(it.X, it.Y, rr, Math.Max(1f, 0.9f * u), T.Fine.WithAlpha(0.55f), 32);
                Reticle(it.X, it.Y, rr, T.Trim.WithAlpha(0.85f));
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
            Diamond(x, y, 6.5f * u, col);
            Diamond(x, y, 3f * u, Bg);
            AddPick(s.Name, "Trade station", s.Gps, s.Pos, x, y, 5 * u);
            if (Opt.Labels && (dest || Cam.Distance < 6e6)) Label(s.Name, x, y, 5 * u, col, dest ? 2 : 7, 10 * u);
        }
    }

    private void GpsMarkers()
    {
        foreach (var m in Snap.Gps)
        {
            if (!P(m.Pos, out var x, out var y) || !OnView(x, y, 10)) continue;
            Stalk(m.Pos, m.Color.WithAlpha(0.3f));
            c.Disc(x, y, 3.6f * u, m.Color);
            c.Disc(x, y, 1.8f * u, Bg);
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
        if (speed > 1)
        {
            // velocity: a fixed 46 px arrow in the direction of travel
            double m = 46 * u / Cam.PixelsPerMetre(Cam.Depth(Snap.ShipPos));
            var tip = Snap.ShipPos + Snap.ShipVel / speed * m;
            if (P(tip, out var tx, out var ty))
            {
                c.Line(x, y, tx, ty, 2 * u, ShipCol.WithAlpha(0.9f));
                float dx = tx - x, dy = ty - y, l = (float)Math.Sqrt(dx * dx + dy * dy);
                if (l > 4)
                {
                    float ux = dx / l, uy = dy / l, h = 6 * u;
                    c.Line(tx, ty, tx - ux * h - uy * h * 0.6f, ty - uy * h + ux * h * 0.6f, 2 * u, ShipCol);
                    c.Line(tx, ty, tx - ux * h + uy * h * 0.6f, ty - uy * h - ux * h * 0.6f, 2 * u, ShipCol);
                }
            }
        }
        c.Disc(x, y, 9 * u, ShipCol.WithAlpha(0.18f));
        ScreenCircle(x, y, 7 * u, Math.Max(1f, 1.4f * u), ShipCol, 24);
        c.Disc(x, y, 3 * u, ShipCol);
        AddPick(Snap.ShipName, "Your ship", SystemData.SectorAt(Snap.ShipPos).Dx, Snap.ShipPos, x, y, 8 * u);
        Label(Snap.ShipName + (speed > 1 ? "  " + Fmt.Speed(speed) : ""), x, y, 9 * u, ShipCol, 1, 11 * u, true);
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

    private void PathLines()
    {
        curved = Snap.HasShip && Snap.Prediction.Count > 1 && Snap.PredictionPhases.Count == Snap.Prediction.Count;
        for (int i = 0; i < Path.Legs.Count; i++) RouteLines(Path.Legs[i], Path.LegStartTime(i), i == Path.Legs.Count - 1, i);
        if (curved) CurvedRoute();
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

    private void RouteLines(RoutePlan r, double t0, bool final, int legIndex)
    {
        float w = Math.Max(1.5f, 2.6f * u);
        void Leg(double a, double b, MapColor col)
        {
            if (b - a < 1) return;
            var pa = r.PointAt(a); var pb = r.PointAt(b);
            // split long legs so the near-plane clip and perspective stay right
            const int n = 24;
            for (int i = 0; i < n; i++)
            {
                var q0 = V3.Lerp(pa, pb, i / (double)n); var q1 = V3.Lerp(pa, pb, (i + 1) / (double)n);
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

        // T+ marks along the plan
        if (r.HasBurnPlan && r.Eta > 0 && !double.IsNaN(r.Eta))
        {
            double step = NiceTime(Math.Max(r.Eta, Path.Eta) / 6);
            for (double t = (Math.Floor(t0 / step) + 1) * step - t0; t < r.Eta - step * 0.3; t += step)
            {
                if (!P(r.PointAt(r.AlongAt(t)), out var tx, out var ty) || !OnView(tx, ty)) continue;
                c.Disc(tx, ty, 2.4f * u, TextCol);
                if (Opt.Labels && !Compact) Label("T+" + Fmt.Time(t0 + t), tx, ty, 3 * u, DimText, 10, 9 * u);
            }
        }

        // flip point
        if (r.HasBurnPlan && !r.TooFastToStop && P(r.FlipPoint, out var fx, out var fy))
        {
            Diamond(fx, fy, 10 * u, BrakeCol);
            Diamond(fx, fy, 5 * u, Bg);
            AddPick("Flip point", "Route", "T+" + Fmt.Time(t0 + r.BurnTime + r.CoastTime), r.FlipPoint, fx, fy, 6 * u);
            Label("FLIP  T+" + Fmt.Time(t0 + r.BurnTime + r.CoastTime), fx, fy, 7 * u, BrakeCol, 3, 10.5f * u, true);
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
        if (!final && P(r.Target, out var wx, out var wy))
        {
            int n = Path.Next + legIndex;
            var wp = Path.Waypoints[n];
            var wc = wp.User ? WaypointCol : DetourCol;
            ScreenCircle(wx, wy, 7 * u, 2 * u, wc, 20);
            c.Disc(wx, wy, 2.5f * u, wc);
            AddPick(wp.Name, wp.User ? "Your waypoint" : "Detour around " + (wp.Around?.Name ?? "?"), "pick it in F&B's list (N)", wp.Pos, wx, wy, 8 * u);
            Label((wp.User ? wp.Name : "D" + (n + 1)) + (legIndex == 0 ? "  next pick" : ""), wx, wy, 9 * u, wc, legIndex == 0 ? 1 : 3, 10.5f * u, true);
        }

        // destination
        if (final && P(r.Target, out var dx, out var dy))
        {
            Stalk(r.Target, BrakeCol.WithAlpha(0.6f));
            ScreenCircle(dx, dy, 10 * u, 2 * u, BrakeCol, 28);
            float t = 15 * u, g = 6 * u;
            c.Line(dx - t, dy, dx - g, dy, 2 * u, BrakeCol); c.Line(dx + g, dy, dx + t, dy, 2 * u, BrakeCol);
            c.Line(dx, dy - t, dx, dy - g, 2 * u, BrakeCol); c.Line(dx, dy + g, dx, dy + t, 2 * u, BrakeCol);
            AddPick(r.TargetName, "F&B destination", r.TargetSector.Dx, r.Target, dx, dy, 10 * u);
            Label(r.TargetName, dx, dy, 13 * u, BrakeCol, 0, 12 * u, true);
        }
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
            if (d >= 10 * u) continue;
            bool world = p.Kind == "Planet" || p.Kind == "Star" || p.Kind != null && p.Kind.StartsWith("Moon of");
            // a marker the mouse is on comes before any world; one only near the mouse ranks with the worlds
            float score = Math.Max(0, d) * 4 + p.R + (world || d > 3 * u ? 1000 : 0);
            if (score < best) { best = score; Hovered = p; }
        }
        if (Hovered is Pick h) ScreenCircle(h.X, h.Y, h.R + 5 * u, 1.5f * u, MapColor.White.WithAlpha(0.85f), 28);
    }

    private void PlaceLabels()
    {
        labels.Sort((a, b) => a.Prio.CompareTo(b.Prio));
        foreach (var l in labels)
        {
            float tw = c.TextWidth(l.Text, l.Px), th = l.Px, pad = 2 * u, gap = l.R + 3 * u;
            Box[] tries =
            {
                new Box(l.X + gap, l.Y - th / 2, tw, th), new Box(l.X - gap - tw, l.Y - th / 2, tw, th),
                new Box(l.X - tw / 2, l.Y - gap - th, tw, th), new Box(l.X - tw / 2, l.Y + gap, tw, th),
            };
            foreach (var t in tries)
            {
                var tb = t.Inflate(pad);
                if (!view.Contains(tb.X, tb.Y) || !view.Contains(tb.Right, tb.Bottom)) continue;
                bool free = true;
                foreach (var o in taken) if (o.Overlaps(tb)) { free = false; break; }
                if (!free) continue;
                taken.Add(tb);
                if (l.Boxed) c.Rect(tb.X, tb.Y, tb.W, tb.H, MapColor.Black.WithAlpha(0.6f));
                c.Text(l.Text, t.X, t.Y, l.Px, l.Col);
                break;
            }
        }
    }

    private void Panels()
    {
        float px = 12 * u, lh = px * 1.35f;
        // title bar
        string title = Mini ? "MAP" : Compact ? "ROUTE PREVIEW" : "SDX2 SYSTEM MAP";
        string sub = Route != null ? Route.TargetName : Snap.FnbReady ? "No Flip and Burn destination — press N and pick one" : "Flip and Burn not detected";
        c.Rect(view.X, view.Y, view.W, lh * 1.5f, PanelBg);
        TitleBottom = view.Y + lh * 1.5f;
        c.Rect(view.X, view.Y + lh * 1.5f - 2 * u, view.W, 2 * u, Accent.WithAlpha(0.8f));
        if (T.Hud) for (float tx = view.X + px; tx < view.Right - px; tx += 9 * u) c.Rect(tx, view.Y + lh * 1.5f - 6 * u, 1, 4 * u, Accent.WithAlpha(0.45f));
        if (T.Style == ThemeStyle.Naval) c.Rect(view.X, view.Y + lh * 1.5f, view.W, Math.Max(1f, 1f * u), T.Trim.WithAlpha(0.7f));
        if (T.Style == ThemeStyle.Belter) HazardStripe(view.X, view.Y + lh * 1.5f, view.W, 5 * u);
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
        if (lines.Count > 0)
        {
            float w = 0;
            foreach (var (t, _) in lines) w = Math.Max(w, c.TextWidth(t, px));
            w += px * 2;
            int half = (lines.Count + 1) / 2;               // compact: two columns
            float h = (Compact ? half : lines.Count) * lh + px;
            float x0 = Compact ? view.X : view.X + px, y0 = Compact ? view.Bottom - h - barH : view.Y + lh * 2.2f;
            if (Compact) w = view.W;
            panelBox = new Box(x0, y0, w, h);
            c.Rect(x0, y0, w, h, PanelBg);
            c.Rect(x0, y0, 2 * u, h, Accent.WithAlpha(0.8f));
            if (T.Hud) Brackets(new Box(x0, y0, w, h), 8 * u, Math.Max(1f, 1.5f * u), Accent.WithAlpha(0.8f));
            if (T.Style == ThemeStyle.Naval) { c.Rect(x0, y0, 2 * u, h, T.Trim.WithAlpha(0.9f)); c.Rect(x0, y0, w, 1, T.Trim.WithAlpha(0.5f)); }
            if (T.Style == ThemeStyle.Belter) Brackets(new Box(x0, y0, w, h), 7 * u, Math.Max(1f, 1.5f * u), T.Trim.WithAlpha(0.8f));
            float y = y0 + px * 0.5f;
            if (Compact)
            {
                for (int i = 0; i < lines.Count; i++)
                {
                    float cx = x0 + px + (i < half ? 0 : w / 2), cy = y + (i < half ? i : i - half) * lh;
                    c.Text(lines[i].Text, cx, cy, px * 0.95f, lines[i].Col);
                }
            }
            else for (int i = 0; i < lines.Count; i++) c.Text(lines[i].Text, x0 + px, y + i * lh, px, lines[i].Col);
        }

        // bottom bar: the legend and scale on the first row, then one row per help group (label in the accent colour)
        float by = view.Bottom - barH;
        if (!Mini) { c.Rect(view.X, by, view.W, barH, PanelBg); c.Rect(view.X, by, view.W, 1, Accent.WithAlpha(0.35f)); }
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
        if (!Mini && Opt.Grid && gridStep > 0) c.Text("grid " + Fmt.Dist(gridStep), view.Right - px, ry + rowH * 0.1f, hpx, DimText, TextAnchor.Right);
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
    public List<(string Text, MapColor Col)> RouteLinesText()
    {
        var l = new List<(string, MapColor)>();
        if (Path == null) return l;
        var r = Path.First; var fin = Path.Final;
        int ahead = Path.DetoursAhead, user = Path.UserAhead, det = ahead - user;
        l.Add(("Distance  " + Fmt.Dist(Path.Distance) + (det > 0 ? "  via " + det + (det == 1 ? " detour" : " detours") : "") + (user > 0 ? "  " + user + (user == 1 ? " waypoint" : " waypoints") : ""), TextCol));
        l.Add(("To  " + fin.TargetSector.Dx + " " + fin.TargetSector.Name + (fin.StationAtTarget != null ? "  (" + fin.StationAtTarget.Name + ")" : ""), TextCol));
        if (ahead > 0)
        {
            l.Add(("Pick next in N:  " + Path.Waypoints[Path.Next].Name, DetourCol));
            var names = new List<string>();
            for (int i = Path.Next; i < Path.Waypoints.Count; i++) { var n = Path.Waypoints[i].Around?.Name; if (n != null && !names.Contains(n)) names.Add(n); }
            if (names.Count > 0) l.Add(("Avoids  " + string.Join(", ", names) + "  (" + Fmt.Dist(Path.Clearance) + " clear)", DetourCol));
        }
        if (r.HasBurnPlan)
        {
            bool over = false; foreach (var g in Path.Legs) over |= g.TooFastToStop;
            l.Add(("ETA  " + Fmt.Time(Path.Eta) + (over ? "  (overshoot)" : "") + (Path.Legs.Count > 1 ? "  (stops at each point)" : ""), over ? WarnCol : BrakeCol));
            string leg = Path.Legs.Count > 1 ? "Leg 1:  " : "";
            l.Add((leg + "Peak  " + Fmt.Speed(r.PeakSpeed) + (r.CoastTime > 1 ? "  coast " + Fmt.Time(r.CoastTime) : ""), CoastCol));
            l.Add(("Burn  " + Fmt.Time(r.BurnTime) + "   brake " + Fmt.Time(r.BrakeTime), BurnCol));
            l.Add(("Flip in  " + Fmt.Dist(r.FlipAt) + "  (T+" + Fmt.Time(r.BurnTime + r.CoastTime) + ")", BrakeCol));
            l.Add(("Accel  " + Fmt.Num(r.MaxAccel, "0.0") + " m/s²  (" + Fmt.Num(r.MaxAccel / 9.81, "0.00") + " g)", DimText));
        }
        else l.Add(("ETA  " + (double.IsNaN(r.Eta) ? "-- (not closing)" : Fmt.Time(r.Eta) + " at current speed"), DimText));
        l.Add(("Closing  " + Fmt.Speed(r.ClosingSpeed) + "   drift " + Fmt.Speed(r.LateralSpeed), r.LateralSpeed > Math.Max(50, Math.Abs(r.ClosingSpeed) * 0.1) ? BorderCol : DimText));
        int borders = 0;
        double along = 0;
        foreach (var g in Path.Legs)
        {
            foreach (var x in g.Crossings) { borders++; l.Add(("Border " + x.From.Dx + " → " + x.To.Dx + " " + x.To.Name + " at " + Fmt.Dist(along + x.At), BorderCol)); }
            foreach (var h in g.Hazards) l.Add(((h.Blocked ? "BLOCKED by " + h.World.Name : "Passes " + h.World.Name + " at " + Fmt.Dist(h.Clearance)) + " (" + Fmt.Dist(along + h.At) + " along)", WarnCol));
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

    private void Frame()
    {
        float w = Math.Max(1f, 1.5f * u);
        if (T.Style == ThemeStyle.Naval)
        {
            // a thin outer line, a gold inner line and gold corner squares
            c.Rect(view.X, view.Y, view.W, w, Accent); c.Rect(view.X, view.Bottom - w, view.W, w, Accent);
            c.Rect(view.X, view.Y, w, view.H, Accent); c.Rect(view.Right - w, view.Y, w, view.H, Accent);
            float i = 3 * u, g = Math.Max(1f, 0.8f * u), s = 6 * u;
            var gold = T.Trim.WithAlpha(0.7f);
            c.Rect(view.X + i, view.Y + i, view.W - 2 * i, g, gold); c.Rect(view.X + i, view.Bottom - i - g, view.W - 2 * i, g, gold);
            c.Rect(view.X + i, view.Y + i, g, view.H - 2 * i, gold); c.Rect(view.Right - i - g, view.Y + i, g, view.H - 2 * i, gold);
            foreach (var (cx, cy) in new[] { (view.X, view.Y), (view.Right - s, view.Y), (view.X, view.Bottom - s), (view.Right - s, view.Bottom - s) })
                c.Rect(cx, cy, s, s, T.Trim);
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
