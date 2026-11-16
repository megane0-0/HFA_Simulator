// FOVE（FOVE0 / FOVE480）の視線取得。FOVE Unity Plugin v4 系を使う。
// プラグインを入れたプロジェクトで、Scripting Define Symbols に HFA_FOVE を追加すると有効になる。
#if HFA_FOVE
using Fove.Unity;
using UnityEngine;

public class HfaGazeFove : HfaGazeSource
{
    FoveInterface fove;

    public override string Name { get { return "視線追跡（FOVE）"; } }

    public override void Begin(Camera head)
    {
        fove = head.GetComponent<FoveInterface>();
        if (fove == null) fove = Object.FindObjectOfType<FoveInterface>();
        if (fove == null) Debug.LogWarning("[HFA] FoveInterface が見つかりません。カメラに Fove Interface を付けてください。");
        FoveManager.RegisterCapabilities(Fove.ClientCapabilities.EyeTracking);
    }

    public override bool TryGetGaze(Camera head, out Vector3 dir)
    {
        dir = Vector3.forward;
        if (fove == null) return false;
        var r = fove.GetCombinedGazeRay();   // ワールド座標の視線
        if (r.Failed) return false;          // まばたき・未較正など。直前の視線を保つ
        dir = r.value.direction;
        return dir.sqrMagnitude > 1e-6f;
    }

    public override void Calibrate()
    {
        FoveManager.StartEyeTrackingCalibration();
    }
}
#endif
