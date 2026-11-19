using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.Game.World;
using Sandbox.Graphics;
using Sandbox.Graphics.GUI;
using SharpDX.DXGI;
using VRage.Game.Entity;
using VRage.Generics;
using VRage.Render.Scene;
using VRage.Render11.Common;
using VRage.Render11.LightingStage.EnvironmentProbe;
using VRage.Render11.Render;
using VRage.Render11.Resources;
using VRage.Render11.Resources.Internal;
using VRageMath;
using VRageRender;
using VRageRender.Messages;

namespace SDX2_SystemMap;

/// <summary>
/// The destination view: within the set range (up to 30 km) of the route's destination a small live window comes up, looking at it from your
/// ship. How: straight after the game draws its 3D scene (a postfix on MyRender11.DrawGameScene, on the render thread)
/// the camera and resolution are swapped to the view's, the game's untouched DrawGameScene (a Harmony reverse patch) runs
/// into a texture of ours, everything is put back, and the picture is stretched into the window. The frame and labels are
/// drawn after, on top. (The same way the Protogen Systems Docking Solutions PiP works.)
///
/// What it may show, the same rules as the docking PiP:
///  - Only what your own client already has: the game's own scene, nothing asked of the server and nothing sent.
///  - The camera sits on your own ship's hull, on the side facing the destination (just outside the outermost block on
///    the line from the ship's centre to it), where a camera block could be, so it never looks through blocks. With
///    the destination astern (a braking burn) it sits beside the stern instead, clear of the drives' exhaust.
///  - It rides the ship but aims at the destination itself, so the ship's small turns don't shake the picture.
///  - Its lens zooms no further than a camera block's (0.1 rad at the narrowest).
///  - Nothing is rendered while the plugin is paused (admin mode, creative tools, spectator, AFK: <see cref="PlayGuard"/>),
///    and the window steps aside for another view (a camera block, a turret), a cutscene, the hidden HUD, the full map
///    and menus.
/// Flares, shadow-map, environment-probe and highlight updates, offscreen LCD rendering and LOD changes are skipped for
/// its pass; it renders 20 times a second at up to 540 px tall, without SSAO and bloom.
/// </summary>
internal static class DestinationView
{
    private static readonly int[] Heights = { 180, 270, 360, 450, 540, 720, 900, 1080 };
    /// <summary>Ultra drawing quality: the picture at up to 1080 lines and 30 frames a second (540 and 20 otherwise).</summary>
    public static bool Ultra;
    private static int Fps => Ultra ? 30 : 20;
    private static int MaxHeight => Ultra ? 1080 : 540;

    // ------------------------------------------------------------------ the game thread

    private static int ticksWithoutDraw;
    private static long targetId;
    private static string targetFor;
    private static double nextFind, nextSpot;
    private static Vector3D spotLocal, spotDir, sideAxis;
    private static bool spotOk, astern;
    private static readonly List<MyEntity> found = new List<MyEntity>();
    private static readonly List<Vector3I> cells = new List<Vector3I>();
    private static readonly GameCanvas canvas = new GameCanvas();

    /// <summary>The window on screen this frame (empty when not shown).</summary>
    public static Box Shown { get; private set; }

    public static void Reset()
    {
        Clear();
        targetId = 0; targetFor = null; nextFind = nextSpot = 0; spotOk = false; Shown = default;
    }

    /// <summary>Every update: no draw for two ticks (another screen, paused...) = no picture.</summary>
    public static void Tick()
    {
        if (++ticksWithoutDraw >= 2) { Clear(); Shown = default; }
    }

