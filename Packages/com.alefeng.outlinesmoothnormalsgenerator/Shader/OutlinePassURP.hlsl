#ifndef OUTLINE_PASS_URP_INCLUDED
#define OUTLINE_PASS_URP_INCLUDED

// ═══════════════════════════════════════════════════════════════════════
//  OutlinePassURP.hlsl —— 描边 OUTLINE Pass 模板（Universal RP）
//
//  把背面外扩描边接进你自己的 URP Shader，整个过程就是下面这一段。
//  实际逻辑在 OutlinePassCommon.hlsl，本文件只负责把两个变换宏指向 URP。
//
//  ── 用法 ──────────────────────────────────────────────────────────────
//  1) 在 Properties 里加上这 6 个属性（ShaderLab 不支持宏，只能复制）：
//
//        [Header(Outline)]
//        _OutlineColor   ("Outline Color", Color) = (0,0,0,1)
//        [PowerSlider(3.0)]
//        _OutlineWidth   ("Outline Width", Range(0, 0.1)) = 0.015
//        [Enum(Screen Space, 0, World Space, 1)]
//        _OutlineWidthMode ("Outline Width Mode", Float) = 0
//        _SmoothNormalSrc ("Smooth Normal Source", Float) = 0
//        [Enum(RG, 0, GB, 1, BA, 2)]
//        _VCChannel      ("Vertex Color Channel", Float) = 2
//        [Enum(Object Space, 0, Tangent Space, 1)]
//        _SmoothNormalSpace ("Smooth Normal Space", Float) = 1
//
//     _SmoothNormalSrc 有 11 档、超过 [KeywordEnum] 上限，故不写 [Enum]。
//     想要下拉菜单就把材质的 CustomEditor 设为本包的 OutlineShaderGUI：
//        CustomEditor "OutlineSmoothNormalsGenerator.OutlineShaderGUI"
//
//  2) 新增一个 Pass —— 整段复制即可：
//
//        Pass
//        {
//            Name "OUTLINE"
//            Tags { "LightMode" = "SRPDefaultUnlit" }
//
//            Cull Front          // 只画背面，露出的边缘即描边
//            ZWrite On
//            ZTest LEqual
//
//            HLSLPROGRAM
//            #pragma vertex   OSN_OutlineVert
//            #pragma fragment OSN_OutlineFrag
//
//            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
//            #include "Packages/com.alefeng.outlinesmoothnormalsgenerator/Shader/OutlineSmoothNormals.hlsl"
//
//            CBUFFER_START(UnityPerMaterial)
//                // ↓↓ 你自己的材质属性，必须与其他 Pass 的 CBUFFER 完全一致
//                float4 _BaseColor;
//                float4 _BaseMap_ST;
//                // ↑↑
//                OSN_OUTLINE_MATERIAL_FIELDS
//            CBUFFER_END
//
//            #include "Packages/com.alefeng.outlinesmoothnormalsgenerator/Shader/OutlinePassURP.hlsl"
//            ENDHLSL
//        }
//
//  ⚠ include 顺序不能变：OutlineSmoothNormals.hlsl 要在 CBUFFER 之前
//    （CBUFFER 用到了它里面的宏），OutlinePassURP.hlsl 要在 CBUFFER 之后
//    （模板里的顶点着色器要用到这些 uniform）。顺序错了就是编译错误。
//
//  ⚠ SRP Batcher：CBUFFER 名必须是 UnityPerMaterial，且【所有 Pass 的字段
//    列表完全一致】—— 包括你的主 Pass。少写或顺序不同都会让 batcher 静默
//    失效：不报错、只掉性能。所以描边属性要用宏拼进那唯一的一份，而不是
//    单独开一个 CBUFFER。
//
//  ⚠ LightMode 用 SRPDefaultUnlit，这样描边 Pass 会被 URP 自动渲染，无需
//    额外的 Renderer Feature。
// ═══════════════════════════════════════════════════════════════════════

// URP 的逆转置法线变换与裁剪空间变换（来自已 include 的 Core.hlsl）。
#define OSN_OBJECT_TO_WORLD_NORMAL(n) TransformObjectToWorldNormal(n)
#define OSN_OBJECT_TO_CLIP(p)         TransformObjectToHClip(p)

#include "OutlinePassCommon.hlsl"

#endif // OUTLINE_PASS_URP_INCLUDED
