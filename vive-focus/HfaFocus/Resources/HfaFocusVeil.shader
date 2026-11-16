// パススルー映像の上に、視野欠損を「霧」として重ねる（VIVE Focus Vision 用）。
//
// アプリからはパススルー映像の画素を読めないので、Web版や FOVE 版のような「ぼかし」は作れない。
// 代わりに、欠損レベルに応じた濃さの灰色をアルファつきで出力し、コントラストの低下として表す。
// アルファ 0 の画素は、下敷きのパススルー映像がそのまま見える。黒塗りはしない。
//
// 頭の位置に置いた球の内側に描く。どちらの眼の画面を描いているかは、描画中のカメラ位置が
// 頭の中心より右か左かで判定する（ステレオ描画の方式に依存しないようにするため）。
Shader "HFA/FocusVeil"
{
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Transparent" }
        Cull Front
        ZWrite Off
        ZTest Always
        Blend Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            sampler2D _HfaMapL;      // 左の画面に使う dB マップ（R = dB/40）
            sampler2D _HfaMapR;      // 右の画面に使う dB マップ
            float3 _HfaGazeDir;      // 視線方向（ワールド）
            float3 _HfaHeadPos;      // 頭（両眼の中点）の位置
            float3 _HfaHeadRight;
            float3 _HfaHeadUp;
            float4 _HfaVeil;         // レベル1〜4の霧の濃さ（アルファ）
            float _HfaFog;           // 霧の明るさ（0〜1）
            float _HfaPremul;        // 1=乗算済みアルファで出力 0=通常のアルファで出力
            float _HfaFieldDeg;      // 30
            float _HfaOutside;       // 0=外周の値を延長 1=加工しない
            float _HfaShowGaze;      // 1 で視線位置に印
            float _HfaCross;         // 1 で固視目標（＋）
            float _HfaDebugEye;      // 1 で左画面を赤、右画面を青に着色（左右判定の確認用）
            float _HfaEnabled;       // 0 で欠損表示なし

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
                float side : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                // 球は頭の位置に追従し回転しないので、オブジェクト座標がそのまま方向になる
                o.dir = v.vertex.xyz;
                // ステレオ描画では _WorldSpaceCameraPos が眼ごとの位置になる
                o.side = dot(_WorldSpaceCameraPos - _HfaHeadPos, _HfaHeadRight);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);

                // 視線を基準にした視野座標（度）。x は右が正、y は上が正
                float3 g = normalize(_HfaGazeDir);
                float3 r = normalize(cross(_HfaHeadUp, g));
                float3 u = cross(g, r);
                float2 m = float2(dot(d, r), dot(d, u));
                float ecc = degrees(acos(clamp(dot(d, g), -1.0, 1.0)));
                float2 fdeg = m / max(length(m), 1e-6) * ecc;

                // 30度より外は、同じ経線上の最外周の値を参照する
                float2 f = fdeg * min(1.0, (_HfaFieldDeg - 0.5) / max(ecc, 1e-6));
                float2 muv = f / (2.0 * _HfaFieldDeg) + 0.5;

                float db;
                if (i.side > 0.0) db = tex2Dlod(_HfaMapR, float4(muv, 0, 0)).r * 40.0;
                else              db = tex2Dlod(_HfaMapL, float4(muv, 0, 0)).r * 40.0;

                // dB → レベル（境界 25/15/5/0 は Web版と同じ。境界の前後をなめらかにつなぐ）
                float lv = (1.0 - smoothstep(24.0, 26.0, db))
                         + (1.0 - smoothstep(14.0, 16.0, db))
                         + (1.0 - smoothstep(4.0, 6.0, db))
                         + (1.0 - smoothstep(0.0, 1.0, db));
                float a = _HfaVeil.x * saturate(lv)
                        + (_HfaVeil.y - _HfaVeil.x) * saturate(lv - 1.0)
                        + (_HfaVeil.z - _HfaVeil.y) * saturate(lv - 2.0)
                        + (_HfaVeil.w - _HfaVeil.z) * saturate(lv - 3.0);
                a = saturate(a) * _HfaEnabled;
                if (_HfaOutside > 0.5) a *= 1.0 - smoothstep(_HfaFieldDeg * 0.9, _HfaFieldDeg, ecc);
                float3 c = float3(_HfaFog, _HfaFog, _HfaFog);

                // 固視目標（黒縁つきの黄色い＋）。視線追跡を使わないときに、正面を見続けてもらうための印
                if (_HfaCross > 0.5)
                {
                    float2 af = abs(fdeg);
                    float mx = max(af.x, af.y);
                    float mn = min(af.x, af.y);
                    if (mx < 1.2 && mn < 0.3) { c = float3(0.0, 0.0, 0.0); a = 1.0; }
                    if (mx < 1.05 && mn < 0.15) { c = float3(1.0, 0.85, 0.1); a = 1.0; }
                }
                if (_HfaShowGaze > 0.5 && ecc > 0.35 && ecc < 0.55) { c = float3(1.0, 0.9, 0.0); a = 1.0; }
                if (_HfaDebugEye > 0.5)
                {
                    c = lerp(c, i.side > 0.0 ? float3(0.0, 0.0, 1.0) : float3(1.0, 0.0, 0.0), 0.7);
                    a = max(a, 0.35);
                }

                // 合成側が乗算済みアルファを前提にするか、通常のアルファを前提にするかは実機で確かめる
                return _HfaPremul > 0.5 ? fixed4(c * a, a) : fixed4(c, a);
            }
            ENDCG
        }
    }
}
