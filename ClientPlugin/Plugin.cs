using System;
using System.Diagnostics;
using System.IO;
using HarmonyLib;
using Sandbox.Game.Gui;
using VRage.FileSystem;
using VRage.Game.Components;
using VRage.Plugins;
using VRage.Utils;

namespace SDX2_SystemMap;

// ReSharper disable once UnusedType.Global
public class Plugin : IPlugin
{
    private const string Id = "SDX2_SystemMap";
    private static readonly Stopwatch clock = Stopwatch.StartNew();
    private static DateTime nextThrottled;
    private static Harmony harmony;
    /// <summary>For patches made once a mod has loaded (Flip and Burn's N list).</summary>
    public static Harmony Harmony => harmony;

    public static double Now => clock.Elapsed.TotalSeconds;
    public static MapSettings Settings { get; private set; } = new MapSettings();
    private static string SettingsPath => Path.Combine(MyFileSystem.UserDataPath ?? "", "Storage", "SDX2_SystemMap.ini");

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public void Init(object gameInstance)
    {
        Settings = MapSettings.Load(SettingsPath);
        MapTheme.Current = MapTheme.Find(Settings.Theme);
        SidePreview.ApplySettings(Settings);
        try
        {
            harmony = new Harmony(Id);
            harmony.PatchAll(typeof(Plugin).Assembly);
        }
        catch (Exception e) { Log("patching failed, the map won't draw: " + e); }
    }

    public void Update()
    {
        try
        {
            if (Sandbox.Game.World.MySession.Static == null) return;
            SettingsScreen.CheckHotkey();
            if (!WorldGate.Update(Now)) return; // not an SDX2 world: stay off
            PlayGuard.Update(Now);
            if (PlayGuard.ViewOnly) { SidePreview.ViewUpdate(Now); SecondScreen.Update(Now); return; } // AFK: the map and pinned preview, read-only
            if (PlayGuard.Blocked) { SidePreview.Pause(); SecondScreen.Update(Now); return; }
            FlipAndBurnLink.Search(Now);
            SidePreview.Update(Now);
            SecondScreen.Update(Now);
        }
        catch (Exception e) { LogThrottled("update failed: " + e); }
    }

    public void Dispose()
    {
        try { SecondScreen.Reset(); } catch { }
        try { harmony?.UnpatchAll(Id); } catch { }
    }

    /// <summary>Pulsar's "Config" button opens the settings window (Ctrl+Alt+/ in game does too).</summary>
    public void OpenConfigDialog() => SettingsScreen.Open();

    public static void SaveSettings()
    {
        try { Settings.Save(SettingsPath); } catch (Exception e) { Log("can't save settings: " + e.Message); }
    }

    public static void Log(string msg)
    {
        try { MyLog.Default?.WriteLineAndConsole(Id + ": " + msg); } catch { }
    }

    public static void LogThrottled(string msg)
    {
        if (DateTime.UtcNow < nextThrottled) return;
        nextThrottled = DateTime.UtcNow.AddSeconds(10);
        Log(msg);
    }
}

/// <summary>Draws the side preview after the game screen, on the main thread (session components draw on a worker task).</summary>
[HarmonyPatch(typeof(MyGuiScreenGamePlay), nameof(MyGuiScreenGamePlay.Draw))]
internal static class GamePlayDrawPatch
{
    private static void Postfix(bool __result)
    {
        if (!__result || !WorldGate.Active) return;
        try { SidePreview.Draw(); } catch (Exception e) { Plugin.LogThrottled("preview draw failed: " + e); }
    }
}

/// <summary>Connects to Flip and Burn's API when a world loads and forgets it when the world closes.</summary>
[MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
public class SystemMapSession : MySessionComponentBase
{
    public override void BeforeStart()
    {
        FlipAndBurnLink.Load();
        PlayGuard.Reset();
        WorldGate.Start(Plugin.Now); // toolbar actions and the pinned map only in a world with Flip and Burn
    }

    protected override void UnloadData()
    {
        FlipAndBurnLink.Unload();
        WorldGate.Reset();
        SpectrumLink.Reset();
        SidePreview.Reset();
        SecondScreen.Reset();
        RichHudCursor.Reset();
        Plugin.SaveSettings();
        base.UnloadData();
    }
}
