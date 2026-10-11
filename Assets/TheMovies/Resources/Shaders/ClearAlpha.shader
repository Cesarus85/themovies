// Writes transparent black over the whole view, drawn first. Mixed reality test: guarantees alpha 0 behind the
// scene even if the camera clear did not keep the background alpha (passthrough shows where alpha is 0).
Shader "TheMovies/ClearAlpha"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Background" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend Off
        ColorMask RGBA
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                // The mesh is a triangle in clip space that covers the whole view of each eye.
                o.pos = float4(v.vertex.xy, UNITY_NEAR_CLIP_VALUE, 1);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return fixed4(0, 0, 0, 0);
            }
            ENDCG
        }
    }
}
