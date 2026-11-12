"""R3 - Tables for revision 3 from the real inputs, the derived cohort and the typical replicate.
Writes outputs/followup_simulation/tables/*.csv|md and tables/r3_values.json. Each table title carries its evidence status.
"""
import csv, io, json, os, sys
import numpy as np
import pandas as pd
from scipy import stats

HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
from common import t_ci, bca_ci, wilson, hodges_lehmann_paired, wilcoxon_paired, cohen_kappa, median_iqr, write_md_table

import paths
R = paths.OUT_FOLLOWUP; D = os.path.join(R, "typical_replicate"); T = os.path.join(R, "tables")
REAL = paths.DERIVED      # derived, non-identifying inputs (counts, densities, pseudonymous elements)
os.makedirs(T, exist_ok=True)
FIELDS = ["element_id", "timestamp", "module_id", "category", "status", "screenshot_path", "notes",
          "element_name", "element_type", "geometric_descriptors", "report_status"]
FNAME = dict(element_id="Element ID", timestamp="Timestamp", module_id="Active module ID", category="Element category",
             status="Inspection status", screenshot_path="Screenshot path", notes="Inspector notes", element_name="Element name",
             element_type="Element type", geometric_descriptors="Key geometric descriptors", report_status="Report status")
DS = ["M3", "Best Graphene", "LG MONO"]
V = {}


