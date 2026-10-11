// Lighting for the cinema hall without real-time lights: a few point lights (wall sconces) passed as
// global arrays, dimmable house lights, the colored glow of the movie screen and a velvet sheen.
// Works in the Built-in and Universal pipelines and with Single Pass Instanced / Multiview.
Shader "TheMovies/CinemaLit"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Sheen ("Velvet sheen", Range(0, 2)) = 0
        _Emission ("Emission", Range(0, 4)) = 0
        _RoomLightAffect ("Emission dims with house lights", Range(0, 1)) = 0
        _ScreenGlow ("Screen glow", Range(0, 3)) = 1
        _Specular ("Specular", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            #define TM_MAX_LIGHTS 8

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _Sheen;
            float _Emission;
            float _RoomLightAffect;
            float _ScreenGlow;
            float _Specular;

            // Set globally by CinemaEnvironment / ScreenController
            float4 _TM_LightPos[TM_MAX_LIGHTS];    // xyz = position, w = range
            float4 _TM_LightColor[TM_MAX_LIGHTS];  // rgb = color * intensity
            float _TM_LightCount;
            float _TM_RoomLight;
            sampler2D _TM_GlowTex;
            float4 _TM_ScreenCenter;  // xyz = center, w = range
            float _TM_GlowEnabled;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float3 worldNormal : TEXCOORD2;
                fixed4 color : COLOR;
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
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 albedo = tex2D(_MainTex, i.uv).rgb * i.color.rgb * _Color.rgb;
                float3 n = normalize(i.worldNormal);
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);
                // Double-sided surfaces: light the side facing the viewer.
                n = dot(n, viewDir) < 0 ? -n : n;

                float room = saturate(_TM_RoomLight);
                float lampLevel = lerp(0.18, 1.0, room);

                // House lights: soft ambient, a bit brighter from above
                float3 light = lerp(0.035, 0.30, room) * (0.75 + 0.25 * n.y) * float3(1.0, 0.95, 0.9);

                // Wall sconces
                float3 spec = 0;
                [unroll]
                for (int k = 0; k < TM_MAX_LIGHTS; k++)
                {
                    float enabled = k < _TM_LightCount ? 1.0 : 0.0;
                    float3 toLight = _TM_LightPos[k].xyz - i.worldPos;
                    float dist = length(toLight);
                    float3 l = toLight / max(dist, 0.001);
                    float atten = saturate(1.0 - dist / max(_TM_LightPos[k].w, 0.01));
                    atten *= atten;
                    float ndl = saturate(dot(n, l)) * 0.85 + 0.15;
                    float3 c = _TM_LightColor[k].rgb * atten * lampLevel * enabled;
                    light += c * ndl;
                    float3 h = normalize(l + viewDir);
                    spec += c * pow(saturate(dot(n, h)), 32.0);
                }

                // Light from the movie screen, colored by the picture
                if (_TM_GlowEnabled > 0.5)
                {
                    float3 glow = tex2Dlod(_TM_GlowTex, float4(0.5, 0.5, 0, 8)).rgb;
                    float3 toScreen = _TM_ScreenCenter.xyz - i.worldPos;
                    float dist = length(toScreen);
                    float falloff = saturate(1.0 - dist / max(_TM_ScreenCenter.w, 0.01));
                    float facing = saturate(dot(n, toScreen / max(dist, 0.001))) * 0.7 + 0.3;
                    light += glow * falloff * falloff * facing * _ScreenGlow * 1.6;
                }

                float3 col = albedo * light;

                // Velvet: brighter rim at grazing angles
                float rim = pow(1.0 - saturate(dot(n, viewDir)), 3.0);
                col += albedo * rim * _Sheen * (light + 0.05);

                col += spec * _Specular;
                col += albedo * _Emission * lerp(1.0, lampLevel, _RoomLightAffect);
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
