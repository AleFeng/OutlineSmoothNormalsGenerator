using System.Collections.Generic;
using UnityEngine;

namespace OutlineSmoothNormalsGenerator
{
    /// <summary>
    /// 平滑法线计算核心：把「位置相同」的顶点视为一组，将它们所属面的法线
    /// 按夹角加权平均后归一化，得到跨越硬边连续的外扩方向。
    /// </summary>
    public static class OutlineSmoothNormalsCalculator
    {
        /// <summary>默认合并容差（世界单位）。</summary>
        public const float DefaultMergeTolerance = 0.0001f;

        /// <summary>容差可调范围。下限接近精确匹配，上限足以吞掉明显的建模误差。</summary>
        public const float MinMergeTolerance = 0.000001f;
        public const float MaxMergeTolerance = 0.01f;

        /// <summary>
        /// 计算每个顶点的平滑法线（对象空间）。
        /// </summary>
        /// <param name="mesh"></param>
        /// <param name="mergeTolerance">
        /// 位置合并容差：距离在此范围内的顶点被视为同一点。
        /// 必须远小于模型的最小真实特征尺寸，否则会把本应分开的顶点错误合并。
        /// </param>
        public static Vector3[] Calculate(Mesh mesh, float mergeTolerance = DefaultMergeTolerance)
        {
            // 每次访问 mesh.vertices / normals / triangles 都会从原生层完整拷贝一份数组，
            // 因此在这里各取一次，后续全部传递引用。
            var vertices  = mesh.vertices;
            var triangles = mesh.triangles;
            var normals   = GetNormalsOrRecalculate(mesh, vertices.Length);
            if (normals == null) return null;

            float tol = Mathf.Clamp(mergeTolerance, MinMergeTolerance, MaxMergeTolerance);

            // 量化键每个顶点只算一次。此前 Quantize 在三角形循环里按顶点被调 3 次、
            // 在平均循环里再调 1 次，同一个顶点要重复算 4 遍。
            var keys = BuildQuantizedKeys(vertices, tol);

            var normalSumMap = CreateWeightedNormalSumMap(vertices, normals, triangles, keys);
            return CalculateAverageNormals(normalSumMap, keys, normals);
        }

        // ─────────────────────────────────────────────────────────────
        /// <summary>
        /// 取顶点法线。没有法线的网格无法计算面朝向，此时就地重算一次。
        ///
        /// 注意 mesh.normals 在「导入时未生成法线」的情况下返回的是【空数组】
        /// 而不是 null，直接按索引访问会抛 IndexOutOfRangeException。
        /// </summary>
        private static Vector3[] GetNormalsOrRecalculate(Mesh mesh, int vCount)
        {
            var normals = mesh.normals;
            if (normals != null && normals.Length == vCount) return normals;

            if (!mesh.isReadable)
            {
                Debug.LogError($"{LocLog.Prefix} {LocLog.NoNormalsNotReadable(mesh.name)}");
                return null;
            }

            Debug.LogWarning($"{LocLog.Prefix} {LocLog.NoNormalsRecalculated(mesh.name)}");
            mesh.RecalculateNormals();

            normals = mesh.normals;
            return (normals != null && normals.Length == vCount) ? normals : null;
        }

        /// <summary>
        /// 把位置量化成整数格坐标作为分组键。
        ///
        /// 这里刻意不用 Vector3 本身作键：Dictionary 会走 IEquatable&lt;Vector3&gt;，
        /// 那是逐分量的【精确】浮点相等（并非 Vector3 == 运算符的近似比较）。
        /// 而接缝顶点在 DCC 导出、FBX 浮点截断、缩放之后往往只差 1e-6 量级，
        /// 精确相等会把它们判为不同点、拒绝合并 —— 工具恰好在它最该起作用的
        /// 那些接缝上静默失效。
        ///
        /// 已知局限：格点取整仍会把恰好跨越格边界的两点分开。要完全正确需要
        /// 邻格探查或并查集。取整是标准做法，代价是容差必须远小于最小特征尺寸。
        /// </summary>
        private static Vector3Int Quantize(Vector3 p, float invTol)
        {
            return new Vector3Int(
                Mathf.RoundToInt(p.x * invTol),
                Mathf.RoundToInt(p.y * invTol),
                Mathf.RoundToInt(p.z * invTol));
        }

