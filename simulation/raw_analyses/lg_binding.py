"""LG MONO case data and binding coverage (REAL DATA; restricted, needs the proprietary export).

Counts the elements, fields, MEP share and storeys of the LG MONO metadata export, and the
binding of the OBJ objects to IFC GlobalIds (one Unity object per GlobalId). Reported in
Section 3.1 / 3.3.1 and Table 4. Writes restricted/lg_case.json (counts only, no identifiers);
prepare_public_inputs.py copies it to calibration/derived_inputs/.
"""
import csv, io, json, os, sys
from collections import Counter

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
import paths

ASSETS = paths.assets()
META = "My 3D Models/LG/MONO_metadata.csv"
OBJ = "My 3D Models/LG/MONO.obj"


def main():
    csv.field_size_limit(10 ** 9)
    rows = list(csv.reader(io.StringIO(open(os.path.join(ASSETS, META), encoding="utf-8-sig").read())))
    h = rows[0]; gi, ci = h.index("GlobalId"), h.index("IfcClass")
    spatial = {"IfcProject", "IfcSite", "IfcBuilding", "IfcBuildingStorey"}
    el = [r for r in rows[1:] if r[ci] not in spatial]
    objs, cur, groups = {}, None, 0
    for line in open(os.path.join(ASSETS, OBJ), "rb"):
        if line.startswith(b"o "): cur = line[2:].decode().strip(); objs.setdefault(cur, 0)
        elif line.startswith(b"g "): groups += 1
        elif line.startswith(b"f ") and cur: objs[cur] += 1
    ids = {r[gi] for r in el}
    withf = {k for k, v in objs.items() if v}
    populated = sum(1 for i in range(len(h)) if any(len(r) > i and r[i].strip() for r in el))
    mep = sum(1 for r in el if r[ci] in ("IfcFlowSegment", "IfcFlowTerminal", "IfcDistributionControlElement"))
    storeys = sorted({r[h.index("Storey")] for r in el if r[h.index("Storey")]})
    lg = dict(elements=len(el), csv_rows=len(rows) - 1, fields=len(h), fields_populated=populated, obj_objects=len(objs),
              obj_groups=groups, renderable_objects=len(withf), resolved=len(withf & ids), unresolved=len(withf - ids),
              duplicates=len(el) - len(ids), mep=mep, mep_pct=round(100 * mep / len(el), 1), storey_levels=len(storeys),
              classes=dict(Counter(r[ci] for r in el).most_common()))
    os.makedirs(paths.RESTRICTED, exist_ok=True)
    json.dump(lg, open(os.path.join(paths.RESTRICTED, "lg_case.json"), "w", encoding="utf-8"), indent=2)
    print(json.dumps(lg, indent=2))


if __name__ == "__main__":
    main()
