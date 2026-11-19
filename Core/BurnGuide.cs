using System;

namespace SDX2_SystemMap;

/// <summary>
/// Where to point the nose while you fly a route by hand: along the route's line toward the next point (BURN), back
/// along it (BRAKE, after the flip), or across it (COAST, only when you've drifted), tilted so the burn also takes out
/// the drift off the line and brings the ship back onto it. Drawn as a marker on the screen with your nose's marker,
/// so you line one up on the other (it works in first and third person, the crosshair isn't the nose there).
/// </summary>
public static class BurnGuide
{
    public enum Phase { None, Burn, Coast, Brake }

    /// <summary>Seconds over which a drift across the line is taken out.</summary>
    public const double FixTime = 20;
    /// <summary>The share of thrust a return to the line may use (its speed back is what that can stop again).</summary>
    public const double ReturnShare = 0.25;
    /// <summary>While coasting, a correction smaller than this share of thrust isn't worth a burn.</summary>
    public const double CoastMin = 0.05;
    /// <summary>Within this many degrees the nose counts as on the mark.</summary>
    public const double OnMark = 2;
    /// <summary>A pilot's slack (s): the flip is called this much earlier, at the speed you close on the point.</summary>
    public const double ReactTime = 6;
    /// <summary>The share of the braking thrust the hand-flown plan counts on (the rest is for a late or rough brake).</summary>
    public const double BrakeShare = 0.85;
    /// <summary>The reaction allowance never takes more than this share of the distance left.</summary>
    public const double MaxPad = 0.4;

    /// <summary>
    /// The leg to fly by hand now: the route's first leg, and when that is the last one, ending where the route stops
    /// (<see cref="RouteExport.StopPoint"/>: above a world's surface, short of a station), as the autopilot does. A
    /// world's own point is its centre: braking to stop on it flew a hauler into Deimos at 824 m/s (2026-11-19).
    /// </summary>
    public static RoutePlan HandLeg(RoutePath path)
    {
        var leg = path.First;
        if (path.Legs.Count > 1) return leg;
        var stop = RouteExport.StopPoint(path, out _);
        return V3.Distance(stop, leg.Target) < 1 ? leg : leg.EndingAt(stop);
    }

    /// <summary>
    /// The leg replanned with a pilot's slack: the stop pulled in by <see cref="ReactTime"/> at the closing speed (so
    /// FLIP comes that much earlier, and braking aims that far short until the speed is gone) and braking at
    /// <see cref="BrakeShare"/> of the thrust. For the hand-flown guide only; the autopilot flies the plain plan.
    /// </summary>
    public static RoutePlan Padded(RoutePlan leg, V3 ship, V3 vel)
    {
        if (leg == null || leg.MaxAccel <= 0.01) return leg;
        var d = leg.Target - ship;
        double dist = d.Length;
        if (dist < 1) return leg;
        double u = Math.Max(0, vel.Dot(d) / dist);
        double pad = Math.Min(u * ReactTime, MaxPad * dist);
        double brake = (leg.BrakeAccel > 0.01 ? leg.BrakeAccel : leg.MaxAccel) * BrakeShare;
        return RoutePlan.Make(leg.TargetName, ship, leg.Target - d * (pad / dist), vel, leg.MaxAccel, leg.MaxSpeed, leg.Clearance, brake, null, leg.EndSpeed);
    }

    /// <summary>
    /// The nose direction for the leg from <paramref name="from"/> to <paramref name="to"/>. accel: the ship's thrust
    /// (m/s²); phase: from the leg's plan (burn, coast or brake). Returns a unit vector, or zero with nothing to do.
    /// </summary>
    public static V3 Aim(V3 from, V3 to, V3 ship, V3 vel, double accel, Phase phase)
    {
        if (phase == Phase.None || accel <= 0) return V3.Zero;
        var u = (to - from).Normalized;
        if (u.LengthSq < 0.5) u = (to - ship).Normalized;
        if (u.LengthSq < 0.5) return V3.Zero;
        // off the line (e) and drifting across it (vl)
        var rel = ship - from;
        var e = rel - u * rel.Dot(u);
        var vl = vel - u * vel.Dot(u);
        // back toward the line no faster than a share of thrust can stop again, and the drift taken out over FixTime
        double el = e.Length;
        var back = el > 1 ? e * (-Math.Sqrt(2 * ReturnShare * accel * el) / el) : V3.Zero;
        var fix = (back - vl) * (1 / FixTime);
        double fl = fix.Length;
        if (fl > accel) { fix = fix * (accel / fl); fl = accel; } // at most 45° off the line
        switch (phase)
        {
            case Phase.Burn: return (u * accel + fix).Normalized;
            case Phase.Brake: return (u * -accel + fix).Normalized;
            default: return fl >= CoastMin * accel ? fix.Normalized : V3.Zero;
        }
    }

