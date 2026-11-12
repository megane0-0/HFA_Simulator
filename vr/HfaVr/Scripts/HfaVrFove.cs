// FOVE（FOVE0 / FOVE480）用の入口。空の GameObject にこのコンポーネントを付ける。
// FOVE Unity Plugin を入れ、Scripting Define Symbols に HFA_FOVE を追加しておくこと。
using UnityEngine;

public class HfaVrFove : HfaVrApp
{
    protected override HfaGazeSource CreateEyeTracker()
    {
#if HFA_FOVE
        return new HfaGazeFove();
#else
        Debug.LogError("[HFA] HFA_FOVE が定義されていません。Player Settings の Scripting Define Symbols に HFA_FOVE を追加してください。頭の向きを視線として動かします。");
        return new HfaGazeHead();
#endif
    }
}
