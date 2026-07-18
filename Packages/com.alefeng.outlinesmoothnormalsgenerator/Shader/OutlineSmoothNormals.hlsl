#ifndef OUTLINE_SMOOTH_NORMALS_INCLUDED
#define OUTLINE_SMOOTH_NORMALS_INCLUDED

// ═══════════════════════════════════════════════════════════════════════
//  OutlineSmoothNormals.hlsl —— 平滑法线解码与描边外扩的【唯一真源】
//
//  共用方：
//    - Samples~/URP/Outline.shader          描边 Shader（URP，OUTLINE Pass）
//    - Samples~/BuiltIn/Outline.shader      描边 Shader（Built-in，OUTLINE Pass）
//    - Editor/Shader/OutlinePreview.shader  编辑器内嵌预览
//
//  任何解码 / 外扩的改动只能改这里。此前这套数学被抄成三份并各自漂移，
//  直接导致了「预览正常、生产错误」的一系列缺陷。一份代码让漂移在结构
//  上不可能发生。
//
//  C# 侧的编码器（StorageWriter）与解码器（生成器窗口的预览叠加层）
//  无法共用 HLSL，必须与本文件手工保持一致 —— 以本文件为准。
//
//  约束：
//    - 纯数学，不 include 任何管线头文件。UNITY_MATRIX_VP 由调用方所在
//      管线的头文件（UnityCG.cginc / URP Core.hlsl）提供。
//    - 只用 float / half，禁止使用 real —— real 是 URP 专有类型，会让
//      Built-in 版本编译失败。
//    - 不使用 URP 的 Packing.hlsl，否则会把 URP 依赖拖进 Built-in 版本。
//
//  ── 存储格式 ─────────────────────────────────────────────────────────
//  平滑法线一律以【对象空间】存储，且始终是完整的三维方向，不做半球压缩：
//
//    顶点色     选定通道对 (8-bit × 2) ← 八面体编码，全球面双射
//    切线       tangent.xyz (float × 3) ← 直接存，w 恒为 1
//    TEXCOORDn  uv.xyz      (float × 3) ← 直接存
//
//  为什么不再用「存 XY + sqrt 重建 Z + 按顶点法线修正符号」：
//  该方案在硬边角点上必然失效。以立方体角 (1,1,-1) 为例，平滑法线是
//  (0.577, 0.577, -0.577)，Z 为负；重建只能得到 +0.577，需靠与顶点法线
//  点积来决定是否翻转。但该角上三个位置重合的顶点，法线分别是 (1,0,0)、
//  (0,1,0)、(0,0,-1) —— 只有第三个点积为负会翻转，前两个不会。于是同一
//  条平滑法线被解码成两个不同结果，描边恰好在它本该修复的角上裂开。
//  符号启发式无法修好，只能换格式：八面体编码是全球面双射，无需重建、
//  无符号歧义，8-bit 下误差约 1°，远低于描边外扩能察觉的程度。
// ═══════════════════════════════════════════════════════════════════════

// ── 顶点色通道对：与 C# 侧 VertexColorChannel 枚举一一对应 ─────────────
#define OSN_VC_RG 0
#define OSN_VC_GB 1
#define OSN_VC_BA 2

// ───────────────────────────────────────────────────────────────────────
//  八面体编码 —— 与 C# 侧 OutlineSmoothNormalsCodec 必须逐行一致
// ───────────────────────────────────────────────────────────────────────
float2 OSN_OctWrap(float2 v)
{
    return (1.0 - abs(v.yx)) * (v.xy >= 0.0 ? 1.0 : -1.0);
}

/// 单位向量 → [0,1]^2。
float2 OSN_OctEncode(float3 n)
{
    n /= max(1e-8, abs(n.x) + abs(n.y) + abs(n.z));
    n.xy = (n.z >= 0.0) ? n.xy : OSN_OctWrap(n.xy);
    return n.xy * 0.5 + 0.5;
}

/// [0,1]^2 → 单位向量。
float3 OSN_OctDecode(float2 f)
{
    f = f * 2.0 - 1.0;
    float3 n = float3(f.x, f.y, 1.0 - abs(f.x) - abs(f.y));
    float  t = saturate(-n.z);
    n.xy += (n.xy >= 0.0) ? -t : t;
    return normalize(n);
}

// ── 顶点色解码：从选定通道对取八面体坐标 ──────────────────────────────
float3 OSN_DecodeVertexColor(float4 col, float vcChannel)
{
    int ch = (int)round(vcChannel);
    float2 oct;
    if      (ch == OSN_VC_RG) oct = col.rg;
    else if (ch == OSN_VC_GB) oct = col.gb;
    else                      oct = col.ba;
    return OSN_OctDecode(oct);
}

