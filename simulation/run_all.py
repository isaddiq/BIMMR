"""Regenerate every model-based output of the paper from this repository.

    python simulation/run_all.py                    # public pipeline (200 replicates)
    python simulation/run_all.py --replicates 20    # quick check with fewer replicates
    python simulation/run_all.py --with-raw-data    # authors only: first recompute the binding-
                                                    # coverage and geometry-density analyses from the
                                                    # proprietary exports (needs BIMMR_ASSETS)

Public pipeline (inputs: calibration/ only; single seed set, see README):
  1. a6_tci_model.py          TCI model: Table 15, 15a, 16, 17, sensitivity grid, PSA  -> outputs/tci_model
  2. r3_model_figures.py      model-based TCI figures (Section 4.3)                     -> outputs/tci_model
  3. r3_three_building_sim.py follow-up session, three datasets, 200 replicates        -> outputs/followup_simulation
  4. r3_tables.py             Tables 10, 10a, 11, 14a, 14b, A5, A6 (Sections 4.2.2-4.2.4) -> outputs/followup_simulation/tables
  5. r3_figure21_datasets.py  three-dataset figure (Section 4.2.4)                     -> outputs/followup_simulation/figures

Each step's console output is written to a run log next to its outputs.
"""
import argparse, os, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import paths


def run(script, log=None, args=()):
    print(">>", script, *args, flush=True)
    env = dict(os.environ, PYTHONIOENCODING="utf-8", PYTHONHASHSEED="0", MPLBACKEND="Agg")
    p = subprocess.run([sys.executable, os.path.join(HERE, script), *args], cwd=os.path.dirname(os.path.join(HERE, script)),
                       env=env, capture_output=True, text=True, encoding="utf-8")
    sys.stdout.write(p.stdout[-2000:]); sys.stderr.write(p.stderr)
    if log:
        os.makedirs(os.path.dirname(log), exist_ok=True)
        with open(log, "w", encoding="utf-8", newline="\n") as f: f.write(p.stdout)
    if p.returncode: raise SystemExit(f"{script} failed (exit {p.returncode})")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--replicates", type=int, default=None, help="follow-up simulation replicates (default: calibration file, 200)")
    ap.add_argument("--with-raw-data", action="store_true", help="recompute the derived inputs from the proprietary exports first")
    a = ap.parse_args()
    if a.with_raw_data:
        paths.assets()                                     # fails early with a clear message if BIMMR_ASSETS is missing
        for s in ("geometry_density.py", "a5_binding_coverage.py", "a5b_module_binding.py", "lg_binding.py", "prepare_public_inputs.py"):
            run(os.path.join("raw_analyses", s))
    run("a6_tci_model.py", os.path.join(paths.OUT_TCI, "run_log.txt"))
    run("r3_model_figures.py")
    run("r3_three_building_sim.py", os.path.join(paths.OUT_FOLLOWUP, "run_log.txt"), [str(a.replicates)] if a.replicates else [])
    run("r3_tables.py", os.path.join(paths.OUT_FOLLOWUP, "tables_log.txt"))
    run("r3_figure21_datasets.py")
    print("done; outputs in", os.path.relpath(paths.OUT, paths.ROOT))


if __name__ == "__main__":
    main()
