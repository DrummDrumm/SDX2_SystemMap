using System;
using System.Collections.Generic;

namespace SDX2_SystemMap;

/// <summary>
/// The autopilot's on-screen parts, drawn under Flip and Burn's N list and on the HUD (game-free, so the tests render
/// them): the AUTOPILOT / MANUAL / STOP buttons, the route panel, the status line while flying, and the asteroid warning.
/// Each draw returns the boxes you can click.
/// </summary>
public static class AutopilotPanel
{
    public static MapColor Panel => MapTheme.Current.ApPanel;
    public static MapColor Text => MapTheme.Current.ApText;
    public static MapColor Dim => MapTheme.Current.ApDim;
    public static readonly MapColor Stop = MapColor.Rgb(0xFF5A4A), Warn = MapColor.Rgb(0xFFB020);
    private static readonly MapColor GoGreen = MapColor.Rgb(0x3FD08A);
    /// <summary>The go colour in the theme's look: its accent (Tachi: its fine white, since its accent is red; Classic green).</summary>
    public static MapColor Go => MapTheme.Current.Style switch
    {
        ThemeStyle.Chart => GoGreen,
        ThemeStyle.Tachi => MapTheme.Current.Fine,
        _ => MapTheme.Current.Accent,
    };

    /// <summary>A centred button; returns its box.</summary>
    public static Box Button(IMapCanvas c, float cx, float y, float px, string text, MapColor accent, bool hover)
    {
        float w = c.TextWidth(text, px) + px * 2.4f, h = px * 1.9f;
        var b = new Box(cx - w / 2, y, w, h);
        PanelStyle.Button(c, b, px, text, accent, hover, false, true);
        return b;
    }

    /// <summary>The Steer round asteroid key, as shown (empty: none set). The plugin keeps it current.</summary>
    public static string SteerKey = "Ctrl+Shift+M";

    /// <summary>What the confirmation says about the route.</summary>
    public static List<(string Text, MapColor Col)> ConfirmLines(RoutePath p, string stopKey)
    {
        var l = new List<(string, MapColor)>();
        int user = p.UserAhead, det = p.DetoursAhead - user;
        string via = (det > 0 ? "   " + det + (det == 1 ? " detour" : " detours") : "") + (user > 0 ? "   " + user + (user == 1 ? " waypoint" : " waypoints") : "");
        l.Add((Fmt.Dist(p.Distance) + "   ETA " + (double.IsNaN(p.Eta) ? "unknown" : Fmt.Time(p.Eta)) + via, Text));
        var stopAt = RouteExport.StopPoint(p, out var why, out double aside);
        double above = V3.Distance(stopAt, p.Target);
        var station = SystemData.StationNear(p.Target, RouteExport.StationRange);
        var under = why == "station" ? RouteExport.WorldUnder(p.Target, p.Clearance) : null;
        string stop = why == "station" ? "Stops " + Fmt.Dist(above) + (under != null ? (station?.Above > 0 ? " straight above " : " above ") : " short of ") + (station?.Name ?? "the station")
                + (under != null ? (station?.Above > 0 ? " (a ground facility on " + under.Name + ")" : ", on the side away from " + under.Name) : "")
            : why == "sol" ? "Stops at Sol's " + Fmt.Dist(SystemData.SolNoFly) + " no-fly sphere"
            : why == "world" ? (RouteExport.WorldUnder(stopAt, 1e12) is { } sw ? "Stops " + Fmt.Dist(V3.Distance(stopAt, sw.Pos) - sw.Radius) + " above " + sw.Name + "'s surface" : "Stops above the surface") + ", then you take it down"
            : why == "surface" ? "Stops " + Fmt.Dist(above) + " straight above it (as low as your thrust holds), then you take it down"
            : why == "well" ? "Stops " + Fmt.Dist(RouteExport.WellMargin) + " outside its gravity well, then you take it down"
            : why == "offset" ? "Stops " + Fmt.Dist(above) + " short of the point"
            : "Stops on the point";
        l.Add((stop, Text));
        if (aside > 1) l.Add(("Another ship stops there: yours stops " + Fmt.Dist(aside) + " to the side", Text));
        int bent = 0; double corner = double.PositiveInfinity;
        foreach (var leg in p.Legs) if (leg.Curved) { bent++; corner = Math.Min(corner, leg.CornerSpeed); }
        if (p.Curved && p.DetoursAhead > 0)
            l.Add(("Parabolic: curves through each point without stopping, one flip" + (double.IsInfinity(corner) ? "" : ", bends at up to " + Fmt.Speed(corner)), Text));
        else if (p.Legs.Count > 1)
            l.Add(("Flip and burn: slows at each point to turn" + (bent > 0 ? ", " + bent + (bent == 1 ? " leg bent" : " legs bent") : ""), Text));
        else if (bent > 0)
            l.Add(("Bent: curves up / down without stopping" + (double.IsInfinity(corner) ? "" : ", bend at up to " + Fmt.Speed(corner)), Text));
        int borders = 0;
        foreach (var leg in p.Legs) borders += leg.Crossings.Count;
        if (borders > 0) l.Add(("Crosses " + borders + (borders == 1 ? " sector border" : " sector borders") + " (Nexus transfers): stay in the seat", Warn));
        if (p.MaxAccel <= 0) l.Add(("Max acceleration unknown: the ship's own thrust is used", Warn));
        l.Add(("Asteroids on the route are flagged, not avoided: " + (string.IsNullOrEmpty(SteerKey) ? "set a Steer round asteroid key to steer round them" : SteerKey + " steers round them"), Dim));
        l.Add(("Move or turn to take over", Dim));
        return l;
    }

