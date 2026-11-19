"""Add SDX2's planets, moons and station GPS points to a single-player creative save.

The official SDX2 creative world has no planets. This copies the server's geography from Core/SystemData.cs (the
plugin's own data) into a save, so Flip and Burn's N list, gravity and the map's detours can be tested the way they
are on the server:

- every planet and moon that has a planet generator in the world's mods, at its server centre, written as a planet
  entity in SANDBOX_0_0_0_.sbs plus its small .vx2 storage file (an untouched procedural planet);
- the station GPS points (Belt.Tycho.STN ...) for the local player, from Tools/stations.local.tsv: a file you keep
  yourself (gps name, title, x, y, z, tab separated; not in the repository, and the plugin has no station list of
  its own). Without it no stations are added;
- a stand-in grid at each station: the real stations are grids in the server's own world, not in any of its mods,
  so each gets a static, unowned cross of armour arms (77.5 m across each way: something to fly round and stop
  short of) with an Installation core, a battery and a beacon named after it ("Rhea Station (stand-in)"). Stations on
  a world's ground (Rhea, Ganymede ...) sit on it as on the server, half buried.

Not added: Sol (Real Stars is server-only, and Flip and Burn ignores it), Eros (no planet generator: it's a voxel
asteroid on the server) and the Asteroid Ores zones (the server's own config).

Planet sizes: SystemData's radii (the community map's). The server's exact diameters aren't published.

Run with the game closed:
    python Tools/add-server-map.py "<path to save folder>"
It backs up the save into <save>/Backup/<timestamp> map-tool/ first, skips planets, GPS points and stand-ins that
are already there, and deletes SANDBOX_0_0_0_.sbsB5 (the binary cache, which would otherwise be loaded instead of the edit).
"""
import datetime
import glob
import gzip
import os
import random
import re
import shutil
import struct
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
SYSTEM_DATA = os.path.join(HERE, "..", "Core", "SystemData.cs")
WORKSHOP = r"C:\Program Files (x86)\Steam\steamapps\workshop\content\244850"
PLANET_MODS = ["2815515313", "3395240098", "3408339376", "3411688088"]

# Body name -> planet generator subtype (SDX2 Planets mod and the Solar System Pack).
GENERATORS = {
    "Mercury": "[SSP] Mercury", "Venus": "[SSP] Venus", "Earth": "[SSP] Earth", "Luna": "[SSP] Luna",
    "Mars": "Mars_E", "Phobos": "Phobos", "Deimos": "Deimos",
    "Ceres": "Ceres", "Vesta": "Vesta", "Pallas": "Pallas", "Hygeia": "Hygeia",
    "Jupiter": "sdx_jupiterAdjusted", "Io": "[SSP] Io", "Europa": "[SSP] Europa", "Ganymede": "[SSP] Ganymede",
    "Callisto": "[SSP] Callisto",
    "Saturn": "sdx_saturnAdjusted", "Mimas": "[SSP] Mimas", "Enceladus": "[SSP] Enceladus", "Tethys": "[SSP] Tethys",
    "Dione": "[SSP] Dione", "Rhea": "[SSP] Rhea", "Titan": "[SSP] Titan", "Iapetus": "[SSP] Iapetus",
    "Phoebe": "Phoebe",
    "Uranus": "sdx_uranusAdjusted", "Miranda": "[SSP] Miranda", "Ariel": "[SSP] Ariel", "Umbriel": "[SSP] Umbriel",
    "Titania": "[SSP] Titania", "Oberon": "[SSP] Oberon",
    "Neptune": "sdx_neptuneAdjusted", "Triton": "[SSP] Triton",
    "Pluto": "[SSP] Pluto", "Charon": "[SSP] Charon",
}
SEED = 12345
STATION_COLOR = (117, 201, 241)  # light blue

# Voxel material table (index -> name). An untouched planet stores no voxels, and any entry whose index differs from
# the running game's makes the loader remap the planet's materials, so it's left empty.
MATERIALS = []


