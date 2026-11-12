"""A5b - Binding coverage of the module prefabs as deployed on the headset (REAL DATA).

The Unity module prefabs are the objects participants actually selected. For every module
this script parses the prefab YAML and applies the runtime rule of
BIMElementMetadataReader: an object is bound when it, or one of up to 8 ancestors, carries a
Metadata component with a non-empty value for the first matching stable-id key.

Per module it reports:
  renderable_objects          GameObjects with a MeshRenderer (selectable geometry)
  bound_self                  renderable objects with their own Metadata id
  bound_via_parent            renderable objects bound only through an ancestor
  unbound                     renderable objects with no id (not selectable after U1)
  bound_ids                   distinct element ids among bound renderable objects
  multi_mesh_ids              ids shared by more than one renderable object
  duplicate_metadata_ids      ids carried by more than one Metadata component
  coverage_pct                (bound_self + bound_via_parent) / renderable_objects
  one_to_one_pct              renderable objects whose id is carried by exactly one object
"""
import csv, json, os, re, sys
from collections import Counter, defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
import paths
ASSETS = paths.assets()                     # proprietary exports (BIMMR_ASSETS)
OUT = os.path.join(paths.RESTRICTED, "A5_binding_coverage")   # untracked: element-level results with real ids
METADATA_GUID = "eccd944b81ac37c498c806ff882b16d5"
STABLE_ID_KEYS = ["GlobalId", "IfcGuid", "IFCGuid", "Ifc_Guid", "ifcGuid", "RevitUniqueId", "UniqueId",
                  "Revit_UniqueId", "ElementId", "ElementID", "Element_Id",
                  "ID and Categories.Element ID", "General Parameters.Element ID",
                  "General Parameters.Unique ID", "Element ID"]
SETS = [
    ("M3 (Revit, OBJ)", "My 3D Models/M3 Building/Prefabs", r"^M\d-\d\.prefab$"),
    ("Best Graphene (ArchiCAD, FBX)", "My 3D Models/Yoju/Prefabs_20250714", r"^M\d-\d\.prefab$"),
]
DOC = re.compile(r"^--- !u!(\d+) &(-?\d+)", re.M)


def yaml_str(v):
    v = v.strip()
    if v.startswith('"'):
        try:
            return json.loads(v)
        except Exception:
            return v.strip('"')
    if v.startswith("'"):
        return v[1:-1].replace("''", "'")
    return v


def parse_prefab(path):
    text = open(path, encoding="utf-8", errors="replace").read()
    heads = list(DOC.finditer(text))
    go_name, father, tr_go, renderers, md = {}, {}, {}, set(), {}
    for i, h in enumerate(heads):
        cls, fid = int(h.group(1)), h.group(2)
        body = text[h.end(): heads[i + 1].start() if i + 1 < len(heads) else len(text)]
        if cls == 1:
            m = re.search(r"^  m_Name: (.*)$", body, re.M)
            go_name[fid] = yaml_str(m.group(1)) if m else ""
        elif cls == 4:
            g = re.search(r"m_GameObject: \{fileID: (-?\d+)\}", body)
            f = re.search(r"m_Father: \{fileID: (-?\d+)\}", body)
            if g:
                tr_go[fid] = g.group(1)
                father[g.group(1)] = f.group(1) if f else "0"
        elif cls == 23:
            g = re.search(r"m_GameObject: \{fileID: (-?\d+)\}", body)
            if g: renderers.add(g.group(1))
        elif cls == 114 and METADATA_GUID in body:
            g = re.search(r"m_GameObject: \{fileID: (-?\d+)\}", body)
            props = {}
            for k, v in re.findall(r"^  - Key: (.*)\n    Value: ?(.*)$", body, re.M):
                k, v = yaml_str(k), yaml_str(v)
                if v and v.lower() != "<undefined>" and k not in props:
                    props[k] = v
            sid = next((props[k] for k in STABLE_ID_KEYS if k in props), "")
            if g: md[g.group(1)] = sid
    # father map is Transform fileID -> convert to GameObject fileID
    parent_go = {go: tr_go.get(tf, None) for go, tf in father.items()}
    return go_name, parent_go, renderers, md


def analyse(path):
    names, parent, renderers, md = parse_prefab(path)
    per_obj_id, self_b, via_parent, unbound = {}, 0, 0, []
    for go in renderers:
        sid, cur, depth = md.get(go, ""), go, 0
        if sid:
            self_b += 1
        else:
            cur = parent.get(go)
            while cur and depth < 8 and not sid:
                sid = md.get(cur, "")
                cur, depth = parent.get(cur), depth + 1
            if sid: via_parent += 1
        if sid: per_obj_id[go] = sid
        else: unbound.append(names.get(go, go))
    ids = Counter(per_obj_id.values())
    md_ids = Counter(v for v in md.values() if v)
    n = len(renderers)
    one_to_one = sum(1 for g, s in per_obj_id.items() if ids[s] == 1)
    return dict(renderable_objects=n, metadata_components=len(md), bound_self=self_b,
                bound_via_parent=via_parent, unbound=len(unbound), bound_ids=len(ids),
                multi_mesh_ids=sum(1 for v in ids.values() if v > 1),
                duplicate_metadata_ids=sum(1 for v in md_ids.values() if v > 1),
                coverage_pct=round(100 * (self_b + via_parent) / n, 2) if n else 0.0,
                one_to_one_pct=round(100 * one_to_one / n, 2) if n else 0.0), unbound


def main():
    os.makedirs(OUT, exist_ok=True)
    rows, summary = [], []
    for dataset, folder, pattern in SETS:
        base = os.path.join(ASSETS, folder)
        files = sorted(f for f in os.listdir(base) if re.match(pattern, f))
        tot = Counter()
        unbound_all = []
        for f in files:
            rec, unbound = analyse(os.path.join(base, f))
            rows.append(dict(dataset=dataset, module=f[:-7], **rec))
            unbound_all += [f"{f[:-7]}\t{u}" for u in unbound]
            for k in ("renderable_objects", "metadata_components", "bound_self", "bound_via_parent",
                      "unbound", "bound_ids", "multi_mesh_ids", "duplicate_metadata_ids"):
                tot[k] += rec[k]
        n = tot["renderable_objects"]
        summary.append(dict(dataset=dataset, modules=len(files), **tot,
                            coverage_pct=round(100 * (tot["bound_self"] + tot["bound_via_parent"]) / n, 2)))
        safe = dataset.split(" ")[0]
        with open(os.path.join(OUT, f"module_unbound_objects_{safe}.txt"), "w", encoding="utf-8") as fh:
            fh.write("\n".join(unbound_all))
    for name, data in (("module_binding_by_module.csv", rows), ("module_binding_summary.csv", summary)):
        with open(os.path.join(OUT, name), "w", newline="", encoding="utf-8") as fh:
            w = csv.DictWriter(fh, fieldnames=list(data[0])); w.writeheader(); w.writerows(data)
    for s in summary: print(s)
    return rows, summary


if __name__ == "__main__":
    main()
