using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace BIMMR.Inspection
{
    /// <summary>
    /// Extracts metadata from a BIM <see cref="GameObject"/>. The reader walks
    /// the object plus its parents and children and inspects every Component
    /// it finds, trying to pull out:
    ///   * a stable identifier (GlobalId / IfcGuid / RevitUniqueId / etc.)
    ///   * common BIM display values (Name, Category, Type, Level, Room, etc.)
    ///   * a full set of key/value properties for the UI and exporter.
    ///
    /// The reader does NOT assume any particular importer. It uses reflection
    /// and a list of known dictionary / list shapes so it works with most
    /// IFC and Revit importers without modification.
    /// </summary>
    public class BIMElementMetadataReader : MonoBehaviour
    {
        // The set of property names that are treated as stable BIM identifiers.
        // Order matters: the first key found in this list is preferred.
        public static readonly string[] DefaultStableIdKeys = new[]
        {
            "GlobalId", "IfcGuid", "IFCGuid", "Ifc_Guid", "ifcGuid",
            "RevitUniqueId", "UniqueId", "Revit_UniqueId",
            "ElementId", "ElementID", "Element_Id",
            // Column names written by the BIMUniXchange CSV exports (same list the
            // metadata assigner binds on), so bound objects resolve to their persistent id.
            "ID and Categories.Element ID", "General Parameters.Element ID",
            "General Parameters.Unique ID", "Element ID"
        };

        // Optional user-provided override that is checked before the defaults.
        // Wired by <see cref="BIMInspectionBootstrapper"/>.
        [Tooltip("Optional. If set, this metadata key is preferred for the stable id.")]
        public string preferredStableIdKey;

        // Recursion guard so nested objects do not stop the search at the first
        // empty component.
        private const int MaxAncestors = 8;
        private const int MaxDescendants = 64;

        /// <summary>Read metadata from the given GameObject.</summary>
        public BIMElementInfo Read(GameObject element)
        {
            if (element == null) return null;

            var info = new BIMElementInfo();
            var props = info.properties;

            // 1. Self, then walk up through parents (some importers put metadata
            //    on a wrapper parent, others on the leaf mesh).
            CollectFromHierarchy(element.transform, props, MaxAncestors, goUp: true);
            // 2. Walk a limited depth of children (some importers store
            //    properties as separate child "PropertySet" GameObjects).
            CollectFromHierarchy(element.transform, props, MaxDescendants, goUp: false);

            // Pull common known fields if present.
            info.displayName = FirstNonEmpty(props, "Name", "DisplayName", "Title")
                               ?? element.name;
            info.category    = FirstNonEmpty(props, "Category", "IfcCategory", "ObjectType");
            info.typeName    = FirstNonEmpty(props, "Type", "Family", "FamilyName",
                                              "TypeName", "IfcType");
            info.level       = FirstNonEmpty(props, "Level", "Storey", "Story",
                                              "BuildingStorey", "IfcBuildingStorey");
            info.room        = FirstNonEmpty(props, "Room", "Space", "IfcSpace");

            // Resolve a stable identifier.
            info.stableId = ResolveStableId(props, element, out bool fallback);
            info.stableIdIsFallback = fallback;
            if (fallback)
            {
                Debug.LogWarning(
                    $"[BIM Inspection] No stable BIM identifier found on '{element.name}'. " +
                    $"Using runtime instance id '{info.stableId}'. " +
                    $"This id is NOT reliable across sessions or model reloads.");
            }

            return info;
        }

        // ---------- internal helpers ----------------------------------------

        private void CollectFromHierarchy(Transform start, Dictionary<string, string> props,
                                          int limit, bool goUp)
        {
            if (goUp)
            {
                var t = start;
                int steps = 0;
                while (t != null && steps < limit)
                {
                    CollectFromGameObject(t.gameObject, props);
                    t = t.parent;
                    steps++;
                }
            }
            else
            {
                var queue = new Queue<Transform>();
                queue.Enqueue(start);
                int visited = 0;
                while (queue.Count > 0 && visited < limit)
                {
                    var t = queue.Dequeue();
                    if (t != start) CollectFromGameObject(t.gameObject, props);
                    visited++;
                    foreach (Transform c in t) queue.Enqueue(c);
                }
            }
        }

        private void CollectFromGameObject(GameObject go, Dictionary<string, string> props)
        {
            var components = go.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                var c = components[i];
                if (c == null) continue;
                // Skip standard Unity components we know cannot hold BIM data.
                if (c is Transform || c is MeshFilter || c is MeshRenderer ||
                    c is Collider || c is Renderer || c is Rigidbody) continue;

                TryReadComponent(c, props);
            }
        }

        private void TryReadComponent(Component component, Dictionary<string, string> props)
        {
            var type = component.GetType();

            // Inspect public + non-public instance fields/properties via reflection.
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public |
                                       BindingFlags.NonPublic;

            foreach (var field in type.GetFields(flags))
            {
                if (field.IsNotSerialized) continue;
                object value = SafeGet(() => field.GetValue(component));
                AbsorbValue(field.Name, value, props);
            }

            foreach (var prop in type.GetProperties(flags))
            {
                if (!prop.CanRead) continue;
                // Skip indexers and Unity engine noise.
                if (prop.GetIndexParameters().Length != 0) continue;
                if (prop.Name == "name" || prop.Name == "tag" || prop.Name == "hideFlags")
                    continue;
                object value = SafeGet(() => prop.GetValue(component, null));
                AbsorbValue(prop.Name, value, props);
            }
        }

        // Try to interpret the discovered value as either a scalar metadata
        // entry or a collection of key/value pairs.
        private void AbsorbValue(string sourceName, object value,
                                 Dictionary<string, string> props)
        {
            if (value == null) return;

            // --- Dictionaries (most common metadata shape) ------------------
            if (value is IDictionary dict)
            {
                foreach (DictionaryEntry e in dict)
                {
                    if (e.Key == null) continue;
                    Put(props, e.Key.ToString(), e.Value?.ToString());
                }
                return;
            }

            // --- Collections of "KeyValuePair-like" entries -----------------
            if (value is IEnumerable enumerable && !(value is string))
            {
                foreach (var item in enumerable)
                {
                    if (item == null) continue;
                    if (TryExtractKvp(item, out string k, out string v) && !string.IsNullOrEmpty(k))
                        Put(props, k, v);
                }
                return;
            }

            // --- Scalars / strings ------------------------------------------
            // Only keep simple/printable types so we don't pollute properties
            // with object references like "UnityEngine.Material (Instance)".
            if (value is string s)
            {
                if (!string.IsNullOrEmpty(s)) Put(props, sourceName, s);
                return;
            }

            var t = value.GetType();
            if (t.IsPrimitive || t.IsEnum || value is decimal || value is Guid)
            {
                Put(props, sourceName, value.ToString());
            }
        }

        private bool TryExtractKvp(object item, out string key, out string val)
        {
            key = null; val = null;
            var t = item.GetType();
            const BindingFlags f = BindingFlags.Instance | BindingFlags.Public;

            // KeyValuePair<,>
            var keyMember  = (MemberInfo)t.GetProperty("Key",   f) ?? t.GetField("Key",   f);
            var valMember  = (MemberInfo)t.GetProperty("Value", f) ?? t.GetField("Value", f);

            // Common BIM importer shapes use "Name"/"Value" or "PropertyName"/"PropertyValue".
            if (keyMember == null) keyMember = (MemberInfo)t.GetProperty("Name", f) ?? t.GetField("Name", f);
            if (keyMember == null) keyMember = (MemberInfo)t.GetProperty("PropertyName", f) ?? t.GetField("PropertyName", f);
            if (valMember == null) valMember = (MemberInfo)t.GetProperty("PropertyValue", f) ?? t.GetField("PropertyValue", f);

            if (keyMember == null || valMember == null) return false;

            object k = SafeGet(() => GetMemberValue(keyMember, item));
            object v = SafeGet(() => GetMemberValue(valMember, item));
            key = k?.ToString();
            val = v?.ToString();
            return key != null;
        }

        private static object GetMemberValue(MemberInfo m, object target)
        {
            return m is PropertyInfo p ? p.GetValue(target, null) : ((FieldInfo)m).GetValue(target);
        }

        private static T SafeGet<T>(Func<T> f)
        {
            try { return f(); } catch { return default; }
        }

        // Insert into the dictionary, preferring the first non-empty value seen.
        private static void Put(Dictionary<string, string> props, string k, string v)
        {
            if (string.IsNullOrEmpty(k)) return;
            if (string.IsNullOrEmpty(v)) return;
            if (!props.ContainsKey(k)) props[k] = v;
        }

        private static string FirstNonEmpty(Dictionary<string, string> props, params string[] keys)
        {
            foreach (var k in keys)
            {
                if (props.TryGetValue(k, out var v) && !string.IsNullOrEmpty(v)) return v;
            }
            return null;
        }

        private string ResolveStableId(Dictionary<string, string> props, GameObject element,
                                       out bool fallback)
        {
            // 1. Caller-preferred key.
            if (!string.IsNullOrEmpty(preferredStableIdKey) &&
                props.TryGetValue(preferredStableIdKey, out var v) && !string.IsNullOrEmpty(v))
            {
                fallback = false; return v;
            }

            // 2. Built-in known keys.
            foreach (var key in DefaultStableIdKeys)
            {
                if (props.TryGetValue(key, out var found) && !string.IsNullOrEmpty(found))
                {
                    fallback = false; return found;
                }
            }

            // 3. Fallback: GameObject name + instance id. Unstable across sessions.
            fallback = true;
            return $"runtime::{element.name}::{element.GetEntityId()}";
        }
    }
}
