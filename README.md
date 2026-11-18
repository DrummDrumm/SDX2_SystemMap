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

### Your ship on the map

Your ship is drawn as a top-down silhouette of its SDX2 core class, with drive bells on its stern, turned the way the
ship faces (the Barge and Hauler lead with their square cargo blocks). The silhouette gets
shorter along its nose as the nose turns toward or away from the view. There is one silhouette for each class:
Skiff (no core), Barge, Hauler, Picket, Corvette, Frigate, Cruiser, Battleship, Carrier, Dreadnought, Outpost,
Installation and Station Admin. Bigger classes draw bigger.

Out from the icon, a trajectory marker shows where the ship is actually going (its velocity), so a ship flying
backwards or drifting sideways shows both. Each theme has its own marker:

| Theme | Trajectory marker |
|---|---|
| Classic | plain arrow |
| Tachi | segmented line ending in a double chevron |
| UNN | plotting vector with ruler ticks and a gold diamond |
| OPA | run of hazard chevrons |
| Protogen | thin line to a small crosshair reticle |
| Belter | dotted trail ending in a triangle |

**Ship icon: From real ships** (settings, MAP WINDOW) swaps in silhouettes taken from real ships in the SDX2 creative
save, in plan view with the nose along the core's forward:

| Class | Sampled from |
|---|---|
| Skiff | Comet Shuttle |
| Barge | Jaeger Class Barge |
| Hauler | Natim Fo Séfesowng |
| Picket | BlackWing Torp Picket |
| Corvette | The Formido |
| Frigate | Aftermath Frigate |
| Installation | Naust Station |

Classes with no ship in the save (Cruiser, Battleship, Carrier, Dreadnought, Outpost, Station Admin) keep the drawn
icon. To re-sample after adding ships to a save:
`python Tools/ship-silhouettes.py "<save folder>" ["Class=Grid name" ...]`. It only reads the save and rewrites
`Core/ShipSamples.cs`.

On foot, or before the plugin knows the class, the theme's plain ship marker is shown instead. The test run renders
every icon and marker to `previews/ship-icons.png`.

### Detours round worlds

If the straight line to the destination passes through a planet's or moon's gravity well, the route bends round it,
skimming about **5 km outside the well** (the 0.05 g edge). The game reports each well once you're in the system;
until then, and for big asteroids, the route keeps 100 km clear instead. A route planned before a well was known is
planned again as soon as it is known, unless the autopilot is flying it.

**How low it goes is set by your thrust.** The map reads each planet's gravity from the game (surface gravity, how it
falls off with height, the top of its highest hills, and its atmosphere) and your ship's thrust at the autopilot's
thrust setting over its mass now. A height counts as safe where your thrust is at least **1.5× the pull** (a 50% margin).
- Thrust that holds 1.5× the surface gravity: the route may come down to **3 km over the highest hills**, going past,
  taking off, landing at a surface base, or stopping at the world itself.
- Less thrust: the route stays up where gravity has fallen to two-thirds of your thrust (the game's falloff:
  g = surface × (r / hilltop)^-falloff).
