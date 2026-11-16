// 360度映像（正距円筒図法）を球の内側に映し、視線の位置に合わせて視野欠損を重ねる。
// 欠損は Web版と同じく減光・黒塗りではなく「ぼかして周囲に溶かす」表現（ミップマップ＋多点サンプル）。
// どちらの眼を描いているかは、描画中のカメラ位置が頭の中心より右か左かで判定する
// （Unity の XR でも FOVE プラグインでも同じ方法で動くようにするため）。
Shader "HFA/GazeSphere"
{
    Properties
    {
        _MainTex ("Equirect", 2D) = "gray" {}
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Opaque" }
        Cull Front
        ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            sampler2D _HfaMapL;      // 左の画面に使う dB マップ（R = dB/40）
            sampler2D _HfaMapR;      // 右の画面に使う dB マップ
            float3 _HfaGazeDir;      // 視線方向（ワールド）
            float3 _HfaHeadPos;      // 頭（両眼の中点）の位置
            float3 _HfaHeadRight;
            float3 _HfaHeadUp;
            float4 _HfaBlurPx;       // レベル1〜4のぼかし幅（映像テクスチャのピクセル数）
            float _HfaFieldDeg;      // 30
            float _HfaOutside;       // 0=外周の値を延長 1=黒 2=加工なし
            float _HfaShowGaze;      // 1 で視線位置に印
            float _HfaDebugEye;      // 1 で左画面を赤、右画面を青に着色（左右判定の確認用）
            float _HfaYaw;           // 映像の水平回転（ラジアン）
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
                // ビュー行列から実際のカメラ位置を取り出す（眼ごとに異なる）
                float3 camPos = mul(UNITY_MATRIX_I_V, float4(0, 0, 0, 1)).xyz;
                o.side = dot(camPos - _HfaHeadPos, _HfaHeadRight);
                return o;
            }

            #define HFA_PI 3.14159265

            float2 dirToUv(float3 d)
            {
                return float2((atan2(d.x, d.z) + _HfaYaw) / (2.0 * HFA_PI) + 0.5,
                              asin(clamp(d.y, -1.0, 1.0)) / HFA_PI + 0.5);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float2 uv = dirToUv(d);

                // 継ぎ目（u が 1→0 に飛ぶ所）で微分が壊れないよう、2通りの u のうち変化の小さい方を使う
                float2 uvB = float2(frac(uv.x + 0.5) - 0.5, uv.y);
                float2 dxA = ddx(uv), dyA = ddy(uv);
                float2 dxB = ddx(uvB), dyB = ddy(uvB);
                bool useB = (abs(dxB.x) + abs(dyB.x)) < (abs(dxA.x) + abs(dyA.x));
                float2 gx = (useB ? dxB : dxA) * _MainTex_TexelSize.zw;
                float2 gy = (useB ? dyB : dyA) * _MainTex_TexelSize.zw;
                float baseLod = max(0.0, 0.5 * log2(max(dot(gx, gx), dot(gy, gy)) + 1e-8));

                // 視線を基準にした視野座標（度）。x は右が正、y は上が正
                float3 g = normalize(_HfaGazeDir);
                float3 r = normalize(cross(_HfaHeadUp, g));
                float3 u = cross(g, r);
                float2 m = float2(dot(d, r), dot(d, u));
                float ecc = degrees(acos(clamp(dot(d, g), -1.0, 1.0)));
                float2 f = m / max(length(m), 1e-6) * min(ecc, _HfaFieldDeg);
                float2 muv = f / (2.0 * _HfaFieldDeg) + 0.5;

                float db;
                if (i.side > 0.0) db = tex2Dlod(_HfaMapR, float4(muv, 0, 0)).r * 40.0;
                else              db = tex2Dlod(_HfaMapL, float4(muv, 0, 0)).r * 40.0;

                // dB → レベル（境界 25/15/5/0 は Web版と同じ。境界の前後をなめらかにつなぐ）
                float lv = (1.0 - smoothstep(24.0, 26.0, db))
                         + (1.0 - smoothstep(14.0, 16.0, db))
                         + (1.0 - smoothstep(4.0, 6.0, db))
                         + (1.0 - smoothstep(0.0, 1.0, db));
                float px = _HfaBlurPx.x * saturate(lv)
                         + (_HfaBlurPx.y - _HfaBlurPx.x) * saturate(lv - 1.0)
                         + (_HfaBlurPx.z - _HfaBlurPx.y) * saturate(lv - 2.0)
                         + (_HfaBlurPx.w - _HfaBlurPx.z) * saturate(lv - 3.0);
                px *= _HfaEnabled;
                if (_HfaOutside > 1.5) px *= 1.0 - smoothstep(_HfaFieldDeg * 0.9, _HfaFieldDeg, ecc);

                float lod = max(baseLod, log2(max(px, 1.0)));
                float2 off = (px > 1.0 ? 0.5 * exp2(lod) : 0.0) * _MainTex_TexelSize.xy;
                fixed4 col = tex2Dlod(_MainTex, float4(uv, 0, lod)) * 0.2;
                col += tex2Dlod(_MainTex, float4(uv + off * float2( 1.0,  0.4), 0, lod)) * 0.2;
                col += tex2Dlod(_MainTex, float4(uv + off * float2(-1.0, -0.4), 0, lod)) * 0.2;
                col += tex2Dlod(_MainTex, float4(uv + off * float2(-0.4,  1.0), 0, lod)) * 0.2;
                col += tex2Dlod(_MainTex, float4(uv + off * float2( 0.4, -1.0), 0, lod)) * 0.2;

                // 視野30度より外を黒にする場合（Web版と同じ扱い）
                if (_HfaOutside > 0.5 && _HfaOutside < 1.5)
                    col.rgb *= 1.0 - smoothstep(_HfaFieldDeg * 0.9, _HfaFieldDeg, ecc);

                if (_HfaShowGaze > 0.5 && ecc > 0.35 && ecc < 0.55) col.rgb = float3(1.0, 0.9, 0.0);
                if (_HfaDebugEye > 0.5)
                    col.rgb = lerp(col.rgb, i.side > 0.0 ? float3(0, 0, 1) : float3(1, 0, 0), 0.35);
                col.a = 1.0;
                return col;
            }
            ENDCG
        }
    }
}
