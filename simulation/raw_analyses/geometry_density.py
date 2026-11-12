"""R3-G1 - Element geometry and selection density for the three datasets (REAL DATA).

For every bound element of the M3 (Revit OBJ), Best Graphene (ArchiCAD FBX) and LG MONO
(Revit IFC -> OBJ) models this script computes the axis-aligned bounding box from the
exported geometry and counts the neighbouring elements of the SAME MODULE whose boxes lie
within 0.5 m (the 'density' used in the task sheet, U2: "elements within 0.5 m of the
target"). Module scoping restricts candidates to the active module, so density is counted
inside the module only. M3 and Best Graphene module membership comes from the deployed
Unity module prefabs; LG MONO is a single volumetric unit.

Outputs (restricted/R3_geometry_density/, untracked):
  element_density.csv     one row per element: dataset, module, element_id, category,
                          bbox (m), diag_m, neighbours_0p5m, density_band
  density_summary.csv     per dataset: elements, median/P90 neighbours, band shares
Bands are fixed cut points on the pooled distribution: low <= 16, medium 17-26, high > 26
neighbours (pooled tertiles, rounded).
"""
import csv, io, os, re, struct, sys, zlib
from collections import Counter, defaultdict
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.dirname(HERE))
import paths
ASSETS = paths.assets()                     # proprietary exports (BIMMR_ASSETS)
OUT = os.path.join(paths.RESTRICTED, "R3_geometry_density")   # untracked: element-level results with real ids
RADIUS = 0.5
BANDS = (16, 26)
csv.field_size_limit(10 ** 9)


# ---------------------------------------------------------------- OBJ -------------
def obj_boxes(path, key_of, mode):
    """mode 'g': one element per group (Revit OBJ, id token from the group name);
    mode 'o': one element per object, groups are material submeshes (IFC importer)."""
    verts = []
    boxes = {}
    cur = None
    cur_idx = []

    def flush():
        if cur is not None and cur_idx:
            idx = np.unique(np.array(cur_idx, dtype=np.int64))
            v = V[idx]
            lo, hi = v.min(0), v.max(0)
            if cur in boxes:
                a, b = boxes[cur]; lo, hi = np.minimum(a, lo), np.maximum(b, hi)
            boxes[cur] = (lo, hi)

    # pass 1: vertices
    with open(path, "rb") as f:
        for line in f:
            if line[:2] == b"v ":
                p = line.split()
                verts.append((float(p[1]), float(p[2]), float(p[3])))
    V = np.asarray(verts, float)
    del verts
    # pass 2: faces per element
    with open(path, "rb") as f:
        for line in f:
            tag = line[:2]
            if tag == b"f ":
                for tok in line.split()[1:]:
                    i = int(tok.split(b"/")[0])
                    cur_idx.append(i - 1 if i > 0 else len(V) + i)
            elif (mode == "g" and tag == b"g ") or (mode == "o" and tag == b"o "):
                flush()
                cur = key_of(line[2:].decode("utf-8", "replace").strip())
                cur_idx = []
    flush()
    return boxes


# ---------------------------------------------------------------- FBX -------------
def _props(buf, pos, n, want_arrays):
    out = []
    for _ in range(n):
        t = chr(buf[pos]); pos += 1
        if t == "Y": v = struct.unpack_from("<h", buf, pos)[0]; pos += 2
        elif t == "C": v = buf[pos] != 0; pos += 1
        elif t == "I": v = struct.unpack_from("<i", buf, pos)[0]; pos += 4
        elif t == "F": v = struct.unpack_from("<f", buf, pos)[0]; pos += 4
        elif t == "D": v = struct.unpack_from("<d", buf, pos)[0]; pos += 8
        elif t == "L": v = struct.unpack_from("<q", buf, pos)[0]; pos += 8
        elif t in "SR":
            ln = struct.unpack_from("<I", buf, pos)[0]; pos += 4
            v = bytes(buf[pos:pos + ln]); pos += ln
        elif t in "fdlib":
            ln, enc, clen = struct.unpack_from("<III", buf, pos); pos += 12
            raw = bytes(buf[pos:pos + clen]); pos += clen
            if want_arrays:
                if enc == 1: raw = zlib.decompress(raw)
                dt = {"f": "<f4", "d": "<f8", "l": "<i8", "i": "<i4", "b": "<u1"}[t]
                v = np.frombuffer(raw, dtype=dt)
            else:
                v = None
        else:
            raise ValueError(t)
        out.append(v)
    return out, pos