    /// <summary>The two choices under the N list once a destination is picked: AUTOPILOT (opens the route panel) and
    /// MANUAL (close the list and fly it with Flip and Burn). Returns the row's box.</summary>
    public static Box Choice(PanelUi ui, float cx, float y, float px, bool hasAutopilot, out bool autopilot, out bool manual)
    {
        float w = Math.Max(ui.C.TextWidth("ROUTE PLAN", px), ui.C.TextWidth("FLY MANUAL", px)) + px * 3, h = px * 1.9f, gap = px * 0.8f;
        var a = new Box(cx - gap / 2 - w, y, w, h);
        var m = new Box(cx + gap / 2, y, w, h);
        autopilot = ui.Button(a, "ROUTE PLAN", px, hasAutopilot ? Go : Dim);
        manual = ui.Button(m, "FLY MANUAL", px, MapTheme.Current.Style == ThemeStyle.Chart ? MapColor.Rgb(0x4FC3F7) : MapTheme.Current.Coast);
        return new Box(a.X, y, m.Right - a.X, h);
    }

    /// <summary>What was clicked on the full map's autopilot card this frame.</summary>
    public struct CardHit { public bool Stop, FullStop, Resume, Autopilot, Manual, Clear; }

    /// <summary>The autopilot card's height at text size px (with the RESUME ROUTE row or not).</summary>
    public static float MapCardHeight(float px, bool resume) =>
        PanelStyle.HeaderH(px) + px * 1.3f + px * 1.9f + (resume ? px * 2.6f : 0) + px * 0.5f + px * 1.1f + px * 0.5f;

