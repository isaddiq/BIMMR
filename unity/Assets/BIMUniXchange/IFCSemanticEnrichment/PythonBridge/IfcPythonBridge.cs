using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace BIMUniXchange.SemanticEnrichment
{
    /// <summary>
    /// Bridge to Python's IfcOpenShell library for IFC file operations.
    /// Executes Python scripts and parses JSON responses.
    /// </summary>
    public class IfcPythonBridge : IDisposable
    {
        private string _pythonExecutable = "python";
        private string _pythonScriptPath;
        private string _currentIfcPath;
        private bool _disposed = false;

        // Cached data from last load
        private string _schema;
        private int _elementCount;
        private int _spatialElementCount;

        public bool IsModelLoaded => !string.IsNullOrEmpty(_currentIfcPath);
        public string CurrentIfcPath => _currentIfcPath;
        public string Schema => _schema;
        public int ElementCount => _elementCount;
        public int SpatialElementCount => _spatialElementCount;

        /// <summary>
        /// Initialize the Python bridge.
        /// </summary>
        /// <param name="pythonExecutable">Path to Python executable (default: "python")</param>
        public IfcPythonBridge(string pythonExecutable = "python")
        {
            _pythonExecutable = pythonExecutable;

            // Locate the Python helper script
            _pythonScriptPath = FindPythonScript();

            if (string.IsNullOrEmpty(_pythonScriptPath))
            {
                Debug.LogError("Could not locate ifc_helper.py script!");
            }
        }

        /// <summary>
        /// Find the ifc_helper.py script in the project.
        /// </summary>
        private string FindPythonScript()
        {
            // Try relative to this script
            string scriptDir = Path.Combine(Application.dataPath, "BIMUniXchange", "IFCSemanticEnrichment", "PythonScripts");
            string scriptPath = Path.Combine(scriptDir, "ifc_helper.py");

            if (File.Exists(scriptPath))
            {
                return scriptPath;
            }

            // Search in Assets folder
            string[] foundFiles = Directory.GetFiles(Application.dataPath, "ifc_helper.py", SearchOption.AllDirectories);
            if (foundFiles.Length > 0)
            {
                return foundFiles[0];
            }

            return null;
        }

        /// <summary>
        /// Load an IFC model.
        /// </summary>
        public PythonIfcResult<LoadModelResult> LoadModel(string ifcPath)
        {
            if (!File.Exists(ifcPath))
            {
                return PythonIfcResult<LoadModelResult>.Error($"IFC file not found: {ifcPath}");
            }

            var jsonResult = ExecutePythonCommand("load_model", ifcPath);
            var result = ParseJsonResult<LoadModelResult>(jsonResult);

            if (result.Success)
            {
                _currentIfcPath = ifcPath;
                _schema = result.Data.Schema;
                _elementCount = result.Data.ElementCount;
                _spatialElementCount = result.Data.SpatialElementCount;
            }

            return result;
        }

        /// <summary>
        /// Get an element by GlobalId.
        /// </summary>
        public PythonIfcResult<IfcElementData> GetElementByGlobalId(string globalId)
        {
            if (!IsModelLoaded)
            {
                return PythonIfcResult<IfcElementData>.Error("No model loaded");
            }

            var jsonResult = ExecutePythonCommand("get_element", _currentIfcPath, globalId);
            var result = ParseJsonResult<GetElementResult>(jsonResult);

            if (result.Success && result.Data != null)
            {
                return PythonIfcResult<IfcElementData>.Ok(result.Data.Element);
            }

            return PythonIfcResult<IfcElementData>.Error(result.ErrorMessage);
        }

        /// <summary>
        /// Get element(s) by Tag.
        /// </summary>
        public PythonIfcResult<List<IfcElementData>> GetElementByTag(string tag)
        {
            if (!IsModelLoaded)
            {
                return PythonIfcResult<List<IfcElementData>>.Error("No model loaded");
            }

            var jsonResult = ExecutePythonCommand("get_element_by_tag", _currentIfcPath, tag);
            var result = ParseJsonResult<GetElementsResult>(jsonResult);

            if (result.Success && result.Data != null)
            {
                return PythonIfcResult<List<IfcElementData>>.Ok(result.Data.Elements);
            }

            return PythonIfcResult<List<IfcElementData>>.Error(result.ErrorMessage);
        }

        /// <summary>
        /// Get all properties for an element.
        /// </summary>
        public PythonIfcResult<Dictionary<string, string>> GetElementProperties(string globalId)
        {
            if (!IsModelLoaded)
            {
                return PythonIfcResult<Dictionary<string, string>>.Error("No model loaded");
            }

            var jsonResult = ExecutePythonCommand("get_properties", _currentIfcPath, globalId);

            try
            {
                var parsed = SimpleJsonParser.ParseObject(jsonResult);
                bool success = SimpleJsonParser.GetBool(parsed, "success", false);

                if (!success)
                {
                    string error = SimpleJsonParser.GetString(parsed, "error", "Unknown error");
                    return PythonIfcResult<Dictionary<string, string>>.Error(error);
                }

                var propertiesObj = SimpleJsonParser.GetObject(parsed, "properties");
                var properties = new Dictionary<string, string>();

                foreach (var kvp in propertiesObj)
                {
                    properties[kvp.Key] = kvp.Value?.ToString() ?? "";
                }

                return PythonIfcResult<Dictionary<string, string>>.Ok(properties);
            }
            catch (Exception ex)
            {
                return PythonIfcResult<Dictionary<string, string>>.Error($"Parse error: {ex.Message}");
            }
        }

        /// <summary>
        /// Get the complete spatial structure.
        /// </summary>
        public PythonIfcResult<SpatialNode> GetSpatialStructure()
        {
            if (!IsModelLoaded)
            {
                return PythonIfcResult<SpatialNode>.Error("No model loaded");
            }

            var jsonResult = ExecutePythonCommand("get_spatial_structure", _currentIfcPath);

            // SpatialNode is a property-based tree with parent links, which JsonUtility
            // cannot populate, so the result is parsed manually.
            try
            {
                var parsed = SimpleJsonParser.ParseObject(jsonResult);

                if (!SimpleJsonParser.GetBool(parsed, "success", false))
                {
                    string error = SimpleJsonParser.GetString(parsed, "error", "Unknown error");
                    return PythonIfcResult<SpatialNode>.Error(error);
                }

                if (!(parsed.TryGetValue("structure", out var structure) && structure is Dictionary<string, object> rootObj))
                {
                    return PythonIfcResult<SpatialNode>.Error("Spatial structure missing from Python response");
                }

                return PythonIfcResult<SpatialNode>.Ok(BuildSpatialNode(rootObj, null));
            }
            catch (Exception ex)
            {
                return PythonIfcResult<SpatialNode>.Error($"Parse error: {ex.Message}");
            }
        }

        private static SpatialNode BuildSpatialNode(Dictionary<string, object> obj, SpatialNode parent)
        {
            var node = new SpatialNode
            {
                GlobalId = SimpleJsonParser.GetString(obj, "globalId"),
                Name = SimpleJsonParser.GetString(obj, "name"),
                LongName = SimpleJsonParser.GetString(obj, "longName"),
                IfcClass = SimpleJsonParser.GetString(obj, "ifcClass"),
                Parent = parent
            };

            foreach (var child in SimpleJsonParser.GetList(obj, "children"))
            {
                if (child is Dictionary<string, object> childObj)
                    node.Children.Add(BuildSpatialNode(childObj, node));
            }

            foreach (var elementId in SimpleJsonParser.GetList(obj, "containedElements"))
            {
                if (elementId != null)
                    node.ContainedElementGlobalIds.Add(elementId.ToString());
            }

            return node;
        }

        /// <summary>
        /// Get all elements in the model.
        /// </summary>
        public PythonIfcResult<List<IfcElementData>> GetAllElements()
        {
            if (!IsModelLoaded)
            {
                return PythonIfcResult<List<IfcElementData>>.Error("No model loaded");
            }

            var jsonResult = ExecutePythonCommand("get_all_elements", _currentIfcPath);
            var result = ParseJsonResult<GetAllElementsResult>(jsonResult);

            if (result.Success && result.Data != null)
            {
                return PythonIfcResult<List<IfcElementData>>.Ok(result.Data.Elements);
            }

            return PythonIfcResult<List<IfcElementData>>.Error(result.ErrorMessage);
        }

        /// <summary>
        /// Execute a Python command and return the JSON output.
        /// </summary>
        private string ExecutePythonCommand(string command, params string[] args)
        {
            if (string.IsNullOrEmpty(_pythonScriptPath))
            {
                return "{\"success\": false, \"error\": \"Python script not found\"}";
            }

            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = _pythonExecutable,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                // Build arguments: script_path command arg1 arg2 ...
                StringBuilder arguments = new StringBuilder();
                arguments.Append($"\"{_pythonScriptPath}\" {command}");

                foreach (var arg in args)
                {
                    arguments.Append($" \"{arg}\"");
                }

                startInfo.Arguments = arguments.ToString();

                using (Process process = Process.Start(startInfo))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    if (process.ExitCode != 0 && !string.IsNullOrEmpty(error))
                    {
                        Debug.LogError($"Python error: {error}");
                        return $"{{\"success\": false, \"error\": \"{EscapeJsonString(error)}\"}}";
                    }

                    return output;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to execute Python command: {ex.Message}");
                return $"{{\"success\": false, \"error\": \"{EscapeJsonString(ex.Message)}\"}}";
            }
        }

        /// <summary>
        /// Parse JSON result from Python.
        /// </summary>
        private PythonIfcResult<T> ParseJsonResult<T>(string json) where T : class
        {
            try
            {
                // First check if it's an error response
                var baseResult = JsonUtility.FromJson<BaseResult>(json);

                if (baseResult == null || !baseResult.success)
                {
                    string error = baseResult?.error ?? "Unknown error";
                    return PythonIfcResult<T>.Error(error);
                }

                // Parse the full result
                var fullResult = JsonUtility.FromJson<T>(json);
                return PythonIfcResult<T>.Ok(fullResult);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to parse JSON: {ex.Message}\nJSON: {json}");
                return PythonIfcResult<T>.Error($"JSON parsing error: {ex.Message}");
            }
        }

        /// <summary>
        /// Escape a string for JSON.
        /// </summary>
        private string EscapeJsonString(string str)
        {
            if (string.IsNullOrEmpty(str))
                return str;

            return str.Replace("\\", "\\\\")
                      .Replace("\"", "\\\"")
                      .Replace("\n", "\\n")
                      .Replace("\r", "\\r")
                      .Replace("\t", "\\t");
        }

        /// <summary>
        /// Close the current model.
        /// </summary>
        public void CloseModel()
        {
            _currentIfcPath = null;
            _schema = null;
            _elementCount = 0;
            _spatialElementCount = 0;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                CloseModel();
                _disposed = true;
            }
        }

        #region Result Classes

        [Serializable]
        private class BaseResult
        {
            public bool success;
            public string error;
        }

        [Serializable]
        public class LoadModelResult
        {
            public bool success;
            public string error;
            [SerializeField] private string schema;
            [SerializeField] private int elementCount;
            [SerializeField] private int spatialElementCount;

            public string Schema => schema;
            public int ElementCount => elementCount;
            public int SpatialElementCount => spatialElementCount;
        }

        [Serializable]
        private class GetElementResult
        {
            public bool success;
            public string error;
            [SerializeField] private IfcElementData element;

            public IfcElementData Element => element;
        }

        [Serializable]
        private class GetElementsResult
        {
            public bool success;
            public string error;
            [SerializeField] private List<IfcElementData> elements;

            public List<IfcElementData> Elements => elements ?? new List<IfcElementData>();
        }

        [Serializable]
        private class GetPropertiesResult
        {
            public bool success;
            public string error;
            [SerializeField] private SerializableDictionary properties;

            public Dictionary<string, string> Properties
            {
                get
                {
                    // Unity's JsonUtility doesn't support Dictionary directly
                    // We'll need custom deserialization
                    var dict = new Dictionary<string, string>();
                    // This would need custom JSON parsing or use Newtonsoft.Json
                    return dict;
                }
            }
        }

        [Serializable]
        private class GetAllElementsResult
        {
            public bool success;
            public string error;
            [SerializeField] private List<IfcElementData> elements;
            public int count;

            public List<IfcElementData> Elements => elements ?? new List<IfcElementData>();
        }

        [Serializable]
        private class SerializableDictionary
        {
            // Placeholder for dictionary serialization
        }

        #endregion
    }

    /// <summary>
    /// Result wrapper for Python IFC operations.
    /// </summary>
    public class PythonIfcResult<T>
    {
        public bool Success { get; private set; }
        public T Data { get; private set; }
        public string ErrorMessage { get; private set; }

        private PythonIfcResult() { }

        public static PythonIfcResult<T> Ok(T data)
        {
            return new PythonIfcResult<T>
            {
                Success = true,
                Data = data,
                ErrorMessage = null
            };
        }

        public static PythonIfcResult<T> Error(string errorMessage)
        {
            return new PythonIfcResult<T>
            {
                Success = false,
                Data = default(T),
                ErrorMessage = errorMessage
            };
        }
    }

    /// <summary>
    /// IFC element data structure.
    /// </summary>
    [Serializable]
    public class IfcElementData
    {
        public string globalId;
        public string ifcClass;
        public string name;
        public string description;
        public string tag;
        public string objectType;
    }

    /// <summary>
    /// Spatial node structure (reusing from IfcModelNavigator).
    /// </summary>
    [Serializable]
    public class PythonSpatialNode
    {
        public string globalId;
        public string name;
        public string longName;
        public string ifcClass;
        public List<PythonSpatialNode> children = new List<PythonSpatialNode>();
        public List<string> containedElements = new List<string>();
    }
}
