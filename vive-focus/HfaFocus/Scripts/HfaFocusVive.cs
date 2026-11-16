// VIVE Focus Vision 用の入口。空の GameObject にこのコンポーネントを付ける。
//
// パススルー（カメラ映像の下敷き表示）を始める呼び出しは SDK ごとに違うので、
// Player Settings の Scripting Define Symbols で、入れた SDK に合うものを1つ選ぶ。
//
//   HFA_VIVE_OPENXR      VIVE OpenXR プラグイン（現行の API リファレンスの形。ハンドルを out 引数で受け取る）
//   HFA_VIVE_OPENXR_OLD  VIVE OpenXR プラグイン（公式チュートリアルに載っている形。ID を戻り値で受け取る）
//   HFA_WAVE             VIVE Wave XR プラグイン
//
// どれも定義しなければ、このスクリプトはパススルーを始めない。その場合は、SDK に付属する
// パススルー（アンダーレイ）のサンプルスクリプトをシーンに置けば、同じように動く
// （このプログラムが必要とするのは「カメラの背景が透明」と「下敷きにカメラ映像がある」ことだけ）。
//
// 呼び出しの形は HTC の公開資料で確認したが、実物の SDK でのコンパイルは確認していない。
//   OpenXR: https://hub.vive.com/apidoc/api/VIVE.OpenXR.Passthrough.PassthroughAPI.html
//           https://developer.vive.com/resources/openxr/unity/tutorials/passthrough/
//   Wave:   https://hub.vive.com/storage/docs/en-us/UnityXR/UnityXRPassthrough.html
namespace HfaFocus
{
public class HfaFocusVive : HfaFocusApp
{
    protected override bool StartPassthrough(bool resumed, out string message)
    {
#if HFA_VIVE_OPENXR
        // パススルーは OpenXR のセッションが破棄されると自動で消える。
        // 中断から戻ったときは、残っていれば作り直さない
        var current = VIVE.OpenXR.Passthrough.PassthroughAPI.GetCurrentPassthroughLayerIDs();
        if (current != null && current.Count > 0)
        {
            message = "パススルー: 表示中";
            return true;
        }
        VIVE.OpenXR.Passthrough.XrPassthroughHTC handle;
        VIVE.OpenXR.XrResult result = VIVE.OpenXR.Passthrough.PassthroughAPI.CreatePlanarPassthrough(
            out handle, VIVE.OpenXR.CompositionLayer.LayerType.Underlay);
        bool ok = result == VIVE.OpenXR.XrResult.XR_SUCCESS;
        message = ok ? "パススルー: 開始しました" : "パススルーを開始できませんでした（" + result + "）";
        return ok;
#elif HFA_VIVE_OPENXR_OLD
        if (resumed)
        {
            message = "";
            return true;
        }
        VIVE.OpenXR.Passthrough.PassthroughAPI.CreatePlanarPassthrough(
            VIVE.OpenXR.CompositionLayer.LayerType.Underlay);
        message = "パススルー: 開始を要求しました";
        return true;
#elif HFA_WAVE
        // 戻り値は WVR_Result で、0 が成功
        var result = Wave.Native.Interop.WVR_ShowPassthroughUnderlay(true);
        bool ok = (int)result == 0;
        message = ok ? "パススルー: 開始しました（Wave）" : "パススルーを開始できませんでした（" + result + "）";
        return ok;
#else
        message = "パススルー: このスクリプトからは開始していません";
        return true;
#endif
    }
}
}
