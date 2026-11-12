"""R3 - Three-dataset simulation for revision 3 (M3, Best Graphene, LG MONO).

STATUS OF EVERY OUTPUT
  DERIVED  Part A rebuilds Table 10 / Table A6 from the per-participant values in the study
           workbook (MR_BIM_Survey_Data.xlsx, Table5_Performance). Accuracy and completeness
           rates are converted to whole selections (k of 10) and whole fields (k of 110) by the
           largest-remainder method, which keeps the cohort totals of the workbook.
  SIMULATED  Everything event-level (which targets failed, which fields were empty, the
           follow-up session for Table 11, the three-dataset block, LLM/voice/L1/registration
           logs). It is anchored to real inputs but the sessions did not happen.

REAL INPUTS (no value below is invented where a real one exists). In this public repository they
are read from calibration/derived_inputs/, written by raw_analyses/prepare_public_inputs.py:
  * participants.csv: per-participant session values (Appendix Table A6), primary language and
    mean NASA-TLX rating (anonymous codes P01-P15)
  * elements.csv: one row per bound element of the three models with a pseudonymous id, category,
    module-local bounding box, within-module density and the presence of category, type and
    dimension fields in the metadata export (raw ids and type names removed)
  * module_binding_by_module.csv: binding coverage per module (A5b) and of the LG export
  * dataset_constants.json: mean populated properties per element of each export
  * Table 12 (voice playback), Table 13 (LLM latency/tokens), Table 9 (workload)

ASSUMPTIONS (stated, swept in replicates): selection difficulty grows with log(1 + neighbours
within 0.5 m) and shrinks with element size; popup rescue probability; noticing rate of
misbinding; live-speech penalty; LLM flag rates; L1 frame loss; registration residual medians.
Calibration: the Best Graphene cohort reproduces the derived 84.0% / 94.7% exactly.

    python r3_three_building_sim.py [n_replicates]
"""
import csv, io, json, os, sys
import numpy as np
import pandas as pd
from scipy import special, stats, optimize
import paths

OUT = paths.OUT_FOLLOWUP
DENS = os.path.join(paths.DERIVED, "elements.csv")
MODBIND = os.path.join(paths.DERIVED, "module_binding_by_module.csv")
PARTICIPANTS = os.path.join(paths.DERIVED, "participants.csv")
CONSTANTS = os.path.join(paths.DERIVED, "dataset_constants.json")
_CFG = json.load(open(os.path.join(paths.CAL, "followup_simulation_parameters.json"), encoding="utf-8"))
SEED0 = _CFG["seed_base"]

DATASETS = ["M3", "Best Graphene", "LG MONO"]
TASK_MODULE = {"M3": "M3-1", "Best Graphene": "M1-1", "LG MONO": "MONO"}
T11_MODULE = ("Best Graphene", "M2-1")
FIELDS = ["element_id", "timestamp", "module_id", "category", "status", "screenshot_path", "notes",
          "element_name", "element_type", "geometric_descriptors", "report_status"]
CODE_FIELDS = {"element_id", "timestamp", "module_id", "status", "element_name", "report_status"}

P = dict(_CFG["params"])
for _k in ("voice_playback", "live_penalty", "voice_latency"):     # JSON keys are strings; noise bands are ints
    P[_k] = {int(b): v for b, v in P[_k].items()}


# ------------------------------------------------------------------ real inputs -----
def lrr(rates, n):
    """Largest-remainder rounding of rates (%) to integer counts out of n; keeps the total."""
    x = np.asarray(rates, float) / 100 * n
    tot = int(round(x.sum())); fl = np.floor(x).astype(int); rem = x - fl
    add = np.zeros(len(x), int); add[np.argsort(-rem, kind="stable")[: tot - fl.sum()]] = 1
    return fl + add


