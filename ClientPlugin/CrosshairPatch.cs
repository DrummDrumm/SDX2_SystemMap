using System;
using HarmonyLib;
using Sandbox.Game.Gui;

namespace SDX2_SystemMap;

/// <summary>
/// Hides the game's crosshair in a seat (first or third person) when HideCrosshair is on: the side preview draws the
/// map theme's nose marker where the ship points instead.
/// </summary>
[HarmonyPatch(typeof(MyHudCrosshair), nameof(MyHudCrosshair.Draw))]
internal static class CrosshairPatch
{
    private static bool Prefix()
    {
        try { return !SidePreview.CrosshairHidden; }
        catch (Exception e) { Plugin.LogThrottled("crosshair hide failed: " + e.Message); return true; }
    }
}
