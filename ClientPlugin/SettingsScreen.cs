using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Sandbox;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Input;
using VRage.Utils;
using VRageMath;

namespace SDX2_SystemMap;

/// <summary>
/// The settings window: Pulsar's Config button for the plugin, or Ctrl+Alt+/ in game (rebindable). Changes apply at
/// once and are saved when it closes. Two columns: the map window and pinned map on the left, keys, route and HUD on the
/// right. A key button waits for the next key with its Ctrl/Shift/Alt (Esc cancels, Backspace unbinds).
/// </summary>
internal sealed class SettingsScreen : MyGuiScreenBase
{
    private const float RowH = 0.046f, W = 0.98f, H = 0.86f, SliderW = 0.15f;
    private static SettingsScreen open;
    private float y, labelX, controlX;

    // key capture
    private sealed class Bind { public string Name, Tip; public Func<string> Get; public Action<string> Set; }
    private List<Bind> bindList;
    private Bind capturing;
    private MyGuiControlButton captureButton;
    private string note;
    private static MyKeys[] allKeys;

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

    /// <summary>The settings key (Ctrl+Alt+/ by default): every frame from Plugin.Update, also while paused and in any world.</summary>
    public static void CheckHotkey()
    {
        if (open != null || MyInput.Static == null || !Binds.Settings.Pressed()) return;
        if (MyAPIGateway.Gui?.ChatEntryVisible ?? false) return;
        Open();
    }

    private SettingsScreen() : base(new Vector2(0.5f, 0.5f), MyGuiConstants.SCREEN_BACKGROUND_COLOR, new Vector2(W, H), false, null,
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
        capturing = null; captureButton = null;
        var s = Plugin.Settings;
        var caption = AddCaption("SDX2 System Map settings");
        float top = caption.Position.Y + caption.Size.Y / 2 + 0.03f;

        // ---- left column
        Column(-W / 2 + 0.03f, -0.22f, top);
        Section("MAP WINDOW");
        Check("Only in a cockpit", "Show the map, the pinned map and the autopilot HUD lines only while you sit in a cockpit, seat or remote control. Its keys work only there too.",
            s.OnlyInCockpit, v => s.OnlyInCockpit = v);
        Check("Open with Flip and Burn's list", "Show the map window whenever the N list opens.", s.ShowMapWithNav, v => s.ShowMapWithNav = v);
        Check("Keep the list open on a pick", "Flip and Burn's N list stays open when you pick a destination.", s.KeepNavOpen, v => s.KeepNavOpen = v);
        Check("Window on the left", "Put the map window on the left of the N list instead of the right.", s.PreviewLeft, v => s.PreviewLeft = v);
        Slider("Window size", s.PreviewSize, 0.5f, 1.5f, v => s.PreviewSize = v, v => v.ToString("0.00", CultureInfo.InvariantCulture) + "x");
        var grids = new List<string>(); foreach (var g in MapSettings.GridChoicesKm) grids.Add(g.ToString("#,0", CultureInfo.InvariantCulture) + " km grid");
        Combo("Default zoom", "How far out the map and full map start when no route is picked: the size of a grid square round your ship (F in the full map frames it again).",
            grids, Math.Max(0, Array.IndexOf(MapSettings.GridChoicesKm, s.DefaultGridKm)), i =>
            {
                s.DefaultGridKm = MapSettings.GridChoicesKm[i]; MapScene.DefaultGridKm = s.DefaultGridKm;
                SidePreview.Reframe();
            });
        var themes = new List<string>(); foreach (var t in MapTheme.All) themes.Add(t.Name);
        Combo("Theme", "The look of the map, the preview and the autopilot panels (K in the full map).", themes, Math.Max(0, themes.IndexOf(MapTheme.Current.Name)),
            i => { MapTheme.Current = MapTheme.All[i]; s.Theme = MapTheme.Current.Name; });

        Section("PINNED MAP");
        Check("Pin the map on screen", "A small map stays in a screen corner while you fly.", s.Pip, v => SidePreview.SetPip(v));
        var corners = new List<string> { "TR", "BR", "BL", "TL" };
        var cornerNames = new List<string>(); foreach (var c in corners) cornerNames.Add(Cap(SidePreview.CornerName(c)));
        Combo("Corner", "Which corner it sits in (Q in the full map).", cornerNames, Math.Max(0, corners.IndexOf(s.PipCorner)), i => s.PipCorner = corners[i]);
        Slider("Size", s.PipSize, 0.5f, 2f, v => s.PipSize = v, v => v.ToString("0.00", CultureInfo.InvariantCulture) + "x");

        // ---- right column
        Column(0.02f, 0.25f, top);
        Section("KEYS");
        bindList = new List<Bind>
        {
            new Bind { Name = "Map", Tip = "Map window, then the full map, then off.", Get = () => s.KeyMap, Set = v => s.KeyMap = v },
            new Bind { Name = "Autopilot", Tip = "Open the autopilot route panel; while it flies, stop it (asks first).", Get = () => s.KeyAutopilot, Set = v => s.KeyAutopilot = v },
            new Bind { Name = "Pin the map", Tip = "Pin the small map on screen, or unpin it.", Get = () => s.KeyPin, Set = v => s.KeyPin = v },
            new Bind { Name = "Steer round asteroid", Tip = "While the autopilot flies: send the route with the suggested way round an asteroid.", Get = () => s.KeySteer, Set = v => s.KeySteer = v },
            new Bind { Name = "This window", Tip = "Opens these settings in game (Pulsar's Config button always works).", Get = () => s.KeySettings, Set = v => s.KeySettings = v },
        };
        foreach (var b in bindList) KeyRow(b);

        Section("ROUTE");
        Check("Detours round worlds", "Bend the route round planets and moons in the way (V in the full map).", s.AvoidBodies, v => { s.AvoidBodies = v; Replan(); });
        Slider("Clearance", (float)s.ClearanceKm, 10, 5000, v => { s.ClearanceKm = Math.Round(v / 10) * 10; Replan(); },
            v => (Math.Round(v / 10) * 10).ToString("0", CultureInfo.InvariantCulture) + " km");
        Check("Load plans into the autopilot", "Send each new route to the autopilot PB without flying it (ENGAGE still starts it).", s.AutopilotAutoSend, v => s.AutopilotAutoSend = v);

        Section("HUD");
        Check("Steady speed dial", "Show Flip and Burn's real speed on the HUD speed dial so it doesn't flicker to 150 m/s.", s.FixSpeedometer, v => s.FixSpeedometer = v);

        // ---- bottom
        float bottom = H / 2 - 0.035f;
        Controls.Add(new MyGuiControlButton(new Vector2(-0.1f, bottom), text: new StringBuilder("Defaults"),
            toolTip: "Put every setting in this window back to its default, keys included.", originAlign: MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_BOTTOM,
            onButtonClick: b => ResetDefaults()));
        Controls.Add(new MyGuiControlButton(new Vector2(0.1f, bottom), text: new StringBuilder("Close"),
            originAlign: MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_BOTTOM, onButtonClick: b => CloseScreen()));
        string foot = note ?? (Binds.Settings.IsNone ? "Changes apply at once." : Binds.Settings.Display + " opens this window in game. Changes apply at once.");
        note = null;
        Controls.Add(new MyGuiControlLabel(new Vector2(0, bottom - 0.065f), text: foot,
            textScale: 0.7f, originAlign: MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_BOTTOM) { ColorMask = new Vector4(0.7f, 0.7f, 0.7f, 1) });
    }

