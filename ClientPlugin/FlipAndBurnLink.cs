using System;
using System.Collections.Generic;
using System.Reflection;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using HarmonyLib;
using ProtoBuf;
using VRageMath;

namespace SDX2_SystemMap;

/// <summary>
/// The link to SDX2's Flip and Burn mod (workshop 2832248541).
///  - Its public mod API (channel 9806650): true high-speed velocity, whether the N list is open, opening it.
///  - Read-only reflection for what the API doesn't offer: the destination picked in the N list (Hud._selection),
///    whether its search box has focus, the grid's speed state and F&amp;B's own max acceleration (Extensions.GetThrustData).
/// Picking still happens only in the N list. Two small changes, both client side: the list stays open when you pick a
/// destination (a Harmony patch on NavigationMenu, setting KeepNavOpen), and the plugin can close it (MANUAL, ENGAGE).
/// </summary>
internal static class FlipAndBurnLink
{
    private const long Channel = 9806650;
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private static bool registered;
    public static bool ApiReady { get; private set; }
    private static Func<IMyCubeGrid, Vector3D> getVelocity;
    private static Func<bool> isNavMenuOpen;
    private static Action<bool, bool> setNavMenuOpen;
    private static Action<string, byte[]> setPoints;
    private const string Collection = "SDX2_SystemMap";
    private const long PoiIdBase = 0x5D2A000000000000L;

    // reflection (found once the mod's scripts are loaded)
    private static bool found;
    private static double nextSearch;
    private static FieldInfo sessionHud, hudSelection, hudNavMenu, sessionConfig;
    private static PropertyInfo menuSearching;
    private static MethodInfo getThrustData, getAs;
    private static Type speedControllerType;
    private static FieldInfo hudScanner, asteroidBox, asteroidSeed;
    private static MethodInfo scanPath;

    public static bool Found => found;

    public static void Load()
    {
        if (registered || MyAPIGateway.Utilities == null) return;
        registered = true;
        MyAPIGateway.Utilities.RegisterMessageHandler(Channel, OnMessage);
        MyAPIGateway.Utilities.SendModMessage(Channel, "init");
    }

    public static void Unload()
    {
        if (registered) try { MyAPIGateway.Utilities?.UnregisterMessageHandler(Channel, OnMessage); } catch { }
        registered = false; ApiReady = false; found = false; nextSearch = 0;
        Array.Clear(scanners, 0, Slots); Array.Clear(scannerPlanets, 0, Slots); scannerFailed = false;
        getVelocity = null; isNavMenuOpen = null; setNavMenuOpen = null; setPoints = null;
    }

    private static void OnMessage(object obj)
    {
        if (ApiReady || !(obj is Dictionary<string, Delegate> d)) return;
        try
        {
            getVelocity = d.TryGetValue("GetVelocity", out var a) ? a as Func<IMyCubeGrid, Vector3D> : null;
            isNavMenuOpen = d.TryGetValue("IsNavMenuOpen", out var b) ? b as Func<bool> : null;
            setNavMenuOpen = d.TryGetValue("SetNavMenuOpen", out var c) ? c as Action<bool, bool> : null;
            setPoints = d.TryGetValue("SetPointsOfInterest", out var e) ? e as Action<string, byte[]> : null;
            ApiReady = true;
            Plugin.Log("Flip and Burn API connected");
        }
        catch (Exception e) { Plugin.Log("Flip and Burn API failed: " + e.Message); }
    }

