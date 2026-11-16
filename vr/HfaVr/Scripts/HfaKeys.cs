// キーボード・マウス入力の薄いラッパー。
// Unity の「Active Input Handling」が旧 Input Manager でも新 Input System でも動くようにする
// （OpenXR を入れると新 Input System が有効になるため）。
using UnityEngine;

public static class HfaKeys
{
    public static bool Down(KeyCode k)
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(k);
#elif ENABLE_INPUT_SYSTEM
        var c = Control(k);
        return c != null && c.wasPressedThisFrame;
#else
        return false;
#endif
    }

    public static bool Held(KeyCode k)
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKey(k);
#elif ENABLE_INPUT_SYSTEM
        var c = Control(k);
        return c != null && c.isPressed;
#else
        return false;
#endif
    }

    public static bool Shift { get { return Held(KeyCode.LeftShift) || Held(KeyCode.RightShift); } }

    public static Vector3 MousePosition
    {
        get
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.mousePosition;
#elif ENABLE_INPUT_SYSTEM
            var m = UnityEngine.InputSystem.Mouse.current;
            if (m == null) return Vector3.zero;
            Vector2 p = m.position.ReadValue();
            return new Vector3(p.x, p.y, 0);
#else
            return Vector3.zero;
#endif
        }
    }

    public static bool RightMouseHeld
    {
        get
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButton(1);
#elif ENABLE_INPUT_SYSTEM
            var m = UnityEngine.InputSystem.Mouse.current;
            return m != null && m.rightButton.isPressed;
#else
            return false;
#endif
        }
    }

#if !ENABLE_LEGACY_INPUT_MANAGER && ENABLE_INPUT_SYSTEM
    static UnityEngine.InputSystem.Controls.KeyControl Control(KeyCode k)
    {
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return null;
        UnityEngine.InputSystem.Key key;
        switch (k)
        {
            case KeyCode.Alpha0: key = UnityEngine.InputSystem.Key.Digit0; break;
            case KeyCode.Alpha1: key = UnityEngine.InputSystem.Key.Digit1; break;
            case KeyCode.Alpha2: key = UnityEngine.InputSystem.Key.Digit2; break;
            case KeyCode.Alpha3: key = UnityEngine.InputSystem.Key.Digit3; break;
            case KeyCode.Alpha4: key = UnityEngine.InputSystem.Key.Digit4; break;
            case KeyCode.Return: key = UnityEngine.InputSystem.Key.Enter; break;
            default:
                // 文字キー・矢印・Space・Tab・F1 などは名前が同じ
                if (!System.Enum.TryParse(k.ToString(), out key)) return null;
                break;
        }
        return kb[key];
    }
#endif
}
