using System;

namespace SDX2_SystemMap;

/// <summary>
/// "Turn with the ship": while a view follows your ship, the camera turns (yaw only; your tilt stays) so the ship's
/// forward movement points up the screen. Below <see cref="MinSpeed"/> across the map's plane it uses the way the ship
/// faces instead. Offset is your own turn on top (an orbit drag), so a turned view keeps turning with the ship.
/// </summary>
public sealed class HeadingFollow
{
    /// <summary>Slower than this across the map's plane, the ship's facing is used.</summary>
    public const double MinSpeed = 5;
    /// <summary>How fast the view catches up (per second).</summary>
    public const double Rate = 4;

    public double Offset;

    /// <summary>The direction across the map's plane (XZ) that should point up the screen. False when there's none.</summary>
    public static bool HeadingOf(V3 velocity, V3 facing, out V3 heading)
    {
        var hv = new V3(velocity.X, 0, velocity.Z);
        if (hv.Length >= MinSpeed) { heading = hv.Normalized; return true; }
        var hf = new V3(facing.X, 0, facing.Z);
        if (hf.Length > 0.1) { heading = hf.Normalized; return true; }
        heading = default;
        return false;
    }

    /// <summary>The camera yaw that looks along heading (so it points up the screen).</summary>
    public static double YawFor(V3 heading) => OrbitCamera.Wrap(Math.Atan2(-heading.X, -heading.Z));

    /// <summary>Turns the camera toward the heading (smoothly; dt 0 = at once).</summary>
    public void Update(OrbitCamera cam, V3 velocity, V3 facing, double dt)
    {
        if (!HeadingOf(velocity, facing, out var h)) return;
        double goal = YawFor(h) + Offset;
        double d = Math.IEEERemainder(goal - cam.Yaw, Math.PI * 2); // the short way round
        double k = dt <= 0 ? 1 : 1 - Math.Exp(-dt * Rate);
        cam.Yaw = OrbitCamera.Wrap(cam.Yaw + d * k);
    }

    /// <summary>An orbit drag while turning with the ship: kept as your own turn on top.</summary>
    public void Turned(double dYaw) => Offset = Math.IEEERemainder(Offset + dYaw, Math.PI * 2);
}
