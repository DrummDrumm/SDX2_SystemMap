using System;
using System.Collections.Generic;

namespace SDX2_SystemMap;

/// <summary>
/// The full map's route panel: each theme has its own place, shape and widgets.
/// <list type="bullet">
/// <item>Classic: a plain card at the top left with bar meters and the timeline.</item>
/// <item>Tachi: a cut-corner tactical block on a lit spine, centred on the left edge, with speed and acceleration
/// tapes, big readouts and the segmented timeline.</item>
/// <item>UNN: a full-height plotting sheet docked to the left edge, with a compass (course and velocity bearings), a
/// speed-against-time graph and a ruled log.</item>
/// <item>OPA: stacked salvage cards at the top left: readout tiles, block gauges with status lights, and a route log.</item>
/// <item>Protogen: a strip along the bottom with a tabbed header, a ring timeline, ruler gauges and the rows in columns.</item>
/// <item>Belter: a wide chamfered chart card with dials and a range track beside boxed rows.</item>
/// </list>
/// </summary>
public sealed partial class MapScene
{
    private struct PanelRow { public string Key, Val; public MapColor Col; }

    private sealed class PanelText
    {
        public readonly List<PanelRow> Rows = new List<PanelRow>();
        public float Kw, Vw, Px, Lh, Kpx, Gap;
    }

    private double ShipSpeed => Snap.HasShip ? Snap.ShipVel.Length : 0;
    private static double SpeedTop(RoutePlan p, double v) => Math.Max(1, Math.Max(p.PeakSpeed, v) * 1.15);
    private static (double G, double Top) Gs(RoutePlan p)
    {
        double g = p.MaxAccel / 9.81;
        return (g, g <= 1 ? 1 : g <= 2 ? 2 : Math.Ceiling(g));
    }
    private string LegEnd => Path.Legs.Count > 1 ? "LEG 1 " : "ETA ";
    private static float Clamp01(float f) => float.IsNaN(f) ? 0 : Math.Max(0, Math.Min(1, f));

    // ------------------------------------------------------------------ sides and the autopilot controls

    /// <summary>The time in seconds (unused now the side is a key; kept for the tests' clock); NaN = the system clock.</summary>
    public double Now = double.NaN;
    /// <summary>The route panel's place on screen this frame (empty when there's none), the controls card included.</summary>
    public Box PanelBox => panelBox;
    /// <summary>The route panel is on the right, laid out as the mirror image of the left (a key moves it).</summary>
    public bool PanelOnRight;
    /// <summary>
    /// The height the autopilot controls card needs at the panel's text size px (0 = no card); the panel keeps a slot
    /// for it at the top of its stack, which <see cref="ControlsSlot"/> gives on screen for the owner to draw into.
    /// </summary>
    public Func<float, float> ControlsHeight;
    /// <summary>Where the autopilot controls card goes this frame (screen coordinates; empty = none).</summary>
    public Box ControlsSlot { get; private set; }
    /// <summary>The panel's text size this frame, for the controls card.</summary>
    public float PanelPx { get; private set; }

    private Box slot;
    private void ControlsSlotClear() { slot = default; ControlsSlot = default; }

    private float PanelTop(float lh) => view.Y + lh * 2.2f;

    /// <summary>The controls card's height at text size px (0 = none).</summary>
    private float SlotH(float px) => ControlsHeight?.Invoke(px) ?? 0;

    /// <summary>Keeps the slot for the controls card at (x, y), w wide; returns where the panel goes on below it.</summary>
    private float Slot(float x, float y, float w, float px)
    {
        float h = SlotH(px);
        if (h <= 0) return y;
        slot = new Box(x, y, w, h);
        return y + h + px * 0.55f;
    }

    /// <summary>
    /// The route panel on the left, or on the right as its mirror image: the frames, headers and rows flipped across
    /// the screen (labels to the outer edge, values beside them), with the gauges, dials and graphs kept the right way
    /// round so they still read true.
    /// </summary>
    private void SidedRoutePanel(List<(string Text, MapColor Col)> lines, float px, float lh, RoutePlan plan, bool timeline, float bottom)
    {
        slot = default; PanelPx = px;
        var real = c;
        float axis = view.X + view.Right;
        if (PanelOnRight) c = new Mirror(real, axis);
        try { RoutePanel(lines, px, lh, plan, timeline, bottom); }
        finally { c = real; }
        if (PanelOnRight)
        {
            panelBox = new Box(axis - panelBox.Right, panelBox.Y, panelBox.W, panelBox.H);
            if (slot.W > 0) slot = new Box(axis - slot.Right, slot.Y, slot.W, slot.H);
        }
        ControlsSlot = slot;
        if (slot.W > 0) panelBox = Union(panelBox, slot);
    }

    private static Box Union(Box a, Box b)
    {
        if (a.W <= 0) return b;
        float x = Math.Min(a.X, b.X), y = Math.Min(a.Y, b.Y);
        return new Box(x, y, Math.Max(a.Right, b.Right) - x, Math.Max(a.Bottom, b.Bottom) - y);
    }

    /// <summary>Draws the mirror image across x = axis / 2; text keeps reading left to right, its anchor flipped.
    /// Inside a <see cref="Keep"/> region the drawing is only moved across, not flipped (gauges, graphs, dials).</summary>
    private sealed class Mirror : IMapCanvas
    {
        private readonly IMapCanvas c; private readonly float a;
        private bool keep; private float shift;
        public Mirror(IMapCanvas c, float axis) { this.c = c; a = axis; }
        public void Keep(Box r) { keep = true; shift = a - r.Right - r.X; }
        public void Release() => keep = false;
        private float X(float x) => keep ? x + shift : a - x;
        public void Rect(float x, float y, float w, float h, MapColor col, float rotation = 0)
        {
            if (keep) c.Rect(x + shift, y, w, h, col, rotation);
            else c.Rect(a - x - w, y, w, h, col, -rotation);
        }
        public void Line(float x1, float y1, float x2, float y2, float width, MapColor col) => c.Line(X(x1), y1, X(x2), y2, width, col);
        public void Disc(float cx, float cy, float r, MapColor col) => c.Disc(X(cx), cy, r, col);
        public void Text(string text, float x, float y, float px, MapColor col, TextAnchor anchor = TextAnchor.Left)
        {
            if (keep) { c.Text(text, x + shift, y, px, col, anchor); return; }
            c.Text(text, a - x, y, px, col, anchor == TextAnchor.Left ? TextAnchor.Right : anchor == TextAnchor.Right ? TextAnchor.Left : anchor);
        }
        public float TextWidth(string text, float px) => c.TextWidth(text, px);
        public void PushClip(float x, float y, float w, float h) => c.PushClip(keep ? x + shift : a - x - w, y, w, h);
        public void PopClip() => c.PopClip();
    }

