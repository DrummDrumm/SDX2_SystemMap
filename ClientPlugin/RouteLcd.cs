using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI.Ingame;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRageMath;

namespace SDX2_SystemMap;

/// <summary>
/// "System Map: route" in an LCD's or a cockpit screen's Script list: your current route, framed to fit, seen straight
/// down onto the ecliptic with north (-Z) up and east (+X) right, N/E/S/W on the edges. It draws on this PC only
/// (the screen just stores the script's name); players without the plugin see a blank screen.
/// </summary>
[MyTextSurfaceScript(Id, "System Map: route")]
public sealed class RouteLcd : MyTSSCommon
{
    public const string Id = "SDX2Map_Route";

    private readonly MapScene scene = new MapScene { Compact = true, Mini = true, TitleText = "ROUTE  N UP" };
    private readonly SpriteCanvas canvas;
    private readonly List<V3> frame = new List<V3>();

    public override ScriptUpdate NeedsUpdate => ScriptUpdate.Update10;

    public RouteLcd(IMyTextSurface surface, IMyCubeBlock block, Vector2 size) : base(surface, block, size)
    {
        scene.Cam.TopDown = true;
        canvas = new SpriteCanvas(surface);
    }

    public override void Run()
    {
        base.Run();
        try
        {
            using (var f = m_surface.DrawFrame())
            {
                var off = (m_surface.TextureSize - m_surface.SurfaceSize) / 2;
                var box = new Box(off.X, off.Y, m_surface.SurfaceSize.X, m_surface.SurfaceSize.Y);
                canvas.Begin(f);
                Draw(box);
                canvas.End();
            }
        }
        catch (Exception e) { Plugin.LogThrottled("route screen failed: " + e.Message); }
    }

    private void Draw(Box box)
    {
        var snap = SidePreview.Snap;
        var path = SidePreview.Path;
        scene.Snap = snap; scene.Path = path; scene.Bends = SidePreview.Tracker.Bends; scene.Original = null;
        scene.Opt = Plugin.Settings.Options;
        scene.MouseX = scene.MouseY = float.NaN; scene.Help = "";
        // frame the map's part of the screen (the title bar takes the top): the route, the ship and its projected path
        scene.Cam.Setup(box.X, box.Y, box.W, box.H);
        frame.Clear();
        if (path != null) frame.AddRange(path.Points());
        if (snap.HasShip) frame.Add(snap.ShipPos);
        if (path != null) foreach (var q in snap.Prediction) frame.Add(q);
        if (path != null) scene.Cam.FrameFlat(frame, 20e3, 1.3);
        else if (snap.HasShip) scene.Cam.FrameFlat(frame, MapScene.ShipViewDistance);
        else { scene.Cam.GoalTarget = scene.Cam.Target = V3.Zero; scene.Cam.GoalDistance = scene.Cam.Distance = 6e7; }
        scene.Draw(canvas, box);
        Compass(box);
    }

    /// <summary>N / E / S / W at the middle of each edge (under the title bar for N).</summary>
    private void Compass(Box b)
    {
        var th = MapTheme.Current;
        float px = Math.Max(10f, Math.Min(b.W, b.H) / 22f), pad = px * 0.5f;
        float top = scene.TitleBottom + pad;
        void Mark(string t, float x, float y, TextAnchor a)
        {
            float w = canvas.TextWidth(t, px) + px * 0.5f;
            float bx = a == TextAnchor.Center ? x - w / 2 : a == TextAnchor.Right ? x - w : x;
            canvas.Rect(bx, y, w, px * 1.15f, th.PanelBg.WithAlpha(0.7f));
            canvas.Text(t, a == TextAnchor.Left ? x + px * 0.25f : a == TextAnchor.Right ? x - px * 0.25f : x, y + px * 0.05f, px, t == "N" ? th.Accent : th.Dim, a);
        }
        float midY = (top + b.Bottom) / 2 - px * 0.6f;
        Mark("N", b.X + b.W / 2, top, TextAnchor.Center);
        Mark("S", b.X + b.W / 2, b.Bottom - px * 1.15f - pad, TextAnchor.Center);
        Mark("W", b.X + pad, midY, TextAnchor.Left);
        Mark("E", b.Right - pad, midY, TextAnchor.Right);
    }
}

