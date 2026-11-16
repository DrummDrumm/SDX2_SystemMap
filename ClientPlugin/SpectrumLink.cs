using System;
using System.Reflection;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using VRage.Game.Components;

namespace SDX2_SystemMap;

/// <summary>
/// Read-only link to SDX2's Spectrum mod (workshop 3580536771): the figures its HUD shows for your own ship
/// (Session.ClientSelfEmission, sent by the server about once a second while you control the grid).
/// </summary>
internal static class SpectrumLink
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static bool found, failed;
    private static double nextSearch;
    private static MySessionComponentBase session;
    private static FieldInfo selfField, gridField, sphField, dirField;

    public static void Reset() { found = false; failed = false; nextSearch = 0; session = null; }

    public static void Search(double now)
    {
        if (found || failed || now < nextSearch || MyScriptManager.Static == null || MySession.Static == null) return;
        nextSearch = now + 5;
        try
        {
            foreach (var asm in MyScriptManager.Static.Scripts.Values)
            {
                var t = asm.GetType("Spectrum.Session", false);
                if (t == null || !typeof(MySessionComponentBase).IsAssignableFrom(t)) continue;
                selfField = t.GetField("ClientSelfEmission", Any);
                gridField = t.GetField("ClientSelfEmissionGridId", Any);
                if (selfField == null || gridField == null) continue;
                sphField = selfField.FieldType.GetField("SphericalStrength", Any);
                dirField = selfField.FieldType.GetField("DirectionalStrength", Any);
                var get = typeof(MySession).GetMethod("GetComponent", Type.EmptyTypes)?.MakeGenericMethod(t);
                session = get?.Invoke(MySession.Static, null) as MySessionComponentBase;
                if (session == null || sphField == null || dirField == null) continue;
                found = true;
                Plugin.Log("Spectrum found in " + asm.GetName().Name + ": the route panel shows its live signature");
                return;
            }
        }
        catch (Exception e) { failed = true; Plugin.Log("Spectrum link failed: " + (e.InnerException ?? e).Message); }
    }

    /// <summary>Spectrum's HUD figures for this grid now (strengths), or -1 each when unknown.</summary>
    public static void ReadLive(MyCubeGrid grid, SignatureInfo sig)
    {
        sig.LiveAllRound = sig.LiveCone = -1;
        if (!found || grid == null) return;
        try
        {
            if ((long)gridField.GetValue(session) != grid.EntityId) return;
            var self = selfField.GetValue(session);
            sig.LiveAllRound = (double)sphField.GetValue(self);
            sig.LiveCone = (double)dirField.GetValue(self);
        }
        catch { }
    }
}
