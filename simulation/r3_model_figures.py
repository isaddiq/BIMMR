"""R3 - Redraw the model-based TCI figures with the common-random-number re-run (A6), so that the
figures, Tables 15-17 and the text report one value for the full configuration.

Writes to outputs/tci_model/:
  Figure22_TCI_conditions.png  (was Figure 21; panels A-D)
  Figure23_ablation_bracket.png (was Figure 22; panels A-B)
  Figure24_sensitivity_power.png (was Figure 23; panels A-C)
Every value is model-generated (MODEL-BASED; simulated, not observed).
"""
import json, os, re
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.patches import FancyBboxPatch, Patch
import a6_tci_model as tm

OUT = tm.OUT
C = {"paper": "#009E73", "tablet": "#D55E00", "proposed": "#0072B2"}
LAB = {"paper": "Paper-based", "tablet": "BIM-on-tablet", "proposed": "Proposed BIM-MR"}
LAB2 = {"paper": "Paper-\nbased", "tablet": "BIM-on-\ntablet", "proposed": "Proposed\nBIM-MR"}
ORDER = ["paper", "tablet", "proposed"]
plt.rcParams.update({"font.family": "serif", "font.serif": ["Times New Roman", "DejaVu Serif"], "font.size": 11,
                     "axes.spines.top": False, "axes.spines.right": False, "savefig.dpi": 300,
                     "savefig.bbox": "tight", "axes.edgecolor": "#444", "mathtext.fontset": "stix"})


def frame(ax, letter, title):
    lg = ax.get_legend()
    if lg is not None:
        h, l = lg.legend_handles, [t.get_text() for t in lg.get_texts()]; lg.remove()
    else:
        h, l = ax.get_legend_handles_labels()
    ax.set_title("      " + title, loc="left", fontsize=12, pad=30)
    ax.annotate(letter, xy=(0, 1), xycoords="axes fraction", xytext=(-2, 30), textcoords="offset points",
                fontsize=17, weight="bold", va="center", ha="right",
                bbox=dict(boxstyle="round,pad=0.25", fc="white", ec="#4a6fa5", lw=0.9))
    if h:
        ax.legend(h, l, ncol=len(h), loc="lower center", bbox_to_anchor=(0.5, 1.0), frameon=False, fontsize=10)


def run():
    crn = tm.CRN(tm.CFG["seed"]); conds = tm.CFG["conditions"]
    res, sig = {}, {}
    for c in ORDER:
        lam = tm.lam_vec(conds[c]["lambda"]); sig[c] = tm.calibrate_sigma(lam, conds[c]["target_session_sd"], crn)
        res[c] = tm.simulate(lam, np.full(7, sig[c]), crn); res[c]["lam"] = lam
        t = conds[c]["time"]
        res[c]["on"] = np.maximum(t["on_floor_mean"] + t["on_floor_sd"] * crn.z[:, 0], 0).mean()
        res[c]["rec"] = np.maximum(t["record_mean"] + t["record_sd"] * crn.z[:, 1], 0).mean()
    full_lam = res["proposed"]["lam"]; sp = np.full(7, sig["proposed"])
    abl = []
    for key, a in tm.CFG["ablations"].items():
        lam = full_lam.copy()
        for k, v in a["lambda"].items(): lam[tm.LINKS.index(k)] = v
        abl.append((a["label"], tm.simulate(lam, sp, crn)))
    g = tm.CFG["sensitivity_grid"]
    l1s, l4s = np.linspace(*g["L1"][:2], g["L1"][2]), np.linspace(*g["L4"][:2], g["L4"][2])
    grid = np.array([[tm.simulate(np.r_[l1, full_lam[1:3], l4, full_lam[4:]], sp, crn)["tci"].mean() for l1 in l1s] for l4 in l4s])
    op = res["proposed"]["tci"].mean()
    el = []
    for k in range(7):
        lam = full_lam.copy(); lam[k] -= 0.05; el.append(op - tm.simulate(lam, sp, crn)["tci"].mean())
    return res, abl, (l1s, l4s, grid), el


