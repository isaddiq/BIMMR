// VoiceGPTBridge.cs - Speech-to-Text Bridge with TTS Control for AEC ChatGPT Integration
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;
using TMPro;
using UnityEngine.UI;
using Newtonsoft.Json;

namespace AEC.VoiceAssistant
{
    public class VoiceGPTBridge : MonoBehaviour
    {
        [Header("Component References")]
        [SerializeField] private AECGPTVoiceAssistant gptAssistant;

        [System.NonSerialized] private string openAIApiKey = "";   // never serialized: see ApiKeyProvider
        [Header("OpenAI Whisper Speech-to-Text Configuration")]
        [SerializeField] private string whisperModel = "whisper-1";
        [SerializeField] private string whisperLanguage = ""; // Leave empty for auto-detect, or "ko", "en", etc.
        [SerializeField] private float whisperTemperature = 0.0f;
        [SerializeField] private bool enableVerboseJson = false;

        [Header("Voice Input Actions")]
        [SerializeField] private InputActionReference chatGPTVoiceAction;
        [SerializeField] private bool useChatGPTInputAction = true;

        [Header("Recording Settings")]
        [SerializeField] private float maxRecordingDuration = 15f;
        [SerializeField] private int recordingFrequency = 16000;
        [SerializeField] private float singlePressThreshold = 0.3f; // Time to differentiate single press vs hold

        [Header("UI Feedback")]
        [SerializeField] private TMP_Text modeIndicatorText;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private Image statusIndicator;
        [SerializeField] private Color recordingColor = Color.red;
        [SerializeField] private Color processingColor = Color.yellow;
        [SerializeField] private Color readyColor = Color.green;
        [SerializeField] private Color errorColor = Color.red;

        public enum VoiceInputMode
        {
            Ready,
            Recording,
            Processing,
            Sending,
            ChatGPTInput
        }

        public event Action<string, float> OnSpeechRecognized;
        public event Action<string> OnMessageSentToChatGPT;
        public event Action<VoiceInputMode> OnModeChanged;
        public event Action OnTTSStopRequested;

        // Backward compatibility events
        public event Action<string, float> OnVoiceInputReceived;
        public event Action<string, float> OnChatGPTVoiceReceived;
        public event Action<string> OnVoiceInputForwarded;

        private VoiceInputMode currentMode = VoiceInputMode.Ready;
        private bool isRecording = false;
        private bool isProcessing = false;
        private bool wasButtonPressed = false;
        private float buttonPressTime = 0f;
        private AudioClip recordedClip;
        private Coroutine recordingCoroutine;
        private const string OPENAI_WHISPER_URL = "https://api.openai.com/v1/audio/transcriptions";

        private int totalRecordings = 0;
        private int successfulTranscriptions = 0;
        private int failedTranscriptions = 0;

        #region Unity Lifecycle

        private void Start()
        {
            InitializeBridge();
            SetupInputActions();
        }

        private void Update()
        {
            HandleVoiceInputAction();
            UpdateUI();
        }

        private void OnDestroy()
        {
            CleanupBridge();
        }

        #endregion

        #region Initialization

        private void InitializeBridge()
        {
            if (gptAssistant == null)
                gptAssistant = FindFirstObjectByType<AECGPTVoiceAssistant>();

            InitializeAzureSTT();
            UpdateStatusText("🎤 Ready");
            SetMode(VoiceInputMode.Ready);
        }

        private void InitializeAzureSTT() // Renamed to InitializeWhisperSTT but keeping method name for compatibility
        {
            if (string.IsNullOrEmpty(openAIApiKey)) openAIApiKey = ApiKeyProvider.OpenAI;
            if (!string.IsNullOrEmpty(openAIApiKey))
            {
                string cleaned = SanitizeKey(openAIApiKey);
                if (cleaned != openAIApiKey)
                {
                    BIMLog.Verbose("[VoiceGPTBridge] API key sanitized (whitespace/hidden chars removed)");
                    openAIApiKey = cleaned;
                }
            }

            if (!IsValidAPIKey(openAIApiKey))
            {
                UpdateStatusText("❌ Invalid / Missing API key");
                return;
            }

            UpdateStatusText("✅ Whisper STT ready");
        }

        private void SetupInputActions()
        {
            if (useChatGPTInputAction && chatGPTVoiceAction != null)
                chatGPTVoiceAction.action.Enable();
        }

        private void CleanupBridge()
        {
            if (useChatGPTInputAction && chatGPTVoiceAction != null)
                chatGPTVoiceAction.action.Disable();

            if (recordingCoroutine != null)
                StopCoroutine(recordingCoroutine);

            if (isRecording)
                StopRecording();
        }

