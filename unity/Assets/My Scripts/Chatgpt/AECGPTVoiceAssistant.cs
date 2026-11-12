// AECGPTVoiceAssistant.cs - Enhanced GPT + Azure TTS for Mixed Reality AEC Applications
// Updated to provide flexible model mappings for OpenAI, Anthropic, and Google APIs
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;

namespace AEC.VoiceAssistant
{
    [System.Serializable]
    public enum AIProvider
    {
        [InspectorName("OpenAI (GPT)")] OpenAI,
        [InspectorName("Anthropic (Claude)")] Anthropic,
        [InspectorName("Google (Gemini)")] Google
    }

    [System.Serializable]
    public enum GPTModelType
    {
        [InspectorName("GPT-5 Mini")] GPT5Mini,
        [InspectorName("GPT-4o Mini (Recommended)")] GPT4oMini,
        [InspectorName("GPT-4o")] GPT4o,
        [InspectorName("GPT-4 Turbo")] GPT4Turbo,
        [InspectorName("GPT-4")] GPT4,
        [InspectorName("GPT-3.5 Turbo")] GPT35Turbo,
        [InspectorName("Custom (Set Model ID Below)")] Custom
    }

    [System.Serializable]
    public enum ClaudeModelType
    {
        [InspectorName("Claude Sonnet 4.5 (Latest)")] Claude45Sonnet,
        [InspectorName("Claude Sonnet 4.0")] Claude40Sonnet,
        [InspectorName("Claude Opus 4.1")] Claude41Opus,
        [InspectorName("Claude Haiku 4.5")] Claude45Haiku,
        [InspectorName("Custom (Set Model ID Below)")] Custom
    }

    [System.Serializable]
    public enum GeminiModelType
    {
        [InspectorName("Gemini 2.5 Pro (Recommended)")] Gemini25Pro,
        [InspectorName("Gemini 2.5 Flash")] Gemini25Flash,
        [InspectorName("Gemini 2.5 Flash Lite")] Gemini25FlashLite,
        [InspectorName("Custom (Set Model ID Below)")] Custom
    }


    public class AECGPTVoiceAssistant : MonoBehaviour, BIMMR.Inspection.IReportNarrativeService
    {
        [Serializable]
        private sealed class OpenAIModelConfiguration
        {
            [Tooltip("Chat Completions model ID for GPT-5 Mini.")]
            public string gpt5Mini = "gpt-5-mini";

            [Tooltip("Chat Completions model ID for GPT-4o Mini (default fallback).")]
            public string gpt4oMini = "gpt-4o-mini";

            [Tooltip("Chat Completions model ID for GPT-4o.")]
            public string gpt4o = "gpt-4o";

            [Tooltip("Chat Completions model ID for GPT-4 Turbo.")]
            public string gpt4Turbo = "gpt-4-turbo";

            [Tooltip("Chat Completions model ID for GPT-4.")]
            public string gpt4 = "gpt-4";

            [Tooltip("Chat Completions model ID for GPT-3.5 Turbo.")]
            public string gpt35Turbo = "gpt-3.5-turbo";

            [Tooltip("Set this when using Custom to adopt preview or unreleased models (e.g., gpt-4.1-mini, gpt-5).")]
            public string custom = string.Empty;
        }

        [Serializable]
        private sealed class ClaudeModelConfiguration
        {
            [Tooltip("Claude API model ID for Claude 4.5 Sonnet (Latest - Check /v1/models for exact version).")]
            public string claude45Sonnet = "claude-sonnet-4-5-20250929";

            [Tooltip("Claude API model ID for Claude 4.0 Sonnet.")]
            public string claude40Sonnet = "claude-4-opus-20250514";

            [Tooltip("Claude API model ID for Claude Opus 4.1 (High-end reasoning model).")]
            public string claude41Opus = "claude-opus-4-1-20250805";

            [Tooltip("Claude API model ID for Claude Haiku 4.5 (Fast / Low-latency model).")]
            public string claude45Haiku = "claude-haiku-4-5-20251001";

            [Tooltip("Set this when using Custom to adopt newer Claude models (check /v1/models for latest).")]
            public string custom = string.Empty;
        }

        [Serializable]
        private sealed class GeminiModelConfiguration
        {
            [Tooltip("Gemini model ID for Gemini 2.5 Pro (Recommended - Stable).")]
            public string gemini25Pro = "gemini-2.5-pro";

            [Tooltip("Gemini model ID for Gemini 2.5 Flash.")]
            public string gemini25Flash = "gemini-2.5-flash";

            [Tooltip("Gemini model ID for Gemini 2.5 Flash Lite (Fastest).")]
            public string gemini25FlashLite = "gemini-2.5-flash-lite";

            [Tooltip("Set this when using Custom to adopt newer Gemini models (e.g., models/gemini-3.0-pro).")]
            public string custom = string.Empty;
        }

        private const string DEFAULT_OPENAI_MODEL = "gpt-4o-mini";
        private const string DEFAULT_CLAUDE_MODEL = "claude-3-7-sonnet-20250219";
        private const string DEFAULT_GEMINI_MODEL = "gemini-2.5-pro";

        private readonly HashSet<string> modelWarningCache = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        [Header("AI Provider Selection")]
        [Tooltip("Select which AI provider to use for text processing. TTS/STT remains OpenAI Whisper/TTS.")]
        [SerializeField] private AIProvider selectedProvider = AIProvider.OpenAI;

        [System.NonSerialized] private string openAIApiKey = "";      // never serialized: see ApiKeyProvider / config.example.json
        [Header("OpenAI Configuration")]
        [SerializeField] private bool loadKeyFromEnvironment = false;
        [SerializeField] private string environmentVariableName = "OPENAI_API_KEY";
        [Space(5)]
        [Tooltip("Select the GPT model to use. GPT-4o Mini is recommended for fast, cost-effective responses.")]
        [SerializeField] private GPTModelType selectedGPTModel = GPTModelType.GPT4oMini;
        private OpenAIModelConfiguration openAIModelIds = new OpenAIModelConfiguration();

        [System.NonSerialized] private string anthropicApiKey = "";   // never serialized
        [Header("Anthropic Configuration")]
        [Tooltip("Select the Claude model to use. Claude Sonnet 4.5 is recommended for best quality.")]
        [SerializeField] private ClaudeModelType selectedClaudeModel = ClaudeModelType.Claude45Sonnet;
        private ClaudeModelConfiguration claudeModelIds = new ClaudeModelConfiguration();

        [System.NonSerialized] private string googleApiKey = "";      // never serialized
        [Header("Google Configuration")]
        [Tooltip("Select the Gemini model to use. Gemini 2.5 Pro is recommended for best quality.")]
        [SerializeField] private GeminiModelType selectedGeminiModel = GeminiModelType.Gemini25Pro;
        private GeminiModelConfiguration geminiModelIds = new GeminiModelConfiguration();

        [Header("Model Parameters")]
        [SerializeField] private int maxTokens = 220;
        [SerializeField] private float temperature = 0.6f;

        [Header("Study configuration (U5-U7)")]
        [Tooltip("U7: force near-deterministic decoding. Overrides the serialized temperature " +
                 "with studyTemperature (0 to 0.2) at start-up.")]
        [SerializeField] private bool enforceStudyDecoding = true;
        [SerializeField, Range(0f, 0.2f)] private float studyTemperature = 0.1f;
        [Tooltip("Token cap for report narratives (JSON mode).")]
        [SerializeField] private int narrativeMaxTokens = 400;
        [Tooltip("U7: use the revised system message (no industry-rate cost rule) instead of the " +
                 "serialized one. Appendix B reproduces RevisedSystemPrompt.")]
        [SerializeField] private bool useRevisedSystemPrompt = true;
        [Tooltip("U5: restrict the model digest to the active module.")]
        [SerializeField] private bool limitDigestToActiveModule = true;
        [Tooltip("U5: add the inspection state (current event, element, record counts).")]
        [SerializeField] private bool includeInspectionState = true;
        [Tooltip("U5: add the list of linked documents for the active project.")]
        [SerializeField] private bool includeDocuments = true;
        [SerializeField] private DocumentManager documentManager;
        [SerializeField] private BIMMR.Inspection.BIMInspectionManager inspectionManager;

        /// <summary>Version tag of the prompt/decoding configuration, written into every LLM log line.</summary>
        public const string PromptVersion = "mr-v2-2026-11";

        /// <summary>
        /// Revised system message (U7). Differences from the evaluated version: the cost rule
        /// that allowed industry rates is removed; answers must quote element ids exactly as
        /// given; values that are not in the context must be reported as missing.
        /// </summary>
        public const string RevisedSystemPrompt = @"You are an AEC engineer talking to a colleague through a Mixed Reality headset during a modular construction inspection. Your answers are spoken aloud.

HOW TO TALK:
- Lead with the answer in the first sentence; add at most one short supporting detail. 1 to 3 sentences, about 50 spoken words at most.
- No bullet points, headers, markdown or lists. No filler and no apologies.
- Say numbers and units so they can be read aloud (""42 square meters"").

WHERE THE FACTS COME FROM:
- Use ONLY the CURRENT MR SCENE CONTEXT below. Every element id, name, number and property you mention must appear there.
- When you refer to an element, quote its Element ID exactly as written in the context.
- If a property is missing or undefined, say that it is not recorded. Do not estimate it and do not use outside knowledge, typical values or market rates.
- Counts: sum the leaf elements listed for the relevant category. Areas and heights: read Area/Height values; do not derive values that are not listed.

SCOPE:
- Element not in the context: say it is not in the current scene.
- Questions outside the inspection (weather, general chat, prices): say it is outside what you can help with here.
- You assist the inspector; you do not decide whether an element passes inspection.";

        public const string NarrativeSystemPrompt = @"You draft the narrative field of a modular construction inspection record. Use ONLY the facts in the RECORD block. Copy the element id exactly. Do not add measurements, causes, costs or recommendations that are not in the record; if the notes are empty, say that no observation was recorded. Return JSON only: {""element_id"": ""<id from record>"", ""narrative"": ""<2-3 sentences>""}";

#if UNITY_EDITOR
        private void OnValidate()
        {
            openAIModelIds ??= new OpenAIModelConfiguration();
            claudeModelIds ??= new ClaudeModelConfiguration();
            geminiModelIds ??= new GeminiModelConfiguration();
            modelWarningCache.Clear();
        }
#endif

        [Header("API Retry Configuration")]
        [SerializeField] private bool enableRetryOnRateLimit = true;
        [SerializeField] private int maxRetryAttempts = 3;
        [SerializeField] private float baseRetryDelay = 1.0f;
        [SerializeField] private float maxRetryDelay = 8.0f;
        [SerializeField] private int requestTimeout = 20; // Reduced timeout for faster responses

        [Header("OpenAI Text-to-Speech (replacing Azure)")]
        [SerializeField] private bool enableTextToSpeech = true;
        [SerializeField] private string openAITTSModel = "tts-1"; // tts-1 = lowest latency; switch to "tts-1-hd" for richer voice
        [SerializeField] private string openAITTSVoiceEN = "nova"; // nova/shimmer sound most natural; alloy/verse/aria also available
        [SerializeField] private string openAITTSVoiceKR = "nova";
        [SerializeField] private float speechVolume = 0.85f;
        [SerializeField, Range(0.5f, 1.5f)] private float speechRate = 1.08f; // Slightly faster = more conversational
        [SerializeField, Range(0.8f, 1.2f)] private float speechPitch = 1.0f;
        [SerializeField] private bool persistentTTS = true;
        [SerializeField] private bool useFastTTSMode = true; // Enable optimized TTS processing
        [SerializeField] private bool preInitializeAudio = true; // Pre-initialize audio components

