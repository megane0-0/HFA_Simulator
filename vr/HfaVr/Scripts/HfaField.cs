// HFA 30-2 の視野データと計算（Web版 index.html からの移植）。
// UnityEngine に依存しない純粋な C# にしてあり、Web版の値と照合できる。
// 値やしきい値を変えるときは、必ず index.html 側と揃えること。
using System;

public static class HfaField
{
    public const int GridSize = 64;      // 補間グリッド
    public const float FieldDeg = 30f;   // グリッドは視野 ±30度
    public const int KNearest = 4;       // IDW の近傍点数
    public const float DbMax = 40f;
    public const int N = 76;

    // 30-2 の76点（度）。並びは index.html の STANDARD_POINTS と同じ
    public static readonly int[,] Points = {
        {3,3},{3,-3},{-3,3},{-3,-3},
        {3,9},{3,-9},{-3,9},{-3,-9},
        {9,3},{9,-3},{-9,3},{-9,-3},
        {3,15},{3,-15},{-3,15},{-3,-15},
        {9,9},{9,-9},{-9,9},{-9,-9},
        {15,3},{15,-3},{-15,3},{-15,-3},
        {3,21},{3,-21},{-3,21},{-3,-21},
        {9,15},{9,-15},{-9,15},{-9,-15},
        {15,9},{15,-9},{-15,9},{-15,-9},
        {21,3},{21,-3},{-21,3},{-21,-3},
        {3,27},{3,-27},{-3,27},{-3,-27},
        {9,21},{9,-21},{-9,21},{-9,-21},
        {15,15},{15,-15},{-15,15},{-15,-15},
        {21,9},{21,-9},{-21,9},{-21,-9},
        {27,3},{27,-3},{-27,3},{-27,-3},
        {9,27},{9,-27},{-9,27},{-9,-27},
        {15,21},{15,-21},{-15,21},{-15,-21},
        {21,15},{21,-15},{-21,15},{-21,-15},
        {27,9},{27,-9},{-27,9},{-27,-9}
    };

    // レベル選択で塗るときの代表値
    public static readonly float[] LevelDb = { 30, 20, 10, 3, 0 };

    // dB → レベル（25以上=0 / 15〜24=1 / 5〜14=2 / 1〜4=3 / 0以下=4）
    public static int LevelForDb(float db)
    {
        if (db >= 25) return 0;
        if (db >= 15) return 1;
        if (db >= 5) return 2;
        if (db > 0) return 3;
        return 4;
    }

    public static float Clamp(float db)
    {
        if (float.IsNaN(db)) return 0;
        return Math.Max(0f, Math.Min(DbMax, db));
    }

    // ---- プリセット ----
    public static readonly string[] PresetKeys = {
        "normal", "constriction", "central", "superior",
        "hemianopiaRight", "hemianopiaLeft",
        "glaucomaEarly", "glaucomaModerate", "glaucomaAdvanced"
    };
    public static readonly string[] PresetNames = {
        "正常", "求心性視野狭窄", "中心暗点", "上半盲",
        "右同名半盲", "左同名半盲",
        "緑内障 初期", "緑内障 中期", "緑内障 後期"
    };

    // 緑内障の表。右眼を患者から見た向き（左=鼻側）。行 y=+27…-27、列 x=-27…+27。-1 は測定点なし
    static readonly int[] GlCols = { -27, -21, -15, -9, -3, 3, 9, 15, 21, 27 };
    static readonly int[] GlRows = { 27, 21, 15, 9, 3, -3, -9, -15, -21, -27 };
    const int X = -1;
    static readonly int[][,] Glaucoma = {
        new int[,] { // early
            { X, X, X,26,28,28,28, X, X, X},
            { X, X,21,25,29,29,29,28, X, X},
            { X, 9,17,24,28,30,30,29,28, X},
            { 6,10,16,27,30,31,31,30,29,28},
            { 8,14,27,31,32,32,31,30,29,28},
            {28,29,30,31,32,32,31,30,29,28},
            {28,29,30,31,31,31,31,30,29,28},
            { X,28,29,30,30,30,30,29,28, X},
            { X, X,28,29,29,29,29,28, X, X},
            { X, X, X,28,28,28,28, X, X, X}
        },
        new int[,] { // moderate
            { X, X, X,22,24,26,28, X, X, X},
            { X, X, 3, 6,12,24,27,28, X, X},
            { X, 0, 0, 0, 4,10,26,26,28, X},
            { 0, 0, 2, 6,22,28,27,26,28,28},
            { 0, 3,12,28,18,30,30,30,29,28},
            {10,23,27,29,30,30,30,30,29,28},
            {16,24,27,29,29,30,30,30,29,28},
            { X,25,28,29,29,29,30,29,28, X},
            { X, X,28,29,29,29,29,28, X, X},
            { X, X, X,28,28,28,28, X, X, X}
        },
        new int[,] { // advanced
            { X, X, X, 0, 0, 0, 2, X, X, X},
            { X, X, 0, 0, 0, 0, 3, 8, X, X},
            { X, 0, 0, 0, 0, 2, 6,12,18, X},
            { 0, 0, 0, 0, 4,14,12,14,22,25},
            { 0, 0, 0, 6,14,29,26,22,26,27},
            { 0, 2, 8,20,28,30,29,28,28,28},
            { 0, 0, 2,10,18,24,28,28,28,27},
            { X, 0, 0, 0, 4,10,18,26,27, X},
            { X, X, 0, 2, 8,14,20,25, X, X},
            { X, X, X,10,16,20,23, X, X, X}
        }
    };

