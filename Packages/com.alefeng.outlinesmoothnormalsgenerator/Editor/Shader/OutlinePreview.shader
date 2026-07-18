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
        // UV channel index (0-3) —— 与 mesh.SetUVs 的索引一致
        _UVChannel      ("UV Channel",      Float)   = 1
        // Vertex color channel pair: 0=RG, 1=GB, 2=BA
        _VCChannel      ("VC Channel",      Float)   = 2
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
                    else              uvXYZ = v.uv3.xyz;
                    smoothNormalOS = OSN_DecodeTexCoord(uvXYZ);
                }

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
