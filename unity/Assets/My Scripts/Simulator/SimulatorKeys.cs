using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Keyboard "pressed this frame" checks for the simulator shortcuts.
///
/// The Meta XR Simulator runs in its own window, so while it has focus Unity receives
/// no keyboard input. When Unity is unfocused this falls back to polling the OS key
/// state (Windows only), so shortcuts work from either window.
/// </summary>
public static class SimulatorKeys
{
    /// <summary>Also catch keys while another window (e.g. the Meta XR Simulator) has focus.</summary>
    public static bool listenWhenUnityUnfocused = true;

    private struct GlobalKeyState
    {
        public int frame;
        public bool down;
        public bool pressed;
    }

    private static readonly Dictionary<Key, GlobalKeyState> s_GlobalStates = new Dictionary<Key, GlobalKeyState>();

    public static bool WasPressedThisFrame(Key key)
    {
        if (Application.isFocused)
        {
            var keyboard = Keyboard.current;
            return keyboard != null && keyboard[key].wasPressedThisFrame;
        }

        if (!listenWhenUnityUnfocused)
            return false;

        s_GlobalStates.TryGetValue(key, out var state);
        if (state.frame == Time.frameCount)
            return state.pressed;

        bool isDown = IsKeyDownGlobal(key);
        state.pressed = isDown && !state.down;
        state.down = isDown;
        state.frame = Time.frameCount;
        s_GlobalStates[key] = state;
        return state.pressed;
    }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    private static bool IsKeyDownGlobal(Key key)
    {
        int vk = ToVirtualKey(key);
        return vk != 0 && (GetAsyncKeyState(vk) & 0x8000) != 0;
    }

    private static int ToVirtualKey(Key key)
    {
        if (key >= Key.A && key <= Key.Z) return 'A' + (key - Key.A);
        if (key >= Key.Digit1 && key <= Key.Digit9) return '1' + (key - Key.Digit1);
        if (key >= Key.Numpad0 && key <= Key.Numpad9) return 0x60 + (key - Key.Numpad0);
        if (key >= Key.F1 && key <= Key.F12) return 0x70 + (key - Key.F1);

        switch (key)
        {
            case Key.Digit0: return '0';
            case Key.Tab: return 0x09;
            case Key.Space: return 0x20;
            case Key.Enter: return 0x0D;
            case Key.Backquote: return 0xC0;
            case Key.CapsLock: return 0x14;
            case Key.Minus: return 0xBD;
            case Key.Equals: return 0xBB;
            case Key.NumpadMinus: return 0x6D;
            case Key.NumpadPlus: return 0x6B;
            default: return 0;
        }
    }
#else
    private static bool IsKeyDownGlobal(Key key) => false;
#endif
}
