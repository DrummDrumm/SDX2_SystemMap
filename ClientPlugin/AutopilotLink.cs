using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using IngamePB = Sandbox.ModAPI.Ingame.IMyProgrammableBlock;

namespace SDX2_SystemMap;

/// <summary>
/// The link to the programmable-block autopilot (PB Autopilot\Protogen Systems - Autopilot.cs) on your ship.
///  - Orders go into the [SDX2 Route] section of its Custom Data (RouteExport format, the rest is kept). The game syncs
///    Custom Data you set on your own blocks to the server, and the script reads it on its own update (no Run call).
///  - The script answers in its [SDX2 Autopilot] section (state, the order id it acted on, distance, speed, ETA), which
///    syncs back. A PB counts as an autopilot once its Custom Data has a [SDX2 Route] line; the script adds one.
/// </summary>
internal static class AutopilotLink
{
    private static readonly List<IngamePB> pbs = new List<IngamePB>();
    private static readonly List<IMyTerminalBlock> autopilots = new List<IMyTerminalBlock>();
    private static double nextScan;
    private static long gridId;
    private static int lastId;
    private static string lastCommand;
    private static double sentAt = -1;

    /// <summary>Autopilot PBs on the ship you control (rescanned every 2 s).</summary>
    public static int Count => autopilots.Count;
    /// <summary>The first autopilot's own report, or null.</summary>
    public static RouteExport.Status Status { get; private set; }
    /// <summary>The route the first autopilot holds (its [SDX2 Route] section), or null.</summary>
    public static RouteExport.Parsed Route { get; private set; }
    /// <summary>The last order we sent this session, and whether the PB has acted on it yet.</summary>
    public static string LastCommand => lastCommand;
    /// <summary>Whether this order id is the last one this session sent (so the plugin knows that route already).</summary>
    public static bool SentHere(int id) => id != 0 && (id == lastId || sentIds.Contains(id));
    /// <summary>Every order id this session wrote (an older one can still be in the PB's copy we last read).</summary>
    private static readonly HashSet<int> sentIds = new HashSet<int>();
    public static bool Pending => lastId != 0 && (Status == null || Status.Ack != lastId);

    /// <summary>Flying (or just told to fly and not answered yet, for up to 10 s).</summary>
    public static bool Engaged(double now) =>
        Pending && (lastCommand == "engage" || lastCommand == "update") && now - sentAt < 10 ? true
        : Pending && (lastCommand == "stop" || lastCommand == "brake" || lastCommand == "manual") && now - sentAt < 10 ? false
        : Status != null && Status.Flying;

    public static void Reset()
    {
        autopilots.Clear(); Status = null; Route = null; gridId = 0; lastId = 0; sentIds.Clear(); lastCommand = null; sentAt = -1; nextScan = 0;
    }

    /// <summary>The ship you fly (0: none), and whether its autopilot has answered and isn't on a route.</summary>
    public static long FlownShip => ControlledGrid()?.EntityId ?? 0;
    public static bool FlownIdle => Status != null && !Pending && !Status.Flying && !Status.Manual;

    private static MyCubeGrid ControlledGrid() =>
        Sandbox.Game.World.MySession.Static?.ControlledEntity?.Entity?.GetTopMostParent() as MyCubeGrid;