- Too little to hold anywhere in the well (or thrust not known): 5 km outside the well, as before.
- No gravity worth the name (an asteroid, or a moon whose well doesn't reach past its hills): **3 km over the hills**.
- A planet with an **atmosphere**: the route always stays 3 km above its top; only the final climb out or descent to a base
  goes through it.

If you take off from or land on a world your thrust can't hold at its surface, the route panel warns **TOO HEAVY** with
the thrust you have and the thrust needed. When the ship's mass or the thrust setting changes by more than 10%, a route
not yet being flown is planned again. The detour points are added to Flip and Burn's N list as **"Detour 1 of 2 - <destination>"**, with the destination's short name ("Tycho" for "Tycho Station" or "Belt.Tycho.STN"; the map, panels and autopilot card use it too).

Flip and Burn only flies straight lines, so when flying by hand, pick them in order: Detour 1, then Detour 2, then the
destination. The prompt and the route panel always say what to pick next. The plan moves on by itself once you're
within 25 km of a detour.

The detours are planned once when you pick the destination and then stay put, so the entries you pick don't move.
Press **R** in the full map to plan again from where you are, or **V** to turn detours off. ETA assumes the ship slows at each
detour just enough to turn there (Legs: FLIP & BURN), or curves through them (PARABOLIC).

Near a world the route still goes round it, never through:
- **In low orbit** (inside the clearance): it goes round near your own height, dipping to no lower than 80% of it.
- **On the ground** (taking off, or landing at a base on it): it climbs to the clearance leaning toward where it's
  going (up to 60° from straight up), to the point where the way on just skims the clearance, instead of straight up
  and then round with room to spare. A landing comes down the same way. Toward something straight overhead, or right
  round the far side, the climb is straight up.
- **Low over its ground** (a short hop by a moon, or a take-off or landing that doesn't go round it): any leg that
  would dip into the hills (up to 10% over the radius; SDX2's moons reach 6%) climbs straight up over them, follows the
  curve at that height, and comes down over the far end.
- **Picking the world itself** (its entry in the N list): it's flown to, not round, and the stop is **just outside its
  gravity well** on your side (2 km out, and above any atmosphere that reaches further); with no well, 3 km over its hills.

**Where a route stops.** The **Offset** setting (3 km by default, 0.5 km at the least) is how far short of the
destination the autopilot stops: short of a point in open space or a station, on your side. A **base or station on a
world's surface** is stopped straight above, never closer than **3 km** (or the Offset, if larger), and higher when your
thrust can't hold the ship there: where 1.5× the pull is held, outside the well when it can't hold anywhere in it, and
above any atmosphere. Your own waypoints are flown to on the point (3 km above one on the ground).

The "clearance" above is the height a route keeps round a planet or moon whose gravity the game hasn't sent yet: a fixed
100 km, until it's known. There's no setting for it any more.

**Asteroid fields** (setting *Keep out of asteroid fields*, on by default): the route also keeps 25 km out of the
Belt, the Trojans and the Greeks. It crosses the Belt over or under its tube (whichever is shorter; usually a few tens
of km extra), and goes round the Trojans' and Greeks' spheres. These match their Nexus sectors, so it also saves the
server transfers into and out of them. A route that starts or ends in a field (Tycho Station, Achilles Station) flies
through it as before. Shift+right-click one of these detours to fly straight through instead.

**Bases and stations.** The route also bends round the known stations (8 km) and Sol (a fixed 800 km no-fly sphere: a route that starts inside it goes straight out first) and the bases owned by you or your
faction that your game has loaded (their size plus 2 km; bases close together count as one). A base is a grid set to
station with an Outpost or Installation core; ships and other grids aren't obstacles. Leaving a station or base
(docked, or inside its sphere), the route first goes straight out of it, then round it, when the way ahead passes
through it. The station or base you fly to isn't gone round. Other players' grids are left out on purpose: the map
only uses what you already know (SDX2's plugin rules).

Side window (while the N list is open, so the mouse is free): with the mouse over the map it works like the full map.
Drag orbits, right-drag pans, the wheel zooms, a click focuses on a world, station or GPS point, and middle click
re-frames. **Shift+click** sets the destination, **Shift+right-click** adds or removes a waypoint (or straightens a
bent leg), **Shift+right-drag** adds one above or below the plane, and dragging your waypoint or a flip point raises,
lowers or bends it, all as in the full map. The full map's keys (F H S U A R V C G O X T P L J K) work there too:
while the mouse is over the map, Flip and Burn's search box takes no typing, and it takes it again when the mouse
leaves (N then closes the list, as F&B's own key). **FOLLOW SHIP** (top right of the window) keeps the view centred on your
ship; it's the same setting as the full map's S, on at the start of each session, and panning, middle click or the
button turns it off. The bottom bar holds the legend (burn / coast / brake / flown, grid
scale) with the keys under it in labelled rows (VIEW, ROUTE, SHOW, KEYS, each layer with its on/off).
Esc closes the full map.

**Free the mouse (hold F2).** While the autopilot flies, hold **F2** to bring up the mouse cursor without opening the
N list: the mouse stops turning the ship (so the autopilot isn't taken over) and works on the side window or pinned map
as above, including FOLLOW SHIP and the route panel's buttons. Let go and the mouse steers again. It uses Rich HUD
Master's cursor, which SDX2 loads.

**After a reload or sector change.** The autopilot PB keeps flying its route through a world load or a Nexus sector
change. When you sit back in the seat and the PB is flying, the plugin reads the route back from the PB (destination,
your waypoints and detours, and how far along it is) and shows it on the map, side window and pinned map again, with
the next point picked in Flip and Burn's list. This holds even when Flip and Burn remembered your last pick and the map
has already drawn a fresh route to it: the PB's route replaces it. Nothing is sent to the PB. A bent leg comes back straight on the map
(the PB still flies the curve it was sent).

**Picture-in-picture.** **Alt+M** or the cockpit toolbar action pins a small map in a screen corner that
stays up while you fly (the N list still shows the full side window while it's open). It shows the route, ship,
flown track and a short summary (distance, ETA, flip, next pick). **Q** in the full map moves it to the next corner;
its size is `PipSize` in the settings file. It's saved, so it comes back on the next world load. Closing the full map
keeps a pinned map pinned; with a route picked it pins the map for that route (unpinned again when the route is
cleared).

**Toolbar actions.** Every cockpit, seat and remote control gets three actions you can drag onto the ship's toolbar (G
menu): **System Map: pin preview on/off**, **System Map: full map** and **System Map: stop autopilot** (stops the
flight at once, no pop-up; RESUME ROUTE then sits under the N list). They run only on your client.

**Admin mode and AFK.** SDX2's client-plugin rules say a plugin must switch itself off in admin mode or while you're
AFK, so the plugin pauses while any of these hold: creative tools on, any admin-menu toggle on (invulnerable, show
players, use terminals...), the admin menu open, the spectator camera, or no key, mouse or joystick input for 30 s (the
game's own AFK rule). While paused nothing is written to the autopilot PB, the asteroid watch stops, Flip and Burn's
list behaves as normal, the speedometer fix steps aside, and the plugin's keys and mouse do nothing. An autopilot
already flying carries on (that's the PB script). In admin mode the full map closes and the preview and PiP are hidden.
While AFK the full map and the pinned preview stay up as a read-only view: your ship, the route and the autopilot's
status keep updating on them, as on the game's own map, but nothing is sent anywhere and no input is taken (the side
preview under the N list, the prompts and the asteroid warning are hidden). Everything comes back on your next input.
The log says `paused: <reason>`.

There are no chat commands: the plugin doesn't read chat.

**Picking over overlaps.** A station, GPS point, asteroid or route marker the mouse is on is picked before the world
under it (Ganymede Station over Ganymede or Jupiter), in both the map and the preview. Stations are matched to Flip and
Burn's entry by their GPS name too (JU.Ganymede.STN), so Shift+click works for them.

**Themes.** **K** in the full map cycles the map's look: Classic, Tachi, UNN, OPA, Protogen, Belter (or set `Theme` in the settings file).
**Belter** is a green OPA belt pilot chart, a radar contact plot crossed with an inner-system chart: a honeycomb grid
(the hex size shows in the bottom bar), a crosshair through Sol with bearings 000 / 090 / 180 / 270, alternating dashed and solid orbits,
a ringed Sol, planets with a dashed sphere of influence, hexagon stations, triangle contacts for your ship and GPS points,
tinted label boxes, yellow brackets on the destination and a chamfered frame.
UNN draws a large square chart grid.
The Belt is drawn as a 3D torus, using the shape the Skylark Belt plugin draws in game (centred on Sol, 8,500 km out, a
500 km tube, flat to the ecliptic). Each theme draws it its own way: Classic draws it as Skylark does (6 rings, 36
cross-sections); Tachi uses segmented red ribs; UNN shows the tube's edges with a cross-section on every 30-degree bearing;
OPA has dashed edges and a scatter of rocks; Protogen a fine wireframe lattice; Belter a hexagonal tube.
DX10 Trojans and DX11 Greeks are asteroid fields: each has an icon in the theme's style (Classic a ring of rocks, Tachi a
dashed red ring, UNN a chart hazard circle, OPA a hazard triangle, Protogen a bracketed diamond, Belter a hexagon) and
fills with rocks when you zoom in. Clicking to focus stands back far enough to see a world or field whole. The wheel
ignores the jump the game reports when it loses and regains focus (the Windows key, Alt+Tab).
**B** (the **Map panel side** key in settings, saved) moves the route panel to the other side of the screen. On the
right it is drawn as a mirror image: frames, headers and rows flip, while gauges, graphs and dials stay upright.
Each theme places, shapes and fills the full map's route panel its own way. The panel is a fixed size, so it doesn't
grow and shrink as the numbers change; a value too long for it is shortened ("(curved)", "wpt", "Stn") or drawn smaller:
- Classic: a plain card at the top left with speed and acceleration bars and the burn / coast / brake timeline.
- Tachi: a cut-corner block on a lit spine, centred on the left edge, with speed and acceleration tapes and big ETA / flip / range readouts.
- UNN: a full-height plotting sheet docked left, with a compass (course and velocity bearings), a speed-against-time graph and a ruled log.
- OPA: stacked salvage cards with readout tiles, block gauges, status lights (Nexus border, detour, hazard, overrun) and a route log.
- Protogen: a tabbed strip along the bottom with a ring timeline, ruler gauges and the rows in columns.
- Belter: a wide chamfered card with dials and a range track by distance beside boxed rows.
Each theme also marks the destination with its own icon: Classic a ring with a gapped crosshair; Tachi a segmented red
lock ring round a diamond, with chevrons closing in; UNN a plotting fix (double ring, bearing ticks, gold pointers); OPA
a heavy orange octagon with hazard-striped bars on its diagonals; Protogen the lock reticle; Belter yellow brackets with
a ranging triangle above. The destination's name sits beside its mark, or out on a diagonal with a leader line to it when
there is no room beside it; when the destination is on an asteroid field, the field's icon steps aside for it.

The side window's strip has the timeline, and the autopilot panel gets the theme's frame and header (brackets, gold
trim, hazard stripes, ruler ticks or a chamfer).
**Protogen** is a cyan-and-magenta nav HUD: a lock reticle on the destination (range rings, a red dashed ring, a
magenta wedge, a thick lock arc and a TRAJECTORY VECTOR tag with chevrons), a faint dashed crosshair through it, red
arcs on the Belt, ruler ticks under the title bar and over the bottom bar, and bracketed panels. Mockups of it and four
other looks in the same style are in [Themes](Themes/README.md).
**UNN** is a UN Navy plotting chart, the [navy chart](Themes/png/chart-navy.png) and the
[Naval Ice HUD](Themes/png/naval-ice.png) mockups combined: deep navy, ice-white linework, UN blue with gold trim, a
faint hex texture with a crosshair through Sol, alternating dashed and solid orbits with degree ticks, gold bearing
spokes labelled 000 to 330, planets with a ring, gold reticle ticks and a dashed sphere, hexagon stations and triangle
contacts, tinted label boxes, the lock reticle with a crosshair on the destination, red arcs on the Belt, ruler ticks
on the bars, and chamfered panels and frame with gold bars. **OPA** is a Belter salvage HUD: charcoal, bone-white
dashed orbits with orange-and-teal hazard marks, hexagons round Sol, planets and big asteroids, a hazard-striped title
bar, a heavy orange frame with teal notched corners, and burn orange / coast teal / brake yellow route legs.
**Classic** is the blue chart. **Tachi** is a black-and-red MCRN tactical display:
segmented thick red arcs, fine dashed rings and tick scales on the orbits, a glowing ringed Sol, planets lit in their
own colours inside thin rings, red sector spheres, amber/white route legs (burn orange, coast white, brake amber) and
bracketed panels. The full map, the side preview and the autopilot panels all follow it; it's saved.

**Flown and projected track** (side window and full map): a dashed white line shows where the ship has actually flown
since the route was set (or the autopilot engaged), from a small ring at the start, with a thin grey line for the route
as set, so you can see how far it strayed and how it corrects back. The route itself is drawn along the **projected path**: where the ship really goes from its current position and
velocity as it corrects onto the route (turning with its acceleration, sideways error first), braking to its turn speed at each
detour and waypoint and turning there (Legs: FLIP & BURN, as the autopilot flies it), then braking into the destination.
A PARABOLIC or bent route is drawn as planned instead, since the autopilot holds to the curve. It's coloured by what the ship is doing (burn / coast / brake) with a diamond
where it turns to brake; the straight plan stays underneath, thin and dim, with its flip point and T+ marks. A red ring
and "projected end" mark where it ends if it can't get there (no thrust known).

Full-screen map:

| Input | Does |
|---|---|
| Drag / right-drag / wheel | Orbit / pan / zoom |
| Click a world, station or GPS | Focus the view on it |
| Click an asteroid field (DX10 Trojans, DX11 Greeks) or the Belt | Fly to the field, or to the part of the Belt you clicked |
| Shift+click a world, station, GPS or route point | Make it Flip and Burn's destination (a waypoint while ADD WAYPOINT is on) and open the autopilot controls. While the autopilot flies, a pop-up asks first (see below) |
| Shift+right-click | Add a waypoint there, in the leg it's nearest (Shift+right-click one of your waypoints to remove it): on a world, station or GPS point it's that point; near the route's line it's on the line; elsewhere it's level with the nearest part of the route. Build a route of several stages this way (up to 8 waypoints; **x** in the autopilot panel removes one). With no route yet, the first Shift+right-click sets the destination instead: the point you click, or a point in space level with your ship (Shift+right-drag to set its height). Drag that point to raise or lower it |
| Shift+right-click a detour | Remove it: the route flies straight past that world (every detour round it), also when it's planned again. **R** in the full map, or a new destination, brings the detours back. While flying it's an edit like any other (APPLY → SEND ROUTE) |
| Shift+right-drag a waypoint | Move one of your waypoints (or the point in space you set as the destination) across the level plane, keeping its height |
| Right-click the destination, or Delete | **Clear the route**: the destination, your waypoints and Flip and Burn's pick go. Shift+right-click works too. The route otherwise stays until you clear it or arrive: leaving the seat, respawning or deselecting in Flip and Burn's list doesn't drop it. Arriving (the autopilot says so, or you stop inside 25 km of the destination after being away from it) finishes it. Also **CLEAR ROUTE** on the autopilot card and the **System Map: clear route** hotbar action. While the autopilot flies, a pop-up asks **CLEAR ROUTE** (stops it and clears) or **KEEP FLYING** |
| Shift+right-drag | Add a waypoint above or below the plane: press where it should go, then drag up or down with the view tilted (the marker shows the height); let go to add it |
| Drag one of your waypoints | Drag along the screen's up / down to raise or lower it; drag side to side (or any way while looking down on it) to move it along the X or Z axis, whichever runs most along your drag on screen, keeping its height and the other axis (Shift+right-drag moves it anywhere across the level plane). The first move picks which, for that drag. The route is planned again when you let go |
| Drag a flip point | Bend that leg up or down: it curves through the point you let go at (a **BEND** marker), turning partly sideways instead of stopping. Drag the BEND marker to move it; drag it back near the straight line, or Shift+right-click it, to straighten the leg |
| S | Follow your ship (toggle; the same setting as the window's FOLLOW SHIP, on at the start of each session; panning, F or H turns it off) |
| U | Turn with the ship (on by default, saved): while following, the view turns so your forward movement points up the screen (nearly stopped: the way the ship faces). Your tilt stays; a sideways drag turns the view and it keeps turning with the ship from there. Also in the settings, and for the side window and pinned map while they follow |
| F / H | Frame the route / the whole system |
| C | Copy the flip point as a GPS (paste it in the GPS tab) |
| A | Open the autopilot controls in the map (while it flies: asks, then stops it) |
| R / V | Plan detours again from here / detours on or off |
| N | Close the map and open Flip and Burn's list |
| G O X T P L J | Grid, orbits, sectors, stations, GPS, labels, moons. With orbits on, each moon gets a faint orbit line round its planet, and each station one round the world it sits by (dashed, in the station colour). A ground facility (a station on a moon's or asteroid's ground, such as Rhea Station) gets no orbit line. It's marked as a square pad on a ground bar, with a dashed line up to its approach point (a ring); they show once you're close enough to see them |
| Esc | Back to the map window |
| Shift+M | Close the map and hide the map window |

**Autopilot controls in the map.** After a Shift+click (or A), an **AUTOPILOT** card at the top of the route panel
works like the buttons under the N list, framed and with buttons in the theme's style: **AUTOPILOT** opens the same route panel on the side away from the info panel (top speed, burn and brake caps, **Legs**, waypoints: Shift+click points
on the map while ADD WAYPOINT is on, then ENGAGE), and **MANUAL** closes the map so you fly it with Flip and Burn.
After a stop part-way it also shows **RESUME ROUTE**. If any of the ship's fusion drives are switched off when you press ENGAGE (or RESUME ROUTE), a
pop-up says how many and offers **TURN ON & ENGAGE**, which switches them on and starts the route. If the route would leave
less than 10 % water in the tanks (or run them dry), a **Low water** pop-up gives the water it burns and what is left,
and asks: **ENGAGE ANYWAY** or **CANCEL**. The estimate is the drives' burn at 100 % (55.8 L/s for a Canterbury) times
each leg's burn and brake time at the thrust share it uses; coasting uses none. The route panel shows the same estimate on its **WATER** row before you
engage as the tanks now, what is left on arrival and the thrust it assumes ("Water now 64 % → 47 % @ Ceres, max 100 % thrust";
"now 64 % → dry before Ceres" and the warning colour below 10 %; the destination's short name, "Tycho" for "Tycho Station" or "Belt.Tycho.STN", cut further if it still doesn't fit). Both work the route out at its thrust and brake caps (the route panel's
limits, or the ones sent while it flies), whether or not the map draws it that way. While the autopilot flies it shows **STOP AUTOPILOT**, and it
stays open after ENGAGE so you can watch the route.

**End of the route: dampeners on.** When the autopilot arrives, the PB switches the inertial dampeners on as it lets go,
and the plugin switches them on for the ship you control as well (in case the PB's write didn't take); the arrival
notice says "dampeners on".

**Gravity wells.** Once a second the map asks the game's gravity at points along the route still ahead. Where the route
first goes into a planet's or moon's well (over 0.05 g) it draws a red ring labelled **GRAVITY: Mars** (or whichever),
and the route panel gets a red **GRAVITY** row ("route enters Mars's well in 1,200 km"; in one now: "in Earth's well,
0.42 g"). While the autopilot flies you also get on-screen warnings: once when the route is found to enter a well,
again about a minute before it gets there, and when the ship goes in.

**Legs** (route panel, saved) sets how a route with waypoints or detours meets each direction change:
- **FLIP & BURN**: a leg per stage. At each point the ship brakes only as much as the turn needs, then turns onto the
  new course and burns again: slow enough that burning the velocity change cuts the corner by no more than the
  point's reach radius (up to 2 km), and that the turn fits in half the shorter leg. Doubling back is nearly a stop;
  a point almost straight on costs no slowing.
- **PARABOLIC**: one curved leg through all the points and one flip. The ship turns its drives partly sideways to bend
  its path, never using more than 60% of the thrust for that, and slows for a sharp bend only as much as it needs.
  The confirm lines show the fastest it takes the tightest bend.

The route shows:
- the burn (orange), coast (blue) and brake (green) legs, the flip point and T+ marks. The marks stay at the same
  points in space as you fly (spaced when the route is set, and set again only for a new route, a trip that grows well
  past its first time, or a route that moves away from them), and each counts down the time until you reach it;
- the ETA, from Flip and Burn's own max acceleration and its 50 km/s cap;
- the Nexus sector borders crossed (each is a server transfer);
- the worlds passed within 100 km, and the station at the destination;
- asteroids found on the route, in red.

### Map on a second screen (touch)

Turn on **Map on a second screen** in [Settings](#settings) (or press **Ctrl+Alt+M**). The full map then opens in a
window of its own: it fills the other monitor with no border (pick which in **Screen**), or stands as a normal window
when there is only one. It has its own view, and shows the same route, autopilot card and route editor as the full map.
It never takes the focus from the game, so touching it leaves your keyboard and mouse in the cockpit. It opens with each
world while the setting is on. Its close button turns the setting off.

- **Touch:** drag to orbit; pinch to zoom; move two fingers to pan; twist them to turn; tap to focus on a world,
  station or GPS point; **hold, then let go** to make it the destination (a toast says what it will pick). Drag one of
  your waypoints to move it, as with the mouse.
- **TAP** button: what a tap does. **FOCUS** (the default), **DESTINATION** (a tap sets it), or **WAYPOINTS** (a tap
  adds a waypoint, or removes one of yours; a drag places one above or below the plane).
- The **buttons down the side** do what the full map's keys do: follow ship, turn with ship, frame route, whole
  system, autopilot, re-plan, detours, clear route (tap twice), layers (grid, orbits, sectors, stations, GPS, labels,
  moons, theme), window / full screen, and hide.
- **Mouse** on that screen: as in the full map (drag orbit, right-drag pan, wheel zoom, click focus, Shift+click
  destination, Shift+right-click waypoint, right-click the destination to clear).
- A touch or click there counts as being at the keyboard, so it also brings the plugin back from AFK. In admin
  mode, or out of a cockpit with **Only in a cockpit** on, it shows why it is paused.

It draws at up to 30 frames a second. The game records each frame and the window draws it on its own thread with GDI+,
so a slow frame there doesn't hold the game up.

## Autopilot

`PB Autopilot\Protogen Systems - Autopilot.cs` is a programmable-block script that flies the route. SDX2 needs a script-access ticket
(#sigma-support) before PBs run on the server.

**Setup.** Paste the script into a PB on the ship. It adds a `[SDX2 Route]` line to the PB's Custom Data, which is how
the plugin finds it. The ship needs a cockpit or remote control, gyros (RCS Control Computer or braced gyros), and its
fusion drives (`sdx_drive*`) all facing one way, which SDX2 requires anyway. Only the drives push the ship along a
route: it turns the ship so they point the way it needs to push, and leaves the RCS thrusters to turning and the dampeners.

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
  round worlds are still planned on every leg; a station waypoint stops 3 km short, a world one at the clearance.
- The route summary (distance, ETA with your caps, where it stops, borders), then **ENGAGE** or **CANCEL**.
- **While the autopilot flies**, Ctrl+M (or A in the full map) opens the same panel as **CHANGE FLIGHT**: change the
  top speed, caps or waypoints, then **APPLY** asks once (**SEND ROUTE** / **KEEP EDITING**, with your changes listed)
  and sends them; **CANCEL** drops them and it flies on as before. Closing the N list or the map with changes made asks
  the same (**SEND ROUTE** / **DROP CHANGES**). Nothing is sent until you say so.
- **Changing the route mid-flight from the map**: any edit on the map or preview while the autopilot flies (a new
  destination with Shift+click, adding, moving or removing a waypoint, bending or straightening a leg, moving your own
  destination) goes into an edit copy and opens the route editor. Make as many edits as you like: the autopilot keeps
  flying the original route, drawn dashed underneath and labelled FLYING, until **APPLY** → **SEND ROUTE** sends the
  new one and it flies on from where the ship is.
- The planned route and your waypoints are dropped when you get out of the cockpit or seat, or die / respawn. Pick a
  point in Flip and Burn's list again to plan a new one. An autopilot that's already flying keeps flying (stop it
  from the hotbar or the PB).

The panel keeps its settings (in the settings file). ENGAGE sends the route and closes the list.

**Flying.** The PB:
- turns the drives the way it needs to push, burns, flips and brakes, using the true Flip and Burn speed (position over
  time, since physics speed is capped);
- never runs the drives above the route's burn cap while speeding up, or its brake cap while slowing down, and keeps
  under its top speed;
- slows at each detour round a world and each of your waypoints just enough to turn onto the next leg there (Legs: FLIP & BURN), or
  curves through them without stopping (PARABOLIC, or a leg you bent), slowing only as much as each bend needs;
- stops the **Offset short of a station or point** (3 km by default; on the side away from the planet or moon when the station is on or near one, going round the station to get there), **3 km or more straight above a ground facility or base** (as low as your thrust holds; 8 km over Hygeia's, which is buried), at **800 km from Sol**, or **just outside the gravity well of a planet or moon** picked itself,
  then switches the dampeners on;
- learns how fast the ship really turns (weak RCS gyros) and flips earlier if needed.

While it flies, a status line at the top shows the distance, speed, ETA and which point it's flying to. It is framed
in the map's theme, and its place across and down the screen and its size are set under HUD in the settings. The buttons
under the N list become **STOP AUTOPILOT**. Flip and Burn's destination follows the route: the plugin selects each
detour and waypoint in the N list as the autopilot reaches the one before, then the destination, so Flip and Burn's
HUD marker always shows where it's heading.

**Taking over, at any time.** Any of these stops it:
- touching any movement or turn control in a seat;
- switching the dampeners on (Z);
- STOP AUTOPILOT under the N list (asks first: **TAKE CONTROL** or **KEEP FLYING**);
- the **System Map: stop autopilot** toolbar action (at once);
- a **hotbar** slot: drag the PB onto the cockpit's toolbar, pick **Run**, and give it the argument `manual`.

**FULL STOP** (beside STOP AUTOPILOT under the N list and in the map's autopilot card, or the **System Map: full stop**
toolbar action) hands the ship back nearly at rest instead. The PB turns retrograde and burns the drives (holding
against gravity) until the ship is under 50 m/s, then switches the dampeners on to stop the rest and lets go. While it brakes, the status line shows
the speed and the distance to the stop, and STOP AUTOPILOT lets go at once. The route is kept, so RESUME ROUTE flies
the rest. It works on a drifting ship too, and needs the PB script from this version: an older PB ignores the order,
and the plugin says so after a few seconds.

It lets go of the thrusters and gyros at once. When you took over (controls, dampeners or the hotbar) a pop-up says
so and offers **RESUME ROUTE** or **STAY MANUAL**. The stopped route is kept: **RESUME ROUTE** also sits under the N
list until you pick a different destination, and flies the rest of the route from where you are. A second hotbar
slot running the PB with `go` resumes it too. PB arguments: `manual` (or `stop`), `go` (fly the route already
loaded), `prograde`, `retrograde`, `brake`.

**Hotbar turns.** These don't need a route. Put each on its own hotbar slot (the PB, **Run**, with the argument):
- `prograde`: turns the ship so its drives push along your travel, then holds it there.
- `retrograde`: turns them against your travel, ready to brake, then holds.
- `brake` (or `fullstop`): retrograde full stop. It turns the dampeners off, turns retrograde, and burns at full thrust,
  Under 50 m/s it lets go and turns the dampeners back on, and they stop the rest. Near a planet it also pushes against
  gravity, so it doesn't settle into a fall.

A hold lasts until you touch the controls, run `manual`, or press the same slot again. The dampeners can stay on while
holding. Any of the three takes over from a route being flown. The map then says the autopilot is turning and doesn't
offer **RESUME ROUTE**; `go` from the hotbar flies the rest of the route. Turns aren't resumed after a reload or a
sector transfer.

Optional PB settings go in a `[SDX2 Autopilot Config]` section of its Custom Data:
- `TurnRate=0.6`: maximum turn rate, rad/s. The script also learns how quickly the gyros can speed up and slow a turn. It slows each turn in time to line up without swinging past, which matters for weak RCS gyros.
- `BrakeMargin=0.8`: share of the max acceleration planned for stops, bends and corners. The rest is held back so the ship can still stop if it runs ahead of the plan.
- `SpeedBuffer=0.03`: at the speed cap, this share under or over it counts as at speed. The ship doesn't keep burning to reach the last bit (the game doesn't always allow it), and doesn't flip round to shed a few m/s over it. Curves are flown by aiming 15 s ahead along the route, and small course errors at speed are left until they're worth a burn, so the ship holds its attitude when cruising.
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
stop=station                  station | surface (above a base) | well (outside it) | world (no well: 3 km over it) | sol | offset | point
accel=12.5                    F&B max acceleration when sent (m/s², 0 = unknown)
maxspeed=50000                speed cap (m/s): the route panel's, at most F&B's
thrust=1                      share of the drives' thrust it may use burning (the panel's Burn cap)
brake=1                       share it may use braking (the panel's Brake cap; missing = same as thrust)
clearance=100000              height kept round a world whose gravity isn't known yet (m)
eta=2051                      planned seconds, slowing to turn at each detour
wp=x,y,z,arrive,kind,name     in flight order; kind = pass | curve | detour | waypoint | target (last: the stop point)
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
showing it only in a cockpit, the default zoom, the map window (opens with the N list, keep the list open, side, size, theme), the pinned map (on, corner, size), the second screen (on, which screen, its key),
detours, the Offset (how far short of the destination it stops), loading new plans into the autopilot, and the speed dial fix. Changes apply at once and are
saved when it closes; **Defaults** puts them back.

Everything is kept in `%AppData%\SpaceEngineers\Storage\SDX2_SystemMap.ini`:
- `KeyMap` (`Shift+M`), `KeyAutopilot` (`Ctrl+M`), `KeyPin` (`Alt+M`), `KeySteer` (`Ctrl+Shift+M`), `KeySettings`
  (`Ctrl+Alt+OemQuestion`, which is Ctrl+Alt+/), `KeyCursor` (`F2`, held; extra Ctrl/Shift/Alt are fine): Ctrl/Shift/Alt plus a game key name, a side mouse button (`Mouse 4` / `Mouse 5`, stored as
  `ExtraButton1` / `ExtraButton2`), or `None`. A key fires only with
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
- `SecondScreen` (off), `SecondScreenMonitor` (0 = the first screen that isn't the main one, else 1, 2...),
  `KeySecondScreen` (`Ctrl+Alt+M`): the [map on a second screen](#map-on-a-second-screen-touch);
- `Theme`: `Classic`, `Tachi`, `UNN`, `OPA`, `Protogen` or `Belter` (K in the full map);
- `AvoidBodies`, `AvoidFields`, `OffsetKm` (0.5–20; how far short of the destination it stops);
- `RealShipIcons` (ship icon from real ships);
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
