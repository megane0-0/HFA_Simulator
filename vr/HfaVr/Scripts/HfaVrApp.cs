// HFA 視野シミュレータ VR版の本体。
// 機種ごとの入口は HfaVrFove（FOVE 用）と HfaVrVive（HTC Vive 用）で、このクラスを継承している。
// シーンには「VR カメラ」と「このコンポーネントを付けた空の GameObject」だけあればよい。
// 360度映像の球・入力パネル・表示用の文字は、起動時にコードで作る。
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

public enum HfaEyeMode { PerEye, OD, OS, Both }
public enum HfaOutside { Extend, Black, Clear }

[DefaultExecutionOrder(10000)]
public class HfaVrApp : MonoBehaviour
{
    [Tooltip("ヘッドセットのカメラ。空なら Camera.main を使う")]
    public Camera headCamera;
    [Tooltip("StreamingAssets の下の、360度映像を置くフォルダ名")]
    public string mediaFolder = "360";
    [Tooltip("レベル1〜4のぼかし幅（視角・度）。Web版の画角46度での見え方に合わせた既定値")]
    public float[] blurDeg = { 0.5f, 1f, 2.5f, 5f };
    [Tooltip("PerEye=左右の画面にそれぞれの眼のデータ / OD・OS=両画面に片眼 / Both=両眼の良い方")]
    public HfaEyeMode eyeMode = HfaEyeMode.PerEye;
    [Tooltip("視野30度より外: Extend=外周の値を延長 / Black=黒 / Clear=加工しない")]
    public HfaOutside outside = HfaOutside.Extend;
    [Tooltip("視線のゆれを抑える時定数（秒）。0 で無効。大きいと欠損が視線に遅れる")]
    public float gazeSmoothing = 0.02f;
    public bool showGazeMark = false;
    [Tooltip("映像の水平回転（度）")]
    public float mediaYawDeg = 0f;

    HfaData data;
    bool editOs;                 // 入力中の眼（false=右眼 OD）
    int brush = 4;               // 塗るレベル
    int preset = 0;
    bool simEnabled = true;
    bool debugEye;

    HfaGazeSource[] sources;
    int sourceIndex;
    Vector3 gazeDir = Vector3.forward;
    bool gazeValid;

    HfaMedia media;
    HfaPanel panel;
    Material sphereMat;
    Transform sphere;
    Texture2D mapL, mapR;
    TextMesh toast;
    float toastUntil;
    bool mapsDirty = true, saveDirty;
    float saveAt;
    Vector3 lastMouse;
    bool prevMenu, prevStickUp, prevStickDown;
    readonly List<InputDevice> hands = new List<InputDevice>();

    // 機種ごとの視線取得。派生クラスで差し替える
    protected virtual HfaGazeSource CreateEyeTracker() { return new HfaGazeOpenXr(); }

    static readonly int IdGaze = Shader.PropertyToID("_HfaGazeDir");
    static readonly int IdHeadPos = Shader.PropertyToID("_HfaHeadPos");
    static readonly int IdHeadRight = Shader.PropertyToID("_HfaHeadRight");
    static readonly int IdHeadUp = Shader.PropertyToID("_HfaHeadUp");

