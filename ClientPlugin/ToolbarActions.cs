using System;
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
        }
        catch (Exception e) { Plugin.Log("toolbar actions not added: " + e.Message); }
    }
}
