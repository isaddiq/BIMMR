// BimBenchmarkRunner.cs - Auto-starting, budget-capped benchmark for BIM-grounded conversational LLMs.
//
// Drop this component into the scene, press Play, and it will:
//   1. index the loaded BIM model and write a scene profile,
//   2. compute deterministic ground truth for every question from that model,
//   3. ask each enabled model every question N times (recording the full answer),
//   4. score every answer deterministically and stream results to CSV/JSONL,
//   5. stop before the configured USD budget is exceeded.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

namespace AEC.Benchmark
{
    public class BimBenchmarkRunner : MonoBehaviour
    {
        [Header("Execution")]
        [Tooltip("Start the benchmark automatically when the scene is played.")]
        [SerializeField] private bool autoStartOnPlay = true;
        [Tooltip("Seconds to wait after scene load so BIM metadata finishes importing.")]
        [SerializeField] private float startDelaySeconds = 3f;
        [SerializeField] private int runsPerItem = 3;
        [SerializeField] private float delayBetweenCallsSeconds = 0.5f;
        [SerializeField] private float delayBetweenModelsSeconds = 3f;

        [Header("Question suite")]
        [SerializeField] private bool includeCore30 = true;
        [SerializeField] private bool includeRobustnessProbes = true;
        [SerializeField] private bool includeHardReasoning = true;

        [Header("Noisy-metadata pass (optional, costs extra)")]
        [SerializeField] private bool enableDegradationPass = false;
        [SerializeField] private float[] degradationRates = { 0.10f, 0.25f, 0.40f };
        [SerializeField] private int degradationItemCount = 12;
        [SerializeField] private int degradationSeed = 20260730;

        [Header("Budget (hard cap)")]
        [SerializeField] private double maxBudgetUsd = 30.0;
        [Tooltip("Log the projected cost and stop without calling any API.")]
        [SerializeField] private bool estimateOnly = false;

        [Header("Request settings")]
        [SerializeField] private float temperature = 1f;
        [SerializeField] private int maxRetries = 3;
        [SerializeField] private float requestTimeoutSeconds = 90f;
        [SerializeField] private int maxContextRows = 500;

        [Header("Scene interpretation")]
        [Tooltip("World axis treated as project north for orientation questions.")]
        [SerializeField] private Vector3 projectNorthAxis = Vector3.forward;

        [Header("Models (cheapest first; disable any you do not want to pay for)")]
        [SerializeField] private List<ModelSpec> models = new List<ModelSpec>();

        // API keys: read from the voice assistant, then ApiKeyProvider (environment variables or config.json)
        [System.NonSerialized] private string openAiKey = "";      // never serialized: see AEC.VoiceAssistant.ApiKeyProvider
        [System.NonSerialized] private string anthropicKey = "";
        [System.NonSerialized] private string googleKey = "";

        [Header("Output")]
        [SerializeField] private string outputDirectory = "bim_benchmark";

        private const string SystemPromptTemplate =
            "You are a BIM assistant embedded in a mixed-reality inspection system.\n" +
            "Answer ONLY from the BIM scene context provided below. If the answer cannot be determined from that context, " +
            "say so clearly, set \"answerable\" to false, give a short reason_code, and do not speculate.\n" +
            "Respond with a single JSON object and nothing else, using exactly these keys:\n" +
            "{ \"answer\": string, \"entities\": [string], \"element_ids\": [string], \"confidence\": number between 0 and 1, " +
            "\"hallucination_flag\": boolean, \"answerable\": boolean, \"reason_code\": string }\n" +
            "Rules:\n" +
            "- \"answer\" must state the full result, including any counts, sums, ratios or lists.\n" +
            "- \"element_ids\" may contain only identifiers that literally appear in the context.\n" +
            "- Never invent element IDs, property names, or property values.\n" +
            "- A value shown as <undefined> is missing data, not a value.\n" +
            "- Do not wrap the JSON in markdown fences.\n\n";