    /// <summary>Within r, draw the right way round even on the mirrored side (dispose to end it).</summary>
    private Upright Up(float x, float y, float w, float h)
    {
        var m = c as Mirror;
        m?.Keep(new Box(x, y, w, h));
        return new Upright(m);
    }

    private readonly struct Upright : IDisposable
    {
        private readonly Mirror m;
        public Upright(Mirror m) { this.m = m; }
        public void Dispose() => m?.Release();
    }

    /// <summary>The full map's route panel for the current theme; bottom = the bottom bar's top.</summary>
    private void RoutePanel(List<(string Text, MapColor Col)> lines, float px, float lh, RoutePlan plan, bool timeline, float bottom)
    {
        // split each line at its first double space: "ETA  31 m 58 s" → ETA | 31 m 58 s
        var t = new PanelText { Px = px, Lh = px * 1.22f, Kpx = px * 0.92f, Gap = px * 0.9f }; // tight rows
        foreach (var (text, col) in lines)
        {
            int i = text.IndexOf("  ", StringComparison.Ordinal);
            bool keyed = i > 0 && i <= 18;
            var r = new PanelRow { Key = keyed ? text.Substring(0, i).TrimEnd(':').ToUpperInvariant() : "", Val = keyed ? text.Substring(i).Trim() : text, Col = col };
            t.Rows.Add(r);
            if (keyed) t.Kw = Math.Max(t.Kw, c.TextWidth(r.Key, t.Kpx));
        }
        // a fixed size, so the panel doesn't grow and shrink as the numbers change: the key column fits the longest key
        // the plan writes, the value column a fixed width; a longer value is shortened (and then its text made smaller) to fit
        t.Kw = Math.Max(t.Kw, c.TextWidth(KeyWidest, t.Kpx));
        t.Vw = px * ValueCols;
        float top = view.Y + lh * 1.5f;
        switch (T.Style)
        {
            case ThemeStyle.Tachi: TachiPanel(t, plan, timeline, top, bottom); break;
            case ThemeStyle.Naval: NavalPanel(t, plan, timeline, top, bottom); break;
            case ThemeStyle.Belter: OpaPanel(t, plan, timeline); break;
            case ThemeStyle.Protogen: ProtoPanel(t, plan, timeline, bottom); break;
            case ThemeStyle.Contact: ContactPanel(t, plan, timeline); break;
            default: ClassicPanel(t, plan, timeline); break;
        }
    }

    /// <summary>The longest key the plan's rows use; the key column is always this wide.</summary>
    private const string KeyWidest = "PICK NEXT IN N";
    /// <summary>The value column's fixed width, in text heights.</summary>
    internal const float ValueCols = 15.5f;

    /// <summary>The width rows take: the key column, the gap and the fixed value column.</summary>
    private float RowsW(PanelText t, int from, int to) => t.Kw + t.Gap + t.Vw;

    /// <summary>Shorter forms tried in order when a value doesn't fit its column.</summary>
    private static readonly (string From, string To)[] Shorter =
    {
        ("  (curves through the points)", "  (curved)"), ("  (slows to turn at each point)", "  (turns at points)"),
        (" at current speed", " now"), (" (not closing)", ""), ("   drift ", "  drift "), ("   brake ", "  brake "),
        (" waypoints", " wpts"), (" waypoint", " wpt"), (" detours", " det"), (" detour", " det"), ("  via ", "  "),
        (" Station)", ")"), (" Station", " Stn"), ("Border ", ""), (" along)", ")"), (" no-fly", ""), (" clear)", ")"),
        ("  coast ", "  cst "), (" (no Nexus transfer)", ""),
    };

    /// <summary>Value text that fits w at size px: shortened with <see cref="Shorter"/>, then drawn smaller (to 75%) if it still doesn't fit.</summary>
    private string FitText(string s, float px, float w, out float fp)
    {
        fp = px;
        if (c.TextWidth(s, px) <= w) return s;
        foreach (var (a, b) in Shorter)
        {
            if (s.IndexOf(a, StringComparison.Ordinal) < 0) continue;
            s = s.Replace(a, b);
            if (c.TextWidth(s, px) <= w) return s;
        }
        fp = Math.Max(px * 0.75f, px * w / Math.Max(1f, c.TextWidth(s, px)));
        return s;
    }

    // ------------------------------------------------------------------ layouts

    private void ClassicPanel(PanelText t, RoutePlan p, bool tl)
    {
        float px = t.Px, w = Math.Max(px * 22, px * 2 + t.Kw + t.Gap + t.Vw);
        float top = tl ? px * 4.6f + PanelStyle.TimeBarH(px) + px * 0.3f : 0;
        float y0 = Slot(view.X + px, PanelTop(t.Lh), w, px);
        var b = new Box(view.X + px, y0, w, PanelStyle.HeaderH(px) + top + t.Rows.Count * t.Lh + px * 0.6f);
        panelBox = b;
        float y = PanelStyle.Frame(c, b, u, px, "FLIGHT PLAN", Path.Final.TargetSector.Dx);
        if (tl)
        {
            float x = b.X + px, iw = b.W - px * 2;
            using var up = Up(x, y, iw, top);
            double v = ShipSpeed, vt = SpeedTop(p, v);
            var (g, gt) = Gs(p);
            Meter(x, y, iw, px, "SPEED", Fmt.Speed(v) + "   peak " + Fmt.Speed(p.PeakSpeed), (float)(v / vt), (float)(p.PeakSpeed / vt), ShipCol);
            Meter(x, y + px * 2.3f, iw, px, "ACCEL", Fmt.Num(g, "0.00") + " g   of " + Fmt.Num(gt, "0") + " g", (float)(g / gt), float.NaN, BurnCol);
            PanelStyle.TimeBar(c, new Box(x, y + px * 4.6f, iw, 0), u, px, p.BurnTime, p.CoastTime, p.BrakeTime, LegEnd);
            y += top;
        }
        PanelRows(t, 0, t.Rows.Count, b.X + px, y, b.W - px * 2);
    }

