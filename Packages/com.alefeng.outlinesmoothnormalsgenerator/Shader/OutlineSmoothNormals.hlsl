#ifndef OUTLINE_SMOOTH_NORMALS_INCLUDED
#define OUTLINE_SMOOTH_NORMALS_INCLUDED

// ═══════════════════════════════════════════════════════════════════════
//  OutlineSmoothNormals.hlsl —— 平滑法线解码与描边外扩的【唯一真源】
//
//  ★ 本文件是【面向用户的公开接口】，可以直接 include 进你自己的生产 Shader：
//
//      #include "Packages/com.alefeng.outlinesmoothnormalsgenerator/Shader/OutlineSmoothNormals.hlsl"
//
//    多数情况下你不必直接用这里的函数 —— 用 Shader/OutlinePassURP.hlsl 或
//    Shader/OutlinePassBuiltIn.hlsl 这两个现成的 Pass 模板更省事，它们内部
//    就调这里。完整接入步骤见 README「在游戏中使用描边」。
//
//    需要完全掌控顶点着色器时，最少只需两个函数：
//      OSN_GetSmoothNormalOS(...)   解码 + 空间还原，拿到对象空间平滑法线
//      OSN_ApplyOutlineOffset(...)  沿该方向外扩，得到最终裁剪坐标
//
//  包内共用方：
//    - Shader/OutlinePassCommon.hlsl        描边 Pass 模板（URP / Built-in 共用主体）
//    - Editor/Shader/OutlinePreview.shader  编辑器内嵌预览
//
//  任何解码 / 外扩的改动只能改这里。此前这套数学被抄成三份并各自漂移，
//  直接导致了「预览正常、生产错误」的一系列缺陷。一份代码让漂移在结构
//  上不可能发生 —— 用户 Shader 走 include 而非复制，同样是为了这一点：
//  库升级时你的 Shader 跟着一起对。
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
//  平滑法线始终是完整的三维方向，不做半球压缩：
//
//    顶点色     选定通道对 (8-bit × 2) ← 八面体编码，全球面双射
//    切线       tangent.xyz (float × 3) ← 直接存，w 恒为 1
//    TEXCOORDn  uv.xy       (float × 2) ← 八面体编码，全球面双射
//
//  ⚠ TEXCOORD 的格式在 1.7.0 变过一次：1.6.x 及更早存的是三分量 uv.xyz 原始方向。
//    两种格式在 GPU 侧【无从分辨】—— 顶点装配会把缺失分量补 0，两分量数据
//    与「z 恰好为 0」的三分量数据 xyz 逐位相同，任何判据都存在真实反例。
//    因此没有任何自动兼容的余地：旧版烘的 TEXCOORD 数据必须重新烘焙。
//    编辑器侧尚可用顶点属性的分量数（3 = 旧格式）给出提示，但那只是强信号
//    而非判定 —— 网格合并会把维度统一取最大，别的把三分量方向写进 UV 的
//    工具也会命中。
//
//  ── 存储空间 ─────────────────────────────────────────────────────────
//  与「存进哪个通道」正交的另一个维度：方向本身写在哪个空间里。
//
//    对象空间   写绑定姿势下的对象空间方向，解码出来直接用。
//    切线空间   写相对该顶点自身 TBN 的坐标，解码时用【蒙皮后】的法线与
//               切线重建 TBN，再转回对象空间。
//
//  为什么需要切线空间：顶点色与 TEXCOORD 不参与蒙皮，原样传给顶点着色器。
//  于是 SkinnedMeshRenderer 上存对象空间方向会「顶点跟着骨骼走、外扩方向
//  却停在绑定姿势」，关节一弯描边就撕开。而切线空间坐标是蒙皮不变量：
//  N 与 T 都被 Unity 蒙皮，S = a·T + b·B + c·N 两侧同乘同一个旋转，(a,b,c)
//  恒定不变 —— 与法线贴图能在骨骼动画上正常工作是同一个道理。
//
//  切线存储模式（mode 1）不适用切线空间：那会覆盖掉重建基所必需的切线本身，
//  自噬。所幸它也不需要 —— Unity 会把 tangent.xyz 当方向一起蒙皮，存进去的
//  对象空间方向天然跟随动画。顶点法线对照（mode 6）同理不参与转换。
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

// ── 存储空间：与 C# 侧 NormalSpace 枚举一一对应 ────────────────────────
#define OSN_SPACE_OBJECT  0
#define OSN_SPACE_TANGENT 1

