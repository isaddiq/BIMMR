using System;
using System.Linq;

namespace BIMMR.Inspection.Study
{
    /// <summary>
    /// U13: voice_live.csv, one row per live utterance during a session:
    ///   utterance_ts, language, transcript, recognized_intent, intended_intent (filled by the
    ///   researcher from the task script), confidence, ambient_dBA_estimate, latency_ms.
    ///
    /// The ambient level is estimated from the headset microphone as the 10th percentile of
    /// 20 ms frame RMS levels (the noise floor between words), in dBFS, plus a calibration
    /// offset measured once against a sound level meter. Uncalibrated estimates are marked
    /// level_calibrated = false and must only be used to rank noise bands, not as dB(A).
    /// </summary>
    public static class VoiceLiveLog
    {
        private static readonly CsvLog Log = new CsvLog("voice_live.csv", true,
            "utterance_ts", "language", "transcript", "recognized_intent", "intended_intent",
            "confidence", "ambient_dBA_estimate", "level_calibrated", "latency_ms", "stt_endpoint");

        public static void Write(DateTime utteranceStartUtc, string transcript, string recognizedIntent,
                                 float confidence, float ambientDba, bool calibrated, double latencyMs,
                                 string sttEndpoint)
        {
            Log.Write(StudySession.Iso(utteranceStartUtc), DetectLanguage(transcript), transcript,
                      recognizedIntent, "", confidence, Math.Round(ambientDba, 1), calibrated,
                      Math.Round(latencyMs), sttEndpoint);
        }

        /// <summary>Noise floor in dBFS: 10th percentile of 20 ms frame RMS.</summary>
        public static float NoiseFloorDbfs(float[] samples, int frequency, int channels)
        {
            if (samples == null || samples.Length == 0 || frequency <= 0) return -120f;
            int frame = Math.Max(1, frequency * Math.Max(1, channels) / 50);
            int n = samples.Length / frame;
            if (n == 0) return -120f;
            var rms = new float[n];
            for (int f = 0; f < n; f++)
            {
                double acc = 0;
                for (int i = f * frame; i < (f + 1) * frame; i++) acc += samples[i] * samples[i];
                rms[f] = (float)Math.Sqrt(acc / frame);
            }
            Array.Sort(rms);
            float p10 = Math.Max(rms[(int)(0.1 * (n - 1))], 1e-6f);
            return 20f * (float)Math.Log10(p10);
        }

        /// <summary>Intent label from the command keyword table; "none" when the transcript
        /// is not a valid command.</summary>
        public static string ClassifyIntent(string transcript, bool isValidCommand)
        {
            if (!isValidCommand || string.IsNullOrWhiteSpace(transcript)) return "none";
            string t = transcript.ToLowerInvariant();
            var hit = VoiceCommandKeywords.ActionKeywords
                .Where(kv => t.Contains(kv.Key.ToLowerInvariant()))
                .OrderByDescending(kv => kv.Key.Length)
                .Select(kv => kv.Value.ToString().ToLowerInvariant())
                .FirstOrDefault();
            return hit ?? "command";
        }

        public static string DetectLanguage(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            int ko = text.Count(c => c >= 0xAC00 && c <= 0xD7A3);
            return ko > 0 ? "ko" : "en";
        }
    }
}