// ── 切线解码：tangent.xyz 直接就是对象空间平滑法线 ─────────────────────
// 不存在「切线基」这回事：写入器存的就是对象空间方向，解码只需归一化。
// tangent.w 不参与，因此一整类符号错误从根上不存在。
float3 OSN_DecodeTangent(float4 tangentOS)
{
    return normalize(tangentOS.xyz);
}

// ── TEXCOORD 解码：uv.xyz 直接就是对象空间平滑法线 ─────────────────────
float3 OSN_DecodeTexCoord(float3 uv)
{
    return normalize(uv);
}

// ── 按存储关键字选择解码来源 ───────────────────────────────────────────
//  把「_SMOOTHNORMALSRC_* 关键字 → 用哪个解码器」这条映射收敛到一处。
//  两个描边 Shader 的 OUTLINE Pass 曾各抄一份完全相同的 #if 分支，改一处
//  另一处漏改就会「同名不同行为」—— 归一到这里，两处只剩一行调用。
//
//  关键字由调用方 Shader 用
//    #pragma shader_feature_local_vertex _SMOOTHNORMALSRC_VERTEXCOLOR ...
//  声明；未命中任何关键字（含材质未设置任何存储关键字）时走 VERTEXNORMAL
//  分支 —— 即原始顶点法线，「未使用本工具」的对照组。
//
//  预览 Shader（OutlinePreview.shader）不用此函数：它以 float uniform 做
//  运行时分支，好在面板里实时切换存储模式，与关键字方案语义不同。
float3 OSN_SelectSmoothNormalOS(float4 color, float4 tangentOS,
                                float3 uv0, float3 uv1, float3 uv2, float3 uv3,
                                float3 normalOS, float vcChannel)
{
    #if defined(_SMOOTHNORMALSRC_VERTEXCOLOR)
        return OSN_DecodeVertexColor(color, vcChannel);
    #elif defined(_SMOOTHNORMALSRC_TANGENTSPACE)
        return OSN_DecodeTangent(tangentOS);
    #elif defined(_SMOOTHNORMALSRC_TEXCOORD0)
        return OSN_DecodeTexCoord(uv0);
    #elif defined(_SMOOTHNORMALSRC_TEXCOORD1)
        return OSN_DecodeTexCoord(uv1);
    #elif defined(_SMOOTHNORMALSRC_TEXCOORD2)
        return OSN_DecodeTexCoord(uv2);
    #elif defined(_SMOOTHNORMALSRC_TEXCOORD3)
        return OSN_DecodeTexCoord(uv3);
    #else
        return normalize(normalOS);
    #endif
}

// ───────────────────────────────────────────────────────────────────────
// 描边外扩：把顶点沿平滑法线外扩，支持两种宽度模式。
//
// 只接受【已变换好】的 clipPos 与 worldNormal —— 由各管线自己用
// UnityObjectToClipPos / TransformObjectToHClip 算好再传进来，
// 本函数因此与管线无关。
//
//   mode = 0（屏幕空间）：描边在屏幕上【等宽】，不随距离变化。
//       取裁剪空间 XY 方向、乘 clipPos.w，抵消透视除法。
//   mode = 1（世界空间）：按【世界单位】偏移，近大远小（透视除法后自然收缩）。
//       因 VP 是线性变换，clipPos + width·VP·(n,0) 恰好等于在投影【之前】
//       把顶点沿世界法线移动 width 个世界单位，因此无需世界坐标即可实现。
//
// 要点：
//   - worldNormal 必须经逆转置矩阵变换（UnityObjectToWorldNormal /
//     TransformObjectToWorldNormal），否则非均匀缩放下描边会倾斜。
//   - 函数内归一化 worldNormal：世界空间模式下 width 才等于真实世界单位；
//     屏幕空间模式不受影响（其方向随后又在 2D 里归一化了一次）。
//   - 在裁剪空间取方向（而非视图空间），描边才不受 FOV / 宽高比影响。
//   - dirLen 保护（仅屏幕空间）：法线正对 / 背对相机时 xy≈0，未保护的
//     normalize 会产生 NaN，GPU 会直接丢弃整个三角形（表现为闪烁的空洞）。
// ───────────────────────────────────────────────────────────────────────
float4 OSN_ApplyOutlineOffset(float4 clipPos, float3 worldNormal, float width, float mode)
{
    float3 n = normalize(worldNormal);
    float4 clipNormal = mul(UNITY_MATRIX_VP, float4(n, 0.0));

    if (mode < 0.5)
    {
        // 屏幕空间：等宽，不随深度变化。
        float2 offsetDir = clipNormal.xy;
        float  dirLen    = length(offsetDir);
        offsetDir = (dirLen > 1e-5) ? (offsetDir / dirLen) : float2(0.0, 0.0);
        clipPos.xy += offsetDir * width * clipPos.w;
    }
    else
    {
        // 世界空间：沿世界法线移动 width 个世界单位，透视除法后近大远小。
        clipPos += clipNormal * width;
    }
    return clipPos;
}

#endif // OUTLINE_SMOOTH_NORMALS_INCLUDED
