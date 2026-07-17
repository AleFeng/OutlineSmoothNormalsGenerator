#ifndef OUTLINE_SMOOTH_NORMALS_INCLUDED
#define OUTLINE_SMOOTH_NORMALS_INCLUDED

// ═══════════════════════════════════════════════════════════════════════
//  OutlineSmoothNormals.hlsl —— 平滑法线解码与描边外扩的【唯一真源】
//
//  共用方：
//    - Shader/Outline.shader                生产描边（URP）
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

// ───────────────────────────────────────────────────────────────────────
// 描边外扩：把世界空间平滑法线投影到裁剪空间 XY 平面后偏移顶点。
//
// 只接受【已变换好】的 clipPos 与 worldNormal —— 由各管线自己用
// UnityObjectToClipPos / TransformObjectToHClip 算好再传进来，
// 本函数因此与管线无关。
//
// 要点：
//   - worldNormal 必须经逆转置矩阵变换（UnityObjectToWorldNormal /
//     TransformObjectToWorldNormal），否则非均匀缩放下描边会倾斜。
//   - 在裁剪空间取方向（而非视图空间），描边才不受 FOV / 宽高比影响。
//   - 乘 clipPos.w，使描边在屏幕空间等宽、不随深度变化。
//   - dirLen 保护：法线正对 / 背对相机时 xy≈0，未保护的 normalize 会
//     产生 NaN，GPU 会直接丢弃整个三角形（表现为闪烁的空洞）。
// ───────────────────────────────────────────────────────────────────────
float4 OSN_ApplyOutlineOffset(float4 clipPos, float3 worldNormal, float width)
{
    float4 clipNormal = mul(UNITY_MATRIX_VP, float4(worldNormal, 0.0));

    float2 offsetDir = clipNormal.xy;
    float  dirLen    = length(offsetDir);
    offsetDir = (dirLen > 1e-5) ? (offsetDir / dirLen) : float2(0.0, 0.0);

    clipPos.xy += offsetDir * width * clipPos.w;
    return clipPos;
}

#endif // OUTLINE_SMOOTH_NORMALS_INCLUDED
