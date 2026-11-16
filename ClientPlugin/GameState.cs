using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using VRage.Game.ModAPI;
using VRageMath;

namespace SDX2_SystemMap;

/// <summary>Reads the ship, your GPS list and Flip and Burn into a MapSnapshot. Slow parts are refreshed once a second.</summary>
internal static class GameState
{
    private static readonly List<IMyGps> gps = new List<IMyGps>();
    private static double nextSlow;
    private static double accel, maxSpeed = RoutePlan.DefaultMaxSpeed, driveWeight;
    private static string state = "";

    private static V3 V(Vector3D v) => new V3(v.X, v.Y, v.Z);

    public static void Fill(MapSnapshot s, double now)
    {
        var session = MySession.Static;
        s.HasShip = false; s.InCockpit = false;
        if (session == null) return;
        var controlled = session.ControlledEntity;
        var top = controlled?.Entity?.GetTopMostParent();
        var grid = top as MyCubeGrid;
        bool slow = now >= nextSlow;
        if (slow) nextSlow = now + 1;

        if (top != null)
        {
            s.HasShip = true;
            s.InCockpit = controlled is MyShipController;
            s.ShipName = grid != null ? grid.DisplayName : "You";
            s.ShipPos = V(top.PositionComp.GetPosition());
            s.ShipVel = grid != null ? V(FlipAndBurnLink.Velocity(grid)) : V(top.Physics?.LinearVelocity ?? Vector3.Zero);
            if (slow)
            {
                accel = grid != null ? FlipAndBurnLink.MaxAcceleration(grid) : 0;
                state = grid != null ? FlipAndBurnLink.State(grid) : "";
                maxSpeed = FlipAndBurnLink.MaxSpeed();
                driveWeight = ReadSignature(grid, s.Signature);
            }
        }
        s.MaxAccel = accel; s.FnbState = state; s.MaxSpeed = maxSpeed; s.DriveWeight = driveWeight;
        SpectrumLink.Search(now);
        SpectrumLink.ReadLive(grid, s.Signature);
        s.FnbReady = FlipAndBurnLink.Found || FlipAndBurnLink.ApiReady;

        if (FlipAndBurnLink.TryGetSelection(out var name, out var pos)) { s.FnbTargetName = name; s.FnbTarget = V(pos); }
        else s.FnbTargetName = null;

        if (slow)
        {
            s.Gps.Clear();
            gps.Clear();
            try { session.Gpss?.GetGpsList(session.LocalPlayerId, gps); } catch { }
            foreach (var g in gps)
            {
                if (!g.ShowOnHud) continue;
                s.Gps.Add(new MapSnapshot.Marker { Name = g.Name, Pos = V(g.Coords), Color = new MapColor(g.GPSColor.R, g.GPSColor.G, g.GPSColor.B) });
            }
        }
    }

    /// <summary>
    /// The grid's Spectrum signature parts, as Spectrum's Emitter collects them: working fusion drives (weight, gain),
    /// and the strongest other all-round emission (antenna/beacon broadcast, power draw). Returns the drive weight.
    /// </summary>
    private static double ReadSignature(MyCubeGrid grid, SignatureInfo sig)
    {
        double w = 0, gw = 0, floor = 0;
        string what = null;
        if (grid != null)
        {
            try
            {
                foreach (var b in grid.GetFatBlocks())
                {
                    if (b is Sandbox.Game.Entities.MyThrust t)
                    {
                        if (!t.IsWorking) continue;
                        var sub = t.BlockDefinition.Id.SubtypeName;
                        double k = DriveSignature.Weight(sub);
                        w += k; gw += k * DriveSignature.Gain(sub);
                        continue;
                    }
                    if (!b.IsWorking) continue;
                    double r = 0; string name = null;
                    if (b is Sandbox.ModAPI.IMyRadioAntenna a) { if (a.IsBroadcasting) { r = a.Radius; name = "antenna"; } }
                    else if (b is Sandbox.ModAPI.IMyBeacon bc) { r = bc.Radius; name = "beacon"; }
                    if (r > 0)
                    {
                        double st = DriveSignature.StrengthForKm(r / 1000);
                        if (st > floor) { floor = st; what = name + " broadcast"; }
                    }
                }
                var dist = ((IMyCubeGrid)grid).ResourceDistributor;
                if (dist != null && grid.IsPowerSwitchOn)
                {
                    double mw = dist.TotalRequiredInputByType(Sandbox.Game.EntityComponents.MyResourceDistributorComponent.ElectricityId, grid);
                    if (mw >= 1 && mw * 1e6 > floor) { floor = mw * 1e6; what = "power draw " + mw.ToString("0") + " MW"; }
                }
            }
            catch (System.Exception e) { Plugin.LogThrottled("signature read failed: " + e.Message); }
        }
        sig.Weight = w; sig.Gain = w > 0 ? gw / w : 4; sig.Floor = floor; sig.FloorWhat = what;
        return w;
    }
}
