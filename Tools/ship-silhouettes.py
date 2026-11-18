"""
Builds the map's sampled ship icons (Core/ShipSamples.cs) from real ships in a Space Engineers save: for each SDX2 core
class, one grid's plan view (looking down its thinnest axis, nose along the core's forward), as a fill mask.

Usage (with the game closed or not; the save is only read):
    python Tools/ship-silhouettes.py "<save folder>" [Class=Grid name ...]
Without names, the largest grid of each class is taken; a grid with no core is a Skiff (the smallest one with drives).
Block sizes come from the game's and the workshop mods' CubeBlock definitions.
"""
import glob, os, re, sys
import xml.etree.ElementTree as ET

GAME = r"C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers\Content\Data"
WORKSHOP = r"C:\Program Files (x86)\Steam\steamapps\workshop\content\244850"
XSI = "{http://www.w3.org/2001/XMLSchema-instance}type"
AXES = {"Forward": (2, -1), "Backward": (2, 1), "Up": (1, 1), "Down": (1, -1), "Right": (0, 1), "Left": (0, -1)}
LENGTH = 36  # mask cells nose to tail


def block_sizes():
    sizes = {}
    pat = re.compile(r"<Definition\b.*?</Definition>", re.S)
    for root in (GAME, WORKSHOP):
        for f in glob.glob(root + r"\**\*.sbc", recursive=True):
            try:
                txt = open(f, encoding="utf-8", errors="ignore").read()
            except OSError:
                continue
            if "<Size" not in txt:
                continue
            for m in pat.finditer(txt):
                d = m.group(0)
                sid = re.search(r"<SubtypeId>([^<]*)</SubtypeId>", d)
                sz = re.search(r'<Size\s+x="(\d+)"\s+y="(\d+)"\s+z="(\d+)"', d)
                if sid and sz:
                    sizes.setdefault(sid.group(1), tuple(int(v) for v in sz.groups()))
    return sizes


def core_class(sub):
    raw = sub[len("sdx_shipcore"):]
    if raw.lower() == "nocore":
        return "Skiff"
    return re.sub(r"(?<=[a-z])(?=[A-Z])", " ", raw)


def grids(save):
    for ev, el in ET.iterparse(os.path.join(save, "SANDBOX_0_0_0_.sbs"), events=("end",)):
        if el.tag != "MyObjectBuilder_EntityBase" or el.get(XSI) != "MyObjectBuilder_CubeGrid":
            continue
        g = {"name": el.findtext("DisplayName") or "", "blocks": [], "core": None, "drives": 0}
        for b in el.iter("MyObjectBuilder_CubeBlock"):
            sub = b.findtext("SubtypeName") or ""
            mn = b.find("Min")
            o = b.find("BlockOrientation")
            blk = {"s": sub, "min": tuple(int(float(mn.get(k))) for k in "xyz") if mn is not None else (0, 0, 0),
                   "f": o.get("Forward") if o is not None else "Forward", "u": o.get("Up") if o is not None else "Up"}
            g["blocks"].append(blk)
            if sub.startswith("sdx_shipcore") and g["core"] is None:
                g["core"] = blk
            if sub.startswith("sdx_drive"):
                g["drives"] += 1
        el.clear()
        yield g


def cells(g, sizes):
    for b in g["blocks"]:
        sx, sy, sz = sizes.get(b["s"], (1, 1, 1))
        ext = [0, 0, 0]
        fa, ua = AXES[b["f"]][0], AXES[b["u"]][0]
        ext[fa] = sz; ext[ua] = sy; ext[3 - fa - ua] = sx
        x0, y0, z0 = b["min"]
        for x in range(ext[0]):
            for y in range(ext[1]):
                for z in range(ext[2]):
                    yield (x0 + x, y0 + y, z0 + z)


def plan(g, sizes, fwd_name):
    pts = set(cells(g, sizes))
    fa, fs = AXES[fwd_name]
    others = [a for a in range(3) if a != fa]
    span = {a: max(p[a] for p in pts) - min(p[a] for p in pts) + 1 for a in range(3)}
    up = min(others, key=lambda a: span[a])  # look down the thinnest axis
    right = others[0] if others[1] == up else others[1]
    # right = forward x up (SE's axes), with up along +axis
    f = [0, 0, 0]; f[fa] = fs
    u = [0, 0, 0]; u[up] = 1
    r = [f[1] * u[2] - f[2] * u[1], f[2] * u[0] - f[0] * u[2], f[0] * u[1] - f[1] * u[0]]
    rs = r[right]
    flat = {(p[fa] * fs, p[right] * rs) for p in pts}
    return flat