        #endregion

        #region Voice Input Handling

        private void HandleVoiceInputAction()
        {
            if (!useChatGPTInputAction || chatGPTVoiceAction == null) return;

            bool currentButtonPressed = chatGPTVoiceAction.action.IsPressed();

            if (currentButtonPressed && !wasButtonPressed)
            {
                // Button pressed - stop TTS first
                OnTTSStopRequested?.Invoke();
                buttonPressTime = Time.time;

                if (currentMode == VoiceInputMode.Ready)
                {
                    // Start recording after a short delay to determine press type
                    StartCoroutine(DelayedRecordingStart());
                }
            }
            else if (!currentButtonPressed && wasButtonPressed)
            {
                float pressDuration = Time.time - buttonPressTime;

                if (pressDuration < singlePressThreshold)
                {
                    // Single press - just stop TTS
                    StopCoroutine(DelayedRecordingStart());
                    if (currentMode == VoiceInputMode.Recording)
                        StopRecording();
                }
                else if (currentMode == VoiceInputMode.Recording)
                {
                    // Hold released - stop recording
                    StopRecording();
                }
            }

            wasButtonPressed = currentButtonPressed;
        }

        private IEnumerator DelayedRecordingStart()
        {
            yield return new WaitForSeconds(singlePressThreshold);
            if (wasButtonPressed)
                StartRecording();
        }

        private void StartRecording()
        {
            if (isRecording || isProcessing) return;

            if (!IsValidAPIKey(openAIApiKey))
            {
                UpdateStatusText("❌ Invalid / Missing API key");
                return;
            }

            string microphoneName = Microphone.devices.Length > 0 ? Microphone.devices[0] : null;
            if (string.IsNullOrEmpty(microphoneName))
            {
                UpdateStatusText("❌ No microphone");
                return;
            }

            try
            {
                recordedClip = Microphone.Start(microphoneName, false, (int)maxRecordingDuration, recordingFrequency);
                isRecording = true;
                totalRecordings++;

                SetMode(VoiceInputMode.Recording);
                UpdateStatusText("🎤 Recording...");

                if (recordingCoroutine != null)
                    StopCoroutine(recordingCoroutine);
                recordingCoroutine = StartCoroutine(AutoStopRecording());
            }
            catch
            {
                UpdateStatusText("❌ Recording failed");
                isRecording = false;
                SetMode(VoiceInputMode.Ready);
            }
        }

        private IEnumerator AutoStopRecording()
        {
            yield return new WaitForSeconds(maxRecordingDuration);
            if (isRecording)
                StopRecording();
        }

        private void StopRecording()
        {
            if (!isRecording) return;

            string microphoneName = Microphone.devices.Length > 0 ? Microphone.devices[0] : null;

            try
            {
                int recordingPosition = Microphone.GetPosition(microphoneName);
                Microphone.End(microphoneName);

                if (recordingPosition > 0 && recordedClip != null && recordingPosition < recordedClip.samples)
                    recordedClip = TrimAudioClip(recordedClip, recordingPosition);

                isRecording = false;
                isProcessing = true;

                SetMode(VoiceInputMode.Processing);
                UpdateStatusText("📡 Processing...");

                if (recordingCoroutine != null)
                {
                    StopCoroutine(recordingCoroutine);
                    recordingCoroutine = null;
                }

                StartCoroutine(ProcessAudioWithAzure());
            }
            catch
            {
                UpdateStatusText("❌ Processing failed");
                isRecording = false;
                isProcessing = false;
                SetMode(VoiceInputMode.Ready);
            }
        }

        private AudioClip TrimAudioClip(AudioClip original, int newLength)
        {
            if (original == null || newLength <= 0) return original;

            try
            {
                float[] originalSamples = new float[original.samples * original.channels];
                original.GetData(originalSamples, 0);

                AudioClip trimmedClip = AudioClip.Create("TrimmedRecording", newLength, original.channels, original.frequency, false);
                float[] trimmedSamples = new float[newLength * original.channels];
                System.Array.Copy(originalSamples, trimmedSamples, trimmedSamples.Length);
                trimmedClip.SetData(trimmedSamples, 0);

                return trimmedClip;
            }
            catch
            {
                return original;
            }
        }

        #endregion

        #region OpenAI Whisper Processing

