#ifndef OUTLINE_PASS_COMMON_INCLUDED
#define OUTLINE_PASS_COMMON_INCLUDED

// ═══════════════════════════════════════════════════════════════════════
//  OutlinePassCommon.hlsl —— 描边 OUTLINE Pass 的【共用主体】
//
//  ★ 不要直接 include 本文件。请按管线选用现成的适配层：
//      Shader/OutlinePassURP.hlsl       Universal RP
//      Shader/OutlinePassBuiltIn.hlsl   Built-in RP
//    两者都只是「定义两个变换宏 → include 本文件」，实际逻辑全在这里。
//    完整接入步骤（含可复制的 Properties 与 Pass）见各适配层头部与 README。
//
//  为什么拆成「共用主体 + 极薄适配层」：
//    URP 与 Built-in 的描边顶点逻辑只差两个变换函数名，其余完全相同。
//    这套数学此前被两个管线各抄一份，改一处另一处漏改就产生「同名不同
//    行为」——本包所有共享 .hlsl 都是为消灭这类漂移而存在的，Pass 模板
//    自然不该再犯一次。
//
//  ── 包含方需要先准备好两样东西 ────────────────────────────────────────
//  1) 两个变换宏（适配层已代劳）：
//       OSN_OBJECT_TO_WORLD_NORMAL(n)  对象空间法线 → 世界空间（须走逆转置）
//       OSN_OBJECT_TO_CLIP(p)          对象空间位置 → 裁剪空间
//
//  2) 描边的 6 个材质 uniform。用 OutlineSmoothNormals.hlsl 里的
//     OSN_OUTLINE_MATERIAL_FIELDS 宏把它们拼进【你自己的】UnityPerMaterial，
//     不要让本库另开一个 CBUFFER —— 那会让 SRP Batcher 静默失效。详见该宏注释。
//
//  ── 导出 ──────────────────────────────────────────────────────────────
//    OSN_OutlineVert / OSN_OutlineFrag   直接写进 #pragma 即可
//
//  结构体与函数一律带 OSN_ 前缀：本文件是 include 进【用户 Pass 内部】的，
//  而 URP 的惯例是把顶点输入输出命名为 Attributes / Varyings —— 不加前缀
//  必然和用户自己的同名结构体撞车，且是硬编译错误。
//
//  约束（与 OutlineSmoothNormals.hlsl 一致）：
//    - 不 include 任何管线头文件。UnityCG.cginc / URP Core.hlsl 由用户在
//      Pass 里自己 include（URP 惯例如此），本文件因此与管线无关。
//    - 只用 float / half，禁止使用 real 与 fixed —— real 是 URP 专有、
//      fixed 是 Built-in 专有，用任一个都会让另一边编译失败。
// ═══════════════════════════════════════════════════════════════════════

#if !defined(OSN_OBJECT_TO_WORLD_NORMAL) || !defined(OSN_OBJECT_TO_CLIP)
    #error "不要直接 include OutlinePassCommon.hlsl，请改用 OutlinePassURP.hlsl 或 OutlinePassBuiltIn.hlsl。"
#endif

// 相对路径 include：两个文件同在 Shader/ 下，因此无论本包是以 UPM 包安装、
// 还是被整个拷进项目里，这条路径都成立。include 卫兵保证重复包含无害。
#include "OutlineSmoothNormals.hlsl"

// ── 顶点输入 ──────────────────────────────────────────────────────────
// 八个 TEXCOORD 全部声明：平滑法线的存储来源由材质上的 _SmoothNormalSrc
// 在【运行时】切换（共 11 档），编译期并不知道会用到哪一个。
//
// 若你的项目确定只用某一个通道、且在意顶点带宽，那就别用本模板 —— 照
// README「方式三」手写一个只声明所需属性的 vert，零分支、零冗余输入。
struct OSN_OutlineAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float4 tangentOS  : TANGENT;
    float4 color      : COLOR;
    float4 uv0        : TEXCOORD0;
    float4 uv1        : TEXCOORD1;
    float4 uv2        : TEXCOORD2;
    float4 uv3        : TEXCOORD3;
    float4 uv4        : TEXCOORD4;
    float4 uv5        : TEXCOORD5;
    float4 uv6        : TEXCOORD6;
    float4 uv7        : TEXCOORD7;
};

struct OSN_OutlineVaryings
{
    float4 positionCS : SV_POSITION;
};

// ── 顶点着色器 ────────────────────────────────────────────────────────
// 三步：取平滑法线（解码 + 空间还原）→ 变换到世界 / 裁剪空间 → 外扩。
//
// normalOS / tangentOS 在 SkinnedMeshRenderer 上已是【蒙皮后】的值，
// 所以切线空间存储下还原出的方向会跟随骨骼动画，关节处描边不会撕开。
OSN_OutlineVaryings OSN_OutlineVert(OSN_OutlineAttributes IN)
{
    OSN_OutlineVaryings OUT;

    float3 smoothNormalOS = OSN_GetSmoothNormalOS(
        _SmoothNormalSrc, _SmoothNormalSpace, IN.color, IN.tangentOS,
        IN.uv0.xyz, IN.uv1.xyz, IN.uv2.xyz, IN.uv3.xyz,
        IN.uv4.xyz, IN.uv5.xyz, IN.uv6.xyz, IN.uv7.xyz,
        IN.normalOS, _VCChannel);

    // 逆转置变换，非均匀缩放下描边才不会倾斜（宏由适配层指向各管线的实现）。
    float3 normalWS = OSN_OBJECT_TO_WORLD_NORMAL(smoothNormalOS);
    float4 clipPos  = OSN_OBJECT_TO_CLIP(IN.positionOS.xyz);

    OUT.positionCS = OSN_ApplyOutlineOffset(clipPos, normalWS, _OutlineWidth, _OutlineWidthMode);
    return OUT;
}

// ── 片元着色器 ────────────────────────────────────────────────────────
// 描边是纯色的，不参与光照。需要描边随贴图 / 顶点色变化时，别改这里 ——
// 复制一份自己的 frag，vert 仍可继续用 OSN_OutlineVert。
half4 OSN_OutlineFrag(OSN_OutlineVaryings IN) : SV_Target
{
    return half4(_OutlineColor);
}

#endif // OUTLINE_PASS_COMMON_INCLUDED