        private string _sessionId;
        private string _outputPath;
        private BimSceneIndex _index;
        private GroundTruthEngine _truthEngine;
        private ContextPacketBuilder _packetBuilder;
        private CostGovernor _governor;
        private LlmClient _client;
        private StreamWriter _csv;
        private StreamWriter _jsonl;
        private bool _running;

        private readonly List<string> _skippedItems = new List<string>();
        private readonly Dictionary<string, ModelTally> _tallies = new Dictionary<string, ModelTally>();

        private class ModelTally
        {
            public int Calls, Failures, Successes;
            public double Cost, LatencySum;
            public List<double> Latencies = new List<double>();
        }

        // ------------------------------------------------------------ lifecycle

        private void Reset() { models = ModelSpec.DefaultRoster(); }

        private void Awake()
        {
            if (models == null || models.Count == 0) models = ModelSpec.DefaultRoster();
        }

        private void Start()
        {
            if (!autoStartOnPlay) return;
            StartCoroutine(AutoStart());
        }

        private IEnumerator AutoStart()
        {
            yield return new WaitForSeconds(startDelaySeconds);
            StartBenchmark();
        }

        [ContextMenu("Start benchmark")]
        public void StartBenchmark()
        {
            if (_running) { Debug.LogWarning("[Benchmark] Already running."); return; }
            StartCoroutine(RunAll());
        }

        [ContextMenu("Estimate cost only")]
        public void EstimateOnly()
        {
            estimateOnly = true;
            StartBenchmark();
        }

        public bool IsRunning => _running;

        // ------------------------------------------------------------- main loop

        private IEnumerator RunAll()
        {
            _running = true;
            _sessionId = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            _outputPath = Path.Combine(Application.persistentDataPath, outputDirectory, _sessionId);
            Directory.CreateDirectory(_outputPath);

            Debug.Log($"[Benchmark] === session {_sessionId} ===");
            Debug.Log($"[Benchmark] Output: {_outputPath}");

            DisableLegacyEvaluator();

            // 1. Index the scene
            _index = BimSceneIndex.Build(projectNorthAxis);
            _truthEngine = new GroundTruthEngine(_index);
            _packetBuilder = new ContextPacketBuilder(_index, maxContextRows);
            File.WriteAllText(Path.Combine(_outputPath, "scene_profile.json"),
                JsonConvert.SerializeObject(_index.Profile, Formatting.Indented));

            Debug.Log($"[Benchmark] Indexed {_index.Elements.Count} BIM elements, " +
                      $"{_index.PropertyKeys.Count} property keys, hash {_index.Profile.SceneHash}");

            if (_index.Elements.Count == 0)
            {
                Debug.LogError("[Benchmark] No BIM elements with Metadata components found - aborting. " +
                               "Load a BIM model before running the benchmark.");
                _running = false;
                yield break;
            }

            // 2. Build the suite and its ground truth
            var items = BenchmarkSuite.BuildDefault(includeCore30, includeRobustnessProbes, includeHardReasoning);
            var plan = new List<(BenchmarkItem item, GroundTruth gt, ContextPacket packet)>();

            foreach (var item in items)
            {
                var gt = _truthEngine.Evaluate(item);
                if (gt.Skip)
                {
                    _skippedItems.Add($"{item.Id}: {gt.SkipReason}");
                    continue;
                }
                var packet = _packetBuilder.Build(item, gt, null);
                plan.Add((item, gt, packet));
            }

            WriteGroundTruthReport(plan);
            Debug.Log($"[Benchmark] {plan.Count} executable items ({_skippedItems.Count} skipped for this scene)");

            // 3. Budget plan
            _governor = new CostGovernor(maxBudgetUsd);
            var enabled = models.Where(m => m.Enabled).ToList();
            int callsPerModel = plan.Count * Mathf.Max(1, runsPerItem);
            if (enableDegradationPass)
                callsPerModel += Mathf.Min(degradationItemCount, plan.Count) * degradationRates.Length * Mathf.Max(1, runsPerItem);

            int avgPromptTokens = Mathf.Max(200, EstimatePromptTokens(plan));
            const int avgCompletionTokens = 260;

            var affordable = new List<ModelSpec>();
            double projectedTotal = 0;
            Debug.Log($"[Benchmark] Projection: {callsPerModel} calls/model, ~{avgPromptTokens} prompt + {avgCompletionTokens} completion tokens per call");
            foreach (var spec in enabled)
            {
                double projected = _governor.ProjectModelCost(spec, callsPerModel, avgPromptTokens, avgCompletionTokens);
                bool fits = projectedTotal + projected * 1.10 <= maxBudgetUsd;
                Debug.Log($"[Benchmark]   {spec.DisplayName,-22} projected ${projected,7:F3} {(fits ? "-> included" : "-> SKIPPED (budget)")}");
                if (!fits) continue;
                projectedTotal += projected;
                affordable.Add(spec);
            }
            Debug.Log($"[Benchmark] Projected total ${projectedTotal:F2} of ${maxBudgetUsd:F2} budget across {affordable.Count} models");

            if (estimateOnly)
            {
                Debug.Log("[Benchmark] estimateOnly = true, no API calls made.");
                _running = false;
                yield break;
            }

            // 4. Client
            var keys = ResolveApiKeys();
            _client = new LlmClient(keys, temperature, maxRetries, requestTimeoutSeconds);
            affordable = affordable.Where(m =>
            {
                bool ok = _client.HasKey(m.Provider);
                if (!ok) Debug.LogWarning($"[Benchmark] {m.DisplayName} skipped: no {m.Provider} API key");
                return ok;
            }).ToList();

            if (affordable.Count == 0)
            {
                Debug.LogError("[Benchmark] No runnable models (missing keys or budget). Aborting.");
                _running = false;
                yield break;
            }

            OpenWriters();

            // 5. Execute
            var startedAt = DateTime.Now;
            foreach (var spec in affordable)
            {
                if (_governor.Exhausted) { Debug.LogWarning("[Benchmark] Budget exhausted - stopping."); break; }
                yield return RunModel(spec, plan);
                yield return new WaitForSeconds(delayBetweenModelsSeconds);
            }

            CloseWriters();
            WriteSummary(startedAt);

            Debug.Log($"[Benchmark] === complete in {(DateTime.Now - startedAt).TotalMinutes:F1} min, " +
                      $"spent ${_governor.SpentUsd:F3} of ${maxBudgetUsd:F2} ===");
            Debug.Log($"[Benchmark] Results: {_outputPath}");
            _running = false;
        }

