// Soft light around the screen in mixed reality: the colors at the edge of the picture bleed out into the
// room and fade with distance ("ambilight"). Drawn on a quad slightly behind the screen; the picture itself
// covers the middle. Alpha is written so the passthrough shows through the faded parts.
Shader "TheMovies/ScreenGlow"
{
    Properties
    {
        _Strength ("Strength", Float) = 0.6
        _Falloff ("Falloff (m)", Float) = 0.5
        _ScreenSize ("Screen size (m)", Vector) = (4, 2.25, 0, 0)
        _QuadSize ("Quad size (m)", Vector) = (7, 5, 0, 0)
        _EyeST ("Picture part (scale/offset)", Vector) = (1, 1, 0, 0)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent-20" "IgnoreProjector" = "True" "RenderType" = "Transparent" }
        Cull Off
        ZWrite Off
        // Color over the passthrough; alpha accumulates normally (the compositor blends by it).
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _TM_GlowTex;
            float _Strength;
            float _Falloff;
            float4 _ScreenSize;
            float4 _QuadSize;
            float4 _EyeST;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 p = (i.uv - 0.5) * _QuadSize.xy;                     // meters from the screen center
                float2 q = abs(p) - _ScreenSize.xy * 0.5;
                float outside = length(max(q, 0));                           // distance from the screen edge
                float2 edgeUv = saturate(p / _ScreenSize.xy + 0.5);          // nearest point of the picture
                float2 uv = edgeUv * _EyeST.xy + _EyeST.zw;
                float3 c = tex2Dlod(_TM_GlowTex, float4(uv, 0, 1.5)).rgb;
                float a = _Strength * exp(-outside / max(_Falloff, 0.01));
                // Fade out before the edge of the quad.
                float2 border = (_QuadSize.xy * 0.5 - abs(p)) / max(_Falloff, 0.01);
                a *= saturate(min(border.x, border.y));
                // Dark pictures shouldn't paint the room black: weak colors fade more.
                a *= saturate(max(c.r, max(c.g, c.b)) * 3);
                return fixed4(c * 1.4, saturate(a));
            }
            ENDCG
        }
    }
}
