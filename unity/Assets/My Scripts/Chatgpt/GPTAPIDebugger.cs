// GPTAPIDebugger.cs - Enhanced debugging and evaluation utility for GPT API
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace AEC.VoiceAssistant
{
    /// <summary>
    /// Debugging utility for GPT API integration with LLM evaluation support
    /// </summary>
    public class GPTAPIDebugger : MonoBehaviour
    {
        [Header("Component References")]
        [SerializeField] private AECGPTVoiceAssistant gptAssistant;
        [SerializeField] private VoiceGPTBridge voiceBridge;
        [SerializeField] private LLMEvaluationMode evaluationMode;

        [Header("Debug Settings")]
        [SerializeField] private bool enableDebugLogs = true;
        [SerializeField] private bool logAPIRequests = true;
        [SerializeField] private bool logAPIResponses = true;
        [SerializeField] private bool logTokenUsage = true;

        [Header("Test Commands")]
        [SerializeField] private bool runQuickTest = false;
        [SerializeField] private string testPrompt = "List all columns in the scene.";

        [Header("Statistics")]
        [SerializeField] private int totalRequests = 0;
        [SerializeField] private int successfulRequests = 0;
        [SerializeField] private int failedRequests = 0;
        [SerializeField] private float averageResponseTime = 0f;

        private List<float> responseTimes = new List<float>();
        private System.DateTime lastRequestTime;

        private void Start()
        {
            InitializeDebugger();
        }

        private void InitializeDebugger()
        {
            if (gptAssistant == null)
                gptAssistant = FindFirstObjectByType<AECGPTVoiceAssistant>();

            if (voiceBridge == null)
                voiceBridge = FindFirstObjectByType<VoiceGPTBridge>();

            if (evaluationMode == null)
                evaluationMode = FindFirstObjectByType<LLMEvaluationMode>();

            if (gptAssistant != null)
            {
                gptAssistant.OnUserMessageSent += OnUserMessage;
                gptAssistant.OnAssistantMessageReceived += OnAssistantMessage;
            }

            if (enableDebugLogs)
                Debug.Log("[GPTAPIDebugger] Debugger initialized and ready");
        }

        private void OnDestroy()
        {
            if (gptAssistant != null)
            {
                gptAssistant.OnUserMessageSent -= OnUserMessage;
                gptAssistant.OnAssistantMessageReceived -= OnAssistantMessage;
            }
        }

        private void Update()
        {
            if (runQuickTest)
            {
                runQuickTest = false;
                RunQuickTest();
            }
        }

        #region Event Handlers

        private void OnUserMessage(string message)
        {
            lastRequestTime = System.DateTime.Now;
            totalRequests++;

            if (logAPIRequests && enableDebugLogs)
            {
                Debug.Log($"[GPTAPIDebugger] Request #{totalRequests}: {message}");
            }
        }

        private void OnAssistantMessage(string message)
        {
            var responseTime = (System.DateTime.Now - lastRequestTime).TotalSeconds;
            responseTimes.Add((float)responseTime);

            if (responseTimes.Count > 100)
                responseTimes.RemoveAt(0);

            averageResponseTime = responseTimes.Average();
            successfulRequests++;

            if (logAPIResponses && enableDebugLogs)
            {
                Debug.Log($"[GPTAPIDebugger] Response received in {responseTime:F2}s: {message.Substring(0, Mathf.Min(100, message.Length))}...");
            }

            if (logTokenUsage && enableDebugLogs)
            {
                int estimatedTokens = message.Length / 4;
                Debug.Log($"[GPTAPIDebugger] Estimated response tokens: {estimatedTokens}");
            }
        }

        #endregion

        #region Test Functions

        private void RunQuickTest()
        {
            if (gptAssistant == null)
            {
                Debug.LogError("[GPTAPIDebugger] No GPT Assistant found!");
                return;
            }

            if (string.IsNullOrEmpty(testPrompt))
            {
                Debug.LogWarning("[GPTAPIDebugger] Test prompt is empty");
                return;
            }

            Debug.Log($"[GPTAPIDebugger] Running quick test: {testPrompt}");
            gptAssistant.SendChatMessage(testPrompt);
        }

        [ContextMenu("Test GPT Connection")]
        public void TestGPTConnection()
        {
            if (gptAssistant == null)
            {
                Debug.LogError("[GPTAPIDebugger] No GPT Assistant found!");
                return;
            }

            Debug.Log("[GPTAPIDebugger] Testing GPT connection...");
            gptAssistant.SendChatMessage("Hello, this is a connection test. Please respond briefly.");
        }

        [ContextMenu("Test Voice Bridge")]
        public void TestVoiceBridge()
        {
            if (voiceBridge == null)
            {
                Debug.LogError("[GPTAPIDebugger] No Voice Bridge found!");
                return;
            }

            Debug.Log("[GPTAPIDebugger] Testing voice bridge...");
            Debug.Log($"Voice Bridge Status: {voiceBridge.GetStatus()}");
        }

        [ContextMenu("Start LLM Evaluation")]
        public void StartEvaluation()
        {
            if (evaluationMode == null)
            {
                Debug.LogError("[GPTAPIDebugger] No LLM Evaluation Mode component found!");
                return;
            }

            if (!Application.isPlaying)
            {
                Debug.LogWarning("[GPTAPIDebugger] Evaluation can only run in Play Mode!");
                return;
            }

            Debug.Log("[GPTAPIDebugger] Starting LLM evaluation...");
            evaluationMode.StartEvaluation();
        }

        [ContextMenu("Print Statistics")]
        public void PrintStatistics()
        {
            Debug.Log("═══════════════════════════════════════");
            Debug.Log("    GPT API Debugger Statistics");
            Debug.Log("═══════════════════════════════════════");
            Debug.Log($"Total Requests:      {totalRequests}");
            Debug.Log($"Successful:          {successfulRequests}");
            Debug.Log($"Failed:              {failedRequests}");
            Debug.Log($"Success Rate:        {(totalRequests > 0 ? (successfulRequests * 100.0 / totalRequests) : 0):F1}%");
            Debug.Log($"Avg Response Time:   {averageResponseTime:F2}s");

            if (gptAssistant != null)
            {
                Debug.Log($"Current Provider:    {gptAssistant.GetCurrentProvider()}");
                Debug.Log($"API Key Status:      {gptAssistant.GetAPIKeyStatus()}");
            }

            if (voiceBridge != null)
            {
                Debug.Log($"Voice Bridge:        {voiceBridge.GetStatus()}");
            }

            if (evaluationMode != null)
            {
                Debug.Log($"Evaluation Mode:     {(evaluationMode.IsEvaluating() ? "Running" : "Ready")}");
                if (!string.IsNullOrEmpty(evaluationMode.GetCurrentSessionId()))
                {
                    Debug.Log($"Session ID:          {evaluationMode.GetCurrentSessionId()}");
                }
            }

            Debug.Log("═══════════════════════════════════════");
        }

        [ContextMenu("Reset Statistics")]
        public void ResetStatistics()
        {
            totalRequests = 0;
            successfulRequests = 0;
            failedRequests = 0;
            responseTimes.Clear();
            averageResponseTime = 0f;
            Debug.Log("[GPTAPIDebugger] Statistics reset");
        }

        #endregion

        #region Public API

        public void LogDebug(string message)
        {
            if (enableDebugLogs)
                Debug.Log($"[GPTAPIDebugger] {message}");
        }

        public void LogWarning(string message)
        {
            if (enableDebugLogs)
                Debug.LogWarning($"[GPTAPIDebugger] {message}");
        }

        public void LogError(string message)
        {
            Debug.LogError($"[GPTAPIDebugger] {message}");
        }

        public Dictionary<string, object> GetStatistics()
        {
            return new Dictionary<string, object>
            {
                { "TotalRequests", totalRequests },
                { "SuccessfulRequests", successfulRequests },
                { "FailedRequests", failedRequests },
                { "AverageResponseTime", averageResponseTime },
                { "SuccessRate", totalRequests > 0 ? successfulRequests * 100.0 / totalRequests : 0 }
            };
        }

        #endregion
    }
}