    private void TachiPanel(PanelText t, RoutePlan p, bool tl, float top, float bottom)
    {
        float px = t.Px, lh = t.Lh, spine = px * 0.7f, k = px * 1.6f;
        float w = Math.Max(px * 24, px * 2.4f + t.Kw + t.Gap + t.Vw);
        float wid = tl ? px * 8.6f + PanelStyle.TimeBarH(px) + px * 0.4f : 0;
        float h = PanelStyle.HeaderH(px) + wid + t.Rows.Count * lh + px * 1.2f;
        float sx = view.X + px * 0.5f, x0 = sx + spine + px * 0.4f;
        float sh = SlotH(px), sgap = sh > 0 ? sh + px * 0.55f : 0;
        float y0 = Math.Max(top + lh * 0.7f + sgap, Math.Min((top + bottom) / 2 - (h - sgap) / 2, bottom - h - px * 0.6f));
        Slot(x0, y0 - sgap, w, px);
        var b = new Box(x0, y0, w, h);
        panelBox = new Box(view.X, y0, b.Right - view.X, h);
        // the spine: a lit bar down the left with ladder ticks; the block cut at its top-right and bottom-left corners
        float lw = Math.Max(1f, u);
        c.Rect(sx, y0, spine, h, Accent.WithAlpha(0.2f));
        c.Rect(sx, y0, spine, px * 2.4f, Accent.WithAlpha(0.85f));
        c.Rect(sx, y0 + h - px * 0.8f, spine, px * 0.8f, Accent.WithAlpha(0.85f));
        for (float ty = y0 + px * 3; ty < y0 + h - px * 1.2f; ty += 6 * u) c.Rect(sx + spine * 0.25f, ty, spine * 0.5f, lw, Accent.WithAlpha(0.6f));
        FillCut(b, k, false, true, false, true, PanelBg);
        OutlineCut(b, k, false, true, false, true, Math.Max(1f, 1.2f * u), Accent.WithAlpha(0.75f));
        float y = PanelStyle.Header(c, new Box(b.X, b.Y, b.W - k, b.H), u, px, "TAC // FLIGHT PLAN", Path.Final.TargetSector.Dx);
        if (tl)
        {
            double v = ShipSpeed, vt = SpeedTop(p, v);
            var (g, gt) = Gs(p);
            float x = b.X + px * 1.2f, tw = px * 2.4f;
            using var up = Up(b.X, y, b.W, wid);
            Tape(x, y, tw, px * 5.6f, px, "SPD", Fmt.Speed(v), (float)(v / vt), (float)(p.PeakSpeed / vt), ShipCol);
            Tape(x + tw + px * 1.5f, y, tw, px * 5.6f, px, "ACC", Fmt.Num(g, "0.00") + " g", (float)(g / gt), float.NaN, BurnCol);
            // big readouts right of the tapes
            float rx = x + tw * 2 + px * 3f, rw = b.Right - px * 1.2f - rx, ry = y;
            c.Rect(rx - px * 0.7f, y, lw, px * 8f, Accent.WithAlpha(0.5f));
            foreach (var (key, val, col) in new[] { ("ETA", Fmt.Time(Path.Eta), BrakeCol), ("FLIP", "T+" + Fmt.Time(p.BurnTime + p.CoastTime), T.Trim), ("RANGE", Fmt.Dist(Path.Distance), TextCol) })
            {
                c.Text(key, rx, ry, px * 0.72f, DimText);
                float bp = Math.Min(px * 1.5f, rw / Math.Max(1f, c.TextWidth(val, 1)));
                c.Text(val, rx, ry + px * 0.8f, bp, col);
                ry += px * 2.7f;
            }
            PanelStyle.TimeBar(c, new Box(x, y + px * 8.6f, b.W - px * 2.4f, 0), u, px, p.BurnTime, p.CoastTime, p.BrakeTime, LegEnd);
            y += wid;
        }
        PanelRows(t, 0, t.Rows.Count, b.X + px * 1.2f, y, b.W - px * 2.4f);
    }

    private void NavalPanel(PanelText t, RoutePlan p, bool tl, float top, float bottom)
    {
        float px = t.Px, lh = t.Lh, lw = Math.Max(1f, u);
        float w = Math.Max(px * 25, px * 2.6f + t.Kw + t.Gap + t.Vw);
        // the sheet ends two blank log lines under the rows (not at the foot of the screen)
        float sh = SlotH(px);
        float need = (sh > 0 ? px * 0.6f + sh + px * 0.55f : px * 0.3f) + PanelStyle.HeaderH(px) + (tl ? px * 2.9f * 2 + px * 1.9f : 0)
            + px * 1.3f + t.Rows.Count * lh + lh * 2 + px * 0.6f;
        var b = new Box(view.X, top, w, Math.Min(bottom - top, need));
        panelBox = b;
        c.Rect(b.X, b.Y, b.W, b.H, PanelBg);
        // the sheet's edge: a gold double rule down the right with ruler ticks
        c.Rect(b.Right - 4 * u, b.Y, lw, b.H, T.Trim.WithAlpha(0.8f));
        c.Rect(b.Right - lw, b.Y, lw, b.H, T.Trim.WithAlpha(0.5f));
        int n = 0;
        for (float ty = b.Y + px; ty < b.Bottom - px * 0.5f; ty += 10 * u, n++)
        {
            float len = (n % 5 == 0 ? 7 : 3) * u;
            c.Rect(b.Right - 4 * u - len, ty, len, lw, Accent.WithAlpha(0.5f));
        }
        float hy = Slot(b.X + px * 0.6f, b.Y + px * 0.6f, b.W - px * 1.8f, px);
        float y = PanelStyle.Header(c, new Box(b.X + px * 0.3f, hy == b.Y + px * 0.6f ? b.Y + px * 0.3f : hy, b.W - px * 0.9f, b.H), u, px, "NAVIGATION PLOT", Path.Final.TargetSector.Dx);
        float x = b.X + px * 1.3f, iw = b.W - px * 2.6f;
        if (tl)
        {
            using var up = Up(x, y, iw, px * 2.9f * 2 + px * 1.9f);
            float r = px * 2.9f, cx = x + r, cy = y + r + px * 0.3f;
            Compass(cx, cy, r, px, p);
            float gx = cx + r + px * 1.6f;
            SpeedProfile(new Box(gx, y + px * 0.4f, x + iw - gx, r * 2 - px * 0.4f), px, p);
            y += r * 2 + px * 1.9f;
        }
        float lp = px * 0.75f, ltw = c.TextWidth("LOG", lp);
        c.Text("LOG", x, y, lp, T.Trim);
        c.Rect(x + ltw + px * 0.5f, y + lp * 0.55f, iw - ltw - px * 0.5f, lw, T.Trim.WithAlpha(0.6f));
        y += px * 1.3f;
        PanelRows(t, 0, t.Rows.Count, x, y, iw);
        // blank ruled log lines down to the foot of the sheet
        for (float ly = y + t.Rows.Count * lh + lh * 0.9f - lw; ly < b.Bottom - px; ly += lh) c.Rect(x - px * 0.5f, ly, iw + px, lw, T.Trim.WithAlpha(0.1f));
    }