def load_participants():
    """Per-participant inputs (pseudonymous). Columns as in the study workbook sheet
    Table5_Performance, plus primary language (en/ko) and the mean of the six NASA-TLX ratings."""
    raw = pd.read_csv(PARTICIPANTS, dtype={"pid": str})
    t5 = raw[["pid", "B10", "load_s", "inspect_s", "session_s", "top1_pct", "disamb_pct", "report_s", "rereg", "evid_pct"]].copy()
    for c in t5.columns[1:]: t5[c] = t5[c].astype(float)
    t5["top1_k"] = lrr(t5.top1_pct, 10); t5["disamb_k"] = lrr(t5.disamb_pct, 10)
    t5["fields_k"] = lrr(t5.evid_pct, 110); t5["rereg"] = t5.rereg.astype(int)
    prof = pd.DataFrame(dict(pid=t5.pid, lang=raw.lang.values, tlx=raw.tlx_mean.astype(float).values))
    # participant skill on the logit scale, from their own (non-integer) workbook top-1 rate
    prof["skill"] = special.logit(t5.top1_pct / 100) - special.logit(t5.top1_pct.mean() / 100)
    return t5, prof


def metadata_presence():
    """Real per-element presence of category, type and dimensional descriptors in the metadata
    export (precomputed by raw_analyses/prepare_public_inputs.py; blank = element not in the export)."""
    d = pd.read_csv(DENS, dtype={"element_id": str})
    out = {}
    for r in d.itertuples(index=False):
        if pd.isna(r.meta_category): continue
        out[(r.dataset, r.element_id)] = dict(category=int(r.meta_category), element_type=int(r.meta_element_type),
                                              geometric_descriptors=int(r.meta_geometric_descriptors))
    nprops = json.load(open(CONSTANTS, encoding="utf-8"))["mean_populated_properties_per_element"]
    return out, nprops


TYPE_CLASS = {
    "M3": {"Structural Columns": "column", "Structural Framing": "beam", "Walls": "wall", "Curtain Wall Mullions": "connection",
           "Curtain Panels": "panel", "Floors": "slab", "Doors": "opening", "Windows": "opening",
           "Mechanical Equipment": "mep", "Plumbing Fixtures": "mep", "Generic Models": "connection"},
    "Best Graphene": {"Column": "column", "Beam": "beam", "Wall": "wall", "Slab": "slab", "Door": "opening",
                      "Window": "opening", "Object": "mep", "Morph": "connection"},
    "LG MONO": {"IfcColumn": "column", "IfcBeam": "beam", "IfcWallStandardCase": "wall", "IfcWall": "wall", "IfcSlab": "slab",
                "IfcRoof": "slab", "IfcDoor": "opening", "IfcWindow": "opening", "IfcFlowSegment": "mep",
                "IfcFlowTerminal": "mep", "IfcDistributionControlElement": "mep", "IfcBuildingElementProxy": "connection",
                "IfcCovering": "panel"},
}
SHEET = ["column", "column", "beam", "beam", "wall", "wall", "opening", "slab", "mep", "connection"]
FALLBACK = ["column", "wall", "beam", "slab", "panel", "connection"]
BEHAV = ("notes", "screenshot_path", "geometric_descriptors", "element_type", "category")


def load_geometry():
    d = pd.read_csv(DENS, dtype={"element_id": str})
    d["tclass"] = [TYPE_CLASS[ds].get(c, "other") for ds, c in zip(d.dataset, d.category)]
    mb = pd.read_csv(MODBIND)
    unb = {}
    for _, r in mb.iterrows():
        ds = "M3" if r.dataset.startswith("M3") else "Best Graphene"
        unb[(ds, r.module)] = r.unbound / r.renderable_objects
    unb[("LG MONO", "MONO")] = 0.0
    rend = {("M3" if r.dataset.startswith("M3") else "Best Graphene", r.module): r.renderable_objects for _, r in mb.iterrows()}
    rend[("LG MONO", "MONO")] = int(json.load(open(CONSTANTS, encoding="utf-8"))["lg_mono_renderable_objects"])
    ext = {}
    for (ds, m), g in d.groupby(["dataset", "module"]):
        e = g[["xmax", "ymax", "zmax"]].max().values - g[["xmin", "ymin", "zmin"]].min().values
        plan = np.sort(e)[-2:]                     # two largest extents = plan dimensions
        ext[(ds, m)] = float(np.hypot(*plan))
    return d, unb, rend, ext


# ------------------------------------------------------------------ behaviour model -----
def p_top1(alpha, skill, neigh, diag):
    return special.expit(alpha + skill - P["beta_density"] * (np.log1p(neigh) - np.log1p(P["ref_neigh"]))
                         + P["gamma_size"] * (np.log(np.maximum(diag, 0.05)) - np.log(P["ref_diag"])))


def n_cand(nr, neigh):
    return int(min(1 + nr.poisson(P["cand_rate"] * neigh), 8))


