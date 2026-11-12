"""Write the derived, non-identifying inputs in calibration/derived_inputs/ (authors only).

Run after geometry_density.py, a5_binding_coverage.py, a5b_module_binding.py and lg_binding.py.
Needs BIMMR_ASSETS (the proprietary metadata exports) and, for participants.csv, the study
workbook given by BIMMR_SURVEY_WORKBOOK. Neither is public.

What is removed or transformed before anything leaves the restricted folder
  * element ids          -> pseudonymous ids (M3-00001, BG-00001, LG-00001) in file order
  * element type names   -> dropped (only the category, e.g. 'Walls' or 'IfcBeam', is kept)
  * bounding boxes       -> translated to a local origin per dataset (whole metres), so the
                            model's project coordinates are not published; sizes, distances and
                            neighbour counts are unchanged
  * metadata export      -> three presence flags per element (category, type, dimensions) and
                            the mean number of populated properties per element
  * binding reports      -> counts only (no file names, no unbound object names)
  * participants         -> anonymous codes P01-P15 with the per-participant session values,
                            primary language (en/ko) and the mean of the six NASA-TLX ratings
"""
import csv, io, json, math, os, sys
import numpy as np
import pandas as pd

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
import paths

CODES = {"M3": "M3", "Best Graphene": "BG", "LG MONO": "LG"}
META_SPEC = {   # dataset: (metadata export, id column, category column, type column)
    "M3": ("My 3D Models/M3 Building/M3 Building.csv", "Element_Id", "Category", "Family and Type"),
    "Best Graphene": ("My 3D Models/Yoju/20250709_Best Graphene/archicad_elements_20250709_101457.csv",
                      "Element ID", "Element_Type", "Element Type"),
    "LG MONO": ("My 3D Models/LG/MONO_metadata.csv", "GlobalId", "IfcClass", "TypeName"),
}
BAD = {"", "<undefined>", "undefined", "none", "null"}


def dim_cols(header):
    keys = ("length", "width", "height", "area", "volume", "thickness", "diameter", "size", "depth", "lenght", "deepth")
    bad = ("elevation", "offset", "angle", "slope", "level", "lock", "rotation")
    return [i for i, h in enumerate(header) if any(k in h.lower() for k in keys) and not any(b in h.lower() for b in bad)]


def _num(v):
    try: float(str(v).replace(",", "").split()[0]); return True
    except Exception: return False


def metadata_presence(assets):
    """Same rule as the original simulation: presence of category, type and a numeric dimension."""
    csv.field_size_limit(10 ** 9)
    out, nprops = {}, {}
    for ds, (p, idc, catc, typc) in META_SPEC.items():
        rows = list(csv.reader(io.StringIO(open(os.path.join(assets, p), encoding="utf-8-sig", errors="replace").read())))
        h = rows[0]; low = [x.strip().lower() for x in h]
        ii, ci, ti = low.index(idc.lower()), low.index(catc.lower()), low.index(typc.lower())
        dc = dim_cols(h); cnt = []
        for r in rows[1:]:
            if len(r) <= ii or not r[ii].strip(): continue
            g = lambda k: r[k].strip() if len(r) > k else ""
            out[(ds, r[ii].strip())] = (int(g(ci).lower() not in BAD), int(g(ti).lower() not in BAD), int(any(_num(g(k)) for k in dc)))
            cnt.append(sum(1 for k in range(len(r)) if r[k].strip() and r[k].strip().lower() not in BAD))
        nprops[ds] = float(np.mean(cnt))
    return out, nprops


