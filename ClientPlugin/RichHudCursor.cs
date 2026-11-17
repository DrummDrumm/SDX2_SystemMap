using System;
using System.Reflection;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRageMath;
using VRageRender;

namespace SDX2_SystemMap;

/// <summary>
/// Rich HUD's mouse cursor (shown for F&amp;B's N list and other Rich HUD menus). Rich HUD draws as billboards, which
/// the game renders under 2D sprites, so its cursor would pass behind the map: while it shows, the preview takes the
/// mouse and draws a cursor of its own on top.
/// </summary>
internal static class RichHudCursor
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private const string Texture = "Textures\\GUI\\MouseCursor.dds";
    private static PropertyInfo cursorProp, drawProp;
    private static MethodInfo enableTemp;
    private static bool found;
    private static double nextSearch;

    /// <summary>Rich HUD's cursor is on screen (and the game's own isn't).</summary>
    public static bool Shown
    {
        get
        {
            if (!found) Search();
            if (!found) return false;
            try
            {
                if (MyAPIGateway.Gui?.IsCursorVisible ?? false) return false;
                var cursor = cursorProp.GetValue(null);
                if (cursor == null) return false;
                if (drawProp == null || drawProp.DeclaringType == null || !drawProp.DeclaringType.IsInstanceOfType(cursor))
                    drawProp = cursor.GetType().GetProperty("DrawCursor", Any);
                return drawProp != null && (bool)drawProp.GetValue(cursor);
            }
            catch (Exception e) { Plugin.LogThrottled("Rich HUD cursor check failed: " + e.Message); found = false; return false; }
        }
    }

    private static void Search()
    {
        double now = Plugin.Now;
        if (now < nextSearch || MyScriptManager.Static == null) return;
        nextSearch = now + 5;
        try
        {
            foreach (var asm in MyScriptManager.Static.Scripts.Values)
            {
                var main = asm.GetType("RichHudFramework.UI.Server.HudMain", false);
                var prop = main?.GetProperty("Cursor", Any);
                if (prop == null || !prop.GetGetMethod(true).IsStatic) continue;
                cursorProp = prop; drawProp = null; found = true;
                enableTemp = main.GetMethod("EnableCursorTemp", Any, null, Type.EmptyTypes, null);
                return;
            }
        }
        catch (Exception e) { Plugin.LogThrottled("Rich HUD search failed: " + e.Message); }
    }

    public static void Reset() { found = false; cursorProp = drawProp = null; enableTemp = null; nextSearch = 0; }

    /// <summary>
    /// Shows Rich HUD's cursor for this frame (call it every frame while wanted). Rich HUD then takes the mouse off the
    /// ship (no turning, so the autopilot isn't overridden) and the map takes it. False if Rich HUD isn't loaded.
    /// </summary>
    public static bool Request()
    {
        if (!found) Search();
        if (!found || enableTemp == null) return false;
        try { enableTemp.Invoke(null, null); return true; }
        catch (Exception e) { Plugin.LogThrottled("Rich HUD cursor request failed: " + e.Message); enableTemp = null; return false; }
    }

    /// <summary>A mouse cursor at (x, y) screen pixels, drawn over the map (the game's arrow, its tip at the point).</summary>
    public static void Draw(float x, float y, float screenH)
    {
        float s = 64f * screenH / 1080f;
        var r = new RectangleF(x - s / 2, y - s / 2, s, s);
        MyRenderProxy.DrawSprite(Texture, ref r, null, Color.White, 0, true, false);
    }
}