        private IEnumerator RunModel(ModelSpec spec, List<(BenchmarkItem item, GroundTruth gt, ContextPacket packet)> plan)
        {
            Debug.Log($"[Benchmark] ---- {spec.DisplayName} ({spec.ModelId}) ----");
            var tally = new ModelTally();
            _tallies[spec.DisplayName] = tally;

            for (int run = 1; run <= Mathf.Max(1, runsPerItem); run++)
            {
                // Fresh dialogue per pass: recall items must recall this pass's own answers.
                var transcript = new Dictionary<string, LlmTurn[]>();
                string previousItemId = null;

                foreach (var (item, gt, packet) in plan)
                {
                    if (_governor.Exhausted) yield break;

                    double singleCallEstimate = spec.CostOf(Mathf.Max(200, packet.Text.Length / 4 + 300), 260);
                    if (!_governor.CanAfford(singleCallEstimate))
                    {
                        Debug.LogWarning($"[Benchmark] Stopping {spec.DisplayName}: next call would exceed the budget.");
                        yield break;
                    }

                    yield return ExecuteOne(spec, item, gt, packet, run, transcript, previousItemId, null, tally);
                    previousItemId = item.Id;
                    yield return new WaitForSeconds(delayBetweenCallsSeconds);
                }

                if (!enableDegradationPass) continue;

                // Noisy-metadata pass on a deterministic subset of the same items.
                var subset = plan.Where(p => p.gt.Answerable)
                                 .OrderBy(p => p.item.Id, StringComparer.Ordinal)
                                 .Take(degradationItemCount).ToList();

                foreach (var rate in degradationRates)
                {
                    var settings = new PerturbationSettings { Seed = degradationSeed }.Clone(rate);
                    foreach (var (item, gt, _) in subset)
                    {
                        if (_governor.Exhausted) yield break;
                        var noisyPacket = _packetBuilder.Build(item, gt, settings);
                        yield return ExecuteOne(spec, item, gt, noisyPacket, run,
                                                new Dictionary<string, LlmTurn[]>(), null, settings, tally);
                        yield return new WaitForSeconds(delayBetweenCallsSeconds);
                    }
                }
            }

            double p50 = Percentile(tally.Latencies, 0.5);
            double p95 = Percentile(tally.Latencies, 0.95);
            Debug.Log($"[Benchmark] {spec.DisplayName}: {tally.Successes}/{tally.Calls} task success, " +
                      $"P50 {p50:F0} ms, P95 {p95:F0} ms, ${tally.Cost:F4} (session ${_governor.SpentUsd:F3})");
        }