        [Header("Mixed Reality AEC System Prompt")]
        [TextArea(10, 20)]
        [SerializeField] private string mixedRealitySystemPrompt = @"You are a friendly, knowledgeable AEC engineer talking to a colleague through a Mixed Reality headset. You specialize in modular construction and BIM, and you ground every answer in the scene's BIM metadata (Structure, Architecture, MEP).

HOW TO TALK (this is voice — it will be spoken aloud):
- Sound like a real person, not a manual. Use contractions (it's, you're, there's), short sentences, and a warm tone.
- Lead with the answer in the first sentence. Then add one short supporting detail if useful. Stop.
- 1–3 sentences for most questions. Never longer than ~50 spoken words unless the user asks for detail.
- No bullet points, no headers, no markdown, no numbered lists — it's voice. Just flowing sentences.
- No filler like ""Certainly!"", ""Of course!"", ""I'd be happy to"". No apologies. Just answer.
- Numbers and units should be readable aloud (say ""42 square meters"", not ""42m²"").

WHERE THE FACTS COME FROM:
- The scene context below lists real BIM elements with their metadata. Use those numbers, names, and properties — never invent values.
- Categories: Structure (Columns, Beams, Connections, structural Stairs), Architecture (Studs, Walls, Floor, Ceiling, Roof, Windows, Doors, architectural Stairs, Furniture), MEP (Electric, Plumbing, Lighting, HVAC).
- For counts, sum the leaf elements under the relevant category. For areas/heights, read Area/Height metadata or compute from Length × Width / RiserCount × RiserHeight.
- If a property is missing or undefined, just say so naturally: ""I don't see a fire rating on that wall"" — don't recite a template phrase.

EDGE CASES (handle naturally, not robotically):
- Element not in scene → ""That one's not in the current scene.""
- Off-topic question → briefly redirect: ""That's outside what I can help with here — anything about the model I can look at?""
- For cost questions, use metadata quantities first, then reasonable industry rates. Show the math in one short sentence (e.g., ""42 square meters of ACP at about 65 dollars each works out to roughly 2,730 dollars"").

EXAMPLES (notice the voice):
Q: How many columns are in this module?
A: There are 18 columns in this module, all steel HSS sections.

Q: What's the wall material here?
A: It's gypsum board over steel studs — Wall Type W12.

Q: Any doors missing QC status?
A: Yeah, three doors don't have a QC status set — D-104, D-207, and D-301.

Q: What's the weather like?
A: That's outside what I can help with here. Want me to check anything on the model?

Keep it human. Keep it short. Keep it grounded in the metadata.";

        [Header("MR Scene Analysis")]
        [SerializeField] private ModuleManager moduleManager;
        [SerializeField] private ArchiBIM.MR.BIMMetadataViewer metadataViewer;
        [SerializeField] private ArchiBIM.QualityControl.QualityInspectionSystem inspectionSystem;
        [SerializeField] private bool includeDetailedSceneContext = true;
        [SerializeField] private bool includeBIMMetadata = true;
        [SerializeField] private bool includeComponentStates = true;
        [SerializeField] private bool includeSelectedElement = true;

        [Header("Audio")]
        [SerializeField] private AudioSource audioSource;

        [Header("Response Settings")]
        [SerializeField] private bool enableShortResponseMode = true;
        [SerializeField] private int maxResponseLength = 320;

        [Header("Conversation Logging")]
        [SerializeField] private bool enableConversationLogging = true;
        [SerializeField] private bool saveOnExit = true;
        [SerializeField] private string logDirectory = "ConversationLogs";
        [SerializeField] private bool includeTimestamps = true;
        [SerializeField] private bool includeModelInfo = true;

        public event Action<string> OnUserMessageSent;
        public event Action<string> OnAssistantMessageReceived;
        public event Action OnTTSStopped;

        private bool isProcessingRequest = false;
        private bool isSpeaking = false;
        private string lastResponse = "";
        private VoiceGPTBridge voiceBridge;
        private Coroutine currentTTSCoroutine = null;
        private string detectedUserLanguage = "en";

        // Fast TTS optimization variables
        private bool isAudioSourceReady = false;
        private System.Collections.Generic.Queue<byte[]> audioQueue = new System.Collections.Generic.Queue<byte[]>();

        // Conversation logging variables
        private List<ConversationEntry> conversationHistory = new List<ConversationEntry>();
        private System.DateTime sessionStartTime;
        private string currentSessionId;

        public string lastGPTResponse => lastResponse;

        private const string GPT_API_URL = "https://api.openai.com/v1/chat/completions";
        private const string OPENAI_TTS_URL = "https://api.openai.com/v1/audio/speech";
        private const string CLAUDE_API_URL = "https://api.anthropic.com/v1/messages";
        private const string GEMINI_API_URL = "https://generativelanguage.googleapis.com/v1beta/models/";

        #region Unity Lifecycle

        private void Start()
        {
            if (enforceStudyDecoding && temperature != studyTemperature)
            {
                Debug.Log($"[AEC GPT] Study decoding: temperature {temperature} -> {studyTemperature} ({PromptVersion}).");
                temperature = studyTemperature;
            }
            InitializeComponents();
            InitializeAzureTTS();
            ValidateAPIConfiguration();
            InitializeConversationLogging();
        }

        #endregion

        #region Initialization

        private void InitializeComponents()
        {
            // API keys come from environment variables or the untracked config.json (ApiKeyProvider)
            if (string.IsNullOrEmpty(openAIApiKey)) openAIApiKey = ApiKeyProvider.OpenAI;
            if (string.IsNullOrEmpty(anthropicApiKey)) anthropicApiKey = ApiKeyProvider.Anthropic;
            if (string.IsNullOrEmpty(googleApiKey)) googleApiKey = ApiKeyProvider.Google;
            if (loadKeyFromEnvironment && string.IsNullOrEmpty(openAIApiKey))
            {
                string envKey = System.Environment.GetEnvironmentVariable(environmentVariableName);
                if (!string.IsNullOrEmpty(envKey))
                {
                    openAIApiKey = envKey;
                    BIMLog.Verbose("[AEC GPT] API key loaded from environment variable");
                }
                else
                {
                    Debug.LogWarning($"[AEC GPT] Environment variable '{environmentVariableName}' not found or empty");
                }
            }

            voiceBridge = FindFirstObjectByType<VoiceGPTBridge>();
            if (voiceBridge != null)
            {
                voiceBridge.OnVoiceInputReceived += HandleVoiceInput;
                voiceBridge.OnChatGPTVoiceReceived += HandleChatGPTVoiceInput;
                voiceBridge.OnTTSStopRequested += StopTTS;
            }

            if (moduleManager == null)
                moduleManager = FindAnyObjectByType<ModuleManager>();

            if (metadataViewer == null)
                metadataViewer = FindFirstObjectByType<ArchiBIM.MR.BIMMetadataViewer>();

            if (inspectionSystem == null)
                inspectionSystem = FindFirstObjectByType<ArchiBIM.QualityControl.QualityInspectionSystem>();

            if (inspectionManager == null)
                inspectionManager = FindAnyObjectByType<BIMMR.Inspection.BIMInspectionManager>();

            if (documentManager == null)
                documentManager = FindAnyObjectByType<DocumentManager>();

            if (audioSource == null)
            {
                audioSource = gameObject.GetComponent<AudioSource>();
                if (audioSource == null)
                    audioSource = gameObject.AddComponent<AudioSource>();
            }

            // Pre-initialize audio source for faster TTS startup
            if (preInitializeAudio)
            {
                PreInitializeAudioSource();
            }
        }

        private void PreInitializeAudioSource()
        {
            if (audioSource == null) return;

            // Configure audio source for optimal performance
            audioSource.volume = speechVolume;
            audioSource.pitch = speechPitch;
            audioSource.spatialBlend = 0f; // 2D audio for UI
            audioSource.rolloffMode = AudioRolloffMode.Linear;
            audioSource.playOnAwake = false;
            audioSource.loop = false;

            // Mark as ready
            isAudioSourceReady = true;

            BIMLog.Verbose("[AEC GPT Assistant] Audio source pre-initialized for fast TTS");
        }

        private void InitializeAzureTTS() // repurposed: keep name to avoid breaking other code
        {
            // Now just log initialization; OpenAI API key reuse with chat key.
            if (persistentTTS)
                enableTextToSpeech = true;
            string tokenField = UsesCompletionTokenField() ? "max_completion_tokens" : "max_tokens";
            BIMLog.Verbose($"[AEC GPT Assistant] (OpenAI TTS enabled) Using model: {GetModelString()} ({selectedGPTModel}) with {tokenField}");
        }

        private void ValidateAPIConfiguration()
        {
            // Sanitize all API keys
            openAIApiKey = SanitizeKey(openAIApiKey);
            anthropicApiKey = SanitizeKey(anthropicApiKey);
            googleApiKey = SanitizeKey(googleApiKey);

            // Validate based on selected provider
            switch (selectedProvider)
            {
                case AIProvider.OpenAI:
                    ValidateOpenAIKey();
                    break;
                case AIProvider.Anthropic:
                    ValidateAnthropicKey();
                    break;
                case AIProvider.Google:
                    ValidateGoogleKey();
                    break;
            }
        }

        private void ValidateOpenAIKey()
        {
            if (!IsValidAPIKey(openAIApiKey, "sk-"))
            {
                if (string.IsNullOrEmpty(openAIApiKey))
                {
                    Debug.LogError("[AEC GPT] OpenAI API key is empty! Please set your API key in the Inspector.");
                    Debug.LogError("[AEC GPT] Get your API key from: https://platform.openai.com/api-keys");
                }
                else
                {
                    Debug.LogError($"[AEC GPT] Invalid OpenAI API key format. Expected format: sk-... (length > 20)");
                    Debug.LogError($"[AEC GPT] Current key starts with: {openAIApiKey.Substring(0, Mathf.Min(10, openAIApiKey.Length))}...");
                }
                return;
            }
            BIMLog.Verbose($"[AEC GPT] ✅ OpenAI API key valid | Model: {GetModelString()}");
        }

        private void ValidateAnthropicKey()
        {
            if (!IsValidAPIKey(anthropicApiKey, "sk-ant-"))
            {
                if (string.IsNullOrEmpty(anthropicApiKey))
                {
                    Debug.LogError("[AEC GPT] Anthropic API key is empty! Please set your API key in the Inspector.");
                    Debug.LogError("[AEC GPT] Get your API key from: https://console.anthropic.com/");
                }
                else
                {
                    Debug.LogError($"[AEC GPT] Invalid Anthropic API key format. Expected format: sk-ant-... (length > 20)");
                }
                return;
            }
            BIMLog.Verbose($"[AEC GPT] ✅ Anthropic API key valid | Model: {GetClaudeModelString()}");
        }

        private void ValidateGoogleKey()
        {
            if (string.IsNullOrEmpty(googleApiKey) || googleApiKey.Length < 20)
            {
                if (string.IsNullOrEmpty(googleApiKey))
                {
                    Debug.LogError("[AEC GPT] Google API key is empty! Please set your API key in the Inspector.");
                    Debug.LogError("[AEC GPT] Get your API key from: https://makersuite.google.com/app/apikey");
                }
                else
                {
                    Debug.LogError($"[AEC GPT] Invalid Google API key format (length < 20)");
                }
                return;
            }
            BIMLog.Verbose($"[AEC GPT] ✅ Google API key valid | Model: {GetGeminiModelString()}");
        }

        private string GetConfiguredModelId(string configuredValue, string fallbackValue, string providerName, string optionLabel)
        {
            string sanitizedConfigured = NormalizeModelId(configuredValue);
            if (!string.IsNullOrEmpty(sanitizedConfigured))
            {
                return sanitizedConfigured;
            }

            string sanitizedFallback = NormalizeModelId(fallbackValue);
            string cacheKey = $"{providerName}:{optionLabel}";
            if (modelWarningCache.Add(cacheKey))
            {
                Debug.LogWarning($"[AEC GPT] {providerName} model '{optionLabel}' has no configured ID. Falling back to '{sanitizedFallback}'. Update the model ID in the inspector if you intend to use a different release.");
            }

            return sanitizedFallback;
        }

        private string NormalizeModelId(string modelId)
        {
            string model = SanitizeKey(modelId);

            // Claude API requires exact dated model IDs (e.g., claude-3-7-sonnet-20250219)
            // Do NOT replace with -latest suffixes as they cause 404 errors

            // Strip -latest and -exp suffixes from Gemini models to ensure stable IDs
            if (model.Contains("gemini"))
            {
                model = model
                    .Replace("-latest", "")
                    .Replace("-exp", "");
            }

            return model;
        }

        private string GetModelString()
        {
            return selectedGPTModel switch
            {
                GPTModelType.GPT5Mini => GetConfiguredModelId(openAIModelIds.gpt5Mini, DEFAULT_OPENAI_MODEL, "OpenAI", "GPT-5 Mini"),
                GPTModelType.GPT4oMini => GetConfiguredModelId(openAIModelIds.gpt4oMini, DEFAULT_OPENAI_MODEL, "OpenAI", "GPT-4o Mini"),
                GPTModelType.GPT4o => GetConfiguredModelId(openAIModelIds.gpt4o, DEFAULT_OPENAI_MODEL, "OpenAI", "GPT-4o"),
                GPTModelType.GPT4Turbo => GetConfiguredModelId(openAIModelIds.gpt4Turbo, DEFAULT_OPENAI_MODEL, "OpenAI", "GPT-4 Turbo"),
                GPTModelType.GPT4 => GetConfiguredModelId(openAIModelIds.gpt4, DEFAULT_OPENAI_MODEL, "OpenAI", "GPT-4"),
                GPTModelType.GPT35Turbo => GetConfiguredModelId(openAIModelIds.gpt35Turbo, DEFAULT_OPENAI_MODEL, "OpenAI", "GPT-3.5 Turbo"),
                GPTModelType.Custom => GetConfiguredModelId(openAIModelIds.custom, DEFAULT_OPENAI_MODEL, "OpenAI", "Custom"),
                _ => NormalizeModelId(DEFAULT_OPENAI_MODEL)
            };
        }

        private string GetClaudeModelString()
        {
            return selectedClaudeModel switch
            {
                ClaudeModelType.Claude45Sonnet => GetConfiguredModelId(claudeModelIds.claude45Sonnet, DEFAULT_CLAUDE_MODEL, "Claude", "Claude Sonnet 4.5"),
                ClaudeModelType.Claude40Sonnet => GetConfiguredModelId(claudeModelIds.claude40Sonnet, DEFAULT_CLAUDE_MODEL, "Claude", "Claude Sonnet 4.0"),
                ClaudeModelType.Claude41Opus => GetConfiguredModelId(claudeModelIds.claude41Opus, DEFAULT_CLAUDE_MODEL, "Claude", "Claude Opus 4.1"),
                ClaudeModelType.Claude45Haiku => GetConfiguredModelId(claudeModelIds.claude45Haiku, DEFAULT_CLAUDE_MODEL, "Claude", "Claude Haiku 4.5"),
                ClaudeModelType.Custom => GetConfiguredModelId(claudeModelIds.custom, DEFAULT_CLAUDE_MODEL, "Claude", "Custom"),
                _ => NormalizeModelId(DEFAULT_CLAUDE_MODEL)
            };
        }

        private string GetGeminiModelString()
        {
            return selectedGeminiModel switch
            {
                GeminiModelType.Gemini25Pro => GetConfiguredModelId(geminiModelIds.gemini25Pro, DEFAULT_GEMINI_MODEL, "Gemini", "Gemini 2.5 Pro"),
                GeminiModelType.Gemini25Flash => GetConfiguredModelId(geminiModelIds.gemini25Flash, DEFAULT_GEMINI_MODEL, "Gemini", "Gemini 2.5 Flash"),
                GeminiModelType.Gemini25FlashLite => GetConfiguredModelId(geminiModelIds.gemini25FlashLite, DEFAULT_GEMINI_MODEL, "Gemini", "Gemini 2.5 Flash Lite"),
                GeminiModelType.Custom => GetConfiguredModelId(geminiModelIds.custom, DEFAULT_GEMINI_MODEL, "Gemini", "Custom"),
                _ => NormalizeModelId(DEFAULT_GEMINI_MODEL)
            };
        }

        private bool UsesCompletionTokenField()
        {
            string modelId = GetModelString();
            if (string.IsNullOrEmpty(modelId))
            {
                return false;
            }

            string normalized = modelId.ToLowerInvariant();
            return normalized.StartsWith("gpt-4o") || normalized.StartsWith("gpt-4.1") || normalized.StartsWith("gpt-5");
        }

        private bool IsValidAPIKey(string key, string prefix = "sk-")
        {
            return !string.IsNullOrEmpty(key) && key.StartsWith(prefix) && key.Length > 20;
        }

        private string SanitizeKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            string trimmed = key.Trim();
            trimmed = trimmed.Replace("\u200B", string.Empty)
                             .Replace("\u200C", string.Empty)
                             .Replace("\u200D", string.Empty)
                             .Replace("\uFEFF", string.Empty);
            return trimmed;
        }

        private string MaskKey(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Length < 10) return "(not set)";
            return key.Substring(0, 6) + "..." + key.Substring(key.Length - 4);
        }

        #endregion

        #region Public Methods

        public new void SendMessage(string message) => SendChatMessage(message);

        public void SendChatMessage(string message)
        {
            if (isProcessingRequest)
            {
                Debug.LogWarning("[AEC GPT] Cannot send message - already processing a request");
                return;
            }

            if (string.IsNullOrEmpty(message.Trim()))
            {
                Debug.LogWarning("[AEC GPT] Cannot send empty message");
                return;
            }

            StopTTS();

            // Log user message
            LogUserMessage(message);

            BIMLog.Verbose($"[AEC GPT] User message received: {message.Substring(0, Math.Min(100, message.Length))}...");
            OnUserMessageSent?.Invoke(message);
            StartCoroutine(ProcessGPTRequest(message));
        }

        public void SetTTSEnabled(bool enabled)
        {
            enableTextToSpeech = enabled;
            if (!enabled)
                StopTTS();
        }

        public void SetSpeechVolume(float volume)
        {
            speechVolume = Mathf.Clamp(volume, 0.1f, 1.0f);
            if (audioSource != null)
                audioSource.volume = speechVolume;
        }

        public void SetSpeechRate(float rate) => speechRate = Mathf.Clamp(rate, 0.6f, 1.4f);
        public void SetSpeechPitch(float pitch) => speechPitch = Mathf.Clamp(pitch, 0.8f, 1.2f);

        // Model selection methods
        public void SetAIProvider(AIProvider provider)
        {
            selectedProvider = provider;
            ValidateAPIConfiguration();
        }
        public void SetGPTModel(GPTModelType model) => selectedGPTModel = model;
        public void SetClaudeModel(ClaudeModelType model) => selectedClaudeModel = model;
        public void SetGeminiModel(GeminiModelType model) => selectedGeminiModel = model;

        // Get current configuration
        public AIProvider GetCurrentProvider() => selectedProvider;
        public GPTModelType GetCurrentGPTModel() => selectedGPTModel;
        public ClaudeModelType GetCurrentClaudeModel() => selectedClaudeModel;
        public GeminiModelType GetCurrentGeminiModel() => selectedGeminiModel;
        public string GetCurrentModelString()
        {
            return selectedProvider switch
            {
                AIProvider.OpenAI => GetModelString(),
                AIProvider.Anthropic => GetClaudeModelString(),
                AIProvider.Google => GetGeminiModelString(),
                _ => GetModelString()
            };
        }