def read_system_data():
    src = open(SYSTEM_DATA, encoding="utf-8").read()
    num = r"(-?[\d.]+)"
    worlds = [(m[0], float(m[2]), float(m[3]), float(m[4]), float(m[5])) for m in re.findall(
        r'W\("([^"]+)", "([^"]*)", ' + num + ", " + num + ", " + num + ", " + num, src)]
    stations = []
    local = os.path.join(HERE, "stations.local.tsv")
    if os.path.exists(local):
        for line in open(local, encoding="utf-8"):
            f = line.rstrip(chr(10)).split(chr(9))
            if line.startswith("#") or len(f) != 5:
                continue
            stations.append((f[0], f[1], float(f[2]), float(f[3]), float(f[4])))
    else:
        print("no Tools/stations.local.tsv: no station GPS points or stand-in grids are added")
    return worlds, stations


def read_generators():
    gens = {}
    for mod in PLANET_MODS:
        for f in glob.glob(os.path.join(WORKSHOP, mod, "Data", "**", "*.sbc"), recursive=True):
            s = open(f, encoding="utf-8-sig", errors="replace").read()
            for d in re.findall(r"<Definition[^>]*PlanetGeneratorDefinition.*?</Definition>", s, re.S) + \
                     re.findall(r"<PlanetGeneratorDefinition>.*?</PlanetGeneratorDefinition>", s, re.S):
                sub = re.search(r"<SubtypeId>(.*?)</SubtypeId>", d).group(1)
                hill = re.search(r'<HillParams\s+Min\s*=\s*"([^"]+)"\s+Max\s*=\s*"([^"]+)"', d)
                atm = re.search(r"<AtmosphereSettings>.*?<Scale>([^<]+)</Scale>", d, re.S)
                has_atm = re.search(r"<HasAtmosphere>([^<]+)<", d)
                falloff = re.search(r"<GravityFalloffPower>([^<]+)<", d)
                grav = re.search(r"<SurfaceGravity>([^<]+)<", d)
                gens[sub] = dict(
                    hill_min=float(hill.group(1)) if hill else 0.0,
                    hill_max=float(hill.group(2)) if hill else 0.0,
                    atm_scale=float(atm.group(1)) if atm else 0.0,
                    has_atm=bool(has_atm) and has_atm.group(1).strip().lower() == "true",
                    falloff=float(falloff.group(1)) if falloff else 7.0,
                    gravity=float(grav.group(1)) if grav else 1.0)
    return gens


def octree_size(diameter):
    n = 32
    while n < diameter:
        n *= 2
    return n


def write_string(b, s):
    data = s.encode("utf-8")
    write_7bit(b, len(data))
    b += data


def write_7bit(b, v):
    while v >= 0x80:
        b.append((v & 0x7F) | 0x80)
        v >>= 7
    b.append(v)


def chunk(b, kind, version, payload):
    write_7bit(b, kind)
    write_7bit(b, version)
    write_7bit(b, len(payload))
    b += payload


def vx2_bytes(generator, radius, size):
    """An untouched planet storage: MyOctreeStorage file version 1 with a MyPlanetStorageProvider (id 10042)."""
    b = bytearray()
    write_string(b, "Octree")
    write_7bit(b, 1)
    chunk(b, 1, 1, struct.pack("<iiiiB", 4, size, size, size, 0))  # StorageMetaData
    mats = bytearray(struct.pack("<i", len(MATERIALS)))
    for i, name in enumerate(MATERIALS):
        write_7bit(mats, i)
        write_string(mats, name)
    chunk(b, 2, 1, bytes(mats))  # MaterialIndexTable
    prov = bytearray(struct.pack("<iqqd", 10042, 1, SEED, radius))
    write_string(prov, generator)
    chunk(b, 9, 2, bytes(prov))  # DataProvider
    chunk(b, 3, 2, b"")  # MacroContentNodes
    chunk(b, 4, 2, b"")  # MacroMaterialNodes
    # Root leaf key, as MyCellCoord.PackId64 writes it: lod = log2(size) - 4 in the top 4 bits. For storages over
    # 512 km (Jupiter, Saturn) the lod is 16 and the game's own shift wraps to 0; it looks the root up by the same
    # wrapped key, so write what the game would.
    root = struct.pack("<Q", ((size.bit_length() - 1 - 4) << 60) & 0xFFFFFFFFFFFFFFFF)
    chunk(b, 5, 3, root)  # ContentLeafProvider
    chunk(b, 7, 3, root)  # MaterialLeafProvider
    write_7bit(b, 0xFFFF)  # EndOfFile
    write_7bit(b, 0)
    write_7bit(b, 0)
    return gzip.compress(bytes(b))


