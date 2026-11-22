using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game.Gui;
using VRage;
using VRage.GameServices;
using VRage.Utils;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.World;
using VRage.FileSystem;

namespace SDX2_SystemMap;

/// <summary>
/// The map's "ship routes" layer (MapOptions.Routes): where your other ships should be. Nothing is read from a ship
/// after it is sent off. When a route is sent to the autopilot of the ship you fly (ENGAGE, or a change to a route
/// being flown), its plan is kept (SavedRoute: where the plan has the ship at each moment) in a file; from then on
/// the ship is drawn where that plan has it by the clock, whether or not the game still has the ship loaded.
/// A ship's entry goes when you stop its autopilot, when you are aboard and its autopilot isn't flying, when a new
/// route is sent to it, or a day after it should have arrived. The ship you fly isn't drawn (it has the live route).
/// Sharing: each plan kept has a share code (ShareCode). CopyCode puts the flown ship's on the clipboard, for you to
/// paste to your faction; Import reads one from the clipboard; and a code a faction member posts in faction chat (or
/// sends you privately) is read as it arrives. The plugin never posts anything itself. A shared ship is drawn like
/// your own, goes when a newer code for it comes in or a day after it should have arrived, and ClearShared drops all.
/// The clock: a world you host (single player) uses the world's own game time, which stops while it is paused and
/// goes back with an older save; on a server it is the real time.
/// </summary>
internal static class FleetRoutes
{
    private const double KeepAfterArrival = 24 * 3600;
    private static List<SavedRoute> all;
    private static readonly Dictionary<SavedRoute, MapSnapshot.OtherRoute> shown = new Dictionary<SavedRoute, MapSnapshot.OtherRoute>();
    private static double nextTidy;

    private static string FilePath => Path.Combine(MyFileSystem.UserDataPath ?? "", "Storage", "SDX2_SystemMap_ShipRoutes.txt");

    public static void Reset() { all = null; shown.Clear(); nextTidy = 0; Hook(null); }

    // ---------------------------------------------------------------- sharing

    private static MyMultiplayerBase hooked;

    private static void Hook(MyMultiplayerBase mp)
    {
        if (ReferenceEquals(mp, hooked)) return;
        try { if (hooked != null) hooked.ChatMessageReceived -= OnChat; } catch { }
        hooked = mp;
        try { if (mp != null) mp.ChatMessageReceived += OnChat; } catch { hooked = null; }
    }

    /// <summary>A share code in faction chat or a private message, from someone else: read into the layer.</summary>
    private static void OnChat(ulong sender, string text, ChatChannel channel, long target, ChatMessageCustomData? data)
    {
        try
        {
            if (channel != ChatChannel.Faction && channel != ChatChannel.Private) return;
            if (text == null || text.IndexOf(ShareCode.Prefix, StringComparison.Ordinal) < 0 || sender == Sync.MyId) return;
            string by = "";
            try { by = MyMultiplayer.Static?.GetMemberName(sender) ?? ""; } catch { }
            string res = Import(text, by, out bool ok);
            if (ok) SidePreview.Notify(res);
        }
        catch (Exception e) { Plugin.LogThrottled("ship routes chat: " + e.Message); }
    }

    /// <summary>The flown ship's route as a share code on the clipboard; what to tell the pilot.</summary>
    public static string CopyCode(out bool ok)
    {
        ok = false;
        if (MySession.Static == null) return "No world loaded.";
        long ship = AutopilotLink.FlownShip; string w = World;
        var r = ship == 0 ? null : All.Find(x => x.ShipId == ship && x.World == w);
        if (r == null || string.IsNullOrEmpty(r.Code)) return "No route to share: ENGAGE one on this ship first.";
        if (Stamp - r.Start > r.Eta) return "This ship's route is over: nothing to share.";
        try { MyClipboardHelper.SetClipboard(r.Code); } catch (Exception e) { return "Can't copy: " + e.Message; }
        ok = true;
        return r.Code.Length <= ShareCode.ChatMax ? "Share code copied: paste it in faction chat (Ctrl+V)."
            : "Share code copied, but it is too long for game chat (" + r.Code.Length + " characters): paste it somewhere else.";
    }

    /// <summary>Reads a share code from the clipboard into the layer.</summary>
    public static string ImportClipboard(out bool ok)
    {
        ok = false;
        string text = null;
        try
        {
            // (the clipboard is read on a thread of its own, as the game's text boxes do)
            var t = new Thread(() => { try { text = MyVRage.Platform.System.Clipboard; } catch { } });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join(1000);
        }
        catch (Exception e) { return "Can't read the clipboard: " + e.Message; }
        if (ShareCode.Find(text) == null) return "No share code on the clipboard (it starts with " + ShareCode.Prefix + ").";
        return Import(text, "", out ok);
    }

