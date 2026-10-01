// Flat dark silhouette for the distant turbines on the horizon: one color, no lighting, no shadows, no detail.
// The scene fog is mixed in only partly (_FogAmount), so the shape stays readable against the horizon instead of
// dissolving into it (full fog at ~1.3 km would leave nothing), while still reading as far away.
// Ground haze: from the ground up to _GroundFadeHeight the shape fades in from the fog color, so the tower foot melts
// into the hazy ground instead of ending in a hard line (it looked like the turbine floated in the fog).
// WindFarmBackdrop sets _GroundHeight on its own material instance.
Shader "WindFarm/Silhouette"
{
    Properties
    {
        _Color ("Color", Color) = (0.13, 0.19, 0.23, 1)
        _FogAmount ("Fog Amount", Range(0, 1)) = 0.8
        _GroundFadeHeight ("Ground Fade Height (m)", Float) = 35
        _GroundHeight ("Ground Height (m, set by script)", Float) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" "IgnoreProjector" = "True" }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            half4 _Color;
            half _FogAmount;
            float _GroundFadeHeight;
            float _GroundHeight;

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float fog : TEXCOORD0;          // 1 = no fog
                float height : TEXCOORD1;       // m above the ground
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.pos = UnityWorldToClipPos(world);
                o.height = world.y - _GroundHeight;

                o.fog = 1.0;
            #if defined(FOG_LINEAR)
                o.fog = saturate(length(_WorldSpaceCameraPos - world) * unity_FogParams.z + unity_FogParams.w);
            #endif
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                half3 color = lerp(_Color.rgb, unity_FogColor.rgb, (1.0 - i.fog) * _FogAmount);

                // Ground haze: fog color at the foot, the silhouette color from _GroundFadeHeight up (soft both ends).
                float rise = smoothstep(0.0, max(_GroundFadeHeight, 0.01), i.height);
                color = lerp(unity_FogColor.rgb, color, rise);
                return half4(color, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
