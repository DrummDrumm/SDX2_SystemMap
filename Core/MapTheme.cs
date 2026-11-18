using System;

namespace SDX2_SystemMap;

/// <summary>How a theme decorates the map beyond its colours.</summary>
public enum ThemeStyle
{
    /// <summary>Plain chart: solid orbits, plain frame.</summary>
    Chart,
    /// <summary>MCRN tactical HUD: segmented red arcs, dashed and ticked rings, ringed planets, corner brackets.</summary>
    Tachi,
    /// <summary>UNN plotting chart: the contact chart (hex texture, Sol crosshair, hexagon stations, triangle contacts, boxed
    /// labels, chamfered frame) with the nav HUD's lock reticle, ruler ticks and red Belt arcs, plus bearing spokes, degree ticks
    /// and gold trim.</summary>
    Naval,
    /// <summary>OPA Belter salvage HUD: dashed orbits with hazard marks, hexagon-framed worlds, hazard-striped bars.</summary>
    Belter,
    /// <summary>Protogen nav HUD: a lock reticle on the destination with range rings and chevrons, a crosshair through it, ruler ticks, red Belt arcs.</summary>
    Protogen,
    /// <summary>Contact chart: a radar contact plot crossed with an inner-system chart: hex texture, a crosshair and bearings
    /// through Sol, alternating dashed and solid orbits, dashed spheres round worlds, hexagon stations, triangle contacts,
    /// tinted label boxes, bracketed destination and a chamfered frame.</summary>
    Contact,
}

/// <summary>
/// The map's colours and style. <see cref="Classic"/> is the original blue chart look; <see cref="Tachi"/> is a
/// black-and-red MCRN tactical HUD; <see cref="Unn"/> is a navy-and-gold UNN plotting chart; <see cref="Opa"/> is a
/// charcoal, hazard-orange and teal Belter HUD; <see cref="Protogen"/> is a cyan-and-magenta nav HUD; <see cref="Belter"/> is a green OPA contact chart. K in the full map switches it (saved as Theme); the full
/// map, the side preview and the route panels all follow it.
/// </summary>
public sealed class MapTheme
{
    public string Name;
    /// <summary>One line about the look (chat replies).</summary>
    public string Blurb = "";
    public ThemeStyle Style;
    /// <summary>The Tachi's HUD decorations (segmented arcs, ticks, planet rings, corner brackets).</summary>
    public bool Hud => Style == ThemeStyle.Tachi;
    /// <summary>How bright the background stars are (1 = full).</summary>
    public float Stars = 1;
    /// <summary>How far sector colours are pulled toward the accent (0 = their own colours).</summary>
    public float SectorTint;

    public MapColor Bg, Grid, Orbit, Text, Dim, PanelBg, Accent, Ship, Station;
    public MapColor Burn, Coast, Brake, Border, Warn, Detour, Waypoint, Hazard, Track, Plan;
    /// <summary>A second line colour for HUD rings (the thin bright dashes).</summary>
    public MapColor Fine;
    /// <summary>A second accent for trim (UNN gold, OPA teal); the accent itself when unset.</summary>
    public MapColor Trim;
    /// <summary>Autopilot / route panels: background, text, dim text.</summary>
    public MapColor ApPanel, ApText, ApDim;

