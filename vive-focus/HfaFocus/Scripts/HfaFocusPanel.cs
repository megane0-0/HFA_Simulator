// ゴーグルの中に浮かぶ入力パネル。76点を Web版と同じ並び・同じ色（患者から見た向き）で表示し、
// 右側にボタンを並べる。VIVE Focus Vision は単体で動くのでキーボードがなく、
// すべての操作をコントローラーで指して引き金を引くだけで行えるようにしてある。
// プレハブや uGUI を使わず、すべてコードで組み立てる（シーン側の準備を最小にするため）。
using UnityEngine;

namespace HfaFocus
{
public class HfaFocusPanel : MonoBehaviour
{
    // ボタンの番号
    public const int BtnEye = 0;          // 入力する眼
    public const int BtnLevel0 = 1;       // 塗るレベル 0〜4（1〜5）
    public const int BtnPresetPrev = 6;
    public const int BtnPresetNext = 7;
    public const int BtnApply = 8;        // この眼に適用
    public const int BtnApplyBoth = 9;    // 両眼に適用
    public const int BtnCopy = 10;        // もう一方へコピー
    public const int BtnReset = 11;
    public const int BtnResetBoth = 12;
    public const int BtnEyeMode = 13;     // 表示する眼
    public const int BtnOutside = 14;     // 30度より外
    public const int BtnGaze = 15;        // 視線の取得元
    public const int BtnFog = 16;         // 霧の明るさ
    public const int BtnGazeMark = 17;    // 視線の印
    public const int BtnDebugEye = 18;    // 左右確認
    public const int BtnPremul = 19;      // アルファの合成方式
    public const int BtnClose = 20;
    public const int ButtonCount = 21;

    const float MetersPerDeg = 0.01f;   // 視野1度 = パネル上の1cm
    const float CellSize = 0.05f;
    const float PickRadius = 0.034f;

    static readonly Color[] LevelColors = {
        new Color32(0x2e, 0x7d, 0x32, 255), new Color32(0xf9, 0xa8, 0x25, 255),
        new Color32(0xef, 0x6c, 0x00, 255), new Color32(0xc6, 0x28, 0x28, 255),
        new Color32(0x21, 0x21, 0x21, 255)
    };
    static readonly Color ButtonColor = new Color(0.20f, 0.33f, 0.55f, 1f);
    static readonly Color DangerColor = new Color(0.55f, 0.16f, 0.16f, 1f);

    class Button
    {
        public Vector2 center, size;
        public Material mat;
        public TextMesh text;
        public Color color;
    }

    readonly Material[] cellMats = new Material[HfaField.N];
    readonly TextMesh[] cellTexts = new TextMesh[HfaField.N];
    readonly Transform[] cells = new Transform[HfaField.N];
    readonly Button[] buttons = new Button[ButtonCount];
    TextMesh title, info, presetLabel;
    Transform cursor, highlight, brushFrame;
    Shader unlit;
    Font font;
    Material fontMat;

    public int Hover { get; private set; }         // 指している測定点（なければ -1）
    public int HoverButton { get; private set; }   // 指しているボタン（なければ -1）

    // 日本語の入った OS のフォントを探す（Android と Windows の両方の名前を並べてある）
    public static Font CreateFont()
    {
        return Font.CreateDynamicFontFromOSFont(new[] {
            "Noto Sans CJK JP", "NotoSansCJK-Regular", "Noto Sans JP", "Droid Sans Fallback", "Roboto",
            "Yu Gothic UI", "Meiryo UI", "Meiryo", "MS Gothic", "Arial" }, 48);
    }

