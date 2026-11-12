// LLMEvaluationModeEditor.cs - Custom Inspector for LLM Evaluation Mode
#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;

namespace AEC.VoiceAssistant
{
    [CustomEditor(typeof(LLMEvaluationMode))]
    public class LLMEvaluationModeEditor : Editor
    {
        private SerializedProperty enableAutoEvaluation;
        private SerializedProperty evaluateOnStart;
        private SerializedProperty delayBetweenQuestions;
        private SerializedProperty delayBetweenModels;
        private SerializedProperty gptAssistant;
        private SerializedProperty moduleManager;
        private SerializedProperty metadataViewer;
        private SerializedProperty outputDirectory;
        private SerializedProperty runsPerPrompt;

        private void OnEnable()
        {
            enableAutoEvaluation = serializedObject.FindProperty("enableAutoEvaluation");
            evaluateOnStart = serializedObject.FindProperty("evaluateOnStart");
            delayBetweenQuestions = serializedObject.FindProperty("delayBetweenQuestions");
            delayBetweenModels = serializedObject.FindProperty("delayBetweenModels");
            gptAssistant = serializedObject.FindProperty("gptAssistant");
            moduleManager = serializedObject.FindProperty("moduleManager");
            metadataViewer = serializedObject.FindProperty("metadataViewer");
            outputDirectory = serializedObject.FindProperty("outputDirectory");
            runsPerPrompt = serializedObject.FindProperty("runsPerPrompt");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            LLMEvaluationMode evaluator = (LLMEvaluationMode)target;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("LLM Evaluation Mode", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Automatically evaluates all LLM models against 20 standardized AEC/BIM questions.\n" +
                "Runs multiple iterations per question for ANOVA statistical analysis.\n" +
                "Generates metrics, rankings, statistical reports, and interactive visualizations.",
                MessageType.Info);

            EditorGUILayout.Space(10);

            // Evaluation Status
            if (Application.isPlaying)
            {
                DrawStatusSection(evaluator);
                EditorGUILayout.Space(10);
            }

            // Configuration
            EditorGUILayout.LabelField("Configuration", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(enableAutoEvaluation, new GUIContent("Enable Auto Evaluation"));

            if (enableAutoEvaluation.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(evaluateOnStart, new GUIContent("Evaluate On Start"));
                EditorGUILayout.PropertyField(delayBetweenQuestions, new GUIContent("Delay Between Questions (s)"));
                EditorGUILayout.PropertyField(delayBetweenModels, new GUIContent("Delay Between Models (s)"));
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(10);

            // ANOVA Configuration
            EditorGUILayout.LabelField("ANOVA Configuration", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(runsPerPrompt, new GUIContent("Runs Per Question", "Number of times each question is asked to each model for statistical analysis"));

            if (runsPerPrompt.intValue < 1)
                runsPerPrompt.intValue = 1;
            if (runsPerPrompt.intValue > 10)
            {
                EditorGUILayout.HelpBox("Warning: High run count will significantly increase evaluation time.", MessageType.Warning);
            }

            // Calculate and display total evaluations
            int totalModels = 13; // Approximate - actual count determined at runtime
            int totalQuestions = 20;
            int totalEvaluations = totalModels * totalQuestions * runsPerPrompt.intValue;
            EditorGUILayout.HelpBox(
                $"Estimated Total Evaluations: {totalModels} models × {totalQuestions} questions × {runsPerPrompt.intValue} runs = {totalEvaluations} total API calls\n" +
                $"Estimated Time: {(totalEvaluations * 3 / 60):F0}-{(totalEvaluations * 5 / 60):F0} minutes",
                MessageType.Info);

            EditorGUILayout.Space(10);

            // Component References
            EditorGUILayout.LabelField("Component References", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(gptAssistant);
            EditorGUILayout.PropertyField(moduleManager);
            EditorGUILayout.PropertyField(metadataViewer);

            EditorGUILayout.Space(10);

            // Output Settings
            EditorGUILayout.LabelField("Output Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(outputDirectory);

            string fullPath = Path.Combine(Application.persistentDataPath, outputDirectory.stringValue);
            EditorGUILayout.HelpBox($"Output Path: {fullPath}", MessageType.None);

            EditorGUILayout.Space(10);

            // Control Buttons
            DrawControlButtons(evaluator);

            // Evaluation Questions Preview
            EditorGUILayout.Space(10);
            DrawQuestionsPreview();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawStatusSection(LLMEvaluationMode evaluator)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Evaluation Status", EditorStyles.boldLabel);

            if (evaluator.IsEvaluating())
            {
                EditorGUILayout.LabelField("Status:", "⏳ Evaluation Running...", EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("Session ID:", evaluator.GetCurrentSessionId());
            }
            else
            {
                EditorGUILayout.LabelField("Status:", "✅ Ready");
            }

            var results = evaluator.GetResults();
            if (results != null && results.Count > 0)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField($"Models Evaluated: {results.Count}");

                foreach (var kvp in results)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField($"  • {kvp.Key}:", GUILayout.Width(200));
                    EditorGUILayout.LabelField($"Success: {kvp.Value.TaskSuccessRate:P1}, Cost: ${kvp.Value.TotalCostUSD:F4}");
                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawControlButtons(LLMEvaluationMode evaluator)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Manual Controls", EditorStyles.boldLabel);

            EditorGUI.BeginDisabledGroup(!Application.isPlaying || evaluator.IsEvaluating());
            if (GUILayout.Button("▶ Start Evaluation", GUILayout.Height(30)))
            {
                evaluator.StartEvaluation();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(5);

            if (GUILayout.Button("📂 Open Output Folder", GUILayout.Height(25)))
            {
                string path = Path.Combine(Application.persistentDataPath, outputDirectory.stringValue);
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
                EditorUtility.RevealInFinder(path);
            }

            if (Application.isPlaying && !string.IsNullOrEmpty(evaluator.GetCurrentSessionId()))
            {
                if (GUILayout.Button("📊 Open Current Session Folder", GUILayout.Height(25)))
                {
                    string sessionPath = Path.Combine(
                        Application.persistentDataPath,
                        outputDirectory.stringValue,
                        evaluator.GetCurrentSessionId());

                    if (Directory.Exists(sessionPath))
                    {
                        EditorUtility.RevealInFinder(sessionPath);
                    }
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawQuestionsPreview()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Evaluation Questions (20 Questions)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Each model will be asked these 20 questions to evaluate AEC/BIM capabilities:\n\n" +
                "BASIC RETRIEVAL & FILTERING:\n" +
                "  1. Column names and types\n" +
                "  2. Exterior vs interior walls\n" +
                "  3. Walls missing fire rating data\n" +
                "  4. Total wall area by type\n" +
                "  5. Column element IDs and family names\n\n" +
                "DOMAIN BOUNDARY & MEMORY:\n" +
                "  6. Weather question (should refuse - not AEC related)\n" +
                "  7. Cost estimation using Korean construction prices (₩)\n" +
                "  8. Conversation summary (tests memory)\n\n" +
                "SPECIALIZED QUERIES:\n" +
                "  9. Doors on Level 1 with fire ratings\n" +
                " 10. Windows without glazing type\n" +
                " 11. MEP components connected to HVAC system\n" +
                " 12. Beams in Structural System A\n" +
                " 13. Total floor area by level\n" +
                " 14. Elements with QC status marked as 'Fail'\n" +
                " 15. Materials used for roof elements\n" +
                " 16. Insulation thickness for exterior walls\n" +
                " 17. Components scheduled for installation this week\n" +
                " 18. Structural elements missing material definitions\n" +
                " 19. Plumbing fixtures count and grouping by type\n" +
                " 20. List all components in the BIM model",
                MessageType.None);

            EditorGUILayout.Space(5);

            EditorGUILayout.HelpBox(
                "OUTPUT FILES GENERATED:\n" +
                "• leaderboard.csv - Model rankings\n" +
                "• llm_runs.csv - Raw data for all runs\n" +
                "• anova_summary.json - Statistical F-tests and p-values\n" +
                "• statistical_analysis.md - Comprehensive ANOVA report\n" +
                "• radar_charts.html - Interactive performance visualizations\n" +
                "• report.txt - Detailed evaluation report\n" +
                "• metrics_comparison_report.md - Human-readable comparison",
                MessageType.Info);

            EditorGUILayout.EndVertical();
        }
    }
}
#endif
