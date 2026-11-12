// VR 内に浮かぶ入力パネル。76点を Web版と同じ並び・同じ色（患者から見た向き）で表示する。
// プレハブや uGUI を使わず、すべてコードで組み立てる（シーン側の準備を最小にするため）。
using UnityEngine;

public class HfaPanel : MonoBehaviour
{
    const float MetersPerDeg = 0.01f;   // 視野1度 = パネル上の1cm
    const float CellSize = 0.05f;
    const float PickRadius = 0.034f;

    static readonly Color[] LevelColors = {
        new Color32(0x2e, 0x7d, 0x32, 255), new Color32(0xf9, 0xa8, 0x25, 255),
        new Color32(0xef, 0x6c, 0x00, 255), new Color32(0xc6, 0x28, 0x28, 255),
        new Color32(0x21, 0x21, 0x21, 255)
    };

    readonly Material[] cellMats = new Material[HfaField.N];
    readonly TextMesh[] cellTexts = new TextMesh[HfaField.N];
    readonly Transform[] cells = new Transform[HfaField.N];
    TextMesh title, info;
    Transform cursor, highlight;
    Shader unlit;
    Font font;
    Material fontMat;

    public int Hover { get; private set; }   // 指している点（なければ -1）

    public void Build()
    {
        Hover = -1;
        unlit = Resources.Load<Shader>("HfaUnlit");
        font = Font.CreateDynamicFontFromOSFont(new[] { "Yu Gothic UI", "Meiryo UI", "Meiryo", "MS Gothic", "Arial" }, 48);
        fontMat = new Material(font.material);
        fontMat.renderQueue = 3020;

        Quad("Background", new Vector3(0.17f, 0.02f, 0), new Vector2(1.06f, 0.82f), new Color(0.08f, 0.09f, 0.12f, 0.92f), 3000);
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
        info = Text("Info", new Vector3(0.345f, 0.3f, 0), 0.0038f, TextAnchor.UpperLeft);

        cursor = Quad("Cursor", Vector3.zero, new Vector2(0.012f, 0.012f), new Color(0.2f, 0.9f, 1f, 0.9f), 3030).transform;
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
        tm.alignment = TextAlignment.Left;
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

    public void Refresh(float[] db, string titleText, string infoText)
    {
        for (int i = 0; i < HfaField.N; i++)
        {
            int lv = HfaField.LevelForDb(db[i]);
            cellMats[i].color = LevelColors[lv];
            cellTexts[i].text = Mathf.RoundToInt(db[i]).ToString();
            cellTexts[i].color = lv == 1 ? new Color(0.13f, 0.13f, 0.13f) : Color.white;
        }
        title.text = titleText;
        info.text = infoText;
    }

    // 指示線（視線やコントローラー）とパネルの交点から、指している点を求める
    public void UpdatePointer(Ray ray)
    {
        Hover = -1;
        var plane = new Plane(-transform.forward, transform.position);
        float dist;
        if (!plane.Raycast(ray, out dist))
        {
            cursor.gameObject.SetActive(false);
            highlight.gameObject.SetActive(false);
            return;
        }
        Vector3 local = transform.InverseTransformPoint(ray.GetPoint(dist));
        cursor.gameObject.SetActive(true);
        cursor.localPosition = new Vector3(local.x, local.y, 0);

        float best = PickRadius * PickRadius;
        for (int i = 0; i < HfaField.N; i++)
        {
            float dx = local.x - HfaField.Points[i, 0] * MetersPerDeg;
            float dy = local.y - HfaField.Points[i, 1] * MetersPerDeg;
            float d2 = dx * dx + dy * dy;
            if (d2 < best) { best = d2; Hover = i; }
        }
        highlight.gameObject.SetActive(Hover >= 0);
        if (Hover >= 0) highlight.localPosition = cells[Hover].localPosition;
    }
}