    public void Build()
    {
        Hover = -1;
        HoverButton = -1;
        unlit = Resources.Load<Shader>("HfaFocusUnlit");
        font = CreateFont();
        fontMat = new Material(font.material);
        fontMat.renderQueue = 3020;

        Quad("Background", new Vector3(0.17f, -0.02f, 0), new Vector2(1.06f, 0.94f), new Color(0.08f, 0.09f, 0.12f, 0.95f), 3000);
        highlight = Quad("Highlight", Vector3.zero, new Vector2(CellSize + 0.014f, CellSize + 0.014f), Color.white, 3005).transform;
        highlight.gameObject.SetActive(false);

        for (int i = 0; i < HfaField.N; i++)
        {
            var p = new Vector3(HfaField.Points[i, 0] * MetersPerDeg, HfaField.Points[i, 1] * MetersPerDeg, 0);
            GameObject q = Quad("P" + i, p, new Vector2(CellSize, CellSize), LevelColors[0], 3010);
            cells[i] = q.transform;
            cellMats[i] = q.GetComponent<MeshRenderer>().material;
            cellTexts[i] = Text("T" + i, p, 0.0042f, TextAnchor.MiddleCenter);
        }
        // 水平・垂直経線
        Quad("AxisH", Vector3.zero, new Vector2(0.6f, 0.002f), new Color(1, 1, 1, 0.5f), 3004);
        Quad("AxisV", Vector3.zero, new Vector2(0.002f, 0.6f), new Color(1, 1, 1, 0.5f), 3004);

        title = Text("Title", new Vector3(-0.3f, 0.385f, 0), 0.0055f, TextAnchor.MiddleLeft);
        info = Text("Info", new Vector3(-0.3f, -0.335f, 0), 0.0036f, TextAnchor.UpperLeft);

        // ---- 右側のボタン列 ----
        const float x0 = 0.35f, w = 0.30f, pitch = 0.056f, top = 0.335f, gap = 0.006f;
        float half = (w - gap) / 2f;
        float xl = x0 + half / 2f, xr = x0 + w - half / 2f, xc = x0 + w / 2f;
        int row = 0;

        AddButton(BtnEye, xc, top - pitch * row++, w, "", ButtonColor, 0.0034f);

        // 塗るレベル（選択中のものに白い枠）
        float bw = (w - gap * 4f) / 5f;
        float yLevel = top - pitch * row++;
        brushFrame = Quad("BrushFrame", Vector3.zero, new Vector2(bw + 0.008f, 0.046f + 0.008f), Color.white, 3005).transform;
        for (int j = 0; j < 5; j++)
        {
            AddButton(BtnLevel0 + j, x0 + bw / 2f + j * (bw + gap), yLevel, bw, j.ToString(), LevelColors[j], 0.0040f);
            if (j == 1) buttons[BtnLevel0 + j].text.color = new Color(0.13f, 0.13f, 0.13f);
        }

        // プリセットの選択（< 名前 >）
        float yPreset = top - pitch * row++;
        AddButton(BtnPresetPrev, x0 + 0.025f, yPreset, 0.05f, "<", ButtonColor, 0.0040f);
        AddButton(BtnPresetNext, x0 + w - 0.025f, yPreset, 0.05f, ">", ButtonColor, 0.0040f);
        presetLabel = Text("PresetLabel", new Vector3(xc, yPreset, 0), 0.0032f, TextAnchor.MiddleCenter);

        float y = top - pitch * row++;
        AddButton(BtnApply, xl, y, half, "この眼に適用", ButtonColor, 0.0030f);
        AddButton(BtnApplyBoth, xr, y, half, "両眼に適用", ButtonColor, 0.0030f);
        AddButton(BtnCopy, xc, top - pitch * row++, w, "この眼をもう一方へコピー", ButtonColor, 0.0032f);
        y = top - pitch * row++;
        AddButton(BtnReset, xl, y, half, "この眼をリセット", DangerColor, 0.0030f);
        AddButton(BtnResetBoth, xr, y, half, "両眼をリセット", DangerColor, 0.0030f);

        row++;   // 入力の操作と表示の設定のあいだを1段あける
        AddButton(BtnEyeMode, xc, top - pitch * row++, w, "", ButtonColor, 0.0032f);
        AddButton(BtnOutside, xc, top - pitch * row++, w, "", ButtonColor, 0.0032f);
        AddButton(BtnGaze, xc, top - pitch * row++, w, "", ButtonColor, 0.0032f);
        AddButton(BtnFog, xc, top - pitch * row++, w, "", ButtonColor, 0.0032f);
        y = top - pitch * row++;
        AddButton(BtnGazeMark, xl, y, half, "", ButtonColor, 0.0030f);
        AddButton(BtnDebugEye, xr, y, half, "", ButtonColor, 0.0030f);
        AddButton(BtnPremul, xc, top - pitch * row++, w, "", ButtonColor, 0.0032f);
        AddButton(BtnClose, xc, top - pitch * row++, w, "閉じる", new Color(0.30f, 0.30f, 0.33f, 1f), 0.0034f);

        cursor = Quad("Cursor", Vector3.zero, new Vector2(0.012f, 0.012f), new Color(0.2f, 0.9f, 1f, 0.9f), 3030).transform;
    }

    void AddButton(int id, float cx, float cy, float width, string label, Color color, float charSize)
    {
        var b = new Button { center = new Vector2(cx, cy), size = new Vector2(width, 0.046f), color = color };
        GameObject q = Quad("B" + id, new Vector3(cx, cy, 0), b.size, color, 3010);
        b.mat = q.GetComponent<MeshRenderer>().material;
        b.text = Text("BT" + id, new Vector3(cx, cy, 0), charSize, TextAnchor.MiddleCenter);
        b.text.text = label;
        buttons[id] = b;
    }