    /// <summary>
    /// The full map's autopilot card, at the top of the route panel and framed like it: the destination, then STOP
    /// AUTOPILOT while it flies, or RESUME ROUTE (after a stop) and AUTOPILOT / MANUAL, and a hint line.
    /// </summary>
    public static CardHit MapCard(PanelUi ui, Box b, float px, string target, bool engaged, bool resume, bool hasPb, string hint, bool braking = false)
    {
        var c = ui.C; var hit = new CardHit();
        PanelStyle.Frame(c, b, px / 12f, px, braking ? "AUTOPILOT  FULL STOP" : engaged ? "AUTOPILOT  FLYING" : "ROUTE PLAN", null, Panel);
        float pad = px, y = b.Y + PanelStyle.HeaderH(px), cx = b.X + b.W / 2, rowH = px * 1.9f, iw = b.W - pad * 2;
        string to = braking ? "braking to a stop" : "to " + MapScene.ShortName(target);
        float tw = iw;
        if (!braking && !engaged)
        {
            // CLEAR ROUTE: small, at the end of the destination line (the route stays until you clear it or arrive)
            float cp = px * 0.75f, cw = c.TextWidth("CLEAR ROUTE", cp) + px * 1.4f;
            hit.Clear = ui.Button(new Box(b.Right - pad - cw, y - px * 0.15f, cw, px * 1.3f), "CLEAR ROUTE", cp, Dim);
            tw = iw - cw - px * 0.5f;
        }
        c.Text(to, b.X + pad, y, Math.Min(px, tw / Math.Max(1f, c.TextWidth(to, 1))), Text);
        y += px * 1.3f;
        if (braking)
        {
            float w = Math.Min(iw, c.TextWidth("STOP AUTOPILOT", px) + px * 2.4f);
            hit.Stop = ui.Button(new Box(cx - w / 2, y, w, rowH), "STOP AUTOPILOT", px, Stop);
        }
        else if (engaged)
        {
            // STOP hands the ship back as it is (asks first); FLY MANUAL hands it back at once, the route kept for
            // the burn guide; FULL STOP burns it to rest first
            StopPair(ui, cx, y, iw, rowH, px, out hit.Stop, out hit.Manual, out hit.FullStop);
        }
        else
        {
            if (resume)
            {
                float w = Math.Min(iw, c.TextWidth("RESUME ROUTE", px) + px * 2.4f);
                hit.Resume = ui.Button(new Box(cx - w / 2, y, w, rowH), "RESUME ROUTE", px, Go, false, hasPb);
                y += rowH + px * 0.7f;
            }
            Choice(ui, cx, y, px, hasPb, out hit.Autopilot, out hit.Manual);
        }
        y += rowH + px * 0.5f;
        float hp = Math.Min(px * 0.85f, iw / Math.Max(1f, c.TextWidth(hint, 1)));
        c.Text(hint, b.X + pad, y, hp, Dim);
        return hit;
    }

    /// <summary>The FLY MANUAL colour: the theme's coast colour (Chart: light blue).</summary>
    public static MapColor ManualCol => MapTheme.Current.Style == ThemeStyle.Chart ? MapColor.Rgb(0x4FC3F7) : MapTheme.Current.Coast;

    /// <summary>STOP AUTOPILOT, FLY MANUAL and FULL STOP side by side, centred on cx, shrunk to fit width w.</summary>
    public static void StopPair(PanelUi ui, float cx, float y, float w, float rowH, float px, out bool stop, out bool manual, out bool full)
    {
        var c = ui.C;
        float gap = px * 0.6f, w1 = c.TextWidth("STOP AUTOPILOT", px) + px * 2.4f, wm = c.TextWidth("FLY MANUAL", px) + px * 2.4f,
            w2 = c.TextWidth("FULL STOP", px) + px * 2.4f;
        float k = Math.Min(1f, (w - gap * 2) / (w1 + wm + w2));
        w1 *= k; wm *= k; w2 *= k;
        float x0 = cx - (w1 + wm + w2 + gap * 2) / 2;
        stop = ui.Button(new Box(x0, y, w1, rowH), "STOP AUTOPILOT", px * k, Stop);
        manual = ui.Button(new Box(x0 + w1 + gap, y, wm, rowH), "FLY MANUAL", px * k, ManualCol);
        full = ui.Button(new Box(x0 + w1 + wm + gap * 2, y, w2, rowH), "FULL STOP", px * k, Warn);
    }

    /// <summary>What the mouse did in the route panel this frame.</summary>
    public struct RouteHit
    {
        public bool Engage, Cancel, ToggleAdd, Changed;
        /// <summary>FLY MANUAL: keep the route (waypoints too) and fly it by hand.</summary>
        public bool Manual;
        public int Remove;
        public Box Panel;
    }