def f(v):
    return repr(float(v))


def planet_xml(entity_id, name, storage, corner, radius, gen, g):
    atm_radius = (1 + g["atm_scale"] if g["atm_scale"] > 1 else 1.75) * radius
    return f"""    <MyObjectBuilder_EntityBase xsi:type="MyObjectBuilder_Planet">
      <SubtypeName />
      <EntityId>{entity_id}</EntityId>
      <PersistentFlags>CastShadows InScene</PersistentFlags>
      <Name>{name}</Name>
      <PositionAndOrientation>
        <Position x="{f(corner[0])}" y="{f(corner[1])}" z="{f(corner[2])}" />
        <Forward x="-0" y="-0" z="-1" />
        <Up x="0" y="1" z="0" />
        <Orientation>
          <X>0</X>
          <Y>0</Y>
          <Z>0</Z>
          <W>1</W>
        </Orientation>
      </PositionAndOrientation>
      <LocalPositionAndOrientation xsi:nil="true" />
      <ContentChanged>false</ContentChanged>
      <BoulderPlanetId xsi:nil="true" />
      <BoulderSectorId xsi:nil="true" />
      <BoulderItemId xsi:nil="true" />
      <CreatedByUser>false</CreatedByUser>
      <IsGlobalEncounterAsteroid>false</IsGlobalEncounterAsteroid>
      <StorageName>{storage}</StorageName>
      <Radius>{f(radius)}</Radius>
      <HasAtmosphere>{str(g["has_atm"]).lower()}</HasAtmosphere>
      <AtmosphereRadius>{f(atm_radius)}</AtmosphereRadius>
      <MinimumSurfaceRadius>{f(radius * (1 + g["hill_min"]))}</MinimumSurfaceRadius>
      <MaximumHillRadius>{f(radius * (1 + g["hill_max"]))}</MaximumHillRadius>
      <AtmosphereWavelengths>
        <X>0.65</X>
        <Y>0.57</Y>
        <Z>0.475</Z>
      </AtmosphereWavelengths>
      <GravityFalloff>{f(g["falloff"])}</GravityFalloff>
      <MarkAreaEmpty>true</MarkAreaEmpty>
      <SurfaceGravity>{f(g["gravity"])}</SurfaceGravity>
      <SpawnsFlora>false</SpawnsFlora>
      <ShowGPS>false</ShowGPS>
      <SpherizeWithDistance>true</SpherizeWithDistance>
      <PlanetGenerator>{gen}</PlanetGenerator>
      <Seed>{SEED}</Seed>
    </MyObjectBuilder_EntityBase>
"""


def gps_xml(name, pos, color):
    r, g, b = color
    packed = (255 << 24) | (b << 16) | (g << 8) | r
    return f"""            <Entry>
              <name>{name}</name>
              <description />
              <coords>
                <X>{pos[0]}</X>
                <Y>{pos[1]}</Y>
                <Z>{pos[2]}</Z>
              </coords>
              <isFinal>true</isFinal>
              <showOnHud>true</showOnHud>
              <alwaysVisible>false</alwaysVisible>
              <color>
                <PackedValue>{packed}</PackedValue>
                <X>{r}</X>
                <Y>{g}</Y>
                <Z>{b}</Z>
                <R>{r}</R>
                <G>{g}</G>
                <B>{b}</B>
                <A>255</A>
              </color>
              <entityId>0</entityId>
              <isObjective>false</isObjective>
              <contractId>0</contractId>
              <IsGlobalEncounterGPS>false</IsGlobalEncounterGPS>
              <DisplayName />
            </Entry>
"""


ARM = 15  # blocks out from the centre along each axis: 31 blocks (77.5 m) across


def new_id(rng, ids):
    eid = str(rng.randrange(1 << 62, 1 << 63))
    while eid in ids:
        eid = str(rng.randrange(1 << 62, 1 << 63))
    ids.add(eid)
    return eid