        // Retry configuration methods
        public void SetRetryEnabled(bool enabled) => enableRetryOnRateLimit = enabled;
        public void SetMaxRetryAttempts(int attempts) => maxRetryAttempts = Mathf.Clamp(attempts, 0, 10);
        public void SetRetryDelay(float baseDelay, float maxDelay)
        {
            baseRetryDelay = Mathf.Clamp(baseDelay, 0.5f, 5.0f);
            maxRetryDelay = Mathf.Clamp(maxDelay, 2.0f, 30.0f);
        }
        public void SetRequestTimeout(int timeout) => requestTimeout = Mathf.Clamp(timeout, 10, 120);

        public bool IsReady()
        {
            return selectedProvider switch
            {
                AIProvider.OpenAI => IsValidAPIKey(openAIApiKey, "sk-"),
                AIProvider.Anthropic => IsValidAPIKey(anthropicApiKey, "sk-ant-"),
                AIProvider.Google => !string.IsNullOrEmpty(googleApiKey) && googleApiKey.Length > 20,
                _ => false
            };
        }

        public bool IsProcessing() => isProcessingRequest || isSpeaking;

        /// <summary>
        /// Sends a message for batch evaluation mode - bypasses TTS for faster processing
        /// </summary>
        public IEnumerator SendBatchEvaluationMessage(string message, System.Action<string> onComplete)
        {
            BIMLog.Verbose($"[AEC GPT Batch] Sending question: {message.Substring(0, Math.Min(60, message.Length))}...");

            // Store original TTS state
            bool originalTTSState = enableTextToSpeech;
            bool originalPersistentTTS = persistentTTS;

            // Disable TTS completely for batch mode
            enableTextToSpeech = false;
            persistentTTS = false;

            // Wait for any pending processing to complete
            int waitCount = 0;
            while (isProcessingRequest && waitCount < 100)
            {
                yield return new WaitForSeconds(0.1f);
                waitCount++;
            }

            if (isProcessingRequest)
            {
                Debug.LogWarning("[AEC GPT Batch] Previous request still processing, forcing clear");
                isProcessingRequest = false;
            }

            // Clear last response
            lastResponse = null;

            // Validate API is ready
            if (!IsReady())
            {
                Debug.LogError($"[AEC GPT Batch] API not ready for provider: {selectedProvider}");
                onComplete?.Invoke("[API_NOT_READY]");
                enableTextToSpeech = originalTTSState;
                persistentTTS = originalPersistentTTS;
                yield break;
            }

            // Send the message and wait for response
            BIMLog.Verbose($"[AEC GPT Batch] Sending to {selectedProvider} with model: {GetCurrentModelString()}");

            string batchResponse = null;
            bool batchComplete = false;

            // Create a callback that captures the response
            System.Action<string> batchCallback = (response) =>
            {
                batchResponse = response;
                batchComplete = true;
                BIMLog.Verbose($"[AEC GPT Batch] Callback received: {(response?.Length ?? 0)} chars");
            };

            // Process the request directly using the provider-specific method
            yield return StartCoroutine(ProcessBatchRequest(message, batchCallback));

            // Wait for batch callback to complete
            float timeout = 60f;
            float elapsed = 0f;

            while (!batchComplete && elapsed < timeout)
            {
                yield return new WaitForSeconds(0.1f);
                elapsed += 0.1f;
            }

            // Check if we timed out
            if (elapsed >= timeout)
            {
                Debug.LogError($"[AEC GPT Batch] Request timed out after {timeout}s");
                batchResponse = "[TIMEOUT]";
            }

            // Get the response
            string response = batchResponse;

            if (string.IsNullOrEmpty(response))
            {
                Debug.LogWarning("[AEC GPT Batch] Empty response received");
                response = "[EMPTY_RESPONSE]";
            }
            else
            {
                BIMLog.Verbose($"[AEC GPT Batch] Response received ({response.Length} chars): {response.Substring(0, Math.Min(80, response.Length))}...");
            }

            // Restore TTS state
            enableTextToSpeech = originalTTSState;
            persistentTTS = originalPersistentTTS;

            // Callback with response
            onComplete?.Invoke(response);
        }

        public string GetAPIKeyStatus()
        {
            switch (selectedProvider)
            {
                case AIProvider.OpenAI:
                    if (string.IsNullOrEmpty(openAIApiKey))
                        return "OpenAI API key not set - Please add your API key";
                    if (!IsValidAPIKey(openAIApiKey, "sk-"))
                        return "Invalid OpenAI API key format - Should start with 'sk-'";
                    return $"OpenAI API key valid (length: {openAIApiKey.Length})";

                case AIProvider.Anthropic:
                    if (string.IsNullOrEmpty(anthropicApiKey))
                        return "Anthropic API key not set - Please add your API key";
                    if (!IsValidAPIKey(anthropicApiKey, "sk-ant-"))
                        return "Invalid Anthropic API key format - Should start with 'sk-ant-'";
                    return $"Anthropic API key valid (length: {anthropicApiKey.Length})";

                case AIProvider.Google:
                    if (string.IsNullOrEmpty(googleApiKey))
                        return "Google API key not set - Please add your API key";
                    if (googleApiKey.Length < 20)
                        return "Invalid Google API key format (too short)";
                    return $"Google API key valid (length: {googleApiKey.Length})";

                default:
                    return "Unknown provider";
            }
        }

        public void SetAPIKey(string apiKey)
        {
            switch (selectedProvider)
            {
                case AIProvider.OpenAI:
                    openAIApiKey = SanitizeKey(apiKey);
                    BIMLog.Verbose($"[AEC GPT] OpenAI API key set (masked): {MaskKey(openAIApiKey)}");
                    break;
                case AIProvider.Anthropic:
                    anthropicApiKey = SanitizeKey(apiKey);
                    BIMLog.Verbose($"[AEC GPT] Anthropic API key set (masked): {MaskKey(anthropicApiKey)}");
                    break;
                case AIProvider.Google:
                    googleApiKey = SanitizeKey(apiKey);
                    BIMLog.Verbose($"[AEC GPT] Google API key set (masked): {MaskKey(googleApiKey)}");
                    break;
            }
            ValidateAPIConfiguration();
        }

        public void SetOpenAIKey(string apiKey)
        {
            openAIApiKey = SanitizeKey(apiKey);
            BIMLog.Verbose($"[AEC GPT] OpenAI API key set (masked): {MaskKey(openAIApiKey)}");
            if (selectedProvider == AIProvider.OpenAI)
                ValidateAPIConfiguration();
        }

        public void SetAnthropicKey(string apiKey)
        {
            anthropicApiKey = SanitizeKey(apiKey);
            BIMLog.Verbose($"[AEC GPT] Anthropic API key set (masked): {MaskKey(anthropicApiKey)}");
            if (selectedProvider == AIProvider.Anthropic)
                ValidateAPIConfiguration();
        }

        public void SetGoogleKey(string apiKey)
        {
            googleApiKey = SanitizeKey(apiKey);
            BIMLog.Verbose($"[AEC GPT] Google API key set (masked): {MaskKey(googleApiKey)}");
            if (selectedProvider == AIProvider.Google)
                ValidateAPIConfiguration();
        }

        public void StopTTS()
        {
            if (currentTTSCoroutine != null)
            {
                StopCoroutine(currentTTSCoroutine);
                currentTTSCoroutine = null;
            }

            if (audioSource != null && audioSource.isPlaying)
            {
                audioSource.Stop();
                audioSource.clip = null; // Release audio clip memory
            }

            isSpeaking = false;
            audioQueue.Clear(); // Clear any queued audio
            OnTTSStopped?.Invoke();

            BIMLog.Verbose("[AEC GPT TTS] TTS stopped and cleaned up");
        }

        #endregion

        #region Voice Integration

        private void HandleVoiceInput(string transcription, float confidence)
        {
            if (!string.IsNullOrEmpty(transcription) && !isProcessingRequest)
            {
                StopTTS();
                detectedUserLanguage = LanguageDetector.DetectLanguage(transcription);
                SendChatMessage(transcription);
            }
        }

        private void HandleChatGPTVoiceInput(string transcription, float confidence)
        {
            HandleVoiceInput(transcription, confidence);
        }

        #endregion

        #region GPT Processing

        /// <summary>
        /// Process a batch request without modifying conversation history or triggering TTS
        /// </summary>
        private IEnumerator ProcessBatchRequest(string userMessage, System.Action<string> onComplete)
        {
            // Validate the appropriate API key based on selected provider
            bool isKeyValid = false;
            string providerName = "";

            switch (selectedProvider)
            {
                case AIProvider.OpenAI:
                    isKeyValid = IsValidAPIKey(openAIApiKey, "sk-");
                    providerName = "OpenAI";
                    break;
                case AIProvider.Anthropic:
                    isKeyValid = IsValidAPIKey(anthropicApiKey, "sk-ant-");
                    providerName = "Anthropic";
                    break;
                case AIProvider.Google:
                    isKeyValid = !string.IsNullOrEmpty(googleApiKey) && googleApiKey.Length > 20;
                    providerName = "Google";
                    break;
            }

            if (!isKeyValid)
            {
                string errorMsg = $"Error: {providerName} API key is not set or invalid.";
                Debug.LogError($"[AEC GPT Batch] {errorMsg}");
                onComplete?.Invoke(errorMsg);
                yield break;
            }

            // Create system prompt and finalize user message
            string systemPrompt = CreateMRSystemPromptWithContext();
            string languageInstruction = detectedUserLanguage == "ko"
                ? "\n\nIMPORTANT: Respond in Korean."
                : "\n\nIMPORTANT: Respond in English.";

            string finalUserMessage = userMessage + languageInstruction;

            if (enableShortResponseMode)
                finalUserMessage += "\n\nAnswer in 1–3 spoken sentences. End on a complete thought.";

            // U4/U14: log the benchmark call (30-query rerun) like a live question.
            BeginLlmCall("benchmark", ref systemPrompt, ref finalUserMessage, maxTokens, false, "", "");
            var userCallback = onComplete;
            onComplete = response => userCallback?.Invoke(FinishLlmCall(response));

            // Verify BIM context is included (log hash for consistency check across providers)
            int contextHash = systemPrompt.GetHashCode();
            bool hasBIMData = systemPrompt.Contains("BIM Model Hierarchy") && systemPrompt.Contains("Total Individual BIM Components");
            BIMLog.Verbose($"[AEC GPT Batch] {selectedProvider} receives context: {systemPrompt.Length} chars, Hash: {contextHash}, BIM Data: {hasBIMData}");

            if (!hasBIMData)
            {
                Debug.LogWarning($"[AEC GPT Batch] WARNING: {selectedProvider} context missing BIM hierarchy data! Check includeBIMMetadata setting.");
            }

            // Route to appropriate AI provider
            switch (selectedProvider)
            {
                case AIProvider.OpenAI:
                    var gptRequest = new GPTRequest
                    {
                        model = GetModelString(),
                        messages = new List<GPTMessage>
                        {
                            new GPTMessage { role = "system", content = systemPrompt },
                            new GPTMessage { role = "user", content = finalUserMessage }
                        },
                        temperature = temperature
                    };

                    if (UsesCompletionTokenField())
                        gptRequest.max_completion_tokens = maxTokens;
                    else
                        gptRequest.max_tokens = maxTokens;

                    BIMLog.Verbose($"[AEC GPT Batch] Sending to OpenAI with {gptRequest.messages.Count} messages (system + user)");
                    yield return StartCoroutine(SendGPTRequest(gptRequest, onComplete));
                    break;

                case AIProvider.Anthropic:
                    var claudeRequest = new ClaudeRequest
                    {
                        model = GetClaudeModelString(),
                        system = systemPrompt,
                        messages = new List<ClaudeMessage>
                        {
                            new ClaudeMessage { role = "user", content = finalUserMessage }
                        },
                        max_tokens = maxTokens,
                        temperature = temperature
                    };

                    BIMLog.Verbose($"[AEC GPT Batch] Sending to Anthropic with system context ({claudeRequest.system.Length} chars)");
                    yield return StartCoroutine(SendClaudeRequest(claudeRequest, onComplete));
                    break;

                case AIProvider.Google:
                    var geminiRequest = new GeminiRequest
                    {
                        systemInstruction = new GeminiSystemInstruction
                        {
                            parts = new List<GeminiPart>
                            {
                                new GeminiPart { text = systemPrompt }
                            }
                        },
                        contents = new List<GeminiContent>
                        {
                            new GeminiContent
                            {
                                parts = new List<GeminiPart>
                                {
                                    new GeminiPart { text = finalUserMessage }
                                }
                            }
                        },
                        generationConfig = new GeminiGenerationConfig
                        {
                            temperature = temperature,
                            maxOutputTokens = maxTokens
                        }
                    };

                    BIMLog.Verbose($"[AEC GPT Batch] Sending to Google with systemInstruction ({geminiRequest.systemInstruction.parts[0].text.Length} chars)");
                    yield return StartCoroutine(SendGeminiRequest(geminiRequest, onComplete));
                    break;
            }
        }

        private IEnumerator ProcessGPTRequest(string userMessage)
        {
            isProcessingRequest = true;

            // Validate the appropriate API key based on selected provider
            bool isKeyValid = false;
            string providerName = "";

            switch (selectedProvider)
            {
                case AIProvider.OpenAI:
                    isKeyValid = IsValidAPIKey(openAIApiKey, "sk-");
                    providerName = "OpenAI";
                    break;
                case AIProvider.Anthropic:
                    isKeyValid = IsValidAPIKey(anthropicApiKey, "sk-ant-");
                    providerName = "Anthropic";
                    break;
                case AIProvider.Google:
                    isKeyValid = !string.IsNullOrEmpty(googleApiKey) && googleApiKey.Length > 20;
                    providerName = "Google";
                    break;
            }

            if (!isKeyValid)
            {
                string errorMsg = $"Error: {providerName} API key is not set or invalid. Please add your API key in the Inspector panel.";
                OnAssistantMessageReceived?.Invoke(errorMsg);
                Debug.LogError($"[AEC GPT] {errorMsg}");
                isProcessingRequest = false;
                yield break;
            }

            if (persistentTTS && IsValidAPIKey(openAIApiKey, "sk-"))
                enableTextToSpeech = true;

            // Optimize for cost-related queries
            bool isCostQuery = userMessage.ToLower().Contains("cost") ||
                              userMessage.ToLower().Contains("price") ||
                              userMessage.ToLower().Contains("budget") ||
                              userMessage.ToLower().Contains("estimate") ||
                              userMessage.ToLower().Contains("expensive");

            string systemPrompt = CreateMRSystemPromptWithContext();
            string languageInstruction = detectedUserLanguage == "ko"
                ? "\n\nIMPORTANT: Respond in Korean."
                : "\n\nIMPORTANT: Respond in English.";

            string finalUserMessage = userMessage + languageInstruction;

            // For cost queries, nudge toward concrete numbers but stay conversational.
            // Not used with the revised prompt (U7): industry rates are outside the BIM context.
            if (isCostQuery && !useRevisedSystemPrompt)
            {
                finalUserMessage += "\n\nGive a quick cost estimate. Use metadata quantities first, fall back to standard industry rates. Show the math in one short sentence.";
            }

            if (enableShortResponseMode)
                finalUserMessage += "\n\nAnswer in 1–3 spoken sentences. End on a complete thought.";

            // U4/U14: open the request log record; redact names in restricted-data mode.
            BeginLlmCall("question", ref systemPrompt, ref finalUserMessage, maxTokens, false);

            // Verify all providers receive identical BIM context
            int contextHash = systemPrompt.GetHashCode();
            bool hasBIMData = systemPrompt.Contains("BIM Model Hierarchy") && systemPrompt.Contains("Total Individual BIM Components");
            BIMLog.Verbose($"[AEC GPT] {selectedProvider} ({GetCurrentModelString()}) receives context: {systemPrompt.Length} chars, Hash: {contextHash}, BIM Data: {hasBIMData}");

            if (!hasBIMData && includeBIMMetadata)
            {
                Debug.LogWarning($"[AEC GPT] WARNING: {selectedProvider} context missing BIM hierarchy data despite includeBIMMetadata=true! Check metadataViewer.");
            }

            // Route to appropriate AI provider
            switch (selectedProvider)
            {
                case AIProvider.OpenAI:
                    yield return StartCoroutine(ProcessOpenAIRequest(systemPrompt, finalUserMessage, isCostQuery));
                    break;
                case AIProvider.Anthropic:
                    yield return StartCoroutine(ProcessClaudeRequest(systemPrompt, finalUserMessage, isCostQuery));
                    break;
                case AIProvider.Google:
                    yield return StartCoroutine(ProcessGeminiRequest(systemPrompt, finalUserMessage, isCostQuery));
                    break;
            }

            isProcessingRequest = false;
        }