    /// <summary>
    /// The autopilot's route panel: top speed, a thrust cap (as thrust % or as the largest drive signature), your own
    /// waypoints (added by picking them in F&amp;B's list), the route summary, and ENGAGE / CANCEL (while it flies,
    /// <paramref name="flying"/>: APPLY sends the changes, CANCEL drops them). Edits
    /// <paramref name="lim"/> in place. x, y = top-left (moved up to stay on screen), w = width.
    /// </summary>
    /// <summary>The Route Plan panel with no destination yet: its frame and what to do. Returns its box.</summary>
    public static Box EmptyPlan(PanelUi ui, float x, float y, float w, float screenH, float px, string mapKey)
    {
        var c = ui.C;
        float sp = px * 0.9f, pad = px * 1.1f, lh = px * 1.5f;
        var lines = new List<(string, MapColor)>
        {
            ("No destination yet", Text),
            ("Pick one in the list, or Shift+click a place on the map.", Dim),
            ("Top speed, burn, brake and waypoints are set here", Dim),
            ("once there is a route.", Dim),
        };
        if (!string.IsNullOrEmpty(mapKey)) lines.Add((mapKey + ": map, full map, off", Dim));
        float h = px * 2.4f + px * 0.6f + lines.Count * lh + px * 0.8f;
        y = Math.Max(px, Math.Min(y, screenH - h - px));
        var panel = new Box(x, y, w, h);
        PanelStyle.Frame(c, panel, px / 12f, px, "ROUTE PLAN", null, Panel);
        float ty = y + px * 2.4f + px * 0.6f;
        foreach (var (t, col) in lines)
        {
            float size = sp, room = w - pad * 2, tw = c.TextWidth(t, size);
            if (tw > room) size *= room / tw;
            c.Text(t, x + pad, ty, size, col); ty += lh;
        }
        return panel;
    }