def _walk(buf, pos, end, wide, path, cb):
    hdr = 25 if wide else 13
    fmt = "<QQQB" if wide else "<IIIB"
    while pos < end:
        end_off, nprops, plen, nlen = struct.unpack_from(fmt, buf, pos)
        if end_off == 0:
            return
        name = bytes(buf[pos + hdr: pos + hdr + nlen]).decode("ascii", "replace")
        ppos = pos + hdr + nlen
        here = path + (name,)
        descend = cb(here, buf, ppos, nprops)
        child = ppos + plen
        if descend and child < end_off:
            _walk(buf, child, end_off, wide, here, cb)
        pos = end_off


def fbx_boxes(path):
    buf = memoryview(open(path, "rb").read())
    wide = struct.unpack_from("<I", buf, 23)[0] >= 7500
    geoms, models, links = {}, {}, []
    state = {}
    unit = [1.0]

    def cb(here, b, ppos, n):
        if here == ("Objects",) or here == ("Connections",) or here == ("GlobalSettings",) \
                or here == ("GlobalSettings", "Properties70"):
            return True
        if here == ("GlobalSettings", "Properties70", "P"):
            p, _ = _props(b, ppos, n, False)
            if p[0] == b"UnitScaleFactor": unit[0] = float(p[-1])
            return False
        if here == ("Objects", "Geometry"):
            p, _ = _props(b, ppos, n, False); state["g"] = p[0]; return True
        if here == ("Objects", "Geometry", "Vertices"):
            p, _ = _props(b, ppos, n, True); geoms[state["g"]] = p[0].reshape(-1, 3); return False
        if here == ("Objects", "Model"):
            p, _ = _props(b, ppos, n, False)
            nm = p[1].split(b"\x00\x01")[0].decode("utf-8", "replace")
            state["m"] = p[0]; models[p[0]] = dict(name=nm, type=p[2].decode(), T=np.zeros(3),
                                                  R=np.zeros(3), S=np.ones(3))
            return True
        if here == ("Objects", "Model", "Properties70"): return True
        if here == ("Objects", "Model", "Properties70", "P"):
            p, _ = _props(b, ppos, n, False)
            key = {b"Lcl Translation": "T", b"Lcl Rotation": "R", b"Lcl Scaling": "S"}.get(p[0])
            if key: models[state["m"]][key] = np.array(p[-3:], float)
            return False
        if here == ("Connections", "C"):
            p, _ = _props(b, ppos, n, False)
            if p[0] == b"OO": links.append((p[1], p[2]))
            return False
        return False

    _walk(buf, 27, len(buf), wide, (), cb)
    parent = {c: p for c, p in links if c in models}
    geo_of = {p: c for c, p in links if c in geoms and p in models}

    def rot(r):
        x, y, z = np.radians(r)
        Rx = np.array([[1, 0, 0], [0, np.cos(x), -np.sin(x)], [0, np.sin(x), np.cos(x)]])
        Ry = np.array([[np.cos(y), 0, np.sin(y)], [0, 1, 0], [-np.sin(y), 0, np.cos(y)]])
        Rz = np.array([[np.cos(z), -np.sin(z), 0], [np.sin(z), np.cos(z), 0], [0, 0, 1]])
        return Rz @ Ry @ Rx                                   # FBX eEulerXYZ

    def world(uid):
        M = np.eye(4)
        while uid in models:
            m = models[uid]
            L = np.eye(4); L[:3, :3] = rot(m["R"]) * m["S"]; L[:3, 3] = m["T"]
            M = L @ M
            uid = parent.get(uid)
        return M

    boxes = {}
    scale = unit[0] / 100.0                                   # FBX centimetres -> metres
    for uid, m in models.items():
        if m["type"] != "Mesh" or uid not in geo_of: continue
        v = geoms[geo_of[uid]]
        W = world(uid)
        w = (v @ W[:3, :3].T + W[:3, 3]) * scale
        key = m["name"].strip()
        lo, hi = w.min(0), w.max(0)
        if key in boxes:
            a, c = boxes[key]; lo, hi = np.minimum(a, lo), np.maximum(c, hi)
        boxes[key] = (lo, hi)
    return boxes