// ───────────────────────────────────────────────────────────────────────
//  描边材质属性声明
// ───────────────────────────────────────────────────────────────────────
//  展开为描边所需的全部 6 个 uniform。**请把它粘进你自己的那个
//  UnityPerMaterial 里**，而不是让本库另开一个：
//
//    CBUFFER_START(UnityPerMaterial)
//        float4 _BaseColor;          // ← 你自己的属性
//        ...
//        OSN_OUTLINE_MATERIAL_FIELDS // ← 描边的属性
//    CBUFFER_END
//
//  为什么不由本库自己开 CBUFFER：SRP Batcher 要求同一 Shader 各 Pass 的
//  UnityPerMaterial 布局【完全一致】。若本库另开一个，它与你主 Pass 的那个
//  就是两份不同布局，batcher 会静默失效 —— 不报错、只是掉性能，最难查。
//  交给你拼进唯一的那个 CBUFFER，是唯一正确的做法。
//
//  Built-in 管线没有 CBUFFER 概念，直接把这个宏放在 Pass 里当普通 uniform
//  声明即可，同样可用。
//
//  这 6 个属性对应的 ShaderLab Properties 声明见 README「在游戏中使用描边」，
//  ShaderLab 不支持宏，那一段只能复制。
#define OSN_OUTLINE_MATERIAL_FIELDS \
    float4 _OutlineColor;           \
    float  _OutlineWidth;           \
    float  _OutlineWidthMode;       \
    float  _SmoothNormalSrc;        \
    float  _VCChannel;              \
    float  _SmoothNormalSpace;

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

// ── TEXCOORD 解码：uv.xy 是八面体坐标，与顶点色同一套编码 ───────────────
// 形参保持 float3 而不收窄成 float2，是【刻意】的：它是所有手写顶点着色器
// 的入口（经 OSN_SelectSmoothNormalOS / OSN_GetSmoothNormalOS 传入），收窄
// 会强迫连 TEXCOORD 存储都没用的人（例如用顶点色、但照样把 8 个 uv 传进来）
// 做一遍纯机械的改动；而对真正会受影响的那两类人 —— 用 Pass 模板的、以及
// 照 README「极简写法」自己写 normalize(v.uv1.xyz) 的 —— 收窄形参根本产生
// 不了编译错误，一点忙都帮不上。多余的 z 分量由编译器直接优化掉。
float3 OSN_DecodeTexCoord(float3 uv)
{
    return OSN_OctDecode(uv.xy);
}

// ── 按存储模式选择解码来源（运行时分支）─────────────────────────────────
//  把「存储模式 → 用哪个解码器」这条映射收敛到一处。两个描边 Shader 的
//  OUTLINE Pass 曾各抄一份完全相同的分支，改一处另一处漏改就会「同名不同行为」
//  —— 归一到这里，两处只剩一行调用。
//
//  mode 与材质 _SmoothNormalSrc 的下拉一一对应（值保持向后兼容，末尾追加）：
//    0 顶点色（默认）  1 切线      2..5 TEXCOORD0..3
//    6 顶点法线（对照，「未使用本工具」的效果）
//    7..10 TEXCOORD4..7
//
//  用运行时 float 分支而非 shader 关键字：存储通道多达 8 个（TEXCOORD0..7），
//  连同其他模式共 11 项，早已超过 Unity [KeywordEnum] 的上限；且这样与预览
//  Shader（OutlinePreview.shader）的选择方式统一。逐顶点一次整型比较，对描边
//  的开销可忽略。解码本身仍走同一份 OSN_Decode* —— 单一真源不受影响。
float3 OSN_SelectSmoothNormalOS(float mode, float4 color, float4 tangentOS,
                                float3 uv0, float3 uv1, float3 uv2, float3 uv3,
                                float3 uv4, float3 uv5, float3 uv6, float3 uv7,
                                float3 normalOS, float vcChannel)
{
    int m = (int)round(mode);
    if      (m == 1)  return OSN_DecodeTangent(tangentOS);
    else if (m == 2)  return OSN_DecodeTexCoord(uv0);
    else if (m == 3)  return OSN_DecodeTexCoord(uv1);
    else if (m == 4)  return OSN_DecodeTexCoord(uv2);
    else if (m == 5)  return OSN_DecodeTexCoord(uv3);
    else if (m == 6)  return normalize(normalOS);          // 顶点法线（对照）
    else if (m == 7)  return OSN_DecodeTexCoord(uv4);
    else if (m == 8)  return OSN_DecodeTexCoord(uv5);
    else if (m == 9)  return OSN_DecodeTexCoord(uv6);
    else if (m == 10) return OSN_DecodeTexCoord(uv7);
    else              return OSN_DecodeVertexColor(color, vcChannel);  // 0 = 顶点色（默认）
}