    public static RouteHit RoutePanel(PanelUi ui, float x, float y, float w, float screenH, float px, RoutePath p, RouteLimits lim,
        double fnbCap, double fullAccel, SignatureInfo sig, IList<string> waypoints, bool adding, string stopKey, double now = 0, string addHint = null, bool flying = false)
    {
        var c = ui.C;
        var hit = new RouteHit { Remove = -1 };
        // a line of text kept inside the panel: drawn smaller if it is too long, and cut short if that is not enough
        void Fit(string t, float fx, float fy, float size, MapColor col, float room = -1)
        {
            if (room < 0) room = x + w - px * 1.1f - fx;
            float tw = c.TextWidth(t, size);
            if (tw > room)
            {
                float small = Math.Max(size * 0.72f, size * room / tw);
                fy += (size - small) * 0.5f; size = small;
                if (c.TextWidth(t, size) > room)
                {
                    while (t.Length > 4 && c.TextWidth(t + "...", size) > room) t = t.Substring(0, t.Length - 1);
                    t = t.TrimEnd() + "...";
                }
            }
            c.Text(t, fx, fy, size, col);
        }
        sig ??= new SignatureInfo();
        double weight = sig.Weight;
        if (fnbCap <= 0) fnbCap = RoutePlan.DefaultMaxSpeed;
        if (weight <= 0 && lim.BySignature) { lim.BySignature = false; hit.Changed = true; }
        var lines = ConfirmLines(p, stopKey);
        double gain = sig.Gain > 0 ? sig.Gain : 4;
        double frac0 = Math.Min(lim.ThrustFraction(weight, gain), lim.BrakeFraction(weight, gain));
        bool floorWins = weight > 0 && sig.Floor > sig.DriveStrength(frac0), live = sig.LiveAllRound >= 0;
        int infoN = 3 + (sig.Floor > 0 ? 1 : 0) + (live ? 1 : 0);
        float sp = px * 0.9f, pad = px * 1.1f, row = px * 2.0f, lh = px * 1.3f;
        float h = px * 2.4f + row * 5 + lh * (infoN + 0.2f) + px * 0.8f + px * 1.6f + Math.Max(1, waypoints.Count) * lh * 1.15f + row + px * 0.8f + lines.Count * lh + px * 3.4f;
        y = Math.Max(px, Math.Min(y, screenH - h - px));
        var panel = hit.Panel = new Box(x, y, w, h);
        // the theme's frame and header (PanelStyle.HeaderH is the 2.4 px the rows start below)
        PanelStyle.Frame(c, panel, px / 12f, px, (flying ? "CHANGE FLIGHT  to " : "ROUTE PLAN  to ") + MapScene.ShortName(p.TargetName), p.Final.TargetSector.Dx, Panel);
        float labelW = c.TextWidth("Signature", sp) + px * 1.4f, valueW = Math.Max(c.TextWidth("50.00 km/s max", sp), c.TextWidth("00000 km weak cone", sp)) + px * 0.6f;
        float sx = x + pad + labelW, sw = w - pad * 2 - labelW - valueW, ty = y + px * 2.4f;

        // top speed
        double cap = lim.MaxSpeed > 0 ? Math.Min(lim.MaxSpeed, fnbCap) : fnbCap;
        c.Text("Top speed", x + pad, ty + (row - sp) / 2, sp, Dim);
        double v = ui.Slider("speed", new Box(sx, ty + row * 0.2f, sw, row * 0.6f), cap, Math.Min(1000, fnbCap), fnbCap, 500, Go);
        if (Math.Abs(v - cap) > 1) { lim.MaxSpeed = v >= fnbCap - 1 ? 0 : v; hit.Changed = true; }
        c.Text(Fmt.Speed(lim.MaxSpeed > 0 ? lim.MaxSpeed : fnbCap) + (lim.MaxSpeed > 0 ? "" : " max"), x + w - pad, ty + (row - sp) / 2, sp, Text, TextAnchor.Right);
        ty += row;

        // limit by thrust or signature
        c.Text("Limit by", x + pad, ty + (row - sp) / 2, sp, Dim);
        float bw = (sw + valueW) / 2 - px * 0.3f;
        if (ui.Button(new Box(sx, ty + row * 0.1f, bw, row * 0.8f), "THRUST", sp, Warn, !lim.BySignature) && lim.BySignature) { lim.BySignature = false; hit.Changed = true; }
        if (ui.Button(new Box(sx + bw + px * 0.6f, ty + row * 0.1f, bw, row * 0.8f), "SIGNATURE", sp, Warn, lim.BySignature, weight > 0) && !lim.BySignature)
        {
            lim.BySignature = true; hit.Changed = true;
            double max = DriveSignature.WeakCone(weight, 1, gain);
            if (lim.SignatureKm <= 0 || lim.SignatureKm > max) lim.SignatureKm = Math.Round(DriveSignature.WeakCone(weight, lim.ThrustPct / 100, gain));
            if (lim.BrakeSignatureKm <= 0 || lim.BrakeSignatureKm > max) lim.BrakeSignatureKm = Math.Round(DriveSignature.WeakCone(weight, lim.BrakePct / 100, gain));
        }
        ty += row;

        // how the legs meet at each direction change: stop there (flip and burn), or curve through it (parabolic)
        c.Text("Legs", x + pad, ty + (row - sp) / 2, sp, Dim);
        if (ui.Button(new Box(sx, ty + row * 0.1f, bw, row * 0.8f), "FLIP & BURN", sp, Go, !lim.Parabolic) && lim.Parabolic) { lim.Parabolic = false; hit.Changed = true; }
        if (ui.Button(new Box(sx + bw + px * 0.6f, ty + row * 0.1f, bw, row * 0.8f), "PARABOLIC", sp, Go, lim.Parabolic) && !lim.Parabolic) { lim.Parabolic = true; hit.Changed = true; }
        ty += row;

        // the caps: one for burning toward a point, one for braking
        for (int k = 0; k < 2; k++)
        {
            bool brake = k == 1;
            string label = brake ? "Brake" : "Burn", id = brake ? "brake" : "burn";
            float my = ty + (row - sp) / 2;
            c.Text(label, x + pad, my, sp, Dim);
            if (!lim.BySignature)
            {
                double pct = brake ? lim.BrakePct : lim.ThrustPct;
                double t = ui.Slider(id + "%", new Box(sx, ty + row * 0.2f, sw, row * 0.6f), pct, RouteLimits.MinThrustPct, 100, 1, brake ? Stop : Warn);
                if (Math.Abs(t - pct) > 0.01) { if (brake) lim.BrakePct = t; else lim.ThrustPct = t; hit.Changed = true; pct = t; }
                c.Text(Fmt.Num(pct, "0") + " % thrust", x + w - pad, my, sp, Text, TextAnchor.Right);
            }
            else
            {
                double lo = Math.Max(1, Math.Round(DriveSignature.WeakCone(weight, RouteLimits.MinThrustPct / 100, gain))), hi = Math.Ceiling(DriveSignature.WeakCone(weight, 1, gain));
                double km = brake ? lim.BrakeSignatureKm : lim.SignatureKm, cur = km > 0 ? km : hi;
                double sv = ui.Slider(id + "km", new Box(sx, ty + row * 0.2f, sw, row * 0.6f), cur, lo, hi, 1, brake ? Stop : Warn);
                if (Math.Abs(sv - cur) > 0.01) { km = sv >= hi ? 0 : sv; if (brake) lim.BrakeSignatureKm = km; else lim.SignatureKm = km; hit.Changed = true; }
                c.Text(Fmt.Num(km > 0 ? km : hi, "0") + " km weak cone", x + w - pad, my, sp, Text, TextAnchor.Right);
            }
            ty += row;
        }

        // what each cap gives: thrust share, acceleration, and Spectrum's signal tiers (SDX2 wiki, Signals and Detection)
        string T(double st) => DriveSignature.Tiers(st);
        Fit("km seen Detailed (ship core) / Strong (faction) / Weak (distance only), by baseline detectors", x + pad, ty, sp * 0.85f, Dim);
        ty += lh;
        for (int k = 0; k < 2; k++)
        {
            double f = k == 0 ? lim.ThrustFraction(weight, gain) : lim.BrakeFraction(weight, gain);
            string t = (k == 0 ? "Burn " : "Brake ") + Fmt.Num(f * 100, "0") + " %  " + (fullAccel > 0 ? Fmt.Num(fullAccel * f / 9.81, "0.00") + " g" : "accel ?");
            t += weight > 0 ? "   optical: all round " + T(sig.DriveStrength(f)) + "   cone " + T(sig.Cone(f)) : "   (no fusion drives: no optical signal)";
            Fit(t, x + pad, ty, sp * 0.9f, k == 0 ? Text : Text.WithAlpha(0.85f));
            ty += lh;
        }
        if (sig.Floor > 0)
        {
            Fit("Radio: all round " + T(sig.Floor) + " from " + (sig.FloorWhat ?? "other emitters") + (floorWins ? ", louder than the drives" : ""),
                x + pad, ty, sp * 0.9f, floorWins ? Warn : Dim);
            ty += lh;
        }
        if (live)
        {
            Fit("Spectrum now: all round " + T(sig.LiveAllRound) + "   cone " + T(sig.LiveCone), x + pad, ty, sp * 0.9f, Dim);
            ty += lh;
        }
        ty += lh * 0.2f + px * 0.8f;

        // your own waypoints
        c.Rect(x + pad, ty - px * 0.4f, w - pad * 2, 1, Dim.WithAlpha(0.4f));
        c.Text("WAYPOINTS", x + pad, ty, sp, Text);
        c.Text("flown in order, then the destination", x + pad + c.TextWidth("WAYPOINTS", sp) + px, ty + sp * 0.1f, sp * 0.85f, Dim);
        ty += px * 1.6f;
        if (waypoints.Count == 0) { c.Text("None yet (detours round worlds are automatic)", x + pad, ty, sp * 0.9f, Dim); ty += lh * 1.15f; }
        for (int i = 0; i < waypoints.Count; i++)
        {
            var xb = new Box(x + w - pad - lh, ty - lh * 0.1f, lh, lh);
            Fit((i + 1) + ".  " + waypoints[i], x + pad, ty, sp, MapScene.WaypointCol, xb.X - px * 0.5f - x - pad);
            if (ui.Button(xb, "x", sp * 0.9f, Stop)) hit.Remove = i;
            ty += lh * 1.15f;
        }
        if (adding)
        {
            bool blink = now % 1.0 < 0.6;
            c.Text(addHint ?? "Pick a point in the N list", x + pad, ty + (row - sp) / 2, sp, blink ? Warn : Warn.WithAlpha(0.6f));
            float dw = c.TextWidth("DONE", sp) + px * 2.4f;
            if (ui.Button(new Box(x + w - pad - dw, ty + row * 0.1f, dw, row * 0.8f), "DONE", sp, Warn, true)) hit.ToggleAdd = true;
        }
        else
        {
            float aw = c.TextWidth("+ ADD WAYPOINT", sp) + px * 2.4f;
            if (ui.Button(new Box(x + pad, ty + row * 0.1f, aw, row * 0.8f), "+ ADD WAYPOINT", sp, MapScene.WaypointCol, false, waypoints.Count < 8)) hit.ToggleAdd = true;
        }
        ty += row + px * 0.8f;

        // the route
        c.Rect(x + pad, ty - px * 0.4f, w - pad * 2, 1, Dim.WithAlpha(0.4f));
        foreach (var (t, col) in lines) { Fit(t, x + pad, ty, sp, col); ty += lh; }
        float by = panel.Bottom - px * 2.6f;
        if (flying)
        {
            float bwid = (w - pad * 2 - px) / 2;
            hit.Engage = ui.Button(new Box(x + pad, by, bwid, px * 1.9f), "APPLY", px, Go);
            hit.Cancel = ui.Button(new Box(x + pad + bwid + px, by, bwid, px * 1.9f), "CANCEL", px, Dim);
        }
        else
        {
            // ENGAGE, FLY MANUAL (the route kept, flown by hand with the burn guide), CANCEL
            // each as wide as its text needs, the row filling the panel; the text smaller when the panel is narrow
            const string te = "ENGAGE AUTOPILOT", tm = "FLY MANUAL", tc = "CANCEL";
            float gap = px * 0.6f, room = w - pad * 2 - gap * 2;
            float ne = c.TextWidth(te, px) + px * 1.6f, nm = c.TextWidth(tm, px) + px * 1.6f, nc = c.TextWidth(tc, px) + px * 1.6f;
            float k = room / (ne + nm + nc), bpx = px * Math.Min(1f, k);
            float we = ne * k, wm = nm * k, wc = nc * k;
            hit.Engage = ui.Button(new Box(x + pad, by, we, px * 1.9f), te, bpx, Go);
            hit.Manual = ui.Button(new Box(x + pad + we + gap, by, wm, px * 1.9f), tm, bpx, ManualCol);
            hit.Cancel = ui.Button(new Box(x + pad + we + wm + gap * 2, by, wc, px * 1.9f), tc, bpx, Dim);
        }
        return hit;
    }

