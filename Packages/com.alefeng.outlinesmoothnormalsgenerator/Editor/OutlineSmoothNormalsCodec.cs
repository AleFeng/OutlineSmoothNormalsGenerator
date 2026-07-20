using UnityEngine;

namespace OutlineSmoothNormalsGenerator
{
    /// <summary>
    /// 平滑法线存储格式的 C# 侧编解码 —— 是 Shader/OutlineSmoothNormals.hlsl 的镜像。
    ///
    /// ⚠ 本文件与该 .hlsl 必须逐行对应。**以 .hlsl 为准**：它同时被生产描边与
    ///   编辑器预览使用，本文件只服务于写入（StorageWriter）与预览叠加层的
    ///   CPU 解码。两边一旦不一致，症状就是「叠加的法线线段和实际描边对不上」。
    ///
    /// ── 存储格式 ─────────────────────────────────────────────────────────
    /// 平滑法线始终是完整三维方向，不做半球压缩：
    ///   顶点色     选定通道对 (8-bit × 2) ← 八面体编码，全球面双射
    ///   切线       tangent.xyz (float × 3) ← 直接存，w 恒为 1
    ///   TEXCOORDn  uv.xyz      (float × 3) ← 直接存
    ///
    /// ── 存储空间 ─────────────────────────────────────────────────────────
    /// 与「存进哪个通道」正交的维度：方向本身写在对象空间还是该顶点自身的
    /// 切线空间。切线空间坐标是蒙皮不变量，用于让 SkinnedMeshRenderer 上的
    /// 顶点色 / TEXCOORD 存储也能跟随骨骼动画。详见 .hlsl 头注释。
    ///
    /// 早期版本用「存 XY + sqrt 重建 Z + 按顶点法线修正符号」，该方案在硬边
    /// 角点上必然失效：同一角上多个位置重合的顶点法线不同，会把同一条平滑
    /// 法线解码成不同结果，描边恰好在它本该修复的角上裂开。详见 .hlsl 头注释。
    /// </summary>
    public static class OutlineSmoothNormalsCodec
    {
        // ═══════════════════════════════════════════════════════════════
        //  八面体编码
        // ═══════════════════════════════════════════════════════════════
        private static Vector2 OctWrap(Vector2 v)
        {
            return new Vector2(
                (1f - Mathf.Abs(v.y)) * (v.x >= 0f ? 1f : -1f),
                (1f - Mathf.Abs(v.x)) * (v.y >= 0f ? 1f : -1f));
        }

        /// <summary>单位向量 → [0,1]^2。</summary>
        public static Vector2 OctEncode(Vector3 n)
        {
            float l1 = Mathf.Abs(n.x) + Mathf.Abs(n.y) + Mathf.Abs(n.z);
            if (l1 < 1e-8f) return new Vector2(0.5f, 0.5f);
            n /= l1;

            var e = new Vector2(n.x, n.y);
            if (n.z < 0f) e = OctWrap(e);
            return e * 0.5f + new Vector2(0.5f, 0.5f);
        }

        /// <summary>[0,1]^2 → 单位向量。</summary>
        public static Vector3 OctDecode(Vector2 f)
        {
            f = f * 2f - Vector2.one;
            var n = new Vector3(f.x, f.y, 1f - Mathf.Abs(f.x) - Mathf.Abs(f.y));
            float t = Mathf.Clamp01(-n.z);
            n.x += n.x >= 0f ? -t : t;
            n.y += n.y >= 0f ? -t : t;
            return n.normalized;
        }

        // ═══════════════════════════════════════════════════════════════
        //  对象空间 ⇄ 切线空间
        // ═══════════════════════════════════════════════════════════════
        // 与 .hlsl 的 OSN_TangentToObject 逐行对应：同一套 Gram-Schmidt、
        // 同一个退化阈值、同样用 tangent.w 作手性。两边只要有一处不一致，
        // 往返就不闭合，症状是描边整体偏斜而没有任何报错。
        //
        // ⚠ 调用方必须传入网格上【真实的】mesh.normals / mesh.tangents，
        //   不能另算一份 —— 运行时着色器用的就是这两个数组（蒙皮后的版本），
        //   只有同源才能保证 编码 → 解码 恒等。这也意味着模型若以不同的切线
        //   生成方式重新导入，已烘的数据会静默失配，需重新烘焙。

        /// <summary>构造该顶点的正交切线基。切线退化时返回 false。</summary>
        private static bool TryBuildBasis(Vector3 normal, Vector4 tangent,
                                          out Vector3 t, out Vector3 b, out Vector3 n)
        {
            n = normal.normalized;
            var raw = new Vector3(tangent.x, tangent.y, tangent.z);
            t = raw - n * Vector3.Dot(n, raw);              // Gram-Schmidt
            if (t.magnitude < 1e-5f)                        // 切线为零或与法线共线
            {
                t = Vector3.zero;
                b = Vector3.zero;
                return false;
            }
            t.Normalize();
            b = Vector3.Cross(n, t) * tangent.w;            // w 是手性，不可丢
            return true;
        }

        /// <summary>
        /// 对象空间 → 切线空间。基是正交归一的，故逆变换即转置 —— 三次点积。
        /// 切线退化时返回 (0,0,1)，即「就用顶点法线」，与解码侧的退化回退一致。
        /// </summary>
        public static Vector3 ObjectToTangent(Vector3 smoothNormalOS, Vector3 normal, Vector4 tangent)
        {
            if (!TryBuildBasis(normal, tangent, out var t, out var b, out var n))
                return new Vector3(0f, 0f, 1f);

            return new Vector3(
                Vector3.Dot(smoothNormalOS, t),
                Vector3.Dot(smoothNormalOS, b),
                Vector3.Dot(smoothNormalOS, n)).normalized;
        }

        /// <summary>
        /// 切线空间 → 对象空间。仅供编辑器预览的叠加层做 CPU 解码用；
        /// 实际描边走 .hlsl 里的 OSN_TangentToObject。
        /// </summary>
        public static Vector3 TangentToObject(Vector3 smoothNormalTS, Vector3 normal, Vector4 tangent)
        {
            if (!TryBuildBasis(normal, tangent, out var t, out var b, out var n))
                return n;

            return (smoothNormalTS.x * t + smoothNormalTS.y * b + smoothNormalTS.z * n).normalized;
        }

        // ═══════════════════════════════════════════════════════════════
        //  8-bit 通道打包
        // ═══════════════════════════════════════════════════════════════
        /// <summary>[0,1] → [0,255]。</summary>
        public static byte PackUNorm(float v) => (byte)Mathf.RoundToInt(Mathf.Clamp01(v) * 255f);

        /// <summary>[0,255] → [0,1]，与 GPU 对 Color32 的归一化一致。</summary>
        public static float UnpackUNorm(byte v) => v / 255f;
    }
}
