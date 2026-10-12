// Moves the shown depth map a step towards the newest one (R channel), so new depth maps fade in instead of
// making the picture jump. _Blend = share of the new map this frame.
Shader "Hidden/TheMovies/DepthBlend"
{
    Properties
    {
        _MainTex ("Newest depth", 2D) = "gray" {}
        _Previous ("Shown depth", 2D) = "gray" {}
        _Blend ("Share of the newest depth", Float) = 0.2
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _Previous;
            float _Blend;

            fixed4 frag(v2f_img i) : SV_Target
            {
                float target = tex2D(_MainTex, i.uv).r;
                float shown = tex2D(_Previous, i.uv).r;
                return fixed4(lerp(shown, target, _Blend), 0, 0, 1);
            }
            ENDCG
        }
    }
}