    /// <summary>Looks for the F&amp;B scripts every few seconds until found (mods compile while the world loads).</summary>
    public static void Search(double now)
    {
        if (found || now < nextSearch || MyScriptManager.Static == null) return;
        nextSearch = now + 5;
        try
        {
            foreach (var asm in MyScriptManager.Static.Scripts.Values)
            {
                var hud = asm.GetType("FlipAndBurn.Hud", false);
                var session = asm.GetType("FlipAndBurn.Session", false);
                if (hud == null || session == null) continue; // other mods carry copies of F&B's API in the same namespace
                hudSelection = hud.GetField("_selection", Any);
                sessionHud = session.GetField("Hud", Any);
                if (hudSelection == null || sessionHud == null) continue;
                hudNavMenu = hud.GetField("_navMenu", Any);
                menuSearching = asm.GetType("FlipAndBurn.NavigationMenu", false)?.GetProperty("Searching", Any);
                sessionConfig = session.GetField("Config", Any);
                getThrustData = asm.GetType("FlipAndBurn.Extensions", false)?.GetMethod("GetThrustData", Any, null, new[] { typeof(MyCubeGrid) }, null);
                speedControllerType = asm.GetType("FlipAndBurn.SpeedController", false);
                hudScanner = hud.GetField("_asteroidScanner", Any);
                scanPath = hudScanner?.FieldType.GetMethod("GetAsteroidInPath", Any, null, new[] { typeof(RayD), typeof(double) }, null);
                var astType = asm.GetType("FlipAndBurn.Asteroids.Asteroid", false);
                asteroidBox = astType?.GetField("BoundingBox", Any);
                asteroidSeed = astType?.GetField("Seed", Any);
                if (scanPath == null || asteroidBox == null) Plugin.Log("Flip and Burn's asteroid scanner wasn't found: no asteroid warnings");
                try
                {
                    foreach (var m in typeof(MyComponentBase).GetMethods())
                        if (speedControllerType != null && m.Name == "GetAs" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0) { getAs = m.MakeGenericMethod(speedControllerType); break; }
                }
                catch (Exception e) { Plugin.Log("Flip and Burn speed state unavailable: " + e.Message); }
                PatchKeepOpen(asm.GetType("FlipAndBurn.NavigationMenu", false));
                found = true;
                Plugin.Log("Flip and Burn found in " + asm.GetName().Name);
                return;
            }
        }
        catch (Exception e) { Plugin.Log("Flip and Burn search failed: " + e.Message); }
    }

    private static object Hud => found ? sessionHud.GetValue(null) : null;

    /// <summary>The destination picked in F&amp;B's N list.</summary>
    public static bool TryGetSelection(out string name, out Vector3D pos)
    {
        name = null; pos = default;
        try
        {
            var sel = Hud == null ? null : hudSelection.GetValue(Hud);
            if (sel == null) return false;
            var t = sel.GetType();
            name = t.GetProperty("Name", Any)?.GetValue(sel) as string ?? "Destination";
            // an asteroid ring is picked at its point nearest the camera
            var ring = t.GetField("SelectedPosition", Any)?.GetValue(sel) as Vector3D?;
            pos = ring ?? (Vector3D)t.GetProperty("Position", Any).GetValue(sel);
            return true;
        }
        catch { return false; }
    }

    public static bool CanScanAsteroids => found && scanPath != null && asteroidBox != null;

    /// <summary>
    /// Flip and Burn's own procedural-asteroid look-ahead (the one behind its "WARNING: Asteroid"): the first asteroid
    /// whose box the ray hits within <paramref name="length"/> metres (F&amp;B caps it at 80 generator cells).
    /// </summary>
    public static bool FindAsteroid(Vector3D from, Vector3D dir, double length, out BoundingBoxD box, out int seed, int slot = 0)
    {
        box = default; seed = 0;
        try
        {
            var scanner = OwnScanner(slot);
            if (scanner == null || dir.LengthSquared() < 1e-9 || length <= 0) return false;
            var hit = scanPath.Invoke(scanner, new object[] { new RayD(from, Vector3D.Normalize(dir)), length });
            if (hit == null) return false; // a null Asteroid? boxes to null
            box = (BoundingBoxD)asteroidBox.GetValue(hit);
            if (asteroidSeed != null) seed = (int)asteroidSeed.GetValue(hit);
            return true;
        }
        catch (Exception e) { Plugin.LogThrottled("asteroid scan failed: " + (e.InnerException ?? e).Message); return false; }
    }

