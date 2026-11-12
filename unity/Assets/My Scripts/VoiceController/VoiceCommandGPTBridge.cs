// VoiceCommandGPTBridge.cs - Natural Language Voice Command Processing with ChatGPT GPT-4o Mini
// This script bridges the voice controller with ChatGPT to understand natural language commands
// and translate them into specific actions for the BIM-MR system

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using TMPro; // Needed to access existing VoiceCommandController UI if present
using Newtonsoft.Json;
using ArchiBIM.QualityControl;

[System.Serializable]
public enum GPTModelType
{
    GPT4oMini,
    GPT4o,
    GPT4Turbo,
    GPT4,
    GPT35Turbo
}

[System.Serializable]
public class VoiceCommandAction
{
    public string actionType;           // "spawn", "hide", "show", "rotate", "move", "scale", "save", "load", "documentation"
    public string targetType;           // "module", "component", "architecture", "all"
    public string targetName;           // specific module name or component name
    [SerializeField] public Dictionary<string, string> parameters; // additional parameters like direction, amount, etc.
    public float confidence;            // GPT confidence in the interpretation
    public string originalCommand;      // original voice input
    public string explanation;          // GPT explanation of what it understood

    public VoiceCommandAction()
    {
        parameters = new Dictionary<string, string>();
    }
}

public class VoiceCommandGPTBridge : MonoBehaviour
{
    [System.NonSerialized] private string openAIApiKey = "";   // never serialized: read from AEC.VoiceAssistant.ApiKeyProvider
    [Header("ChatGPT Configuration")]
    [SerializeField] private GPTModelType selectedGPTModel = GPTModelType.GPT4oMini;
    [SerializeField] private int maxTokens = 300;
    [SerializeField] private float temperature = 0.1f; // Low temperature for more consistent commands
    [SerializeField] private int requestTimeout = 15;

    [Header("Component References")]
    [SerializeField] private VoiceCommandController voiceController;
    [SerializeField] private ModuleManager moduleManager;
    [SerializeField] private ModuleSaveLoadManager saveLoadManager;
    [SerializeField] private ModuleTransparencyController transparencyController;
    [SerializeField] private ModuleRotationController rotationController;
    [SerializeField] private DocumentManager documentManager;
    [SerializeField] private QualityInspectionSystem inspectionSystem;

    [Header("Debug Settings")]
    [SerializeField] private bool enableDebugLogging = true;
    [SerializeField] private bool showGPTPrompts = false;
    [SerializeField] public bool testMode = false;
    [Tooltip("When false GPT bridge ignores incoming voice inputs (acts as disabled).")]
    [SerializeField] private bool gptEnabled = true;

    // GPT Response structures
    [System.Serializable]
    private class GPTResponse
    {
        public Choice[] choices;
        public Usage usage;
    }

    [System.Serializable]
    private class Choice
    {
        public Message message;
    }

    [System.Serializable]
    private class Message
    {
        public string role;
        public string content;
    }

    [System.Serializable]
    private class Usage
    {
        public int prompt_tokens;
        public int completion_tokens;
        public int total_tokens;
    }

    [System.Serializable]
    private class GPTRequest
    {
        public string model;
        public Message[] messages;
        public int max_tokens;
        public float temperature;
    }

    // Events
    public event Action<VoiceCommandAction> OnCommandInterpreted;
    public event Action<string> OnGPTError;

    private bool isProcessing = false;
    private UnityWebRequest currentRequest;

    // GPT Model names mapping
    private readonly Dictionary<GPTModelType, string> modelNames = new Dictionary<GPTModelType, string>
    {
        { GPTModelType.GPT4oMini, "gpt-4o-mini" },
        { GPTModelType.GPT4o, "gpt-4o" },
        { GPTModelType.GPT4Turbo, "gpt-4-turbo-preview" },
        { GPTModelType.GPT4, "gpt-4" },
        { GPTModelType.GPT35Turbo, "gpt-3.5-turbo" }
    };

