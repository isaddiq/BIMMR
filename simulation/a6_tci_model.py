"""A6 - Traceability Chain Integrity model: common random numbers + probabilistic
sensitivity analysis (MODEL-BASED; every number here is simulated, none is observed).

Model (Section 3.4.2): for session s, event e, link k
    Y[s,e,k] | u_s ~ Bernoulli(p[s,k]),  logit p[s,k] = beta_k + sigma_k u_s,  u_s ~ N(0,1)
with beta_k chosen so that E[p[s,k]] = lambda_k (Gauss-Hermite quadrature).
Event integrity I = prod_k Y;  session TCI = mean_e I.
Traceability depth = number of consecutive links intact from L1.
Audit resolvability = L2 & L3 & L6 & L7 (identity, metadata, evidence, addressability).

Common random numbers: one set of draws (u_s, the uniforms behind every Y, the timing
normals) is generated once and reused for the main comparison, the ablation, the
sensitivity grid and the elasticities, so the full configuration has a single TCI value.

PSA (plan A6): 1,000 outer draws; lambda_k ~ Beta with the calibrated mean and a 95% range
matching the plausible range; sigma_k ~ U(0, sigma_max); 2,000 sessions per draw (draw-wise
common random numbers across conditions). Reported: 2.5th, 50th, 97.5th percentile of mean TCI.
"""
import csv, json, os, sys, time
import numpy as np
from scipy import optimize, special, stats
from common import style, banner, BLUE, ORANGE, AQUA, INK, INK2, write_md_table
import paths

OUT = paths.OUT_TCI
CFG = json.load(open(os.path.join(paths.CAL, "tci_calibration.json"), encoding="utf-8"))
LINKS = CFG["links"]
S, E = CFG["sessions"], CFG["events_per_session"]
GH_X, GH_W = np.polynomial.hermite_e.hermegauss(60)
GH_W = GH_W / GH_W.sum()
COLORS = {"paper": ORANGE, "tablet": AQUA, "proposed": BLUE}
MODEL_BANNER = "MODEL-BASED (simulated, not observed)"


# ---------------------------------------------------------------------------
def beta_for(lam, sigma):
    """Intercept so that E[expit(beta + sigma u)] = lam."""
    if lam >= 1.0: return np.inf
    if lam <= 0.0: return -np.inf
    if sigma == 0: return special.logit(lam)
    f = lambda b: (GH_W * special.expit(b + sigma * GH_X)).sum() - lam
    return optimize.brentq(f, -40, 40)


def link_probs(lams, sigmas, u):
    """p[s,k] for every session (u: shape S)."""
    p = np.empty((len(u), len(LINKS)))
    for k, (l, sg) in enumerate(zip(lams, sigmas)):
        b = beta_for(l, sg)
        p[:, k] = 1.0 if np.isinf(b) and b > 0 else special.expit(b + sg * u)
    return p


class CRN:
    """The single seed set used everywhere (common random numbers)."""
    def __init__(self, seed, sessions=S):
        r = np.random.default_rng(seed)
        self.u = r.standard_normal(sessions)
        self.v = r.random((sessions, E, len(LINKS)))
        self.z = r.standard_normal((sessions, 2))


def simulate(lams, sigmas, crn):
    p = link_probs(lams, sigmas, crn.u)                      # S x K
    y = crn.v < p[:, None, :]                                # S x E x K
    integ = y.all(axis=2)
    tci = integ.mean(axis=1)
    depth = np.cumprod(y, axis=2).sum(axis=2).mean(axis=1)
    audit = y[:, :, [1, 2, 5, 6]].all(axis=2).mean(axis=1)
    marg = y.mean(axis=(0, 1))
    survival = np.cumprod(y, axis=2).mean(axis=(0, 1))
    return dict(tci=tci, depth=depth, audit=audit, marginals=marg, survival=survival)


def lam_vec(d): return np.array([d[k] for k in LINKS], float)


def calibrate_sigma(lams, target_sd, crn):
    """Common sigma for the condition so that the simulated SD of session TCI matches the
    value reported in Table 15 (the dispersion coefficients were not reported)."""
    f = lambda s: simulate(lams, np.full(len(LINKS), s), crn)["tci"].std(ddof=1) - target_sd
    lo, hi = 0.0, 3.0
    if f(lo) >= 0: return 0.0
    if f(hi) <= 0: return hi
    return optimize.brentq(f, lo, hi, xtol=1e-3)


