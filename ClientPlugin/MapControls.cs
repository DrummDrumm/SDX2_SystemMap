using System;

namespace SDX2_SystemMap;

/// <summary>
/// The autopilot controls over a full map (the game's full map and the second screen): the card in the route panel's
/// top slot, or the route editor on the other side.
/// </summary>
internal static class MapControls
{
    /// <summary>Before scene.Draw: whether the controls show, and the room the card needs in the route panel.</summary>
    public static bool Prepare(MapScene scene, bool showControls, double now, out bool card)
    {
        bool flying = AutopilotLink.Engaged(now), braking = AutopilotLink.Braking(now);
        bool wanted = braking || (showControls || flying) && SidePreview.Path != null;
        card = wanted && (braking || !SidePreview.MapEditorOpen);
        bool resume = SidePreview.CanResume(now) && !braking;
        scene.PanelOnRight = Plugin.Settings.PanelRight;
        scene.ControlsHeight = card ? p => AutopilotPanel.MapCardHeight(p, resume) : null;
        return wanted;
    }

    /// <summary>After scene.Draw (and ui.Input): draws them and returns where they are; closeMap = MANUAL was picked.</summary>
    public static Box Draw(PanelUi ui, MapScene scene, Box box, bool card, double now, out bool closeMap)
    {
        closeMap = false;
        float cpx = Math.Max(14f, box.H / 52f);
        if (card)
        {
            bool resume = SidePreview.CanResume(now) && !AutopilotLink.Braking(now);
            var slot = scene.ControlsSlot;
            float spx = scene.PanelPx > 0 ? scene.PanelPx : cpx;
            // no route panel to sit in (compact view): stand alone at the panel's side
            if (slot.W <= 0)
            {
                float w = cpx * 22, h = AutopilotPanel.MapCardHeight(cpx, resume);
                slot = new Box(box.X + (scene.PanelOnRight ? box.W - cpx - w : cpx), box.Y + box.H * 0.08f, w, h);
                spx = cpx;
            }
            SidePreview.MapCard(ui, slot, spx, out closeMap);
            return slot;
        }
        float ex = box.X + (scene.PanelOnRight ? cpx : box.W - cpx * 31.2f);
        return SidePreview.MapEditor(ui, ex, box.Y + box.H * 0.06f, box.H, cpx);
    }
}
