# SDX2 System Map: in-game test checklist

Written 2026-11-19 after a desktop check of the build deployed that day (plugin `v0.1.0+4c18a42`, PB script copied
to `IngameScripts\local\Protogen Systems - Autopilot`). Tick a box when you have seen it work in game.

Test world: "Sigma Draconis Expanse 2 Creative World 2026-11-12 04-35", Iris hauler, seat "Crash Couch Nav".

## Read this first: the current save flies into Mars

The save written at 11:23:30 has all 9 fusion drives at 100% thrust override and no route. On load the ship
accelerates on its own (1.8 km/s to 10.9 km/s in about 70 s) straight at Mars. I paused and closed the game without
saving, so the save is unchanged.

- [ ] On the next load, switch the drives' override off at once, then save. (The save's `Backup` folder has
      copies from 10:58, 11:03 and 11:07; I did not check whether those have the override on.)

## Problems found during the check

- [ ] **Thrust override was left on.** "System Map: match route burn" set the drives' override once and nothing
      cleared it. Fixed 2026-11-19, not flown: the override now switches off when the route ends or is cleared, or
      when you leave the seat, with the note "Drives: override off". To test: match route burn, then clear the
      route; the drives should stop. It does not cover a save made mid-burn, or an override already on at load.
- [ ] **Settings key clashes with Pulsar.** Ctrl+Alt+/ opens the plugin's settings and Pulsar's own "Configure a
      plugin" list at the same time; closing the settings leaves Pulsar's list on screen. Not fixed.
- [ ] **Settings tabs: "Map" stays highlighted** (light blue) whichever tab is selected. Cosmetic. Not fixed.

## Checked on 2026-11-19 (by Claude, on the desktop)

- [x] Plugin loads through Pulsar; Flip and Burn and Spectrum are found.
- [x] Log line "Flip and Burn's marker labels patched: kept apart" appears on world load.
- [x] Flip and Burn's "WARNING / Hostile Space / km" label still draws beside its marker (one marker on screen).
- [x] Settings window opens with five tabs: Map, Keys, Route and HUD, Look, Destination view. All five draw.
- [x] Look tab: theme reads "OPA" with the Tachi palette and Tachi nose marker (your look kept after the name swap).
- [x] Settings file has `ThemeNames=2`, `Theme=OPA`, `Palette.OPA=Tachi`, `Overlay.OPA=TachiArcs`.
- [x] Destination view tab: range slider at 30 km, overlay picker, overlay colours and the live preview draw.
- [x] Pinned map draws top right with FOLLOW SHIP and "No destination: press N and pick one".
- [x] Nose marker draws in third person with the game's crosshair hidden.
- [x] AFK pause triggers ("paused: AFK" in the log).

Seen in your own flights and screenshots the same day:

- [x] Manual route guidance: the MANUAL status line, NEXT FLIP, "match route burn".
- [x] Burn guide and aim mark.
- [x] Taking over from the autopilot starts manual guidance.
- [x] Autopilot engages at speed and writes its turn log.

## Still to test: needs flying

- [ ] **Autopilot brakes above the cap.** Recompile the PB. Reach about 24 km/s, ENGAGE about 2,700 km from Deimos
      with the brake signature limit on. Expect "Braking at N%: over the 54% cap to make the stop" and a stop at
      the route's stop point. Expect "Too fast to stop in time: braking at 100%, will overshoot" if engaged later.
- [ ] **Manual brake guide stops above a world.** Fly by hand to Deimos with "match route burn". The BRAKE % should
      bring you to rest above the surface. Flip and Burn's own "Retroburn in" still aims at the centre.
- [ ] **Flip and Burn labels kept apart.** With a destination marker and a red WARNING marker close together on
      screen, the second label should sit clear of the first. Labels should also stay off the BURN/BRAKE tab, the
      top status line, the pinned map and the destination view.
- [ ] **Burn-guide tab above its mark**, and edge marks (NEXT FLIP) clear of the toolbar.
- [ ] **Mini map footer** is opaque and each column keeps to its half.
- [ ] **Curved routes** show the course-correction arc on the map.
- [ ] **Phobos crash fix.** A curved route starting beside a moon keeps its corners round the moon when sent to
      the autopilot.
- [ ] **Routes to ground stations** stop 3 km above them; the PB's ground guard holds.
- [ ] **Autopilot stop planning:** sideways drift counted, early flip, reserve for a short takeover.
- [ ] **Destination view in flight:** comes up within range of Deimos, takes the map preview's place, themed
      frame and title bar, overlay drawn over the picture.