// ───────────────────────────────────────────────────────────────────────
//  切线空间 → 对象空间
// ───────────────────────────────────────────────────────────────────────
//  normalOS / tangentOS 进到顶点着色器时【已经是蒙皮后的值】—— GPU 与 CPU
//  两条蒙皮路径都会把 POSITION / NORMAL / TANGENT.xyz 变换好再喂给着色器，
//  tangent.w（手性）原样保留。因此这里重建出的就是当前姿势下的正确方向，
//  这正是切线空间存储存在的全部理由。
//
//  必须重新正交化：骨骼矩阵按权重混合后（尤其骨骼带非均匀缩放时），蒙皮
//  出来的 N 与 T 既不再严格正交、也不再是单位长度。不做 Gram-Schmidt 会
//  让重建基发生剪切，描边方向随之偏斜。
//
//  退化保护：UV 退化处（三个 UV 共线或重合）切线为零向量、或与法线共线，
//  正交化后长度趋零，基是奇异的。此时退回顶点法线 —— 描边在该顶点上退化
//  成「未使用本工具」的效果，虽不理想，但远好于 normalize(0) 产生 NaN 让
//  GPU 丢弃整个三角形。这类顶点由 OutlineMeshValidator 在烘焙前报出。
float3 OSN_TangentToObject(float3 smoothNormalTS, float3 normalOS, float4 tangentOS)
{
    float3 n = normalize(normalOS);
    float3 t = tangentOS.xyz - n * dot(n, tangentOS.xyz);   // Gram-Schmidt
    float  l = length(t);
    if (l < 1e-5) return n;                                 // 切线退化，无可用基
    t /= l;
    float3 b = cross(n, t) * tangentOS.w;                   // w 是手性，不可丢
    return normalize(smoothNormalTS.x * t + smoothNormalTS.y * b + smoothNormalTS.z * n);
}

// ── 按存储空间把解码结果归一到对象空间 ─────────────────────────────────
//  刻意做成独立的一步、而不是并进 OSN_SelectSmoothNormalOS：解码（通道 →
//  向量）与空间还原（向量 → 对象空间）是两件正交的事，合并会让那个本已
//  11 路分支的函数再乘以 2。
//
//  space 与材质 _SmoothNormalSpace 一一对应：0 对象空间 / 1 切线空间。
//  mode 传 _SmoothNormalSrc —— 切线存储（1）与顶点法线对照（6）恒为对象
//  空间，即使材质错选了切线空间也不会被误转换（前者会自噬掉重建基，后者
//  压根没经过编码）。
float3 OSN_ResolveSmoothNormalSpace(float3 smoothNormal, float space, float mode,
                                    float3 normalOS, float4 tangentOS)
{
    int m = (int)round(mode);
    if (m == 1 || m == 6) return smoothNormal;              // 该模式恒为对象空间
    if (space < 0.5)      return smoothNormal;              // OSN_SPACE_OBJECT
    return OSN_TangentToObject(smoothNormal, normalOS, tangentOS);
}

// ── 一步取到对象空间平滑法线（解码 + 空间还原）────────────────────────
//  上面两步的合并调用。**手写顶点着色器时请优先用这个**。
//
//  分成两个函数是为了让「解码」与「空间还原」各自可测、可复用；但对调用方
//  而言它们必须成对出现，而漏掉后一步是本插件被验证过的头号坑：不产生任何
//  编译错误或运行时报错，只是描边整体偏斜，极难联想到病因。把成对调用收进
//  一个函数，这个坑在结构上就不存在了。
//
//  参数顺序与材质属性一一对应：mode = _SmoothNormalSrc，space = _SmoothNormalSpace，
//  vcChannel = _VCChannel。
float3 OSN_GetSmoothNormalOS(float mode, float space, float4 color, float4 tangentOS,
                             float3 uv0, float3 uv1, float3 uv2, float3 uv3,
                             float3 uv4, float3 uv5, float3 uv6, float3 uv7,
                             float3 normalOS, float vcChannel)
{
    float3 n = OSN_SelectSmoothNormalOS(mode, color, tangentOS,
                                        uv0, uv1, uv2, uv3, uv4, uv5, uv6, uv7,
                                        normalOS, vcChannel);
    return OSN_ResolveSmoothNormalSpace(n, space, mode, normalOS, tangentOS);
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