    private void ResetDefaults()
    {
        var s = Plugin.Settings; var d = new MapSettings();
        s.OnlyInCockpit = d.OnlyInCockpit; s.ShowMapWithNav = d.ShowMapWithNav; s.KeepNavOpen = d.KeepNavOpen; s.PreviewLeft = d.PreviewLeft;
        s.PreviewSize = d.PreviewSize; s.DefaultGridKm = d.DefaultGridKm; MapScene.DefaultGridKm = d.DefaultGridKm; s.Theme = d.Theme; MapTheme.Current = MapTheme.Find(d.Theme);
        s.PipCorner = d.PipCorner; s.PipSize = d.PipSize; SidePreview.SetPip(d.Pip);
        s.AvoidBodies = d.AvoidBodies; s.ClearanceKm = d.ClearanceKm; s.AutopilotAutoSend = d.AutopilotAutoSend; s.FixSpeedometer = d.FixSpeedometer;
        s.KeyMap = d.KeyMap; s.KeyAutopilot = d.KeyAutopilot; s.KeyPin = d.KeyPin; s.KeySteer = d.KeySteer; s.KeySettings = d.KeySettings;
        Replan();
        RecreateControls(false);
    }

    private static void Replan()
    {
        try { if (Sandbox.Game.World.MySession.Static != null) SidePreview.Replan(); else SidePreview.ApplySettings(Plugin.Settings); }
        catch (Exception e) { Plugin.LogThrottled("re-plan from settings failed: " + e.Message); }
    }

    // ---- key capture

