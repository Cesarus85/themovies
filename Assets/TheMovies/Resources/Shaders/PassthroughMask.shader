// Cuts a hole into the picture where the hands / controllers are, so the passthrough layer behind shows the real
// ones there. The eye buffer is composited as premultiplied alpha (final = rgb + passthrough × (1 − a)), so fading
// rgb and alpha together by the hole strength cross-fades to the room. The hole is full where the surface faces the
// camera and fades out towards the silhouette (soft edge, as Meta's hand guidelines recommend); _Inflate grows the
// shape along its normals (world space, so scaled primitives grow the same). Writes depth: virtual things behind a
// hand that are drawn later don't cover it.
Shader "TheMovies/PassthroughMask"
{
    Properties
    {
        _Inflate ("Grow along the normals (m)", Float) = 0
        _Feather ("Soft edge (0 = hard, 1 = whole surface)", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+50" }
        Cull Back
        ZWrite On
        ZTest LEqual
        Blend Zero OneMinusSrcAlpha
        ColorMask RGBA
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float _Inflate;
            float _Feather;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 n = UnityObjectToWorldNormal(v.normal);
                float3 p = mul(unity_ObjectToWorld, v.vertex).xyz + n * _Inflate;
                o.pos = UnityWorldToClipPos(p);
                o.worldNormal = n;
                o.worldPos = p;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float facing = abs(dot(normalize(i.worldNormal), normalize(UnityWorldSpaceViewDir(i.worldPos))));
                float hole = _Feather > 0.001 ? smoothstep(0.0, _Feather, facing) : 1.0;
                // Blend Zero OneMinusSrcAlpha: color and alpha behind are scaled by (1 − hole).
                return fixed4(0, 0, 0, hole);
            }
            ENDCG
        }
    }
}
