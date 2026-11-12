// VoiceCommandController.cs - Enhanced Module Spawning Support for AEC Building Systems with OpenAI Whisper Speech-to-Text (replacing Azure)
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;
using UnityEngine.Networking;
using System.Text;
using System.Linq;

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

public class VoiceCommandController : MonoBehaviour
{
    [Header("OpenAI Whisper (Speech-to-Text) Configuration")]
    [SerializeField] private bool useOpenAIWhisper = true; // If true, uses OpenAI Whisper endpoint instead of legacy Azure
    [System.NonSerialized] private string openAIApiKey = ""; // never serialized: read from AEC.VoiceAssistant.ApiKeyProvider
    [SerializeField] private string openAIWhisperModel = "gpt-4o-mini-transcribe"; // Preferred model (fallback to whisper-1 if unsupported)
    [SerializeField] private string openAIWhisperFallbackModel = "whisper-1";
    [SerializeField] private string openAILanguage = ""; // Leave empty for auto-detect. Examples: "en", "ko"
    [SerializeField, Range(0f, 1f)] private float openAIWhisperTemperature = 0.0f; // Usually keep at 0 for deterministic
    [SerializeField] private bool openAIEnableVerbose = false; // If true, request verbose_json (segments)

    [Header("Study: live voice log (U13)")]
    [SerializeField] private bool logLiveVoice = true;
    [Tooltip("dB offset that converts headset-microphone dBFS to dB(A). Measure once against a sound level meter.")]
    [SerializeField] private float micDbfsToDbaOffset = 94f;
    [SerializeField] private bool micLevelCalibrated = false;
    private System.DateTime utteranceStartUtc;
    private System.DateTime utteranceEndUtc;
    private float lastAmbientDba;

    [Header("(Deprecated) Azure Speech Configuration - Kept for fallback / migration")]
    [SerializeField, HideInInspector] private string azureSubscriptionKey1 = "";
    [SerializeField, HideInInspector] private string azureSubscriptionKey2 = "";
    [SerializeField, HideInInspector] private string azureRegion = "koreacentral";
    [SerializeField, HideInInspector] private string azureEndpoint = "https://koreacentral.stt.speech.microsoft.com/";
    [SerializeField, HideInInspector] private string azureLanguage = "ko-KR";
    [SerializeField, HideInInspector] private string azureSecondaryLanguage = "en-US";
    [SerializeField, HideInInspector] private bool useMultiLanguageDetection = true;

    [Header("Meta Quest 3 Audio Settings")]
    [SerializeField] private int questMicrophoneFrequency = 16000; // Optimized for Quest 3 and Azure Speech
    [SerializeField] private float questMicrophoneSensitivity = 0.01f; // Minimum volume threshold for Quest 3
    [SerializeField] private bool useQuestOptimizedSettings = true; // Auto-configure for Quest 3

    [Header("Module Manager Reference")]
    public ModuleManager moduleManager;
    public ModuleSaveLoadManager saveLoadManager;

    [Header("Voice Command Settings")]
    public bool enableVoiceCommands = true;
    public float maxRecordingDuration = 10f;
    public float recordingTimeout = 30f;
    public bool enableDebugLogging = true;
    public bool skipCommandValidation = false;
    public bool testMode = false;

    [Header("Voice Recognition Settings")]
    public float minimumConfidenceScore = 0.7f;
    public bool requireExactMatch = false;
    public bool enableKoreanLanguage = true;
    public bool enableContinuousListening = false;
    public bool restrictToKoreanEnglishOnly = true; // Only allow Korean and English
    public float koreanDetectionThreshold = 0.3f; // Minimum Korean character ratio to consider Korean input

    [Header("AEC Context Settings")]
    [SerializeField] private string aecContextKeyword = "building system";
    [SerializeField] private bool requireContextKeyword = false;

    // === FLOW STATE TRACKING ===
    // Step 1: Voice state management (Quest 3 Microphone)
    private bool isRecording = false;
    private bool isProcessing = false;
    private bool wasButtonPressed = false;
    private bool isListeningContinuously = false;

    // Step 2: Audio recording (Convert to Audio File)
    private AudioClip recordedClip;
    private string microphoneName;
    private byte[] audioFileData; // WAV audio file data

    // Step 3: API communication (Send to STT service)
    private UnityWebRequest currentApiRequest;
    private string azureSpeechApiUrl; // legacy
    private const string OPENAI_WHISPER_URL = "https://api.openai.com/v1/audio/transcriptions";

    // Step 4: Text processing (Receive Text from Azure Speech)
    private string currentTranscription = "";
    private float lastCommandConfidence = 0f;
    private string lastProcessedCommand = "";

    // Step 5: Unity command execution (Trigger Commands)
    private bool lastCommandSuccess = false;

    // Public property for accessing transcription from other scripts
    public string CurrentTranscription
    {
        get { return currentTranscription; }
        private set { currentTranscription = value; }
    }

    // Public property for accessing confidence score from other scripts
    public float LastConfidenceScore
    {
        get { return lastCommandConfidence; }
        private set { lastCommandConfidence = value; }
    }

    // Public property for accessing last processed command from other scripts
    public string LastProcessedCommand
    {
        get { return lastProcessedCommand; }
        private set { lastProcessedCommand = value; }
    }

    // Public event for when new transcription is received
    public event System.Action<string, float> OnTranscriptionReceived;

    // Public event for when command is processed
    public event System.Action<string, bool> OnCommandProcessed;

    // Coroutines
    private Coroutine recordingTimeoutCoroutine;
    private Coroutine continuousListeningCoroutine;

    [Header("Input System")]
    public InputActionReference voiceRecordAction;
    public bool useInputAction = true;

    [Header("UI Feedback")]
    public TMP_Text voiceStatusText;
    public TMP_Text lastCommandText;
    public TMP_Text realTimeTranscriptionText;
    public TMP_Text confidenceScoreText;
    public Button manualRecordButton;
    public Image voiceStatusIndicator;
    public Color listeningColor = Color.green;
    public Color recordingColor = Color.red;
    public Color processingColor = Color.yellow;
    public Color inactiveColor = Color.gray;
    public Color connectedColor = Color.blue;

    [Header("Haptic Feedback")]
    public bool enableHapticFeedback = true;
    public float successHapticIntensity = 0.8f;
    public float successHapticDuration = 0.2f;
    public float failureHapticIntensity = 1.0f;
    public float failureHapticDuration = 0.5f;
    public float recordingHapticIntensity = 0.3f;
    public float recordingHapticDuration = 0.1f;

    private void Start()
    {
        RequestPermissions();
        InitializeVoiceSystem();
        SetupUI();
        SetupInputActions();

        if (enableContinuousListening)
        {
            StartCoroutine(DelayedActivation());
        }
    }

    private void Update()
    {
        HandleInputAction();
        UpdateVoiceStatusIndicator();
    }

    private IEnumerator DelayedActivation()
    {
        yield return new WaitForSeconds(1f);
        StartContinuousListening();
    }

    private void RequestPermissions()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            Permission.RequestUserPermission(Permission.Microphone);
        }
