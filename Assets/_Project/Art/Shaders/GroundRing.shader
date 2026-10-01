// "Digital twin" ground marking around the turbine: a thin main ring with degree ticks (every 5 deg, longer every
// 30), faint concentric range rings, and a slow radar-like sweep, all fading out with the distance from the center.
// Drawn procedurally on a flat quad (no textures), additive, so on the dark ground it reads as projected light.
// Distances are world meters measured from the object's pivot, so the quad's scale only sets how far it can draw.
// Lines are antialiased with screen-space derivatives (fwidth): they stay at least about one pixel wide from 600 m
// away instead of breaking up. Additive blending fades to black in the fog, like light would.
Shader "WindFarm/Ground Ring"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (0.36, 0.78, 0.88, 1)
        _Intensity ("Intensity", Range(0, 2)) = 0.8
        _Radius ("Main Ring Radius (m)", Float) = 32
        _LineWidth ("Main Ring Width (m)", Float) = 0.35
        _TickLength ("Tick Length (m)", Float) = 2.5
        _RingSpacing ("Range Ring Spacing (m)", Float) = 16
        _RingStrength ("Range Ring Strength", Range(0, 1)) = 0.3
        _FadeRadius ("Fade Out Radius (m)", Float) = 90
        _SweepStrength ("Sweep Strength", Range(0, 1)) = 0.25
        _SweepPeriod ("Sweep Period (s)", Float) = 10
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off
        Offset -1, -1

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            half4 _Color;
            half _Intensity;
            float _Radius;
            float _LineWidth;
            float _TickLength;
            float _RingSpacing;
            half _RingStrength;
            float _FadeRadius;
            half _SweepStrength;
            float _SweepPeriod;

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 planar : TEXCOORD0;      // world XZ offset from the pivot (m)
                UNITY_FOG_COORDS(1)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 center = float3(unity_ObjectToWorld._m03, unity_ObjectToWorld._m13, unity_ObjectToWorld._m23);
                o.planar = world.xz - center.xz;
                o.pos = UnityObjectToClipPos(v.vertex);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            // 1 inside a band of the given half width around 0, with a one-pixel soft edge.
            float Band(float distance, float halfWidth, float pixel)
            {
                return saturate((halfWidth + pixel - abs(distance)) / max(pixel, 1e-5));
            }

            half4 frag(v2f i) : SV_Target
            {
                float r = length(i.planar);
                float pixel = max(fwidth(r), 1e-4);

                // Main ring: at least about one pixel wide, so it does not break up far away.
                float halfWidth = max(_LineWidth * 0.5, pixel * 0.5);
                float ring = Band(r - _Radius, halfWidth, pixel);

                // Degree ticks just outside the main ring: every 5 deg, every 30 deg twice as long.
                float angle = degrees(atan2(i.planar.y, i.planar.x)) + 180.0;             // 0..360
                float tickAngle = abs(frac(angle / 5.0 + 0.5) - 0.5) * 5.0;              // deg to the nearest tick
                float majorTick = step(abs(frac(angle / 30.0 + 0.5) - 0.5) * 30.0, 0.5);
                float tickLength = _TickLength * (1.0 + majorTick);
                float tickHalfWidth = max(_LineWidth * 0.4, pixel * 0.5);
                float tickAcross = radians(tickAngle) * r;                                // arc length to the tick (m)
                float inTickRange = step(_Radius, r) * step(r, _Radius + tickLength);
                float ticks = Band(tickAcross, tickHalfWidth, max(fwidth(tickAcross), 1e-4)) * inTickRange;

                // Faint range rings every _RingSpacing meters (not over the main ring).
                float ringPhase = r / _RingSpacing;
                float toRing = (frac(ringPhase + 0.5) - 0.5) * _RingSpacing;
                float rangeRings = Band(toRing, pixel * 0.5, pixel) * _RingStrength * step(1.0, ringPhase);

                // Slow sweep: a soft wedge trailing a rotating line, inside the fade radius.
                float sweepAngle = frac(_Time.y / max(_SweepPeriod, 0.1)) * 360.0;
                float behind = frac((sweepAngle - angle) / 360.0);                        // 0 at the line, grows behind it
                float sweep = exp(-behind * 14.0) * _SweepStrength * step(r, _Radius);

                // Everything fades out toward the fade radius; the center under the tower stays empty.
                float fade = 1.0 - smoothstep(_FadeRadius * 0.45, _FadeRadius, r);
                float inner = smoothstep(4.0, 10.0, r);

                float amount = (max(max(ring, ticks), rangeRings) + sweep) * fade * inner;
                half4 color = half4(_Color.rgb * (_Intensity * amount), 1.0);
                UNITY_APPLY_FOG_COLOR(i.fogCoord, color, half4(0, 0, 0, 0));   // additive: fog fades it to nothing
                return color;
            }
            ENDCG
        }
    }

    Fallback Off
}