def rescue_p(rho0, nc):
    return float(np.clip(rho0 + P["rescue_slope"] * (min(nc, 5) - 3), 0.05, 0.98))


def pick_targets(rng, pool, sheet=SHEET, bands=None):
    """Task sheet: one target per sheet slot, by type class, from the real module elements."""
    used, out = set(), []
    for k, cls in enumerate(sheet):
        cand = pool[(pool.tclass == cls) & ~pool.element_id.isin(used)]
        if bands is not None:
            cb = cand[cand.density_band == bands[k]]
            cand = cb if len(cb) else cand
        if not len(cand):
            for fb in FALLBACK:
                cand = pool[(pool.tclass == fb) & ~pool.element_id.isin(used)]
                if bands is not None and len(cand[cand.density_band == bands[k]]): cand = cand[cand.density_band == bands[k]]
                if len(cand): break
        r = cand.iloc[int(rng.integers(len(cand)))]
        used.add(r.element_id); out.append(r)
    return pd.DataFrame(out).reset_index(drop=True)


def calibrate(geo, rng):
    """alpha so that the expected top-1 over Best Graphene M1-1 task sheets equals 0.840, and
    rho0 so that the expected post-disambiguation accuracy equals 0.9467 (derived cohort)."""
    pool = geo[(geo.dataset == "Best Graphene") & (geo.module == "M1-1")]
    sheets = [pick_targets(rng, pool) for _ in range(300)]
    nb = np.concatenate([s.neighbours_0p5m.values for s in sheets]); dg = np.concatenate([s.diag_m.values for s in sheets])
    alpha = optimize.brentq(lambda a: p_top1(a, 0, nb, dg).mean() - 0.840, -5, 8)
    nr = np.random.default_rng(1)
    nc = np.array([n_cand(nr, x) for x in nb]); popup = nc > 1
    p1 = p_top1(alpha, 0, nb, dg)
    f = lambda r: (p1 + (1 - p1) * popup * np.array([rescue_p(r, c) for c in nc])).mean() - 0.94667
    rho0 = optimize.brentq(f, 0.05, 0.999)
    return float(alpha), float(rho0)


# ------------------------------------------------------------------ Part A (cohort) -----
def part_a(t5, prof, geo, meta, alpha, rho0, seed):
    """Event-level allocation for the original cohort, conditioned on the derived counts."""
    rng = np.random.default_rng(seed)
    pool = geo[(geo.dataset == "Best Graphene") & (geo.module == "M1-1")]
    tg = pick_targets(rng, pool, bands=["low"] * 3 + ["medium"] * 4 + ["high"] * 3)
    p1 = p_top1(alpha, 0, tg.neighbours_0p5m.values, tg.diag_m.values)
    rows, recs = [], []
    for i, r in t5.iterrows():
        k1, k2 = int(r.top1_k), int(r.disamb_k)
        w = (1 - p1); w = w / w.sum()
        fail = rng.choice(10, size=10 - k1, replace=False, p=w)                 # top-1 failures, density-weighted
        rescued = set(rng.choice(fail, size=k2 - k1, replace=False)) if k2 > k1 else set()
        for j in range(10):
            t = tg.iloc[j]
            ok1 = j not in fail; ok = ok1 or j in rescued
            decoy = pool[(pool.element_id != t.element_id)]
            # misbound record: a real neighbouring element of the same module (nearest box centre)
            c = np.array([(t.xmin + t.xmax) / 2, (t.ymin + t.ymax) / 2, (t.zmin + t.zmax) / 2])
            dc = np.c_[(decoy.xmin + decoy.xmax) / 2, (decoy.ymin + decoy.ymax) / 2, (decoy.zmin + decoy.zmax) / 2]
            near = decoy.iloc[np.argsort(((dc - c) ** 2).sum(1))[: 5]]
            conf = t if ok else near.iloc[int(rng.integers(len(near)))]
            rows.append(dict(pid=r.pid, step=j + 1, target_id=t.element_id, target_type=t.tclass, density_band=t.density_band,
                             neighbours=int(t.neighbours_0p5m), top1_correct=int(ok1), correct=int(ok),
                             confirmed_id=conf.element_id))
            m = meta.get(("Best Graphene", conf.element_id), dict(category=1, element_type=1, geometric_descriptors=1))
            recs.append(dict(pid=r.pid, step=j + 1, correct=int(ok), **{f: (1 if f in CODE_FIELDS else m.get(f, 1)) for f in FIELDS},
                             **{"meta_" + k: m.get(k, 1) for k in ("category", "element_type", "geometric_descriptors")}))
        # remaining missing fields of this participant: notes and screenshots (behavioural)
        rr = [x for x in recs if x["pid"] == r.pid]
        present = sum(sum(x[f] for f in FIELDS) for x in rr)
        need = present - int(r.fields_k)
        slots = [(x, f) for x in rr for f in ("notes", "screenshot_path", "geometric_descriptors", "element_type", "category") if x[f] == 1]
        wts = np.array([{"notes": 10, "screenshot_path": 1.2, "geometric_descriptors": 2.5, "element_type": 1.0, "category": 0.6}[f] for _, f in slots], float)
        if need > 0:
            for k in rng.choice(len(slots), size=min(need, len(slots)), replace=False, p=wts / wts.sum()):
                x, f = slots[k]; x[f] = 0
        elif need < 0:                       # metadata gaps exceed the participant's missing count: restore notes first
            zero = [(x, f) for x in rr for f in ("geometric_descriptors", "element_type", "category") if x[f] == 0]
            for x, f in zero[: -need]: x[f] = 1
    A = pd.DataFrame(rows); R = pd.DataFrame(recs)
    R["n_present"] = R[FIELDS].sum(axis=1)
    return tg, A, R


