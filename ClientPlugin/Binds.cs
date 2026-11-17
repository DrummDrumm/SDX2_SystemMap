using System;
using System.Collections.Generic;
using VRage;
using VRage.Input;
using VRage.ModAPI;

namespace SDX2_SystemMap;

/// <summary>The plugin's key bindings (MapSettings Key*), read from the settings each time so a change applies at once.</summary>
internal static class Binds
{
    public static KeyCombo Map => Get(Plugin.Settings.KeyMap);
    public static KeyCombo Autopilot => Get(Plugin.Settings.KeyAutopilot);
    public static KeyCombo Pin => Get(Plugin.Settings.KeyPin);
    public static KeyCombo Steer => Get(Plugin.Settings.KeySteer);
    public static KeyCombo Settings => Get(Plugin.Settings.KeySettings);

    private static readonly Dictionary<string, KeyCombo> cache = new Dictionary<string, KeyCombo>();
    private static KeyCombo Get(string text)
    {
        text ??= "";
        if (!cache.TryGetValue(text, out var k))
        {
            k = KeyCombo.Parse(text);
            if (!k.IsNone && GameKey(k) == MyKeys.None) { Plugin.Log("'" + text + "' isn't a game key: that binding is off"); k = KeyCombo.None; }
            cache[text] = k;
        }
        return k;
    }

    public static MyKeys GameKey(KeyCombo k) =>
        !k.IsNone && Enum.TryParse(k.Key, true, out MyKeys key) ? key : MyKeys.None;

    /// <summary>Pressed this frame, with exactly its modifiers held.</summary>
    public static bool Pressed(this KeyCombo k)
    {
        var input = MyInput.Static;
        var key = GameKey(k);
        return input != null && key != MyKeys.None && input.IsNewKeyPressed(key)
            && input.IsAnyCtrlKeyPressed() == k.Ctrl && input.IsAnyShiftKeyPressed() == k.Shift && input.IsAnyAltKeyPressed() == k.Alt;
    }

    /// <summary>
    /// The game control that also reacts to this combo (the player's current game bindings, primary or secondary key), or
    /// null. <paramref name="exact"/>: it has the same modifiers. Otherwise it's a game key with no modifiers, which the
    /// game fires with Ctrl/Shift/Alt held too (MyControl.IsNewPressed), e.g. M (symmetry) on Shift+M while building.
    /// </summary>
    public static string GameConflict(KeyCombo k, out bool exact)
    {
        exact = false;
        string loose = null;
        try
        {
            var key = GameKey(k);
            var input = MyInput.Static;
            if (key == MyKeys.None || input == null) return null;
            foreach (var c in input.GetGameControlsList())
            {
                if (c == null) continue;
                for (int i = 0; i < 2; i++)
                {
                    if ((i == 0 ? c.GetKeyboardControl() : c.GetSecondKeyboardControl()) != key) continue;
                    var m = i == 0 ? c.GetKeyboardModifier() : c.GetSecondKeyboardModifier();
                    bool ctrl = (m & (MyKeyboardModifiers.Control | MyKeyboardModifiers.LeftControl | MyKeyboardModifiers.RightControl)) != 0;
                    bool shift = (m & (MyKeyboardModifiers.Shift | MyKeyboardModifiers.LeftShift | MyKeyboardModifiers.RightShift)) != 0;
                    bool alt = (m & (MyKeyboardModifiers.Alt | MyKeyboardModifiers.LeftAlt | MyKeyboardModifiers.RightAlt)) != 0;
                    string name = MyTexts.GetString(c.GetControlName());
                    if (ctrl == k.Ctrl && shift == k.Shift && alt == k.Alt) { exact = true; return name; }
                    if (!ctrl && !shift && !alt && loose == null) loose = name;
                }
            }
        }
        catch (Exception e) { Plugin.LogThrottled("can't read the game's key bindings: " + e.Message); }
        return loose;
    }

    /// <summary>World load: a HUD warning for each binding that is also one of the game's keys (same modifiers).</summary>
    public static void WarnConflicts()
    {
        void One(KeyCombo k, string what)
        {
            var game = GameConflict(k, out bool exact);
            if (game == null || !exact) return;
            string msg = "SDX2 System Map: " + k.Display + " (" + what + ") is also the game's \"" + game + "\" key. Rebind it in the plugin's settings.";
            Plugin.Log(msg);
            try { Sandbox.ModAPI.MyAPIGateway.Utilities?.ShowNotification(msg, 10000, "Red"); } catch { }
        }
        One(Map, "map"); One(Autopilot, "autopilot"); One(Pin, "pin the map"); One(Steer, "steer round asteroid"); One(Settings, "settings");
    }

    /// <summary>Keys a binding can't use: modifiers, mouse buttons and the Windows keys.</summary>
    public static bool Bindable(MyKeys k)
    {
        switch (k)
        {
            case MyKeys.None: case MyKeys.LeftButton: case MyKeys.RightButton: case MyKeys.MiddleButton: case MyKeys.ExtraButton1: case MyKeys.ExtraButton2:
            case MyKeys.Shift: case MyKeys.Control: case MyKeys.Alt: case MyKeys.LeftShift: case MyKeys.RightShift: case MyKeys.LeftControl:
            case MyKeys.RightControl: case MyKeys.LeftAlt: case MyKeys.RightAlt: case MyKeys.LeftWindows: case MyKeys.RightWindows: case MyKeys.Apps:
            case MyKeys.Escape: case MyKeys.Back: case MyKeys.Delete: case MyKeys.CapsLock:
                return false;
            default: return true;
        }
    }
}