    GameObject Quad(string name, Vector3 pos, Vector2 size, Color color, int queue)
    {
        GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        q.name = name;
        Collider col = q.GetComponent<Collider>();
        if (col != null) Destroy(col);
        q.transform.SetParent(transform, false);
        q.transform.localPosition = pos;
        q.transform.localScale = new Vector3(size.x, size.y, 1);
        var m = new Material(unlit);
        m.color = color;
        m.renderQueue = queue;
        q.GetComponent<MeshRenderer>().material = m;
        return q;
    }

    TextMesh Text(string name, Vector3 pos, float charSize, TextAnchor anchor)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = pos;
        var tm = go.AddComponent<TextMesh>();
        tm.font = font;
        tm.fontSize = 48;
        tm.characterSize = charSize;
        tm.anchor = anchor;
        tm.alignment = anchor == TextAnchor.MiddleCenter ? TextAlignment.Center : TextAlignment.Left;
        tm.color = Color.white;
        go.GetComponent<MeshRenderer>().sharedMaterial = fontMat;
        return tm;
    }

    // 頭の正面 1.2m に、利用者の方を向けて置く
    public void PlaceInFrontOf(Transform head)
    {
        Vector3 fwd = head.forward;
        fwd.y = 0;
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
        fwd.Normalize();
        transform.position = head.position + fwd * 1.2f - Vector3.Cross(Vector3.up, fwd) * 0.17f;
        transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
    }

    public void SetLabel(int id, string s)
    {
        if (buttons[id].text.text != s) buttons[id].text.text = s;
    }

    public void Refresh(float[] db, int brush, string titleText, string presetText, string infoText)
    {
        for (int i = 0; i < HfaField.N; i++)
        {
            int lv = HfaField.LevelForDb(db[i]);
            cellMats[i].color = LevelColors[lv];
            string s = Mathf.RoundToInt(db[i]).ToString();
            if (cellTexts[i].text != s) cellTexts[i].text = s;
            cellTexts[i].color = lv == 1 ? new Color(0.13f, 0.13f, 0.13f) : Color.white;
        }
        Vector2 bc = buttons[BtnLevel0 + brush].center;
        brushFrame.localPosition = new Vector3(bc.x, bc.y, 0);
        if (title.text != titleText) title.text = titleText;
        if (presetLabel.text != presetText) presetLabel.text = presetText;
        if (info.text != infoText) info.text = infoText;
    }

    // 指示線（コントローラーやマウス）とパネルの交点から、指している測定点・ボタンを求める
    public void UpdatePointer(Ray ray, bool hasRay)
    {
        Hover = -1;
        int hb = -1;
        var plane = new Plane(-transform.forward, transform.position);
        float dist = 0f;
        bool hit = hasRay && plane.Raycast(ray, out dist);
        cursor.gameObject.SetActive(hit);
        if (hit)
        {
            Vector3 local = transform.InverseTransformPoint(ray.GetPoint(dist));
            cursor.localPosition = new Vector3(local.x, local.y, 0);

            float best = PickRadius * PickRadius;
            for (int i = 0; i < HfaField.N; i++)
            {
                float dx = local.x - HfaField.Points[i, 0] * MetersPerDeg;
                float dy = local.y - HfaField.Points[i, 1] * MetersPerDeg;
                float d2 = dx * dx + dy * dy;
                if (d2 < best) { best = d2; Hover = i; }
            }
            for (int i = 0; i < ButtonCount; i++)
            {
                Button b = buttons[i];
                if (Mathf.Abs(local.x - b.center.x) <= b.size.x / 2f &&
                    Mathf.Abs(local.y - b.center.y) <= b.size.y / 2f) { hb = i; break; }
            }
        }
        highlight.gameObject.SetActive(Hover >= 0);
        if (Hover >= 0) highlight.localPosition = cells[Hover].localPosition;

        if (hb != HoverButton)
        {
            if (HoverButton >= 0) buttons[HoverButton].mat.color = buttons[HoverButton].color;
            if (hb >= 0)
            {
                Color c = buttons[hb].color;
                buttons[hb].mat.color = new Color(
                    Mathf.Min(1f, c.r * 1.3f + 0.12f), Mathf.Min(1f, c.g * 1.3f + 0.12f), Mathf.Min(1f, c.b * 1.3f + 0.12f), 1f);
            }
            HoverButton = hb;
        }
    }
}
}