    private const int Slots = 4;
    private static readonly object[] scanners = new object[Slots];
    private static readonly bool[] scannerPlanets = new bool[Slots];
    private static bool scannerFailed;

    /// <summary>Our own instance of F&amp;B's AsteroidScanner (same generator, same zones): F&amp;B's scanner keeps only the
    /// cells of its last call, so sharing it would make F&amp;B regenerate its cells every frame. One per ray we cast
    /// each scan (slot), for the same reason.</summary>
    private static object OwnScanner(int slot)
    {
        if (!CanScanAsteroids || scannerFailed || slot < 0 || slot >= Slots) return null;
        try
        {
            var own = scanners[slot] ??= Activator.CreateInstance(hudScanner.FieldType);
            if (!scannerPlanets[slot] && Hud != null)
            {
                // F&B hands its scanner the planets once (their gravity wells clear asteroids near them)
                var solar = Hud.GetType().GetField("_solarSystem", Any)?.GetValue(Hud);
                var planets = solar?.GetType().GetProperty("Planets", Any)?.GetValue(solar) ?? solar?.GetType().GetField("Planets", Any)?.GetValue(solar);
                if (planets is System.Collections.ICollection c && c.Count > 0)
                {
                    hudScanner.FieldType.GetMethod("SetPlanets", Any)?.Invoke(own, new[] { planets });
                    scannerPlanets[slot] = true;
                }
            }
            return own;
        }
        catch (Exception e)
        {
            scannerFailed = true;
            Plugin.Log("Can't make an asteroid scanner: " + (e.InnerException ?? e).Message);
            return null;
        }
    }

    public static bool IsNavMenuOpen()
    {
        try { return isNavMenuOpen?.Invoke() ?? false; } catch { return false; }
    }

    /// <summary>True while F&amp;B's search box takes typing (our key must not fire then).</summary>
    public static bool IsSearching()
    {
        try
        {
            var menu = Hud == null || hudNavMenu == null ? null : hudNavMenu.GetValue(Hud);
            return menu != null && menuSearching != null && (bool)menuSearching.GetValue(menu);
        }
        catch { return false; }
    }

    public static void OpenNavMenu()
    {
        try { setNavMenuOpen?.Invoke(true, false); } catch { }
    }

    /// <summary>Closes the N list (F&amp;B's own Close, as its close button does).</summary>
    public static void CloseNavMenu()
    {
        try
        {
            var menu = Hud == null || hudNavMenu == null ? null : hudNavMenu.GetValue(Hud);
            var close = menu?.GetType().GetMethod("Close", Any, null, Type.EmptyTypes, null);
            if (close != null) { close.Invoke(menu, null); return; }
            setNavMenuOpen?.Invoke(false, false);
        }
        catch (Exception e) { Plugin.LogThrottled("can't close Flip and Burn's list: " + (e.InnerException ?? e).Message); }
    }

    private static readonly HashSet<Type> patchedMenus = new HashSet<Type>();
    /// <summary>True when the keep-open patch is on this session's NavigationMenu.</summary>
    public static bool KeepOpenPatched { get; private set; }

    /// <summary>NavigationMenu.SelectEntry ends with Close(): skip that Close while KeepNavOpen is on.</summary>
    private static void PatchKeepOpen(Type menu)
    {
        KeepOpenPatched = false;
        if (menu == null || Plugin.Harmony == null) return;
        if (patchedMenus.Contains(menu)) { KeepOpenPatched = true; return; }
        try
        {
            var select = menu.GetMethod("SelectEntry", Any);
            if (select == null || select.GetParameters().Length != 1 || menu.GetField("OnSelect", Any) == null)
            { Plugin.Log("Flip and Burn's list has no SelectEntry/OnSelect: it will close when you pick"); return; }
            string odd = UnexpectedCalls(select);
            if (odd != null)
            {
                Plugin.Log("Flip and Burn's SelectEntry has changed (it now calls " + odd + "): not patched, so the list "
                    + "closes when you pick. Please report this so the plugin can be updated.");
                return;
            }
            Plugin.Harmony.Patch(select, prefix: new HarmonyMethod(typeof(KeepNavOpen), nameof(KeepNavOpen.SelectPrefix)));
            patchedMenus.Add(menu);
            KeepOpenPatched = true;
            Plugin.Log("Flip and Burn's list patched: it stays open on a pick");
        }
        catch (Exception e) { Plugin.Log("can't keep Flip and Burn's list open on a pick: " + e.Message); }
    }

