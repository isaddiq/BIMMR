"""Repository paths shared by every script.

Public pipeline (runs from this repository alone):
    calibration/                 calibration values and derived, non-identifying inputs
    outputs/                     everything the public pipeline writes

Restricted pipeline (authors only; needs the proprietary BIM exports, see raw_analyses/):
    BIMMR_ASSETS                 environment variable: path to the Unity 'Assets' folder that
                                 holds the M3, Best Graphene and LG MONO exports and module prefabs
    restricted/                  untracked folder for element-level results with real identifiers
"""
import os

SIM = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(SIM)
CAL = os.path.join(ROOT, "calibration")
DERIVED = os.path.join(CAL, "derived_inputs")
OUT = os.path.join(ROOT, "outputs")
OUT_TCI = os.path.join(OUT, "tci_model")                  # Section 4.3 (and 3.4.2)
OUT_FOLLOWUP = os.path.join(OUT, "followup_simulation")   # Section 4.2.4 (and 3.3.3, 4.2.2)
RESTRICTED = os.environ.get("BIMMR_RESTRICTED_DIR", os.path.join(ROOT, "restricted"))


def assets():
    """Unity Assets folder with the proprietary BIM exports (restricted pipeline only)."""
    p = os.environ.get("BIMMR_ASSETS")
    if not p or not os.path.isdir(p):
        raise SystemExit("Set BIMMR_ASSETS to the Unity Assets folder that contains the proprietary "
                         "BIM exports. These data are not public; see README.md.")
    return os.path.abspath(p)
