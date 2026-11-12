// AECGPTInspectorHelper.cs - Unity Inspector for AEC Voice Chat
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

namespace AEC.VoiceAssistant
{
    [System.Serializable]
    public class AECGPTInspectorHelper : MonoBehaviour
    {
        [Header("Component References")]
        [SerializeField] private AECGPTVoiceAssistant gptAssistant;
        [SerializeField] private VoiceGPTBridge voiceBridge;
        [SerializeField] private ModuleManager moduleManager;

        [Header("Chat Interface")]
        [SerializeField] private ScrollRect chatScrollRect;
        [SerializeField] private TMP_InputField chatInputField;
        [SerializeField] private Button sendMessageButton;
        [SerializeField] private Button voiceToggleButton;

        private TextMeshProUGUI chatDisplay;

        [Header("Message Styling")]
        [SerializeField] private Color userMessageColor = new Color(0.2f, 0.6f, 1f, 1f);
        [SerializeField] private Color userTextColor = new Color(1f, 1f, 1f, 1f);
        [SerializeField] private Color assistantMessageColor = new Color(0.1f, 0.7f, 0.1f, 1f);
        [SerializeField] private Color assistantTextColor = new Color(0.9f, 0.9f, 0.9f, 1f);
        [SerializeField] private Color systemMessageColor = new Color(0.6f, 0.6f, 0.6f, 1f);
        [SerializeField] private Color analysisMessageColor = new Color(1f, 0.7f, 0.2f, 1f);

        [Header("Controls")]
        [SerializeField] private Toggle enableTTSToggle;
        [SerializeField] private Slider volumeSlider;
        [SerializeField] private Button clearChatButton;

        [Header("Status")]
        [SerializeField] private TMP_Text systemStatusText;
        [SerializeField] private Image statusIndicator;
        [SerializeField] private Color connectedColor = Color.green;
        [SerializeField] private Color processingColor = Color.yellow;
        [SerializeField] private Color errorColor = Color.red;

        [Header("Auto-Scroll Settings")]
        [SerializeField] private bool enableAutoScroll = true;
        [SerializeField] private float scrollDelay = 0.1f;

        private bool isInitialized = false;

        private void Start()
        {
            StartCoroutine(InitializeDelayed());
        }

        private IEnumerator InitializeDelayed()
        {
            yield return null;

            if (gptAssistant == null)
                gptAssistant = FindFirstObjectByType<AECGPTVoiceAssistant>();

            if (voiceBridge == null)
                voiceBridge = FindFirstObjectByType<VoiceGPTBridge>();

            if (moduleManager == null)
                moduleManager = FindAnyObjectByType<ModuleManager>();

            SetupChatDisplay();
            SetupUI();
            isInitialized = true;

            StartCoroutine(UpdateStatus());
        }

        private void SetupChatDisplay()
        {
            if (chatScrollRect != null && chatScrollRect.content != null)
            {
                chatDisplay = chatScrollRect.content.GetComponent<TextMeshProUGUI>();
                if (chatDisplay == null)
                    chatDisplay = chatScrollRect.GetComponentInChildren<TextMeshProUGUI>();

                ContentSizeFitter sizeFitter = chatScrollRect.content.GetComponent<ContentSizeFitter>();
                if (sizeFitter == null)
                {
                    sizeFitter = chatScrollRect.content.gameObject.AddComponent<ContentSizeFitter>();
                }
                sizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }

            if (chatDisplay != null)
            {
                chatDisplay.text = "";
                AppendToChatDisplay("<color=#888888><i>🏗️ AEC Assistant Ready</i></color>");
            }
        }

        private void SetupUI()
        {
            if (sendMessageButton != null)
                sendMessageButton.onClick.AddListener(SendChatMessage);

            if (chatInputField != null)
            {
                chatInputField.onEndEdit.AddListener(OnChatInputEnter);
                if (chatInputField.placeholder != null)
                {
                    var placeholderText = chatInputField.placeholder.GetComponent<TMP_Text>();
                    if (placeholderText != null)
                        placeholderText.text = "Ask about construction systems, modules, or BIM coordination...";
                }
            }

            if (voiceToggleButton != null)
                voiceToggleButton.onClick.AddListener(ToggleVoiceInput);

            if (enableTTSToggle != null)
            {
                enableTTSToggle.isOn = true;
                enableTTSToggle.onValueChanged.AddListener(OnTTSToggleChanged);
            }

            if (volumeSlider != null)
            {
                volumeSlider.value = 0.8f;
                volumeSlider.onValueChanged.AddListener(OnVolumeChanged);
            }

            if (clearChatButton != null)
                clearChatButton.onClick.AddListener(ClearChat);

            if (gptAssistant != null)
            {
                gptAssistant.OnUserMessageSent += DisplayUserMessage;
                gptAssistant.OnAssistantMessageReceived += DisplayAssistantMessage;
            }

            if (voiceBridge != null)
            {
                voiceBridge.OnSpeechRecognized += OnSpeechRecognized;
                voiceBridge.OnMessageSentToChatGPT += OnMessageSentToChatGPT;
                voiceBridge.OnModeChanged += OnVoiceModeChanged;
            }
        }

