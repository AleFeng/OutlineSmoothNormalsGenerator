Shader "OutlineSmoothNormalsGenerator/Outline URP"
{
    // ═══════════════════════════════════════════════════════════════════
    //  OutlineSmoothNormalsGenerator/Outline URP
    //  支持顶点色、切线、TEXCOORD 三种平滑法线存储方式的描边 Shader
    //  Universal Render Pipeline (URP 14+)
    //
    //  解码与外扩数学一律来自包内的 OutlineSmoothNormals.hlsl，与编辑器
    //  预览、以及 Built-in 版共用同一份代码，不在本文件内另写一套。
    //
    //  本文件与 Samples~/URP/Outline.shader 应当【逐字节相同】——
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
        // 顶点色 RG/GB/BA 只显示对应通道对（另一通道置 0，BA 的 A 借 R 显示）。
        // 共 10 项，超过 Shader 内联 [Enum(name,val,…)] 的 7 组上限（超了会退化成裸
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

        // 一律以 TEXCOORDn 命名，与 mesh.SetUVs(n) 的索引恒等对应。
        // 不用「UV1/UV2」这类叫法：Unity 自己的 mesh.uv2 就是 TEXCOORD1，
        // 极易差一位 —— 此前生产与工具的 UV 通道正是整体错开了一格。
        // VertexNormal 走原始顶点法线，即「未使用本工具」的对照组。
        [Header(Storage Mode)]
        [KeywordEnum(VertexColor, TangentSpace, TexCoord0, TexCoord1, TexCoord2, TexCoord3, VertexNormal)]
        _SmoothNormalSrc ("Smooth Normal Source", Float) = 0

        // 顶点色模式下使用哪一对通道，需与生成时的选择一致。
        [Enum(RG, 0, GB, 1, BA, 2)]
        _VCChannel      ("Vertex Color Channel", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Geometry"
        }
        LOD 200

        // ── Pass 0: Outline ──────────────────────────────────────────
        // 翻转面外扩：剔除正面、只画背面，露出的边缘即描边。
        Pass
        {
            Name "OUTLINE"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex   OutlineVert
            #pragma fragment OutlineFrag
            #pragma shader_feature_local_vertex _SMOOTHNORMALSRC_VERTEXCOLOR _SMOOTHNORMALSRC_TANGENTSPACE _SMOOTHNORMALSRC_TEXCOORD0 _SMOOTHNORMALSRC_TEXCOORD1 _SMOOTHNORMALSRC_TEXCOORD2 _SMOOTHNORMALSRC_TEXCOORD3 _SMOOTHNORMALSRC_VERTEXNORMAL

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.alefeng.outlinesmoothnormalsgenerator/Shader/OutlineSmoothNormals.hlsl"

            // SRP Batcher 要求同一 Shader 各 Pass 的 UnityPerMaterial 完全一致。
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _MainTex_ST;
                float4 _OutlineColor;
                float4 _ShadeColor;
                float4 _RimColor;
                float  _OutlineWidth;
                float  _OutlineWidthMode;
                float  _SmoothNormalSrc;
                float  _VCChannel;
                float  _ShadeThreshold;
                float  _ShadeSoftness;
                float  _RimPower;
                float  _BaseColorMode;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float4 color      : COLOR;
                float4 uv0        : TEXCOORD0;
                float4 uv1        : TEXCOORD1;
                float4 uv2        : TEXCOORD2;
                float4 uv3        : TEXCOORD3;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings OutlineVert(Attributes IN)
            {
                Varyings OUT;

                // ── 解码平滑法线（对象空间）──────────────────────────
                // 关键字 → 解码器 的映射收敛在共享库里，两个管线共用一份。
                float3 smoothNormalOS = OSN_SelectSmoothNormalOS(
                    IN.color, IN.tangentOS,
                    IN.uv0.xyz, IN.uv1.xyz, IN.uv2.xyz, IN.uv3.xyz,
                    IN.normalOS, _VCChannel);

                // 逆转置变换，正确处理非均匀缩放。
                float3 normalWS = TransformObjectToWorldNormal(smoothNormalOS);
                float4 clipPos  = TransformObjectToHClip(IN.positionOS.xyz);

                OUT.positionCS = OSN_ApplyOutlineOffset(clipPos, normalWS, _OutlineWidth, _OutlineWidthMode);
                return OUT;
            }

            half4 OutlineFrag(Varyings IN) : SV_Target
            {
                return half4(_OutlineColor);
            }
            ENDHLSL
        }

        // ── Pass 1: Forward (NPR) ────────────────────────────────────
        // 最基础的 NPR：卡通两段式明暗（cel shading）+ 边缘光（rim）。
        // 描边多用于 NPR，故 Demo 的基础渲染也做成 NPR 风格，够用即可。
        // 实际项目通常用自己的主材质，把 OUTLINE Pass 复制过去即可。
        Pass
        {
            Name "FORWARD"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex   BaseVert
            #pragma fragment BaseFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.alefeng.outlinesmoothnormalsgenerator/Shader/OutlineNPR.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _MainTex_ST;
                float4 _OutlineColor;
                float4 _ShadeColor;
                float4 _RimColor;
                float  _OutlineWidth;
                float  _OutlineWidthMode;
                float  _SmoothNormalSrc;
                float  _VCChannel;
                float  _ShadeThreshold;
                float  _ShadeSoftness;
                float  _RimPower;
                float  _BaseColorMode;
            CBUFFER_END

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            // 基础色调试模式要用到原始的顶点色 / 切线 / 各 UV，因此这里把它们都读进来。
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float4 color      : COLOR;
                float4 uv0        : TEXCOORD0;
                float4 uv1        : TEXCOORD1;
                float4 uv2        : TEXCOORD2;
                float4 uv3        : TEXCOORD3;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float3 dataColor  : TEXCOORD3;   // 调试基础色（非 Base Map 模式用）
            };

            Varyings BaseVert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.uv         = TRANSFORM_TEX(IN.uv0.xy, _MainTex);
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                // 基础色来源的映射收敛在 OutlineNPR.hlsl，两个管线共用一份。
                OUT.dataColor  = OSN_DebugBaseColor(_BaseColorMode, IN.color, IN.tangentOS,
                                                    IN.uv0.xyz, IN.uv1.xyz, IN.uv2.xyz, IN.uv3.xyz);
                return OUT;
            }

            half4 BaseFrag(Varyings IN) : SV_Target
            {
                // 调试模式：直接把平滑法线数据当颜色输出，不经光照，便于读数。
                if (_BaseColorMode > 0.5)
                    return half4(IN.dataColor, 1.0);

                half4  texCol = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv) * _BaseColor;
                float3 albedo = texCol.rgb;

                float3 N = normalize(IN.normalWS);
                float3 V = normalize(_WorldSpaceCameraPos - IN.positionWS);
                Light  mainLight = GetMainLight();
                float3 L = mainLight.direction;

                // 卡通两段式明暗 + 边缘光，数学收敛在 OutlineNPR.hlsl，两个管线共用。
                float3 toon = OSN_ToonRamp(dot(N, L), _ShadeColor.rgb, _ShadeThreshold, _ShadeSoftness);

                half3 ambient = SampleSH(N);
                half3 col = albedo * (mainLight.color * toon + ambient);

                col += OSN_RimLight(dot(N, V), _RimPower) * _RimColor.rgb;

                return half4(col, texCol.a);
            }
            ENDHLSL
        }
    }

    CustomEditor "OutlineSmoothNormalsGenerator.OutlineShaderGUI"
}