        private IEnumerator ProcessOpenAIRequest(string systemPrompt, string userMessage, bool isCostQuery)
        {
            var gptRequest = new GPTRequest
            {
                model = GetModelString(),
                messages = new List<GPTMessage>
                {
                    new GPTMessage { role = "system", content = systemPrompt },
                    new GPTMessage { role = "user", content = userMessage }
                },
                // Keep the same warm temperature for cost queries — the prompt asks for math, the voice should stay natural.
                temperature = temperature
            };

            // Use correct token parameter based on model
            if (UsesCompletionTokenField())
            {
                gptRequest.max_completion_tokens = maxTokens;
            }
            else
            {
                gptRequest.max_tokens = maxTokens;
            }

            BIMLog.Verbose($"[AEC GPT] Processing with OpenAI {GetModelString()} (using {(UsesCompletionTokenField() ? "max_completion_tokens" : "max_tokens")})");
            yield return StartCoroutine(SendGPTRequest(gptRequest, ProcessGPTResponse));
        }

        private IEnumerator ProcessClaudeRequest(string systemPrompt, string userMessage, bool isCostQuery)
        {
            var claudeRequest = new ClaudeRequest
            {
                model = GetClaudeModelString(),
                max_tokens = maxTokens,
                temperature = temperature,
                system = systemPrompt,
                messages = new List<ClaudeMessage>
                {
                    new ClaudeMessage { role = "user", content = userMessage }
                }
            };

            BIMLog.Verbose($"[AEC GPT] Processing with Claude {GetClaudeModelString()}");
            yield return StartCoroutine(SendClaudeRequest(claudeRequest, ProcessGPTResponse));
        }

        private IEnumerator ProcessGeminiRequest(string systemPrompt, string userMessage, bool isCostQuery)
        {
            // Use systemInstruction field to match OpenAI and Claude's separation of system and user prompts
            var geminiRequest = new GeminiRequest
            {
                systemInstruction = new GeminiSystemInstruction
                {
                    parts = new List<GeminiPart>
                    {
                        new GeminiPart { text = systemPrompt }
                    }
                },
                contents = new List<GeminiContent>
                {
                    new GeminiContent
                    {
                        parts = new List<GeminiPart>
                        {
                            new GeminiPart { text = userMessage }
                        }
                    }
                },
                generationConfig = new GeminiGenerationConfig
                {
                    temperature = temperature,
                    maxOutputTokens = maxTokens
                }
            };

            BIMLog.Verbose($"[AEC GPT] Processing with Gemini {GetGeminiModelString()} (using systemInstruction)");
            yield return StartCoroutine(SendGeminiRequest(geminiRequest, ProcessGPTResponse));
        }

        private IEnumerator SendGPTRequest(GPTRequest request, Action<string> onComplete)
        {
            yield return StartCoroutine(SendGPTRequestWithRetry(request, onComplete, 0));
        }

        private IEnumerator SendClaudeRequest(ClaudeRequest request, Action<string> onComplete)
        {
            yield return StartCoroutine(SendClaudeRequestWithRetry(request, onComplete, 0));
        }

        private IEnumerator SendGeminiRequest(GeminiRequest request, Action<string> onComplete)
        {
            yield return StartCoroutine(SendGeminiRequestWithRetry(request, onComplete, 0));
        }

