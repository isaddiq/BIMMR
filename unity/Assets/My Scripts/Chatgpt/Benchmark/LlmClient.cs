// LlmClient.cs - Direct provider calls for the benchmark, with real token accounting.
//
// The benchmark does not route through the voice assistant: it needs an identical, minimal prompt
// across providers (no TTS hints, no "answer in 1-3 sentences", no language switch) and it needs the
// usage block from each API so that cost and throughput are measured rather than estimated.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

namespace AEC.Benchmark
{
    public enum LlmProvider { OpenAI, Anthropic, Google }

    [Serializable]
    public class ModelSpec
    {
        public bool Enabled = true;
        public string DisplayName;
        public LlmProvider Provider;
        public string ModelId;
        public string ApiVersion = "";
        public double InputPer1M;
        public double OutputPer1M;
        public int MaxOutputTokens = 800;

        public double CostOf(int promptTokens, int completionTokens)
            => (promptTokens * InputPer1M + completionTokens * OutputPer1M) / 1_000_000.0;

        public override string ToString() => $"{DisplayName} ({ModelId})";

        /// <summary>Model roster and list prices (USD per 1M tokens) as published on the access date.</summary>
        public static List<ModelSpec> DefaultRoster()
        {
            return new List<ModelSpec>
            {
                // cheapest first: the cost governor spends the budget on breadth before depth
                new ModelSpec { DisplayName = "Gemini-2.5-Flash-Lite", Provider = LlmProvider.Google,    ModelId = "gemini-2.5-flash-lite",     ApiVersion = "v1beta", InputPer1M = 0.10,  OutputPer1M = 0.40,  MaxOutputTokens = 1200 },
                new ModelSpec { DisplayName = "GPT-4o-Mini",           Provider = LlmProvider.OpenAI,    ModelId = "gpt-4o-mini",               ApiVersion = "",       InputPer1M = 0.15,  OutputPer1M = 0.60 },
                new ModelSpec { DisplayName = "GPT-5-Mini",            Provider = LlmProvider.OpenAI,    ModelId = "gpt-5-mini",                ApiVersion = "",       InputPer1M = 0.25,  OutputPer1M = 2.00,  MaxOutputTokens = 2000 },
                new ModelSpec { DisplayName = "Gemini-2.5-Flash",      Provider = LlmProvider.Google,    ModelId = "gemini-2.5-flash",          ApiVersion = "v1beta", InputPer1M = 0.30,  OutputPer1M = 2.50,  MaxOutputTokens = 1200 },
                new ModelSpec { DisplayName = "GPT-3.5-Turbo",         Provider = LlmProvider.OpenAI,    ModelId = "gpt-3.5-turbo",             ApiVersion = "",       InputPer1M = 0.50,  OutputPer1M = 1.50 },
                new ModelSpec { DisplayName = "Claude-Haiku-4.5",      Provider = LlmProvider.Anthropic, ModelId = "claude-haiku-4-5-20251001", ApiVersion = "2023-06-01", InputPer1M = 1.00, OutputPer1M = 5.00 },
                new ModelSpec { DisplayName = "Gemini-2.5-Pro",        Provider = LlmProvider.Google,    ModelId = "gemini-2.5-pro",            ApiVersion = "v1beta", InputPer1M = 1.25,  OutputPer1M = 10.00, MaxOutputTokens = 2000 },
                new ModelSpec { DisplayName = "GPT-4o",                Provider = LlmProvider.OpenAI,    ModelId = "gpt-4o",                    ApiVersion = "",       InputPer1M = 2.50,  OutputPer1M = 10.00 },
                new ModelSpec { DisplayName = "Claude-Sonnet-4.5",     Provider = LlmProvider.Anthropic, ModelId = "claude-sonnet-4-5-20250929", ApiVersion = "2023-06-01", InputPer1M = 3.00, OutputPer1M = 15.00 },
                new ModelSpec { DisplayName = "Claude-Sonnet-4.0",     Provider = LlmProvider.Anthropic, ModelId = "claude-sonnet-4-20250514",  ApiVersion = "2023-06-01", InputPer1M = 3.00, OutputPer1M = 15.00 },
                new ModelSpec { DisplayName = "GPT-4-Turbo",           Provider = LlmProvider.OpenAI,    ModelId = "gpt-4-turbo",               ApiVersion = "",       InputPer1M = 10.00, OutputPer1M = 30.00 },
                new ModelSpec { DisplayName = "Claude-Opus-4.1",       Provider = LlmProvider.Anthropic, ModelId = "claude-opus-4-1-20250805",  ApiVersion = "2023-06-01", InputPer1M = 15.00, OutputPer1M = 75.00 },
                new ModelSpec { DisplayName = "GPT-4",                 Provider = LlmProvider.OpenAI,    ModelId = "gpt-4",                     ApiVersion = "",       InputPer1M = 30.00, OutputPer1M = 60.00 },
            };
        }
    }

