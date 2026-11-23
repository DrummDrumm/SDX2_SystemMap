namespace SDX2_SystemMap;

/// <summary>The settings window's Help tab: short instructions and explanations, as (heading, text) pairs. Keys named
/// here are the defaults; the Keys tab shows and changes them.</summary>
public static class MapHelp
{
    public static readonly (string Heading, string Text)[] Topics =
    {
        ("GETTING STARTED",
            "1. Sit in a cockpit and press N. Flip and Burn's list opens with the map window beside it and the Route Plan on its other side.\n" +
            "2. Pick a destination: an entry in the list, or one of your saved GPS points (in the full map, or Send to route planner on the terminal's GPS tab).\n" +
            "3. The map shows the route: burn, flip and brake points, detours, sector borders, ETA and water use.\n" +
            "4. In the Route Plan choose FLY MANUAL (you fly, with nose markers) or ENGAGE AUTOPILOT (needs the autopilot script on the ship).\n" +
            "The map only runs in worlds with SDX2's Flip and Burn mod. Elsewhere only this window opens."),

        ("THE MAP VIEWS",
            "- Map window: beside the N list. Shift+M cycles map window, full-screen map, off.\n" +
            "- Full-screen map: the whole system. F frames the route again, K changes the theme, V switches detours round worlds, Q moves the pinned map to the next corner.\n" +
            "- Pinned map: a small map kept in a corner while you fly (Alt+M).\n" +
            "- Destination view: a camera picture of where you're going, once you're within range of it.\n" +
            "- Second screen: the map on another monitor, with touch (Ctrl+Alt+M; set it up on the Map tab).\n" +
            "- Ctrl+M opens the list, the Route Plan and the map window in one go.\n" +
            "- Esc closes what is up (the list, the map window, the full map) without opening the pause menu."),

        ("PICKING A DESTINATION",
            "- An entry in Flip and Burn's N list. The list stays open after the pick.\n" +
            "- A saved GPS point: Shift+click it in the full map, or select it on the terminal's GPS tab and click Send to route planner.\n" +
            "- Stations: none are built in. A station is drawn, listed and routed to only when its GPS point, named region.place.STN, is in your own GPS list.\n" +
            "- Waypoints: add them in the Route Plan or in the full map; the route goes through them in order."),

        ("THE ROUTE PLAN",
            "- Top speed, and thrust or drive-signature caps for the burn and for the brake. A lower brake cap starts the flip earlier.\n" +
            "- Detours round worlds, and Keep out of asteroid fields (Route and HUD tab), bend the route round what's in the way.\n" +
            "- Offset: how far short of the destination the route stops.\n" +
            "- ENGAGE AUTOPILOT hands the route to the autopilot. FLY MANUAL closes the list and starts the burn guide. " +
            "CANCEL puts the panel away until the list is opened again or the destination changes."),

        ("WATER",
            "- The Route Plan shows the water the route burns against what your tanks hold, and warns when it runs them low or dry.\n" +
            "- Then the full map draws a WATER RANGE ring round your ship: about how far you can go and still stop on what's in the tanks.\n" +
            "- The autopilot script brakes to a stop by itself when the tanks are down to what stopping burns (LowWater=0 in its Custom Data turns that off)."),

        ("FLYING BY HAND (BURN GUIDE)",
            "- A MANUAL line on the HUD shows the destination, which point of how many, the distance, speed and ETA.\n" +
            "- A ring shows where your nose points and a marker where it should: BURN, BRAKE or CORRECT, with the thrust to use. " +
            "Put the ring on the marker; it turns green within 2 degrees.\n" +
            "- Under the label a countdown says what comes next (COAST, FLIP, TURN, ARRIVE), and while braking where you'll stop (overshoot, short, or on the mark).\n" +
            "- A fainter NEXT mark shows where the nose goes at the next change. Off the screen, an arrow on the edge points the way.\n" +
            "- Burn guide on the Route and HUD tab turns the markers off."),

        ("AUTOPILOT",
            "Optional: the Protogen Systems - Autopilot script in a programmable block on your own ship. The map hands it the route through the block's Custom Data.\n" +
            "- Needs a cockpit or remote control, gyros, and the fusion drives all facing one way.\n" +
            "- Touch the controls briefly and it takes the ship back when you let go; hold them longer, or switch the dampeners on, and it pauses and asks: RESUME ROUTE or stay manual.\n" +
            "- While it flies: STOP AUTOPILOT, FLY MANUAL (keeps the route for the burn guide) and FULL STOP (turns retrograde and burns down to a stop).\n" +
            "- Ctrl+Shift+M steers it round an asteroid on the route.\n" +
            "- Hotbar: drag the block onto the toolbar, pick Run, and type an argument: manual, go, prograde, retrograde, brake, approach, match."),

        ("CONTROLLER",
            "- LB + Select opens the list, Route Plan and map window. RB + Select opens the full-screen map.\n" +
            "- Full map: right stick moves the cursor, left stick moves the map. RT makes what the cursor is on the destination; LT adds or removes a waypoint. " +
            "R3 focuses on it. X follows the ship, Y frames the route, L3 turns with the ship, Start opens the route plan.\n" +
            "- Hold RB + LB: right stick turns and tilts the view, left stick zooms, Y re-plans, X clears the route.\n" +
            "- D-pad and A work the Route Plan's buttons. B is back, and closes what is up.\n" +
            "- The steady-controller and trigger fixes are now their own plugin, Protogen Systems Controller Fix."),

        ("YOUR SHIP AND THE LOOK",
            "- Your ship is drawn as a silhouette of its core class, turned the way it faces, with a marker showing where it's actually going.\n" +
            "- Ship icon: From Current Ship (Look tab) makes the icon from your ship's blocks; Flip steps through its four sides.\n" +
            "- Six themes: Classic, Tachi, UNN, Belter, Protogen, OPA. The Look tab changes the theme, its colours and the nose marker."),

        ("SETTINGS PER SHIP",
            "Settings for (bottom left of this window) picks whose settings you are changing:\n" +
            "- Each ship: every ship you sit in keeps its own settings, keys, colours and icon, saved under its name. A ship seen for the first time starts from the set for all ships.\n" +
            "- All ships: one set everywhere. Each ship's own are kept on disk and come back when you switch again.\n" +
            "Ships are told apart by name, so two ships with the same name share their settings."),

        ("KEYS (DEFAULTS)",
            "Shift+M: map window, full map, off.   Ctrl+M: route plan.   Alt+M: pin the map.   Ctrl+Shift+M: steer round an asteroid.\n" +
            "Ctrl+Alt+M: second screen.   F2 (hold): free the mouse on the map.   B: route panel to the other side (full map).\n" +
            "Ctrl+Alt+/: the plugin settings list.\n" +
            "Change them on the Keys tab: click a key and press the new keys (Esc cancels, Backspace unbinds). A key shared with one of the game's controls shows (!)."),

        ("SERVERS, RULES AND PAUSES",
            "- Everything runs on your PC. The map reads your own ship, your GPS list and Flip and Burn's list.\n" +
            "- It writes only to Flip and Burn's selection and to your own autopilot block. Nothing is sent anywhere else.\n" +
            "- It hides everything in admin mode and spectator. When you're AFK it is read-only: the displays stay current, but no keys, mouse, prompts or writes."),

        ("IF SOMETHING'S WRONG",
            "- No map, no keys: the world has no Flip and Burn mod, you're in admin mode or spectator, or Only in cockpit is on and you're on foot.\n" +
            "- A station is missing: add its GPS point, named region.place.STN, to your GPS list.\n" +
            "- ENGAGE AUTOPILOT does nothing: the ship needs the autopilot script in a programmable block; recompile the block after the script changes.\n" +
            "- A key does something else too: it is also one of the game's controls (it shows (!) on the Keys tab); rebind it.\n" +
            "- The settings file is Storage\\SDX2_SystemMap.ini in the game's AppData folder; ships' own settings are in Storage\\SDX2_SystemMap_Ships."),
    };
}
