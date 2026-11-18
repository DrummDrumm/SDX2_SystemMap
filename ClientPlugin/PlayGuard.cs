using System;
using Sandbox.Game.Gui;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.World;
using Sandbox.Graphics.GUI;
using VRage.Input;

namespace SDX2_SystemMap;

/// <summary>
/// SDX2's client-plugin rules: the plugin must switch itself off while you're in admin mode or AFK. While
/// <see cref="Blocked"/> is set nothing is written to the autopilot PB or Flip and Burn, the asteroid watch stops, the
/// speedometer fix steps aside and the keys and mouse do nothing. In admin mode the map, preview and PiP are hidden and
/// the full map closes too; AFK (<see cref="ViewOnly"/>) leaves the full map and the pinned preview up as a read-only
/// view (your ship and route keep moving on it; it only reads, like the game's own map). Everything comes back on the
/// first input once the reason has gone.
///  - Admin mode: creative tools, any admin-menu toggle (invulnerable, show players, use terminals...), the admin menu
///    open, or the spectator camera.
///  - AFK: no key, mouse button, mouse movement, wheel or joystick input for 30 s (the game's own AFK rule,
///    MySessionComponentTrash.CONST_TIME_TO_AFK, tracked here so it also counts input on the full map screen).
/// </summary>
internal static class PlayGuard
{
    public const double AfkSeconds = 30;
    private static double lastInput = -1;

    /// <summary>Why the plugin is off right now ("admin mode", "spectator", "AFK"...), or null when it may run.</summary>
    public static string Reason { get; private set; }
    public static bool Blocked => Reason != null;
    /// <summary>Paused for AFK only: the full map and the pinned preview may still be shown (read-only).</summary>
    public static bool ViewOnly => Reason == Afk;
    private const string Afk = "AFK";

    public static void Reset() { lastInput = -1; Reason = null; axisSeen = null; }

    /// <summary>Input the game doesn't see (a touch or click on the second screen's map) counts as being here too.</summary>
    public static void NoteInput(double now) { if (lastInput >= 0) lastInput = now; }

    /// <summary>Every frame, before anything else (Plugin.Update).</summary>
    public static void Update(double now)
    {
        string was = Reason;
        Reason = Check(now);
        if (Reason != null && was == null) Plugin.Log("paused: " + Reason);
        else if (Reason == null && was != null) Plugin.Log("running again");
    }

    private static string Check(double now)
    {
        var s = MySession.Static;
        if (s == null) return null;
        try
        {
            var input = MyInput.Static;
            if (lastInput < 0 || input != null && (input.IsAnyKeyPress() || input.IsAnyNewMousePressed() || input.IsAnyMousePressed()
                || input.GetMouseX() != 0 || input.GetMouseY() != 0 || input.DeltaMouseScrollWheelValue() != 0 || ControllerUsed(input)))
                lastInput = now;
            ulong me = Sync.MyId;
            if (s.CreativeToolsEnabled(me)) return "admin creative tools";
            if (s.AdminSettings != AdminSettingsEnum.None) return "admin mode";
            if (MyScreenManager.IsScreenOfTypeOpen(typeof(MyGuiScreenAdminMenu))) return "admin menu";
            if (s.IsCameraUserAnySpectator()) return "spectator camera";
            if (now - lastInput > AfkSeconds) return Afk;
            return null;
        }
        catch (Exception e)
        {
            Plugin.LogThrottled("admin/AFK check failed, pausing: " + e.Message);
            return "admin/AFK check failed";
        }
    }

    private static readonly MyJoystickAxesEnum[] Axes = (MyJoystickAxesEnum[])Enum.GetValues(typeof(MyJoystickAxesEnum));
    private static readonly MyJoystickButtonsEnum[] Buttons = (MyJoystickButtonsEnum[])Enum.GetValues(typeof(MyJoystickButtonsEnum));
    private static float[] axisSeen;

    /// <summary>
    /// A controller button held, or a stick, trigger or slider moved more than 5% since it was last seen. Not the game's
    /// IsJoystickIdle: that wants every axis and both sliders centred, so a pad without sliders (an Xbox pad reads them
    /// as 0) or a throttle left off-centre never counts as idle, and AFK never comes.
    /// </summary>
    private static bool ControllerUsed(IMyInput input)
    {
        if (!input.IsJoystickConnected()) { axisSeen = null; return false; }
        bool used = false;
        foreach (var b in Buttons)
            if (b != MyJoystickButtonsEnum.None && input.IsJoystickButtonPressed(b)) { used = true; break; }
        if (axisSeen == null) { axisSeen = new float[Axes.Length]; for (int i = 0; i < axisSeen.Length; i++) axisSeen[i] = float.NaN; }
        for (int i = 0; i < Axes.Length; i++)
        {
            if (Axes[i] == MyJoystickAxesEnum.None) continue;
            float v = input.GetJoystickAxisStateForGameplay(Axes[i]);
            if (float.IsNaN(axisSeen[i])) { axisSeen[i] = v; continue; }
            if (Math.Abs(v - axisSeen[i]) > 0.05f) { axisSeen[i] = v; used = true; }
        }
        return used;
    }
}