## Still to test: no flying needed

- [ ] **GPS tab:** "Send to route planner" button on a selected GPS.
- [ ] **Hotbar:** PB `manual` keeps the route in MANUAL; "System Map: match route burn" on a drive group.
- [ ] **FLY MANUAL** button on the Route Plan panel and on the AUTOPILOT FLYING card.
- [ ] **Screen script** "System Map: route" on an LCD and a cockpit screen.
- [ ] **Second screen:** ROUTE PLAN button works; backgrounds fit the cut-corner frames.
- [ ] **Pinned map fix** and the corrected help bars, tooltips and notifications.
- [ ] **Per-theme colours:** change a colour on the Look tab, confirm it applies and survives a restart.
- [ ] **Belter theme:** pick "Belter" and confirm it is the charcoal and orange salvage look.

## Ultra drawing quality (added 2026-11-19)

Settings, Look tab, "Drawing quality: Standard / Ultra". Off by default.

- [x] The option shows on the Look tab and switches live (checked by Claude in game).
- [x] Pinned map: lines and circles draw with smooth edges in Ultra (compare `previews/ultra-on-ingame.png` and
      `previews/ultra-off-ingame.png`, both 4x enlargements). Line edges were tightened afterwards:
      `previews/ultra-tight-ingame.png` is the current look, `ultra-on-ingame.png` the first, softer one.
- [ ] Pinned map in Ultra, after the exact-placement change (not seen in game yet): the route line is one unbroken
      line with no notches or sideways steps between its pieces; hex grid and sector circles meet at their corners;
      no line is missing, doubled or cut short at the edge of the map panel.
- [ ] Full map (Shift+M), route panel, burn guide and nose marker in Ultra: nothing missing or misplaced.
- [ ] Destination view in Ultra: sharper picture (up to 1080 lines, 30 frames a second) and no frame-rate drop.
- [ ] Ultra still draws after leaving a world and loading another without restarting the game.

## Ship icon from the current ship, settings for each ship (added 2026-11-19, not seen in game)

- [ ] Look tab, "Ship icon: From Current Ship": the picture under it shows the ship you sit in, nose up, and the map
      draws the same shape. (The four sides of a test shape are in `previews/ship-icon-sides.png`.)
- [ ] "Flip" steps top, right side, bottom, left side; the picture and the map icon change at once; the choice is
      still there after a restart.
