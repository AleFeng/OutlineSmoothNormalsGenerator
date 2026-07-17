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
    /// 平滑法线一律以【对象空间】存储，且始终是完整三维方向，不做半球压缩：
    ///   顶点色     选定通道对 (8-bit × 2) ← 八面体编码，全球面双射
    ///   切线       tangent.xyz (float × 3) ← 直接存，w 恒为 1
    ///   TEXCOORDn  uv.xyz      (float × 3) ← 直接存
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
        //  8-bit 通道打包
        // ═══════════════════════════════════════════════════════════════
        /// <summary>[0,1] → [0,255]。</summary>
        public static byte PackUNorm(float v) => (byte)Mathf.RoundToInt(Mathf.Clamp01(v) * 255f);

        /// <summary>[0,255] → [0,1]，与 GPU 对 Color32 的归一化一致。</summary>
        public static float UnpackUNorm(byte v) => v / 255f;
    }
}