        private IEnumerator SendGPTRequestWithRetry(GPTRequest request, Action<string> onComplete, int attemptNumber)
        {
            string jsonPayload = JsonConvert.SerializeObject(request, Formatting.Indented);

            // Log the request payload for debugging
            BIMLog.Verbose($"[AEC GPT] Request Payload:\n{jsonPayload}");

            // Sanitize key just before use (in case changed externally)
            if (!string.IsNullOrEmpty(openAIApiKey))
            {
                string cleaned = SanitizeKey(openAIApiKey);
                if (cleaned != openAIApiKey)
                {
                    BIMLog.Verbose("[AEC GPT] API key sanitized (whitespace/hidden chars removed)");
                    openAIApiKey = cleaned;
                }
            }

            if (!IsValidAPIKey(openAIApiKey))
            {
                string err = "Invalid / Missing API key before request dispatch";
                Debug.LogError($"[AEC GPT] {err}");
                onComplete?.Invoke(err);
                yield break;
            }

            using (UnityWebRequest www = new UnityWebRequest(GPT_API_URL, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
                LogOutbound("llm", bodyRaw.Length, jsonPayload); // U14
                www.uploadHandler = new UploadHandlerRaw(bodyRaw);
                www.downloadHandler = new DownloadHandlerBuffer();
                www.SetRequestHeader("Content-Type", "application/json");
                www.SetRequestHeader("Authorization", $"Bearer {openAIApiKey}");
                www.timeout = requestTimeout;

                BIMLog.Verbose($"[AEC GPT] Sending request (attempt {attemptNumber + 1}/{maxRetryAttempts + 1}) to {GetModelString()}");

                yield return www.SendWebRequest();

                bool shouldRetryOnEmptyResponse = false;

                if (www.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        // Log the raw response for debugging
                        string rawResponse = www.downloadHandler.text;
                        BIMLog.Verbose($"[AEC GPT] Raw API Response (first 500 chars): {(rawResponse.Length > 500 ? rawResponse.Substring(0, 500) + "..." : rawResponse)}");

                        var response = JsonConvert.DeserializeObject<GPTResponse>(rawResponse);

                        if (response?.choices != null && response.choices.Count > 0)
                        {
                            string content = response.choices[0].message?.content?.Trim();
                            BIMLog.Verbose($"[AEC GPT] Success on attempt {attemptNumber + 1}, Content length: {content?.Length ?? 0}");

                            // Handle empty or whitespace-only responses
                            if (string.IsNullOrWhiteSpace(content))
                            {
                                Debug.LogWarning("[AEC GPT] Received empty/null response content from API");
                                Debug.LogWarning($"[AEC GPT] Message object null: {response.choices[0].message == null}");
                                Debug.LogWarning($"[AEC GPT] Content value: '{response.choices[0].message?.content}'");

                                // Mark for retry if we have attempts left
                                if (attemptNumber < maxRetryAttempts)
                                {
                                    shouldRetryOnEmptyResponse = true;
                                }
                                else
                                {
                                    onComplete?.Invoke("I apologize, but I didn't generate a response. Could you please rephrase your question about the project costs or technical details?");
                                }
                            }
                            else
                            {
                                BIMLog.Verbose($"[AEC GPT] Response received: {content.Substring(0, Math.Min(100, content.Length))}...");
                                _lastCallOk = true; // U4: provider usage block
                                _lastPromptTokens = response.usage?.prompt_tokens; _lastCompletionTokens = response.usage?.completion_tokens;
                                onComplete?.Invoke(content);
                            }
                        }
                        else
                        {
                            Debug.LogWarning("[AEC GPT] API response contained no choices");
                            Debug.LogWarning($"[AEC GPT] Response null: {response == null}, Choices null: {response?.choices == null}, Choices count: {response?.choices?.Count ?? 0}");
                            onComplete?.Invoke("I apologize, but I'm having trouble processing your cost estimation request. Please try asking about specific module costs or material quantities.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[AEC GPT] Error parsing response: {ex.Message}");
                        onComplete?.Invoke("I encountered an error processing your cost estimation request. Please try asking about project costs in a different way.");
                    }
                }

                // Handle retry for empty response outside of try-catch
                if (shouldRetryOnEmptyResponse)
                {
                    BIMLog.Verbose($"[AEC GPT] Retrying due to empty response (attempt {attemptNumber + 2}/{maxRetryAttempts + 1})");
                    yield return new WaitForSeconds(baseRetryDelay);
                    yield return StartCoroutine(SendGPTRequestWithRetry(request, onComplete, attemptNumber + 1));
                    yield break;
                }

                if (www.result != UnityWebRequest.Result.Success)
                {
                    bool shouldRetry = false;
                    string errorDetails = "";
                    long responseCode = www.responseCode;

                    // Check if we should retry based on response code
                    if (enableRetryOnRateLimit && attemptNumber < maxRetryAttempts)
                    {
                        shouldRetry = responseCode == 429 || // Too Many Requests
                                     responseCode == 529 || // Service Overloaded  
                                     responseCode == 502 || // Bad Gateway
                                     responseCode == 503 || // Service Unavailable
                                     responseCode == 504;   // Gateway Timeout
                    }

                    // Handle 401 Unauthorized specifically
                    if (responseCode == 401)
                    {
                        Debug.LogError("[AEC GPT] 401 Unauthorized - API key is invalid, missing, or expired");
                        Debug.LogError("[AEC GPT] Please check your OpenAI API key in the Inspector or get a new one from: https://platform.openai.com/api-keys");
                        shouldRetry = false; // Don't retry 401 errors
                    }

                    if (!string.IsNullOrEmpty(www.downloadHandler.text))
                    {
                        try
                        {
                            var errorResponse = JsonConvert.DeserializeObject<GPTErrorResponse>(www.downloadHandler.text);
                            if (errorResponse?.error != null)
                            {
                                errorDetails = errorResponse.error.code switch
                                {
                                    "invalid_api_key" => "Invalid API key - Please check your OpenAI API key in the Inspector. Get a new key from: https://platform.openai.com/api-keys",
                                    "model_not_found" => $"Model '{GetModelString()}' not found - Check if you have access to this model",
                                    "rate_limit_exceeded" => "Rate limit exceeded - too many requests. Please wait a moment",
                                    "insufficient_quota" => "API quota exceeded - Please check your OpenAI billing at: https://platform.openai.com/usage",
                                    "invalid_request_error" => $"Invalid request: {errorResponse.error.message}",
                                    _ => $"{errorResponse.error.type}: {errorResponse.error.message}"
                                };

                                // Check error code for retry eligibility
                                if (enableRetryOnRateLimit && attemptNumber < maxRetryAttempts)
                                {
                                    shouldRetry = shouldRetry || errorResponse.error.code == "rate_limit_exceeded";
                                }
                            }
                        }
                        catch
                        {
                            errorDetails = $"HTTP {responseCode}: {www.downloadHandler.text}";
                        }
                    }
                    else
                    {
                        errorDetails = responseCode switch
                        {
                            401 => "HTTP 401 Unauthorized: Invalid or missing API key. Please set your OpenAI API key in the Inspector.",
                            403 => "HTTP 403 Forbidden: API key doesn't have permission or has expired.",
                            _ => $"HTTP {responseCode}: {www.error}"
                        };
                    }

                    if (shouldRetry)
                    {
                        // Calculate exponential backoff delay
                        float retryDelay = Mathf.Min(baseRetryDelay * Mathf.Pow(2, attemptNumber), maxRetryDelay);

                        string retryMessage = $"Service temporarily busy, retrying in {retryDelay:F0} seconds... (attempt {attemptNumber + 2}/{maxRetryAttempts + 1})";
                        Debug.LogWarning($"[AEC GPT] Request failed (HTTP {responseCode}), retrying in {retryDelay:F1}s... (attempt {attemptNumber + 1}/{maxRetryAttempts + 1})");

                        // Provide user feedback during retry
                        OnAssistantMessageReceived?.Invoke(retryMessage);

                        yield return new WaitForSeconds(retryDelay);
                        yield return StartCoroutine(SendGPTRequestWithRetry(request, onComplete, attemptNumber + 1));
                    }
                    else
                    {
                        string finalError = attemptNumber > 0
                            ? $"Failed after {attemptNumber + 1} attempts. Last error: {errorDetails}"
                            : $"Error: {errorDetails}";

                        Debug.LogError($"[AEC GPT] {finalError}");
                        onComplete?.Invoke(finalError);
                    }
                }
            }
        }

        private IEnumerator SendClaudeRequestWithRetry(ClaudeRequest request, Action<string> onComplete, int attemptNumber)
        {
            string jsonPayload = JsonConvert.SerializeObject(request);

            // Sanitize key before use
            anthropicApiKey = SanitizeKey(anthropicApiKey);

            if (!IsValidAPIKey(anthropicApiKey, "sk-ant-"))
            {
                Debug.LogError("[AEC GPT] Invalid Anthropic API key");
                onComplete?.Invoke("Invalid Anthropic API key");
                yield break;
            }

            using (UnityWebRequest www = new UnityWebRequest(CLAUDE_API_URL, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
                LogOutbound("llm", bodyRaw.Length, jsonPayload); // U14
                www.uploadHandler = new UploadHandlerRaw(bodyRaw);
                www.downloadHandler = new DownloadHandlerBuffer();
                www.SetRequestHeader("Content-Type", "application/json");
                www.SetRequestHeader("x-api-key", anthropicApiKey);
                www.SetRequestHeader("anthropic-version", "2023-06-01");
                www.timeout = requestTimeout;

                BIMLog.Verbose($"[AEC GPT] Sending Claude request (attempt {attemptNumber + 1})");

                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        var response = JsonConvert.DeserializeObject<ClaudeResponse>(www.downloadHandler.text);
                        if (response?.content != null && response.content.Count > 0)
                        {
                            string content = response.content[0].text?.Trim();
                            if (!string.IsNullOrWhiteSpace(content))
                            {
                                _lastCallOk = true; // U4: provider usage block
                                _lastPromptTokens = response.usage?.input_tokens; _lastCompletionTokens = response.usage?.output_tokens;
                                onComplete?.Invoke(content);
                            }
                            else
                            {
                                onComplete?.Invoke("I apologize, but I didn't generate a response. Please try again.");
                            }
                        }
                        else
                        {
                            onComplete?.Invoke("I'm having trouble processing your request. Please try again.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[AEC GPT] Claude response parsing error: {ex.Message}");
                        onComplete?.Invoke("Error processing response. Please try again.");
                    }
                }
                else
                {
                    // Special-case 404 for Anthropic to surface likely invalid model IDs
                    if (www.responseCode == 404)
                    {
                        string configuredModel = GetClaudeModelString();
                        string body = www.downloadHandler != null ? www.downloadHandler.text : string.Empty;
                        Debug.LogError($"[AEC GPT] Claude 404 Not Found. This usually means the model ID '{configuredModel}' is invalid or unavailable in your account. Body: {body}");
                        BIMLog.Verbose("[AEC GPT] Tip: Use 'List Anthropic Models' from the context menu to see valid model IDs returned by your account.");
                        onComplete?.Invoke($"Claude model not found: '{configuredModel}'. Please choose a valid model ID from your Anthropic models list.");
                        yield break;
                    }

                    HandleAPIError(www, "Claude", attemptNumber, () =>
                        StartCoroutine(SendClaudeRequestWithRetry(request, onComplete, attemptNumber + 1)),
                        onComplete);
                }
            }
        }

        private IEnumerator SendGeminiRequestWithRetry(GeminiRequest request, Action<string> onComplete, int attemptNumber)
        {
            string jsonPayload = JsonConvert.SerializeObject(request);

            // Sanitize key before use
            googleApiKey = SanitizeKey(googleApiKey);

            if (string.IsNullOrEmpty(googleApiKey) || googleApiKey.Length < 20)
            {
                Debug.LogError("[AEC GPT] Invalid Google API key");
                onComplete?.Invoke("Invalid Google API key");
                yield break;
            }

            string url = $"{GEMINI_API_URL}{GetGeminiModelString()}:generateContent?key={googleApiKey}";

            using (UnityWebRequest www = new UnityWebRequest(url, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
                LogOutbound("llm", bodyRaw.Length, jsonPayload); // U14
                www.uploadHandler = new UploadHandlerRaw(bodyRaw);
                www.downloadHandler = new DownloadHandlerBuffer();
                www.SetRequestHeader("Content-Type", "application/json");
                www.timeout = requestTimeout;

                BIMLog.Verbose($"[AEC GPT] Sending Gemini request (attempt {attemptNumber + 1})");

                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        var response = JsonConvert.DeserializeObject<GeminiResponse>(www.downloadHandler.text);
                        if (response?.candidates != null && response.candidates.Count > 0 &&
                            response.candidates[0].content?.parts != null && response.candidates[0].content.parts.Count > 0)
                        {
                            string content = response.candidates[0].content.parts[0].text?.Trim();
                            if (!string.IsNullOrWhiteSpace(content))
                            {
                                _lastCallOk = true; // U4: provider usage block
                                _lastPromptTokens = response.usageMetadata?.promptTokenCount; _lastCompletionTokens = response.usageMetadata?.candidatesTokenCount;
                                onComplete?.Invoke(content);
                            }
                            else
                            {
                                onComplete?.Invoke("I apologize, but I didn't generate a response. Please try again.");
                            }
                        }
                        else
                        {
                            onComplete?.Invoke("I'm having trouble processing your request. Please try again.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[AEC GPT] Gemini response parsing error: {ex.Message}");
                        onComplete?.Invoke("Error processing response. Please try again.");
                    }
                }
                else
                {
                    HandleAPIError(www, "Gemini", attemptNumber, () =>
                        StartCoroutine(SendGeminiRequestWithRetry(request, onComplete, attemptNumber + 1)),
                        onComplete);
                }
            }
        }

        private void HandleAPIError(UnityWebRequest www, string provider, int attemptNumber, Action retryAction, Action<string> onComplete)
        {
            bool shouldRetry = false;
            long responseCode = www.responseCode;

            if (enableRetryOnRateLimit && attemptNumber < maxRetryAttempts)
            {
                shouldRetry = responseCode == 429 || responseCode == 529 ||
                             responseCode == 502 || responseCode == 503 || responseCode == 504;
            }

            string errorDetails = $"HTTP {responseCode}: {www.error}";
            if (!string.IsNullOrEmpty(www.downloadHandler?.text))
            {
                errorDetails += $" | {www.downloadHandler.text}";
            }

            if (shouldRetry)
            {
                float retryDelay = Mathf.Min(baseRetryDelay * Mathf.Pow(2, attemptNumber), maxRetryDelay);
                Debug.LogWarning($"[AEC GPT] {provider} request failed, retrying in {retryDelay:F1}s...");
                StartCoroutine(RetryAfterDelay(retryDelay, retryAction));
            }
            else
            {
                string finalError = $"{provider} Error: {errorDetails}";
                Debug.LogError($"[AEC GPT] {finalError}");
                onComplete?.Invoke(finalError);
            }
        }

        private IEnumerator RetryAfterDelay(float delay, Action retryAction)
        {
            yield return new WaitForSeconds(delay);
            retryAction?.Invoke();
        }

        #region Study instrumentation (U4-U7, U14)

        private BIMMR.Inspection.Study.LlmCallRecord _pendingCall;
        private string _pendingContext = "";
        private int? _lastPromptTokens, _lastCompletionTokens;
        private bool _lastCallOk;
        private bool _lastValidatorFlag;
        private int _namesRegisteredForModule = int.MinValue;

        /// <summary>Open the llm_requests.jsonl record for a call (U4). In restricted-data mode
        /// the system message, user message and context are redacted first (U14).</summary>
        private void BeginLlmCall(string callType, ref string systemPrompt, ref string userMessage,
                                  int tokens, bool jsonMode, string eventId = null, string elementId = null,
                                  string contextOverride = null)
        {
            RegisterSceneNames();
            string context = contextOverride ?? _lastContextBlock ?? "";
            if (eventId == null) eventId = inspectionManager != null ? inspectionManager.CurrentEventId : "";
            if (elementId == null) elementId = inspectionManager?.CurrentEvidence?.elementId ?? "";

            bool restricted = BIMMR.Inspection.Study.StudySession.RestrictedDataMode;
            if (restricted)
            {
                systemPrompt = BIMMR.Inspection.Study.RestrictedDataFilter.Redact(systemPrompt);
                userMessage = BIMMR.Inspection.Study.RestrictedDataFilter.Redact(userMessage);
                context = BIMMR.Inspection.Study.RestrictedDataFilter.Redact(context);
                BIMMR.Inspection.Study.RestrictedDataFilter.SaveMap();
            }

            string contextHash = BIMMR.Inspection.Study.Hashing.Sha256(context);
            var rec = new BIMMR.Inspection.Study.LlmCallRecord
            {
                prompt_version = PromptVersion,
                event_id = eventId,
                element_id = elementId,
                request_ts = BIMMR.Inspection.Study.StudySession.NowIso(),
                call_type = callType,
                provider = selectedProvider.ToString(),
                model_id = GetCurrentModelString(),
                temperature = temperature,
                max_tokens = tokens,
                json_mode = jsonMode,
                system_hash = BIMMR.Inspection.Study.Hashing.Sha256(systemPrompt),
                context_hash = contextHash,
                context_chars = context.Length,
                context_contains_event_id = !string.IsNullOrEmpty(elementId) &&
                                            context.IndexOf(elementId, StringComparison.Ordinal) >= 0,
                user_message = userMessage
            };
            if (restricted)
            {
                // Keep the redacted context out of the shared log; store it locally by hash.
                string rel = $"contexts/{contextHash}.txt";
                try { System.IO.File.WriteAllText(BIMMR.Inspection.Study.StudySession.PathIn(rel), context); } catch { }
                rec.context_text = "file:" + rel;
            }
            else rec.context_text = context;

            _pendingCall = rec;
            _pendingContext = context;
            _lastPromptTokens = _lastCompletionTokens = null;
            _lastCallOk = false;
        }

        /// <summary>Close the pending record: response, usage, validator output (U6).
        /// Returns the response with names restored for display.</summary>
        private string FinishLlmCall(string response)
        {
            var rec = _pendingCall;
            _pendingCall = null;
            _lastValidatorFlag = false;
            if (rec == null) return response;

            rec.response_ts = BIMMR.Inspection.Study.StudySession.NowIso();
            rec.ok = _lastCallOk;
            rec.prompt_tokens = _lastPromptTokens;
            rec.completion_tokens = _lastCompletionTokens;
            rec.response_text = response;
            if (_lastCallOk)
            {
                var v = BIMMR.Inspection.Study.LLMResponseValidator.Validate(response, _pendingContext);
                rec.ids_in_response = v.idsInResponse;
                rec.numbers_in_response = v.numbersInResponse;
                rec.unsupported_tokens = v.unsupportedTokens;
                rec.validator_flag = v.Flag;
                _lastValidatorFlag = v.Flag;
            }
            BIMMR.Inspection.Study.LlmRequestLog.Write(rec);

            return BIMMR.Inspection.Study.StudySession.RestrictedDataMode
                ? BIMMR.Inspection.Study.RestrictedDataFilter.Restore(response)
                : response;
        }

        private void LogOutbound(string endpoint, long bytes, string payloadText)
        {
            RegisterSceneNames();
            BIMMR.Inspection.Study.OutboundPayloadLog.Write(endpoint, bytes,
                BIMMR.Inspection.Study.RestrictedDataFilter.ContainsSensitiveNames(payloadText));
        }

        /// <summary>Register project, module and element names once per loaded module, so
        /// restricted-data mode can redact them and the outbound log can detect them.</summary>
        private void RegisterSceneNames()
        {
            var module = moduleManager != null ? moduleManager.GetCurrentModule() : null;
            int key = module != null ? module.GetHashCode() : 0;
            if (key == _namesRegisteredForModule) return;
            _namesRegisteredForModule = key;

            if (moduleManager != null)
            {
                foreach (var p in moduleManager.projects)
                    if (p != null) BIMMR.Inspection.Study.RestrictedDataFilter.RegisterProject(p.projectName);
                foreach (var m in moduleManager.GetAvailableModuleNames())
                    BIMMR.Inspection.Study.RestrictedDataFilter.RegisterModule(m);
            }
            if (module != null)
            {
                BIMMR.Inspection.Study.RestrictedDataFilter.RegisterModule(module.name);
                foreach (var md in module.GetComponentsInChildren<Metadata>(true))
                    BIMMR.Inspection.Study.RestrictedDataFilter.RegisterElement(CleanModuleName(md.gameObject.name));
            }
        }

        /// <summary>Persistent BIM id from the element's metadata, using the same keys and
        /// order as the inspection module's metadata reader.</summary>
        private string GetPersistentElementId(GameObject element)
        {
            var t = element != null ? element.transform : null;
            for (int depth = 0; t != null && depth < 8; depth++, t = t.parent)
            {
                var md = t.GetComponent<Metadata>();
                if (md == null || md.Properties == null) continue;
                foreach (var key in BIMMR.Inspection.BIMElementMetadataReader.DefaultStableIdKeys)
                {
                    var p = md.Properties.Find(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
                    if (p != null && !string.IsNullOrWhiteSpace(p.Value) &&
                        !p.Value.Equals("<undefined>", StringComparison.OrdinalIgnoreCase))
                        return p.Value.Trim();
                }
            }
            return element != null ? "unbound:" + CleanModuleName(element.name) : "";
        }

        private void AppendInspectionState(StringBuilder context)
        {
            var mgr = inspectionManager;
            context.AppendLine("Inspection State:");
            var ev = mgr.CurrentEvidence;
            if (ev != null)
            {
                string status = mgr.Records.TryGetValue(ev.elementId, out var rec) ? rec.status.ToString() : "InProgress";
                context.AppendLine($"Current inspection event: {ev.eventId}, Element ID: {ev.elementId}, " +
                                   $"name {ev.elementName}, category {ev.category}, status {status}");
            }
            else context.AppendLine("No element confirmed yet in this session.");
            var statuses = mgr.Records.Values.GroupBy(r => r.status).Select(g => $"{g.Key} {g.Count()}");
            context.AppendLine($"Events recorded this session: {mgr.Evidence.Count}; element statuses: {string.Join(", ", statuses)}");
            context.AppendLine();
        }

        private void AppendDocuments(StringBuilder context)
        {
            var lines = new List<string>();
            foreach (var pd in documentManager.projectDrawings)
            {
                if (pd == null) continue;
                var parts = pd.elements.Where(e => e != null && e.sheets.Count > 0)
                                       .Select(e => $"{e.elementName} ({e.sheets.Count} sheets)");
                string joined = string.Join(", ", parts);
                if (joined.Length > 0) lines.Add($"{pd.projectName}: {joined}");
            }
            context.AppendLine("\nAvailable Documents:");
            context.AppendLine(lines.Count > 0 ? string.Join("\n", lines.Take(5)) : "No linked documents loaded.");
        }

        // ---- IReportNarrativeService (U1, U7) ---------------------------------

        public bool NarrativeAvailable => IsReady();

        public IEnumerator RequestNarrative(BIMMR.Inspection.EvidenceRecord record, Action<string, bool> onDone)
        {
            float waited = 0f;
            while (isProcessingRequest && waited < 15f) { waited += Time.unscaledDeltaTime; yield return null; }
            isProcessingRequest = true;

            var block = new Newtonsoft.Json.Linq.JObject
            {
                ["element_id"] = record.elementId,
                ["element_name"] = record.elementName,
                ["element_type"] = record.elementType,
                ["category"] = record.category,
                ["module_id"] = record.moduleId,
                ["timestamp"] = record.timestamp,
                ["status"] = record.status,
                ["geometric_descriptors"] = record.geometricDescriptors,
                ["notes"] = record.notes
            }.ToString(Formatting.Indented);

            string system = NarrativeSystemPrompt;
            string user = "RECORD:\n" + block;
            bool jsonMode = selectedProvider == AIProvider.OpenAI;
            BeginLlmCall("report_narrative", ref system, ref user, narrativeMaxTokens, jsonMode,
                         record.eventId, record.elementId, block);

            string raw = null;
            bool done = false;
            Action<string> cb = r => { raw = r; done = true; };
            switch (selectedProvider)
            {
                case AIProvider.OpenAI:
                    var req = new GPTRequest
                    {
                        model = GetModelString(),
                        messages = new List<GPTMessage>
                        {
                            new GPTMessage { role = "system", content = system },
                            new GPTMessage { role = "user", content = user }
                        },
                        temperature = temperature,
                        response_format = new { type = "json_object" }
                    };
                    if (UsesCompletionTokenField()) req.max_completion_tokens = narrativeMaxTokens;
                    else req.max_tokens = narrativeMaxTokens;
                    yield return StartCoroutine(SendGPTRequest(req, cb));
                    break;
                case AIProvider.Anthropic:
                    yield return StartCoroutine(SendClaudeRequest(new ClaudeRequest
                    {
                        model = GetClaudeModelString(), system = system, max_tokens = narrativeMaxTokens,
                        temperature = temperature,
                        messages = new List<ClaudeMessage> { new ClaudeMessage { role = "user", content = user } }
                    }, cb));
                    break;
                case AIProvider.Google:
                    yield return StartCoroutine(SendGeminiRequest(new GeminiRequest
                    {
                        systemInstruction = new GeminiSystemInstruction { parts = new List<GeminiPart> { new GeminiPart { text = system } } },
                        contents = new List<GeminiContent> { new GeminiContent { parts = new List<GeminiPart> { new GeminiPart { text = user } } } },
                        generationConfig = new GeminiGenerationConfig { temperature = temperature, maxOutputTokens = narrativeMaxTokens }
                    }, cb));
                    break;
            }
            float t = 0f;
            while (!done && t < 30f) { t += Time.unscaledDeltaTime; yield return null; }

            bool ok = done && _lastCallOk;
            string text = FinishLlmCall(raw ?? "");
            isProcessingRequest = false;
            onDone?.Invoke(ok ? ExtractNarrative(text) : "", _lastValidatorFlag);
        }

        private static string ExtractNarrative(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            try
            {
                string s = text.Trim();
                int a = s.IndexOf('{'), b = s.LastIndexOf('}');
                if (a >= 0 && b > a) s = s.Substring(a, b - a + 1);
                var jo = Newtonsoft.Json.Linq.JObject.Parse(s);
                return (string)jo["narrative"] ?? text;
            }
            catch { return text; }   // non-JSON reply: keep it, the validator has already run
        }

        #endregion

        // Context block of the most recent request: what the validator checks against (U6).
        private string _lastContextBlock = "";

        private string CreateMRSystemPromptWithContext()
        {
            StringBuilder prompt = new StringBuilder(useRevisedSystemPrompt ? RevisedSystemPrompt : mixedRealitySystemPrompt);
            _lastContextBlock = "";

            if (includeDetailedSceneContext && !BIMMR.Inspection.Study.StudySession.LlmContextEnabled)
            {
                // U15 ablation: the assistant is prompted generically, without the scene payload.
                BIMLog.Verbose("[AEC GPT Context] Scene context switched off for this condition (U15).");
            }
            else if (includeDetailedSceneContext)
            {
                string sceneContext = GenerateDetailedSceneContext();
                _lastContextBlock = sceneContext ?? "";
                if (!string.IsNullOrEmpty(sceneContext))
                {
                    prompt.AppendLine("\n\nCURRENT MR SCENE CONTEXT:");
                    prompt.AppendLine(sceneContext);

                    // Log context size for verification (all providers receive the same context)
                    BIMLog.Verbose($"[AEC GPT Context] Generated BIM context for {selectedProvider}: {sceneContext.Length} chars, {sceneContext.Split('\n').Length} lines");
                }
                else
                {
                    Debug.LogWarning($"[AEC GPT Context] No scene context generated for {selectedProvider} - BIM metadata may be unavailable");
                }
            }
            else
            {
                Debug.LogWarning($"[AEC GPT Context] Detailed scene context is DISABLED for {selectedProvider}");
            }

            return prompt.ToString();
        }

        private string GenerateDetailedSceneContext()
        {
            try
            {
                StringBuilder context = new StringBuilder();

                if (includeSelectedElement)
                {
                    var selectedElements = GetSelectedElements();
                    if (selectedElements.Count > 0)
                    {
                        context.AppendLine("Currently Selected Elements:");
                        foreach (var element in selectedElements.Take(5)) // Limit to 5 elements for speed
                        {
                            AppendElementMetadata(context, element);
                        }
                        if (selectedElements.Count > 5)
                        {
                            context.AppendLine($"... and {selectedElements.Count - 5} more elements");
                        }
                        context.AppendLine();
                    }
                }

                // U5: inspection state, right after the selected element.
                if (includeInspectionState && inspectionManager != null)
                    AppendInspectionState(context);

                if (includeBIMMetadata && metadataViewer != null)
                {
                    context.AppendLine("BIM Model Hierarchy and Elements:");
                    context.AppendLine("IMPORTANT: The BIM model follows a strict hierarchical structure:");
                    context.AppendLine("  - Top Level: Structure, Architecture, MEP (main categories)");
                    context.AppendLine("  - Mid Level: Columns, Beams, Walls, Doors, etc. (component types)");
                    context.AppendLine("  - Leaf Level: Column-001, Beam-045, Door-23, etc. (individual elements with metadata)");
                    context.AppendLine("Note: Element counts represent all leaf-level components. Parent nodes are organizational containers.");
                    context.AppendLine("Example hierarchy: Structure → Columns → Column-001, Column-002...");
                    context.AppendLine("                  Architecture → Exterior Walls → Wall-101, Wall-102...");

                    var bimElements = GetBIMElements();

                    // U5: the digest describes the active module only, matching module scoping.
                    var activeModule = moduleManager != null ? moduleManager.GetCurrentModule() : null;
                    if (limitDigestToActiveModule && activeModule != null)
                    {
                        bimElements = bimElements.Where(e => e != null && e.transform.IsChildOf(activeModule.transform)).ToList();
                        context.AppendLine($"Scope: active module {CleanModuleName(activeModule.name)} only.");
                    }

                    context.AppendLine($"\nTotal Individual BIM Components (leaf-level elements): {bimElements.Count}");

                    if (bimElements.Count == 0)
                    {
                        Debug.LogWarning("[AEC GPT Context] No BIM elements found! Check if metadataViewer is properly initialized.");
                        context.AppendLine("WARNING: No BIM elements detected in scene. Metadata may not be available.");
                    }

                    var elementsByType = bimElements.GroupBy(e => GetElementType(e)).OrderBy(g => g.Key).ToList();
                    context.AppendLine($"\nComponent Counts by Type (all hierarchy levels included):");

                    // Group by main categories first (Structure, Architecture, MEP)
                    var categoryGroups = new Dictionary<string, List<string>>();
                    categoryGroups["Structure"] = new List<string> { "Column", "Beam", "Connection", "Stairs" };
                    categoryGroups["Architecture"] = new List<string> { "Stud", "Wall", "Exterior Wall", "Interior Wall",
                                                                        "Floor", "Ceiling", "Roof", "Window", "Door", "Furniture" };
                    categoryGroups["MEP"] = new List<string> { "Electric", "Plumbing", "Lighting", "HVAC", "Duct", "Pipe" };

                    foreach (var category in categoryGroups)
                    {
                        var categoryElements = elementsByType.Where(g =>
                            category.Value.Any(type => g.Key.Contains(type, StringComparison.OrdinalIgnoreCase))).ToList();

                        if (categoryElements.Any())
                        {
                            int categoryTotal = categoryElements.Sum(g => g.Count());
                            context.AppendLine($"\n  {category.Key} ({categoryTotal} total components):");
                            foreach (var typeGroup in categoryElements)
                            {
                                context.AppendLine($"    • {typeGroup.Key}: {typeGroup.Count()} components");
                            }
                        }
                    }

                    // Show other/uncategorized elements
                    var uncategorized = elementsByType.Where(g =>
                        !categoryGroups.Values.SelectMany(v => v).Any(type =>
                            g.Key.Contains(type, StringComparison.OrdinalIgnoreCase))).ToList();

                    if (uncategorized.Any())
                    {
                        context.AppendLine($"\n  Other Components ({uncategorized.Sum(g => g.Count())} total):");
                        foreach (var typeGroup in uncategorized)
                        {
                            context.AppendLine($"    • {typeGroup.Key}: {typeGroup.Count()} components");
                        }
                    }

                    context.AppendLine($"\nDetailed Component Information (samples showing hierarchy paths):");
                    // Show samples from each type with hierarchical context
                    foreach (var typeGroup in elementsByType.Take(10))
                    {
                        context.AppendLine($"\n{typeGroup.Key} Components ({typeGroup.Count()} total):");

                        // Show up to 3 elements of each type with full metadata
                        foreach (var element in typeGroup.Take(3))
                        {
                            var metadata = GetElementMetadata(element);
                            if (metadata != null)
                            {
                                // Include hierarchy path to show nesting
                                string hierarchyPath = GetHierarchyPath(element);
                                context.Append($"  [{hierarchyPath}] ");
                                context.Append($"{metadata.ElementType}: {CleanModuleName(metadata.ElementName)} (Element ID: {metadata.ElementID})");

                                // Include ALL relevant properties (not just 5) to ensure comprehensive metadata access
                                if (metadata.Properties != null && metadata.Properties.Count > 0)
                                {
                                    context.AppendLine();

                                    // Priority properties (show first)
                                    var priorityKeys = new[] { "Name", "Type", "Material", "Height", "Area", "Width", "Length",
                                                              "FireRating", "GlazingType", "Diameter", "FlowRate", "PowerRating",
                                                              "Slope", "RiserCount", "QC_Status", "SystemName", "Level", "Function" };

                                    var priorityProps = metadata.Properties
                                        .Where(p => priorityKeys.Contains(p.Key, StringComparer.OrdinalIgnoreCase) &&
                                                   !string.IsNullOrEmpty(p.Value) &&
                                                   !p.Value.Equals("<undefined>", StringComparison.OrdinalIgnoreCase))
                                        .OrderBy(p => Array.IndexOf(priorityKeys, p.Key));

                                    foreach (var prop in priorityProps)
                                    {
                                        context.AppendLine($"      {prop.Key}: {prop.Value}");
                                    }

                                    // Then show additional properties (up to 10 total to keep context manageable)
                                    var otherProps = metadata.Properties
                                        .Where(p => !priorityKeys.Contains(p.Key, StringComparer.OrdinalIgnoreCase) &&
                                                   !string.IsNullOrEmpty(p.Key) && !string.IsNullOrEmpty(p.Value) &&
                                                   !p.Value.Equals("<undefined>", StringComparison.OrdinalIgnoreCase))
                                        .Take(10 - priorityProps.Count());

                                    foreach (var prop in otherProps)
                                    {
                                        context.AppendLine($"      {prop.Key}: {prop.Value}");
                                    }

                                    // Report missing critical properties
                                    var missingCritical = priorityKeys
                                        .Where(key => !metadata.Properties.ContainsKey(key) ||
                                                     string.IsNullOrEmpty(metadata.Properties[key]) ||
                                                     metadata.Properties[key].Equals("<undefined>", StringComparison.OrdinalIgnoreCase))
                                        .Take(3);

                                    if (missingCritical.Any())
                                    {
                                        context.AppendLine($"      [Missing: {string.Join(", ", missingCritical)}]");
                                    }
                                }
                                else
                                {
                                    context.AppendLine(" [No metadata properties available]");
                                }
                            }
                        }

                        if (typeGroup.Count() > 3)
                        {
                            context.AppendLine($"  ... and {typeGroup.Count() - 3} more {typeGroup.Key} components at various hierarchy levels");
                        }
                    }
                }

                if (moduleManager != null)
                {
                    context.AppendLine("\nModule Information:");
                    var currentModule = moduleManager.GetCurrentModule();
                    if (currentModule != null)
                        context.AppendLine($"Active module: {CleanModuleName(currentModule.name)}");

                    var availableModules = moduleManager.GetAvailableModuleNames();
                    if (availableModules.Count > 0)
                    {
                        var cleanedModules = availableModules.Select(m => CleanModuleName(m)).Take(5);
                        context.AppendLine($"Available modules: {string.Join(", ", cleanedModules)}");
                    }
                }

                if (includeComponentStates)
                {
                    context.AppendLine("\nComponent Status:");
                    var componentStates = GetComponentStates();
                    context.AppendLine($"Active: {componentStates.GetValueOrDefault("Active", 0)}, Inactive: {componentStates.GetValueOrDefault("Inactive", 0)}");
                }

                // U5: linked documents available for the inspection.
                if (includeDocuments && documentManager != null)
                    AppendDocuments(context);

                return context.ToString();
            }
            catch (Exception ex)
            {
                return $"Error analyzing scene: {ex.Message}";
            }
        }

        private void AppendElementMetadata(StringBuilder context, GameObject element)
        {
            var metadata = GetElementMetadata(element);
            if (metadata != null)
            {
                string cleanName = CleanModuleName(metadata.ElementName);
                // U5: the persistent BIM element id is written explicitly (L4 checks for it).
                context.Append($"{metadata.ElementType} named {cleanName} (Element ID: {metadata.ElementID})");

                if (!string.IsNullOrEmpty(metadata.Material))
                    context.Append($" made of {metadata.Material}");

                if (!string.IsNullOrEmpty(metadata.Manufacturer))
                    context.Append($" by {metadata.Manufacturer}");

                if (metadata.Properties != null && metadata.Properties.Count > 0)
                {
                    context.Append(" with ");
                    var props = metadata.Properties.Take(3).Select(p => $"{p.Key}={p.Value}");
                    context.AppendLine(string.Join(", ", props));
                }
                else
                {
                    context.AppendLine();
                }
            }
        }

        private string CleanModuleName(string moduleName)
        {
            return moduleName.Replace("(Clone)", "").Replace(" (Clone)", "").Trim();
        }

        private List<GameObject> GetSelectedElements()
        {
            var selectedElements = new List<GameObject>();

            if (metadataViewer != null && metadataViewer.IsViewerEnabled)
            {
                var selectedElement = metadataViewer.GetSelectedElement();
                if (selectedElement != null)
                    selectedElements.Add(selectedElement);
            }

            if (inspectionSystem != null && inspectionSystem.IsInspectionActive())
                selectedElements.AddRange(inspectionSystem.GetSelectedElements());

            return selectedElements.Distinct().ToList();
        }

        private List<GameObject> GetBIMElements()
        {
            var bimElements = new List<GameObject>();
            var metadataObjects = FindObjectsByType<Metadata>(FindObjectsSortMode.None);

            // Get all leaf-level BIM elements (actual components, not container objects)
            // We need to traverse the full hierarchy and count only the deepest children
            foreach (var metadataComponent in metadataObjects)
            {
                GameObject obj = metadataComponent.gameObject;

                // Add all children at all levels in the hierarchy
                AddAllChildrenRecursively(obj, bimElements);

                // Also add the object itself if it's a leaf node (no children with metadata)
                if (IsLeafBIMElement(obj))
                {
                    if (!bimElements.Contains(obj))
                        bimElements.Add(obj);
                }
            }

            return bimElements.Distinct().ToList();
        }

        private void AddAllChildrenRecursively(GameObject parent, List<GameObject> collection)
        {
            // Recursively traverse all children at all levels
            for (int i = 0; i < parent.transform.childCount; i++)
            {
                GameObject child = parent.transform.GetChild(i).gameObject;

                // Add this child if it's a BIM element
                if (IsBIMObject(child) && !collection.Contains(child))
                {
                    collection.Add(child);
                }

                // Recursively add all descendants
                if (child.transform.childCount > 0)
                {
                    AddAllChildrenRecursively(child, collection);
                }
            }
        }

        private bool IsLeafBIMElement(GameObject obj)
        {
            // A leaf element is one that either has no children, 
            // or its children don't have metadata (i.e., they're not BIM sub-components)
            if (obj.transform.childCount == 0)
                return true;

            for (int i = 0; i < obj.transform.childCount; i++)
            {
                GameObject child = obj.transform.GetChild(i).gameObject;
                if (IsBIMObject(child))
                    return false; // Has BIM children, so it's a container/parent
            }

            return true; // Has children but they're not BIM elements (e.g., visual geometry)
        }

        private bool IsBIMObject(GameObject obj)
        {
            // Check if object has metadata component
            if (obj.GetComponent<Metadata>() != null)
                return true;

            // Check if object name suggests it's a BIM element (individual component)
            string name = obj.name.ToLower();

            // Check for individual component patterns (with numbers/IDs)
            bool hasComponentPattern = System.Text.RegularExpressions.Regex.IsMatch(name,
                @"(wall|column|beam|slab|door|window|floor|roof|stair)[-_]?\d+");

            if (hasComponentPattern)
                return true;

            // Also check for general BIM element keywords
            if (name.Contains("wall") || name.Contains("column") || name.Contains("beam") ||
                name.Contains("slab") || name.Contains("door") || name.Contains("window") ||
                name.Contains("floor") || name.Contains("roof") || name.Contains("stair") ||
                name.Contains("mep") || name.Contains("pipe") || name.Contains("duct"))
                return true;

            // Check if parent has metadata (child of BIM element)
            Transform parent = obj.transform.parent;
            while (parent != null)
            {
                if (parent.GetComponent<Metadata>() != null)
                    return true;
                parent = parent.parent;
            }

            return false;
        }

        private string GetHierarchyPath(GameObject obj)
        {
            if (obj == null) return "";

            List<string> path = new List<string>();
            Transform current = obj.transform;

            // Build path from child to root (max 4 levels to keep it concise)
            int levels = 0;
            while (current != null && levels < 4)
            {
                path.Insert(0, CleanModuleName(current.name));
                current = current.parent;
                levels++;
            }

            return string.Join("/", path);
        }

        private BIMElementMetadata GetElementMetadata(GameObject element)
        {
            var metadata = new BIMElementMetadata
            {
                // Persistent BIM id (was the Unity instance id, which no record can resolve).
                ElementID = GetPersistentElementId(element),
                ElementName = CleanModuleName(element.name),
                Properties = new Dictionary<string, string>()
            };

            var elementMetadata = element.GetComponent<Metadata>();
            if (elementMetadata != null && elementMetadata.Properties != null)
            {
                foreach (var prop in elementMetadata.Properties)
                    metadata.Properties[prop.Key] = prop.Value;

                foreach (var prop in elementMetadata.Properties)
                {
                    if (prop.Key.Equals("ElementType", StringComparison.OrdinalIgnoreCase))
                        metadata.ElementType = !string.IsNullOrEmpty(prop.Value) ? prop.Value : "Component";
                    else if (prop.Key.Equals("Material", StringComparison.OrdinalIgnoreCase))
                        metadata.Material = !string.IsNullOrEmpty(prop.Value) ? prop.Value : "";
                }

                if (string.IsNullOrEmpty(metadata.ElementType))
                    metadata.ElementType = "Component";

                metadata.Status = element.activeInHierarchy ? "Active" : "Inactive";
                return metadata;
            }

            var standardMetadata = element.GetComponent<Metadata>();
            if (standardMetadata != null && standardMetadata.Properties != null)
            {
                foreach (var prop in standardMetadata.Properties)
                    metadata.Properties[prop.Key] = prop.Value;

                foreach (var prop in standardMetadata.Properties)
                {
                    if (prop.Key.Equals("Type", StringComparison.OrdinalIgnoreCase))
                        metadata.ElementType = !string.IsNullOrEmpty(prop.Value) ? prop.Value : "Component";
                    else if (prop.Key.Equals("Material", StringComparison.OrdinalIgnoreCase))
                        metadata.Material = !string.IsNullOrEmpty(prop.Value) ? prop.Value : "";
                }

                if (string.IsNullOrEmpty(metadata.ElementType))
                    metadata.ElementType = "Component";

                metadata.Status = element.activeInHierarchy ? "Active" : "Inactive";
                return metadata;
            }

            return null;
        }

        private string GetElementType(GameObject element)
        {
            // First check metadata for explicit type
            var metadata = GetElementMetadata(element);
            if (metadata != null && !string.IsNullOrEmpty(metadata.ElementType))
                return metadata.ElementType;

            // Check element's own name
            string name = element.name.ToLower();
            string type = InferTypeFromName(name);
            if (type != "Other")
                return type;

            // If not found, traverse up the hierarchy to find the category
            // (e.g., element is under "Columns" or "UpperBeams/Beam-001")
            Transform current = element.transform.parent;
            while (current != null)
            {
                string parentName = current.name.ToLower();
                type = InferTypeFromName(parentName);
                if (type != "Other")
                    return type;
                current = current.parent;
            }

            return "Other";
        }

        private string InferTypeFromName(string name)
        {
            if (name.Contains("wall")) return "Wall";
            if (name.Contains("column")) return "Column";
            if (name.Contains("beam")) return "Beam";
            if (name.Contains("slab")) return "Slab";
            if (name.Contains("floor")) return "Floor";
            if (name.Contains("door")) return "Door";
            if (name.Contains("window")) return "Window";
            if (name.Contains("roof")) return "Roof";
            if (name.Contains("stair")) return "Stair";
            if (name.Contains("mep")) return "MEP";
            if (name.Contains("pipe")) return "Pipe";
            if (name.Contains("duct")) return "Duct";
            return "Other";
        }

        private Dictionary<string, int> GetComponentStates()
        {
            var states = new Dictionary<string, int> { { "Active", 0 }, { "Inactive", 0 } };
            var bimElements = GetBIMElements();
            foreach (var element in bimElements)
            {
                if (element.activeInHierarchy)
                    states["Active"]++;
                else
                    states["Inactive"]++;
            }
            return states;
        }

        private void ProcessGPTResponse(string assistantMessage)
        {
            // U4/U6: log the raw response with validator output; restores names in restricted mode.
            assistantMessage = FinishLlmCall(assistantMessage);
            assistantMessage = HumanizeSpokenText(assistantMessage);

            if (enableShortResponseMode && assistantMessage.Length > maxResponseLength)
                assistantMessage = TrimToSentence(assistantMessage, maxResponseLength);

            lastResponse = assistantMessage;

            // Start TTS first so audio begins generating while we log/notify (saves perceived latency).
            if (persistentTTS && IsValidAPIKey(openAIApiKey))
                enableTextToSpeech = true;

            if (enableTextToSpeech)
            {
                if (useFastTTSMode)
                    currentTTSCoroutine = StartCoroutine(ConvertToSpeechFast(assistantMessage));
                else
                    currentTTSCoroutine = StartCoroutine(ConvertToSpeech(assistantMessage));
            }

            LogAssistantMessage(assistantMessage);
            OnAssistantMessageReceived?.Invoke(assistantMessage);
        }

        // Strips markdown/list artifacts that sound robotic when read aloud.
        private string HumanizeSpokenText(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var sb = new StringBuilder(text.Length);
            foreach (string rawLine in text.Split('\n'))
            {
                string line = rawLine.TrimEnd();
                // Drop common markdown bullets / numbered list markers at line start.
                line = System.Text.RegularExpressions.Regex.Replace(line, @"^\s*([*\-•]+|\d+[.)])\s+", "");
                // Strip bold/italic markers.
                line = line.Replace("**", "").Replace("__", "");
                // Collapse leading hashes (headers).
                line = System.Text.RegularExpressions.Regex.Replace(line, @"^#+\s*", "");
                if (line.Length == 0) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(line);
            }
            return sb.ToString().Trim();
        }

        // Trim to the last complete sentence at or before maxLen — never mid-word.
        private string TrimToSentence(string text, int maxLen)
        {
            if (text.Length <= maxLen) return text;
            string slice = text.Substring(0, maxLen);
            int lastStop = slice.LastIndexOfAny(new[] { '.', '!', '?' });
            if (lastStop >= maxLen / 2) return slice.Substring(0, lastStop + 1).Trim();
            int lastSpace = slice.LastIndexOf(' ');
            if (lastSpace > 0) return slice.Substring(0, lastSpace).TrimEnd(',', ';', ':', ' ') + ".";
            return slice.TrimEnd() + ".";
        }

        #endregion

        #region Anthropic Model Utilities

        [System.Serializable]
        public class AnthropicModelsList
        {
            public List<AnthropicModel> data;
        }

        [System.Serializable]
        public class AnthropicModel
        {
            public string id;
            public string type;
        }

        /// <summary>
        /// Lists available Anthropic models for the configured API key and logs them.
        /// </summary>
        [ContextMenu("List Anthropic Models")]
        public void ListAnthropicModelsMenu()
        {
            if (!IsValidAPIKey(anthropicApiKey, "sk-ant-"))
            {
                Debug.LogError("[AEC GPT] Cannot list Anthropic models - invalid or missing API key.");
                return;
            }
            StartCoroutine(ListAnthropicModels());
        }

        private IEnumerator ListAnthropicModels(Action<List<string>> onComplete = null)
        {
            using (UnityWebRequest www = UnityWebRequest.Get("https://api.anthropic.com/v1/models"))
            {
                www.SetRequestHeader("x-api-key", anthropicApiKey);
                www.SetRequestHeader("anthropic-version", "2023-06-01");
                www.timeout = Mathf.Max(8, requestTimeout / 2);

                BIMLog.Verbose("[AEC GPT] Fetching Anthropic models list...");
                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        var modelList = JsonConvert.DeserializeObject<AnthropicModelsList>(www.downloadHandler.text);
                        var ids = modelList?.data?.Select(m => m.id).Where(id => !string.IsNullOrEmpty(id)).ToList() ?? new List<string>();

                        if (ids.Count == 0)
                        {
                            Debug.LogWarning("[AEC GPT] No Anthropic models returned for this API key.");
                        }
                        else
                        {
                            BIMLog.Verbose("[AEC GPT] Anthropic models available (first 20 shown):\n - " + string.Join("\n - ", ids.Take(20)));
                        }
                        onComplete?.Invoke(ids);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[AEC GPT] Failed to parse Anthropic models list: {ex.Message}");
                        onComplete?.Invoke(new List<string>());
                    }
                }
                else
                {
                    Debug.LogError($"[AEC GPT] Failed to list Anthropic models (HTTP {www.responseCode}): {www.error} | {www.downloadHandler?.text}");
                    onComplete?.Invoke(new List<string>());
                }
            }
        }

