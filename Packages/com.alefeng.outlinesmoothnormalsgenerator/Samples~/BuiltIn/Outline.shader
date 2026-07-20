Shader "OutlineSmoothNormalsGenerator/Outline Built-in"
{
    // ═══════════════════════════════════════════════════════════════════
    //  OutlineSmoothNormalsGenerator/Outline Built-in
    //  支持顶点色、切线、TEXCOORD 三种平滑法线存储方式的描边 Shader
    //  Built-in Render Pipeline
    //
    //  解码与外扩数学一律来自包内的 OutlineSmoothNormals.hlsl，与编辑器
    //  预览、以及 URP 版共用同一份代码，不在本文件内另写一套。
    //
    //  ⚠ 本 Shader 在 URP 项目下【能编译但不会渲染】—— URP 只绘制
    //    UniversalForward 等 Pass，找不到 Always / ForwardBase。
    //    要验证它，需把 Project Settings > Graphics 的渲染管线资产设为 None
    //    （即切回 Built-in）。详见 Assets/Demo/README.md。
    //
    //  本文件与 Samples~/BuiltIn/Outline.shader 应当【逐字节相同】——
    //  插件以 embedded package 形式位于 Packages/ 下，因此下面的 Packages/
    //  路径在开发期与安装后都成立，拷贝进 Samples~ 时无需改动任何内容。
    // ═══════════════════════════════════════════════════════════════════
    Properties
    {
        [Header(Base)]
        _BaseColor      ("Base Color",      Color)  = (1,1,1,1)
        _MainTex        ("Albedo",          2D)     = "white" {}
        // 基础色来源。除 Base Map 外都是【调试模式】：把平滑法线数据直接当颜色显示、
        // 不经光照，便于肉眼核对生成结果。切线 [-1,1]→[0,1]；UV 取 xy 作 RG、B=0；
        // 顶点色 RG/GB/BA 只显示对应通道对（另一通道置 0，BA 的 A 借 R 显示）；UV0..7 各一档。
        // 共 14 项，超过 Shader 内联 [Enum(name,val,…)] 的 7 组上限（超了会退化成裸
        // 数字输入框），故不写 [Enum]，下拉改由自定义 Inspector（OutlineShaderGUI）绘制。
        _BaseColorMode  ("Base Color Mode", Float)  = 0

        // 基础 NPR：卡通两段式明暗 + 边缘光。描边多用于 NPR，故 Demo 的基础
        // 渲染也做成 NPR 风格；只做最基础的效果，演示够用即可。
        [Header(NPR Shading)]
        _ShadeColor     ("Shade Tint",      Color)  = (0.42, 0.48, 0.60, 1)
        _ShadeThreshold ("Shade Threshold", Range(0, 1)) = 0.5
        _ShadeSoftness  ("Shade Softness",  Range(0.001, 0.5)) = 0.06
        _RimColor       ("Rim Color",       Color)  = (0.85, 0.9, 1.0, 1)
        _RimPower       ("Rim Power",       Range(0.5, 12)) = 5

        [Header(Outline)]
        _OutlineColor   ("Outline Color",   Color)  = (0,0,0,1)
        [PowerSlider(3.0)]
        _OutlineWidth   ("Outline Width",   Range(0, 0.1)) = 0.015
        // 屏幕空间：描边等宽，不随距离变化；世界空间：按世界单位偏移，近大远小。
        [Enum(Screen Space, 0, World Space, 1)]
        _OutlineWidthMode ("Outline Width Mode", Float) = 0

        // 平滑法线来源。一律以 TEXCOORDn 命名，与 mesh.SetUVs(n) 的索引恒等对应
        // （Unity 的 mesh.uv2 就是 TEXCOORD1，极易差一位）。VertexNormal 走原始顶点
        // 法线，即「未使用本工具」的对照组。
        // 取值：0 顶点色 / 1 切线 / 2..5 TEXCOORD0..3 / 6 顶点法线 / 7..10 TEXCOORD4..7。
        // 共 11 项，超过 Shader 内联 [KeywordEnum] 的上限，故改为运行时按 float 分支
        // （见 OSN_SelectSmoothNormalOS），下拉由自定义 Inspector（OutlineShaderGUI）绘制。
        [Header(Storage Mode)]
        _SmoothNormalSrc ("Smooth Normal Source", Float) = 0

        // 顶点色模式下使用哪一对通道，需与生成时的选择一致。
        [Enum(RG, 0, GB, 1, BA, 2)]
        _VCChannel      ("Vertex Color Channel", Float) = 2

        // 平滑法线写在哪个空间里 —— 与「存进哪个通道」正交，需与生成时的选择一致。
        //   0 对象空间：解码即用，仅静态模型正确。
        //   1 切线空间：用蒙皮后的法线与切线重建 TBN 再还原，SkinnedMeshRenderer 上也正确。
        // 默认 1（切线空间），与工具窗口、导入自动烘焙的默认值一致 —— 按默认设置烘完
        // 即可直接用，且蒙皮模型不会踩「动画一跑描边就撕开」的坑。
        // ⚠ 从 1.4.x 升级：存量数据都是按对象空间烘的，材质却会改用切线空间去解，描边
        //   会整体偏斜。请重新烘焙一次，或把本项手动改回 Object Space。
        [Enum(Object Space, 0, Tangent Space, 1)]
        _SmoothNormalSpace ("Smooth Normal Space", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200

        // ── Pass 0: Outline ──────────────────────────────────────────
        // 翻转面外扩：剔除正面、只画背面，露出的边缘即描边。
        Pass
        {
            Name "OUTLINE"
            Tags { "LightMode" = "Always" }

            Cull Front
            ZWrite On
            ZTest LEqual

            CGPROGRAM
            #pragma vertex   OSN_OutlineVert
            #pragma fragment OSN_OutlineFrag

            #include "UnityCG.cginc"
            #include "Packages/com.alefeng.outlinesmoothnormalsgenerator/Shader/OutlineSmoothNormals.hlsl"

            // Built-in 没有 SRP Batcher，描边这 6 个属性直接当普通 uniform 声明即可。
            OSN_OUTLINE_MATERIAL_FIELDS

            // 顶点输入输出与 vert / frag 全部来自共享模板。Demo 与用户项目走的是
            // 同一条接入路径 —— 模板出问题，这里会第一时间暴露。
            #include "Packages/com.alefeng.outlinesmoothnormalsgenerator/Shader/OutlinePassBuiltIn.hlsl"
            ENDCG
        }

        // ── Pass 1: Forward (NPR) ────────────────────────────────────
        // 最基础的 NPR：卡通两段式明暗（cel shading）+ 边缘光（rim）。
        // 描边多用于 NPR，故 Demo 的基础渲染也做成 NPR 风格，够用即可。
        // 实际项目通常用自己的主材质，把 OUTLINE Pass 复制过去即可。
        Pass
        {
            Name "FORWARD"
            Tags { "LightMode" = "ForwardBase" }

            Cull Back
            ZWrite On

            CGPROGRAM
            #pragma vertex   BaseVert
            #pragma fragment BaseFrag
            #pragma multi_compile_fwdbase

            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "Packages/com.alefeng.outlinesmoothnormalsgenerator/Shader/Demo/OutlineNPR.hlsl"

            sampler2D _MainTex;
            float4    _MainTex_ST;
            float4    _BaseColor;
            float4    _ShadeColor;
            float4    _RimColor;
            float     _ShadeThreshold;
            float     _ShadeSoftness;
            float     _RimPower;
            float     _BaseColorMode;

            // 基础色调试模式要用到原始的顶点色 / 切线 / 各 UV，因此这里把它们都读进来。
            struct BaseAppdata
            {
                float4 vertex  : POSITION;
                float3 normal  : NORMAL;
                float4 tangent : TANGENT;
                float4 color   : COLOR;
                float4 uv0     : TEXCOORD0;
                float4 uv1     : TEXCOORD1;
                float4 uv2     : TEXCOORD2;
                float4 uv3     : TEXCOORD3;
                float4 uv4     : TEXCOORD4;
                float4 uv5     : TEXCOORD5;
                float4 uv6     : TEXCOORD6;
                float4 uv7     : TEXCOORD7;
            };

            struct BaseV2F
            {
                float4 pos       : SV_POSITION;
                float2 uv        : TEXCOORD0;
                float3 worldN    : TEXCOORD1;
                float3 worldPos  : TEXCOORD2;
                float3 dataColor : TEXCOORD3;   // 调试基础色（非 Base Map 模式用）
            };

            BaseV2F BaseVert(BaseAppdata v)
            {
                BaseV2F o;
                o.pos       = UnityObjectToClipPos(v.vertex);
                o.uv        = TRANSFORM_TEX(v.uv0.xy, _MainTex);
                o.worldN    = UnityObjectToWorldNormal(v.normal);
                o.worldPos  = mul(unity_ObjectToWorld, v.vertex).xyz;
                // 基础色来源的映射收敛在 OutlineNPR.hlsl，两个管线共用一份。
                o.dataColor = OSN_DebugBaseColor(_BaseColorMode, v.color, v.tangent,
                                                 v.uv0.xyz, v.uv1.xyz, v.uv2.xyz, v.uv3.xyz,
                                                 v.uv4.xyz, v.uv5.xyz, v.uv6.xyz, v.uv7.xyz);
                return o;
            }

            fixed4 BaseFrag(BaseV2F i) : SV_Target
            {
                // 调试模式：直接把平滑法线数据当颜色输出，不经光照，便于读数。
                if (_BaseColorMode > 0.5)
                    return fixed4(i.dataColor, 1.0);

                fixed4 texCol = tex2D(_MainTex, i.uv) * _BaseColor;
                float3 albedo = texCol.rgb;

                float3 N = normalize(i.worldN);
                float3 V = normalize(_WorldSpaceCameraPos - i.worldPos);
                float3 L = normalize(_WorldSpaceLightPos0.xyz);

                // 卡通两段式明暗 + 边缘光，数学收敛在 OutlineNPR.hlsl，两个管线共用。
                float3 toon = OSN_ToonRamp(dot(N, L), _ShadeColor.rgb, _ShadeThreshold, _ShadeSoftness);

                fixed3 col = albedo * (_LightColor0.rgb * toon + UNITY_LIGHTMODEL_AMBIENT.rgb);

                col += OSN_RimLight(dot(N, V), _RimPower) * _RimColor.rgb;

                return fixed4(col, texCol.a);
            }
            ENDCG
        }
    }

    CustomEditor "OutlineSmoothNormalsGenerator.OutlineShaderGUI"
}