        private IEnumerator ExecuteOne(ModelSpec spec, BenchmarkItem item, GroundTruth gt, ContextPacket packet,
                                       int run, Dictionary<string, LlmTurn[]> transcript, string previousItemId,
                                       PerturbationSettings perturbation, ModelTally tally)
        {
            string systemPrompt = SystemPromptTemplate + packet.Text;

            var turns = new List<LlmTurn>();
            string anchorAnswer = null;
            if (item.RequiresHistory)
            {
                if (!string.IsNullOrEmpty(item.HistoryAnchorItemId) &&
                    transcript.TryGetValue(item.HistoryAnchorItemId, out var anchor))
                {
                    turns.AddRange(anchor);
                    anchorAnswer = ResponseScorer.ExtractAnswerText(anchor[1].Content);
                }
                if (!string.IsNullOrEmpty(previousItemId) && previousItemId != item.HistoryAnchorItemId &&
                    transcript.TryGetValue(previousItemId, out var recent))
                    turns.AddRange(recent);
            }
            turns.Add(new LlmTurn { Role = "user", Content = gt.ResolvedPrompt });

            LlmResult result = null;
            yield return _client.Send(spec, systemPrompt, turns, r => result = r);
            result = result ?? new LlmResult { Error = "no result" };

            _governor.Record(result.CostUsd);
            tally.Calls++;
            tally.Cost += result.CostUsd;
            tally.LatencySum += result.LatencyMs;
            tally.Latencies.Add(result.LatencyMs);
            if (!result.Success) tally.Failures++;

            var scored = ResponseScorer.Score(result.Text, gt, _index);

            // Memory items are scored as self-consistency: did the model reproduce the number it
            // itself reported earlier? Correctness of that earlier number is scored on its own item.
            double memoryConsistency = double.NaN;
            if (item.Kind == QueryKind.MemoryRecall && anchorAnswer != null)
            {
                memoryConsistency = ResponseScorer.NumbersAgree(anchorAnswer, scored.AnswerText) ? 1 : 0;
                bool ok = memoryConsistency >= 1 && scored.FormatAdherence >= 1 && scored.InventedIdCount == 0;
                scored.TaskSuccess = ok;
                if (!ok && string.IsNullOrEmpty(scored.FailureNote)) scored.FailureNote = "recalled value differs from own earlier answer";
            }

            if (scored.TaskSuccess) tally.Successes++;

            transcript[item.Id] = new[]
            {
                new LlmTurn { Role = "user", Content = gt.ResolvedPrompt },
                new LlmTurn { Role = "assistant", Content = string.IsNullOrEmpty(result.Text) ? "(no response)" : result.Text }
            };

            WriteCsvRow(spec, item, gt, run, perturbation, result, scored, packet, memoryConsistency);
            WriteJsonl(spec, item, gt, run, perturbation, result, scored);

            Debug.Log($"[Benchmark] {spec.DisplayName} {item.Id} r{run}" +
                      (perturbation != null && perturbation.Enabled ? $" noise={perturbation.Rate:P0}" : "") +
                      $" -> {(scored.TaskSuccess ? "PASS" : "fail")} " +
                      $"({result.LatencyMs:F0} ms, {result.PromptTokens}+{result.CompletionTokens} tok, ${result.CostUsd:F5})" +
                      (scored.TaskSuccess ? "" : $" [{scored.FailureNote}]"));
        }

