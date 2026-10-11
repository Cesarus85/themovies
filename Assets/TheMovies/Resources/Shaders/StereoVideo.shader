// Shows a video texture on the cinema screen. Each eye samples its own part of the frame
// (side-by-side or top/bottom packed 3D), selected via unity_StereoEyeIndex.
// Works with Single Pass Instanced / Multiview and in the Built-in and Universal render pipelines.
// 2D→3D: with _Convert3D > 0 each eye's picture is shifted sideways by the depth in _DepthTex
// (0 = far, 1 = near; _Convergence stays on the screen plane).
Shader "TheMovies/StereoVideo"
{
    Properties
    {
        _MainTex ("Video", 2D) = "black" {}
        _LeftEyeST ("Left eye scale/offset", Vector) = (1, 1, 0, 0)
        _RightEyeST ("Right eye scale/offset", Vector) = (1, 1, 0, 0)
        _Brightness ("Brightness", Float) = 1
        _IdleColor ("Color without video", Color) = (0.08, 0.08, 0.09, 1)
        _HasVideo ("Has video", Float) = 0
        _DepthTex ("Depth (2D to 3D)", 2D) = "gray" {}
        _Convert3D ("2D to 3D shift per eye (uv)", Float) = 0
        _Convergence ("Depth on the screen plane", Float) = 0.6
        _ShowDepth ("Show the depth map", Float) = 0
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
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _LeftEyeST;
            float4 _RightEyeST;
            float _Brightness;
            fixed4 _IdleColor;
            float _HasVideo;
            sampler2D _DepthTex;
            float _Convert3D;
            float _Convergence;
            float _ShowDepth;

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
                float4 st = unity_StereoEyeIndex == 0 ? _LeftEyeST : _RightEyeST;
                float2 uv = i.uv * st.xy + st.zw;
                if (_Convert3D > 0 && _ShowDepth > 0)
                    return fixed4(tex2D(_DepthTex, uv).rrr, 1);
                if (_Convert3D > 0)
                {
                    // Near parts move right for the left eye and left for the right eye (they come out of the
                    // screen). The depth is looked up twice: at the pixel, then where that points to.
                    float side = unity_StereoEyeIndex == 0 ? -1 : 1;
                    float d = tex2D(_DepthTex, uv).r;
                    float2 shifted = uv + float2(side * _Convert3D * (d - _Convergence), 0);
                    d = tex2D(_DepthTex, shifted).r;
                    uv = clamp(uv + float2(side * _Convert3D * (d - _Convergence), 0), 0.0005, 0.9995);
                }
                fixed3 video = tex2D(_MainTex, uv).rgb * _Brightness;
                return fixed4(lerp(_IdleColor.rgb, video, saturate(_HasVideo)), 1);
            }
            ENDCG
        }
    }
}