def hedges_g(a, b):
    na, nb = len(a), len(b)
    sp = np.sqrt(((na - 1) * a.var(ddof=1) + (nb - 1) * b.var(ddof=1)) / (na + nb - 2))
    return (a.mean() - b.mean()) / sp * (1 - 3 / (4 * (na + nb) - 9))


def cliffs_delta(a, b):
    b_sorted = np.sort(b)
    gt = np.searchsorted(b_sorted, a, side="left").sum()
    lt = (len(b) - np.searchsorted(b_sorted, a, side="right")).sum()
    return (gt - lt) / (len(a) * len(b))


def ci95(x):
    m, s = x.mean(), x.std(ddof=1)
    h = stats.t.ppf(0.975, len(x) - 1) * s / np.sqrt(len(x))
    return m - h, m + h


def beta_params(mean, lo, hi):
    """Beta(a, b) with the given mean whose 2.5th/97.5th percentiles best match (lo, hi)
    (least squares over the concentration; the mean is held fixed)."""
    def loss(log_k):
        k = np.exp(log_k)
        a, b = mean * k, (1 - mean) * k
        q = stats.beta.ppf([0.025, 0.975], a, b)
        return (q[0] - lo) ** 2 + (q[1] - hi) ** 2
    r = optimize.minimize_scalar(loss, bounds=(np.log(2.0), np.log(1e6)), method="bounded")
    k = np.exp(r.x)
    return mean * k, (1 - mean) * k


