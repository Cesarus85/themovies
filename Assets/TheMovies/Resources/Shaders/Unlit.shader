// Flat color with an optional fake directional light so simple shapes (avatars, seats) read as 3D.
// The cinema walls additionally pick up a glow from the screen (see ScreenGlow.cs).
// Variant _TM_TEXTURED multiplies a texture (e.g. the controller models of the headset); without it nothing is sampled.
Shader "TheMovies/Unlit"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _MainTex ("Texture (variant _TM_TEXTURED)", 2D) = "white" {}
        _Shading ("Fake light amount", Range(0, 1)) = 0
        _ScreenGlow ("Screen glow amount", Range(0, 2)) = 0
        _RoomLightAffect ("Affected by room light", Range(0, 1)) = 0
        // Markers that must stay visible behind walls and balustrades (seat choice): ZTest Always, ZWrite off.
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth test", Float) = 4
        [Enum(Off, 0, On, 1)] _ZWrite ("Depth write", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Cull Off
        ZTest [_ZTest]
        ZWrite [_ZWrite]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local __ _TM_TEXTURED
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _Shading;
            float _ScreenGlow;
            float _RoomLightAffect;
            sampler2D _MainTex;
            float4 _MainTex_ST;

            // Set globally by ScreenGlow.cs
            sampler2D _TM_GlowTex;
            float4 _TM_ScreenCenter;   // xyz = screen center, w = glow range in meters
            float _TM_RoomLight;       // 0 = dark cinema, 1 = lights on
            float _TM_GlowEnabled;

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
                float3 worldNormal : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float2 uv : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 n = normalize(i.worldNormal);
                float light = 0.55 + 0.45 * saturate(dot(n, normalize(float3(0.3, 0.8, -0.5))));
                float3 albedo = _Color.rgb;
#if _TM_TEXTURED
                albedo *= tex2D(_MainTex, i.uv).rgb;
#endif
                float3 col = albedo * lerp(1.0, light, _Shading);

                float roomLight = lerp(1.0, lerp(0.18, 1.0, saturate(_TM_RoomLight)), _RoomLightAffect);
                col *= roomLight;

                if (_ScreenGlow > 0 && _TM_GlowEnabled > 0.5)
                {
                    float3 glow = tex2Dlod(_TM_GlowTex, float4(0.5, 0.5, 0, 8)).rgb;
                    float dist = distance(i.worldPos, _TM_ScreenCenter.xyz);
                    float falloff = saturate(1.0 - dist / max(_TM_ScreenCenter.w, 0.01));
                    col += albedo * glow * falloff * falloff * _ScreenGlow;
                }
                return fixed4(col, _Color.a);
            }
            ENDCG
        }
    }
}
