"""Shared statistics and figure style for the revision analyses."""
import os
import numpy as np
from scipy import stats

# ---- figure style (reference palette, slots 1-3 validate all-pairs) -------------
BLUE, ORANGE, AQUA = "#2a78d6", "#eb6834", "#1baf7a"
INK, INK2, GRID = "#0b0b0b", "#52514e", "#d9d8d4"
SYNTH_BANNER = "SYNTHETIC DRY-RUN DATA - NOT A RESULT"


def style():
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    plt.rcParams.update({
        "font.family": "DejaVu Sans", "font.size": 9, "axes.edgecolor": INK2,
        "axes.labelcolor": INK, "xtick.color": INK2, "ytick.color": INK2,
        "axes.spines.top": False, "axes.spines.right": False, "axes.grid": True,
        "grid.color": GRID, "grid.linewidth": 0.6, "axes.axisbelow": True,
        "savefig.dpi": 300, "savefig.bbox": "tight", "figure.facecolor": "white",
        "legend.frameon": False,
    })
    return plt


def banner(fig, text=SYNTH_BANNER, diagonal=True):
    """Label for every figure built from simulated data. Synthetic dry-run figures get a
    diagonal watermark as well; model-based figures only the corner label."""
    if diagonal:
        fig.text(0.5, 0.5, text, ha="center", va="center", rotation=25, fontsize=20,
                 color="#e34948", alpha=0.18, weight="bold", zorder=100)
    fig.text(0.01, -0.03, text, ha="left", va="top", fontsize=7, color="#e34948", weight="bold")


# ---- intervals ----------------------------------------------------------------
def t_ci(x, level=0.95):
    x = np.asarray(x, float)
    n = len(x); m = x.mean(); sd = x.std(ddof=1) if n > 1 else 0.0
    h = stats.t.ppf(0.5 + level / 2, n - 1) * sd / np.sqrt(n) if n > 1 else np.nan
    return m, sd, m - h, m + h


def bca_ci(x, level=0.95, n_resamples=10000, seed=20261103):
    x = np.asarray(x, float)
    if np.all(x == x[0]):
        return x[0], x[0]
    r = stats.bootstrap((x,), np.mean, n_resamples=n_resamples, confidence_level=level,
                        method="BCa", random_state=np.random.default_rng(seed))
    return r.confidence_interval.low, r.confidence_interval.high


def wilson(k, n, level=0.95):
    if n == 0:
        return np.nan, np.nan, np.nan
    z = stats.norm.ppf(0.5 + level / 2)
    p = k / n
    d = 1 + z * z / n
    c = (p + z * z / (2 * n)) / d
    h = z * np.sqrt(p * (1 - p) / n + z * z / (4 * n * n)) / d
    return p, max(0.0, c - h), min(1.0, c + h)


def _signed_rank_null(n):
    """Exact null distribution of W+ for n untied pairs (counts over 0..n(n+1)/2)."""
    dist = np.zeros(n * (n + 1) // 2 + 1); dist[0] = 1
    for k in range(1, n + 1):
        new = dist.copy(); new[k:] += dist[:-k]; dist = new
    return dist / dist.sum()


def hodges_lehmann_paired(diff, level=0.95):
    """Hodges-Lehmann estimate of the median paired difference with the exact
    Walsh-average confidence interval (Hollander & Wolfe)."""
    d = np.asarray(diff, float)
    n = len(d)
    walsh = np.sort([(d[i] + d[j]) / 2 for i in range(n) for j in range(i, n)])
    est = np.median(walsh)
    cdf = np.cumsum(_signed_rank_null(n))
    alpha = 1 - level
    c = int(np.searchsorted(cdf, alpha / 2, side="right"))  # P(W+ <= c-1) <= alpha/2
    c = max(c, 1)
    lo, hi = walsh[c - 1], walsh[len(walsh) - c]
    return est, lo, hi


def wilcoxon_paired(a, b):
    """Two-sided Wilcoxon signed-rank p (zero differences dropped; exact when possible)
    and matched-pairs rank-biserial r = (W+ - W-)/(W+ + W-) for b - a."""
    d = np.asarray(b, float) - np.asarray(a, float)
    nz = d[d != 0]
    if len(nz) == 0:
        return 1.0, 0.0
    ranks = stats.rankdata(np.abs(nz))
    wp, wm = ranks[nz > 0].sum(), ranks[nz < 0].sum()
    p = stats.wilcoxon(nz, zero_method="wilcox", alternative="two-sided").pvalue
    return float(p), float((wp - wm) / (wp + wm))


def cohen_kappa(r1, r2):
    r1, r2 = np.asarray(r1), np.asarray(r2)
    cats = np.union1d(r1, r2)
    po = np.mean(r1 == r2)
    pe = sum(np.mean(r1 == c) * np.mean(r2 == c) for c in cats)
    return (po - pe) / (1 - pe) if pe < 1 else 1.0


def median_iqr(x):
    x = np.asarray(x, float)
    q1, m, q3 = np.percentile(x, [25, 50, 75])
    return m, q1, q3


def write_md_table(path, header, rows, title=None, note=None):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        if title: f.write(f"**{title}**\n\n")
        f.write("| " + " | ".join(header) + " |\n")
        f.write("|" + "|".join(["---"] * len(header)) + "|\n")
        for r in rows:
            f.write("| " + " | ".join(str(c) for c in r) + " |\n")
        if note: f.write(f"\n{note}\n")
