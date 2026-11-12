# BIM-MR conversational LLM benchmark (v2)

Replacement for `LLMEvaluationMode.cs`. Add **`BimBenchmarkRunner`** to any object in the scene,
press Play, and the whole campaign runs unattended: questions are asked, answers are recorded, every
answer is scored against ground truth computed from the loaded BIM model, and the run stops before
the USD budget is reached.

---

## 1. Running it

1. Load the scene with the BIM model (elements must carry `Metadata` components).
2. Add `BimBenchmarkRunner` to a GameObject (an empty `LLM_Benchmark` object is fine).
3. API keys: leave the three fields blank to reuse the keys already set on `AECGPTVoiceAssistant`,
   or fall back to `OPENAI_API_KEY` / `ANTHROPIC_API_KEY` / `GOOGLE_API_KEY`.
4. Press Play. `Auto Start On Play` is on by default; the run begins after `Start Delay Seconds`
   (3 s) so metadata import can finish.

Right-click the component header for **Estimate cost only** (prints the projection, calls no API) and
**Start benchmark** (manual start).

The legacy `LLMEvaluationMode` component is disabled automatically at start so the two evaluators
cannot run against the same keys at once.

### Budget

`Max Budget Usd` (default **30**) is a hard cap, enforced three times:

* before the run, each model's projected cost is computed and models that do not fit are **skipped**
  (roster is ordered cheapest-first, so breadth is preserved);
* before every call, the next call's projected cost is checked against the remaining budget;
* after every call, the **actual** cost from the provider's usage block is recorded.

Costs are computed from real `usage` token counts, not character estimates. GPT-4 and Claude Opus 4.1
are the two models most likely to be dropped by the governor - that is intended: they cost 10-30x the
median model per call. Disable models in the inspector list to steer the budget deliberately.

**Fitting all 13 models into $30.** 60 items x 3 runs x 13 models does not fit; the two most
expensive models will be skipped. Either:

* run in two passes - pass 1 with `Include Hard Reasoning` off (Core-30 + R1-R8, all 13 models),
  pass 2 with only the hard block enabled on the 6-8 models you want to compare in depth; or
* set `Runs Per Item` to 2 for the full suite and report the reduced repetition honestly.

Run **Estimate cost only** first: it prints the per-model projection and the include/skip decision
without spending anything.

---

## 2. Question suite (60 items)

| Block | Items | Content |
|---|---|---|
| Core-30 | Q1-Q30 | verbatim from Table 4 of the manuscript |
| Robustness | R1-R8 | verbatim from Table 5 - **executed**, no longer "future work" |
| Hard reasoning | H1-H22 | new families added for the review |

Hard families: spatial adjacency / vertical relations / proximity / clash / orientation /
containment / relative position / closest pair (H1-H8), cross-discipline coordination (H9-H12),
ambiguous, colloquial and underspecified language (H13-H16), semantic interpretation - unit
conversion, code thresholds, metadata completeness, null semantics (H17-H20), and multi-turn recall
(H21-H22).

Prompts containing `{ANCHOR:Category}` are instantiated at run time with a real element ID from the
loaded model. If the scene has no such element the item is skipped and listed in `ground_truth.md`
rather than being asked with a broken prompt.

---

## 3. Ground truth

`GroundTruthEngine` computes the expected answer for every item **from the scene that is loaded**,
using `BimSceneIndex` (properties, categories, disciplines, levels, modules, and world-space bounds).
Spatial answers come from Unity geometry; the CSV export does not carry IFC relationships.

Consequences worth knowing:

* If the metadata a question needs is absent, the expected behaviour becomes **abstain with a
  reason**, and a confident answer is scored as a failure.
* If a category is absent, the expected answer is a **zero/none statement**, not a refusal.
* Everything is written to `ground_truth.md` / `ground_truth.json` before any API call, so the
  expected answers can be audited independently of model behaviour.

---

## 4. Metrics

Per response: `format_adherence` (strict JSON with all required keys and types), `entity_f1`,
`grounding_overlap` (Jaccard over element IDs), `numeric_rel_error` + `numeric_at1` / `numeric_at5`,
`group_accuracy`, `coverage` (element-property-value triples), `id_hallucination_rate` and
`invented_ids` (IDs not present in the scene), `abstained`, `safe_refusal`, `over_abstention`,
`memory_consistency`, `task_success`, `task_score_0_100`, plus latency, tokens, tokens/s and cost.

Two rules that the previous evaluator violated:

* **A metric that does not apply to an item is written as empty (NaN) and excluded from averages.**
  Nothing is credited 1.0 because ground truth was missing.
* **Empty expected ID set is not a free pass**: citing IDs when none are expected scores 0.

`task_success` is a strict conjunction: schema valid AND no invented IDs AND not abstained AND every
applicable content gate passed (entity F1 >= 0.8, grounding >= 0.7, numeric within 1%, group accuracy
>= 0.8, coverage >= 0.8). For abstention items it is simply a clean refusal with no cited IDs. For
memory items it is self-consistency with the model's own earlier number.

---

## 5. Outputs

`%USERPROFILE%/AppData/LocalLow/<company>/<product>/bim_benchmark/<timestamp>/`

| File | Contents |
|---|---|
| `runs.csv` | one row per model x item x run - the analysis input |
| `responses.jsonl` | full prompt, full answer, expected answer, parsed IDs, cost, latency |
| `ground_truth.md` / `.json` | expected behaviour per item, plus items skipped for this scene |
| `scene_profile.json` | element counts, categories, disciplines, property availability, scene hash |
| `summary.md` | per-model task success, latency P95, spend |

Analyse with `Analysis/analyze_runs.py` (outside `Assets/`).

---

## 6. Other BIM models / modules

Nothing is hard-coded to one scene. Load a different model (or a different module configuration),
press Play, and the index, ground truth, anchors and context packets are all recomputed. The scene
hash and profile are recorded so runs can be compared across models. For the multi-scene experiment,
run the same suite on each scene and concatenate the CSVs - `scene_profile.json` identifies which is
which.

---

## 7. What this does and does not address

Addressed from the review: robustness probes executed rather than proposed; spatial/topological,
cross-discipline, ambiguous-language and semantic-interpretation tasks; noisy/incomplete metadata via
the seeded degradation pass; real deterministic scoring; scene portability; honest cost accounting.

**Not** addressed here: the methodological contribution itself (plan-execute-verify grounding,
deictic/viewpoint resolution), multi-scene execution (the harness supports it - the scenes still have
to be loaded and run), and the in-situ MR user study. Those are in `LLM_Paper_Rev2_Plan.md`.

---

## 8. Files

| File | Role |
|---|---|
| `BimSceneIndex.cs` | scene -> queryable element index, classification, spatial predicates |
| `BenchmarkSuite.cs` | the 60 items and their specifications |
| `GroundTruthEngine.cs` | computes expected answers from the index |
| `ContextPacketBuilder.cs` | prompt serialization + `PerturbationEngine` (noise operators) |
| `ResponseScorer.cs` | deterministic scoring of a response |
| `LlmClient.cs` | provider calls, usage parsing, retries, `ModelSpec` roster, `CostGovernor` |
| `BimBenchmarkRunner.cs` | MonoBehaviour orchestrator, auto-start, CSV/JSONL output |