# ------------------------------------------------------------------ Part B (follow-up) -----
def ln(nr, med, sig): return float(nr.lognormal(np.log(med), sig))


def nearest(pool, t, k=5):
    d = pool[pool.element_id != t.element_id]
    c = np.array([(t.xmin + t.xmax) / 2, (t.ymin + t.ymax) / 2, (t.zmin + t.zmax) / 2])
    dc = np.c_[(d.xmin + d.xmax) / 2, (d.ymin + d.ymax) / 2, (d.zmin + d.zmax) / 2]
    return list(d.element_id.values[np.argsort(((dc - c) ** 2).sum(1))[:k]])


def run_block(nr, rng, alpha, rho0, skill, targets, disamb, dataset, module, unbound_share, pool):
    out = []
    for j, t in targets.iterrows():
        attempt, tsum = 0, 0.0
        while True:
            attempt += 1
            pa = p_top1(alpha, skill, t.neighbours_0p5m, t.diag_m)
            ok1 = nr.random() < pa
            nc = n_cand(nr, t.neighbours_0p5m)
            hover_to_press = ln(nr, *P["aim"])
            if nr.random() < unbound_share:                         # ray first lands on an unbound object: rejected, re-aim
                hover_to_press += ln(nr, 2.0, 0.3)
            popup = disamb and nc > 1
            lat = hover_to_press + (ln(nr, P["popup"][0] + P["popup"][1] * min(nc, 5), P["popup"][2]) if popup else 0.0)
            if ok1: ok, rank = True, 1
            elif popup and nr.random() < rescue_p(rho0, nc): ok, rank = True, int(nr.integers(2, min(nc, 5) + 1))
            else: ok, rank = False, 1
            tsum += lat
            conf = t.element_id if ok else nearest(pool, t)[int(nr.integers(5))]
            out.append(dict(dataset=dataset, module=module, step=j + 1, attempt=attempt, target_id=t.element_id, confirmed_id=conf,
                            target_type=t.tclass, density_band=t.density_band, neighbours=int(t.neighbours_0p5m),
                            top1_correct=int(ok1), correct=int(ok), popup=int(popup), candidates=min(nc, 5) if popup else 0,
                            chosen_rank=rank, latency_s=lat, condition="disamb_on" if disamb else "disamb_off"))
            notice = P["p_notice_with"] if disamb else P["p_notice_without"]
            if not ok and attempt == 1 and nr.random() < notice:
                tsum += ln(nr, *P["undo"]); continue
            break
        out[-1]["target_time_s"] = tsum
    return out