    /// <summary>
    /// The prefix replaces SelectEntry (F&amp;B 2832248541: check Enabled, pick an asteroid ring's nearest point,
    /// OnSelect, Close), so it is only safe while the method still does just that. Returns the first call outside that
    /// list, or null when the shape matches.
    /// </summary>
    private static string UnexpectedCalls(MethodInfo select)
    {
        var known = new HashSet<string> { "get_Enabled", "get_PointOfInterest", "get_Ring", "ClosestPoint", "get_Session",
            "get_Camera", "get_Position", "set_SelectedPosition", ".ctor", "Invoke", "Close" };
        bool invoke = false, close = false;
        try
        {
            foreach (var op in PatchProcessor.ReadMethodBody(select))
            {
                if (!(op.Value is MethodBase m)) continue;
                if (!known.Contains(m.Name)) return m.DeclaringType?.Name + "." + m.Name;
                invoke |= m.Name == "Invoke";
                close |= m.Name == "Close";
            }
        }
        catch (Exception e) { return "unreadable IL: " + e.Message; }
        return invoke && close ? null : "no OnSelect/Close";
    }

    /// <summary>Puts our detour waypoints in F&amp;B's N list (replacing the last set; empty clears them). Client side only.</summary>
    public static bool SetDetours(IList<Waypoint> waypoints)
    {
        if (setPoints == null) return false;
        try
        {
            var list = new List<FnbPoi>();
            for (int i = 0; i < waypoints.Count; i++)
                if (!waypoints[i].User) // your own waypoints are already entries in the list
                list.Add(new FnbPoi { Id = PoiIdBase + i, Name = waypoints[i].Name, Position = new Vector3D(waypoints[i].Pos.X, waypoints[i].Pos.Y, waypoints[i].Pos.Z), IconSubtypeId = "PointOfInterest" });
            setPoints(Collection, MyAPIGateway.Utilities.SerializeToBinary(list));
            return true;
        }
        catch (Exception ex) { Plugin.LogThrottled("can't add detours to Flip and Burn's list: " + ex.Message); return false; }
    }

    private const string TargetCollection = "SDX2_SystemMap_Target";
    private const long TargetPoiId = PoiIdBase + 0x100000;

    /// <summary>
    /// Makes this point Flip and Burn's destination, as a click in the N list would (NavigationMenu.OnSelect → Hud's
    /// UpdateSelection). Uses F&amp;B's own entry when it has one here (a world, GPS, station marker or one of our detour
    /// entries); otherwise adds it to the list as a custom point first. Does nothing when it's already selected.
    /// </summary>
    public static bool SelectPoint(string name, Vector3D pos, out string error, string altName = null)
    {
        error = null;
        try
        {
            var hud = Hud;
            var menu = hud == null || hudNavMenu == null ? null : hudNavMenu.GetValue(hud);
            var onSelect = menu?.GetType().GetField("OnSelect", Any)?.GetValue(menu) as Delegate;
            if (onSelect == null) { error = "Flip and Burn's list isn't ready"; return false; }
            var poi = FindPoi(hud, name, pos, altName);
            if (poi == null && setPoints != null)
            {
                var one = new List<FnbPoi> { new FnbPoi { Id = TargetPoiId, Name = name, Position = pos, IconSubtypeId = "PointOfInterest" } };
                setPoints(TargetCollection, MyAPIGateway.Utilities.SerializeToBinary(one));
                poi = FindPoi(hud, name, pos, altName);
            }
            if (poi == null) FindPoi(hud, name, pos, altName, log: true);
            if (poi == null) { error = "Flip and Burn has no entry for " + name + (altName != null ? " (" + altName + ")" : ""); Plugin.LogThrottled(error); return false; }
            if (ReferenceEquals(hudSelection.GetValue(hud), poi)) return true; // selecting it again would clear it
            onSelect.DynamicInvoke(poi);
            return true;
        }
        catch (Exception e) { error = "Can't set Flip and Burn's destination: " + (e.InnerException ?? e).Message; return false; }
    }