#endif
    }

    private void InitializeVoiceSystem()
    {
        try
        {
            // === STEP 1: Initialize Meta Quest 3 Microphone ===
            if (Microphone.devices.Length > 0)
            {
                microphoneName = Microphone.devices[0];

                // Construct Azure Speech API URL using the CORRECTED endpoint
                if (!azureEndpoint.EndsWith("/"))
                    azureEndpoint += "/";
                azureSpeechApiUrl = azureEndpoint + "speech/recognition/conversation/cognitiveservices/v1";

                if (enableDebugLogging)
                {
                    Debug.Log($"[QUEST 3 VOICE] Initializing microphone: {microphoneName}");
                    Debug.Log($"[QUEST 3 VOICE] Quest optimized settings: {useQuestOptimizedSettings}");
                    Debug.Log($"[QUEST 3 VOICE] Microphone frequency: {questMicrophoneFrequency}Hz");
                    Debug.Log($"[AZURE SPEECH] API URL: {azureSpeechApiUrl}");
                    Debug.Log($"[AZURE SPEECH] Endpoint: {azureEndpoint}");
                    Debug.Log($"[AZURE SPEECH] Region: {azureRegion}");
                    Debug.Log($"[AZURE SPEECH] Primary Language: {azureLanguage}");
                }

                // Apply Quest 3 optimizations
                if (useQuestOptimizedSettings)
                {
                    ApplyQuest3Optimizations();
                }

                UpdateVoiceStatus("✅ Quest 3 → Azure Speech API voice system ready");

                if (string.IsNullOrEmpty(azureSubscriptionKey1))
                {
                    UpdateVoiceStatus("⚠️ Azure subscription key not set - Please add key in inspector");
                }
            }
            else
            {
                UpdateVoiceStatus("❌ No microphone detected on Quest 3");
                Debug.LogError("[QUEST 3 VOICE] No microphone devices found!");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[QUEST 3 VOICE] Failed to initialize voice system: {ex.Message}");
            UpdateVoiceStatus("❌ Voice system initialization failed");
        }
    }

    private void ApplyQuest3Optimizations()
    {
        // Optimize settings specifically for Meta Quest 3 headset microphone with Azure Speech
        if (enableDebugLogging)
        {
            Debug.Log("[QUEST 3 VOICE] Applying Quest 3 microphone optimizations for Azure Speech...");
        }

        // Quest 3 has good built-in noise cancellation, so we can use higher sensitivity
        questMicrophoneSensitivity = 0.005f;

        // Azure Speech works well with 16kHz for balance of quality vs bandwidth
        questMicrophoneFrequency = 16000;

        // Optimize recording duration for VR comfort (shorter sessions)
        maxRecordingDuration = Mathf.Min(maxRecordingDuration, 8f);

        if (enableDebugLogging)
        {
            Debug.Log($"[QUEST 3 VOICE] Optimized sensitivity: {questMicrophoneSensitivity}");
            Debug.Log($"[QUEST 3 VOICE] Optimized frequency: {questMicrophoneFrequency}Hz");
            Debug.Log($"[QUEST 3 VOICE] Optimized duration: {maxRecordingDuration}s");
        }
    }

    private void SetupUI()
    {
        if (manualRecordButton != null)
        {
            manualRecordButton.onClick.AddListener(ToggleManualRecording);
        }

        if (realTimeTranscriptionText != null)
        {
            realTimeTranscriptionText.text = "Azure Speech API ready for AEC commands...";
        }

        if (confidenceScoreText != null)
        {
            confidenceScoreText.text = "Ready";
        }

        UpdateVoiceStatusIndicator();
    }

    private void SetupInputActions()
    {
        if (useInputAction && voiceRecordAction != null)
        {
            voiceRecordAction.action.Enable();
        }
    }

    private void HandleInputAction()
    {
        if (!useInputAction || voiceRecordAction == null) return;

        bool currentButtonPressed = voiceRecordAction.action.IsPressed();

        if (currentButtonPressed && !wasButtonPressed)
        {
            if (!isRecording && !isProcessing)
            {
                StartVoiceRecording();
            }
        }
        else if (!currentButtonPressed && wasButtonPressed)
        {
            if (isRecording)
            {
                StopVoiceRecording();
            }
        }

        wasButtonPressed = currentButtonPressed;
    }

    #region Voice Recording and Processing

    public void StartVoiceRecording()
    {
        if (isRecording || isProcessing) return;

        if (string.IsNullOrEmpty(azureSubscriptionKey1))
        {
            UpdateVoiceStatus("❌ Azure subscription key required");
            return;
        }

        try
        {
            // Use higher quality recording settings optimized for Azure Speech
            int recordingFrequency = 16000; // Azure Speech works well with 16kHz
            recordedClip = Microphone.Start(microphoneName, false, (int)maxRecordingDuration, recordingFrequency);
            isRecording = true;
            isProcessing = false;
            utteranceStartUtc = BIMMR.Inspection.Study.StudySession.Now;

            UpdateVoiceStatus("🎤 Recording AEC command...");
            UpdateRealTimeTranscription("🎤 Recording building system command...");
            TriggerHapticFeedback(recordingHapticIntensity, recordingHapticDuration);

            if (enableDebugLogging)
            {
                Debug.Log($"[VOICE DEBUG] Started recording with microphone: {microphoneName}");
                Debug.Log($"[VOICE DEBUG] Recording frequency: {recordingFrequency}Hz, Max duration: {maxRecordingDuration}s");
            }

            if (recordingTimeoutCoroutine != null)
                StopCoroutine(recordingTimeoutCoroutine);
            recordingTimeoutCoroutine = StartCoroutine(AutoStopRecording());
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to start recording: {ex.Message}");
            UpdateVoiceStatus("❌ Recording failed - check microphone");
            isRecording = false;
        }
    }

    private IEnumerator AutoStopRecording()
    {
        yield return new WaitForSeconds(maxRecordingDuration);
        if (isRecording)
        {
            StopVoiceRecording();
        }
    }

    public void StopVoiceRecording()
    {
        if (!isRecording) return;

        try
        {
            // Get the actual recording position to trim silence
            int recordingPosition = Microphone.GetPosition(microphoneName);
            Microphone.End(microphoneName);

            if (enableDebugLogging)
            {
                Debug.Log($"[VOICE DEBUG] Stopped recording. Position: {recordingPosition}, Clip length: {recordedClip.samples}");
            }

            // Trim the audio clip to actual recorded length
            if (recordingPosition > 0 && recordingPosition < recordedClip.samples)
            {
                float[] originalSamples = new float[recordedClip.samples * recordedClip.channels];
                recordedClip.GetData(originalSamples, 0);

                // Create trimmed clip
                AudioClip trimmedClip = AudioClip.Create("TrimmedRecording", recordingPosition, recordedClip.channels, recordedClip.frequency, false);
                float[] trimmedSamples = new float[recordingPosition * recordedClip.channels];
                System.Array.Copy(originalSamples, trimmedSamples, trimmedSamples.Length);
                trimmedClip.SetData(trimmedSamples, 0);

                recordedClip = trimmedClip;

                if (enableDebugLogging)
                {
                    Debug.Log($"[VOICE DEBUG] Trimmed audio to {recordingPosition} samples ({(float)recordingPosition / recordedClip.frequency:F2}s)");
                }
            }

            isRecording = false;
            isProcessing = true;

            // U13: end of utterance and ambient level from the microphone noise floor.
            utteranceEndUtc = BIMMR.Inspection.Study.StudySession.Now;
            if (logLiveVoice && recordedClip != null)
            {
                var levelSamples = new float[recordedClip.samples * recordedClip.channels];
                recordedClip.GetData(levelSamples, 0);
                lastAmbientDba = BIMMR.Inspection.Study.VoiceLiveLog.NoiseFloorDbfs(
                    levelSamples, recordedClip.frequency, recordedClip.channels) + micDbfsToDbaOffset;
            }

            UpdateVoiceStatus(useOpenAIWhisper ? "📡 Processing with OpenAI Whisper..." : "📡 Processing with Azure Speech API...");
            TriggerHapticFeedback(recordingHapticIntensity, recordingHapticDuration);

            if (recordingTimeoutCoroutine != null)
            {
                StopCoroutine(recordingTimeoutCoroutine);
                recordingTimeoutCoroutine = null;
            }

            if (useOpenAIWhisper)
                StartCoroutine(ProcessAudioWithOpenAIWhisper());
            else
                StartCoroutine(ProcessAudioWithAzureSpeech());
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to stop recording: {ex.Message}");
            UpdateVoiceStatus("❌ Recording processing failed");
            isRecording = false;
            isProcessing = false;
        }
    }

    // === OpenAI Whisper Processing ===
    private IEnumerator ProcessAudioWithOpenAIWhisper()
    {
        if (recordedClip == null)
        {
            UpdateVoiceStatus("❌ [STEP 2] No audio recorded");
            isProcessing = false;
            yield break;
        }

        UpdateRealTimeTranscription("📡 [STEP 3] Preparing audio for OpenAI Whisper...");

        audioFileData = ConvertAudioClipToWAV(recordedClip);
        if (audioFileData == null || audioFileData.Length == 0)
        {
            UpdateVoiceStatus("❌ [STEP 3] Audio conversion failed");
            isProcessing = false;
            yield break;
        }

        // Sanitize & validate API key (environment variable or untracked config.json)
        if (string.IsNullOrEmpty(openAIApiKey)) openAIApiKey = AEC.VoiceAssistant.ApiKeyProvider.OpenAI;
        if (!string.IsNullOrEmpty(openAIApiKey))
        {
            string cleaned = SanitizeAPIKey(openAIApiKey);
            if (cleaned != openAIApiKey)
            {
                if (enableDebugLogging)
                    Debug.Log("[WHISPER] API key sanitized (whitespace/hidden chars removed)");
                openAIApiKey = cleaned;
            }
        }

        if (!IsValidAPIKey(openAIApiKey))
        {
            UpdateVoiceStatus("❌ Invalid / Missing API key");
            UpdateRealTimeTranscription("❌ Provide valid OpenAI API key (starts with sk-)");
            isProcessing = false;
            yield break;
        }

        string modelToUse = openAIWhisperModel;
        if (string.IsNullOrEmpty(modelToUse)) modelToUse = "gpt-4o-mini-transcribe";

        // Build multipart form
        var formSections = new List<IMultipartFormSection>();
        formSections.Add(new MultipartFormFileSection("file", audioFileData, "recording.wav", "audio/wav"));
        formSections.Add(new MultipartFormDataSection("model", modelToUse));
        if (!string.IsNullOrEmpty(openAILanguage)) formSections.Add(new MultipartFormDataSection("language", openAILanguage));
        if (openAIWhisperTemperature > 0f) formSections.Add(new MultipartFormDataSection("temperature", openAIWhisperTemperature.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        if (openAIEnableVerbose) formSections.Add(new MultipartFormDataSection("response_format", "verbose_json"));
        else formSections.Add(new MultipartFormDataSection("response_format", "json"));

        UnityWebRequest whisperRequest = UnityWebRequest.Post(OPENAI_WHISPER_URL, formSections);
        whisperRequest.timeout = (int)Mathf.Clamp(recordingTimeout, 10f, 120f);
        // U14: only the audio clip leaves the device for speech-to-text.
        BIMMR.Inspection.Study.OutboundPayloadLog.Write("stt", audioFileData.Length, false);
        whisperRequest.SetRequestHeader("Authorization", $"Bearer {openAIApiKey}");

        if (enableDebugLogging)
        {
            Debug.Log($"[WHISPER] Sending audio ({audioFileData.Length} bytes) to OpenAI model '{modelToUse}' with key {MaskAPIKey(openAIApiKey)}");
        }

        yield return whisperRequest.SendWebRequest();

        if (whisperRequest.result == UnityWebRequest.Result.Success)
        {
            if (enableDebugLogging)
                Debug.Log($"[WHISPER] Raw Response: {whisperRequest.downloadHandler.text}");

            UpdateRealTimeTranscription("📥 [STEP 4] Processing OpenAI Whisper response...");
            ProcessOpenAIWhisperTextResponse(whisperRequest.downloadHandler.text);
        }
        else
        {
            string err = whisperRequest.error;
            string body = whisperRequest.downloadHandler?.text;
            Debug.LogError($"[WHISPER] ❌ STT Error: {err} | Body: {body}");
            UpdateVoiceStatus("❌ OpenAI Whisper error");
            UpdateRealTimeTranscription("❌ Whisper transcription failed");
            TriggerHapticFeedback(failureHapticIntensity, failureHapticDuration);

            // Simple fallback: try fallback model once if different
            if (modelToUse != openAIWhisperFallbackModel && !string.IsNullOrEmpty(openAIWhisperFallbackModel))
            {
                if (enableDebugLogging)
                    Debug.Log("[WHISPER] Attempting fallback model...");
                openAIWhisperModel = openAIWhisperFallbackModel;
                yield return new WaitForSeconds(0.5f);
                yield return StartCoroutine(ProcessAudioWithOpenAIWhisper());
                yield break;
            }
        }

        whisperRequest.Dispose();
        isProcessing = false;

        if (enableContinuousListening && isListeningContinuously && !isRecording)
        {
            yield return new WaitForSeconds(1.5f);
            if (!isRecording && !isProcessing)
                StartVoiceRecording();
        }
    }

    // === LEGACY AZURE PATH (Kept for backward compatibility) ===
    private IEnumerator ProcessAudioWithAzureSpeech()
    {
        if (useOpenAIWhisper)
        {
            yield return ProcessAudioWithOpenAIWhisper();
            yield break;
        }
        if (recordedClip == null)
        {
            UpdateVoiceStatus("❌ [STEP 2] No audio recorded");
            isProcessing = false;
            yield break;
        }

        if (enableDebugLogging)
        {
            Debug.Log("[STEP 3] 📡 SENDING TO AZURE SPEECH API...");
        }

        UpdateRealTimeTranscription("📡 [STEP 3] Converting Quest 3 audio to WAV file...");

        // Convert Quest 3 AudioClip to WAV file format
        audioFileData = ConvertAudioClipToWAV(recordedClip);

        if (audioFileData == null || audioFileData.Length == 0)
        {
            UpdateVoiceStatus("❌ [STEP 3] Audio file conversion failed");
            isProcessing = false;
            yield break;
        }

        if (enableDebugLogging)
        {
            Debug.Log($"[STEP 3] Audio file created - Size: {audioFileData.Length} bytes");
            Debug.Log($"[STEP 3] Sending WAV file to Azure Speech API...");
        }

        UpdateRealTimeTranscription("📡 [STEP 3] Sending audio file to Azure Speech API...");

        // Prepare Azure Speech API URL with parameters
        string apiUrl = azureSpeechApiUrl;

        // Add language and format parameters
        if (useMultiLanguageDetection)
        {
            // Use language auto-detection for Korean/English
            apiUrl += $"?language={azureLanguage}&language={azureSecondaryLanguage}&format=detailed&profanity=raw";
        }
        else
        {
            apiUrl += $"?language={azureLanguage}&format=detailed&profanity=raw";
        }

        if (enableDebugLogging)
        {
            Debug.Log($"[STEP 3] Final API URL: {apiUrl}");
        }

        // Create request to Azure Speech API
        currentApiRequest = new UnityWebRequest(apiUrl, "POST");
        currentApiRequest.uploadHandler = new UploadHandlerRaw(audioFileData);
        currentApiRequest.downloadHandler = new DownloadHandlerBuffer();

        // Validate API key before making request
        if (string.IsNullOrEmpty(azureSubscriptionKey1))
        {
            Debug.LogError("[STEP 3] ❌ Azure Subscription Key 1 is empty! Please set it in the Inspector.");
            UpdateVoiceStatus("❌ [STEP 3] API key not configured");
            yield break;
        }

        if (azureSubscriptionKey1.Length != 32)
        {
            Debug.LogWarning($"[STEP 3] ⚠️ Azure key length ({azureSubscriptionKey1.Length}) seems unusual. Standard keys are 32 characters.");
        }

        // Set required headers for Azure Speech API
        currentApiRequest.SetRequestHeader("Ocp-Apim-Subscription-Key", azureSubscriptionKey1);
        currentApiRequest.SetRequestHeader("Content-Type", "audio/wav");
        currentApiRequest.SetRequestHeader("Accept", "application/json");

        currentApiRequest.timeout = (int)recordingTimeout;

        if (enableDebugLogging)
        {
            Debug.Log($"[STEP 3] API Request configured - Language: {azureLanguage}, Multi-language: {useMultiLanguageDetection}");
        }

        yield return currentApiRequest.SendWebRequest();

        if (enableDebugLogging)
        {
            Debug.Log("[STEP 4] 📥 RECEIVING TEXT FROM AZURE SPEECH API...");
        }

        if (currentApiRequest.result == UnityWebRequest.Result.Success)
        {
            if (enableDebugLogging)
            {
                Debug.Log($"[STEP 4] Azure Speech API Response received successfully");
                Debug.Log($"[STEP 4] Raw JSON Response: {currentApiRequest.downloadHandler.text}");
            }

            UpdateRealTimeTranscription("📥 [STEP 4] Receiving text from Azure Speech API...");

            // Process the text response and trigger Unity commands (Step 5)
            ProcessAzureSpeechTextResponse(currentApiRequest.downloadHandler.text);
        }
        else
        {
            string errorMessage = $"[STEP 4] Azure Speech API Error: {currentApiRequest.error}";
            string responseCode = currentApiRequest.responseCode.ToString();
            string responseText = currentApiRequest.downloadHandler?.text ?? "No response text";

            // Enhanced error logging
            if (!string.IsNullOrEmpty(responseText))
            {
                errorMessage += $" - Response: {responseText}";
            }

            Debug.LogError($"=== AZURE SPEECH API ERROR DETAILS ===");
            Debug.LogError($"Error: {currentApiRequest.error}");
            Debug.LogError($"Response Code: {responseCode}");
            Debug.LogError($"Response Text: {responseText}");
            Debug.LogError($"Request URL: {apiUrl}");
            Debug.LogError($"Audio File Size: {audioFileData?.Length ?? 0} bytes");
            Debug.LogError($"Subscription Key Length: {azureSubscriptionKey1?.Length ?? 0}");
            Debug.LogError($"Region: {azureRegion}");

            // Try with backup key if primary fails
            if (!string.IsNullOrEmpty(azureSubscriptionKey2) && responseCode == "401")
            {
                BIMLog.Verbose("[STEP 3] Retrying with backup Azure subscription key...");
                yield return StartCoroutine(RetryWithBackupKey());
            }
            else
            {
                UpdateVoiceStatus($"❌ [STEP 4] API Error {responseCode} - check console for details");
                UpdateRealTimeTranscription($"❌ [STEP 4] API Error {responseCode}");
                TriggerHapticFeedback(failureHapticIntensity, failureHapticDuration);
            }
        }

        if (currentApiRequest != null)
        {
            currentApiRequest.Dispose();
            currentApiRequest = null;
        }

        isProcessing = false;

        // Resume continuous listening if enabled
        if (enableContinuousListening && isListeningContinuously)
        {
            yield return new WaitForSeconds(2f);
            if (!isRecording && !isProcessing)
            {
                StartVoiceRecording();
            }
        }
    }

    private IEnumerator RetryWithBackupKey()
    {
        // Prepare retry request with backup key
        string apiUrl = azureSpeechApiUrl;

        if (useMultiLanguageDetection)
        {
            apiUrl += $"?language={azureLanguage}&language={azureSecondaryLanguage}&format=detailed&profanity=raw";
        }
        else
        {
            apiUrl += $"?language={azureLanguage}&format=detailed&profanity=raw";
        }

        UnityWebRequest retryRequest = new UnityWebRequest(apiUrl, "POST");
        retryRequest.uploadHandler = new UploadHandlerRaw(audioFileData);
        retryRequest.downloadHandler = new DownloadHandlerBuffer();

        // Use backup subscription key
        retryRequest.SetRequestHeader("Ocp-Apim-Subscription-Key", azureSubscriptionKey2);
        retryRequest.SetRequestHeader("Content-Type", "audio/wav");
        retryRequest.SetRequestHeader("Accept", "application/json");

        retryRequest.timeout = (int)recordingTimeout;

        yield return retryRequest.SendWebRequest();

        if (retryRequest.result == UnityWebRequest.Result.Success)
        {
            if (enableDebugLogging)
            {
                Debug.Log($"[STEP 4] Azure Speech API Response received with backup key");
                Debug.Log($"[STEP 4] Raw JSON Response: {retryRequest.downloadHandler.text}");
            }

            UpdateRealTimeTranscription("📥 [STEP 4] Receiving text from Azure Speech API (backup key)...");
            ProcessAzureSpeechTextResponse(retryRequest.downloadHandler.text);
        }
        else
        {
            Debug.LogError($"[STEP 4] Backup key also failed: {retryRequest.error}");
            UpdateVoiceStatus($"❌ [STEP 4] Both API keys failed - check configuration");
            UpdateRealTimeTranscription($"❌ [STEP 4] API connection failed");
            TriggerHapticFeedback(failureHapticIntensity, failureHapticDuration);
        }

        retryRequest.Dispose();
    }

    // === STEP 5: TRIGGER COMMANDS IN UNITY ===
    private void ProcessAzureSpeechTextResponse(string jsonResponse)
    {
        try
        {
            if (enableDebugLogging)
            {
                Debug.Log("[STEP 5] ⚡ TRIGGERING UNITY COMMANDS...");
            }

            // Parse Azure Speech API JSON response to extract text
            AzureSpeechResponse response = JsonUtility.FromJson<AzureSpeechResponse>(jsonResponse);

            if (response != null && !string.IsNullOrEmpty(response.DisplayText))
            {
                string transcription = response.DisplayText.Trim();
                currentTranscription = transcription;
                LogLiveUtterance(transcription,
                    response.NBest != null && response.NBest.Length > 0 && response.NBest[0] != null ? response.NBest[0].Confidence : 1f,
                    "azure");

                // Extract confidence score if available
                float confidence = 1.0f;
                if (response.NBest != null && response.NBest.Length > 0 && response.NBest[0] != null)
                {
                    confidence = response.NBest[0].Confidence;
                }
                lastCommandConfidence = confidence;

                // Fire event for other scripts to access transcription
                OnTranscriptionReceived?.Invoke(transcription, confidence);

                if (enableDebugLogging)
                {
                    Debug.Log($"[STEP 5] Text extracted from Azure Speech: \"{transcription}\"");
                    Debug.Log($"[STEP 5] Confidence score: {confidence:F2}");
                }

                UpdateRealTimeTranscription($"📝 [STEP 4] Text received: \"{transcription}\"");

                if (confidenceScoreText != null)
                {
                    confidenceScoreText.text = $"[STEP 4] \"{transcription}\" (Confidence: {confidence:F2})";
                }

                // Skip validation in test mode to see all transcriptions
                if (skipCommandValidation || testMode)
                {
                    UpdateVoiceStatus($"✅ [TEST] Quest 3 → Azure Speech → Unity: \"{transcription}\"");
                    UpdateLastCommand($"Azure Test: {transcription}");
                    TriggerHapticFeedback(successHapticIntensity, successHapticDuration);

                    if (enableDebugLogging)
                    {
                        Debug.Log($"[STEP 5] Test mode - skipping command validation");
                    }
                    return;
                }

                UpdateRealTimeTranscription($"⚡ [STEP 5] Processing Unity command: \"{transcription}\"");

                // Check confidence threshold
                if (confidence < minimumConfidenceScore)
                {
                    if (enableDebugLogging)
                    {
                        Debug.Log($"[STEP 5] Confidence too low: {confidence:F2} < {minimumConfidenceScore:F2}");
                    }

                    UpdateVoiceStatus($"❌ [STEP 5] Low confidence: {confidence:F2} - please speak clearer");
                    UpdateRealTimeTranscription($"❌ [STEP 5] Confidence too low: {confidence:F2}");
                    TriggerHapticFeedback(failureHapticIntensity, failureHapticDuration);
                    return;
                }

                // Validate language before processing commands
                if (restrictToKoreanEnglishOnly && !IsKoreanOrEnglishText(transcription))
                {
                    if (enableDebugLogging)
                    {
                        Debug.Log($"[STEP 5] Rejected non-Korean/English text: \"{transcription}\"");
                    }

                    UpdateVoiceStatus($"❌ [STEP 5] Language not supported: \"{transcription}\"");
                    UpdateRealTimeTranscription($"❌ [STEP 5] Only Korean/English supported");
                    TriggerHapticFeedback(failureHapticIntensity, failureHapticDuration);
                    return;
                }

                // Validate and execute Unity commands for AEC building systems
                if (IsValidAECCommand(transcription))
                {
                    if (enableDebugLogging)
                    {
                        Debug.Log($"[STEP 5] Valid AEC building command detected");
                    }

                    lastCommandSuccess = ExecuteUnityCommand(transcription);

                    // Fire event for other scripts
                    OnCommandProcessed?.Invoke(transcription, lastCommandSuccess);

                    if (lastCommandSuccess)
                    {
                        UpdateLastCommand($"Quest 3 → Azure Speech → Unity: {transcription}");
                        TriggerHapticFeedback(successHapticIntensity, successHapticDuration);
                        UpdateVoiceStatus($"✅ [STEP 5] Unity command executed: \"{transcription}\"");
                        UpdateRealTimeTranscription($"✅ [STEP 5] SUCCESS: \"{transcription}\"");

                        if (enableDebugLogging)
                        {
                            Debug.Log($"[STEP 5] Unity command executed successfully");
                        }
                    }
                    else
                    {
                        UpdateLastCommand($"Failed: {transcription}");
                        TriggerHapticFeedback(failureHapticIntensity, failureHapticDuration);
                        UpdateVoiceStatus($"❌ [STEP 5] Unity command failed: \"{transcription}\"");
                        UpdateRealTimeTranscription($"❌ [STEP 5] FAILED: \"{transcription}\"");

                        if (enableDebugLogging)
                        {
                            Debug.Log($"[STEP 5] Unity command execution failed");
                        }
                    }
                }
                else
                {
                    if (enableDebugLogging)
                    {
                        Debug.Log($"[STEP 5] Invalid AEC command: \"{transcription}\"");
                        Debug.Log($"[STEP 5] Try commands like: 'hide architecture', 'show structural', 'spawn module m1-1'");
                    }

                    UpdateVoiceStatus($"❌ [STEP 5] Invalid building command: \"{transcription}\"");
                    UpdateRealTimeTranscription($"❌ [STEP 5] Try: 'spawn m1-1' or 'show structural'");
                    TriggerHapticFeedback(failureHapticIntensity, failureHapticDuration);
                }
            }
            else
            {
                if (enableDebugLogging)
                {
                    Debug.Log($"[STEP 5] No text received from Azure Speech API");
                }

                UpdateVoiceStatus("❌ [STEP 4] No text received from Azure Speech");
                UpdateRealTimeTranscription("❌ [STEP 4] No speech detected - try speaking louder");
                TriggerHapticFeedback(failureHapticIntensity, failureHapticDuration);
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[STEP 5] Error processing Azure Speech text response: {ex.Message}");
            Debug.LogError($"[STEP 5] Raw JSON response: {jsonResponse}");
            UpdateVoiceStatus("❌ [STEP 5] Text processing failed");
            TriggerHapticFeedback(failureHapticIntensity, failureHapticDuration);
        }
    }

    // === OpenAI Whisper Text Response Processing ===
    [Serializable] private class OpenAIWhisperBasicResponse { public string text; }
    [Serializable] private class OpenAIWhisperVerboseSegment { public string text; public float start; public float end; }
    [Serializable] private class OpenAIWhisperVerboseResponse { public string text; public OpenAIWhisperVerboseSegment[] segments; }

    private void ProcessOpenAIWhisperTextResponse(string jsonResponse)
    {
        try
        {
            if (enableDebugLogging)
                Debug.Log("[WHISPER] Parsing transcription response");

            string transcription = null;

            if (openAIEnableVerbose)
            {
                try
                {
                    var verbose = JsonUtility.FromJson<OpenAIWhisperVerboseResponse>(jsonResponse);
                    if (verbose != null && !string.IsNullOrEmpty(verbose.text))
                        transcription = verbose.text.Trim();
                }
                catch { }
            }

            if (string.IsNullOrEmpty(transcription))
            {
                try
                {
                    var basic = JsonUtility.FromJson<OpenAIWhisperBasicResponse>(jsonResponse);
                    if (basic != null && !string.IsNullOrEmpty(basic.text))
                        transcription = basic.text.Trim();
                }
                catch { }
            }

            if (string.IsNullOrEmpty(transcription))
            {
                UpdateVoiceStatus("❌ Whisper parse error");
                UpdateRealTimeTranscription("❌ Could not parse Whisper response");
                if (enableDebugLogging) Debug.LogError($"[WHISPER] Parse failure: {jsonResponse}");
                return;
            }

            currentTranscription = transcription;
            lastCommandConfidence = 1.0f; // Whisper basic endpoint lacks explicit confidence
            LogLiveUtterance(transcription, lastCommandConfidence, "openai");
            OnTranscriptionReceived?.Invoke(transcription, lastCommandConfidence);

            UpdateRealTimeTranscription($"📝 [STEP 4] Text received: \"{transcription}\" (Whisper)");
            if (confidenceScoreText != null)
                confidenceScoreText.text = $"[STEP 4] \"{transcription}\" (Whisper)";

            // Reuse command validation logic: mimic Azure path (confidence always high)
            if (skipCommandValidation || testMode)
            {
                UpdateVoiceStatus($"✅ [TEST] Whisper → Unity: \"{transcription}\"");
                UpdateLastCommand($"Whisper Test: {transcription}");
                TriggerHapticFeedback(successHapticIntensity, successHapticDuration);
                return;
            }

            if (restrictToKoreanEnglishOnly && !IsKoreanOrEnglishText(transcription))
            {
                UpdateVoiceStatus($"❌ [STEP 5] Language not supported: \"{transcription}\"");
                UpdateRealTimeTranscription("❌ [STEP 5] Only Korean/English supported");
                TriggerHapticFeedback(failureHapticIntensity, failureHapticDuration);
                return;
            }

            if (IsValidAECCommand(transcription))
            {
                lastCommandSuccess = ExecuteUnityCommand(transcription);
                OnCommandProcessed?.Invoke(transcription, lastCommandSuccess);
                if (lastCommandSuccess)
                {
                    UpdateLastCommand($"Whisper → Unity: {transcription}");
                    TriggerHapticFeedback(successHapticIntensity, successHapticDuration);
                    UpdateVoiceStatus($"✅ [STEP 5] Unity command executed: \"{transcription}\"");
                    UpdateRealTimeTranscription($"✅ [STEP 5] SUCCESS: \"{transcription}\"");
                }
                else
                {
                    UpdateLastCommand($"Failed: {transcription}");
                    TriggerHapticFeedback(failureHapticIntensity, failureHapticDuration);
                    UpdateVoiceStatus($"❌ [STEP 5] Unity command failed: \"{transcription}\"");
                    UpdateRealTimeTranscription($"❌ [STEP 5] FAILED: \"{transcription}\"");
                }
            }
            else
            {
                UpdateVoiceStatus($"❌ [STEP 5] Invalid building command: \"{transcription}\"");
                UpdateRealTimeTranscription("❌ [STEP 5] Try: 'spawn m1-1' or 'show structural'");
                TriggerHapticFeedback(failureHapticIntensity, failureHapticDuration);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[WHISPER] Error processing Whisper response: {ex.Message}");
            UpdateVoiceStatus("❌ Whisper processing error");
        }
    }

    /// <summary>U13: one voice_live.csv row per live utterance. Latency runs from the end of
    /// the utterance to the transcript being available for command parsing.</summary>
    private void LogLiveUtterance(string transcription, float confidence, string endpoint)
    {
        if (!logLiveVoice) return;
        double latencyMs = (BIMMR.Inspection.Study.StudySession.Now - utteranceEndUtc).TotalMilliseconds;
        string intent = BIMMR.Inspection.Study.VoiceLiveLog.ClassifyIntent(transcription, IsValidAECCommand(transcription));
        BIMMR.Inspection.Study.VoiceLiveLog.Write(utteranceStartUtc, transcription, intent, confidence,
                                                  lastAmbientDba, micLevelCalibrated, latencyMs, endpoint);
    }

    // Execute the actual Unity command for building systems
    private bool ExecuteUnityCommand(string transcription)
    {
        if (enableDebugLogging)
        {
            Debug.Log($"[STEP 5] Executing Unity command for: \"{transcription}\"");
        }

        // Store the processed command for external access
        lastProcessedCommand = transcription;

        // Use the existing AEC command processing system
        return ProcessAECVoiceCommand(transcription);
    }

    private byte[] ConvertAudioClipToWAV(AudioClip clip)
    {
        if (clip == null) return null;

        try
        {
            float[] samples = new float[clip.samples * clip.channels];
            clip.GetData(samples, 0);

            // Convert to WAV format - Azure Speech requires specific WAV format
            return ConvertSamplesToWAV(samples, clip.frequency, clip.channels);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Audio conversion failed: {ex.Message}");
            return null;
        }
    }

    private byte[] ConvertSamplesToWAV(float[] samples, int frequency, int channels)
    {
        byte[] wav = new byte[44 + samples.Length * 2];

        // WAV header - Required format for Azure Speech API
        System.Text.Encoding.ASCII.GetBytes("RIFF").CopyTo(wav, 0);
        System.BitConverter.GetBytes(wav.Length - 8).CopyTo(wav, 4);
        System.Text.Encoding.ASCII.GetBytes("WAVE").CopyTo(wav, 8);
        System.Text.Encoding.ASCII.GetBytes("fmt ").CopyTo(wav, 12);
        System.BitConverter.GetBytes(16).CopyTo(wav, 16);
        System.BitConverter.GetBytes((short)1).CopyTo(wav, 20);
        System.BitConverter.GetBytes((short)channels).CopyTo(wav, 22);
        System.BitConverter.GetBytes(frequency).CopyTo(wav, 24);
        System.BitConverter.GetBytes(frequency * channels * 2).CopyTo(wav, 28);
        System.BitConverter.GetBytes((short)(channels * 2)).CopyTo(wav, 32);
        System.BitConverter.GetBytes((short)16).CopyTo(wav, 34);
        System.Text.Encoding.ASCII.GetBytes("data").CopyTo(wav, 36);
        System.BitConverter.GetBytes(samples.Length * 2).CopyTo(wav, 40);

        // Convert samples to 16-bit PCM
        for (int i = 0; i < samples.Length; i++)
        {
            short sample = (short)(samples[i] * 32767f);
            System.BitConverter.GetBytes(sample).CopyTo(wav, 44 + i * 2);
        }

        return wav;
    }

    #endregion

    #region AEC Command Processing

    private bool IsValidAECCommand(string transcription)
    {
        if (string.IsNullOrEmpty(transcription)) return false;

        string command = transcription.ToLower().Trim();

        if (enableDebugLogging)
        {
            Debug.Log($"[VOICE DEBUG] === VALIDATING AEC COMMAND ===");
            Debug.Log($"[VOICE DEBUG] Input: '{command}'");
        }

        // Check for AEC context keyword if required
        if (requireContextKeyword && !string.IsNullOrEmpty(aecContextKeyword))
        {
            if (!command.Contains(aecContextKeyword.ToLower()))
            {
                BIMLog.Verbose($"AEC context keyword '{aecContextKeyword}' not found in: '{command}'");
                return false;
            }
        }

        // Check if command contains any valid action keywords
        bool hasValidAction = false;
        foreach (var actionKeyword in VoiceCommandKeywords.ActionKeywords.Keys)
        {
            if (ContainsExactWord(command, actionKeyword))
            {
                hasValidAction = true;
                if (enableDebugLogging)
                {
                    Debug.Log($"[VOICE DEBUG] Found action: '{actionKeyword}'");
                }
                break;
            }
        }

        if (!hasValidAction)
        {
            if (enableDebugLogging)
            {
                Debug.Log($"[VOICE DEBUG] No valid AEC action found in: '{command}'");
            }
            return false;
        }

        // Check if command contains valid target (building component or module)
        bool hasValidTarget = HasValidAECTarget(command);

        if (!hasValidTarget)
        {
            if (enableDebugLogging)
            {
                Debug.Log($"[VOICE DEBUG] No valid AEC target found in: '{command}'");
            }
            return false;
        }

        if (enableDebugLogging)
        {
            Debug.Log($"[VOICE DEBUG] ✅ Valid AEC command detected: '{command}'");
        }
        return true;
    }

    private bool HasValidAECTarget(string command)
    {
        if (enableDebugLogging)
        {
            Debug.Log($"[VOICE DEBUG] Checking AEC targets in: '{command}'");
        }

        // Check for building system components
        foreach (var componentKeywords in VoiceCommandKeywords.BuildingSystemKeywords.Values)
        {
            foreach (var keyword in componentKeywords)
            {
                if (ContainsExactWord(command, keyword))
                {
                    if (enableDebugLogging)
                    {
                        Debug.Log($"[VOICE DEBUG] Found building component keyword: '{keyword}'");
                    }
                    return true;
                }
            }
        }

        // Check for prefabricated modules
        foreach (var moduleKeywords_list in VoiceCommandKeywords.ModuleKeywords.Values)
        {
            foreach (var keyword in moduleKeywords_list)
            {
                if (ContainsExactWord(command, keyword))
                {
                    if (enableDebugLogging)
                    {
                        Debug.Log($"[VOICE DEBUG] Found module keyword: '{keyword}'");
                    }
                    return true;
                }
            }
        }

        // Enhanced module pattern detection
        var modulePatterns = new[]
        {
            @"module\s*\d+[-\s]\d+",     // "module 1-2" or "module 1 2"
            @"m\s*\d+\s*[-_]\s*\d+",     // "m1-1" or "m 1 1"
            @"unit\s*\d+[-\s]\d+",       // "unit 1-2"
            @"\b\d+[-\s]\d+\b"           // "1-2" or "1 2" as standalone
        };

        foreach (var pattern in modulePatterns)
        {
            if (Regex.IsMatch(command, pattern, RegexOptions.IgnoreCase))
            {
                if (enableDebugLogging)
                {
                    Debug.Log($"[VOICE DEBUG] Found module pattern: '{pattern}' in '{command}'");
                }
                return true;
            }
        }

        // Check if it mentions actual module names from ModuleManager
        if (moduleManager != null)
        {
            var availableModules = moduleManager.GetAvailableModuleNames();
            foreach (var moduleName in availableModules)
            {
                if (ContainsExactWord(command, moduleName.ToLower()) ||
                    command.Contains(moduleName.ToLower()))
                {
                    if (enableDebugLogging)
                    {
                        Debug.Log($"[VOICE DEBUG] Found actual module name: '{moduleName}'");
                    }
                    return true;
                }
            }
        }

        // Check for save/load project commands
        bool isProjectCommand = command.Contains("project") || command.Contains("프로젝트") ||
                              command.Contains("current") || command.Contains("현재");
        if (isProjectCommand)
        {
            if (enableDebugLogging)
            {
                Debug.Log($"[VOICE DEBUG] Found project command keyword");
            }
            return true;
        }

        // Check for system-wide AEC commands
        if (command.Contains("all") && (command.Contains("systems") || command.Contains("components") ||
            command.Contains("building") || command.Contains("전체") || command.Contains("모든")))
        {
            if (enableDebugLogging)
            {
                Debug.Log($"[VOICE DEBUG] Found system-wide command keywords");
            }
            return true;
        }

        if (enableDebugLogging)
        {
            Debug.Log($"[VOICE DEBUG] No valid AEC target found in: '{command}'");
        }
        return false;
    }

    private bool ProcessAECVoiceCommand(string command)
    {
        if (string.IsNullOrEmpty(command)) return false;

        command = command.ToLower().Trim();

        if (enableDebugLogging)
        {
            Debug.Log($"[VOICE DEBUG] === PROCESSING AEC COMMAND ===");
            Debug.Log($"[VOICE DEBUG] Processing AEC voice command: '{command}'");
        }

        lastProcessedCommand = command;

        // Check for project save/load commands
        if (IsProjectSaveLoadCommand(command))
        {
            return ProcessProjectSaveLoadCommand(command);
        }

        // Check for prefabricated module spawning commands - This is the key enhancement!
        if (IsModuleSpawningCommand(command))
        {
            return ProcessModuleSpawningCommand(command);
        }

        // Check for system-wide building commands
        if (IsSystemWideBuildingCommand(command))
        {
            return ProcessSystemWideBuildingCommand(command);
        }

        // Parse action and target building component
        VoiceCommandKeywords.VoiceAction action = ParseAECAction(command);
        string targetComponent = ParseTargetBuildingComponent(command);

        if (!string.IsNullOrEmpty(targetComponent))
        {
            bool success = ExecuteBuildingSystemCommand(action, targetComponent);
            if (success)
            {
                string actionText = GetAECActionDisplayText(action);
                UpdateVoiceStatus($"✅ {actionText} {targetComponent} system");
                return true;
            }
            else
            {
                UpdateVoiceStatus($"❌ {targetComponent} system not found");
                return false;
            }
        }
        else
        {
            Debug.LogWarning($"Could not identify building system component in command: {command}");
            UpdateVoiceStatus("❌ Building component not recognized");
            return false;
        }
    }

    private bool IsProjectSaveLoadCommand(string command)
    {
        return (command.Contains("save") || command.Contains("저장")) ||
               (command.Contains("load") || command.Contains("불러오기") || command.Contains("로드"));
    }

    private bool ProcessProjectSaveLoadCommand(string command)
    {
        if (saveLoadManager == null)
        {
            Debug.LogError("ModuleSaveLoadManager not assigned to VoiceCommandController");
            UpdateVoiceStatus("❌ Project save/load manager not found");
            return false;
        }

        VoiceCommandKeywords.VoiceAction action = ParseAECAction(command);

        if (enableDebugLogging)
        {
            Debug.Log($"[VOICE DEBUG] === SAVE/LOAD COMMAND ===");
            Debug.Log($"[VOICE DEBUG] Command: '{command}', Action: {action}");
        }

        try
        {
            switch (action)
            {
                case VoiceCommandKeywords.VoiceAction.Save:
                    bool saveSuccess = saveLoadManager.SaveVoiceCommand(command);
                    if (saveSuccess)
                    {
                        UpdateVoiceStatus("✅ Saved current project state via voice");
                        return true;
                    }
                    else
                    {
                        UpdateVoiceStatus("❌ Failed to save project state");
                        return false;
                    }

                case VoiceCommandKeywords.VoiceAction.Load:
                    bool loadSuccess = saveLoadManager.LoadVoiceCommand(command);
                    if (loadSuccess)
                    {
                        UpdateVoiceStatus("✅ Loaded project state via voice");
                        return true;
                    }
                    else
                    {
                        UpdateVoiceStatus("❌ Failed to load project state");
                        return false;
                    }

                default:
                    UpdateVoiceStatus("❌ Unknown project command");
                    return false;
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Project save/load command failed: {ex.Message}");
            UpdateVoiceStatus($"❌ Project operation failed: {ex.Message}");
            return false;
        }
    }

    private bool IsModuleSpawningCommand(string command)
    {
        // Enhanced detection for module spawning commands
        bool hasSpawnAction = command.Contains("spawn") || command.Contains("place") || command.Contains("install") ||
               command.Contains("construct") || command.Contains("build") || command.Contains("create") ||
               command.Contains("add") || command.Contains("load") || command.Contains("activate") ||
               command.Contains("스폰") || command.Contains("로드") || command.Contains("설치") ||
               command.Contains("시공") || command.Contains("배치") || command.Contains("만들기") ||
               command.Contains("추가") || command.Contains("생성");

        bool hasModuleTarget = ContainsModuleName(command) || ContainsActualModuleName(command);

        if (enableDebugLogging)
        {
            Debug.Log($"[VOICE DEBUG] Module spawn check - Action: {hasSpawnAction}, Target: {hasModuleTarget}");
        }

        return hasSpawnAction && hasModuleTarget;
    }

    private bool ContainsModuleName(string command)
    {
        foreach (var moduleEntry in VoiceCommandKeywords.ModuleKeywords)
        {
            foreach (string keyword in moduleEntry.Value)
            {
                if (ContainsExactWord(command, keyword))
                {
                    if (enableDebugLogging)
                    {
                        Debug.Log($"[VOICE DEBUG] Found module keyword: '{keyword}' -> '{moduleEntry.Key}'");
                    }
                    return true;
                }
            }
        }
        return false;
    }

    private bool ContainsActualModuleName(string command)
    {
        if (moduleManager == null) return false;

        var availableModules = moduleManager.GetAvailableModuleNames();
        foreach (var moduleName in availableModules)
        {
            if (ContainsExactWord(command, moduleName.ToLower()) ||
                command.Contains(moduleName.ToLower()))
            {
                if (enableDebugLogging)
                {
                    Debug.Log($"[VOICE DEBUG] Found actual module name: '{moduleName}'");
                }
                return true;
            }
        }
        return false;
    }

    private bool ProcessModuleSpawningCommand(string command)
    {
        if (enableDebugLogging)
        {
            Debug.Log($"[VOICE DEBUG] === MODULE SPAWN COMMAND ===");
            Debug.Log($"[VOICE DEBUG] Command: '{command}'");
        }

        if (moduleManager == null)
        {
            Debug.LogError("ModuleManager not assigned to VoiceCommandController");
            UpdateVoiceStatus("❌ Module manager not found");
            return false;
        }

        string targetModule = ParseTargetPrefabricatedModule(command);

        if (!string.IsNullOrEmpty(targetModule))
        {
            if (enableDebugLogging)
            {
                Debug.Log($"[VOICE DEBUG] Attempting to spawn module: '{targetModule}'");
            }

            // Use ModuleManager's enhanced SpawnModuleByName method
            bool success = moduleManager.SpawnModuleByName(targetModule);

            if (success)
            {
                UpdateVoiceStatus($"✅ Installed prefabricated module '{targetModule}' via voice");

                if (enableDebugLogging)
                {
                    Debug.Log($"[VOICE DEBUG] ✅ Successfully spawned module: '{targetModule}'");
                }
                return true;
            }
            else
            {
                UpdateVoiceStatus($"❌ Prefabricated module '{targetModule}' not found");

                if (enableDebugLogging)
                {
                    Debug.Log($"[VOICE DEBUG] ❌ Failed to spawn module '{targetModule}'");
                    var availableModules = moduleManager.GetAvailableModuleNames();
                    Debug.Log($"[VOICE DEBUG] Available modules: {string.Join(", ", availableModules)}");
                }
                return false;
            }
        }
        else
        {
            // Try direct module name extraction from the command
            string directModuleName = ExtractDirectModuleName(command);

            if (!string.IsNullOrEmpty(directModuleName))
            {
                if (enableDebugLogging)
                {
                    Debug.Log($"[VOICE DEBUG] Trying direct module name: '{directModuleName}'");
                }

                bool success = moduleManager.SpawnModuleByName(directModuleName);

                if (success)
                {
                    UpdateVoiceStatus($"✅ Installed module '{directModuleName}' via voice");
                    return true;
                }
            }

            Debug.LogWarning($"Could not identify prefabricated module in command: {command}");
            UpdateVoiceStatus("❌ Module not recognized in voice command");

            if (enableDebugLogging)
            {
                Debug.Log($"[VOICE DEBUG] ❌ Could not parse module name from: '{command}'");
                Debug.Log($"[VOICE DEBUG] Try commands like: 'spawn m1-1', 'install module m2-3', 'place unit m3-4'");

                if (moduleManager != null)
                {
                    var availableModules = moduleManager.GetAvailableModuleNames();
                    Debug.Log($"[VOICE DEBUG] Available modules: {string.Join(", ", availableModules)}");
                }
            }
            return false;
        }
    }

    private string ExtractDirectModuleName(string command)
    {
        if (moduleManager == null) return null;

        var availableModules = moduleManager.GetAvailableModuleNames();

        if (enableDebugLogging)
        {
            Debug.Log($"[VOICE DEBUG] Extracting module name from: '{command}'");
            Debug.Log($"[VOICE DEBUG] Available modules: {string.Join(", ", availableModules)}");
        }

        // Check for direct matches with available module names
        foreach (var moduleName in availableModules)
        {
            if (command.Contains(moduleName.ToLower()))
            {
                if (enableDebugLogging)
                {
                    Debug.Log($"[VOICE DEBUG] Found direct module match: '{moduleName}'");
                }
                return moduleName;
            }
        }

        // Enhanced module pattern matching - multiple patterns
        var patterns = new[]
        {
            @"module\s*(\d+)[-\s](\d+)",  // "module 1-2" or "module 1 2"
            @"m\s*(\d+)\s*[-_]\s*(\d+)",  // "m1-1" or "m 1 1"
            @"(\d+)[-\s](\d+)",           // "1-2" or "1 2"
            @"unit\s*(\d+)[-\s](\d+)",    // "unit 1-2"
            @"(\d+)\s*(\d+)"              // "1 2" (separate numbers)
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(command, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                string num1 = match.Groups[1].Value;
                string num2 = match.Groups[2].Value;

                // Try different module name formats
                var possibleNames = new[]
                {
                    $"m{num1}-{num2}",
                    $"M{num1}-{num2}",
                    $"module{num1}-{num2}",
                    $"Module{num1}-{num2}",
                    $"{num1}-{num2}",
                    $"m{num1}_{num2}",
                    $"Module {num1}-{num2}",
                    $"m {num1}-{num2}"
                };

                foreach (var possibleName in possibleNames)
                {
                    // Check if this matches any available module
                    var foundModule = availableModules.FirstOrDefault(m =>
                        m.ToLower() == possibleName.ToLower() ||
                        m.ToLower().Contains(possibleName.ToLower()) ||
                        possibleName.ToLower().Contains(m.ToLower()));

                    if (!string.IsNullOrEmpty(foundModule))
                    {
                        if (enableDebugLogging)
                        {
                            Debug.Log($"[VOICE DEBUG] Pattern '{pattern}' extracted '{possibleName}' -> found '{foundModule}'");
                        }
                        return foundModule;
                    }
                }

                // If no exact match, return the most likely format
                string bestGuess = $"m{num1}-{num2}";
                if (enableDebugLogging)
                {
                    Debug.Log($"[VOICE DEBUG] Pattern '{pattern}' extracted numbers {num1},{num2} -> best guess: '{bestGuess}'");
                }
                return bestGuess;
            }
        }

        if (enableDebugLogging)
        {
            Debug.Log($"[VOICE DEBUG] No module pattern found in command");
        }
        return null;
    }

    private bool IsSystemWideBuildingCommand(string command)
    {
        // English system-wide commands
        bool englishSystemWide = command.Contains("all") &&
                                (command.Contains("systems") || command.Contains("components") ||
                                 command.Contains("building"));

        // Korean system-wide commands
        bool koreanSystemWide = command.Contains("전체") || command.Contains("모든") ||
                               command.Contains("모든시스템") || command.Contains("전체시스템") ||
                               command.Contains("전체건물");

        return englishSystemWide || koreanSystemWide;
    }

    private bool ProcessSystemWideBuildingCommand(string command)
    {
        if (moduleManager == null) return false;

        VoiceCommandKeywords.VoiceAction action = ParseAECAction(command);

        // Handle Korean system-wide commands
        if (command.Contains("전체") || command.Contains("모든"))
        {
            if (command.Contains("숨기기") || command.Contains("끄기") || command.Contains("비활성화"))
                action = VoiceCommandKeywords.VoiceAction.Hide;
            else if (command.Contains("보이기") || command.Contains("켜기") || command.Contains("활성화"))
                action = VoiceCommandKeywords.VoiceAction.Show;
            else if (command.Contains("토글") || command.Contains("전환"))
                action = VoiceCommandKeywords.VoiceAction.Toggle;
        }

        bool targetState = action == VoiceCommandKeywords.VoiceAction.Show;

        if (action == VoiceCommandKeywords.VoiceAction.Toggle)
        {
            moduleManager.ToggleAllComponents();
        }
        else
        {
            moduleManager.SetAllComponentsVisibility(targetState);
        }

        string actionText = action == VoiceCommandKeywords.VoiceAction.Toggle ? "Toggled" : (targetState ? "Showed" : "Hid");
        UpdateVoiceStatus($"✅ {actionText} all building systems");
        return true;
    }

    private VoiceCommandKeywords.VoiceAction ParseAECAction(string command)
    {
        // Use exact matching for better accuracy
        foreach (var actionPair in VoiceCommandKeywords.ActionKeywords)
        {
            if (ContainsExactWord(command, actionPair.Key))
            {
                if (enableDebugLogging)
                {
                    Debug.Log($"[VOICE DEBUG] Found AEC action: '{actionPair.Key}' -> {actionPair.Value}");
                }
                return actionPair.Value;
            }
        }

        if (enableDebugLogging)
        {
            Debug.Log($"[VOICE DEBUG] No action found in AEC command: '{command}', defaulting to Toggle");
        }
        return VoiceCommandKeywords.VoiceAction.Toggle;
    }

    private string ParseTargetBuildingComponent(string command)
    {
        foreach (var componentPair in VoiceCommandKeywords.BuildingSystemKeywords)
        {
            foreach (string keyword in componentPair.Value)
            {
                if (ContainsExactWord(command, keyword))
                {
                    if (enableDebugLogging)
                    {
                        Debug.Log($"[VOICE DEBUG] Found building component: '{keyword}' -> {componentPair.Key}");
                    }
                    return componentPair.Key;
                }
            }
        }

        if (enableDebugLogging)
        {
            Debug.Log($"[VOICE DEBUG] No building component found in command: '{command}'");
        }
        return null;
    }

    private string ParseTargetPrefabricatedModule(string command)
    {
        // Strategy 1: Check predefined module keywords
        foreach (var modulePair in VoiceCommandKeywords.ModuleKeywords)
        {
            foreach (string keyword in modulePair.Value)
            {
                if (ContainsExactWord(command, keyword))
                {
                    if (enableDebugLogging)
                    {
                        Debug.Log($"[VOICE DEBUG] Found prefabricated module keyword: '{keyword}' -> '{modulePair.Key}'");
                    }
                    return modulePair.Key;
                }
            }
        }

        // Strategy 2: Check actual module names from ModuleManager
        if (moduleManager != null)
        {
            var availableModules = moduleManager.GetAvailableModuleNames();
            foreach (var moduleName in availableModules)
            {
                if (ContainsExactWord(command, moduleName.ToLower()) ||
                    command.Contains(moduleName.ToLower()))
                {
                    if (enableDebugLogging)
                    {
                        Debug.Log($"[VOICE DEBUG] Found actual module name: '{moduleName}'");
                    }
                    return moduleName;
                }
            }
        }

        // Strategy 3: Extract module pattern (m1-1, m2-3, etc.)
        string extractedModule = ExtractDirectModuleName(command);
        if (!string.IsNullOrEmpty(extractedModule))
        {
            if (enableDebugLogging)
            {
                Debug.Log($"[VOICE DEBUG] Extracted module pattern: '{extractedModule}'");
            }
            return extractedModule;
        }

        if (enableDebugLogging)
        {
            Debug.Log($"[VOICE DEBUG] No prefabricated module found in command: '{command}'");
        }
        return null;
    }

    private bool ContainsExactWord(string text, string word)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(word)) return false;

        // Enhanced Korean text handling
        if (IsKoreanCharacter(word[0]) || IsKoreanCharacter(text[0]))
        {
            string normalizedText = NormalizeKoreanText(text);
            string normalizedWord = NormalizeKoreanText(word);

            if (enableDebugLogging)
            {
                Debug.Log($"[KOREAN DEBUG] Original: '{word}' -> Normalized: '{normalizedWord}'");
                Debug.Log($"[KOREAN DEBUG] Text: '{text}' -> Normalized: '{normalizedText}'");
            }

            // For Korean, use contains matching due to agglutinative nature
            bool result = normalizedText.Contains(normalizedWord) || normalizedWord.Contains(normalizedText);

            if (enableDebugLogging)
            {
                Debug.Log($"[KOREAN DEBUG] Match result: {result}");
            }

            return result;
        }

        // For very short words or patterns, use simple contains
        if (word.Length <= 2)
        {
            return text.ToLower().Contains(word.ToLower());
        }

        // For number patterns like "1-2", "2-3", etc., use more flexible matching
        if (Regex.IsMatch(word, @"^\d+[-\s]\d+$"))
        {
            string pattern = word.Replace("-", @"[-\s]").Replace(" ", @"[-\s]");
            return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase);
        }

        // For module patterns like "module 1-2", check both exact and flexible patterns
        if (word.Contains("module") && Regex.IsMatch(word, @"module\s*\d+[-\s]\d+"))
        {
            // Extract the number part
            var match = Regex.Match(word, @"module\s*(\d+)[-\s](\d+)");
            if (match.Success)
            {
                string num1 = match.Groups[1].Value;
                string num2 = match.Groups[2].Value;

                // Check multiple variations
                return text.Contains($"module {num1}-{num2}") ||
                       text.Contains($"module {num1} {num2}") ||
                       text.Contains($"module{num1}-{num2}") ||
                       text.Contains($"module{num1}{num2}") ||
                       text.Contains($"{num1}-{num2}") ||
                       text.Contains($"{num1} {num2}");
            }
        }

        // Standard word boundary matching for other cases
        try
        {
            string pattern = $@"\b{Regex.Escape(word)}\b";
            return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase);
        }
        catch
        {
            // Fallback to simple contains if regex fails
            return text.ToLower().Contains(word.ToLower());
        }
    }

    private bool ExecuteBuildingSystemCommand(VoiceCommandKeywords.VoiceAction action, string componentName)
    {
        if (moduleManager == null)
        {
            Debug.LogError("ModuleManager not assigned to VoiceCommandController");
            return false;
        }

        bool targetState = action == VoiceCommandKeywords.VoiceAction.Show;
        if (action == VoiceCommandKeywords.VoiceAction.Toggle)
        {
            targetState = !moduleManager.GetComponentVisibility(componentName);
        }

        bool success = moduleManager.SetComponentVisibility(componentName, targetState);

        if (success)
        {
            string actionText = action == VoiceCommandKeywords.VoiceAction.Toggle ? (targetState ? "Showed" : "Hid") : action.ToString();
            if (enableDebugLogging)
            {
                Debug.Log($"[VOICE DEBUG] AEC voice command executed: {actionText} {componentName} building system");
            }
        }

        return success;
    }

    private string GetAECActionDisplayText(VoiceCommandKeywords.VoiceAction action)
    {
        switch (action)
        {
            case VoiceCommandKeywords.VoiceAction.Hide: return "Hidden";
            case VoiceCommandKeywords.VoiceAction.Show: return "Displayed";
            case VoiceCommandKeywords.VoiceAction.Toggle: return "Toggled";
            case VoiceCommandKeywords.VoiceAction.Spawn: return "Installed";
            case VoiceCommandKeywords.VoiceAction.Save: return "Saved";
            case VoiceCommandKeywords.VoiceAction.Load: return "Loaded";
            default: return action.ToString();
        }
    }

    #endregion

    #region Language Detection and Validation

    /// <summary>
    /// Determines if the input text is Korean or English only
    /// </summary>
    private bool IsKoreanOrEnglishText(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;

        text = text.Trim();

        if (enableDebugLogging)
        {
            Debug.Log($"[LANGUAGE DEBUG] Checking language for: '{text}'");
        }

        // Count different character types
        int koreanChars = 0;
        int englishChars = 0;
        int numberChars = 0;
        int spaceChars = 0;
        int punctuationChars = 0;
        int otherChars = 0;

        foreach (char c in text)
        {
            if (IsKoreanCharacter(c))
            {
                koreanChars++;
            }
            else if (char.IsLetter(c) && c >= 'A' && c <= 'z')
            {
                englishChars++;
            }
            else if (char.IsDigit(c))
            {
                numberChars++;
            }
            else if (char.IsWhiteSpace(c))
            {
                spaceChars++;
            }
            else if (char.IsPunctuation(c) || c == '-' || c == '_')
            {
                punctuationChars++;
            }
            else
            {
                otherChars++;
            }
        }

        int totalMeaningfulChars = koreanChars + englishChars + numberChars;

        if (enableDebugLogging)
        {
            Debug.Log($"[LANGUAGE DEBUG] Korean: {koreanChars}, English: {englishChars}, Numbers: {numberChars}, Other: {otherChars}");
        }

        // If there are characters from other languages, reject
        if (otherChars > 0 && totalMeaningfulChars > 0)
        {
            float otherRatio = (float)otherChars / totalMeaningfulChars;
            if (otherRatio > 0.1f) // Allow up to 10% "other" characters (for special symbols)
            {
                if (enableDebugLogging)
                {
                    Debug.Log($"[LANGUAGE DEBUG] Rejected: Too many non-Korean/English characters ({otherRatio:P1})");
                }
                return false;
            }
        }

        // Accept if it's primarily Korean, English, or numbers
        bool isAcceptable = totalMeaningfulChars > 0 && otherChars <= totalMeaningfulChars * 0.1f;

        if (enableDebugLogging)
        {
            Debug.Log($"[LANGUAGE DEBUG] Language check result: {(isAcceptable ? "ACCEPTED" : "REJECTED")}");
        }

        return isAcceptable;
    }

    /// <summary>
    /// Checks if a character is Korean (Hangul)
    /// </summary>
    private bool IsKoreanCharacter(char c)
    {
        // Korean character ranges:
        // Hangul Syllables: U+AC00–U+D7AF
        // Hangul Jamo: U+1100–U+11FF
        // Hangul Compatibility Jamo: U+3130–U+318F
        return (c >= 0xAC00 && c <= 0xD7AF) ||  // Hangul Syllables
               (c >= 0x1100 && c <= 0x11FF) ||  // Hangul Jamo
               (c >= 0x3130 && c <= 0x318F);    // Hangul Compatibility Jamo
    }

    /// <summary>
    /// Enhanced Korean text normalization for better matching
    /// </summary>
    private string NormalizeKoreanText(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        return text.ToLower()
            .Replace("  ", " ")  // Remove double spaces
            .Replace("모듈 ", "모듈")
            .Replace("엠 ", "엠")
            .Replace(" 일", "일")
            .Replace(" 이", "이")
            .Replace(" 삼", "삼")
            .Replace(" 사", "사")
            .Replace("대", "-")  // Convert "대" to "-"
            .Replace("에", " ")  // Convert "에" to space
            .Trim();
    }

    #endregion

    #region Continuous Listening

    public void StartContinuousListening()
    {
        if (!enableContinuousListening) return;

        isListeningContinuously = true;

        if (continuousListeningCoroutine != null)
            StopCoroutine(continuousListeningCoroutine);

        continuousListeningCoroutine = StartCoroutine(ContinuousListeningHandler());
        UpdateVoiceStatus("🔄 Continuous listening enabled for AEC commands");
    }

    public void StopContinuousListening()
    {
        isListeningContinuously = false;

        if (continuousListeningCoroutine != null)
        {
            StopCoroutine(continuousListeningCoroutine);
            continuousListeningCoroutine = null;
        }

        if (isRecording)
        {
            StopVoiceRecording();
        }

        UpdateVoiceStatus("⏸️ Continuous listening stopped");
    }

    private IEnumerator ContinuousListeningHandler()
    {
        while (isListeningContinuously)
        {
            if (!isRecording && !isProcessing)
            {
                StartVoiceRecording();
            }

            yield return new WaitForSeconds(0.5f);
        }
    }

    #endregion

    #region Public Control Methods

    public void ToggleManualRecording()
    {
        if (isRecording)
            StopVoiceRecording();
        else
            StartVoiceRecording();
    }

    #endregion

    #region Haptic Feedback

    /// <summary>
    /// Trigger haptic feedback on Quest 3 controllers
    /// </summary>
    private void TriggerHapticFeedback(float intensity, float duration)
    {
        if (!enableHapticFeedback) return;

        try
        {
#if UNITY_XR_HANDS
            // Quest 3 hand tracking haptics
            var leftHand = UnityEngine.XR.Hands.XRHandSubsystem.leftHand;
            var rightHand = UnityEngine.XR.Hands.XRHandSubsystem.rightHand;
            
            if (leftHand.isTracked)
            {
                // Trigger haptic on left hand
                UnityEngine.XR.HapticCapabilities capabilities;
                if (UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.LeftHand).TryGetHapticCapabilities(out capabilities))
                {
                    if (capabilities.supportsImpulse)
                    {
                        UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.LeftHand).SendHapticImpulse(0, intensity, duration);
                    }
                }
            }
            
            if (rightHand.isTracked)
            {
                // Trigger haptic on right hand
                UnityEngine.XR.HapticCapabilities capabilities;
                if (UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.RightHand).TryGetHapticCapabilities(out capabilities))
                {
                    if (capabilities.supportsImpulse)
                    {
                        UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.RightHand).SendHapticImpulse(0, intensity, duration);
                    }
                }
            }