def records_for(nr, sel, meta, beh, pid):
    """One exported record per step (the last attempt). Metadata-derived fields follow the real CSV
    of the confirmed element; behavioural gaps follow the participant's own cohort rates."""
    final = {}
    for s in sel: final[(s["dataset"], s["step"])] = s
    recs = []
    for (dset, step), s in final.items():
        eid = s["target_id"] if s["correct"] else s.get("confirmed_id", s["target_id"])
        m = meta.get((dset, eid), dict(category=1, element_type=1, geometric_descriptors=1))
        rec = {f: 1 for f in FIELDS}
        for f in BEHAV:
            if f in m and not m[f]:
                rec[f] = 0
            elif f == "screenshot_path":                 # revised build renders the screenshot off-screen (U1)
                rec[f] = int(nr.random() > P["p_screenshot_fail"])
            else:
                rec[f] = int(nr.random() > beh[(pid, f)])
        recs.append(dict(dataset=dset, step=step, correct=s["correct"], target_type=s["target_type"], **rec))
    return recs


def part_b(seed, t5, prof, geo, meta, nprops, unb, rend, ext, alpha, rho0, A_rec):
    nr = np.random.default_rng(seed); rng = np.random.default_rng(seed + 7)
    # participant notes propensity from the cohort allocation (derived)
    beh = {}
    for f in BEHAV:
        mk = "meta_" + f
        sub = A_rec[A_rec[mk] == 1] if mk in A_rec else A_rec
        pool = 1 - sub[f].mean()
        g = sub.groupby("pid")[f].agg(["sum", "count"])
        for pid, row in g.iterrows():
            beh[(pid, f)] = ((row["count"] - row["sum"]) + 10 * pool) / (row["count"] + 10)   # shrunk to the cohort rate
    notes_p = beh
    sel_rows, rec_rows, sess_rows, llm_rows, l1_rows, reg_rows, voice_rows, pay_rows, wl_rows = ([] for _ in range(9))
    # task sheets: B1 two matched sets on Best Graphene M2-1; B2 one sheet per dataset
    pool11 = geo[(geo.dataset == T11_MODULE[0]) & (geo.module == T11_MODULE[1])]
    bands = ["low"] * 3 + ["medium"] * 4 + ["high"] * 3
    setA = pick_targets(rng, pool11, bands=bands)
    setB = pick_targets(rng, pool11[~pool11.element_id.isin(setA.element_id)], bands=bands)
    sheets = {ds: pick_targets(rng, geo[(geo.dataset == ds) & (geo.module == TASK_MODULE[ds])]) for ds in DATASETS}
    orders = [list(o) for o in (("M3", "Best Graphene", "LG MONO"), ("Best Graphene", "LG MONO", "M3"), ("LG MONO", "M3", "Best Graphene"),
                                ("M3", "LG MONO", "Best Graphene"), ("Best Graphene", "M3", "LG MONO"), ("LG MONO", "Best Graphene", "M3"))]
    ext_bg = ext[("Best Graphene", "M1-1")]
    for i, r in t5.iterrows():
        pid = r.pid; sk = prof.skill[i] + nr.normal(0, P["skill_sd_resid"])
        pf_load = r.load_s / t5.load_s.mean(); pf_insp = r.inspect_s / t5.inspect_s.mean(); pf_rep = r.report_s / t5.report_s.mean()
        rr = 0.5 * r.rereg + 0.5 * t5.rereg.mean()             # shrunk participant re-registration tendency
        # ---- B1: Table 11 (within-subject, counterbalanced) ----
        cond_order = ["disamb_on", "disamb_off"] if i % 2 == 0 else ["disamb_off", "disamb_on"]
        set_for = {"disamb_on": setA, "disamb_off": setB} if i % 4 in (0, 1) else {"disamb_on": setB, "disamb_off": setA}
        for oi, cond in enumerate(cond_order):
            s = run_block(nr, rng, alpha, rho0, sk + (0.10 if oi == 1 else 0), set_for[cond], cond == "disamb_on",
                          "Best Graphene", T11_MODULE[1], unb[T11_MODULE], pool11)
            for x in s: x.update(pid=pid, block="T11", order=oi + 1)
            sel_rows += s
            pass_s = sum(x.get("target_time_s", 0) for x in s) + sum(
                ln(nr, P["per_target_inspect"][0] * pf_insp, P["per_target_inspect"][1]) + P["walk_per_m"] * ext[T11_MODULE] / 3
                for _ in range(10))
            tlx = float(np.clip(prof.tlx[i] + (P["tlx_disamb_extra"] if cond == "disamb_on" else 0) + nr.normal(0, 0.18), 1, 5))
            wl_rows.append(dict(pid=pid, condition=cond, order=oi + 1, tlx=tlx, pass_s=pass_s,
                                popup_ease=int(np.clip(round(nr.normal(4.0, 0.6)), 1, 5)) if cond == "disamb_on" else np.nan))
        # ---- B2: three datasets, disambiguation on, Latin-square order ----
        for oi, ds in enumerate(orders[i % 6]):
            mod = TASK_MODULE[ds]
            s = run_block(nr, rng, alpha, rho0, sk + 0.05 * oi, sheets[ds], True, ds, mod, unb[(ds, mod)],
                          geo[(geo.dataset == ds) & (geo.module == mod)])
            for x in s: x.update(pid=pid, block="X3", order=oi + 1)
            sel_rows += s
            recs = records_for(nr, s, meta, notes_p, pid)
            for x in recs: x.update(pid=pid)
            rec_rows += recs
            scale = ext[(ds, mod)] / ext_bg
            load = (P["load_base"] + P["load_per_object"] * rend[(ds, mod)]) * pf_load * ln(nr, 1.0, 0.05)
            insp = sum(x.get("target_time_s", 0) for x in s) + sum(
                ln(nr, P["per_target_inspect"][0] * pf_insp, P["per_target_inspect"][1]) + P["walk_per_m"] * ext[(ds, mod)] / 3
                for _ in range(10))
            rep = (sum(max(1.0, nr.normal(*P["narrative"])) for _ in range(10)) + max(1.0, nr.normal(*P["export_overhead"]))) * pf_rep \
                  * (37.1 / 37.0)
            rereg = int(nr.poisson(rr * np.sqrt(scale) * 0.55))       # ~10-target block is shorter than a full session
            sess_rows.append(dict(pid=pid, dataset=ds, order=oi + 1, load_s=load, inspect_s=insp, report_s=rep, rereg=rereg,
                                  completeness=np.mean([sum(x[f] for f in FIELDS) / 11 for x in recs]) * 100))
            # LLM calls (questions + one narrative per record)
            tok_scale = min(1.25, max(0.8, (min(10, nprops[ds]) / 10) * (min(10, geo[(geo.dataset == ds) & (geo.module == mod)].category.nunique()) / 8)))
            mep_share = float(np.mean([x["target_type"] == "mep" for x in s]))
            for x in recs:
                for _ in range(int(nr.poisson(P["questions_per_event"]))):
                    pf = P["p_flag_q"] + P["p_flag_q_mep"] * (x["target_type"] == "mep")
                    llm_rows.append(dict(pid=pid, dataset=ds, call_type="question", ctx_has_id=int(nr.random() > P["p_ctx_missing"]),
                                         flag=int(nr.random() < pf), latency_s=ln(nr, *P["llm_latency"]) * (0.9 + 0.1 * tok_scale),
                                         prompt_tokens=int(nr.normal(380 * tok_scale, 50)), completion_tokens=max(5, int(nr.normal(46, 14)))))
                llm_rows.append(dict(pid=pid, dataset=ds, call_type="report_narrative", ctx_has_id=1, flag=int(nr.random() < P["p_flag_narr"]),
                                     latency_s=ln(nr, 3.0, 0.3), prompt_tokens=int(nr.normal(260 * tok_scale, 30)),
                                     completion_tokens=int(nr.normal(70, 15))))
            # L1 observation frames
            for x in [y for y in s if y["attempt"] == max(z["attempt"] for z in s if z["step"] == y["step"])]:
                miss = nr.random() < P["p_frame_missing"]
                truth = bool(x["correct"]) and nr.random() > P["p_escape"] * scale
                r1 = truth if nr.random() > P["rater_err"] else not truth
                r2 = truth if nr.random() > P["rater_err"] else not truth
                l1_rows.append(dict(pid=pid, dataset=ds, missing=int(miss), r1=int(r1), r2=int(r2), final=0 if miss else int(truth)))
            # export consistency
            for x in recs:
                pay_rows.append(dict(pid=pid, dataset=ds, kind="record", consistent=int(x["screenshot_path"] == 1)))
            # registration check points (6 before, 6 after re-registration)
            for after in (0, 1):
                med = (P["resid_after"] if after else P["resid_before"]) * np.sqrt(scale)
                for cp in range(6):
                    reg_rows.append(dict(pid=pid, dataset=ds, after=after, residual_mm=ln(nr, med, P["resid_sigma"])))
        # live voice (12-18 utterances) in the factory noise mix
        for u in range(int(nr.integers(12, 19))):
            band = int(nr.choice([60, 70, 80], p=[0.40, 0.35, 0.25]))
            pacc = P["voice_playback"][band] - P["live_penalty"][band] - (P["ko_penalty"] if prof.lang[i] == "ko" else 0)
            voice_rows.append(dict(pid=pid, band=band, dba=band + nr.normal(0, 2.5), lang=prof.lang[i], ok=int(nr.random() < pacc),
                                   latency_s=ln(nr, P["voice_latency"][band] * 1.03, 0.18)))
        # outbound payloads (restricted-data mode on for the second half of the cohort)
        redact = i >= 8
        for k in range(int(nr.integers(35, 60))):
            ep = rng.choice(["stt", "llm", "llm", "tts"])
            pay_rows.append(dict(pid=pid, dataset="", kind="payload", endpoint=ep, redaction=int(redact),
                                 names=int((ep != "stt") and not redact)))
    bench = []
    for q in range(1, 31):
        tier = "T1" if q <= 12 else "T2" if q <= 26 else "T3"
        for run in range(3):
            bench.append(dict(q=q, tier=tier, flag=int(nr.random() < P["bench_flag"][tier] and q != 16)))
    return dict(sel=pd.DataFrame(sel_rows), rec=pd.DataFrame(rec_rows), sess=pd.DataFrame(sess_rows), llm=pd.DataFrame(llm_rows),
                l1=pd.DataFrame(l1_rows), reg=pd.DataFrame(reg_rows), voice=pd.DataFrame(voice_rows), pay=pd.DataFrame(pay_rows),
                wl=pd.DataFrame(wl_rows), bench=pd.DataFrame(bench), sheets=sheets, setA=setA, setB=setB)


