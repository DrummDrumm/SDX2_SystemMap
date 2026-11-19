namespace SDX2_SystemMap;

/// <summary>
/// Keys that never control the game: volume, media and browser keys (Windows key codes 166-183) and Sleep (95). A pad's
/// driver, a volume knob or a stuck media key can hold one down, and the game would then count every frame as keyboard
/// use and switch away from the controller after each press (on the user's PC: VolumeUp, 522 times a minute).
/// </summary>
public static class SystemKeys
{
    public static bool Is(int code) => code == 95 || (code >= 166 && code <= 183);
}

/// <summary>
/// One controller trigger read as a clean button: it goes down past <see cref="On"/>, comes up below <see cref="Off"/>,
/// and keeps each state at least <see cref="Hold"/> seconds. A trigger held half-way that wobbles around the game's 50%
/// line no longer gives a new press each wobble. Used by the controller trigger fix (ClientPlugin/ControllerGuard.cs).
/// </summary>
public struct TriggerLatch
{
    public const float On = 0.55f, Off = 0.3f;
    public const double Hold = 0.06;

    public bool Down;
    private double changed;

    /// <summary>Feed the trigger's travel (0 = released, 1 = fully pulled); returns whether it counts as down.</summary>
    public bool Update(float travel, double now)
    {
        if (now - changed < Hold && now >= changed) return Down;
        bool next = Down ? travel > Off : travel >= On;
        if (next != Down) { Down = next; changed = now; }
        return Down;
    }

    /// <summary>The game's shared trigger axis (DirectInput Z, 0..65535, centre 32767): the left trigger pushes it up
    /// (Zpos), the right one down (Zneg). With both down, the one pulled first keeps it, instead of the two cancelling.</summary>
    public static int SharedAxis(bool left, bool right, bool leftFirst)
    {
        if (left && (!right || leftFirst)) return 65535;
        if (right) return 0;
        return 32767;
    }
}
