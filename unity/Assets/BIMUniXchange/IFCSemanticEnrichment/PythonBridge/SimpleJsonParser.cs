using System;
using System.Collections.Generic;
using System.Text;

namespace BIMUniXchange.SemanticEnrichment
{
    /// <summary>
    /// Simple JSON parser for parsing Python responses.
    /// Handles dictionaries and lists which Unity's JsonUtility doesn't support well.
    /// </summary>
    public static class SimpleJsonParser
    {
        /// <summary>
        /// Parse a JSON string into a dictionary.
        /// </summary>
        public static Dictionary<string, object> ParseObject(string json)
        {
            if (string.IsNullOrEmpty(json))
                return new Dictionary<string, object>();

            json = json.Trim();
            if (!json.StartsWith("{") || !json.EndsWith("}"))
                throw new FormatException("JSON object must start with { and end with }");

            var result = new Dictionary<string, object>();
            json = json.Substring(1, json.Length - 2).Trim(); // Remove { }

            if (string.IsNullOrEmpty(json))
                return result;

            var tokens = Tokenize(json);
            ParseKeyValuePairs(tokens, result);

            return result;
        }

        /// <summary>
        /// Parse a JSON string into a string dictionary.
        /// </summary>
        public static Dictionary<string, string> ParseStringDictionary(string json)
        {
            var obj = ParseObject(json);
            var result = new Dictionary<string, string>();

            foreach (var kvp in obj)
            {
                result[kvp.Key] = kvp.Value?.ToString() ?? "";
            }

            return result;
        }

