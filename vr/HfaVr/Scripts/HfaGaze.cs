// 視線の取得元。機種ごとの差はここだけに閉じ込める。
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

public abstract class HfaGazeSource
{
    public abstract string Name { get; }
    public virtual void Begin(Camera head) { }
    // 両眼を合わせた視線方向（ワールド座標）。取得できないフレームは false（呼び出し側が直前の値を保つ）
    public abstract bool TryGetGaze(Camera head, out Vector3 dir);
    public virtual void Calibrate() { }
}

// 視線追跡なし: 頭の正面を視線とみなす
public class HfaGazeHead : HfaGazeSource
{
    public override string Name { get { return "頭の向き"; } }
    public override bool TryGetGaze(Camera head, out Vector3 dir)
    {
        dir = head.transform.forward;
        return true;
    }
}

// マウス位置を視線とみなす（ヘッドセットなしで Unity エディタ上で試すため）
public class HfaGazeMouse : HfaGazeSource
{
    public override string Name { get { return "マウス"; } }
    public override bool TryGetGaze(Camera head, out Vector3 dir)
    {
        dir = head.ScreenPointToRay(HfaKeys.MousePosition).direction;
        return true;
    }
}

// OpenXR の Eye Gaze Interaction（HTC Vive 系など）。
// OpenXR パッケージに直接依存しないよう、機能名は文字列で指定している
public class HfaGazeOpenXr : HfaGazeSource
{
    static readonly InputFeatureUsage<Quaternion> GazeRotation = new InputFeatureUsage<Quaternion>("gazeRotation");
    static readonly InputFeatureUsage<bool> GazeTracked = new InputFeatureUsage<bool>("gazeTracked");
    readonly List<InputDevice> devices = new List<InputDevice>();

    public override string Name { get { return "視線追跡（OpenXR）"; } }

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
                // 旧来の eyesData を出す実装向け
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
