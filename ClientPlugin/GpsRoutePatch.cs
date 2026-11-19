using System;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Sandbox.Game.Screens.Helpers;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using VRageMath;

namespace SDX2_SystemMap;

/// <summary>
/// The terminal's GPS tab: a "Send to route planner" button beside "Copy to clipboard" that makes the selected GPS
/// the route's destination (as Shift+click on it in the full map does).
/// </summary>
[HarmonyPatch]
internal static class GpsRoutePatch
{
    private const string ButtonName = "sdx2SendToRoute";

    private static MethodBase TargetMethod() => AccessTools.Method("Sandbox.Game.Screens.Terminal.MyTerminalGpsController:Init");

    private static void Postfix(IMyGuiControlsParent controlsParent)
    {
        try
        {
            var controls = controlsParent?.Controls;
            if (controls == null || controls.GetControlByName(ButtonName) != null) return;
            if (!(controls.GetControlByName("buttonToClipboard") is MyGuiControlButton copy)) return;
            var list = controls.GetControlByName("ListboxGps") as MyGuiControlListbox;
            var button = new MyGuiControlButton(copy.Position - new Vector2(copy.Size.X + 0.008f, 0), copy.VisualStyle, copy.Size,
                text: new StringBuilder("Send to route planner"), textScale: copy.TextScale, originAlign: copy.OriginAlign,
                toolTip: "Set the selected GPS as the route's destination (same as Shift+click in the full map).",
                onButtonClick: b => Send(list))
                { Name = ButtonName, ShowTooltipWhenDisabled = true };
            controls.Add(button);
        }
        catch (Exception e) { Plugin.LogThrottled("GPS route button failed: " + e.Message); }
    }

    private static void Send(MyGuiControlListbox list)
    {
        string msg;
        try
        {
            var gps = list != null && list.SelectedItems.Count == 1 ? list.SelectedItems[0].UserData as MyGps : null;
            if (gps == null) msg = "Select one GPS first.";
            else
            {
                var p = gps.Coords;
                msg = SidePreview.SetDestination(gps.Name, "GPS", new V3(p.X, p.Y, p.Z));
                Plugin.Log("GPS sent to the route planner: " + gps.Name + " -> " + msg);
            }
        }
        catch (Exception e) { msg = "Couldn't send the GPS: " + e.Message; Plugin.LogThrottled("GPS route send failed: " + e.Message); }
        MyAPIGateway.Utilities?.ShowNotification(msg, 4000);
    }
}