    private void OpaPanel(PanelText t, RoutePlan p, bool tl)
    {
        float px = t.Px, lh = t.Lh, gap = px * 0.55f, w = Math.Max(px * 24, px * 2.2f + t.Kw + t.Gap + t.Vw);
        float x0 = view.X + px, y0 = PanelTop(lh), y = Slot(x0, y0, w, px), x = x0 + px, iw = w - px * 2;
        if (tl)
        {
            // card 1: readout tiles
            var c1 = new Box(x0, y, w, PanelStyle.HeaderH(px) + px * 3.4f);
            float ty = PanelStyle.Frame(c, c1, u, px, "BURN SHEET", Path.Final.TargetSector.Dx);
            using (Up(x, ty, iw, px * 2.8f)) Tiles(x, ty, iw, px, new[] { ("DIST", Fmt.Dist(Path.Distance), TextCol), ("ETA", Fmt.Time(Path.Eta), BrakeCol), ("PEAK", Fmt.Speed(p.PeakSpeed), CoastCol) });
            y = c1.Bottom + gap;
            // card 2: block gauges and status lights
            var c2 = new Box(x0, y, w, px * 5.6f);
            float gy = PanelStyle.Frame(c, c2, u, px, null) + px * 0.1f;
            double v = ShipSpeed, vt = SpeedTop(p, v);
            var (g, gt) = Gs(p);
            using (Up(x, gy, iw, px * 4.2f))
            {
                Blocks(x, gy, iw, px, "SPEED", Fmt.Speed(v), (float)(v / vt), (float)(p.PeakSpeed / vt), ShipCol);
                Blocks(x, gy + px * 1.6f, iw, px, "THRUST", Fmt.Num(g, "0.00") + " g", (float)(g / gt), float.NaN, BurnCol);
                Lights(x, gy + px * 3.3f, iw, px);
            }
            y = c2.Bottom + gap;
        }
        // card 3: the timeline and the route log
        float bar = tl ? PanelStyle.TimeBarH(px) + px * 0.4f : 0;
        var c3 = new Box(x0, y, w, PanelStyle.HeaderH(px) + bar + t.Rows.Count * lh + px * 1.1f);
        float ry = PanelStyle.Frame(c, c3, u, px, "ROUTE LOG", tl ? null : Path.Final.TargetSector.Dx);
        if (tl)
        {
            using (Up(x, ry, iw, bar)) PanelStyle.TimeBar(c, new Box(x, ry, iw, 0), u, px, p.BurnTime, p.CoastTime, p.BrakeTime, LegEnd);
            ry += bar;
        }
        PanelRows(t, 0, t.Rows.Count, x + px * 0.2f, ry, iw);
        panelBox = new Box(x0, y0, w, c3.Bottom - y0);
    }

    private void ProtoPanel(PanelText t, RoutePlan p, bool tl, float bottom)
    {
        float px = t.Px, lh = t.Lh, R = px * 3.3f, lw = Math.Max(1f, u);
        float ringW = tl ? R * 2 + px * 1.4f : 0, rulW = tl ? px * 13f : 0;
        float maxRight = view.Right - px * 33;   // the autopilot controls sit at the top right
        int n = t.Rows.Count, half = (n + 1) / 2;
        float one = RowsW(t, 0, n), two = RowsW(t, 0, half) + px * 1.5f + RowsW(t, half, n);
        float lead = px + (tl ? ringW + px + rulW + px * 1.2f : 0);
        bool cols2 = view.X + px + lead + two + px <= maxRight;
        int rowsN = cols2 ? half : n;
        float bodyH = Math.Max(tl ? R * 2 + px * 1.4f : 0, rowsN * lh + px * 0.4f) + px * 0.6f;
        float w = lead + (cols2 ? two : one) + px, tabH = px * 1.9f;
        var b = new Box(view.X + px, bottom - px * 0.6f - bodyH, w, bodyH);
        panelBox = new Box(b.X, b.Y - tabH, b.W, b.H + tabH);
        // the controls card stands on the strip, over its tab
        float sh = SlotH(px);
        if (sh > 0) Slot(b.X, b.Y - tabH - px * 0.55f - sh, Math.Min(w, px * 26), px);
        // the tab: the title on a cut-corner tab standing on the strip's top left
        string title = "NAV SOLUTION";
        float tp = px * 0.9f, tw = c.TextWidth(title, tp) + c.TextWidth(Path.Final.TargetSector.Dx, tp) + px * 3.6f, k = px * 1.1f, kb = px * 1.4f;
        var acc = Accent.WithAlpha(0.9f);
        FillCut(new Box(b.X, b.Y - tabH, tw, tabH + lw), k, false, true, false, false, PanelBg);
        FillCut(b, kb, false, true, false, false, PanelBg);
        c.Line(b.X, b.Y - tabH, b.X + tw - k, b.Y - tabH, lw, acc);
        c.Line(b.X + tw - k, b.Y - tabH, b.X + tw, b.Y - tabH + k, lw, acc);
        c.Line(b.X + tw, b.Y - tabH + k, b.X + tw, b.Y, lw, acc);
        c.Line(b.X + tw, b.Y, b.Right - kb, b.Y, lw, acc);
        c.Line(b.Right - kb, b.Y, b.Right, b.Y + kb, lw, acc);
        c.Line(b.Right, b.Y + kb, b.Right, b.Bottom, lw, Accent.WithAlpha(0.5f));
        c.Line(b.X, b.Y - tabH, b.X, b.Bottom, lw, Accent.WithAlpha(0.5f));
        c.Rect(b.X, b.Bottom - lw, b.W, lw, Accent.WithAlpha(0.5f));
        c.Rect(b.X + 2 * u, b.Y - tabH + 2 * u, Math.Min(tw * 0.3f, 40 * u), 2 * u, T.Trim.WithAlpha(0.85f));
        float ty = b.Y - tabH + (tabH - tp) / 2;
        c.Text("+", b.X + px * 0.5f, ty, tp, Accent);
        c.Text(title, b.X + px * 1.5f, ty, tp, T.Fine);
        c.Text(Path.Final.TargetSector.Dx, b.X + tw - px * 1.3f, ty, tp, T.Trim, TextAnchor.Right);
        if (b.Right - kb - px > b.X + tw + px) RulerTicks(b.X + tw + px, b.Right - kb - px, b.Y, 1);
        float x = b.X + px, y = b.Y + px * 0.5f;
        if (tl)
        {
            float cx = x + R + px * 0.5f, cy = b.Y + b.H / 2;
            float rx = x + ringW + px, ry = cy - px * 3.6f;
            using (Up(x, b.Y, rx + rulW - x, b.H))
            {
                Ring(cx, cy, R, px, p);
                double v = ShipSpeed, vt = SpeedTop(p, v);
                var (g, gt) = Gs(p);
                Ruler(rx, ry, rulW, px, "SPEED", Fmt.Speed(v), (float)(v / vt), (float)(p.PeakSpeed / vt), ShipCol);
                Ruler(rx, ry + px * 2.45f, rulW, px, "ACCEL", Fmt.Num(g, "0.00") + " g", (float)(g / gt), float.NaN, BurnCol);
                Ruler(rx, ry + px * 4.9f, rulW, px, "FLIP AT", Fmt.Dist(p.FlipAt), (float)(p.FlipAt / Math.Max(1, p.Distance)), float.NaN, T.Trim);
            }
            x = rx + rulW + px * 1.2f;
            c.Rect(x - px * 0.6f, b.Y + px * 0.5f, lw, b.H - px, Accent.WithAlpha(0.35f));
        }
        PanelRows(t, 0, rowsN, x, y, RowsW(t, 0, rowsN));
        if (cols2) PanelRows(t, half, n, x + RowsW(t, 0, half) + px * 1.5f, y, RowsW(t, half, n));
    }