        private void Update()
        {
            if (!isInitialized) return;

            if (chatInputField != null && chatInputField.isFocused && Input.GetKeyDown(KeyCode.Return))
                SendChatMessage();
        }

        private void OnChatInputEnter(string text)
        {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                SendChatMessage();
        }

        private void SendChatMessage()
        {
            if (gptAssistant == null || chatInputField == null)
                return;

            string message = chatInputField.text.Trim();
            if (!string.IsNullOrEmpty(message))
            {
                gptAssistant.SendMessage(message);
                chatInputField.text = "";
                chatInputField.ActivateInputField();
            }
        }

        private void ToggleVoiceInput()
        {
            if (voiceBridge != null)
            {
                voiceBridge.TestVoiceInput();
                UpdateVoiceButtonText();
            }
        }

        private void OnTTSToggleChanged(bool enabled)
        {
            if (gptAssistant != null)
                gptAssistant.SetTTSEnabled(enabled);
        }

        private void OnVolumeChanged(float volume)
        {
            if (gptAssistant != null)
                gptAssistant.SetSpeechVolume(volume);
        }

        private void ClearChat()
        {
            if (chatDisplay != null)
            {
                chatDisplay.text = "";
                AppendToChatDisplay("<color=#888888><i>🏗️ Chat cleared</i></color>");
            }
        }

        private void OnSpeechRecognized(string transcription, float confidence) { }
        private void OnMessageSentToChatGPT(string message) { }
        private void OnVoiceModeChanged(VoiceGPTBridge.VoiceInputMode mode) => UpdateVoiceButtonText();

        private void DisplayUserMessage(string message)
        {
            string labelColorHex = ColorUtility.ToHtmlStringRGB(userMessageColor);
            string textColorHex = ColorUtility.ToHtmlStringRGB(userTextColor);
            AppendToChatDisplay($"<color=#{labelColorHex}><b>You:</b></color> <color=#{textColorHex}>{message}</color>");
        }

        private void DisplayAssistantMessage(string message)
        {
            string labelColorHex = ColorUtility.ToHtmlStringRGB(assistantMessageColor);
            string textColorHex = ColorUtility.ToHtmlStringRGB(assistantTextColor);
            AppendToChatDisplay($"<color=#{labelColorHex}><b>🏗️ AEC Assistant:</b></color> <color=#{textColorHex}>{message}</color>");
        }

        private void AppendToChatDisplay(string text)
        {
            if (chatDisplay == null) return;

            if (string.IsNullOrEmpty(chatDisplay.text))
                chatDisplay.text = text;
            else
                chatDisplay.text += "\n\n" + text;

            if (enableAutoScroll)
                StartCoroutine(ScrollToBottomDelayed());
        }

        private IEnumerator ScrollToBottomDelayed()
        {
            if (chatScrollRect == null) yield break;

            yield return new WaitForEndOfFrame();
            yield return new WaitForSeconds(scrollDelay);

            Canvas.ForceUpdateCanvases();
            yield return null;

            chatScrollRect.verticalNormalizedPosition = 0f;
            Canvas.ForceUpdateCanvases();
            yield return null;
            chatScrollRect.verticalNormalizedPosition = 0f;
        }

        private void UpdateVoiceButtonText()
        {
            if (voiceToggleButton != null && voiceBridge != null)
            {
                var buttonText = voiceToggleButton.GetComponentInChildren<TMP_Text>();
                if (buttonText != null)
                {
                    bool isActive = voiceBridge.IsBridgeActive();
                    buttonText.text = isActive ? "🎤 Voice ON" : "🎤 Voice OFF";

                    var buttonImage = voiceToggleButton.GetComponent<Image>();
                    if (buttonImage != null)
                        buttonImage.color = isActive ? connectedColor : Color.gray;
                }
            }
        }

        private IEnumerator UpdateStatus()
        {
            while (true)
            {
                if (isInitialized)
                    UpdateSystemStatus();
                yield return new WaitForSeconds(2f);
            }
        }