    /// <summary>The leg's phase now, from its plan: the flip point reached → brake; the coast reached → coast.</summary>
    public static Phase PhaseOf(RoutePlan leg)
    {
        if (leg == null) return Phase.None;
        if (!leg.HasBurnPlan) return Phase.Burn;
        if (leg.ClosingSpeed > 1 && leg.FlipAt <= 0) return Phase.Brake;
        if (leg.CoastAt <= 0 && leg.FlipAt > 0) return Phase.Coast;
        return Phase.Burn;
    }

    /// <summary>
    /// The countdown to the next change, from the leg's plan: BURN → "COAST in 1 m 20 s" or "FLIP in 45 s"; COAST →
    /// "FLIP in 2 m 10 s"; BRAKE → "TURN for X in 30 s" (another point follows) or "ARRIVE in 30 s". Null with no plan.
    /// </summary>
    public static string Next(RoutePlan leg, Phase phase, string afterName)
    {
        if (leg == null || !leg.HasBurnPlan) return null;
        string end = afterName != null ? "TURN for " + MapScene.ShortName(afterName) : "ARRIVE";
        switch (phase)
        {
            case Phase.Burn:
                if (leg.TooFastToStop) return "too fast: FLIP now";
                if (leg.BrakeTime <= 0.5) return end + " in " + Fmt.Time(leg.Eta); // burns the whole leg (a turn at speed)
                return (leg.CoastTime > 1 ? "COAST in " : "FLIP in ") + Fmt.Time(leg.BurnTime);
            case Phase.Coast: return "FLIP in " + Fmt.Time(leg.CoastTime);
            case Phase.Brake: return end + " in " + Fmt.Time(leg.Eta);
            default: return null;
        }
    }

    /// <summary>The angle (degrees) between two directions.</summary>
    public static double Angle(V3 a, V3 b)
    {
        a = a.Normalized; b = b.Normalized;
        if (a.LengthSq < 0.5 || b.LengthSq < 0.5) return 0;
        return Math.Acos(Math.Max(-1, Math.Min(1, a.Dot(b)))) * 180 / Math.PI;
    }

    /// <summary>
    /// A direction in camera space (x right, y up, looking down -z) on the screen: m11 / m22 are the projection's
    /// scales. Off the screen or behind, it's put on the screen's edge toward it (onScreen false).
    /// </summary>
    public static void Project(V3 cam, double m11, double m22, float sw, float sh, float margin, out float x, out float y, out bool onScreen)
    {
        Project(cam, m11, m22, sw, sh, margin, margin, out x, out y, out onScreen);
    }