# ------------------------------------------------------------------ metrics -----
def first_attempts(sel): return sel[sel.attempt == 1]


def final_attempts(sel):
    return sel.sort_values("attempt").groupby(["pid", "block", "dataset", "condition", "step"]).tail(1)


def metrics(B):
    sel, sess, rec = B["sel"], B["sess"], B["rec"]
    m = {}
    x3 = sel[sel.block == "X3"]
    fa = first_attempts(x3)
    for ds in DATASETS:
        f = fa[fa.dataset == ds]
        m[f"{ds}|top1"] = 100 * f.top1_correct.mean()
        m[f"{ds}|after"] = 100 * f.correct.mean()
        m[f"{ds}|completeness"] = sess[sess.dataset == ds].completeness.mean()
        m[f"{ds}|inspect_s"] = sess[sess.dataset == ds].inspect_s.mean()
        m[f"{ds}|latency_s"] = x3[(x3.dataset == ds)].groupby(["pid", "step"]).latency_s.sum().median()
    t = sel[sel.block == "T11"]
    per = first_attempts(t).groupby(["pid", "condition"]).correct.mean().unstack()
    m["t11_acc_diff"] = 100 * (per.disamb_on - per.disamb_off).mean()
    lat = t.groupby(["pid", "condition", "step"]).latency_s.sum().groupby(["pid", "condition"]).median().unstack()
    m["t11_lat_diff"] = float((lat.disamb_on - lat.disamb_off).median())
    wl = B["wl"].pivot(index="pid", columns="condition", values="tlx")
    m["t11_tlx_diff"] = float((wl.disamb_on - wl.disamb_off).median())
    live = B["llm"]
    m["L4_pct"] = 100 * live.ctx_has_id.mean(); m["L5_pct"] = 100 * (1 - live.flag.mean())
    l1 = B["l1"]; m["L1_pct"] = 100 * l1.final.mean()
    v = B["voice"]; m["voice_high"] = 100 * v[v.band == 80].ok.mean()
    return m


