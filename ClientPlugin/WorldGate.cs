using System;
using Sandbox.Game.World;

namespace SDX2_SystemMap;

/// <summary>
/// Keeps the plugin off in worlds without SDX2's Flip and Burn mod (workshop 2832248541): no keys, no drawing, no
/// pinned map, no toolbar actions and no speed-dial fix there. Only the settings window (Ctrl+Alt+/) still opens.
/// On when the world's mod list has Flip and Burn, or when its scripts turn up within the first minute (a local copy
/// under another id); after that the plugin stays off until the next world.
/// </summary>
internal static class WorldGate
{
    public const ulong FlipAndBurnId = 2832248541;
    private const double SearchSeconds = 60;

    private static bool inModList, begun;
    private static double startedAt = -1;

    /// <summary>True in a world with Flip and Burn.</summary>
    public static bool Active => startedAt >= 0 && (inModList || FlipAndBurnLink.Found);

    /// <summary>When a world has loaded (session BeforeStart).</summary>
    public static void Start(double now)
    {
        startedAt = now;
        begun = false;
        inModList = HasFlipAndBurn();
        if (inModList) Begin();
        else Plugin.Log("Flip and Burn isn't in this world's mods: the map stays off");
    }

    public static void Reset() { startedAt = -1; inModList = begun = false; }

    /// <summary>Every frame: true when the plugin may run. Looks for a renamed copy of F&amp;B for the first minute.</summary>
    public static bool Update(double now)
    {
        if (startedAt < 0) return false;
        if (!Active)
        {
            if (now - startedAt > SearchSeconds) return false;
            FlipAndBurnLink.Search(now);
            if (!Active) return false;
            Plugin.Log("Flip and Burn's scripts found: the map is on");
        }
        if (!begun) Begin();
        return true;
    }

    private static void Begin()
    {
        begun = true;
        ToolbarActions.Register();
        SidePreview.Start();
    }

    private static bool HasFlipAndBurn()
    {
        try
        {
            var mods = MySession.Static?.Mods;
            if (mods == null) return false;
            foreach (var m in mods)
                if (m.PublishedFileId == FlipAndBurnId) return true;
        }
        catch (Exception e) { Plugin.Log("can't read the world's mod list: " + e.Message); }
        return false;
    }
}