    /// <summary>After the game screen is drawn (main thread): the picture for the render thread, and the frame and labels.</summary>
    public static void Draw(bool inGame)
    {
        ticksWithoutDraw = 0;
        Shown = default;
        try
        {
            var s = Plugin.Settings;
            var path = SidePreview.Path;
            var cam = MySector.MainCamera;
            if (!Wanted(inGame, out var grid, out var dest, out double dist) || cam == null) { Clear(); return; }

            double now = Plugin.Now;
            var target = Target(path.TargetName, dest, grid, now);
            var aim = target?.PositionComp.WorldVolume.Center ?? dest;
            // no grid there: frame the world when it's one (its whole disc), else a point
            double radius = target?.PositionComp.WorldVolume.Radius ?? WorldAt(path.Target)?.Radius ?? 0;
            var full = MyGuiManager.GetFullscreenRectangle();
            // in the map preview's place when it gave it up this frame, else in its own corner
            var box = SidePreview.GivenUp is Box given ? given
                : DestView.Window(full.Width, full.Height, s.DestViewSize, s.DestViewCorner, s.PipCorner, SidePreview.PipOnScreen);
            Shown = box;
            float u = DestView.Unit(box), cutS = MapScene.CutSmall(u), cutB = MapScene.CutBig(u);
            var pic = DestView.Picture(box, u, cutS, cutB);

            string why = null;
            uint actor = grid.Render?.GetRenderObjectID() ?? uint.MaxValue;
            if (failed != null) why = "VIEW UNAVAILABLE (SEE LOG)";
            else if (actor == uint.MaxValue) why = "NO PICTURE";
            MatrixD world = MatrixD.Identity;
            if (why == null && !CameraSpot(grid, aim, now, out world)) why = "NO CLEAR SPOT ON THE HULL";
            float fov = (float)DestView.FitFov(radius, Vector3D.Distance(world.Translation, aim));
            if (why == null && pic.W >= 16 && pic.H >= 16)
                Volatile.Write(ref request, new Request
                {
                    Actor = actor,
                    Local = Vector3D.Transform(world.Translation, MatrixD.Invert(grid.WorldMatrix)), Aim = aim,
                    Fov = fov, Near = cam.NearPlaneDistance, Far = cam.FarPlaneDistance, FarFar = cam.FarFarPlaneDistance,
                    Rect = new RectangleF(pic.X, pic.Y, pic.W, pic.H), Screen = new Vector2(full.Width, full.Height),
                });
            else Clear();

            // the themed frame, its labels and the overlay (round and over the picture, which the render thread draws earlier
            // in the frame; nothing is filled over it)
            bool picture = why == null && Volatile.Read(ref ready);
            DestView.DrawWindow(canvas, box, MapTheme.Current, path.TargetName, dist, s.OverlayOf(MapTheme.Current), s.FeedNoise, now,
                picture, why, target != null || WorldAt(path.Target) != null);
        }
        catch (Exception e) { Clear(); Plugin.LogThrottled("destination view failed: " + e.Message); }
    }

    /// <summary>
    /// Whether the view is up this frame: switched on, a route, in your ship's seat with its own view, nothing pausing
    /// or covering it, and within the set range of the destination. The map preview asks too (to give up its place).
    /// </summary>
    public static bool Wanted(bool inGame, out MyCubeGrid grid, out Vector3D dest, out double dist)
    {
        grid = null; dest = default; dist = double.NaN;
        try
        {
            var s = Plugin.Settings;
            var path = SidePreview.Path;
            var controller = MySession.Static?.ControlledEntity as MyShipController;
            grid = controller?.CubeGrid;
            if (!s.DestView || path == null || grid == null || (PlayGuard.Blocked && !PlayGuard.ViewOnly) || !inGame || MapScreen.IsOpen
                || MyHud.CutsceneHud || !MyHud.IsVisible || OtherView(controller)) return false;
            dest = new Vector3D(path.Target.X, path.Target.Y, path.Target.Z);
            var c = grid.PositionComp.WorldVolume.Center;
            var here = new V3(c.X, c.Y, c.Z);
            // a world: to its surface, and to where the route stops (outside its well)
            double toStop;
            try { toStop = V3.Distance(here, RouteExport.StopPoint(path, out _)); } catch { toStop = double.NaN; }
            dist = DestView.Nearness(Vector3D.Distance(c, dest), toStop, WorldAt(path.Target)?.Radius ?? 0);
            return DestView.Shown(true, true, dist, s.DestViewRangeKm * 1000);
        }
        catch { return false; }
    }

