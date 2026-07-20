using System.Collections.Generic;
using UnityEngine;
using NormalSpace = OutlineSmoothNormalsGenerator.OutlineSmoothNormalsGeneratorWindow.NormalSpace;

namespace OutlineSmoothNormalsGenerator
{
    /// <summary>
    /// 将平滑法线数据写入不同存储通道。
    ///
    /// 三种方式存的都是完整三维方向，格式定义见 OutlineSmoothNormalsCodec 与
    /// Shader/OutlineSmoothNormals.hlsl。方向本身写在对象空间还是切线空间，
    /// 由 <see cref="NormalSpace"/> 决定 —— 与「写进哪个通道」正交。
    ///
    /// 所有写入都是幂等的：不读取自身此前写入的结果作为输入，因此重复点击
    /// 「生成」不会累积误差、也不会损坏数据。切线空间转换读的是 mesh.normals /
    /// mesh.tangents，都不是本类写入的目标（切线存储模式恒为对象空间、不做转换），
    /// 幂等性因此不受影响。
    /// </summary>
    public static class StorageWriter
    {
        // ═══════════════════════════════════════════════════════════════
        //  存储空间转换
        // ═══════════════════════════════════════════════════════════════
        /// <summary>
        /// 按目标空间换算一遍平滑法线。对象空间时原样返回，切线空间时逐顶点
        /// 投影到该顶点自身的 TBN。
        ///
        /// 永远返回新数组而不就地改写入参：调用方（生成器窗口）会把同一份
        /// 计算结果复用于预览叠加层，就地改会让预览显示的是切线空间坐标。
        ///
        /// 缺法线或缺切线时无法构造基，退回对象空间并告警 —— 此时写出的数据
        /// 与材质上选的「切线空间」不匹配，描边会整体偏斜。正常流程走不到这里：
        /// OutlineMeshValidator 会在烘焙前就把这种网格报为 Error。
        /// </summary>
        private static Vector3[] ResolveSpace(Mesh mesh, Vector3[] smoothNormals, NormalSpace space)
        {
            if (space == NormalSpace.Object) return smoothNormals;

            int vCount = mesh.vertexCount;
            var normals  = mesh.normals;
            var tangents = mesh.tangents;

            bool normalsOk  = normals  != null && normals.Length  == vCount;
            bool tangentsOk = tangents != null && tangents.Length == vCount;
            if (!normalsOk || !tangentsOk)
            {
                Debug.LogWarning(
                    $"[平滑法线] 网格「{mesh.name}」缺少{(normalsOk ? "" : "法线")}" +
                    $"{(!normalsOk && !tangentsOk ? "与" : "")}{(tangentsOk ? "" : "切线")}，" +
                    "无法构造切线空间基，已退回【对象空间】写入。" +
                    "请在模型导入设置中开启切线生成后重新烘焙，" +
                    "或把材质的「存储空间」改为对象空间 —— 否则描边方向会整体偏斜。", mesh);
                return smoothNormals;
            }

            var result = new Vector3[vCount];
            for (int i = 0; i < vCount; i++)
                result[i] = OutlineSmoothNormalsCodec.ObjectToTangent(smoothNormals[i], normals[i], tangents[i]);
            return result;
        }

        // ═══════════════════════════════════════════════════════════════
        //  顶点色通道对（八面体编码 → 两个 8-bit 通道）
        // ═══════════════════════════════════════════════════════════════
        public static void WriteToVertexColor(Mesh mesh, Vector3[] smoothNormals,
            OutlineSmoothNormalsGeneratorWindow.VertexColorChannel channel =
            OutlineSmoothNormalsGeneratorWindow.VertexColorChannel.BA,
            NormalSpace space = NormalSpace.Object)
        {
            int vCount = mesh.vertexCount;
            smoothNormals = ResolveSpace(mesh, smoothNormals, space);

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
                    case OutlineSmoothNormalsGeneratorWindow.VertexColorChannel.RG:
                        r = x; g = y;
                        break;
                    case OutlineSmoothNormalsGeneratorWindow.VertexColorChannel.GB:
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
        //
        // 本模式【恒为对象空间】，故不接受 NormalSpace 参数：
        //   · 做不到 —— 把切线空间坐标存进切线，等于覆盖掉重建基所必需的切线本身。
        //     上面那段「静默损坏网格」说的正是这件事，与顶点色 / TEXCOORD 的切线
        //     空间存储不是一回事：后者的基来自另外两个通道，不被自己写坏。
        //   · 不需要 —— Unity 会把 tangent.xyz 当方向一起蒙皮，存进去的对象空间
        //     方向天然跟随骨骼动画，本就没有切线空间要解决的那个问题。
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
        public static void WriteToUV(Mesh mesh, Vector3[] smoothNormals, int channel,
            NormalSpace space = NormalSpace.Object)
        {
            // Unity 网格支持 TEXCOORD0..7，共 8 个 UV 通道。
            channel = Mathf.Clamp(channel, 0, 7);
            int vCount = mesh.vertexCount;
            smoothNormals = ResolveSpace(mesh, smoothNormals, space);

            var uvData = new List<Vector3>(vCount);
            for (int i = 0; i < vCount; i++)
                uvData.Add(smoothNormals[i].normalized);

            mesh.SetUVs(channel, uvData);
        }
    }
}
