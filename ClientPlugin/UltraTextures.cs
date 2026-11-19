using System;
using VRageRender;
using VRageRender.Messages;

namespace SDX2_SystemMap;

/// <summary>
/// The textures of the Ultra drawing quality, made in memory the first time they are asked for (the plugin ships as
/// source, with no files of its own): strips with soft top and bottom edges for lines, and a large circle with a soft
/// rim. White with premultiplied alpha, as the sprite renderer expects; the sprite's colour tints them.
/// A null means the texture couldn't be made: the caller draws the standard way.
/// </summary>
internal static class UltraTextures
{
    private const int Quarters = 48, DiscSize = 256; // lines up to 12 px thick, in quarter pixels
    private const float Fade = 0.5f; // pixels over which a line's edge goes from solid to clear

    /// <summary>
    /// The layout of a line texture: <see cref="Pad"/> clear texels at every side; between them the line, its whole
    /// length spread over <see cref="Solid"/> texels and its profile over <see cref="RowsPerPixel"/> rows a pixel.
    /// </summary>
    public const int Pad = 2, Solid = 1020, RowsPerPixel = 8;
    private const int Width = Solid + 2 * Pad;

    private static readonly string[] lines = new string[Quarters + 1];
    private static string disc;
    private static bool failed;

    /// <summary>
    /// The texture for a line <paramref name="width"/> pixels thick, or null. <paramref name="lineWidth"/> is the
    /// width it was made for (the nearest quarter pixel): its profile spans that plus one pixel, solid across the
    /// line less a quarter pixel each side, fading to clear over the next half pixel.
    /// </summary>
    public static string Line(float width, out float lineWidth)
    {
        int q = Math.Max(2, Math.Min(Quarters, (int)Math.Round(width * 4)));
        lineWidth = q / 4f;
        if (failed) return null;
        if (lines[q] != null) return lines[q];
        try
        {
            float lw = lineWidth, h = lw + 1;
            int profile = (int)Math.Round(h * RowsPerPixel), rows = profile + 2 * Pad;
            var data = new byte[Width * rows * 4];
            for (int y = 0; y < profile; y++)
            {
                float d = Math.Abs((y + 0.5f) / RowsPerPixel - h / 2);
                byte a = (byte)Math.Round(255 * Math.Max(0, Math.Min(1, (lw / 2 + Fade / 2 - d) / Fade)));
                int row = (y + Pad) * Width * 4;
                for (int i = Pad * 4; i < (Pad + Solid) * 4; i++) data[row + i] = a;
            }
            string name = "SDX2Map_UltraLineP" + q;
            MyRenderProxy.CreateGeneratedTexture(name, Width, rows, MyGeneratedTextureType.RGBA_Linear, 1, data, false);
            return lines[q] = name;
        }
        catch (Exception e) { Fail(e); return null; }
    }

    /// <summary>A circle with a soft rim, large enough to stay sharp when drawn big (smaller sizes come from its mipmaps).</summary>
    public static string Disc()
    {
        if (failed) return null;
        if (disc != null) return disc;
        try
        {
            const int n = DiscSize;
            // With mipmaps on, the game hands the graphics card a pointer for every smaller copy too, each one past
            // the end of the copy before it: the array has to hold them all (a third more than the picture, here
            // twice to be safe), or the card reads past the array and the game can crash. The game then fills the
            // smaller copies in itself.
            var data = new byte[n * n * 4 * 2];
            double c = (n - 1) / 2.0, r = n / 2.0 - 1;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    double d = Math.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    byte a = (byte)Math.Round(255 * Math.Max(0, Math.Min(1, r - d + 0.5)));
                    int i = (y * n + x) * 4;
                    data[i] = data[i + 1] = data[i + 2] = data[i + 3] = a;
                }
            const string name = "SDX2Map_UltraDisc";
            MyRenderProxy.CreateGeneratedTexture(name, n, n, MyGeneratedTextureType.RGBA_Linear, 1, data, true);
            return disc = name;
        }
        catch (Exception e) { Fail(e); return null; }
    }

    private static void Fail(Exception e)
    {
        failed = true;
        Plugin.Log("Ultra textures not made, drawing the standard way: " + e.Message);
    }
}