    /// <summary>
    /// F&amp;B's list entry for a point (see <see cref="PoiMatch"/> for the rules). Scans its planets and asteroid zones
    /// (with their children), GPS points and custom points (both kept in dictionaries). On a miss, logs how many entries
    /// it saw (no names or positions).
    /// </summary>
    private static object FindPoi(object hud, string name, Vector3D pos, string altName = null, bool log = false)
    {
        var solar = hud.GetType().GetField("_solarSystem", Any)?.GetValue(hud);
        if (solar == null) { if (log) Plugin.LogThrottled("Flip and Burn's solar system not found"); return null; }
        var all = new List<object>();
        var seen = new HashSet<object>();
        void Add(object p)
        {
            if (p == null || !seen.Add(p)) return;
            all.Add(p);
            AddAll(p.GetType().GetField("Children", Any)?.GetValue(p));
        }
        void AddAll(object coll)
        {
            if (coll == null || coll is string) return;
            if (coll is System.Collections.IDictionary d) { foreach (var v in d.Values) Item(v); return; }
            if (coll is System.Collections.IEnumerable e) foreach (var p in e) Item(p);
        }
        void Item(object v)
        {
            if (v is System.Collections.IDictionary || v is System.Collections.IEnumerable && !(v is string)) AddAll(v);
            else Add(v);
        }
        var st = solar.GetType();
        foreach (var f in new[] { "Bodies", "Hierarchy", "AsteroidZones", "GpsPoints", "CustomPoints" }) AddAll(st.GetField(f, Any)?.GetValue(solar));
        object best = null; double bestD = double.MaxValue; int bestRank = PoiMatch.NoMatch;
        foreach (var p in all)
        {
            var pt = p.GetType();
            if (pt.Name.Contains("Signal") || pt.Name.Contains("Ring")) continue; // moving contacts; rings have no single point
            var pp = pt.GetProperty("Position", Any)?.GetValue(p);
            if (!(pp is Vector3D ppos)) continue;
            double d = Vector3D.Distance(ppos, pos);
            string pn = pt.GetProperty("Name", Any)?.GetValue(p) as string ?? "";
            int rank = PoiMatch.Rank(name, altName, pn, d);
            if (rank < bestRank || rank == bestRank && rank < PoiMatch.NoMatch && d < bestD) { best = p; bestD = d; bestRank = rank; }
        }
        if (best == null && log) Plugin.LogThrottled("Flip and Burn match: " + all.Count + " entries scanned, no entry near the point");
        return best;
    }

    public static Vector3D Velocity(IMyCubeGrid grid)
    {
        try { if (getVelocity != null) return getVelocity(grid); } catch { }
        return grid?.Physics?.LinearVelocity ?? Vector3D.Zero;
    }

    /// <summary>F&amp;B's own max acceleration for the grid's physical group (m/s²), 0 if unknown.</summary>
    public static double MaxAcceleration(MyCubeGrid grid)
    {
        try
        {
            if (getThrustData == null || grid == null) return 0;
            var data = getThrustData.Invoke(null, new object[] { grid });
            var t = data.GetType();
            if (!(bool)t.GetField("IsValid").GetValue(data)) return 0;
            return (double)t.GetField("MaxAcceleration").GetValue(data);
        }
        catch { return 0; }
    }