    /// <summary>
    /// The line on the HUD while the autopilot flies, framed in the map's theme like its panels: centred on cx with its
    /// top at y (kept on the screen), "AUTOPILOT" in the theme's accent, the figures in its text colour.
    /// </summary>
    public static Box StatusLine(IMapCanvas c, float screenW, float y, float px, string target, RouteExport.Status s, string stopKey, float cx = float.NaN, float screenH = float.NaN)
    {
        var t = MapTheme.Current;
        const string head = "AUTOPILOT";
        string text = "to " + MapScene.ShortName(target);
        if (s != null && s.Braking)
        {
            text = "full stop    " + Fmt.Speed(s.Speed) + "    stops in " + Fmt.Dist(s.Dist) + (s.Eta > 0 ? "  " + Fmt.Time(s.Eta) : "");
            string bs = (!string.IsNullOrEmpty(s.Msg) ? s.Msg + "    " : "") + "Dampeners on under 50 m/s    Move or turn to take over";
            return Line(c, screenW, y, px, head, text, bs, cx, screenH);
        }
        if (s != null && s.Flying)
        {
            text += "    " + Fmt.Dist(s.Dist) + "    " + Fmt.Speed(s.Speed) + (s.Eta > 0 ? "    ETA " + Fmt.Time(s.Eta) : "");
            if (s.Count > 1) text += "    point " + Math.Min(s.Wp + 1, s.Count) + " of " + s.Count;
        }
        else text += "    starting...";
        string sub = (s != null && !string.IsNullOrEmpty(s.Msg) ? s.Msg + "    " : "") + "Move or turn to take over    " + stopKey + ": change the route";
        return Line(c, screenW, y, px, head, text, sub, cx, screenH);
    }