        /// <summary>
        /// Validates the configured Claude model by checking against the models list.
        /// Logs a helpful message and returns the result via callback.
        /// </summary>
        [ContextMenu("Validate Claude Model ID")]
        public void ValidateClaudeModelIdMenu()
        {
            if (!IsValidAPIKey(anthropicApiKey, "sk-ant-"))
            {
                Debug.LogError("[AEC GPT] Cannot validate model - invalid or missing Anthropic API key.");
                return;
            }
            StartCoroutine(ValidateClaudeModelId());
        }

        private IEnumerator ValidateClaudeModelId(Action<bool, string> onDone = null)
        {
            string configured = GetClaudeModelString();
            bool result = false;
            string message = string.Empty;

            yield return ListAnthropicModels(ids =>
            {
                if (ids != null && ids.Contains(configured))
                {
                    result = true;
                    message = $"Configured Claude model is valid: '{configured}'";
                }
                else
                {
                    result = false;
                    message = $"Configured Claude model NOT found: '{configured}'. Use 'List Anthropic Models' to pick a valid ID.";
                }
            });

            if (result)
                BIMLog.Verbose($"[AEC GPT] ✅ {message}");
            else
                Debug.LogWarning($"[AEC GPT] ⚠️ {message}");

            onDone?.Invoke(result, message);
        }