    private void ContactPanel(PanelText t, RoutePlan p, bool tl)
    {
        float px = t.Px, lh = t.Lh, leftW = tl ? px * 19f : 0, dialR = px * 2.6f;
        float rowsW = RowsW(t, 0, t.Rows.Count) + px * 0.7f;
        float leftH = tl ? dialR * 2 + px * 1.6f + px * 3.2f : 0;
        float w = Math.Max(px * 22, px * 2.4f + leftW + (tl ? px * 1.6f : 0) + rowsW);
        float h = PanelStyle.HeaderH(px) + Math.Max(leftH, t.Rows.Count * lh + px * 0.2f) + px * 0.9f;
        var b = new Box(view.X + px, Slot(view.X + px, PanelTop(lh), w, px), w, h);
        panelBox = b;
        float y = PanelStyle.Frame(c, b, u, px, "FLIGHT PLAN", Path.Final.TargetSector.Dx);
        float x = b.X + px * 1.2f;
        if (tl)
        {
            using (Up(x, y, leftW, leftH))
            {
                double v = ShipSpeed, vt = SpeedTop(p, v);
                var (g, gt) = Gs(p);
                float cy = y + dialR + px * 0.4f;
                PanelStyle.Dial(c, x + leftW * 0.25f, cy, dialR, u, px, (float)(v / vt), (float)(p.PeakSpeed / vt), Fmt.Speed(v), "SPEED", ShipCol);
                PanelStyle.Dial(c, x + leftW * 0.75f, cy, dialR, u, px, (float)(g / gt), float.NaN, Fmt.Num(g, "0.00") + " g", "ACCEL", BurnCol);
                RangeTrack(new Box(x + px * 0.5f, y + dialR * 2 + px * 1.6f, leftW - px, px * 2.8f), px, p);
            }
            float sx = x + leftW + px * 0.7f;
            c.Rect(sx, y, Math.Max(1f, u), b.Bottom - y - px * 0.6f, Accent.WithAlpha(0.4f));
            x = sx + px * 0.9f;
        }
        PanelRows(t, 0, t.Rows.Count, x + px * 0.35f, y + px * 0.1f, rowsW);
    }

    // ------------------------------------------------------------------ rows

    /// <summary>Rows from..to as key / value lines from (x, y), w wide, decorated in the theme's style.</summary>
    private void PanelRows(PanelText t, int from, int to, float x, float y, float w)
    {
        float px = t.Px, lh = t.Lh, kpx = t.Kpx, vx = x + t.Kw + t.Gap, lw = Math.Max(1f, u);
        for (int i = from; i < to; i++)
        {
            var r = t.Rows[i];
            bool keyed = r.Key.Length > 0;
            MapColor kc = DimText;
            switch (T.Style)
            {
                case ThemeStyle.Tachi:
                    if (keyed)
                    {
                        c.Rect(x - px * 0.35f, y - lh * 0.06f, t.Kw + px * 0.7f, lh * 0.84f, Accent.WithAlpha(0.15f));
                        c.Rect(x - px * 0.35f, y - lh * 0.06f, Math.Max(1.5f, 2 * u), lh * 0.84f, Accent.WithAlpha(0.9f));
                    }
                    else c.Text("»", x, y, px, Accent);
                    kc = T.Fine;
                    break;
                case ThemeStyle.Naval:
                    if ((i - from) % 2 == 0) c.Rect(x - px * 0.5f, y - lh * 0.1f, w + px, lh, Accent.WithAlpha(0.07f));
                    c.Rect(x - px * 0.5f, y + lh * 0.9f - lw, w + px, lw, T.Trim.WithAlpha(0.22f));
                    if (keyed) c.Rect(vx - t.Gap * 0.45f, y - lh * 0.1f, lw, lh, T.Trim.WithAlpha(0.35f));
                    else c.Rect(x, y + px * 0.5f, px * 0.6f, Math.Max(1.5f, 1.5f * u), T.Trim);
                    kc = T.Trim.WithAlpha(0.9f);
                    break;
                case ThemeStyle.Belter:
                    if (keyed) c.Rect(x - px * 0.45f, y + px * 0.2f, Math.Max(2f, 3 * u), px * 0.6f, r.Col);
                    else c.Rect(x, y + px * 0.25f, px * 0.5f, px * 0.5f, r.Col);
                    kc = Accent;
                    break;
                case ThemeStyle.Protogen:
                    if (keyed)
                    {
                        // a dotted leader from the key to its value
                        for (float dx = x + c.TextWidth(r.Key, kpx) + px * 0.35f; dx < vx - px * 0.4f; dx += 5 * u) c.Rect(dx, y + px * 0.72f, lw, lw, Accent.WithAlpha(0.45f));
                    }
                    else c.Text("+", x, y, px, Accent);
                    break;
                case ThemeStyle.Contact:
                    if (keyed)
                    {
                        c.Rect(x - px * 0.35f, y - lh * 0.06f, t.Kw + px * 0.7f, lh * 0.84f, MapColor.Lerp(PanelBg, Accent, 0.16f));
                        c.Rect(x - px * 0.35f, y - lh * 0.06f, Math.Max(1.5f, 2 * u), lh * 0.84f, T.Trim);
                    }
                    else Triangle(x + px * 0.35f, y + px * 0.55f, px * 0.32f, 1, 0, r.Col);
                    kc = TextCol;
                    break;
            }
            if (keyed)
            {
                c.Text(r.Key, x, y + (px - kpx) * 0.6f, kpx, kc);
                string v = FitText(r.Val, px, x + w - vx, out float fp);
                c.Text(v, vx, y + (px - fp) * 0.6f, fp, r.Col);
            }
            else
            {
                float ix = x + (T.Style == ThemeStyle.Chart ? 0 : px * 1.1f);
                string v = FitText(r.Val, px, x + w - ix, out float fp);
                c.Text(v, ix, y + (px - fp) * 0.6f, fp, r.Col);
            }
            y += lh;
        }
    }

    // ------------------------------------------------------------------ widgets

    /// <summary>Classic: a label and value over a thin bar, with a mark (NaN = none).</summary>
    private void Meter(float x, float y, float w, float px, string label, string value, float frac, float mark, MapColor col)
    {
        float sp = px * 0.85f, bh = Math.Max(4f, px * 0.45f), by = y + sp * 1.35f;
        c.Text(label, x, y, sp, DimText);
        c.Text(value, x + w, y, sp, TextCol, TextAnchor.Right);
        c.Rect(x, by, w, bh, DimText.WithAlpha(0.18f));
        c.Rect(x, by, w * Clamp01(frac), bh, col);
        if (!float.IsNaN(mark)) c.Rect(x + w * Clamp01(mark) - Math.Max(1f, u), by - 2 * u, Math.Max(2f, 2 * u), bh + 4 * u, T.Trim);
    }

