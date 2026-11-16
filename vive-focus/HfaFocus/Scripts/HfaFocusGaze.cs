// 視線の取得元。
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace HfaFocus
{
public abstract class HfaFocusGazeSource
{
    public abstract string Name { get; }
    // 両眼を合わせた視線方向（ワールド座標）。取得できないフレームは false
    public abstract bool TryGetGaze(Camera head, out Vector3 dir);
}

// 視線追跡なし: 頭の正面を視線とみなす（欠損は頭の向きに固定される）
public class HfaFocusGazeHead : HfaFocusGazeSource
{
    public override string Name { get { return "頭の向き（視線追跡なし）"; } }
    public override bool TryGetGaze(Camera head, out Vector3 dir)
    {
        dir = head.transform.forward;
        return true;
    }
}

// マウス位置を視線とみなす（ヘッドセットなしで Unity エディタ上で試すため）
public class HfaFocusGazeMouse : HfaFocusGazeSource
{
    public override string Name { get { return "マウス"; } }
    public override bool TryGetGaze(Camera head, out Vector3 dir)
    {
        dir = head.ScreenPointToRay(HfaFocusInput.MousePosition).direction;
        return true;
    }
}

// OpenXR の Eye Gaze Interaction（VIVE Focus Vision の内蔵アイトラッカー）。
// OpenXR パッケージに直接依存しないよう、機能名は文字列で指定している
public class HfaFocusGazeOpenXr : HfaFocusGazeSource
{
    static readonly InputFeatureUsage<Quaternion> GazeRotation = new InputFeatureUsage<Quaternion>("gazeRotation");
    static readonly InputFeatureUsage<bool> GazeTracked = new InputFeatureUsage<bool>("gazeTracked");
    readonly List<InputDevice> devices = new List<InputDevice>();

    public override string Name { get { return "視線追跡"; } }

    public override bool TryGetGaze(Camera head, out Vector3 dir)
    {
        dir = Vector3.forward;
        devices.Clear();
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.EyeTracking, devices);
        for (int i = 0; i < devices.Count; i++)
        {
            InputDevice d = devices[i];
            if (!d.isValid) continue;
            bool tracked;
            if (d.TryGetFeatureValue(GazeTracked, out tracked) && !tracked) continue;

            Quaternion rot;
            bool ok = d.TryGetFeatureValue(GazeRotation, out rot);
            if (!ok)
            {
                // 左右の眼を別々に出す実装向け
                Eyes eyes;
                Quaternion l, r;
                if (d.TryGetFeatureValue(CommonUsages.eyesData, out eyes) &&
                    eyes.TryGetLeftEyeRotation(out l) && eyes.TryGetRightEyeRotation(out r))
                {
                    rot = Quaternion.Slerp(l, r, 0.5f);
                    ok = true;
                }
            }
            if (!ok) continue;
            // 値はトラッキング原点基準なので、カメラの親（リグ）の回転を掛けてワールドへ
            Transform rig = head.transform.parent;
            dir = (rig != null ? rig.rotation : Quaternion.identity) * rot * Vector3.forward;
            return true;
        }
        return false;
    }
}

// キーボード・マウスの薄いラッパー（Unity エディタでの試用と、Bluetooth キーボード用）。
// 「Active Input Handling」が旧 Input Manager でも新 Input System でも動くようにする
public static class HfaFocusInput
{
    // k は文字キー（KeyCode.M など）だけを想定
    public static bool Down(KeyCode k)
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(k);
#elif ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        UnityEngine.InputSystem.Key key;
        if (kb == null || !System.Enum.TryParse(k.ToString(), out key)) return false;
        return kb[key].wasPressedThisFrame;
#else
        return false;
#endif
    }

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

    public static bool MouseHeld(int button)
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetMouseButton(button);
#elif ENABLE_INPUT_SYSTEM
        var m = UnityEngine.InputSystem.Mouse.current;
        if (m == null) return false;
        return button == 1 ? m.rightButton.isPressed : m.leftButton.isPressed;
#else
        return false;
#endif
    }
}
}