def save(name, header, rows, title, note=None):
    write_md_table(os.path.join(T, name + ".md"), header, rows, title=title, note=note)
    with open(os.path.join(T, name + ".csv"), "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f); w.writerow(header); w.writerows(rows)
    V[name] = dict(header=header, rows=[[str(c) for c in r] for r in rows], title=title, note=note)


def f1(x): return f"{x:.1f}"


# ---------------------------------------------------------------- real: LG case and binding -----
def lg_case():
    # LG MONO counts from the metadata export and OBJ (raw_analyses/lg_binding.py; export not public)
    lg = json.load(open(os.path.join(REAL, "lg_case.json"), encoding="utf-8"))
    V["lg"] = dict(lg, multi_material=None); V["lg"].pop("renderable_objects", None)
    n_obj, n_res, n_unres = lg["renderable_objects"], lg["resolved"], lg["unresolved"]
    bd = pd.read_csv(os.path.join(REAL, "module_binding_summary.csv"))
    ex = pd.read_csv(os.path.join(REAL, "export_binding_summary.csv"))
    rows_ = []
    for _, r in bd.iterrows():
        e = ex[ex.dataset == r.dataset].iloc[0]
        rows_.append([r.dataset, int(r.modules), int(r.renderable_objects), int(r.bound_self + r.bound_via_parent), int(r.unbound),
                      int(r.duplicate_metadata_ids), int(r.multi_mesh_ids), f"{r.coverage_pct:.2f}",
                      f"{int(e.ids_resolved_to_one_record):,} of {int(e.scene_objects):,} ({e.coverage_pct:.2f})"])
    rows_.append(["LG MONO (Revit, IFC > OBJ)", 1, n_obj, n_res, n_unres, 0, 0,
                  f"{100 * n_res / n_obj:.2f}", f"{n_res} of {n_obj} (100.00)"])
    save("R3_binding_coverage", ["Dataset", "Modules", "Selectable objects", "Resolved to one record", "Unresolved", "Duplicated IDs",
                                 "Multi-mesh elements", "Coverage, deployed (%)", "Full export, resolved (%)"], rows_,
         "Binding coverage of the three datasets (REAL; computed from the exports and the Unity module prefabs).",
         "LG MONO: one Unity object per IFC GlobalId; the 499 OBJ groups are material submeshes of 402 objects, so no element is split.")
    dens = pd.read_csv(os.path.join(REAL, "density_summary.csv"))
    V["density"] = dens.to_dict("records")


# ---------------------------------------------------------------- derived cohort -----
def cohort():
    a = pd.read_csv(os.path.join(D, "A_participant_derived.csv"))
    top = a.top1_k * 10.0; dis = a.disamb_k * 10.0; ev = a.fields_k / 110 * 100
    rows = []
    def r(name, x, pooled="", dec=1):
        m, sd, lo, hi = t_ci(x); bl, bh = bca_ci(x)
        fmt = (lambda v: f"{v:.{dec}f}")
        rows.append([name, f"{fmt(m)} ({fmt(sd)})", f"{fmt(lo)} to {fmt(hi)}", f"{fmt(bl)} to {fmt(bh)}", pooled])
        return m, sd, lo, hi, bl, bh
    V["t10"] = {}
    V["t10"]["load"] = r("Task completion time, module load and spatial positioning (s)", a.load_s)
    V["t10"]["inspect"] = r("Task completion time, element-level inspection pass (s)", a.inspect_s)
    V["t10"]["session"] = r("Task completion time, full session, end-to-end (s)", a.session_s)
    V["t10"]["top1"] = r("Element-selection accuracy, top-1 (%)", top, f"{int(a.top1_k.sum())} of 150")
    V["t10"]["disamb"] = r("Element-selection accuracy, including disambiguation (%)", dis, f"{int(a.disamb_k.sum())} of 150")
    V["t10"]["report"] = r("Reporting time, HTML and JSON export (s)", a.report_s)
    V["t10"]["rereg"] = r("Re-registration events per session (count)", a.rereg.astype(float), f"{int(a.rereg.sum())} in total", dec=2)
    V["t10"]["evid"] = r("Evidence completeness, field presence (%)", ev, f"{int(a.fields_k.sum()):,} of 1,650 fields")
    V["t10"]["pooled"] = dict(top1=int(a.top1_k.sum()), disamb=int(a.disamb_k.sum()), fields=int(a.fields_k.sum()), rereg=int(a.rereg.sum()),
                              top1_range=[int(top.min()), int(top.max())], disamb_range=[int(dis.min()), int(dis.max())])
    save("Table10_revised", ["Metric", "Mean (SD)", "95% CI (t, df = 14)", "95% CI (BCa, 10,000)", "Pooled count"], rows,
         "Table 10 (revised). DERIVED from the per-participant values of the study workbook; accuracy and completeness converted to whole selections and fields by largest-remainder rounding.")
    rows = [[p, f"{k1}/10", f"{k2}/10", f"{e:.1f}", int(rr), f"{s:.1f}"] for p, k1, k2, e, rr, s in
            zip(a.pid, a.top1_k, a.disamb_k, ev, a.rereg, a.session_s)]
    m = lambda x, d=1: f"{np.mean(x):.{d}f} ({np.std(x, ddof=1):.{d}f})"
    rows.append(["Mean (SD)", m(top) + "%", m(dis) + "%", m(ev), m(a.rereg, 2), m(a.session_s)])
    tc = lambda x, d=1: "{:.{d}f}–{:.{d}f}".format(*t_ci(x)[2:], d=d)
    bc = lambda x, d=1: "{:.{d}f}–{:.{d}f}".format(*bca_ci(x), d=d)
    rows.append(["t-based 95% CI", tc(top), tc(dis), tc(ev), tc(a.rereg.astype(float), 2), tc(a.session_s)])
    rows.append(["BCa bootstrap 95% CI", bc(top), bc(dis), bc(ev), bc(a.rereg.astype(float), 2), bc(a.session_s)])
    save("TableA6_participant_level", ["Participant", "Top-1 correct (of 10)", "Correct after disambiguation (of 10)",
                                       "Evidence completeness (%)", "Re-registrations", "Session time (s)"], rows,
         "Table A6. DERIVED participant-level results (counts from largest-remainder rounding of the workbook rates).")

    sel = pd.read_csv(os.path.join(D, "A_cohort_selections.csv")); rec = pd.read_csv(os.path.join(D, "A_cohort_records.csv"))
    rows = []
    for f in FIELDS:
        n = int((rec[f] == 0).sum()); rows.append([FNAME[f], n, len(rec), f"{100 * n / len(rec):.1f}"])
    tot = int((rec[FIELDS] == 0).sum().sum()); rows.append(["All fields", tot, len(rec) * 11, f"{100 * tot / (len(rec) * 11):.1f}"])
    save("TableA5_missingness", ["Required field", "Missing, n", "Total records", "Missing (%)"], rows,
         "Table A5. Field-level missingness (SIMULATED allocation of each participant's derived missing-field count; metadata gaps are the real gaps of the confirmed elements).")
    V["a5"] = {FNAME[f]: int((rec[f] == 0).sum()) for f in FIELDS}
    rows = []
    for lab, g in (("Correctly bound", rec[rec.correct == 1]), ("Misbound", rec[rec.correct == 0])):
        rows.append([lab, len(g), f"{100 * g.n_present.mean() / 11:.1f}", f"{int((g.n_present == 11).sum())}/{len(g)}", f"{len(g)}/{len(g)}"])
    save("A4_error_propagation", ["Exported events", "n", "Mean completeness (%)", "All 11 fields present", "Report ID = confirmed ID"], rows,
         "Error propagation (SIMULATED allocation conditioned on the derived counts).")
    V["a4"] = rows
    rows = []
    for col, lab in (("target_type", "Target type"), ("density_band", "Density band")):
        for lv, g in sel.groupby(col):
            k1, k2, n = int(g.top1_correct.sum()), int(g.correct.sum()), len(g)
            p1, l1, h1 = wilson(k1, n); p2, l2, h2 = wilson(k2, n)
            rows.append([lab, lv, n, f"{k1} ({100 * p1:.1f}%; {100 * l1:.1f}–{100 * h1:.1f})", f"{k2} ({100 * p2:.1f}%; {100 * l2:.1f}–{100 * h2:.1f})"])
    save("Table10a_accuracy_by_target", ["Grouping", "Level", "Selections", "Top-1 correct (Wilson 95% CI)", "Correct after disambiguation (Wilson 95% CI)"],
         rows, "Table 10a. Selection accuracy by target type and density band, original cohort (SIMULATED allocation of the derived counts; densities REAL).")


# ---------------------------------------------------------------- follow-up session (simulated) -----
def followup():
    sel = pd.read_csv(os.path.join(D, "B_sel.csv")); wl = pd.read_csv(os.path.join(D, "B_wl.csv"))
    t = sel[sel.block == "T11"]
    fa = t[t.attempt == 1]
    per = {}
    acc = fa.groupby(["pid", "condition"]).correct.mean().unstack() * 100
    lat = t.groupby(["pid", "condition", "step"]).latency_s.sum().groupby(["pid", "condition"]).median().unstack()
    corr = t[t.attempt > 1].groupby(["pid", "condition"]).size().unstack().reindex(acc.index).fillna(0)
    for c in ("disamb_on", "disamb_off"):
        if c not in corr: corr[c] = 0
    pw = wl.pivot(index="pid", columns="condition", values="pass_s"); tl = wl.pivot(index="pid", columns="condition", values="tlx")
    rows = []
    def r(name, off, on, dec=1):
        mo, qo1, qo3 = median_iqr(off); mn, qn1, qn3 = median_iqr(on)
        est, lo, hi = hodges_lehmann_paired(np.asarray(on) - np.asarray(off)); p, rr = wilcoxon_paired(off, on)
        f = lambda v: f"{v:.{dec}f}"
        rows.append([name, f"{f(mo)} [{f(qo1)}–{f(qo3)}]", f"{f(mn)} [{f(qn1)}–{f(qn3)}]", f"{est:+.{dec}f} ({lo:+.{dec}f} to {hi:+.{dec}f})",
                     "< 0.001" if p < 0.001 else f"{p:.3f}", f"{rr:+.2f}"])
        return est, lo, hi, p, rr
    V["t11"] = {}
    V["t11"]["acc"] = r("Element-selection accuracy (% of 10)", acc.disamb_off, acc.disamb_on, 0)
    V["t11"]["lat"] = r("Selection latency per target, hover to confirmation (s)", lat.disamb_off, lat.disamb_on, 2)
    V["t11"]["pass"] = r("Inspection-pass completion time (s)", pw.disamb_off, pw.disamb_on, 1)
    V["t11"]["corr"] = r("Selection corrections per session", corr.disamb_off, corr.disamb_on, 1)
    V["t11"]["tlx"] = r("Workload, NASA-TLX-derived (1–5)", tl.disamb_off, tl.disamb_on, 2)
    on = fa[fa.condition == "disamb_on"]
    inv = on.groupby("pid").popup.sum(); cand = on[on.popup == 1].candidates
    rows.append(["Popup invocations per session", "Not applicable", "{:.0f} [{:.0f}–{:.0f}]".format(*median_iqr(inv)), "Not applicable", "", ""])
    rows.append(["Candidates shown per popup, mean (maximum)", "Not applicable", f"{cand.mean():.2f} ({int(cand.max())})", "Not applicable", "", ""])
    V["t11"]["popup"] = (float(np.median(inv)), float(cand.mean()), int(cand.max()))
    V["t11"]["acc_means"] = (float(acc.disamb_off.mean()), float(acc.disamb_on.mean()))
    V["t11"]["ease"] = float(wl.popup_ease.dropna().mean())
    save("Table11_disambiguation", ["Measure", "Without disambiguation, median [IQR]", "With disambiguation, median [IQR]",
                                    "Paired difference, HL (95% CI)", "Wilcoxon p", "r"], rows,
         "Table 11. SIMULATED within-subject comparison (n = 15; two matched target sets of 10 on Best Graphene module M2-1; order counterbalanced).",
         "Difference = with minus without; HL = Hodges-Lehmann estimate with exact Walsh-average CI; r = matched-pairs rank-biserial correlation.")

    # ---- three datasets ----
    x = sel[sel.block == "X3"]; xa = x[x.attempt == 1]
    sess = pd.read_csv(os.path.join(D, "B_sess.csv")); rec = pd.read_csv(os.path.join(D, "B_rec.csv")); llm = pd.read_csv(os.path.join(D, "B_llm.csv"))
    l1 = pd.read_csv(os.path.join(D, "B_l1.csv")); reg = pd.read_csv(os.path.join(D, "B_reg.csv")); tci = pd.read_csv(os.path.join(D, "B_tci_by_dataset.csv"))
    M = pd.read_csv(os.path.join(R, "replicates_metrics.csv"))
    dens = pd.read_csv(os.path.join(REAL, "elements.csv"), dtype={"element_id": str})
    TM = {"M3": "M3-1", "Best Graphene": "M1-1", "LG MONO": "MONO"}
    rows = []; V["x3"] = {}
    def add(name, fn, status):
        rows.append([name] + [fn(d) for d in DS] + [status])
    add("Task module (selectable objects)", lambda d: f"{TM[d]} ({len(dens[(dens.dataset == d) & (dens.module == TM[d])])})", "Real")
    add("Neighbours within 0.5 m of an element, median [IQR]", lambda d: "{:.0f} [{:.0f}–{:.0f}]".format(*np.percentile(dens[(dens.dataset == d) & (dens.module == TM[d])].neighbours_0p5m, [50, 25, 75])), "Real")
    add("Targets in the high-density band (of 10)", lambda d: str(int((xa[(xa.dataset == d) & (xa.pid == xa.pid.iloc[0])].density_band == "high").sum())), "Real")
    def accrow(col):
        def fn(d):
            g = xa[xa.dataset == d]; k, n = int(g[col].sum()), len(g); p, lo, hi = wilson(k, n)
            q = M[f"{d}|{'top1' if col == 'top1_correct' else 'after'}"].quantile([0.025, 0.975]).values
            V["x3"][(d, col)] = (k, n, 100 * p, 100 * lo, 100 * hi)
            return f"{k}/{n} = {100 * p:.1f}% ({100 * lo:.1f}–{100 * hi:.1f}) [{q[0]:.0f}–{q[1]:.0f}]"
        return fn
    add("Top-1 accuracy, pooled (Wilson 95% CI) [replicate range]", accrow("top1_correct"), "Simulated")
    add("Accuracy after disambiguation, pooled (Wilson 95% CI) [replicate range]", accrow("correct"), "Simulated")
    add("Popup invocations per participant, median", lambda d: f"{xa[xa.dataset == d].groupby('pid').popup.sum().median():.0f}", "Simulated")
    add("Candidates per popup, mean (max)", lambda d: f"{xa[(xa.dataset == d) & (xa.popup == 1)].candidates.mean():.2f} ({int(xa[(xa.dataset == d) & (xa.popup == 1)].candidates.max())})", "Simulated")
    add("Selection latency per target, median (s)", lambda d: f"{x[x.dataset == d].groupby(['pid', 'step']).latency_s.sum().median():.2f}", "Simulated")
    add("Module load and positioning, mean (SD) (s)", lambda d: f"{sess[sess.dataset == d].load_s.mean():.1f} ({sess[sess.dataset == d].load_s.std():.1f})", "Simulated")
    add("Inspection pass, 10 targets, mean (SD) (s)", lambda d: f"{sess[sess.dataset == d].inspect_s.mean():.1f} ({sess[sess.dataset == d].inspect_s.std():.1f})", "Simulated")
    add("Reporting time, mean (SD) (s)", lambda d: f"{sess[sess.dataset == d].report_s.mean():.1f} ({sess[sess.dataset == d].report_s.std():.1f})", "Simulated")
    add("Evidence completeness, mean (SD) (%)", lambda d: f"{sess[sess.dataset == d].completeness.mean():.1f} ({sess[sess.dataset == d].completeness.std():.1f})", "Simulated")
    add("Re-registrations per block, mean", lambda d: f"{sess[sess.dataset == d].rereg.mean():.2f}", "Simulated")
    add("LLM prompt tokens per question, mean", lambda d: f"{llm[(llm.dataset == d) & (llm.call_type == 'question')].prompt_tokens.mean():.0f}", "Simulated")
    add("LLM responses free of unsupported values (L5) (%)", lambda d: f"{100 * (1 - llm[llm.dataset == d].flag.mean()):.1f}", "Simulated")
    add("Observation frame confirms target (L1) (%)", lambda d: f"{100 * l1[l1.dataset == d].final.mean():.1f}", "Simulated")
    add("Registration residual before / after re-registration, median (mm)",
        lambda d: f"{reg[(reg.dataset == d) & (reg.after == 0)].residual_mm.median():.1f} / {reg[(reg.dataset == d) & (reg.after == 1)].residual_mm.median():.1f}", "Simulated")
    add("Modelled TCI, proposed system, mean (SD)", lambda d: f"{tci[tci.dataset == d].tci.iloc[0]:.3f} ({tci[tci.dataset == d].tci_sd.iloc[0]:.3f})", "Model-based")
    add("Modelled audit resolvability", lambda d: f"{tci[tci.dataset == d].audit.iloc[0]:.3f}", "Model-based")
    save("Table11b_three_datasets", ["Measure", "M3 (Revit, OBJ)", "Best Graphene (ArchiCAD, FBX)", "LG MONO (Revit, IFC)", "Evidence"], rows,
         "Table 11b. Cross-dataset block of the follow-up session (n = 15; 10 targets per dataset; disambiguation on; Latin-square order). Densities REAL; session outcomes SIMULATED; TCI MODEL-BASED.",
         "Replicate range = 2.5th–97.5th percentile over 200 simulated replications of the 15-participant block.")
    V["x3_sess"] = {d: dict(load=sess[sess.dataset == d].load_s.mean(), inspect=sess[sess.dataset == d].inspect_s.mean(),
                            report=sess[sess.dataset == d].report_s.mean(), comp=sess[sess.dataset == d].completeness.mean(),
                            lat=float(x[x.dataset == d].groupby(['pid', 'step']).latency_s.sum().median()),
                            tci=float(tci[tci.dataset == d].tci.iloc[0]), audit=float(tci[tci.dataset == d].audit.iloc[0]),
                            l5=100 * (1 - llm[llm.dataset == d].flag.mean()), tokens=llm[(llm.dataset == d) & (llm.call_type == 'question')].prompt_tokens.mean())
                    for d in DS}
    # accuracy by density band pooled over datasets
    rows = []
    for b in ("low", "medium", "high"):
        g = xa[xa.density_band == b]; k1, k2, n = int(g.top1_correct.sum()), int(g.correct.sum()), len(g)
        p1, l1_, h1 = wilson(k1, n); p2, l2, h2 = wilson(k2, n)
        rows.append([b, n, f"{100 * p1:.1f} ({100 * l1_:.1f}–{100 * h1:.1f})", f"{100 * p2:.1f} ({100 * l2:.1f}–{100 * h2:.1f})"])
    save("X3_accuracy_by_band", ["Density band", "Selections", "Top-1 (%)", "After disambiguation (%)"], rows,
         "Cross-dataset accuracy by density band (SIMULATED).")
    V["x3_band"] = rows

    # ---- system integrity ----
    pay = pd.read_csv(os.path.join(D, "B_pay.csv")); bench = pd.read_csv(os.path.join(D, "B_bench.csv")); voice = pd.read_csv(os.path.join(D, "B_voice.csv"))
    recs = pay[pay.kind == "record"]; pl = pay[pay.kind == "payload"]
    rows = []
    def ir(check, unit, k, n, link):
        p, lo, hi = wilson(k, n); rows.append([check, unit, f"{k} / {n}", f"{100 * p:.1f}% ({100 * lo:.1f}–{100 * hi:.1f})", link])
        return (k, n, 100 * p, 100 * lo, 100 * hi)
    V["integ"] = {}
    V["integ"]["export"] = ir("Export consistency (ID, timestamp, screenshot path, hash)", "Record", int(recs.consistent.sum()), len(recs), "L6, L7")
    V["integ"]["l4"] = ir("Context contains the confirmed element ID", "LLM request (live)", int(llm.ctx_has_id.sum()), len(llm), "L4")
    V["integ"]["l5"] = ir("Response free of unsupported IDs or values", "LLM response (live)", int((1 - llm.flag).sum()), len(llm), "L5")
    V["integ"]["l5b"] = ir("Response free of unsupported IDs or values", "LLM response (30-query benchmark × 3)", int((1 - bench.flag).sum()), len(bench), "L5")
    V["integ"]["l1"] = ir("Observation frame confirms the target", "Event", int(l1.final.sum()), len(l1), "L1")
    red = pl[pl.redaction == 1]
    V["integ"]["redact"] = ir("Outbound payload free of project names (restricted-data mode)", "Payload", int((red.names == 0).sum()), len(red), "—")
    save("Table11a_system_integrity", ["Check", "Unit", "Passed / total", "Rate (Wilson 95% CI)", "Link in Table 7"], rows,
         "Table 11a. System integrity checks of the revised build (SIMULATED follow-up session, n = 15, three datasets).")
    rated = l1[l1.missing == 0]
    V["l1"] = dict(events=len(l1), rated=len(rated), missing=int(l1.missing.sum()), agree=100 * (rated.r1 == rated.r2).mean(),
                   kappa=cohen_kappa(rated.r1, rated.r2), pass_pct=100 * l1.final.mean())
    V["reg"] = dict(n=int((reg.after == 0).sum()), before=(reg[reg.after == 0].residual_mm.median(), reg[reg.after == 0].residual_mm.quantile(0.95)),
                    after=(reg[reg.after == 1].residual_mm.median(), reg[reg.after == 1].residual_mm.quantile(0.95)))
    rows = []
    for b, lab in ((60, "Low (< 65 dB(A))"), (70, "Medium (65–75)"), (80, "High (≥ 75)")):
        g = voice[voice.band == b]; p, lo, hi = wilson(int(g.ok.sum()), len(g))
        rows.append([lab, len(g), f"{int(g.ok.sum())}/{len(g)} = {100 * p:.1f}% ({100 * lo:.1f}–{100 * hi:.1f})", f"{g.latency_s.median():.2f}"])
    save("A13_live_voice", ["Noise band", "Utterances", "Intent accuracy (Wilson 95% CI)", "Median voice-to-response latency (s)"], rows,
         "Live voice commands by ambient-noise band (SIMULATED; playback accuracy of Table 12 minus an assumed live-speech penalty).")
    V["voice"] = rows
    V["llm_tokens"] = float(llm[llm.call_type == "question"].prompt_tokens.mean())


if __name__ == "__main__":
    lg_case(); cohort(); followup()
    def conv(o):
        if isinstance(o, dict): return {str(k) if not isinstance(k, str) else k: conv(v) for k, v in o.items()}
        if isinstance(o, (list, tuple)): return [conv(v) for v in o]
        if isinstance(o, (np.integer,)): return int(o)
        if isinstance(o, (np.floating,)): return float(o)
        return o
    json.dump(conv(V), open(os.path.join(T, "r3_values.json"), "w", encoding="utf-8"), indent=1, ensure_ascii=False, default=str)
    for f in sorted(os.listdir(T)):
        if f.endswith(".md"): print(open(os.path.join(T, f), encoding="utf-8").read())
