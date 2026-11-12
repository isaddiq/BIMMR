// FPSBenchmarker.cs - Advanced FPS performance benchmarking for BIM models in Quest 3
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// FPS benchmarking system that measures frame rate performance for BIM models.
/// Integrates with ModuleManager to automatically or manually benchmark models.
/// </summary>
public class FPSBenchmarker : MonoBehaviour
{
    [Header("Benchmark Settings")]
    [Tooltip("Duration of warm-up period before sampling begins")]
    [SerializeField] private float warmupDuration = 15f;

    [Tooltip("Duration of FPS sampling period")]
    [SerializeField] private float sampleDuration = 60f;

    [Header("References")]
    [Tooltip("Reference to your ModuleManager (must implement IBimModelProvider)")]
    [SerializeField] private MonoBehaviour moduleManagerReference;

    [Header("Auto-Benchmark")]
    [SerializeField] private bool autoBenchmarkOnLoad = false;

    [Header("Debug")]
    [SerializeField] private bool enableDebugLogs = true;

    // Benchmark state
    private IBimModelProvider modelProvider;
    private BenchmarkState currentState = BenchmarkState.Idle;
    private List<float> fpsSamples = new List<float>();
    private float stateTimer = 0f;
    private string currentModelId;
    private GameObject currentModelRoot;
    private int runIdForCurrentModel = 0;
    private Dictionary<string, int> modelRunCounts = new Dictionary<string, int>();
    private string cancellationReason = "";
    private bool modelChangedDuringSampling = false;

    // Memory tracking
    private List<long> memorySamples = new List<long>();

    // CSV file path
    private string csvFilePath;
    private const string CSV_FILENAME = "fps_benchmark_results.csv";

    private enum BenchmarkState
    {
        Idle,
        WaitingForModel,
        WarmingUp,
        Sampling
    }

    #region Unity Lifecycle

    private void Awake()
    {
        // Setup CSV file path (Quest-compatible)
        csvFilePath = Path.Combine(Application.persistentDataPath, CSV_FILENAME);

        if (enableDebugLogs)
            Debug.Log($"[FPS Benchmarker] CSV output path: {csvFilePath}");

        // Initialize CSV file with header if it doesn't exist
        InitializeCSVFile();

        // Get model provider reference
        if (moduleManagerReference != null)
        {
            modelProvider = moduleManagerReference as IBimModelProvider;
            if (modelProvider == null)
            {
                Debug.LogError($"[FPS Benchmarker] ModuleManager does not implement IBimModelProvider interface!");
            }
        }
        else
        {
            Debug.LogError($"[FPS Benchmarker] No ModuleManager reference assigned!");
        }
    }

    private void OnEnable()
    {
        if (modelProvider != null)
        {
            modelProvider.OnModelLoaded += OnModelLoaded;
            modelProvider.OnModelUnloaded += OnModelUnloaded;
        }
    }

    private void OnDisable()
    {
        if (modelProvider != null)
        {
            modelProvider.OnModelLoaded -= OnModelLoaded;
            modelProvider.OnModelUnloaded -= OnModelUnloaded;
        }
    }

    private void Update()
    {
        // Update benchmark state machine
        switch (currentState)
        {
            case BenchmarkState.WaitingForModel:
                UpdateWaitingForModel();
                break;

            case BenchmarkState.WarmingUp:
                UpdateWarmingUp();
                break;

            case BenchmarkState.Sampling:
                UpdateSampling();
                break;
        }
    }

    #endregion

    #region State Machine Updates

    private void UpdateWaitingForModel()
    {
        // Check if model is now loaded
        if (modelProvider.IsModelLoaded &&
            modelProvider.CurrentModelRoot != null &&
            modelProvider.CurrentModelRoot.activeInHierarchy)
        {
            currentModelId = modelProvider.CurrentModelId;
            currentModelRoot = modelProvider.CurrentModelRoot;

            TransitionToWarmup();
        }
    }

    private void UpdateWarmingUp()
    {
        stateTimer += Time.unscaledDeltaTime;

        // Check for model change during warmup
        if (CheckForModelChange())
        {
            modelChangedDuringSampling = true;
            cancellationReason = "model switched during warmup";
            CancelBenchmark();
            return;
        }

        if (stateTimer >= warmupDuration)
        {
            TransitionToSampling();
        }
    }