    /// <summary>As above, with its own margin at the bottom (<paramref name="bottom"/>): a mark put on the edge stays
    /// above the game's toolbar and gauges; a mark really on the screen is left where it is.</summary>
    public static void Project(V3 cam, double m11, double m22, float sw, float sh, float margin, float bottom, out float x, out float y, out bool onScreen)
    {
        float cx = sw / 2, cy = sh / 2;
        if (cam.Z < -1e-6)
        {
            double nx = m11 * cam.X / -cam.Z, ny = m22 * cam.Y / -cam.Z;
            x = (float)(cx + nx * cx); y = (float)(cy - ny * cy);
            if (x >= margin && x <= sw - margin && y >= margin && y <= sh - margin) { onScreen = true; return; }
        }
        onScreen = false;
        double dx = cam.X * m11 * cx, dy = -cam.Y * m22 * cy;
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9) dy = 1; // straight behind: point down
        double sx = (cx - margin) / Math.Max(1e-9, Math.Abs(dx)), sy = (cy - margin) / Math.Max(1e-9, Math.Abs(dy));
        double s = Math.Min(sx, sy);
        x = (float)(cx + dx * s); y = Math.Min((float)(cy + dy * s), sh - Math.Max(margin, bottom));
    }

    /// <summary>
    /// Where the nose goes at the next change, and what it is: FLIP (straight back along the line, while burning or
    /// coasting to the flip) or TURN (along the next leg, from <paramref name="to"/> to <paramref name="after"/>, while
    /// braking for a route point, or burning a leg flown at speed). Zero with no change of heading ahead (arriving).
    /// </summary>
    public static V3 NextAim(RoutePlan leg, Phase phase, V3 from, V3 to, V3? after, out string label)
    {
        label = null;
        var u = (to - from).Normalized;
        bool turnNext = after.HasValue && (phase == Phase.Brake || (phase == Phase.Burn && leg != null && leg.HasBurnPlan && leg.BrakeTime <= 0.5));
        if (turnNext)
        {
            var u2 = (after.Value - to).Normalized;
            if (u2.LengthSq < 0.5) return V3.Zero;
            label = "TURN"; return u2;
        }
        if ((phase == Phase.Burn || phase == Phase.Coast) && u.LengthSq > 0.5) { label = "FLIP"; return u * -1; }
        return V3.Zero;
    }

    /// <summary>
    /// The next change's marker: a faint aim mark where the nose goes next (in the colour of what comes then: brake for
    /// FLIP, burn for TURN) with "NEXT FLIP 1 m 20 s", or, off the screen, a small arrow on the edge pointing at it.
    /// </summary>
    /// <summary>Where <see cref="Draw"/> last put its label tab.</summary>
    public static Box LastTab;

    public static void DrawNext(IMapCanvas c, float px, float x, float y, bool on, float fromX, float fromY, string label, string when)
    {
        if (label == null) return;
        var t = MapTheme.Current;
        var col = (label == "FLIP" ? t.Brake : t.Burn).WithAlpha(0.6f);
        float r = px * 0.6f, lw = Math.Max(1.2f, px / 12f);
        string text = "NEXT " + label + (string.IsNullOrEmpty(when) ? "" : " " + when);
        float tp = px * 0.62f, tw = c.TextWidth(text, tp);
        if (on)
        {
            AimMark(c, x, y, r, lw, col);
            c.Rect(x + r * 1.4f, y - tp * 0.6f, tw + px * 0.4f, tp * 1.25f, t.ApPanel.WithAlpha(0.55f));
            c.Text(text, x + r * 1.4f + px * 0.2f, y - tp * 0.5f, tp, col);
            return;
        }
        // on the edge: a single chevron toward it, the label on the screen's side
        float dx = x - fromX, dy = y - fromY, len = (float)Math.Sqrt(dx * dx + dy * dy);
        if (len < 1) { dx = 0; dy = 1; len = 1; }
        dx /= len; dy /= len;
        float bx = x - dx * r * 1.2f, by = y - dy * r * 1.2f;
        c.Line(bx - dy * r * 0.7f, by + dx * r * 0.7f, x, y, lw * 1.3f, col);
        c.Line(bx + dy * r * 0.7f, by - dx * r * 0.7f, x, y, lw * 1.3f, col);
        float lx = dx > 0.3f ? x - r * 1.6f - tw : dx < -0.3f ? x + r * 1.6f : x - tw / 2;
        float ly = dy > 0.3f ? y - r * 1.6f - tp * 1.2f : dy < -0.3f ? y + r * 1.6f : y - tp * 0.5f;
        c.Rect(lx - px * 0.2f, ly - tp * 0.1f, tw + px * 0.4f, tp * 1.25f, t.ApPanel.WithAlpha(0.55f));
        c.Text(text, lx, ly, tp, col);
    }

    /// <summary>The nose marker's style when the player picked one (settings), else null for the theme's own.</summary>
    public static ThemeStyle? NoseOverride;

    /// <summary>
    /// How hard to thrust, as a share (%) of the ship's full thrust (<paramref name="fullAccel"/>): BURN at the plan's
    /// thrust; BRAKE at what stops the ship (or slows it to the leg's end speed) at the point
    /// (<paramref name="dist"/> away, closing at <paramref name="closing"/>). NaN with nothing to say.
    /// </summary>
    public static double ThrustPct(Phase phase, double planAccel, double fullAccel, double dist, double closing, double endSpeed)
    {
        if (fullAccel <= 0.01) return double.NaN;
        switch (phase)
        {
            case Phase.Burn: return Math.Max(1, Math.Min(100, planAccel / fullAccel * 100));
            case Phase.Brake:
                if (dist < 1 || closing <= endSpeed) return double.NaN;
                return Math.Max(1, (closing * closing - endSpeed * endSpeed) / (2 * dist) / fullAccel * 100);
            default: return double.NaN;
        }
    }

    /// <summary>
    /// Where the ship stops against the point (m): + past it (overshoot), − short of it. While braking, from how hard it
    /// slows now (<paramref name="decel"/>, m/s²); otherwise only when even full thrust can't stop in time. NaN when it
    /// isn't closing, isn't braking yet with room to stop, or slows too little to tell.
    /// </summary>
    public static double StopError(Phase phase, double dist, double closing, double endSpeed, double decel, double fullAccel)
    {
        if (dist < 1 || closing <= endSpeed + 0.5) return double.NaN;
        double dv2 = closing * closing - endSpeed * endSpeed;
        if (phase == Phase.Brake && fullAccel > 0.01 && decel > 0.03 * fullAccel) return dv2 / (2 * decel) - dist;
        if (fullAccel > 0.01)
        {
            double full = dv2 / (2 * fullAccel);
            if (full > dist) return full - dist;
        }
        return double.NaN;
    }

    /// <summary>"overshoot 3.2 km", "short 850 m", "on the mark" (within 3% or 100 m, braking only) or null.</summary>
    public static string StopText(Phase phase, double err, double dist)
    {
        if (double.IsNaN(err)) return null;
        if (Math.Abs(err) <= Math.Max(100, 0.03 * dist)) return phase == Phase.Brake ? "stops on the mark" : null;
        return (err > 0 ? "overshoot " : "short ") + Fmt.Dist(Math.Abs(err));
    }

    /// <summary>
    /// Draws your nose's marker and the aim marker (with the phase, the angle to go and the countdown), joined by a
    /// line, both in the map theme's style; the aim marker sits on the screen's edge with an arrow when it's off the
    /// screen.
    /// </summary>
    public static void Draw(IMapCanvas c, float px, float noseX, float noseY, bool noseOn, float aimX, float aimY, bool aimOn, Phase phase, double angle, string next = null, double thrustPct = double.NaN, string stop = null, bool stopBad = false)
    {
        var t = MapTheme.Current;
        var col = phase == Phase.Brake ? t.Brake : phase == Phase.Coast ? t.Coast : t.Burn;
        bool on = angle <= OnMark;
        var mark = on ? AutopilotPanel.Go : col;
        float r = px * 0.9f, lw = Math.Max(1.5f, px / 9f);
        if (noseOn && aimOn && !on) Lead(c, noseX, noseY, aimX, aimY, lw * 0.7f, col.WithAlpha(0.45f));
        if (noseOn) Nose(c, px, noseX, noseY);

        if (aimOn) AimMark(c, aimX, aimY, r, lw, mark);
        else
        {
            // an arrow on the edge pointing at it (a double chevron in the HUD themes)
            float dx = aimX - noseX, dy = aimY - noseY, len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 1) { dx = 0; dy = -1; len = 1; }
            dx /= len; dy /= len;
            float bx = aimX - dx * r * 1.4f, by = aimY - dy * r * 1.4f;
            c.Line(bx - dy * r * 0.8f, by + dx * r * 0.8f, aimX, aimY, lw, mark);
            c.Line(bx + dy * r * 0.8f, by - dx * r * 0.8f, aimX, aimY, lw, mark);
            if (t.Style == ThemeStyle.Tachi || t.Style == ThemeStyle.Protogen)
            {
                float ex = aimX - dx * r * 0.7f, ey = aimY - dy * r * 0.7f;
                bx -= dx * r * 0.7f; by -= dy * r * 0.7f;
                c.Line(bx - dy * r * 0.8f, by + dx * r * 0.8f, ex, ey, lw, mark.WithAlpha(0.6f));
                c.Line(bx + dy * r * 0.8f, by - dx * r * 0.8f, ex, ey, lw, mark.WithAlpha(0.6f));
            }
        }
        string pct = double.IsNaN(thrustPct) ? "" : " " + (thrustPct > 100 ? ">100" : Fmt.Num(Math.Ceiling(thrustPct), "0")) + "%";
        string label = (phase == Phase.Brake ? "BRAKE" : phase == Phase.Coast ? "CORRECT" : "BURN") + pct + (on ? "" : "  " + Fmt.Num(angle, "0") + "°");
        // the label, the countdown to the next change and where the ship stops, on a tab in the theme's panel colours
        float lp = px * 0.8f, np = px * 0.7f, pad0 = px * 0.35f;
        float tw = Math.Max(c.TextWidth(label, lp), Math.Max(next != null ? c.TextWidth(next, np) : 0, stop != null ? c.TextWidth(stop, np) : 0));
        int lines = (next != null ? 1 : 0) + (stop != null ? 1 : 0);
        float pad = pad0, th = lp + np * 1.25f * lines + pad * 1.4f;
        float lx, ly;
        if (aimOn)
        {
            // above the mark (below with no room): the game writes a GPS marker's name, range and time to the right of
            // the same point, Flip and Burn its warning and retroburn countdown right and below, and its own marker
            // sits beside ours, so neither side is free
            lx = Math.Max(pad, aimX - (tw + pad * 2) / 2);
            ly = aimY - r * 2.2f - th;
            if (ly < px * 2.5f) ly = aimY + r * 2.2f;
        }
        else
        {
            bool right = aimX < tw * 2;
            lx = right ? aimX + r * 1.6f : aimX - tw - r * 1.8f - pad * 2;
            ly = aimY - th / 2;
        }
        LastTab = new Box(lx, ly, tw + pad * 2, th); // other mods' labels are kept off it (LabelSpread)
        Tab(c, LastTab, mark, lw);
        c.Text(label, lx + pad, ly + pad * 0.7f, lp, mark);
        float ty = ly + pad * 0.7f + lp * 1.15f;
        if (next != null) { c.Text(next, lx + pad, ty, np, t.ApText); ty += np * 1.25f; }
        if (stop != null) c.Text(stop, lx + pad, ty, np, stopBad ? t.Warn : t.ApDim);
    }

    /// <summary>Your nose's marker in the map theme's style (also drawn on its own in third person, in place of the crosshair).</summary>
    public static void Nose(IMapCanvas c, float px, float x, float y)
    {
        var t = MapTheme.Current;
        var col = t.Ship.WithAlpha(0.95f);
        float r = px * 0.55f, lw = Math.Max(1.5f, px / 9f);
        switch (NoseOverride ?? t.Style)
        {
            case ThemeStyle.Tachi:
                // four arc segments with gaps, a centre dot
                for (int q = 0; q < 4; q++) Arc(c, x, y, r, q * Math.PI / 2 + 0.25, q * Math.PI / 2 + Math.PI / 2 - 0.25, lw, col);
                c.Disc(x, y, lw, col);
                break;
            case ThemeStyle.Naval:
                // a ring with ruler ticks out at three bearings and a gold pip on top
                Arc(c, x, y, r, 0, 2 * Math.PI, lw, col);
                c.Line(x - r * 1.7f, y, x - r, y, lw, col); c.Line(x + r, y, x + r * 1.7f, y, lw, col);
                c.Line(x, y + r, x, y + r * 1.5f, lw, col);
                c.Rect(x - lw * 1.2f, y - r - lw * 3.2f, lw * 2.4f, lw * 2.4f, t.Trim);
                break;
            case ThemeStyle.Belter:
                // a hexagon with a hazard bar under it
                Poly(c, x, y, r * 1.1f, 6, Math.PI / 6, lw, col);
                c.Line(x - r * 0.4f, y + r * 1.5f, x + r * 0.4f, y + r * 1.5f, lw * 1.6f, t.Trim);
                c.Disc(x, y, lw * 0.9f, col);
                break;
            case ThemeStyle.Protogen:
                // a ring with chevrons either side, a magenta centre
                Arc(c, x, y, r, 0, 2 * Math.PI, lw, col);
                float g = r * 1.35f, k = r * 0.45f;
                c.Line(x - g - k, y - k, x - g, y, lw, col); c.Line(x - g - k, y + k, x - g, y, lw, col);
                c.Line(x + g + k, y - k, x + g, y, lw, col); c.Line(x + g + k, y + k, x + g, y, lw, col);
                c.Disc(x, y, lw, t.Trim);
                break;
            case ThemeStyle.Contact:
                // a radar contact cross with a gap and a dashed ring
                for (int i = 0; i < 8; i += 2) Arc(c, x, y, r, i * Math.PI / 4, (i + 1) * Math.PI / 4, lw, col.WithAlpha(0.7f));
                c.Line(x - r * 1.6f, y, x - r * 0.4f, y, lw, col); c.Line(x + r * 0.4f, y, x + r * 1.6f, y, lw, col);
                c.Line(x, y - r * 1.6f, x, y - r * 0.4f, lw, col); c.Line(x, y + r * 0.4f, x, y + r * 1.6f, lw, col);
                break;
            default:
                Arc(c, x, y, r, 0, 2 * Math.PI, lw, t.Text.WithAlpha(0.9f));
                c.Disc(x, y, lw * 0.8f, t.Text.WithAlpha(0.9f));
                break;
        }
    }

    /// <summary>Where to point, in the theme's style: a diamond (Classic), a lock box (Tachi), an octagon lock with a
    /// gold pip (UNN), a hazard triangle (Belter), a lock reticle (Protogen) or a bracketed contact (OPA).</summary>
    private static void AimMark(IMapCanvas c, float x, float y, float r, float lw, MapColor col)
    {
        var t = MapTheme.Current;
        switch (t.Style)
        {
            case ThemeStyle.Tachi:
                Corners(c, x, y, r, r * 0.55f, lw, col);
                c.Line(x - r * 0.35f, y, x + r * 0.35f, y, lw, col); c.Line(x, y - r * 0.35f, x, y + r * 0.35f, lw, col);
                break;
            case ThemeStyle.Naval:
                Poly(c, x, y, r * 1.05f, 8, Math.PI / 8, lw, col);
                c.Rect(x - lw * 1.3f, y - lw * 1.3f, lw * 2.6f, lw * 2.6f, t.Trim);
                break;
            case ThemeStyle.Belter:
                Poly(c, x, y + r * 0.15f, r * 1.15f, 3, -Math.PI / 2, lw, col);
                c.Line(x, y - r * 0.2f, x, y + r * 0.35f, lw * 1.4f, col);
                c.Disc(x, y + r * 0.6f, lw * 0.9f, col);
                break;
            case ThemeStyle.Protogen:
                Arc(c, x, y, r, 0, 2 * Math.PI, lw, col);
                Arc(c, x, y, r * 1.45f, -0.5, 0.5, lw, col.WithAlpha(0.7f)); Arc(c, x, y, r * 1.45f, Math.PI - 0.5, Math.PI + 0.5, lw, col.WithAlpha(0.7f));
                c.Line(x - r * 1.9f, y, x - r * 0.45f, y, lw, col); c.Line(x + r * 0.45f, y, x + r * 1.9f, y, lw, col);
                c.Line(x, y - r * 1.9f, x, y - r * 0.45f, lw, col); c.Line(x, y + r * 0.45f, x, y + r * 1.9f, lw, col);
                c.Disc(x, y, lw, t.Trim);
                break;
            case ThemeStyle.Contact:
                float b = r * 1.1f, k = r * 0.45f;
                c.Line(x - b, y - b, x - b + k, y - b, lw, col); c.Line(x - b, y - b, x - b, y + b, lw, col); c.Line(x - b, y + b, x - b + k, y + b, lw, col);
                c.Line(x + b, y - b, x + b - k, y - b, lw, col); c.Line(x + b, y - b, x + b, y + b, lw, col); c.Line(x + b, y + b, x + b - k, y + b, lw, col);
                Poly(c, x, y, r * 0.5f, 3, -Math.PI / 2, lw, col);
                break;
            default:
                Poly(c, x, y, r, 4, 0, lw, col);
                c.Disc(x, y, lw * 1.2f, col);
                break;
        }
    }

    /// <summary>The line from the nose to the aim: solid, or dashed in the themes with dashed orbits.</summary>
    private static void Lead(IMapCanvas c, float x0, float y0, float x1, float y1, float w, MapColor col)
    {
        var st = MapTheme.Current.Style;
        if (st != ThemeStyle.Belter && st != ThemeStyle.Contact && st != ThemeStyle.Tachi) { c.Line(x0, y0, x1, y1, w, col); return; }
        float dx = x1 - x0, dy = y1 - y0, len = (float)Math.Sqrt(dx * dx + dy * dy), dash = Math.Max(6, w * 6);
        if (len < 1) return;
        for (float s = 0; s < len; s += dash * 2)
        {
            float e = Math.Min(len, s + dash);
            c.Line(x0 + dx * s / len, y0 + dy * s / len, x0 + dx * e / len, y0 + dy * e / len, w, col);
        }
    }

    /// <summary>The label's tab: the theme's panel colour with an edge in the marker's colour (and the theme's trim).</summary>
    private static void Tab(IMapCanvas c, Box b, MapColor col, float lw)
    {
        var t = MapTheme.Current;
        c.Rect(b.X, b.Y, b.W, b.H, t.ApPanel.WithAlpha(0.8f));
        switch (t.Style)
        {
            case ThemeStyle.Tachi: Corners(c, b.X + b.W / 2, b.Y + b.H / 2, b.W / 2, Math.Min(b.H, b.W) * 0.3f, lw * 0.8f, col); break;
            case ThemeStyle.Naval: case ThemeStyle.Contact: c.Rect(b.X, b.Y, lw * 1.5f, b.H, t.Trim); break;
            case ThemeStyle.Belter: c.Rect(b.X, b.Bottom - lw * 1.5f, b.W, lw * 1.5f, t.Trim); break;
            case ThemeStyle.Protogen: c.Rect(b.X, b.Y, b.W, lw, col); c.Rect(b.X, b.Y + lw, Math.Min(b.W * 0.3f, b.H), lw, t.Trim); break;
            default: c.Rect(b.X, b.Y, lw * 1.5f, b.H, col); break;
        }
    }

    private static void Arc(IMapCanvas c, float x, float y, float r, double a0, double a1, float w, MapColor col)
    {
        int n = Math.Max(2, (int)Math.Ceiling((a1 - a0) / (Math.PI / 8)));
        for (int i = 0; i < n; i++)
        {
            double p = a0 + (a1 - a0) * i / n, q = a0 + (a1 - a0) * (i + 1) / n;
            c.Line(x + r * (float)Math.Cos(p), y + r * (float)Math.Sin(p), x + r * (float)Math.Cos(q), y + r * (float)Math.Sin(q), w, col);
        }
    }

    private static void Poly(IMapCanvas c, float x, float y, float r, int sides, double start, float w, MapColor col)
    {
        for (int i = 0; i < sides; i++)
        {
            double p = start + i * 2 * Math.PI / sides, q = start + (i + 1) * 2 * Math.PI / sides;
            c.Line(x + r * (float)Math.Cos(p), y + r * (float)Math.Sin(p), x + r * (float)Math.Cos(q), y + r * (float)Math.Sin(q), w, col);
        }
    }

    /// <summary>Four corner brackets round a square of half-size h, arms k long.</summary>
    private static void Corners(IMapCanvas c, float x, float y, float h, float k, float w, MapColor col)
    {
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
            {
                float cx = x + sx * h, cy = y + sy * h;
                c.Line(cx, cy, cx - sx * k, cy, w, col);
                c.Line(cx, cy, cx, cy - sy * k, w, col);
            }
    }
}