        // ---------------------------------------------------------------- io

        private void OpenWriters()
        {
            _csv = new StreamWriter(Path.Combine(_outputPath, "runs.csv"), false, Encoding.UTF8);
            _csv.WriteLine(string.Join(",", new[]
            {
                "session_id","timestamp","model","provider","model_id","item_id","family","tier","kind","run_index",
                "perturbation_rate","expects_abstention","gt_numeric","gt_id_count","gt_summary",
                "http_status","api_success","retries","prompt_tokens","completion_tokens","latency_ms","tokens_per_sec","cost_usd",
                "format_adherence","format_field_ratio","entity_f1","grounding_overlap","numeric_rel_error","numeric_at1","numeric_at5",
                "group_accuracy","coverage","id_hallucination_rate","invented_ids","abstained","safe_refusal","over_abstention",
                "memory_consistency","task_success","task_score_0_100","context_rows","failure_note"
            }));
            _csv.AutoFlush = true;

            _jsonl = new StreamWriter(Path.Combine(_outputPath, "responses.jsonl"), false, Encoding.UTF8) { AutoFlush = true };
        }

        private void CloseWriters()
        {
            _csv?.Flush(); _csv?.Dispose(); _csv = null;
            _jsonl?.Flush(); _jsonl?.Dispose(); _jsonl = null;
        }

        private void WriteCsvRow(ModelSpec spec, BenchmarkItem item, GroundTruth gt, int run,
                                 PerturbationSettings perturbation, LlmResult result, ScoredResponse scored,
                                 ContextPacket packet, double memoryConsistency)
        {
            double tps = result.LatencyMs > 0 ? result.CompletionTokens / (result.LatencyMs / 1000.0) : 0;

            var fields = new[]
            {
                _sessionId,
                DateTime.Now.ToString("o"),
                spec.DisplayName, spec.Provider.ToString(), spec.ModelId,
                item.Id, item.Family, item.Tier, item.Kind.ToString(), run.ToString(),
                (perturbation != null && perturbation.Enabled ? perturbation.Rate : 0f).ToString("0.00", CultureInfo.InvariantCulture),
                gt.ExpectAbstention ? "1" : "0",
                gt.Numeric.HasValue ? gt.Numeric.Value.ToString("0.###", CultureInfo.InvariantCulture) : "",
                gt.ElementIds.Count.ToString(),
                gt.Summary,
                result.HttpStatus.ToString(), result.Success ? "1" : "0", result.Retries.ToString(),
                result.PromptTokens.ToString(), result.CompletionTokens.ToString(),
                result.LatencyMs.ToString("0.##", CultureInfo.InvariantCulture),
                tps.ToString("0.##", CultureInfo.InvariantCulture),
                result.CostUsd.ToString("0.######", CultureInfo.InvariantCulture),
                N(scored.FormatAdherence), N(scored.FormatFieldRatio), N(scored.EntityF1), N(scored.GroundingOverlap),
                N(scored.NumericRelativeError), N(scored.NumericSuccess1), N(scored.NumericSuccess5),
                N(scored.GroupAccuracy), N(scored.Coverage), N(scored.IdHallucinationRate),
                scored.InventedIdCount.ToString(), scored.Abstained ? "1" : "0",
                N(scored.SafeRefusal), N(scored.OverAbstention), N(memoryConsistency),
                scored.TaskSuccess ? "1" : "0", scored.TaskScore0To100.ToString("0.##", CultureInfo.InvariantCulture),
                packet.RowCount.ToString(),
                scored.FailureNote
            };

            _csv.WriteLine(string.Join(",", fields.Select(Csv)));
        }