    /// <summary>The HUD line while you fly a route by hand (MANUAL), in the autopilot line's frame and place.</summary>
    public static Box ManualLine(IMapCanvas c, float screenW, float y, float px, string text, string sub, float cx = float.NaN, float screenH = float.NaN) =>
        Line(c, screenW, y, px, "MANUAL", text, sub, cx, screenH);

    private static Box Line(IMapCanvas c, float screenW, float y, float px, string head, string text, string sub, float cx, float screenH)
    {
        var t = MapTheme.Current;
        float u = px / 12f, pad = px * 1.1f, gap = px * 0.7f;
        float hw = c.TextWidth(head, px), tw = c.TextWidth(text, px);
        float w = Math.Max(hw + gap + tw, c.TextWidth(sub, px * 0.8f)) + pad * 2, h = px * 3.1f;
        if (float.IsNaN(cx)) cx = screenW / 2;
        float x = Math.Max(0, Math.Min(screenW - w, cx - w / 2));
        if (!float.IsNaN(screenH)) y = Math.Max(0, Math.Min(screenH - h, y));
        var b = new Box(x, y, w, h);
        PanelStyle.Frame(c, b, u, px, null);
        float lx = x + (w - (hw + gap + tw)) / 2, ty = y + px * 0.45f;
        c.Text(head, lx, ty, px, t.Accent);
        c.Text(text, lx + hw + gap, ty, px, t.Text);
        c.Text(sub, x + w / 2, y + px * 1.65f, px * 0.8f, t.Dim, TextAnchor.Center);
        return b;
    }

