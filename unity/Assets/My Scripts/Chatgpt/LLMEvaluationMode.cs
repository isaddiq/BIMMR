// LLMEvaluationMode.cs - Automatic LLM Benchmarking System for AEC/BIM
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AEC.VoiceAssistant
{
    /// <summary>
    /// Automated evaluation system that benchmarks all registered LLMs 
    /// against 20 fixed AEC/BIM questions and generates comprehensive metrics with ANOVA analysis.
    /// 
    /// EVALUATION SCOPE (20 Questions):
    /// - Metadata Retrieval: Column/wall/door/window properties and attributes
    /// - Filtering & Categorization: Exterior/interior, level-based, system-based filtering
    /// - Calculation Tasks: Area, cost estimation with Korean prices (₩)
    /// - Domain Boundary: Refusal of non-AEC queries (weather)
    /// - Conversation Memory: Summary of Q&A history
    /// - Specialized Queries: MEP/HVAC, structural systems, QC status, schedules, materials
    /// 
    /// STATISTICAL ANALYSIS:
    /// - Multiple runs per question (configurable, default: 3)
    /// - One-way ANOVA to determine statistical significance across models
    /// - Within-model and between-model variance analysis
    /// 
    /// OUTPUTS:
    /// - leaderboard.csv: Overall rankings by performance metrics
    /// - llm_runs.csv: Raw data for all runs (13 models × 20 questions × 3 runs = 780 rows)
    /// - anova_summary.json: F-statistics and p-values per metric
    /// - statistical_analysis.md: Comprehensive statistical report with interpretations
    /// - radar_charts.html: Interactive visualization of model performance
    /// - radar_chart_overall.csv: Overall performance metrics data for manual radar chart creation
    /// - radar_chart_quality.csv: Quality metrics data for manual radar chart creation
    /// - radar_chart_efficiency.csv: Efficiency metrics data for manual radar chart creation
    /// - radar_chart_cost_performance.csv: Cost-performance scatter plot data
    /// - report.txt: Detailed text-based evaluation report
    /// - metrics_comparison_report.md: Human-readable Markdown comparison
    /// </summary>
    public class LLMEvaluationMode : MonoBehaviour
    {
        [Header("Evaluation Configuration")]
        [SerializeField] private bool enableAutoEvaluation = false;
        [SerializeField] private bool evaluateOnStart = true;
        [SerializeField] private float delayBetweenQuestions = 2f;
        [SerializeField] private float delayBetweenModels = 5f;
        [SerializeField] private bool evaluateAllModels = true; // If true, evaluates all 13 models; if false, only current model
        [SerializeField] private bool continueOnModelError = true; // If true, continues to next model on error

        [Header("Component References")]
        [SerializeField] private AECGPTVoiceAssistant gptAssistant;
        [SerializeField] private ModuleManager moduleManager;
        [SerializeField] private ArchiBIM.MR.BIMMetadataViewer metadataViewer;

        [Header("Output Configuration")]
        [SerializeField] private string outputDirectory = "llm_runs";

        [Header("ANOVA Configuration")]
        [SerializeField] private int runsPerPrompt = 3; // Number of runs per question for statistical analysis
        private List<RawRunData> allRawRuns = new List<RawRunData>(); // Store all individual runs for ANOVA

        // Tracking for evaluation completeness
        private List<string> successfulModels = new List<string>();
        private List<string> failedModels = new List<string>();

        // Standard evaluation questions - Voice-like conversational queries (20 questions)
        // These simulate transcribed voice input from users speaking naturally
        // Questions test the LLM's ability to understand BIM hierarchy, metadata retrieval, and domain boundaries

        // EXPECTED BEHAVIORS:
        // Q1: Read metadata from Structure → Columns; list "Name" and "Type" for each column
        // Q2: Examine Structure → Beams metadata; return elements where "Material" field is empty or undefined
        // Q3: Count all child components under Structure → Connections using their metadata list
        // Q4: Retrieve Structure → Stairs; read "Height" or "RiserCount × RiserHeight" from metadata and sum
        // Q5: Count all components under Architecture → Studs and report the total
        // Q6: Categorize elements under Architecture → Exterior Walls and Interior Walls based on metadata (Function or Location)
        // Q7: Check metadata of all wall elements; list walls where "FireRating" is missing
        // Q8: Sum "Area" or compute (Length × Width) for all Floor elements, grouped by "Level" metadata
        // Q9: Read "Material" field for all elements under Architecture → Ceiling and list distinct materials
        // Q10: Read metadata for Roof elements; output "TypeName" and "Slope" for each
        // Q11: Check all Architecture → Windows; identify any missing "GlazingType" or "Material"
        // Q12: Read metadata under Architecture → Doors; output "Name," "Type," and "FireRating"
        // Q13: Retrieve Architecture → Stairs; read "RiserCount" and sum across all stairs
        // Q14: Read "Category" and "Material" for each element under Architecture → Furniture
        // Q15: Count all MEP → Electric elements; group by "TypeName" to find the most frequent
        // Q16: Check all MEP → Plumbing elements; return those missing "Diameter" or "FlowRate" in metadata
        // Q17: Retrieve MEP → Lighting elements; read "PowerRating" or "Wattage" and return as text summary
        // Q18: Identify MEP elements whose "SystemName" or "Network" metadata equals "HVAC" and list them
        // Q19: Check all visible elements across Structure, Architecture, and MEP; list those where "QC_Status = Fail"
        // Q20: Retrieve all top-level categories (Structure, Architecture, MEP); count their child elements and report totals per category

        private readonly string[] evaluationQuestions = new string[]
        {
            "Can you tell me the name and type of each column in this structure?",
            "Could you check if any beams are missing their material information?",
            "What's the total number of connections in this structure?",
            "Can you tell me how many stairs are included in the structure category, and their total height?",
            "Can you tell me how many studs are used in this model?",
            "Could you separate exterior and interior walls for me?",
            "Are there any walls missing their fire rating values?",
            "What's the total floor area for each level in this model?",
            "What materials are used in the ceilings?",
            "Can you tell me the type and slope of each roof?",
            "Are there any windows without a glazing type or material defined?",
            "Could you list all doors along with their fire ratings?",
            "How many stairs are there in the architecture category, and what's their total riser count?",
            "Can you tell me what kind of furniture is placed in this model?",
            "Can you tell me how many electric fixtures are in this model, and which type appears most often?",
            "Are there any plumbing components missing diameter or flow rate data?",
            "Can you list all lighting fixtures and their power ratings?",
            "Which MEP systems are currently connected to the HVAC network?",
            "Are there any components with QC_Status marked as 'Fail'?",
            "Can you give me a summary of all components in the currently loaded BIM model, grouped by category?"
        };

        private string currentSessionId;
        private string currentOutputPath;
        private Dictionary<string, ModelEvaluationResults> modelResults = new Dictionary<string, ModelEvaluationResults>();
        private SceneSnapshot sceneSnapshot;
        private bool isEvaluating = false;

        // Progress tracking
        private int totalModels = 0;
        private int currentModelIndex = 0;
        private int currentQuestionIndex = 0;
        private DateTime evaluationStartTime;

        #region Unity Lifecycle

        private void Start()
        {
            if (enableAutoEvaluation && evaluateOnStart)
            {
                StartCoroutine(RunEvaluationDelayed());
            }
        }

        #endregion

        #region Evaluation Control

        private IEnumerator RunEvaluationDelayed()
        {
            yield return new WaitForSeconds(2f); // Allow scene to initialize
            StartEvaluation();
        }

        public void StartEvaluation()
        {
            if (isEvaluating)
            {
                Debug.LogWarning("[LLMEvaluation] Evaluation already in progress");
                return;
            }

            StartCoroutine(RunFullEvaluation());
        }

        private IEnumerator RunFullEvaluation()
        {
            isEvaluating = true;
            evaluationStartTime = DateTime.Now;
            currentSessionId = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            currentOutputPath = Path.Combine(Application.persistentDataPath, outputDirectory, currentSessionId);

            Debug.Log($"[LLMEvaluation] ========================================");
            Debug.Log($"[LLMEvaluation] Starting evaluation session: {currentSessionId}");
            Debug.Log($"[LLMEvaluation] Output path: {currentOutputPath}");
            Debug.Log($"[LLMEvaluation] ========================================");

            // Create output directory
            Directory.CreateDirectory(currentOutputPath);

            // Snapshot scene once
            sceneSnapshot = CaptureSceneSnapshot();
            SaveSceneSnapshot(sceneSnapshot);

            // Get all available models to test
            var modelsToTest = GetAvailableModels();

            // If evaluateAllModels is false, only test the currently configured model
            if (!evaluateAllModels && modelsToTest.Count > 0)
            {
                modelsToTest = new List<ModelConfig> { modelsToTest[0] };
                Debug.LogWarning($"[LLMEvaluation] evaluateAllModels is FALSE - Only testing first model: {modelsToTest[0].DisplayName}");
            }

            totalModels = modelsToTest.Count;
            currentModelIndex = 0;
            successfulModels.Clear();
            failedModels.Clear();

            Debug.Log($"[LLMEvaluation] Found {totalModels} models to evaluate");
            Debug.Log($"[LLMEvaluation] Evaluate All Models: {evaluateAllModels}");
            Debug.Log($"[LLMEvaluation] Continue On Error: {continueOnModelError}");
            Debug.Log($"[LLMEvaluation] Questions per model: {evaluationQuestions.Length}");
            Debug.Log($"[LLMEvaluation] Runs per question: {runsPerPrompt}");
            Debug.Log($"[LLMEvaluation] Total evaluations: {totalModels} × {evaluationQuestions.Length} × {runsPerPrompt} = {totalModels * evaluationQuestions.Length * runsPerPrompt}");
            Debug.Log($"[LLMEvaluation] ========================================");

            // Evaluate each model with error handling
            foreach (var modelConfig in modelsToTest)
            {
                currentModelIndex++;
                float overallProgress = ((currentModelIndex - 1) / (float)totalModels) * 100f;

                Debug.Log($"[LLMEvaluation] ╔════════════════════════════════════════╗");
                Debug.Log($"[LLMEvaluation] ║ MODEL {currentModelIndex}/{totalModels}: {modelConfig.DisplayName.PadRight(25)} ║");
                Debug.Log($"[LLMEvaluation] ║ Overall Progress: {overallProgress:F1}%{new string(' ', 18)}║");
                Debug.Log($"[LLMEvaluation] ╚════════════════════════════════════════╝");

                // Evaluate model and track success/failure
                yield return EvaluateModelWithErrorHandling(modelConfig);

                // Check if we should stop due to error
                if (!isEvaluating)
                {
                    Debug.LogError($"[LLMEvaluation] Stopping evaluation early due to error (continueOnModelError = false)");
                    break;
                }

                Debug.Log($"[LLMEvaluation] ----------------------------------------");
                yield return new WaitForSeconds(delayBetweenModels);
            }

            // Generate final reports
            Debug.Log($"[LLMEvaluation] ========================================");
            Debug.Log($"[LLMEvaluation] Generating final reports...");
            GenerateLeaderboard(); // Generate leaderboard with all model comparisons
            GenerateFinalReport();
            GenerateMetricsComparisonReport();
            GenerateStatisticalAnalysisReport(); // New: ANOVA and statistical validation
            GenerateRadarChartsHTML(); // New: Radar charts for visualization
            GenerateRadarChartDataCSV(); // New: Export radar chart data to CSV

            var totalTime = DateTime.Now - evaluationStartTime;
            Debug.Log($"[LLMEvaluation] ========================================");
            Debug.Log($"[LLMEvaluation] EVALUATION COMPLETE!");
            Debug.Log($"[LLMEvaluation] ========================================");
            Debug.Log($"[LLMEvaluation] Summary:");
            Debug.Log($"[LLMEvaluation]   Total Models Attempted: {totalModels}");
            Debug.Log($"[LLMEvaluation]   ✓ Successful: {successfulModels.Count}");
            Debug.Log($"[LLMEvaluation]   ✗ Failed: {failedModels.Count}");

            if (successfulModels.Count > 0)
            {
                Debug.Log($"[LLMEvaluation] ✓ Successfully Evaluated Models:");
                foreach (var model in successfulModels)
                {
                    Debug.Log($"[LLMEvaluation]     - {model}");
                }
            }

            if (failedModels.Count > 0)
            {
                Debug.LogWarning($"[LLMEvaluation] ✗ Failed Models:");
                foreach (var model in failedModels)
                {
                    Debug.LogWarning($"[LLMEvaluation]     - {model}");
                }
            }

            Debug.Log($"[LLMEvaluation] ----------------------------------------");
            Debug.Log($"[LLMEvaluation] Total time: {totalTime.TotalMinutes:F2} minutes");
            Debug.Log($"[LLMEvaluation] Results saved to: {currentOutputPath}");
            Debug.Log($"[LLMEvaluation] ========================================");

            isEvaluating = false;
        }

        #endregion

        #region Model Discovery

        private List<ModelConfig> GetAvailableModels()
        {
            var models = new List<ModelConfig>();

            if (gptAssistant == null)
            {
                gptAssistant = FindFirstObjectByType<AECGPTVoiceAssistant>();
            }

            if (gptAssistant == null)
            {
                Debug.LogError("[LLMEvaluation] No AECGPTVoiceAssistant found!");
                return models;
            }

            // ========== Google Gemini Models (EVALUATED FIRST) ==========

            // Gemini 2.5 Series (Latest)
            models.Add(new ModelConfig
            {
                Provider = AIProvider.Google,
                ModelType = GeminiModelType.Gemini25Pro,
                DisplayName = "Gemini-2.5-Pro",
                CostPer1KInput = 0.00125,
                CostPer1KOutput = 0.005
            });

            models.Add(new ModelConfig
            {
                Provider = AIProvider.Google,
                ModelType = GeminiModelType.Gemini25Flash,
                DisplayName = "Gemini-2.5-Flash",
                CostPer1KInput = 0.00015,
                CostPer1KOutput = 0.0006
            });

            models.Add(new ModelConfig
            {
                Provider = AIProvider.Google,
                ModelType = GeminiModelType.Gemini25FlashLite,
                DisplayName = "Gemini-2.5-Flash-Lite",
                CostPer1KInput = 0.000075,
                CostPer1KOutput = 0.0003
            });

            // ========== OpenAI GPT Models ==========

            // GPT-5 Series (Latest)
            models.Add(new ModelConfig
            {
                Provider = AIProvider.OpenAI,
                ModelType = GPTModelType.GPT5Mini,
                DisplayName = "GPT-5-Mini",
                CostPer1KInput = 0.0008,
                CostPer1KOutput = 0.003
            });

            // GPT-4o Series (Recommended)
            models.Add(new ModelConfig
            {
                Provider = AIProvider.OpenAI,
                ModelType = GPTModelType.GPT4oMini,
                DisplayName = "GPT-4o-Mini",
                CostPer1KInput = 0.00015,
                CostPer1KOutput = 0.0006
            });

            models.Add(new ModelConfig
            {
                Provider = AIProvider.OpenAI,
                ModelType = GPTModelType.GPT4o,
                DisplayName = "GPT-4o",
                CostPer1KInput = 0.0025,
                CostPer1KOutput = 0.01
            });

            // GPT-4 Series
            models.Add(new ModelConfig
            {
                Provider = AIProvider.OpenAI,
                ModelType = GPTModelType.GPT4Turbo,
                DisplayName = "GPT-4-Turbo",
                CostPer1KInput = 0.001,
                CostPer1KOutput = 0.003
            });

            models.Add(new ModelConfig
            {
                Provider = AIProvider.OpenAI,
                ModelType = GPTModelType.GPT4,
                DisplayName = "GPT-4",
                CostPer1KInput = 0.003,
                CostPer1KOutput = 0.006
            });

            // GPT-3.5 Series
            models.Add(new ModelConfig
            {
                Provider = AIProvider.OpenAI,
                ModelType = GPTModelType.GPT35Turbo,
                DisplayName = "GPT-3.5-Turbo",
                CostPer1KInput = 0.0005,
                CostPer1KOutput = 0.0015
            });

            // ========== Anthropic Claude Models ==========

            // Claude Sonnet 4.5 (Latest)
            models.Add(new ModelConfig
            {
                Provider = AIProvider.Anthropic,
                ModelType = ClaudeModelType.Claude45Sonnet,
                DisplayName = "Claude-Sonnet-4.5",
                CostPer1KInput = 0.003,
                CostPer1KOutput = 0.015
            });

            // Claude Sonnet 4.0
            models.Add(new ModelConfig
            {
                Provider = AIProvider.Anthropic,
                ModelType = ClaudeModelType.Claude40Sonnet,
                DisplayName = "Claude-Sonnet-4.0",
                CostPer1KInput = 0.015,
                CostPer1KOutput = 0.075
            });

            // Claude Opus 4.1 (High-end reasoning model)
            models.Add(new ModelConfig
            {
                Provider = AIProvider.Anthropic,
                ModelType = ClaudeModelType.Claude41Opus,
                DisplayName = "Claude-Opus-4.1",
                CostPer1KInput = 0.015,
                CostPer1KOutput = 0.075
            });

            // Claude Haiku 4.5 (Fast / Low-latency model)
            models.Add(new ModelConfig
            {
                Provider = AIProvider.Anthropic,
                ModelType = ClaudeModelType.Claude45Haiku,
                DisplayName = "Claude-Haiku-4.5",
                CostPer1KInput = 0.0008,
                CostPer1KOutput = 0.004
            });

            Debug.Log($"[LLMEvaluation] Registered {models.Count} models for evaluation:");
            Debug.Log($"  - Google Gemini: 3 models (evaluated FIRST)");
            Debug.Log($"  - OpenAI GPT: 6 models (including GPT-5 series)");
            Debug.Log($"  - Anthropic Claude: 4 models");

            return models;
        }

        #endregion

        #region Model Evaluation

        private IEnumerator EvaluateModel(ModelConfig modelConfig)
        {
            var results = new ModelEvaluationResults
            {
                ModelName = modelConfig.DisplayName,
                Provider = modelConfig.Provider.ToString(),
                SessionId = currentSessionId,
                StartTime = DateTime.Now,
                TurnResults = new List<TurnResult>()
            };

            // Configure the assistant for this model
            ConfigureAssistantForModel(modelConfig);

            // Disable TTS for batch evaluation
            gptAssistant.SetTTSEnabled(false);

            // Clear conversation history for fresh start
            gptAssistant.ClearConversationHistory();

            // Wait for assistant to be ready
            yield return new WaitForSeconds(1f);

            Debug.Log($"[LLMEvaluation] Starting batch evaluation for {modelConfig.DisplayName} (TTS disabled for speed)");
            Debug.Log($"[LLMEvaluation] Runs per question: {runsPerPrompt} (for ANOVA analysis)");

            int totalRuns = evaluationQuestions.Length * runsPerPrompt;
            int currentRun = 0;

            // Ask each question in sequence, multiple times for ANOVA
            for (int i = 0; i < evaluationQuestions.Length; i++)
            {
                currentQuestionIndex = i + 1;
                string question = evaluationQuestions[i];

                // Run each question multiple times
                for (int runIdx = 0; runIdx < runsPerPrompt; runIdx++)
                {
                    currentRun++;

                    // Calculate progress
                    float modelProgress = (currentRun / (float)totalRuns) * 100f;
                    float overallProgress = ((currentModelIndex - 1 + (currentRun / (float)totalRuns)) / totalModels) * 100f;
                    var elapsed = DateTime.Now - evaluationStartTime;
                    var estimatedTotal = totalModels > 0 ? TimeSpan.FromTicks(elapsed.Ticks * totalModels / currentModelIndex) : TimeSpan.Zero;
                    var remaining = estimatedTotal - elapsed;

                    Debug.Log($"[LLMEvaluation] ┌─────────────────────────────────────────");
                    Debug.Log($"[LLMEvaluation] │ Question {currentQuestionIndex}/{evaluationQuestions.Length} Run {runIdx + 1}/{runsPerPrompt} ({modelProgress:F1}% of {modelConfig.DisplayName})");
                    Debug.Log($"[LLMEvaluation] │ Overall: {overallProgress:F1}% | Elapsed: {elapsed.TotalMinutes:F1}m | ETA: {remaining.TotalMinutes:F1}m");
                    Debug.Log($"[LLMEvaluation] │ Q: {(question.Length > 60 ? question.Substring(0, 60) + "..." : question)}");
                    Debug.Log($"[LLMEvaluation] └─────────────────────────────────────────");

                    var turnResult = new TurnResult
                    {
                        TurnNumber = i + 1,
                        Question = question,
                        QuestionTime = DateTime.Now
                    };

                    // Send question and wait for response using batch mode
                    var startTime = DateTime.Now;
                    string response = null;
                    bool responseReceived = false;

                    // Use the new batch evaluation method
                    yield return gptAssistant.SendBatchEvaluationMessage(question, (r) =>
                    {
                        response = r;
                        responseReceived = true;
                    });

                    var endTime = DateTime.Now;

                    if (!responseReceived || string.IsNullOrEmpty(response))
                    {
                        Debug.LogWarning($"[LLMEvaluation] No response received for question {i + 1} run {runIdx + 1}");
                        response = "[NO_RESPONSE]";
                    }

                    turnResult.Response = response;
                    turnResult.ResponseTime = endTime;
                    turnResult.LatencyMs = (endTime - startTime).TotalMilliseconds;

                    // Compute metrics for this turn
                    ComputeTurnMetrics(turnResult, i, modelConfig);

                    results.TurnResults.Add(turnResult);

                    // Create raw run data for ANOVA
                    var rawRun = new RawRunData
                    {
                        ModelName = modelConfig.DisplayName,
                        QuestionId = i + 1,
                        RunIndex = runIdx + 1,
                        TaskSuccess = turnResult.RefusalCorrectness >= 0, // Success if not hallucination
                        HallucinationRate = turnResult.ContainsHallucination ? 1.0 : 0.0,
                        LatencyMs = turnResult.LatencyMs,
                        TotalCostUsd = turnResult.CostUSD,
                        TokensPerSec = turnResult.LatencyMs > 0 ? (turnResult.CompletionTokens / (turnResult.LatencyMs / 1000.0)) : 0,
                        EntityF1 = turnResult.EntityF1,
                        GroundingOverlap = turnResult.GroundingOverlap,
                        FormatAdherence = turnResult.FormatAdherence
                    };
                    allRawRuns.Add(rawRun);

                    // Append to CSV immediately
                    AppendRawRunToCSV(rawRun);

                    // Log response summary
                    string responseSummary = turnResult.Response.Length > 80 ?
                        turnResult.Response.Substring(0, 80) + "..." : turnResult.Response;
                    Debug.Log($"[LLMEvaluation] ✓ Run {runIdx + 1}/{runsPerPrompt} completed ({turnResult.LatencyMs:F0}ms): {responseSummary}");

                    // Wait before next run
                    yield return new WaitForSeconds(delayBetweenQuestions);
                }
            }

            results.EndTime = DateTime.Now;
            ComputeDialogueMetrics(results);

            // Save results
            modelResults[modelConfig.DisplayName] = results;
            SaveModelResults(results, modelConfig);

            Debug.Log($"[LLMEvaluation] ═══════════════════════════════════════════");
            Debug.Log($"[LLMEvaluation] {modelConfig.DisplayName} Summary:");
            Debug.Log($"[LLMEvaluation]   • Task Success Rate: {results.TaskSuccessRate:P2}");
            Debug.Log($"[LLMEvaluation]   • Avg Latency: {results.AverageLatencyMs:F0}ms");
            Debug.Log($"[LLMEvaluation]   • Total Cost: ${results.TotalCostUSD:F4}");
            Debug.Log($"[LLMEvaluation]   • Hallucination Rate: {results.HallucinationRate:P2}");
            Debug.Log($"[LLMEvaluation] ═══════════════════════════════════════════");
        }

        private IEnumerator EvaluateModelWithErrorHandling(ModelConfig modelConfig)
        {
            // Track if model was already in results before evaluation
            bool hadPriorResults = modelResults.ContainsKey(modelConfig.DisplayName);

            // Run the evaluation
            yield return EvaluateModel(modelConfig);

            // Check if evaluation succeeded by verifying results were added/updated
            if (modelResults.ContainsKey(modelConfig.DisplayName))
            {
                var results = modelResults[modelConfig.DisplayName];
                if (results != null && results.TurnResults != null && results.TurnResults.Count > 0)
                {
                    successfulModels.Add(modelConfig.DisplayName);
                    Debug.Log($"[LLMEvaluation] ✓ Successfully completed {modelConfig.DisplayName} with {results.TurnResults.Count} question runs");
                }
                else
                {
                    failedModels.Add(modelConfig.DisplayName);
                    Debug.LogWarning($"[LLMEvaluation] ⚠ {modelConfig.DisplayName} completed but has no results!");
                }
            }
            else
            {
                failedModels.Add(modelConfig.DisplayName);
                Debug.LogError($"[LLMEvaluation] ✗ {modelConfig.DisplayName} failed - no results generated!");

                if (!continueOnModelError)
                {
                    Debug.LogError($"[LLMEvaluation] continueOnModelError is FALSE - Stopping evaluation!");
                    isEvaluating = false;
                }
            }
        }

        private void ConfigureAssistantForModel(ModelConfig modelConfig)
        {
            if (gptAssistant == null) return;

            // Use reflection to set the model
            var assistantType = gptAssistant.GetType();

            // Set provider
            var providerField = assistantType.GetField("selectedProvider",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (providerField != null)
            {
                providerField.SetValue(gptAssistant, modelConfig.Provider);
            }

            // Set model type based on provider
            switch (modelConfig.Provider)
            {
                case AIProvider.OpenAI:
                    var gptModelField = assistantType.GetField("selectedGPTModel",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (gptModelField != null && modelConfig.ModelType is GPTModelType gptModel)
                    {
                        gptModelField.SetValue(gptAssistant, gptModel);
                    }
                    break;

                case AIProvider.Anthropic:
                    var claudeModelField = assistantType.GetField("selectedClaudeModel",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (claudeModelField != null && modelConfig.ModelType is ClaudeModelType claudeModel)
                    {
                        claudeModelField.SetValue(gptAssistant, claudeModel);
                    }
                    break;

                case AIProvider.Google:
                    var geminiModelField = assistantType.GetField("selectedGeminiModel",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (geminiModelField != null && modelConfig.ModelType is GeminiModelType geminiModel)
                    {
                        geminiModelField.SetValue(gptAssistant, geminiModel);
                    }
                    break;
            }
        }

        #endregion

        #region Metrics Computation

        private void ComputeTurnMetrics(TurnResult turn, int questionIndex, ModelConfig modelConfig)
        {
            // JSON Validity
            turn.IsValidJSON = IsValidJSON(turn.Response);

            // Format Adherence (question-specific)
            turn.FormatAdherence = CheckFormatAdherence(turn.Response, questionIndex);

            // Grounding Overlap
            turn.GroundingOverlap = ComputeGroundingOverlap(turn.Response, questionIndex);

            // Entity F1
            turn.EntityF1 = ComputeEntityF1(turn.Response, questionIndex);

            // Exact/Set Match
            turn.ExactMatch = ComputeExactMatch(turn.Response, questionIndex);

            // Refusal Correctness (no longer applicable - removed weather question)
            turn.RefusalCorrectness = 0; // Not applicable for new question set

            // Hallucination detection
            turn.ContainsHallucination = DetectHallucination(turn.Response, questionIndex);

            // Token estimation (rough approximation: 1 token ≈ 4 characters)
            turn.PromptTokens = estimateTokens(evaluationQuestions[questionIndex]);
            turn.CompletionTokens = estimateTokens(turn.Response);

            // Cost calculation
            turn.CostUSD = CalculateCost(turn.PromptTokens, turn.CompletionTokens, modelConfig);
        }

        private void ComputeDialogueMetrics(ModelEvaluationResults results)
        {
            if (results.TurnResults.Count == 0) return;

            // Task Success @1 - Comprehensive criteria that includes information retrieval quality
            int successfulTurns = results.TurnResults.Count(t =>
            {
                // Basic requirements
                bool basicQuality = t.FormatAdherence > 0.5 && !t.ContainsHallucination;

                // Information retrieval quality - check if the response contains useful information
                bool hasInformationRetrieval = t.EntityF1 > 0.3 || t.GroundingOverlap > 0.3 ||
                                               t.FormatAdherence > 0.7; // High format adherence indicates structured information

                // Special case for refusal questions (like weather) - just check refusal correctness
                bool refusalHandled = t.RefusalCorrectness >= 0;

                return basicQuality && (hasInformationRetrieval || refusalHandled);
            });
            results.TaskSuccessRate = (double)successfulTurns / results.TurnResults.Count;

            // Hallucination Rate
            int hallucinatedTurns = results.TurnResults.Count(t => t.ContainsHallucination);
            results.HallucinationRate = (double)hallucinatedTurns / results.TurnResults.Count;

            // Latency statistics
            var latencies = results.TurnResults.Select(t => t.LatencyMs).OrderBy(l => l).ToList();
            results.LatencyP50 = GetPercentile(latencies, 0.5);
            results.LatencyP95 = GetPercentile(latencies, 0.95);
            results.AverageLatencyMs = latencies.Average();

            // Token statistics
            results.TotalPromptTokens = results.TurnResults.Sum(t => t.PromptTokens);
            results.TotalCompletionTokens = results.TurnResults.Sum(t => t.CompletionTokens);
            results.TokensPerSecond = (results.TotalPromptTokens + results.TotalCompletionTokens) /
                                     (results.EndTime - results.StartTime).TotalSeconds;

            // Total cost
            results.TotalCostUSD = results.TurnResults.Sum(t => t.CostUSD);

            // Memory carry-over (Q8 - conversation summary question at index 7)
            // This tests if the model can recall previous questions/answers
            if (results.TurnResults.Count >= 8)
            {
                var q8 = results.TurnResults[7]; // Q8 (index 7): "Could you summarize everything we've talked about so far?"

                // Check if response shows conversation awareness
                bool hasConversationIndicators = false;
                if (!string.IsNullOrEmpty(q8.Response))
                {
                    string lower = q8.Response.ToLower();
                    hasConversationIndicators =
                        lower.Contains("question") ||
                        lower.Contains("asked") ||
                        lower.Contains("discussed") ||
                        lower.Contains("conversation") ||
                        lower.Contains("talk") ||
                        lower.Contains("summary") ||
                        lower.Contains("summarize") ||
                        // Check for mentions of previous topics
                        lower.Contains("column") ||
                        lower.Contains("wall") ||
                        lower.Contains("fire rating") ||
                        lower.Contains("area") ||
                        lower.Contains("cost") ||
                        lower.Contains("exterior") ||
                        lower.Contains("interior");
                }

                // Give full credit if format adherence is good OR if it shows conversation awareness
                results.MemoryCarryOverSuccess = (q8.FormatAdherence > 0.5 || hasConversationIndicators) ? 1.0 : 0.0;
            }
            else
            {
                // Not enough questions to test memory carry-over
                results.MemoryCarryOverSuccess = 1.0; // N/A - give full credit
            }

            // Average scores
            results.AverageEntityF1 = results.TurnResults.Average(t => t.EntityF1);
            results.AverageGroundingOverlap = results.TurnResults.Average(t => t.GroundingOverlap);
            results.AverageFormatAdherence = results.TurnResults.Average(t => t.FormatAdherence);

            // Compute enhanced statistical metrics
            ComputeEnhancedStatistics(results);
        }

        private void ComputeEnhancedStatistics(ModelEvaluationResults results)
        {
            if (results.TurnResults.Count == 0) return;

            var latencies = results.TurnResults.Select(t => t.LatencyMs).ToList();
            var entityF1s = results.TurnResults.Select(t => t.EntityF1).ToList();
            var groundingOverlaps = results.TurnResults.Select(t => t.GroundingOverlap).ToList();
            var formatAdherences = results.TurnResults.Select(t => t.FormatAdherence).ToList();

            // Latency statistics
            results.LatencyMin = latencies.Min();
            results.LatencyMax = latencies.Max();
            results.LatencyStdDev = CalculateStandardDeviation(latencies);
            results.LatencyVarianceCoefficient = results.AverageLatencyMs > 0
                ? results.LatencyStdDev / results.AverageLatencyMs
                : 0;

            // Quality metric standard deviations
            results.EntityF1StdDev = CalculateStandardDeviation(entityF1s);
            results.GroundingOverlapStdDev = CalculateStandardDeviation(groundingOverlaps);
            results.FormatAdherenceStdDev = CalculateStandardDeviation(formatAdherences);

            // Consistency Score (lower std dev = more consistent)
            // Normalized to 0-1 where 1 is most consistent
            double avgStdDev = (results.EntityF1StdDev + results.GroundingOverlapStdDev +
                               results.FormatAdherenceStdDev) / 3.0;
            results.ConsistencyScore = Math.Max(0, 1.0 - avgStdDev);

            // Reliability Score (combines low hallucination with high consistency)
            results.ReliabilityScore = (1.0 - results.HallucinationRate) * results.ConsistencyScore;

            // Cost per successful task
            int successfulTasks = (int)(results.TaskSuccessRate * results.TurnResults.Count);
            results.CostPerSuccessfulTask = successfulTasks > 0
                ? results.TotalCostUSD / successfulTasks
                : results.TotalCostUSD;

            // Quality Weighted Score (composite of accuracy metrics)
            results.QualityWeightedScore =
                (results.AverageEntityF1 * 0.3) +
                (results.AverageGroundingOverlap * 0.25) +
                (results.AverageFormatAdherence * 0.25) +
                (results.TaskSuccessRate * 0.15) +
                (results.ReliabilityScore * 0.05);

            // Efficiency Score (balances speed and cost)
            // Normalized: faster and cheaper = higher score
            double normalizedSpeed = results.AverageLatencyMs > 0 ? 1000.0 / results.AverageLatencyMs : 0;
            double normalizedCost = results.TotalCostUSD > 0 ? 0.01 / results.TotalCostUSD : 1.0;
            results.EfficiencyScore = (normalizedSpeed * 0.6) + (normalizedCost * 0.4);

            // Performance Index (overall ranking metric)
            results.PerformanceIndex =
                (results.QualityWeightedScore * 0.50) +
                (results.TaskSuccessRate * 0.20) +
                (results.ReliabilityScore * 0.15) +
                (results.EfficiencyScore * 0.10) +
                (results.MemoryCarryOverSuccess * 0.05);
        }

        private double CalculateStandardDeviation(List<double> values)
        {
            if (values.Count == 0) return 0;
            double avg = values.Average();
            double sumSquaredDiff = values.Sum(v => Math.Pow(v - avg, 2));
            return Math.Sqrt(sumSquaredDiff / values.Count);
        }

        private bool IsValidJSON(string response)
        {
            if (string.IsNullOrEmpty(response)) return false;

            try
            {
                JToken.Parse(response);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private double CheckFormatAdherence(string response, int questionIndex)
        {
            if (string.IsNullOrEmpty(response)) return 0.0;

            string lower = response.ToLower();
            bool hasNumeric = ContainsNumericValue(response);

            // Question-specific format checks (20 questions)
            switch (questionIndex)
            {
                case 0: // Q1: Column names and types from Structure → Columns
                    return (lower.Contains("column") && lower.Contains("name") && lower.Contains("type")) ? 1.0 : 0.4;

                case 1: // Q2: Beams missing material (Structure → Beams)
                    bool hasBeamRef = lower.Contains("beam");
                    bool hasMaterialMissing = lower.Contains("material") && (lower.Contains("missing") || lower.Contains("without") || lower.Contains("none"));
                    return (hasBeamRef && hasMaterialMissing) ? 1.0 : hasBeamRef ? 0.5 : 0.3;

                case 2: // Q3: Total number of connections (Structure → Connections)
                    bool hasConnection = lower.Contains("connection");
                    bool hasCount = hasNumeric || lower.Contains("total") || lower.Contains("count");
                    return (hasConnection && hasCount) ? 1.0 : hasConnection ? 0.6 : 0.3;

                case 3: // Q4: Stairs count and total height (Structure → Stairs)
                    bool hasStairs = lower.Contains("stair");
                    bool hasHeight = lower.Contains("height") || lower.Contains("riser");
                    return (hasStairs && hasHeight && hasNumeric) ? 1.0 : (hasStairs && hasNumeric) ? 0.6 : 0.3;

                case 4: // Q5: Stud count (Architecture → Studs)
                    bool hasStuds = lower.Contains("stud");
                    bool hasStudCount = hasNumeric || lower.Contains("count");
                    return (hasStuds && hasStudCount) ? 1.0 : hasStuds ? 0.5 : 0.3;

                case 5: // Q6: Exterior/Interior walls categorization
                    bool hasWall = lower.Contains("wall");
                    bool hasCategories = (lower.Contains("exterior") || lower.Contains("interior"));
                    return (hasWall && hasCategories) ? 1.0 : hasWall ? 0.4 : 0.3;

                case 6: // Q7: Walls missing fire rating
                    bool hasFire = lower.Contains("fire rating") || lower.Contains("firerating");
                    bool hasMissingFire = lower.Contains("missing") || lower.Contains("without") || lower.Contains("none");
                    return (hasFire && hasMissingFire) ? 1.0 : hasFire ? 0.6 : 0.3;

                case 7: // Q8: Total floor area by level
                    bool hasFloor = lower.Contains("floor");
                    bool hasArea = lower.Contains("area") || lower.Contains("m²") || lower.Contains("m2");
                    bool hasLevel = lower.Contains("level");
                    return (hasFloor && hasArea && hasLevel && hasNumeric) ? 1.0 : (hasArea && hasNumeric) ? 0.6 : 0.3;

                case 8: // Q9: Ceiling materials (Architecture → Ceiling)
                    bool hasCeiling = lower.Contains("ceiling");
                    bool hasMaterial = lower.Contains("material");
                    return (hasCeiling && hasMaterial) ? 1.0 : hasCeiling ? 0.6 : 0.3;

                case 9: // Q10: Roof type and slope
                    bool hasRoof = lower.Contains("roof");
                    bool hasType = lower.Contains("type");
                    bool hasSlope = lower.Contains("slope") || lower.Contains("pitch");
                    return (hasRoof && hasType && hasSlope) ? 1.0 : (hasRoof && (hasType || hasSlope)) ? 0.6 : 0.3;

                case 10: // Q11: Windows missing glazing type or material
                    bool hasWindow = lower.Contains("window");
                    bool hasGlazing = lower.Contains("glazing") || lower.Contains("glass");
                    bool hasWithout = lower.Contains("without") || lower.Contains("missing") || lower.Contains("none");
                    return (hasWindow && hasGlazing) ? 1.0 : hasWindow ? 0.5 : 0.3;

                case 11: // Q12: Doors with fire ratings
                    bool hasDoor = lower.Contains("door");
                    bool hasFireRating = lower.Contains("fire rating") || lower.Contains("firerating");
                    return (hasDoor && hasFireRating) ? 1.0 : hasDoor ? 0.6 : 0.3;

                case 12: // Q13: Architecture stairs and riser count
                    bool hasArchStairs = lower.Contains("stair");
                    bool hasRiserCount = lower.Contains("riser") || hasNumeric;
                    return (hasArchStairs && hasRiserCount) ? 1.0 : hasArchStairs ? 0.6 : 0.3;

                case 13: // Q14: Furniture category and material
                    bool hasFurniture = lower.Contains("furniture");
                    bool hasCategoryOrMaterial = lower.Contains("category") || lower.Contains("material");
                    return (hasFurniture && hasCategoryOrMaterial) ? 1.0 : hasFurniture ? 0.6 : 0.3;

                case 14: // Q15: Electric fixtures count and most frequent type
                    bool hasElectric = lower.Contains("electric") || lower.Contains("fixture");
                    bool hasFrequent = lower.Contains("most") || lower.Contains("frequent") || lower.Contains("common");
                    return (hasElectric && hasFrequent && hasNumeric) ? 1.0 : (hasElectric && hasNumeric) ? 0.7 : 0.4;

                case 15: // Q16: Plumbing missing diameter or flow rate
                    bool hasPlumbing = lower.Contains("plumbing");
                    bool hasDiameter = lower.Contains("diameter") || lower.Contains("flow rate");
                    bool hasMissingData = lower.Contains("missing") || lower.Contains("without");
                    return (hasPlumbing && hasDiameter) ? 1.0 : hasPlumbing ? 0.5 : 0.3;

                case 16: // Q17: Lighting fixtures and power ratings
                    bool hasLighting = lower.Contains("lighting") || lower.Contains("light");
                    bool hasPower = lower.Contains("power") || lower.Contains("watt") || lower.Contains("rating");
                    return (hasLighting && hasPower) ? 1.0 : hasLighting ? 0.6 : 0.3;

                case 17: // Q18: MEP systems connected to HVAC
                    bool hasMEP = lower.Contains("mep") || lower.Contains("system");
                    bool hasHVAC = lower.Contains("hvac") || lower.Contains("heating") || lower.Contains("ventilation");
                    return (hasMEP || hasHVAC) ? 1.0 : 0.4;

                case 18: // Q19: Components with QC_Status = Fail
                    bool hasQC = lower.Contains("quality") || lower.Contains("qc") || lower.Contains("qc_status");
                    bool hasFail = lower.Contains("fail") || lower.Contains("failed");
                    return (hasQC || hasFail) ? 1.0 : 0.4;

                case 19: // Q20: Summary of all components by category
                    bool hasComponents = lower.Contains("component") || lower.Contains("element");
                    bool hasStructArch = lower.Contains("structure") || lower.Contains("architecture") || lower.Contains("mep");
                    bool hasComprehensive = response.Length > 150;
                    return (hasComponents && hasStructArch && hasComprehensive) ? 1.0 : (hasStructArch && hasComprehensive) ? 0.7 : 0.4;

                default:
                    return 0.5;
            }
        }

        private double ComputeGroundingOverlap(string response, int questionIndex)
        {
            // Get scene objects that should be referenced
            var groundTruthIds = GetGroundTruthObjects(questionIndex);

            if (groundTruthIds.Count == 0) return 1.0; // No ground truth required

            // Extract referenced IDs from response
            var referencedIds = ExtractReferencedIds(response);

            // If no explicit IDs found, give partial credit based on relevant keywords
            if (referencedIds.Count == 0)
            {
                string lower = response.ToLower();
                bool hasRelevantKeywords = false;
                bool hasSubstantiveContent = response.Length > 100;

                // Check for question-specific keywords (updated for 20 questions)
                switch (questionIndex)
                {
                    case 0: // Q1: Column names and types from Structure
                        hasRelevantKeywords = lower.Contains("column") && (lower.Contains("name") || lower.Contains("type"));
                        break;
                    case 1: // Q2: Beams missing material
                        hasRelevantKeywords = lower.Contains("beam") && lower.Contains("material");
                        break;
                    case 2: // Q3: Connection count
                        hasRelevantKeywords = lower.Contains("connection") && (lower.Contains("count") || lower.Contains("total"));
                        break;
                    case 3: // Q4: Stairs count and height
                        hasRelevantKeywords = lower.Contains("stair") && (lower.Contains("height") || lower.Contains("riser"));
                        break;
                    case 4: // Q5: Stud count
                        hasRelevantKeywords = lower.Contains("stud") && (lower.Contains("count") || lower.Contains("total"));
                        break;
                    case 5: // Q6: Exterior/Interior walls
                        hasRelevantKeywords = lower.Contains("wall") && (lower.Contains("exterior") || lower.Contains("interior"));
                        break;
                    case 6: // Q7: Walls missing fire rating
                        hasRelevantKeywords = lower.Contains("wall") && lower.Contains("fire");
                        break;
                    case 7: // Q8: Floor area by level
                        hasRelevantKeywords = lower.Contains("floor") && lower.Contains("area");
                        break;
                    case 8: // Q9: Ceiling materials
                        hasRelevantKeywords = lower.Contains("ceiling") && lower.Contains("material");
                        break;
                    case 9: // Q10: Roof type and slope
                        hasRelevantKeywords = lower.Contains("roof") && (lower.Contains("type") || lower.Contains("slope"));
                        break;
                    case 10: // Q11: Windows without glazing
                        hasRelevantKeywords = lower.Contains("window") && lower.Contains("glazing");
                        break;
                    case 11: // Q12: Doors with fire ratings
                        hasRelevantKeywords = lower.Contains("door") && lower.Contains("fire");
                        break;
                    case 12: // Q13: Architecture stairs and riser count
                        hasRelevantKeywords = lower.Contains("stair") && lower.Contains("riser");
                        break;
                    case 13: // Q14: Furniture category and material
                        hasRelevantKeywords = lower.Contains("furniture") && (lower.Contains("category") || lower.Contains("material"));
                        break;
                    case 14: // Q15: Electric fixtures count and most frequent
                        hasRelevantKeywords = lower.Contains("electric") && (lower.Contains("count") || lower.Contains("fixture"));
                        break;
                    case 15: // Q16: Plumbing missing diameter/flow rate
                        hasRelevantKeywords = lower.Contains("plumbing") && (lower.Contains("diameter") || lower.Contains("flow"));
                        break;
                    case 16: // Q17: Lighting fixtures and power ratings
                        hasRelevantKeywords = lower.Contains("lighting") && lower.Contains("power");
                        break;
                    case 17: // Q18: MEP HVAC systems
                        hasRelevantKeywords = lower.Contains("hvac") || (lower.Contains("mep") && lower.Contains("system"));
                        break;
                    case 18: // Q19: QC status fail
                        hasRelevantKeywords = lower.Contains("fail") || lower.Contains("qc");
                        break;
                    case 19: // Q20: All components summary
                        hasRelevantKeywords = lower.Contains("component") || (lower.Contains("structure") && lower.Contains("architecture"));
                        break;
                }

                // Scoring for keyword-based responses
                if (hasRelevantKeywords && hasSubstantiveContent)
                    return 0.7; // High partial credit
                else if (hasRelevantKeywords)
                    return 0.5; // Medium partial credit
                else if (hasSubstantiveContent)
                    return 0.3; // Low partial credit
                else
                    return 0.0; // No credit
            }

            // Compute Jaccard index for ID-based responses
            var intersection = groundTruthIds.Intersect(referencedIds).Count();
            var union = groundTruthIds.Union(referencedIds).Count();

            return union > 0 ? (double)intersection / union : 0.0;
        }

        private double ComputeEntityF1(string response, int questionIndex)
        {
            var groundTruth = GetGroundTruthEntities(questionIndex);
            var predicted = ExtractPredictedEntities(response, questionIndex);

            // If no ground truth entities, not applicable
            if (groundTruth.Count == 0)
            {
                return 1.0;
            }

            // If no predicted entities, give partial credit for relevant content
            if (predicted.Count == 0)
            {
                string lower = response.ToLower();
                bool hasRelevantContent = false;

                // Check relevant content for all 20 questions (updated)
                switch (questionIndex)
                {
                    case 0: // Q1: Columns (Structure)
                        hasRelevantContent = lower.Contains("column") && response.Length > 50;
                        break;
                    case 1: // Q2: Beams missing material
                        hasRelevantContent = lower.Contains("beam") && response.Length > 50;
                        break;
                    case 2: // Q3: Connections count
                        hasRelevantContent = lower.Contains("connection") && response.Length > 30;
                        break;
                    case 3: // Q4: Stairs height
                        hasRelevantContent = lower.Contains("stair") && response.Length > 30;
                        break;
                    case 4: // Q5: Studs count
                        hasRelevantContent = lower.Contains("stud") && response.Length > 30;
                        break;
                    case 5:
                    case 6: // Q6, Q7: Walls
                        hasRelevantContent = lower.Contains("wall") && response.Length > 50;
                        break;
                    case 7: // Q8: Floors
                        hasRelevantContent = lower.Contains("floor") && response.Length > 50;
                        break;
                    case 8: // Q9: Ceilings
                        hasRelevantContent = lower.Contains("ceiling") && response.Length > 50;
                        break;
                    case 9: // Q10: Roofs
                        hasRelevantContent = lower.Contains("roof") && response.Length > 50;
                        break;
                    case 10: // Q11: Windows
                        hasRelevantContent = lower.Contains("window") && response.Length > 50;
                        break;
                    case 11: // Q12: Doors
                        hasRelevantContent = lower.Contains("door") && response.Length > 50;
                        break;
                    case 12: // Q13: Stairs (Architecture)
                        hasRelevantContent = lower.Contains("stair") && response.Length > 30;
                        break;
                    case 13: // Q14: Furniture
                        hasRelevantContent = lower.Contains("furniture") && response.Length > 50;
                        break;
                    case 14: // Q15: Electric fixtures
                        hasRelevantContent = lower.Contains("electric") && response.Length > 50;
                        break;
                    case 15: // Q16: Plumbing
                        hasRelevantContent = lower.Contains("plumbing") && response.Length > 50;
                        break;
                    case 16: // Q17: Lighting
                        hasRelevantContent = lower.Contains("lighting") && response.Length > 50;
                        break;
                    case 17: // Q18: MEP HVAC
                        hasRelevantContent = (lower.Contains("mep") || lower.Contains("hvac")) && response.Length > 50;
                        break;
                    case 18: // Q19: QC status
                        hasRelevantContent = (lower.Contains("qc") || lower.Contains("fail")) && response.Length > 50;
                        break;
                    case 19: // Q20: All components
                        hasRelevantContent = response.Length > 100;
                        break;
                }

                return hasRelevantContent ? 0.4 : 0.0; // Partial credit
            }

            var truePositives = groundTruth.Intersect(predicted).Count();

            // If no true positives but predicted entities exist, give partial credit for attempting to extract entities
            if (truePositives == 0)
            {
                return 0.2; // Small credit for extracting some entities, even if not matching ground truth
            }

            var precision = (double)truePositives / predicted.Count;
            var recall = (double)truePositives / groundTruth.Count;

            if (precision + recall == 0) return 0.2;
            return 2 * (precision * recall) / (precision + recall);
        }

        private double ComputeExactMatch(string response, int questionIndex)
        {
            var groundTruth = GetGroundTruthEntities(questionIndex);
            var predicted = ExtractPredictedEntities(response, questionIndex);

            return groundTruth.SetEquals(predicted) ? 1.0 : 0.0;
        }

        private int CheckRefusalCorrectness(string response)
        {
            if (string.IsNullOrEmpty(response)) return -1;

            string lowerResponse = response.ToLower();

            // Correct refusal patterns
            string[] refusalPatterns = new[]
            {
                "not related to aec",
                "cannot determine",
                "don't have information",
                "outside my scope",
                "beyond my capabilities",
                "i don't know",
                "no access to",
                "real-time",
                "weather data"
            };

            foreach (var pattern in refusalPatterns)
            {
                if (lowerResponse.Contains(pattern))
                    return 1; // Correct refusal
            }

            // Hallucination patterns (answering without data)
            string[] hallucinationPatterns = new[]
            {
                "sunny",
                "cloudy",
                "rainy",
                "°c",
                "°f",
                "temperature is",
                "degrees"
            };

            foreach (var pattern in hallucinationPatterns)
            {
                if (lowerResponse.Contains(pattern))
                    return -1; // Hallucination
            }

            return 0; // Neutral
        }

        private bool DetectHallucination(string response, int questionIndex)
        {
            if (string.IsNullOrEmpty(response)) return false;

            string lower = response.ToLower();

            // Question 1 (index 1): Beams missing material - should not fabricate material data
            if (questionIndex == 1)
            {
                // If LLM provides specific material values without indicating they're missing, it's hallucinating
                if ((lower.Contains("material:") || lower.Contains("material of")) &&
                    !lower.Contains("missing") && !lower.Contains("not found") &&
                    !lower.Contains("undefined") && !lower.Contains("empty"))
                {
                    return true; // Likely fabricated materials for beams that should be missing them
                }
            }

            // Question 6 (index 6): Fire rating check - should not fabricate fire ratings
            if (questionIndex == 6)
            {
                // If LLM provides specific fire rating values without checking metadata, it's hallucinating
                if ((lower.Contains("fire rating:") || lower.Contains("fire rating of")) &&
                    !lower.Contains("missing") && !lower.Contains("not found") &&
                    !lower.Contains("checked metadata") && !lower.Contains("property") &&
                    !lower.Contains("undefined"))
                {
                    return true; // Likely fabricated fire ratings without metadata verification
                }
            }

            // Question 7 (index 7): Floor area calculation - should not provide areas without calculation/metadata
            if (questionIndex == 7)
            {
                // If provides area values but doesn't mention dimensions or calculation
                if (ContainsNumericValue(response) && lower.Contains("area") &&
                    !lower.Contains("length") && !lower.Contains("width") &&
                    !lower.Contains("dimension") && !lower.Contains("metadata") &&
                    !lower.Contains("×") && !lower.Contains("*") && !lower.Contains("calculat"))
                {
                    return true; // Likely fabricated area without calculation
                }
            }

            // Question 10 (index 10): Windows missing glazing - should not fabricate glazing types
            if (questionIndex == 10)
            {
                if ((lower.Contains("glazing type:") || lower.Contains("glazing of")) &&
                    !lower.Contains("missing") && !lower.Contains("without") &&
                    !lower.Contains("undefined") && !lower.Contains("not specified"))
                {
                    return true; // Likely fabricated glazing types for windows that should be missing them
                }
            }

            // Question 15 (index 15): Plumbing missing data - should not fabricate diameter/flow rate
            if (questionIndex == 15)
            {
                if ((lower.Contains("diameter:") || lower.Contains("flow rate:")) &&
                    !lower.Contains("missing") && !lower.Contains("without") &&
                    !lower.Contains("undefined") && !lower.Contains("not specified"))
                {
                    return true; // Likely fabricated plumbing data that should be missing
                }
            }

            // General hallucination detection: fabricated IDs, impossible values
            if (response.Contains("id-", StringComparison.OrdinalIgnoreCase) ||
                response.Contains("element_", StringComparison.OrdinalIgnoreCase))
            {
                // Check if these IDs exist in scene
                var mentionedIds = ExtractReferencedIds(response);
                var sceneIds = GetAllSceneObjectIds();

                // If mentions IDs but none match scene, likely hallucinating
                if (mentionedIds.Count > 0 && !mentionedIds.Any(id => sceneIds.Contains(id)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Calculates overall performance score for ranking models.
        /// Score components:
        /// - Task Success Rate (40%): How well the model completed tasks without hallucinations
        /// - Information Retrieval Quality (30%): EntityF1 + GroundingOverlap + FormatAdherence
        /// - Reliability (20%): Inverse of hallucination rate
        /// - Efficiency (10%): Speed and cost effectiveness
        /// </summary>
        private double CalculateOverallScore(ModelEvaluationResults result)
        {
            // Normalize all metrics to 0-1 range

            // 1. Task Success Rate (40%) - already 0-1
            double taskSuccessScore = result.TaskSuccessRate * 0.40;

            // 2. Information Retrieval Quality (30%) - average of EntityF1, GroundingOverlap, FormatAdherence
            double infoRetrievalScore = ((result.AverageEntityF1 + result.AverageGroundingOverlap + result.AverageFormatAdherence) / 3.0) * 0.30;

            // 3. Reliability (20%) - inverse of hallucination rate
            double reliabilityScore = (1.0 - result.HallucinationRate) * 0.20;

            // 4. Efficiency (10%) - combination of speed and cost
            // Normalize latency (assuming 5000ms is poor, 500ms is excellent)
            double normalizedLatency = Math.Max(0, Math.Min(1, 1.0 - (result.LatencyP50 - 500) / 4500.0));
            // Normalize cost (assuming $0.10 is poor, $0.001 is excellent)
            double normalizedCost = Math.Max(0, Math.Min(1, 1.0 - (result.TotalCostUSD - 0.001) / 0.099));
            double efficiencyScore = ((normalizedLatency + normalizedCost) / 2.0) * 0.10;

            // Total score
            return taskSuccessScore + infoRetrievalScore + reliabilityScore + efficiencyScore;
        }

        private double CalculateCost(int promptTokens, int completionTokens, ModelConfig config)
        {
            double inputCost = (promptTokens / 1000.0) * config.CostPer1KInput;
            double outputCost = (completionTokens / 1000.0) * config.CostPer1KOutput;
            return inputCost + outputCost;
        }

        private int estimateTokens(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            // Rough approximation: 1 token ≈ 4 characters
            return text.Length / 4;
        }

        private double GetPercentile(List<double> sortedValues, double percentile)
        {
            if (sortedValues.Count == 0) return 0.0;
            int index = (int)Math.Ceiling(percentile * sortedValues.Count) - 1;
            index = Math.Max(0, Math.Min(sortedValues.Count - 1, index));
            return sortedValues[index];
        }

        private bool ContainsNumericValue(string text)
        {
            return System.Text.RegularExpressions.Regex.IsMatch(text, @"\d+\.?\d*");
        }

        #endregion

        #region Scene Analysis

        private SceneSnapshot CaptureSceneSnapshot()
        {
            var snapshot = new SceneSnapshot
            {
                Timestamp = DateTime.Now,
                SceneHash = ComputeSceneHash()
            };

            // Capture all BIM objects
            if (moduleManager != null)
            {
                snapshot.Modules = CaptureModules();
            }

            if (metadataViewer != null)
            {
                snapshot.BIMElements = CaptureBIMElements();
            }

            Debug.Log($"[LLMEvaluation] Scene snapshot captured: {snapshot.BIMElements.Count} elements, {snapshot.Modules.Count} modules");

            return snapshot;
        }

        private string ComputeSceneHash()
        {
            // Create deterministic hash of scene state
            var sb = new StringBuilder();

            if (moduleManager != null)
            {
                var modules = moduleManager.GetXRInteractableObjects();
                if (modules != null)
                {
                    foreach (var module in modules.OrderBy(m => m != null ? m.name : ""))
                    {
                        if (module != null)
                        {
                            sb.Append(module.name);
                            sb.Append(module.transform.position.ToString());
                        }
                    }
                }
            }

            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                return BitConverter.ToString(hash).Replace("-", "").Substring(0, 16);
            }
        }
        private List<ModuleSnapshot> CaptureModules()
        {
            var modules = new List<ModuleSnapshot>();

            if (moduleManager == null) return modules;

            // Get XR interactable objects as a proxy for modules
            var loadedModules = moduleManager.GetXRInteractableObjects();
            if (loadedModules != null)
            {
                foreach (var module in loadedModules)
                {
                    if (module != null)
                    {
                        modules.Add(new ModuleSnapshot
                        {
                            Name = module.name,
                            Position = new SerializableVector3(module.transform.position),
                            IsActive = module.activeInHierarchy
                        });
                    }
                }
            }

            return modules;
        }
        private List<BIMElementSnapshot> CaptureBIMElements()
        {
            var elements = new List<BIMElementSnapshot>();

            // Find all objects with Metadata component
            var metadataComponents = FindObjectsByType<Metadata>(FindObjectsSortMode.None);

            Debug.Log($"[LLMEvaluation] Found {metadataComponents.Length} objects with Metadata components");

            foreach (var metadata in metadataComponents)
            {
                if (metadata == null || metadata.gameObject == null)
                    continue;

                var obj = metadata.gameObject;

                // Get properties from Metadata component
                var properties = GetBIMPropertiesFromMetadata(metadata);

                // Determine element category from metadata properties
                string category = DetermineElementCategoryFromMetadata(properties, obj.name);

                var element = new BIMElementSnapshot
                {
                    ElementId = obj.GetEntityId().ToString(),
                    Name = obj.name,
                    Type = properties.ContainsKey("Type") ? properties["Type"] : category,
                    Category = category,
                    Level = GetBIMLevel(obj),
                    Properties = properties
                };

                elements.Add(element);
            }

            Debug.Log($"[LLMEvaluation] Captured {elements.Count} BIM elements from metadata");

            // Log category breakdown
            var categoryCounts = elements.GroupBy(e => e.Category)
                .Select(g => $"{g.Key}: {g.Count()}")
                .ToList();
            Debug.Log($"[LLMEvaluation] Category breakdown: {string.Join(", ", categoryCounts)}");

            return elements;
        }

        /// <summary>
        /// Extracts all properties from a Metadata component
        /// </summary>
        private Dictionary<string, string> GetBIMPropertiesFromMetadata(Metadata metadata)
        {
            var props = new Dictionary<string, string>();

            if (metadata == null || metadata.Properties == null)
                return props;

            // Extract all metadata properties
            foreach (var property in metadata.Properties)
            {
                if (property != null && !string.IsNullOrEmpty(property.Key))
                {
                    string key = property.Key.Trim();
                    string value = property.Value?.Trim() ?? "";

                    // Store the property
                    props[key] = value;
                }
            }

            return props;
        }

        /// <summary>
        /// Determines the BIM element category by analyzing metadata properties
        /// </summary>
        private string DetermineElementCategoryFromMetadata(Dictionary<string, string> properties, string objectName)
        {
            // Priority 1: Check explicit Category property
            if (properties.ContainsKey("Category") && !string.IsNullOrEmpty(properties["Category"]))
            {
                string category = properties["Category"];
                // Normalize category names
                if (category.Contains("Wall", StringComparison.OrdinalIgnoreCase))
                    return "Wall";
                if (category.Contains("Column", StringComparison.OrdinalIgnoreCase))
                    return "Column";
                if (category.Contains("Beam", StringComparison.OrdinalIgnoreCase))
                    return "Beam";
                if (category.Contains("Floor", StringComparison.OrdinalIgnoreCase) ||
                    category.Contains("Slab", StringComparison.OrdinalIgnoreCase))
                    return "Floor";
                if (category.Contains("Door", StringComparison.OrdinalIgnoreCase))
                    return "Door";
                if (category.Contains("Window", StringComparison.OrdinalIgnoreCase))
                    return "Window";
                if (category.Contains("Roof", StringComparison.OrdinalIgnoreCase))
                    return "Roof";
                if (category.Contains("Stair", StringComparison.OrdinalIgnoreCase))
                    return "Stair";

                return category; // Return as-is if recognized
            }

            // Priority 2: Check Family property (common in Revit exports)
            if (properties.ContainsKey("Family") && !string.IsNullOrEmpty(properties["Family"]))
            {
                string family = properties["Family"];
                if (family.Contains("Wall", StringComparison.OrdinalIgnoreCase))
                    return "Wall";
                if (family.Contains("Column", StringComparison.OrdinalIgnoreCase))
                    return "Column";
                if (family.Contains("Beam", StringComparison.OrdinalIgnoreCase))
                    return "Beam";
                if (family.Contains("Floor", StringComparison.OrdinalIgnoreCase) ||
                    family.Contains("Slab", StringComparison.OrdinalIgnoreCase))
                    return "Floor";
                if (family.Contains("Door", StringComparison.OrdinalIgnoreCase))
                    return "Door";
                if (family.Contains("Window", StringComparison.OrdinalIgnoreCase))
                    return "Window";
            }

            // Priority 3: Check Type property
            if (properties.ContainsKey("Type") && !string.IsNullOrEmpty(properties["Type"]))
            {
                string type = properties["Type"];
                if (type.Contains("Wall", StringComparison.OrdinalIgnoreCase))
                    return "Wall";
                if (type.Contains("Column", StringComparison.OrdinalIgnoreCase))
                    return "Column";
                if (type.Contains("Beam", StringComparison.OrdinalIgnoreCase))
                    return "Beam";
            }

            // Priority 4: Check Family Name property (alternative naming)
            if (properties.ContainsKey("FamilyName") && !string.IsNullOrEmpty(properties["FamilyName"]))
            {
                string familyName = properties["FamilyName"];
                if (familyName.Contains("Wall", StringComparison.OrdinalIgnoreCase))
                    return "Wall";
                if (familyName.Contains("Column", StringComparison.OrdinalIgnoreCase))
                    return "Column";
                if (familyName.Contains("Beam", StringComparison.OrdinalIgnoreCase))
                    return "Beam";
            }

            // Priority 5: Check common property combinations
            // Columns often have "Structural" property
            if (properties.ContainsKey("Structural") || properties.ContainsKey("StructuralUsage"))
            {
                if (properties.Any(p => p.Value.Contains("Column", StringComparison.OrdinalIgnoreCase)))
                    return "Column";
                if (properties.Any(p => p.Value.Contains("Beam", StringComparison.OrdinalIgnoreCase)))
                    return "Beam";
            }

            // Priority 6: Check object name as fallback
            string name = objectName.ToLower();
            if (name.Contains("wall")) return "Wall";
            if (name.Contains("column")) return "Column";
            if (name.Contains("beam")) return "Beam";
            if (name.Contains("floor") || name.Contains("slab")) return "Floor";
            if (name.Contains("door")) return "Door";
            if (name.Contains("window")) return "Window";
            if (name.Contains("roof")) return "Roof";
            if (name.Contains("stair")) return "Stair";

            return "Generic";
        }

        private string GetBIMLevel(GameObject obj)
        {
            // Priority 1: Check if object has metadata with Level property
            var metadata = obj.GetComponent<Metadata>();
            if (metadata != null && metadata.Properties != null)
            {
                var levelProp = metadata.Properties.FirstOrDefault(p =>
                    p.Key != null &&
                    (p.Key.Equals("Level", StringComparison.OrdinalIgnoreCase) ||
                     p.Key.Equals("Base Level", StringComparison.OrdinalIgnoreCase) ||
                     p.Key.Contains("Level", StringComparison.OrdinalIgnoreCase)));

                if (levelProp != null && !string.IsNullOrEmpty(levelProp.Value))
                    return levelProp.Value;
            }

            // Priority 2: Try to extract level from name or hierarchy
            string name = obj.name;
            if (name.Contains("L1") || name.Contains("Level 1") || name.Contains("Floor 1")) return "Level 1";
            if (name.Contains("L2") || name.Contains("Level 2") || name.Contains("Floor 2")) return "Level 2";
            if (name.Contains("L3") || name.Contains("Level 3") || name.Contains("Floor 3")) return "Level 3";

            // Priority 3: Check height-based level
            float height = obj.transform.position.y;
            if (height < 3f) return "Level 1";
            if (height < 6f) return "Level 2";
            if (height < 9f) return "Level 3";

            return "Unknown";
        }

        private HashSet<string> GetGroundTruthObjects(int questionIndex)
        {
            var ids = new HashSet<string>();

            if (sceneSnapshot == null) return ids;

            switch (questionIndex)
            {
                case 0: // Columns metadata
                case 4: // Column IDs and families
                    foreach (var elem in sceneSnapshot.BIMElements.Where(e => e.Category == "Column"))
                    {
                        ids.Add(elem.ElementId);
                    }
                    break;

                case 1: // Interior walls
                case 2: // Walls with missing fire rating
                case 3: // Wall areas
                    foreach (var elem in sceneSnapshot.BIMElements.Where(e => e.Category == "Wall"))
                    {
                        ids.Add(elem.ElementId);
                    }
                    break;

                case 6: // Cost estimation - all elements
                    foreach (var elem in sceneSnapshot.BIMElements)
                    {
                        ids.Add(elem.ElementId);
                    }
                    break;
            }

            return ids;
        }

        private HashSet<string> GetGroundTruthEntities(int questionIndex)
        {
            // Similar to GetGroundTruthObjects but returns entity names/identifiers
            var entities = new HashSet<string>();

            if (sceneSnapshot == null) return entities;

            switch (questionIndex)
            {
                case 0: // Columns metadata
                case 4: // Column IDs and families
                    foreach (var elem in sceneSnapshot.BIMElements.Where(e => e.Category == "Column"))
                    {
                        entities.Add(elem.Name);

                        // Add Family information if available
                        if (elem.Properties.ContainsKey("Family") && !string.IsNullOrEmpty(elem.Properties["Family"]))
                        {
                            entities.Add(elem.Properties["Family"]);
                        }

                        // Add Family Name if available (alternative property name)
                        if (elem.Properties.ContainsKey("FamilyName") && !string.IsNullOrEmpty(elem.Properties["FamilyName"]))
                        {
                            entities.Add(elem.Properties["FamilyName"]);
                        }

                        // Add Type information for columns
                        if (elem.Properties.ContainsKey("Type") && !string.IsNullOrEmpty(elem.Properties["Type"]))
                        {
                            entities.Add(elem.Properties["Type"]);
                        }

                        // Add Element ID for reference
                        if (!string.IsNullOrEmpty(elem.ElementId))
                        {
                            entities.Add(elem.ElementId);
                        }
                    }
                    break;

                case 1: // Interior walls
                case 2: // Walls with missing fire rating
                case 3: // Wall areas
                    foreach (var elem in sceneSnapshot.BIMElements.Where(e => e.Category == "Wall"))
                    {
                        entities.Add(elem.Name);

                        // Add wall type information
                        if (elem.Properties.ContainsKey("Type") && !string.IsNullOrEmpty(elem.Properties["Type"]))
                        {
                            entities.Add(elem.Properties["Type"]);
                        }

                        // Add Family information for walls
                        if (elem.Properties.ContainsKey("Family") && !string.IsNullOrEmpty(elem.Properties["Family"]))
                        {
                            entities.Add(elem.Properties["Family"]);
                        }
                    }
                    break;
            }

            return entities;
        }

        private HashSet<string> ExtractReferencedIds(string response)
        {
            var ids = new HashSet<string>();
            if (string.IsNullOrEmpty(response)) return ids;

            // Look for ID patterns - more flexible matching
            // Match patterns like: "ID: 12345", "element 12345", "id=12345", etc.
            var matches = System.Text.RegularExpressions.Regex.Matches(response,
                @"\b(?:id|ID|element|Element|column|Column|wall|Wall)[\s:=-]*([a-zA-Z0-9_-]{3,})\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                if (match.Groups.Count > 1)
                {
                    ids.Add(match.Groups[1].Value);
                }
            }

            // Also look for numeric IDs that appear standalone
            var numericMatches = System.Text.RegularExpressions.Regex.Matches(response,
                @"\b\d{5,}\b"); // Match 5+ digit numbers as potential element IDs

            foreach (System.Text.RegularExpressions.Match match in numericMatches)
            {
                ids.Add(match.Value);
            }

            return ids;
        }

        private HashSet<string> ExtractPredictedEntities(string response, int questionIndex)
        {
            var entities = new HashSet<string>();
            if (string.IsNullOrEmpty(response)) return entities;

            // Try to parse as JSON first
            try
            {
                var json = JToken.Parse(response);

                // Extract IDs, names, families from JSON structure
                ExtractEntitiesFromJson(json, entities);
            }
            catch
            {
                // Not valid JSON, use text parsing
            }

            // Extract entity names based on question type using text patterns
            switch (questionIndex)
            {
                case 0: // Columns metadata
                case 4: // Column IDs and families
                    // Look for column-related patterns
                    ExtractColumnEntities(response, entities);
                    break;

                case 1: // Interior walls
                case 2: // Missing fire rating walls
                case 3: // Wall areas
                    // Look for wall-related patterns
                    ExtractWallEntities(response, entities);
                    break;
            }

            return entities;
        }

        /// <summary>
        /// Recursively extracts entity values from JSON structure
        /// </summary>
        private void ExtractEntitiesFromJson(JToken token, HashSet<string> entities)
        {
            if (token == null) return;

            switch (token.Type)
            {
                case JTokenType.Object:
                    foreach (var prop in ((JObject)token).Properties())
                    {
                        // Extract values from known property names
                        if (prop.Name.Equals("ElementId", StringComparison.OrdinalIgnoreCase) ||
                            prop.Name.Equals("Id", StringComparison.OrdinalIgnoreCase) ||
                            prop.Name.Equals("Name", StringComparison.OrdinalIgnoreCase) ||
                            prop.Name.Equals("Family", StringComparison.OrdinalIgnoreCase) ||
                            prop.Name.Equals("FamilyName", StringComparison.OrdinalIgnoreCase) ||
                            prop.Name.Equals("Type", StringComparison.OrdinalIgnoreCase))
                        {
                            string value = prop.Value?.ToString();
                            if (!string.IsNullOrWhiteSpace(value))
                            {
                                entities.Add(value.Trim());
                            }
                        }

                        // Recurse into nested objects
                        ExtractEntitiesFromJson(prop.Value, entities);
                    }
                    break;

                case JTokenType.Array:
                    foreach (var item in (JArray)token)
                    {
                        ExtractEntitiesFromJson(item, entities);
                    }
                    break;
            }
        }

        /// <summary>
        /// Extracts column-related entities from text
        /// </summary>
        private void ExtractColumnEntities(string response, HashSet<string> entities)
        {
            // Pattern 1: "Column [name/id]" - more flexible
            var columnMatches = System.Text.RegularExpressions.Regex.Matches(response,
                @"\b[Cc]olumn[s]?\s*[:-]?\s*([A-Za-z0-9_-]+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            foreach (System.Text.RegularExpressions.Match match in columnMatches)
            {
                if (match.Groups.Count > 1 && match.Groups[1].Value.Length > 0)
                {
                    entities.Add(match.Groups[1].Value);
                }
            }

            // Pattern 2: "Family: [name]" or "Family Name: [name]"
            var familyMatches = System.Text.RegularExpressions.Regex.Matches(response,
                @"\b[Ff]amily\s*[Nn]ame?\s*[:-]\s*([A-Za-z0-9_\s-]+?)(?:\n|,|;|\.|$)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            foreach (System.Text.RegularExpressions.Match match in familyMatches)
            {
                if (match.Groups.Count > 1 && match.Groups[1].Value.Trim().Length > 0)
                {
                    entities.Add(match.Groups[1].Value.Trim());
                }
            }

            // Pattern 3: Look for quoted text that might be entity names
            var quotedMatches = System.Text.RegularExpressions.Regex.Matches(response,
                @"""([^""]+)""|'([^']+)'");
            foreach (System.Text.RegularExpressions.Match match in quotedMatches)
            {
                for (int i = 1; i < match.Groups.Count; i++)
                {
                    if (match.Groups[i].Success && match.Groups[i].Value.Length > 0)
                    {
                        entities.Add(match.Groups[i].Value);
                    }
                }
            }

            // Pattern 4: Element IDs
            ExtractElementIds(response, entities);
        }

        /// <summary>
        /// Extracts wall-related entities from text
        /// </summary>
        private void ExtractWallEntities(string response, HashSet<string> entities)
        {
            // Pattern 1: "Wall [name/id]" - more flexible
            var wallMatches = System.Text.RegularExpressions.Regex.Matches(response,
                @"\b[Ww]all[s]?\s*[:-]?\s*([A-Za-z0-9_-]+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            foreach (System.Text.RegularExpressions.Match match in wallMatches)
            {
                if (match.Groups.Count > 1 && match.Groups[1].Value.Length > 0)
                {
                    entities.Add(match.Groups[1].Value);
                }
            }

            // Pattern 2: Wall types
            var typeMatches = System.Text.RegularExpressions.Regex.Matches(response,
                @"\b[Tt]ype\s*[:-]\s*([A-Za-z0-9_\s-]+?)(?:\n|,|;|\.|$)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            foreach (System.Text.RegularExpressions.Match match in typeMatches)
            {
                if (match.Groups.Count > 1 && match.Groups[1].Value.Trim().Length > 0)
                {
                    entities.Add(match.Groups[1].Value.Trim());
                }
            }

            // Pattern 3: Look for fire rating mentions (relevant for question 2)
            var fireRatingMatches = System.Text.RegularExpressions.Regex.Matches(response,
                @"\b[Ff]ire\s*[Rr]ating\s*[:-]?\s*([A-Za-z0-9_\s-]+?)(?:\n|,|;|\.|$)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            foreach (System.Text.RegularExpressions.Match match in fireRatingMatches)
            {
                if (match.Groups.Count > 1 && match.Groups[1].Value.Trim().Length > 0)
                {
                    entities.Add(match.Groups[1].Value.Trim());
                }
            }

            // Pattern 4: Look for quoted text that might be entity names
            var quotedMatches = System.Text.RegularExpressions.Regex.Matches(response,
                @"""([^""]+)""|'([^']+)'");
            foreach (System.Text.RegularExpressions.Match match in quotedMatches)
            {
                for (int i = 1; i < match.Groups.Count; i++)
                {
                    if (match.Groups[i].Success && match.Groups[i].Value.Length > 0)
                    {
                        entities.Add(match.Groups[i].Value);
                    }
                }
            }

            // Extract element IDs
            ExtractElementIds(response, entities);
        }

        /// <summary>
        /// Extracts element IDs from text
        /// </summary>
        private void ExtractElementIds(string response, HashSet<string> entities)
        {
            var idMatches = System.Text.RegularExpressions.Regex.Matches(response,
                @"\b(?:Element\s*ID|ID|ElementId)\s*[:-]\s*([A-Za-z0-9_-]+)");
            foreach (System.Text.RegularExpressions.Match match in idMatches)
            {
                if (match.Groups.Count > 1)
                {
                    entities.Add(match.Groups[1].Value);
                }
            }
        }

        private HashSet<string> GetAllSceneObjectIds()
        {
            var ids = new HashSet<string>();

            if (sceneSnapshot != null)
            {
                foreach (var elem in sceneSnapshot.BIMElements)
                {
                    ids.Add(elem.ElementId);
                }
            }

            return ids;
        }

        #endregion

        #region Report Generation

        private void SaveSceneSnapshot(SceneSnapshot snapshot)
        {
            string path = Path.Combine(currentOutputPath, "scene_snapshot.json");

            // Use JSON settings to prevent circular reference issues
            var settings = new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore
            };

            string json = JsonConvert.SerializeObject(snapshot, settings);
            File.WriteAllText(path, json);
            Debug.Log($"[LLMEvaluation] Scene snapshot saved: {path}");
        }

        private void SaveModelResults(ModelEvaluationResults results, ModelConfig config)
        {
            string baseName = results.ModelName.Replace(" ", "_").Replace(".", "_");

            // Use JSON settings to prevent circular reference issues
            var jsonSettings = new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                NullValueHandling = NullValueHandling.Ignore
            };

            var jsonSettingsIndented = new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                NullValueHandling = NullValueHandling.Ignore,
                Formatting = Formatting.Indented
            };

            // 1. Save complete JSON results (full model evaluation data)
            string jsonPath = Path.Combine(currentOutputPath, $"results_{baseName}.json");
            string completeJson = JsonConvert.SerializeObject(results, jsonSettingsIndented);
            File.WriteAllText(jsonPath, completeJson);

            // 2. Save JSONL dialogue log (one turn per line)
            string jsonlPath = Path.Combine(currentOutputPath, $"dialogue_{baseName}.jsonl");
            using (var writer = new StreamWriter(jsonlPath))
            {
                foreach (var turn in results.TurnResults)
                {
                    var turnJson = JsonConvert.SerializeObject(turn, jsonSettings);
                    writer.WriteLine(turnJson);
                }
            }

            // 3. Save human-readable conversation report
            string conversationPath = Path.Combine(currentOutputPath, $"conversation_{baseName}.txt");
            SaveConversationReport(results, conversationPath);

            // 4. Save CSV summary
            string csvPath = Path.Combine(currentOutputPath, $"summary_{baseName}.csv");
            using (var writer = new StreamWriter(csvPath))
            {
                // Header
                writer.WriteLine("Turn,Question,Response,LatencyMs,PromptTokens,CompletionTokens,CostUSD," +
                               "ValidJSON,FormatAdherence,GroundingOverlap,EntityF1,ExactMatch,RefusalCorrectness,Hallucination");

                // Turn data
                foreach (var turn in results.TurnResults)
                {
                    writer.WriteLine($"{turn.TurnNumber}," +
                                   $"\"{EscapeCsv(turn.Question)}\"," +
                                   $"\"{EscapeCsv(turn.Response)}\"," +
                                   $"{turn.LatencyMs:F2}," +
                                   $"{turn.PromptTokens}," +
                                   $"{turn.CompletionTokens}," +
                                   $"{turn.CostUSD:F6}," +
                                   $"{turn.IsValidJSON}," +
                                   $"{turn.FormatAdherence:F4}," +
                                   $"{turn.GroundingOverlap:F4}," +
                                   $"{turn.EntityF1:F4}," +
                                   $"{turn.ExactMatch:F4}," +
                                   $"{turn.RefusalCorrectness}," +
                                   $"{turn.ContainsHallucination}");
                }

                // Aggregate metrics
                writer.WriteLine();
                writer.WriteLine("Aggregate Metrics");
                writer.WriteLine($"Task Success Rate,{results.TaskSuccessRate:F4}");
                writer.WriteLine($"Hallucination Rate,{results.HallucinationRate:F4}");
                writer.WriteLine($"Average Latency (ms),{results.AverageLatencyMs:F2}");
                writer.WriteLine($"Latency P50 (ms),{results.LatencyP50:F2}");
                writer.WriteLine($"Latency P95 (ms),{results.LatencyP95:F2}");
                writer.WriteLine($"Total Cost (USD),${results.TotalCostUSD:F4}");
                writer.WriteLine($"Tokens/Second,{results.TokensPerSecond:F2}");
                writer.WriteLine($"Memory Carry-Over Success,{results.MemoryCarryOverSuccess:F4}");
            }

            Debug.Log($"[LLMEvaluation] Model results saved:");
            Debug.Log($"  - JSON: {jsonPath}");
            Debug.Log($"  - JSONL: {jsonlPath}");
            Debug.Log($"  - Conversation: {conversationPath}");
            Debug.Log($"  - CSV: {csvPath}");
        }

        private void SaveConversationReport(ModelEvaluationResults results, string filePath)
        {
            using (var writer = new StreamWriter(filePath))
            {
                // Header
                writer.WriteLine("═══════════════════════════════════════════════════════════════");
                writer.WriteLine($"    CONVERSATION REPORT - {results.ModelName}");
                writer.WriteLine("═══════════════════════════════════════════════════════════════");
                writer.WriteLine();
                writer.WriteLine($"Model: {results.ModelName}");
                writer.WriteLine($"Provider: {results.Provider}");
                writer.WriteLine($"Session: {results.SessionId}");
                writer.WriteLine($"Started: {results.StartTime:yyyy-MM-dd HH:mm:ss}");
                writer.WriteLine($"Ended: {results.EndTime:yyyy-MM-dd HH:mm:ss}");
                writer.WriteLine($"Duration: {(results.EndTime - results.StartTime).TotalMinutes:F1} minutes");
                writer.WriteLine();
                writer.WriteLine("───────────────────────────────────────────────────────────────");
                writer.WriteLine("PERFORMANCE SUMMARY");
                writer.WriteLine("───────────────────────────────────────────────────────────────");
                writer.WriteLine($"Task Success Rate:    {results.TaskSuccessRate:P1}");
                writer.WriteLine($"Hallucination Rate:   {results.HallucinationRate:P1}");
                writer.WriteLine($"Average Latency:      {results.AverageLatencyMs:F0} ms");
                writer.WriteLine($"Total Cost:           ${results.TotalCostUSD:F4}");
                writer.WriteLine($"Total Tokens:         {results.TotalPromptTokens + results.TotalCompletionTokens:N0}");
                writer.WriteLine();
                writer.WriteLine("═══════════════════════════════════════════════════════════════");
                writer.WriteLine("CONVERSATION TRANSCRIPT");
                writer.WriteLine("═══════════════════════════════════════════════════════════════");
                writer.WriteLine();

                // Conversation turns
                foreach (var turn in results.TurnResults)
                {
                    writer.WriteLine($"┌─ TURN {turn.TurnNumber} ─────────────────────────────────────────────────────────");
                    writer.WriteLine($"│ Time: {turn.QuestionTime:HH:mm:ss}");
                    writer.WriteLine($"│ Latency: {turn.LatencyMs:F0} ms | Cost: ${turn.CostUSD:F6}");
                    writer.WriteLine("└────────────────────────────────────────────────────────────────");
                    writer.WriteLine();
                    writer.WriteLine("👤 USER:");
                    writer.WriteLine(WrapText(turn.Question, 70, "   "));
                    writer.WriteLine();
                    writer.WriteLine($"🤖 {results.ModelName.ToUpper()}:");
                    writer.WriteLine(WrapText(turn.Response, 70, "   "));
                    writer.WriteLine();

                    // Metrics for this turn
                    writer.WriteLine("📊 METRICS:");
                    writer.WriteLine($"   • JSON Valid: {(turn.IsValidJSON ? "✓" : "✗")}");
                    writer.WriteLine($"   • Format Adherence: {turn.FormatAdherence:P0}");
                    writer.WriteLine($"   • Grounding Overlap: {turn.GroundingOverlap:P0}");
                    writer.WriteLine($"   • Entity F1 Score: {turn.EntityF1:F2}");
                    writer.WriteLine($"   • Exact Match: {(turn.ExactMatch > 0.9 ? "✓" : "✗")}");

                    if (turn.RefusalCorrectness != 0)
                    {
                        string refusalStatus = turn.RefusalCorrectness > 0 ? "✓ Correct Refusal" : "✗ Hallucination";
                        writer.WriteLine($"   • Refusal Handling: {refusalStatus}");
                    }

                    if (turn.ContainsHallucination)
                    {
                        writer.WriteLine($"   • ⚠️ HALLUCINATION DETECTED");
                    }

                    writer.WriteLine($"   • Tokens: {turn.PromptTokens} in / {turn.CompletionTokens} out");
                    writer.WriteLine();
                    writer.WriteLine();
                }

                // Footer with aggregate metrics
                writer.WriteLine("═══════════════════════════════════════════════════════════════");
                writer.WriteLine("DETAILED METRICS");
                writer.WriteLine("═══════════════════════════════════════════════════════════════");
                writer.WriteLine();
                writer.WriteLine("Task Performance:");
                writer.WriteLine($"  • Successful Turns: {results.TurnResults.Count(t => !t.ContainsHallucination && t.FormatAdherence > 0.7)} / {results.TurnResults.Count}");
                writer.WriteLine($"  • Average Entity F1: {results.AverageEntityF1:F3}");
                writer.WriteLine($"  • Average Grounding: {results.AverageGroundingOverlap:F3}");
                writer.WriteLine($"  • Average Format Adherence: {results.AverageFormatAdherence:F3}");
                writer.WriteLine($"  • Memory Carry-Over: {results.MemoryCarryOverSuccess:F3}");
                writer.WriteLine();
                writer.WriteLine("Response Times:");
                writer.WriteLine($"  • Average: {results.AverageLatencyMs:F0} ms");
                writer.WriteLine($"  • Median (P50): {results.LatencyP50:F0} ms");
                writer.WriteLine($"  • 95th Percentile (P95): {results.LatencyP95:F0} ms");
                writer.WriteLine();
                writer.WriteLine("Cost Breakdown:");
                writer.WriteLine($"  • Prompt Tokens: {results.TotalPromptTokens:N0}");
                writer.WriteLine($"  • Completion Tokens: {results.TotalCompletionTokens:N0}");
                writer.WriteLine($"  • Total Tokens: {results.TotalPromptTokens + results.TotalCompletionTokens:N0}");
                writer.WriteLine($"  • Throughput: {results.TokensPerSecond:F1} tokens/sec");
                writer.WriteLine($"  • Total Cost: ${results.TotalCostUSD:F4}");
                writer.WriteLine($"  • Cost per Turn: ${results.TotalCostUSD / results.TurnResults.Count:F6}");
                writer.WriteLine();
                writer.WriteLine("═══════════════════════════════════════════════════════════════");
                writer.WriteLine($"Report generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                writer.WriteLine("═══════════════════════════════════════════════════════════════");
            }
        }

        private string WrapText(string text, int maxWidth, string indent)
        {
            if (string.IsNullOrEmpty(text)) return indent + "[No response]";

            var result = new StringBuilder();
            var words = text.Split(new[] { ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var currentLine = new StringBuilder(indent);
            int currentWidth = indent.Length;

            foreach (var word in words)
            {
                if (currentWidth + word.Length + 1 > maxWidth && currentWidth > indent.Length)
                {
                    result.AppendLine(currentLine.ToString());
                    currentLine.Clear();
                    currentLine.Append(indent);
                    currentWidth = indent.Length;
                }

                if (currentWidth > indent.Length)
                {
                    currentLine.Append(" ");
                    currentWidth++;
                }

                currentLine.Append(word);
                currentWidth += word.Length;
            }

            if (currentLine.Length > indent.Length)
            {
                result.AppendLine(currentLine.ToString());
            }

            return result.ToString().TrimEnd('\r', '\n');
        }

        /// <summary>
        /// Append a single raw run to the CSV file for ANOVA analysis
        /// </summary>
        private void AppendRawRunToCSV(RawRunData run)
        {
            string csvPath = Path.Combine(currentOutputPath, "llm_runs.csv");
            bool fileExists = File.Exists(csvPath);

            using (var writer = new StreamWriter(csvPath, append: true))
            {
                // Write header if file doesn't exist
                if (!fileExists)
                {
                    writer.WriteLine("model_name,question_id,run_index,task_success,hallucination_rate,latency_ms,total_cost_usd,tokens_per_sec,entity_f1,grounding_overlap,format_adherence");
                }

                // Write data row
                writer.WriteLine($"{run.ModelName},{run.QuestionId},{run.RunIndex},{(run.TaskSuccess ? 1 : 0)},{run.HallucinationRate:F6},{run.LatencyMs:F2},{run.TotalCostUsd:F8},{run.TokensPerSec:F2},{run.EntityF1:F6},{run.GroundingOverlap:F6},{run.FormatAdherence:F6}");
            }
        }

        private void GenerateLeaderboard()
        {
            if (modelResults.Count == 0) return;

            // Rank models based on overall performance score
            // Score considers: Task Success (40%), Information Retrieval Quality (30%), 
            // Hallucination Rate (20%), and Speed/Cost (10%)
            var ranked = modelResults.Values
                .Select(r => new
                {
                    Result = r,
                    OverallScore = CalculateOverallScore(r)
                })
                .OrderByDescending(x => x.OverallScore)
                .Select(x => x.Result)
                .ToList();

            // Save leaderboard CSV
            string csvPath = Path.Combine(currentOutputPath, "leaderboard.csv");
            using (var writer = new StreamWriter(csvPath))
            {
                writer.WriteLine("Rank,Model,Provider,TaskSuccess,HallucinationRate,LatencyP50,LatencyP95," +
                               "TotalCost,TokensPerSec,EntityF1,GroundingOverlap,FormatAdherence,MemoryCarryOver");

                int rank = 1;
                foreach (var result in ranked)
                {
                    writer.WriteLine($"{rank}," +
                                   $"{result.ModelName}," +
                                   $"{result.Provider}," +
                                   $"{result.TaskSuccessRate:F4}," +
                                   $"{result.HallucinationRate:F4}," +
                                   $"{result.LatencyP50:F2}," +
                                   $"{result.LatencyP95:F2}," +
                                   $"${result.TotalCostUSD:F4}," +
                                   $"{result.TokensPerSecond:F2}," +
                                   $"{result.AverageEntityF1:F4}," +
                                   $"{result.AverageGroundingOverlap:F4}," +
                                   $"{result.AverageFormatAdherence:F4}," +
                                   $"{result.MemoryCarryOverSuccess:F4}");
                    rank++;
                }
            }

            Debug.Log($"[LLMEvaluation] Leaderboard saved: {csvPath}");
        }

        private void GenerateFinalReport()
        {
            if (modelResults.Count == 0) return;

            var ranked = modelResults.Values
                .Select(r => new
                {
                    Result = r,
                    OverallScore = CalculateOverallScore(r)
                })
                .OrderByDescending(x => x.OverallScore)
                .Select(x => x.Result)
                .ToList();

            string reportPath = Path.Combine(currentOutputPath, "report.txt");
            using (var writer = new StreamWriter(reportPath))
            {
                writer.WriteLine("═══════════════════════════════════════════════════════════════");
                writer.WriteLine("    LLM EVALUATION REPORT - AEC/BIM BENCHMARKING RESULTS");
                writer.WriteLine("═══════════════════════════════════════════════════════════════");
                writer.WriteLine();
                writer.WriteLine($"Session ID: {currentSessionId}");
                writer.WriteLine($"Evaluation Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                writer.WriteLine($"Scene Hash: {sceneSnapshot?.SceneHash ?? "N/A"}");
                writer.WriteLine($"Models Evaluated: {modelResults.Count}");
                writer.WriteLine($"Questions Per Model: {evaluationQuestions.Length}");
                writer.WriteLine();

                writer.WriteLine("───────────────────────────────────────────────────────────────");
                writer.WriteLine("LEADERBOARD");
                writer.WriteLine("───────────────────────────────────────────────────────────────");
                writer.WriteLine();

                int rank = 1;
                foreach (var result in ranked)
                {
                    writer.WriteLine($"#{rank} - {result.ModelName} ({result.Provider})");
                    writer.WriteLine($"     Task Success:      {result.TaskSuccessRate:P2}");
                    writer.WriteLine($"     Hallucination:     {result.HallucinationRate:P2}");
                    writer.WriteLine($"     Latency (P50):     {result.LatencyP50:F0} ms");
                    writer.WriteLine($"     Total Cost:        ${result.TotalCostUSD:F4}");
                    writer.WriteLine($"     Tokens/Sec:        {result.TokensPerSecond:F1}");
                    writer.WriteLine();
                    rank++;
                }

                writer.WriteLine("───────────────────────────────────────────────────────────────");
                writer.WriteLine("DETAILED METRICS SUMMARY");
                writer.WriteLine("───────────────────────────────────────────────────────────────");
                writer.WriteLine();

                foreach (var result in ranked)
                {
                    writer.WriteLine($"Model: {result.ModelName}");
                    writer.WriteLine($"  Performance Metrics:");
                    writer.WriteLine($"    - Entity F1 Score:        {result.AverageEntityF1:F4}");
                    writer.WriteLine($"    - Grounding Overlap:      {result.AverageGroundingOverlap:F4}");
                    writer.WriteLine($"    - Format Adherence:       {result.AverageFormatAdherence:F4}");
                    writer.WriteLine($"    - Memory Carry-Over:      {result.MemoryCarryOverSuccess:F4}");
                    writer.WriteLine($"  Efficiency Metrics:");
                    writer.WriteLine($"    - Average Latency:        {result.AverageLatencyMs:F0} ms");
                    writer.WriteLine($"    - Latency P95:            {result.LatencyP95:F0} ms");
                    writer.WriteLine($"    - Total Tokens:           {result.TotalPromptTokens + result.TotalCompletionTokens:N0}");
                    writer.WriteLine($"    - Cost per Question:      ${result.TotalCostUSD / evaluationQuestions.Length:F5}");
                    writer.WriteLine();
                }

                writer.WriteLine("───────────────────────────────────────────────────────────────");
                writer.WriteLine("EVALUATION QUESTIONS");
                writer.WriteLine("───────────────────────────────────────────────────────────────");
                writer.WriteLine();

                for (int i = 0; i < evaluationQuestions.Length; i++)
                {
                    writer.WriteLine($"{i + 1}. {evaluationQuestions[i]}");
                }

                writer.WriteLine();
                writer.WriteLine("═══════════════════════════════════════════════════════════════");
                writer.WriteLine($"Report generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                writer.WriteLine($"Full results available at: {currentOutputPath}");
                writer.WriteLine("═══════════════════════════════════════════════════════════════");
            }

            Debug.Log($"[LLMEvaluation] Final report saved: {reportPath}");
        }

        private void GenerateMetricsComparisonReport()
        {
            if (modelResults.Count == 0) return;

            var ranked = modelResults.Values
                .Select(r => new
                {
                    Result = r,
                    OverallScore = CalculateOverallScore(r)
                })
                .OrderByDescending(x => x.OverallScore)
                .Select(x => x.Result)
                .ToList();

            string reportPath = Path.Combine(currentOutputPath, "metrics_comparison_report.md");
            using (var writer = new StreamWriter(reportPath))
            {
                // Header
                writer.WriteLine("# LLM Metrics Comparison Report");
                writer.WriteLine($"**Generated:** {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                writer.WriteLine($"**Session ID:** {currentSessionId}");
                writer.WriteLine($"**Models Evaluated:** {modelResults.Count}");
                writer.WriteLine($"**Questions Per Model:** {evaluationQuestions.Length}");
                writer.WriteLine();
                writer.WriteLine("---");
                writer.WriteLine();

                // Executive Summary
                writer.WriteLine("## Executive Summary");
                writer.WriteLine();
                if (ranked.Count > 0)
                {
                    var best = ranked[0];
                    var fastest = ranked.OrderBy(r => r.LatencyP50).First();
                    var cheapest = ranked.OrderBy(r => r.TotalCostUSD).First();
                    var mostAccurate = ranked.OrderByDescending(r => r.AverageEntityF1).First();

                    writer.WriteLine($"**🏆 Best Overall Performance:** {best.ModelName} ({best.Provider})");
                    writer.WriteLine($"- Task Success Rate: {best.TaskSuccessRate:P2}");
                    writer.WriteLine($"- Hallucination Rate: {best.HallucinationRate:P2}");
                    writer.WriteLine();
                    writer.WriteLine($"**⚡ Fastest Response:** {fastest.ModelName} ({fastest.Provider})");
                    writer.WriteLine($"- P50 Latency: {fastest.LatencyP50:F0} ms");
                    writer.WriteLine($"- Throughput: {fastest.TokensPerSecond:F1} tokens/sec");
                    writer.WriteLine();
                    writer.WriteLine($"**💰 Most Cost-Effective:** {cheapest.ModelName} ({cheapest.Provider})");
                    writer.WriteLine($"- Total Cost: ${cheapest.TotalCostUSD:F4}");
                    writer.WriteLine($"- Cost per Question: ${cheapest.TotalCostUSD / evaluationQuestions.Length:F5}");
                    writer.WriteLine();
                    writer.WriteLine($"**🎯 Most Accurate:** {mostAccurate.ModelName} ({mostAccurate.Provider})");
                    writer.WriteLine($"- Entity F1 Score: {mostAccurate.AverageEntityF1:F4}");
                    writer.WriteLine($"- Grounding Overlap: {mostAccurate.AverageGroundingOverlap:F4}");
                    writer.WriteLine();
                }
                writer.WriteLine("---");
                writer.WriteLine();

                // Overall Rankings
                writer.WriteLine("## Overall Rankings");
                writer.WriteLine();
                writer.WriteLine("| Rank | Model | Provider | Task Success | Hallucination Rate | Latency (P50) | Total Cost |");
                writer.WriteLine("|------|-------|----------|--------------|-------------------|---------------|------------|");

                int rank = 1;
                foreach (var result in ranked)
                {
                    writer.WriteLine($"| {rank} | {result.ModelName} | {result.Provider} | {result.TaskSuccessRate:P2} | {result.HallucinationRate:P2} | {result.LatencyP50:F0} ms | ${result.TotalCostUSD:F4} |");
                    rank++;
                }
                writer.WriteLine();
                writer.WriteLine("---");
                writer.WriteLine();

                // Performance Metrics Comparison
                writer.WriteLine("## Performance Metrics Comparison");
                writer.WriteLine();
                writer.WriteLine("### Accuracy & Reliability");
                writer.WriteLine();
                writer.WriteLine("| Model | Entity F1 | Grounding Overlap | Format Adherence | Memory Carry-Over |");
                writer.WriteLine("|-------|-----------|-------------------|------------------|-------------------|");

                foreach (var result in ranked)
                {
                    writer.WriteLine($"| {result.ModelName} | {result.AverageEntityF1:F4} | {result.AverageGroundingOverlap:F4} | {result.AverageFormatAdherence:F4} | {result.MemoryCarryOverSuccess:F4} |");
                }
                writer.WriteLine();

                // Latency Comparison
                writer.WriteLine("### Latency & Speed");
                writer.WriteLine();
                writer.WriteLine("| Model | Avg Latency | P50 Latency | P95 Latency | Tokens/Sec |");
                writer.WriteLine("|-------|-------------|-------------|-------------|------------|");

                foreach (var result in ranked)
                {
                    writer.WriteLine($"| {result.ModelName} | {result.AverageLatencyMs:F0} ms | {result.LatencyP50:F0} ms | {result.LatencyP95:F0} ms | {result.TokensPerSecond:F1} |");
                }
                writer.WriteLine();

                // Cost Analysis
                writer.WriteLine("### Cost Analysis");
                writer.WriteLine();
                writer.WriteLine("| Model | Total Tokens | Total Cost | Cost/Question | Cost/1K Tokens |");
                writer.WriteLine("|-------|--------------|------------|---------------|----------------|");

                foreach (var result in ranked)
                {
                    int totalTokens = result.TotalPromptTokens + result.TotalCompletionTokens;
                    double costPerQuestion = result.TotalCostUSD / evaluationQuestions.Length;
                    double costPer1KTokens = totalTokens > 0 ? (result.TotalCostUSD / totalTokens) * 1000 : 0;

                    writer.WriteLine($"| {result.ModelName} | {totalTokens:N0} | ${result.TotalCostUSD:F4} | ${costPerQuestion:F5} | ${costPer1KTokens:F5} |");
                }
                writer.WriteLine();
                writer.WriteLine("---");
                writer.WriteLine();

                // Detailed Model Analysis
                writer.WriteLine("## Detailed Model Analysis");
                writer.WriteLine();

                foreach (var result in ranked)
                {
                    writer.WriteLine($"### {result.ModelName} ({result.Provider})");
                    writer.WriteLine();

                    // Performance Score Card
                    writer.WriteLine("**Performance Scorecard:**");
                    writer.WriteLine($"- ✅ Task Success Rate: {result.TaskSuccessRate:P2} ({GenerateBar(result.TaskSuccessRate, 1.0)})");
                    writer.WriteLine($"- ❌ Hallucination Rate: {result.HallucinationRate:P2} ({GenerateBar(result.HallucinationRate, 1.0, inverse: true)})");
                    writer.WriteLine($"- 🎯 Entity F1 Score: {result.AverageEntityF1:F4} ({GenerateBar(result.AverageEntityF1, 1.0)})");
                    writer.WriteLine($"- 🔗 Grounding Overlap: {result.AverageGroundingOverlap:F4} ({GenerateBar(result.AverageGroundingOverlap, 1.0)})");
                    writer.WriteLine($"- 📋 Format Adherence: {result.AverageFormatAdherence:F4} ({GenerateBar(result.AverageFormatAdherence, 1.0)})");
                    writer.WriteLine($"- 🧠 Memory Carry-Over: {result.MemoryCarryOverSuccess:F4} ({GenerateBar(result.MemoryCarryOverSuccess, 1.0)})");
                    writer.WriteLine();

                    // Speed & Efficiency
                    writer.WriteLine("**Speed & Efficiency:**");
                    writer.WriteLine($"- ⚡ Average Latency: {result.AverageLatencyMs:F0} ms");
                    writer.WriteLine($"- 📊 P50 Latency: {result.LatencyP50:F0} ms");
                    writer.WriteLine($"- 📈 P95 Latency: {result.LatencyP95:F0} ms");
                    writer.WriteLine($"- 🚀 Throughput: {result.TokensPerSecond:F1} tokens/sec");
                    writer.WriteLine();

                    // Cost Breakdown
                    writer.WriteLine("**Cost Breakdown:**");
                    int totalTokens = result.TotalPromptTokens + result.TotalCompletionTokens;
                    writer.WriteLine($"- 💵 Total Cost: ${result.TotalCostUSD:F4}");
                    writer.WriteLine($"- 📝 Prompt Tokens: {result.TotalPromptTokens:N0}");
                    writer.WriteLine($"- 💬 Completion Tokens: {result.TotalCompletionTokens:N0}");
                    writer.WriteLine($"- 📊 Total Tokens: {totalTokens:N0}");
                    writer.WriteLine($"- 💰 Cost per Question: ${result.TotalCostUSD / evaluationQuestions.Length:F5}");
                    writer.WriteLine();

                    // Strengths & Weaknesses
                    writer.WriteLine("**Key Insights:**");

                    // Identify strengths
                    List<string> strengths = new List<string>();
                    if (result.TaskSuccessRate >= 0.9) strengths.Add("High task success rate");
                    if (result.HallucinationRate <= 0.1) strengths.Add("Low hallucination rate");
                    if (result.LatencyP50 <= 1000) strengths.Add("Fast response time");
                    if (result.AverageEntityF1 >= 0.8) strengths.Add("High accuracy");
                    if (result.TotalCostUSD <= ranked.Average(r => r.TotalCostUSD)) strengths.Add("Cost-effective");

                    if (strengths.Count > 0)
                    {
                        writer.WriteLine("- ✅ **Strengths:** " + string.Join(", ", strengths));
                    }

                    // Identify weaknesses
                    List<string> weaknesses = new List<string>();
                    if (result.TaskSuccessRate < 0.7) weaknesses.Add("Low task success rate");
                    if (result.HallucinationRate > 0.3) weaknesses.Add("High hallucination rate");
                    if (result.LatencyP50 > 3000) weaknesses.Add("Slow response time");
                    if (result.AverageEntityF1 < 0.5) weaknesses.Add("Low accuracy");
                    if (result.TotalCostUSD > ranked.Average(r => r.TotalCostUSD) * 1.5) weaknesses.Add("Expensive");

                    if (weaknesses.Count > 0)
                    {
                        writer.WriteLine("- ⚠️ **Areas for Improvement:** " + string.Join(", ", weaknesses));
                    }

                    writer.WriteLine();
                    writer.WriteLine("---");
                    writer.WriteLine();
                }

                // Question-by-Question Analysis
                writer.WriteLine("## Question-by-Question Analysis");
                writer.WriteLine();

                for (int qIdx = 0; qIdx < evaluationQuestions.Length; qIdx++)
                {
                    writer.WriteLine($"### Question {qIdx + 1}: {evaluationQuestions[qIdx]}");
                    writer.WriteLine();
                    writer.WriteLine("| Model | Success | Entity F1 | Latency | Hallucination |");
                    writer.WriteLine("|-------|---------|-----------|---------|---------------|");

                    foreach (var result in ranked)
                    {
                        if (result.TurnResults != null && qIdx < result.TurnResults.Count)
                        {
                            var turn = result.TurnResults[qIdx];
                            string success = turn.RefusalCorrectness >= 0 ? "✅" : "❌";
                            string hallucination = turn.ContainsHallucination ? "⚠️ Yes" : "✅ No";

                            writer.WriteLine($"| {result.ModelName} | {success} | {turn.EntityF1:F4} | {turn.LatencyMs:F0} ms | {hallucination} |");
                        }
                    }
                    writer.WriteLine();
                }

                writer.WriteLine("---");
                writer.WriteLine();

                // Recommendations
                writer.WriteLine("## Recommendations");
                writer.WriteLine();

                if (ranked.Count > 0)
                {
                    writer.WriteLine("### Use Case Recommendations");
                    writer.WriteLine();

                    var productionModel = ranked.FirstOrDefault(r => r.TaskSuccessRate >= 0.85 && r.HallucinationRate <= 0.15);
                    if (productionModel != null)
                    {
                        writer.WriteLine($"**🏢 Production/Enterprise Use:** {productionModel.ModelName}");
                        writer.WriteLine($"- Excellent reliability and low hallucination rate");
                        writer.WriteLine();
                    }

                    var prototypingModel = ranked.OrderBy(r => r.TotalCostUSD).FirstOrDefault();
                    if (prototypingModel != null)
                    {
                        writer.WriteLine($"**🛠️ Development/Prototyping:** {prototypingModel.ModelName}");
                        writer.WriteLine($"- Most cost-effective for testing and iteration");
                        writer.WriteLine();
                    }

                    var realtimeModel = ranked.OrderBy(r => r.LatencyP50).FirstOrDefault();
                    if (realtimeModel != null)
                    {
                        writer.WriteLine($"**⚡ Real-time Applications:** {realtimeModel.ModelName}");
                        writer.WriteLine($"- Fastest response time for interactive scenarios");
                        writer.WriteLine();
                    }

                    var accuracyModel = ranked.OrderByDescending(r => r.AverageEntityF1).FirstOrDefault();
                    if (accuracyModel != null)
                    {
                        writer.WriteLine($"**🎯 High-Accuracy Requirements:** {accuracyModel.ModelName}");
                        writer.WriteLine($"- Best for critical BIM metadata extraction");
                        writer.WriteLine();
                    }
                }

                writer.WriteLine("---");
                writer.WriteLine();

                // Footer
                writer.WriteLine("## Evaluation Details");
                writer.WriteLine();
                writer.WriteLine($"- **Scene Hash:** {sceneSnapshot?.SceneHash ?? "N/A"}");
                writer.WriteLine($"- **Evaluation Start:** {evaluationStartTime:yyyy-MM-dd HH:mm:ss}");
                writer.WriteLine($"- **Evaluation End:** {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                writer.WriteLine($"- **Total Duration:** {(DateTime.Now - evaluationStartTime).TotalMinutes:F2} minutes");
                writer.WriteLine($"- **Output Directory:** `{currentOutputPath}`");
                writer.WriteLine();
                writer.WriteLine("---");
                writer.WriteLine();
                writer.WriteLine("*Report generated automatically by LLM Evaluation System*");
            }

            Debug.Log($"[LLMEvaluation] Metrics comparison report saved: {reportPath}");
        }

        /// <summary>
        /// Generates a simple text-based progress bar for visualizing metrics
        /// </summary>
        private string GenerateBar(double value, double max, bool inverse = false)
        {
            const int barLength = 20;
            double normalizedValue = inverse ? (max - value) / max : value / max;
            normalizedValue = Math.Max(0, Math.Min(1, normalizedValue));

            int filledLength = (int)(normalizedValue * barLength);
            int emptyLength = barLength - filledLength;

            string bar = new string('█', filledLength) + new string('░', emptyLength);
            return $"{bar} {normalizedValue:P0}";
        }

        private string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Replace("\"", "\"\"");
        }

        #endregion

        #region Statistical Analysis

        private void GenerateStatisticalAnalysisReport()
        {
            if (modelResults.Count < 2)
            {
                Debug.LogWarning("[LLMEvaluation] Need at least 2 models for statistical analysis");
                return;
            }

            if (allRawRuns.Count == 0)
            {
                Debug.LogWarning("[LLMEvaluation] No raw run data available for ANOVA analysis");
                return;
            }

            string reportPath = Path.Combine(currentOutputPath, "statistical_analysis.md");
            string anovaSummaryPath = Path.Combine(currentOutputPath, "anova_summary.json");

            using (var writer = new StreamWriter(reportPath))
            {
                writer.WriteLine("# Statistical Analysis Report");
                writer.WriteLine();
                writer.WriteLine($"**Generated:** {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                writer.WriteLine($"**Session ID:** {currentSessionId}");
                writer.WriteLine($"**Models Analyzed:** {modelResults.Count}");
                writer.WriteLine($"**Total Runs:** {allRawRuns.Count}");
                writer.WriteLine($"**Runs per Question:** {runsPerPrompt}");
                writer.WriteLine();
                writer.WriteLine("---");
                writer.WriteLine();

                // Enhanced Metrics Summary
                writer.WriteLine("## Enhanced Metrics Summary");
                writer.WriteLine();
                writer.WriteLine("| Model | Performance Index | Quality Score | Reliability | Efficiency | Consistency |");
                writer.WriteLine("|-------|-------------------|---------------|-------------|------------|-------------|");

                var rankedByPerformance = modelResults.Values
                    .OrderByDescending(r => r.PerformanceIndex)
                    .ToList();

                foreach (var result in rankedByPerformance)
                {
                    writer.WriteLine($"| {result.ModelName} | {result.PerformanceIndex:F4} | {result.QualityWeightedScore:F4} | {result.ReliabilityScore:F4} | {result.EfficiencyScore:F4} | {result.ConsistencyScore:F4} |");
                }
                writer.WriteLine();
                writer.WriteLine("---");
                writer.WriteLine();

                // Variability Analysis
                writer.WriteLine("## Variability Analysis");
                writer.WriteLine();
                writer.WriteLine("### Latency Statistics");
                writer.WriteLine();
                writer.WriteLine("| Model | Min (ms) | Max (ms) | Avg (ms) | StdDev | CV |");
                writer.WriteLine("|-------|----------|----------|----------|--------|-----|");

                foreach (var result in rankedByPerformance)
                {
                    writer.WriteLine($"| {result.ModelName} | {result.LatencyMin:F0} | {result.LatencyMax:F0} | {result.AverageLatencyMs:F0} | {result.LatencyStdDev:F2} | {result.LatencyVarianceCoefficient:F4} |");
                }
                writer.WriteLine();
                writer.WriteLine("**CV (Coefficient of Variation)**: Lower values indicate more consistent performance");
                writer.WriteLine();

                // Quality Metrics Variability
                writer.WriteLine("### Quality Metrics Variability");
                writer.WriteLine();
                writer.WriteLine("| Model | Entity F1 σ | Grounding σ | Format σ |");
                writer.WriteLine("|-------|-------------|-------------|----------|");

                foreach (var result in rankedByPerformance)
                {
                    writer.WriteLine($"| {result.ModelName} | {result.EntityF1StdDev:F4} | {result.GroundingOverlapStdDev:F4} | {result.FormatAdherenceStdDev:F4} |");
                }
                writer.WriteLine();
                writer.WriteLine("**σ (Standard Deviation)**: Lower values indicate more consistent quality across questions");
                writer.WriteLine();
                writer.WriteLine("---");
                writer.WriteLine();

                // ANOVA Analysis using raw runs
                writer.WriteLine("## Analysis of Variance (ANOVA)");
                writer.WriteLine();
                writer.WriteLine("One-way ANOVA comparing models across multiple runs per question.");
                writer.WriteLine();

                // Group raw runs by model
                var groupedByModel = allRawRuns.GroupBy(r => r.ModelName).ToList();

                // Check minimum observations
                var modelsWithSufficientData = groupedByModel.Where(g => g.Count() >= 2).ToList();

                if (modelsWithSufficientData.Count < 2)
                {
                    writer.WriteLine("⚠️ **Insufficient data for ANOVA**: Need at least 2 models with 2+ observations each.");
                    writer.WriteLine();
                }
                else
                {
                    // Create ANOVA summary object
                    var anovaSummary = new Dictionary<string, object>();

                    // Perform ANOVA for Latency
                    var latencyResult = PerformOneWayANOVA(writer, "Latency (ms)",
                        modelsWithSufficientData.Select(g => g.Select(r => r.LatencyMs).ToList()).ToList(),
                        modelsWithSufficientData.Select(g => g.Key).ToList());
                    anovaSummary["latency"] = latencyResult;

                    // Perform ANOVA for Cost
                    var costResult = PerformOneWayANOVA(writer, "Total Cost (USD)",
                        modelsWithSufficientData.Select(g => g.Select(r => r.TotalCostUsd).ToList()).ToList(),
                        modelsWithSufficientData.Select(g => g.Key).ToList());
                    anovaSummary["cost"] = costResult;

                    // Perform ANOVA for Task Success
                    var taskSuccessResult = PerformOneWayANOVA(writer, "Task Success Rate",
                        modelsWithSufficientData.Select(g => g.Select(r => r.TaskSuccess ? 1.0 : 0.0).ToList()).ToList(),
                        modelsWithSufficientData.Select(g => g.Key).ToList());
                    anovaSummary["task_success"] = taskSuccessResult;

                    // Save ANOVA summary to JSON
                    File.WriteAllText(anovaSummaryPath, JsonConvert.SerializeObject(anovaSummary, Formatting.Indented));
                    Debug.Log($"[LLMEvaluation] ANOVA summary saved: {anovaSummaryPath}");
                }

                writer.WriteLine("---");
                writer.WriteLine();

                // Cost-Performance Analysis
                writer.WriteLine("## Cost-Performance Trade-off Analysis");
                writer.WriteLine();
                writer.WriteLine("| Model | Cost per Success | Performance Index | Value Ratio |");
                writer.WriteLine("|-------|------------------|-------------------|-------------|");

                foreach (var result in rankedByPerformance)
                {
                    double valueRatio = result.CostPerSuccessfulTask > 0
                        ? result.PerformanceIndex / result.CostPerSuccessfulTask
                        : result.PerformanceIndex;
                    writer.WriteLine($"| {result.ModelName} | ${result.CostPerSuccessfulTask:F5} | {result.PerformanceIndex:F4} | {valueRatio:F2} |");
                }
                writer.WriteLine();
                writer.WriteLine("**Value Ratio**: Performance Index divided by Cost per Successful Task (higher is better)");
                writer.WriteLine();

                // Statistical Significance Tests
                writer.WriteLine("## Statistical Significance");
                writer.WriteLine();
                writer.WriteLine("### Pairwise Comparisons (Top 3 Models)");
                writer.WriteLine();

                var top3 = rankedByPerformance.Take(3).ToList();
                if (top3.Count >= 2)
                {
                    writer.WriteLine("| Comparison | Metric | Difference | Effect Size |");
                    writer.WriteLine("|------------|--------|------------|-------------|");

                    for (int i = 0; i < top3.Count - 1; i++)
                    {
                        var model1 = top3[i];
                        var model2 = top3[i + 1];

                        // Task Success Rate comparison
                        double diff = model1.TaskSuccessRate - model2.TaskSuccessRate;
                        double pooledStd = Math.Sqrt((Math.Pow(model1.EntityF1StdDev, 2) + Math.Pow(model2.EntityF1StdDev, 2)) / 2);
                        double cohensD = pooledStd > 0 ? diff / pooledStd : 0;

                        writer.WriteLine($"| {model1.ModelName} vs {model2.ModelName} | Task Success | {diff:+0.0000;-0.0000} | {cohensD:F2} |");

                        // Latency comparison
                        double latencyDiff = model1.AverageLatencyMs - model2.AverageLatencyMs;
                        double latencyPooledStd = Math.Sqrt((Math.Pow(model1.LatencyStdDev, 2) + Math.Pow(model2.LatencyStdDev, 2)) / 2);
                        double latencyCohensD = latencyPooledStd > 0 ? latencyDiff / latencyPooledStd : 0;

                        writer.WriteLine($"| {model1.ModelName} vs {model2.ModelName} | Latency | {latencyDiff:+0.0;-0.0} ms | {latencyCohensD:F2} |");
                    }
                }
                writer.WriteLine();
                writer.WriteLine("**Effect Size (Cohen's d)**: |d| < 0.2 (small), 0.2-0.8 (medium), > 0.8 (large)");
                writer.WriteLine();
                writer.WriteLine("---");
                writer.WriteLine();

                // Recommendations based on statistical analysis
                writer.WriteLine("## Statistical Recommendations");
                writer.WriteLine();

                var mostConsistent = rankedByPerformance.OrderByDescending(r => r.ConsistencyScore).First();
                var mostReliable = rankedByPerformance.OrderByDescending(r => r.ReliabilityScore).First();
                var bestValue = rankedByPerformance.OrderByDescending(r => r.PerformanceIndex / Math.Max(0.0001, r.CostPerSuccessfulTask)).First();

                writer.WriteLine($"- **Most Consistent Performance:** {mostConsistent.ModelName} (Consistency Score: {mostConsistent.ConsistencyScore:F4})");
                writer.WriteLine($"- **Most Reliable:** {mostReliable.ModelName} (Reliability Score: {mostReliable.ReliabilityScore:F4})");
                writer.WriteLine($"- **Best Value for Money:** {bestValue.ModelName}");
                writer.WriteLine();

                writer.WriteLine("---");
                writer.WriteLine();
                writer.WriteLine("*Statistical analysis generated automatically by LLM Evaluation System*");
            }

            Debug.Log($"[LLMEvaluation] Statistical analysis report saved: {reportPath}");
        }

        /// <summary>
        /// Performs one-way ANOVA across multiple groups (models)
        /// Returns dictionary with F-statistic and p-value
        /// </summary>
        private Dictionary<string, double> PerformOneWayANOVA(StreamWriter writer, string metricName,
            List<List<double>> groups, List<string> groupNames)
        {
            writer.WriteLine($"### {metricName}");
            writer.WriteLine();

            int k = groups.Count; // number of groups (models)
            int n = groups.Sum(g => g.Count); // total observations

            if (k < 2 || n < k + 1)
            {
                writer.WriteLine("⚠️ Insufficient data for ANOVA");
                writer.WriteLine();
                return new Dictionary<string, double> { { "F", 0 }, { "p", 1.0 } };
            }

            // Calculate grand mean
            double grandMean = groups.SelectMany(g => g).Average();

            // Calculate group means
            var groupMeans = groups.Select(g => g.Average()).ToList();

            // Calculate Sum of Squares Between (SSB)
            double ssb = 0;
            for (int i = 0; i < k; i++)
            {
                int ni = groups[i].Count;
                double groupMean = groupMeans[i];
                ssb += ni * Math.Pow(groupMean - grandMean, 2);
            }

            // Calculate Sum of Squares Within (SSW)
            double ssw = 0;
            for (int i = 0; i < k; i++)
            {
                double groupMean = groupMeans[i];
                foreach (var value in groups[i])
                {
                    ssw += Math.Pow(value - groupMean, 2);
                }
            }

            // Calculate degrees of freedom
            int dfBetween = k - 1;
            int dfWithin = n - k;

            // Calculate Mean Squares
            double msb = dfBetween > 0 ? ssb / dfBetween : 0;
            double msw = dfWithin > 0 ? ssw / dfWithin : 0;

            // Calculate F-statistic
            double fStatistic = msw > 0 ? msb / msw : 0;

            // Approximate p-value (simplified using F-distribution approximation)
            double pValue = ApproximatePValue(fStatistic, dfBetween, dfWithin);

            // Write results
            writer.WriteLine($"**Groups (Models):** {k}");
            writer.WriteLine($"**Total Observations:** {n}");
            writer.WriteLine($"**Grand Mean:** {grandMean:F4}");
            writer.WriteLine();

            writer.WriteLine("| Model | N | Mean | Std Dev |");
            writer.WriteLine("|-------|---|------|---------|");
            for (int i = 0; i < k; i++)
            {
                double mean = groupMeans[i];
                double stdDev = CalculateStandardDeviation(groups[i]);
                writer.WriteLine($"| {groupNames[i]} | {groups[i].Count} | {mean:F4} | {stdDev:F4} |");
            }
            writer.WriteLine();

            writer.WriteLine($"**Sum of Squares Between (SSB):** {ssb:F4}");
            writer.WriteLine($"**Sum of Squares Within (SSW):** {ssw:F4}");
            writer.WriteLine($"**Mean Square Between (MSB):** {msb:F4}");
            writer.WriteLine($"**Mean Square Within (MSW):** {msw:F4}");
            writer.WriteLine($"**F-statistic:** {fStatistic:F4}");
            writer.WriteLine($"**p-value (approx):** {pValue:F6}");
            writer.WriteLine();

            // Interpretation
            if (pValue < 0.001)
                writer.WriteLine($"✅ **Highly significant difference** (p < 0.001) - Models differ substantially on {metricName}");
            else if (pValue < 0.01)
                writer.WriteLine($"✅ **Very significant difference** (p < 0.01) - Strong evidence of model differences");
            else if (pValue < 0.05)
                writer.WriteLine($"✅ **Significant difference** (p < 0.05) - Models show statistically significant differences");
            else
                writer.WriteLine($"⚠️ **No significant difference** (p ≥ 0.05) - Models perform similarly on {metricName}");

            writer.WriteLine();

            return new Dictionary<string, double>
            {
                { "F", fStatistic },
                { "p", pValue },
                { "df_between", dfBetween },
                { "df_within", dfWithin },
                { "SSB", ssb },
                { "SSW", ssw }
            };
        }

        /// <summary>
        /// Approximate p-value for F-distribution (simplified)
        /// For production use, consider using a proper statistical library
        /// </summary>
        private double ApproximatePValue(double f, int df1, int df2)
        {
            if (f <= 0) return 1.0;

            // Very rough approximation using critical values
            // F(0.05, df1, df2) critical values for common cases
            if (df1 == 1)
            {
                if (f > 161.4) return 0.001;
                if (f > 18.5) return 0.01;
                if (f > 4.0) return 0.05;
            }
            else if (df1 <= 5)
            {
                if (f > 15.0) return 0.001;
                if (f > 6.0) return 0.01;
                if (f > 3.0) return 0.05;
            }
            else
            {
                if (f > 10.0) return 0.001;
                if (f > 5.0) return 0.01;
                if (f > 2.5) return 0.05;
            }

            return 0.1; // Not significant
        }

        private void GenerateRadarChartsHTML()
        {
            if (modelResults.Count == 0) return;

            string htmlPath = Path.Combine(currentOutputPath, "radar_charts.html");

            using (var writer = new StreamWriter(htmlPath))
            {
                writer.WriteLine("<!DOCTYPE html>");
                writer.WriteLine("<html>");
                writer.WriteLine("<head>");
                writer.WriteLine("    <meta charset='UTF-8'>");
                writer.WriteLine("    <meta name='viewport' content='width=device-width, initial-scale=1.0'>");
                writer.WriteLine("    <title>LLM Performance Radar Charts</title>");
                writer.WriteLine("    <script src='https://cdn.jsdelivr.net/npm/chart.js@4.4.0/dist/chart.umd.min.js'></script>");
                writer.WriteLine("    <style>");
                writer.WriteLine("        body { font-family: Arial, sans-serif; margin: 20px; background: #f5f5f5; }");
                writer.WriteLine("        h1 { color: #333; text-align: center; }");
                writer.WriteLine("        .chart-container { background: white; padding: 20px; margin: 20px auto; border-radius: 8px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); max-width: 800px; }");
                writer.WriteLine("        .chart-title { font-size: 18px; font-weight: bold; margin-bottom: 10px; color: #555; }");
                writer.WriteLine("        canvas { max-width: 100%; height: auto !important; }");
                writer.WriteLine("        .legend { margin-top: 20px; padding: 10px; background: #fafafa; border-radius: 4px; }");
                writer.WriteLine("        .info { background: #e3f2fd; padding: 15px; border-radius: 4px; margin: 20px auto; max-width: 800px; }");
                writer.WriteLine("    </style>");
                writer.WriteLine("</head>");
                writer.WriteLine("<body>");
                writer.WriteLine($"    <h1>LLM Performance Analysis - Radar Charts</h1>");
                writer.WriteLine($"    <div class='info'>");
                writer.WriteLine($"        <strong>Session:</strong> {currentSessionId}<br>");
                writer.WriteLine($"        <strong>Models Evaluated:</strong> {modelResults.Count}<br>");
                writer.WriteLine($"        <strong>Generated:</strong> {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                writer.WriteLine($"    </div>");

                // Chart 1: Overall Performance Metrics
                GenerateRadarChartSection(writer, "Overall Performance Metrics", new[]
                {
                    ("Task Success Rate", modelResults.Values.Select(r => r.TaskSuccessRate).ToList()),
                    ("Quality Score", modelResults.Values.Select(r => r.QualityWeightedScore).ToList()),
                    ("Reliability", modelResults.Values.Select(r => r.ReliabilityScore).ToList()),
                    ("Consistency", modelResults.Values.Select(r => r.ConsistencyScore).ToList()),
                    ("Memory Retention", modelResults.Values.Select(r => r.MemoryCarryOverSuccess).ToList())
                });

                // Chart 2: Quality Metrics
                GenerateRadarChartSection(writer, "Quality Metrics", new[]
                {
                    ("Entity F1 Score", modelResults.Values.Select(r => r.AverageEntityF1).ToList()),
                    ("Grounding Overlap", modelResults.Values.Select(r => r.AverageGroundingOverlap).ToList()),
                    ("Format Adherence", modelResults.Values.Select(r => r.AverageFormatAdherence).ToList()),
                    ("Task Success", modelResults.Values.Select(r => r.TaskSuccessRate).ToList()),
                    ("Anti-Hallucination", modelResults.Values.Select(r => 1.0 - r.HallucinationRate).ToList())
                });

                // Chart 3: Efficiency Metrics (normalized)
                var maxLatency = modelResults.Values.Max(r => r.AverageLatencyMs);
                var maxCost = modelResults.Values.Max(r => r.TotalCostUSD);

                GenerateRadarChartSection(writer, "Efficiency Metrics (Normalized)", new[]
                {
                    ("Speed", modelResults.Values.Select(r => 1.0 - (r.AverageLatencyMs / maxLatency)).ToList()),
                    ("Cost Efficiency", modelResults.Values.Select(r => 1.0 - (r.TotalCostUSD / maxCost)).ToList()),
                    ("Value Ratio", modelResults.Values.Select(r => r.PerformanceIndex / Math.Max(0.001, r.CostPerSuccessfulTask) / 100.0).ToList()),
                    ("Throughput", modelResults.Values.Select(r => Math.Min(1.0, r.TokensPerSecond / 1000.0)).ToList()),
                    ("Efficiency Score", modelResults.Values.Select(r => Math.Min(1.0, r.EfficiencyScore)).ToList())
                });

                // Chart 4: Cost-Performance Trade-off (separate chart for each model)
                writer.WriteLine("    <div class='chart-container'>");
                writer.WriteLine("        <div class='chart-title'>Cost-Performance Trade-off Comparison</div>");
                writer.WriteLine("        <canvas id='costPerformanceChart'></canvas>");
                writer.WriteLine("    </div>");

                writer.WriteLine("    <script>");
                GenerateCostPerformanceScatterScript(writer);
                writer.WriteLine("    </script>");

                writer.WriteLine("</body>");
                writer.WriteLine("</html>");
            }

            Debug.Log($"[LLMEvaluation] Radar charts HTML saved: {htmlPath}");
        }

        private void GenerateRadarChartSection(StreamWriter writer, string title, (string Label, List<double> Values)[] datasets)
        {
            string chartId = title.Replace(" ", "_").ToLower();

            writer.WriteLine($"    <div class='chart-container'>");
            writer.WriteLine($"        <div class='chart-title'>{title}</div>");
            writer.WriteLine($"        <canvas id='{chartId}'></canvas>");
            writer.WriteLine($"    </div>");

            writer.WriteLine($"    <script>");
            writer.WriteLine($"        {{");
            writer.WriteLine($"            const ctx_{chartId} = document.getElementById('{chartId}').getContext('2d');");
            writer.WriteLine($"            new Chart(ctx_{chartId}, {{");
            writer.WriteLine($"                type: 'radar',");
            writer.WriteLine($"                data: {{");
            writer.WriteLine($"                    labels: {JsonConvert.SerializeObject(datasets.Select(d => d.Label).ToArray())},");
            writer.WriteLine($"                    datasets: [");

            var modelNames = modelResults.Keys.ToList();
            var colors = GenerateColors(modelNames.Count);

            for (int i = 0; i < modelNames.Count; i++)
            {
                var modelName = modelNames[i];
                var dataValues = datasets.Select(d => d.Values[i]).ToArray();
                var color = colors[i];

                writer.WriteLine($"                        {{");
                writer.WriteLine($"                            label: '{modelName}',");
                writer.WriteLine($"                            data: {JsonConvert.SerializeObject(dataValues)},");
                writer.WriteLine($"                            backgroundColor: 'rgba({color.r}, {color.g}, {color.b}, 0.2)',");
                writer.WriteLine($"                            borderColor: 'rgba({color.r}, {color.g}, {color.b}, 1)',");
                writer.WriteLine($"                            pointBackgroundColor: 'rgba({color.r}, {color.g}, {color.b}, 1)',");
                writer.WriteLine($"                            pointBorderColor: '#fff',");
                writer.WriteLine($"                            pointHoverBackgroundColor: '#fff',");
                writer.WriteLine($"                            pointHoverBorderColor: 'rgba({color.r}, {color.g}, {color.b}, 1)'");
                writer.WriteLine($"                        }}{(i < modelNames.Count - 1 ? "," : "")}");
            }

            writer.WriteLine($"                    ]");
            writer.WriteLine($"                }},");
            writer.WriteLine($"                options: {{");
            writer.WriteLine($"                    responsive: true,");
            writer.WriteLine($"                    maintainAspectRatio: true,");
            writer.WriteLine($"                    scales: {{");
            writer.WriteLine($"                        r: {{");
            writer.WriteLine($"                            beginAtZero: true,");
            writer.WriteLine($"                            max: 1,");
            writer.WriteLine($"                            ticks: {{ stepSize: 0.2 }}");
            writer.WriteLine($"                        }}");
            writer.WriteLine($"                    }},");
            writer.WriteLine($"                    plugins: {{");
            writer.WriteLine($"                        legend: {{ position: 'top' }},");
            writer.WriteLine($"                        title: {{ display: false }}");
            writer.WriteLine($"                    }}");
            writer.WriteLine($"                }}");
            writer.WriteLine($"            }});");
            writer.WriteLine($"        }}");
            writer.WriteLine($"    </script>");
        }

        private void GenerateCostPerformanceScatterScript(StreamWriter writer)
        {
            writer.WriteLine("        const ctx_costPerf = document.getElementById('costPerformanceChart').getContext('2d');");
            writer.WriteLine("        new Chart(ctx_costPerf, {");
            writer.WriteLine("            type: 'scatter',");
            writer.WriteLine("            data: {");
            writer.WriteLine("                datasets: [");

            var modelNames = modelResults.Keys.ToList();
            var colors = GenerateColors(modelNames.Count);

            for (int i = 0; i < modelNames.Count; i++)
            {
                var modelName = modelNames[i];
                var result = modelResults[modelName];
                var color = colors[i];

                writer.WriteLine("                    {");
                writer.WriteLine($"                        label: '{modelName}',");
                writer.WriteLine($"                        data: [{{ x: {result.TotalCostUSD:F6}, y: {result.PerformanceIndex:F6} }}],");
                writer.WriteLine($"                        backgroundColor: 'rgba({color.r}, {color.g}, {color.b}, 0.6)',");
                writer.WriteLine($"                        borderColor: 'rgba({color.r}, {color.g}, {color.b}, 1)',");
                writer.WriteLine("                        pointRadius: 8,");
                writer.WriteLine("                        pointHoverRadius: 12");
                writer.WriteLine($"                    }}{(i < modelNames.Count - 1 ? "," : "")}");
            }

            writer.WriteLine("                ]");
            writer.WriteLine("            },");
            writer.WriteLine("            options: {");
            writer.WriteLine("                responsive: true,");
            writer.WriteLine("                plugins: {");
            writer.WriteLine("                    legend: { position: 'right' },");
            writer.WriteLine("                    title: { display: false },");
            writer.WriteLine("                    tooltip: {");
            writer.WriteLine("                        callbacks: {");
            writer.WriteLine("                            label: function(context) {");
            writer.WriteLine("                                return context.dataset.label + ': Cost $' + context.parsed.x.toFixed(4) + ', Performance ' + context.parsed.y.toFixed(4);");
            writer.WriteLine("                            }");
            writer.WriteLine("                        }");
            writer.WriteLine("                    }");
            writer.WriteLine("                },");
            writer.WriteLine("                scales: {");
            writer.WriteLine("                    x: { title: { display: true, text: 'Total Cost (USD)' } },");
            writer.WriteLine("                    y: { title: { display: true, text: 'Performance Index' }, beginAtZero: true }");
            writer.WriteLine("                }");
            writer.WriteLine("            }");
            writer.WriteLine("        });");
        }

        /// <summary>
        /// Exports radar chart data to CSV files for manual chart creation.
        /// Creates three separate CSV files:
        /// 1. radar_chart_overall.csv - Overall performance metrics
        /// 2. radar_chart_quality.csv - Quality metrics
        /// 3. radar_chart_efficiency.csv - Efficiency metrics (normalized)
        /// </summary>
        private void GenerateRadarChartDataCSV()
        {
            if (modelResults.Count == 0) return;

            var modelNames = modelResults.Keys.ToList();

            // Calculate normalized efficiency metrics once
            var maxLatency = modelResults.Values.Max(r => r.AverageLatencyMs);
            var maxCost = modelResults.Values.Max(r => r.TotalCostUSD);

            // ========== Chart 1: Overall Performance Metrics ==========
            string overallPath = Path.Combine(currentOutputPath, "radar_chart_overall.csv");
            using (var writer = new StreamWriter(overallPath))
            {
                // Header row
                writer.Write("Model");
                writer.Write(",Task Success Rate");
                writer.Write(",Quality Score");
                writer.Write(",Reliability");
                writer.Write(",Consistency");
                writer.Write(",Memory Retention");
                writer.WriteLine();

                // Data rows
                foreach (var modelName in modelNames)
                {
                    var result = modelResults[modelName];
                    writer.Write($"\"{modelName}\"");
                    writer.Write($",{result.TaskSuccessRate:F6}");
                    writer.Write($",{result.QualityWeightedScore:F6}");
                    writer.Write($",{result.ReliabilityScore:F6}");
                    writer.Write($",{result.ConsistencyScore:F6}");
                    writer.Write($",{result.MemoryCarryOverSuccess:F6}");
                    writer.WriteLine();
                }
            }
            Debug.Log($"[LLMEvaluation] Radar chart data (Overall) saved: {overallPath}");

            // ========== Chart 2: Quality Metrics ==========
            string qualityPath = Path.Combine(currentOutputPath, "radar_chart_quality.csv");
            using (var writer = new StreamWriter(qualityPath))
            {
                // Header row
                writer.Write("Model");
                writer.Write(",Entity F1 Score");
                writer.Write(",Grounding Overlap");
                writer.Write(",Format Adherence");
                writer.Write(",Task Success");
                writer.Write(",Anti-Hallucination");
                writer.WriteLine();

                // Data rows
                foreach (var modelName in modelNames)
                {
                    var result = modelResults[modelName];
                    writer.Write($"\"{modelName}\"");
                    writer.Write($",{result.AverageEntityF1:F6}");
                    writer.Write($",{result.AverageGroundingOverlap:F6}");
                    writer.Write($",{result.AverageFormatAdherence:F6}");
                    writer.Write($",{result.TaskSuccessRate:F6}");
                    writer.Write($",{(1.0 - result.HallucinationRate):F6}");
                    writer.WriteLine();
                }
            }
            Debug.Log($"[LLMEvaluation] Radar chart data (Quality) saved: {qualityPath}");

            // ========== Chart 3: Efficiency Metrics (Normalized) ==========
            string efficiencyPath = Path.Combine(currentOutputPath, "radar_chart_efficiency.csv");
            using (var writer = new StreamWriter(efficiencyPath))
            {
                // Header row
                writer.Write("Model");
                writer.Write(",Speed (Normalized)");
                writer.Write(",Cost Efficiency (Normalized)");
                writer.Write(",Value Ratio");
                writer.Write(",Throughput (Normalized)");
                writer.Write(",Efficiency Score");
                writer.WriteLine();

                // Data rows
                foreach (var modelName in modelNames)
                {
                    var result = modelResults[modelName];
                    double speedNormalized = 1.0 - (result.AverageLatencyMs / maxLatency);
                    double costEfficiencyNormalized = 1.0 - (result.TotalCostUSD / maxCost);
                    double valueRatio = result.PerformanceIndex / Math.Max(0.001, result.CostPerSuccessfulTask) / 100.0;
                    double throughputNormalized = Math.Min(1.0, result.TokensPerSecond / 1000.0);
                    double efficiencyScore = Math.Min(1.0, result.EfficiencyScore);

                    writer.Write($"\"{modelName}\"");
                    writer.Write($",{speedNormalized:F6}");
                    writer.Write($",{costEfficiencyNormalized:F6}");
                    writer.Write($",{valueRatio:F6}");
                    writer.Write($",{throughputNormalized:F6}");
                    writer.Write($",{efficiencyScore:F6}");
                    writer.WriteLine();
                }
            }
            Debug.Log($"[LLMEvaluation] Radar chart data (Efficiency) saved: {efficiencyPath}");

            // ========== Chart 4: Cost-Performance Scatter Data ==========
            string scatterPath = Path.Combine(currentOutputPath, "radar_chart_cost_performance.csv");
            using (var writer = new StreamWriter(scatterPath))
            {
                // Header row
                writer.Write("Model");
                writer.Write(",Total Cost (USD)");
                writer.Write(",Performance Index");
                writer.WriteLine();

                // Data rows
                foreach (var modelName in modelNames)
                {
                    var result = modelResults[modelName];
                    writer.Write($"\"{modelName}\"");
                    writer.Write($",{result.TotalCostUSD:F6}");
                    writer.Write($",{result.PerformanceIndex:F6}");
                    writer.WriteLine();
                }
            }
            Debug.Log($"[LLMEvaluation] Radar chart data (Cost-Performance) saved: {scatterPath}");

            Debug.Log($"[LLMEvaluation] ========================================");
            Debug.Log($"[LLMEvaluation] All radar chart CSV files generated successfully!");
            Debug.Log($"[LLMEvaluation] Files created:");
            Debug.Log($"[LLMEvaluation]   - radar_chart_overall.csv");
            Debug.Log($"[LLMEvaluation]   - radar_chart_quality.csv");
            Debug.Log($"[LLMEvaluation]   - radar_chart_efficiency.csv");
            Debug.Log($"[LLMEvaluation]   - radar_chart_cost_performance.csv");
            Debug.Log($"[LLMEvaluation] ========================================");
        }

        private List<(int r, int g, int b)> GenerateColors(int count)
        {
            var colors = new List<(int r, int g, int b)>();
            for (int i = 0; i < count; i++)
            {
                float hue = (i / (float)count) * 360f;
                var rgb = HSVToRGB(hue, 0.7f, 0.9f);
                colors.Add(rgb);
            }
            return colors;
        }

        private (int r, int g, int b) HSVToRGB(float h, float s, float v)
        {
            float c = v * s;
            float x = c * (1 - Math.Abs((h / 60f) % 2 - 1));
            float m = v - c;

            float r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }

            return ((int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
        }

        #endregion

        #region Public API

        public bool IsEvaluating() => isEvaluating;

        public string GetCurrentSessionId() => currentSessionId;

        public Dictionary<string, ModelEvaluationResults> GetResults() => modelResults;

        #endregion

        #region Data Structures

        [System.Serializable]
        public class ModelConfig
        {
            public AIProvider Provider;
            [NonSerialized] public object ModelType; // Can be GPTModelType, ClaudeModelType, or GeminiModelType
            public string DisplayName;
            public double CostPer1KInput;
            public double CostPer1KOutput;
        }

        [System.Serializable]
        public class SceneSnapshot
        {
            public DateTime Timestamp;
            public string SceneHash;
            public List<ModuleSnapshot> Modules = new List<ModuleSnapshot>();
            public List<BIMElementSnapshot> BIMElements = new List<BIMElementSnapshot>();
        }

        [System.Serializable]
        public class ModuleSnapshot
        {
            public string Name;
            public SerializableVector3 Position;
            public bool IsActive;
        }

        [System.Serializable]
        public class SerializableVector3
        {
            public float x;
            public float y;
            public float z;

            public SerializableVector3() { }

            public SerializableVector3(Vector3 vector)
            {
                x = vector.x;
                y = vector.y;
                z = vector.z;
            }

            public Vector3 ToVector3()
            {
                return new Vector3(x, y, z);
            }

            public override string ToString()
            {
                return $"({x:F2}, {y:F2}, {z:F2})";
            }
        }

        [System.Serializable]
        public class BIMElementSnapshot
        {
            public string ElementId;
            public string Name;
            public string Type;
            public string Category;
            public string Level;
            [SerializeField] public Dictionary<string, string> Properties = new Dictionary<string, string>();
        }

        /// <summary>
        /// Raw run data for ANOVA analysis - one row per run
        /// </summary>
        [System.Serializable]
        public class RawRunData
        {
            public string ModelName;
            public int QuestionId;
            public int RunIndex;
            public bool TaskSuccess;
            public double HallucinationRate;
            public double LatencyMs;
            public double TotalCostUsd;
            public double TokensPerSec;
            public double EntityF1;
            public double GroundingOverlap;
            public double FormatAdherence;
        }

        [System.Serializable]
        public class ModelEvaluationResults
        {
            public string ModelName;
            public string Provider;
            public string SessionId;
            public DateTime StartTime;
            public DateTime EndTime;
            public List<TurnResult> TurnResults;

            // Aggregate metrics
            public double TaskSuccessRate;
            public double HallucinationRate;
            public double AverageLatencyMs;
            public double LatencyP50;
            public double LatencyP95;
            public double TotalCostUSD;
            public double TokensPerSecond;
            public double MemoryCarryOverSuccess;
            public int TotalPromptTokens;
            public int TotalCompletionTokens;

            // Average turn metrics
            public double AverageEntityF1;
            public double AverageGroundingOverlap;
            public double AverageFormatAdherence;

            // Enhanced statistical metrics
            public double LatencyStdDev;
            public double LatencyMin;
            public double LatencyMax;
            public double LatencyVarianceCoefficient;
            public double EntityF1StdDev;
            public double GroundingOverlapStdDev;
            public double FormatAdherenceStdDev;
            public double ConsistencyScore; // Overall consistency across all metrics
            public double ReliabilityScore; // (1 - HallucinationRate) * ConsistencyScore
            public double CostPerSuccessfulTask;
            public double QualityWeightedScore; // Composite quality metric
            public double EfficiencyScore; // Speed/cost trade-off
            public double PerformanceIndex; // Overall performance ranking metric
        }

        [System.Serializable]
        public class TurnResult
        {
            public int TurnNumber;
            public string Question;
            public string Response;
            public DateTime QuestionTime;
            public DateTime ResponseTime;
            public double LatencyMs;

            // Metrics
            public bool IsValidJSON;
            public double ExactMatch;
            public double EntityF1;
            public double GroundingOverlap;
            public double FormatAdherence;
            public int RefusalCorrectness; // +1 correct, 0 neutral, -1 hallucination
            public bool ContainsHallucination;

            // Token and cost
            public int PromptTokens;
            public int CompletionTokens;
            public double CostUSD;
        }

        #endregion
    }
}
