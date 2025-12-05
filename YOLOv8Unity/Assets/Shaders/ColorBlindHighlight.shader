Shader "Custom/ColorBlindHighlight" //定義一個名為 “Custom/ColorBlindHighlight” 的著色器 (Shader)
{
    Properties
    {
        _MainTex   ("Input", 2D) = "white" {} // 貼圖輸入參數，名稱 _MainTex，類型 2D 貼圖（Texture2D），預設為白色。
        _Intensity ("Mix", Range(0,1)) = 0.7 // 一個浮點數參數 Range(0,1)，名字 “Mix”，用於控制混合強度，預設值為 0.7。
        _Mode      ("Mode (0=prot,1=deut,2=tri)", Int) = 0 // 整數參數 Mode，說明「0 = Protanopia, 1 = Deuteranopia, 2 = Tritanopia」，預設值為 0。
    }

    SubShader // 實際渲染的著色器階段。
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        // 因為要渲染半透明場景，需要用queue來確保渲染順序(background -> 純色物體 -> AlphaTest -> transparent -> overlay )
        // 設定這個材質屬於透明渲染隊列 (transparent)，適用於需要覆蓋／混合的場景。
        Cull Off ZWrite Off ZTest Always 
        /*
        Cull Off：關閉背面剔除（即即使物件背面也會渲染）。
        ZWrite Off：不寫入深度緩衝 (Z-buffer)，表示它不會阻擋後面物件。
        ZTest Always：深度測試總是通過，該材質會在任何情況下渲染。
        */
        Pass // 渲染通道
        {
            CGPROGRAM
            #pragma vertex vert // 指定頂點著色器函式為 vert。
            #pragma fragment frag // 指定片段 (pixel) 著色器函式為 frag。
            #include "UnityCG.cginc" // 引入 Unity 預設的 CG／HLSL 共用函式庫。

            sampler2D _MainTex; // 貼圖採樣器，對應上面 _MainTex 屬性。
            float4    _MainTex_ST; // 貼圖 UV 的縮放／偏移資訊（Unity 自動提供）。
            float _WarpStrength;
            int       _Mode; // 色盲模式參數 (0／1／2)，對應 Properties。
            float4    _BoxData[64]; // 一個 float4 陣列，最多儲存 64 個框 (bounding boxes) 的資料，每個 float4 通常包含 (x, y, width, height) 的 UV 值。
            int       _BoxCount; // 實際傳入的框數量。


            // sRGB <-> Linear
            float3 SRGBToLin(float3 c){ return pow(c, 2.2); }
            float3 LinToSRGB(float3 c){ return pow(saturate(c), 1/2.2); }

            // RGB -> λ,YB,RG (論文式3)
            static const float3x3 RGB2OPP = float3x3(
              0.3479,  0.5981, -0.3657,
             -0.0074, -0.1130, -1.1858,
              1.1851, -1.5708,  0.3838
            );
            // λ,YB,RG -> RGB (論文式4)
            static const float3x3 OPP2RGB = float3x3(
              1.2256, -0.2217,  0.4826,
              0.9018, -0.3645, -0.2670,
             -0.0936, -0.8072,  0.0224
            );

            // 若要用固定模擬角，可在此保留，暫不用：
            // float3 _SimAngles = float3(radians(150), radians(140), radians(80));

            // 對立色平面扭曲：把角度往反方向推，強度由 warp 控制
            float2 WarpOpp(float2 yb_rg, float warp)
            {
                float theta = atan2(yb_rg.y, yb_rg.x);
                float theta_new = lerp(theta, theta + 3.14159265, warp);
                float len = length(yb_rg);
                return float2(cos(theta_new), sin(theta_new)) * len;
            }

            float3 ColorWarp(float3 srgb, float warp)
            {
                float3 opp = mul(RGB2OPP, SRGBToLin(srgb)); // λ,YB,RG
                float lambda = opp.x;
                float2 yb_rg = opp.yz;

                yb_rg = WarpOpp(yb_rg, warp); // 以 warp 控制推開程度

                float3 oppWarped = float3(lambda, yb_rg.x, yb_rg.y);
                float3 rgbLin = mul(OPP2RGB, oppWarped);
                return LinToSRGB(rgbLin);
            }

            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };

            v2f vert(appdata v){
                v2f o; o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 src = tex2D(_MainTex, i.uv).rgb;

                bool inside = false;
                [loop] for (int idx = 0; idx < _BoxCount; idx++) {
                    float4 b = _BoxData[idx];
                    if (i.uv.x >= b.x && i.uv.x <= b.x + b.z &&
                        i.uv.y >= b.y && i.uv.y <= b.y + b.w) { inside = true; break; }
                }

                if (inside) {
                    src = ColorWarp(src, _WarpStrength);
                }
                return float4(src, 1);
            }
            ENDCG
        }
    }
}
