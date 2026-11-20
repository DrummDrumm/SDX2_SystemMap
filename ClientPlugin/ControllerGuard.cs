using System;
using System.Collections.Generic;
using HarmonyLib;
using VRage.Input;

namespace SDX2_SystemMap;

/// <summary>
/// The controller flicker: the game shows controller or keyboard/mouse icons (and runs its menus that way) by the
/// device it thinks you use last (IsJoystickLastUsed). Each frame it switches to keyboard/mouse on any key down, mouse
/// button, movement or scroll, and back on any controller input. Its "any key" test also counts key code 0xFF, which no
/// real key has but hotkey drivers and input tools send. The list of pressed keys stops at 0xFE, so on your PC it
/// switched about 480 times a minute with nothing you could see behind it.
///
/// The same happens with a volume or media key held down (on the user's PC, VolumeUp was down the whole time: 522
/// switches a minute). Those keys never control the game (<see cref="SystemKeys"/>), so they don't count either.
///
/// This blocks a switch to keyboard/mouse when the list of pressed keys is empty (or holds only volume/media keys) and no mouse button is down, the mouse
/// hasn't moved and the wheel hasn't turned. Any real key or mouse use still switches. The guard used to live in Script
/// HUD, so the flicker came back when that plugin was turned off. It's on in every world (not only SDX2), and
/// SteadyController=false turns it off. Blocked switches are logged once a minute.
///
/// The setter is small enough for the runtime to inline into the game's input update, where a patch on it never runs.
/// So the change event is watched too (<see cref="Hook"/>): a phantom switch that gets through is put back in the same
/// frame, before anything is drawn, the way Script HUD's guard did it.
/// </summary>
[HarmonyPatch(typeof(MyVRageInput), nameof(MyVRageInput.IsJoystickLastUsed), MethodType.Setter)]
internal static class ControllerGuard
{
    private static readonly List<MyKeys> keys = new List<MyKeys>();
    private static int blocked, logged;
    private static double nextLog;

    private static bool Prefix(MyVRageInput __instance, bool value)
    {
        try
        {
            if (value || undoing || !Plugin.Settings.SteadyController || !__instance.IsJoystickLastUsed || !__instance.IsJoystickConnected()) return true;
            if (!Phantom(__instance)) return true;
            Count();
            return false;
        }
        catch (Exception e) { Plugin.LogThrottled("controller guard failed: " + e.Message); return true; }
    }

    private static void Count()
    {
        blocked++;
        double now = Plugin.Now;
        if (now >= nextLog && blocked > logged)
        {
            Plugin.Log("controller: kept " + (blocked - logged) + " switch(es) to keyboard/mouse with no key, button or mouse movement behind them (a phantom key)");
            logged = blocked; nextLog = now + 60;
        }
    }

    private static MyVRageInput hooked;
    private static bool undoing;

    /// <summary>Each update (menus too): watch the game's input for switches the patch didn't see.</summary>
    public static void Hook()
    {
        if (MyInput.Static is MyVRageInput input && !ReferenceEquals(input, hooked))
        {
            try { if (hooked != null) hooked.OnJoystickLastUsedChanged -= Changed; } catch { }
            hooked = input;
            input.OnJoystickLastUsedChanged += Changed;
        }
    }

    // Why the game switched modes, counted and logged once a minute (to find what's behind a flicker).
    private static int toPad, toKeys, byKey, byButton, byMove, byScroll;
    private static double nextSwitchLog;
    private static string lastKeys = "";

    private static void Note(MyVRageInput input)
    {
        try
        {
            if (input.IsJoystickLastUsed) toPad++;
            else
            {
                toKeys++;
                keys.Clear();
                input.GetPressedKeys(keys);
                if (keys.Count > 0) { byKey++; lastKeys = string.Join("+", keys); }
                if (input.IsAnyMousePressed()) byButton++;
                if (input.IsMouseMoved()) byMove++;
                if (input.IsScrolled()) byScroll++;
            }
            double now = Plugin.Now;
            if (now < nextSwitchLog || toPad + toKeys < 6) return;
            Plugin.Log("controller: mode switched " + toPad + "x to controller, " + toKeys + "x to keyboard/mouse in the last minute (keys " + byKey
                + (byKey > 0 ? " [" + lastKeys + "]" : "") + ", mouse button " + byButton + ", mouse moved " + byMove + ", scroll " + byScroll + ")");
            toPad = toKeys = byKey = byButton = byMove = byScroll = 0;
            nextSwitchLog = now + 60;
        }
        catch { }
    }

    private static void Changed()
    {
        var input = hooked;
        if (input != null && !undoing) Note(input);
        if (input == null || undoing || input.IsJoystickLastUsed || !Plugin.Settings.SteadyController) return;
        try
        {
            if (!input.IsJoystickConnected() || !Phantom(input)) return;
            undoing = true;
            input.IsJoystickLastUsed = true;
            Count();
        }
        catch (Exception e) { Plugin.LogThrottled("controller guard failed: " + e.Message); }
        finally { undoing = false; }
    }

