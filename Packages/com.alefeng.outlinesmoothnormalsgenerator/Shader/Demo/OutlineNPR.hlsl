#ifndef OUTLINE_NPR_INCLUDED
#define OUTLINE_NPR_INCLUDED

// ═══════════════════════════════════════════════════════════════════════
//  OutlineNPR.hlsl —— Demo 描边 Shader 的基础渲染数学【唯一真源】
//                     基础 NPR 光照（卡通明暗 + 边缘光）+ 基础色调试模式。
//
//  ★ 本文件是【Demo 专用】的，与描边功能无关，生产 Shader 不需要 include。
//    它之所以单独放在 Shader/Demo/ 而不与核心库同级：Shader/ 根目录下的
//    东西都是给用户直接用进自己 Shader 的公开接口，混进一个「只为把演示
//    场景渲染得好看点」的文件，会让人以为它也是接入描边的必需品。
//
//    这里的卡通明暗与边缘光只是最基础的演示效果，不追求完整卡通光照；
//    实际项目请用自己的主材质，只把 OUTLINE Pass 接进去即可 ——
//    见 Shader/OutlinePassURP.hlsl 或 Shader/OutlinePassBuiltIn.hlsl。
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

// ── 基础色调试模式 ────────────────────────────────────────────────────
//  把「平滑法线数据」直接当基础色显示，肉眼即可核对生成结果。返回的颜色由
//  调用方在【不经光照】的分支里直接输出 —— 一旦叠加卡通明暗，数值就被扭曲、
//  失去调试意义。mode 与 Shader 里 _BaseColorMode 的下拉一一对应（顺序与
//  OutlineShaderGUI 的 BaseColorModeOptions 完全一致 —— 下拉写入的正是这个值）：
//    0 Base Map      贴图，走正常 NPR，不在本函数处理
//    1 顶点色        color.rgb 原样
//    2 顶点色 RG     (R, G, 0)  —— 只看 RG 通道对，B 置 0
//    3 顶点色 GB     (0, G, B)  —— 只看 GB 通道对，R 置 0
//    4 顶点色 BA     (A, 0, B)  —— 只看 BA 通道对；A 无显示位、借用 R，G 置 0
//    5 切线空间      tangent.xyz，从 [-1,1] 映射到 [0,1] 才能当颜色显示
//    6..13 UV0..7    对应 UV 的 xy 作为 RG，B 恒为 0
//  顶点色的通道对与工具里八面体编码所用的 _VCChannel（RG/GB/BA）一一对应，
//  便于单看某一对通道里到底存了什么。两个管线共用同一份映射，避免各写一套而漂移。
float3 OSN_DebugBaseColor(float mode, float4 vertexColor, float4 tangent,
                          float3 uv0, float3 uv1, float3 uv2, float3 uv3,
                          float3 uv4, float3 uv5, float3 uv6, float3 uv7)
{
    if      (mode < 1.5)  return vertexColor.rgb;                            // 1 顶点色 RGB
    else if (mode < 2.5)  return float3(vertexColor.r, vertexColor.g, 0.0);  // 2 顶点色 RG → (R,G,0)
    else if (mode < 3.5)  return float3(0.0, vertexColor.g, vertexColor.b);  // 3 顶点色 GB → (0,G,B)
    else if (mode < 4.5)  return float3(vertexColor.a, 0.0, vertexColor.b);  // 4 顶点色 BA → (A,0,B)
    else if (mode < 5.5)  return tangent.xyz * 0.5 + 0.5;                    // 5 切线：[-1,1] → [0,1]
    else if (mode < 6.5)  return float3(uv0.xy, 0.0);                        // 6 UV0 → RG
    else if (mode < 7.5)  return float3(uv1.xy, 0.0);                        // 7 UV1 → RG
    else if (mode < 8.5)  return float3(uv2.xy, 0.0);                        // 8 UV2 → RG
    else if (mode < 9.5)  return float3(uv3.xy, 0.0);                        // 9 UV3 → RG
    else if (mode < 10.5) return float3(uv4.xy, 0.0);                        // 10 UV4 → RG
    else if (mode < 11.5) return float3(uv5.xy, 0.0);                        // 11 UV5 → RG
    else if (mode < 12.5) return float3(uv6.xy, 0.0);                        // 12 UV6 → RG
    else                  return float3(uv7.xy, 0.0);                        // 13 UV7 → RG
}

#endif // OUTLINE_NPR_INCLUDED
