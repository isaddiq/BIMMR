using System;
using System.IO;
using UnityEngine;

namespace AEC.VoiceAssistant
{
    /// <summary>
    /// Single source of the LLM / speech provider API keys. Keys are never serialized into scenes,
    /// prefabs or assets. Lookup order for each key:
    ///   1. environment variable (OPENAI_API_KEY, ANTHROPIC_API_KEY, GOOGLE_API_KEY)
    ///   2. config.json in Application.persistentDataPath (on Quest: adb push to
    ///      /sdcard/Android/data/&lt;package&gt;/files/config.json)
    ///   3. Editor only: config.json in the Unity project root (next to Assets/)
    /// config.json is untracked (.gitignore); copy config.example.json and fill in your own keys.
    /// </summary>
    public static class ApiKeyProvider
    {
        [Serializable]
        private class KeyFile
        {
            public string openai_api_key = "";
            public string anthropic_api_key = "";
            public string google_api_key = "";
        }

        private const string FileName = "config.json";
        private static KeyFile cached;

        public static string OpenAI => Resolve("OPENAI_API_KEY", f => f.openai_api_key);
        public static string Anthropic => Resolve("ANTHROPIC_API_KEY", f => f.anthropic_api_key);
        public static string Google => Resolve("GOOGLE_API_KEY", f => f.google_api_key);

        private static string Resolve(string envVar, Func<KeyFile, string> pick)
        {
            string v = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
            v = pick(Load());
            return string.IsNullOrWhiteSpace(v) || v.StartsWith("YOUR_") ? "" : v.Trim();
        }

        private static KeyFile Load()
        {
            if (cached != null) return cached;
            cached = new KeyFile();
            foreach (string dir in CandidateDirs())
            {
                string path = Path.Combine(dir, FileName);
                if (!File.Exists(path)) continue;
                try
                {
                    cached = JsonUtility.FromJson<KeyFile>(File.ReadAllText(path)) ?? new KeyFile();
                    Debug.Log($"[ApiKeyProvider] Keys loaded from {path}");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[ApiKeyProvider] Could not read {path}: {e.Message}");
                }
                break;
            }
            return cached;
        }

        private static string[] CandidateDirs()
        {
#if UNITY_EDITOR
            return new[] { Application.persistentDataPath, Path.GetFullPath(Path.Combine(Application.dataPath, "..")) };
#else
            return new[] { Application.persistentDataPath };
#endif
        }
    }
}