        private IEnumerator ProcessAudioWithAzure() // Renamed but keeping method name for compatibility
        {
            if (recordedClip == null)
            {
                UpdateStatusText("❌ No audio");
                isProcessing = false;
                SetMode(VoiceInputMode.Ready);
                yield break;
            }

            byte[] audioData = ConvertAudioClipToWAV(recordedClip);
            if (audioData == null || audioData.Length == 0)
            {
                UpdateStatusText("❌ Conversion failed");
                isProcessing = false;
                SetMode(VoiceInputMode.Ready);
                failedTranscriptions++;
                yield break;
            }

            // Build multipart form for OpenAI Whisper
            var formSections = new List<IMultipartFormSection>();
            formSections.Add(new MultipartFormFileSection("file", audioData, "recording.wav", "audio/wav"));
            formSections.Add(new MultipartFormDataSection("model", whisperModel));

            if (!string.IsNullOrEmpty(whisperLanguage))
                formSections.Add(new MultipartFormDataSection("language", whisperLanguage));

            if (whisperTemperature > 0f)
                formSections.Add(new MultipartFormDataSection("temperature", whisperTemperature.ToString(System.Globalization.CultureInfo.InvariantCulture)));

            formSections.Add(new MultipartFormDataSection("response_format", enableVerboseJson ? "verbose_json" : "json"));

            // U14: only the audio clip leaves the device for speech-to-text.
            BIMMR.Inspection.Study.OutboundPayloadLog.Write("stt", audioData.Length, false);

            using (UnityWebRequest request = UnityWebRequest.Post(OPENAI_WHISPER_URL, formSections))
            {
                request.SetRequestHeader("Authorization", $"Bearer {openAIApiKey}");
                request.timeout = 30;

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                    ProcessWhisperResponse(request.downloadHandler.text);
                else
                    HandleSpeechError(request.error, request.responseCode);
            }

            isProcessing = false;
        }

        private void ProcessSpeechResponse(string jsonResponse) // Legacy method kept for compatibility
        {
            // Redirect to Whisper processing
            ProcessWhisperResponse(jsonResponse);
        }

