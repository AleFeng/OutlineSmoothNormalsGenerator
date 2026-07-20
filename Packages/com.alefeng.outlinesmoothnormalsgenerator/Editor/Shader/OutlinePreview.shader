Shader "OutlineSmoothNormalsGenerator/OutlinePreview"
{
    // ═══════════════════════════════════════════════════════════════════
    //  编辑器内嵌预览专用，请勿用于生产。
    //
    //  解码与外扩数学一律来自 ../../Shader/OutlineSmoothNormals.hlsl，
    //  与生产 Outline.shader 共用同一份代码 —— 这正是「预览和实际效果
    //  不一致」不再可能发生的原因。本文件不得另写一套数学。
    //
    //  与生产版的唯一区别：这里用 float uniform 做运行时分支，而不是
    //  shader 关键字，因为预览面板需要实时切换存储模式。
    // ═══════════════════════════════════════════════════════════════════
    Properties
    {
        _OutlineColor   ("Outline Color",   Color)   = (0, 0, 0, 1)
        _OutlineWidth   ("Outline Width",   Float)   = 0.02
        // 0 = 屏幕空间（等宽）, 1 = 世界空间（世界单位偏移）
        _OutlineWidthMode ("Outline Width Mode", Float) = 0
        // 0 = VertexColor, 1 = TangentSpace, 2 = UV
        _StorageMode    ("Storage Mode",    Float)   = 0
        // UV channel index (0-7) —— 与 mesh.SetUVs 的索引一致
        _UVChannel      ("UV Channel",      Float)   = 1
        // Vertex color channel pair: 0=RG, 1=GB, 2=BA
        _VCChannel      ("VC Channel",      Float)   = 2
        // 0 = 对象空间, 1 = 切线空间（与「存进哪个通道」正交）
        _NormalSpace    ("Normal Space",    Float)   = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+1" }

        // ── Outline Pass ─────────────────────────────────────────────
        // Cull Front：只渲染背面。翻转面外扩要求剔除正面，让背面围绕
        // 模型边缘露出，形成描边。
        //
        // 刻意不写 LightMode 标签：Built-in 下按默认 Pass 绘制；URP 下
        // 被视作 SRPDefaultUnlit 同样会绘制。预览因此不依赖具体管线。
        Pass
        {
            Name "OUTLINE"
            Cull Front
            ZWrite On
            ZTest LEqual

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "../../Shader/OutlineSmoothNormals.hlsl"

            float4 _OutlineColor;
            float  _OutlineWidth;
            float  _OutlineWidthMode;
            float  _StorageMode;
            float  _UVChannel;
            float  _VCChannel;
            float  _NormalSpace;

            struct appdata
            {
                float4 vertex   : POSITION;
                float3 normal   : NORMAL;
                float4 tangent  : TANGENT;
                float4 color    : COLOR;
                float4 uv0      : TEXCOORD0;
                float4 uv1      : TEXCOORD1;
                float4 uv2      : TEXCOORD2;
                float4 uv3      : TEXCOORD3;
                float4 uv4      : TEXCOORD4;
                float4 uv5      : TEXCOORD5;
                float4 uv6      : TEXCOORD6;
                float4 uv7      : TEXCOORD7;
            };

            struct v2f
            {
                float4 pos  : SV_POSITION;
                UNITY_FOG_COORDS(0)
            };

            v2f vert(appdata v)
            {
                v2f o;

                // ── 解码平滑法线（对象空间）──────────────────────────
                float3 smoothNormalOS;
                int mode = (int)round(_StorageMode);

                if (mode == 0)
                {
                    smoothNormalOS = OSN_DecodeVertexColor(v.color, _VCChannel);
                }
                else if (mode == 1)
                {
                    // tangent.xyz 直接就是对象空间平滑法线
                    smoothNormalOS = OSN_DecodeTangent(v.tangent);
                }
                else
                {
                    int ch = (int)round(_UVChannel);
                    float3 uvXYZ;
                    if      (ch == 0) uvXYZ = v.uv0.xyz;
                    else if (ch == 1) uvXYZ = v.uv1.xyz;
                    else if (ch == 2) uvXYZ = v.uv2.xyz;
                    else if (ch == 3) uvXYZ = v.uv3.xyz;
                    else if (ch == 4) uvXYZ = v.uv4.xyz;
                    else if (ch == 5) uvXYZ = v.uv5.xyz;
                    else if (ch == 6) uvXYZ = v.uv6.xyz;
                    else              uvXYZ = v.uv7.xyz;
                    smoothNormalOS = OSN_DecodeTexCoord(uvXYZ);
                }

                // ── 存储空间还原 ────────────────────────────────────
                // 这里不能调 OSN_ResolveSmoothNormalSpace：它的 mode 参数走的是生产
                // Shader 的 _SmoothNormalSrc 编号（1=切线 / 6=顶点法线），与本文件
                // _StorageMode 的编号（0=顶点色 / 1=切线 / 2=UV）不是一套。直接按本地
                // 编号判断并调用底层的 OSN_TangentToObject，数学仍是同一份。
                //
                // 切线通道模式（mode==1）不参与：存进去的就是对象空间方向，且切线已被
                // 数据占用、无基可重建。C# 侧也已把该情形的 _NormalSpace 强制置 0。
                if (_NormalSpace > 0.5 && mode != 1)
                    smoothNormalOS = OSN_TangentToObject(smoothNormalOS, v.normal, v.tangent);

                // 逆转置变换，正确处理非均匀缩放。
                float3 normalWS = UnityObjectToWorldNormal(smoothNormalOS);
                float4 clipPos  = UnityObjectToClipPos(v.vertex);

                o.pos = OSN_ApplyOutlineOffset(clipPos, normalWS, _OutlineWidth, _OutlineWidthMode);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 col = _OutlineColor;
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }

    FallBack "Diffuse"
}
