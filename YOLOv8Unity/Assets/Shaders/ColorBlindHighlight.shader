Shader "Custom/ColorBlind_LMSColorWarp_Optimized"
{
    Properties
    {
        _MainTex ("Input", 2D) = "white" {}
        _Mode ("Mode 0=prot 1=deut 2=tri", Int) = 1
        _ContrastStrength ("Contrast Strength", Range(0,2)) = 2
        
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex; float4 _MainTex_ST;
            int _Mode;
            float _ContrastStrength;
            float4 _BoxData[64]; int _BoxCount;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float2 uv : TEXCOORD0; float4 pos : SV_POSITION; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            float3 SRGBToLin(float3 c) { return pow(c, 2.2); }
            float3 LinToSRGB(float3 c) { return pow(saturate(c), 1.0 / 2.2); }

            static const float3x3 RGB2OPP = float3x3(
                0.3479,  0.5981, -0.3657,
               -0.0074, -0.1130, -1.1858,
                1.1851, -1.5708,  0.3838
            );
            static const float3x3 OPP2RGB = float3x3(
                1.2256, -0.2217,  0.4826,
                0.9018, -0.3645, -0.2670,
               -0.0936, -0.8072,  0.0224
            );

            static const float3x3 RGB2LMS = float3x3(
                17.8824, 43.5161, 4.11935, 
                3.45565, 27.1554, 3.86714,
                0.0299566, 0.184309, 1.46709
            );
            static const float3x3 LMS2RGB = float3x3(
                0.0809, -0.1305, 0.1167,
                -0.0102, 0.0540, -0.1136,
                -0.0004, -0.0041, 0.6935
            );

            static const float3x3 M_PROT = float3x3(
                0, 2.02344, -2.52581,
                0, 1, 0,
                0, 0, 1
            );
            static const float3x3 M_DEUT = float3x3(
                1, 0, 0,
                0.494207, 0, 1.24827,
                0, 0, 1
            );
            static const float3x3 M_TRI = float3x3(
                1, 0, 0, 
                0, 1, 0,
                -0.395913, 0.801109, 0
            );

            float2 OppYB_RG(float3 opp) { return opp.yz; }
            
            // Opponent-plane inverse rotation: push toward the opposite hue by warp
            float2 WarpOpp(float2 yb_rg, float warp)
            {
                float theta = atan2(yb_rg.y, yb_rg.x);
                float theta_new = lerp(theta, theta + 2.3561925, warp);
                float len = length(yb_rg);
                return float2(cos(theta_new), sin(theta_new)) * len;
            }

            // Convenience: apply opponent-plane warp to an sRGB color
            float3 ColorWarp(float3 srgb, float warp)
            {
                float3 opp = mul(RGB2OPP, SRGBToLin(srgb)); // λ,YB,RG
                float lambda = opp.x;
                float2 yb_rg = opp.yz;
                yb_rg = WarpOpp(yb_rg, warp);
                float3 oppWarped = float3(lambda, yb_rg.x, yb_rg.y);
                float3 rgbLin = mul(OPP2RGB, oppWarped);
                return LinToSRGB(rgbLin);
            }


            fixed4 frag (v2f i) : SV_Target
            {
                float3 src = tex2D(_MainTex, i.uv).rgb;

                bool inside = false;
                for (int b = 0; b < _BoxCount; b++)
                {
                    float4 box = _BoxData[b];
                    if (i.uv.x>=box.x && i.uv.x<=box.x+box.z &&
                        i.uv.y>=box.y && i.uv.y<=box.y+box.w)
                    { inside = true; break; }
                }

                if (inside)
                {
                    float3 lin = SRGBToLin(src);

                    // 1) Simulate CVD color perception
                    float3 lms = mul(RGB2LMS, lin);
                    float3x3 M = (_Mode==0) ? M_PROT : (_Mode==1) ? M_DEUT : M_TRI;
                    float3 simLms = mul(M, lms);
                    float3 simLin = mul(LMS2RGB, simLms);
                    float3 simRGBlin = simLin;

                    // 2) Opponent space representation
                    float3 oppOrig = mul(RGB2OPP, lin);
                    float3 oppSim  = mul(RGB2OPP, simRGBlin);

                    float2 oOrig2 = OppYB_RG(oppOrig);
                    float2 oSim2  = OppYB_RG(oppSim);

                    float2 diff2 = oOrig2 - oSim2;
                    float dist2 = length(diff2);
                    float modeBoost = (_Mode==0) ? 1.3 : (_Mode==1) ? 1.5 : 0.8;
                    float warp = saturate(dist2 * _ContrastStrength * modeBoost); // adaptive strength

                    // warp the simulated view in the opposite direction to separate hues
                    float3 simSRGB = LinToSRGB(simRGBlin);
                    float3 compSRGB = ColorWarp(simSRGB, warp);
                    src = compSRGB;
                }

                return float4(src,1);
            }
            ENDCG
        }
    }
}
