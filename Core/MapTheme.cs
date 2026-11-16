using System;

namespace SDX2_SystemMap;

/// <summary>How a theme decorates the map beyond its colours.</summary>
public enum ThemeStyle
{
    /// <summary>Plain chart: solid orbits, plain frame.</summary>
    Chart,
    /// <summary>MCRN tactical HUD: segmented red arcs, dashed and ticked rings, ringed planets, corner brackets.</summary>
    Tachi,
    /// <summary>UNN plotting chart: bearing spokes and degree ticks, reticle-marked planets, a double frame with gold trim.</summary>
    Naval,
    /// <summary>OPA Belter salvage HUD: dashed orbits with hazard marks, hexagon-framed worlds, hazard-striped bars.</summary>
    Belter,
}

/// <summary>
/// The map's colours and style. <see cref="Classic"/> is the original blue chart look; <see cref="Tachi"/> is a
/// black-and-red MCRN tactical HUD; <see cref="Unn"/> is a navy-and-gold UNN plotting chart; <see cref="Opa"/> is a
/// charcoal, hazard-orange and teal Belter HUD. K in the full map switches it (saved as Theme); the full
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

    /// <summary>UNN: deep navy plotting chart, white linework, UN blue with gold trim.</summary>
    public static readonly MapTheme Unn = new MapTheme
    {
        Name = "UNN", Blurb = "UN Navy plotting chart", Style = ThemeStyle.Naval, Stars = 0.55f, SectorTint = 0.35f,
        Bg = MapColor.Rgb(0x050E1C, 242), Grid = MapColor.Rgb(0x1F3D63), Orbit = MapColor.Rgb(0xC4D6EC),
        Text = MapColor.Rgb(0xEEF4FB), Dim = MapColor.Rgb(0x8EA5C2), PanelBg = MapColor.Rgb(0x0A1932, 232),
        Accent = MapColor.Rgb(0x6FB4FF), Ship = MapColor.Rgb(0x9CF0FF), Station = MapColor.Rgb(0xE8C25A),
        Burn = MapColor.Rgb(0xFF9F43), Coast = MapColor.Rgb(0x6FB4FF), Brake = MapColor.Rgb(0x7FE8A8),
        Border = MapColor.Rgb(0xFFE9A8), Warn = MapColor.Rgb(0xFF5A5A), Detour = MapColor.Rgb(0xC79BFF),
        Waypoint = MapColor.Rgb(0x7FE0E8), Hazard = MapColor.Rgb(0xFF4A3D), Track = MapColor.Rgb(0xFFFFFF),
        Plan = MapColor.Rgb(0x7F95AE), Fine = MapColor.Rgb(0xEEF4FB), Trim = MapColor.Rgb(0xE8C25A),
        ApPanel = MapColor.Rgb(0x0A1932, 238), ApText = MapColor.Rgb(0xEEF4FB), ApDim = MapColor.Rgb(0x8EA5C2),
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

    public static readonly MapTheme[] All = { Classic, Tachi, Unn, Opa };

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
