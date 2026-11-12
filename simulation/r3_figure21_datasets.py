"""R3 - New Figure 21: the three datasets. (A) REAL within-module density of the task modules;
(B) SIMULATED top-1 and post-disambiguation accuracy with Wilson CI and replicate range;
(C) REAL binding coverage and SIMULATED evidence completeness; (D) SIMULATED selection latency."""
import os, sys
import numpy as np, pandas as pd
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.patches import Patch
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
from common import wilson
import r3_model_figures as mf

import paths
R = paths.OUT_FOLLOWUP; D = os.path.join(R, "typical_replicate")
REAL = paths.DERIVED
DS = ["M3", "Best Graphene", "LG MONO"]; TM = {"M3": "M3-1", "Best Graphene": "M1-1", "LG MONO": "MONO"}
LAB = {"M3": "M3\n(Revit, OBJ)", "Best Graphene": "Best Graphene\n(ArchiCAD, FBX)", "LG MONO": "LG MONO\n(Revit, IFC)"}
COL = {"M3": "#0072B2", "Best Graphene": "#009E73", "LG MONO": "#D55E00"}

dens = pd.read_csv(os.path.join(REAL, "elements.csv"), dtype={"element_id": str})
sel = pd.read_csv(os.path.join(D, "B_sel.csv")); x = sel[sel.block == "X3"]; xa = x[x.attempt == 1]
sess = pd.read_csv(os.path.join(D, "B_sess.csv")); M = pd.read_csv(os.path.join(R, "replicates_metrics.csv"))
cov = {"M3": 98.66, "Best Graphene": 94.61, "LG MONO": 100.0}

fig = plt.figure(figsize=(15, 9.5))
a = fig.add_axes([0.08, 0.57, 0.38, 0.31]); b = fig.add_axes([0.59, 0.57, 0.39, 0.31])
c = fig.add_axes([0.08, 0.08, 0.38, 0.31]); d = fig.add_axes([0.59, 0.08, 0.39, 0.31])
data = [dens[(dens.dataset == k) & (dens.module == TM[k])].neighbours_0p5m.values for k in DS]
vp = a.violinplot(data, showextrema=False, widths=0.8)
for body, k in zip(vp["bodies"], DS): body.set_facecolor(COL[k]); body.set_alpha(0.25)
for i, (k, v) in enumerate(zip(DS, data)):
    q = np.percentile(v, [25, 50, 75]); a.add_patch(plt.Rectangle((i + 0.9, q[0]), 0.2, q[2] - q[0], color=COL[k]))
    a.hlines(q[1], i + 0.9, i + 1.1, color="white", lw=2); a.text(i + 1.15, q[1], f"{q[1]:.0f}", va="center")
a.axhspan(26.5, 160, color="#eee", zorder=0); a.text(0.55, 150, "high-density band (> 26)", fontsize=9, color="#666")
a.set_xticks([1, 2, 3], [f"{LAB[k]}\n{TM[k]}, n = {len(v)}" for k, v in zip(DS, data)]); a.set_ylim(0, 160)
a.set_ylabel("elements within 0.5 m (same module)"); a.grid(axis="y", color="#ddd")
mf.frame(a, "A", "Selection density of the task modules (measured from geometry)")
w = 0.34
for i, k in enumerate(DS):
    for j, (col, nm) in enumerate((("top1_correct", "top1"), ("correct", "after"))):
        g = xa[xa.dataset == k]; p, lo, hi = wilson(int(g[col].sum()), len(g))
        xx = i + (j - 0.5) * w
        b.bar(xx, 100 * p, w * 0.9, color=COL[k], alpha=0.45 if j == 0 else 0.95)
        b.errorbar(xx, 100 * p, yerr=[[100 * (p - lo)], [100 * (hi - p)]], color="#222", capsize=3, lw=1)
        q = M[f"{k}|{nm}"].quantile([0.025, 0.975]).values
        b.plot([xx + 0.13, xx + 0.13], q, color="#888", lw=3, alpha=0.6, solid_capstyle="butt")
        b.text(xx, 100 * hi + 1.0, f"{100 * p:.1f}", ha="center", fontsize=10)
b.axhline(90, ls="--", color="#555", lw=1); b.text(2.62, 62, "dashed line: C1 threshold 90%", ha="right", va="bottom", fontsize=9, color="#555",
       bbox=dict(fc="white", ec="none", pad=1))
b.set_xticks(range(3), [LAB[k] for k in DS]); b.set_ylim(60, 104); b.set_ylabel("accuracy (%) of 150 selections")
b.grid(axis="y", color="#ddd")
b.legend(handles=[Patch(color="#999", alpha=0.45, label="top-1"), Patch(color="#555", label="after n-best disambiguation"),
                  Patch(color="#888", alpha=0.6, label="2.5–97.5% over 200 replications")])
mf.frame(b, "B", "Grounding accuracy by dataset (simulated session)")
for i, k in enumerate(DS):
    comp = sess[sess.dataset == k].completeness
    c.bar(i - 0.18, cov[k], 0.34, color=COL[k], alpha=0.95); c.text(i - 0.18, cov[k] + 0.4, f"{cov[k]:.1f}", ha="center", fontsize=10)
    c.bar(i + 0.18, comp.mean(), 0.34, color="white", ec=COL[k], hatch="///", lw=1.5)
    lo, hi = np.percentile(comp, [25, 75]); c.vlines(i + 0.18, lo, hi, color="#222")
    c.text(i + 0.18, max(hi, comp.mean()) + 0.4, f"{comp.mean():.1f}", ha="center", fontsize=10)
c.axhline(90, ls="--", color="#555", lw=1)
c.set_xticks(range(3), [LAB[k] for k in DS]); c.set_ylim(80, 102); c.set_ylabel("%"); c.grid(axis="y", color="#ddd")
c.legend(handles=[Patch(color="#777", label="binding coverage, deployed (measured)"),
                  Patch(fc="white", ec="#777", hatch="///", label="evidence completeness (simulated; IQR)")])
mf.frame(c, "C", "Binding coverage and evidence completeness")
lat = [x[x.dataset == k].groupby(["pid", "step"]).latency_s.sum().values for k in DS]
bp = d.boxplot(lat, widths=0.45, patch_artist=True, showfliers=False, whis=(2.5, 97.5), medianprops=dict(color="white", lw=2))
for patch, k in zip(bp["boxes"], DS): patch.set_facecolor(COL[k]); patch.set_edgecolor(COL[k])
for i, (k, v) in enumerate(zip(DS, lat)):
    cand = xa[(xa.dataset == k) & (xa.popup == 1)].candidates.mean()
    d.text(i + 1, np.percentile(v, 97.5) + 0.35, "median %.2f s\n%.1f candidates / popup" % (np.median(v), cand),
           ha="center", va="bottom", fontsize=9)
d.set_xticks([1, 2, 3], [LAB[k] for k in DS]); d.set_xlim(0.5, 3.5); d.set_ylim(0, 10.5); d.set_ylabel("hover to confirmation per target (s)"); d.grid(axis="y", color="#ddd")
mf.frame(d, "D", "Interaction cost of disambiguation (simulated session)")
os.makedirs(os.path.join(R, "figures"), exist_ok=True)
fig.savefig(os.path.join(R, "figures", "Figure21_three_datasets.png")); plt.close(fig)
print("ok")
