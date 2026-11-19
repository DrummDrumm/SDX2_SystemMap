using System;
using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.GUI;
using Sandbox.Game.World;

namespace SDX2_SystemMap;

/// <summary>
/// Steadies the HUD speed dial at Flip and Burn speeds. F&amp;B clamps the ship's physics speed to the game's limit
/// (150 m/s) during each simulation tick and puts its real speed back a tick later only to fool the speedometer, so
/// a frame drawn in between shows 150 m/s: the dial flickers. This shows F&amp;B's own velocity instead whenever it's
/// higher than the physics speed. Off with FixSpeedometer=false.
/// </summary>
[HarmonyPatch(typeof(MyStatControlledEntitySpeed), nameof(MyStatControlledEntitySpeed.Update))]
internal static class SpeedometerPatch
{
    private static void Postfix(ref float ___m_currentValue, ref string ___m_valueStringCache)
    {
        try
        {
            if (!Plugin.Settings.FixSpeedometer || (PlayGuard.Blocked && !PlayGuard.ViewOnly) || !WorldGate.Active) return;
            if (!(MySession.Static?.ControlledEntity is MyShipController sc) || sc.CubeGrid == null) return;
            float real = (float)FlipAndBurnLink.Velocity(sc.CubeGrid).Length();
            if (real > ___m_currentValue + 0.5f && !float.IsNaN(real) && !float.IsInfinity(real))
            {
                ___m_currentValue = real;
                ___m_valueStringCache = null;
            }
        }
        catch (Exception e) { Plugin.LogThrottled("speedometer fix failed: " + e.Message); }
    }
}
