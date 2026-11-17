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
    public string KeyMap = "Shift+M", KeyAutopilot = "Ctrl+M", KeyPin = "Alt+M", KeySteer = "Ctrl+Shift+M", KeySettings = "Ctrl+Alt+OemQuestion";
    /// <summary>Show the map, preview, pinned map and HUD lines only while you sit in a cockpit, seat or remote control.</summary>
    public bool OnlyInCockpit = false;
    /// <summary>Show the map window whenever F&amp;B's N list opens.</summary>
    public bool ShowMapWithNav = true;
    /// <summary>Keep F&amp;B's N list open when you pick a destination (it closes by itself otherwise).</summary>
    public bool KeepNavOpen = true;
    /// <summary>Preview window beside the N list: right (default) or left.</summary>
    public bool PreviewLeft = false;
    public float PreviewSize = 1f;
    /// <summary>The side preview stays centred on your ship (its FOLLOW button). On at the start of each session; not saved.</summary>
    public bool PreviewFollow = true;
    /// <summary>Picture-in-picture: the preview stays on screen in a corner while you fly (Alt+key, cockpit toolbar action; Q in the full map moves it).</summary>
    public bool Pip = false;
    /// <summary>Show Flip and Burn's real speed on the HUD speed dial (stops it flickering to 150 m/s).</summary>
    public bool FixSpeedometer = true;
    /// <summary>PiP corner: TR, TL, BR or BL.</summary>
    public string PipCorner = "TR";
    /// <summary>PiP size factor (0.5 to 2).</summary>
    public float PipSize = 1f;
    /// <summary>The map's look: "Classic" or "Tachi" (K in the full map).</summary>
    public string Theme = "Classic";
    /// <summary>Plan detour waypoints round worlds in the way (added to F&amp;B's list as "Detour n of m - destination").</summary>
    public bool AvoidBodies = true;
    /// <summary>Wanted clearance above world surfaces, km.</summary>
    public double ClearanceKm = 100;
    /// <summary>Send the route to the autopilot PB by itself whenever the plan changes (else Ctrl+key / A in the map).</summary>
    public bool AutopilotAutoSend = false;
    /// <summary>The autopilot route panel's last limits.</summary>
    public RouteLimits Limits = new RouteLimits();
    public double Yaw = 0.9, Pitch = 0.62;
    public MapOptions Options = new MapOptions();

    public static MapSettings Load(string path)
    {
        var s = new MapSettings();
        string legacyKey = null; bool keys = false;
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
                    case "ShowMapWithNav": s.ShowMapWithNav = B(v, true); break;
                    case "OnlyInCockpit": s.OnlyInCockpit = B(v, false); break;
                    case "KeepNavOpen": s.KeepNavOpen = B(v, true); break;
                    case "ApMaxSpeed": s.Limits.MaxSpeed = Math.Max(0, D(v, 0)); break;
                    case "ApBySignature": s.Limits.BySignature = B(v, false); break;
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
                    case "PipCorner": s.PipCorner = SideLayout.Corner(v) ?? "TR"; break;
                    case "PipSize": s.PipSize = (float)D(v, 1); break;
                    case "Theme": s.Theme = MapTheme.Find(v).Name; break;
                    case "AvoidBodies": s.AvoidBodies = B(v, true); break;
                    case "ClearanceKm": s.ClearanceKm = D(v, 100); break;
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
        s.ClearanceKm = double.IsNaN(s.ClearanceKm) ? 100 : Math.Max(10, Math.Min(5000, s.ClearanceKm));
        s.PreviewSize = Math.Max(0.5f, Math.Min(1.5f, float.IsNaN(s.PreviewSize) ? 1 : s.PreviewSize));
        s.PipSize = Math.Max(0.5f, Math.Min(2f, float.IsNaN(s.PipSize) ? 1 : s.PipSize));
        return s;
    }

    public void Save(string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# SDX2 System Map settings. Keys are Ctrl/Shift/Alt plus a game key name (M, F8, OemQuestion...), or None.");
        void L(string k, object v) => sb.Append(k).Append('=').AppendLine(Convert.ToString(v, CultureInfo.InvariantCulture));
        L("KeyMap", KeyMap); L("KeyAutopilot", KeyAutopilot); L("KeyPin", KeyPin); L("KeySteer", KeySteer); L("KeySettings", KeySettings);
        L("OnlyInCockpit", OnlyInCockpit); L("ShowMapWithNav", ShowMapWithNav); L("KeepNavOpen", KeepNavOpen); L("PreviewLeft", PreviewLeft); L("PreviewSize", PreviewSize); L("Theme", Theme);
        L("Pip", Pip); L("FixSpeedometer", FixSpeedometer); L("PipCorner", PipCorner); L("PipSize", PipSize);
        L("AvoidBodies", AvoidBodies); L("ClearanceKm", ClearanceKm); L("AutopilotAutoSend", AutopilotAutoSend);
        L("ApMaxSpeed", Limits.MaxSpeed); L("ApBySignature", Limits.BySignature); L("ApThrustPct", Limits.ThrustPct); L("ApConeSignatureKm", Limits.SignatureKm);
        L("ApBrakePct", Limits.BrakePct); L("ApBrakeConeSignatureKm", Limits.BrakeSignatureKm);
        L("Yaw", Yaw); L("Pitch", Pitch);
        L("Grid", Options.Grid); L("Orbits", Options.Orbits); L("Sectors", Options.Sectors); L("Stations", Options.Stations);
        L("Gps", Options.Gps); L("Labels", Options.Labels); L("Moons", Options.Moons);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, sb.ToString());
    }

    private static bool B(string v, bool def) => bool.TryParse(v, out var b) ? b : def;
    private static double D(string v, double def) => double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : def;
}
