// Gradient sky for the dashboard palette: zenith color fading into a horizon color, with a soft glow band just
// above the horizon. Below the horizon it stays at the horizon color, which is also the fog color, so the fogged
// ground plane melts into the sky without a visible edge. No textures (nothing to download in WebGL).
// SceneEnvironment sets the colors; edit them there, not on the material.
Shader "WindFarm/Skybox Gradient"
{
    Properties
    {
        _TopColor ("Zenith Color", Color) = (0.106, 0.204, 0.275, 1)
        _HorizonColor ("Horizon Color", Color) = (0.235, 0.353, 0.420, 1)
        _Exponent ("Gradient Exponent", Range(0.1, 4)) = 0.6
        _GlowColor ("Horizon Glow Color", Color) = (0.36, 0.47, 0.52, 1)
        _GlowWidth ("Horizon Glow Width", Range(0.005, 0.5)) = 0.08
        _GlowStrength ("Horizon Glow Strength", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            half4 _TopColor;
            half4 _HorizonColor;
            half _Exponent;
            half4 _GlowColor;
            half _GlowWidth;
            half _GlowStrength;

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 direction : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.direction = v.vertex.xyz;
                o.screenPos = ComputeScreenPos(o.pos);
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                float height = normalize(i.direction).y;

                // Sky: horizon -> zenith. Below the horizon: horizon (= fog) color.
                half3 color = lerp(_HorizonColor.rgb, _TopColor.rgb, pow(saturate(height), _Exponent));

                // Soft glow band just above the horizon (the bright haze of a dusk sky).
                half glow = exp(-max(height, 0.0) / _GlowWidth) * step(0.0, height) * _GlowStrength;
                color = lerp(color, _GlowColor.rgb, glow);

                // Dark gradients band visibly in 8 bits: add +-0.5 LSB of screen-space noise (dithering). The step is
                // one sRGB level, so in a linear project the noise is added in gamma space.
                float2 pixel = i.screenPos.xy / max(i.screenPos.w, 1e-5) * _ScreenParams.xy;
                float noise = frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715)))) - 0.5;
            #ifdef UNITY_COLORSPACE_GAMMA
                color += noise / 255.0;
            #else
                color = GammaToLinearSpace(LinearToGammaSpace(color) + noise / 255.0);
            #endif

                return half4(color, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