        private void WriteJsonl(ModelSpec spec, BenchmarkItem item, GroundTruth gt, int run,
                                PerturbationSettings perturbation, LlmResult result, ScoredResponse scored)
        {
            var record = new Dictionary<string, object>
            {
                { "session_id", _sessionId },
                { "model", spec.DisplayName },
                { "model_id", spec.ModelId },
                { "item_id", item.Id },
                { "family", item.Family },
                { "kind", item.Kind.ToString() },
                { "run_index", run },
                { "perturbation", perturbation == null ? "clean" : perturbation.Describe() },
                { "prompt", gt.ResolvedPrompt },
                { "expected", gt.Summary },
                { "expects_abstention", gt.ExpectAbstention },
                { "expected_numeric", gt.Numeric },
                { "expected_element_ids", gt.ElementIds.ToList() },
                { "response_text", result.Text },
                { "api_error", result.Error },
                { "latency_ms", result.LatencyMs },
                { "prompt_tokens", result.PromptTokens },
                { "completion_tokens", result.CompletionTokens },
                { "cost_usd", result.CostUsd },
                { "predicted_element_ids", scored.PredictedIds },
                { "task_success", scored.TaskSuccess },
                { "failure_note", scored.FailureNote }
            };
            _jsonl.WriteLine(JsonConvert.SerializeObject(record));
        }

        private void WriteGroundTruthReport(List<(BenchmarkItem item, GroundTruth gt, ContextPacket packet)> plan)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Ground truth computed from the loaded scene");
            sb.AppendLine();
            sb.AppendLine($"Scene: {_index.Profile.SceneName}  |  elements: {_index.Profile.TotalElements}  |  hash: {_index.Profile.SceneHash}");
            sb.AppendLine();
            sb.AppendLine("| Item | Family | Expected behaviour | Context rows |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var (item, gt, packet) in plan)
            {
                string expected = gt.ExpectAbstention ? "ABSTAIN - " + gt.UnanswerableReason : gt.Summary;
                sb.AppendLine($"| {item.Id} | {item.Family} | {expected.Replace("|", "/")} | {packet.RowCount} |");
            }
            if (_skippedItems.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## Skipped in this scene");
                foreach (var s in _skippedItems) sb.AppendLine($"- {s}");
            }
            File.WriteAllText(Path.Combine(_outputPath, "ground_truth.md"), sb.ToString());

            var json = plan.ToDictionary(p => p.item.Id, p => new
            {
                p.item.Family,
                p.item.Kind,
                prompt = p.gt.ResolvedPrompt,
                expects_abstention = p.gt.ExpectAbstention,
                reason = p.gt.UnanswerableReason,
                numeric = p.gt.Numeric,
                element_ids = p.gt.ElementIds.ToList(),
                entities = p.gt.Entities.ToList(),
                groups = p.gt.Groups,
                summary = p.gt.Summary,
                context_rows = p.packet.RowCount
            });
            File.WriteAllText(Path.Combine(_outputPath, "ground_truth.json"),
                JsonConvert.SerializeObject(json, Formatting.Indented));
        }

        private void WriteSummary(DateTime startedAt)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# BIM-MR conversational benchmark - session summary");
            sb.AppendLine();
            sb.AppendLine($"- Session: {_sessionId}");
            sb.AppendLine($"- Scene: {_index.Profile.SceneName} (hash {_index.Profile.SceneHash}, {_index.Profile.TotalElements} elements)");
            sb.AppendLine($"- Runs per item: {runsPerItem}, temperature {temperature}");
            sb.AppendLine($"- Duration: {(DateTime.Now - startedAt).TotalMinutes:F1} min");
            sb.AppendLine($"- Spend: ${_governor.SpentUsd:F3} of ${maxBudgetUsd:F2} cap");
            sb.AppendLine();
            sb.AppendLine("| Model | Calls | API failures | Task success | Mean latency (ms) | P95 (ms) | Cost (USD) |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            foreach (var kv in _tallies.OrderByDescending(k => k.Value.Successes / (double)Math.Max(1, k.Value.Calls)))
            {
                var t = kv.Value;
                sb.AppendLine($"| {kv.Key} | {t.Calls} | {t.Failures} | {t.Successes / (double)Math.Max(1, t.Calls):P1} | " +
                              $"{(t.Calls == 0 ? 0 : t.LatencySum / t.Calls):F0} | {Percentile(t.Latencies, 0.95):F0} | {t.Cost:F4} |");
            }
            sb.AppendLine();
            sb.AppendLine("Per-run records: `runs.csv`. Full answers: `responses.jsonl`. " +
                          "Expected answers: `ground_truth.md` / `.json`. Scene contents: `scene_profile.json`.");
            File.WriteAllText(Path.Combine(_outputPath, "summary.md"), sb.ToString());
        }

