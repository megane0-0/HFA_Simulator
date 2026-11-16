// HFA 視野シミュレータ VIVE Focus Vision 版の本体。
// ゴーグルのカメラ映像（パススルー）を下敷きにして、左右の画面それぞれに、その眼の視野欠損を重ねる。
// 入口は HfaFocusVive で、このクラスを継承している。
// シーンには「VR カメラ」と「HfaFocusVive を付けた空の GameObject」だけあればよい。
// 霧を描く球・入力パネル・通知の文字は、起動時にコードで作る。
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace HfaFocus
{
public enum HfaEyeMode { PerEye, OD, OS, Both }
public enum HfaOutside { Extend, Clear }

[DefaultExecutionOrder(10000)]
public class HfaFocusApp : MonoBehaviour
{
    [Tooltip("ヘッドセットのカメラ。空なら Camera.main を使う")]
    public Camera headCamera;
    [Tooltip("PerEye=左右の画面にそれぞれの眼のデータ / OD・OS=両画面に片眼 / Both=両眼の良い方")]
    public HfaEyeMode eyeMode = HfaEyeMode.PerEye;
    [Tooltip("視野30度より外: Extend=外周の値を延長 / Clear=加工しない")]
    public HfaOutside outside = HfaOutside.Extend;
    [Tooltip("レベル1〜4の霧の濃さ（0=透明、1=景色が完全に隠れる）")]
    public float[] veilAlpha = { 0.35f, 0.65f, 0.9f, 1f };
    [Tooltip("霧の明るさ（0=黒、1=白）。部屋の明るさに近い値にする")]
    [Range(0f, 1f)] public float fogBrightness = 0.5f;
    [Tooltip("画面のアルファを乗算済みとして出力するか。半透明の部分が白く光って見えるなら切り替える")]
    public bool premultipliedAlpha = true;
    [Tooltip("視線のゆれを抑える時定数（秒）。0 で無効。大きいと欠損が視線に遅れる")]
    public float gazeSmoothing = 0.02f;
    public bool showGazeMark = false;
    [Tooltip("視線追跡を使わないとき、正面に固視目標（＋）を出す")]
    public bool showFixationCross = true;

    static readonly float[] FogSteps = { 0.3f, 0.5f, 0.7f };
    static readonly string[] FogNames = { "暗め", "中間", "明るめ" };

    HfaData data;
    bool editOs;                 // 入力中の眼（false=右眼 OD）
    int brush = 4;               // 塗るレベル
    int preset = 0;
    bool simEnabled = true;
    bool debugEye;

    HfaFocusGazeSource[] sources;
    int sourceIndex;
    Vector3 gazeLocal = Vector3.forward;   // 頭を基準にした視線方向
    bool gazeValid;
    bool gazeLost;                         // 視線が取れない状態が続いている（頭の正面で代用中）
    float lastGazeTime;

    HfaFocusPanel panel;
    Transform sphere;
    Texture2D mapL, mapR;
    TextMesh toast;
    GameObject toastBack;
    float toastUntil;
    bool mapsDirty = true, saveDirty;
    float saveAt;
    Vector3 lastMouse;

    // コントローラー（またはマウス）の状態
    Ray pointerRay;
    bool hasPointer, trigger, prevTrigger, pressOnButton;
    bool primary, prevPrimary, menu, prevMenu;
    readonly List<InputDevice> hands = new List<InputDevice>();
    static readonly InputFeatureUsage<Vector3> PointerPosition = new InputFeatureUsage<Vector3>("pointerPosition");
    static readonly InputFeatureUsage<Quaternion> PointerRotation = new InputFeatureUsage<Quaternion>("pointerRotation");

    static readonly int IdGaze = Shader.PropertyToID("_HfaGazeDir");
    static readonly int IdHeadPos = Shader.PropertyToID("_HfaHeadPos");
    static readonly int IdHeadRight = Shader.PropertyToID("_HfaHeadRight");
    static readonly int IdHeadUp = Shader.PropertyToID("_HfaHeadUp");

    // パススルー（カメラ映像の下敷き表示）を始める。機種・SDK ごとに派生クラスで差し替える。
    // 戻り値は利用者に見せる一言
    protected virtual string StartPassthrough()
    {
        return "パススルーの開始処理がありません（HfaFocusVive を使ってください）";
    }

    void Start()
    {
        if (headCamera == null) headCamera = Camera.main;
        if (headCamera == null)
        {
            Debug.LogError("[HFA] カメラが見つかりません。headCamera にヘッドセットのカメラを指定してください。");
            enabled = false;
            return;
        }
        // パススルー映像を下敷きにするので、カメラの背景は「透明な黒」で消去する。
        // アルファが 0 の画素にカメラ映像が見える
        headCamera.clearFlags = CameraClearFlags.SolidColor;
        headCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        headCamera.allowHDR = false;   // 中間の描画先を経由すると、アルファが失われることがある
        if (headCamera.farClipPlane < 120f) headCamera.farClipPlane = 120f;

        LoadSettings();
        data = HfaStore.Load();

        // 霧を描く球（頭の位置に追従、回転はしない）
        GameObject s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        s.name = "HFA Veil Sphere";
        Collider col = s.GetComponent<Collider>();
        if (col != null) Destroy(col);
        sphere = s.transform;
        sphere.localScale = Vector3.one * 100f;
        s.GetComponent<MeshRenderer>().material = new Material(Resources.Load<Shader>("HfaFocusVeil"));

        mapL = NewMap();
        mapR = NewMap();

        panel = new GameObject("HFA Panel").AddComponent<HfaFocusPanel>();
        panel.Build();
        panel.gameObject.SetActive(false);

        BuildToast();

        sources = new HfaFocusGazeSource[] { new HfaFocusGazeOpenXr(), new HfaFocusGazeHead(), new HfaFocusGazeMouse() };
        // ヘッドセットがつながっていなければ（エディタでの試用）マウスを視線にする
        if (Application.isEditor && !XRSettings.isDeviceActive) sourceIndex = 2;
        else if (sourceIndex == 2) sourceIndex = 0;

        lastMouse = HfaFocusInput.MousePosition;
        Application.onBeforeRender += PushPose;
        StartCoroutine(BeginPassthrough());
    }

    IEnumerator BeginPassthrough()
    {
        // XR の開始を待ってから要求する（早すぎると、要求は通っても映像が出ないことがある）
        float until = Time.realtimeSinceStartup + 10f;
        while (!XRSettings.isDeviceActive && Time.realtimeSinceStartup < until) yield return null;
        yield return new WaitForSecondsRealtime(0.5f);
        string msg;
        try { msg = StartPassthrough(); }
        catch (Exception e)
        {
            msg = "パススルーを開始できませんでした: " + e.Message;
            Debug.LogError("[HFA] " + msg);
        }
        Toast(msg + "\nメニューボタン（または B / Y）: 入力パネル　A / X: 欠損あり・なし", 8f);
    }

    void OnDestroy()
    {
        Application.onBeforeRender -= PushPose;
        if (saveDirty && data != null) HfaStore.Save(data);
    }

    // Android では、ゴーグルを外したりホームに戻ったりしたまま終了されることがあるので、中断時に保存する
    void OnApplicationPause(bool paused)
    {
        if (paused && saveDirty && data != null) { HfaStore.Save(data); saveDirty = false; }
    }

    // ---- 設定の保存（ビルド後は Inspector で変えられないため、ゴーグル内で変えた値を覚えておく） ----
    void LoadSettings()
    {
        eyeMode = (HfaEyeMode)Mathf.Clamp(PlayerPrefs.GetInt("hfa-focus-eye", (int)eyeMode), 0, 3);
        outside = (HfaOutside)Mathf.Clamp(PlayerPrefs.GetInt("hfa-focus-outside", (int)outside), 0, 1);
        fogBrightness = Mathf.Clamp01(PlayerPrefs.GetFloat("hfa-focus-fog", fogBrightness));
        premultipliedAlpha = PlayerPrefs.GetInt("hfa-focus-premul", premultipliedAlpha ? 1 : 0) != 0;
        sourceIndex = Mathf.Clamp(PlayerPrefs.GetInt("hfa-focus-gaze", 0), 0, 1);
    }

    void SaveSettings()
    {
        PlayerPrefs.SetInt("hfa-focus-eye", (int)eyeMode);
        PlayerPrefs.SetInt("hfa-focus-outside", (int)outside);
        PlayerPrefs.SetFloat("hfa-focus-fog", fogBrightness);
        PlayerPrefs.SetInt("hfa-focus-premul", premultipliedAlpha ? 1 : 0);
        if (sourceIndex < 2) PlayerPrefs.SetInt("hfa-focus-gaze", sourceIndex);
        PlayerPrefs.Save();
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

    // 左の画面には左眼（OS）、右の画面には右眼（OD）のデータを使う
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
        ReadInput();
        HandleInput();
        if (panel.gameObject.activeSelf) UpdatePanel();
        prevTrigger = trigger;

        if (mapsDirty) RebuildMaps();
        if (saveDirty && Time.unscaledTime >= saveAt) { HfaStore.Save(data); saveDirty = false; }
        if (toastBack.activeSelf && Time.unscaledTime > toastUntil) { toast.text = ""; toastBack.SetActive(false); }
        PushParams();
    }

    void LateUpdate() { PushPose(); }

    bool UsingMouse { get { return sources[sourceIndex] is HfaFocusGazeMouse; } }

    // ヘッドセットなしでエディタ実行したとき、右ドラッグで見回せるようにする
    void EditorLook()
    {
        Vector3 m = HfaFocusInput.MousePosition;
        if (!XRSettings.isDeviceActive && UsingMouse && HfaFocusInput.MouseHeld(1))
        {
            Vector3 d = m - lastMouse;
            Vector3 e = headCamera.transform.localEulerAngles;
            float pitch = e.x > 180f ? e.x - 360f : e.x;
            pitch = Mathf.Clamp(pitch - d.y * 0.2f, -85f, 85f);
            headCamera.transform.localEulerAngles = new Vector3(pitch, e.y + d.x * 0.2f, 0);
        }
        lastMouse = m;
    }

    // 視線は「頭を基準にした向き」で持つ。描画の直前に最新の頭の姿勢と合わせ直すので、
    // 頭を振っても欠損が画面の中でぶれない
    void UpdateGaze()
    {
        Vector3 dir;
        gazeValid = sources[sourceIndex].TryGetGaze(headCamera, out dir);
        Vector3 local;
        if (gazeValid)
        {
            lastGazeTime = Time.unscaledTime;
            gazeLost = false;
            local = headCamera.transform.InverseTransformDirection(dir.normalized);
        }
        else if (Time.unscaledTime - lastGazeTime > 0.5f)
        {
            gazeLost = true;
            local = Vector3.forward;   // 視線が取れない状態が続くときは、頭の正面で代用する
        }
        else return;                   // まばたきなど。直前の向きを保つ

        if (gazeSmoothing > 0f && gazeValid)
        {
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime / gazeSmoothing);
            gazeLocal = Vector3.Slerp(gazeLocal, local, k).normalized;
        }
        else gazeLocal = local;
    }

    Vector3 GazeWorld { get { return headCamera.transform.TransformDirection(gazeLocal); } }

    // 頭の姿勢と視線をシェーダーへ渡す（描画直前にも呼び、頭の動きへの遅れを減らす）
    void PushPose()
    {
        if (headCamera == null || sphere == null) return;
        Transform h = headCamera.transform;
        sphere.position = h.position;
        sphere.rotation = Quaternion.identity;
        Shader.SetGlobalVector(IdGaze, GazeWorld);
        Shader.SetGlobalVector(IdHeadPos, h.position);
        Shader.SetGlobalVector(IdHeadRight, h.right);
        Shader.SetGlobalVector(IdHeadUp, h.up);
    }

    void PushParams()
    {
        Shader.SetGlobalVector("_HfaVeil", new Vector4(veilAlpha[0], veilAlpha[1], veilAlpha[2], veilAlpha[3]));
        Shader.SetGlobalFloat("_HfaFog", fogBrightness);
        Shader.SetGlobalFloat("_HfaPremul", premultipliedAlpha ? 1f : 0f);
        Shader.SetGlobalFloat("_HfaFieldDeg", HfaField.FieldDeg);
        Shader.SetGlobalFloat("_HfaOutside", (float)(int)outside);
        Shader.SetGlobalFloat("_HfaShowGaze", showGazeMark ? 1f : 0f);
        // 固視目標は、欠損が頭の向きに固定されているとき（視線追跡なし・視線が取れていない）に出す
        bool headLocked = sources[sourceIndex] is HfaFocusGazeHead || gazeLost;
        Shader.SetGlobalFloat("_HfaCross", showFixationCross && headLocked && simEnabled ? 1f : 0f);
        Shader.SetGlobalFloat("_HfaDebugEye", debugEye ? 1f : 0f);
        Shader.SetGlobalFloat("_HfaEnabled", simEnabled ? 1f : 0f);
    }

    // ---- 入力 ----

    // コントローラー: 指す向き・引き金・A/X（primary）・B/Y とメニュー（menu）。右手を優先する
    void ReadInput()
    {
        hasPointer = false;
        trigger = primary = menu = false;
        hands.Clear();
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller, hands);
        Transform rig = headCamera.transform.parent;
        bool pointerIsRight = false;
        for (int i = 0; i < hands.Count; i++)
        {
            InputDevice d = hands[i];
            if (!d.isValid) continue;
            bool b;
            if (d.TryGetFeatureValue(CommonUsages.primaryButton, out b) && b) primary = true;
            if (d.TryGetFeatureValue(CommonUsages.secondaryButton, out b) && b) menu = true;
            if (d.TryGetFeatureValue(CommonUsages.menuButton, out b) && b) menu = true;

            bool right = (d.characteristics & InputDeviceCharacteristics.Right) != 0;
            if (hasPointer && (pointerIsRight || !right)) continue;
            Vector3 p = Vector3.zero;
            Quaternion q = Quaternion.identity;
            bool ok = d.TryGetFeatureValue(PointerPosition, out p) && d.TryGetFeatureValue(PointerRotation, out q);
            if (!ok) ok = d.TryGetFeatureValue(CommonUsages.devicePosition, out p) &&
                          d.TryGetFeatureValue(CommonUsages.deviceRotation, out q);
            if (!ok) continue;
            Vector3 wp = rig != null ? rig.TransformPoint(p) : p;
            Quaternion wq = rig != null ? rig.rotation * q : q;
            pointerRay = new Ray(wp, wq * Vector3.forward);
            hasPointer = true;
            pointerIsRight = right;
            float tv;
            trigger = (d.TryGetFeatureValue(CommonUsages.triggerButton, out b) && b) ||
                      (d.TryGetFeatureValue(CommonUsages.trigger, out tv) && tv > 0.6f);
        }

        // コントローラーがないとき（エディタでの試用）はマウスで指し、左ボタンを引き金にする
        if (!hasPointer && !XRSettings.isDeviceActive)
        {
            pointerRay = headCamera.ScreenPointToRay(HfaFocusInput.MousePosition);
            hasPointer = true;
            trigger = HfaFocusInput.MouseHeld(0);
        }
        if (HfaFocusInput.Down(KeyCode.M)) menu = !prevMenu;
        if (HfaFocusInput.Down(KeyCode.X)) primary = !prevPrimary;
    }

    void HandleInput()
    {
        if (menu && !prevMenu) TogglePanel();
        if (primary && !prevPrimary)
        {
            simEnabled = !simEnabled;
            Toast(simEnabled ? "欠損表示: あり" : "欠損表示: なし（そのままの景色）");
        }
        prevMenu = menu;
        prevPrimary = primary;
    }

    float[] EditArr { get { return editOs ? data.os : data.od; } }
    static string EyeName(bool os) { return os ? "左眼（OS）" : "右眼（OD）"; }

    string EyeModeName()
    {
        switch (eyeMode)
        {
            case HfaEyeMode.PerEye: return "左右それぞれの眼";
            case HfaEyeMode.OD: return "右眼（OD）のみ";
            case HfaEyeMode.OS: return "左眼（OS）のみ";
            default: return "両眼の良い方";
        }
    }

    string FogName()
    {
        for (int i = 0; i < FogSteps.Length; i++)
            if (Mathf.Abs(fogBrightness - FogSteps[i]) < 0.01f) return FogNames[i];
        return fogBrightness.ToString("0.00");
    }

    void Changed()
    {
        mapsDirty = true;
        saveDirty = true;
        saveAt = Time.unscaledTime + 0.5f;
    }

    void BuildToast()
    {
        // 頭に追従する短い通知（景色に埋もれないよう、暗い下地を敷く）
        Transform cam = headCamera.transform;
        toastBack = GameObject.CreatePrimitive(PrimitiveType.Quad);
        toastBack.name = "HFA Toast Back";
        Collider col = toastBack.GetComponent<Collider>();
        if (col != null) Destroy(col);
        toastBack.transform.SetParent(cam, false);
        toastBack.transform.localPosition = new Vector3(0, -0.28f, 1.0f);
        toastBack.transform.localScale = new Vector3(1.0f, 0.16f, 1f);
        var bm = new Material(Resources.Load<Shader>("HfaFocusUnlit"));
        bm.color = new Color(0f, 0f, 0f, 0.75f);
        bm.renderQueue = 3035;
        toastBack.GetComponent<MeshRenderer>().material = bm;
        toastBack.SetActive(false);

        var t = new GameObject("HFA Toast");
        t.transform.SetParent(cam, false);
        t.transform.localPosition = new Vector3(0, -0.28f, 1.0f);
        toast = t.AddComponent<TextMesh>();
        toast.font = HfaFocusPanel.CreateFont();
        toast.fontSize = 48;
        toast.characterSize = 0.0042f;
        toast.anchor = TextAnchor.MiddleCenter;
        toast.alignment = TextAlignment.Center;
        var fm = new Material(toast.font.material);
        fm.renderQueue = 3040;
        t.GetComponent<MeshRenderer>().sharedMaterial = fm;
    }

    void Toast(string s, float sec = 2.5f)
    {
        toast.text = s;
        toastBack.SetActive(true);
        toastUntil = Time.unscaledTime + sec;
    }

    void TogglePanel()
    {
        bool show = !panel.gameObject.activeSelf;
        panel.gameObject.SetActive(show);
        if (show) panel.PlaceInFrontOf(headCamera.transform);
    }

    void Paint(int i)
    {
        float v = HfaField.LevelDb[brush];
        if (EditArr[i] == v) return;
        EditArr[i] = v;
        Changed();
    }

    void UpdatePanel()
    {
        panel.UpdatePointer(pointerRay, hasPointer);

        // 引き金: ボタンの上で引いたらそのボタン、測定点の上なら塗る（引いたままなぞると連続で塗れる）
        bool pressed = trigger && !prevTrigger;
        if (pressed) pressOnButton = panel.HoverButton >= 0;
        if (pressed && pressOnButton) OnButton(panel.HoverButton);
        else if (trigger && !pressOnButton && panel.Hover >= 0) Paint(panel.Hover);
        if (!panel.gameObject.activeSelf) return;   // 「閉じる」を押した

        panel.SetLabel(HfaFocusPanel.BtnEye, "入力する眼: " + EyeName(editOs));
        panel.SetLabel(HfaFocusPanel.BtnEyeMode, "表示: " + EyeModeName());
        panel.SetLabel(HfaFocusPanel.BtnOutside, "30度より外: " + (outside == HfaOutside.Extend ? "外周の値を延長" : "加工しない"));
        panel.SetLabel(HfaFocusPanel.BtnGaze, "欠損の位置: " + sources[sourceIndex].Name);
        panel.SetLabel(HfaFocusPanel.BtnFog, "霧の明るさ: " + FogName());
        panel.SetLabel(HfaFocusPanel.BtnGazeMark, showGazeMark ? "視線の印: あり" : "視線の印: なし");
        panel.SetLabel(HfaFocusPanel.BtnDebugEye, debugEye ? "左右確認: 中" : "左右確認");
        panel.SetLabel(HfaFocusPanel.BtnPremul, premultipliedAlpha ? "半透明の合成: 方式A" : "半透明の合成: 方式B");

        string title = "入力中: " + EyeName(editOs) + "　　塗るレベル: " + brush + "（" + HfaField.LevelDb[brush] + " dB）";
        string hover = panel.Hover >= 0
            ? "指している点: (" + HfaField.Points[panel.Hover, 0] + ", " + HfaField.Points[panel.Hover, 1] + ")  "
              + Mathf.RoundToInt(EditArr[panel.Hover]) + " dB"
            : "指している点: なし";
        string gaze = gazeLost ? "視線が取れていません（頭の向きで代用中）\n" : "";
        string info = hover + "\n" + gaze +
            "点を指して引き金で塗る（引いたままなぞれます）\n" +
            "視線を固定した瞬間の見え方の再現です。医療機器ではありません。";
        panel.Refresh(EditArr, brush, title, HfaField.PresetNames[preset], info);
    }

    void OnButton(int id)
    {
        if (id >= HfaFocusPanel.BtnLevel0 && id < HfaFocusPanel.BtnLevel0 + 5)
        {
            brush = id - HfaFocusPanel.BtnLevel0;
            return;
        }
        int n = HfaField.PresetKeys.Length;
        switch (id)
        {
            case HfaFocusPanel.BtnEye: editOs = !editOs; break;
            case HfaFocusPanel.BtnPresetPrev: preset = (preset + n - 1) % n; break;
            case HfaFocusPanel.BtnPresetNext: preset = (preset + 1) % n; break;
            case HfaFocusPanel.BtnApply:
                HfaField.ApplyPreset(preset, EditArr, editOs);
                Toast(EyeName(editOs) + " に適用: " + HfaField.PresetNames[preset]);
                Changed();
                break;
            case HfaFocusPanel.BtnApplyBoth:
                // 眼ごとに計算するので、緑内障は左眼で左右反転し、同名半盲は両眼とも同じ側になる
                HfaField.ApplyPreset(preset, data.od, false);
                HfaField.ApplyPreset(preset, data.os, true);
                Toast("両眼に適用: " + HfaField.PresetNames[preset]);
                Changed();
                break;
            case HfaFocusPanel.BtnCopy:
                // Web版と同じく反転しないコピー（同名半盲用）。緑内障を両眼に入れるときは「両眼に適用」を使う
                float[] src = EditArr, dst = editOs ? data.od : data.os;
                for (int i = 0; i < HfaField.N; i++) dst[i] = src[i];
                Toast(EyeName(editOs) + " を " + EyeName(!editOs) + " へコピー（反転なし）");
                Changed();
                break;
            case HfaFocusPanel.BtnReset:
                HfaField.ApplyPreset(0, EditArr, editOs);
                Toast(EyeName(editOs) + " をリセット");
                Changed();
                break;
            case HfaFocusPanel.BtnResetBoth:
                HfaField.ApplyPreset(0, data.od, false);
                HfaField.ApplyPreset(0, data.os, true);
                Toast("両眼をリセット");
                Changed();
                break;
            case HfaFocusPanel.BtnEyeMode:
                eyeMode = (HfaEyeMode)(((int)eyeMode + 1) % 4);
                mapsDirty = true;
                SaveSettings();
                break;
            case HfaFocusPanel.BtnOutside:
                outside = (HfaOutside)(((int)outside + 1) % 2);
                SaveSettings();
                break;
            case HfaFocusPanel.BtnGaze:
                // 視線追跡 ⇔ 頭の向き（エディタでの試用中はマウスも巡回に入れる）
                int count = XRSettings.isDeviceActive ? 2 : 3;
                sourceIndex = (sourceIndex + 1) % count;
                SaveSettings();
                break;
            case HfaFocusPanel.BtnFog:
                int next = 0;
                for (int i = 0; i < FogSteps.Length; i++)
                    if (fogBrightness >= FogSteps[i] - 0.01f) next = (i + 1) % FogSteps.Length;
                fogBrightness = FogSteps[next];
                SaveSettings();
                break;
            case HfaFocusPanel.BtnGazeMark: showGazeMark = !showGazeMark; break;
            case HfaFocusPanel.BtnDebugEye:
                debugEye = !debugEye;
                if (debugEye) Toast("左右確認: 左眼に赤、右眼に青が見えれば正常です", 4f);
                break;
            case HfaFocusPanel.BtnPremul: premultipliedAlpha = !premultipliedAlpha; SaveSettings(); break;
            case HfaFocusPanel.BtnClose: TogglePanel(); break;
        }
    }
}
}
