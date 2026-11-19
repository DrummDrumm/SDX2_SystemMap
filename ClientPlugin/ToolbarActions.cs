using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;

namespace SDX2_SystemMap;

/// <summary>
/// Toolbar actions on every cockpit, seat and remote control (drag them from the G menu onto the ship's toolbar):
///   "System Map: pin preview on/off" (picture-in-picture), "System Map: full map" and "System Map: stop autopilot".
/// They run only on this client; the server just stores the toolbar slot. Registered each world load (the game
/// clears terminal actions when a world closes).
/// </summary>
internal static class ToolbarActions
{
    private const string Icon = @"Textures\GUI\Icons\Actions\Toggle.dds";

    public static void Register()
    {
        Forget();
        try
        {
            var tc = MyAPIGateway.TerminalControls;
            if (tc == null) return;
            var pip = tc.CreateAction<IMyShipController>("SDX2Map_Pip");
            pip.Name = new StringBuilder("System Map: pin preview on/off");
            pip.Icon = Icon;
            pip.ValidForGroups = false;
            pip.Action = b => SidePreview.Notify(SidePreview.TogglePip(), true);
            pip.Writer = (b, sb) => sb.Append(SidePreview.PinnedNow && SidePreview.Visible ? "Map On" : "Map Off");
            tc.AddAction<IMyShipController>(pip);

            var map = tc.CreateAction<IMyShipController>("SDX2Map_Full");
            map.Name = new StringBuilder("System Map: full map");
            map.Icon = @"Textures\GUI\Icons\Actions\SwitchOn.dds";
            map.ValidForGroups = false;
            map.Action = b => MapScreen.Open(); // says "paused" itself in admin mode
            map.Writer = (b, sb) => sb.Append("Map");
            tc.AddAction<IMyShipController>(map);

            var stop = tc.CreateAction<IMyShipController>("SDX2Map_StopAutopilot");
            stop.Name = new StringBuilder("System Map: stop autopilot");
            stop.Icon = @"Textures\GUI\Icons\Actions\SwitchOff.dds";
            stop.ValidForGroups = false;
            stop.Action = b => SidePreview.HotbarStop(); // at once, no pop-up: you have the controls
            stop.Writer = (b, sb) => sb.Append("Stop AP");
            tc.AddAction<IMyShipController>(stop);

            var full = tc.CreateAction<IMyShipController>("SDX2Map_FullStop");
            full.Name = new StringBuilder("System Map: full stop");
            full.Icon = @"Textures\GUI\Icons\Actions\SwitchOff.dds";
            full.ValidForGroups = false;
            full.Action = b => SidePreview.FullStop(); // brakes to a standstill, then dampeners on and hands back
            full.Writer = (b, sb) => sb.Append("Full stop");
            tc.AddAction<IMyShipController>(full);

            var clear = tc.CreateAction<IMyShipController>("SDX2Map_ClearRoute");
            clear.Name = new StringBuilder("System Map: clear route");
            clear.Icon = @"Textures\GUI\Icons\Actions\Reset.dds";
            clear.ValidForGroups = false;
            clear.Action = b => SidePreview.Notify(SidePreview.ClearRoute()); // the route stays until cleared or flown
            clear.Writer = (b, sb) => sb.Append("Clear route");
            tc.AddAction<IMyShipController>(clear);

            // on drives (one, or a group): set the thrust override to the route's burn now
            var burn = tc.CreateAction<IMyThrust>("SDX2Map_MatchBurn");
            burn.Name = new StringBuilder("System Map: match route burn");
            burn.Icon = @"Textures\GUI\Icons\Actions\Increase.dds";
            burn.ValidForGroups = true;
            burn.Enabled = b => !(b?.BlockDefinition.SubtypeId ?? "").StartsWith("sdx_thrusterRCS", StringComparison.OrdinalIgnoreCase);
            burn.Action = b => MatchBurn(b as IMyThrust);
            burn.Writer = (b, sb) =>
            {
                double p = SidePreview.RouteBurnPct(out var ph, out _);
                sb.Append(double.IsNaN(p) ? "No route" : ph == BurnGuide.Phase.Coast ? "Coast 0%"
                    : (ph == BurnGuide.Phase.Brake ? "Brake " : "Burn ") + Math.Min(100, Math.Ceiling(p)).ToString("0") + "%");
            };
            tc.AddAction<IMyThrust>(burn);
        }
        catch (Exception e) { Plugin.Log("toolbar actions not added: " + e.Message); }
    }

    private static double lastBurnNote;
    private static readonly List<KeyValuePair<IMyThrust, float>> held = new List<KeyValuePair<IMyThrust, float>>(); // drives this set, and to what

    /// <summary>
    /// Each update: the overrides "match route burn" set are switched off once there is no route to fly by hand
    /// (arrived, cleared) or you have left the ship. Left on, the drives kept burning with nothing to stop them: a
    /// save made that way loaded at full thrust towards Mars (2026-11-19). A drive whose override the pilot has
    /// changed since is left alone; when the autopilot takes the route the drives are its own.
    /// </summary>
    public static void ReleaseBurn(bool hasRoute, bool hasShip, bool engaged)
    {
        if (held.Count == 0) return;
        if (engaged) { held.Clear(); return; }
        if (hasRoute && hasShip) return;
        int n = 0;
        foreach (var h in held)
        {
            try
            {
                var d = h.Key;
                if (d == null || d.Closed || Math.Abs(d.ThrustOverridePercentage - h.Value) > 0.005f) continue;
                d.ThrustOverridePercentage = 0; n++;
            }
            catch { }
        }
        held.Clear();
        if (n == 0) return;
        string note = "Drives: override off (" + (hasRoute ? "you left the ship" : "route ended") + ")";
        SidePreview.Notify(note); Plugin.Log("match route burn: " + note + ", " + n + " drives");
    }

    /// <summary>A world closing: the drives are gone.</summary>
    public static void Forget() => held.Clear();

    /// <summary>
    /// "System Map: match route burn" on a drive: its thrust override set to the burn guide's share (BURN at the plan's
    /// thrust, BRAKE at what stops on the point, capped at 100%; coasting turns the override off). A group runs it on
    /// each drive; the note shows once.
    /// </summary>
    private static void MatchBurn(IMyThrust drive)
    {
        if (drive == null) return;
        double pct = SidePreview.RouteBurnPct(out var phase, out string why);
        string note;
        if (double.IsNaN(pct)) note = why;
        else
        {
            float f = (float)Math.Max(0, Math.Min(1, pct / 100));
            drive.ThrustOverridePercentage = f;
            held.RemoveAll(h => h.Key == drive);
            if (f > 0) held.Add(new KeyValuePair<IMyThrust, float>(drive, f));
            note = phase == BurnGuide.Phase.Coast ? "Drives: override off (coasting)"
                : (phase == BurnGuide.Phase.Brake ? "Drives: BRAKE " : "Drives: BURN ") + (f * 100).ToString("0") + "%"
                  + (pct > 100.5 ? " (needs " + pct.ToString("0") + "%: you'll overshoot)" : "")
                  + (phase == BurnGuide.Phase.Brake ? ", nose on the BRAKE mark" : ", nose on the BURN mark");
        }
        if (Plugin.Now - lastBurnNote > 0.5) { lastBurnNote = Plugin.Now; SidePreview.Notify(note, !double.IsNaN(pct)); Plugin.Log("match route burn: " + note); }
    }
}
