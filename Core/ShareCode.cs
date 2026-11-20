using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SDX2_SystemMap;

/// <summary>
/// A route plan as a short line of text to hand to someone else ("SDX2R:" and base64): made when a route is sent to
/// the autopilot, pasted by you into faction chat (or anywhere), and read by another player's map into its ship
/// routes layer. It carries what the layer needs and no more: the ship's and the destination's names, when it set
/// off (real time), where from, and for each leg where it ends and its burn plan (start, peak and end speed; burn,
/// coast and brake time). The reader rebuilds where the plan has the ship at each moment from those; each leg as a
/// straight line (a curved leg loses its curve), times to the second.
/// A route of up to four legs fits one game chat message (ChatMax); longer ones need another way across.
/// </summary>
public static class ShareCode
{
    public const string Prefix = "SDX2R:";
    /// <summary>The game's chat message limit (MyChatConstants.MAX_CHAT_STRING_LENGTH).</summary>
    public const int ChatMax = 200;
    private const byte Version = 1;
    private const int MaxLegs = 40, MaxName = 20;
    private static readonly DateTime Epoch = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Real time as the code stamps it (s since 2020, UTC).</summary>
    public static double Now => (DateTime.UtcNow - Epoch).TotalSeconds;

    private struct Leg { public V3 End; public double U, Peak, E, Burn, Coast, Brake; }

    /// <summary>The code of a route being sent now (startUtc = <see cref="Now"/>). Null when it has no time plan.</summary>
    public static string Make(RoutePath path, long shipId, string ship, double startUtc)
    {
        if (path == null || path.Legs.Count == 0 || path.Legs.Count > MaxLegs) return null;
        var legs = new List<Leg>();
        foreach (var p in path.Legs)
        {
            if (double.IsNaN(p.Eta) || double.IsInfinity(p.Eta) || p.Eta <= 0) return null;
            double u = Math.Max(0, p.ClosingSpeed);
            var l = new Leg { End = p.Target, U = u, Peak = u, E = u, Coast = p.Eta };
            if (p.HasBurnPlan && p.TooFastToStop) { l.Coast = 0; l.Brake = p.Eta; l.E = Math.Max(0, u - p.BrakeUsed * p.Eta); }
            else if (p.HasBurnPlan)
            {
                l.Peak = p.PeakSpeed; l.Burn = p.BurnTime; l.Coast = p.CoastTime; l.Brake = p.BrakeTime;
                l.E = Math.Max(0, p.PeakSpeed - p.BrakeUsed * p.BrakeTime);
            }
            legs.Add(l);
        }
        // names cut down until it fits a chat message (it may still not, with many legs)
        string code = null;
        for (int n = MaxName; n >= 4; n -= 4)
        {
            code = Write(path.Legs[0].Start, legs, shipId, Cut(ship, n), Cut(path.TargetName, n), startUtc);
            if (code.Length <= ChatMax) break;
        }
        return code;
    }

    private static string Cut(string s, int n) { s = (s ?? "").Trim(); return s.Length <= n ? s : s.Substring(0, n).Trim(); }
    private static ushort U16(double v) => (ushort)Math.Max(0, Math.Min(65535, Math.Round(v)));

    private static void Put(BinaryWriter w, V3 p) { w.Write((int)Math.Round(p.X)); w.Write((int)Math.Round(p.Y)); w.Write((int)Math.Round(p.Z)); }
    private static V3 Get(BinaryReader r) { double x = r.ReadInt32(), y = r.ReadInt32(), z = r.ReadInt32(); return new V3(x, y, z); }
    private static void Put(BinaryWriter w, string s) { var b = Encoding.UTF8.GetBytes(s); w.Write((byte)b.Length); w.Write(b); }
    private static string GetS(BinaryReader r) { int n = r.ReadByte(); return Encoding.UTF8.GetString(r.ReadBytes(n)); }