    void Start()
    {
        if (headCamera == null) headCamera = Camera.main;
        if (headCamera == null)
        {
            Debug.LogError("[HFA] カメラが見つかりません。headCamera にヘッドセットのカメラを指定してください。");
            enabled = false;
            return;
        }
        if (headCamera.farClipPlane < 120f) headCamera.farClipPlane = 120f;

        data = HfaStore.Load();

        // 360度映像を映す球（頭の位置に追従、回転はしない）
        GameObject s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        s.name = "HFA 360 Sphere";
        Collider col = s.GetComponent<Collider>();
        if (col != null) Destroy(col);
        sphere = s.transform;
        sphere.localScale = Vector3.one * 100f;
        sphereMat = new Material(Resources.Load<Shader>("HfaGazeSphere"));
        s.GetComponent<MeshRenderer>().material = sphereMat;

        media = gameObject.AddComponent<HfaMedia>();
        media.Scan(mediaFolder);

        mapL = NewMap();
        mapR = NewMap();

        panel = new GameObject("HFA Panel").AddComponent<HfaPanel>();
        panel.Build();
        panel.gameObject.SetActive(false);

        // 頭に追従する短い通知
        var t = new GameObject("HFA Toast");
        t.transform.SetParent(headCamera.transform, false);
        t.transform.localPosition = new Vector3(0, -0.28f, 1.0f);
        toast = t.AddComponent<TextMesh>();
        toast.font = Font.CreateDynamicFontFromOSFont(new[] { "Yu Gothic UI", "Meiryo UI", "Meiryo", "MS Gothic", "Arial" }, 48);
        toast.fontSize = 48;
        toast.characterSize = 0.0045f;
        toast.anchor = TextAnchor.MiddleCenter;
        toast.alignment = TextAlignment.Center;
        var fm = new Material(toast.font.material);
        fm.renderQueue = 3040;
        t.GetComponent<MeshRenderer>().sharedMaterial = fm;

        sources = new HfaGazeSource[] { CreateEyeTracker(), new HfaGazeHead(), new HfaGazeMouse() };
        foreach (HfaGazeSource g in sources) g.Begin(headCamera);
        // ヘッドセットがつながっていなければ（エディタでの試用）マウスを視線にする
        bool noHeadset = Application.isEditor && !XRSettings.isDeviceActive && sources[0] is HfaGazeOpenXr;
        sourceIndex = noHeadset ? 2 : 0;

        gazeDir = headCamera.transform.forward;
        lastMouse = HfaKeys.MousePosition;
        Application.onBeforeRender += PushPose;
        Toast("M: 入力パネル　E: 表示する眼　N: 映像　G: 視線の取得元\nデータ: " + HfaStore.FilePath, 8f);
    }

    void OnDestroy()
    {
        Application.onBeforeRender -= PushPose;
        if (saveDirty && data != null) HfaStore.Save(data);
    }

    static Texture2D NewMap()
    {
        var t = new Texture2D(HfaField.GridSize, HfaField.GridSize, TextureFormat.RGBA32, false, true);
        t.wrapMode = TextureWrapMode.Clamp;
        t.filterMode = FilterMode.Bilinear;
        return t;
    }