# ------------------------------------------------------------------ TCI by dataset (model-based) -----
def tci_by_dataset(B):
    import a6_tci_model as tm
    cfg = tm.CFG["conditions"]["proposed"]
    lam0 = tm.lam_vec(cfg["lambda"])
    crn = tm.CRN(tm.CFG["seed"])
    sig = tm.calibrate_sigma(lam0, cfg["target_session_sd"], crn) if hasattr(tm, "calibrate_sigma") else 0.48
    sigmas = np.full(7, sig) if np.ndim(sig) == 0 else sig
    sel = B["sel"]; fa = first_attempts(sel[sel.block == "X3"])
    l1 = B["l1"]
    rows = []
    for ds in DATASETS:
        acc = fa[fa.dataset == ds].correct.mean()
        comp = B["sess"][B["sess"].dataset == ds].completeness.mean() / 100
        lam = lam0.copy()
        lam[0] = min(0.995, 0.972 * l1[l1.dataset == ds].final.mean() / l1[l1.dataset == "Best Graphene"].final.mean())
        lam[1] = min(0.999, acc / lam[0])
        lam[2] = min(0.999, lam0[2] * comp / 0.907)
        res = tm.simulate(lam, sigmas, crn)
        rows.append(dict(dataset=ds, lam_L1=lam[0], lam_L2=lam[1], lam_L3=lam[2], tci=res["tci"].mean(), tci_sd=res["tci"].std(ddof=1),
                         depth=res["depth"].mean(), audit=res["audit"].mean(), indep=float(np.prod(lam)), weakest=f"L{int(np.argmin(lam)) + 1}"))
    return pd.DataFrame(rows)