    public static void Update(double now)
    {
        if (now < nextScan) return;
        nextScan = now + 2;
        autopilots.Clear();
        var grid = ControlledGrid();
        if (grid == null) { Status = null; Route = null; return; }
        if (grid.EntityId != gridId) { gridId = grid.EntityId; lastId = 0; lastCommand = null; }
        pbs.Clear();
        try { MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid)?.GetBlocksOfType(pbs); }
        catch { return; }
        foreach (var pb in pbs)
            if (pb is IMyTerminalBlock tb && tb.IsFunctional && RouteExport.HasSection(tb.CustomData) && pb.HasLocalPlayerAccess())
                autopilots.Add(tb);
        Status = autopilots.Count > 0 ? RouteExport.ParseStatus(autopilots[0].CustomData) : null;
        Route = autopilots.Count > 0 && RouteExport.Parse(autopilots[0].CustomData, out var r) ? r : null;
    }

    /// <summary>Sends the route with a command (engage | update | load); returns how many PBs got it, -1 if there's no ship or route.</summary>
    public static int Send(RoutePath path, string command, IList<V3> passes, double thrust, double brake, out string error)
    {
        error = null;
        if (path == null) { error = "No route: pick a destination first."; return -1; }
        List<int> marks = null;
        int n = Write(id => RouteExport.Build(path, id, out marks, command, passes, thrust, brake), command, out error);
        if (n > 0) { sentMarks = marks; sentMarksCount = path.Waypoints.Count; }
        // the plan as sent, for the map's ship routes layer once you have left this ship
        if (n > 0 && command != "load" && ControlledGrid() is MyCubeGrid g) FleetRoutes.Started(g.EntityId, g.DisplayName, path);
        return n;
    }

    private static List<int> sentMarks;
    private static int sentMarksCount;
    /// <summary>The map route's revision the last route sent was made from (set by the sender).</summary>
    public static int SentRevision = -1;

    /// <summary>
    /// How many of the map's route waypoints the PB has put behind it, from its "wp" report on the route sent last
    /// (-1 when it isn't flying that one, or the map's route has since changed size).
    /// </summary>
    public static int PassedOnRoute(int waypointCount, int revision)
    {
        var st = Status;
        if (st == null || !st.Flying || Pending || sentMarks == null || st.Ack != lastId || waypointCount != sentMarksCount || revision != SentRevision) return -1;
        if (st.Wp < 0 || st.Wp >= sentMarks.Count || st.Count != sentMarks.Count) return -1;
        return sentMarks[st.Wp];
    }

    public static int Stop(out string error) => Write(RouteExport.BuildStop, "stop", out error);
    /// <summary>CONFIRM MANUAL: the pilot took over a route and keeps the ship; the PB drops its hold on the route.</summary>
    public static int ConfirmManual(out string error) => Write(id => RouteExport.BuildCommand(id, "manual"), "manual", out error);
    /// <summary>FULL STOP: the PB burns the ship to a standstill, then switches the dampeners on and lets go.</summary>
    public static int FullStop(out string error) => Write(id => RouteExport.BuildCommand(id, "brake"), "brake", out error);
    /// <summary>When the last order went (Plugin.Now), -1 for none.</summary>
    public static double SentAt => sentAt;

    /// <summary>Braking to a standstill (or just told to, and not answered yet, for up to 10 s).</summary>
    public static bool Braking(double now) =>
        Pending && lastCommand == "brake" && now - sentAt < 10 ? true : Status != null && Status.Braking;

    private static int Write(Func<int, string> section, string command, out string error)
    {
        error = null;
        if (PlayGuard.Blocked) { error = SidePreview.Paused; return -1; }
        if (ControlledGrid() == null) { error = "Sit in your ship's cockpit to use the autopilot."; return -1; }
        nextScan = 0; Update(Plugin.Now);
        int id = NewId();
        string text = section(id);
        int sent = 0;
        foreach (var tb in autopilots)
        {
            try { tb.CustomData = RouteExport.Merge(tb.CustomData, text); sent++; }
            catch (Exception e) { error = "Can't write to " + tb.CustomName + ": " + e.Message; }
        }
        if (sent == 0) error ??= "No autopilot PB on this ship: load the Protogen Systems - Autopilot script into a programmable block.";
        else
        {
            lastId = id; lastCommand = command; sentAt = Plugin.Now; sentIds.Add(id);
            if (command == "stop" || command == "brake" || command == "manual") FleetRoutes.Ended(ControlledGrid()?.EntityId ?? 0);
            // the copy read before writing is stale now: read the route back, so the old one isn't mistaken for a
            // route sent from somewhere else (and taken back over the new one) until the next scan
            Route = RouteExport.Parse(autopilots[0].CustomData, out var r) ? r : null;
        }
        return sent;
    }

    private static int NewId()
    {
        int id = (int)((DateTime.UtcNow.Ticks / (TimeSpan.TicksPerSecond / 10)) % int.MaxValue);
        return id <= lastId ? lastId + 1 : id;
    }
}