    public override void HandleInput(bool receivedFocusInThisUpdate)
    {
        if (capturing == null) { base.HandleInput(receivedFocusInThisUpdate); return; }
        var input = MyInput.Static;
        if (input == null || receivedFocusInThisUpdate) return;
        try
        {
            if (input.IsNewKeyPressed(MyKeys.Escape)) { RecreateControls(false); return; } // cancel, and the window stays open
            if (input.IsNewKeyPressed(MyKeys.Back) || input.IsNewKeyPressed(MyKeys.Delete)) { Assign(capturing, KeyCombo.None); return; }
            allKeys ??= (MyKeys[])Enum.GetValues(typeof(MyKeys));
            foreach (var k in allKeys)
            {
                if (!Binds.Bindable(k) || !input.IsNewKeyPressed(k)) continue;
                Assign(capturing, new KeyCombo(k.ToString(), input.IsAnyCtrlKeyPressed(), input.IsAnyShiftKeyPressed(), input.IsAnyAltKeyPressed()));
                return;
            }
        }
        catch (Exception e) { Plugin.LogThrottled("key binding failed: " + e.Message); RecreateControls(false); }
    }

    /// <summary>Sets the binding; another action on the same keys is unbound, and the footer says so.</summary>
    private void Assign(Bind b, KeyCombo combo)
    {
        if (!combo.IsNone)
            foreach (var other in bindList)
                if (other != b && KeyCombo.Parse(other.Get()).Equals(combo))
                {
                    other.Set(KeyCombo.None.ToString());
                    note = combo.Display + " was on \"" + other.Name + "\": that one is unbound now.";
                }
        b.Set(combo.ToString());
        if (note == null && !combo.IsNone && !combo.Ctrl && !combo.Shift && !combo.Alt)
            note = combo.Display + " alone may also be one of the game's keys.";
        RecreateControls(false);
    }

    private void KeyRow(Bind b)
    {
        Label(b.Name, b.Tip);
        MyGuiControlButton btn = null;
        btn = new MyGuiControlButton(new Vector2(controlX, y), text: new StringBuilder(KeyCombo.Parse(b.Get()).Display), toolTip: b.Tip + " Click, then press the keys.",
            originAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER, onButtonClick: x =>
            {
                if (capturing != null) return;
                capturing = b; captureButton = btn;
                btn.Text = "Press keys... (Esc cancels, Backspace unbinds)";
            })
            { VisualStyle = MyGuiControlButtonStyleEnum.Rectangular, Size = new Vector2(SliderW + 0.06f, 0.038f) };
        Controls.Add(btn);
        y += RowH;
    }

    // ---- rows

    private void Column(float label, float control, float top) { labelX = label; controlX = control; y = top; }

    private void Section(string title)
    {
        y += 0.012f;
        Controls.Add(new MyGuiControlLabel(new Vector2(labelX, y), text: title, textScale: 0.75f, originAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER)
            { ColorMask = new Vector4(1f, 0.75f, 0.35f, 1) });
        var sep = new MyGuiControlSeparatorList();
        sep.AddHorizontal(new Vector2(labelX, y + 0.018f), controlX + SliderW + 0.07f - labelX);
        Controls.Add(sep);
        y += RowH * 0.8f;
    }

    private void Label(string text, string tip)
    {
        var l = new MyGuiControlLabel(new Vector2(labelX + 0.01f, y), text: text, textScale: 0.75f, originAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
        if (tip != null) l.SetToolTip(tip);
        Controls.Add(l);
    }

    private void Check(string text, string tip, bool value, Action<bool> set)
    {
        Label(text, tip);
        var c = new MyGuiControlCheckbox(new Vector2(controlX, y), toolTip: tip, isChecked: value, originAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
        c.IsCheckedChanged += cb => { try { set(cb.IsChecked); } catch (Exception e) { Plugin.LogThrottled("setting failed: " + e.Message); } };
        Controls.Add(c);
        y += RowH;
    }

    private void Slider(string text, float value, float min, float max, Action<float> set, Func<float, string> show)
    {
        Label(text, null);
        var sl = new MyGuiControlSlider(new Vector2(controlX, y), min, max, SliderW, value, originAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
        sl.Value = value;
        var v = new MyGuiControlLabel(new Vector2(controlX + SliderW + 0.01f, y), text: show(value), textScale: 0.75f, originAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
        sl.ValueChanged += x => { v.Text = show(x.Value); try { set(x.Value); } catch (Exception e) { Plugin.LogThrottled("setting failed: " + e.Message); } };
        Controls.Add(sl); Controls.Add(v);
        y += RowH;
    }

    private void Combo(string text, string tip, IList<string> items, int selected, Action<int> set)
    {
        Label(text, tip);
        var c = new MyGuiControlCombobox(new Vector2(controlX, y), new Vector2(SliderW + 0.06f, 0.04f), toolTip: tip, originAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
        for (int i = 0; i < items.Count; i++) c.AddItem(i, items[i], sort: false);
        c.SelectItemByKey(selected, sendEvent: false);
        c.ItemSelected += () => { try { set((int)c.GetSelectedKey()); } catch (Exception e) { Plugin.LogThrottled("setting failed: " + e.Message); } };
        Controls.Add(c);
        y += RowH;
    }

    private static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
}
