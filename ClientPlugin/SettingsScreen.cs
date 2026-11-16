using System;
using System.Collections.Generic;
using System.Globalization;
using Sandbox;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Input;
using VRage.Utils;
using VRageMath;

namespace SDX2_SystemMap;

/// <summary>
/// The settings window: Pulsar's Config button for the plugin, or Ctrl+Alt+/ in game. Changes apply at once and are
/// saved when it closes. The autopilot route panel keeps its own limits; the map's layers stay on their keys in the full map.
/// </summary>
internal sealed class SettingsScreen : MyGuiScreenBase
{
    private const float RowH = 0.048f, LabelX = -0.27f, ControlX = 0.02f, SliderW = 0.19f;
    private static SettingsScreen open;
    private float y;

    public static bool IsOpen => open != null;

    public static void Open()
    {
        if (open != null) return;
        try
        {
            open = new SettingsScreen();
            open.Closed += (s, unloading) => { open = null; Plugin.SaveSettings(); };
            MyGuiSandbox.AddScreen(open);
        }
        catch (Exception e) { open = null; Plugin.Log("can't open the settings window: " + e.Message); }
    }

    /// <summary>Ctrl+Alt+/ (the ? key): every frame from Plugin.Update, also while paused.</summary>
    public static void CheckHotkey()
    {
        var input = MyInput.Static;
        if (input == null || open != null || !input.IsNewKeyPressed(MyKeys.OemQuestion)) return;
        if (!input.IsAnyCtrlKeyPressed() || !input.IsAnyAltKeyPressed()) return;
        if (MyAPIGateway.Gui?.ChatEntryVisible ?? false) return;
        Open();
    }

    private SettingsScreen() : base(new Vector2(0.5f, 0.5f), MyGuiConstants.SCREEN_BACKGROUND_COLOR, new Vector2(0.62f, 0.86f), false, null,
        MySandboxGame.Config.UIBkOpacity, MySandboxGame.Config.UIOpacity)
    {
        EnabledBackgroundFade = true;
        CloseButtonEnabled = true;
        RecreateControls(true);
    }

    public override string GetFriendlyName() => "SDX2SystemMapSettings";

    public override void RecreateControls(bool constructor)
    {
        base.RecreateControls(constructor);
        var s = Plugin.Settings;
        var caption = AddCaption("SDX2 System Map settings");
        y = caption.Position.Y + caption.Size.Y / 2 + 0.03f;

        Section("MAP WINDOW");
        Combo("Preview key", "Toggles the map window. Shift+key opens the full map, Ctrl+key the autopilot, Alt+key pins the map.",
            Keys(), KeyIndex(s.PreviewKey), i => s.PreviewKey = Keys()[i]);
        Check("Open with Flip and Burn's list", "Show the map window whenever the N list opens.", s.ShowMapWithNav, v => s.ShowMapWithNav = v);
        Check("Keep the list open on a pick", "Flip and Burn's N list stays open when you pick a destination.", s.KeepNavOpen, v => s.KeepNavOpen = v);
        Check("Window on the left", "Put the map window on the left of the N list instead of the right.", s.PreviewLeft, v => s.PreviewLeft = v);
        Slider("Window size", s.PreviewSize, 0.5f, 1.5f, v => s.PreviewSize = v, v => v.ToString("0.00", CultureInfo.InvariantCulture) + "x");
        var themes = new List<string>(); foreach (var t in MapTheme.All) themes.Add(t.Name);
        Combo("Theme", "The look of the map, the preview and the autopilot panels (K in the full map).", themes, Math.Max(0, themes.IndexOf(MapTheme.Current.Name)),
            i => { MapTheme.Current = MapTheme.All[i]; s.Theme = MapTheme.Current.Name; });

        Section("PINNED MAP");
        Check("Pin the map on screen", "A small map stays in a screen corner while you fly (Alt+key).", s.Pip, v => SidePreview.SetPip(v));
        var corners = new List<string> { "TR", "BR", "BL", "TL" };
        var cornerNames = new List<string>(); foreach (var c in corners) cornerNames.Add(Cap(SidePreview.CornerName(c)));
        Combo("Corner", "Which corner it sits in (Q in the full map).", cornerNames, Math.Max(0, corners.IndexOf(s.PipCorner)), i => s.PipCorner = corners[i]);
        Slider("Size", s.PipSize, 0.5f, 2f, v => s.PipSize = v, v => v.ToString("0.00", CultureInfo.InvariantCulture) + "x");

        Section("ROUTE");
        Check("Detours round worlds", "Bend the route round planets and moons in the way (V in the full map).", s.AvoidBodies, v => { s.AvoidBodies = v; Replan(); });
        Slider("Clearance", (float)s.ClearanceKm, 10, 5000, v => { s.ClearanceKm = Math.Round(v / 10) * 10; Replan(); },
            v => (Math.Round(v / 10) * 10).ToString("0", CultureInfo.InvariantCulture) + " km");
        Check("Load new plans into the autopilot", "Send each new route to the autopilot PB without flying it (ENGAGE still starts it).", s.AutopilotAutoSend, v => s.AutopilotAutoSend = v);

        Section("HUD");
        Check("Steady speed dial", "Show Flip and Burn's real speed on the HUD speed dial so it doesn't flicker to 150 m/s.", s.FixSpeedometer, v => s.FixSpeedometer = v);

        float bottom = m_size.Value.Y / 2 - 0.035f;
        Controls.Add(new MyGuiControlButton(new Vector2(-0.1f, bottom), text: new System.Text.StringBuilder("Defaults"),
            toolTip: "Put every setting in this window back to its default.", originAlign: MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_BOTTOM,
            onButtonClick: b => ResetDefaults()));
        Controls.Add(new MyGuiControlButton(new Vector2(0.1f, bottom), text: new System.Text.StringBuilder("Close"),
            originAlign: MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_BOTTOM, onButtonClick: b => CloseScreen()));
        Controls.Add(new MyGuiControlLabel(new Vector2(0, bottom - 0.065f), text: "Ctrl+Alt+/ opens this window in game. Changes apply at once.",
            textScale: 0.7f, originAlign: MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_BOTTOM) { ColorMask = new Vector4(0.7f, 0.7f, 0.7f, 1) });
    }

