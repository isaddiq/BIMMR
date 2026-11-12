// ModuleSaveLoadManager.cs - Enhanced Voice Command Support for Save/Load Operations
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ModuleSaveLoadManager : MonoBehaviour
{
    [Header("References")]
    public ModuleManager moduleManager;
    public Button saveButton;             // Save current module position
    public Button loadButton;             // Load current module position
    public Button clearAllButton;         // Clear all saved positions
    public TMP_Text statusText;           // Shows save/load status and saved list

    [Header("Save Settings")]
    public string saveFileName = "module_positions.json";

    [Header("Voice Command Debug")]
    [SerializeField] private bool enableVoiceDebug = true;

    private Dictionary<string, ModulePosition> savedPositions = new Dictionary<string, ModulePosition>();
    private string saveFilePath;

    [System.Serializable]
    public class ModulePosition
    {
        public string moduleName;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
        public float timestamp;
        public string saveMethod = "manual"; // "manual", "voice", "auto"
    }

    [System.Serializable]
    private class ModulePositionData
    {
        public List<ModulePosition> positions = new List<ModulePosition>();
        public string saveVersion = "1.0";
        public float lastSaveTime;
        public int totalSaves;
    }

    private void Awake()
    {
        // Use Meta Quest 3 local storage path
        saveFilePath = Path.Combine(Application.persistentDataPath, saveFileName);

        SetupButtonListeners();

        // Subscribe to module manager events
        if (moduleManager != null)
        {
            moduleManager.OnModuleSpawned += OnModuleSpawned;
        }

        // Load saved positions from disk
        LoadFromDisk();
        RefreshSavedList();

        if (enableVoiceDebug)
        {
            Debug.Log($"[SAVE/LOAD DEBUG] Initialized with save path: {saveFilePath}");
            Debug.Log($"[SAVE/LOAD DEBUG] Loaded {savedPositions.Count} saved positions");
        }
    }

    private void SetupButtonListeners()
    {
        if (saveButton != null)
            saveButton.onClick.AddListener(() => SaveCurrentModulePosition("manual"));

        if (loadButton != null)
            loadButton.onClick.AddListener(() => LoadCurrentModulePosition("manual"));

        if (clearAllButton != null)
            clearAllButton.onClick.AddListener(ClearAllSaves);
    }

    private void OnModuleSpawned(GameObject spawnedModule)
    {
        if (spawnedModule == null) return;

        // When a module is spawned, automatically position it at saved location (if exists)
        string moduleName = GetCleanModuleName(spawnedModule.name);

        if (savedPositions.TryGetValue(moduleName, out var savedPos))
        {
            spawnedModule.transform.position = savedPos.position;
            spawnedModule.transform.rotation = savedPos.rotation;
            spawnedModule.transform.localScale = savedPos.scale;

            UpdateStatus($"Spawned {moduleName} at saved position");

            if (enableVoiceDebug)
            {
                Debug.Log($"[SAVE/LOAD DEBUG] Auto-positioned {moduleName} at saved location: {savedPos.position}");
            }
        }
        else
        {
            UpdateStatus($"Spawned {moduleName} at default position (no saved position)");

            if (enableVoiceDebug)
            {
                Debug.Log($"[SAVE/LOAD DEBUG] No saved position for {moduleName}, using default");
            }
        }
    }

    // Enhanced voice command integration methods
    public bool SaveCurrentModulePosition(string method = "voice")
    {
        if (enableVoiceDebug)
        {
            Debug.Log($"[SAVE/LOAD DEBUG] === SAVE REQUEST ===");
            Debug.Log($"[SAVE/LOAD DEBUG] Method: {method}");
        }

        if (moduleManager == null)
        {
            UpdateStatus("❌ ModuleManager not found");
            if (enableVoiceDebug) Debug.Log($"[SAVE/LOAD DEBUG] ❌ ModuleManager is null");
            return false;
        }

        var currentModule = moduleManager.GetCurrentModule();
        if (currentModule == null)
        {
            UpdateStatus("❌ No module selected to save");
            if (enableVoiceDebug) Debug.Log($"[SAVE/LOAD DEBUG] ❌ No current module");
            return false;
        }

        string moduleName = GetCleanModuleName(currentModule.name);

        // Save the current position
        savedPositions[moduleName] = new ModulePosition
        {
            moduleName = moduleName,
            position = currentModule.transform.position,
            rotation = currentModule.transform.rotation,
            scale = currentModule.transform.localScale,
            timestamp = Time.time,
            saveMethod = method
        };

        bool saveSuccess = SaveToDisk();

        if (saveSuccess)
        {
            UpdateStatus($"✅ Saved position for {moduleName} ({method})");
            RefreshSavedList();

            if (enableVoiceDebug)
            {
                Debug.Log($"[SAVE/LOAD DEBUG] ✅ Successfully saved {moduleName} position: {currentModule.transform.position}");
                Debug.Log($"[SAVE/LOAD DEBUG] Save method: {method}");
            }
            return true;
        }
        else
        {
            UpdateStatus($"❌ Failed to save {moduleName}");
            if (enableVoiceDebug) Debug.Log($"[SAVE/LOAD DEBUG] ❌ Save to disk failed");
            return false;
        }
    }

    public bool LoadCurrentModulePosition(string method = "voice")
    {
        if (enableVoiceDebug)
        {
            Debug.Log($"[SAVE/LOAD DEBUG] === LOAD REQUEST ===");
            Debug.Log($"[SAVE/LOAD DEBUG] Method: {method}");
        }

        if (moduleManager == null)
        {
            UpdateStatus("❌ ModuleManager not found");
            if (enableVoiceDebug) Debug.Log($"[SAVE/LOAD DEBUG] ❌ ModuleManager is null");
            return false;
        }

        var currentModule = moduleManager.GetCurrentModule();
        if (currentModule == null)
        {
            UpdateStatus("❌ No module selected to load");
            if (enableVoiceDebug) Debug.Log($"[SAVE/LOAD DEBUG] ❌ No current module");
            return false;
        }

        string moduleName = GetCleanModuleName(currentModule.name);

        if (savedPositions.TryGetValue(moduleName, out var savedPos))
        {
            currentModule.transform.position = savedPos.position;
            currentModule.transform.rotation = savedPos.rotation;
            currentModule.transform.localScale = savedPos.scale;

            UpdateStatus($"✅ Loaded position for {moduleName} ({method})");
            RefreshSavedList();

            if (enableVoiceDebug)
            {
                Debug.Log($"[SAVE/LOAD DEBUG] ✅ Successfully loaded {moduleName} to position: {savedPos.position}");
                Debug.Log($"[SAVE/LOAD DEBUG] Load method: {method}");
                Debug.Log($"[SAVE/LOAD DEBUG] Original save method: {savedPos.saveMethod}");
            }
            return true;
        }
        else
        {
            UpdateStatus($"❌ No saved position for {moduleName}");

            if (enableVoiceDebug)
            {
                Debug.Log($"[SAVE/LOAD DEBUG] ❌ No saved position found for {moduleName}");
                Debug.Log($"[SAVE/LOAD DEBUG] Available saved modules: {string.Join(", ", savedPositions.Keys)}");
            }
            return false;
        }
    }

    // Enhanced save/load methods for different scenarios
    public bool SaveModuleByName(string moduleName, string method = "voice")
    {
        if (enableVoiceDebug)
        {
            Debug.Log($"[SAVE/LOAD DEBUG] === SAVE BY NAME REQUEST ===");
            Debug.Log($"[SAVE/LOAD DEBUG] Module: '{moduleName}', Method: {method}");
        }

        // First try to spawn the module if it's not current
        if (moduleManager != null)
        {
            var currentModule = moduleManager.GetCurrentModule();
            string currentModuleName = currentModule != null ? GetCleanModuleName(currentModule.name) : "";
            string targetModuleName = GetCleanModuleName(moduleName);

            if (currentModuleName != targetModuleName)
            {
                // Try to spawn the target module first
                bool spawnSuccess = moduleManager.SpawnModuleByName(moduleName);
                if (!spawnSuccess)
                {
                    UpdateStatus($"❌ Could not spawn module '{moduleName}' to save");
                    return false;
                }

                // Wait a frame for the module to be properly spawned
                StartCoroutine(DelayedSave(method));
                return true;
            }
        }

        // If it's already the current module, save directly
        return SaveCurrentModulePosition(method);
    }

    private System.Collections.IEnumerator DelayedSave(string method)
    {
        yield return null; // Wait one frame
        SaveCurrentModulePosition(method);
    }

    public bool LoadModuleByName(string moduleName, string method = "voice")
    {
        if (enableVoiceDebug)
        {
            Debug.Log($"[SAVE/LOAD DEBUG] === LOAD BY NAME REQUEST ===");
            Debug.Log($"[SAVE/LOAD DEBUG] Module: '{moduleName}', Method: {method}");
        }

        string cleanModuleName = GetCleanModuleName(moduleName);

        // Check if we have a saved position for this module
        if (!savedPositions.ContainsKey(cleanModuleName))
        {
            UpdateStatus($"❌ No saved position for module '{moduleName}'");
            if (enableVoiceDebug)
            {
                Debug.Log($"[SAVE/LOAD DEBUG] ❌ No saved position for '{cleanModuleName}'");
                Debug.Log($"[SAVE/LOAD DEBUG] Available: {string.Join(", ", savedPositions.Keys)}");
            }
            return false;
        }

        // First spawn the module
        if (moduleManager != null)
        {
            bool spawnSuccess = moduleManager.SpawnModuleByName(moduleName);
            if (!spawnSuccess)
            {
                UpdateStatus($"❌ Could not spawn module '{moduleName}' to load");
                return false;
            }

            // The OnModuleSpawned event will automatically position it at the saved location
            UpdateStatus($"✅ Loaded module '{moduleName}' at saved position ({method})");

            if (enableVoiceDebug)
            {
                Debug.Log($"[SAVE/LOAD DEBUG] ✅ Successfully loaded module '{moduleName}' via spawn");
            }
            return true;
        }

        return false;
    }

    public bool SaveAllModules(string method = "voice")
    {
        if (enableVoiceDebug)
        {
            Debug.Log($"[SAVE/LOAD DEBUG] === SAVE ALL MODULES REQUEST ===");
        }

        if (moduleManager == null) return false;

        var availableModules = moduleManager.GetAvailableModuleNames();
        int savedCount = 0;

        foreach (string moduleName in availableModules)
        {
            if (SaveModuleByName(moduleName, method))
            {
                savedCount++;
            }
        }

        UpdateStatus($"✅ Saved {savedCount}/{availableModules.Count} modules ({method})");

        if (enableVoiceDebug)
        {
            Debug.Log($"[SAVE/LOAD DEBUG] ✅ Saved {savedCount} out of {availableModules.Count} modules");
        }

        return savedCount > 0;
    }

    private void ClearAllSaves()
    {
        savedPositions.Clear();
        SaveToDisk();
        UpdateStatus("✅ Cleared all saved positions");
        RefreshSavedList();

        if (enableVoiceDebug)
        {
            Debug.Log("[SAVE/LOAD DEBUG] All saved positions cleared");
        }
    }

    private string GetCleanModuleName(string name)
    {
        // Remove (Clone) and trim whitespace to get clean module name
        return string.IsNullOrEmpty(name) ? string.Empty : name.Replace("(Clone)", "").Trim();
    }

    private bool SaveToDisk()
    {
        try
        {
            var data = new ModulePositionData();
            data.lastSaveTime = Time.time;
            data.totalSaves = savedPositions.Count;

            foreach (var pos in savedPositions.Values)
            {
                data.positions.Add(pos);
            }

            string jsonData = JsonUtility.ToJson(data, true);
            File.WriteAllText(saveFilePath, jsonData);

            if (enableVoiceDebug)
            {
                Debug.Log($"[SAVE/LOAD DEBUG] Successfully saved {data.positions.Count} positions to: {saveFilePath}");
            }
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[SAVE/LOAD DEBUG] Failed to save: {e.Message}");
            UpdateStatus($"❌ Save failed: {e.Message}");
            return false;
        }
    }

    private void LoadFromDisk()
    {
        if (!File.Exists(saveFilePath))
        {
            savedPositions.Clear();
            if (enableVoiceDebug)
            {
                Debug.Log("[SAVE/LOAD DEBUG] No save file found, starting fresh");
            }
            return;
        }

        try
        {
            string jsonData = File.ReadAllText(saveFilePath);
            var data = JsonUtility.FromJson<ModulePositionData>(jsonData);

            savedPositions.Clear();
            if (data?.positions != null)
            {
                foreach (var pos in data.positions)
                {
                    // Skip malformed entries rather than failing the whole load
                    if (pos != null && !string.IsNullOrEmpty(pos.moduleName))
                        savedPositions[pos.moduleName] = pos;
                }
            }

            if (enableVoiceDebug)
            {
                Debug.Log($"[SAVE/LOAD DEBUG] Loaded {savedPositions.Count} module positions from disk");
            }
            UpdateStatus($"✅ Loaded {savedPositions.Count} saved positions");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[SAVE/LOAD DEBUG] Failed to load: {e.Message}");
            savedPositions.Clear();
            UpdateStatus($"❌ Load failed: {e.Message}");
        }
    }

    private void UpdateStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
        BIMLog.Verbose($"[SAVE/LOAD] {message}");
    }

    private void RefreshSavedList()
    {
        if (statusText == null) return;

        var sorted = savedPositions.Values
            .OrderByDescending(p => p.timestamp)
            .ToList();

        string listText = $"Saved Modules ({sorted.Count}):\n";
        if (sorted.Count == 0)
        {
            listText += "<none>";
        }
        else
        {
            for (int i = 0; i < Mathf.Min(sorted.Count, 10); i++) // Show max 10
            {
                var pos = sorted[i];
                string methodIcon = pos.saveMethod == "voice" ? "🎤" :
                                  pos.saveMethod == "auto" ? "🤖" : "👆";
                listText += $"{methodIcon} {pos.moduleName}\n";
            }

            if (sorted.Count > 10)
            {
                listText += $"... and {sorted.Count - 10} more";
            }
        }

        statusText.text = listText.TrimEnd();
    }

    // Voice command integration methods (called by VoiceCommandController)
    public void SaveCurrentVoice()
    {
        SaveCurrentModulePosition("voice");
    }

    public void LoadCurrentVoice()
    {
        LoadCurrentModulePosition("voice");
    }

    public bool SaveVoiceCommand(string command = "")
    {
        if (enableVoiceDebug)
        {
            Debug.Log($"[SAVE/LOAD DEBUG] === VOICE SAVE COMMAND ===");
            Debug.Log($"[SAVE/LOAD DEBUG] Command: '{command}'");
        }

        // Handle different save command variations
        if (!string.IsNullOrEmpty(command) && command.ToLower().Contains("all"))
        {
            return SaveAllModules("voice");
        }
        else
        {
            return SaveCurrentModulePosition("voice");
        }
    }

    public bool LoadVoiceCommand(string command = "")
    {
        if (enableVoiceDebug)
        {
            Debug.Log($"[SAVE/LOAD DEBUG] === VOICE LOAD COMMAND ===");
            Debug.Log($"[SAVE/LOAD DEBUG] Command: '{command}'");
        }

        return LoadCurrentModulePosition("voice");
    }

    // Get information about saved positions
    public bool HasSavedPosition(string moduleName)
    {
        string cleanName = GetCleanModuleName(moduleName);
        return savedPositions.ContainsKey(cleanName);
    }

    public Vector3 GetSavedPosition(string moduleName)
    {
        string cleanName = GetCleanModuleName(moduleName);
        return savedPositions.TryGetValue(cleanName, out var savedPos) ? savedPos.position : Vector3.zero;
    }

    public int GetSavedPositionCount()
    {
        return savedPositions.Count;
    }

    public List<string> GetSavedModuleNames()
    {
        return new List<string>(savedPositions.Keys);
    }

    public List<string> GetVoiceSavedModules()
    {
        return savedPositions.Values
            .Where(p => p.saveMethod == "voice")
            .Select(p => p.moduleName)
            .ToList();
    }

    // Get save file info
    public string GetSaveFilePath()
    {
        return saveFilePath;
    }

    public string GetSaveSystemStatus()
    {
        string status = "=== SAVE/LOAD SYSTEM STATUS ===\n";
        status += $"📁 Save File: {(File.Exists(saveFilePath) ? "✅ Exists" : "❌ Not Found")}\n";
        status += $"💾 Total Saved: {savedPositions.Count} modules\n";
        status += $"🎤 Voice Saved: {GetVoiceSavedModules().Count} modules\n";
        status += $"📍 Current Module: {(moduleManager?.GetCurrentModule()?.name ?? "None")}\n";

        if (savedPositions.Count > 0)
        {
            status += "\n--- Recent Saves ---\n";
            var recent = savedPositions.Values
                .OrderByDescending(p => p.timestamp)
                .Take(5)
                .ToList();

            foreach (var pos in recent)
            {
                string methodIcon = pos.saveMethod == "voice" ? "🎤" :
                                  pos.saveMethod == "auto" ? "🤖" : "👆";
                status += $"{methodIcon} {pos.moduleName}\n";
            }
        }

        return status;
    }

    private void OnDestroy()
    {
        // Clean up event subscriptions
        if (moduleManager != null)
        {
            moduleManager.OnModuleSpawned -= OnModuleSpawned;
        }
    }

    // Editor testing methods
    [ContextMenu("Debug: Save Current")]
    private void DebugSaveCurrent()
    {
        SaveCurrentModulePosition("debug");
    }

    [ContextMenu("Debug: Load Current")]
    private void DebugLoadCurrent()
    {
        LoadCurrentModulePosition("debug");
    }

    [ContextMenu("Debug: Show Save Status")]
    private void DebugShowSaveStatus()
    {
        Debug.Log(GetSaveSystemStatus());
    }

    [ContextMenu("Debug: Clear All Saves")]
    private void DebugClearAllSaves()
    {
        ClearAllSaves();
    }

    [ContextMenu("Debug: Save All Modules")]
    private void DebugSaveAllModules()
    {
        SaveAllModules("debug");
    }
}