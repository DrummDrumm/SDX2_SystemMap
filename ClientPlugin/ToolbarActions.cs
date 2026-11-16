using System;
using System.Text;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;

namespace SDX2_SystemMap;

/// <summary>
/// Toolbar actions on every cockpit, seat and remote control (drag them from the G menu onto the ship's toolbar):
///   "System Map: pin preview on/off" (picture-in-picture) and "System Map: full map".
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
            pip.Writer = (b, sb) => sb.Append(Plugin.Settings.Pip && SidePreview.Visible ? "Map On" : "Map Off");
            tc.AddAction<IMyShipController>(pip);

            var map = tc.CreateAction<IMyShipController>("SDX2Map_Full");
            map.Name = new StringBuilder("System Map: full map");
            map.Icon = @"Textures\GUI\Icons\Actions\SwitchOn.dds";
            map.ValidForGroups = false;
            map.Action = b => MapScreen.Open(); // says "paused" itself in admin mode
            map.Writer = (b, sb) => sb.Append("Map");
            tc.AddAction<IMyShipController>(map);
        }
        catch (Exception e) { Plugin.Log("toolbar actions not added: " + e.Message); }
    }
}
