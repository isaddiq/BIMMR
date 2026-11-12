using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class ExitButtonHandler : MonoBehaviour
{
    [Header("Assign your UI Exit Button here")]
    public Button exitButton;

    void Start()
    {
        if (exitButton == null)
        {
            Debug.LogError("ExitButtonHandler: No Button assigned in the inspector.");
            return;
        }

        exitButton.onClick.AddListener(OnExitButtonClicked);
    }

    private void OnExitButtonClicked()
    {
        Debug.Log("Exit button clicked — quitting application.");
        Application.Quit();

#if UNITY_EDITOR
        // If running inside the Editor, stop play mode
        EditorApplication.isPlaying = false;
#endif
    }
}