    public class LlmTurn
    {
        public string Role;      // "user" | "assistant"
        public string Content;
    }

    public class LlmResult
    {
        public bool Success;
        public string Text = "";
        public string Error = "";
        public int PromptTokens;
        public int CompletionTokens;
        public double LatencyMs;
        public int Retries;
        public double CostUsd;
        public long HttpStatus;
    }

    /// <summary>Hard spend limit for a benchmark session; refuses work it cannot pay for.</summary>
    public class CostGovernor
    {
        public double BudgetUsd { get; }
        public double SpentUsd { get; private set; }
        public double RemainingUsd => Math.Max(0, BudgetUsd - SpentUsd);
        public bool Exhausted => SpentUsd >= BudgetUsd;

        public CostGovernor(double budgetUsd) { BudgetUsd = budgetUsd; }

        public void Record(double cost) { SpentUsd += cost; }

        public bool CanAfford(double projected, double safetyMargin = 1.10)
            => SpentUsd + projected * safetyMargin <= BudgetUsd;

        public double ProjectModelCost(ModelSpec spec, int calls, int avgPromptTokens, int avgCompletionTokens)
            => calls * spec.CostOf(avgPromptTokens, avgCompletionTokens);
    }

    public class LlmClient
    {
        private readonly Dictionary<LlmProvider, string> _keys;
        private readonly float _temperature;
        private readonly int _maxRetries;
        private readonly float _requestTimeoutSeconds;

        public LlmClient(Dictionary<LlmProvider, string> keys, float temperature, int maxRetries, float timeoutSeconds)
        {
            _keys = keys;
            _temperature = temperature;
            _maxRetries = Mathf.Max(0, maxRetries);
            _requestTimeoutSeconds = timeoutSeconds;
        }

        public bool HasKey(LlmProvider provider)
            => _keys.TryGetValue(provider, out var k) && !string.IsNullOrWhiteSpace(k);

        public IEnumerator Send(ModelSpec spec, string systemPrompt, List<LlmTurn> turns, Action<LlmResult> onComplete)
        {
            var result = new LlmResult();
            if (!HasKey(spec.Provider))
            {
                result.Error = $"No API key configured for {spec.Provider}";
                onComplete?.Invoke(result);
                yield break;
            }

            for (int attempt = 0; attempt <= _maxRetries; attempt++)
            {
                result.Retries = attempt;
                var stopwatch = Stopwatch.StartNew();

                using (var request = BuildRequest(spec, systemPrompt, turns))
                {
                    request.timeout = Mathf.CeilToInt(_requestTimeoutSeconds);
                    yield return request.SendWebRequest();
                    stopwatch.Stop();

                    result.LatencyMs = stopwatch.Elapsed.TotalMilliseconds;
                    result.HttpStatus = request.responseCode;

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        try
                        {
                            ParseResponse(spec, request.downloadHandler.text, result);
                            result.Success = !string.IsNullOrEmpty(result.Text);
                            if (!result.Success) result.Error = "Empty completion";
                        }
                        catch (Exception ex)
                        {
                            result.Success = false;
                            result.Error = $"Parse error: {ex.Message}";
                        }

                        if (result.Success)
                        {
                            result.CostUsd = spec.CostOf(result.PromptTokens, result.CompletionTokens);
                            onComplete?.Invoke(result);
                            yield break;
                        }
                    }
                    else
                    {
                        result.Error = $"HTTP {request.responseCode}: {request.error} | " +
                                       Truncate(request.downloadHandler?.text, 300);
                    }
                }

                bool retryable = result.HttpStatus == 429 || result.HttpStatus >= 500 || result.HttpStatus == 0;
                if (!retryable || attempt == _maxRetries) break;

                float backoff = Mathf.Pow(2, attempt + 1);   // 2s, 4s, 8s
                Debug.LogWarning($"[Benchmark] {spec.DisplayName} retry {attempt + 1} in {backoff}s ({result.Error})");
                yield return new WaitForSeconds(backoff);
            }

            onComplete?.Invoke(result);
        }

        // ---------------------------------------------------------- requests

        private UnityWebRequest BuildRequest(ModelSpec spec, string systemPrompt, List<LlmTurn> turns)
        {
            string url;
            string body;

            switch (spec.Provider)
            {
                case LlmProvider.OpenAI:
                {
                    url = "https://api.openai.com/v1/chat/completions";
                    var messages = new List<object> { new { role = "system", content = systemPrompt } };
                    foreach (var t in turns) messages.Add(new { role = t.Role, content = t.Content });

                    var payload = new Dictionary<string, object>
                    {
                        { "model", spec.ModelId },
                        { "messages", messages }
                    };
                    // Reasoning-era models only accept the default temperature and a renamed token cap.
                    if (UsesCompletionTokenField(spec.ModelId))
                        payload["max_completion_tokens"] = spec.MaxOutputTokens;
                    else
                    {
                        payload["max_tokens"] = spec.MaxOutputTokens;
                        payload["temperature"] = _temperature;
                    }
                    body = JsonConvert.SerializeObject(payload);
                    break;
                }

                case LlmProvider.Anthropic:
                {
                    url = "https://api.anthropic.com/v1/messages";
                    var messages = new List<object>();
                    foreach (var t in turns) messages.Add(new { role = t.Role, content = t.Content });

                    body = JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        { "model", spec.ModelId },
                        { "max_tokens", spec.MaxOutputTokens },
                        { "temperature", _temperature },
                        { "system", systemPrompt },
                        { "messages", messages }
                    });
                    break;
                }

