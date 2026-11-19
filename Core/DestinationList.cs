using System;
using System.Collections.Generic;

namespace SDX2_SystemMap;

/// <summary>
/// The list of places to fly to, for a screen Flip and Burn's own N list can't be shown on (the second screen): the
/// worlds with their moons under them, the stations and your GPS points, each with its distance, a page at a time.
/// </summary>
public static class DestinationList
{
    public struct Entry
    {
        public string Name, Kind, Detail; public V3 Pos;
        /// <summary>A moon (drawn set in under its planet).</summary>
        public bool Moon;
        /// <summary>A heading row (WORLDS, STATIONS, GPS), not a place.</summary>
        public bool Head;
    }

    /// <summary>What a pick does and where it was drawn.</summary>
    public struct Hit { public Box Panel; public int Picked; public bool Close; }

    /// <summary>Every place, in the order shown.</summary>
    public static List<Entry> Build(MapSnapshot snap)
    {
        var list = new List<Entry> { new Entry { Name = "WORLDS", Head = true } };
        foreach (var w in SystemData.Worlds)
        {
            if (w.IsStar || w.IsObstacle || w.IsField) continue;
            list.Add(new Entry { Name = w.Name, Kind = "World", Detail = SystemData.SectorAt(w.Pos).Dx, Pos = w.Pos, Moon = !string.IsNullOrEmpty(w.Parent) && w.Parent != "Sol" });
        }
        list.Add(new Entry { Name = "STATIONS", Head = true });
        foreach (var s in SystemData.Stations) list.Add(new Entry { Name = s.Name, Kind = "Trade station", Detail = s.Gps, Pos = s.Pos });
        if (snap != null && snap.Gps.Count > 0)
        {
            list.Add(new Entry { Name = "GPS", Head = true });
            foreach (var m in snap.Gps) list.Add(new Entry { Name = m.Name, Kind = "GPS", Detail = "", Pos = m.Pos });
        }
        return list;
    }

    /// <summary>How many rows fit a panel h high at text size px.</summary>
    public static int Rows(float h, float px) => Math.Max(3, (int)((h - PanelStyle.HeaderH(px) - px * 3.2f) / (px * 2.0f)));

    /// <summary>
    /// Draws the list in a panel at x, y (w wide, at most h high) and returns what was picked (-1 = nothing).
    /// <paramref name="current"/> is the destination now (lit); <paramref name="adding"/>: a pick adds a waypoint.
    /// </summary>
    public static Hit Draw(PanelUi ui, IReadOnlyList<Entry> items, float x, float y, float w, float h, float px, MapSnapshot snap, string current, bool adding, ref int page)
    {
        var c = ui.C;
        var hit = new Hit { Picked = -1 };
        int rows = Rows(h, px), pages = Math.Max(1, (items.Count + rows - 1) / rows);
        page = Math.Max(0, Math.Min(pages - 1, page));
        float rowH = px * 1.7f, step = px * 2.0f, pad = px * 0.9f;
        int first = page * rows, n = Math.Min(rows, items.Count - first);
        float ph = PanelStyle.HeaderH(px) + rows * step + px * 3.2f;
        var panel = hit.Panel = new Box(x, y, w, ph);
        PanelStyle.Frame(c, panel, px / 12f, px, adding ? "ADD WAYPOINT" : "DESTINATIONS", null, AutopilotPanel.Panel);
        float ty = y + PanelStyle.HeaderH(px);
        float sp = px * 0.9f;
        for (int i = 0; i < n; i++, ty += step)
        {
            var e = items[first + i];
            if (e.Head) { c.Text(e.Name, x + pad, ty + (rowH - sp) / 2, sp, AutopilotPanel.Dim); continue; }
            float inset = e.Moon ? px * 1.2f : 0;
            var b = new Box(x + pad + inset, ty, w - pad * 2 - inset, rowH);
            string dist = snap != null && snap.HasShip ? Fmt.Dist(V3.Distance(snap.ShipPos, e.Pos)) : "";
            float dw = dist.Length > 0 ? c.TextWidth(dist, sp) + px * 0.8f : 0;
            // the name cut to the room left of the distance
            string name = e.Name;
            float room = b.W - dw - px * 1.6f;
            if (c.TextWidth(name, px) > room)
            {
                while (name.Length > 4 && c.TextWidth(name + "...", px) > room) name = name.Substring(0, name.Length - 1);
                name = name.TrimEnd() + "...";
            }
            bool on = current != null && e.Name == current;
            bool over = ui.Over(b);
            if (ui.Button(b, "", px, AutopilotPanel.Go, on)) hit.Picked = first + i;
            c.Text(name, b.X + px * 0.8f, b.Y + (rowH - px) / 2, px, on || over ? AutopilotPanel.Text : AutopilotPanel.Text.WithAlpha(0.85f));
            if (dw > 0) c.Text(dist, b.Right - dw, b.Y + (rowH - sp) / 2, sp, AutopilotPanel.Dim);
        }
        // PREV, page, NEXT, CLOSE
        float by = panel.Bottom - px * 2.6f, bw = (w - pad * 2 - px * 1.2f) / 4;
        if (ui.Button(new Box(x + pad, by, bw, px * 1.9f), "PREV", sp, AutopilotPanel.Dim, false, page > 0)) page--;
        string pt = (page + 1) + " / " + pages;
        c.Text(pt, x + pad + bw * 1.5f + px * 0.4f - c.TextWidth(pt, sp) / 2, by + (px * 1.9f - sp) / 2, sp, AutopilotPanel.Dim);
        if (ui.Button(new Box(x + pad + (bw + px * 0.4f) * 2, by, bw, px * 1.9f), "NEXT", sp, AutopilotPanel.Dim, false, page < pages - 1)) page++;
        hit.Close = ui.Button(new Box(x + pad + (bw + px * 0.4f) * 3, by, bw, px * 1.9f), "CLOSE", sp, AutopilotPanel.Dim);
        return hit;
    }
}