# ---------------------------------------------------------------------------
def main():
    os.makedirs(OUT, exist_ok=True)
    t0 = time.time()
    crn = CRN(CFG["seed"])
    conds = CFG["conditions"]

    # --- sigma calibration and main comparison (common seeds) --------------------
    sigma, res = {}, {}
    for c, d in conds.items():
        lam = lam_vec(d["lambda"])
        sigma[c] = calibrate_sigma(lam, d["target_session_sd"], crn)
        res[c] = simulate(lam, np.full(len(LINKS), sigma[c]), crn)
        t = d["time"]
        on = np.maximum(t["on_floor_mean"] + t["on_floor_sd"] * crn.z[:, 0], 0)
        rec = np.maximum(t["record_mean"] + t["record_sd"] * crn.z[:, 1], 0)
        res[c].update(on_floor=on, record=rec, total=on + rec)

    labels = {c: d["label"] for c, d in conds.items()}
    order = ["paper", "tablet", "proposed"]
    t15 = []
    def row(name, fn): t15.append([name] + [fn(c) for c in order])
    row("TCI, mean (SD)", lambda c: f"{res[c]['tci'].mean():.3f} ({res[c]['tci'].std(ddof=1):.3f})")
    row("TCI, 95% CI (Monte Carlo)", lambda c: "{:.3f} - {:.3f}".format(*ci95(res[c]["tci"])))
    row("Independence lower bound, prod lambda", lambda c: f"{np.prod(lam_vec(conds[c]['lambda'])):.3f}")
    row("Dependence upper bound, min lambda", lambda c: f"{lam_vec(conds[c]['lambda']).min():.3f}")
    row("Bracket width", lambda c: f"{lam_vec(conds[c]['lambda']).min() - np.prod(lam_vec(conds[c]['lambda'])):.3f}")
    row("Weakest link", lambda c: LINKS[int(np.argmin(lam_vec(conds[c]['lambda'])))])
    row("Traceability depth, mean (SD), of 7", lambda c: f"{res[c]['depth'].mean():.2f} ({res[c]['depth'].std(ddof=1):.2f})")
    row("Audit resolvability, mean (SD)", lambda c: f"{res[c]['audit'].mean():.3f} ({res[c]['audit'].std(ddof=1):.3f})")
    row("On-floor inspection time, mean (s)", lambda c: f"{res[c]['on_floor'].mean():.1f}")
    row("Record-completion time, mean (s)", lambda c: f"{res[c]['record'].mean():.1f}")
    row("Time to a traceable record, mean (SD) (s)", lambda c: f"{res[c]['total'].mean():.1f} ({res[c]['total'].std(ddof=1):.1f})")
    row("Hedges' g vs proposed, TCI", lambda c: "-" if c == "proposed" else f"{hedges_g(res['proposed']['tci'], res[c]['tci']):.2f}")
    row("Cliff's delta vs proposed, TCI", lambda c: "-" if c == "proposed" else f"{cliffs_delta(res['proposed']['tci'], res[c]['tci']):.2f}")
    row("Dispersion sigma (calibrated to Table 15 SD)", lambda c: f"{sigma[c]:.2f}")
    write_md_table(os.path.join(OUT, "Table15_rerun_common_seeds.md"), ["Measure"] + [labels[c] for c in order], t15,
                   title="Table 15 (re-run with common random numbers). Model-based scenario comparison; simulated, not observed.",
                   note=f"2,000 sessions x 10 events per condition; seed {CFG['seed']} shared by every run. Baseline lambdas and timing inputs reconstructed (tci_calibration.json).")
    with open(os.path.join(OUT, "Table15_rerun_common_seeds.csv"), "w", newline="", encoding="utf-8") as f:
        csv.writer(f).writerows([["measure"] + order] + t15)

    # --- ablation (same seeds; full system identical to the main run) ------------
    full_lam = lam_vec(conds["proposed"]["lambda"])
    sp = np.full(len(LINKS), sigma["proposed"])
    full = res["proposed"]
    t16 = [["Full system", f"{full['tci'].mean():.3f} ({full['tci'].std(ddof=1):.3f})", "-", "-",
            f"{full['depth'].mean():.2f}", f"{full['audit'].mean():.3f}", LINKS[int(np.argmin(full_lam))]]]
    abl = {}
    for key, a in CFG["ablations"].items():
        lam = full_lam.copy()
        for k, v in a["lambda"].items(): lam[LINKS.index(k)] = v
        r = simulate(lam, sp, crn); abl[key] = (lam, r)
        d = r["tci"].mean() - full["tci"].mean()
        t16.append([a["label"], f"{r['tci'].mean():.3f} ({r['tci'].std(ddof=1):.3f})", f"{d:+.3f}",
                    f"{100 * d / full['tci'].mean():+.1f}%", f"{r['depth'].mean():.2f}",
                    f"{r['audit'].mean():.3f}", LINKS[int(np.argmin(lam))]])
    write_md_table(os.path.join(OUT, "Table16_ablation_common_seeds.md"),
                   ["Configuration", "TCI (SD)", "dTCI", "Relative change", "Traceability depth", "Audit resolvability", "Weakest link"],
                   t16, title="Table 16 (re-run, common random numbers). Ablation of design mechanisms; model-based, not an experimental ablation.",
                   note="The full configuration now has the same TCI as in Table 15 because both use the same seed set.")
    with open(os.path.join(OUT, "Table16_ablation_common_seeds.csv"), "w", newline="", encoding="utf-8") as f:
        csv.writer(f).writerows([["configuration", "tci_sd", "delta", "relative", "depth", "audit", "weakest"]] + t16)

    # --- sensitivity grid and elasticities (same seeds) --------------------------
    g = CFG["sensitivity_grid"]
    l1s, l4s = np.linspace(*g["L1"][:2], g["L1"][2]), np.linspace(*g["L4"][:2], g["L4"][2])
    grid = np.zeros((len(l4s), len(l1s)))
    for i, l4 in enumerate(l4s):
        for j, l1 in enumerate(l1s):
            lam = full_lam.copy(); lam[0], lam[3] = l1, l4
            grid[i, j] = simulate(lam, sp, crn)["tci"].mean()
    op = full["tci"].mean()
    n_ge90 = int((grid >= 0.90).sum())
    with open(os.path.join(OUT, "sensitivity_grid_L1_L4.csv"), "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f); w.writerow(["L4 \\ L1"] + [f"{x:.3f}" for x in l1s])
        for i, l4 in enumerate(l4s): w.writerow([f"{l4:.2f}"] + [f"{v:.4f}" for v in grid[i]])
    elast = []
    for k in range(len(LINKS)):
        lam = full_lam.copy(); lam[k] = max(lam[k] - 0.05, 0)
        elast.append(op - simulate(lam, sp, crn)["tci"].mean())

    # --- PSA ---------------------------------------------------------------------
    P = CFG["psa"]; rng = np.random.default_rng(P["seed"]); D = P["outer_draws"]
    psa = {c: np.zeros(D) for c in order}
    beta_par = {}
    for c in order:
        for k in LINKS:
            rg = conds[c]["range"][k]
            if rg is not None:
                beta_par[(c, k)] = beta_params(conds[c]["lambda"][k], *rg)
    wins_vs_tablet = 0
    for d in range(D):
        u = rng.standard_normal(S)                 # draw-wise common random numbers
        ev = rng.random((S, E))
        sg = rng.uniform(0, P["sigma_max"], len(LINKS))
        lam_draw = {}
        for c in order:
            lam = np.array([rng.beta(*beta_par[(c, k)]) if (c, k) in beta_par else conds[c]["lambda"][k] for k in LINKS])
            lam_draw[c] = lam
            pe = link_probs(lam, sg, u).prod(axis=1)            # event integrity prob per session
            psa[c][d] = (ev < pe[:, None]).mean()
        wins_vs_tablet += psa["proposed"][d] > psa["tablet"][d]
    psa_rows = []
    for c in order:
        q = np.percentile(psa[c], [2.5, 50, 97.5])
        psa_rows.append([labels[c], f"{res[c]['tci'].mean():.3f}", f"{q[1]:.3f}", f"{q[0]:.3f} to {q[2]:.3f}"])
    write_md_table(os.path.join(OUT, "Table15b_parameter_uncertainty.md"),
                   ["Condition", "TCI, fixed parameters (common seeds)", "Median of PSA draws", "95% parameter-uncertainty interval"],
                   psa_rows, title="TCI parameter uncertainty (probabilistic sensitivity analysis; model-based, not observed).",
                   note=f"{D} outer draws; lambda_k ~ Beta (calibrated mean, 95% range as in tci_calibration.json); sigma_k ~ U(0, {P['sigma_max']}); "
                        f"{S} sessions x {E} events per draw. Proposed > tablet in {wins_vs_tablet}/{D} draws "
                        f"({100 * wins_vs_tablet / D:.1f}%).")
    with open(os.path.join(OUT, "psa_draws.csv"), "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f); w.writerow(["draw"] + order)
        for d in range(D): w.writerow([d] + [f"{psa[c][d]:.4f}" for c in order])

    # --- prospective power (Table 17), same model --------------------------------
    pw = CFG["power"]; prng = np.random.default_rng(pw["seed"])
    contrasts = [("Proposed system vs paper-based", (full_lam, sp), (lam_vec(conds["paper"]["lambda"]), np.full(7, sigma["paper"]))),
                 ("Proposed system vs BIM-on-tablet", (full_lam, sp), (lam_vec(conds["tablet"]["lambda"]), np.full(7, sigma["tablet"])))]
    for key, (lam, r) in abl.items():
        contrasts.append((f"Full vs {CFG['ablations'][key]['label']}", (full_lam, sp), (lam, sp)))
    t17 = []
    pcurves = {}
    for name, (la, sa), (lb, sb) in contrasts:
        eff = None
        curve = []
        for n in pw["n_grid"]:
            R = pw["replicates"]
            ua, ub = prng.standard_normal((R, n)), prng.standard_normal((R, n))
            pa = link_probs(la, sa, ua.ravel()).prod(axis=1).reshape(R, n)
            pb = link_probs(lb, sb, ub.ravel()).prod(axis=1).reshape(R, n)
            ta, tb = prng.binomial(E, pa) / E, prng.binomial(E, pb) / E
            diff = ta - tb
            if eff is None: eff = diff.mean()
            with np.errstate(all="ignore"):
                p = stats.wilcoxon(diff, axis=1, zero_method="wilcox", nan_policy="omit").pvalue
            power = np.mean(np.nan_to_num(p, nan=1.0) < pw["alpha"])
            curve.append((n, power))
            if power >= 0.95 and n >= 20: break
        pcurves[name] = curve
        n80 = next((n for n, pv in curve if pv >= 0.80), ">" + str(pw["n_grid"][-1]))
        n90 = next((n for n, pv in curve if pv >= 0.90), ">" + str(pw["n_grid"][-1]))
        sign_eff = eff if "vs paper" in name or "vs BIM" in name else -eff
        t17.append([name, f"{sign_eff:+.3f}", n80, n90])
    write_md_table(os.path.join(OUT, "Table17_power_rerun.md"), ["Contrast", "Effect on TCI", "n for 80% power", "n for 90% power"], t17,
                   title="Table 17 (re-run with the common-seed model). Prospective sample sizes for a within-subject trial; model-based, exploratory.",
                   note=f"Two-sided Wilcoxon signed-rank test, alpha = {pw['alpha']}, {pw['replicates']} replicates per n, 10 events per participant and condition, "
                        "participant latent factors independent between conditions (conservative). n searched on the grid " + str(pw["n_grid"]) + ".")

    # --- summary json --------------------------------------------------------------
    summary = dict(
        seed=CFG["seed"], sigma=sigma,
        tci={c: round(float(res[c]["tci"].mean()), 4) for c in order},
        full_system_tci_main=round(float(full["tci"].mean()), 4),
        full_system_tci_ablation=round(float(full["tci"].mean()), 4),
        full_system_tci_grid_operating_point=round(float(op), 4),
        grid=dict(min=round(float(grid.min()), 3), max=round(float(grid.max()), 3), combos=int(grid.size), combos_ge_090=n_ge90,
                  both_links_1=round(float(grid[-1, -1]), 3)),
        elasticity_minus_005={LINKS[k]: round(float(e), 4) for k, e in enumerate(elast)},
        psa={c: [round(float(x), 3) for x in np.percentile(psa[c], [2.5, 50, 97.5])] for c in order},
        psa_proposed_gt_tablet=f"{wins_vs_tablet}/{D}",
        runtime_s=round(time.time() - t0, 1))
    json.dump(summary, open(os.path.join(OUT, "tci_summary.json"), "w"), indent=2)
    print(json.dumps(summary, indent=2))

    # --- figures -------------------------------------------------------------------
    plt = style()
    fig, ax = plt.subplots(figsize=(5.4, 2.6))
    for i, c in enumerate(order):
        q = np.percentile(psa[c], [2.5, 25, 50, 75, 97.5])
        ax.plot([q[0], q[4]], [i, i], color=COLORS[c], lw=2, solid_capstyle="round")
        ax.plot([q[1], q[3]], [i, i], color=COLORS[c], lw=6, alpha=0.45, solid_capstyle="butt")
        ax.plot(q[2], i, "o", color=COLORS[c], ms=7, mec="white", mew=1.5, zorder=3, label="PSA median" if i == 0 else None)
        ax.plot(res[c]["tci"].mean(), i, "D", color=INK, ms=5, zorder=4, label="Fixed parameters" if i == 0 else None)
        ax.text(q[4] + 0.015, i, f"{q[0]:.2f}-{q[4]:.2f}", va="center", fontsize=8, color=INK2)
    ax.set_yticks(range(3), [labels[c] for c in order]); ax.set_xlim(0, 1.08)
    ax.set_xlabel("Mean TCI (thin: 95% parameter-uncertainty interval; thick: IQR)")
    ax.grid(axis="y", visible=False); ax.legend(loc="lower right", fontsize=7)
    ax.set_title("TCI under parameter uncertainty (model-based)", loc="left", fontsize=10)
    banner(fig, MODEL_BANNER, diagonal=False); fig.savefig(os.path.join(OUT, "Figure_TCI_parameter_uncertainty.png")); plt.close(fig)

    fig, ax = plt.subplots(figsize=(5.4, 3.0))
    for name, curve in pcurves.items():
        n, pv = zip(*curve); ax.plot(n, pv, lw=2, marker="o", ms=3, label=name)
    ax.axhline(0.8, ls="--", lw=1, color=INK2); ax.set_ylim(0, 1.02)
    ax.set_xlabel("Participants (within-subject)"); ax.set_ylabel("Power")
    ax.legend(fontsize=6.5, loc="lower right"); ax.set_title("Prospective power (model-based)", loc="left", fontsize=10)
    banner(fig, MODEL_BANNER, diagonal=False); fig.savefig(os.path.join(OUT, "Figure23C_power_rerun.png")); plt.close(fig)
    return summary


if __name__ == "__main__":
    main()