# ---------------------------------------------------------------- helpers ---------
def csv_rows(path, id_cols):
    rows = list(csv.reader(io.StringIO(open(path, encoding="utf-8-sig", errors="replace").read())))
    h = rows[0]
    low = {c.lower() for c in id_cols}
    i = next(k for k, c in enumerate(h) if c.strip().lower() in low)
    return h, {r[i].strip(): r for r in rows[1:] if len(r) > i and r[i].strip()}


def module_ids(folder, pattern):
    from a5b_module_binding import parse_prefab
    base = os.path.join(ASSETS, folder)
    out = {}
    for f in sorted(os.listdir(base)):
        if not re.match(pattern, f): continue
        _, _, _, md = parse_prefab(os.path.join(base, f))
        out[f[:-7]] = {v for v in md.values() if v}
    return out


def neighbours(boxes, radius=RADIUS):
    ids = list(boxes)
    lo = np.array([boxes[i][0] for i in ids]); hi = np.array([boxes[i][1] for i in ids])
    cnt = np.zeros(len(ids), int)
    for k in range(len(ids)):
        gap = np.maximum(0, np.maximum(lo - hi[k], lo[k] - hi))   # per-axis separation
        d = np.sqrt((gap ** 2).sum(1))
        cnt[k] = int((d <= radius).sum()) - 1
    return dict(zip(ids, cnt))


def band(n): return "low" if n <= BANDS[0] else "medium" if n <= BANDS[1] else "high"


def to_metres(boxes):
    span = np.median([np.max(b[1] - b[0]) for b in boxes.values()])
    f = 0.001 if span > 50 else 1.0          # millimetre exports -> metres
    return {k: (a * f, b * f) for k, (a, b) in boxes.items()}, f