        #endregion

        #region Text-to-Speech

        private IEnumerator ConvertToSpeechFast(string text)
        {
            if (!enableTextToSpeech || isSpeaking || !IsValidAPIKey(openAIApiKey))
                yield break;

            isSpeaking = true;
            BIMLog.Verbose("[AEC GPT TTS] Starting fast TTS conversion");

            // Early audio source check
            if (audioSource == null || !isAudioSourceReady)
            {
                Debug.LogError("[AEC GPT TTS] Audio source not ready for fast TTS");
                isSpeaking = false;
                yield break;
            }

            if (BIMMR.Inspection.Study.StudySession.RestrictedDataMode) text = BIMMR.Inspection.Study.RestrictedDataFilter.Redact(text); // U14
            string voice = LanguageDetector.IsKorean(text) ? openAITTSVoiceKR : openAITTSVoiceEN;
            var payload = new
            {
                model = openAITTSModel,
                voice = voice,
                input = text,
                response_format = "mp3",
                speed = Mathf.Clamp(speechRate, 0.25f, 4.0f) // OpenAI TTS allows wider range
            };

            string json = JsonConvert.SerializeObject(payload);
            LogOutbound("tts", Encoding.UTF8.GetByteCount(json), json); // U14
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

            using (UnityWebRequest ttsRequest = new UnityWebRequest(OPENAI_TTS_URL, "POST"))
            {
                ttsRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
                ttsRequest.downloadHandler = new DownloadHandlerBuffer();
                ttsRequest.SetRequestHeader("Content-Type", "application/json");
                ttsRequest.SetRequestHeader("Authorization", $"Bearer {openAIApiKey}");

                // Set shorter timeout for faster response
                ttsRequest.timeout = 10;

                yield return ttsRequest.SendWebRequest();

                if (ttsRequest.result == UnityWebRequest.Result.Success)
                {
                    byte[] audioData = ttsRequest.downloadHandler.data;
                    if (audioData != null && audioData.Length > 0)
                    {
                        // Direct audio processing without file I/O
                        yield return StartCoroutine(PlayAudioFromBytesDirectly(audioData));
                    }
                    else
                    {
                        Debug.LogError("[AEC GPT TTS] No audio data received");
                    }
                }
                else
                {
                    Debug.LogError($"[AEC GPT TTS] Fast TTS error: {ttsRequest.error} | {ttsRequest.downloadHandler?.text}");
                }
            }

            isSpeaking = false;
            currentTTSCoroutine = null;

            if (persistentTTS && IsValidAPIKey(openAIApiKey))
                enableTextToSpeech = true;
        }

