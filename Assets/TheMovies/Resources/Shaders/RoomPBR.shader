// Photorealistic room surfaces (pipeline v2, docs/ART-BRIEF.md section 15): base color, normal map and
// AO/roughness/metal from Blender, directional baked light (three RNM atlases on UV2), box-projected reflections of
// the room, the screen's glow, and the Khronos PBR Neutral tone curve. No real-time lights.
// Built-in pipeline, Single Pass Instanced / Multiview.
Shader "TheMovies/RoomPBR"
{
    Properties
    {
        _MainTex ("Base color", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        [Normal] _NormalTex ("Normal map (OpenGL)", 2D) = "bump" {}
        _ORMTex ("AO / roughness / metal", 2D) = "white" {}
        _Roughness ("Roughness (without ORM map)", Range(0, 1)) = 0.7
        _Metallic ("Metal (without ORM map)", Range(0, 1)) = 0
        _RNM0 ("Baked light RNM 0 (UV2)", 2D) = "black" {}
        _RNM1 ("Baked light RNM 1 (UV2)", 2D) = "black" {}
        _RNM2 ("Baked light RNM 2 (UV2)", 2D) = "black" {}
        _BakedDim ("Light left with house lights off", Range(0, 1)) = 0.04
        _Exposure ("Exposure", Float) = 1
        _Sheen ("Velvet sheen", Range(0, 2)) = 0
        _Emission ("Emission (HDR)", Float) = 0
        _RoomLightAffect ("Emission dims with house lights", Range(0, 1)) = 0
        _ScreenGlow ("Screen glow", Range(0, 3)) = 1
        _Flicker ("Flicker of own glow (0..1)", Range(0, 1)) = 0
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
            #pragma multi_compile_local __ _TM_NORMALMAP
            #pragma multi_compile_local __ _TM_ORM
            #include "RoomPBR.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            sampler2D _NormalTex;
            sampler2D _ORMTex;
            float _Roughness;
            float _Metallic;
            sampler2D _RNM0;
            sampler2D _RNM1;
            sampler2D _RNM2;
            float _BakedDim;
            float _Exposure;
            float _Sheen;
            float _Emission;
            float _RoomLightAffect;
            float _ScreenGlow;
            float _Flicker;
            float _Blink;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 uv : TEXCOORD0; // xy: UV0, zw: UV2 (light)
                float3 worldPos : TEXCOORD1;
                float3 worldNormal : TEXCOORD2;
                float4 worldTangent : TEXCOORD3; // w: bitangent sign
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = float4(TRANSFORM_TEX(v.uv, _MainTex), v.uv2);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldTangent = float4(UnityObjectToWorldDir(v.tangent.xyz), v.tangent.w * unity_WorldTransformParams.w);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 albedo = tex2D(_MainTex, i.uv.xy).rgb * _Color.rgb;
#if _TM_NORMALMAP
                float3 nTS = UnpackNormal(tex2D(_NormalTex, i.uv.xy));
#else
                float3 nTS = float3(0, 0, 1);
#endif
#if _TM_ORM
                float3 orm = tex2D(_ORMTex, i.uv.xy).rgb;
#else
                float3 orm = float3(1, _Roughness, _Metallic);
#endif
                float ao = orm.r;
                float roughness = clamp(orm.g, 0.03, 1.0);
                float metal = orm.b;

                float3 v = normalize(_WorldSpaceCameraPos - i.worldPos);
                float3 ng = normalize(i.worldNormal);
                float3 t = normalize(i.worldTangent.xyz);
                // Double-sided surfaces (curtains, thin panels): shade the side facing the viewer.
                float side = dot(ng, v) < 0 ? -1.0 : 1.0;
                ng *= side;
                float3 b = cross(ng, t) * i.worldTangent.w * side;
                float3 n = normalize(t * nTS.x + b * nTS.y + ng * nTS.z);
                float nv = saturate(dot(n, v)) + 1e-4;
                float3 r = reflect(-v, n);

                float room = saturate(_TM_RoomLight);
                float dim = lerp(_BakedDim, 1.0, room);
                float3 light = RnmLight(tex2D(_RNM0, i.uv.zw), tex2D(_RNM1, i.uv.zw), tex2D(_RNM2, i.uv.zw), nTS);
                light *= dim * FireLight(i.worldPos);

                float3 screenDiffuse, screenSpecular;
                ScreenLight(i.worldPos, n, r, roughness, screenDiffuse, screenSpecular);

                // Diffuse: baked light and the screen's glow.
                float3 diffuseColor = albedo * (1.0 - metal);
                float3 col = diffuseColor * (light * ao + screenDiffuse * _ScreenGlow);

                // Specular: the room's reflection, darkened where the baked light says little reaches (corners, under
                // furniture) and by the material's own occlusion.
                float3 f0 = lerp(0.04, albedo, metal);
                float3 brdf = EnvBrdf(f0, roughness, nv);
                float lightLevel = dot(light, float3(0.2126, 0.7152, 0.0722));
                float specOcclusion = saturate(lightLevel * 4.0 / max(dim, 0.02)) * saturate(pow(nv + ao, exp2(-16.0 * roughness - 1.0)) - 1.0 + ao);
                col += brdf * (RoomReflection(i.worldPos, r, roughness) * dim * specOcclusion + screenSpecular * _ScreenGlow);

                // Velvet: brighter rim at grazing angles.
                col += albedo * pow(1.0 - nv, 3.0) * _Sheen * light;

                // Own glow (lamp shades, bulbs), dimmed with the house lights, optionally flickering or blinking.
                float lampLevel = lerp(0.18, 1.0, room);
                float glow = _Emission * lerp(1.0, lampLevel, _RoomLightAffect);
                float phase = dot(floor(i.worldPos * 4.0), float3(1.7, 3.1, 2.3));
                glow *= 1.0 + _Flicker * FlickerNoise(_Time.y, phase);
                if (_Blink > 0.0) glow *= frac(_Time.y / _Blink + phase * 0.07) < 0.12 ? 1.6 : 0.15;
                col += albedo * glow;

                return fixed4(PbrNeutral(col * _Exposure), 1);
            }
            ENDCG
        }
    }
}