def block_xml(kind, sub, pos, eid=None, extra=""):
    x, y, z = pos
    ident = f"\n          <EntityId>{eid}</EntityId>" if eid else ""
    return (f'        <MyObjectBuilder_CubeBlock xsi:type="MyObjectBuilder_{kind}">\n'
            f"          <SubtypeName>{sub}</SubtypeName>{ident}\n"
            f'          <Min x="{x}" y="{y}" z="{z}" />\n'
            f'          <BlockOrientation Forward="Forward" Up="Up" />\n'
            f'          <ColorMaskHSV x="0.55" y="-0.6" z="0.05" />\n{extra}'
            f"        </MyObjectBuilder_CubeBlock>\n")


def standin_xml(rng, ids, name, pos):
    """A static grid: armour arms along X, Y and Z through the station's position, and the working blocks by the centre."""
    blocks = ""
    cells = {(0, 0, 0)}
    for a in range(1, ARM + 1):
        for c in ((a, 0, 0), (-a, 0, 0), (0, a, 0), (0, -a, 0), (0, 0, a), (0, 0, -a)):
            cells.add(c)
    for c in sorted(cells):
        blocks += block_xml("CubeBlock", "LargeBlockArmorBlock", c)
    blocks += block_xml("FunctionalBlock", "sdx_shipcoreInstallation", (1, 0, 1), new_id(rng, ids),
                        "          <Enabled>true</Enabled>\n")
    blocks += block_xml("BatteryBlock", "LargeBlockBatteryBlock", (-1, 0, 1), new_id(rng, ids),
                        "          <Enabled>true</Enabled>\n          <CurrentStoredPower>3</CurrentStoredPower>\n"
                        "          <ProducerEnabled>true</ProducerEnabled>\n")
    blocks += block_xml("Beacon", "LargeBlockBeacon", (1, 0, -1), new_id(rng, ids),
                        f"          <CustomName>{name}</CustomName>\n          <Enabled>true</Enabled>\n"
                        f"          <BroadcastRadius>20000</BroadcastRadius>\n          <HudText>{name}</HudText>\n")
    eid = new_id(rng, ids)
    return f"""    <MyObjectBuilder_EntityBase xsi:type="MyObjectBuilder_CubeGrid">
      <SubtypeName />
      <EntityId>{eid}</EntityId>
      <PersistentFlags>CastShadows InScene</PersistentFlags>
      <PositionAndOrientation>
        <Position x="{f(pos[0])}" y="{f(pos[1])}" z="{f(pos[2])}" />
        <Forward x="-0" y="-0" z="-1" />
        <Up x="0" y="1" z="0" />
        <Orientation>
          <X>0</X>
          <Y>0</Y>
          <Z>0</Z>
          <W>1</W>
        </Orientation>
      </PositionAndOrientation>
      <LocalPositionAndOrientation xsi:nil="true" />
      <GridSizeEnum>Large</GridSizeEnum>
      <CubeBlocks>
{blocks}      </CubeBlocks>
      <IsStatic>true</IsStatic>
      <IsUnsupportedStation>true</IsUnsupportedStation>
      <Skeleton />
      <LinearVelocity x="0" y="0" z="0" />
      <AngularVelocity x="0" y="0" z="0" />
      <XMirroxPlane xsi:nil="true" />
      <YMirroxPlane xsi:nil="true" />
      <ZMirroxPlane xsi:nil="true" />
      <BlockGroups />
      <Handbrake>false</Handbrake>
      <DisplayName>{name}</DisplayName>
      <DestructibleBlocks>true</DestructibleBlocks>
      <IsRespawnGrid>false</IsRespawnGrid>
      <LocalCoordSys>0</LocalCoordSys>
      <TargetingTargets />
    </MyObjectBuilder_EntityBase>
"""


def game_running():
    try:
        out = subprocess.run(["tasklist", "/FI", "IMAGENAME eq SpaceEngineers.exe"], capture_output=True, text=True).stdout
        return "SpaceEngineers.exe" in out
    except OSError:
        return False