def fig22(res):
    fig = plt.figure(figsize=(15, 9))
    a = fig.add_axes([0.08, 0.56, 0.38, 0.32]); b = fig.add_axes([0.59, 0.56, 0.39, 0.32])
    c = fig.add_axes([0.08, 0.07, 0.36, 0.32]); d = fig.add_axes([0.59, 0.07, 0.39, 0.32])
    mk = {"paper": ("s", ":"), "tablet": ("^", "--"), "proposed": ("o", "-")}
    for k in ORDER:
        a.plot(range(1, 8), res[k]["survival"], ls=mk[k][1], marker=mk[k][0], color=C[k], mfc="white", lw=2, label=LAB[k])
    a.set_xticks(range(1, 8), [f"L{i}" for i in range(1, 8)]); a.set_ylim(0, 1.02); a.set_xlabel("chain position")
    a.set_ylabel("proportion of events\nstill intact"); a.grid(axis="y", color="#ddd"); a.legend()
    s = res["paper"]["survival"]; a.annotate("largest single break\nat L2 (elementId)", (2, s[1]), (3.5, 0.55), arrowprops=dict(arrowstyle="-", color="#666"))
    frame(a, "A", "Where provenance breaks along the chain")
    data = [res[k]["tci"] for k in ORDER]
    vp = b.violinplot(data, showextrema=False, widths=0.8)
    for body, k in zip(vp["bodies"], ORDER): body.set_facecolor(C[k]); body.set_alpha(0.2)
    for i, k in enumerate(ORDER):
        q = np.percentile(res[k]["tci"], [5, 25, 50, 75, 95])
        b.vlines(i + 1, q[0], q[4], color=C[k], lw=1.5); b.add_patch(plt.Rectangle((i + 0.92, q[1]), 0.16, q[3] - q[1], color=C[k]))
        b.hlines(q[2], i + 0.92, i + 1.08, color="white", lw=1.5)
        b.text(i + 1, 1.12, f"{res[k]['tci'].mean():.3f}", ha="center", weight="bold", fontsize=13)
    b.set_xticks([1, 2, 3], [LAB2[k] for k in ORDER]); b.set_ylim(0, 1.2); b.set_yticks(np.arange(0, 1.01, 0.2))
    b.set_ylabel("Traceability Chain Integrity"); b.grid(axis="y", color="#ddd")
    frame(b, "B", "Session-level distribution (condition means at top)")
    M = np.array([res[k]["marginals"] for k in ORDER])
    im = c.imshow(M, cmap="Blues", vmin=0.4, vmax=1.15, aspect="auto")
    for i in range(3):
        for j in range(7): c.text(j, i, f"{M[i, j]:.2f}", ha="center", va="center", fontsize=12)
    c.set_xticks(range(7), [f"L{i}" for i in range(1, 8)]); c.set_yticks(range(3), [LAB2[k] for k in ORDER])
    c.set_xlabel(r"marginal link integrity  $\lambda_k$"); cb = fig.colorbar(im, ax=c, fraction=0.04, pad=0.02); cb.set_ticks([0.4, 0.6, 0.8, 1.0])
    frame(c, "C", "Per-link marginal integrity")
    for i, k in enumerate(ORDER[::-1]):
        on, rec = res[k]["on"], res[k]["rec"]
        d.barh(i, on, color=C[k], alpha=0.45); d.barh(i, rec, left=on, color="white", ec=C[k], hatch="///")
        d.text(on / 2, i, f"{on:.0f}", ha="center", va="center");
        if rec > 150: d.text(on + rec / 2, i, f"{rec:,.0f}", ha="center", va="center")
        d.text(on + rec + 40, i, f"{on + rec:,.0f} s", va="center", weight="bold")
    d.set_yticks(range(3), [LAB2[k] for k in ORDER[::-1]]); d.set_xlim(0, 3000); d.set_xlabel("time (s)"); d.grid(axis="x", color="#ddd")
    d.legend(handles=[Patch(color="#bbb", label="on-floor inspection"), Patch(fc="white", ec="#777", hatch="///", label="completing a BIM-addressable record")])
    frame(d, "D", "Time to a traceable record")
    fig.savefig(os.path.join(OUT, "Figure22_TCI_conditions.png")); plt.close(fig)