    private void UpdateSampling()
    {
        stateTimer += Time.unscaledDeltaTime;

        // Sample FPS every frame
        float currentFPS = 1f / Time.unscaledDeltaTime;
        fpsSamples.Add(currentFPS);

        // Sample memory
        long managedMemory = GC.GetTotalMemory(false);
        memorySamples.Add(managedMemory);

        // Check for model change during sampling
        if (CheckForModelChange())
        {
            modelChangedDuringSampling = true;
            cancellationReason = "model switched during sampling";
            CompleteBenchmark();
            return;
        }

        if (stateTimer >= sampleDuration)
        {
            CompleteBenchmark();
        }
    }

    #endregion

    #region State Transitions

    private void TransitionToWarmup()
    {
        currentState = BenchmarkState.WarmingUp;
        stateTimer = 0f;
        fpsSamples.Clear();
        memorySamples.Clear();
        modelChangedDuringSampling = false;
        cancellationReason = "";

        if (enableDebugLogs)
            Debug.Log($"[FPS Benchmarker] Starting warmup for model '{currentModelId}' ({warmupDuration}s)");
    }

    private void TransitionToSampling()
    {
        currentState = BenchmarkState.Sampling;
        stateTimer = 0f;

        if (enableDebugLogs)
            Debug.Log($"[FPS Benchmarker] Warmup complete. Starting FPS sampling for '{currentModelId}' ({sampleDuration}s)");
    }

    #endregion

    #region Model Event Handlers

    private void OnModelLoaded(string modelId, GameObject modelRoot)
    {
        if (enableDebugLogs)
            Debug.Log($"[FPS Benchmarker] Model loaded: {modelId}");

        // Auto-benchmark if enabled
        if (autoBenchmarkOnLoad && currentState == BenchmarkState.Idle)
        {
            StartBenchmarkForCurrentModel();
        }
    }

    private void OnModelUnloaded(string modelId)
    {
        if (enableDebugLogs)
            Debug.Log($"[FPS Benchmarker] Model unloaded: {modelId}");

        // Cancel benchmark if running
        if (currentState != BenchmarkState.Idle)
        {
            modelChangedDuringSampling = true;
            cancellationReason = $"model '{modelId}' unloaded during benchmark";
            CancelBenchmark();
        }
    }

    #endregion

    #region Public API

    /// <summary>
    /// Start benchmarking the currently loaded model.
    /// </summary>
    public void StartBenchmarkForCurrentModel()
    {
        if (currentState != BenchmarkState.Idle)
        {
            Debug.LogWarning("[FPS Benchmarker] Benchmark already in progress. Cancel first.");
            return;
        }

        if (modelProvider == null)
        {
            Debug.LogError("[FPS Benchmarker] No model provider reference!");
            return;
        }

        // Check if model is loaded
        if (!modelProvider.IsModelLoaded ||
            modelProvider.CurrentModelRoot == null ||
            !modelProvider.CurrentModelRoot.activeInHierarchy)
        {
            if (enableDebugLogs)
                Debug.Log("[FPS Benchmarker] No model currently loaded. Waiting for model...");

            currentState = BenchmarkState.WaitingForModel;
            return;
        }

        // Increment run ID for this model
        currentModelId = modelProvider.CurrentModelId;
        currentModelRoot = modelProvider.CurrentModelRoot;

        if (!modelRunCounts.ContainsKey(currentModelId))
            modelRunCounts[currentModelId] = 0;

        runIdForCurrentModel = ++modelRunCounts[currentModelId];

        TransitionToWarmup();
    }

    /// <summary>
    /// Enable or disable automatic benchmarking whenever a model loads.
    /// </summary>
    public void StartAutoBenchmarkOnEveryLoad(bool enabled)
    {
        autoBenchmarkOnLoad = enabled;

        if (enableDebugLogs)
            Debug.Log($"[FPS Benchmarker] Auto-benchmark on load: {(enabled ? "ENABLED" : "DISABLED")}");
    }

    /// <summary>
    /// Cancel the current benchmark safely.
    /// </summary>
    public void CancelBenchmark()
    {
        if (currentState == BenchmarkState.Idle)
        {
            if (enableDebugLogs)
                Debug.Log("[FPS Benchmarker] No benchmark in progress.");
            return;
        }

        string previousState = currentState.ToString();
        currentState = BenchmarkState.Idle;
        stateTimer = 0f;
        fpsSamples.Clear();
        memorySamples.Clear();

        if (enableDebugLogs)
        {
            string reason = string.IsNullOrEmpty(cancellationReason) ? "user requested" : cancellationReason;
            Debug.Log($"[FPS Benchmarker] Benchmark cancelled during {previousState} - Reason: {reason}");
        }

        cancellationReason = "";
    }

    #endregion

    #region Benchmark Completion

