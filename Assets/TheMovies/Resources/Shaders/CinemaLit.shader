// Lighting for the cinema hall without real-time lights: a few point lights (wall sconces) passed as
// global arrays, dimmable house lights, the colored glow of the movie screen and a velvet sheen. Room models from
// Blender bring their light baked into a texture on the second UV set instead (variant _TM_BAKED).
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
        _VertexColor ("Use vertex colors", Range(0, 1)) = 1
        // Variant _TM_BAKED: light baked in Blender, on the second UV set, stored at half (× _LightScale = 2).
        _LightTex ("Baked light (UV2)", 2D) = "white" {}
        _LightScale ("Baked light scale", Float) = 2
        _BakedDim ("Baked light left with house lights off", Range(0, 1)) = 0.04
        // Passing landscape / clouds (material name "…scroll<N>"): texture moves horizontally, u per second.
        _Scroll ("Scroll (u per second)", Float) = 0
        _ScrollV ("Scroll (v per second)", Float) = 0
        // Motion in baked rooms (material names "…flicker<N>", "…twinkle<N>", "…blink<N>"):
        _Flicker ("Flicker of own glow (0..1)", Range(0, 1)) = 0
        _Twinkle ("Twinkle of bright pixels (0..1)", Range(0, 1)) = 0
        _Blink ("Blink period in seconds (0 = off)", Float) = 0
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
            #pragma multi_compile_local __ _TM_BAKED
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
            float _VertexColor;
            sampler2D _LightTex;
            float _LightScale;
            float _BakedDim;
            float _Scroll;
            float _ScrollV;
            float _Flicker;
            float _Twinkle;
            float _Blink;
            float4 _MainTex_TexelSize;
            // Flickering firelight on the baked light around up to 4 points (room model markers "FireLight_<cm>").
            float4 _TM_FireLight[4]; // xyz = position, w = radius
            float _TM_FireCount;

            // Irregular flicker in [-1, 1]; the phase keeps neighbouring lamps out of step.
            float FlickerNoise(float t, float phase)
            {
                return sin(t * 7.3 + phase) * 0.5 + sin(t * 13.1 + phase * 1.7) * 0.3 + sin(t * 23.7 + phase * 0.6) * 0.2;
            }

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
            }

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
                float2 uv2 : TEXCOORD1;
                fixed4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float3 worldNormal : TEXCOORD2;
                float2 uv2 : TEXCOORD3;
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
                o.uv.x += frac(_Scroll * _Time.y); // 0 for everything but passing scenery
                o.uv.y += frac(_ScrollV * _Time.y);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.color = v.color;
                o.uv2 = v.uv2;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 albedo = tex2D(_MainTex, i.uv).rgb * lerp(1.0, i.color.rgb, _VertexColor) * _Color.rgb;
                float3 n = normalize(i.worldNormal);
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);
                // Double-sided surfaces: light the side facing the viewer.
                n = dot(n, viewDir) < 0 ? -n : n;

                float room = saturate(_TM_RoomLight);
                float lampLevel = lerp(0.18, 1.0, room);

#if _TM_BAKED
                // Light baked in Blender (chandeliers, sconces, ambient), dimmed with the house lights.
                float3 light = tex2D(_LightTex, i.uv2).rgb * _LightScale * lerp(_BakedDim, 1.0, room);
#else
                // House lights: soft ambient, a bit brighter from above
                float3 light = lerp(0.035, 0.30, room) * (0.75 + 0.25 * n.y) * float3(1.0, 0.95, 0.9);
#endif

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

                // Firelight: the baked light flickers warm around fireplaces and candles.
                [unroll]
                for (int f = 0; f < 4; f++)
                {
                    float on = f < _TM_FireCount ? 1.0 : 0.0;
                    float d = distance(i.worldPos, _TM_FireLight[f].xyz);
                    float fall = saturate(1.0 - d / max(_TM_FireLight[f].w, 0.01));
                    light *= 1.0 + on * 0.22 * FlickerNoise(_Time.y, f * 12.9) * fall * fall * float3(1.0, 0.85, 0.6);
                }

                float3 col = albedo * light;

                // Velvet: brighter rim at grazing angles
                float rim = pow(1.0 - saturate(dot(n, viewDir)), 3.0);
                col += albedo * rim * _Sheen * (light + 0.05);

                col += spec * _Specular;
                // Own glow, with flicker (flames, bulbs), blink (signal lights) and twinkle (stars).
                float glow = _Emission * lerp(1.0, lampLevel, _RoomLightAffect);
                float phase = dot(floor(i.worldPos * 4.0), float3(1.7, 3.1, 2.3)); // per lamp, uniform over one bulb
                glow *= 1.0 + _Flicker * FlickerNoise(_Time.y, phase);
                if (_Blink > 0.0) glow *= frac(_Time.y / _Blink + phase * 0.07) < 0.12 ? 1.6 : 0.15;
                if (_Twinkle > 0.0)
                {
                    float h = Hash(floor(i.uv * _MainTex_TexelSize.zw * 0.5));
                    float bright = smoothstep(0.35, 0.8, dot(albedo, float3(0.3333, 0.3333, 0.3333)));
                    glow *= 1.0 + _Twinkle * bright * sin(_Time.y * (1.5 + h * 3.5) + h * 40.0);
                }
                col += albedo * glow;
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
