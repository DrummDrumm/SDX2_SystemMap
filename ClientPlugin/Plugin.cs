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
    private static string GlobalPath => Path.Combine(MyFileSystem.UserDataPath ?? "", "Storage", "SDX2_SystemMap.ini");
    /// <summary>Each ship keeps its own settings, in a file named after it; off: one set for all ships.</summary>
    public static bool PerShip { get; private set; } = true;
    /// <summary>The ship whose settings are in use (null: the set for all ships, which a ship with no file of its own starts from).</summary>
    public static string SettingsShip { get; private set; }
    private static double nextShipCheck;
    private static string SettingsPath => PerShip && SettingsShip != null ? ShipPath(SettingsShip) : GlobalPath;

    private static string ShipPath(string ship)
    {
        var sb = new System.Text.StringBuilder();
        var bad = Path.GetInvalidFileNameChars();
        foreach (char ch in ship.Trim()) sb.Append(Array.IndexOf(bad, ch) >= 0 || ch == '.' ? '_' : ch);
        string name = sb.Length == 0 ? "ship" : sb.Length > 80 ? sb.ToString(0, 80) : sb.ToString();
        return Path.Combine(MyFileSystem.UserDataPath ?? "", "Storage", "SDX2_SystemMap_Ships", name + ".ini");
    }

    /// <summary>Once a second: seated in a ship other than the one whose settings are in use, take up that ship's.</summary>
    private static void CheckShip()
    {
        if (!PerShip || Now < nextShipCheck) return;
        nextShipCheck = Now + 1;
        var seat = Sandbox.Game.World.MySession.Static?.ControlledEntity as Sandbox.Game.Entities.MyShipController;
        string name = seat?.CubeGrid?.DisplayName;
        if (string.IsNullOrWhiteSpace(name) || name == SettingsShip) return;
        SaveSettings();
        var was = Settings;
        SettingsShip = name;
        string path = ShipPath(name);
        bool own = File.Exists(path);
        var s = MapSettings.Load(own ? path : GlobalPath);
        s.PerShip = true; s.PreviewFollow = was.PreviewFollow;
        Settings = s;
        s.ApplyLook();
        SidePreview.SettingsSwapped();
        SettingsScreen.Refresh();
        Log("settings: " + (own ? "this ship's own" : "a new ship, starting from the set for all ships") + " (" + name + ")");
    }

    /// <summary>
    /// Each ship its own settings, or one set for all. Turning it off makes the settings in use the set for all ships
    /// (the ships' files stay on disk); turning it on brings back the seated ship's own when it has a file.
    /// </summary>
    public static void SetPerShip(bool on)
    {
        if (on == PerShip) return;
        if (on)
        {
            Settings.PerShip = true; SaveSettings(); // still the set for all ships: the switch is kept there
            PerShip = true; SettingsShip = null; nextShipCheck = 0;
            CheckShip();
        }
        else
        {
            PerShip = false; SettingsShip = null; Settings.PerShip = false;
            SaveSettings();
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public void Init(object gameInstance)
    {
        Settings = MapSettings.Load(GlobalPath);
        PerShip = Settings.PerShip;
        Settings.ApplyLook();
        SidePreview.ApplySettings(Settings);
        try
        {
            harmony = new Harmony(Id);
            harmony.PatchAll(typeof(Plugin).Assembly);
            TriggerFix.Apply(harmony);
            DestinationView.Init();
        }
        catch (Exception e) { Log("patching failed, the map won't draw: " + e); }
    }

    public void Update()
    {
        try
        {
            ControllerGuard.Hook();
            if (Sandbox.Game.World.MySession.Static == null) return;
            CheckShip();
            SettingsScreen.CheckHotkey();
            DestinationView.Tick();
            if (!WorldGate.Update(Now)) return; // not an SDX2 world: stay off
            PlayGuard.Update(Now);
            GamePad.Poll(Now);
            if (PlayGuard.ViewOnly) { SidePreview.ViewUpdate(Now); SecondScreen.Update(Now); return; } // AFK: everything stays on show and current, read-only
            if (PlayGuard.Blocked) { SidePreview.Pause(); SecondScreen.Update(Now); return; }
            FlipAndBurnLink.Search(Now);
            Perf.BeginUpdate();
            SidePreview.Update(Now);
            Perf.EndUpdate();
            SecondScreen.Update(Now);
        }
        catch (Exception e) { LogThrottled("update failed: " + e); }
    }

    public void Dispose()
    {
        try { SecondScreen.Reset(); } catch { }
        try { DestinationView.Dispose(); } catch { }
        try { harmony?.UnpatchAll(Id); } catch { }
    }

    /// <summary>Pulsar's "Config" button and its "Configure a plugin" list (Ctrl+Alt+/ in game) open the settings window.</summary>
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
        if (Perf.BeginDraw()) // (a debugging tool: off unless its file exists)
        {
            try { SidePreview.Draw(); } catch (Exception e) { Plugin.LogThrottled("preview draw failed: " + e); }
        }
        Perf.EndDraw();
        DestinationView.Draw(SidePreview.InGameScreen);
    }
}

/// <summary>Esc, or B on a controller, closes our panels first (the N list, the Route Plan, the map window); only with none up does it reach the pause menu.</summary>
[HarmonyPatch(typeof(MyGuiScreenGamePlay), nameof(MyGuiScreenGamePlay.HandleUnhandledInput))]
internal static class GamePlayEscapePatch
{
    private static bool Prefix()
    {
        try
        {
            var input = VRage.Input.MyInput.Static;
            // this frame's input is used up by the close (the rest of the method is the pause menu and game keys)
            if (input != null && (input.IsNewKeyPressed(VRage.Input.MyKeys.Escape) || GamePad.BackPressed) && SidePreview.EscapeClose()) return false;
        }
        catch (Exception e) { Plugin.LogThrottled("esc close failed: " + e); }
        return true;
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
        SystemData.SetKnownStations(null);
        WorldGate.Reset();
        Perf.Reset();
        SpectrumLink.Reset();
        SidePreview.Reset();
        OwnShipIcon.Reset();
        DestinationView.Reset();
        SecondScreen.Reset();
        RichHudCursor.Reset();
        Plugin.SaveSettings();
        base.UnloadData();
    }
}
