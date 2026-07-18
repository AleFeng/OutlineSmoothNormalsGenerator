using System.Collections.Generic;
using UnityEngine;

namespace OutlineSmoothNormalsGenerator
{
    /// <summary>
    /// 将平滑法线数据写入不同存储通道。
    ///
    /// 三种方式存的都是【对象空间】的完整三维方向，格式定义见
    /// OutlineSmoothNormalsCodec 与 Shader/OutlineSmoothNormals.hlsl。
    ///
    /// 所有写入都是幂等的：不读取自身此前写入的结果作为输入，因此重复点击
    /// 「生成」不会累积误差、也不会损坏数据。
    /// </summary>
    public static class StorageWriter
    {
        // ═══════════════════════════════════════════════════════════════
        //  顶点色通道对（八面体编码 → 两个 8-bit 通道）
        // ═══════════════════════════════════════════════════════════════
        public static void WriteToVertexColor(Mesh mesh, Vector3[] smoothNormals,
            OutlineSmoothNormalsGeneratorWindow.VertexColorChannel channel =
            OutlineSmoothNormalsGeneratorWindow.VertexColorChannel.Ba)
        {
            int vCount = mesh.vertexCount;

            var existingColors = mesh.colors32;
            var colors = new Color32[vCount];
            bool hasExisting = existingColors != null && existingColors.Length == vCount;

            for (int i = 0; i < vCount; i++)
            {
                var oct = OutlineSmoothNormalsCodec.OctEncode(smoothNormals[i]);
                byte x = OutlineSmoothNormalsCodec.PackUNorm(oct.x);
                byte y = OutlineSmoothNormalsCodec.PackUNorm(oct.y);

                // 未被选中的通道保留原值，不干扰其他效果使用的顶点色。
                byte r = hasExisting ? existingColors[i].r : (byte)128;
                byte g = hasExisting ? existingColors[i].g : (byte)128;
                byte b = hasExisting ? existingColors[i].b : (byte)128;
                byte a = hasExisting ? existingColors[i].a : (byte)128;

                switch (channel)
                {
                    case OutlineSmoothNormalsGeneratorWindow.VertexColorChannel.Rg:
                        r = x; g = y;
                        break;
                    case OutlineSmoothNormalsGeneratorWindow.VertexColorChannel.Gb:
                        g = x; b = y;
                        break;
                    default: // Ba
                        b = x; a = y;
                        break;
                }

                colors[i] = new Color32(r, g, b, a);
            }

            mesh.colors32 = colors;
        }

        // ═══════════════════════════════════════════════════════════════
        //  切线（tangent.xyz 直接存对象空间平滑法线，w 恒为 1）
        // ═══════════════════════════════════════════════════════════════
        // 注意：本模式会覆盖网格原始切线，法线贴图将失效 —— 这是该模式固有
        // 的代价，UI 已就此告警。但它换来的是「三个完整 float 分量」：无需
        // 压缩、无需重建、无符号歧义。
        //
        // 早期版本会把法线转换到切线空间再存，那既要求写入时读取 mesh.tangents
        // 作为基（导致第二次生成时读到的是自己上次写入的结果，静默损坏网格），
        // 又与解码侧重建的基不一致。由于编解码用的是同一组由顶点法线导出的
        // 正交基，该变换在数学上恒等于原向量本身 —— 纯属多余。直接存即可。
        public static void WriteToTangent(Mesh mesh, Vector3[] smoothNormals)
        {
            int vCount = mesh.vertexCount;
            var tangents = new Vector4[vCount];

            for (int i = 0; i < vCount; i++)
            {
                var n = smoothNormals[i].normalized;
                tangents[i] = new Vector4(n.x, n.y, n.z, 1f);
            }

            mesh.tangents = tangents;
        }

        // ═══════════════════════════════════════════════════════════════
        //  TEXCOORD 通道（uv.xyz 直接存对象空间平滑法线）
        // ═══════════════════════════════════════════════════════════════
        // 提交 Vector3 而非 Vector4：既够用，又避免把目标通道无谓地撑成
        // 4 分量、白白翻倍顶点缓冲占用。
        public static void WriteToUV(Mesh mesh, Vector3[] smoothNormals, int channel)
        {
            // Unity 网格支持 TEXCOORD0..7，共 8 个 UV 通道。
            channel = Mathf.Clamp(channel, 0, 7);
            int vCount = mesh.vertexCount;

            var uvData = new List<Vector3>(vCount);
            for (int i = 0; i < vCount; i++)
                uvData.Add(smoothNormals[i].normalized);

            mesh.SetUVs(channel, uvData);
        }
    }
}
