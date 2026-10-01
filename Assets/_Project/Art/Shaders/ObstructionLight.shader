// Red aviation obstruction light (ICAO medium intensity, red, flashing) on the nacelle roof, drawn as a camera-facing
// glow (billboard) instead of relying on Bloom, which is expensive in WebGL. All four vertices of the mesh sit at the
// light's center and the corner is in the UV, so the center is the transformed vertex itself: this also holds when
// Unity batches the lights into world space (the first version read the center from the object matrix, which
// batching resets, and the lights flew off into the sky). The quad is expanded around that center: world size _Size,
// but never smaller than _MinPixels on screen, so the light stays a visible point from far away like a real one.
//
// The flash is driven by the shared shader time, so every light in the scene (the near turbine and the distant ones)
// flashes in sync, as the lights of a real wind farm do. ICAO allows 20-60 flashes per minute; the default is 40.
// The global float _WindFarmObstructionLights (set by ObstructionLights.LightsOn) switches all of them; unset = off.
// Additive. Fog fades it only partly (_FogResist): a real light cuts through haze better than an unlit surface.
Shader "WindFarm/Obstruction Light"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (1, 0.06, 0.02, 1)
        _Intensity ("Intensity", Range(0, 8)) = 3
        _Size ("Size (m)", Float) = 1.4
        _MinPixels ("Minimum Size (px)", Float) = 6
        _Period ("Flash Period (s)", Float) = 1.5
        _OnFraction ("On Fraction", Range(0.05, 1)) = 0.35
        _FogResist ("Fog Resistance", Range(0, 1)) = 0.6
    }

    SubShader
    {
        Tags { "Queue" = "Transparent+10" "RenderType" = "Transparent" "IgnoreProjector" = "True" "DisableBatching" = "True" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            half4 _Color;
            half _Intensity;
            float _Size;
            float _MinPixels;
            float _Period;
            half _OnFraction;
            half _FogResist;
            float _WindFarmObstructionLights;

            struct appdata
            {
                float4 vertex : POSITION;       // the light's center (same for all four vertices)
                float2 corner : TEXCOORD0;      // -0.5..0.5
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 offset : TEXCOORD0;      // -1..1 across the glow
                float fog : TEXCOORD1;          // 1 = no fog
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float3 center = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1.0)).xyz;
                float viewDistance = length(_WorldSpaceCameraPos - center);

                // World size of one screen pixel at that distance: 2 d tan(fov/2) / height, and P[1][1] = 1 / tan(fov/2).
                float pixel = 2.0 * viewDistance / (abs(UNITY_MATRIX_P._m11) * _ScreenParams.y);
                float size = max(_Size, _MinPixels * pixel);

                float3 right = UNITY_MATRIX_V[0].xyz;
                float3 up = UNITY_MATRIX_V[1].xyz;
                float3 world = center + (v.corner.x * right + v.corner.y * up) * size;
                o.pos = mul(UNITY_MATRIX_VP, float4(world, 1.0));
                o.offset = v.corner * 2.0;

                o.fog = 1.0;
            #if defined(FOG_LINEAR)
                o.fog = saturate(viewDistance * unity_FogParams.z + unity_FogParams.w);
            #endif
                o.fog = lerp(o.fog, 1.0, _FogResist);
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                float r = length(i.offset);
                float core = 1.0 - smoothstep(0.12, 0.28, r);
                float halo = exp(-r * r * 6.0) * 0.45;

                // LED flash: a quick rise and fall around an on phase of _OnFraction of the period.
                float phase = frac(_Time.y / max(_Period, 0.1));
                float flash = smoothstep(0.0, 0.04, phase) * (1.0 - smoothstep(_OnFraction - 0.04, _OnFraction, phase));

                float amount = (core + halo) * flash * _WindFarmObstructionLights * i.fog;
                return half4(_Color.rgb * (_Intensity * amount), 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
