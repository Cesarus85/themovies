// Prepares a video frame for the depth model (Depth Anything): sRGB values normalized with the ImageNet mean
// and standard deviation, written to a float texture that becomes the model's input tensor.
Shader "Hidden/TheMovies/DepthInput"
{
    Properties
    {
        _MainTex ("Video", 2D) = "black" {}
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

            float4 frag(v2f_img i) : SV_Target
            {
                float3 c = tex2Dlod(_MainTex, float4(i.uv, 0, 1)).rgb;
            #ifndef UNITY_COLORSPACE_GAMMA
                c = LinearToGammaSpace(c); // the model was trained on sRGB values
            #endif
                c = (c - float3(0.485, 0.456, 0.406)) / float3(0.229, 0.224, 0.225);
                return float4(c, 1);
            }
            ENDCG
        }
    }
}
