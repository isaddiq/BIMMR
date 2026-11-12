// VoiceCommandSetupWizard.cs (Simplified)
// This class was previously a multi-step UI wizard. Per updated requirements,
// all extra UI has been removed. It now only auto-connects the GPT bridge and
// integration manager at startup and can be safely deleted if not needed.

using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("Voice Commands/GPT Auto Connector (No UI)")]
public class VoiceCommandSetupWizard : MonoBehaviour
{
    [Header("System References (Auto)"), Tooltip("Optional explicit reference to the GPT bridge; auto-found if null")]
    [SerializeField] private VoiceCommandGPTBridge gptBridge;
    [Header("Optional integration manager reference; auto-found if null")]
    [SerializeField] private VoiceCommandIntegrationManager integrationManager;
    [Header("Auto Enable GPT On Start")]
    [SerializeField] private bool enableGPTOnStart = true;
    [SerializeField] private bool logStatus = true;

    private bool autoConnected = false;

    private void Awake()
    {
        AutoConnect();
    }

    private void Start()
    {
        if (enableGPTOnStart && integrationManager != null && gptBridge != null)
        {
            integrationManager.EnableGPTSystem(true);
        }
        if (logStatus)
            Debug.Log($"[GPT AUTO CONNECT] Ready. GPT={(gptBridge != null)} Integration={(integrationManager != null)} Enabled={enableGPTOnStart}");
    }

    private void AutoConnect()
    {
        if (gptBridge == null)
            gptBridge = FindFirstObjectByType<VoiceCommandGPTBridge>();
        if (integrationManager == null)
            integrationManager = FindFirstObjectByType<VoiceCommandIntegrationManager>();
        autoConnected = gptBridge != null && integrationManager != null;
    }

    // Public minimal API (legacy compatibility)
    public void ForceReconnect()
    {
        AutoConnect();
        if (logStatus)
            Debug.Log("[GPT AUTO CONNECT] ForceReconnect executed: " + (autoConnected ? "Success" : "Missing components"));
    }

    public bool IsConnected()
    {
        return autoConnected;
    }

    // Kept for backwards compatibility (no-op)
    public bool IsWizardComplete() => true;
    public void ShowWizard() { if (logStatus) Debug.Log("[GPT AUTO CONNECT] ShowWizard() called (deprecated, no UI)."); }
    public void HideWizard() { }

    // Context menu helpers for quick testing in editor
    [ContextMenu("Force Reconnect Components")] private void CtxReconnect() => ForceReconnect();
    [ContextMenu("Print Connection Status")] private void CtxStatus() => BIMLog.Verbose(GetStatusReport());

    public string GetStatusReport()
    {
        return $"=== GPT AUTO CONNECT STATUS ===\nGPT Bridge: {(gptBridge ? "OK" : "Missing")}\nIntegration Manager: {(integrationManager ? "OK" : "Missing")}\nConnected: {autoConnected}\nAuto Enable: {enableGPTOnStart}";
    }
}