# ---------------------------------------------------------------- main ------------
def main():
    os.makedirs(OUT, exist_ok=True)
    out_rows = []

    # --- LG MONO (single unit) --------------------------------------------------
    h, rows = csv_rows(os.path.join(ASSETS, "My 3D Models/LG/MONO_metadata.csv"), {"GlobalId"})
    ci, ti = h.index("IfcClass"), h.index("TypeName")
    lg = obj_boxes(os.path.join(ASSETS, "My 3D Models/LG/MONO.obj"), lambda s: s, "o")
    lg = {k: v for k, v in lg.items() if k in rows}
    lg, f = to_metres(lg)
    print("LG boxes", len(lg), "unit factor", f)
    nb = neighbours(lg)
    for k, (a, b) in lg.items():
        out_rows.append(["LG MONO", "MONO", k, rows[k][ci], rows[k][ti], *np.round(a, 3), *np.round(b, 3),
                         round(float(np.linalg.norm(b - a)), 3), nb[k], band(nb[k])])

    # --- M3 (Revit OBJ; modules from prefabs) ------------------------------------
    h, rows = csv_rows(os.path.join(ASSETS, "My 3D Models/M3 Building/M3 Building.csv"),
                       {"Element_Id", "ID and Categories.Element ID", "Element ID", "ElementID"})
    ci = h.index("Category") if "Category" in h else None
    fi = h.index("Family and Type") if "Family and Type" in h else None
    tok = lambda s: s[s.rfind("_") + 1:] if "_" in s else ""
    m3 = obj_boxes(os.path.join(ASSETS, "My 3D Models/M3 Building/ExportedModel.obj"), tok, "g")
    m3, f = to_metres(m3)
    print("M3 boxes", len(m3), "unit factor", f)
    mods = module_ids("My 3D Models/M3 Building/Prefabs", r"^M\d-\d\.prefab$")
    for mod, ids in mods.items():
        sub = {i: m3[i] for i in ids if i in m3}
        nb = neighbours(sub)
        for k, (a, b) in sub.items():
            r = rows.get(k, [])
            out_rows.append(["M3", mod, k, r[ci] if r and ci is not None else "", r[fi] if r and fi is not None else "",
                             *np.round(a, 3), *np.round(b, 3), round(float(np.linalg.norm(b - a)), 3), nb[k], band(nb[k])])

    # --- Best Graphene (ArchiCAD FBX; modules from prefabs) -----------------------
    h, rows = csv_rows(os.path.join(ASSETS, "My 3D Models/Yoju/20250709_Best Graphene/archicad_elements_20250709_101457.csv"),
                       {"Element ID"})
    ci = h.index("Element_Type") if "Element_Type" in h else None
    fi = h.index("Element Type") if "Element Type" in h else None
    bg = fbx_boxes(os.path.join(ASSETS, "My 3D Models/Yoju/20250709_Best Graphene/여주 베스트그래핀_작업중.fbx"))
    print("BG boxes", len(bg))
    mods = module_ids("My 3D Models/Yoju/Prefabs_20250714", r"^M\d-\d\.prefab$")
    for mod, ids in mods.items():
        sub = {i: bg[i] for i in ids if i in bg}
        nb = neighbours(sub)
        for k, (a, b) in sub.items():
            r = rows.get(k, [])
            out_rows.append(["Best Graphene", mod, k, r[ci] if r and ci is not None else "", r[fi] if r and fi is not None else "",
                             *np.round(a, 3), *np.round(b, 3), round(float(np.linalg.norm(b - a)), 3), nb[k], band(nb[k])])

    hdr = ["dataset", "module", "element_id", "category", "type", "xmin", "ymin", "zmin", "xmax", "ymax", "zmax",
           "diag_m", "neighbours_0p5m", "density_band"]
    with open(os.path.join(OUT, "element_density.csv"), "w", newline="", encoding="utf-8") as fh:
        w = csv.writer(fh); w.writerow(hdr); w.writerows(out_rows)

    summ = []
    for ds in ("M3", "Best Graphene", "LG MONO"):
        r = [x for x in out_rows if x[0] == ds]
        n = np.array([x[12] for x in r]); bands = Counter(x[13] for x in r)
        mods = sorted({x[1] for x in r})
        summ.append(dict(dataset=ds, modules=len(mods), elements=len(r),
                         elements_per_module=round(len(r) / len(mods), 1),
                         neighbours_median=float(np.median(n)), neighbours_p90=float(np.percentile(n, 90)),
                         neighbours_mean=round(float(n.mean()), 2),
                         low_pct=round(100 * bands["low"] / len(r), 1), medium_pct=round(100 * bands["medium"] / len(r), 1),
                         high_pct=round(100 * bands["high"] / len(r), 1),
                         median_diag_m=round(float(np.median([x[11] for x in r])), 3)))
    with open(os.path.join(OUT, "density_summary.csv"), "w", newline="", encoding="utf-8") as fh:
        w = csv.DictWriter(fh, fieldnames=list(summ[0])); w.writeheader(); w.writerows(summ)
    for s in summ: print(s)


if __name__ == "__main__":
    main()