def main():
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    save = sys.argv[1]
    sbs_path, sbc_path = os.path.join(save, "SANDBOX_0_0_0_.sbs"), os.path.join(save, "Sandbox.sbc")
    if not (os.path.isfile(sbs_path) and os.path.isfile(sbc_path)):
        sys.exit(f"not a save folder: {save}")
    if game_running():
        sys.exit("Space Engineers is running: close it first (it would overwrite the edit).")

    worlds, stations = read_system_data()
    gens = read_generators()
    sbs = open(sbs_path, encoding="utf-8-sig").read()
    sbc = open(sbc_path, encoding="utf-8-sig").read()

    backup = os.path.join(save, "Backup", datetime.datetime.now().strftime("%Y-%m-%d %H%M%S") + " map-tool")
    os.makedirs(backup)
    for name in os.listdir(save):
        p = os.path.join(save, name)
        if os.path.isfile(p):
            shutil.copy2(p, backup)
    print(f"backup: {backup}")

    existing = set(re.findall(r'xsi:type="MyObjectBuilder_Planet">.*?<Name>([^<]*)</Name>', sbs, re.S))
    ids = set(re.findall(r"<EntityId>(\d+)</EntityId>", sbs))
    rng = random.Random()
    added, skipped = [], []
    xml = ""
    for name, x, y, z, radius in worlds:
        gen = GENERATORS.get(name)
        if gen is None or gen not in gens:
            skipped.append(name)
            continue
        if name in existing:
            continue
        g = gens[gen]
        size = octree_size(2 * (radius + radius * g["hill_max"]))
        corner = (x - size / 2, y - size / 2, z - size / 2)
        storage = f"{name}-{SEED}d{int(round(radius * 2))}"
        with open(os.path.join(save, storage + ".vx2"), "wb") as fh:
            fh.write(vx2_bytes(gen, radius, size))
        eid = str(rng.randrange(1 << 62, 1 << 63))
        while eid in ids:
            eid = str(rng.randrange(1 << 62, 1 << 63))
        ids.add(eid)
        xml += planet_xml(eid, name, storage, corner, radius, gen, g)
        added.append(name)
    # Stand-in station grids
    grids = set(re.findall(r"<DisplayName>([^<]*)</DisplayName>", sbs))
    placed = []
    for _, title, x, y, z in stations:
        name = f"{title} (stand-in)"
        if name in grids:
            continue
        xml += standin_xml(rng, ids, name, (x, y, z))
        placed.append(title)
    if xml:
        i = sbs.index("</SectorObjects>")
        sbs = sbs[:i] + xml.lstrip() + "  " + sbs[i:]
        with open(sbs_path, "w", encoding="utf-8") as fh:
            fh.write(sbs)
    print(f"planets added ({len(added)}): {', '.join(added) or 'none'}")
    print(f"not added (no generator): {', '.join(skipped)}")
    print(f"station stand-ins added ({len(placed)}): {', '.join(placed) or 'none'}")

    # Station GPS points for the local player (the identity of the first AllPlayersData entry).
    m = re.search(r"<AllPlayersData>.*?<IdentityId>(\d+)</IdentityId>", sbc, re.S)
    if not m:
        sys.exit("no player in Sandbox.sbc: GPS points not added")
    identity = m.group(1)
    have = set(re.findall(r"<name>([^<]*)</name>", sbc))
    entries = "".join(gps_xml(n, (x, y, z), STATION_COLOR) for n, _, x, y, z in stations if n not in have)
    if entries:
        item = re.search(r"<item>\s*<Key>" + identity + r"</Key>\s*<Value>\s*<Entries>(.*?)</Entries>", sbc, re.S)
        if item:
            sbc = sbc[:item.end(1)] + entries + "          " + sbc[item.end(1):]
        else:
            new_item = (f"      <item>\n        <Key>{identity}</Key>\n        <Value>\n          <Entries>\n{entries}"
                        f"          </Entries>\n        </Value>\n      </item>\n")
            if "<Gps>" in sbc:
                i = sbc.index("<dictionary>", sbc.index("<Gps>")) + len("<dictionary>\n")
                sbc = sbc[:i] + new_item + sbc[i:]
            else:
                i = sbc.index("</MyObjectBuilder_Checkpoint>")
                sbc = sbc[:i] + f"  <Gps>\n    <dictionary>\n{new_item}    </dictionary>\n  </Gps>\n" + sbc[i:]
        with open(sbc_path, "w", encoding="utf-8") as fh:
            fh.write(sbc)
    print(f"station GPS added: {entries.count('<Entry>')}")

    b5 = sbs_path + "B5"
    if os.path.exists(b5) and (added or entries or placed):
        os.remove(b5)
        print("removed SANDBOX_0_0_0_.sbsB5 (the game rebuilds it on the next save)")


if __name__ == "__main__":
    main()