    /// <summary>A switch to keyboard/mouse with nothing real behind it (see <see cref="Phantom(bool, bool, bool, bool)"/>).</summary>
    private static bool Phantom(MyVRageInput input)
    {
        keys.Clear();
        input.GetPressedKeys(keys);
        bool gameKey = false;
        foreach (var k in keys) if (!SystemKeys.Is((int)k)) { gameKey = true; break; }
        return Phantom(gameKey, input.IsAnyMousePressed(), input.IsMouseMoved(), input.IsScrolled());
    }

    /// <summary>No key in the pressed list, no mouse button, no movement, no scroll: the game's "any key" saw a code
    /// no real key has.</summary>
    public static bool Phantom(bool keyListed, bool mouseButton, bool mouseMoved, bool scrolled) =>
        !keyListed && !mouseButton && !mouseMoved && !scrolled;
}

/// <summary>
/// The controller triggers as clean buttons. The game reads both triggers through one shared axis (DirectInput Z:
/// left trigger up, right trigger down), and its menus page and switch on each new push of that axis past 50%. A trigger
/// that wobbles near that line, or both triggers pulled together, gives a stream of new presses: the radial menu pages
/// back and forth and the screen flickers. When the pad also has XInput (an Xbox-style pad), this reads each trigger on
/// its own from XInput, latches it as a button (<see cref="TriggerLatch"/>), and writes the shared axis and the separate
/// trigger values from that. Off with SteadyController=false.
/// </summary>
internal static class TriggerFix
{
    private static AccessTools.FieldRef<object, object> xinput;
    private static TriggerLatch left, right;
    private static bool leftFirst;
    private static int fixedCount;

    public static void Apply(Harmony harmony)
    {
        try
        {
            var type = AccessTools.TypeByName("VRage.Platform.Windows.Input.MyJoystick");
            var method = type == null ? null : AccessTools.Method(type, "GetJoystickState");
            var field = type == null ? null : AccessTools.Field(type, "m_joystickXInput");
            if (method == null || field == null) { Plugin.Log("controller: trigger fix not applied (game input code changed)"); return; }
            xinput = AccessTools.FieldRefAccess<object, object>(field);
            harmony.Patch(method, postfix: new HarmonyMethod(typeof(TriggerFix), nameof(Postfix)));
        }
        catch (Exception e) { Plugin.Log("controller: trigger fix not applied: " + e.Message); }
    }

    private static void Postfix(object __instance, ref MyJoystickState state)
    {
        try
        {
            if (!Plugin.Settings.SteadyController || xinput(__instance) == null) return;
            double now = Plugin.Now;
            bool wasL = left.Down, wasR = right.Down;
            bool l = left.Update(state.Z_Left / 65280f, now), r = right.Update(state.Z_Right / 65280f, now);
            if (l && !wasL && !r) leftFirst = true;
            else if (r && !wasR && !l) leftFirst = false;
            int z = TriggerLatch.SharedAxis(l, r, leftFirst);
            if (Math.Abs(state.Z - z) > 8000 && ++fixedCount == 1) Plugin.Log("controller: steadying the triggers (reading LT/RT from XInput)");
            state.Z = z;
            state.Z_Left = l ? 65280 : 0;
            state.Z_Right = r ? 65280 : 0;
        }
        catch (Exception e) { Plugin.LogThrottled("controller trigger fix failed: " + e.Message); }
    }
}

/// <summary>
/// While the buttons under the N list have the controller (<see cref="SidePreview.PadInput"/>), the game screen's own
/// input is skipped: A, B, the D-pad and the sticks don't reach the ship, the character or the toolbar. The ship is
/// told to hold still (as the game does while its input is off).
/// </summary>
[HarmonyPatch(typeof(Sandbox.Game.Gui.MyGuiScreenGamePlay), nameof(Sandbox.Game.Gui.MyGuiScreenGamePlay.HandleUnhandledInput))]
internal static class GamePlayInputPatch
{
    private static bool Prefix()
    {
        try
        {
            if (!WorldGate.Active) return true;
            // LB / RB + Select is ours: the game's Select (the control panel) stays shut
            bool chord = GamePad.SelectShortcutHeld();
            if (!SidePreview.PadInput() && !chord) return true;
            Sandbox.Game.World.MySession.Static?.ControlledEntity?.MoveAndRotate(VRageMath.Vector3.Zero, VRageMath.Vector2.Zero, 0f);
            return false;
        }
        catch (Exception e) { Plugin.LogThrottled("controller input hold failed: " + e.Message); return true; }
    }
}