def fig23(res, abl):
    fig = plt.figure(figsize=(16, 4.8))
    a = fig.add_axes([0.13, 0.12, 0.34, 0.68]); b = fig.add_axes([0.62, 0.12, 0.35, 0.68])
    full = res["proposed"]; rows = [("Full system", full)] + abl
    names = ["Full system", "− module\nscoping", "− n-best\ndisambiguation", "− structured\nevidence schema", "− scene-conditioned\nLLM context"]
    for i, (n, r) in enumerate(rows):
        y = len(rows) - 1 - i; t, au = r["tci"].mean(), r["audit"].mean()
        a.barh(y + 0.18, t, 0.34, color="#1f78b4"); a.barh(y - 0.18, au, 0.34, color="#e07020")
        rel = "" if i == 0 else f" ({100 * (t - full['tci'].mean()) / full['tci'].mean():+.1f}%)"
        a.text(t + 0.01, y + 0.18, f"{t:.3f}{rel}", va="center"); a.text(au + 0.01, y - 0.18, f"{au:.3f}", va="center")
    a.axvline(full["tci"].mean(), ls="--", lw=0.8, color="#1f78b4")
    a.set_yticks(range(len(rows))[::-1], names); a.set_xlim(0, 1.25); a.set_xticks(np.arange(0, 1.01, 0.2)); a.set_xlabel("proportion of inspection events")
    a.legend(handles=[Patch(color="#1f78b4", label="Traceability Chain Integrity"), Patch(color="#e07020", label="audit resolvability")]); a.grid(axis="x", color="#ddd")
    frame(a, "A", "Removing one mechanism at a time")
    for i, k in enumerate(ORDER):
        y = 2 - i; lam = res[k]["lam"]; lo, hi = np.prod(lam), lam.min()
        b.plot([lo, hi], [y, y], color=C[k], lw=9, alpha=0.3, solid_capstyle="butt"); b.vlines([lo, hi], y - 0.15, y + 0.15, color=C[k], lw=2)
        b.plot(res[k]["tci"].mean(), y, "o", mfc="white", mec=C[k], mew=2.5, ms=10)
        b.text(lo - 0.03, y, f"{lo:.2f}", ha="right", va="center"); b.text(hi + 0.03, y, f"{hi:.2f}", va="center")
        b.text((lo + hi) / 2, y - 0.38, f"width {hi - lo:.2f}", ha="center", weight="bold")
    b.set_yticks([2, 1, 0], [LAB2[k] for k in ORDER]); b.set_xlim(-0.2, 1.3); b.set_xticks([0, 0.5, 1.0]); b.set_ylim(-0.7, 2.5)
    b.set_xlabel("Traceability Chain Integrity"); b.grid(axis="x", color="#ddd")
    b.plot([], [], "o", mfc="white", mec="#888", mew=2, label="modelled TCI"); b.plot([], [], color="#ccc", lw=8, label=r"bound [$\Pi\lambda_k$, min $\lambda_k$]")
    b.legend()
    frame(b, "B", "Bracketing bound and its width")
    fig.savefig(os.path.join(OUT, "Figure23_ablation_bracket.png")); plt.close(fig)


