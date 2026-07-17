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
//  直接导致了「预览正常、生产错误」的一系列缺陷（硬编码通道、缺符号
//  修正、外扩算法不同、NaN）。一份代码让漂移在结构上不可能发生。
//
//  C# 侧解码器（OutlineSmoothNormalsGeneratorWindow.GetDecodedSmoothNormals）
//  无法共用 HLSL，必须与本文件手工保持一致 —— 以本文件为准。
//
//  约束：
//    - 纯数学，不 include 任何管线头文件。UNITY_MATRIX_VP 由调用方所在
//      管线的头文件（UnityCG.cginc / URP Core.hlsl）提供。
//    - 只用 float / half，禁止使用 real —— real 是 URP 专有类型，会让
//      Built-in 版本编译失败。
// ═══════════════════════════════════════════════════════════════════════

// ── 顶点色通道对：与 C# 侧 VertexColorChannel 枚举一一对应 ─────────────
#define OSN_VC_RG 0
#define OSN_VC_GB 1
#define OSN_VC_BA 2

// ───────────────────────────────────────────────────────────────────────
// 修正重建出的 Z 符号：XY 压缩存储时 Z 只能重建为正值，用原始顶点法线
// 做点积验证，方向相反则翻转 Z。
//
// ⚠ 这是启发式，在硬边角点上必然失效：角上多个位置重合的顶点各自法线
//   不同，同一条平滑法线会被解码成不同结果，描边恰好在它要修的角上裂开。
//   Phase 3 将以八面体编码取代「XY + 重建 Z」，届时本函数删除。
// ───────────────────────────────────────────────────────────────────────
float3 OSN_FixNormalZ(float3 smoothN, float3 vertexNormal)
{
    float3 vn = normalize(vertexNormal);
    if (dot(smoothN, vn) < 0.0)
        smoothN.z = -smoothN.z;
    return normalize(smoothN);
}

/// 从 XY 重建 Z（上半球）后修正符号。
float3 OSN_RebuildFromXY(float2 nxy, float3 vertexNormal)
{
    float nz = sqrt(max(0.0, 1.0 - nxy.x * nxy.x - nxy.y * nxy.y));
    return OSN_FixNormalZ(normalize(float3(nxy.x, nxy.y, nz)), vertexNormal);
}

// ── 顶点色解码：[0,1] → [-1,1]，按通道对取 XY ─────────────────────────
float3 OSN_DecodeVertexColor(float4 col, float vcChannel, float3 vertexNormal)
{
    int ch = (int)round(vcChannel);
    float2 nxy;
    if      (ch == OSN_VC_RG) nxy = col.rg * 2.0 - 1.0;
    else if (ch == OSN_VC_GB) nxy = col.gb * 2.0 - 1.0;
    else                      nxy = col.ba * 2.0 - 1.0;
    return OSN_RebuildFromXY(nxy, vertexNormal);
}

// ── 切线空间解码 ───────────────────────────────────────────────────────
// 写入器已把 tangent.xyz 覆盖为「切线空间平滑法线」，原始切线数据不复
// 存在，因此只能用 Gram-Schmidt 从顶点法线重建一组正交切线帧。
//
// ⚠ 此处重建的基与写入器 ConvertToTangentSpace 实际使用的基并不一致
//   （后者用的是网格原本的切线），因此本模式当前解码结果是错的。
//   Phase 3 将改为直接存对象空间 XYZ，从根本上消除「基」这个概念。
// ───────────────────────────────────────────────────────────────────────
float3 OSN_DecodeTangentSpace(float3 tsNormal, float3 vertexNormal, float tangentW)
{
    float3 N  = normalize(vertexNormal);
    float3 up = abs(N.y) < 0.999 ? float3(0, 1, 0) : float3(1, 0, 0);
    float3 T  = normalize(cross(up, N));
    float3 B  = normalize(cross(N, T)) * tangentW;
    return normalize(T * tsNormal.x + B * tsNormal.y + N * tsNormal.z);
}

// ── UV 通道解码：xy 直接就是 [-1,1] 的法线 XY ──────────────────────────
float3 OSN_DecodeTexCoord(float2 uv, float3 vertexNormal)
{
    return OSN_RebuildFromXY(uv, vertexNormal);
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