    private void CompleteBenchmark()
    {
        if (fpsSamples.Count == 0)
        {
            Debug.LogWarning("[FPS Benchmarker] No samples collected!");
            currentState = BenchmarkState.Idle;
            return;
        }

        // Calculate statistics
        var stats = CalculateStatistics();

        // Log results
        if (enableDebugLogs)
        {
            Debug.Log($"[FPS Benchmarker] Sampling complete for '{currentModelId}'");
            Debug.Log($"  Samples: {stats.sampleCount}");
            Debug.Log($"  Mean FPS: {stats.meanFPS:F2}");
            Debug.Log($"  Median FPS: {stats.medianFPS:F2}");
            Debug.Log($"  95th Percentile: {stats.p95FPS:F2}");
            Debug.Log($"  99th Percentile: {stats.p99FPS:F2}");
            Debug.Log($"  1% Low: {stats.p1LowFPS:F2}");
            Debug.Log($"  Avg Memory: {stats.avgMemoryMB:F2} MB");
        }

        // Save to CSV
        SaveResultsToCSV(stats);

        if (enableDebugLogs)
            Debug.Log($"[FPS Benchmarker] Results saved to: {csvFilePath}");

        // Reset state
        currentState = BenchmarkState.Idle;
        fpsSamples.Clear();
        memorySamples.Clear();
    }

    private BenchmarkStats CalculateStatistics()
    {
        var stats = new BenchmarkStats();

        // Sort samples for percentile calculations
        var sortedSamples = fpsSamples.OrderBy(x => x).ToList();

        stats.sampleCount = fpsSamples.Count;
        stats.meanFPS = fpsSamples.Average();
        stats.medianFPS = GetPercentile(sortedSamples, 50);
        stats.p95FPS = GetPercentile(sortedSamples, 95);
        stats.p99FPS = GetPercentile(sortedSamples, 99);
        stats.p1LowFPS = GetPercentile(sortedSamples, 1);

        // Memory statistics
        if (memorySamples.Count > 0)
        {
            long avgMemoryBytes = (long)memorySamples.Average();
            stats.avgMemoryMB = avgMemoryBytes / (1024f * 1024f);
        }

        return stats;
    }

    private float GetPercentile(List<float> sortedData, int percentile)
    {
        if (sortedData.Count == 0) return 0f;

        int index = Mathf.Clamp(
            Mathf.RoundToInt(sortedData.Count * percentile / 100f) - 1,
            0,
            sortedData.Count - 1
        );

        return sortedData[index];
    }

    private struct BenchmarkStats
    {
        public int sampleCount;
        public float meanFPS;
        public float medianFPS;
        public float p95FPS;
        public float p99FPS;
        public float p1LowFPS;
        public float avgMemoryMB;
    }

    #endregion

    #region CSV Management

    private void InitializeCSVFile()
    {
        // Create file with header if it doesn't exist
        if (!File.Exists(csvFilePath))
        {
            try
            {
                using (StreamWriter writer = new StreamWriter(csvFilePath, false))
                {
                    writer.WriteLine("timestamp,modelId,runId,warmupSeconds,sampleSeconds,sampleCount,meanFPS,medianFPS,p95FPS,p99FPS,p1LowFPS,avgMemoryMB,notes");
                }

                if (enableDebugLogs)
                    Debug.Log($"[FPS Benchmarker] Created new CSV file at: {csvFilePath}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FPS Benchmarker] Failed to create CSV file: {ex.Message}");
            }
        }
    }

    private void SaveResultsToCSV(BenchmarkStats stats)
    {
        try
        {
            using (StreamWriter writer = new StreamWriter(csvFilePath, true))
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string notes = modelChangedDuringSampling ? cancellationReason : "";

                string csvRow = string.Format(
                    "{0},{1},{2},{3:F1},{4:F1},{5},{6:F2},{7:F2},{8:F2},{9:F2},{10:F2},{11:F2},{12}",
                    timestamp,
                    currentModelId,
                    runIdForCurrentModel,
                    warmupDuration,
                    sampleDuration,
                    stats.sampleCount,
                    stats.meanFPS,
                    stats.medianFPS,
                    stats.p95FPS,
                    stats.p99FPS,
                    stats.p1LowFPS,
                    stats.avgMemoryMB,
                    notes
                );

                writer.WriteLine(csvRow);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[FPS Benchmarker] Failed to save results to CSV: {ex.Message}");
        }
    }

    #endregion

    #region Helpers

    private bool CheckForModelChange()
    {
        // Check if the model reference has changed
        return modelProvider.CurrentModelRoot != currentModelRoot ||
               modelProvider.CurrentModelId != currentModelId ||
               !modelProvider.IsModelLoaded ||
               currentModelRoot == null ||
               !currentModelRoot.activeInHierarchy;
    }

    #endregion
}