    /// <summary>Tachi: a vertical tape, filled in blocks from the bottom, with ladder ticks, a mark line and the value under it.</summary>
    private void Tape(float x, float y, float w, float h, float px, string label, string value, float frac, float mark, MapColor col)
    {
        float lw = Math.Max(1f, u), ty = y + px * 1.1f;
        c.Text(label, x + w / 2, y, px * 0.8f, T.Fine, TextAnchor.Center);
        c.Rect(x, ty, w, h, Accent.WithAlpha(0.07f));
        c.Rect(x, ty, w, lw, Accent.WithAlpha(0.6f));
        c.Rect(x, ty + h - lw, w, lw, Accent.WithAlpha(0.6f));
        c.Rect(x, ty, lw, h, Accent.WithAlpha(0.35f));
        c.Rect(x + w - lw, ty, lw, h, Accent.WithAlpha(0.35f));
        const int n = 14;
        float pad = 3 * u, sh = (h - pad * 2) / n, lit = Clamp01(frac) * n;
        for (int i = 0; i < n; i++)
        {
            float sy = ty + h - pad - (i + 1) * sh;
            c.Rect(x + pad, sy + u, w * 0.6f - pad, Math.Max(1f, sh - 2 * u), i < lit ? col : col.WithAlpha(0.1f));
        }
        for (int i = 0; i <= n; i++)
        {
            float ly = ty + h - pad - i * sh;
            c.Rect(x + w * 0.68f, ly, i % 7 == 0 ? w * 0.26f : w * 0.14f, lw, Accent.WithAlpha(0.6f));
        }
        if (!float.IsNaN(mark))
        {
            float my = ty + h - pad - Clamp01(mark) * (h - pad * 2);
            c.Rect(x - 3 * u, my - u, w + 6 * u, Math.Max(2f, 2 * u), T.Trim);
        }
        float vp = Math.Min(px * 0.85f, (w + px * 1.3f) / Math.Max(1f, c.TextWidth(value, 1)));
        c.Text(value, x + w / 2, ty + h + px * 0.3f, vp, TextCol, TextAnchor.Center);
    }

    /// <summary>UNN: a compass rose with the course to the leg's end (gold) and the velocity's bearing (ship colour), as on the map's bearings.</summary>
    private void Compass(float cx, float cy, float r, float px, RoutePlan p)
    {
        float lw = Math.Max(1f, u), sp = px * 0.55f;
        c.Disc(cx, cy, r, MapColor.Lerp(PanelBg, Accent, 0.06f));
        PanelStyle.Arc(c, cx, cy, r, 0, 360, Math.Max(1f, 1.2f * u), Accent.WithAlpha(0.75f), 48);
        PanelStyle.Arc(c, cx, cy, r * 0.6f, 0, 360, lw, Accent.WithAlpha(0.25f), 36);
        c.Rect(cx - r, cy, r * 2, lw, Accent.WithAlpha(0.15f));
        c.Rect(cx, cy - r, lw, r * 2, Accent.WithAlpha(0.15f));
        for (int k = 0; k < 36; k++)
        {
            float len = k % 9 == 0 ? r * 0.2f : k % 3 == 0 ? r * 0.11f : r * 0.05f;
            Spoke(cx, cy, k * 10, r - len, r, lw, k % 9 == 0 ? T.Trim : Accent.WithAlpha(0.55f));
        }
        foreach (var (txt, a) in new[] { ("000", 0f), ("090", 90f), ("180", 180f), ("270", 270f) })
        {
            double ra = a * Math.PI / 180;
            c.Text(txt, cx + (float)Math.Sin(ra) * r * 0.6f, cy - (float)Math.Cos(ra) * r * 0.6f - sp * 0.5f, sp, T.Trim.WithAlpha(0.8f), TextAnchor.Center);
        }
        float crs = float.NaN, vel = float.NaN;
        if (Snap.HasShip && P(Snap.ShipPos, out var sx, out var sy))
        {
            if (P(p.Target, out var tx, out var ty)) crs = Bearing(tx - sx, ty - sy);
            double vl = Snap.ShipVel.Length;
            if (vl > 1 && P(Snap.ShipPos + Snap.ShipVel * (Cam.Distance * 0.05 / vl), out var vx, out var vy)) vel = Bearing(vx - sx, vy - sy);
        }
        if (!float.IsNaN(vel)) Needle(cx, cy, vel, r * 0.8f, px, ShipCol);
        if (!float.IsNaN(crs)) Needle(cx, cy, crs, r * 0.94f, px, T.Trim);
        c.Disc(cx, cy, Math.Max(2f, 2.5f * u), T.Trim);
        float ly = cy + r + px * 0.25f, lp = px * 0.66f;
        c.Text("CRS " + Deg(crs), cx - px * 0.3f, ly, lp, T.Trim, TextAnchor.Right);
        c.Text("VEL " + Deg(vel), cx + px * 0.3f, ly, lp, ShipCol);
    }

    /// <summary>Screen bearing of (dx, dy): 0 up, clockwise, as the chart's 000 / 090 / 180 / 270 marks.</summary>
    private static float Bearing(float dx, float dy) => (float)((Math.Atan2(dx, -dy) * 180 / Math.PI + 360) % 360);
    private static string Deg(float a) => float.IsNaN(a) ? "---" : ((int)Math.Round(a) % 360).ToString("000");

    private void Spoke(float cx, float cy, float a, float r0, float r1, float w, MapColor col)
    {
        double t = a * Math.PI / 180;
        float s = (float)Math.Sin(t), k = (float)Math.Cos(t);
        c.Line(cx + s * r0, cy - k * r0, cx + s * r1, cy - k * r1, w, col);
    }

    private void Needle(float cx, float cy, float a, float len, float px, MapColor col)
    {
        double t = a * Math.PI / 180;
        float dx = (float)Math.Sin(t), dy = -(float)Math.Cos(t), s = px * 0.42f;
        c.Line(cx, cy, cx + dx * (len - s), cy + dy * (len - s), Math.Max(1.5f, 2 * u), col);
        Triangle(cx + dx * (len - s), cy + dy * (len - s), s, dx, dy, col);
    }

