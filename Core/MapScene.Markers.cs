using System;

namespace SDX2_SystemMap;

/// <summary>
/// The destination marker, one per theme:
/// <list type="bullet">
/// <item>Classic: a ring with a gapped crosshair.</item>
/// <item>Tachi: a segmented lock ring round a red diamond, with chevrons closing in from four sides.</item>
/// <item>UNN: a plotting fix: a double ring with bearing ticks every 30 degrees, gold pointers and a gold square at its centre.</item>
/// <item>Belter: a heavy octagon round a dashed teal ring, with hazard bars on its diagonals and a cross at its centre.</item>
/// <item>Protogen: the lock reticle (range rings, lock arc, chevrons, TRAJECTORY VECTOR tag).</item>
/// <item>OPA: a contact plot target: yellow brackets round a ring, with a ranging triangle above it.</item>
/// </list>
/// </summary>
public sealed partial class MapScene
{
    /// <summary>Draws the destination marker at (x, y); returns how far from it the label goes.</summary>
    private float DestinationMark(float x, float y)
    {
        switch (T.Style)
        {
            case ThemeStyle.Protogen: LockReticle(x, y); return 36 * u;
            case ThemeStyle.Tachi: TachiMark(x, y); return 30 * u;
            case ThemeStyle.Naval: NavalMark(x, y); return 30 * u;
            case ThemeStyle.Belter: BelterMark(x, y); return 26 * u;
            case ThemeStyle.Contact: ContactMark(x, y); return 17 * u;
            default: ClassicMark(x, y); return 15 * u;
        }
    }

    private void ClassicMark(float x, float y)
    {
        ScreenCircle(x, y, 10 * u, 2 * u, BrakeCol, 28);
        float t = 15 * u, g = 6 * u;
        c.Line(x - t, y, x - g, y, 2 * u, BrakeCol); c.Line(x + g, y, x + t, y, 2 * u, BrakeCol);
        c.Line(x, y - t, x, y - g, 2 * u, BrakeCol); c.Line(x, y + g, x, y + t, 2 * u, BrakeCol);
    }

    private void TachiMark(float x, float y)
    {
        float lw = Math.Max(1.5f, 2 * u), d = 9 * u;
        for (int k = 0; k < 8; k++) Arc(x, y, 20 * u, k * 45 + 6, k * 45 + 39, lw, k % 2 == 0 ? Accent : Accent.WithAlpha(0.45f), 6);
        c.Line(x, y - d, x + d, y, lw, BrakeCol); c.Line(x + d, y, x, y + d, lw, BrakeCol);
        c.Line(x, y + d, x - d, y, lw, BrakeCol); c.Line(x - d, y, x, y - d, lw, BrakeCol);
        foreach (var (dx, dy) in new[] { (0f, -1f), (1f, 0f), (0f, 1f), (-1f, 0f) })
            Chevron(x + dx * 27 * u, y + dy * 27 * u, 4 * u, -dx, -dy, lw, T.Fine);
        c.Disc(x, y, Math.Max(1.5f, 1.8f * u), T.Fine);
        taken.Add(new Box(x - 28 * u, y - 28 * u, 56 * u, 56 * u));
    }

    private void NavalMark(float x, float y)
    {
        float lw = Math.Max(1f, 1.2f * u);
        ScreenCircle(x, y, 12 * u, Math.Max(1.5f, 1.8f * u), BrakeCol, 36);
        ScreenCircle(x, y, 18 * u, lw, T.Trim.WithAlpha(0.8f), 40);
        for (int k = 0; k < 12; k++) Spoke(x, y, k * 30, 18 * u, (k % 3 == 0 ? 25 : 21.5f) * u, lw, k % 3 == 0 ? T.Trim : Accent.WithAlpha(0.7f));
        foreach (var (dx, dy) in new[] { (0f, -1f), (1f, 0f), (0f, 1f), (-1f, 0f) })
            Triangle(x + dx * 31 * u, y + dy * 31 * u, 4 * u, -dx, -dy, T.Trim);
        c.Line(x - 6 * u, y, x + 6 * u, y, lw, BrakeCol.WithAlpha(0.8f));
        c.Line(x, y - 6 * u, x, y + 6 * u, lw, BrakeCol.WithAlpha(0.8f));
        c.Rect(x - 2 * u, y - 2 * u, 4 * u, 4 * u, T.Trim);
        taken.Add(new Box(x - 30 * u, y - 30 * u, 60 * u, 60 * u));
    }

    private void BelterMark(float x, float y)
    {
        float r = 14 * u, w = Math.Max(2f, 2.6f * u), lw = Math.Max(1f, 1.2f * u);
        float lx = 0, ly = 0;
        for (int i = 0; i <= 8; i++)
        {
            double a = (22.5 + i * 45) * Math.PI / 180;
            float qx = x + r * (float)Math.Cos(a), qy = y + r * (float)Math.Sin(a);
            if (i > 0) c.Line(lx, ly, qx, qy, w, Accent);
            lx = qx; ly = qy;
        }
        DashedCircle(x, y, 8 * u, lw, T.Ship, 20);
        // hazard bars on the diagonals: orange, dark, orange
        for (int k = 0; k < 4; k++)
        {
            float a = 45 + k * 90;
            Spoke(x, y, a, 18 * u, 21 * u, Math.Max(2f, 3.2f * u), Accent);
            Spoke(x, y, a, 21 * u, 22.5f * u, Math.Max(2f, 3.2f * u), MapColor.Rgb(0x15130F));
            Spoke(x, y, a, 22.5f * u, 25 * u, Math.Max(2f, 3.2f * u), Accent);
        }
        float s = 3 * u;
        c.Line(x - s, y - s, x + s, y + s, lw * 1.4f, T.Ship);
        c.Line(x - s, y + s, x + s, y - s, lw * 1.4f, T.Ship);
        taken.Add(new Box(x - 24 * u, y - 24 * u, 48 * u, 48 * u));
    }

    private void ContactMark(float x, float y)
    {
        ScreenCircle(x, y, 8 * u, Math.Max(1f, 1.5f * u), BrakeCol, 28);
        float b = 15 * u;
        Brackets(new Box(x - b, y - b, 2 * b, 2 * b), 7 * u, Math.Max(1.5f, 2f * u), T.Trim);
        // ranging ticks across the brackets' gaps and a contact triangle above
        float lw = Math.Max(1f, u);
        c.Line(x - b - 4 * u, y, x - b + 3 * u, y, lw, T.Trim);
        c.Line(x + b - 3 * u, y, x + b + 4 * u, y, lw, T.Trim);
        Triangle(x, y - b - 7 * u, 4 * u, 0, 1, BrakeCol);
        c.Disc(x, y, Math.Max(1.5f, 1.6f * u), BrakeCol);
    }
}