def elements(assets):
    d = pd.read_csv(os.path.join(paths.RESTRICTED, "R3_geometry_density", "element_density.csv"), dtype={"element_id": str})
    meta, nprops = metadata_presence(assets)
    pseudo = {}
    for ds, g in d.groupby("dataset", sort=False):
        for i, eid in enumerate(dict.fromkeys(g.element_id), 1):
            pseudo[(ds, eid)] = f"{CODES[ds]}-{i:05d}"
    flags = [meta.get((ds, e)) for ds, e in zip(d.dataset, d.element_id)]
    out = pd.DataFrame(dict(dataset=d.dataset, module=d.module,
                            element_id=[pseudo[(ds, e)] for ds, e in zip(d.dataset, d.element_id)],
                            category=d.category))
    for ax in "xyz":     # local origin per dataset, whole metres (keeps the 3-decimal values exact)
        off = d.groupby("dataset")[[f"{ax}min"]].transform("min")[f"{ax}min"].apply(math.floor)
        out[f"{ax}min"] = (d[f"{ax}min"] - off).round(3); out[f"{ax}max"] = (d[f"{ax}max"] - off).round(3)
    out = out[["dataset", "module", "element_id", "category", "xmin", "ymin", "zmin", "xmax", "ymax", "zmax"]]
    out["diag_m"] = d.diag_m; out["neighbours_0p5m"] = d.neighbours_0p5m; out["density_band"] = d.density_band
    for k, name in enumerate(("meta_category", "meta_element_type", "meta_geometric_descriptors")):
        out[name] = pd.array([f[k] if f else pd.NA for f in flags], dtype="Int64")
    out.to_csv(os.path.join(paths.DERIVED, "elements.csv"), index=False)
    return nprops


def binding(lg):
    A = os.path.join(paths.RESTRICTED, "A5_binding_coverage")
    pd.read_csv(os.path.join(A, "module_binding_by_module.csv")).to_csv(os.path.join(paths.DERIVED, "module_binding_by_module.csv"), index=False)
    pd.read_csv(os.path.join(A, "module_binding_summary.csv")).to_csv(os.path.join(paths.DERIVED, "module_binding_summary.csv"), index=False)
    ex = pd.read_csv(os.path.join(A, "binding_report.csv"))
    ex.drop(columns=["geometry_file", "csv_file"]).to_csv(os.path.join(paths.DERIVED, "export_binding_summary.csv"), index=False)
    pd.read_csv(os.path.join(paths.RESTRICTED, "R3_geometry_density", "density_summary.csv")).to_csv(
        os.path.join(paths.DERIVED, "density_summary.csv"), index=False)
    json.dump(lg, open(os.path.join(paths.DERIVED, "lg_case.json"), "w", encoding="utf-8"), indent=2)


def participants():
    wb = os.environ.get("BIMMR_SURVEY_WORKBOOK")
    if not wb:
        print("BIMMR_SURVEY_WORKBOOK not set: participants.csv left unchanged"); return
    t5 = pd.read_excel(wb, "Table5_Performance", header=None).iloc[4:19, :10]
    t5.columns = ["pid", "B10", "load_s", "inspect_s", "session_s", "top1_pct", "disamb_pct", "report_s", "rereg", "evid_pct"]
    t5 = t5.reset_index(drop=True)
    t5["pid"] = [f"P{i:02d}" for i in range(1, len(t5) + 1)]      # anonymous codes P01-P15, in workbook order
    sv = pd.read_excel(wb, "Survey_Data")
    tlx = ["D_Mental_Demand", "D_Physical_Demand", "D_Time_Pressure", "D_Performance", "D_Effort", "D_Frustration"]
    t5["lang"] = ["ko" if str(x).startswith("Kor") else "en" for x in sv.A5_Primary_Language]
    t5["tlx_mean"] = sv[tlx].mean(axis=1).values
    t5.to_csv(os.path.join(paths.DERIVED, "participants.csv"), index=False)


def main():
    os.makedirs(paths.DERIVED, exist_ok=True)
    assets = paths.assets()
    nprops = elements(assets)
    lg = json.load(open(os.path.join(paths.RESTRICTED, "lg_case.json"), encoding="utf-8"))
    binding(lg)
    json.dump(dict(mean_populated_properties_per_element=nprops, lg_mono_renderable_objects=lg["renderable_objects"]),
              open(os.path.join(paths.DERIVED, "dataset_constants.json"), "w", encoding="utf-8"), indent=2)
    participants()
    print("written", paths.DERIVED)


if __name__ == "__main__":
    main()