    /// <summary>F&amp;B's speed cap (Settings.MaxSpeed), or 50 km/s.</summary>
    public static double MaxSpeed()
    {
        try
        {
            var sync = sessionConfig?.GetValue(null);
            var settings = sync?.GetType().GetProperty("Value", Any)?.GetValue(sync);
            var v = settings?.GetType().GetField("MaxSpeed", Any)?.GetValue(settings);
            if (v is double d && d > 0) return d;
        }
        catch { }
        return RoutePlan.DefaultMaxSpeed;
    }

    /// <summary>The grid's F&amp;B speed state ("HighSpeed", "Normal", ...), or "".</summary>
    public static string State(IMyCubeGrid grid)
    {
        try
        {
            if (getAs == null || grid?.GameLogic == null) return "";
            var ctrl = getAs.Invoke(grid.GameLogic, null);
            var sync = ctrl?.GetType().GetField("State", Any)?.GetValue(ctrl);
            return sync?.GetType().GetProperty("Value", Any)?.GetValue(sync)?.ToString() ?? "";
        }
        catch { return ""; }
    }
}

/// <summary>
/// Harmony prefix replacing F&amp;B's NavigationMenu.SelectEntry (a pick in the N list). Its own body is
/// "ring point; OnSelect(poi); Close()", and Close() is small enough for the JIT to inline, so a patch on Close never
/// runs: the prefix does the same work itself, minus the Close. While <see cref="Intercept"/> wants the pick (adding
/// waypoints), the point goes to it instead and F&amp;B's destination stays as it was.
/// </summary>
internal static class KeepNavOpen
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    /// <summary>Called with a pick (name, position); returns true when it took the pick as a waypoint.</summary>
    public static Func<string, Vector3D, bool> Intercept;

    public static bool SelectPrefix(object __instance, object entry)
    {
        try
        {
            if (entry == null || __instance == null || PlayGuard.Blocked) return true;
            var et = entry.GetType();
            if (et.GetProperty("Enabled", Any)?.GetValue(entry) is bool on && !on) return false;
            var poi = et.GetField("PointOfInterest", Any)?.GetValue(entry);
            if (poi == null) return true;
            var pt = poi.GetType();
            var cam = MyAPIGateway.Session?.Camera?.Position ?? Vector3D.Zero;
            Vector3D pos = (Vector3D)pt.GetProperty("Position", Any).GetValue(poi);
            var sel = pt.GetField("SelectedPosition", Any);
            var ring = pt.GetProperty("Ring", Any)?.GetValue(poi);
            if (sel != null && ring != null)
            {
                pos = (Vector3D)ring.GetType().GetMethod("ClosestPoint", new[] { typeof(Vector3D) }).Invoke(ring, new object[] { cam });
                sel.SetValue(poi, (Vector3D?)pos);
            }
            var name = pt.GetProperty("Name", Any)?.GetValue(poi) as string ?? "Point";
            if (Intercept != null && Intercept(name, pos)) return false;
            if (!Plugin.Settings.KeepNavOpen) return true;
            (__instance.GetType().GetField("OnSelect", Any)?.GetValue(__instance) as Delegate)?.DynamicInvoke(poi);
            return false;
        }
        catch (Exception e)
        {
            Plugin.LogThrottled("keep-open pick failed, Flip and Burn handles it: " + (e.InnerException ?? e).Message);
            return true;
        }
    }
}

/// <summary>Same wire format as F&amp;B's PointOfInterestData (protobuf fields 1-4).</summary>
[ProtoContract]
public struct FnbPoi
{
    [ProtoMember(1)] public long Id;
    [ProtoMember(2)] public string Name;
    [ProtoMember(3)] public Vector3D Position;
    [ProtoMember(4)] public string IconSubtypeId;
}
