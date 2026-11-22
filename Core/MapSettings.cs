using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace SDX2_SystemMap;

/// <summary>Plain key=value settings file (Storage\SDX2_SystemMap.ini). Game-free so it can be tested.</summary>
public sealed class MapSettings
{
    /// <summary>Key bindings (<see cref="KeyCombo"/>; "None" unbinds). The map key cycles map window → full map → off.
    /// M alone is left to the game (its symmetry key).</summary>
    public string KeyMap = "Shift+M", KeyAutopilot = "Ctrl+M", KeyPin = "Alt+M", KeySteer = "Ctrl+Shift+M", KeySettings = "Ctrl+Alt+OemQuestion", KeyCursor = "F2", KeyPanel = "B", KeySecondScreen = "Ctrl+Alt+M";
    /// <summary>The full map's route panel stands on the right (mirrored); the route editor then opens on the left.</summary>
    public bool PanelRight = false;
    /// <summary>Show the map, preview, pinned map and HUD lines only while you sit in a cockpit, seat or remote control.</summary>
    public bool OnlyInCockpit = false;
    /// <summary>Default zoom with no route: the grid size round your ship, km (one of <see cref="GridChoicesKm"/>).</summary>
    public double DefaultGridKm = DefaultGridDefault;
    public const double DefaultGridDefault = 2000;
    public static readonly double[] GridChoicesKm = { 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000 };
    /// <summary>Show the map window whenever F&amp;B's N list opens.</summary>
    public bool ShowMapWithNav = true;
    /// <summary>Keep F&amp;B's N list open when you pick a destination (it closes by itself otherwise).</summary>
    public bool KeepNavOpen = true;
    /// <summary>Preview window beside the N list: right (default) or left.</summary>
    public bool PreviewLeft = false;
    public float PreviewSize = 1f;
    /// <summary>The side preview stays centred on your ship (its FOLLOW button). On at the start of each session; not saved.</summary>
    public bool PreviewFollow = true;
    /// <summary>While a view follows your ship it turns so your forward movement points up the screen (U in the full map).</summary>
    public bool FollowHeading = true;
    /// <summary>Your ship's map icon: made from the blocks of the ship you fly, else the drawn one for its class.</summary>
    public bool ShipIconFromShip = false;
    /// <summary>The side of the ship that icon shows: 0 top, 1 right, 2 bottom, 3 left; <see cref="ShipMask.Auto"/> picks top or right, whichever looks down its thinner dimension.</summary>
    public int ShipIconSide = ShipMask.Auto;
    /// <summary>Each ship keeps its own settings (a file per ship name); off: one set for all ships. Read from the main file only.</summary>
    public bool PerShip = true;
    /// <summary>Ultra drawing quality: smooth-edged lines and circles, shapes on whole pixels, rounder curves, a sharper destination view.</summary>
    public bool Ultra = false;
    /// <summary>Picture-in-picture: the preview stays on screen in a corner while you fly (Alt+key, cockpit toolbar action; Q in the full map moves it).</summary>
    public bool Pip = false;
    /// <summary>Show Flip and Burn's real speed on the HUD speed dial (stops it flickering to 150 m/s).</summary>
    public bool FixSpeedometer = true;
    /// <summary>Flying a route by hand: the markers for where to point the nose (burn, brake, correct).</summary>
    public bool BurnGuide = true;
    /// <summary>In a seat (first or third person): no game crosshair; the nose marker, in the map theme's style, shows where the ship points.</summary>
    public bool HideCrosshair;
    /// <summary>The nose marker's look: "Theme" (the map theme's own) or a theme's name for its marker.</summary>
    public string NoseStyle = "Theme";
    /// <summary>Each theme's palette: another theme's name to use its colours (missing: its own).</summary>
    public readonly System.Collections.Generic.Dictionary<string, string> Palettes = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Each theme's own colours over its palette: theme → slot key (<see cref="MapTheme.Slots"/>) → 0xRRGGBB.</summary>
    public readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, int>> Colors = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Each theme's destination-view overlay (missing: the theme's own).</summary>
    public readonly System.Collections.Generic.Dictionary<string, FeedOverlay> Overlays = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Screen noise on the overlays that have it (grids, scan lines, specks).</summary>
    public bool FeedNoise = true;

    /// <summary>The overlay <paramref name="theme"/> uses.</summary>
    public FeedOverlay OverlayOf(MapTheme theme)
    {
        theme = theme?.Base ?? MapTheme.Classic;
        return Overlays.TryGetValue(theme.Name, out var o) ? o : DestOverlay.Default(theme.Style);
    }

    /// <summary>The theme <paramref name="name"/> as this player has it: its palette and their colours.</summary>
    public MapTheme Look(string name)
    {
        var look = MapTheme.Find(name);
        Palettes.TryGetValue(look.Name, out var pal);
        Colors.TryGetValue(look.Name, out var cols);
        return MapTheme.Make(look, pal != null ? MapTheme.Find(pal) : null, cols);
    }

    /// <summary>The theme's colours for editing (made when missing).</summary>
    public System.Collections.Generic.Dictionary<string, int> ColorsOf(string theme)
    {
        theme = MapTheme.Find(theme).Name;
        if (!Colors.TryGetValue(theme, out var d)) Colors[theme] = d = new System.Collections.Generic.Dictionary<string, int>();
        return d;
    }

    /// <summary>The nose marker's style, or null for the theme's own.</summary>
    public ThemeStyle? NoseOverride => string.Equals(NoseStyle, "Theme", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(NoseStyle) ? null : MapTheme.Find(NoseStyle).Style;

    /// <summary>Puts the chosen theme (with its palette and colours) and nose marker in use.</summary>
    public void ApplyLook()
    {
        MapTheme.Current = Look(Theme);
        SDX2_SystemMap.BurnGuide.NoseOverride = NoseOverride;
    }
    /// <summary>Keep the game on controller mode (glyphs, menus) when it would flip to keyboard/mouse with no key, button, movement or scroll behind it.</summary>
    /// <summary>PiP corner: TR, TL, BR or BL.</summary>
    public string PipCorner = "TR";
    /// <summary>PiP size factor (0.5 to 2).</summary>
    public float PipSize = 1f;
    /// <summary>Within the set range (up to 30 km) of the route's destination, a live view of it from your ship comes up in a corner.</summary>
    public bool DestView = true;
    /// <summary>The destination view's corner (it takes the other side when the pinned map is in the same one) and size factor (0.5 to 2).</summary>
    public string DestViewCorner = "TL";
    public float DestViewSize = 1f;
    /// <summary>How near the destination the view comes up (km, 1 to 30).</summary>
    public double DestViewRangeKm = 10;
    /// <summary>The view takes the map preview's place while it's up (the preview comes back after).</summary>
    public bool DestViewReplacesMap = false;
    /// <summary>The autopilot's line on the HUD: its middle across the screen and its top down it (fractions of the
    /// screen), and its size.</summary>
    public float HudX = 0.5f, HudY = 0.05f, HudSize = 1f;
    /// <summary>The map's look: a theme's name (K in the full map).</summary>
    public string Theme = "Classic";
    /// <summary>Plan detour waypoints round worlds in the way (added to F&amp;B's list as "Detour n of m - destination", the destination's short name: "Tycho").</summary>
    public bool AvoidBodies = true;
    /// <summary>Detours also keep out of the asteroid fields (over or under the Belt, round the Trojans and the Greeks) where they can.</summary>
    public bool AvoidFields = true;
    /// <summary>How far short of the destination a route stops, km (0.5 at the least; a base on a world's ground: 3 km over it at the least).</summary>
    public double OffsetKm = 3;
    /// <summary>Send the route to the autopilot PB by itself whenever the plan changes (else Ctrl+key / A in the map).</summary>
    public bool AutopilotAutoSend = false;
    /// <summary>The autopilot route panel's last limits.</summary>
    public RouteLimits Limits = new RouteLimits();
    /// <summary>The full map on a second screen (its own window), opened with each world.</summary>
    public bool SecondScreen = false;
    /// <summary>Which monitor it goes on: 0 = the first that isn't the main one, else 1-based.</summary>
    public int SecondScreenMonitor = 0;
    public double Yaw = 0.9, Pitch = 0.62;
    public MapOptions Options = new MapOptions();

    public static MapSettings Load(string path)
    {
        var s = new MapSettings();
        string legacyKey = null; bool keys = false, swapped = false;
        var names = new System.Collections.Generic.List<(string K, string V)>(); // theme-keyed lines, read once the file's version is known
        try
        {
            if (!File.Exists(path)) return s;
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                int eq = line.IndexOf('=');
                if (line.StartsWith("#") || eq < 1) continue;
                string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                switch (k)
                {
                    case "PreviewKey": legacyKey = v; break; // older files: one key with Shift/Ctrl/Alt/Ctrl+Shift
                    case "KeyMap": s.KeyMap = KeyCombo.Parse(v).ToString(); keys = true; break;
                    case "KeyAutopilot": s.KeyAutopilot = KeyCombo.Parse(v).ToString(); keys = true; break;
                    case "KeyPin": s.KeyPin = KeyCombo.Parse(v).ToString(); keys = true; break;
                    case "KeySteer": s.KeySteer = KeyCombo.Parse(v).ToString(); keys = true; break;
                    case "KeySettings": s.KeySettings = KeyCombo.Parse(v).ToString(); keys = true; break;
                    case "KeyCursor": s.KeyCursor = KeyCombo.Parse(v).ToString(); break;
                    case "KeyPanel": s.KeyPanel = KeyCombo.Parse(v).ToString(); break;
                    case "KeySecondScreen": s.KeySecondScreen = KeyCombo.Parse(v).ToString(); break;
                    case "SecondScreen": s.SecondScreen = B(v, false); break;
                    case "SecondScreenMonitor": s.SecondScreenMonitor = (int)Math.Max(0, Math.Min(16, D(v, 0))); break;
                    case "PanelRight": s.PanelRight = B(v, false); break;
                    case "ShowMapWithNav": s.ShowMapWithNav = B(v, true); break;
                    case "OnlyInCockpit": s.OnlyInCockpit = B(v, false); break;
                    case "FollowHeading": s.FollowHeading = B(v, true); break;
                    case "ShipIconFromShip": case "RealShipIcons": s.ShipIconFromShip = B(v, false); break;
                    case "ShipIconSide": { int side = (int)D(v, ShipMask.Auto); s.ShipIconSide = side >= 0 && side <= 3 ? side : ShipMask.Auto; break; }
                    case "PerShip": s.PerShip = B(v, true); break;
                    case "Ultra": s.Ultra = B(v, false); break;
                    case "DefaultGridKm": s.DefaultGridKm = NearestGrid(D(v, DefaultGridDefault)); break;
                    case "KeepNavOpen": s.KeepNavOpen = B(v, true); break;
                    case "ApMaxSpeed": s.Limits.MaxSpeed = Math.Max(0, D(v, 0)); break;
                    case "ApBySignature": s.Limits.BySignature = B(v, false); break;
                    case "ApParabolic": s.Limits.Parabolic = B(v, false); break;
                    case "ApThrustPct": s.Limits.ThrustPct = Math.Max(RouteLimits.MinThrustPct, Math.Min(100, D(v, 100))); break;
                    // the signature caps are the Weak exhaust-cone range; older files had the Strong all-round range
                    // (x2 for Weak, x4 for the cone) or, briefly, the Weak all-round range (x4)
                    case "ApSignatureKm": s.Limits.SignatureKm = Math.Max(0, D(v, 0)) * 8; break;
                    case "ApWeakSignatureKm": s.Limits.SignatureKm = Math.Max(0, D(v, 0)) * 4; break;
                    case "ApConeSignatureKm": s.Limits.SignatureKm = Math.Max(0, D(v, 0)); break;
                    case "ApBrakePct": s.Limits.BrakePct = Math.Max(RouteLimits.MinThrustPct, Math.Min(100, D(v, 100))); break;
                    case "ApBrakeSignatureKm": s.Limits.BrakeSignatureKm = Math.Max(0, D(v, 0)) * 8; break;
                    case "ApBrakeWeakSignatureKm": s.Limits.BrakeSignatureKm = Math.Max(0, D(v, 0)) * 4; break;
                    case "ApBrakeConeSignatureKm": s.Limits.BrakeSignatureKm = Math.Max(0, D(v, 0)); break;
                    case "PreviewLeft": s.PreviewLeft = B(v, false); break;
                    case "PreviewSize": s.PreviewSize = (float)D(v, 1); break;
                    case "Pip": s.Pip = B(v, false); break;
                    case "FixSpeedometer": s.FixSpeedometer = B(v, true); break;
                    case "BurnGuide": s.BurnGuide = B(v, true); break;
                    case "HideCrosshair": s.HideCrosshair = B(v, false); break;
                    case "PipCorner": s.PipCorner = SideLayout.Corner(v) ?? "TR"; break;
                    case "PipSize": s.PipSize = (float)D(v, 1); break;
                    case "DestView": s.DestView = B(v, true); break;
                    case "DestViewCorner": s.DestViewCorner = SideLayout.Corner(v) ?? "TL"; break;
                    case "DestViewSize": s.DestViewSize = (float)D(v, 1); break;
                    case "DestViewRangeKm": s.DestViewRangeKm = D(v, 10); break;
                    case "DestViewReplacesMap": s.DestViewReplacesMap = B(v, false); break;
                    case "FeedNoise": s.FeedNoise = B(v, true); break;
                    case "HudX": s.HudX = (float)D(v, 0.5); break;
                    case "HudY": s.HudY = (float)D(v, 0.05); break;
                    case "HudSize": s.HudSize = (float)D(v, 1); break;
                    case "ThemeNames": swapped = D(v, 1) >= 2; break;
                    case "Theme": names.Add((k, v)); break;
                    case "AvoidBodies": s.AvoidBodies = B(v, true); break;
                    case "AvoidFields": s.AvoidFields = B(v, true); break;
                    case "OffsetKm": s.OffsetKm = D(v, 3); break;
                    case "AutopilotAutoSend": s.AutopilotAutoSend = B(v, false); break;
                    case "Yaw": s.Yaw = D(v, s.Yaw); break;
                    case "Pitch": s.Pitch = D(v, s.Pitch); break;
                    case "Grid": s.Options.Grid = B(v, true); break;
                    case "Orbits": s.Options.Orbits = B(v, true); break;
                    case "Sectors": s.Options.Sectors = B(v, true); break;
                    case "Stations": s.Options.Stations = B(v, true); break;
                    case "Gps": s.Options.Gps = B(v, true); break;
                    case "Labels": s.Options.Labels = B(v, true); break;
                    case "Moons": s.Options.Moons = B(v, true); break;
                    case "ShipRoutes": s.Options.Routes = B(v, true); break;
                    case "NoseStyle": names.Add((k, v)); break;
                    default:
                        // Palette.<theme>=<theme>; Color.<theme>.<slot>=#RRGGBB
                        if (k.StartsWith("Palette.", StringComparison.OrdinalIgnoreCase) || k.StartsWith("Overlay.", StringComparison.OrdinalIgnoreCase)
                            || k.StartsWith("Color.", StringComparison.OrdinalIgnoreCase)) names.Add((k, v));
                        break;
                }
            }
            // A file from before the OPA and Belter names changed places (no ThemeNames=2) keeps each look it chose.
            string N(string name) => swapped ? name : string.Equals(name, "OPA", StringComparison.OrdinalIgnoreCase) ? "Belter"
                : string.Equals(name, "Belter", StringComparison.OrdinalIgnoreCase) ? "OPA" : name;
            foreach (var (k, v) in names)
            {
                if (k == "Theme") s.Theme = MapTheme.Find(N(v)).Name;
                else if (k == "NoseStyle") s.NoseStyle = string.Equals(v, "Theme", StringComparison.OrdinalIgnoreCase) ? "Theme" : MapTheme.Find(N(v)).Name;
                else if (k.StartsWith("Palette.", StringComparison.OrdinalIgnoreCase))
                {
                    var th = MapTheme.Find(N(k.Substring(8))).Name; var pal = MapTheme.Find(N(v)).Name;
                    if (pal != th) s.Palettes[th] = pal;
                }
                else if (k.StartsWith("Overlay.", StringComparison.OrdinalIgnoreCase))
                {
                    string o = swapped ? v : v.ToLowerInvariant() switch { "opatag" => "BelterTag", "opascrap" => "BelterScrap", "beltercontact" => "OpaContact", _ => v };
                    if (DestOverlay.TryParse(o, out var ov)) s.Overlays[MapTheme.Find(N(k.Substring(8))).Name] = ov;
                }
                else
                {
                    var parts = k.Split('.');
                    if (parts.Length == 3 && MapTheme.ParseHex(v, out int rgb) && Array.Exists(MapTheme.AllSlots, x => x.Key == parts[2]))
                        s.ColorsOf(N(parts[1]))[parts[2]] = rgb;
                }
            }
        }
        catch { }
        if (!keys && !string.IsNullOrWhiteSpace(legacyKey))
        {
            string k = KeyCombo.Parse(legacyKey).Key ?? "M";
            s.KeyMap = "Shift+" + k; s.KeyAutopilot = "Ctrl+" + k; s.KeyPin = "Alt+" + k; s.KeySteer = "Ctrl+Shift+" + k;
        }
        if (double.IsNaN(s.Yaw) || double.IsInfinity(s.Yaw)) s.Yaw = 0.9;
        if (double.IsNaN(s.Pitch) || double.IsInfinity(s.Pitch)) s.Pitch = 0.62;
        s.Pitch = Math.Max(OrbitCamera.MinPitch, Math.Min(OrbitCamera.MaxPitch, s.Pitch));
        s.OffsetKm = double.IsNaN(s.OffsetKm) ? 3 : Math.Max(0.5, Math.Min(20, s.OffsetKm));
        s.PreviewSize = Math.Max(0.5f, Math.Min(1.5f, float.IsNaN(s.PreviewSize) ? 1 : s.PreviewSize));
        s.PipSize = Math.Max(0.5f, Math.Min(2f, float.IsNaN(s.PipSize) ? 1 : s.PipSize));
        s.DestViewSize = Math.Max(0.5f, Math.Min(2f, float.IsNaN(s.DestViewSize) ? 1 : s.DestViewSize));
        s.DestViewRangeKm = SDX2_SystemMap.DestView.ClampRangeKm(s.DestViewRangeKm);
        s.HudX = Math.Max(0f, Math.Min(1f, float.IsNaN(s.HudX) ? 0.5f : s.HudX));
        s.HudY = Math.Max(0f, Math.Min(0.95f, float.IsNaN(s.HudY) ? 0.05f : s.HudY));
        s.HudSize = Math.Max(0.6f, Math.Min(1.8f, float.IsNaN(s.HudSize) ? 1 : s.HudSize));
        return s;
    }

    public void Save(string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# SDX2 System Map settings. Keys are Ctrl/Shift/Alt plus a game key name (M, F8, OemQuestion...), or None.");
        void L(string k, object v) => sb.Append(k).Append('=').AppendLine(Convert.ToString(v, CultureInfo.InvariantCulture));
        L("KeyMap", KeyMap); L("KeyAutopilot", KeyAutopilot); L("KeyPin", KeyPin); L("KeySteer", KeySteer); L("KeySettings", KeySettings); L("KeyCursor", KeyCursor); L("KeyPanel", KeyPanel); L("KeySecondScreen", KeySecondScreen); L("PanelRight", PanelRight);
        L("SecondScreen", SecondScreen); L("SecondScreenMonitor", SecondScreenMonitor);
        L("OnlyInCockpit", OnlyInCockpit); L("FollowHeading", FollowHeading); L("ShipIconFromShip", ShipIconFromShip); L("ShipIconSide", ShipIconSide); L("PerShip", PerShip); L("Ultra", Ultra); L("DefaultGridKm", DefaultGridKm); L("ShowMapWithNav", ShowMapWithNav); L("KeepNavOpen", KeepNavOpen); L("PreviewLeft", PreviewLeft); L("PreviewSize", PreviewSize); L("Theme", Theme);
        L("Pip", Pip); L("FixSpeedometer", FixSpeedometer); L("BurnGuide", BurnGuide); L("HideCrosshair", HideCrosshair); L("PipCorner", PipCorner); L("PipSize", PipSize); L("DestView", DestView); L("DestViewCorner", DestViewCorner); L("DestViewSize", DestViewSize); L("DestViewRangeKm", DestViewRangeKm); L("DestViewReplacesMap", DestViewReplacesMap); L("FeedNoise", FeedNoise); L("HudX", HudX); L("HudY", HudY); L("HudSize", HudSize);
        L("AvoidBodies", AvoidBodies); L("AvoidFields", AvoidFields); L("OffsetKm", OffsetKm); L("AutopilotAutoSend", AutopilotAutoSend);
        L("ApMaxSpeed", Limits.MaxSpeed); L("ApBySignature", Limits.BySignature); L("ApParabolic", Limits.Parabolic); L("ApThrustPct", Limits.ThrustPct); L("ApConeSignatureKm", Limits.SignatureKm);
        L("ApBrakePct", Limits.BrakePct); L("ApBrakeConeSignatureKm", Limits.BrakeSignatureKm);
        L("Yaw", Yaw); L("Pitch", Pitch);
        L("Grid", Options.Grid); L("Orbits", Options.Orbits); L("Sectors", Options.Sectors); L("Stations", Options.Stations);
        L("Gps", Options.Gps); L("Labels", Options.Labels); L("Moons", Options.Moons); L("ShipRoutes", Options.Routes);
        L("ThemeNames", 2); L("NoseStyle", NoseStyle);
        foreach (var kv in Palettes) L("Palette." + kv.Key, kv.Value);
        foreach (var kv in Overlays) L("Overlay." + kv.Key, kv.Value);
        foreach (var th in Colors)
            foreach (var kv in th.Value) L("Color." + th.Key + "." + kv.Key, MapTheme.Hex(MapColor.Rgb(kv.Value)));
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, sb.ToString());
    }

    /// <summary>The grid choice nearest to <paramref name="km"/> (by ratio).</summary>
    public static double NearestGrid(double km)
    {
        if (double.IsNaN(km) || km <= 0) return DefaultGridDefault;
        double best = GridChoicesKm[0];
        foreach (var g in GridChoicesKm) if (Math.Abs(Math.Log(g / km)) < Math.Abs(Math.Log(best / km))) best = g;
        return best;
    }

    private static bool B(string v, bool def) => bool.TryParse(v, out var b) ? b : def;
    private static double D(string v, double def) => double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : def;
}