        // ----------------------------------------------------------- helpers

        private Dictionary<LlmProvider, string> ResolveApiKeys()
        {
            var keys = new Dictionary<LlmProvider, string>
            {
                { LlmProvider.OpenAI, openAiKey },
                { LlmProvider.Anthropic, anthropicKey },
                { LlmProvider.Google, googleKey }
            };

            // Fall back to the assistant component already configured in the scene, then to env vars.
            var assistant = FindAnyObjectByType<AEC.VoiceAssistant.AECGPTVoiceAssistant>();
            if (assistant != null)
            {
                var type = assistant.GetType();
                TryPullKey(keys, LlmProvider.OpenAI, type, assistant, "openAIApiKey");
                TryPullKey(keys, LlmProvider.Anthropic, type, assistant, "anthropicApiKey");
                TryPullKey(keys, LlmProvider.Google, type, assistant, "googleApiKey");
            }

            if (string.IsNullOrWhiteSpace(keys[LlmProvider.OpenAI]))
                keys[LlmProvider.OpenAI] = AEC.VoiceAssistant.ApiKeyProvider.OpenAI;
            if (string.IsNullOrWhiteSpace(keys[LlmProvider.Anthropic]))
                keys[LlmProvider.Anthropic] = AEC.VoiceAssistant.ApiKeyProvider.Anthropic;
            if (string.IsNullOrWhiteSpace(keys[LlmProvider.Google]))
                keys[LlmProvider.Google] = AEC.VoiceAssistant.ApiKeyProvider.Google;

            return keys;
        }

        private static void TryPullKey(Dictionary<LlmProvider, string> keys, LlmProvider provider,
                                       Type type, object instance, string fieldName)
        {
            if (!string.IsNullOrWhiteSpace(keys[provider])) return;
            var field = type.GetField(fieldName,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var value = field?.GetValue(instance) as string;
            if (!string.IsNullOrWhiteSpace(value)) keys[provider] = value.Trim();
        }

        private void DisableLegacyEvaluator()
        {
            var legacy = FindAnyObjectByType<AEC.VoiceAssistant.LLMEvaluationMode>();
            if (legacy != null && legacy.enabled)
            {
                legacy.enabled = false;
                Debug.LogWarning("[Benchmark] Disabled the legacy LLMEvaluationMode component so the two evaluators " +
                                 "do not run against the same API keys simultaneously.");
            }
        }

        private static int EstimatePromptTokens(List<(BenchmarkItem item, GroundTruth gt, ContextPacket packet)> plan)
        {
            if (plan.Count == 0) return 500;
            double avgChars = plan.Average(p => p.packet.Text.Length);
            return (int)(avgChars / 4.0) + 350;   // ~4 chars/token plus the fixed instruction block
        }

        private static double Percentile(List<double> values, double p)
        {
            if (values == null || values.Count == 0) return 0;
            var sorted = values.OrderBy(v => v).ToList();
            double position = (sorted.Count - 1) * p;
            int lower = Mathf.FloorToInt((float)position);
            int upper = Mathf.CeilToInt((float)position);
            if (lower == upper) return sorted[lower];
            return sorted[lower] + (position - lower) * (sorted[upper] - sorted[lower]);
        }

        private static string N(double value)
            => double.IsNaN(value) ? "" : value.ToString("0.####", CultureInfo.InvariantCulture);

        private static string Csv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var cleaned = value.Replace("\r", " ").Replace("\n", " ");
            if (cleaned.Contains(",") || cleaned.Contains("\""))
                return "\"" + cleaned.Replace("\"", "\"\"") + "\"";
            return cleaned;
        }
    }
}