    private static string Write(V3 start, List<Leg> legs, long shipId, string ship, string target, double startUtc)
    {
        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            w.Write(Version);
            w.Write((uint)Math.Max(0, Math.Min(uint.MaxValue, Math.Round(startUtc))));
            w.Write((int)shipId);
            Put(w, ship); Put(w, target);
            Put(w, start);
            w.Write((byte)legs.Count);
            foreach (var l in legs)
            {
                Put(w, l.End);
                w.Write(U16(l.U)); w.Write(U16(l.Peak)); w.Write(U16(l.E));
                w.Write(U16(l.Burn)); w.Write(U16(l.Coast)); w.Write(U16(l.Brake));
            }
            w.Flush();
            return Prefix + Convert.ToBase64String(ms.ToArray()).TrimEnd('=');
        }
    }

    /// <summary>The first code in a text (a chat line, the clipboard), or null.</summary>
    public static string Find(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        int i = text.IndexOf(Prefix, StringComparison.Ordinal);
        if (i < 0) return null;
        int j = i + Prefix.Length, k = j;
        while (k < text.Length && (char.IsLetterOrDigit(text[k]) && text[k] < 128 || text[k] == '+' || text[k] == '/')) k++;
        return k - j >= 20 ? text.Substring(i, k - i) : null;
    }

    /// <summary>
    /// Reads a code into a saved route: Start is the real time it set off (see <see cref="Now"/>; the caller moves it
    /// to its own clock), ShipId is negative (never one of your own ships' ids), World is left empty. Null: not a
    /// code this version reads.
    /// </summary>
    public static SavedRoute Read(string code)
    {
        try
        {
            code = Find(code);
            if (code == null) return null;
            string b64 = code.Substring(Prefix.Length);
            b64 += new string('=', (4 - b64.Length % 4) % 4);
            using (var r = new BinaryReader(new MemoryStream(Convert.FromBase64String(b64))))
            {
                if (r.ReadByte() != Version) return null;
                double start = r.ReadUInt32();
                int id = r.ReadInt32();
                string ship = GetS(r), target = GetS(r);
                var from = Get(r);
                int n = r.ReadByte();
                if (n < 1 || n > MaxLegs) return null;
                var res = new SavedRoute { ShipId = -1 - (long)(uint)id, Ship = SavedRoute.Clean(ship), Target = SavedRoute.Clean(target), Start = start, Code = code };
                res.Track.Add((0, from));
                double t0 = 0;
                for (int i = 0; i < n; i++)
                {
                    var l = new Leg { End = Get(r), U = r.ReadUInt16(), Peak = r.ReadUInt16(), E = r.ReadUInt16(), Burn = r.ReadUInt16(), Coast = r.ReadUInt16(), Brake = r.ReadUInt16() };
                    double eta = l.Burn + l.Coast + l.Brake;
                    if (eta <= 0) { res.Track.Add((t0, l.End)); from = l.End; continue; }
                    double total = Along(l, eta);
                    for (int k = 1; k < SavedRoute.PerLeg; k++)
                    {
                        double t = eta * k / SavedRoute.PerLeg;
                        res.Track.Add((t0 + t, V3.Lerp(from, l.End, total > 0 ? Math.Min(1, Along(l, t) / total) : 1)));
                    }
                    t0 += eta;
                    res.Track.Add((t0, l.End));
                    from = l.End;
                }
                return res.Track.Count >= 2 && res.Eta > 0 ? res : null;
            }
        }
        catch { return null; }
    }

    /// <summary>Metres along a leg t seconds into it: the burn from U to Peak, the coast, the brake from Peak to E.</summary>
    private static double Along(Leg l, double t)
    {
        double d = 0;
        double tb = Math.Min(t, l.Burn);
        if (l.Burn > 0) d += l.U * tb + (l.Peak - l.U) / l.Burn * tb * tb / 2;
        t -= l.Burn;
        if (t <= 0) return d;
        d += l.Peak * Math.Min(t, l.Coast);
        t -= l.Coast;
        if (t <= 0 || l.Brake <= 0) return d;
        double tk = Math.Min(t, l.Brake);
        return d + l.Peak * tk - (l.Peak - l.E) / l.Brake * tk * tk / 2;
    }
}
