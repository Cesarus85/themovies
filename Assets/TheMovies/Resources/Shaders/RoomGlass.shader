// Window and display glass of photorealistic rooms (material keyword "glass", docs/ART-BRIEF.md section 15): the
// room's reflection (Fresnel, box-projected), the screen's glint and lit smudges over whatever lies behind, which is
// dimmed by the share the glass reflects (at grazing angles a window mirrors the lit car more than it shows the night).
Shader "TheMovies/RoomGlass"
{
    Properties
    {
        _MainTex ("Smudges (black = clean)", 2D) = "black" {}
        _ORMTex ("AO / roughness / metal", 2D) = "white" {}
        _Roughness ("Roughness (without ORM map)", Range(0, 1)) = 0.05
        _RNM0 ("Baked light RNM 0 (UV2)", 2D) = "black" {}
        _RNM1 ("Baked light RNM 1 (UV2)", 2D) = "black" {}
        _RNM2 ("Baked light RNM 2 (UV2)", 2D) = "black" {}
        _BakedDim ("Light left with house lights off", Range(0, 1)) = 0.04
        _Exposure ("Exposure", Float) = 1
        _Reflectivity ("Reflection strength", Range(0, 2)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite Off
        Blend One OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local __ _TM_ORM
            #include "RoomPBR.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _ORMTex;
            float _Roughness;
            sampler2D _RNM0;
            sampler2D _RNM1;
            sampler2D _RNM2;
            float _BakedDim;
            float _Exposure;
            float _Reflectivity;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float3 worldNormal : TEXCOORD2;
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
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 smudge = tex2D(_MainTex, i.uv.xy).rgb;
#if _TM_ORM
                float roughness = clamp(tex2D(_ORMTex, i.uv.xy).g, 0.02, 1.0);
#else
                float roughness = _Roughness;
#endif
                float3 v = normalize(_WorldSpaceCameraPos - i.worldPos);
                float3 n = normalize(i.worldNormal);
                n = dot(n, v) < 0 ? -n : n;
                float nv = saturate(dot(n, v)) + 1e-4;
                float3 r = reflect(-v, n);

                float dim = lerp(_BakedDim, 1.0, saturate(_TM_RoomLight));
                float3 light = RnmLight(tex2D(_RNM0, i.uv.zw), tex2D(_RNM1, i.uv.zw), tex2D(_RNM2, i.uv.zw), float3(0, 0, 1)) * dim;
                float3 screenDiffuse, screenSpecular;
                ScreenLight(i.worldPos, n, r, roughness, screenDiffuse, screenSpecular);

                float3 brdf = EnvBrdf(0.04, roughness, nv);
                float reflected = saturate(brdf.x * _Reflectivity);
                float3 col = brdf * (RoomReflection(i.worldPos, r, roughness) * dim + screenSpecular) * _Reflectivity;
                col += smudge * (light + screenDiffuse) * 0.5;
                // Alpha = reflected share: the view through is dimmed by it (passthrough alpha stays 1 behind the glass).
                return fixed4(PbrNeutral(col * _Exposure), reflected);
            }
            ENDCG
        }
    }
}
