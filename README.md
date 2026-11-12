# BIM-MR traceability: simulation code, calibration data and Unity source

This repository contains:

1. **Simulation code**: the traceability chain integrity (TCI) model, a simulated follow-up inspection session over three BIM datasets, and the binding-coverage and geometry-density analyses.
2. **Calibration files** and derived, non-identifying inputs used by the simulations.
3. **Run logs** of the reference run.
4. **Unity C# source** of the BIM-MR inspection system (Meta Quest 3, Unity 6).

## Repository layout

| Path | Contents |
|---|---|
| `simulation/run_all.py` | Runs the whole public pipeline |
| `simulation/a6_tci_model.py` | TCI model: random-intercept link model with common random numbers; scenario comparison (paper-based, BIM-on-tablet, proposed system), ablation, L1 × L4 sensitivity grid, link elasticities, probabilistic sensitivity analysis (PSA), prospective power |
| `simulation/r3_model_figures.py` | Figures of the TCI model |
| `simulation/r3_three_building_sim.py` | Simulated follow-up session: within-subject disambiguation block and cross-dataset block (M3, Best Graphene, LG MONO); 200 replicates and a typical replicate |
| `simulation/r3_tables.py` | Summary tables of the simulated session |
| `simulation/r3_figure21_datasets.py` | Three-dataset comparison figure |
| `simulation/raw_analyses/` | Binding coverage (`a5_binding_coverage.py`, `a5b_module_binding.py`, `lg_binding.py`), geometry density (`geometry_density.py`) and `prepare_public_inputs.py`. These read the proprietary BIM exports; their non-identifying results are in `calibration/derived_inputs/` |
| `calibration/tci_calibration.json` | Per-link marginal integrities λ, plausible ranges for the PSA, dispersion targets, timing distributions; the `paper` and `tablet` blocks are the baseline assumptions |
| `calibration/followup_simulation_parameters.json` | Behaviour and timing assumptions of the follow-up simulation, seed base, number of replicates |
| `calibration/derived_inputs/` | Derived, non-identifying inputs (see below) |
| `outputs/` | Run logs of the reference run; `run_all.py` writes all regenerated results here |
| `unity/Assets/` | C# scripts of the BIM-MR system, BIMUniXchange data-exchange tools, UI prefabs and materials |

### Derived inputs (`calibration/derived_inputs/`)

| File | Contents |
|---|---|
| `elements.csv` | One row per bound element of the three datasets (5,844): pseudonymous id (`M3-00001`, `BG-00001`, `LG-00001`), module, category, bounding box in a local origin per dataset, diagonal, neighbours within 0.5 m, density band, and whether category, type and dimensions are populated in the metadata export. |
| `participants.csv` | 15 participants (pseudonymous P01–P15): session performance values, primary language (en/ko) and mean NASA-TLX rating. |
| `module_binding_by_module.csv`, `module_binding_summary.csv`, `export_binding_summary.csv`, `lg_case.json` | Binding-coverage counts per module and per export. |
| `density_summary.csv`, `dataset_constants.json` | Density statistics per dataset; mean populated properties per element. |

### Outputs (`outputs/`)

| File | Contents |
|---|---|
| `tci_model/tci_summary.json` | Calibrated dispersion per condition, mean TCI per condition, sensitivity-grid range, elasticities, PSA intervals |
| `followup_simulation/simulation_parameters.json` | Calibrated selection difficulty (α) and popup rescue (ρ₀), typical replicate, parameters, number of replicates |
| `followup_simulation/run_log.txt` | Console log of the follow-up simulation run |

`run_all.py` also writes the tables, figures and event-level simulation data into `outputs/`.

## Reproducing the results

Requires Python 3.13.

```bash
python -m venv .venv
.venv/bin/activate          # Windows: .venv\Scripts\activate
pip install -r simulation/requirements.txt
python simulation/run_all.py
```

`run_all.py` regenerates every table, figure and log into `outputs/` from `calibration/` alone. `--replicates N` runs a shorter check; the reference run uses 200.

### Seeds

