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

// OpenXR の Eye Gaze Interaction Profile（VIVE Focus Vision の内蔵アイトラッカー）。
// 取り方は2通りあり、順に試す。
//   1. XR の入力装置（InputDevices）から gazeRotation を読む
//   2. Input System から <EyeGaze>/pose を読む（HTC の資料が案内している経路。Input System が有効なときだけ）
// OpenXR パッケージに直接依存しないよう、機能名は文字列で指定している
public class HfaFocusGazeOpenXr : HfaFocusGazeSource
{
    static readonly InputFeatureUsage<Quaternion> GazeRotation = new InputFeatureUsage<Quaternion>("gazeRotation");
    static readonly InputFeatureUsage<bool> GazeTracked = new InputFeatureUsage<bool>("gazeTracked");
    readonly List<InputDevice> devices = new List<InputDevice>();

    public override string Name { get { return "視線追跡"; } }

#if ENABLE_INPUT_SYSTEM
    UnityEngine.InputSystem.InputAction rotationAction, trackedAction;

    bool TryInputSystem(out Quaternion rot)
    {
        rot = Quaternion.identity;
        if (rotationAction == null)
        {
            rotationAction = new UnityEngine.InputSystem.InputAction(
                "HfaGazeRotation", UnityEngine.InputSystem.InputActionType.Value, "<EyeGaze>/pose/rotation");
            trackedAction = new UnityEngine.InputSystem.InputAction(
                "HfaGazeTracked", UnityEngine.InputSystem.InputActionType.Value, "<EyeGaze>/pose/isTracked");
            rotationAction.Enable();
            trackedAction.Enable();
        }
        if (rotationAction.controls.Count == 0) return false;   // アイトラッカーの装置が見つからない
        if (trackedAction.controls.Count > 0 && trackedAction.ReadValue<float>() < 0.5f) return false;
        Quaternion q = rotationAction.ReadValue<Quaternion>();
        if (q.x == 0f && q.y == 0f && q.z == 0f && q.w == 0f) return false;   // まだ値が来ていない
        rot = q;
        return true;
    }
#endif

    public override bool TryGetGaze(Camera head, out Vector3 dir)
    {
        dir = Vector3.forward;
        Transform rig = head.transform.parent;   // 値はトラッキング原点基準なので、リグの回転を掛けてワールドへ
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
            dir = (rig != null ? rig.rotation : Quaternion.identity) * rot * Vector3.forward;
            return true;
        }
#if ENABLE_INPUT_SYSTEM
        Quaternion r2;
        if (TryInputSystem(out r2))
        {
            dir = (rig != null ? rig.rotation : Quaternion.identity) * r2 * Vector3.forward;
            return true;
        }
#endif
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
