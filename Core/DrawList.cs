using System;
using System.Collections.Generic;

namespace SDX2_SystemMap;

/// <summary>
/// A canvas that only records: the game thread draws the map into it, and the second screen's window plays it back on
/// its own thread (<see cref="Replay"/>), so the slow part (GDI+) never holds the game up. Text widths come from
/// <see cref="Measure"/>, which must match the canvas it is played back on.
/// </summary>
public sealed class DrawList : IMapCanvas
{
    private enum Op : byte { Rect, Line, Disc, Text, Push, Pop }
    private struct Cmd { public Op Op; public float A, B, C, D, E; public MapColor Col; public string Text; public TextAnchor Anchor; }

    private Cmd[] cmds = new Cmd[4096];
    private int n;
    /// <summary>The size it was drawn for (px).</summary>
    public int Width, Height;
    public Func<string, float, float> Measure = (s, px) => s.Length * px * 0.5f;

    public int Count => n;

    public void Clear(int width, int height) { n = 0; Width = width; Height = height; }

    private void Add(Cmd c)
    {
        if (n == cmds.Length) Array.Resize(ref cmds, n * 2);
        cmds[n++] = c;
    }

    public void Rect(float x, float y, float w, float h, MapColor col, float rotation = 0)
    {
        if (col.A == 0 || w <= 0 || h <= 0) return;
        Add(new Cmd { Op = Op.Rect, A = x, B = y, C = w, D = h, E = rotation, Col = col });
    }

    public void Line(float x1, float y1, float x2, float y2, float width, MapColor col)
    {
        if (col.A == 0 || float.IsNaN(x1 + y1 + x2 + y2)) return;
        Add(new Cmd { Op = Op.Line, A = x1, B = y1, C = x2, D = y2, E = width, Col = col });
    }

    public void Disc(float cx, float cy, float r, MapColor col)
    {
        if (col.A == 0 || r <= 0 || float.IsNaN(cx + cy + r)) return;
        Add(new Cmd { Op = Op.Disc, A = cx, B = cy, C = r, Col = col });
    }

    public void Text(string text, float x, float y, float px, MapColor col, TextAnchor anchor = TextAnchor.Left)
    {
        if (string.IsNullOrEmpty(text) || col.A == 0) return;
        Add(new Cmd { Op = Op.Text, A = x, B = y, C = px, Col = col, Text = text, Anchor = anchor });
    }

    public float TextWidth(string text, float px) => string.IsNullOrEmpty(text) ? 0 : Measure(text, px);

    public void PushClip(float x, float y, float w, float h) => Add(new Cmd { Op = Op.Push, A = x, B = y, C = w, D = h });

    public void PopClip() => Add(new Cmd { Op = Op.Pop });

    /// <summary>Draws everything recorded onto c, in order (clips left open are closed at the end).</summary>
    public void Replay(IMapCanvas c)
    {
        int open = 0;
        for (int i = 0; i < n; i++)
        {
            ref var k = ref cmds[i];
            switch (k.Op)
            {
                case Op.Rect: c.Rect(k.A, k.B, k.C, k.D, k.Col, k.E); break;
                case Op.Line: c.Line(k.A, k.B, k.C, k.D, k.E, k.Col); break;
                case Op.Disc: c.Disc(k.A, k.B, k.C, k.Col); break;
                case Op.Text: c.Text(k.Text, k.A, k.B, k.C, k.Col, k.Anchor); break;
                case Op.Push: c.PushClip(k.A, k.B, k.C, k.D); open++; break;
                case Op.Pop: if (open > 0) { c.PopClip(); open--; } break;
            }
        }
        while (open-- > 0) c.PopClip();
    }
}
