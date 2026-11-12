// VoiceCommandIntegrationManager.cs - Central Hub for Voice Command Systems
// This script manages the integration between the existing keyword-based voice controller
// and the new GPT-powered natural language voice controller

using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class VoiceCommandIntegrationManager : MonoBehaviour
{
    [Header("Voice Controller References")]
    [SerializeField] private VoiceCommandController keywordVoiceController;
    [SerializeField] private VoiceCommandGPTBridge gptVoiceController;

    [Header("Integration Settings")]
    [SerializeField] private bool useGPTEnhancement = true;  // Enable GPT for complex commands
    [SerializeField] private bool fallbackToKeywords = true; // Use keywords if GPT fails
    [SerializeField] private float gptTimeout = 10f;        // Timeout for GPT requests
    [SerializeField] private bool debugMode = true;

    // No dedicated UI – reuse existing VoiceCommandController UI elements
    [Header("Status Colors")]
    [SerializeField] private Color systemActiveColor = Color.green;
    [SerializeField] private Color systemInactiveColor = Color.gray;
    [SerializeField] private Color systemErrorColor = Color.red;

    // Events
    public System.Action<string, bool> OnCommandProcessed;
    public System.Action<VoiceSystemType> OnSystemChanged;

    public enum VoiceSystemType
    {
        Keywords,
        GPT,
        Hybrid
    }

    private VoiceSystemType currentSystemType = VoiceSystemType.Hybrid;
    private bool isInitialized = false;

    private void Start()
    {
        InitializeVoiceIntegration();
        UpdateSystemStatus();
    }

    private void InitializeVoiceIntegration()
    {
        // Auto-find controllers if not assigned
        if (keywordVoiceController == null)
            keywordVoiceController = FindFirstObjectByType<VoiceCommandController>();

        if (gptVoiceController == null)
            gptVoiceController = FindFirstObjectByType<VoiceCommandGPTBridge>();

        // Subscribe to events
        if (keywordVoiceController != null)
        {
            keywordVoiceController.OnCommandProcessed += HandleKeywordCommand;
        }

        if (gptVoiceController != null)
        {
            gptVoiceController.OnCommandInterpreted += HandleGPTCommand;
            gptVoiceController.OnGPTError += HandleGPTError;
        }

        isInitialized = true;

        if (debugMode)
        {
            Debug.Log($"[VOICE INTEGRATION] Initialized - Keyword: {keywordVoiceController != null}, GPT: {gptVoiceController != null}");
        }
    }

    // Removed SetupUI – configuration handled via inspector

    private void HandleKeywordCommand(string command, bool success)
    {
        if (!isInitialized)
        {
            if (debugMode)
                Debug.LogWarning("[VOICE INTEGRATION] System not initialized, ignoring command");
            return;
        }

        if (debugMode)
        {
            Debug.Log($"[VOICE INTEGRATION] Keyword command: '{command}' - Success: {success}");
        }

        OnCommandProcessed?.Invoke(command, success);

        // If keyword command fails and GPT is enabled, try GPT interpretation
        if (!success && useGPTEnhancement && gptVoiceController != null && !gptVoiceController.IsProcessing())
        {
            if (debugMode)
            {
                Debug.Log($"[VOICE INTEGRATION] Keyword failed, trying GPT for: '{command}'");
            }

            StartCoroutine(ProcessGPTWithTimeout(command));
        }
    }

    private System.Collections.IEnumerator ProcessGPTWithTimeout(string command)
    {
        if (gptVoiceController == null) yield break;

        // Start processing the command
        gptVoiceController.ProcessTestCommand(command);

        // Wait for processing to complete or timeout
        float elapsedTime = 0f;
        while (gptVoiceController.IsProcessing() && elapsedTime < gptTimeout)
        {
            yield return new WaitForSeconds(0.1f);
            elapsedTime += 0.1f;
        }

        // Handle timeout
        if (elapsedTime >= gptTimeout && gptVoiceController.IsProcessing())
        {
            if (debugMode)
            {
                Debug.LogWarning($"[VOICE INTEGRATION] GPT processing timed out after {gptTimeout} seconds");
            }

            UpdateSystemStatus($"❌ GPT timeout after {gptTimeout}s");

            // Trigger fallback if enabled
            if (fallbackToKeywords)
            {
                currentSystemType = VoiceSystemType.Keywords;
                OnSystemChanged?.Invoke(currentSystemType);
            }
        }
    }

    private void HandleGPTCommand(VoiceCommandAction commandAction)
    {
        if (debugMode)
        {
            Debug.Log($"[VOICE INTEGRATION] GPT command: '{commandAction.actionType}' - Target: '{commandAction.targetName}' - Confidence: {commandAction.confidence:F2}");
        }

        OnCommandProcessed?.Invoke(commandAction.originalCommand, commandAction.confidence > 0.5f);
        UpdateSystemStatus($"GPT: {commandAction.explanation}");
    }

    private void HandleGPTError(string error)
    {
        if (debugMode)
        {
            Debug.LogError($"[VOICE INTEGRATION] GPT Error: {error}");
        }

        UpdateSystemStatus($"GPT Error: {error}");
        // Indicators handled in UpdateSystemStatus now

        // Fall back to keywords if enabled
        if (fallbackToKeywords)
        {
            currentSystemType = VoiceSystemType.Keywords;
            OnSystemChanged?.Invoke(currentSystemType);
        }
    }

    private void OnGPTToggleChanged(bool value)
    {
        useGPTEnhancement = value;
        currentSystemType = value ? VoiceSystemType.Hybrid : VoiceSystemType.Keywords;

        UpdateSystemStatus(value ? "GPT Enhancement Enabled" : "Using Keywords Only");
        // Indicator handled in UpdateSystemStatus

        OnSystemChanged?.Invoke(currentSystemType);

        if (debugMode)
        {
            Debug.Log($"[VOICE INTEGRATION] System changed to: {currentSystemType}");
        }
    }

    // Removed SetupGPTFromUI – API key set directly on GPT bridge via inspector

    private void UpdateSystemStatus(string message = "")
    {
        if (keywordVoiceController != null)
        {
            if (string.IsNullOrEmpty(message))
            {
                string keywordStatus = keywordVoiceController != null ? "✅" : "❌";
                string gptStatus = gptVoiceController != null && useGPTEnhancement ? "✅" : "❌";
                message = $"Keywords: {keywordStatus} | GPT: {gptStatus} | Mode: {currentSystemType}";
            }
            if (keywordVoiceController.voiceStatusText != null)
                keywordVoiceController.voiceStatusText.text = message;
            if (keywordVoiceController.lastCommandText != null)
                keywordVoiceController.lastCommandText.text = "GPT Integration → " + message;
            if (keywordVoiceController.voiceStatusIndicator != null)
                keywordVoiceController.voiceStatusIndicator.color = (gptVoiceController != null && useGPTEnhancement) ? systemActiveColor : systemInactiveColor;
        }
        if (debugMode)
        {
            Debug.Log("[VOICE INTEGRATION] " + message);
        }
    }

    private void UpdateSystemIndicators() { }

    // Public API
    public bool IsInitialized()
    {
        return isInitialized;
    }

    public bool IsGPTEnabled()
    {
        return useGPTEnhancement && gptVoiceController != null;
    }

    public bool IsKeywordSystemEnabled()
    {
        return keywordVoiceController != null;
    }

    public VoiceSystemType GetCurrentSystemType()
    {
        return currentSystemType;
    }

    public void EnableGPTSystem(bool enable)
    {
        useGPTEnhancement = enable;
        OnGPTToggleChanged(enable);
    }

    public void SetGPTAPIKey(string apiKey)
    {
        if (gptVoiceController != null)
            gptVoiceController.SetAPIKey(apiKey);
    }

    public string GetSystemStatusReport()
    {
        System.Text.StringBuilder report = new System.Text.StringBuilder();

        report.AppendLine("=== VOICE COMMAND SYSTEM STATUS ===");
        report.AppendLine($"Current Mode: {currentSystemType}");
        report.AppendLine($"Keyword System: {(keywordVoiceController != null ? "Available" : "Missing")}");
        report.AppendLine($"GPT System: {(gptVoiceController != null ? "Available" : "Missing")}");
        report.AppendLine($"GPT Enabled: {useGPTEnhancement}");
        report.AppendLine($"Fallback to Keywords: {fallbackToKeywords}");

        if (gptVoiceController != null)
        {
            report.AppendLine($"GPT Processing: {gptVoiceController.IsProcessing()}");
        }

        return report.ToString();
    }

    // Testing methods
    public void TestKeywordCommand(string command)
    {
        if (keywordVoiceController != null && debugMode)
        {
            Debug.Log($"[VOICE INTEGRATION] Testing keyword command: '{command}'");
            // Trigger keyword processing (this would depend on the keyword controller's API)
        }
    }

    public void TestGPTCommand(string command)
    {
        if (gptVoiceController != null && debugMode)
        {
            Debug.Log($"[VOICE INTEGRATION] Testing GPT command: '{command}'");
            gptVoiceController.ProcessTestCommand(command);
        }
    }

    // Context menu for testing
    [ContextMenu("Test: Hide Architecture")]
    private void TestHideArchitecture()
    {
        TestGPTCommand("Hide architecture");
    }

    [ContextMenu("Test: Spawn Module")]
    private void TestSpawnModule()
    {
        TestGPTCommand("Spawn module m1-1");
    }

    [ContextMenu("Test: Rotate Left")]
    private void TestRotateLeft()
    {
        TestGPTCommand("Rotate left");
    }

    [ContextMenu("Test: Make Transparent")]
    private void TestTransparency()
    {
        TestGPTCommand("Make it 50% transparent");
    }

    [ContextMenu("Print System Status")]
    private void PrintSystemStatus()
    {
        Debug.Log(GetSystemStatusReport());
    }

    private void OnDestroy()
    {
        // Unsubscribe from events
        if (keywordVoiceController != null)
        {
            keywordVoiceController.OnCommandProcessed -= HandleKeywordCommand;
        }

        if (gptVoiceController != null)
        {
            gptVoiceController.OnCommandInterpreted -= HandleGPTCommand;
            gptVoiceController.OnGPTError -= HandleGPTError;
        }
    }
}
