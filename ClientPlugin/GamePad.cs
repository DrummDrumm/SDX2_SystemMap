using System;
using Sandbox;
using VRage.Input;

namespace SDX2_SystemMap;

/// <summary>
/// The controller for the plugin's own buttons, read through the game's GUI bindings like its menus: D-pad (MOVE_*),
/// A (ACCEPT), B (CANCEL). The left stick moves focus too, where the stick isn't flying the ship. Moves are read once
/// per game update (<see cref="Poll"/>) and kept until a draw takes them, so a draw rate above the update rate doesn't
/// move twice.
/// </summary>
internal static class GamePad
{
    private const float StickOn = 0.6f, StickOff = 0.35f;
    private const double RepeatFirst = 0.45, RepeatNext = 0.16;
    private static PanelUi.PadDir pending, stickDir;
    private static double stickNext;

    /// <summary>A controller is connected and was used last (the game shows controller icons).</summary>
    public static bool Active
    {
        get
        {
            var i = MyInput.Static;
            return i != null && i.IsJoystickLastUsed && i.IsJoystickConnected();
        }
    }

    private static bool Gui(VRage.Utils.MyStringId control, MyControlStateType state) =>
        MyControllerHelper.IsControl(MyControllerHelper.CX_GUI, control, state);

    /// <summary>A held (the game's ACCEPT).</summary>
    public static bool ADown => Active && Gui(MyControlsGUI.ACCEPT, MyControlStateType.PRESSED);
    /// <summary>B went down / came up this update (the game's CANCEL).</summary>
    public static bool BPressed => Active && Gui(MyControlsGUI.CANCEL, MyControlStateType.NEW_PRESSED);
    public static bool BReleased => Active && Gui(MyControlsGUI.CANCEL, MyControlStateType.NEW_RELEASED);
    public static bool BDown => Active && Gui(MyControlsGUI.CANCEL, MyControlStateType.PRESSED);

    /// <summary>The controller's shortcuts: RB + Select = the full map, LB + Select = the N list with the Route Plan.</summary>
    public enum Shortcut { None, FullMap, Autopilot }

    /// <summary>
    /// Select (View / Back, the game's J07) went down this update with RB (J06) or LB (J05) held, and not both. Read
    /// from the buttons themselves, whatever the game has bound to them.
    /// </summary>
    public static Shortcut Pressed()
    {
        try
        {
            var i = MyInput.Static;
            if (i == null || !i.IsJoystickConnected() || !i.IsJoystickButtonNewPressed(MyJoystickButtonsEnum.J07)) return Shortcut.None;
            bool lb = i.IsJoystickButtonPressed(MyJoystickButtonsEnum.J05), rb = i.IsJoystickButtonPressed(MyJoystickButtonsEnum.J06);
            return rb && !lb ? Shortcut.FullMap : lb && !rb ? Shortcut.Autopilot : Shortcut.None;
        }
        catch (Exception e) { Plugin.LogThrottled("controller read failed: " + e.Message); return Shortcut.None; }
    }

    /// <summary>B (the game's J02) went down this update, read from the button itself: closes what Esc closes.</summary>
    public static bool BackPressed
    {
        get
        {
            try { var i = MyInput.Static; return i != null && i.IsJoystickConnected() && i.IsJoystickButtonNewPressed(MyJoystickButtonsEnum.J02); }
            catch { return false; }
        }
    }

    /// <summary>A new D-pad push this update (with the game's key repeat while held).</summary>
    public static PanelUi.PadDir Dpad()
    {
        if (!Active) return PanelUi.PadDir.None;
        const MyControlStateType r = MyControlStateType.NEW_PRESSED_REPEATING;
        return Gui(MyControlsGUI.MOVE_UP, r) ? PanelUi.PadDir.Up
            : Gui(MyControlsGUI.MOVE_DOWN, r) ? PanelUi.PadDir.Down
            : Gui(MyControlsGUI.MOVE_LEFT, r) ? PanelUi.PadDir.Left
            : Gui(MyControlsGUI.MOVE_RIGHT, r) ? PanelUi.PadDir.Right
            : PanelUi.PadDir.None;
    }

    /// <summary>Once per game update: the D-pad, and the left stick (pushed past 60%, repeating while held).</summary>
    public static void Poll(double now)
    {
        try
        {
            if (!Active) { pending = stickDir = PanelUi.PadDir.None; return; }
            var d = Dpad();
            if (d != PanelUi.PadDir.None) { pending = d; return; }
            var s = Stick();
            if (s == PanelUi.PadDir.None) { stickDir = s; return; }
            if (s != stickDir) { stickDir = s; stickNext = now + RepeatFirst; pending = s; }
            else if (now >= stickNext) { stickNext = now + RepeatNext; pending = s; }
        }
        catch (Exception e) { Plugin.LogThrottled("controller read failed: " + e.Message); }
    }

    private static PanelUi.PadDir Stick()
    {
        var i = MyInput.Static;
        float up = i.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Yneg), down = i.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Ypos);
        float left = i.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Xneg), right = i.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Xpos);
        float y = down - up, x = right - left;
        float on = stickDir == PanelUi.PadDir.None ? StickOn : StickOff; // held: let go a little before it counts as released
        if (Math.Abs(y) >= Math.Abs(x)) return Math.Abs(y) < on ? PanelUi.PadDir.None : y < 0 ? PanelUi.PadDir.Up : PanelUi.PadDir.Down;
        return Math.Abs(x) < on ? PanelUi.PadDir.None : x < 0 ? PanelUi.PadDir.Left : PanelUi.PadDir.Right;
    }

    /// <summary>The move since the last draw (taken once).</summary>
    public static PanelUi.PadDir Take()
    {
        var d = pending;
        pending = PanelUi.PadDir.None;
        return d;
    }
}