    private void ResetDefaults()
    {
        var s = Plugin.Settings; var d = new MapSettings();
        s.PreviewKey = d.PreviewKey; s.ShowMapWithNav = d.ShowMapWithNav; s.KeepNavOpen = d.KeepNavOpen; s.PreviewLeft = d.PreviewLeft;
        s.PreviewSize = d.PreviewSize; s.Theme = d.Theme; MapTheme.Current = MapTheme.Find(d.Theme);
        s.PipCorner = d.PipCorner; s.PipSize = d.PipSize; SidePreview.SetPip(d.Pip);
        s.AvoidBodies = d.AvoidBodies; s.ClearanceKm = d.ClearanceKm; s.AutopilotAutoSend = d.AutopilotAutoSend; s.FixSpeedometer = d.FixSpeedometer;
        Replan();
        RecreateControls(false);
    }

    private static void Replan()
    {
        try { if (Sandbox.Game.World.MySession.Static != null) SidePreview.Replan(); else SidePreview.ApplySettings(Plugin.Settings); }
        catch (Exception e) { Plugin.LogThrottled("re-plan from settings failed: " + e.Message); }
    }

    // ---- rows

    private void Section(string title)
    {
        y += 0.012f;
        Controls.Add(new MyGuiControlLabel(new Vector2(LabelX, y), text: title, textScale: 0.75f, originAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER)
            { ColorMask = new Vector4(1f, 0.75f, 0.35f, 1) });
        var sep = new MyGuiControlSeparatorList();
        sep.AddHorizontal(new Vector2(LabelX, y + 0.018f), -LabelX * 2);
        Controls.Add(sep);
        y += RowH * 0.8f;
    }

    private void Label(string text, string tip)
    {
        var l = new MyGuiControlLabel(new Vector2(LabelX + 0.01f, y), text: text, originAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
        if (tip != null) l.SetToolTip(tip);
        Controls.Add(l);
    }

    private void Check(string text, string tip, bool value, Action<bool> set)
    {
        Label(text, tip);
        var c = new MyGuiControlCheckbox(new Vector2(ControlX, y), toolTip: tip, isChecked: value, originAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
        c.IsCheckedChanged += cb => { try { set(cb.IsChecked); } catch (Exception e) { Plugin.LogThrottled("setting failed: " + e.Message); } };
        Controls.Add(c);
        y += RowH;
    }

    private void Slider(string text, float value, float min, float max, Action<float> set, Func<float, string> show)
    {
        Label(text, null);
        var sl = new MyGuiControlSlider(new Vector2(ControlX, y), min, max, SliderW, value, originAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
        sl.Value = value;
        var v = new MyGuiControlLabel(new Vector2(ControlX + SliderW + 0.012f, y), text: show(value), originAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
        sl.ValueChanged += x => { v.Text = show(x.Value); try { set(x.Value); } catch (Exception e) { Plugin.LogThrottled("setting failed: " + e.Message); } };
        Controls.Add(sl); Controls.Add(v);
        y += RowH;
    }

    private void Combo(string text, string tip, IList<string> items, int selected, Action<int> set)
    {
        Label(text, tip);
        var c = new MyGuiControlCombobox(new Vector2(ControlX, y), new Vector2(SliderW, 0.04f), toolTip: tip, originAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
        for (int i = 0; i < items.Count; i++) c.AddItem(i, items[i], sort: false);
        c.SelectItemByKey(selected, sendEvent: false);
        c.ItemSelected += () => { try { set((int)c.GetSelectedKey()); } catch (Exception e) { Plugin.LogThrottled("setting failed: " + e.Message); } };
        Controls.Add(c);
        y += RowH;
    }

    // ---- preview key choices: letters, then F1-F12

    private static List<string> keys;
    private static List<string> Keys()
    {
        if (keys != null) return keys;
        keys = new List<string>();
        for (char ch = 'A'; ch <= 'Z'; ch++) keys.Add(ch.ToString());
        for (int f = 1; f <= 12; f++) keys.Add("F" + f);
        return keys;
    }

    private static int KeyIndex(string name)
    {
        var k = Keys();
        string n = (name ?? "").Trim();
        if (n.Length == 0) return k.IndexOf("M");
        for (int i = 0; i < k.Count; i++) if (string.Equals(k[i], n, StringComparison.OrdinalIgnoreCase)) return i;
        k.Add(n); // a key set in the file that isn't in the list
        return k.Count - 1;
    }

    private static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
}
