// HTC Vive 用の入口。空の GameObject にこのコンポーネントを付ける。
// 既定は OpenXR の Eye Gaze Interaction で視線を取る。
// VIVE Pro Eye で OpenXR から視線が取れない場合は、SRanipal SDK を入れて
// Scripting Define Symbols に HFA_SRANIPAL を追加すると SRanipal 経由に切り替わる。
public class HfaVrVive : HfaVrApp
{
    protected override HfaGazeSource CreateEyeTracker()
    {
#if HFA_SRANIPAL
        return new HfaGazeSranipal();
#else
        return new HfaGazeOpenXr();
#endif
    }
}