    /// <summary>UNN: speed against time on graph paper: the burn ramp, the coast, the brake, and the flip as a dashed line.</summary>
    private void SpeedProfile(Box g, float px, RoutePlan p)
    {
        float lw = Math.Max(1f, u), sp = px * 0.62f;
        c.Rect(g.X, g.Y, g.W, g.H, MapColor.Lerp(PanelBg, Accent, 0.07f));
        for (int i = 0; i <= 10; i++) c.Rect(g.X + g.W * i / 10f, g.Y, lw, g.H, Accent.WithAlpha(i % 5 == 0 ? 0.28f : 0.1f));
        for (int j = 0; j <= 6; j++) c.Rect(g.X, g.Y + g.H * j / 6f, g.W, lw, Accent.WithAlpha(j % 3 == 0 ? 0.28f : 0.1f));
        c.Rect(g.X, g.Bottom, g.W, lw, T.Trim.WithAlpha(0.8f));
        c.Rect(g.X - lw, g.Y, lw, g.H, T.Trim.WithAlpha(0.8f));
        double total = p.BurnTime + p.CoastTime + p.BrakeTime;
        if (!(total > 0)) return;
        double v0 = Math.Max(0, Math.Min(p.PeakSpeed, p.ClosingSpeed)), vp = p.PeakSpeed, ve = Math.Max(0, p.ExitSpeed), vmax = Math.Max(1, vp * 1.2);
        float X(double tt) => g.X + (float)(tt / total) * g.W;
        float Y(double v) => g.Bottom - (float)(v / vmax) * g.H;
        float w2 = Math.Max(1.5f, 2.2f * u);
        double t1 = p.BurnTime, t2 = p.BurnTime + p.CoastTime;
        c.Line(X(0), Y(v0), X(t1), Y(vp), w2, BurnCol);
        c.Line(X(t1), Y(vp), X(t2), Y(vp), w2, CoastCol);
        c.Line(X(t2), Y(vp), X(total), Y(ve), w2, BrakeCol);
        Dashed(X(t2), g.Y, X(t2), g.Bottom, lw, T.Trim, 3 * u, 3 * u);
        c.Text("v " + Fmt.Speed(vp), g.X + px * 0.3f, g.Y + px * 0.12f, sp, CoastCol);
        float ay = g.Bottom + px * 0.2f;
        string end = LegEnd + Fmt.Time(total);
        float l0 = c.TextWidth("T+0", sp), le = c.TextWidth(end, sp), lf = c.TextWidth("FLIP", sp);
        c.Text("T+0", g.X, ay, sp, DimText);
        c.Text(end, g.Right, ay, sp, BrakeCol, TextAnchor.Right);
        float fx = X(t2);
        if (fx - lf / 2 > g.X + l0 + px * 0.3f && fx + lf / 2 < g.Right - le - px * 0.3f) c.Text("FLIP", fx, ay, sp, T.Trim, TextAnchor.Center);
    }

    /// <summary>OPA: readout tiles across w: a label, a big value and a colour tab.</summary>
    private void Tiles(float x, float y, float w, float px, (string K, string V, MapColor Col)[] tiles)
    {
        float gap = px * 0.5f, tw = (w - gap * (tiles.Length - 1)) / tiles.Length, th = px * 2.8f, bar = Math.Max(2f, 3 * u);
        for (int i = 0; i < tiles.Length; i++)
        {
            float tx = x + i * (tw + gap);
            c.Rect(tx, y, tw, th, MapColor.Lerp(PanelBg, Accent, 0.12f));
            c.Rect(tx, y, tw * 0.4f, bar, Accent);
            c.Rect(tx + tw - bar, y + th * 0.5f, bar, th * 0.5f, tiles[i].Col.WithAlpha(0.8f));
            c.Text(tiles[i].K, tx + px * 0.45f, y + px * 0.35f, px * 0.68f, DimText);
            float vp = Math.Min(px * 1.15f, (tw - px * 1.1f) / Math.Max(1f, c.TextWidth(tiles[i].V, 1)));
            c.Text(tiles[i].V, tx + px * 0.45f, y + px * 1.2f, vp, tiles[i].Col);
        }
    }

    /// <summary>OPA: a chunky block gauge (12 blocks) between its label and value; the mark's block outlined.</summary>
    private void Blocks(float x, float y, float w, float px, string label, string value, float frac, float mark, MapColor col)
    {
        float lp = px * 0.8f, bx = x + px * 4.4f, bw = w - px * 4.4f - px * 5.4f, bh = px * 0.95f, lw = Math.Max(1f, u);
        c.Text(label, x, y + (bh - lp) / 2, lp, Accent);
        c.Text(value, x + w, y + (bh - lp) / 2, lp, TextCol, TextAnchor.Right);
        const int n = 12;
        float g = Math.Max(2f, 3 * u), cw = (bw - g * (n - 1)) / n;
        int lit = (int)Math.Round(Clamp01(frac) * n), mk = float.IsNaN(mark) ? -1 : Math.Min(n - 1, (int)(Clamp01(mark) * n));
        for (int i = 0; i < n; i++)
        {
            float cx = bx + i * (cw + g);
            c.Rect(cx, y, cw, bh, i < lit ? col : col.WithAlpha(0.12f));
            if (i != mk) continue;
            float e = 1.5f * u;
            c.Rect(cx - e, y - e, cw + 2 * e, lw, T.Trim); c.Rect(cx - e, y + bh + e - lw, cw + 2 * e, lw, T.Trim);
            c.Rect(cx - e, y - e, lw, bh + 2 * e, T.Trim); c.Rect(cx + cw + e - lw, y - e, lw, bh + 2 * e, T.Trim);
        }
    }

    /// <summary>OPA: status lights: Nexus border, detour, hazard and overrun, lit when the route has them.</summary>
    private void Lights(float x, float y, float w, float px)
    {
        bool border = false, hazard = false, over = false;
        foreach (var g in Path.Legs) { border |= g.Crossings.Count > 0; hazard |= g.Hazards.Count > 0; over |= g.TooFastToStop; }
        var ls = new[] { ("NEXUS", border, BorderCol), ("DETOUR", Path.DetoursAhead > 0, DetourCol), ("HAZARD", hazard, WarnCol), ("OVERRUN", over, WarnCol) };
        float sw = w / ls.Length, s = px * 0.62f, lp = px * 0.66f;
        for (int i = 0; i < ls.Length; i++)
        {
            var (name, on, col) = ls[i];
            float lx = x + i * sw, ly = y + px * 0.15f;
            c.Rect(lx, ly, s, s, on ? col : DimText.WithAlpha(0.18f));
            c.Text(name, lx + s + px * 0.35f, ly + (s - lp) / 2, lp, on ? TextCol : DimText.WithAlpha(0.6f));
        }
    }