                default:
                {
                    url = $"https://generativelanguage.googleapis.com/{(string.IsNullOrEmpty(spec.ApiVersion) ? "v1beta" : spec.ApiVersion)}" +
                          $"/models/{spec.ModelId}:generateContent?key={_keys[LlmProvider.Google]}";

                    var contents = new List<object>();
                    foreach (var t in turns)
                        contents.Add(new { role = t.Role == "assistant" ? "model" : "user", parts = new[] { new { text = t.Content } } });

                    var generationConfig = new Dictionary<string, object>
                    {
                        { "temperature", _temperature },
                        { "maxOutputTokens", spec.MaxOutputTokens }
                    };
                    // Flash tiers allow thinking to be switched off; otherwise reasoning tokens can
                    // consume the whole output budget and return an empty candidate.
                    if (spec.ModelId.IndexOf("flash", StringComparison.OrdinalIgnoreCase) >= 0)
                        generationConfig["thinkingConfig"] = new Dictionary<string, object> { { "thinkingBudget", 0 } };

                    body = JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        { "system_instruction", new { parts = new[] { new { text = systemPrompt } } } },
                        { "contents", contents },
                        { "generationConfig", generationConfig }
                    });
                    break;
                }
            }

            var www = new UnityWebRequest(url, "POST")
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
                downloadHandler = new DownloadHandlerBuffer()
            };
            www.SetRequestHeader("Content-Type", "application/json");

            if (spec.Provider == LlmProvider.OpenAI)
                www.SetRequestHeader("Authorization", "Bearer " + _keys[LlmProvider.OpenAI]);
            else if (spec.Provider == LlmProvider.Anthropic)
            {
                www.SetRequestHeader("x-api-key", _keys[LlmProvider.Anthropic]);
                www.SetRequestHeader("anthropic-version", string.IsNullOrEmpty(spec.ApiVersion) ? "2023-06-01" : spec.ApiVersion);
            }

            return www;
        }

        private static bool UsesCompletionTokenField(string modelId)
        {
            return modelId.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase) ||
                   modelId.StartsWith("o1", StringComparison.OrdinalIgnoreCase) ||
                   modelId.StartsWith("o3", StringComparison.OrdinalIgnoreCase) ||
                   modelId.StartsWith("o4", StringComparison.OrdinalIgnoreCase);
        }

        private static void ParseResponse(ModelSpec spec, string json, LlmResult result)
        {
            var root = JObject.Parse(json);

            switch (spec.Provider)
            {
                case LlmProvider.OpenAI:
                    result.Text = root["choices"]?[0]?["message"]?["content"]?.ToString() ?? "";
                    result.PromptTokens = root["usage"]?["prompt_tokens"]?.Value<int>() ?? 0;
                    result.CompletionTokens = root["usage"]?["completion_tokens"]?.Value<int>() ?? 0;
                    break;

                case LlmProvider.Anthropic:
                {
                    var content = root["content"] as JArray;
                    if (content != null)
                    {
                        var sb = new StringBuilder();
                        foreach (var block in content)
                            if ((block["type"]?.ToString() ?? "text") == "text") sb.Append(block["text"]?.ToString());
                        result.Text = sb.ToString();
                    }
                    result.PromptTokens = root["usage"]?["input_tokens"]?.Value<int>() ?? 0;
                    result.CompletionTokens = root["usage"]?["output_tokens"]?.Value<int>() ?? 0;
                    break;
                }

                default:
                {
                    var parts = root["candidates"]?[0]?["content"]?["parts"] as JArray;
                    if (parts != null)
                    {
                        var sb = new StringBuilder();
                        foreach (var p in parts) sb.Append(p["text"]?.ToString());
                        result.Text = sb.ToString();
                    }
                    result.PromptTokens = root["usageMetadata"]?["promptTokenCount"]?.Value<int>() ?? 0;
                    result.CompletionTokens = (root["usageMetadata"]?["candidatesTokenCount"]?.Value<int>() ?? 0) +
                                              (root["usageMetadata"]?["thoughtsTokenCount"]?.Value<int>() ?? 0);
                    break;
                }
            }
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max) + "...";
        }
    }
}