        private void UpdateSystemStatus()
        {
            if (systemStatusText == null) return;

            bool gptReady = gptAssistant != null && gptAssistant.IsReady();
            bool voiceReady = voiceBridge != null;
            bool processing = gptAssistant != null && gptAssistant.IsProcessing();
            bool bridgeActive = voiceBridge != null && voiceBridge.IsBridgeActive();

            string status = "";
            Color indicatorColor = connectedColor;

            // Get current AI provider info
            string providerInfo = "";
            if (gptAssistant != null)
            {
                var provider = gptAssistant.GetCurrentProvider();
                string providerName = provider switch
                {
                    AEC.VoiceAssistant.AIProvider.OpenAI => "GPT",
                    AEC.VoiceAssistant.AIProvider.Anthropic => "Claude",
                    AEC.VoiceAssistant.AIProvider.Google => "Gemini",
                    _ => "AI"
                };
                providerInfo = $"[{providerName}] ";
            }

            if (processing)
            {
                status = providerInfo + "🤖 Processing...";
                indicatorColor = processingColor;
            }
            else if (gptReady && voiceReady)
            {
                if (bridgeActive)
                {
                    var currentMode = voiceBridge.GetCurrentMode();
                    status = currentMode == VoiceGPTBridge.VoiceInputMode.ChatGPTInput
                        ? providerInfo + "🎤 Ready (Hold Button A)"
                        : providerInfo + "✅ Ready for AEC commands";
                }
                else
                {
                    status = providerInfo + "✅ Ready (Voice disabled)";
                }
                indicatorColor = connectedColor;
            }
            else
            {
                status = "❌ ";
                if (!gptReady)
                {
                    if (gptAssistant != null)
                    {
                        string apiStatus = gptAssistant.GetAPIKeyStatus();
                        if (apiStatus.Contains("not set"))
                            status += "API Key Missing ";
                        else if (apiStatus.Contains("Invalid"))
                            status += "Invalid API Key ";
                        else
                            status += providerInfo;
                    }
                    else
                    {
                        status += "AI ";
                    }
                }
                if (!voiceReady) status += "Voice ";
                status += "Not Ready";
                indicatorColor = errorColor;
            }

            systemStatusText.text = status;

            if (statusIndicator != null)
                statusIndicator.color = indicatorColor;
        }

        public void AddSystemMessage(string message)
        {
            string colorHex = ColorUtility.ToHtmlStringRGB(systemMessageColor);
            AppendToChatDisplay($"<color=#{colorHex}><i>System: {message}</i></color>");
        }

        public void AddAnalysisMessage(string message)
        {
            string colorHex = ColorUtility.ToHtmlStringRGB(analysisMessageColor);
            AppendToChatDisplay($"<color=#{colorHex}><b>📊 Analysis:</b> {message}</color>");
        }

        public void AddErrorMessage(string message)
        {
            string colorHex = ColorUtility.ToHtmlStringRGB(errorColor);
            AppendToChatDisplay($"<color=#{colorHex}><b>❌ Error:</b> {message}</color>");
        }

        public void ShowAPIKeyHelp()
        {
            AddErrorMessage("OpenAI API key required!");
            AddSystemMessage("To fix this issue:");
            AddSystemMessage("1. Get an API key from: https://platform.openai.com/api-keys");
            AddSystemMessage("2. In Unity Inspector, find 'AEC GPT Voice Assistant' component");
            AddSystemMessage("3. Paste your API key in the 'Open AIApi Key' field");
            AddSystemMessage("4. API keys start with 'sk-' and are about 50+ characters long");
        }

        public void SetAutoScroll(bool enabled) => enableAutoScroll = enabled;
        public void SetScrollDelay(float delay) => scrollDelay = Mathf.Clamp(delay, 0f, 1f);

        private void OnDestroy()
        {
            if (gptAssistant != null)
            {
                gptAssistant.OnUserMessageSent -= DisplayUserMessage;
                gptAssistant.OnAssistantMessageReceived -= DisplayAssistantMessage;
            }

            if (voiceBridge != null)
            {
                voiceBridge.OnSpeechRecognized -= OnSpeechRecognized;
                voiceBridge.OnMessageSentToChatGPT -= OnMessageSentToChatGPT;
                voiceBridge.OnModeChanged -= OnVoiceModeChanged;
            }

            if (sendMessageButton != null)
                sendMessageButton.onClick.RemoveAllListeners();

            if (voiceToggleButton != null)
                voiceToggleButton.onClick.RemoveAllListeners();

            if (enableTTSToggle != null)
                enableTTSToggle.onValueChanged.RemoveAllListeners();

            if (volumeSlider != null)
                volumeSlider.onValueChanged.RemoveAllListeners();

            if (clearChatButton != null)
                clearChatButton.onClick.RemoveAllListeners();

            if (chatInputField != null)
                chatInputField.onEndEdit.RemoveAllListeners();
        }
    }
}