using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Sandbox;
using Sandbox.Graphics;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Input;
using VRage.Utils;
using VRageMath;

namespace SDX2_SystemMap;

/// <summary>
/// The settings window: Pulsar's Config button for the plugin, or Ctrl+Alt+/ in game (rebindable). Changes apply at
/// once and are saved when it closes. Tabs split it into pages: the map (window, pinned map, second screen), keys, route
/// and HUD, and the look (theme, its palette and colours, the nose marker). A key button waits for the next key with its Ctrl/Shift/Alt (Esc cancels, Backspace unbinds).
/// </summary>
internal sealed class SettingsScreen : MyGuiScreenBase
{
    private const float RowH = 0.041f, W = 0.98f, H = 0.97f, SliderW = 0.15f;
    /// <summary>The window's own background stays this opaque at least, whatever the game's UI background setting: the
    /// text reads over a bright planet too.</summary>
    private const float MinBack = 0.96f;
    private static readonly Vector4 Tint = new Vector4(0.02f, 0.03f, 0.05f, 0.82f);
    private int columnStart;
    private float columnTop, columnsBottom;
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

    /// <summary>The settings in use were swapped (another ship's): show them.</summary>
    public static void Refresh() { try { open?.RecreateControls(false); } catch (Exception e) { Plugin.LogThrottled("settings window refresh failed: " + e.Message); } }

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
        Math.Max(MinBack, MySandboxGame.Config.UIBkOpacity), Math.Max(MinBack, MySandboxGame.Config.UIOpacity))
    {
        EnabledBackgroundFade = true;
        CloseButtonEnabled = true;
        RecreateControls(true);
    }

    public override string GetFriendlyName() => "SDX2SystemMapSettings";

    /// <summary>The page shown (kept while the game runs): 0 map, 1 keys, 2 route and HUD, 3 look, 4 destination view.</summary>
    private static int page;
    private static readonly string[] Pages = { "Map", "Keys", "Route and HUD", "Look", "Destination view" };
    /// <summary>The colour being edited on the Look page (a <see cref="MapTheme.Slots"/> index).</summary>
    private static int colorSlot;
    private bool syncing;

    public override void RecreateControls(bool constructor)
    {
        base.RecreateControls(constructor);
        preview = null; iconPreview = null;
        capturing = null; captureButton = null;
        var s = Plugin.Settings;
        var caption = AddCaption("SDX2 System Map settings");
        float top = caption.Position.Y + caption.Size.Y / 2 + 0.02f;

        // ---- tabs
        float tabW = (W - 0.06f) / Pages.Length;
        for (int i = 0; i < Pages.Length; i++)
        {
            int p = i;
            var tab = new MyGuiControlButton(new Vector2(-W / 2 + 0.03f + tabW * i + tabW / 2, top), text: new StringBuilder(Pages[i]),
                originAlign: MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP, onButtonClick: b => { page = p; RecreateControls(false); })
                { VisualStyle = MyGuiControlButtonStyleEnum.Rectangular, Size = new Vector2(tabW - 0.006f, 0.04f), Checked = i == page };
            Controls.Add(tab);
            if (i == page)
                Controls.Add(new MyGuiControlPanel(new Vector2(tab.Position.X, top + 0.04f), new Vector2(tabW - 0.006f, 0.005f), new Vector4(1f, 0.75f, 0.35f, 1),
                    MyGuiConstants.BLANK_TEXTURE, originAlign: MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP));
        }
        top += 0.065f;
        columnsBottom = top;

        var secondBind = new Bind { Name = "Second screen", Tip = "Open or close the map on the second screen.", Get = () => s.KeySecondScreen, Set = v => s.KeySecondScreen = v };
        bindList = new List<Bind>
        {
            new Bind { Name = "Map", Tip = "Map window, then the full map, then off.", Get = () => s.KeyMap, Set = v => s.KeyMap = v },
            new Bind { Name = "Route plan", Tip = "Open the N list with the Route Plan and the map preview. While flying: change the route (APPLY sends, CANCEL drops).", Get = () => s.KeyAutopilot, Set = v => s.KeyAutopilot = v },
            new Bind { Name = "Pin the map", Tip = "Pin the small map on screen, or unpin it.", Get = () => s.KeyPin, Set = v => s.KeyPin = v },
            new Bind { Name = "Steer round asteroid", Tip = "While the autopilot flies: steer round an asteroid on the route.", Get = () => s.KeySteer, Set = v => s.KeySteer = v },
            new Bind { Name = "Free the mouse (hold)", Tip = "Hold to use the mouse on the map without turning the ship (needs Rich HUD Master).", Get = () => s.KeyCursor, Set = v => s.KeyCursor = v },
            new Bind { Name = "Map panel side", Tip = "Full map: move the route panel to the other side.", Get = () => s.KeyPanel, Set = v => s.KeyPanel = v },
            new Bind { Name = "This window", Tip = "Open these settings in game (Pulsar's Config button also works).", Get = () => s.KeySettings, Set = v => s.KeySettings = v },
            secondBind,
        };

        float leftL = -W / 2 + 0.03f, leftC = -0.22f, rightL = 0.02f, rightC = 0.25f;
        switch (page)
        {
            case 0: MapPage(s, top, leftL, leftC, rightL, rightC); break;
            case 1:
                Column(leftL, leftC, top);
                Section("KEYS");
                foreach (var b in bindList) KeyRow(b);
                EndColumn();
                break;
            case 2: RoutePage(s, top, leftL, leftC, rightL, rightC); break;
            case 4: DestPage(s, top, leftL, leftC, rightL, rightC); break;
            default: LookPage(s, top, leftL, leftC, rightL, rightC); break;
        }

        // ---- bottom
        float bottom = H / 2 - 0.025f;
        Controls.Add(new MyGuiControlButton(new Vector2(-0.1f, bottom), text: new StringBuilder("Defaults"),
            toolTip: "Reset every setting here, keys and colours included.", originAlign: MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_BOTTOM,
            onButtonClick: b => ResetDefaults()));
        Controls.Add(new MyGuiControlButton(new Vector2(0.1f, bottom), text: new StringBuilder("Close"),
            originAlign: MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_BOTTOM, onButtonClick: b => CloseScreen()));
        // whose settings these are
        float keepX = -W / 2 + 0.03f, keepY = bottom - 0.03f;
        const string keepTip = "Each ship: every ship you sit in keeps its own settings, keys, colours and icon, saved under its name (a ship seen for the first time starts from the set for all ships).\n"
            + "All ships: one set everywhere. Switching to it makes the settings shown here that set; each ship's own are kept on disk and come back when you switch again.";
        var keepLabel = new MyGuiControlLabel(new Vector2(keepX, keepY), text: "Settings for", textScale: 0.75f, originAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
        keepLabel.SetToolTip(keepTip);
        Controls.Add(keepLabel);
        var keep = new MyGuiControlCombobox(new Vector2(keepX + 0.085f, keepY), new Vector2(0.17f, 0.04f), toolTip: keepTip, originAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
        keep.AddItem(0, "Each ship", sort: false); keep.AddItem(1, "All ships", sort: false);
        keep.SelectItemByKey(Plugin.PerShip ? 0 : 1, sendEvent: false);
        keep.ItemSelected += () =>
        {
            try { Plugin.SetPerShip(keep.GetSelectedKey() == 0); RecreateControls(false); }
            catch (Exception e) { Plugin.LogThrottled("setting failed: " + e.Message); }
        };
        Controls.Add(keep);
        string whose = !Plugin.PerShip ? "These settings are for all ships."
            : Plugin.SettingsShip != null ? "These settings are for " + Plugin.SettingsShip + "." : "These settings are for all ships (sit in a ship for its own).";
        string foot = note ?? ((Binds.Settings.IsNone ? "Changes apply at once." : Binds.Settings.Display + " opens this window in game. Changes apply at once.") + " " + whose);
        note = null;
        Controls.Add(new MyGuiControlLabel(new Vector2(0, bottom - 0.052f), text: foot,
            textScale: 0.7f, originAlign: MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_BOTTOM) { ColorMask = new Vector4(0.7f, 0.7f, 0.7f, 1) });
    }

    private void MapPage(MapSettings s, float top, float leftL, float leftC, float rightL, float rightC)
    {
        Column(leftL, leftC, top);
        Section("MAP WINDOW");
        Check("Only in a cockpit", "Show the map, pinned map and autopilot HUD only while seated (cockpit, seat or remote control). The keys work only there too.",
            s.OnlyInCockpit, v => s.OnlyInCockpit = v);
        Check("Open with the N list", "Show the map window whenever the N list opens.", s.ShowMapWithNav, v => s.ShowMapWithNav = v);
        Check("Follow turns with the ship", "While following your ship, the map turns so your travel points up (U in the full map).",
            s.FollowHeading, v => s.FollowHeading = v);
        Check("Keep the N list open on a pick", "The N list (Flip and Burn's) stays open when you pick a destination.", s.KeepNavOpen, v => s.KeepNavOpen = v);
        Check("Window on the left", "Put the map window on the left of the N list instead of the right.", s.PreviewLeft, v => s.PreviewLeft = v);
        Slider("Window size", s.PreviewSize, 0.5f, 1.5f, v => s.PreviewSize = v, v => v.ToString("0.00", CultureInfo.InvariantCulture) + "x");
        var grids = new List<string>(); foreach (var g in MapSettings.GridChoicesKm) grids.Add(g.ToString("#,0", CultureInfo.InvariantCulture) + " km grid");
        Combo("Default zoom", "How far out the map starts with no route: the grid square size round your ship (F in the full map re-frames).",
            grids, Math.Max(0, Array.IndexOf(MapSettings.GridChoicesKm, s.DefaultGridKm)), i =>
            {
                s.DefaultGridKm = MapSettings.GridChoicesKm[i]; MapScene.DefaultGridKm = s.DefaultGridKm;
                SidePreview.Reframe();
            });
        EndColumn();

        Column(rightL, rightC, top);
        Section("PINNED MAP");
        Check("Pin the map on screen", "A small map stays in a screen corner while you fly.", s.Pip, v => SidePreview.SetPip(v));
        var corners = new List<string> { "TR", "BR", "BL", "TL" };
        var cornerNames = new List<string>(); foreach (var c in corners) cornerNames.Add(Cap(SidePreview.CornerName(c)));
        Combo("Corner", "Which corner it sits in (Q in the full map).", cornerNames, Math.Max(0, corners.IndexOf(s.PipCorner)), i => s.PipCorner = corners[i]);
        Slider("Size", s.PipSize, 0.5f, 2f, v => s.PipSize = v, v => v.ToString("0.00", CultureInfo.InvariantCulture) + "x");

        Section("SECOND SCREEN");
        Check("Map on a second screen", "The full map in its own window on another monitor, with touch (drag, pinch, tap; hold to set the destination). It never takes focus from the game. Opens with each world.",
            s.SecondScreen, v => SecondScreen.Set(v));
        Combo("Screen", "Which monitor it fills (a normal window on the main one). Turn it off and on to move it.", SecondScreen.ScreenNames(), s.SecondScreenMonitor,
            i => s.SecondScreenMonitor = i);
        EndColumn();
    }

    private void RoutePage(MapSettings s, float top, float leftL, float leftC, float rightL, float rightC)
    {
        Column(leftL, leftC, top);
        Section("ROUTE");
        Check("Detours round worlds", "Bend the route round planets and moons in the way (V in the full map).", s.AvoidBodies, v => { s.AvoidBodies = v; Replan(); });
        Check("Keep out of asteroid fields", "Also go over or under the Belt and round the Trojans and Greeks, unless the route starts or ends in them.", s.AvoidFields, v => { s.AvoidFields = v; Replan(); });
        Slider("Offset", (float)s.OffsetKm, 0.5f, 20, v => { s.OffsetKm = Math.Round(v * 2) / 2; RouteExport.Offset = s.OffsetKm * 1000; Replan(); },
            v => (Math.Round(v * 2) / 2).ToString("0.0", CultureInfo.InvariantCulture) + " km",
            "How far short of the destination the autopilot stops. Over a base on a planet or moon: at least 3 km up, higher if your thrust can't hold lower.");
        Check("Load plans into the autopilot", "Send each new route to the autopilot without flying it (ENGAGE starts it).", s.AutopilotAutoSend, v => s.AutopilotAutoSend = v);
        Section("CONTROLS");
        Check("Steady speed dial", "Show Flip and Burn's real speed on the HUD speed dial so it doesn't flicker to 150 m/s.", s.FixSpeedometer, v => s.FixSpeedometer = v);
        Check("Steady controller", "Stop icons and menus flickering between controller and keyboard/mouse when a driver or hotkey tool sends phantom keys.", s.SteadyController, v => s.SteadyController = v);
        EndColumn();

        Column(rightL, rightC, top);
        Section("HUD");
        Check("Burn guide", "Flying a route by hand: nose markers (BURN, BRAKE, CORRECT) with the thrust to use, a countdown to the next step and where the ship will stop.", s.BurnGuide, v => s.BurnGuide = v);
        Check("Nose marker for the crosshair", "While seated: replace the game's crosshair with the nose marker (styled on the Look page).", s.HideCrosshair, v => s.HideCrosshair = v);
        Slider("Autopilot line across", s.HudX, 0f, 1f, v => s.HudX = v, v => (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%");
        Slider("Autopilot line down", s.HudY, 0f, 0.95f, v => s.HudY = v, v => (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%");
        Slider("Autopilot line size", s.HudSize, 0.6f, 1.8f, v => s.HudSize = v, v => v.ToString("0.00", CultureInfo.InvariantCulture) + "x");
        EndColumn();
    }

    private void LookPage(MapSettings s, float top, float leftL, float leftC, float rightL, float rightC)
    {
        var theme = MapTheme.Current.Base;
        Column(leftL, leftC, top);
        Section("THEME");
        var themes = new List<string>(); foreach (var t in MapTheme.All) themes.Add(t.Name);
        Combo("Theme", "The look of the map, panels, HUD markers and destination view (K in the full map). The palette and colours below belong to this theme.",
            themes, Math.Max(0, themes.IndexOf(theme.Name)), i => { s.Theme = MapTheme.All[i].Name; s.ApplyLook(); RecreateControls(false); });
        var pals = new List<string> { "Its own (" + theme.Name + ")" };
        foreach (var t in MapTheme.All) if (t != theme) pals.Add(t.Name);
        s.Palettes.TryGetValue(theme.Name, out var pal);
        Combo("Palette", "Which theme's colours " + theme.Name + " uses (its shapes stay). Each theme keeps its own choice.", pals, pal == null ? 0 : Math.Max(0, pals.IndexOf(pal)), i =>
        {
            if (i == 0) s.Palettes.Remove(theme.Name); else s.Palettes[theme.Name] = pals[i];
            s.ApplyLook(); RecreateControls(false);
        });
        var noses = new List<string> { "The theme's own" }; foreach (var t in MapTheme.All) noses.Add(t.Name);
        int nose = s.NoseOverride == null ? 0 : 1 + Math.Max(0, Array.FindIndex(MapTheme.All, t => t.Name == MapTheme.Find(s.NoseStyle).Name));
        Combo("Nose marker", "The marker for where the ship points: the theme's own, or another theme's.",
            noses, nose, i => { s.NoseStyle = i == 0 ? "Theme" : MapTheme.All[i - 1].Name; s.ApplyLook(); });
        Combo("Drawing quality", "Ultra: smooth-edged lines and circles, shapes set on whole pixels, rounder curves, and a destination view at up to 1080 lines and 30 frames a second. Costs a little more to draw.",
            new List<string> { "Standard", "Ultra" }, s.Ultra ? 1 : 0, i => { s.Ultra = i == 1; SidePreview.ApplyQuality(s.Ultra); });

        // every colour of the theme, as swatches (hover for the name); the one being edited is outlined
        Section("PALETTE");
        float sx = labelX + 0.01f, sz = 0.026f, gap = 0.006f;
        int perRow = 12;
        for (int i = 0; i < MapTheme.Slots.Length; i++)
        {
            var (key, label) = MapTheme.Slots[i];
            float x = sx + (i % perRow) * (sz + gap), yy = y - RowH * 0.3f + (i / perRow) * (sz + gap);
            if (i == colorSlot)
                Controls.Add(new MyGuiControlPanel(new Vector2(x - 0.003f, yy - 0.004f), new Vector2(sz + 0.006f, sz + 0.008f), new Vector4(1f, 0.75f, 0.35f, 1),
                    MyGuiConstants.BLANK_TEXTURE, originAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP));
            var col = MapTheme.Current.Get(key);
            Controls.Add(new MyGuiControlPanel(new Vector2(x, yy), new Vector2(sz, sz), V4(col), MyGuiConstants.BLANK_TEXTURE, label + " " + MapTheme.Hex(col),
                originAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP));
        }
        y += ((MapTheme.Slots.Length + perRow - 1) / perRow) * (sz + gap) + RowH * 0.3f;

        Section("SHIP ICON");
        Combo("Ship icon", "Your ship on the map: a drawn icon for its Ship Core class, or one made from the blocks of the ship you are flying.",
            new List<string> { "Drawn", "From Current Ship" }, s.ShipIconFromShip ? 1 : 0,
            i => { s.ShipIconFromShip = i == 1; MapScene.ShipIconFromShip = s.ShipIconFromShip; RecreateControls(false); });
        if (s.ShipIconFromShip)
        {
            const string sideTip = "Which side of the ship the icon shows. Click to flip to the next: top, right side, bottom, left side.";
            Label("Side shown", sideTip);
            bool auto = s.ShipIconSide < 0 || s.ShipIconSide > 3;
            string side = MapScene.OwnShip == null ? (auto ? "Auto" : ShipMask.SideName(s.ShipIconSide)) : ShipMask.SideName(OwnShipIcon.SideUsed) + (auto ? " (auto)" : "");
            Controls.Add(new MyGuiControlButton(new Vector2(controlX, y), text: new StringBuilder("Flip: " + side), toolTip: sideTip,
                originAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER, onButtonClick: b =>
                {
                    s.ShipIconSide = ((MapScene.OwnShip != null ? OwnShipIcon.SideUsed : auto ? -1 : s.ShipIconSide) + 1) % 4;
                    OwnShipIcon.Refresh();
                    RecreateControls(false);
                })
                { VisualStyle = MyGuiControlButtonStyleEnum.Rectangular, Size = new Vector2(SliderW + 0.06f, 0.036f) });
            y += RowH;
        }
        float iconW = controlX + SliderW + 0.06f - labelX - 0.01f, iconH = Math.Max(0.1f, Math.Min(0.24f, H / 2 - 0.12f - y));
        iconPreview = new Vector4(labelX + 0.01f, y - RowH * 0.3f, iconW, iconH);
        y += iconH;
        EndColumn();

        // the colour being edited
        Column(rightL, rightC, top);
        colorSlot = ColorEditor(s, theme, MapTheme.Slots, colorSlot, i => colorSlot = i, "COLOURS: " + theme.Name.ToUpperInvariant(), "Reset " + theme.Name,
            "Reset " + theme.Name + "'s colours and palette.",
            () => { s.Colors.Remove(theme.Name); s.Palettes.Remove(theme.Name); });
        EndColumn();
    }

    /// <summary>
    /// A colour picker for one of <paramref name="slots"/> of <paramref name="theme"/>: which colour, its hex and red, green
    /// and blue, and buttons to reset it or (<paramref name="resetAll"/>) the lot. Returns the slot shown (clamped).
    /// </summary>
    private int ColorEditor(MapSettings s, MapTheme theme, (string Key, string Label)[] slots, int slot, Action<int> pick, string title,
        string resetAllText, string resetAllTip, Action resetAll)
    {
        var colors = s.ColorsOf(theme.Name);
        slot = Math.Max(0, Math.Min(slots.Length - 1, slot));
        var (slotKey, slotLabel) = slots[slot];
        Section(title);
        var names = new List<string>(); foreach (var (k, l) in slots) names.Add(l + (colors.ContainsKey(k) ? " *" : ""));
        Combo("Colour", "The colour to set for " + theme.Name + " (* = changed from the palette).", names, slot, i => { pick(i); RecreateControls(false); });
        var cur = MapTheme.Current.Get(slotKey);
        Label("Hex", "Type a colour as #RRGGBB.");
        var swatch = new MyGuiControlPanel(new Vector2(controlX, y), new Vector2(0.03f, 0.03f), V4(cur), MyGuiConstants.BLANK_TEXTURE,
            originAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
        Controls.Add(swatch);
        var hex = new MyGuiControlTextbox(new Vector2(controlX + 0.04f, y), MapTheme.Hex(cur), 7) { OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER, Size = new Vector2(SliderW + 0.02f, 0.04f) };
        Controls.Add(hex);
        y += RowH;
        MyGuiControlSlider red = null, green = null, blue = null;
        void SetColor(int rgb, bool fromHex)
        {
            if (syncing) return;
            syncing = true;
            try
            {
                colors[slotKey] = rgb;
                s.ApplyLook();
                var c = MapTheme.Current.Get(slotKey);
                swatch.ColorMask = V4(c);
                if (!fromHex) hex.Text = MapTheme.Hex(c);
                else { red.Value = c.R; green.Value = c.G; blue.Value = c.B; }
            }
            catch (Exception e) { Plugin.LogThrottled("colour failed: " + e.Message); }
            finally { syncing = false; }
        }
        int Rgb() => ((int)Math.Round(red.Value) << 16) | ((int)Math.Round(green.Value) << 8) | (int)Math.Round(blue.Value);
        red = Slider("Red", cur.R, 0, 255, v => SetColor(Rgb(), false), v => v.ToString("0", CultureInfo.InvariantCulture));
        green = Slider("Green", cur.G, 0, 255, v => SetColor(Rgb(), false), v => v.ToString("0", CultureInfo.InvariantCulture));
        blue = Slider("Blue", cur.B, 0, 255, v => SetColor(Rgb(), false), v => v.ToString("0", CultureInfo.InvariantCulture));
        hex.TextChanged += tb => { if (!syncing && MapTheme.ParseHex(tb.Text, out int rgb)) SetColor(rgb, true); };
        y += 0.01f;
        Controls.Add(new MyGuiControlButton(new Vector2(labelX + 0.01f, y), text: new StringBuilder("Reset colour"),
            toolTip: "Reset " + slotLabel + " to the palette's colour.", originAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER,
            onButtonClick: b => { colors.Remove(slotKey); s.ApplyLook(); RecreateControls(false); }));
        Controls.Add(new MyGuiControlButton(new Vector2(controlX, y), text: new StringBuilder(resetAllText),
            toolTip: resetAllTip, originAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER,
            onButtonClick: b => { resetAll(); s.ApplyLook(); RecreateControls(false); }));
        y += RowH * 1.3f;
        return slot;
    }

    // ---- destination view page

    /// <summary>The overlay colour being edited (an <see cref="MapTheme.OverlaySlots"/> index).</summary>
    private static int overlaySlot;
    /// <summary>Where the preview goes (screen-local normalized: left, top, width); null off its page.</summary>
    private Vector3? preview;
    private readonly GameCanvas previewCanvas = new GameCanvas();

    private void DestPage(MapSettings s, float top, float leftL, float leftC, float rightL, float rightC)
    {
        var theme = MapTheme.Current.Base;
        var corners = new List<string> { "TR", "BR", "BL", "TL" };
        var cornerNames = new List<string>(); foreach (var c in corners) cornerNames.Add(Cap(SidePreview.CornerName(c)));
        Column(leftL, leftC, top);
        Section("DESTINATION VIEW");
        Check("Show the destination", "Near the destination (within the range below), a live view of it from your hull appears. It shows only what your game has loaded, never through blocks, and zooms no further than a camera block.",
            s.DestView, v => s.DestView = v);
        Slider("Range", (float)s.DestViewRangeKm, (float)DestView.MinRangeKm, (float)DestView.MaxRangeKm, v => s.DestViewRangeKm = DestView.ClampRangeKm(Math.Round(v * 2) / 2),
            v => (Math.Round(v * 2) / 2).ToString("0.#", CultureInfo.InvariantCulture) + " km");
        Check("In the map's place", "The view takes the map window while it is up; the map returns after. Not while the N list is open.",
            s.DestViewReplacesMap, v => s.DestViewReplacesMap = v);
        Combo("Corner", "Where it sits otherwise (the other side if the pinned map has that corner).", cornerNames, Math.Max(0, corners.IndexOf(s.DestViewCorner)), i => s.DestViewCorner = corners[i]);
        Slider("Size", s.DestViewSize, 0.5f, 2f, v => s.DestViewSize = v, v => v.ToString("0.00", CultureInfo.InvariantCulture) + "x");


        Section("OVERLAY");
        var themes = new List<string>(); foreach (var t in MapTheme.All) themes.Add(t.Name);
        Combo("Theme", "The theme in use (same as the Look page). Each theme keeps its own overlay and colours.",
            themes, Math.Max(0, themes.IndexOf(theme.Name)), i => { s.Theme = MapTheme.All[i].Name; s.ApplyLook(); RecreateControls(false); });
        var styles = new List<string>(); foreach (var o in DestOverlay.Styles) styles.Add(o.Name + (o.Style == DestOverlay.Default(theme.Style) ? " (its own)" : ""));
        var mine = s.OverlayOf(theme);
        Combo("Overlay", "The marks over " + theme.Name + "'s destination view: its own style, another theme's, or none.", styles,
            Math.Max(0, Array.FindIndex(DestOverlay.Styles, o => o.Style == mine)), i =>
            {
                var o = DestOverlay.Styles[i].Style;
                if (o == DestOverlay.Default(theme.Style)) s.Overlays.Remove(theme.Name); else s.Overlays[theme.Name] = o;
            });
        Check("Screen noise", "Faint grids, scan lines and static on the overlays that have them.", s.FeedNoise, v => s.FeedNoise = v);
        EndColumn();

        Column(rightL, rightC, top);
        Section("PREVIEW: " + theme.Name.ToUpperInvariant());
        float pw = 0.36f, x0 = labelX + 0.01f;
        var unit = MyGuiManager.GetScreenSizeFromNormalizedSize(new Vector2(1, 1));
        float ph = pw * 9f / 16f * (unit.X / Math.Max(1f, unit.Y));
        preview = new Vector3(x0, y - RowH * 0.3f, pw);
        y += ph + RowH * 0.2f;
        overlaySlot = ColorEditor(s, theme, MapTheme.OverlaySlots, overlaySlot, i => overlaySlot = i, "OVERLAY COLOURS: " + theme.Name.ToUpperInvariant(),
            "Reset overlay", "Reset " + theme.Name + "'s overlay and its colours.",
            () => { s.Overlays.Remove(theme.Name); var cols = s.ColorsOf(theme.Name); foreach (var (k, _) in MapTheme.OverlaySlots) cols.Remove(k); });
        EndColumn();
    }

    /// <summary>Where the ship icon's picture goes on the Look page (screen-local normalized: left, top, width, height); null off it.</summary>
    private Vector4? iconPreview;
    private readonly MapScene iconScene = new MapScene();

    /// <summary>The window, then the destination view preview over it (its page only): the stand-in picture with the theme's frame and overlay.</summary>
    public override bool Draw()
    {
        if (!base.Draw()) return false;
        if (preview is Vector3 pv)
        {
            try
            {
                var tl = MyGuiManager.GetScreenCoordinateFromNormalizedCoordinate(GetPosition() + new Vector2(pv.X, pv.Y));
                var sz = MyGuiManager.GetScreenSizeFromNormalizedSize(new Vector2(pv.Z, 0));
                var box = new Box(tl.X, tl.Y, sz.X, sz.X * 9f / 16f);
                var s = Plugin.Settings;
                DestView.DrawWindow(previewCanvas, box, MapTheme.Current, "Pallas Station", 4820, s.OverlayOf(MapTheme.Current), s.FeedNoise, Plugin.Now,
                    true, null, true, standIn: true);
            }
            catch (Exception e) { preview = null; Plugin.LogThrottled("destination view preview failed: " + e.Message); }
        }
        if (iconPreview is Vector4 iv)
        {
            try
            {
                var tl = MyGuiManager.GetScreenCoordinateFromNormalizedCoordinate(GetPosition() + new Vector2(iv.X, iv.Y));
                var sz = MyGuiManager.GetScreenSizeFromNormalizedSize(new Vector2(iv.Z, iv.W));
                bool own = Plugin.Settings.ShipIconFromShip;
                string cls = SidePreview.Snap.HasShip ? SidePreview.Snap.ShipClass : "";
                var shape = (own ? MapScene.OwnShip : null) ?? ShipIcons.For(cls);
                string caption = shape == null ? null : shape.Runs != null ? shape.From + ": " + ShipMask.SideName(OwnShipIcon.SideUsed).ToLowerInvariant() : shape.Class + " (drawn)";
                iconScene.ShipPreview(previewCanvas, shape, tl.X, tl.Y, sz.X, sz.Y, caption, "Sit in a ship to see its icon");
            }
            catch (Exception e) { iconPreview = null; Plugin.LogThrottled("ship icon preview failed: " + e.Message); }
        }
        return true;
    }

    private static Vector4 V4(MapColor c) => new Vector4(c.R / 255f, c.G / 255f, c.B / 255f, 1);

    private void ResetDefaults()
    {
        var s = Plugin.Settings; var d = new MapSettings();
        s.OnlyInCockpit = d.OnlyInCockpit; s.ShowMapWithNav = d.ShowMapWithNav; s.FollowHeading = d.FollowHeading; s.KeepNavOpen = d.KeepNavOpen; s.PreviewLeft = d.PreviewLeft;
        s.PreviewSize = d.PreviewSize; s.DefaultGridKm = d.DefaultGridKm; MapScene.DefaultGridKm = d.DefaultGridKm; s.Theme = d.Theme; s.NoseStyle = d.NoseStyle; s.Palettes.Clear(); s.Colors.Clear(); s.ApplyLook();
        s.ShipIconFromShip = d.ShipIconFromShip; MapScene.ShipIconFromShip = d.ShipIconFromShip; s.ShipIconSide = d.ShipIconSide; OwnShipIcon.Refresh(); s.Ultra = d.Ultra; SidePreview.ApplyQuality(d.Ultra);
        s.PipCorner = d.PipCorner; s.PipSize = d.PipSize; SidePreview.SetPip(d.Pip);
        s.DestView = d.DestView; s.DestViewCorner = d.DestViewCorner; s.DestViewSize = d.DestViewSize; s.DestViewRangeKm = d.DestViewRangeKm; s.DestViewReplacesMap = d.DestViewReplacesMap; s.FeedNoise = d.FeedNoise; s.Overlays.Clear();
        s.AvoidBodies = d.AvoidBodies; s.AvoidFields = d.AvoidFields; s.OffsetKm = d.OffsetKm; RouteExport.Offset = d.OffsetKm * 1000; s.AutopilotAutoSend = d.AutopilotAutoSend; s.FixSpeedometer = d.FixSpeedometer; s.BurnGuide = d.BurnGuide; s.HideCrosshair = d.HideCrosshair; s.SteadyController = d.SteadyController;
        s.HudX = d.HudX; s.HudY = d.HudY; s.HudSize = d.HudSize;
        s.KeyMap = d.KeyMap; s.KeyAutopilot = d.KeyAutopilot; s.KeyPin = d.KeyPin; s.KeySteer = d.KeySteer; s.KeySettings = d.KeySettings; s.KeyCursor = d.KeyCursor; s.KeyPanel = d.KeyPanel; s.KeySecondScreen = d.KeySecondScreen; s.SecondScreenMonitor = d.SecondScreenMonitor; SecondScreen.Set(d.SecondScreen); s.PanelRight = d.PanelRight;
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
            if (input.IsNewKeyPressed(MyKeys.Escape) || GamePad.BackPressed) { RecreateControls(false); return; } // cancel, and the window stays open
            if (input.IsNewKeyPressed(MyKeys.Back) || input.IsNewKeyPressed(MyKeys.Delete)) { Assign(capturing, KeyCombo.None); return; }
            allKeys ??= (MyKeys[])Enum.GetValues(typeof(MyKeys));
            foreach (var k in allKeys)
            {
                if (!Binds.Bindable(k) || !Binds.IsNew(input, k)) continue;
                Assign(capturing, new KeyCombo(k.ToString(), input.IsAnyCtrlKeyPressed(), input.IsAnyShiftKeyPressed(), input.IsAnyAltKeyPressed()));
                return;
            }
        }
        catch (Exception e) { Plugin.LogThrottled("key binding failed: " + e.Message); RecreateControls(false); }
    }

    /// <summary>
    /// A new combo: if it's also one of the game's keys with the same modifiers, a pop-up asks first. Then
    /// <see cref="Apply"/>.
    /// </summary>
    private void Assign(Bind b, KeyCombo combo)
    {
        bool exact = false;
        string game = combo.IsNone ? null : Binds.GameConflict(combo, out exact);
        if (game != null && exact)
        {
            capturing = null; // stop listening while the pop-up is up
            MyGuiSandbox.AddScreen(MyGuiSandbox.CreateMessageBox(MyMessageBoxStyleEnum.Info, MyMessageBoxButtonsType.YES_NO,
                new StringBuilder(combo.Display + " is already the game's \"" + game + "\" key, so both would happen when you press it."
                    + "\n\nBind \"" + b.Name + "\" to " + combo.Display + " anyway?"),
                new StringBuilder("Key already in use"),
                callback: r => { if (r == MyGuiScreenMessageBox.ResultEnum.YES) Apply(b, combo); else RecreateControls(false); },
                focusedResult: MyGuiScreenMessageBox.ResultEnum.NO));
            return;
        }
        if (game != null) note = "Note: the game's \"" + game + "\" key (" + KeyCombo.KeyName(combo.Key) + ") also fires on " + combo.Display + ".";
        Apply(b, combo);
    }

    /// <summary>Sets the binding; another of this plugin's actions on the same keys is unbound, and the footer says so.</summary>
    private void Apply(Bind b, KeyCombo combo)
    {
        if (!combo.IsNone)
            foreach (var other in bindList)
                if (other != b && KeyCombo.Parse(other.Get()).Equals(combo))
                {
                    other.Set(KeyCombo.None.ToString());
                    note = combo.Display + " was on \"" + other.Name + "\": that one is unbound now.";
                }
        b.Set(combo.ToString());
        RecreateControls(false);
    }

    private void KeyRow(Bind b)
    {
        Label(b.Name, b.Tip);
        MyGuiControlButton btn = null;
        var combo = KeyCombo.Parse(b.Get());
        string game = Binds.GameConflict(combo, out bool exact);
        string tip = b.Tip + " Click, then press the keys (a side mouse button works too).";
        if (game != null) tip += exact ? "\nAlso the game's \"" + game + "\" key: both happen." : "\nThe game's \"" + game + "\" key (" + KeyCombo.KeyName(combo.Key) + ") also fires on this.";
        btn = new MyGuiControlButton(new Vector2(controlX, y), text: new StringBuilder(combo.Display + (exact ? "  (!)" : "")), toolTip: tip,
            originAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER, onButtonClick: x =>
            {
                if (capturing != null) return;
                capturing = b; captureButton = btn;
                btn.Text = "Press keys or a side button... (Esc cancels, Backspace unbinds)";
            })
            { VisualStyle = MyGuiControlButtonStyleEnum.Rectangular, Size = new Vector2(SliderW + 0.06f, 0.038f) };
        Controls.Add(btn);
        y += RowH;
    }

    // ---- rows

    private void Column(float label, float control, float top) { labelX = label; controlX = control; y = top; columnTop = top; columnStart = Controls.Count; }

    /// <summary>A dark panel behind the column just laid out (under its controls), so its text reads on a bright screen.</summary>
    private void EndColumn()
    {
        float x0 = labelX - 0.012f, x1 = controlX + SliderW + 0.085f, y0 = columnTop - 0.012f, y1 = y - RowH * 0.35f;
        Controls.Add(new MyGuiControlPanel(new Vector2(x0, y0), new Vector2(x1 - x0, y1 - y0), Tint, MyGuiConstants.BLANK_TEXTURE,
            originAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP), columnStart);
        columnsBottom = Math.Max(columnsBottom, y1);
    }

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

    private MyGuiControlSlider Slider(string text, float value, float min, float max, Action<float> set, Func<float, string> show, string tip = null)
    {
        Label(text, tip);
        var sl = new MyGuiControlSlider(new Vector2(controlX, y), min, max, SliderW, value, toolTip: tip, originAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
        sl.Value = value;
        var v = new MyGuiControlLabel(new Vector2(controlX + SliderW + 0.01f, y), text: show(value), textScale: 0.75f, originAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
        sl.ValueChanged += x => { v.Text = show(x.Value); try { set(x.Value); } catch (Exception e) { Plugin.LogThrottled("setting failed: " + e.Message); } };
        Controls.Add(sl); Controls.Add(v);
        y += RowH;
        return sl;
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
