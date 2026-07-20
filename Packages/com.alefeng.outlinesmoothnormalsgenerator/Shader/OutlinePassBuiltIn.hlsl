#ifndef OUTLINE_PASS_BUILTIN_INCLUDED
#define OUTLINE_PASS_BUILTIN_INCLUDED

// ═══════════════════════════════════════════════════════════════════════
//  OutlinePassBuiltIn.hlsl —— 描边 OUTLINE Pass 模板（Built-in RP）
//
//  把背面外扩描边接进你自己的 Built-in Shader，整个过程就是下面这一段。
//  实际逻辑在 OutlinePassCommon.hlsl，本文件只负责把两个变换宏指向 Built-in。
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
//            Tags { "LightMode" = "Always" }
//
//            Cull Front          // 只画背面，露出的边缘即描边
//            ZWrite On
//            ZTest LEqual
//
//            CGPROGRAM
//            #pragma vertex   OSN_OutlineVert
//            #pragma fragment OSN_OutlineFrag
//
//            #include "UnityCG.cginc"
//            #include "Packages/com.alefeng.outlinesmoothnormalsgenerator/Shader/OutlineSmoothNormals.hlsl"
//
//            // Built-in 没有 SRP Batcher，直接当普通 uniform 声明即可，
//            // 不需要 CBUFFER。
//            OSN_OUTLINE_MATERIAL_FIELDS
//
//            #include "Packages/com.alefeng.outlinesmoothnormalsgenerator/Shader/OutlinePassBuiltIn.hlsl"
//            ENDCG
//        }
//
//  ⚠ include 顺序不能变：OutlineSmoothNormals.hlsl 要在属性声明之前
//    （声明用到了它里面的宏），OutlinePassBuiltIn.hlsl 要在其之后
//    （模板里的顶点着色器要用到这些 uniform）。顺序错了就是编译错误。
//
//  ⚠ 描边 Pass 必须排在基础渲染 Pass【之前】：它 Cull Front + ZWrite On，
//    先写好背面深度，正面才能正常盖住内侧。
//
//  ⚠ Built-in 的多 Pass 前向渲染中，LightMode = Always 表示该 Pass 与光照
//    无关、每个物体只渲染一次 —— 描边正是这种情况，用 ForwardBase 会让它
//    参与逐光源渲染而被重复绘制。
// ═══════════════════════════════════════════════════════════════════════

// Built-in 的逆转置法线变换与裁剪空间变换（来自已 include 的 UnityCG.cginc）。
#define OSN_OBJECT_TO_WORLD_NORMAL(n) UnityObjectToWorldNormal(n)
#define OSN_OBJECT_TO_CLIP(p)         UnityObjectToClipPos(p)

#include "OutlinePassCommon.hlsl"

#endif // OUTLINE_PASS_BUILTIN_INCLUDED