    public static readonly MapTheme Classic = new MapTheme
    {
        Name = "Classic", Blurb = "the blue chart",
        Bg = MapColor.Rgb(0x04080E, 238), Grid = MapColor.Rgb(0x3A6A8C), Orbit = MapColor.Rgb(0x6C8EA8),
        Text = MapColor.Rgb(0xD8E6F0), Dim = MapColor.Rgb(0x8AA2B4), PanelBg = MapColor.Rgb(0x0A121C, 225),
        Accent = MapColor.Rgb(0x4FC3F7), Ship = MapColor.Rgb(0x7CFC9A), Station = MapColor.Rgb(0x66E0FF),
        Burn = MapColor.Rgb(0xFF8A3D), Coast = MapColor.Rgb(0x4FC3F7), Brake = MapColor.Rgb(0x7CFC9A),
        Border = MapColor.Rgb(0xFFD24A), Warn = MapColor.Rgb(0xFF5252), Detour = MapColor.Rgb(0xE07CFF),
        Waypoint = MapColor.Rgb(0x5CE1E6), Hazard = MapColor.Rgb(0xFF4A3D), Track = MapColor.Rgb(0xF2F6FA),
        Plan = MapColor.Rgb(0x9FB4C4), Fine = MapColor.Rgb(0xD8E6F0), Trim = MapColor.Rgb(0x4FC3F7),
        ApPanel = MapColor.Rgb(0x0A121C, 235), ApText = MapColor.Rgb(0xD8E6F0), ApDim = MapColor.Rgb(0x8FA3B5),
    };

    public static readonly MapTheme Tachi = new MapTheme
    {
        Name = "Tachi", Blurb = "MCRN red tactical HUD", Style = ThemeStyle.Tachi, Stars = 0.35f, SectorTint = 0.6f,
        Bg = MapColor.Rgb(0x000000, 245), Grid = MapColor.Rgb(0x8A1C14), Orbit = MapColor.Rgb(0xE8302A),
        Text = MapColor.Rgb(0xFF7A5C), Dim = MapColor.Rgb(0xB8503F), PanelBg = MapColor.Rgb(0x080202, 232),
        Accent = MapColor.Rgb(0xFF3B2F), Ship = MapColor.Rgb(0xFFD27A), Station = MapColor.Rgb(0xFFB347),
        Burn = MapColor.Rgb(0xFF8A1E), Coast = MapColor.Rgb(0xF4ECE2), Brake = MapColor.Rgb(0xFFC830),
        Border = MapColor.Rgb(0xFFE08A), Warn = MapColor.Rgb(0xFF2020), Detour = MapColor.Rgb(0xFF8FA8),
        Waypoint = MapColor.Rgb(0x52D6DE), Hazard = MapColor.Rgb(0xFF2A1A), Track = MapColor.Rgb(0xFFFFFF),
        Plan = MapColor.Rgb(0xA8483C), Fine = MapColor.Rgb(0xF4ECE2), Trim = MapColor.Rgb(0xFF3B2F),
        ApPanel = MapColor.Rgb(0x080202, 238), ApText = MapColor.Rgb(0xFF8A6A), ApDim = MapColor.Rgb(0xB8503F),
    };

    /// <summary>UNN: the navy chart (Themes/png/chart-navy.png) crossed with the Naval Ice HUD (Themes/png/naval-ice.png):
    /// deep navy, ice-white linework, UN blue with gold trim.</summary>
    public static readonly MapTheme Unn = new MapTheme
    {
        Name = "UNN", Blurb = "UN Navy plotting chart", Style = ThemeStyle.Naval, Stars = 0.5f, SectorTint = 0.4f,
        Bg = MapColor.Rgb(0x040C1C, 244), Grid = MapColor.Rgb(0x4A6AA0), Orbit = MapColor.Rgb(0xE6F0FF),
        Text = MapColor.Rgb(0xE6F0FF), Dim = MapColor.Rgb(0x9AB0D0), PanelBg = MapColor.Rgb(0x071226, 232),
        Accent = MapColor.Rgb(0x8EC3FF), Ship = MapColor.Rgb(0x6AD8FF), Station = MapColor.Rgb(0xC4E2FF),
        Burn = MapColor.Rgb(0xFF9F43), Coast = MapColor.Rgb(0xF2F8FF), Brake = MapColor.Rgb(0x7FE8A8),
        Border = MapColor.Rgb(0xFFE9A8), Warn = MapColor.Rgb(0xFF5A5A), Detour = MapColor.Rgb(0xC79BFF),
        Waypoint = MapColor.Rgb(0x7FE0E8), Hazard = MapColor.Rgb(0xFF4A3D), Track = MapColor.Rgb(0xFFFFFF),
        Plan = MapColor.Rgb(0x7F95AE), Fine = MapColor.Rgb(0xF2F8FF), Trim = MapColor.Rgb(0xE8C25A),
        ApPanel = MapColor.Rgb(0x071226, 238), ApText = MapColor.Rgb(0xE6F0FF), ApDim = MapColor.Rgb(0x9AB0D0),
    };

