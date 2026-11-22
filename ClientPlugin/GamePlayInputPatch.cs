using System;
using HarmonyLib;

namespace SDX2_SystemMap;

/// <summary>
/// While the buttons under the N list have the controller (<see cref="SidePreview.PadInput"/>), the game screen's own
/// input is skipped: A, B, the D-pad and the sticks don't reach the ship, the character or the toolbar. The ship is
/// told to hold still (as the game does while its input is off).
/// </summary>
[HarmonyPatch(typeof(Sandbox.Game.Gui.MyGuiScreenGamePlay), nameof(Sandbox.Game.Gui.MyGuiScreenGamePlay.HandleUnhandledInput))]
internal static class GamePlayInputPatch
{
    private static bool Prefix()
    {
        try
        {
            if (!WorldGate.Active) return true;
            // LB / RB + Select is ours: the game's Select (the control panel) stays shut
            bool chord = GamePad.SelectShortcutHeld();
            if (!SidePreview.PadInput() && !chord) return true;
            Sandbox.Game.World.MySession.Static?.ControlledEntity?.MoveAndRotate(VRageMath.Vector3.Zero, VRageMath.Vector2.Zero, 0f);
            return false;
        }
        catch (Exception e) { Plugin.LogThrottled("controller input hold failed: " + e.Message); return true; }
    }
}