    // System prompt for natural language command interpretation
    private string systemPrompt = @"You are a voice command interpreter for a BIM (Building Information Modeling) Mixed Reality system. 
Your task is to interpret natural language voice commands and convert them into structured JSON actions.

AVAILABLE ACTIONS:
- spawn/load/create: Create or load modules/components
- hide/show/toggle: Control visibility of architecture/components  
- rotate: Rotate modules (left, right, up, down, clockwise, counterclockwise) with optional angle (extract numbers like ""90 degrees"", ""45°"", ""180"")
- move: Move modules (up, down, left, right, forward, backward) with optional distance (extract numbers like ""1 meter"", ""2m"", ""50cm"")
- scale: Scale modules (up/increase, down/decrease, reset)
- save: Save current state or module positions
- load: Load saved state or module positions
- documentation/docs/help: Open documentation or help
- transparency: Control transparency/opacity of modules
- inspect: Start quality inspection or screenshot

TARGET TYPES:
- module: Specific building modules (m1-1, m1-2, m2-1, etc.)
- component: Building components (architecture, structural, mep, walls, ceiling, floor, etc.)
- architecture: Architectural elements specifically
- all: All components or modules

RESPONSE FORMAT (JSON only):
{
  ""actionType"": ""spawn|hide|show|rotate|move|scale|save|load|documentation|transparency|inspect"",
  ""targetType"": ""module|component|architecture|all"",
  ""targetName"": ""specific name or empty"",
  ""parameters"": {
    ""direction"": ""left|right|up|down|forward|backward|clockwise|counterclockwise"",
    ""amount"": ""small|medium|large|1|2|3|etc"",
    ""value"": ""0.5|1.0|reset|etc""
  },
  ""confidence"": 0.8,
  ""originalCommand"": ""original voice input"",
  ""explanation"": ""brief explanation of interpretation""
}

EXAMPLES:
""Hide architecture"" → {""actionType"":""hide"",""targetType"":""architecture"",""targetName"":"""",""parameters"":{},""confidence"":0.9,""originalCommand"":""Hide architecture"",""explanation"":""Hide architectural components""}
""Spawn module m1-2"" → {""actionType"":""spawn"",""targetType"":""module"",""targetName"":""m1-2"",""parameters"":{},""confidence"":0.9,""originalCommand"":""Spawn module m1-2"",""explanation"":""Create module m1-2""}
""Rotate left by 90 degrees"" → {""actionType"":""rotate"",""targetType"":""module"",""targetName"":"""",""parameters"":{""direction"":""left"",""angle"":""90""},""confidence"":0.9,""originalCommand"":""Rotate left by 90 degrees"",""explanation"":""Rotate current module left by 90 degrees""}
""Move up 2 meters"" → {""actionType"":""move"",""targetType"":""module"",""targetName"":"""",""parameters"":{""direction"":""up"",""amount"":""2""},""confidence"":0.9,""originalCommand"":""Move up 2 meters"",""explanation"":""Move current module up by 2 meters""}
""Rotate right"" → {""actionType"":""rotate"",""targetType"":""module"",""targetName"":"""",""parameters"":{""direction"":""right""},""confidence"":0.8,""originalCommand"":""Rotate right"",""explanation"":""Rotate current module to the right""}
""Make it transparent"" → {""actionType"":""transparency"",""targetType"":""module"",""targetName"":"""",""parameters"":{""value"":""0.5""},""confidence"":0.7,""originalCommand"":""Make it transparent"",""explanation"":""Set module transparency to 50%""}
""Save current position"" → {""actionType"":""save"",""targetType"":""module"",""targetName"":"""",""parameters"":{},""confidence"":0.9,""originalCommand"":""Save current position"",""explanation"":""Save current module position""}

KOREAN SUPPORT:
- 숨기기/감추기 = hide
- 보이기/표시 = show  
- 회전/돌리기 = rotate
- 이동/움직이기 = move
- 크기조절/스케일 = scale
- 저장 = save
- 불러오기/로드 = load
- 도움말/문서 = documentation
- 투명 = transparency
- 모듈 = module
- 건축/아키텍처 = architecture

IMPORTANT: Always respond with ONLY the JSON object, no additional text or formatting.";

    private void Start()
    {
        InitializeComponents();
        if (voiceController != null)
        {
            voiceController.OnTranscriptionReceived += HandleVoiceInput;
        }
    }

    private void InitializeComponents()
    {
        // Auto-find components if not assigned
        if (voiceController == null)
            voiceController = FindFirstObjectByType<VoiceCommandController>();

        if (moduleManager == null)
            moduleManager = FindAnyObjectByType<ModuleManager>();

        if (saveLoadManager == null)
            saveLoadManager = FindFirstObjectByType<ModuleSaveLoadManager>();

        if (transparencyController == null)
            transparencyController = FindFirstObjectByType<ModuleTransparencyController>();

        if (rotationController == null)
            rotationController = FindFirstObjectByType<ModuleRotationController>();

        if (documentManager == null)
            documentManager = FindFirstObjectByType<DocumentManager>();

        if (inspectionSystem == null)
            inspectionSystem = FindFirstObjectByType<QualityInspectionSystem>();

        if (enableDebugLogging)
        {
            Debug.Log($"[GPT BRIDGE] Initialized with components: " +
                     $"Voice:{voiceController != null}, Module:{moduleManager != null}, " +
                     $"Save:{saveLoadManager != null}, Transparency:{transparencyController != null}, " +
                     $"Rotation:{rotationController != null}, Docs:{documentManager != null}, " +
                     $"Inspection:{inspectionSystem != null}");
        }
    }

    private void HandleVoiceInput(string transcription, float confidence)
    {
        if (!gptEnabled || string.IsNullOrEmpty(transcription) || isProcessing)
            return;

        if (enableDebugLogging)
        {
            Debug.Log($"[GPT BRIDGE] Received voice input: '{transcription}' (confidence: {confidence:F2})");
        }

        StartCoroutine(ProcessVoiceCommandWithGPT(transcription));
    }

    private IEnumerator ProcessVoiceCommandWithGPT(string voiceInput)
    {
        // Sanitize API key before use (handles pasted whitespace or hidden chars)
        if (string.IsNullOrEmpty(openAIApiKey)) openAIApiKey = AEC.VoiceAssistant.ApiKeyProvider.OpenAI;
        if (!string.IsNullOrEmpty(openAIApiKey))
        {
            string cleaned = SanitizeKey(openAIApiKey);
            if (cleaned != openAIApiKey)
            {
                if (enableDebugLogging)
                    Debug.Log("[GPT BRIDGE] API key sanitized (whitespace/hidden chars removed)");
                openAIApiKey = cleaned;
            }
        }

        if (!IsValidAPIKey(openAIApiKey))
        {
            Debug.LogError("[GPT BRIDGE] OpenAI API key missing or invalid. Keys must start with 'sk-' and be > 20 chars.");
            UpdateStatus("❌ Invalid / Missing API key", Color.red);
            yield break;
        }

        isProcessing = true;
        UpdateStatus("🤖 GPT processing...", Color.yellow);

        // Prepare the context for GPT
        string contextInfo = GetCurrentSceneContext();
        string fullPrompt = $"{systemPrompt}\n\nCURRENT SCENE CONTEXT:\n{contextInfo}\n\nVOICE COMMAND TO INTERPRET: \"{voiceInput}\"";

        if (showGPTPrompts && enableDebugLogging)
        {
            Debug.Log($"[GPT BRIDGE] Sending prompt to GPT:\n{fullPrompt}");
        }

        // Create GPT request
        var request = new GPTRequest
        {
            model = modelNames[selectedGPTModel],
            messages = new Message[]
            {
                new Message { role = "system", content = systemPrompt },
                new Message { role = "user", content = $"CURRENT SCENE CONTEXT:\n{contextInfo}\n\nVOICE COMMAND: \"{voiceInput}\"" }
            },
            max_tokens = maxTokens,
            temperature = temperature
        };

        string jsonRequest = JsonConvert.SerializeObject(request);
        byte[] requestData = Encoding.UTF8.GetBytes(jsonRequest);

        currentRequest = new UnityWebRequest("https://api.openai.com/v1/chat/completions", "POST");
        currentRequest.uploadHandler = new UploadHandlerRaw(requestData);
        currentRequest.downloadHandler = new DownloadHandlerBuffer();
        currentRequest.SetRequestHeader("Content-Type", "application/json");
        currentRequest.SetRequestHeader("Authorization", "Bearer " + openAIApiKey);
        currentRequest.timeout = requestTimeout;

        yield return currentRequest.SendWebRequest();

        if (currentRequest.result == UnityWebRequest.Result.Success)
        {
            string responseText = currentRequest.downloadHandler.text;

            if (enableDebugLogging)
            {
                Debug.Log($"[GPT BRIDGE] GPT Response: {responseText}");
            }

            ProcessGPTResponse(responseText, voiceInput);
        }
        else
        {
            string error = $"GPT API Error: {currentRequest.error}";
            Debug.LogError($"[GPT BRIDGE] {error}");
            UpdateStatus("❌ GPT request failed", Color.red);
            OnGPTError?.Invoke(error);
        }

        currentRequest?.Dispose();
        currentRequest = null;
        isProcessing = false;
    }

    private string GetCurrentSceneContext()
    {
        StringBuilder context = new StringBuilder();

        // Current module information
        if (moduleManager != null)
        {
            var currentModule = moduleManager.GetCurrentModule();
            if (currentModule != null)
            {
                context.AppendLine($"Current Module: {currentModule.name}");
                context.AppendLine($"Position: {currentModule.transform.position}");
                context.AppendLine($"Rotation: {currentModule.transform.rotation.eulerAngles}");
                context.AppendLine($"Scale: {currentModule.transform.localScale}");
            }

            // Available modules
            var availableModules = moduleManager.GetAvailableModuleNames();
            if (availableModules != null && availableModules.Count > 0)
            {
                context.AppendLine($"Available Modules: {string.Join(", ", availableModules)}");
            }
        }

        // Transparency state
        if (transparencyController != null)
        {
            context.AppendLine("Transparency Control: Available");
        }

        // Save/Load information
        if (saveLoadManager != null)
        {
            context.AppendLine($"Saved Positions: {saveLoadManager.GetSavedPositionCount()}");
        }

        return context.ToString();
    }

    private void ProcessGPTResponse(string responseJson, string originalInput)
    {
        try
        {
            var gptResponse = JsonConvert.DeserializeObject<GPTResponse>(responseJson);

            if (gptResponse.choices != null && gptResponse.choices.Length > 0)
            {
                string content = gptResponse.choices[0].message.content.Trim();

                // Remove any markdown formatting
                if (content.StartsWith("```json"))
                {
                    content = content.Substring(7);
                    if (content.EndsWith("```"))
                        content = content.Substring(0, content.Length - 3);
                }
                else if (content.StartsWith("```"))
                {
                    content = content.Substring(3);
                    if (content.EndsWith("```"))
                        content = content.Substring(0, content.Length - 3);
                }

                content = content.Trim();

                if (enableDebugLogging)
                {
                    Debug.Log($"[GPT BRIDGE] Parsed GPT content: {content}");
                }

                // Parse the command action
                var commandAction = JsonConvert.DeserializeObject<VoiceCommandAction>(content);
                commandAction.originalCommand = originalInput;

                if (enableDebugLogging)
                {
                    Debug.Log($"[GPT BRIDGE] Interpreted command - Action: {commandAction.actionType}, " +
                             $"Target: {commandAction.targetType}, Name: {commandAction.targetName}, " +
                             $"Confidence: {commandAction.confidence:F2}");
                }

                // Update UI with interpretation
                // Reuse existing voice controller real-time transcription area if available
                if (voiceController != null && voiceController.realTimeTranscriptionText != null)
                {
                    voiceController.realTimeTranscriptionText.text = $"GPT → {commandAction.explanation}";
                }

                // Execute the command
                bool success = ExecuteCommand(commandAction);

                UpdateStatus(success ? $"✅ {commandAction.explanation}" : $"❌ {commandAction.explanation}", success ? Color.green : Color.red);

                // Notify listeners
                OnCommandInterpreted?.Invoke(commandAction);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[GPT BRIDGE] Failed to parse GPT response: {e.Message}");
            Debug.LogError($"[GPT BRIDGE] Response content: {responseJson}");
            UpdateStatus("❌ Failed to parse GPT response", Color.red);
        }
    }

    private bool ExecuteCommand(VoiceCommandAction command)
    {
        if (enableDebugLogging)
        {
            Debug.Log($"[GPT BRIDGE] Executing command: {command.actionType} on {command.targetType} {command.targetName}");
        }

        try
        {
            switch (command.actionType.ToLower())
            {
                case "spawn":
                case "load":
                case "create":
                    return ExecuteSpawnCommand(command);

                case "hide":
                case "show":
                case "toggle":
                    return ExecuteVisibilityCommand(command);

                case "rotate":
                    return ExecuteRotateCommand(command);

                case "move":
                    return ExecuteMoveCommand(command);

                case "scale":
                    return ExecuteScaleCommand(command);

                case "save":
                    return ExecuteSaveCommand(command);

                case "transparency":
                    return ExecuteTransparencyCommand(command);

                case "documentation":
                case "docs":
                case "help":
                    return ExecuteDocumentationCommand(command);

                case "inspect":
                    return ExecuteInspectionCommand(command);

                default:
                    Debug.LogWarning($"[GPT BRIDGE] Unknown command action: {command.actionType}");
                    return false;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[GPT BRIDGE] Error executing command: {e.Message}");
            return false;
        }
    }

    private bool ExecuteSpawnCommand(VoiceCommandAction command)
    {
        if (moduleManager == null) return false;

        if (command.targetType == "module" && !string.IsNullOrEmpty(command.targetName))
        {
            return moduleManager.SpawnModuleByName(command.targetName);
        }

        return false;
    }

    private bool ExecuteVisibilityCommand(VoiceCommandAction command)
    {
        if (moduleManager == null) return false;

        bool targetState = command.actionType.ToLower() == "show";

        if (command.actionType.ToLower() == "toggle")
        {
            if (command.targetType == "architecture")
            {
                moduleManager.ToggleArchitecturalComponents();
                return true;
            }
            else if (command.targetType == "all")
            {
                moduleManager.ToggleAllComponents();
                return true;
            }
            else if (!string.IsNullOrEmpty(command.targetName))
            {
                targetState = !moduleManager.GetComponentVisibility(command.targetName);
            }
        }

        if (command.targetType == "architecture")
        {
            moduleManager.SetArchitecturalVisibility(targetState);
            return true;
        }
        else if (command.targetType == "all")
        {
            moduleManager.SetAllComponentsVisibility(targetState);
            return true;
        }
        else if (!string.IsNullOrEmpty(command.targetName))
        {
            return moduleManager.SetComponentVisibility(command.targetName, targetState);
        }

        return false;
    }

    private bool ExecuteRotateCommand(VoiceCommandAction command)
    {
        if (rotationController == null && moduleManager == null) return false;

        string direction = command.parameters.ContainsKey("direction") ? command.parameters["direction"] : "";
        float angle = 90f; // Default rotation angle

        // Check for specific angle amount
        if (command.parameters.ContainsKey("angle"))
        {
            string angleStr = command.parameters["angle"];
            if (float.TryParse(angleStr, out float parsedAngle))
            {
                angle = parsedAngle;
            }
            else
            {
                // Try to extract angle from text
                var match = System.Text.RegularExpressions.Regex.Match(angleStr, @"(\d+(?:\.\d+)?)\s*(?:degree|degrees|°)?");
                if (match.Success && float.TryParse(match.Groups[1].Value, out float extractedAngle))
                {
                    angle = extractedAngle;
                }
            }
        }
        else if (command.parameters.ContainsKey("amount"))
        {
            string amountStr = command.parameters["amount"];
            if (float.TryParse(amountStr, out float parsedAmount))
            {
                angle = parsedAmount;
            }
        }

        // If we have a rotation controller, use it with custom angle
        if (rotationController != null)
        {
            switch (direction.ToLower())
            {
                case "left":
                case "counterclockwise":
                    RotateWithCustomAngle(rotationController, "left", angle);
                    return true;
                case "right":
                case "clockwise":
                    RotateWithCustomAngle(rotationController, "right", angle);
                    return true;
                case "up":
                    RotateWithCustomAngle(rotationController, "up", angle);
                    return true;
                case "down":
                    RotateWithCustomAngle(rotationController, "down", angle);
                    return true;
                default:
                    RotateWithCustomAngle(rotationController, "left", angle);
                    return true;
            }
        }
        // Fallback to direct module rotation if no rotation controller
        else if (moduleManager != null)
        {
            var currentModule = moduleManager.GetCurrentModule();
            if (currentModule != null)
            {
                Vector3 rotationAxis = Vector3.zero;
                switch (direction.ToLower())
                {
                    case "left":
                    case "counterclockwise":
                        rotationAxis = Vector3.up;
                        angle = -angle; // Counter-clockwise
                        break;
                    case "right":
                    case "clockwise":
                        rotationAxis = Vector3.up;
                        break;
                    case "up":
                        rotationAxis = Vector3.right;
                        angle = -angle;
                        break;
                    case "down":
                        rotationAxis = Vector3.right;
                        break;
                    default:
                        rotationAxis = Vector3.up;
                        angle = -angle;
                        break;
                }

                currentModule.transform.Rotate(rotationAxis, angle, Space.World);
                BIMLog.Verbose($"[VoiceCommand] Rotated module {direction} by {angle} degrees");
                return true;
            }
        }

        return false;
    }

    private void RotateWithCustomAngle(ModuleRotationController controller, string direction, float angle)
    {
        // Since we can't modify the existing RotationController methods,
        // we'll calculate how many standard rotations we need
        float standardRotation = 90f; // Assuming standard rotation is 90 degrees
        int rotationCount = Mathf.RoundToInt(angle / standardRotation);

        if (rotationCount == 0) rotationCount = 1; // At least one rotation

        for (int i = 0; i < rotationCount; i++)
        {
            switch (direction.ToLower())
            {
                case "left":
                    controller.RotateLeft();
                    break;
                case "right":
                    controller.RotateRight();
                    break;
                case "up":
                    controller.RotateUp();
                    break;
                case "down":
                    controller.RotateDown();
                    break;
            }
        }

        BIMLog.Verbose($"[VoiceCommand] Executed {rotationCount} rotations {direction} (target: {angle}°)");
    }

    private bool ExecuteMoveCommand(VoiceCommandAction command)
    {
        if (moduleManager == null) return false;

        var currentModule = moduleManager.GetCurrentModule();
        if (currentModule == null) return false;

        string direction = command.parameters.ContainsKey("direction") ? command.parameters["direction"] : "";
        float amount = 1.0f; // Default movement amount

        if (command.parameters.ContainsKey("amount"))
        {
            string amountStr = command.parameters["amount"];
            if (float.TryParse(amountStr, out float parsedAmount))
            {
                amount = parsedAmount;
            }
            else
            {
                switch (amountStr.ToLower())
                {
                    case "small": amount = 0.5f; break;
                    case "medium": amount = 1.0f; break;
                    case "large": amount = 2.0f; break;
                }
            }
        }

        Vector3 moveVector = Vector3.zero;
        switch (direction.ToLower())
        {
            case "up":
                moveVector = Vector3.up * amount;
                break;
            case "down":
                moveVector = Vector3.down * amount;
                break;
            case "left":
                moveVector = Vector3.left * amount;
                break;
            case "right":
                moveVector = Vector3.right * amount;
                break;
            case "forward":
                moveVector = Vector3.forward * amount;
                break;
            case "backward":
            case "back":
                moveVector = Vector3.back * amount;
                break;
        }

        if (moveVector != Vector3.zero)
        {
            currentModule.transform.position += moveVector;
            return true;
        }

        return false;
    }

    private bool ExecuteScaleCommand(VoiceCommandAction command)
    {
        if (moduleManager == null) return false;

        var currentModule = moduleManager.GetCurrentModule();
        if (currentModule == null) return false;

        string value = command.parameters.ContainsKey("value") ? command.parameters["value"] : "";

        if (value.ToLower() == "reset")
        {
            currentModule.transform.localScale = Vector3.one;
            return true;
        }

        float scaleAmount = 1.1f; // Default scale factor

        if (float.TryParse(value, out float parsedScale))
        {
            currentModule.transform.localScale = Vector3.one * parsedScale;
            return true;
        }

        string direction = command.parameters.ContainsKey("direction") ? command.parameters["direction"] : "";
        if (direction.ToLower() == "up" || direction.ToLower() == "increase")
        {
            currentModule.transform.localScale *= scaleAmount;
            return true;
        }
        else if (direction.ToLower() == "down" || direction.ToLower() == "decrease")
        {
            currentModule.transform.localScale /= scaleAmount;
            return true;
        }

        return false;
    }

    private bool ExecuteSaveCommand(VoiceCommandAction command)
    {
        if (saveLoadManager == null) return false;

        if (command.targetType == "all")
        {
            return saveLoadManager.SaveAllModules("gpt_voice");
        }
        else
        {
            return saveLoadManager.SaveCurrentModulePosition("gpt_voice");
        }
    }

    private bool ExecuteTransparencyCommand(VoiceCommandAction command)
    {
        if (transparencyController == null) return false;

        string value = command.parameters.ContainsKey("value") ? command.parameters["value"] : "0.5";

        if (float.TryParse(value, out float transparency))
        {
            transparencyController.SetBuildingTransparency(transparency);
            return true;
        }

        // Default to 50% transparency if no value specified
        transparencyController.SetBuildingTransparency(0.5f);
        return true;
    }

    private bool ExecuteDocumentationCommand(VoiceCommandAction command)
    {
        if (documentManager != null)
        {
            documentManager.OpenDocumentation();
            return true;
        }

        BIMLog.Verbose("[GPT BRIDGE] Opening documentation (placeholder)");
        return true;
    }

    private bool ExecuteInspectionCommand(VoiceCommandAction command)
    {
        if (inspectionSystem != null)
        {
            inspectionSystem.StartInspection();
            return true;
        }

        BIMLog.Verbose("[GPT BRIDGE] Starting inspection (placeholder)");
        return true;
    }

    private void UpdateStatus(string message, Color color)
    {
        // Prefer reusing existing VoiceCommandController UI elements
        if (voiceController != null)
        {
            if (voiceController.voiceStatusText != null)
            {
                voiceController.voiceStatusText.text = message;
            }
            if (voiceController.lastCommandText != null)
            {
                voiceController.lastCommandText.text = "GPT: " + message;
            }
            if (voiceController.voiceStatusIndicator != null)
            {
                voiceController.voiceStatusIndicator.color = color;
            }
        }

        if (enableDebugLogging)
        {
            Debug.Log($"[GPT BRIDGE] Status: {message}");
        }
    }

    // Public API for manual testing
    public void ProcessTestCommand(string command)
    {
        if (testMode)
        {
            StartCoroutine(ProcessVoiceCommandWithGPT(command));
        }
    }

    public void SetAPIKey(string apiKey)
    {
        openAIApiKey = SanitizeKey(apiKey);
        if (enableDebugLogging)
        {
            string masked = MaskKey(openAIApiKey);
            Debug.Log($"[GPT BRIDGE] API key set (masked): {masked}");
        }
    }

    public void SetGPTEnabled(bool enabled)
    {
        gptEnabled = enabled;
        if (enableDebugLogging)
        {
            Debug.Log($"[GPT BRIDGE] GPT processing {(gptEnabled ? "ENABLED" : "DISABLED")}");
        }
    }

    public bool IsProcessing()
    {
        return isProcessing;
    }

    private void OnDestroy()
    {
        if (voiceController != null)
        {
            voiceController.OnTranscriptionReceived -= HandleVoiceInput;
        }

        currentRequest?.Dispose();
    }

    // --- API Key Utilities ---
    private string SanitizeKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;
        // Trim whitespace and common hidden characters
        string trimmed = key.Trim();
        // Remove BOM or zero-width chars
        trimmed = trimmed.Replace("\u200B", string.Empty)
                         .Replace("\u200C", string.Empty)
                         .Replace("\u200D", string.Empty)
                         .Replace("\uFEFF", string.Empty);
        return trimmed;
    }

    private bool IsValidAPIKey(string key)
    {
        return !string.IsNullOrEmpty(key) && key.StartsWith("sk-") && key.Length > 20;
    }

    private string MaskKey(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Length < 10) return "(not set)";
        return key.Substring(0, 6) + "..." + key.Substring(key.Length - 4);
    }

    // Context menu methods for testing
    [ContextMenu("Test: Hide Architecture")]
    private void TestHideArchitecture()
    {
        ProcessTestCommand("Hide architecture");
    }

    [ContextMenu("Test: Spawn Module M1-1")]
    private void TestSpawnModule()
    {
        ProcessTestCommand("Spawn module m1-1");
    }

    [ContextMenu("Test: Rotate Left")]
    private void TestRotateLeft()
    {
        ProcessTestCommand("Rotate left");
    }

    [ContextMenu("Test: Make Transparent")]
    private void TestTransparency()
    {
        ProcessTestCommand("Make it transparent");
    }
}