    /// <summary>OPA: charcoal salvage HUD, bone-white linework, hazard orange with Belter teal.</summary>
    public static readonly MapTheme Opa = new MapTheme
    {
        Name = "OPA", Blurb = "Belter salvage HUD", Style = ThemeStyle.Belter, Stars = 0.45f, SectorTint = 0.45f,
        Bg = MapColor.Rgb(0x0C0C0B, 244), Grid = MapColor.Rgb(0x3B3524), Orbit = MapColor.Rgb(0xCFC6B0),
        Text = MapColor.Rgb(0xF2E8D4), Dim = MapColor.Rgb(0xA3967C), PanelBg = MapColor.Rgb(0x15130F, 232),
        Accent = MapColor.Rgb(0xFF7A1A), Ship = MapColor.Rgb(0x2EC4B6), Station = MapColor.Rgb(0xFFB000),
        Burn = MapColor.Rgb(0xFF5A1F), Coast = MapColor.Rgb(0x2EC4B6), Brake = MapColor.Rgb(0xFFD23F),
        Border = MapColor.Rgb(0xF2E8D4), Warn = MapColor.Rgb(0xFF3030), Detour = MapColor.Rgb(0xFF6FB5),
        Waypoint = MapColor.Rgb(0x8EE3D9), Hazard = MapColor.Rgb(0xFF2A1A), Track = MapColor.Rgb(0xFFFFFF),
        Plan = MapColor.Rgb(0x8A7F68), Fine = MapColor.Rgb(0xF2E8D4), Trim = MapColor.Rgb(0x2EC4B6),
        ApPanel = MapColor.Rgb(0x15130F, 238), ApText = MapColor.Rgb(0xF2E8D4), ApDim = MapColor.Rgb(0xA3967C),
    };

    /// <summary>Protogen: a near-black teal nav HUD, cyan linework, magenta trim, red Belt arcs (Themes/png/protogen-cyan.png).</summary>
    public static readonly MapTheme Protogen = new MapTheme
    {
        Name = "Protogen", Blurb = "Protogen Systems cyan nav HUD", Style = ThemeStyle.Protogen, Stars = 0.6f, SectorTint = 0.5f,
        Bg = MapColor.Rgb(0x03070A, 244), Grid = MapColor.Rgb(0x1E5A60), Orbit = MapColor.Rgb(0x5FE6EE),
        Text = MapColor.Rgb(0xDCFBFF), Dim = MapColor.Rgb(0x96D6DC), PanelBg = MapColor.Rgb(0x061218, 232),
        Accent = MapColor.Rgb(0x5FE6EE), Ship = MapColor.Rgb(0x8FF7FF), Station = MapColor.Rgb(0xFF7AD9),
        Burn = MapColor.Rgb(0xFF9A4A), Coast = MapColor.Rgb(0xF0FEFF), Brake = MapColor.Rgb(0xFF7AD9),
        Border = MapColor.Rgb(0xFFD86A), Warn = MapColor.Rgb(0xFF4A6A), Detour = MapColor.Rgb(0xC79BFF),
        Waypoint = MapColor.Rgb(0x7FE8A8), Hazard = MapColor.Rgb(0xFF4A6A), Track = MapColor.Rgb(0xFFFFFF),
        Plan = MapColor.Rgb(0x5E9AA0), Fine = MapColor.Rgb(0xDCFBFF), Trim = MapColor.Rgb(0xFF7AD9),
        ApPanel = MapColor.Rgb(0x061218, 238), ApText = MapColor.Rgb(0xDCFBFF), ApDim = MapColor.Rgb(0x96D6DC),
    };

