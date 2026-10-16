// A planet seen from orbit (space lounge): day side lit by a fixed sun, city lights on the night side, a soft
// terminator and a blue atmosphere rim. The sun direction is global (_TM_SunDir, set from the room's "Sun" marker),
// so the planet can turn underneath while day and night stay where they are. Not affected by the house lights.
Shader "TheMovies/Planet"
{
    Properties
    {
        _MainTex ("Day side", 2D) = "white" {}
        _NightTex ("Night lights", 2D) = "black" {}
        _DayStrength ("Day brightness", Range(0, 2)) = 1
        _NightStrength ("Night lights", Range(0, 4)) = 1.6
        _AtmosphereColor ("Atmosphere", Color) = (0.35, 0.62, 1, 1)
        _AtmosphereStrength ("Atmosphere strength", Range(0, 3)) = 1.3
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Cull Back
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _NightTex;
            float _DayStrength;
            float _NightStrength;
            fixed4 _AtmosphereColor;
            float _AtmosphereStrength;
            float4 _TM_SunDir; // xyz: direction towards the sun (world space)

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float3 worldPos : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 n = normalize(i.worldNormal);
                float3 l = dot(_TM_SunDir.xyz, _TM_SunDir.xyz) > 1e-6 ? normalize(_TM_SunDir.xyz) : float3(0, 1, 0);
                float3 v = normalize(_WorldSpaceCameraPos - i.worldPos);
                float ndl = dot(n, l);

                float3 day = tex2D(_MainTex, i.uv).rgb * saturate(ndl * 1.1 + 0.04) * _DayStrength;
                // City lights only where it's night, fading in across the terminator.
                float night = saturate((0.08 - ndl) * 5.0);
                float3 lights = tex2D(_NightTex, i.uv).rgb * night * _NightStrength;
                // Atmosphere: bright at the limb, on the day side and a little into the night.
                float rim = pow(1.0 - saturate(dot(n, v)), 4.0);
                float3 atmosphere = _AtmosphereColor.rgb * rim * _AtmosphereStrength * saturate(ndl * 0.9 + 0.3);
                return fixed4(day + lights + atmosphere, 1);
            }
            ENDCG
        }
    }
}
