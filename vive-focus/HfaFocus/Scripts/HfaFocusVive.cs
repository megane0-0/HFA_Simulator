// VIVE Focus Vision 用の入口。空の GameObject にこのコンポーネントを付ける。
//
// パススルー（カメラ映像の下敷き表示）を始める呼び出しは SDK ごとに違うので、
// Player Settings の Scripting Define Symbols で、入れた SDK に合うものを1つ選ぶ。
//
//   HFA_VIVE_OPENXR      VIVE OpenXR プラグイン（ID を out 引数で受け取る版）
//   HFA_VIVE_OPENXR_OLD  VIVE OpenXR プラグイン（ID を戻り値で受け取る古い版）
//   HFA_WAVE             VIVE Wave XR プラグイン
//
// どれも定義しなければ、このスクリプトはパススルーを始めない。その場合は、SDK に付属する
// パススルー（アンダーレイ）のサンプルスクリプトをシーンに置けば、同じように動く
// （このプログラムが必要とするのは「カメラの背景が透明」と「下敷きにカメラ映像がある」ことだけ）。
//
// 下の呼び出しは公開資料をもとに書いたもので、実物の SDK でのコンパイルは確認していない。
// エラーになる場合は、上の「定義しない」方法に切り替えること。
namespace HfaFocus
{
public class HfaFocusVive : HfaFocusApp
{
    protected override string StartPassthrough()
    {
#if HFA_VIVE_OPENXR
        VIVE.OpenXR.Passthrough.PassthroughAPI.CreatePlanarPassthrough(
            out var id, VIVE.OpenXR.CompositionLayer.LayerType.Underlay);
        return "パススルー: 開始を要求しました（VIVE OpenXR）";
#elif HFA_VIVE_OPENXR_OLD
        VIVE.OpenXR.Passthrough.PassthroughAPI.CreatePlanarPassthrough(
            VIVE.OpenXR.CompositionLayer.LayerType.Underlay);
        return "パススルー: 開始を要求しました（VIVE OpenXR）";
#elif HFA_WAVE
        Wave.Native.Interop.WVR_ShowPassthroughUnderlay(true);
        return "パススルー: 開始を要求しました（Wave）";
#else
        return "パススルー: このスクリプトからは開始していません";
#endif
    }
}
}