    // dB グリッドをテクスチャへ。グリッドは行0が上、テクスチャは行0が下なので上下を入れ替える
    static void FillMap(Texture2D tex, float[] db)
    {
        float[] g = HfaField.ComputeGrid(db);
        int n = HfaField.GridSize;
        var px = new Color32[n * n];
        for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++)
            {
                byte v = (byte)Mathf.Clamp(Mathf.RoundToInt(g[r * n + c] / HfaField.DbMax * 255f), 0, 255);
                px[(n - 1 - r) * n + c] = new Color32(v, v, v, 255);
            }
        tex.SetPixels32(px);
        tex.Apply(false);
    }

    void RebuildMaps()
    {
        mapsDirty = false;
        switch (eyeMode)
        {
            case HfaEyeMode.PerEye: FillMap(mapL, data.os); FillMap(mapR, data.od); break;
            case HfaEyeMode.OD: FillMap(mapL, data.od); FillMap(mapR, data.od); break;
            case HfaEyeMode.OS: FillMap(mapL, data.os); FillMap(mapR, data.os); break;
            default:
                float[] b = HfaField.BetterEye(data.od, data.os);
                FillMap(mapL, b); FillMap(mapR, b);
                break;
        }
        Shader.SetGlobalTexture("_HfaMapL", mapL);
        Shader.SetGlobalTexture("_HfaMapR", mapR);
    }

    void Update()
    {
        if (data == null) return;
        EditorLook();
        UpdateGaze();
        HandleKeys();
        HandleController();
        if (panel.gameObject.activeSelf) UpdatePanel();

        if (mapsDirty) RebuildMaps();
        if (saveDirty && Time.unscaledTime >= saveAt) { HfaStore.Save(data); saveDirty = false; }
        if (toast.text.Length > 0 && Time.unscaledTime > toastUntil) toast.text = "";
        PushParams();
    }

    void LateUpdate() { PushPose(); }

    // ヘッドセットなしでエディタ実行したとき、右ドラッグで見回せるようにする
    void EditorLook()
    {
        Vector3 m = HfaKeys.MousePosition;
        if (!XRSettings.isDeviceActive && sources[sourceIndex] is HfaGazeMouse && HfaKeys.RightMouseHeld)
        {
            Vector3 d = m - lastMouse;
            Vector3 e = headCamera.transform.localEulerAngles;
            float pitch = e.x > 180f ? e.x - 360f : e.x;
            pitch = Mathf.Clamp(pitch - d.y * 0.2f, -85f, 85f);
            headCamera.transform.localEulerAngles = new Vector3(pitch, e.y + d.x * 0.2f, 0);
        }
        lastMouse = m;
    }

    void UpdateGaze()
    {
        Vector3 dir;
        gazeValid = sources[sourceIndex].TryGetGaze(headCamera, out dir);
        if (!gazeValid) return;   // まばたきなど。直前の向きを保つ
        dir.Normalize();
        if (gazeSmoothing > 0f)
        {
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime / gazeSmoothing);
            gazeDir = Vector3.Slerp(gazeDir, dir, k).normalized;
        }
        else gazeDir = dir;
    }

    // 頭の姿勢と視線をシェーダーへ渡す（描画直前にも呼び、頭の動きへの遅れを減らす）
    void PushPose()
    {
        if (headCamera == null || sphere == null) return;
        Transform h = headCamera.transform;
        sphere.position = h.position;
        sphere.rotation = Quaternion.identity;
        Shader.SetGlobalVector(IdGaze, gazeDir);
        Shader.SetGlobalVector(IdHeadPos, h.position);
        Shader.SetGlobalVector(IdHeadRight, h.right);
        Shader.SetGlobalVector(IdHeadUp, h.up);
    }

    void PushParams()
    {
        Texture tex = media.Current;
        if (tex != null && sphereMat.mainTexture != tex) sphereMat.mainTexture = tex;
        float pxPerDeg = (tex != null ? tex.width : 4096) / 360f;
        Shader.SetGlobalVector("_HfaBlurPx", new Vector4(
            blurDeg[0] * pxPerDeg, blurDeg[1] * pxPerDeg, blurDeg[2] * pxPerDeg, blurDeg[3] * pxPerDeg));
        Shader.SetGlobalFloat("_HfaFieldDeg", HfaField.FieldDeg);
        Shader.SetGlobalFloat("_HfaOutside", (float)(int)outside);
        Shader.SetGlobalFloat("_HfaShowGaze", showGazeMark ? 1f : 0f);
        Shader.SetGlobalFloat("_HfaDebugEye", debugEye ? 1f : 0f);
        Shader.SetGlobalFloat("_HfaYaw", mediaYawDeg * Mathf.Deg2Rad);
        Shader.SetGlobalFloat("_HfaEnabled", simEnabled ? 1f : 0f);
    }

    float[] EditArr { get { return editOs ? data.os : data.od; } }
    string EyeName(bool os) { return os ? "左眼（OS）" : "右眼（OD）"; }

    string EyeModeName()
    {
        switch (eyeMode)
        {
            case HfaEyeMode.PerEye: return "左右それぞれの眼";
            case HfaEyeMode.OD: return "右眼（OD）のみ";
            case HfaEyeMode.OS: return "左眼（OS）のみ";
            default: return "両眼重ね合わせ（良い方）";
        }
    }

    string OutsideName()
    {
        switch (outside)
        {
            case HfaOutside.Extend: return "外周の値を延長";
            case HfaOutside.Black: return "黒";
            default: return "加工しない";
        }
    }

    void Changed()
    {
        mapsDirty = true;
        saveDirty = true;
        saveAt = Time.unscaledTime + 0.5f;
    }

    void Toast(string s, float sec = 2.5f)
    {
        toast.text = s;
        toastUntil = Time.unscaledTime + sec;
    }

    void TogglePanel()
    {
        bool show = !panel.gameObject.activeSelf;
        panel.gameObject.SetActive(show);
        if (show) panel.PlaceInFrontOf(headCamera.transform);
    }

    void HandleKeys()
    {
        bool shift = HfaKeys.Shift;

        // ---- いつでも使える操作 ----
        if (HfaKeys.Down(KeyCode.M)) TogglePanel();
        if (HfaKeys.Down(KeyCode.E))
        {
            eyeMode = (HfaEyeMode)(((int)eyeMode + 1) % 4);
            mapsDirty = true;
            Toast("表示: " + EyeModeName());
        }
        if (HfaKeys.Down(KeyCode.O))
        {
            outside = (HfaOutside)(((int)outside + 1) % 3);
            Toast("視野30度より外: " + OutsideName());
        }
        if (HfaKeys.Down(KeyCode.N)) { media.Next(); Toast("映像: " + media.CurrentName); }
        if (HfaKeys.Down(KeyCode.G))
        {
            sourceIndex = (sourceIndex + 1) % sources.Length;
            Toast("視線の取得元: " + sources[sourceIndex].Name);
        }
        if (HfaKeys.Down(KeyCode.V)) { showGazeMark = !showGazeMark; Toast(showGazeMark ? "視線の印: 表示" : "視線の印: 非表示"); }
        if (HfaKeys.Down(KeyCode.X)) { simEnabled = !simEnabled; Toast(simEnabled ? "欠損表示: あり" : "欠損表示: なし（元の映像）"); }
        if (HfaKeys.Down(KeyCode.F1)) { sources[sourceIndex].Calibrate(); Toast("視線の較正を開始"); }
        if (HfaKeys.Down(KeyCode.F2)) { debugEye = !debugEye; Toast(debugEye ? "左右確認: 左の画面=赤 / 右の画面=青" : "左右確認: 終了"); }
        if (HfaKeys.Down(KeyCode.LeftBracket)) mediaYawDeg -= 15f;
        if (HfaKeys.Down(KeyCode.RightBracket)) mediaYawDeg += 15f;

        // ---- 入力パネルを開いているときの操作 ----
        if (!panel.gameObject.activeSelf) return;

        if (HfaKeys.Down(KeyCode.Tab)) editOs = !editOs;
        for (int i = 0; i <= 4; i++)
            if (HfaKeys.Down(KeyCode.Alpha0 + i)) brush = i;

        int hover = panel.Hover;
        if (hover >= 0)
        {
            if (HfaKeys.Held(KeyCode.Space)) Paint(hover);
            int step = shift ? 5 : 1;
            if (HfaKeys.Down(KeyCode.UpArrow)) { EditArr[hover] = HfaField.Clamp(EditArr[hover] + step); Changed(); }
            if (HfaKeys.Down(KeyCode.DownArrow)) { EditArr[hover] = HfaField.Clamp(EditArr[hover] - step); Changed(); }
        }

        if (HfaKeys.Down(KeyCode.P))
        {
            int n = HfaField.PresetKeys.Length;
            preset = (preset + (shift ? n - 1 : 1)) % n;
        }
        if (HfaKeys.Down(KeyCode.Return))
        {
            if (shift)
            {
                // 両眼に適用: 眼ごとに計算するので、緑内障は左眼で左右反転し、同名半盲は両眼同じ側になる
                HfaField.ApplyPreset(preset, data.od, false);
                HfaField.ApplyPreset(preset, data.os, true);
                Toast("両眼に適用: " + HfaField.PresetNames[preset]);
            }
            else
            {
                HfaField.ApplyPreset(preset, EditArr, editOs);
                Toast(EyeName(editOs) + " に適用: " + HfaField.PresetNames[preset]);
            }
            Changed();
        }
        if (HfaKeys.Down(KeyCode.C))
        {
            // Web版と同じく反転しないコピー（同名半盲用）。緑内障を両眼に入れるときは Shift+Enter を使う
            float[] src = EditArr, dst = editOs ? data.od : data.os;
            for (int i = 0; i < HfaField.N; i++) dst[i] = src[i];
            Toast(EyeName(editOs) + " を " + EyeName(!editOs) + " へコピー（反転なし）");
            Changed();
        }
        if (HfaKeys.Down(KeyCode.R))
        {
            if (shift) { HfaField.ApplyPreset(0, data.od, false); HfaField.ApplyPreset(0, data.os, true); Toast("両眼をリセット"); }
            else { HfaField.ApplyPreset(0, EditArr, editOs); Toast(EyeName(editOs) + " をリセット"); }
            Changed();
        }
    }

    void Paint(int i)
    {
        float v = HfaField.LevelDb[brush];
        if (EditArr[i] == v) return;
        EditArr[i] = v;
        Changed();
    }

    // Vive などのコントローラー: トリガー=塗る / メニュー=パネル開閉 / パッド上下=塗るレベル
    bool TryController(out Ray ray, out bool trigger)
    {
        ray = new Ray();
        trigger = false;
        hands.Clear();
        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Right, hands);
        if (hands.Count == 0)
            InputDevices.GetDevicesWithCharacteristics(
                InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Left, hands);
        if (hands.Count == 0 || !hands[0].isValid) return false;
        InputDevice d = hands[0];

        Vector3 p; Quaternion q;
        if (!d.TryGetFeatureValue(CommonUsages.devicePosition, out p) ||
            !d.TryGetFeatureValue(CommonUsages.deviceRotation, out q)) return false;
        Transform rig = headCamera.transform.parent;
        Vector3 wp = rig != null ? rig.TransformPoint(p) : p;
        Quaternion wq = rig != null ? rig.rotation * q : q;
        ray = new Ray(wp, wq * Vector3.forward);
        d.TryGetFeatureValue(CommonUsages.triggerButton, out trigger);

        bool menu;
        d.TryGetFeatureValue(CommonUsages.menuButton, out menu);
        if (menu && !prevMenu) TogglePanel();
        prevMenu = menu;

        Vector2 axis;
        d.TryGetFeatureValue(CommonUsages.primary2DAxis, out axis);
        bool click = true, hasClick = d.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out click);
        bool up = axis.y > 0.6f && (!hasClick || click);
        bool down = axis.y < -0.6f && (!hasClick || click);
        if (panel.gameObject.activeSelf)
        {
            if (up && !prevStickUp) brush = Mathf.Max(0, brush - 1);
            if (down && !prevStickDown) brush = Mathf.Min(4, brush + 1);
        }
        prevStickUp = up; prevStickDown = down;
        return true;
    }

    Ray pointerRay;
    bool controllerTrigger, hasController;

    void HandleController()
    {
        hasController = TryController(out pointerRay, out controllerTrigger);
        if (!hasController) pointerRay = new Ray(headCamera.transform.position, gazeDir);
    }

    void UpdatePanel()
    {
        panel.UpdatePointer(pointerRay);
        if (hasController && controllerTrigger && panel.Hover >= 0) Paint(panel.Hover);

        string title = "入力中: " + EyeName(editOs) + "　　塗るレベル: " + brush + "（" + HfaField.LevelDb[brush] + " dB）";
        string hover = panel.Hover >= 0
            ? "指している点: (" + HfaField.Points[panel.Hover, 0] + ", " + HfaField.Points[panel.Hover, 1] + ")  "
              + Mathf.RoundToInt(EditArr[panel.Hover]) + " dB"
            : "指している点: なし";
        string info =
            hover + "\n" +
            "プリセット: " + HfaField.PresetNames[preset] + "\n" +
            "表示: " + EyeModeName() + "\n" +
            "視線: " + sources[sourceIndex].Name + (gazeValid ? "" : "（取得できていません）") + "\n" +
            "映像: " + media.CurrentName + "\n\n" +
            "[見る＋Space／トリガー] 塗る\n" +
            "[0-4] 塗るレベル　[↑↓] ±1dB（Shiftで±5）\n" +
            "[Tab] 右眼／左眼\n" +
            "[P] プリセット選択　[Enter] この眼に適用\n" +
            "[Shift+Enter] 両眼に適用\n" +
            "[C] もう一方へコピー　[R] リセット\n" +
            "[E] 表示する眼　[O] 30度より外\n" +
            "[N] 映像　[G] 視線の取得元　[V] 視線の印\n" +
            "[X] 欠損あり／なし　[F1] 較正　[M] 閉じる\n\n" +
            "視線を固定した瞬間の見え方の再現です。\n医療機器ではありません。";
        panel.Refresh(EditArr, title, info);
    }
}