    /// <summary>Protogen: the timeline as a ring: burn, coast and brake arcs clockwise from the top, the flip marked, the ETA inside.</summary>
    private void Ring(float cx, float cy, float R, float px, RoutePlan p)
    {
        float tw = Math.Max(3f, px * 0.45f), lw = Math.Max(1f, u);
        double total = p.BurnTime + p.CoastTime + p.BrakeTime;
        PanelStyle.Arc(c, cx, cy, R, 0, 360, tw, DimText.WithAlpha(0.15f), 60);
        if (total > 0)
        {
            float a1 = (float)(360 * p.BurnTime / total), a2 = (float)(360 * (p.BurnTime + p.CoastTime) / total);
            void ArcPart(float s0, float s1, MapColor col)
            {
                if (s1 - s0 > 2.5f) PanelStyle.Arc(c, cx, cy, R, s0 + 1, s1 - 1, tw, col, Math.Max(2, (int)((s1 - s0) / 6)));
            }
            ArcPart(0, a1, BurnCol);
            ArcPart(a1, a2, CoastCol.WithAlpha(0.8f));
            ArcPart(a2, 360, BrakeCol);
            Spoke(cx, cy, a2, R - tw * 1.6f, R + tw * 1.6f, Math.Max(2f, 2 * u), T.Trim);
            Spoke(cx, cy, 0, R - tw * 1.6f, R + tw * 1.6f, Math.Max(1.5f, 1.5f * u), T.Fine);
        }
        for (int k = 0; k < 12; k++) Spoke(cx, cy, k * 30, R + tw * 0.9f, R + tw * 0.9f + (k % 3 == 0 ? 5 : 2.5f) * u, lw, Accent.WithAlpha(0.6f));
        PanelStyle.DashedArc(c, cx, cy, R * 0.74f, 0, 360, lw, Accent.WithAlpha(0.4f), 36);
        c.Text("ETA", cx, cy - px * 1.2f, px * 0.6f, DimText, TextAnchor.Center);
        string v = Fmt.Time(Path.Eta);
        float vp = Math.Min(px, R * 1.3f / Math.Max(1f, c.TextWidth(v, 1)));
        c.Text(v, cx, cy - vp * 0.5f, vp, TextCol, TextAnchor.Center);
        string f = "FLIP T+" + Fmt.Time(p.BurnTime + p.CoastTime);
        float fp = Math.Min(px * 0.55f, R * 1.25f / Math.Max(1f, c.TextWidth(f, 1)));
        c.Text(f, cx, cy + px * 0.65f, fp, T.Trim, TextAnchor.Center);
    }

    /// <summary>Protogen: a ruler gauge: label and value over a ticked rule, a fill, a pointer and a mark (NaN = none).</summary>
    private void Ruler(float x, float y, float w, float px, string label, string value, float frac, float mark, MapColor col)
    {
        float lp = px * 0.72f, lw = Math.Max(1f, u), ly = y + px * 1.55f;
        frac = Clamp01(frac);
        c.Text(label, x, y, lp, DimText);
        c.Text(value, x + w, y, px * 0.8f, TextCol, TextAnchor.Right);
        c.Rect(x, ly - px * 0.28f, w * frac, px * 0.28f, col.WithAlpha(0.45f));
        c.Rect(x, ly, w, lw, Accent.WithAlpha(0.7f));
        for (int k = 0; k <= 20; k++) c.Rect(x + w * k / 20f, ly, lw, (k % 5 == 0 ? 5 : 2) * u, Accent.WithAlpha(0.55f));
        if (!float.IsNaN(mark)) c.Rect(x + w * Clamp01(mark) - lw, ly - px * 0.5f, Math.Max(2f, 2 * u), px * 0.5f, T.Trim);
        Triangle(x + w * frac, ly - px * 0.62f, px * 0.32f, 0, 1, col);
    }

    /// <summary>Belter: the leg as a range track by distance: the ship, the coast start, the flip and the leg's end.</summary>
    private void RangeTrack(Box r, float px, RoutePlan p)
    {
        double d = Math.Max(1, p.Distance);
        float sp = px * 0.62f, ly = r.Y + px * 1.25f, lw = Math.Max(1f, u), w2 = Math.Max(2f, 2.5f * u);
        float X(double m) => r.X + (float)(Math.Max(0, Math.Min(d, m)) / d) * r.W;
        float xc = X(p.CoastAt), xf = X(p.FlipAt);
        c.Rect(r.X, ly - w2 / 2, xc - r.X, w2, BurnCol);
        c.Rect(xc, ly - w2 / 2, xf - xc, w2, CoastCol.WithAlpha(0.8f));
        c.Rect(xf, ly - w2 / 2, r.Right - xf, w2, BrakeCol);
        for (int k = 0; k <= 10; k++) c.Rect(r.X + r.W * k / 10f, ly + 3 * u, lw, (k % 5 == 0 ? 5 : 3) * u, OrbitCol.WithAlpha(0.55f));
        Triangle(r.X, ly, px * 0.42f, 1, 0, ShipCol);
        Hexagon(r.Right, ly, px * 0.42f, Math.Max(1f, 1.5f * u), T.Trim);
        float s = px * 0.42f;
        c.Rect(xf - s / 2, ly - s / 2, s, s, T.Trim, (float)(Math.PI / 4));
        c.Text("RANGE " + Fmt.Dist(d), r.X, r.Y, sp, DimText);
        c.Text(Path.Legs.Count > 1 ? "WAYPOINT" : "TARGET", r.Right, r.Y, sp, BrakeCol, TextAnchor.Right);
        string ft = "FLIP " + Fmt.Dist(p.FlipAt);
        float fw = c.TextWidth(ft, sp), fx = Math.Max(r.X + fw / 2, Math.Min(r.Right - fw / 2, xf));
        c.Text(ft, fx, ly + px * 0.6f, sp, T.Trim, TextAnchor.Center);
    }

    // ------------------------------------------------------------------ shapes

    /// <summary>A filled box with 45-degree cuts of k at the corners asked for, the cuts as fine steps so nothing overlaps.</summary>
    private void FillCut(Box b, float k, bool tl, bool tr, bool br, bool bl, MapColor col)
    {
        k = Math.Min(k, Math.Min(b.W, b.H) / 2);
        c.Rect(b.X, b.Y + k, b.W, b.H - 2 * k, col);
        float s = Math.Max(1f, k / 10f);
        for (float j = 0; j < k - 0.01f; j += s)
        {
            float h = Math.Min(s, k - j), cut = k - (j + h / 2);
            float l = tl ? cut : 0, r = tr ? cut : 0;
            c.Rect(b.X + l, b.Y + j, b.W - l - r, h, col);
            l = bl ? cut : 0; r = br ? cut : 0;
            c.Rect(b.X + l, b.Bottom - j - h, b.W - l - r, h, col);
        }
    }

    /// <summary>The outline of a box with 45-degree cuts of k at the corners asked for.</summary>
    private void OutlineCut(Box b, float k, bool tl, bool tr, bool br, bool bl, float w, MapColor col)
    {
        float l = b.X, t = b.Y, r = b.Right, bt = b.Bottom, a = tl ? k : 0, e = tr ? k : 0, f = br ? k : 0, d = bl ? k : 0;
        var pts = new[] { (l + a, t), (r - e, t), (r, t + e), (r, bt - f), (r - f, bt), (l + d, bt), (l, bt - d), (l, t + a) };
        for (int i = 0; i < pts.Length; i++)
        {
            var (ax, ay) = pts[i];
            var (bx, by) = pts[(i + 1) % pts.Length];
            if (Math.Abs(ax - bx) + Math.Abs(ay - by) > 0.5f) c.Line(ax, ay, bx, by, w, col);
        }
    }
}