    // 緑内障だけ左眼で x を反転する。同名半盲など他のプリセットは眼を見ない
    static float GlaucomaValue(int stage, int x, int y, bool isOs)
    {
        int r = Array.IndexOf(GlRows, y);
        int c = Array.IndexOf(GlCols, isOs ? -x : x);
        int v = (r >= 0 && c >= 0) ? Glaucoma[stage][r, c] : X;
        return v == X ? 30 : v;
    }

    public static float PresetValue(int preset, int x, int y, bool isOs)
    {
        double r = Math.Sqrt((double)x * x + (double)y * y);
        switch (preset)
        {
            case 0: return 30;
            case 1: return r <= 9 ? 30 : (r <= 16 ? 10 : 0);
            case 2: return r <= 9 ? 0 : (r <= 16 ? 10 : 30);
            case 3: return y > 0 ? 0 : 30;
            case 4: return x > 0 ? 0 : 30;
            case 5: return x < 0 ? 0 : 30;
            case 6: return GlaucomaValue(0, x, y, isOs);
            case 7: return GlaucomaValue(1, x, y, isOs);
            case 8: return GlaucomaValue(2, x, y, isOs);
        }
        return 30;
    }

    // 配列は作り直さず中身を書き換える
    public static void ApplyPreset(int preset, float[] arr, bool isOs)
    {
        for (int i = 0; i < N; i++) arr[i] = PresetValue(preset, Points[i, 0], Points[i, 1], isOs);
    }

    // 両眼（better eye）: 各点で感度の良い方
    public static float[] BetterEye(float[] od, float[] os)
    {
        var a = new float[N];
        for (int i = 0; i < N; i++) a[i] = Math.Max(od[i], os[i]);
        return a;
    }

    // IDW 補間（power=2、近傍4点、最内4点の平均を持つ仮想中心点つき）。
    // 戻り値は GridSize×GridSize。行0が上（y=+30側）、列0が左（x=-30側）。index.html の computeGrid と同じ
    public static float[] ComputeGrid(float[] db)
    {
        var g = new float[GridSize * GridSize];
        int n = N + 1;
        var xs = new float[n]; var ys = new float[n]; var vs = new float[n];
        xs[0] = 0; ys[0] = 0; vs[0] = (db[0] + db[1] + db[2] + db[3]) / 4f;
        for (int k = 0; k < N; k++) { xs[k + 1] = Points[k, 0]; ys[k + 1] = Points[k, 1]; vs[k + 1] = db[k]; }

        var bw = new float[KNearest]; var bv = new float[KNearest];
        double step = 2.0 * FieldDeg / GridSize;
        for (int r = 0; r < GridSize; r++)
        {
            double yDeg = FieldDeg - (r + 0.5) * step;
            for (int c = 0; c < GridSize; c++)
            {
                double xDeg = (c + 0.5) * step - FieldDeg;
                for (int j = 0; j < KNearest; j++) { bw[j] = 0; bv[j] = 0; }
                int exact = -1;
                for (int k = 0; k < n; k++)
                {
                    double dx = xDeg - xs[k], dy = yDeg - ys[k];
                    double d2 = dx * dx + dy * dy;
                    if (d2 < 0.01) { exact = k; break; }
                    double w = 1.0 / d2;
                    if (w > bw[KNearest - 1])
                    {
                        int j = KNearest - 1;
                        while (j > 0 && bw[j - 1] < w) { bw[j] = bw[j - 1]; bv[j] = bv[j - 1]; j--; }
                        bw[j] = (float)w; bv[j] = vs[k];
                    }
                }
                if (exact >= 0) { g[r * GridSize + c] = vs[exact]; continue; }
                double wSum = 0, vSum = 0;
                for (int j = 0; j < KNearest; j++) { wSum += bw[j]; vSum += (double)bw[j] * bv[j]; }
                g[r * GridSize + c] = wSum > 0 ? (float)(vSum / wSum) : 25f;
            }
        }
        return g;
    }
}