def fig24(res, gridpack, el):
    l1s, l4s, grid = gridpack
    t17 = [r for r in open(os.path.join(OUT, "Table17_power_rerun.md"), encoding="utf-8").read().splitlines() if r.startswith("| ") and "Contrast" not in r]
    pw = [(c.strip(), int(n8), int(n9)) for c, e, n8, n9 in (r.strip("| ").split(" | ") for r in t17)]
    fig = plt.figure(figsize=(15, 11))
    a = fig.add_axes([0.08, 0.57, 0.34, 0.33]); b = fig.add_axes([0.60, 0.57, 0.37, 0.33]); c = fig.add_axes([0.32, 0.07, 0.42, 0.32])
    im = a.pcolormesh(l4s, l1s, grid.T, cmap="Blues", vmin=0.65, vmax=1.0, shading="nearest")
    a.contour(l4s, l1s, grid.T, levels=[0.85], colors="#333", linestyles="--", linewidths=1.2)
    a.contour(l4s, l1s, grid.T, levels=[0.90], colors="#D55E00", linewidths=2)
    op = res["proposed"]["tci"].mean()
    a.plot(0.965, 0.972, "o", color="#D55E00", mec="white", ms=10)
    a.annotate(f"operating point\nTCI = {op:.2f}", (0.965, 0.972), (0.89, 0.945), arrowprops=dict(arrowstyle="-", color="#333"))
    a.set_xlabel(r"$\lambda_{L4}$  metadata-to-context"); a.set_ylabel(r"$\lambda_{L1}$  physical-to-scene")
    cb = fig.colorbar(im, ax=a, fraction=0.05, pad=0.03); cb.set_label("TCI")
    a.plot([], [], "--", color="#333", label="TCI = 0.85"); a.plot([], [], color="#D55E00", lw=2, label="TCI = 0.90")
    a.legend()
    frame(a, "A", "Sensitivity to the two unmeasured links")
    b.barh(range(7)[::-1], el, color="#1f78b4", height=0.6, label="each link reduced by 0.05 in turn")
    for i, e in enumerate(el): b.text(e + 0.001, 6 - i, f"{e:.3f}", va="center")
    b.set_yticks(range(7)[::-1], [f"L{i}" for i in range(1, 8)]); b.set_xlim(0, 0.07); b.set_xlabel("loss in TCI"); b.grid(axis="x", color="#ddd")
    b.legend()
    frame(b, "B", "No single link dominates")
    short = {"Proposed system vs paper-based": "vs paper-based", "Proposed system vs BIM-on-tablet": "vs BIM-on-tablet",
             "Full vs - module scoping": "− module scoping", "Full vs - n-best disambiguation": "− disambiguation",
             "Full vs - structured evidence schema": "− evidence schema", "Full vs - scene-conditioned LLM context": "− LLM context"}
    pw = sorted(pw, key=lambda x: (x[1], x[2]))[::-1]
    for i, (nm, n8, n9) in enumerate(pw):
        col = "#0072B2" if nm.startswith("Proposed") else "#D55E00"
        c.plot([n8, n9], [i, i], color=col, alpha=0.5, lw=2); c.plot(n8, i, "o", color=col, ms=5); c.plot(n9, i, "o", mfc="white", mec=col, mew=2, ms=10)
        c.text(n9 + 1.5, i, f"{n8} / {n9}", va="center")
    c.axvline(15, ls="--", color="#333", lw=1); c.text(15.8, len(pw) - 0.4, "present study, n = 15", ha="left")
    c.set_yticks(range(len(pw)), [short.get(p[0], p[0]) for p in pw]); c.set_xlim(0, 62); c.set_xticks([0, 15, 30, 45, 60])
    c.set_xlabel("participants required   (marker pair: 80% / 90% power)"); c.grid(axis="x", color="#ddd")
    c.plot([], [], "o", color="#888", ms=5, label="80% power"); c.plot([], [], "o", mfc="white", mec="#888", mew=2, ms=10, label="90% power")
    c.legend(); c.set_ylim(-0.6, len(pw) + 0.2)
    frame(c, "C", "Sample size a confirmatory trial would need")
    fig.savefig(os.path.join(OUT, "Figure24_sensitivity_power.png")); plt.close(fig)


if __name__ == "__main__":
    res, abl, grid, el = run()
    fig22(res); fig23(res, abl); fig24(res, grid, el)
    s = dict(survival={k: [round(float(x), 3) for x in res[k]["survival"]] for k in ORDER},
             marginals={k: [round(float(x), 3) for x in res[k]["marginals"]] for k in ORDER},
             elasticity=[round(float(e), 4) for e in el], grid_min=float(grid[2].min()), grid_max=float(grid[2].max()),
             ablation={n: dict(tci=round(float(r["tci"].mean()), 3), audit=round(float(r["audit"].mean()), 3), depth=round(float(r["depth"].mean()), 2)) for n, r in abl})
    json.dump(s, open(os.path.join(OUT, "r3_figure_values.json"), "w"), indent=2)
    print(json.dumps(s, indent=1))