        /// <summary>每个顶点的分组键，预先算好供后续两轮遍历查表。</summary>
        private static Vector3Int[] BuildQuantizedKeys(Vector3[] vertices, float tol)
        {
            float invTol = 1f / tol;
            var keys = new Vector3Int[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
                keys[i] = Quantize(vertices[i], invTol);
            return keys;
        }

        /// <summary>
        /// 分组键的相等比较器。
        ///
        /// 存在的理由只有哈希质量：Unity 的 <c>Vector3Int.GetHashCode</c> 是
        /// <c>x ^ (y &lt;&lt; 4) ^ (z &gt;&gt; 4)</c> 之类的简单位运算，对随机整数尚可，
        /// 但本工具的键【恰好是规则格点】—— 网格顶点在空间里成行成列排布，
        /// 量化后相邻键只差 1，正是这类哈希冲突最密集的输入分布。冲突多了
        /// 字典就退化成链表扫描。这里改用空间哈希常用的大质数乘法配一轮
        /// 雪崩混合，让相邻键散得开。
        ///
        /// Equals 逐分量比较，与默认实现语义一致（Vector3Int 是精确整数相等）。
        /// </summary>
        private sealed class GridKeyComparer : IEqualityComparer<Vector3Int>
        {
            public static readonly GridKeyComparer Instance = new GridKeyComparer();

            public bool Equals(Vector3Int a, Vector3Int b)
                => a.x == b.x && a.y == b.y && a.z == b.z;

            public int GetHashCode(Vector3Int k)
            {
                unchecked
                {
                    uint h = (uint)k.x * 73856093u ^ (uint)k.y * 19349663u ^ (uint)k.z * 83492791u;
                    h ^= h >> 15;
                    h *= 2246822519u;
                    h ^= h >> 13;
                    return (int)h;
                }
            }
        }

        // ─────────────────────────────────────────────────────────────
        /// <summary>
        /// 遍历所有三角形，为每个顶点位置累加「角度加权后的面法线」。
        ///
        /// 这里直接累加成一个 Vector3，而不是先攒一个 List&lt;Vector3&gt; 再求和：
        /// 那些列表唯一的用途就是随后按插入顺序相加，保留它们等于给每一个
        /// 唯一顶点位置都分配一个 List 对象及其内部数组，还要承担扩容时的
        /// 重新分配 —— 十万顶点级别的网格上这是主要的 GC 压力来源。
        ///
        /// 就地累加的顺序与「按插入顺序求和」完全一致，因此结果【逐位相同】，
        /// 不存在浮点加法结合律带来的差异。
        /// </summary>
        private static Dictionary<Vector3Int, Vector3> CreateWeightedNormalSumMap(
            Vector3[] vertices, Vector3[] normals, int[] triangles, Vector3Int[] keys)
        {
            // 容量按顶点数预留：唯一位置数不会超过它，一次到位省掉全部 rehash。
            var map = new Dictionary<Vector3Int, Vector3>(keys.Length, GridKeyComparer.Instance);

            for (int i = 0; i < triangles.Length; i += 3)
            {
                int idx0 = triangles[i];
                int idx1 = triangles[i + 1];
                int idx2 = triangles[i + 2];

                Vector3 p0 = vertices[idx0];
                Vector3 p1 = vertices[idx1];
                Vector3 p2 = vertices[idx2];

                Vector3 edge1 = p1 - p0;
                Vector3 edge2 = p2 - p0;
                Vector3 faceNormal = Vector3.Cross(edge1, edge2);

                // 退化判定用【相对】比例，而非叉积的绝对长度：
                // |cross| = |e1||e2|sin(θ)，除掉边长后剩下 sin(θ)，与模型尺度无关。
                // 旧实现把边向量乘 1000「提升精度」，但叉积随后就会被归一化，
                // 缩放对方向毫无影响；它唯一的实际后果是把 sqrMagnitude 放大 1e12，
                // 使退化阈值 1e-10 实际变成 1e-22，让狭长三角形全部混过检查、
                // 向结果注入噪声。
                float denom = edge1.magnitude * edge2.magnitude;
                if (denom < 1e-20f) continue;
                if (faceNormal.magnitude / denom < 1e-6f) continue;  // sin(θ) 过小，几乎共线

                faceNormal.Normalize();

                // ── 朝向修正 ─────────────────────────────────────────
                // 叉积朝向由三角形绕序决定。背面三角形绕序相反，会让叉积与
                // 实际外表面法线反向。用三顶点原始法线的均值验证并修正。
                Vector3 avgVertNormal = normals[idx0] + normals[idx1] + normals[idx2];
                if (Vector3.Dot(faceNormal, avgVertNormal) < 0f)
                    faceNormal = -faceNormal;

                // 各顶点处的内角权重（弧度）：夹角越大，该面对此顶点的影响越大。
                float w0 = AngleRadians(p1 - p0, p2 - p0);
                float w1 = AngleRadians(p2 - p1, p0 - p1);
                float w2 = AngleRadians(p0 - p2, p1 - p2);

                AddToMap(map, keys[idx0], faceNormal * w0);
                AddToMap(map, keys[idx1], faceNormal * w1);
                AddToMap(map, keys[idx2], faceNormal * w2);
            }

            return map;
        }

        /// <summary>累加到 map，若 key 不存在则以该值建项。</summary>
        private static void AddToMap(Dictionary<Vector3Int, Vector3> map,
                                     Vector3Int key, Vector3 weightedNormal)
        {
            map[key] = map.TryGetValue(key, out var sum) ? sum + weightedNormal : weightedNormal;
        }

        // ─────────────────────────────────────────────────────────────
        /// <summary>
        /// 把每个位置累积好的加权法线归一化，写入对应顶点索引。
        /// </summary>
        private static Vector3[] CalculateAverageNormals(
            Dictionary<Vector3Int, Vector3> normalSumMap,
            Vector3Int[] keys, Vector3[] normals)
        {
            int vCount = keys.Length;
            var result = new Vector3[vCount];

            for (int i = 0; i < vCount; i++)
            {
                if (!normalSumMap.TryGetValue(keys[i], out var sum))
                {
                    result[i] = normals[i]; // 孤立顶点：退回原始法线
                    continue;
                }

                // 加权和可能相互抵消到零（例如退化的对折面），此时退回原始法线，
                // 否则 normalized 会得到零向量、让描边整个塌掉。
                result[i] = sum.sqrMagnitude > 1e-12f ? sum.normalized : normals[i];
            }

            return result;
        }

        // ─────────────────────────────────────────────────────────────
        /// <summary>两向量夹角（弧度），Clamp 防止浮点误差让 acos 溢出。</summary>
        private static float AngleRadians(Vector3 a, Vector3 b)
        {
            float sqA = a.sqrMagnitude;
            float sqB = b.sqrMagnitude;
            if (sqA < 1e-20f || sqB < 1e-20f) return 0f;
            float dot = Vector3.Dot(a, b) / Mathf.Sqrt(sqA * sqB);
            return Mathf.Acos(Mathf.Clamp(dot, -1f, 1f));
        }

        // ─────────────────────────────────────────────────────────────
        // 曾有一个 ConvertToTangentSpace(mesh, smoothNormals) 在此，用于把平滑
        // 法线转换到切线空间再存进 tangent.xyz。已删除，原因有三：
        //
        //   1. 它以 mesh.tangents 为基，而写入正是覆盖 tangent.xyz —— 第二次
        //      生成时读到的「基」其实是上一次写入的法线，静默损坏网格。
        //   2. 它与解码侧重建的基不一致（解码侧只能用顶点法线重建，因为原始
        //      切线已被覆盖、无从恢复），因此结果本就是错的。
        //   3. 若编解码统一改用同一组由顶点法线导出的正交基，该变换在数学上
        //      恒等于原向量：encode 后 decode 得回 sn 本身。纯属多余。
        //
        // 现在直接把对象空间平滑法线存进 tangent.xyz（见 StorageWriter）。
    }
}
