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
    public static readonly MapColor Go = MapColor.Rgb(0x3FD08A), Stop = MapColor.Rgb(0xFF5A4A), Warn = MapColor.Rgb(0xFFB020);

    /// <summary>A centred button; returns its box.</summary>
    public static Box Button(IMapCanvas c, float cx, float y, float px, string text, MapColor accent, bool hover)
    {
        float w = c.TextWidth(text, px) + px * 2.4f, h = px * 1.9f;
        var b = new Box(cx - w / 2, y, w, h);
        c.Rect(b.X, b.Y, b.W, b.H, hover ? accent.WithAlpha(0.35f) : Panel);
        c.Rect(b.X, b.Y, b.W, 2, accent);
        c.Rect(b.X, b.Bottom - 2, b.W, 2, accent);
        c.Text(text, cx, b.Y + px * 0.42f, px, hover ? MapColor.Rgb(0xFFFFFF) : accent, TextAnchor.Center);
        return b;
    }

    /// <summary>What the confirmation says about the route.</summary>
    public static List<(string Text, MapColor Col)> ConfirmLines(RoutePath p, string stopKey)
    {
        var l = new List<(string, MapColor)>();
        int user = p.UserAhead, det = p.DetoursAhead - user;
        string via = (det > 0 ? "   " + det + (det == 1 ? " detour" : " detours") : "") + (user > 0 ? "   " + user + (user == 1 ? " waypoint" : " waypoints") : "");
        l.Add((Fmt.Dist(p.Distance) + "   ETA " + (double.IsNaN(p.Eta) ? "unknown" : Fmt.Time(p.Eta)) + via, Text));
        RouteExport.StopPoint(p, out var why);
        var station = SystemData.StationNear(p.Target, RouteExport.StationRange);
        string stop = why == "station" ? "Stops " + Fmt.Dist(RouteExport.StationStandoff) + " short of " + (station?.Name ?? "the station")
            : why == "world" ? "Stops " + Fmt.Dist(p.Clearance) + " above the surface, then you take it down"
            : "Stops on the point";
        l.Add((stop, Text));
        int borders = 0;
        foreach (var leg in p.Legs) borders += leg.Crossings.Count;
        if (borders > 0) l.Add(("Crosses " + borders + (borders == 1 ? " sector border" : " sector borders") + " (server transfers): stay in the seat", Warn));
        if (p.MaxAccel <= 0) l.Add(("Max acceleration unknown: the ship's own thrust is used", Warn));
        l.Add(("Asteroids found on the line are flown round", Dim));
        l.Add(("Move, turn, or press Ctrl+" + stopKey + " at any time to take over", Dim));
        return l;
    }

    /// <summary>The two choices under the N list once a destination is picked: AUTOPILOT (opens the route panel) and
    /// MANUAL (close the list and fly it with Flip and Burn). Returns the row's box.</summary>
    public static Box Choice(PanelUi ui, float cx, float y, float px, bool hasAutopilot, out bool autopilot, out bool manual)
    {
        float w = Math.Max(ui.C.TextWidth("AUTOPILOT", px), ui.C.TextWidth("MANUAL", px)) + px * 3, h = px * 1.9f, gap = px * 0.8f;
        var a = new Box(cx - gap / 2 - w, y, w, h);
        var m = new Box(cx + gap / 2, y, w, h);
        autopilot = ui.Button(a, "AUTOPILOT", px, hasAutopilot ? Go : Dim);
        manual = ui.Button(m, "MANUAL", px, MapColor.Rgb(0x4FC3F7));
        return new Box(a.X, y, m.Right - a.X, h);
    }

    /// <summary>What the mouse did in the route panel this frame.</summary>
    public struct RouteHit
    {
        public bool Engage, Cancel, ToggleAdd, Changed;
        public int Remove;
        public Box Panel;
    }

    /// <summary>
    /// The autopilot's route panel: top speed, a thrust cap (as thrust % or as the largest drive signature), your own
    /// waypoints (added by picking them in F&amp;B's list), the route summary, and ENGAGE / CANCEL. Edits
    /// <paramref name="lim"/> in place. x, y = top-left (moved up to stay on screen), w = width.
    /// </summary>
    public static RouteHit RoutePanel(PanelUi ui, float x, float y, float w, float screenH, float px, RoutePath p, RouteLimits lim,
        double fnbCap, double fullAccel, SignatureInfo sig, IList<string> waypoints, bool adding, string stopKey, double now = 0, string addHint = null)
    {
        var c = ui.C;
        var hit = new RouteHit { Remove = -1 };
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
        float h = px * 2.4f + row * 4 + lh * (infoN + 0.2f) + px * 0.8f + px * 1.6f + Math.Max(1, waypoints.Count) * lh * 1.15f + row + px * 0.8f + lines.Count * lh + px * 3.4f;
        y = Math.Max(px, Math.Min(y, screenH - h - px));
        var panel = hit.Panel = new Box(x, y, w, h);
        c.Rect(panel.X, panel.Y, panel.W, panel.H, Panel);
        c.Rect(panel.X, panel.Y, panel.W, 2, Go);
        c.Text("AUTOPILOT  to " + p.TargetName, x + pad, y + px * 0.55f, px * 1.1f, Text);
        float labelW = c.TextWidth("Signature", sp) + px * 1.4f, valueW = c.TextWidth("50.00 km/s max", sp) + px * 0.6f;
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
        c.Text("km seen Detailed (ship core) / Strong (faction) / Weak (distance only), by baseline sensors", x + pad, ty, sp * 0.85f, Dim);
        ty += lh;
        for (int k = 0; k < 2; k++)
        {
            double f = k == 0 ? lim.ThrustFraction(weight, gain) : lim.BrakeFraction(weight, gain);
            string t = (k == 0 ? "Burn " : "Brake ") + Fmt.Num(f * 100, "0") + " %  " + (fullAccel > 0 ? Fmt.Num(fullAccel * f / 9.81, "0.00") + " g" : "accel ?");
            t += weight > 0 ? "   optical: all round " + T(sig.DriveStrength(f)) + "   cone " + T(sig.Cone(f)) : "   (no fusion drives: no optical signal)";
            c.Text(t, x + pad, ty, sp * 0.9f, k == 0 ? Text : Text.WithAlpha(0.85f));
            ty += lh;
        }
        if (sig.Floor > 0)
        {
            c.Text("Radio: all round " + T(sig.Floor) + " from " + (sig.FloorWhat ?? "other emitters") + (floorWins ? ", louder than the drives" : ""),
                x + pad, ty, sp * 0.9f, floorWins ? Warn : Dim);
            ty += lh;
        }
        if (live)
        {
            c.Text("Spectrum now: all round " + T(sig.LiveAllRound) + "   cone " + T(sig.LiveCone), x + pad, ty, sp * 0.9f, Dim);
            ty += lh;
        }
        ty += lh * 0.2f + px * 0.8f;

        // your own waypoints
        c.Rect(x + pad, ty - px * 0.4f, w - pad * 2, 1, Dim.WithAlpha(0.4f));
        c.Text("WAYPOINTS", x + pad, ty, sp, Text);
        c.Text("flown in order, then the destination", x + pad + c.TextWidth("WAYPOINTS", sp) + px, ty + sp * 0.1f, sp * 0.85f, Dim);
        ty += px * 1.6f;
        if (waypoints.Count == 0) { c.Text("None yet: detours round worlds are added by themselves", x + pad, ty, sp * 0.9f, Dim); ty += lh * 1.15f; }
        for (int i = 0; i < waypoints.Count; i++)
        {
            c.Text((i + 1) + ".  " + waypoints[i], x + pad, ty, sp, MapScene.WaypointCol);
            var xb = new Box(x + w - pad - lh, ty - lh * 0.1f, lh, lh);
            if (ui.Button(xb, "x", sp * 0.9f, Stop)) hit.Remove = i;
            ty += lh * 1.15f;
        }
        if (adding)
        {
            bool blink = now % 1.0 < 0.6;
            c.Text(addHint ?? "Pick a point in Flip and Burn's list", x + pad, ty + (row - sp) / 2, sp, blink ? Warn : Warn.WithAlpha(0.6f));
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
        foreach (var (t, col) in lines) { c.Text(t, x + pad, ty, sp, col); ty += lh; }
        float by = panel.Bottom - px * 2.6f, bwid = (w - pad * 2 - px) / 2;
        hit.Engage = ui.Button(new Box(x + pad, by, bwid, px * 1.9f), "ENGAGE", px, Go);
        hit.Cancel = ui.Button(new Box(x + pad + bwid + px, by, bwid, px * 1.9f), "CANCEL", px, Dim);
        return hit;
    }

    /// <summary>The line at the top of the screen while the autopilot flies.</summary>
    public static Box StatusLine(IMapCanvas c, float screenW, float y, float px, string target, RouteExport.Status s, string stopKey)
    {
        string text = "AUTOPILOT  to " + target;
        if (s != null && s.Flying)
        {
            text += "    " + Fmt.Dist(s.Dist) + "    " + Fmt.Speed(s.Speed) + (s.Eta > 0 ? "    ETA " + Fmt.Time(s.Eta) : "");
            if (s.Count > 1) text += "    point " + Math.Min(s.Wp + 1, s.Count) + " of " + s.Count;
        }
        else text += "    starting...";
        string sub = (s != null && !string.IsNullOrEmpty(s.Msg) ? s.Msg + "    " : "") + "Move or turn to take over    Ctrl+" + stopKey + ": stop";
        float w = Math.Max(c.TextWidth(text, px), c.TextWidth(sub, px * 0.8f)) + px * 2, h = px * 2.6f;
        var b = new Box((screenW - w) / 2, y, w, h);
        c.Rect(b.X, b.Y, b.W, b.H, Panel);
        c.Rect(b.X, b.Bottom - 2, b.W, 2, Go);
        c.Text(text, screenW / 2, y + px * 0.25f, px, Go, TextAnchor.Center);
        c.Text(sub, screenW / 2, y + px * 1.4f, px * 0.8f, Dim, TextAnchor.Center);
        return b;
    }

    /// <summary>The asteroid warning (blinks while it's on the line and not dodged).</summary>
    public static Box AsteroidWarning(IMapCanvas c, float screenW, float y, float px, double dist, double speed, bool onRoute, bool dodged, bool engaged, double now, string key = "M")
    {
        string text = (onRoute ? "ASTEROID ON ROUTE  " : "ASTEROID AHEAD  ") + Fmt.Dist(dist);
        if (speed > 1) text += "  (" + Fmt.Time(dist / speed) + ")";
        string sub = dodged ? "Autopilot flies round it"
            : engaged ? "Ctrl+Shift+" + key + " steers the autopilot round it"
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