        /// <summary>
        /// Get a boolean value from parsed JSON.
        /// </summary>
        public static bool GetBool(Dictionary<string, object> obj, string key, bool defaultValue = false)
        {
            if (obj == null || !obj.ContainsKey(key))
                return defaultValue;

            var value = obj[key];
            if (value is bool)
                return (bool)value;

            if (value is string str)
            {
                if (str.Equals("true", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (str.Equals("false", StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return defaultValue;
        }

        /// <summary>
        /// Get a string value from parsed JSON.
        /// </summary>
        public static string GetString(Dictionary<string, object> obj, string key, string defaultValue = "")
        {
            if (obj == null || !obj.ContainsKey(key))
                return defaultValue;

            return obj[key]?.ToString() ?? defaultValue;
        }

        /// <summary>
        /// Get an integer value from parsed JSON.
        /// </summary>
        public static int GetInt(Dictionary<string, object> obj, string key, int defaultValue = 0)
        {
            if (obj == null || !obj.ContainsKey(key))
                return defaultValue;

            var value = obj[key];
            if (value is int)
                return (int)value;

            if (value is string str && int.TryParse(str, out int result))
                return result;

            return defaultValue;
        }

        /// <summary>
        /// Get a dictionary value from parsed JSON.
        /// </summary>
        public static Dictionary<string, object> GetObject(Dictionary<string, object> obj, string key)
        {
            if (obj == null || !obj.ContainsKey(key))
                return new Dictionary<string, object>();

            if (obj[key] is Dictionary<string, object> dict)
                return dict;

            return new Dictionary<string, object>();
        }

        /// <summary>
        /// Get a list value from parsed JSON.
        /// </summary>
        public static List<object> GetList(Dictionary<string, object> obj, string key)
        {
            if (obj == null || !obj.ContainsKey(key))
                return new List<object>();

            if (obj[key] is List<object> list)
                return list;

            return new List<object>();
        }

        private static List<Token> Tokenize(string json)
        {
            var tokens = new List<Token>();
            int i = 0;

            while (i < json.Length)
            {
                char c = json[i];

                // Skip whitespace
                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                // String
                if (c == '"')
                {
                    int start = i + 1;
                    i++;
                    while (i < json.Length && (json[i] != '"' || json[i - 1] == '\\'))
                    {
                        i++;
                    }
                    tokens.Add(new Token { Type = TokenType.String, Value = Unescape(json.Substring(start, i - start)) });
                    i++;
                }
                // Colon
                else if (c == ':')
                {
                    tokens.Add(new Token { Type = TokenType.Colon });
                    i++;
                }
                // Comma
                else if (c == ',')
                {
                    tokens.Add(new Token { Type = TokenType.Comma });
                    i++;
                }
                // Object start
                else if (c == '{')
                {
                    tokens.Add(new Token { Type = TokenType.ObjectStart });
                    i++;
                }
                // Object end
                else if (c == '}')
                {
                    tokens.Add(new Token { Type = TokenType.ObjectEnd });
                    i++;
                }
                // Array start
                else if (c == '[')
                {
                    tokens.Add(new Token { Type = TokenType.ArrayStart });
                    i++;
                }
                // Array end
                else if (c == ']')
                {
                    tokens.Add(new Token { Type = TokenType.ArrayEnd });
                    i++;
                }
                // Number, boolean, or null
                else
                {
                    int start = i;
                    while (i < json.Length && !char.IsWhiteSpace(json[i]) &&
                           json[i] != ',' && json[i] != '}' && json[i] != ']')
                    {
                        i++;
                    }
                    string value = json.Substring(start, i - start);

                    if (value == "true" || value == "false")
                        tokens.Add(new Token { Type = TokenType.Boolean, Value = value });
                    else if (value == "null")
                        tokens.Add(new Token { Type = TokenType.Null });
                    else
                        tokens.Add(new Token { Type = TokenType.Number, Value = value });
                }
            }

            return tokens;
        }

        private static void ParseKeyValuePairs(List<Token> tokens, Dictionary<string, object> result)
        {
            int i = 0;
            while (i < tokens.Count)
            {
                // Expect string key
                if (tokens[i].Type != TokenType.String)
                {
                    i++;
                    continue;
                }

                string key = tokens[i].Value;
                i++;

                // Expect colon
                if (i >= tokens.Count || tokens[i].Type != TokenType.Colon)
                {
                    i++;
                    continue;
                }
                i++;

                // Parse value
                if (i >= tokens.Count)
                    break;

                object value = ParseValue(tokens, ref i);
                result[key] = value;

                // Skip comma
                if (i < tokens.Count && tokens[i].Type == TokenType.Comma)
                    i++;
            }
        }

        private static object ParseValue(List<Token> tokens, ref int i)
        {
            if (i >= tokens.Count)
                return null;

            Token token = tokens[i];

            switch (token.Type)
            {
                case TokenType.String:
                    i++;
                    return token.Value;

                case TokenType.Number:
                    i++;
                    if (int.TryParse(token.Value, out int intVal))
                        return intVal;
                    if (double.TryParse(token.Value, out double doubleVal))
                        return doubleVal;
                    return token.Value;

                case TokenType.Boolean:
                    i++;
                    return token.Value == "true";

                case TokenType.Null:
                    i++;
                    return null;

                case TokenType.ObjectStart:
                    return ParseNestedObject(tokens, ref i);

                case TokenType.ArrayStart:
                    return ParseArray(tokens, ref i);

                default:
                    i++;
                    return null;
            }
        }

        private static Dictionary<string, object> ParseNestedObject(List<Token> tokens, ref int i)
        {
            i++; // Skip {
            var result = new Dictionary<string, object>();

            while (i < tokens.Count && tokens[i].Type != TokenType.ObjectEnd)
            {
                if (tokens[i].Type != TokenType.String)
                {
                    i++;
                    continue;
                }

                string key = tokens[i].Value;
                i++;

                if (i >= tokens.Count || tokens[i].Type != TokenType.Colon)
                    break;
                i++;

                object value = ParseValue(tokens, ref i);
                result[key] = value;

                if (i < tokens.Count && tokens[i].Type == TokenType.Comma)
                    i++;
            }

            if (i < tokens.Count && tokens[i].Type == TokenType.ObjectEnd)
                i++;

            return result;
        }

        private static List<object> ParseArray(List<Token> tokens, ref int i)
        {
            i++; // Skip [
            var result = new List<object>();

            while (i < tokens.Count && tokens[i].Type != TokenType.ArrayEnd)
            {
                object value = ParseValue(tokens, ref i);
                result.Add(value);

                if (i < tokens.Count && tokens[i].Type == TokenType.Comma)
                    i++;
            }

            if (i < tokens.Count && tokens[i].Type == TokenType.ArrayEnd)
                i++;

            return result;
        }

        private static string Unescape(string str)
        {
            if (string.IsNullOrEmpty(str))
                return str;

            return str.Replace("\\\"", "\"")
                      .Replace("\\\\", "\\")
                      .Replace("\\n", "\n")
                      .Replace("\\r", "\r")
                      .Replace("\\t", "\t");
        }

        private enum TokenType
        {
            String,
            Number,
            Boolean,
            Null,
            ObjectStart,
            ObjectEnd,
            ArrayStart,
            ArrayEnd,
            Colon,
            Comma
        }

        private class Token
        {
            public TokenType Type { get; set; }
            public string Value { get; set; }
        }
    }
}