# ------------------------------------------------------------------ main -----
def main():
    nrep = int(sys.argv[1]) if len(sys.argv) > 1 else int(_CFG["replicates"])
    os.makedirs(OUT, exist_ok=True)
    t5, prof = load_participants()
    geo, unb, rend, ext = load_geometry()
    meta, nprops = metadata_presence()
    alpha, rho0 = calibrate(geo, np.random.default_rng(SEED0))
    print(f"calibration: alpha={alpha:.3f} rho0={rho0:.3f}; props/element {nprops}")
    rows = []
    for rep in range(1, nrep + 1):
        tg, A, R = part_a(t5, prof, geo, meta, alpha, rho0, SEED0 + rep)
        B = part_b(SEED0 + 1000 + rep, t5, prof, geo, meta, nprops, unb, rend, ext, alpha, rho0, R)
        m = metrics(B); m["seed"] = rep
        m["A_misbound_completeness"] = R[R.correct == 0].n_present.mean() / 11 * 100 if (R.correct == 0).any() else np.nan
        m["A_correct_completeness"] = R[R.correct == 1].n_present.mean() / 11 * 100
        rows.append(m)
        if rep % 25 == 0: print(rep, "/", nrep, flush=True)
    M = pd.DataFrame(rows)
    M.to_csv(os.path.join(OUT, "replicates_metrics.csv"), index=False)
    key = [c for c in M.columns if "|top1" in c or "|after" in c or "|completeness" in c] + ["t11_acc_diff", "t11_lat_diff"]
    z = (M[key] - M[key].median()) / M[key].std(ddof=1).replace(0, 1)
    best = int(M.seed.iloc[(z ** 2).sum(axis=1).idxmin()])
    json.dump(dict(typical_seed=best, alpha=alpha, rho0=rho0, params=P, nprops=nprops, replicates=nrep),
              open(os.path.join(OUT, "simulation_parameters.json"), "w"), indent=2, default=str)
    print("typical replicate", best)
    # final typical replicate, saved in full
    tg, A, R = part_a(t5, prof, geo, meta, alpha, rho0, SEED0 + best)
    B = part_b(SEED0 + 1000 + best, t5, prof, geo, meta, nprops, unb, rend, ext, alpha, rho0, R)
    D = os.path.join(OUT, "typical_replicate"); os.makedirs(D, exist_ok=True)
    t5.to_csv(os.path.join(D, "A_participant_derived.csv"), index=False)
    tg.to_csv(os.path.join(D, "A_cohort_targets.csv"), index=False)
    A.to_csv(os.path.join(D, "A_cohort_selections.csv"), index=False); R.to_csv(os.path.join(D, "A_cohort_records.csv"), index=False)
    for k, v in B.items():
        if isinstance(v, pd.DataFrame): v.to_csv(os.path.join(D, f"B_{k}.csv"), index=False)
        else:
            pd.concat([s.assign(dataset_sheet=n) for n, s in v.items()]).to_csv(os.path.join(D, f"B_{k}.csv"), index=False)
    tci_by_dataset(B).to_csv(os.path.join(D, "B_tci_by_dataset.csv"), index=False)
    print("written", os.path.relpath(OUT, paths.ROOT).replace(os.sep, "/"))


if __name__ == "__main__":
    main()
