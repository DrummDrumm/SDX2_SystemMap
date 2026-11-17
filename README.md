# SDX2 System Map

A standalone Pulsar client plugin: a 3D map of the Sigma Draconis Expanse 2 system that plots the route to the
destination you pick in **Flip and Burn's N list**. A programmable-block autopilot can then fly that route. Written
from scratch.

## In game

The plugin only runs in worlds that have SDX2's Flip and Burn mod (workshop 2832248541). Elsewhere it stays off:
no keys, no map, no toolbar actions, no speed-dial fix. Only its settings window opens.

1. Sit in a cockpit and press **N**. Flip and Burn's list opens with the **map window** beside it (**Shift+M** cycles
   it: map window, full-screen map, off. M alone stays the game's symmetry key; every key can be rebound in
   [Settings](#settings)).
2. Pick a destination. The list **stays open** (Flip and Burn would close it), and the map shows the route.
3. Under the list, choose:
   - **MANUAL**: the list closes and you fly it with Flip and Burn as usual.
   - **AUTOPILOT** (needs the autopilot script in a PB on the ship): the **route panel** opens beside the list. See
     [Autopilot](#autopilot).

### Detours round worlds

If the straight line to the destination passes within 100 km of a planet, moon or big asteroid, or through it, the
route bends round it. The detour points are added to Flip and Burn's N list as **"Detour 1 of 2 - <destination>"**.

Flip and Burn only flies straight lines, so when flying by hand, pick them in order: Detour 1, then Detour 2, then the
destination. The prompt and the route panel always say what to pick next. The plan moves on by itself once you're
within 25 km of a detour.

The detours are planned once when you pick the destination and then stay put, so the entries you pick don't move.
Press **R** in the full map to plan again from where you are, or **V** to turn detours off. The world you're leaving
and the one you're flying to are never avoided (a world counts when the start or destination is within its
clearance). ETA assumes a stop at each detour.

Side window (while the N list is open, so the mouse is free): right-drag orbits, wheel zooms, middle click re-frames,
and hovering shows what a point is. It doesn't take clicks on the map: pick destinations in the N list, or with
**Shift+click** in the full map (Shift+M from the map window). **FOLLOW SHIP** (top right of the window) keeps the view centred on your
ship; it's on at the start of each session, and middle click or the button turns it off. The bottom bar holds the legend (burn / coast / brake / flown, grid
scale) with the keys under it in labelled rows (VIEW, KEYS; the full map adds ROUTE and SHOW with each layer's on/off).
Esc closes the full map.

**Picture-in-picture.** **Alt+M** or the cockpit toolbar action pins a small map in a screen corner that
stays up while you fly (the N list still shows the full side window while it's open). It shows the route, ship,
flown track and a short summary (distance, ETA, flip, next pick). **Q** in the full map moves it to the next corner;
its size is `PipSize` in the settings file. It's saved, so it comes back on the next world load.

**Toolbar actions.** Every cockpit, seat and remote control gets two actions you can drag onto the ship's toolbar (G
menu): **System Map: pin preview on/off** and **System Map: full map**. They run only on your client.

**Admin mode and AFK.** SDX2's client-plugin rules say a plugin must switch itself off in admin mode or while you're
AFK, so the plugin pauses while any of these hold: creative tools on, any admin-menu toggle on (invulnerable, show
players, use terminals...), the admin menu open, the spectator camera, or no key, mouse or joystick input for 30 s (the
game's own AFK rule). While paused the full map closes, the preview and PiP are hidden, nothing is written to the
autopilot PB, the asteroid watch stops and Flip and Burn's list behaves as normal. An autopilot already flying carries
on (that's the PB script). Everything comes back on your next input. The log says `paused: <reason>`.

There are no chat commands: the plugin doesn't read chat.

**Picking over overlaps.** A station, GPS point, asteroid or route marker the mouse is on is picked before the world
under it (Ganymede Station over Ganymede or Jupiter), in both the map and the preview. Stations are matched to Flip and
Burn's entry by their GPS name too (JU.Ganymede.STN), so Shift+click works for them.

**Themes.** **K** in the full map cycles the map's look: Classic, Tachi, UNN, OPA (or set `Theme` in the settings file).
**UNN** is a UN Navy plotting chart: deep navy, white linework, UN blue with gold trim, degree ticks on every orbit,
gold bearing spokes from Sol labelled 000 to 330, planets marked with a ring and a gold reticle, a gold underline on
the title bar and a double frame with gold corner squares. **OPA** is a Belter salvage HUD: charcoal, bone-white
dashed orbits with orange-and-teal hazard marks, hexagons round Sol, planets and big asteroids, a hazard-striped title
bar, a heavy orange frame with teal notched corners, and burn orange / coast teal / brake yellow route legs.
**Classic** is the blue chart. **Tachi** is a black-and-red MCRN tactical display:
segmented thick red arcs, fine dashed rings and tick scales on the orbits, a glowing ringed Sol, planets lit in their
own colours inside thin rings, red sector spheres, amber/white route legs (burn orange, coast white, brake amber) and
bracketed panels. The full map, the side preview and the autopilot panels all follow it; it's saved.

**Flown and projected track** (side window and full map): a dashed white line shows where the ship has actually flown
since the route was set (or the autopilot engaged), from a small ring at the start, with a thin grey line for the route
as set, so you can see how far it strayed and how it corrects back. The route itself is drawn along the **projected path**: where the ship really goes from its current position and
velocity as it corrects onto the route (turning with its acceleration, sideways error first), round each detour and
waypoint on a curve (constant thrust sideways, a parabola): it slows to a speed the corner allows and starts turning
early, so a sharp turn cuts inside the waypoint instead of overshooting and doubling back. Then it brakes into the
destination. It's coloured by what the ship is doing (burn / coast / brake) with a diamond
where it turns to brake; the straight plan stays underneath, thin and dim, with its flip point and T+ marks. A red ring
and "projected end" mark where it ends if it can't get there (no thrust known).

Full-screen map:

| Input | Does |
|---|---|
| Drag / right-drag / wheel | Orbit / pan / zoom |
| Click a world, station or GPS | Focus the view on it |
| Shift+click a world, station, GPS or route point | Make it Flip and Burn's destination (a waypoint while ADD WAYPOINT is on) and open the autopilot controls. Off while the autopilot flies |
| Shift+right-click | Add a waypoint there, in the leg it's nearest (Shift+right-click one of your waypoints to remove it): on a world, station or GPS point it's that point; near the route's line it's on the line; elsewhere it's level with the nearest part of the route. Build a route of several stages this way (up to 8 waypoints; **x** in the autopilot panel removes one) |
| S | Follow your ship (toggle; on each time the map opens; panning, F or H turns it off) |
| F / H | Frame the route / the whole system |
| C | Copy the flip point as a GPS (paste it in the GPS tab) |
| A | Open the autopilot controls in the map (while it flies: asks, then stops it) |
| R / V | Plan detours again from here / detours on or off |
| N | Close the map and open Flip and Burn's list |
| G O X T P L J | Grid, orbits, sectors, stations, GPS, labels, moons |
| Esc | Back to the map window |
| Shift+M | Close the map and hide the map window |

**Autopilot controls in the map.** After a Shift+click (or A), a window on the right works like the buttons under the
N list: **AUTOPILOT** opens the same route panel there (top speed, burn and brake caps, waypoints: Shift+click points
on the map while ADD WAYPOINT is on, then ENGAGE), and **MANUAL** closes the map so you fly it with Flip and Burn.
After a stop part-way it also shows **RESUME ROUTE**. While the autopilot flies it shows **STOP AUTOPILOT**, and it
stays open after ENGAGE so you can watch the route.

The route shows:
- the burn (orange), coast (blue) and brake (green) legs, the flip point and T+ marks;
- the ETA, from Flip and Burn's own max acceleration and its 50 km/s cap;
- the Nexus sector borders crossed (each is a server transfer);
- the worlds passed within 100 km, and the station at the destination;
- asteroids found on the route, in red.

## Autopilot

`PB Autopilot\Protogen Systems - Autopilot.cs` is a programmable-block script that flies the route. SDX2 needs a script-access ticket
(#sigma-support) before PBs run on the server.

**Setup.** Paste the script into a PB on the ship. It adds a `[SDX2 Route]` line to the PB's Custom Data, which is how
the plugin finds it. The ship needs a cockpit or remote control, gyros (RCS Control Computer or braced gyros), and its
fusion drives (`sdx_drive*`) all facing one way, which SDX2 requires anyway.

**Route panel.** AUTOPILOT under the N list (or Ctrl+M, or A in the full map) opens it beside the list:
- **Top speed**: a slider up to Flip and Burn's cap.
- **Limit by THRUST or SIGNATURE**, with a separate slider for the **Burn** (speeding up toward each point) and the
  **Brake** (slowing down to stop): cap the drives at a share of their thrust, or at the largest **Weak exhaust-cone**
  range you want the drives to show (how far behind your drives a baseline sensor first picks you up). A gentle brake means a longer brake, so the flip comes earlier; the route, ETA and flip
  point on the map follow both caps. For each, the panel shows the thrust share and acceleration, then the figures
  Spectrum will show at that cap, worked out the way Spectrum does and classed the way the SDX2 wiki's
  [Signals and Detection](https://wiki.drac.gg/s/expanse/doc/signals-and-detection-uzjF4eXJkM) page does:
  - every range is three figures, **Detailed / Strong / Weak** km: how far off a baseline (1.0x) sensor sees you with
    your ship core, with your faction, or as a faint distance-only blip. Each tier needs half the distance of the
    next (thresholds 1.6 / 0.4 / 0.1). Their sensors scale it: none 0.5x, dish or scope 1.4x, advanced 2.0x;
  - **optical** (the drives): **all round** (the HUD's sphere) and the **exhaust cone** (the HUD's arrow, gain² times
    stronger: up to 4x the range, Torch 2x);
  - **radio**: the loudest antenna or beacon broadcast (Strong at its radius) or power draw. It's picked up by radio
    detectors, not optical ones; the line turns amber when it's louder than the drives and so sets your HUD's
    all-round figure.

  Drive emission: 3x3 3e9, 5x5 6e9, 7x7 12e9, 9x9 18e9, Torch 1e9, times thrust %; range = √(strength / threshold
  / 4π). The signature sliders are the drives' Weak exhaust-cone range (gain × the all-round range). When Spectrum is loaded, the panel also shows
  what its HUD shows **now**. While the ship coasts, the drives emit nothing, so only the radio emitters show.
- **Waypoints**: click **+ ADD WAYPOINT**, then pick points in Flip and Burn's list (stations, planets, GPS); each pick
  is added in order before the destination instead of changing it. Or Shift+right-click in the full map to drop one
  into the leg you click. **DONE** stops adding, **x** removes one. Detours
  round worlds are still planned on every leg; a station waypoint stops 8 km short, a world one at the clearance.
- The route summary (distance, ETA with your caps, where it stops, borders), then **ENGAGE** or **CANCEL**.

The panel keeps its settings (in the settings file). ENGAGE sends the route and closes the list.

**Flying.** The PB:
- turns the drives the way it needs to push, burns, flips and brakes, using the true Flip and Burn speed (position over
  time, since physics speed is capped);
- never runs the drives above the route's burn cap while speeding up, or its brake cap while slowing down, and keeps
  under its top speed;
- stops at each detour round a world and each of your waypoints, then turns for the next leg;
- stops **8 km short of a station** (Flip and Burn's 5 km exclusion) or at the **clearance above a planet or moon**,
  then switches the dampeners on;
- learns how fast the ship really turns (weak RCS gyros) and flips earlier if needed.

While it flies, a status line at the top shows the distance, speed, ETA and which point it's flying to. The buttons
under the N list become **STOP AUTOPILOT**. Flip and Burn's destination follows the route: the plugin selects each
detour and waypoint in the N list as the autopilot reaches the one before, then the destination, so Flip and Burn's
HUD marker always shows where it's heading.

**Taking over, at any time.** Any of these stops it:
- touching any movement or turn control in a seat;
- switching the dampeners on (Z);
- STOP AUTOPILOT or Ctrl+M (both ask first: **TAKE CONTROL** or **KEEP FLYING**);
- a **hotbar** slot: drag the PB onto the cockpit's toolbar, pick **Run**, and give it the argument `manual`.

It lets go of the thrusters and gyros at once. When you took over (controls, dampeners or the hotbar) a pop-up says
so and offers **RESUME ROUTE** or **STAY MANUAL**. The stopped route is kept: **RESUME ROUTE** also sits under the N
list until you pick a different destination, and flies the rest of the route from where you are. A second hotbar
slot running the PB with `go` resumes it too. PB arguments: `manual` (or `stop`), `go` (fly the route already
loaded), `prograde`, `retrograde`, `brake`.

**Hotbar turns.** These don't need a route. Put each on its own hotbar slot (the PB, **Run**, with the argument):
- `prograde`: turns the ship so its drives push along your travel, then holds it there.
- `retrograde`: turns them against your travel, ready to brake, then holds.
- `brake` (or `fullstop`): retrograde full stop. It turns the dampeners off, turns retrograde, and burns at full thrust,
  easing off near the end. Under 2 m/s it lets go and turns the dampeners back on. Near a planet it also pushes against
  gravity, so it doesn't settle into a fall.

A hold lasts until you touch the controls, run `manual`, or press the same slot again. The dampeners can stay on while
holding. Any of the three takes over from a route being flown. The map then says the autopilot is turning and doesn't
offer **RESUME ROUTE**; `go` from the hotbar flies the rest of the route. Turns aren't resumed after a reload or a
sector transfer.

Optional PB settings go in a `[SDX2 Autopilot Config]` section of its Custom Data:
- `TurnRate=0.6`: maximum turn rate, rad/s.
- `BrakeMargin=0.8`: share of the max acceleration planned for braking.
- `MaxSpeed=0`: m/s; 0 = Flip and Burn's cap.

It runs every 10 ticks while flying and every 100 while idle, with no block scans per tick. SDX2's PB watchdog allows
0.5 ms for all your PBs together.

The PB keeps its state in Storage, so it should carry on after a Nexus sector transfer. That is untested, so stay in
the seat when the route crosses a border; the confirmation lists them.

### Asteroids

Flip and Burn warns about procedural asteroids 30 s ahead along your velocity, and it stops the ship hard when its
next high-speed step would hit one. The plugin runs **its own copy of Flip and Burn's asteroid scanner** (same
generator, same zones) once a second, and looks **no further than Flip and Burn does: 30 s at your current speed**,
along the route still ahead and along your velocity (SDX2's rules: no information the game wouldn't give you).
- An **ASTEROID ON ROUTE / AHEAD** warning shows at the top of the screen with the distance and time, and a red marker
  appears on the maps.
- While the autopilot flies, a rock on the line gets a suggested **fly-by point** beside it (the rock's size + 2 km).
  Nothing is sent by itself: **Ctrl+Shift+M** sends the route with that point, and the PB flies past it without
  slowing. If you don't, Flip and Burn stops the ship at the rock as usual. The suggestion also goes with any route
  update you send (a new waypoint, RESUME).

The scanner is Flip and Burn's internal code, reached by reflection. If a Flip and Burn update renames it, the log says
`asteroid scanner wasn't found`, and there are no warnings or dodges.

### Hand-off format (v2)

The plugin writes the `[SDX2 Route]` section of the PB's Custom Data and keeps everything else. Custom Data you set on
your own blocks syncs to the server. The PB acts once per `id` and answers in its own `[SDX2 Autopilot]` section, which
syncs back:
- `state`: idle | flying | arrived | override | stopped | error
- `ack`, `target`, `wp=i/n`, `dist`, `speed`, `eta`, `msg`

```
[SDX2 Route]
version=2
id=17632368001                changes on every send
command=engage                engage | update (new points, keep flying) | stop | load (store only)
target=Belt.Tycho.STN
dest=x,y,z                    the picked destination
stop=station                  station (8 km short) | world (clearance above it) | point
accel=12.5                    F&B max acceleration when sent (m/s², 0 = unknown)
maxspeed=50000                speed cap (m/s): the route panel's, at most F&B's
thrust=1                      share of the drives' thrust it may use burning (the panel's Burn cap)
brake=1                       share it may use braking (the panel's Brake cap; missing = same as thrust)
clearance=100000              wanted clearance above world surfaces (m)
eta=2051                      planned seconds, stopping at each detour
wp=x,y,z,arrive,kind,name     in flight order; kind = pass | detour | waypoint | target (last: the stop point)
border=x,y,z,fromDx,toDx      Nexus borders on the route
```

`RouteExport` in `Core` writes and reads it. With `AutopilotAutoSend=true`, every new plan is loaded into the PB
without flying it.

## How it talks to Flip and Burn

- Public API (mod message channel 9806650): true high-speed velocity, `IsNavMenuOpen`, `SetNavMenuOpen`, and
  `SetPointsOfInterest` (the detour entries).
- One Harmony patch, client side: a prefix on Flip and Burn's `NavigationMenu.SelectEntry` does the pick itself
  without the Close at the end (Close is too small to patch: the JIT inlines it), so the list stays open while
  `KeepNavOpen` is on. While ADD WAYPOINT is on, the pick goes to the route as a waypoint and Flip and Burn's
  destination doesn't change. If the patch is missing, the plugin opens the list again after a pick instead.
- Read-only reflection for the rest:
  - the picked destination (`Hud._selection`);
  - whether its search box has focus (the plugin's keys are ignored while you type);
  - `Extensions.GetThrustData` (max acceleration);
  - `Session.Config.MaxSpeed`;
  - the grid's speed state;
  - its `AsteroidScanner` type (for our own instance).

  Flip and Burn's selection changes only as a click in the N list would change it: on a Shift+click in the full map,
  and while the autopilot flies, to the point it's heading for. It closes the list for MANUAL and ENGAGE (Flip and
  Burn's own Close).

If a Flip and Burn update renames those internals, the preview stops finding a destination. The log says
`SDX2_SystemMap: Flip and Burn found in ...` when the lookup works.

## Settings

**Settings window:** Pulsar's **Config** button for the plugin, or **Ctrl+Alt+/** in game. It covers the keys,
showing it only in a cockpit, the default zoom, the map window (opens with the N list, keep the list open, side, size, theme), the pinned map (on, corner, size),
detours and clearance, loading new plans into the autopilot, and the speed dial fix. Changes apply at once and are
saved when it closes; **Defaults** puts them back.

Everything is kept in `%AppData%\SpaceEngineers\Storage\SDX2_SystemMap.ini`:
- `KeyMap` (`Shift+M`), `KeyAutopilot` (`Ctrl+M`), `KeyPin` (`Alt+M`), `KeySteer` (`Ctrl+Shift+M`), `KeySettings`
  (`Ctrl+Alt+OemQuestion`, which is Ctrl+Alt+/): Ctrl/Shift/Alt plus a game key name, or `None`. A key fires only with
  exactly its modifiers. In the window, click a key and press the new keys (Esc cancels, Backspace unbinds); a combo
  already in use moves over and the other action is unbound. If the keys are also one of the game's controls
  (your current game bindings), a pop-up asks before binding them, the key shows **(!)**, and each world load shows a
  red HUD warning until it's rebound. A game key with no Ctrl/Shift/Alt also fires with them held, so the window notes
  that too (Shift+M also switches symmetry while you place blocks). An older file's `PreviewKey=X` becomes Shift/Ctrl/Alt/
  Ctrl+Shift+X;
- `OnlyInCockpit` (off): the map, preview, pinned map and autopilot HUD lines show, and the keys work, only while you
  sit in a cockpit, seat or remote control;
- `DefaultGridKm` (2000): how far out the map window and full map start when no route is picked, as the size of a grid
  square round your ship (50, 100, 200, 500, 1000, 2000, 5000, 10000 or 20000 km; F in the full map frames it again);
- `ShowMapWithNav` (the map window opens with the N list), `KeepNavOpen` (the list stays open on a pick);
- `ApMaxSpeed`, `ApBySignature`, `ApThrustPct`, `ApConeSignatureKm`, `ApBrakePct`, `ApBrakeConeSignatureKm` (the route
  panel's last limits; the first pair of caps is the burn);
- `PreviewLeft`, `PreviewSize` (0.5–1.5);
- `FixSpeedometer` (on): the HUD speed dial shows Flip and Burn's real speed, so it doesn't flicker to 150 m/s;
- `Pip`, `PipCorner` (`TR`, `TL`, `BR`, `BL`), `PipSize` (0.5–2): the pinned picture-in-picture map (Alt+M, Q in the full map);
- `Theme`: `Classic`, `Tachi`, `UNN` or `OPA` (K in the full map);
- `AvoidBodies`, `ClearanceKm` (10–5000);
- `AutopilotAutoSend`;
- the camera angle and the layer toggles.

## Build, test, deploy

```
dotnet run --project Tests                               # 195 checks + previews\full-map.png, side-preview.png, autopilot.png
dotnet run -c Release --project "PB Autopilot\Check"     # compiles the PB script as the game does (C# 6) + 8 flight sims
.\Deploy-LocalDev.ps1                                    # Interim (.NET 10, into Pulsar\Interim or else Pulsar\Legacy, as Pulsar does); -Edition Legacy for .NET 4.8
```

- `Core\` is game-free: maths, data, and the renderer against `IMapCanvas`, including the autopilot panels. It's what
  the tests run.
- `ClientPlugin\` holds the game side: GameCanvas, GameState, FlipAndBurnLink, AutopilotLink, AsteroidWatch,
  SidePreview, MapScreen, Plugin.
- `PB Autopilot\` holds the PB script. Its guidance (`Program.Nav`) makes no game calls, so the check project flies it
  in point-mass sims: straight hops, the 50 km/s cap, a detour, early and late asteroid dodges, a slow-turning ship,
  and gravity.

Client plugins on SDX2 must come from Pulsar or be approved by staff: ask in #sigma-support before using it there.
