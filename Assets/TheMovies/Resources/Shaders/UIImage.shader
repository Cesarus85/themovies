// Pictures in the menu (movie posters): texture with rounded corners, tinted by _Color.
// _Size is the quad size in meters, _Radius the corner radius in meters; _MainTex_ST crops the picture to fill the quad.
Shader "TheMovies/UIImage"
{
    Properties
    {
        _MainTex ("Picture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Size ("Size (m)", Vector) = (0.1, 0.15, 0, 0)
        _Radius ("Corner radius (m)", Float) = 0.006
    }
    SubShader
    {
        Tags { "Queue" = "Transparent-7" "IgnoreProjector" = "True" "RenderType" = "Transparent" }
        Cull Off
        ZWrite Off
        // Alpha: covers what is behind (keeps it opaque over the scene, so passthrough only shows where nothing is).
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float4 _Size;
            float _Radius;

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
                float2 local : TEXCOORD1;
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
                o.local = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                fixed4 c = tex2D(_MainTex, i.uv) * _Color;
                // Signed distance to the rounded rectangle, in meters.
                float2 halfSize = _Size.xy * 0.5;
                float2 p = abs(i.local - 0.5) * _Size.xy;
                float2 q = p - (halfSize - _Radius);
                float d = length(max(q, 0)) + min(max(q.x, q.y), 0) - _Radius;
                float aa = max(fwidth(d), 1e-5);
                c.a *= saturate(0.5 - d / aa);
                return c;
            }
            ENDCG
        }
    }
}
