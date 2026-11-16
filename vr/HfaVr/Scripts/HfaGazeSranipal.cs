// VIVE Pro Eye を SRanipal SDK で使う場合の視線取得（OpenXR で視線が取れないときの代替）。
// SRanipal の Unity パッケージを入れ、Scripting Define Symbols に HFA_SRANIPAL を追加すると有効になる。
#if HFA_SRANIPAL
using UnityEngine;
using ViveSR.anipal.Eye;

public class HfaGazeSranipal : HfaGazeSource
{
    public override string Name { get { return "視線追跡（SRanipal）"; } }

    public override void Begin(Camera head)
    {
        if (Object.FindObjectOfType<SRanipal_Eye_Framework>() == null)
        {
            var fw = new GameObject("SRanipal Eye Framework").AddComponent<SRanipal_Eye_Framework>();
            fw.EnableEye = true;
            fw.EnableEyeVersion = SRanipal_Eye_Framework.SupportedEyeVersion.version2;
        }
    }

    public override bool TryGetGaze(Camera head, out Vector3 dir)
    {
        dir = Vector3.forward;
        if (SRanipal_Eye_Framework.Status != SRanipal_Eye_Framework.FrameworkStatus.WORKING) return false;
        Vector3 origin, local;
        if (!SRanipal_Eye_v2.GetGazeRay(GazeIndex.COMBINE, out origin, out local)) return false;
        dir = head.transform.TransformDirection(local);   // 頭基準 → ワールド
        return dir.sqrMagnitude > 1e-6f;
    }

    public override void Calibrate()
    {
        SRanipal_Eye_v2.LaunchEyeCalibration();
    }
}
#endif