- TCI model: seed set **20261103** shared by the main comparison, the ablation, the sensitivity grid and the elasticities (common random numbers), so the full configuration has a single value (TCI = 0.871). The PSA uses 20261104 and the power analysis 20261105 (`tci_calibration.json`).
- Follow-up simulation: seed base **20261109**; replicate *r* uses 20261109 + *r* and 20261109 + 1000 + *r*; the typical replicate is 150 (`outputs/followup_simulation/simulation_parameters.json`).

### Restricted pipeline (data owners only)

`python simulation/run_all.py --with-raw-data` first recomputes the binding-coverage and geometry-density analyses and rewrites `calibration/derived_inputs/`. It needs the environment variables `BIMMR_ASSETS` (the Unity `Assets` folder holding the BIM exports and module prefabs) and `BIMMR_SURVEY_WORKBOOK` (study workbook). Element-level results with real identifiers go to the untracked `restricted/` folder.

## Unity project

- **Unity 6000.6.6f1** (Unity 6), Android / Meta Quest 3, Universal Render Pipeline.
- `unity/Assets/` contains the project's own code: `Scripts/BIMInspection` (selection, disambiguation, evidence records, export, study logging), `My Scripts` (LLM assistant, voice, benchmark runner, module tools), `BIMUniXchange` (BIM-to-Unity metadata exchange, IFC importer and semantic enrichment), the UI prefabs in `My Assets/Prefabs`, and `My Materials`.
- Unity generates the `.meta` files on first import; re-link the prefab script references after opening the project.

### Dependencies (install from the Unity Package Manager, NuGet or the vendor)

| SDK / package | Version |
|---|---|
| Meta XR Core SDK (incl. Immersive Debugger), Meta XR Interaction SDK, Meta XR Audio/Acoustics | 76.0.1 |
| XR Interaction Toolkit (Starter Assets, Hands Interaction Demo, AR Starter Assets samples) | 3.1.2 |
| XR Interaction Toolkit XR Device Simulator sample | 3.6.1 |
| XR Hands (HandVisualizer sample) | 1.9.0 |
| OpenXR Plugin, AR Foundation, Google ARCore XR Plugin, XR Simulation | see `Packages/manifest.json` |
| Unity Mixed Reality template assets (`MRTemplateAssets`) | Unity MR template |
| TextMesh Pro essential resources | bundled with Unity |
| Newtonsoft Json (`com.unity.nuget.newtonsoft-json`) | see `Packages/manifest.json` |
| NuGetForUnity, with `DocumentFormat.OpenXml` 3.0.2, `Xbim.Essentials`/`Xbim.Ifc*`/`Xbim.Common` 6.0.563, `Xbim.IO.Esent` 6.0.563, `Xbim.IO.MemoryModel` 6.0.563, `Xbim.IO.Table` 6.0.172, `Xbim.Geometry`/`Xbim.ModelGeometry.Scene`/`Xbim.Geometry.Engine.Interop` 5.1.820, `Microsoft.Database.ManagedEsent` 2.0.3, `Microsoft.Extensions.DependencyInjection` 2.1.1, `Microsoft.Extensions.Logging.Abstractions` 2.1.1, `System.IO.Packaging` 8.0.0 | as listed |
| Python tools of BIMUniXchange | `unity/Assets/BIMUniXchange/**/requirements.txt` |

### API keys

The assistant calls OpenAI (GPT-4o-mini, Whisper, TTS), and optionally Anthropic and Google models. Keys are never stored in scenes, prefabs or code: `ApiKeyProvider` reads, in this order:

1. environment variables `OPENAI_API_KEY`, `ANTHROPIC_API_KEY`, `GOOGLE_API_KEY`;
2. `config.json` in `Application.persistentDataPath` (on the headset: `adb push config.json /sdcard/Android/data/<package>/files/`);
3. in the Editor, `config.json` in the Unity project root.

Copy `unity/config.example.json` to `config.json` and fill in your own keys. `config.json` is ignored by git.

## License

MIT (see `LICENSE`).