#else
            // Standard XR controller haptics for Quest 3
            var leftController = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.LeftHand);
            var rightController = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.RightHand);

            // Left controller haptics
            UnityEngine.XR.HapticCapabilities leftCapabilities;
            if (leftController.TryGetHapticCapabilities(out leftCapabilities) && leftCapabilities.supportsImpulse)
            {
                leftController.SendHapticImpulse(0, intensity, duration);
            }

            // Right controller haptics  
            UnityEngine.XR.HapticCapabilities rightCapabilities;
            if (rightController.TryGetHapticCapabilities(out rightCapabilities) && rightCapabilities.supportsImpulse)
            {
                rightController.SendHapticImpulse(0, intensity, duration);
            }
#endif

            if (enableDebugLogging)
            {
                Debug.Log($"[HAPTIC] Triggered haptic feedback - Intensity: {intensity}, Duration: {duration}");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[HAPTIC] Failed to trigger haptic feedback: {ex.Message}");
        }
    }

    /// <summary>
    /// Trigger success haptic feedback
    /// </summary>
    public void TriggerSuccessHaptic()
    {
        TriggerHapticFeedback(successHapticIntensity, successHapticDuration);
    }

    /// <summary>
    /// Trigger failure haptic feedback
    /// </summary>
    public void TriggerFailureHaptic()
    {
        TriggerHapticFeedback(failureHapticIntensity, failureHapticDuration);
    }

    /// <summary>
    /// Trigger recording haptic feedback
    /// </summary>
    public void TriggerRecordingHaptic()
    {
        TriggerHapticFeedback(recordingHapticIntensity, recordingHapticDuration);
    }

    #endregion

    #region UI Management

    private void UpdateVoiceStatus(string status)
    {
        if (voiceStatusText != null)
            voiceStatusText.text = status;

        BIMLog.Verbose($"AEC Voice Status: {status}");
    }

    private void UpdateLastCommand(string command)
    {
        if (lastCommandText != null)
            lastCommandText.text = $"Last: {command}";
    }

    private void UpdateRealTimeTranscription(string text)
    {
        if (realTimeTranscriptionText != null)
        {
            realTimeTranscriptionText.text = text;
        }
        currentTranscription = text;
    }

    private void UpdateVoiceStatusIndicator()
    {
        if (voiceStatusIndicator != null)
        {
            if (isProcessing)
                voiceStatusIndicator.color = processingColor;
            else if (isRecording)
                voiceStatusIndicator.color = recordingColor;
            else if (isListeningContinuously)
                voiceStatusIndicator.color = listeningColor;
            else if (!string.IsNullOrEmpty(azureSubscriptionKey1))
                voiceStatusIndicator.color = connectedColor;
            else
                voiceStatusIndicator.color = inactiveColor;
        }
    }

    #endregion

    #region Public Configuration Methods

    public void SetMaxRecordingDuration(float duration)
    {
        maxRecordingDuration = Mathf.Clamp(duration, 5f, 30f);
    }

    public void SetRecordingTimeout(float timeout)
    {
        recordingTimeout = Mathf.Clamp(timeout, 10f, 60f);
    }

    public void EnableInputAction(bool enable)
    {
        useInputAction = enable;

        if (voiceRecordAction != null)
        {
            if (enable)
                voiceRecordAction.action.Enable();
            else
                voiceRecordAction.action.Disable();
        }
    }

    public void SetVoiceRecordAction(InputActionReference actionReference)
    {
        if (useInputAction && voiceRecordAction != null)
        {
            voiceRecordAction.action.Disable();
        }

        voiceRecordAction = actionReference;

        if (useInputAction && voiceRecordAction != null)
        {
            voiceRecordAction.action.Enable();
        }
    }

    public void SetMinimumConfidenceScore(float score)
    {
        minimumConfidenceScore = Mathf.Clamp01(score);
        BIMLog.Verbose($"Minimum confidence score for AEC commands set to: {minimumConfidenceScore:P1}");
    }

    public void SetExactMatching(bool exact)
    {
        requireExactMatch = exact;
        BIMLog.Verbose($"Exact AEC command matching: {(exact ? "Enabled" : "Disabled")}");
    }

    public void SetKoreanLanguageSupport(bool enable)
    {
        enableKoreanLanguage = enable;

        // Update Azure language settings
        if (enable)
        {
            azureLanguage = "ko-KR"; // Korean
            azureSecondaryLanguage = "en-US"; // English as secondary
            useMultiLanguageDetection = true;
        }
        else
        {
            azureLanguage = "en-US"; // English only
            useMultiLanguageDetection = false;
        }

        BIMLog.Verbose($"Korean AEC terminology support: {(enable ? "Enabled" : "Disabled")}");
        BIMLog.Verbose($"Azure primary language: {azureLanguage}");
        BIMLog.Verbose($"Azure multi-language detection: {useMultiLanguageDetection}");
    }

    public void SetContinuousListening(bool enable)
    {
        enableContinuousListening = enable;

        if (enable && !isListeningContinuously)
            StartContinuousListening();
        else if (!enable && isListeningContinuously)
            StopContinuousListening();
    }

    public void SetAECContextKeyword(string keyword)
    {
        aecContextKeyword = keyword;
        BIMLog.Verbose($"AEC context keyword set to: '{keyword}'");
    }

    public void SetRequireContextKeyword(bool require)
    {
        requireContextKeyword = require;
        BIMLog.Verbose($"Require AEC context keyword: {(require ? "Enabled" : "Disabled")}");
    }

    public void SetAzureSubscriptionKey1(string key)
    {
        azureSubscriptionKey1 = key;

        if (string.IsNullOrEmpty(key))
        {
            UpdateVoiceStatus("⚠️ Azure subscription key 1 cleared");
        }
        else
        {
            UpdateVoiceStatus("✅ Azure subscription key 1 configured");
        }
    }

    public void SetAzureSubscriptionKey2(string key)
    {
        azureSubscriptionKey2 = key;

        if (string.IsNullOrEmpty(key))
        {
            BIMLog.Verbose("Azure subscription key 2 cleared");
        }
        else
        {
            BIMLog.Verbose("Azure subscription key 2 configured as backup");
        }
    }

    public void SetAzureRegion(string region)
    {
        azureRegion = region;
        BIMLog.Verbose($"Azure region set to: {region}");
    }

    public void SetAzureEndpoint(string endpoint)
    {
        azureEndpoint = endpoint;

        // Reconstruct API URL with new endpoint
        if (!azureEndpoint.EndsWith("/"))
            azureEndpoint += "/";
        azureSpeechApiUrl = azureEndpoint + "speech/recognition/conversation/cognitiveservices/v1";

        BIMLog.Verbose($"Azure endpoint set to: {endpoint}");
        BIMLog.Verbose($"Azure API URL updated to: {azureSpeechApiUrl}");
    }

    public void SetAzureLanguage(string language)
    {
        azureLanguage = language;
        BIMLog.Verbose($"Azure primary language set to: {language}");
    }

    public void SetAzureSecondaryLanguage(string language)
    {
        azureSecondaryLanguage = language;
        BIMLog.Verbose($"Azure secondary language set to: {language}");
    }

    public void SetMultiLanguageDetection(bool enable)
    {
        useMultiLanguageDetection = enable;
        BIMLog.Verbose($"Azure multi-language detection: {(enable ? "Enabled" : "Disabled")}");
    }

    public void SetLanguageRestriction(bool restrictToKoreanEnglish)
    {
        restrictToKoreanEnglishOnly = restrictToKoreanEnglish;
        BIMLog.Verbose($"Language restriction (Korean/English only): {(restrictToKoreanEnglish ? "Enabled" : "Disabled")}");
    }

    public void SetKoreanDetectionThreshold(float threshold)
    {
        koreanDetectionThreshold = Mathf.Clamp01(threshold);
        BIMLog.Verbose($"Korean detection threshold set to: {koreanDetectionThreshold:P1}");
    }

    public void SetHapticFeedback(bool enable)
    {
        enableHapticFeedback = enable;
        BIMLog.Verbose($"Haptic feedback: {(enable ? "Enabled" : "Disabled")}");
    }

    public void SetHapticIntensities(float success, float failure, float recording)
    {
        successHapticIntensity = Mathf.Clamp01(success);
        failureHapticIntensity = Mathf.Clamp01(failure);
        recordingHapticIntensity = Mathf.Clamp01(recording);
        BIMLog.Verbose($"Haptic intensities updated - Success: {success}, Failure: {failure}, Recording: {recording}");
    }

    public List<string> GetAvailableAECCommands()
    {
        List<string> commands = new List<string>();

        // Component visibility commands
        foreach (var component in VoiceCommandKeywords.BuildingSystemKeywords.Keys)
        {
            commands.Add($"Hide {component} system");
            commands.Add($"Show {component} system");
            commands.Add($"Toggle {component} system");
        }

        // Module spawning commands - Enhanced with actual module names
        if (moduleManager != null)
        {
            var availableModules = moduleManager.GetAvailableModuleNames();
            foreach (var module in availableModules)
            {
                commands.Add($"Spawn {module}");
                commands.Add($"Install {module}");
                commands.Add($"Place {module}");
                commands.Add($"Build {module}");
                commands.Add($"Create {module}");
                commands.Add($"Load {module}");
            }
        }

        // Common module commands (predefined patterns)
        foreach (var module in VoiceCommandKeywords.ModuleKeywords.Keys)
        {
            commands.Add($"Spawn {module}");
            commands.Add($"Install module {module}");
            commands.Add($"Place module {module}");
            commands.Add($"Build module {module}");
            commands.Add($"Create module {module}");
        }

        // Project management commands
        commands.Add("Save current project");
        commands.Add("Save project");
        commands.Add("Save");
        commands.Add("Load current project");
        commands.Add("Load project");
        commands.Add("Load");

        // System-wide commands
        commands.Add("Hide all building systems");
        commands.Add("Show all building systems");
        commands.Add("Toggle all building systems");

        // Korean examples if enabled
        if (enableKoreanLanguage)
        {
            commands.Add("=== 한국어 명령어 예시 (Korean Command Examples) ===");
            commands.Add("숨기기 건축 시스템 (Hide Architectural System)");
            commands.Add("보이기 구조 시스템 (Show Structural System)");
            commands.Add("켜기 전기 시스템 (Turn On Electrical System)");
            commands.Add("스폰 엠일일 (Spawn M1-1)");
            commands.Add("설치 엠일이 모듈 (Install M1-2 Module)");
            commands.Add("만들기 엠삼삼 (Create M3-3)");
            commands.Add("생성 모듈일대이 (Create Module 1-2)");
            commands.Add("배치 엠이삼 (Place M2-3)");
            commands.Add("저장 프로젝트 (Save Project)");
            commands.Add("불러오기 프로젝트 (Load Project)");
            commands.Add("전체 건축 시스템 보이기 (Show All Building Systems)");

            // Korean action variations
            commands.Add("=== 한국어 동작 명령어 (Korean Action Commands) ===");
            commands.Add("숨겨 / 숨기세요 (Hide)");
            commands.Add("보여 / 보여주세요 (Show)");
            commands.Add("켜 / 켜세요 (Turn On)");
            commands.Add("생성해 / 생성하세요 (Create)");
            commands.Add("설치해 / 설치하세요 (Install)");
            commands.Add("저장해 / 저장하세요 (Save)");
        }

        return commands;
    }

    public bool ValidateAECCommand(string command)
    {
        return IsValidAECCommand(command);
    }

    public string GetAzureSpeechSystemStatus()
    {
        string status = "=== QUEST 3 → AZURE SPEECH API → UNITY FLOW STATUS ===\n";
        status += $"🎤 STEP 1 (Quest 3 Mic): {(Microphone.devices.Length > 0 ? $"✅ {microphoneName}" : "❌ No Mic")}\n";
        status += $"🔄 STEP 2 (Audio File): {(audioFileData != null ? $"✅ {audioFileData.Length} bytes" : "❌ No File")}\n";
        status += $"📡 STEP 3 (Send API): {(!string.IsNullOrEmpty(azureSubscriptionKey1) ? "✅ Ready" : "❌ No Subscription Key")}\n";
        status += $"📥 STEP 4 (Receive Text): {(!string.IsNullOrEmpty(currentTranscription) ? $"✅ \"{currentTranscription}\"" : "❌ No Text")}\n";
        status += $"⚡ STEP 5 (Unity Commands): {(lastCommandSuccess ? "✅ Success" : "❌ Not Executed")}\n";

        status += "\n--- AZURE SPEECH INTEGRATION ---\n";
        status += $"🔑 Primary Key: {(!string.IsNullOrEmpty(azureSubscriptionKey1) ? "✅ Configured" : "❌ Missing")}\n";
        status += $"🔑 Backup Key: {(!string.IsNullOrEmpty(azureSubscriptionKey2) ? "✅ Configured" : "❌ Missing")}\n";
        status += $"🌏 Region: {azureRegion}\n";
        status += $"🌐 Endpoint: {azureEndpoint}\n";
        status += $"🗣️ Primary Language: {azureLanguage}\n";
        status += $"🗣️ Secondary Language: {azureSecondaryLanguage}\n";
        status += $"🔀 Multi-language: {(useMultiLanguageDetection ? "✅ Enabled" : "❌ Disabled")}\n";
        status += $"📡 API URL: {azureSpeechApiUrl}\n";

        status += "\n--- SYSTEM INTEGRATION ---\n";
        status += $"🏗️ ModuleManager: {(moduleManager != null ? "✅ Connected" : "❌ Missing")}\n";
        if (moduleManager != null)
        {
            var modules = moduleManager.GetAvailableModuleNames();
            status += $"📦 Available Modules: {modules.Count} ({string.Join(", ", modules.Take(3))}{(modules.Count > 3 ? "..." : "")})\n";
            status += $"🏠 Current Module: {(moduleManager.GetCurrentModule()?.name ?? "None")}\n";
        }

        status += $"💾 SaveLoadManager: {(saveLoadManager != null ? "✅ Connected" : "❌ Missing")}\n";
        if (saveLoadManager != null)
        {
            status += $"📁 Saved Positions: {saveLoadManager.GetSavedPositionCount()}\n";
            status += $"🎤 Voice Saves: {saveLoadManager.GetVoiceSavedModules().Count}\n";
        }

        status += "\n--- CURRENT STATE ---\n";
        status += $"Recording: {(isRecording ? "🎤 Active" : "⏸️ Inactive")}\n";
        status += $"Processing: {(isProcessing ? "📡 Active" : "⏸️ Inactive")}\n";
        status += $"Continuous: {(isListeningContinuously ? "🔄 Enabled" : "⏹️ Disabled")}\n";
        status += $"Quest 3 Optimized: {(useQuestOptimizedSettings ? "✅ On" : "❌ Off")}\n";
        status += $"Debug Logging: {(enableDebugLogging ? "✅ On" : "❌ Off")}\n";
        status += $"Skip Validation: {(skipCommandValidation ? "✅ Test Mode" : "❌ Normal")}\n";
        status += $"Haptic Feedback: {(enableHapticFeedback ? "✅ On" : "❌ Off")}\n";

        status += "\n--- VOICE COMMAND TYPES ---\n";
        status += "🔧 Component Visibility: hide/show architectural, structural, mep\n";
        status += "📦 Module Spawning: spawn/install/build module m1-1, m2-3, etc.\n";
        status += "💾 Save/Load: save/load current project\n";

        status += "\n--- SETTINGS ---\n";
        status += $"Primary Language: {azureLanguage} {(enableKoreanLanguage ? "(Korean+English)" : "(English Only)")}\n";
        status += $"Korean Support: {(enableKoreanLanguage ? "✅ Enabled" : "❌ Disabled")}\n";
        status += $"Language Restriction: {(restrictToKoreanEnglishOnly ? "✅ Korean/English Only" : "❌ All Languages")}\n";
        status += $"Confidence Threshold: {minimumConfidenceScore:F2}\n";
        status += $"Mic Frequency: {(useQuestOptimizedSettings ? questMicrophoneFrequency : 44100)}Hz\n";
        status += $"Max Recording: {maxRecordingDuration}s\n";
        status += $"Last Command: {(string.IsNullOrEmpty(lastProcessedCommand) ? "None" : lastProcessedCommand)}\n";
        status += $"Last Confidence: {lastCommandConfidence:F2}";

        return status;
    }

    /// <summary>
    /// Get the current transcription text for external scripts to access
    /// </summary>
    public string GetCurrentTranscription()
    {
        return currentTranscription;
    }

    /// <summary>
    /// Get the last confidence score for external scripts to access
    /// </summary>
    public float GetLastConfidenceScore()
    {
        return lastCommandConfidence;
    }

    /// <summary>
    /// Get the last processed command for external scripts to access
    /// </summary>
    public string GetLastProcessedCommand()
    {
        return lastProcessedCommand;
    }

    /// <summary>
    /// Check if the voice recognition system is currently recording
    /// </summary>
    public bool IsRecording()
    {
        return isRecording;
    }

    /// <summary>
    /// Check if the voice recognition system is currently processing
    /// </summary>
    public bool IsProcessing()
    {
        return isProcessing;
    }

    /// <summary>
    /// Check if continuous listening is active
    /// </summary>
    public bool IsContinuousListeningActive()
    {
        return isListeningContinuously;
    }

    #endregion

    #region Azure Speech Response Data Structures

    // Data structures for Azure Speech API response
    [System.Serializable]
    public class AzureSpeechResponse
    {
        public string RecognitionStatus;
        public string DisplayText;
        public int Offset;
        public int Duration;
        public AzureSpeechNBest[] NBest;
    }

    [System.Serializable]
    public class AzureSpeechNBest
    {
        public float Confidence;
        public string Lexical;
        public string ITN;
        public string MaskedITN;
        public string Display;
        public AzureSpeechWord[] Words;
    }

    [System.Serializable]
    public class AzureSpeechWord
    {
        public string Word;
        public int Offset;
        public int Duration;
    }

    #endregion

    private void OnDestroy()
    {
        if (useInputAction && voiceRecordAction != null)
        {
            voiceRecordAction.action.Disable();
        }

        StopContinuousListening();

        if (isRecording)
            StopVoiceRecording();

        if (currentApiRequest != null)
        {
            currentApiRequest.Dispose();
            currentApiRequest = null;
        }
    }

    private void OnDisable()
    {
        StopContinuousListening();

        if (isRecording)
            StopVoiceRecording();

        if (useInputAction && voiceRecordAction != null)
        {
            voiceRecordAction.action.Disable();
        }

        if (currentApiRequest != null)
        {
            currentApiRequest.Dispose();
            currentApiRequest = null;
        }
    }

    // === API Key Utilities (Whisper) ===
    private string SanitizeAPIKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;
        string trimmed = key.Trim();
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

    private string MaskAPIKey(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Length < 10) return "(not set)";
        return key.Substring(0, 6) + "..." + key.Substring(key.Length - 4);
    }
}