- [ ] The nose is the right end of the ship (the Ship Core's forward), and the top view is the top.
- [ ] On foot the picture reads "Sit in a ship to see its icon", with no error in the log.
- [ ] Bottom left, "Settings for: Each ship": change the theme in one ship, sit in another: it has the old theme;
      back in the first: its theme returns. Files appear in `Storage\SDX2_SystemMap_Ships`.
- [ ] "Settings for: All ships": the settings on screen stay and apply in every ship; switching back to "Each ship"
      brings the seated ship's own back.
- [ ] The pinned map, second screen and key binds behave when the settings swap on sitting down.

## Destination view camera (changed 2026-11-19 from the 13:10 clip, not seen in game)

- [ ] Braking toward a station with the drives lit: no grey bands or exhaust across the picture (the camera now
      sits beside the stern, clear of the plume, when the destination is astern).
- [x] The station stays still in the middle of the picture while the autopilot holds the ship (the aim no longer
      turns with the ship's small corrections). Seen steady in the 13:23 clip, 1.1 km to 920 m.
- [ ] The picture has no Flip and Burn squares or bands, no drive flames and no light glare (changed 2026-11-19,
      not seen in game: the view leaves all billboards out).
- [ ] AFK is read-only (changed 2026-11-19, not seen in game): after 30 s without input during an autopilot
      approach the destination view stays live, and the pinned map, status line, manual guide, burn guide, nose
      marker, asteroid warning and speedometer fix all keep showing current figures. Keys, mouse, prompts, notes
      and writes to the PB or Flip and Burn stay off until the first input. Admin mode still hides everything.
- [ ] Nose toward the destination: the picture is as before, from the hull, with no blocks in the way.
- [ ] Turning from nose-on to stern-on (the flip): the camera changes place once, without hopping back and forth.

## Earlier work, 13 to 17 November: status not re-checked

These were noted as untested when built. Some will have been used since; none was re-checked on 2026-11-19.

System Map
- [ ] N list stays open on a pick; ADD WAYPOINT; Shift+click sets the destination; Shift+right-click adds a waypoint.
- [ ] Waypoints above or below the plane (Shift+right-drag), dragging your own waypoints.
- [ ] Separate Burn and Brake sliders in thrust % or signature km.
- [ ] Autopilot controls window on the full map; stop and resume; route restored after a reload or sector change.
- [ ] Routes round worlds, stations and your own or your faction's bases.
- [ ] Follow ship turns the map to your heading (U in the full map).
- [ ] Themes UNN and Solar; station matcher; marker-first hover picking.
- [ ] Stays off in worlds without Flip and Burn; every key rebindable, with the clash warning; Mouse 4 and 5 binds.
- [ ] Hold-to-use-the-mouse key; clicks on a pop-up don't reach Flip and Burn's list.
- [ ] Ship with no core classed as a Skiff.
- [ ] Autopilot: drives only for thrust, turn-speed corners, rounded throttle and gyro writes.
- [ ] Admin, spectator and AFK pause with a controller plugged in.

Virtual Displays (its own `IN-GAME-TESTS.md` holds the detail)
- [ ] Virtual screens On/Off switch, distance slider and list on cockpit and seat terminals.

## Controller shortcuts, Esc / B and the N list (added 2026-11-19, deployed that day, not seen in game)

- [ ] RB + Select opens the full map; RB + Select again closes it.
- [ ] LB + Select opens the N list with the Route Plan; again closes the list. In the full map it goes to the list.
- [ ] Neither shortcut sets off a game action bound to the same buttons (the game's own controller binds are not blocked).
- [ ] Esc with the N list / Route Plan up closes them and the pause menu does not open; Esc with only the (unpinned) map
      window up closes that; Esc again (nothing up, or only the pinned window) opens the pause menu as normal.
- [ ] Controller B does the same as Esc: closes the N list / Route Plan (also while the D-pad has the buttons), the
      unpinned map window, the full map and a key capture in settings; B with nothing up does its usual game action.
- [ ] N list with a destination picked: no AUTOPILOT / MANUAL buttons under the list; the Route Plan is beside it
      every time, with ENGAGE AUTOPILOT, FLY MANUAL and CANCEL. After CANCEL the panel stays away until the list is opened again
      or the destination changes. RESUME ROUTE still shows under the list after a stop.
- [ ] N list while the autopilot flies: STOP AUTOPILOT, FLY MANUAL and FULL STOP are still under the list (unchanged).
- [ ] N list with no autopilot PB on the ship: the panel still opens; ENGAGE says there is no PB.
- [ ] The panel beside the N list is titled "ROUTE PLAN to <destination>" and its buttons read ENGAGE AUTOPILOT,
      FLY MANUAL, CANCEL, all three fitting the panel's width. N opens with the Route Plan on one side and the route
      preview on the other (as in the 13:44 screenshot) without pressing anything.
- [ ] N with no destination (the 13:50 screenshot): a "ROUTE PLAN" panel is beside the list saying "No destination
      yet"; picking a destination turns it into the full Route Plan.
- [ ] Ctrl+M with no route: no "pick a destination first" note; the N list opens with the Route Plan panel and the
      map preview. With a route it opens the plan as before; with no autopilot PB the plan still opens, with the note.
- [ ] Second screen: the button reads ROUTE PLAN (was AUTOPILOT) and opens the Route Plan with ENGAGE AUTOPILOT,
      FLY MANUAL, CANCEL. Full map and second screen card: titled ROUTE PLAN when idle, buttons ROUTE PLAN and
      FLY MANUAL; AUTOPILOT FLYING / FULL STOP unchanged. Help rows read "A route plan" and "Ctrl+M route plan";
      the settings Keys tab lists the bind as "Route plan".
- [ ] Second screen, ROUTE PLAN: a DESTINATIONS list stands beside
      the Route Plan (worlds with moons set in, stations, your GPS points, each with its distance; PREV / NEXT pages).
      Tapping a place sets the destination and opens the Route Plan; with ADD WAYPOINT on it adds a waypoint and the
      list is titled ADD WAYPOINT. With no route the list shows alone (no "pick a destination first" note). CLOSE
      hides the list until ROUTE PLAN is tapped again. Taps on the list don't reach the map; the side buttons move
      clear of it.
