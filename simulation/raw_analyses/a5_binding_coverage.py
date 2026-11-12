"""A5 - Binding coverage of the two case-study datasets (REAL DATA).

Reproduces offline the binding rule of BIMUniXchange/Editor/UnifiedMetadataAssigner.cs:
  * CSV: the element-id column is the first header matching (in header order) one of
    'ID and Categories.Element ID', 'General Parameters.Element ID',
    'General Parameters.Unique ID', 'Element ID', 'Element_Id', 'ElementID', 'ID'
    (case-insensitive); rows with an empty id are skipped.
  * Revit OBJ: one scene object per OBJ group ('g'); id token = text after the last '_'.
  * ArchiCAD FBX: one scene object per Model node of type Mesh; id token = trimmed name.
Coverage = scene objects whose token resolves to exactly one CSV row / scene objects.

Inputs are read from the project, never modified. Outputs: binding_report.csv (same columns
as the U9 report written by the Unity importer) and binding_unresolved_<dataset>.txt.
"""
import csv, io, os, sys
from collections import Counter

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from fbx_reader import read_models

sys.path.insert(0, os.path.dirname(HERE))
import paths
ASSETS = paths.assets()                     # proprietary exports (BIMMR_ASSETS)
OUT = os.path.join(paths.RESTRICTED, "A5_binding_coverage")   # untracked: element-level results with real ids

DATASETS = [
    dict(dataset="M3 (Revit, OBJ)", fmt="OBJ", rule="revit",
         geometry="My 3D Models/M3 Building/ExportedModel.obj",
         csv="My 3D Models/M3 Building/M3 Building.csv"),
    dict(dataset="Best Graphene (ArchiCAD, FBX)", fmt="FBX", rule="archicad",
         geometry="My 3D Models/Yoju/20250709_Best Graphene/여주 베스트그래핀_작업중.fbx",
         csv="My 3D Models/Yoju/20250709_Best Graphene/archicad_elements_20250709_101457.csv"),
]
ID_COLUMNS = ["ID and Categories.Element ID", "General Parameters.Element ID",
              "General Parameters.Unique ID", "Element ID", "Element_Id", "ElementID", "ID"]


def csv_id_counts(path):
    csv.field_size_limit(10 ** 9)
    text = open(path, encoding="utf-8-sig", errors="replace").read()
    rows = list(csv.reader(io.StringIO(text)))
    header = rows[0]
    idx = next(i for i, h in enumerate(header)
               if any(h.strip().lower() == c.lower() for c in ID_COLUMNS))
    counts = Counter(r[idx].strip() for r in rows[1:] if len(r) > idx and r[idx].strip())
    return header[idx], len(header), counts


def obj_groups(path):
    """Return (groups with at least one face, number of groups without faces).
    Unity creates no GameObject for a group without faces (verified against
    ExportedModel.prefab: 3,574 bound objects = 3,574 groups with faces)."""
    names, empty, current, faces = [], 0, None, 0
    with open(path, "rb") as f:
        for line in f:
            if line.startswith(b"g "):
                if current is not None:
                    if faces: names.append(current)
                    else: empty += 1
                current, faces = line[2:].decode("utf-8", "replace").strip(), 0
            elif line.startswith(b"f "):
                faces += 1
    if current is not None:
        if faces: names.append(current)
        else: empty += 1
    return names, empty


def fbx_meshes(path):
    _, models, _ = read_models(path)
    return [n for n, t in models.values() if t == "Mesh"]


def revit_token(name):
    name = name.strip()
    i = name.rfind("_")
    return name[i + 1:] if 0 <= i < len(name) - 1 else ""


def analyse(ds):
    id_col, n_cols, rows = csv_id_counts(os.path.join(ASSETS, ds["csv"]))
    geo = os.path.join(ASSETS, ds["geometry"])
    if ds["fmt"] == "OBJ":
        objects, empty_groups = obj_groups(geo)
    else:
        objects, empty_groups = fbx_meshes(geo), 0
    token = revit_token if ds["rule"] == "revit" else (lambda s: s.strip())

    with_token = resolved = unresolved = duplicated = 0
    per_id = Counter()
    unresolved_names = []
    for name in objects:
        t = token(name)
        if not t:
            unresolved_names.append(f"{name}\t(no id token)")
            continue
        with_token += 1
        per_id[t] += 1
        if t not in rows:
            unresolved += 1
            unresolved_names.append(f"{name}\t(no CSV row)")
        elif rows[t] > 1:
            duplicated += 1
        else:
            resolved += 1
    bound_ids = set(per_id)
    csv_ids_without_geometry = sum(1 for i in rows if i not in bound_ids)
    rec = dict(
        dataset=ds["dataset"], geometry_format=ds["fmt"],
        geometry_file=ds["geometry"], csv_file=ds["csv"], csv_id_column=id_col,
        csv_property_fields=n_cols, csv_rows=sum(rows.values()),
        csv_duplicate_ids=sum(1 for v in rows.values() if v > 1),
        geometry_groups_without_faces=empty_groups,
        scene_objects=len(objects), objects_with_id_token=with_token,
        ids_resolved_to_one_record=resolved, ids_unresolved=unresolved,
        objects_without_token=len(objects) - with_token, ids_duplicated=duplicated,
        multi_mesh_elements=sum(1 for v in per_id.values() if v > 1),
        objects_in_multi_mesh_elements=sum(v for v in per_id.values() if v > 1),
        distinct_elements_bound=len(bound_ids & set(rows)),
        csv_records_without_geometry=csv_ids_without_geometry,
        coverage_pct=round(100.0 * resolved / len(objects), 2) if objects else 0.0,
        record_coverage_pct=round(100.0 * len(bound_ids & set(rows)) / len(rows), 2) if rows else 0.0,
    )
    return rec, unresolved_names


def main():
    os.makedirs(OUT, exist_ok=True)
    recs = []
    for ds in DATASETS:
        rec, unresolved = analyse(ds)
        recs.append(rec)
        safe = rec["dataset"].split(" ")[0].replace("/", "_")
        with open(os.path.join(OUT, f"binding_unresolved_{safe}.txt"), "w", encoding="utf-8") as f:
            f.write("\n".join(unresolved))
        print({k: rec[k] for k in ("dataset", "scene_objects", "ids_resolved_to_one_record",
                                    "ids_unresolved", "ids_duplicated", "multi_mesh_elements", "coverage_pct")})
    with open(os.path.join(OUT, "binding_report.csv"), "w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=list(recs[0]))
        w.writeheader(); w.writerows(recs)
    return recs


if __name__ == "__main__":
    main()