        private void ProcessWhisperResponse(string jsonResponse)
        {
            try
            {
                string transcription = null;

                // Try verbose format first if enabled
                if (enableVerboseJson)
                {
                    try
                    {
                        var verboseResponse = JsonConvert.DeserializeObject<WhisperVerboseResponse>(jsonResponse);
                        if (verboseResponse != null && !string.IsNullOrEmpty(verboseResponse.text))
                            transcription = verboseResponse.text.Trim();
                    }
                    catch { /* Fall back to basic parsing */ }
                }

                // Try basic format
                if (string.IsNullOrEmpty(transcription))
                {
                    try
                    {
                        var basicResponse = JsonConvert.DeserializeObject<WhisperBasicResponse>(jsonResponse);
                        if (basicResponse != null && !string.IsNullOrEmpty(basicResponse.text))
                            transcription = basicResponse.text.Trim();
                    }
                    catch { /* Parsing failed */ }
                }

                if (!string.IsNullOrEmpty(transcription))
                {
                    float confidence = 1.0f; // Whisper doesn't provide explicit confidence scores

                    OnSpeechRecognized?.Invoke(transcription, confidence);
                    OnVoiceInputReceived?.Invoke(transcription, confidence);
                    OnChatGPTVoiceReceived?.Invoke(transcription, confidence);

                    SendToChatGPT(transcription, confidence);
                }
                else
                {
                    UpdateStatusText("❌ No speech detected");
                    SetMode(VoiceInputMode.Ready);
                    failedTranscriptions++;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[VoiceGPTBridge] Whisper processing error: {ex.Message}");
                UpdateStatusText("❌ Processing failed");
                SetMode(VoiceInputMode.Ready);
                failedTranscriptions++;
            }
        }

        private void HandleSpeechError(string error, long responseCode)
        {
            UpdateStatusText($"❌ Error {responseCode}");
            SetMode(VoiceInputMode.Ready);
            failedTranscriptions++;
        }

        private void SendToChatGPT(string transcription, float confidence)
        {
            if (gptAssistant == null)
            {
                UpdateStatusText("❌ ChatGPT not available");
                SetMode(VoiceInputMode.Ready);
                return;
            }

            SetMode(VoiceInputMode.Sending);
            UpdateStatusText($"✅ Sending: \"{transcription}\"");

            gptAssistant.SendChatMessage(transcription);

            OnMessageSentToChatGPT?.Invoke(transcription);
            OnVoiceInputForwarded?.Invoke(transcription);

            successfulTranscriptions++;

            StartCoroutine(ReturnToReadyState());
        }

        private IEnumerator ReturnToReadyState()
        {
            yield return new WaitForSeconds(2f);
            SetMode(VoiceInputMode.Ready);
            UpdateStatusText("🎤 Ready");
        }

        #endregion

        #region Audio Conversion

        private byte[] ConvertAudioClipToWAV(AudioClip clip)
        {
            if (clip == null) return null;

            try
            {
                float[] samples = new float[clip.samples * clip.channels];
                clip.GetData(samples, 0);
                return ConvertSamplesToWAV(samples, clip.frequency, clip.channels);
            }
            catch
            {
                return null;
            }
        }

        private byte[] ConvertSamplesToWAV(float[] samples, int frequency, int channels)
        {
            byte[] wav = new byte[44 + samples.Length * 2];

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

            for (int i = 0; i < samples.Length; i++)
            {
                short sample = (short)(samples[i] * 32767f);
                System.BitConverter.GetBytes(sample).CopyTo(wav, 44 + i * 2);
            }

            return wav;
        }

        #endregion

        #region UI Management

        private void SetMode(VoiceInputMode mode)
        {
            currentMode = mode;
            OnModeChanged?.Invoke(mode);
        }

        private void UpdateUI()
        {
            UpdateModeIndicator();
            UpdateStatusIndicator();
        }

        private void UpdateModeIndicator()
        {
            if (modeIndicatorText != null)
            {
                modeIndicatorText.text = currentMode switch
                {
                    VoiceInputMode.Ready => "💬 Ready (Hold Button A)",
                    VoiceInputMode.Recording => "🎤 Recording",
                    VoiceInputMode.Processing => "📡 Processing",
                    VoiceInputMode.Sending => "🤖 Sending",
                    _ => ""
                };
            }
        }

        private void UpdateStatusIndicator()
        {
            if (statusIndicator != null)
            {
                statusIndicator.color = currentMode switch
                {
                    VoiceInputMode.Ready => readyColor,
                    VoiceInputMode.Recording => recordingColor,
                    VoiceInputMode.Processing => processingColor,
                    VoiceInputMode.Sending => readyColor,
                    _ => readyColor
                };
            }
        }

        private void UpdateStatusText(string status)
        {
            if (statusText != null)
                statusText.text = status;
        }

        #endregion

        #region Public Methods

        public bool IsReady() => currentMode == VoiceInputMode.Ready && !string.IsNullOrEmpty(openAIApiKey);
        public bool IsActive() => currentMode != VoiceInputMode.Ready;
        public VoiceInputMode GetCurrentMode() => currentMode;
        public string GetStatus() => $"Mode: {currentMode}, Recordings: {totalRecordings}, Success: {successfulTranscriptions}, Failed: {failedTranscriptions}";
        public bool IsBridgeActive() => IsReady();
        public bool IsChatGPTRecording() => currentMode == VoiceInputMode.Recording;
        public bool IsChatGPTProcessing() => currentMode == VoiceInputMode.Processing;

        public void TestVoiceInput()
        {
            if (currentMode == VoiceInputMode.Ready)
            {
                OnTTSStopRequested?.Invoke();
                string testMessage = "What building systems are currently active?";
                SendToChatGPT(testMessage, 1.0f);
            }
        }

        public void SetOpenAIKey(string key) // Renamed from SetAzureSTTKey for compatibility
        {
            openAIApiKey = SanitizeKey(key);
            if (IsValidAPIKey(openAIApiKey))
            {
                BIMLog.Verbose($"[VoiceGPTBridge] API key set (masked): {MaskKey(openAIApiKey)}");
            }
            else
            {
                Debug.LogWarning("[VoiceGPTBridge] API key set but appears invalid (must start with 'sk-' and be >20 chars)");
            }
            InitializeAzureSTT(); // Method name kept for compatibility
        }

        public void SetLanguage(string primaryLanguage, string secondaryLanguage = "")
        {
            whisperLanguage = primaryLanguage;
            // Note: OpenAI Whisper doesn't support multi-language detection in the same way as Azure
            // It auto-detects language when whisperLanguage is empty
        }

        public void ResetStatistics()
        {
            totalRecordings = 0;
            successfulTranscriptions = 0;
            failedTranscriptions = 0;
        }

        #endregion

        #region Data Structures

        [System.Serializable]
        public class WhisperBasicResponse
        {
            public string text;
        }

        [System.Serializable]
        public class WhisperVerboseResponse
        {
            public string text;
            public WhisperSegment[] segments;
        }

        [System.Serializable]
        public class WhisperSegment
        {
            public string text;
            public float start;
            public float end;
        }

        #endregion

        // --- API Key Utilities ---
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

        private bool IsValidAPIKey(string key)
        {
            return !string.IsNullOrEmpty(key) && key.StartsWith("sk-") && key.Length > 20;
        }

        private string MaskKey(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Length < 10) return "(not set)";
            return key.Substring(0, 6) + "..." + key.Substring(key.Length - 4);
        }
    }
}