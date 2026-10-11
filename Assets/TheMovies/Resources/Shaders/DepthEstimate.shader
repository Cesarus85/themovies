// 2D→3D without a depth model: guesses depth from the picture the way 2D-to-3D TVs do.
// Lower parts are usually nearer (floor, table, people), the upper part farther (sky, ceiling), the middle of
// the picture tends to hold the subject, saturated and detailed parts are more often in front, a bright blue
// or flat top is sky. The result is blended with the previous map (_Previous) to keep it calm.
// Output: R = depth, 0 = far, 1 = near.
Shader "Hidden/TheMovies/DepthEstimate"
{
    Properties
    {
        _MainTex ("Video", 2D) = "black" {}
        _Previous ("Previous depth", 2D) = "gray" {}
        _Blend ("Share of the new estimate", Float) = 0.15
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

            float3 Sample(float2 uv) { return tex2Dlod(_MainTex, float4(uv, 0, 3)).rgb; }

            fixed4 frag(v2f_img i) : SV_Target
            {
                float2 uv = i.uv;
                float3 c = Sample(uv);
                float lum = dot(c, float3(0.299, 0.587, 0.114));
                float sat = max(c.r, max(c.g, c.b)) - min(c.r, min(c.g, c.b));
                // Local detail: difference to the neighbours (in focus = usually the subject).
                float detail = 0;
                detail += abs(lum - dot(Sample(uv + float2(0.02, 0)), float3(0.299, 0.587, 0.114)));
                detail += abs(lum - dot(Sample(uv - float2(0.02, 0)), float3(0.299, 0.587, 0.114)));
                detail += abs(lum - dot(Sample(uv + float2(0, 0.03)), float3(0.299, 0.587, 0.114)));
                detail += abs(lum - dot(Sample(uv - float2(0, 0.03)), float3(0.299, 0.587, 0.114)));
                detail = saturate(detail * 3);

                float vertical = 1 - uv.y;                                  // uv.y = 1 is the top
                float centre = 1 - saturate(length((uv - 0.5) * float2(1.3, 1.8)));
                float sky = saturate((c.b - max(c.r, c.g) * 0.9) * 4) * saturate((uv.y - 0.45) * 3) * saturate(lum * 2);

                float d = 0.5 * vertical + 0.2 * centre + 0.15 * sat + 0.15 * detail;
                d = saturate(d - sky * 0.4);
                float previous = tex2D(_Previous, uv).r;
                return fixed4(lerp(previous, d, _Blend), 0, 0, 1);
            }
            ENDCG
        }
    }
}
