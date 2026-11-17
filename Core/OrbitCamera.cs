using System;

namespace SDX2_SystemMap;

/// <summary>
/// A perspective camera orbiting a target point: yaw around the up axis, pitch above/below the ecliptic, distance.
/// Moves glide toward a goal (FlyTo), so re-framing on a route or a body is smooth. World axes are SE's: Y up.
/// </summary>
public sealed class OrbitCamera
{
    public const double MinDistance = 2e3, MaxDistance = 2.5e8;
    public const double MinPitch = -1.45, MaxPitch = 1.45;

    public V3 Target, GoalTarget;
    public double Distance = 6e7, GoalDistance = 6e7;
    public double Yaw = 0.6, Pitch = 0.75;
    public double FovY = 50 * Math.PI / 180;

    // per-frame basis, from Setup()
    private V3 eye, right, up, fwd;
    private double focal, cx, cy;
    public float ViewX, ViewY, ViewW, ViewH;

    public V3 Eye => eye;
    public V3 Forward => fwd;
    public double Near => Math.Max(1.0, Distance * 1e-4);

    /// <summary>Recomputes the basis for a viewport (screen pixels).</summary>
    public void Setup(float x, float y, float w, float h)
    {
        ViewX = x; ViewY = y; ViewW = w; ViewH = h;
        Pitch = Math.Max(MinPitch, Math.Min(MaxPitch, Pitch));
        double cp = Math.Cos(Pitch);
        var dir = new V3(cp * Math.Sin(Yaw), Math.Sin(Pitch), cp * Math.Cos(Yaw)); // from target toward the eye
        eye = Target + dir * Distance;
        fwd = -dir;
        right = fwd.Cross(new V3(0, 1, 0)).Normalized;
        up = right.Cross(fwd);
        focal = h / 2 / Math.Tan(FovY / 2);
        cx = x + w / 2.0; cy = y + h / 2.0;
    }

    /// <summary>World point to screen. False if it's behind the camera (depth below Near).</summary>
    public bool Project(V3 p, out float sx, out float sy, out double depth)
    {
        var r = p - eye;
        depth = r.Dot(fwd);
        if (depth < Near) { sx = sy = float.NaN; return false; }
        double k = focal / depth;
        sx = (float)(cx + r.Dot(right) * k);
        sy = (float)(cy - r.Dot(up) * k);
        return true;
    }

    public double Depth(V3 p) => (p - eye).Dot(fwd);

    /// <summary>Screen point to a world ray from the eye (unit direction); the inverse of <see cref="Project"/>.</summary>
    public V3 Ray(float sx, float sy) => (fwd * focal + right * (sx - cx) - up * (sy - cy)).Normalized;

    /// <summary>Screen pixels per metre at a given depth.</summary>
    public double PixelsPerMetre(double depth) => focal / Math.Max(Near, depth);

    /// <summary>Metres per screen pixel at the target's depth (for panning and the scale bar).</summary>
    public double MetresPerPixel => Distance / focal;

    /// <summary>Turns the view (radians).</summary>
    public void Orbit(double dYaw, double dPitch)
    {
        Yaw = Wrap(Yaw + dYaw);
        Pitch = Math.Max(MinPitch, Math.Min(MaxPitch, Pitch + dPitch));
    }

    /// <summary>Slides the target across the view by a screen drag of (dx, dy) pixels.</summary>
    public void Pan(float dx, float dy)
    {
        double m = MetresPerPixel;
        var move = right * (-dx * m) + up * (dy * m);
        Target += move; GoalTarget += move;
    }

    /// <summary>Wheel zoom: positive notches go closer.</summary>
    public void Zoom(double notches)
    {
        GoalDistance = Clamp(GoalDistance * Math.Pow(0.82, notches));
    }

    public void FlyTo(V3 target, double distance)
    {
        GoalTarget = target;
        GoalDistance = Clamp(distance);
    }

    /// <summary>Snaps straight to the goal (no glide).</summary>
    public void Snap() { Target = GoalTarget; Distance = GoalDistance; }

    /// <summary>Glides toward the goal; dt in seconds. Distance moves in log space so big zooms feel even.</summary>
    public void Tick(double dt)
    {
        double k = 1 - Math.Exp(-dt * 6);
        Target = V3.Lerp(Target, GoalTarget, k);
        Distance = Math.Exp(Math.Log(Distance) + (Math.Log(GoalDistance) - Math.Log(Distance)) * k);
    }

    /// <summary>Frames a set of points: target at their middle, far enough back to see all of them.</summary>
    public void Frame(V3[] points, double minDistance)
    {
        if (points.Length == 0) return;
        V3 lo = points[0], hi = points[0];
        foreach (var p in points)
        {
            lo = new V3(Math.Min(lo.X, p.X), Math.Min(lo.Y, p.Y), Math.Min(lo.Z, p.Z));
            hi = new V3(Math.Max(hi.X, p.X), Math.Max(hi.Y, p.Y), Math.Max(hi.Z, p.Z));
        }
        var mid = (lo + hi) * 0.5;
        double radius = (hi - lo).Length * 0.5;
        double aspect = ViewW > 0 && ViewH > 0 ? Math.Min(1.0, ViewW / ViewH) : 1;
        double d = radius / Math.Tan(FovY / 2) / aspect * 1.25;
        FlyTo(mid, Math.Max(minDistance, d));
    }

    private static double Clamp(double d) => Math.Max(MinDistance, Math.Min(MaxDistance, d));

    public static double Wrap(double a)
    {
        double t = Math.PI * 2;
        a %= t;
        return a < 0 ? a + t : a;
    }
}
