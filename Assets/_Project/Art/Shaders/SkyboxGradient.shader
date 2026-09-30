// Gradient sky for the dashboard palette: zenith color fading into a horizon color, with a soft glow band just
// above the horizon. Below the horizon it keeps the exact color it has at the horizon, which SceneEnvironment also
// uses as the fog color, so the fogged ground melts into the sky. Both curves start with zero slope at the horizon:
// a curve that rises steeply there (pow(y, 0.6), exp(-y)) reads as a hard line even when the colors match.
// No textures (nothing to download in WebGL). SceneEnvironment sets the values; edit them there, not on the material.
Shader "WindFarm/Skybox Gradient"
{
    Properties
    {
        _TopColor ("Zenith Color", Color) = (0.106, 0.204, 0.275, 1)
        _HorizonColor ("Horizon Color", Color) = (0.235, 0.353, 0.420, 1)
        _GradientHeight ("Gradient Height", Range(0.05, 1)) = 0.35
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
            half _GradientHeight;
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
                // Below the horizon the sky keeps its horizon value (height clamped to 0).
                float height = max(normalize(i.direction).y, 0.0);

                // Sky: horizon -> zenith, 1 - e^-(y/h)^2 (flat at the horizon, ~98 % at twice the height).
                float toZenith = height / _GradientHeight;
                half3 color = lerp(_HorizonColor.rgb, _TopColor.rgb, 1.0 - exp(-toZenith * toZenith));

                // Soft glow band on the horizon (the bright haze of a dusk sky), Gaussian: flat at the horizon too.
                float fromHorizon = height / _GlowWidth;
                color = lerp(color, _GlowColor.rgb, exp(-fromHorizon * fromHorizon) * _GlowStrength);

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