        private IEnumerator ConvertToSpeech(string text)
        {
            if (!enableTextToSpeech || isSpeaking || !IsValidAPIKey(openAIApiKey))
                yield break;

            isSpeaking = true;

            if (BIMMR.Inspection.Study.StudySession.RestrictedDataMode) text = BIMMR.Inspection.Study.RestrictedDataFilter.Redact(text); // U14
            string voice = LanguageDetector.IsKorean(text) ? openAITTSVoiceKR : openAITTSVoiceEN;
            var payload = new
            {
                model = openAITTSModel,
                voice = voice,
                input = text,
                format = "mp3",
                speed = Mathf.Clamp(speechRate, 0.5f, 1.5f)
            };
            string json = JsonConvert.SerializeObject(payload);
            LogOutbound("tts", Encoding.UTF8.GetByteCount(json), json); // U14

            using (UnityWebRequest ttsRequest = new UnityWebRequest(OPENAI_TTS_URL, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
                ttsRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
                ttsRequest.downloadHandler = new DownloadHandlerBuffer();
                ttsRequest.SetRequestHeader("Content-Type", "application/json");
                ttsRequest.SetRequestHeader("Authorization", $"Bearer {openAIApiKey}");

                yield return ttsRequest.SendWebRequest();

                if (ttsRequest.result == UnityWebRequest.Result.Success)
                {
                    // Response is raw audio bytes for current OpenAI TTS endpoint OR might be base64 depending on API version.
                    byte[] data = ttsRequest.downloadHandler.data;
                    if (data != null && data.Length > 0)
                        yield return StartCoroutine(PlayAudioFromBytes(data));
                }
                else
                {
                    Debug.LogError($"[AEC GPT TTS] OpenAI TTS error: {ttsRequest.error} | {ttsRequest.downloadHandler?.text}");
                }
            }

            isSpeaking = false;
            currentTTSCoroutine = null;
            if (persistentTTS && IsValidAPIKey(openAIApiKey))
                enableTextToSpeech = true;
        }

        // Legacy placeholder to avoid compile errors where CreateSSML was referenced elsewhere
        private string CreateSSML(string text) => text;

        private IEnumerator PlayAudioFromBytesDirectly(byte[] audioBytes)
        {
            if (audioSource == null || audioBytes == null || audioBytes.Length == 0)
            {
                Debug.LogError("[AEC GPT TTS] Invalid audio source or data for direct playback");
                yield break;
            }

            // Create a temporary file with unique name to avoid conflicts
            string tempPath = System.IO.Path.Combine(Application.temporaryCachePath, $"tts_audio_{System.DateTime.Now.Ticks}.mp3");

            // Write audio data to temporary file
            try
            {
                System.IO.File.WriteAllBytes(tempPath, audioBytes);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[AEC GPT TTS] File write error: {ex.Message}");
                yield break;
            }

            // Load audio clip directly using Unity's audio loading
            using (UnityWebRequest audioRequest = UnityWebRequestMultimedia.GetAudioClip($"file://{tempPath}", AudioType.MPEG))
            {
                // Stream so the clip is playable before full decode (cheap on local file://).
                ((DownloadHandlerAudioClip)audioRequest.downloadHandler).streamAudio = true;
                yield return audioRequest.SendWebRequest();

                if (audioRequest.result == UnityWebRequest.Result.Success)
                {
                    AudioClip audioClip = DownloadHandlerAudioClip.GetContent(audioRequest);
                    if (audioClip != null)
                    {
                        audioSource.clip = audioClip;
                        audioSource.Play();
                        BIMLog.Verbose("[AEC GPT TTS] Playing audio");

                        // Tight poll — exits the instant playback ends or is stopped externally.
                        while (audioSource != null && audioSource.isPlaying)
                            yield return null;
                    }
                    else
                    {
                        Debug.LogError("[AEC GPT TTS] Failed to create audio clip from bytes");
                    }
                }
                else
                {
                    Debug.LogError($"[AEC GPT TTS] Audio loading error: {audioRequest.error}");
                }
            }

            // Clean up temporary file
            if (System.IO.File.Exists(tempPath))
            {
                try { System.IO.File.Delete(tempPath); }
                catch (System.Exception ex) { Debug.LogWarning($"[AEC GPT TTS] Failed to delete temp file: {ex.Message}"); }
            }
        }

        private IEnumerator PlayAudioFromBytes(byte[] audioBytes)
        {
            if (audioSource == null) yield break;

            string tempPath = System.IO.Path.Combine(Application.temporaryCachePath, "mr_aec_tts_audio.mp3");
            System.IO.File.WriteAllBytes(tempPath, audioBytes);

            using (UnityWebRequest audioRequest = UnityWebRequestMultimedia.GetAudioClip($"file://{tempPath}", AudioType.MPEG))
            {
                yield return audioRequest.SendWebRequest();

                if (audioRequest.result == UnityWebRequest.Result.Success)
                {
                    AudioClip audioClip = DownloadHandlerAudioClip.GetContent(audioRequest);
                    if (audioClip != null)
                    {
                        audioSource.clip = audioClip;
                        audioSource.volume = speechVolume;
                        audioSource.pitch = 1.0f;
                        audioSource.Play();
                        yield return new WaitForSeconds(audioClip.length + 0.5f);
                    }
                }
            }

            if (System.IO.File.Exists(tempPath))
            {
                try { System.IO.File.Delete(tempPath); } catch { }
            }
        }

        #endregion

        #region Debug and Testing

        [ContextMenu("Test Fast TTS")]
        public void TestFastTTS()
        {
            if (!IsValidAPIKey(openAIApiKey))
            {
                Debug.LogError("[AEC GPT TTS] Cannot test TTS - invalid API key");
                return;
            }

            string testMessage = "This is a test of the fast text-to-speech optimization. The response should start playing immediately.";
            Debug.Log("[AEC GPT TTS] Testing fast TTS with message: " + testMessage);

            if (useFastTTSMode)
                StartCoroutine(ConvertToSpeechFast(testMessage));
            else
                StartCoroutine(ConvertToSpeech(testMessage));
        }

        [ContextMenu("Test Audio Source Ready")]
        public void TestAudioSourceReady()
        {
            Debug.Log($"[AEC GPT TTS] Audio Source Ready: {isAudioSourceReady}");
            Debug.Log($"[AEC GPT TTS] Audio Source: {(audioSource != null ? "Present" : "Missing")}");
            Debug.Log($"[AEC GPT TTS] Fast TTS Mode: {useFastTTSMode}");
            Debug.Log($"[AEC GPT TTS] Pre-Initialize Audio: {preInitializeAudio}");

            if (audioSource != null)
            {
                Debug.Log($"[AEC GPT TTS] Audio Source Volume: {audioSource.volume}");
                Debug.Log($"[AEC GPT TTS] Audio Source Pitch: {audioSource.pitch}");
                Debug.Log($"[AEC GPT TTS] Audio Source Spatial Blend: {audioSource.spatialBlend}");
            }
        }

        [ContextMenu("Re-Initialize Audio")]
        public void ReInitializeAudio()
        {
            Debug.Log("[AEC GPT TTS] Re-initializing audio source...");
            isAudioSourceReady = false;
            PreInitializeAudioSource();
        }

        [ContextMenu("Save Conversation Now")]
        public void SaveConversationNow()
        {
            ManualSaveConversation();
        }

        [ContextMenu("Clear Conversation History")]
        public void ClearConversationNow()
        {
            ClearConversationHistory();
        }

        [ContextMenu("Show Conversation Summary")]
        public void ShowConversationSummary()
        {
            string summary = GetConversationSummary();
            Debug.Log($"[AEC GPT] {summary}");
        }

        [ContextMenu("Open Conversation Logs Folder")]
        public void OpenConversationLogsFolder()
        {
            string directoryPath = System.IO.Path.Combine(Application.persistentDataPath, logDirectory);
            if (System.IO.Directory.Exists(directoryPath))
            {
                System.Diagnostics.Process.Start(directoryPath);
                Debug.Log($"[AEC GPT] Opening logs folder: {directoryPath}");
            }
            else
            {
                Debug.LogWarning($"[AEC GPT] Logs folder doesn't exist yet: {directoryPath}");
            }
        }

        #endregion

        #region Data Structures

        // OpenAI GPT Data Structures
        [System.Serializable]
        public class GPTRequest
        {
            public string model;
            public List<GPTMessage> messages;

            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
            public int max_tokens;

            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
            public int max_completion_tokens;

            public float temperature;

            /// <summary>U7: {"type":"json_object"} for report narratives; omitted otherwise.</summary>
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public object response_format;
        }

        [System.Serializable]
        public class GPTMessage
        {
            public string role;
            public string content;
        }

        [System.Serializable]
        public class GPTResponse
        {
            public List<GPTChoice> choices;
            public GPTUsage usage;
        }

        [System.Serializable]
        public class GPTUsage
        {
            public int? prompt_tokens;
            public int? completion_tokens;
        }

        [System.Serializable]
        public class ClaudeUsage
        {
            public int? input_tokens;
            public int? output_tokens;
        }

        [System.Serializable]
        public class GeminiUsage
        {
            public int? promptTokenCount;
            public int? candidatesTokenCount;
        }

        [System.Serializable]
        public class GPTChoice
        {
            public GPTMessage message;
        }

        [System.Serializable]
        public class GPTErrorResponse
        {
            public GPTError error;
        }

        [System.Serializable]
        public class GPTError
        {
            public string message;
            public string type;
            public string code;
        }

        // Anthropic Claude Data Structures
        [System.Serializable]
        public class ClaudeRequest
        {
            public string model;
            public int max_tokens;
            public float temperature;
            public string system;
            public List<ClaudeMessage> messages;
        }

        [System.Serializable]
        public class ClaudeMessage
        {
            public string role;
            public string content;
        }

        [System.Serializable]
        public class ClaudeResponse
        {
            public string id;
            public string type;
            public string role;
            public List<ClaudeContent> content;
            public string model;
            public string stop_reason;
            public ClaudeUsage usage;
        }

        [System.Serializable]
        public class ClaudeContent
        {
            public string type;
            public string text;
        }

        // Google Gemini Data Structures
        [System.Serializable]
        public class GeminiRequest
        {
            public GeminiSystemInstruction systemInstruction; // Added for proper system prompt support
            public List<GeminiContent> contents;
            public GeminiGenerationConfig generationConfig;
        }

        [System.Serializable]
        public class GeminiSystemInstruction
        {
            public List<GeminiPart> parts;
        }

        [System.Serializable]
        public class GeminiContent
        {
            public List<GeminiPart> parts;
        }

        [System.Serializable]
        public class GeminiPart
        {
            public string text;
        }

        [System.Serializable]
        public class GeminiGenerationConfig
        {
            public float temperature;
            public int maxOutputTokens;
        }

        [System.Serializable]
        public class GeminiResponse
        {
            public List<GeminiCandidate> candidates;
            public GeminiUsage usageMetadata;
        }

        [System.Serializable]
        public class GeminiCandidate
        {
            public GeminiContent content;
            public string finishReason;
        }

        // BIM Data Structures
        [System.Serializable]
        public class BIMElementMetadata
        {
            public string ElementID;
            public string ElementName;
            public string ElementType;
            public string Material;
            public string Manufacturer;
            public string ModelNumber;
            public string Status;
            [NonSerialized] public Dictionary<string, string> Properties;
        }

        // Conversation Logging Data Structure
        [System.Serializable]
        public class ConversationEntry
        {
            public System.DateTime Timestamp;
            public string Role; // "User" or "Assistant"
            public string Message;
            public string Provider; // "OpenAI", "Anthropic", "Google"
            public string Model;
            [NonSerialized] public float? Confidence; // Optional, for voice input
        }

        #endregion

        #region Conversation Logging

        private void InitializeConversationLogging()
        {
            if (!enableConversationLogging) return;

            sessionStartTime = System.DateTime.Now;
            currentSessionId = sessionStartTime.ToString("yyyyMMdd_HHmmss");
            conversationHistory.Clear();

            Debug.Log($"[AEC GPT] Conversation logging initialized. Session ID: {currentSessionId}");
        }

        private void LogUserMessage(string message)
        {
            if (!enableConversationLogging) return;

            var entry = new ConversationEntry
            {
                Timestamp = System.DateTime.Now,
                Role = "User",
                Message = message,
                Provider = selectedProvider.ToString(),
                Model = GetCurrentModelString()
            };

            conversationHistory.Add(entry);
        }

        private void LogAssistantMessage(string message)
        {
            if (!enableConversationLogging) return;

            var entry = new ConversationEntry
            {
                Timestamp = System.DateTime.Now,
                Role = "Assistant",
                Message = message,
                Provider = selectedProvider.ToString(),
                Model = GetCurrentModelString()
            };

            conversationHistory.Add(entry);
        }

        private void SaveConversationLog()
        {
            if (!enableConversationLogging || conversationHistory.Count == 0)
            {
                Debug.Log("[AEC GPT] No conversation to save.");
                return;
            }

            try
            {
                // Create directory if it doesn't exist
                string directoryPath = System.IO.Path.Combine(Application.persistentDataPath, logDirectory);
                if (!System.IO.Directory.Exists(directoryPath))
                {
                    System.IO.Directory.CreateDirectory(directoryPath);
                }

                // Create filename with session ID
                string filename = $"Conversation_{currentSessionId}.txt";
                string fullPath = System.IO.Path.Combine(directoryPath, filename);

                // Build conversation report
                StringBuilder report = new StringBuilder();

                // Header
                report.AppendLine("═══════════════════════════════════════════════════════════════");
                report.AppendLine("             AEC VOICE ASSISTANT - CONVERSATION LOG            ");
                report.AppendLine("═══════════════════════════════════════════════════════════════");
                report.AppendLine();

                report.AppendLine($"Session ID: {currentSessionId}");
                report.AppendLine($"Session Start: {sessionStartTime:yyyy-MM-dd HH:mm:ss}");
                report.AppendLine($"Session End: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");

                TimeSpan duration = System.DateTime.Now - sessionStartTime;
                report.AppendLine($"Session Duration: {duration.Hours}h {duration.Minutes}m {duration.Seconds}s");

                int userMessages = conversationHistory.Count(e => e.Role == "User");
                int assistantMessages = conversationHistory.Count(e => e.Role == "Assistant");
                report.AppendLine($"Total Interactions: {conversationHistory.Count} (User: {userMessages}, Assistant: {assistantMessages})");

                if (includeModelInfo)
                {
                    var providersUsed = conversationHistory.Select(e => e.Provider).Distinct();
                    report.AppendLine($"AI Providers Used: {string.Join(", ", providersUsed)}");
                }

                report.AppendLine();
                report.AppendLine("═══════════════════════════════════════════════════════════════");
                report.AppendLine("                         CONVERSATION                          ");
                report.AppendLine("═══════════════════════════════════════════════════════════════");
                report.AppendLine();

                // Conversation entries
                for (int i = 0; i < conversationHistory.Count; i++)
                {
                    var entry = conversationHistory[i];

                    report.AppendLine("───────────────────────────────────────────────────────────────");

                    if (includeTimestamps)
                    {
                        report.AppendLine($"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss}]");
                    }

                    if (entry.Role == "User")
                    {
                        report.AppendLine($"👤 USER:");
                    }
                    else
                    {
                        if (includeModelInfo)
                        {
                            report.AppendLine($"🤖 ASSISTANT ({entry.Provider} - {entry.Model}):");
                        }
                        else
                        {
                            report.AppendLine($"🤖 ASSISTANT:");
                        }
                    }

                    report.AppendLine();
                    report.AppendLine(WrapText(entry.Message, 60));
                    report.AppendLine();
                }

                report.AppendLine("═══════════════════════════════════════════════════════════════");
                report.AppendLine("                         END OF LOG                            ");
                report.AppendLine("═══════════════════════════════════════════════════════════════");

                // Write to file
                System.IO.File.WriteAllText(fullPath, report.ToString());

                BIMLog.Verbose($"[AEC GPT] Conversation log saved to: {fullPath}");
                BIMLog.Verbose($"[AEC GPT] Total entries saved: {conversationHistory.Count}");

            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[AEC GPT] Failed to save conversation log: {ex.Message}");
            }
        }

        private string WrapText(string text, int maxLineLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLineLength)
                return text;

            StringBuilder wrapped = new StringBuilder();
            string[] words = text.Split(' ');
            int currentLineLength = 0;

            foreach (string word in words)
            {
                if (currentLineLength + word.Length + 1 > maxLineLength)
                {
                    wrapped.AppendLine();
                    currentLineLength = 0;
                }

                if (currentLineLength > 0)
                {
                    wrapped.Append(" ");
                    currentLineLength++;
                }

                wrapped.Append(word);
                currentLineLength += word.Length;
            }

            return wrapped.ToString();
        }

        public void ManualSaveConversation()
        {
            SaveConversationLog();
        }

        public void ClearConversationHistory()
        {
            conversationHistory.Clear();
            InitializeConversationLogging();
            BIMLog.Verbose("[AEC GPT] Conversation history cleared and session restarted.");
        }

        public int GetConversationCount()
        {
            return conversationHistory.Count;
        }

        public string GetConversationSummary()
        {
            if (conversationHistory.Count == 0)
                return "No conversation recorded yet.";

            int userMessages = conversationHistory.Count(e => e.Role == "User");
            int assistantMessages = conversationHistory.Count(e => e.Role == "Assistant");
            TimeSpan duration = System.DateTime.Now - sessionStartTime;

            return $"Session: {currentSessionId} | Duration: {duration.Minutes}m | Exchanges: {userMessages} user, {assistantMessages} assistant";
        }

        #endregion

        private void OnDestroy()
        {
            // Save conversation log before cleanup
            if (saveOnExit && enableConversationLogging)
            {
                SaveConversationLog();
            }

            StopTTS();

            if (voiceBridge != null)
            {
                voiceBridge.OnVoiceInputReceived -= HandleVoiceInput;
                voiceBridge.OnChatGPTVoiceReceived -= HandleChatGPTVoiceInput;
                voiceBridge.OnTTSStopRequested -= StopTTS;
            }
        }

        private void OnApplicationQuit()
        {
            // Also save on application quit as a backup
            if (saveOnExit && enableConversationLogging)
            {
                SaveConversationLog();
            }
        }
    }

    // Shared Language Detection Utility
    public static class LanguageDetector
    {
        public static string DetectLanguage(string text)
        {
            return IsKorean(text) ? "ko" : "en";
        }

        public static bool IsKorean(string text)
        {
            int koreanChars = 0;
            int totalLetters = 0;

            foreach (char c in text)
            {
                if (char.IsLetter(c))
                {
                    totalLetters++;
                    if (IsKoreanCharacter(c))
                        koreanChars++;
                }
            }

            return totalLetters > 0 && (float)koreanChars / totalLetters > 0.3f;
        }

        private static bool IsKoreanCharacter(char c)
        {
            return (c >= 0xAC00 && c <= 0xD7AF) || (c >= 0x1100 && c <= 0x11FF) || (c >= 0x3130 && c <= 0x318F);
        }
    }
}