    public static bool Wanted(bool inGame) => Wanted(inGame, out _, out _, out _);

    /// <summary>The map's world (a planet, moon or asteroid) the destination is the centre of; null for anything else.</summary>
    private static SystemData.World WorldAt(V3 p)
    {
        foreach (var w in SystemData.Worlds)
            if (!w.IsStar && !w.IsObstacle && !w.IsField && w.Radius > 0 && V3.Distance(p, w.Pos) < Math.Max(1, w.Radius * 0.01)) return w;
        return null;
    }

    /// <summary>Another view has the screen: a camera block, a turret, a spectator (anything but the seat you're in).</summary>
    private static bool OtherView(MyShipController controller)
    {
        try
        {
            var cc = MySession.Static?.CameraController as MyEntity;
            return cc != null && cc.EntityId != controller.EntityId;
        }
        catch { return true; }
    }

    /// <summary>
    /// The grid at the destination: the biggest one loaded within 2.5 km of its point (not your own ship or anything
    /// docked or joined to it). Looked for twice a second; null when there's none (a point in space, a world, a grid this
    /// client hasn't got).
    /// </summary>
    private static MyCubeGrid Target(string name, Vector3D dest, MyCubeGrid mine, double now)
    {
        if (name != targetFor) { targetFor = name; targetId = 0; nextFind = 0; }
        if (now >= nextFind)
        {
            nextFind = now + 0.5;
            targetId = 0;
            var sphere = new BoundingSphereD(dest, 2500);
            found.Clear();
            MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, found);
            double best = 0;
            foreach (var e in found)
            {
                if (!(e is MyCubeGrid g) || g.Closed || g.MarkedForClose || g == mine || mine.IsInSameLogicalGroupAs(g)) continue;
                double r = g.PositionComp.WorldVolume.Radius;
                if (r > best) { best = r; targetId = g.EntityId; }
            }
            found.Clear();
        }
        return targetId != 0 && MyEntities.TryGetEntityById(targetId, out MyEntity t) && t is MyCubeGrid tg && !tg.Closed ? tg : null;
    }

    /// <summary>
    /// The camera: on your ship's hull facing the destination. A line from just outside the ship toward its centre stops
    /// at the first block; the camera goes one block short of it, on that line (so in empty space, never inside or
    /// behind a block), and looks at the destination. With the destination astern, within 60 degrees of the fusion
    /// drives' exhaust, that spot would be among the drive bells and in the plume: the camera then sits level with the
    /// stern and out to one side of the hull (the side the destination leans to), looking back past the exhaust. The
    /// spot is kept relative to the ship and found again every two seconds or when the destination's direction swings.
    /// </summary>
    private static bool CameraSpot(MyCubeGrid grid, Vector3D aim, double now, out MatrixD world)
    {
        world = MatrixD.Identity;
        var gm = grid.WorldMatrix;
        var centre = grid.PositionComp.WorldVolume.Center;
        var dir = aim - centre;
        if (dir.LengthSquared() < 1) return false;
        dir.Normalize();
        var dirLocal = Vector3D.TransformNormal(dir, MatrixD.Transpose(gm));
        if (!spotOk || now >= nextSpot || Vector3D.Dot(dirLocal, spotDir) < 0.999)
        {
            nextSpot = now + 2;
            spotDir = dirLocal;
            spotOk = false;
            double r = grid.PositionComp.WorldVolume.Radius, step = grid.GridSize;
            var exhaust = Exhaust(grid);
            double aft = Vector3D.Dot(dirLocal, exhaust);
            astern = aft > (astern ? 0.4 : 0.5); // a little slack, so it doesn't hop between the two spots
            if (astern)
            {
                // the grid axis across the drives that leans most toward the destination; the last one stays unless another clearly leads
                Vector3D side = Vector3D.Zero; double best = double.MinValue;
                foreach (var axis in Base6Directions.Directions)
                {
                    var a = (Vector3D)axis;
                    if (Math.Abs(Vector3D.Dot(a, exhaust)) > 0.5) continue;
                    double score = Vector3D.Dot(a, dirLocal) + (a == sideAxis ? 0.15 : 0);
                    if (score > best) { best = score; side = a; }
                }
                sideAxis = side;
                var box = grid.PositionComp.LocalAABB;
                Vector3D half = box.HalfExtents;
                double alongHalf = Math.Abs(exhaust.X) * half.X + Math.Abs(exhaust.Y) * half.Y + Math.Abs(exhaust.Z) * half.Z;
                double sideHalf = Math.Abs(side.X) * half.X + Math.Abs(side.Y) * half.Y + Math.Abs(side.Z) * half.Z;
                spotLocal = (Vector3D)box.Center + exhaust * alongHalf + side * (sideHalf + Math.Max(2 * step, 5));
                spotOk = true;
            }
            else
            {
                var from = centre + dir * (r + 2 * step);
                cells.Clear();
                grid.RayCastCells(from, centre, cells);
                double hit = double.MaxValue;
                foreach (var c in cells)
                {
                    if (!grid.CubeExists(c)) continue;
                    double t = Vector3D.Dot(from - grid.GridIntegerToWorld(c), dir); // along the line, in from the start
                    if (t < hit) hit = t;
                }
                cells.Clear();
                if (hit == double.MaxValue) return false; // no block on the line: no hull to sit on
                var spot = from - dir * Math.Max(0, hit - step);
                spotLocal = Vector3D.Transform(spot, MatrixD.Invert(gm));
                spotOk = true;
            }
        }
        var pos = Vector3D.Transform(spotLocal, gm);
        var fwd = aim - pos;
        if (fwd.LengthSquared() < 1) return false;
        fwd.Normalize();
        var up = Math.Abs(Vector3D.Dot(fwd, gm.Up)) > 0.98 ? gm.Backward : gm.Up;
        up = Vector3D.Normalize(up - fwd * Vector3D.Dot(up, fwd));
        world = MatrixD.CreateWorld(pos, fwd, up);
        return true;
    }

    /// <summary>Which way the fusion drives' exhaust goes, as a grid axis (zero with no fusion drive).</summary>
    private static Vector3D Exhaust(MyCubeGrid grid)
    {
        foreach (var b in grid.GetFatBlocks())
            if (b is MyThrust t && (t.BlockDefinition?.Id.SubtypeName ?? "").StartsWith("sdx_drive", StringComparison.OrdinalIgnoreCase))
                return (Vector3D)t.ThrustForwardVector;
        return Vector3D.Zero;
    }

    // ------------------------------------------------------------------ hand-over to the render thread

    private sealed class Request
    {
        public uint Actor;           // your grid's render object
        public Vector3D Local;       // the camera's place relative to it
        public Vector3D Aim;         // what it looks at, in the world
        public float Fov, Near, Far, FarFar;
        public RectangleF Rect;      // the window, in GUI pixels from the screen's top-left
        public Vector2 Screen;       // the screen's size, in the same pixels
    }

    private static Request request;
    private static bool ready;

    private static void Clear()
    {
        if (Volatile.Read(ref request) != null) Volatile.Write(ref request, null);
    }

    // ------------------------------------------------------------------ hooking the renderer

    private static readonly Harmony harmony = new Harmony("SDX2_SystemMap.DestinationView");
    private static bool patched;
    private static string failed;

    /// <summary>Once, from Plugin.Init (before the first frame, so no patch lands while the renderer runs).</summary>
    public static void Init()
    {
        if (patched) return;
        try
        {
            var draw = AccessTools.Method(typeof(MyRender11), "DrawGameScene");
            if (draw == null) throw new MissingMethodException("MyRender11.DrawGameScene");
            harmony.CreateReversePatcher(draw, new HarmonyMethod(typeof(DestinationView), nameof(DrawGameSceneOriginal))).Patch();
            harmony.Patch(draw, postfix: new HarmonyMethod(typeof(DestinationView), nameof(DrawGameScenePostfix)));
            var skip = new HarmonyMethod(typeof(DestinationView), nameof(SkipWhileDrawing));
            foreach (var m in new[]
                     {
                         AccessTools.Method(typeof(MyEnvironmentProbe), nameof(MyEnvironmentProbe.UpdateProbe)),
                         AccessTools.Method(typeof(MyEnvironmentProbe), nameof(MyEnvironmentProbe.UpdateCullQuery)),
                         AccessTools.Method(typeof(MyEnvironmentProbe), nameof(MyEnvironmentProbe.FinalizeEnvProbes)),
                         AccessTools.Method(typeof(MyOffscreenRenderer), nameof(MyOffscreenRenderer.Render)),
                         AccessTools.Method(typeof(MyHighlight), nameof(MyHighlight.CopyDepthStencil)),
                         AccessTools.Method(typeof(MyHighlight), nameof(MyHighlight.Run)),
                         // billboards: Flip and Burn's squares and other mods' lines drawn in the world round your ship, which the
                         // narrow lens blows up into bands across the picture; drive flames and light glare go with them
                         AccessTools.Method(typeof(MyBillboardRenderer), "Render"),
                         AccessTools.Method(typeof(MyShadowCascades), nameof(MyShadowCascades.Gather),
                             new[] { typeof(VRage.Render11.RenderContext.MyRenderContext), typeof(MyCommon.MyScreenLayout).MakeByRefType(), typeof(ISrvBindable), typeof(int) }),
                     })
            {
                if (m == null) { Plugin.Log("destination view: a renderer method to skip wasn't found (the view may cost more)"); continue; }
                harmony.Patch(m, prefix: skip);
            }
            var prepare = AccessTools.Method(typeof(MyShadowCascades), nameof(MyShadowCascades.PrepareQueries));
            if (prepare != null) harmony.Patch(prepare, prefix: new HarmonyMethod(typeof(DestinationView), nameof(PrepareQueriesPrefix)));
            var depth = AccessTools.Method(typeof(MyDepthStencilManager), nameof(MyDepthStencilManager.CreateDepthStencil));
            if (depth != null) harmony.Patch(depth, transpiler: new HarmonyMethod(typeof(DestinationView), nameof(DepthStencilTranspiler)));
            patched = true;
        }
        catch (Exception e)
        {
            failed = e.GetType().Name;
            Plugin.Log("destination view: hooking the renderer failed, the view will say so: " + e);
            try { harmony.UnpatchAll(harmony.Id); } catch { }
        }
    }

    public static void Dispose()
    {
        try { harmony.UnpatchAll(harmony.Id); } catch { }
        patched = false;
    }

    /// <summary>The game's own DrawGameScene, without anyone's patches (filled in by the reverse patch).</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DrawGameSceneOriginal(IRtvBindable renderTarget, out IBorrowedRtvTexture debugAmbientOcclusion) =>
        throw new NotImplementedException("reverse patch not applied");

    /// <summary>True while the view's scene is being rendered.</summary>
    private static bool Drawing;

    private static bool SkipWhileDrawing() => !Drawing;

    /// <summary>The view reuses this frame's shadow maps: only the cascades' constants are refreshed for its camera.</summary>
    private static bool PrepareQueriesPrefix(MyShadowCascades __instance, VRage.Render11.RenderContext.MyRenderContext rc)
    {
        if (!Drawing || !__instance.Enabled) return true;
        __instance.m_cascadeStats.Update();
        __instance.FillConstantBuffer(rc, __instance.m_csmConstants2);
        __instance.FillConstantBuffer(rc, __instance.m_csmConstants);
        return false;
    }

    /// <summary>The depth-stencil pool returns null (and the game throws) once its first 16 are used; the view's size
    /// needs its own, so the pool may grow like the other pools. (Harmless if another plugin already did this.)</summary>
    private static IEnumerable<CodeInstruction> DepthStencilTranspiler(IEnumerable<CodeInstruction> code)
    {
        var allocate = AccessTools.Method(typeof(MyObjectsPool<MyDepthStencil>), nameof(MyObjectsPool<MyDepthStencil>.Allocate));
        var grow = AccessTools.Method(typeof(DestinationView), nameof(AllocateDepthStencil));
        foreach (var i in code)
        {
            if ((i.opcode == OpCodes.Callvirt || i.opcode == OpCodes.Call) && Equals(i.operand, allocate)) yield return new CodeInstruction(OpCodes.Call, grow).WithLabels(i.labels);
            else yield return i;
        }
    }

    private static MyDepthStencil AllocateDepthStencil(MyObjectsPool<MyDepthStencil> pool, bool nullAllowed)
    {
        if (nullAllowed) return pool.Allocate(true);
        pool.AllocateOrCreate(out var item);
        return item;
    }

    // ------------------------------------------------------------------ the render thread

    private static IRtvTexture texture;
    private static int texW, texH;
    private static Format texFormat;
    private static long renderedAt;
    private static int idleFrames;

    private static void DrawGameScenePostfix(IRtvBindable renderTarget)
    {
        // AFK (ViewOnly) keeps the picture live: it only shows, like the map
        if (Drawing || renderTarget == null || (PlayGuard.Blocked && !PlayGuard.ViewOnly)) return;
        var r = Volatile.Read(ref request);
        if (r == null)
        {
            if (++idleFrames == 300) Free(); // not on show for a while: give the texture back
            if (idleFrames > 2) Volatile.Write(ref ready, false);
            return;
        }
        idleFrames = 0;
        try
        {
            var size = renderTarget.Size;
            if (size.X < 64 || size.Y < 64) return;
            var format = renderTarget.Rtv.Description.Format;
            long now = Stopwatch.GetTimestamp();
            var (w, h) = PictureSize(r, size);
            bool stale = texture == null || !Volatile.Read(ref ready) || texW != w || texH != h || texFormat != format;
            if (stale || now - renderedAt >= Stopwatch.Frequency / Fps) Render(r, w, h, format, now);
            if (texture == null || !Volatile.Read(ref ready)) return;
            float sx = size.X / Math.Max(1f, r.Screen.X), sy = size.Y / Math.Max(1f, r.Screen.Y);
            var vp = new MyViewport(r.Rect.X * sx, r.Rect.Y * sy, r.Rect.Width * sx, r.Rect.Height * sy);
            MyRender11.RC.PixelShader.SetSamplers(0, MySamplerStateManager.StandardSamplers);
            MyCopyToRT.Run(renderTarget, texture, false, vp, true);
        }
        catch (Exception e) { Plugin.LogThrottled("destination view render failed: " + e.Message); }
    }

    private static (int W, int H) PictureSize(Request r, Vector2I screen)
    {
        float want = r.Rect.Height * screen.Y / Math.Max(1f, r.Screen.Y);
        int cap = Math.Min(screen.Y, MaxHeight);
        int h = Heights.FirstOrDefault(x => x >= want * 0.9f);
        if (h == 0 || h > cap) h = Math.Min(cap, Heights.LastOrDefault(x => x <= cap) is int c && c > 0 ? c : Heights[0]);
        int w = (int)Math.Round(h * r.Rect.Width / Math.Max(1f, r.Rect.Height) / 2) * 2;
        return (Math.Max(32, Math.Min(screen.X, w)), Math.Max(24, h));
    }

    private static void Render(Request r, int w, int h, Format format, long now)
    {
        if (!(MyIDTracker<MyActor>.FindByID(r.Actor) is MyActor actor)) return;
        if (texture == null || texW != w || texH != h || texFormat != format)
        {
            if (texture != null) MyManagers.RwTextures.DisposeTex(ref texture);
            texture = MyManagers.RwTextures.CreateRtv("SDX2_SystemMap.DestinationView", w, h, format);
            texW = w; texH = h; texFormat = format;
            Volatile.Write(ref ready, false);
        }
        // The camera's place from your grid's matrix at this frame's render (interpolated), so it isn't a frame behind a
        // fast ship; its aim from there to the destination itself. Were the aim carried with the ship too, every small
        // turn of the ship would swing the picture: at the narrowest lens a thousandth of a radian is three pixels.
        MatrixD ship = actor.WorldMatrix;
        var eye = Vector3D.Transform(r.Local, ship);
        var look = r.Aim - eye;
        if (look.LengthSquared() < 1) return;
        look.Normalize();
        var top = Math.Abs(Vector3D.Dot(look, ship.Up)) > 0.98 ? ship.Backward : ship.Up;
        top = Vector3D.Normalize(top - look * Vector3D.Dot(top, look));
        MatrixD world = MatrixD.CreateWorld(eye, look, top);

        var env = MyRender11.Environment.Matrices;
        var view0 = env.ViewD;
        var proj0 = env.OriginalProjection;
        var projFar0 = env.OriginalProjectionFar;
        float fov0 = env.FovH, near0 = env.NearClipping, far0 = env.FarClipping;
        float offX = env.Projection.M31, offY = env.Projection.M32;
        var pos0 = env.CameraPosition;
        bool smooth0 = env.LastUpdateWasSmooth;
        var viewport0 = MyRender11.ViewportResolution;
        var resolution0 = MyRender11.m_resolution;
        var overrides = MyRender11.DebugOverrides;
        bool flares0 = overrides.Flares, ssao0 = overrides.SSAO, bloom0 = overrides.Bloom;
        bool frozen0 = MyRender11.Settings.ShadowCameraFrozen;
        var geometry = MyManagers.GeometryRenderer;
        bool lod0 = geometry.IsLodUpdateEnabled;
        try
        {
            MyRender11.ViewportResolution = new Vector2I(w, h);
            MyRender11.m_resolution = new Vector2I(w, h);
            overrides.Flares = overrides.SSAO = overrides.Bloom = false;
            MyRender11.Settings.ShadowCameraFrozen = true;
            geometry.IsLodUpdateEnabled = false;
            SetCamera(MatrixD.Invert(world), proj0, projFar0, r.Fov, r.Near, r.Far, r.FarFar, world.Translation, 0, 0, false);
            Drawing = true;
            DrawGameSceneOriginal(texture, out var ao);
            ao?.Release();
            Volatile.Write(ref ready, true);
            renderedAt = now;
        }
        finally
        {
            Drawing = false;
            MyRender11.ViewportResolution = viewport0;
            MyRender11.m_resolution = resolution0;
            overrides.Flares = flares0; overrides.SSAO = ssao0; overrides.Bloom = bloom0;
            MyRender11.Settings.ShadowCameraFrozen = frozen0;
            geometry.IsLodUpdateEnabled = lod0;
            SetCamera(view0, proj0, projFar0, fov0, near0, far0, r.FarFar, pos0, offX, offY, smooth0);
            MyCommon.UpdateFrameConstants(); // the rest of this frame uses the main camera's constants again
        }
    }

    private static void SetCamera(MatrixD view, MatrixD proj, MatrixD projFar, float fov, float near, float far, float farFar, Vector3D position, float offX, float offY, bool smooth)
    {
        var msg = MyRenderProxy.MessagePool.Get<MyRenderMessageSetCameraViewMatrix>(MyRenderMessageEnum.SetCameraViewMatrix);
        try
        {
            msg.ViewMatrix = view;
            msg.ProjectionMatrix = proj;
            msg.ProjectionFarMatrix = projFar;
            msg.FOV = fov;
            msg.FOVForSkybox = fov;
            msg.NearPlane = near;
            msg.FarPlane = far;
            msg.FarFarPlane = farFar;
            msg.CameraPosition = position;
            msg.ProjectionOffsetX = offX;
            msg.ProjectionOffsetY = offY;
            msg.Smooth = smooth;
            MyRender11.SetupCameraMatrices(msg);
        }
        finally { msg.Dispose(); }
    }

    private static void Free()
    {
        try
        {
            if (texture != null) MyManagers.RwTextures.DisposeTex(ref texture);
            Volatile.Write(ref ready, false);
        }
        catch (Exception e) { Plugin.LogThrottled("destination view cleanup failed: " + e.Message); }
    }
}
