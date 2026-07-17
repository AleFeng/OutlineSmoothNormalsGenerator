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

        [Header(Outline)]
        _OutlineColor   ("Outline Color",   Color)  = (0,0,0,1)
        [PowerSlider(3.0)]
        _OutlineWidth   ("Outline Width",   Range(0, 0.1)) = 0.015

        // 一律以 TEXCOORDn 命名，与 mesh.SetUVs(n) 的索引恒等对应。
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
            #pragma vertex   OutlineVert
            #pragma fragment OutlineFrag
            #pragma shader_feature_local_vertex _SMOOTHNORMALSRC_VERTEXCOLOR _SMOOTHNORMALSRC_TANGENTSPACE _SMOOTHNORMALSRC_TEXCOORD0 _SMOOTHNORMALSRC_TEXCOORD1 _SMOOTHNORMALSRC_TEXCOORD2 _SMOOTHNORMALSRC_TEXCOORD3 _SMOOTHNORMALSRC_VERTEXNORMAL

            #include "UnityCG.cginc"
            #include "Packages/com.alefeng.outlinesmoothnormalsgenerator/Shader/OutlineSmoothNormals.hlsl"

            float4 _OutlineColor;
            float  _OutlineWidth;
            float  _VCChannel;

            struct OutlineAppdata
            {
                float4 vertex  : POSITION;
                float3 normal  : NORMAL;
                float4 tangent : TANGENT;
                float4 color   : COLOR;
                float4 uv0     : TEXCOORD0;
                float4 uv1     : TEXCOORD1;
                float4 uv2     : TEXCOORD2;
                float4 uv3     : TEXCOORD3;
            };

            struct OutlineV2F
            {
                float4 pos : SV_POSITION;
            };

            OutlineV2F OutlineVert(OutlineAppdata v)
            {
                OutlineV2F o;

                // ── 解码平滑法线（对象空间）──────────────────────────
                float3 smoothNormalOS;
                #if defined(_SMOOTHNORMALSRC_VERTEXCOLOR)
                    smoothNormalOS = OSN_DecodeVertexColor(v.color, _VCChannel);
                #elif defined(_SMOOTHNORMALSRC_TANGENTSPACE)
                    smoothNormalOS = OSN_DecodeTangent(v.tangent);
                #elif defined(_SMOOTHNORMALSRC_TEXCOORD0)
                    smoothNormalOS = OSN_DecodeTexCoord(v.uv0.xyz);
                #elif defined(_SMOOTHNORMALSRC_TEXCOORD1)
                    smoothNormalOS = OSN_DecodeTexCoord(v.uv1.xyz);
                #elif defined(_SMOOTHNORMALSRC_TEXCOORD2)
                    smoothNormalOS = OSN_DecodeTexCoord(v.uv2.xyz);
                #elif defined(_SMOOTHNORMALSRC_TEXCOORD3)
                    smoothNormalOS = OSN_DecodeTexCoord(v.uv3.xyz);
                #else
                    // _SMOOTHNORMALSRC_VERTEXNORMAL，以及材质未设置任何关键字时。
                    smoothNormalOS = normalize(v.normal);
                #endif

                // 逆转置变换，正确处理非均匀缩放。
                float3 normalWS = UnityObjectToWorldNormal(smoothNormalOS);
                float4 clipPos  = UnityObjectToClipPos(v.vertex);

                o.pos = OSN_ApplyOutlineOffset(clipPos, normalWS, _OutlineWidth);
                return o;
            }

            fixed4 OutlineFrag(OutlineV2F i) : SV_Target
            {
                return _OutlineColor;
            }
            ENDCG
        }

        // ── Pass 1: Forward Lit ──────────────────────────────────────
        // 极简兰伯特，只为让 Demo 能看。实际项目通常用自己的主材质，
        // 把 OUTLINE Pass 复制过去即可。
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

            sampler2D _MainTex;
            float4    _MainTex_ST;
            float4    _BaseColor;

            struct BaseAppdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv     : TEXCOORD0;
            };

            struct BaseV2F
            {
                float4 pos    : SV_POSITION;
                float2 uv     : TEXCOORD0;
                float3 worldN : TEXCOORD1;
            };

            BaseV2F BaseVert(BaseAppdata v)
            {
                BaseV2F o;
                o.pos    = UnityObjectToClipPos(v.vertex);
                o.uv     = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldN = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 BaseFrag(BaseV2F i) : SV_Target
            {
                fixed4 texCol = tex2D(_MainTex, i.uv) * _BaseColor;

                float3 N   = normalize(i.worldN);
                float3 L   = normalize(_WorldSpaceLightPos0.xyz);
                float  NdL = max(0, dot(N, L));

                fixed3 col = texCol.rgb * (_LightColor0.rgb * NdL + UNITY_LIGHTMODEL_AMBIENT.rgb);
                return fixed4(col, texCol.a);
            }
            ENDCG
        }
    }

    CustomEditor "OutlineSmoothNormalsGenerator.OutlineShaderGUI"
}