    private static string Import(string text, string by, out bool ok)
    {
        ok = false;
        if (MySession.Static == null) return "No world loaded.";
        var r = ShareCode.Read(text);
        if (r == null) return "That share code can't be read (made by a newer version?).";
        string w = World;
        // one of your own ships (its id's low half is in the code): you have its plan already
        uint low = (uint)(-1 - r.ShipId);
        if (All.Exists(x => !x.Shared && x.World == w && (uint)x.ShipId == low)) return r.Ship + " is your own ship: its route is on the map already.";
        double age = ShareCode.Now - r.Start;
        if (age > r.Eta + KeepAfterArrival) return "That share code is old: " + r.Ship + " should have arrived more than a day ago.";
        r.World = w; r.By = SavedRoute.Clean(by);
        r.Start = Stamp - age; // onto this world's clock
        All.RemoveAll(x => x.ShipId == r.ShipId && x.World == w);
        All.Add(r);
        shown.Clear();
        Write();
        ok = true;
        return "Route added: " + r.Ship + " > " + r.Target + (r.By != "" ? " (from " + r.By + ")" : "") + (Plugin.Settings.Options.Routes ? "" : ". Ship routes are off (Y)");
    }

    /// <summary>Drops every route shared with you in this world.</summary>
    public static string ClearShared()
    {
        string w = World;
        int n = MySession.Static == null ? 0 : All.RemoveAll(x => x.Shared && x.World == w);
        if (n > 0) { shown.Clear(); Write(); }
        return n == 0 ? "No shared routes to clear." : n + " shared route" + (n == 1 ? "" : "s") + " cleared.";
    }

    private static bool Local => Sync.IsServer;
    /// <summary>Which saved routes belong here: this world by name when you host it, else the server.</summary>
    private static string World => Local ? "sp:" + (MySession.Static?.Name ?? "") : "mp";
    private static double Stamp => Local ? (MySession.Static?.ElapsedGameTime.TotalSeconds ?? 0) : (DateTime.UtcNow - new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;

    private static List<SavedRoute> All => all ?? (all = SavedRoute.Load(FilePath));

    private static void Write()
    {
        try { SavedRoute.Save(FilePath, All); } catch (Exception e) { Plugin.LogThrottled("can't save ship routes: " + e.Message); }
    }

    /// <summary>A route went to this ship's autopilot (engage, or an update while flying): its plan from here is kept.</summary>
    public static void Started(long shipId, string ship, RoutePath path)
    {
        try
        {
            if (MySession.Static == null || shipId == 0) return;
            var r = SavedRoute.From(path, shipId, ship, World, Stamp);
            if (r != null) { r.Code = ShareCode.Make(path, shipId, r.Ship, ShareCode.Now) ?? ""; r.Stop = RouteExport.StopPoint(path, out _); }
            string w = World;
            All.RemoveAll(x => x.ShipId == shipId && x.World == w);
            if (r != null) All.Add(r);
            Write();
        }
        catch (Exception e) { Plugin.LogThrottled("ship routes: " + e.Message); }
    }

    /// <summary>Where the other ships' routes stop (yours and the shared ones, in this world, layer shown or not), for
    /// RouteExport.Taken: not the flown ship's own.</summary>
    public static void Stops(List<(V3 Pos, double R)> into, long flownShip)
    {
        try
        {
            if (MySession.Static == null) return;
            string w = World;
            foreach (var r in All) if (r.World == w && r.ShipId != flownShip) into.Add((r.StopAt, 0));
        }
        catch (Exception e) { Plugin.LogThrottled("ship routes: " + e.Message); }
    }

    /// <summary>This ship's autopilot was stopped (stop, full stop, manual control confirmed).</summary>
    public static void Ended(long shipId)
    {
        try { string w = World; if (All.RemoveAll(x => x.ShipId == shipId && x.World == w) > 0) Write(); }
        catch (Exception e) { Plugin.LogThrottled("ship routes: " + e.Message); }
    }

    /// <summary>Every update: the saved routes of this world, but the flown ship's, each with the ship where its plan has it now.</summary>
    public static void Update(double now, MapSnapshot snap, bool on, long flownShip, bool flownIdle)
    {
        snap.Others.Clear();
        if (MySession.Static != null) Hook(MyMultiplayer.Static); // codes in chat are kept even while the layer is off
        if (!on || MySession.Static == null) return;
        string w = World; double stamp = Stamp;
        if (now >= nextTidy)
        {
            nextTidy = now + 5;
            // aboard a ship whose autopilot isn't flying: its route is over. Long arrived: dropped
            int gone = All.RemoveAll(x => x.World == w && (x.ShipId == flownShip && flownIdle || stamp - x.Start > x.Eta + KeepAfterArrival));
            if (gone > 0) { Write(); shown.Clear(); }
        }
        foreach (var r in All)
        {
            if (r.World != w || r.ShipId == flownShip) continue;
            if (!shown.TryGetValue(r, out var o)) shown[r] = o = new MapSnapshot.OtherRoute { Ship = r.Ship, Target = r.Target, Shared = r.Shared, By = r.By };
            double elapsed = stamp - r.Start;
            o.Pos = r.At(elapsed, out int next);
            o.Arrived = next >= r.Track.Count;
            o.TimeLeft = Math.Max(0, r.Eta - elapsed);
            o.Flown.Clear(); o.Points.Clear();
            for (int i = 0; i < next && i < r.Track.Count; i++) o.Flown.Add(r.Track[i].P);
            double left = 0; var a = o.Pos;
            for (int i = next; i < r.Track.Count; i++) { o.Points.Add(r.Track[i].P); left += V3.Distance(a, r.Track[i].P); a = r.Track[i].P; }
            o.Dist = left;
            snap.Others.Add(o);
        }
    }
}
