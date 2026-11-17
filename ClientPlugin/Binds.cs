using System;
using System.Collections.Generic;
using VRage.Input;

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
