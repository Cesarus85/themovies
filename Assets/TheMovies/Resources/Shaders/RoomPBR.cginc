// Shared parts of the photorealistic room pipeline v2 (docs/ART-BRIEF.md section 15): directional baked light
// (radiosity normal mapping, RGBM), box-projected reflections from the room's panorama, GGX environment BRDF and
// the Khronos PBR Neutral tone curve (identical to Blender's view transform, so previews match the headset).
#ifndef TM_ROOM_PBR_INCLUDED
#define TM_ROOM_PBR_INCLUDED

#include "UnityCG.cginc"

// Set by CinemaEnvironment for the current v2 room.
float4x4 _TM_ReflWorldToBox;   // world → box space, the projection box is [-1, 1]³
float4x4 _TM_ReflRoomToCube;   // box-space direction × half extents → cubemap lookup direction
float4 _TM_ReflHalfExtents;    // xyz: half size of the box (m), w: strength (0 = no reflection map)
samplerCUBE _TM_ReflCube;
float _TM_RoomLight;
sampler2D _TM_GlowTex;
float4 _TM_ScreenCenter;       // xyz = center, w = range
float _TM_GlowEnabled;
float4 _TM_FireLight[4];       // xyz = position, w = radius
float _TM_FireCount;

// Radiosity normal mapping basis in tangent space (x = U, y = V, z = normal), as baked in Blender.
static const float3 kRnm0 = float3(0.81649658, 0.0, 0.57735027);
static const float3 kRnm1 = float3(-0.40824829, 0.70710678, 0.57735027);
static const float3 kRnm2 = float3(-0.40824829, -0.70710678, 0.57735027);

// RGBM, range 8: RGB was stored sRGB-encoded (the sampler linearizes it), alpha is the linear multiplier.
float3 DecodeRGBM(float4 c)
{
    return c.rgb * c.a * 8.0;
}

// Baked light for a tangent-space normal from the three RNM atlases.
float3 RnmLight(float4 l0, float4 l1, float4 l2, float3 nTS)
{
    float3 w = float3(saturate(dot(nTS, kRnm0)), saturate(dot(nTS, kRnm1)), saturate(dot(nTS, kRnm2)));
    w *= w;
    float sum = max(w.x + w.y + w.z, 1e-4);
    return (DecodeRGBM(l0) * w.x + DecodeRGBM(l1) * w.y + DecodeRGBM(l2) * w.z) / sum;
}

// Khronos PBR Neutral tone mapping (https://github.com/KhronosGroup/ToneMapping), linear in, linear out.
float3 PbrNeutral(float3 color)
{
    const float startCompression = 0.8 - 0.04;
    const float desaturation = 0.15;
    float x = min(color.r, min(color.g, color.b));
    float offset = x < 0.08 ? x - 6.25 * x * x : 0.04;
    color -= offset;
    float peak = max(color.r, max(color.g, color.b));
    if (peak < startCompression) return color;
    const float d = 1.0 - startCompression;
    float newPeak = 1.0 - d * d / (peak + d - startCompression);
    color *= newPeak / peak;
    float g = 1.0 - 1.0 / (desaturation * (peak - newPeak) + 1.0);
    return lerp(color, newPeak.xxx, g);
}

// Split-sum environment BRDF, analytic fit for mobile (Karis 2014).
float3 EnvBrdf(float3 f0, float roughness, float nv)
{
    const float4 c0 = float4(-1.0, -0.0275, -0.572, 0.022);
    const float4 c1 = float4(1.0, 0.0425, 1.04, -0.04);
    float4 r = roughness * c0 + c1;
    float a004 = min(r.x * r.x, exp2(-9.28 * nv)) * r.x + r.y;
    float2 ab = float2(-1.04, 1.04) * a004 + r.zw;
    return f0 * ab.x + ab.y;
}

// Reflection of the room along r, seen from worldPos (box projection), blurred by roughness.
float3 RoomReflection(float3 worldPos, float3 r, float roughness)
{
    if (_TM_ReflHalfExtents.w <= 0.0) return 0;
    float3 p = mul(_TM_ReflWorldToBox, float4(worldPos, 1.0)).xyz;
    float3 d = mul((float3x3)_TM_ReflWorldToBox, r);
    d = sign(d) * max(abs(d), 1e-5);
    float3 t = max((1.0 - p) / d, (-1.0 - p) / d);
    float hit = max(min(min(t.x, t.y), t.z), 0.0);
    float3 dir = (p + d * hit) * _TM_ReflHalfExtents.xyz;
    dir = mul((float3x3)_TM_ReflRoomToCube, dir);
    float mip = roughness * (1.7 - 0.7 * roughness) * 6.0; // UNITY_SPECCUBE_LOD_STEPS, as the importer convolves
    return texCUBElod(_TM_ReflCube, float4(dir, mip)).rgb * _TM_ReflHalfExtents.w;
}

// Light of the movie screen: diffuse glow and a glossy reflection of the bright picture.
void ScreenLight(float3 worldPos, float3 n, float3 r, float roughness, out float3 diffuse, out float3 specular)
{
    diffuse = 0;
    specular = 0;
    if (_TM_GlowEnabled < 0.5) return;
    float3 glow = tex2Dlod(_TM_GlowTex, float4(0.5, 0.5, 0, 8)).rgb;
    float3 toScreen = _TM_ScreenCenter.xyz - worldPos;
    float dist = length(toScreen);
    float3 l = toScreen / max(dist, 0.001);
    float falloff = saturate(1.0 - dist / max(_TM_ScreenCenter.w, 0.01));
    falloff *= falloff;
    diffuse = glow * falloff * (saturate(dot(n, l)) * 0.7 + 0.3) * 1.6;
    // The screen is a large area light: widen the lobe by its apparent size.
    float spread = saturate(roughness * roughness + 1.2 / (dist + 1.0));
    float lobe = pow(saturate(dot(r, l)), 2.0 / max(spread * spread, 1e-3)) * (1.0 / max(spread, 0.05));
    specular = glow * lobe * falloff * 0.6;
}

// Flickering firelight around up to 4 points (markers "FireLight_<cm>").
float FlickerNoise(float t, float phase)
{
    return sin(t * 7.3 + phase) * 0.5 + sin(t * 13.1 + phase * 1.7) * 0.3 + sin(t * 23.7 + phase * 0.6) * 0.2;
}

float3 FireLight(float3 worldPos)
{
    float3 m = 1.0;
    [unroll]
    for (int f = 0; f < 4; f++)
    {
        float on = f < _TM_FireCount ? 1.0 : 0.0;
        float d = distance(worldPos, _TM_FireLight[f].xyz);
        float fall = saturate(1.0 - d / max(_TM_FireLight[f].w, 0.01));
        m *= 1.0 + on * 0.22 * FlickerNoise(_Time.y, f * 12.9) * fall * fall * float3(1.0, 0.85, 0.6);
    }
    return m;
}

#endif
