using System;
using System.Collections.Generic;
using VRage.Game.Definitions;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace SDX2_SystemMap;

/// <summary>Draws the map straight to the screen with the game's sprite renderer (screen pixels). Call during a Draw.</summary>
internal sealed class GameCanvas : IMapCanvas
{
    private const string Blank = "Textures\\GUI\\Blank.dds";
    private const string CircleTex = "Textures\\Sprites\\Circle.dds";
    private const string Font = "White";
    private static readonly Dictionary<string, float> lineHeights = new Dictionary<string, float>();
    private int clips;

    /// <summary>The sprite renderer expects premultiplied alpha.</summary>
    private static Color Pre(MapColor c) => new Color(c.R * c.A / 255, c.G * c.A / 255, c.B * c.A / 255, c.A);

    public void Rect(float x, float y, float w, float h, MapColor col, float rotation = 0)
    {
        if (col.A == 0 || w <= 0 || h <= 0) return;
        var r = new RectangleF(x, y, w, h);
        MyRenderProxy.DrawSprite(Blank, ref r, null, Pre(col), rotation, true, false);
    }

    public void Line(float x1, float y1, float x2, float y2, float width, MapColor col)
    {
        float dx = x2 - x1, dy = y2 - y1, len = (float)Math.Sqrt(dx * dx + dy * dy);
        if (len < 0.5f || float.IsNaN(len)) return;
        Rect((x1 + x2) / 2 - len / 2, (y1 + y2) / 2 - width / 2, len, width, col, (float)Math.Atan2(dy, dx));
    }

    public void Disc(float cx, float cy, float r, MapColor col)
    {
        if (col.A == 0 || r <= 0 || r > 20000 || float.IsNaN(r)) return;
        var rect = new RectangleF(cx - r, cy - r, 2 * r, 2 * r);
        MyRenderProxy.DrawSprite(CircleTex, ref rect, null, Pre(col), 0, true, false);
    }

    public void Text(string text, float x, float y, float px, MapColor col, TextAnchor anchor = TextAnchor.Left)
    {
        if (string.IsNullOrEmpty(text) || col.A == 0) return;
        float scale = Scale(px);
        if (anchor != TextAnchor.Left)
        {
            float w = MyFontDefinition.MeasureStringRaw(Font, text, scale).X;
            x -= anchor == TextAnchor.Center ? w / 2 : w;
        }
        int font = (int)MyStringHash.GetOrCompute(Font);
        var at = new Vector2((float)Math.Round(x), (float)Math.Round(y));
        MyRenderProxy.DrawString(font, at + Vector2.One, new Color(0, 0, 0, (int)(col.A * 0.7f)), text, scale, float.PositiveInfinity, true);
        MyRenderProxy.DrawString(font, at, Pre(col), text, scale, float.PositiveInfinity, true);
    }

    public float TextWidth(string text, float px) => string.IsNullOrEmpty(text) ? 0 : MyFontDefinition.MeasureStringRaw(Font, text, Scale(px)).X;

    public void PushClip(float x, float y, float w, float h)
    {
        MyRenderProxy.SpriteScissorPush(new Rectangle((int)Math.Floor(x), (int)Math.Floor(y), (int)Math.Ceiling(w), (int)Math.Ceiling(h)));
        clips++;
    }

    public void PopClip()
    {
        if (clips <= 0) return;
        MyRenderProxy.SpriteScissorPop();
        clips--;
    }

    /// <summary>Font scale that gives a line of px pixels.</summary>
    private static float Scale(float px)
    {
        if (!lineHeights.TryGetValue(Font, out var lh))
        {
            lh = MyFontDefinition.MeasureStringRaw(Font, "Ag", 1f).Y;
            if (lh <= 0) lh = 30f;
            lineHeights[Font] = lh;
        }
        return px / lh;
    }
}