def mask(flat):
    import math
    xs = [p[0] for p in flat]; ys = [p[1] for p in flat]
    x0, x1, y0, y1 = min(xs), max(xs), min(ys), max(ys)
    L = x1 - x0 + 1
    cell = L / LENGTH
    W = max(3, math.ceil((y1 - y0 + 1) / cell - 1e-9))
    pad = 1 if W % 2 == 0 else 0  # odd, so the middle row is the keel
    W += pad
    grid = [[0] * LENGTH for _ in range(W)]
    for (x, y) in flat:
        # every mask cell the block's cell overlaps (column 0 = nose)
        d0, d1 = (x1 - x) / cell, (x1 + 1 - x) / cell
        e0, e1 = (y - y0) / cell, (y + 1 - y0) / cell
        for i in range(int(d0 + 1e-9), min(LENGTH, math.ceil(d1 - 1e-9))):
            for j in range(int(e0 + 1e-9), min(W - pad, math.ceil(e1 - 1e-9))):
                grid[j][i] = 1
    # close 1-cell gaps (big blocks, open rooms)
    def get(j, i):
        return grid[j][i] if 0 <= j < W and 0 <= i < LENGTH else 0
    closed = [[1 if grid[j][i] or (get(j, i - 1) and get(j, i + 1)) or (get(j - 1, i) and get(j + 1, i)) else 0 for i in range(LENGTH)] for j in range(W)]
    return ["".join("#" if c else "." for c in row) for row in closed], (y1 - y0 + 1) / L


def main():
    save = sys.argv[1]
    picks = dict(a.split("=", 1) for a in sys.argv[2:])
    sizes = block_sizes()
    best = {}
    for g in grids(save):
        if "(stand-in)" in g["name"]:
            continue
        cls = core_class(g["core"]["s"]) if g["core"] else ("Skiff" if g["drives"] > 0 else None)
        if cls is None:
            continue
        if cls in picks:
            if g["name"] != picks[cls]:
                continue
        elif cls in best and (len(g["blocks"]) <= len(best[cls]["blocks"]) if cls != "Skiff" else len(g["blocks"]) >= len(best[cls]["blocks"])):
            continue
        best[cls] = g
    out = []
    for cls, g in sorted(best.items()):
        if g["core"]:
            fwd = g["core"]["f"]
        else:
            # no core: the ship's nose is against its drives' flames
            d = next(b for b in g["blocks"] if b["s"].startswith("sdx_drive"))
            opp = {"Forward": "Backward", "Backward": "Forward", "Up": "Down", "Down": "Up", "Left": "Right", "Right": "Left"}
            fwd = opp[d["f"]]
        rows, aspect = mask(plan(g, sizes, fwd))
        print(f"{cls}: {g['name']} ({len(g['blocks'])} blocks, nose {fwd})")
        for r in rows:
            print("   ", r)
        out.append((cls, g["name"], rows))
    path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Core", "ShipSamples.cs")
    with open(path, "w", encoding="utf-8") as f:
        f.write("// Generated by Tools/ship-silhouettes.py from ships in the SDX2 creative save: do not edit by hand.\n")
        f.write("namespace SDX2_SystemMap;\n\n")
        f.write("/// <summary>Plan views of real ships, one per core class sampled: rows across the ship ('#' = hull), the nose at column 0.</summary>\n")
        f.write("public static class ShipSamples\n{\n")
        f.write("    public static readonly (string Class, string From, string[] Rows)[] All =\n    {\n")
        for cls, name, rows in out:
            f.write(f"        (\"{cls}\", \"{name.replace(chr(34), '')}\", new[]\n        {{\n")
            for r in rows:
                f.write(f"            \"{r}\",\n")
            f.write("        }),\n")
        f.write("    };\n}\n")
    print("wrote", os.path.normpath(path))


if __name__ == "__main__":
    main()