/// <summary>Draws the map onto an LCD's sprite frame (texture pixels).</summary>
internal sealed class SpriteCanvas : IMapCanvas
{
    private const string Font = "White";
    private readonly IMyTextSurface surface;
    private readonly StringBuilder sb = new StringBuilder();
    private readonly Stack<Rectangle> clips = new Stack<Rectangle>();
    private MySpriteDrawFrame frame;
    private float lineHeight;

    public SpriteCanvas(IMyTextSurface surface) { this.surface = surface; }

    public void Begin(MySpriteDrawFrame f) { frame = f; clips.Clear(); }

    public void End()
    {
        if (clips.Count > 0) { clips.Clear(); frame.Add(MySprite.CreateClearClipRect()); }
        frame = default; // the frame's sprite list goes back to the game's pool when it's disposed
    }

    private static Color Col(MapColor c) => new Color(c.R, c.G, c.B, c.A);

    public void Rect(float x, float y, float w, float h, MapColor col, float rotation = 0)
    {
        if (col.A == 0 || w <= 0 || h <= 0 || float.IsNaN(x + y + w + h)) return;
        frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(x + w / 2, y + h / 2), new Vector2(w, h), Col(col), null, TextAlignment.CENTER, rotation));
    }

    public void Line(float x1, float y1, float x2, float y2, float width, MapColor col)
    {
        float dx = x2 - x1, dy = y2 - y1, len = (float)Math.Sqrt(dx * dx + dy * dy);
        if (len < 0.5f || float.IsNaN(len)) return;
        Rect((x1 + x2) / 2 - len / 2, (y1 + y2) / 2 - width / 2, len, Math.Max(1f, width), col, (float)Math.Atan2(dy, dx));
    }

    public void Disc(float cx, float cy, float r, MapColor col)
    {
        if (col.A == 0 || r <= 0 || r > 20000 || float.IsNaN(cx + cy + r)) return;
        frame.Add(new MySprite(SpriteType.TEXTURE, "Circle", new Vector2(cx, cy), new Vector2(2 * r, 2 * r), Col(col)));
    }

    public void Text(string text, float x, float y, float px, MapColor col, TextAnchor anchor = TextAnchor.Left)
    {
        if (string.IsNullOrEmpty(text) || col.A == 0 || float.IsNaN(x + y)) return;
        if (text.IndexOf('→') >= 0) text = text.Replace("→", "->");
        var align = anchor == TextAnchor.Center ? TextAlignment.CENTER : anchor == TextAnchor.Right ? TextAlignment.RIGHT : TextAlignment.LEFT;
        frame.Add(new MySprite(SpriteType.TEXT, text, new Vector2(x, y), null, Col(col), Font, align, Scale(px)));
    }

    public float TextWidth(string text, float px)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        sb.Clear().Append(text.Replace("→", "->"));
        return surface.MeasureStringInPixels(sb, Font, Scale(px)).X;
    }

    public void PushClip(float x, float y, float w, float h)
    {
        var r = new Rectangle((int)Math.Floor(x), (int)Math.Floor(y), (int)Math.Ceiling(w), (int)Math.Ceiling(h));
        clips.Push(r);
        frame.Add(MySprite.CreateClipRect(r));
    }

    public void PopClip()
    {
        if (clips.Count == 0) return;
        clips.Pop();
        frame.Add(clips.Count > 0 ? MySprite.CreateClipRect(clips.Peek()) : MySprite.CreateClearClipRect());
    }

    /// <summary>Font scale that gives a line of px pixels.</summary>
    private float Scale(float px)
    {
        if (lineHeight <= 0)
        {
            sb.Clear().Append("Ag");
            lineHeight = surface.MeasureStringInPixels(sb, Font, 1f).Y;
            if (lineHeight <= 0) lineHeight = 28.8f;
        }
        return px / lineHeight;
    }
}

/// <summary>
/// The game fills its screen-script list when a world loads (from its own assemblies, the world's mods and the
/// game's plugin list, which doesn't include Pulsar's): the route screen is added to it after.
/// </summary>
[HarmonyPatch(typeof(MyTextSurfaceScriptFactory), nameof(MyTextSurfaceScriptFactory.LoadScripts))]
internal static class RouteLcdRegister
{
    private static void Postfix()
    {
        try { MyTextSurfaceScriptFactory.Instance?.RegisterFromAssembly(typeof(RouteLcd).Assembly); }
        catch (Exception e) { Plugin.Log("route screen not added: " + e.Message); }
    }
}
