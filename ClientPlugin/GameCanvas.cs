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

    /// <summary>Sprites and strings sent to the game since this was last set to 0 (the frame-cost measurement).</summary>
    public static int Sprites;

    /// <summary>Ultra drawing quality: soft-edged lines and circles, level and upright shapes on whole pixels.</summary>
    public static bool Ultra;

    /// <summary>The sprite renderer expects premultiplied alpha.</summary>
    private static Color Pre(MapColor c) => new Color(c.R * c.A / 255, c.G * c.A / 255, c.B * c.A / 255, c.A);

    public void Rect(float x, float y, float w, float h, MapColor col, float rotation = 0)
    {
        if (col.A == 0 || w <= 0 || h <= 0) return;
        if (Ultra && rotation == 0)
        {
            // on whole pixels: an edge between two pixels is drawn as a blur across both
            float x1 = (float)Math.Round(x), y1 = (float)Math.Round(y);
            w = Math.Max(1, (float)Math.Round(x + w) - x1); h = Math.Max(1, (float)Math.Round(y + h) - y1);
            x = x1; y = y1;
        }
        var r = new RectangleF(x, y, w, h);
        Sprites++;
        MyRenderProxy.DrawSprite(Blank, ref r, null, Pre(col), rotation, true, false);
    }

    public void Line(float x1, float y1, float x2, float y2, float width, MapColor col)
    {
        float dx = x2 - x1, dy = y2 - y1, len = (float)Math.Sqrt(dx * dx + dy * dy);
        if (len < 0.5f || float.IsNaN(len)) return;
        if (Ultra && col.A != 0 && width > 0)
        {
            // level and upright lines: a whole number of pixels thick, on whole pixels
            float t = Math.Max(1, (float)Math.Round(width));
            if (Math.Abs(dy) < 0.01f) { Rect(Math.Min(x1, x2), (float)Math.Round((y1 + y2) / 2 - t / 2), len, t, col); return; }
            if (Math.Abs(dx) < 0.01f) { Rect((float)Math.Round((x1 + x2) / 2 - t / 2), Math.Min(y1, y2), t, len, col); return; }
            // any other: a soft-edged strip, so its sides don't stair-step
            if (SoftLine(x1, y1, dx, dy, len, width, col)) return;
        }
        Rect((x1 + x2) / 2 - len / 2, (y1 + y2) / 2 - width / 2, len, width, col, (float)Math.Atan2(dy, dx));
    }

    /// <summary>
    /// A slanted line, placed exactly. The renderer keeps a sprite's position, size and turning point as 16-bit
    /// floats: past 1024 pixels from the screen's corner they hold whole pixels only (past 2048, even ones), so a
    /// sprite put where the line is lands up to half a pixel off, each piece of a line by a different amount, and the
    /// pieces don't meet. Here the sprite is one the renderer can hold exactly: centred on a whole pixel, a whole even
    /// number of pixels long and thick, and a little larger than the line. Where the line lies inside it is set by the
    /// part of the texture the sprite shows; outside that part the texture is clear.
    /// </summary>
    private static bool SoftLine(float x1, float y1, float dx, float dy, float len, float width, MapColor col)
    {
        string tex = UltraTextures.Line(width, out float lw);
        if (tex == null) return false;
        if (len > 256)
        {
            // in pieces: the longer the sprite, the less exactly the renderer holds where its texture starts and ends
            int n = (int)Math.Ceiling(len / 256);
            for (int i = 0; i < n; i++) SoftLine(x1 + dx * i / n, y1 + dy * i / n, dx / n, dy / n, len / n, width, col);
            return true;
        }
        float tx = dx / len, ty = dy / len, nx = -ty, ny = tx, h = lw + 1;
        float mx = x1 + dx / 2, my = y1 + dy / 2;
        for (int g = 1; g <= 2; g++) // the grid the renderer holds exactly: whole pixels, or even ones farther out
        {
            float ox = (float)Math.Round(mx / g) * g, oy = (float)Math.Round(my / g) * g;
            float ex = (mx - ox) * tx + (my - oy) * ty, ey = (mx - ox) * nx + (my - oy) * ny; // the line's middle, from the sprite's
            float hl = g * (float)Math.Ceiling((len / 2 + Math.Abs(ex)) / g), hh = g * (float)Math.Ceiling((h / 2 + Math.Abs(ey)) / g);
            if (Math.Max(Math.Abs(ox), Math.Abs(oy)) + hl + hh >= 2040 * g) continue;
            float su = UltraTextures.Solid / len; // texels to a pixel along the line
            const int sv = UltraTextures.RowsPerPixel;
            float a = hl + ex - len / 2, b = hh + ey - h / 2; // from the sprite's corner to the line's
            var src = new Rectangle((int)Math.Round(UltraTextures.Pad - a * su), (int)Math.Round(UltraTextures.Pad - b * sv),
                Math.Max(1, (int)Math.Round(2 * hl * su)), Math.Max(1, (int)Math.Round(2 * hh * sv)));
            var dst = new RectangleF(ox - hl, oy - hh, 2 * hl, 2 * hh);
            Sprites++;
            MyRenderProxy.DrawSprite(tex, ref dst, src, Pre(col), (float)Math.Atan2(dy, dx), true, false);
            return true;
        }
        return false;
    }

    public void Disc(float cx, float cy, float r, MapColor col)
    {
        if (col.A == 0 || r <= 0 || r > 20000 || float.IsNaN(r)) return;
        var rect = new RectangleF(cx - r, cy - r, 2 * r, 2 * r);
        Sprites++;
        MyRenderProxy.DrawSprite(Ultra ? UltraTextures.Disc() ?? CircleTex : CircleTex, ref rect, null, Pre(col), 0, true, false);
    }

    public void Text(string text, float x, float y, float px, MapColor col, TextAnchor anchor = TextAnchor.Left)
    {
        if (string.IsNullOrEmpty(text) || col.A == 0) return;
        text = Glyphs(text);
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
        Sprites += 2;
    }

    public float TextWidth(string text, float px) => string.IsNullOrEmpty(text) ? 0 : MyFontDefinition.MeasureStringRaw(Font, Glyphs(text), Scale(px)).X;

    /// <summary>The game's White font has no arrow (it draws a box): "→" is drawn as "->".</summary>
    private static string Glyphs(string text) => text.IndexOf('→') < 0 ? text : text.Replace("→", "->");

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
