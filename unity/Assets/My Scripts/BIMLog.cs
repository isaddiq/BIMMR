using System.Diagnostics;

/// <summary>
/// Detail logging that is compiled out by default so the Console only shows important messages.
/// To see these logs again, add <c>BIM_VERBOSE_LOGS</c> to
/// Project Settings > Player > Scripting Define Symbols.
/// Warnings and errors should keep using Debug.LogWarning / Debug.LogError.
/// </summary>
public static class BIMLog
{
    [Conditional("BIM_VERBOSE_LOGS")]
    public static void Verbose(object message) => UnityEngine.Debug.Log(message);

    [Conditional("BIM_VERBOSE_LOGS")]
    public static void Verbose(object message, UnityEngine.Object context) => UnityEngine.Debug.Log(message, context);
}