    /// <summary>Belter: a green OPA belt pilot chart, radar contact plot crossed with the inner-system chart (Themes/png/chart-belter.png).</summary>
    public static readonly MapTheme Belter = new MapTheme
    {
        Name = "Belter", Blurb = "green OPA belt pilot chart", Style = ThemeStyle.Contact, Stars = 0.3f, SectorTint = 0.5f,
        Bg = MapColor.Rgb(0x070D07, 244), Grid = MapColor.Rgb(0x3A7A3A), Orbit = MapColor.Rgb(0xA8F0A0),
        Text = MapColor.Rgb(0xE0F8DC), Dim = MapColor.Rgb(0x90C08A), PanelBg = MapColor.Rgb(0x0A140A, 232),
        Accent = MapColor.Rgb(0x5FD36A), Ship = MapColor.Rgb(0x4AE8C8), Station = MapColor.Rgb(0xA8F0A0),
        Burn = MapColor.Rgb(0xFF9A3A), Coast = MapColor.Rgb(0xE0F8DC), Brake = MapColor.Rgb(0x4AE8C8),
        Border = MapColor.Rgb(0xF2E25A), Warn = MapColor.Rgb(0xFF5A4A), Detour = MapColor.Rgb(0xC79BFF),
        Waypoint = MapColor.Rgb(0x9CF0FF), Hazard = MapColor.Rgb(0xFF5A4A), Track = MapColor.Rgb(0xFFFFFF),
        Plan = MapColor.Rgb(0x6A9A66), Fine = MapColor.Rgb(0xE0F8DC), Trim = MapColor.Rgb(0xF2E25A),
        ApPanel = MapColor.Rgb(0x0A140A, 238), ApText = MapColor.Rgb(0xE0F8DC), ApDim = MapColor.Rgb(0x90C08A),
    };

    public static readonly MapTheme[] All = { Classic, Tachi, Unn, Opa, Protogen, Belter };

    /// <summary>The theme in use (the map, the preview and the route panels all read it).</summary>
    public static MapTheme Current = Classic;

    public static MapTheme Find(string name)
    {
        foreach (var t in All) if (string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) return t;
        if (string.Equals(name, "Solar HUD", StringComparison.OrdinalIgnoreCase)) return Tachi; // its first name
        return Classic;
    }

    /// <summary>A theme from what a player might type: names plus faction words (mcrn, mars, un, navy, belter, ceres...); null if none.</summary>
    public static MapTheme Match(string text)
    {
        var w = (text ?? "").Trim().ToLowerInvariant();
        if (w.Length == 0) return null;
        foreach (var t in All) if (w == t.Name.ToLowerInvariant()) return t;
        if (w.Contains("protogen") || w.Contains("cyan") || w == "pgs") return Protogen;
        if (w.Contains("green") || w.Contains("contact") || w.Contains("pilot")) return Belter;
        if (w.Contains("tachi") || w.Contains("mcr") || w.Contains("mars") || w.Contains("hud") || w.Contains("solar") || w.Contains("red")) return Tachi;
        if (w.Contains("unn") || w == "un" || w.Contains("navy") || w.Contains("earth") || w.Contains("naval")) return Unn;
        if (w.Contains("opa") || w.Contains("belt") || w.Contains("ceres") || w.Contains("salvage")) return Opa;
        if (w.Contains("classic") || w.Contains("blue") || w.Contains("default") || w.Contains("chart")) return Classic;
        return null;
    }

    /// <summary>The next theme after this one (K in the full map).</summary>
    public MapTheme Next() => All[(Array.IndexOf(All, this) + 1) % All.Length];

    /// <summary>A sector's colour in this theme.</summary>
    public MapColor SectorColor(MapColor c) => SectorTint <= 0 ? c : MapColor.Lerp(c, Accent, SectorTint);
}