    /// <summary>The asteroid warning (blinks while it's on the line and not dodged).</summary>
    public static Box AsteroidWarning(IMapCanvas c, float screenW, float y, float px, double dist, double speed, bool onRoute, bool dodged, bool engaged, double now, string key = "Ctrl+Shift+M")
    {
        string text = (onRoute ? "ASTEROID ON ROUTE  " : "ASTEROID AHEAD  ") + Fmt.Dist(dist);
        if (speed > 1) text += "  (" + Fmt.Time(dist / speed) + ")";
        string sub = dodged ? "Autopilot flies round it"
            : engaged ? key + " steers the autopilot round it"
            : onRoute ? "Steer round it, or pick a detour" : "On your current heading";
        bool blink = !dodged && (now % 1.0) < 0.5;
        var col = dodged ? Warn : Stop;
        float w = Math.Max(c.TextWidth(text, px * 1.1f), c.TextWidth(sub, px * 0.8f)) + px * 2, h = px * 2.8f;
        var b = new Box((screenW - w) / 2, y, w, h);
        c.Rect(b.X, b.Y, b.W, b.H, blink ? col.WithAlpha(0.3f) : Panel);
        c.Rect(b.X, b.Y, 3, b.H, col);
        c.Rect(b.Right - 3, b.Y, 3, b.H, col);
        c.Text(text, screenW / 2, y + px * 0.2f, px * 1.1f, col, TextAnchor.Center);
        c.Text(sub, screenW / 2, y + px * 1.6f, px * 0.8f, Text, TextAnchor.Center);
        return b;
    }
}
