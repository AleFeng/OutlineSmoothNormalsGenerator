#ifndef OUTLINE_NPR_INCLUDED
#define OUTLINE_NPR_INCLUDED

// ═══════════════════════════════════════════════════════════════════════
//  OutlineNPR.hlsl —— Demo 描边 Shader 的基础 NPR 光照数学【唯一真源】
//
//  共用方：
//    - Samples~/URP/Outline.shader      FORWARD Pass（UniversalForward）
//    - Samples~/BuiltIn/Outline.shader  FORWARD Pass（ForwardBase）
//
//  描边多用于 NPR，故 Demo 的基础渲染做成最简 NPR：卡通两段式明暗 + 边缘光。
//  两个管线的 FORWARD Pass 曾各抄一份完全相同的这套数学 —— 收敛到这里，改一处
//  另一处漏改的漂移在结构上不再可能。只做演示够用的最基础效果，不追求完整卡通光照。
//
//  约束（与 OutlineSmoothNormals.hlsl 一致）：
//    - 纯数学，不 include 任何管线头文件。灯光方向 / 颜色 / 环境光等光照量
//      由各管线自己取好（GetMainLight / _WorldSpaceLightPos0 …）再传进来，
//      本文件因此与管线无关。
//    - 只用 float / half，禁止 real —— real 是 URP 专有类型，会让 Built-in
//      版本编译失败。
// ═══════════════════════════════════════════════════════════════════════

// ── 卡通两段式明暗（cel shading）──────────────────────────────────────
//  半兰伯特 dot(N,L)*0.5+0.5 在阈值处经 smoothstep 硬切出一条明暗带 ——
//  NPR 最典型的特征。返回一个在【暗部染色 shadeColor】与【亮部纯白】之间
//  过渡的调制色，乘到 albedo·灯色 上即可。softness 控制明暗交界的软硬，
//  越小越硬。
//    ndotl     ：dot(法线, 灯光方向)，未做 saturate（半兰伯特需要负半区）
//    threshold ：明暗分界所在的半兰伯特值（0..1）
float3 OSN_ToonRamp(float ndotl, float3 shadeColor, float threshold, float softness)
{
    float halfLambert = ndotl * 0.5 + 0.5;
    float ramp = smoothstep(threshold - softness, threshold + softness, halfLambert);
    return lerp(shadeColor, float3(1.0, 1.0, 1.0), ramp);
}

// ── 边缘光（rim / fresnel）────────────────────────────────────────────
//  正对相机处弱、掠射边缘处强，pow 把亮区收紧到轮廓一圈。返回强度标量，
//  由调用方乘上 rim 颜色后叠加到最终色上。
//    ndotv ：dot(法线, 视线方向)，两者都应已归一化
float OSN_RimLight(float ndotv, float power)
{
    return pow(1.0 - saturate(ndotv), power);
}

#endif // OUTLINE_NPR_INCLUDED
