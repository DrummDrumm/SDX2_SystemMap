using System;

namespace SDX2_SystemMap;

/// <summary>
/// How a map point is matched to one of Flip and Burn's list entries (game-free so it can be tested). Lower rank is
/// better; the nearest of the best rank wins.
///   0: the same name, or the alternative name (a station's GPS name such as JU.Ganymede.STN), within 1000 km;
///   1: the entry's name starts with the name ("Mars-12345d..." is how the game names the planet Mars), within 1000 km;
///   2: the entry's name holds the point's key word ("Ganymede" for "Ganymede Station"), within 1000 km;
///   3: anything within 5 km;
///   NoMatch otherwise.
/// </summary>
public static class PoiMatch
{
    public const int NoMatch = 9;
    public const double NameRange = 1e6, NearRange = 5000;

    public static int Rank(string name, string altName, string entryName, double distance)
    {
        string pn = entryName ?? "";
        if (distance < NameRange)
        {
            if (Same(pn, name) || altName != null && Same(pn, altName)) return 0;
            if (StartsWord(pn, name) || altName != null && StartsWord(pn, altName)) return 1;
            string key = KeyWord(name);
            if (key != null && pn.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0) return 2;
        }
        return distance < NearRange ? 3 : NoMatch;
    }

    private static bool Same(string a, string b) => !string.IsNullOrEmpty(b) && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>a starts with b followed by a non-letter ("Mars-1234" starts with "Mars"; "Marshall" doesn't).</summary>
    private static bool StartsWord(string a, string b)
    {
        if (string.IsNullOrEmpty(b) || a.Length <= b.Length || !a.StartsWith(b, StringComparison.OrdinalIgnoreCase)) return false;
        return !char.IsLetter(a[b.Length]);
    }

    /// <summary>The distinctive word of a name: "Ganymede Station" → Ganymede, "Io Helium Refinery" → Helium (skips short and generic words).</summary>
    public static string KeyWord(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        string best = null;
        foreach (var w in name.Split(new[] { ' ', '.', '-', '_' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var l = w.ToLowerInvariant();
            if (l.Length < 4 || l == "station" || l == "stn" || l == "base" || l == "outpost" || l == "the" || l == "belt") continue;
            if (best == null || w.Length > best.Length) best = w;
        }
        return best;
    }
}
