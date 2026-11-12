// ModuleMarker.cs
using UnityEngine;

public class ModuleMarker : MonoBehaviour
{
    [System.NonSerialized] public string uniqueId;

    private ModuleSaveLoadManager saveManager;

    void Awake()
    {
        // Find save manager
        saveManager = Object.FindFirstObjectByType<ModuleSaveLoadManager>();

        // Generate unique ID if not set
        if (string.IsNullOrEmpty(uniqueId))
        {
            uniqueId = GetCleanModuleName(gameObject.name);
        }
    }

    private string GetCleanModuleName(string name)
    {
        return name.Replace("(Clone)", "").Trim();
    }

    // Get module info for debugging
    public string GetModuleInfo()
    {
        return $"Module: {uniqueId}, Position: {transform.position}, Rotation: {transform.rotation.eulerAngles}";
    }

    private void OnDrawGizmosSelected()
    {
        // Draw module indicator in scene view
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(transform.position, Vector3.one * 0.5f);

#if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * 2f,
            $"Module: {uniqueId}");
#endif
    }
}