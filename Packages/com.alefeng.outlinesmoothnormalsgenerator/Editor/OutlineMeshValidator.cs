using System.Collections.Generic;
using System.Text;
using UnityEngine;
using StorageMode = OutlineSmoothNormalsGenerator.OutlineSmoothNormalsGeneratorWindow.StorageMode;
using NormalSpace = OutlineSmoothNormalsGenerator.OutlineSmoothNormalsGeneratorWindow.NormalSpace;

namespace OutlineSmoothNormalsGenerator
{
    /// <summary>问题的严重级别。</summary>
    public enum HealthSeverity { Info, Warning, Error }

    /// <summary>单条健康检查结果。</summary>
    public readonly struct HealthIssue
    {
        public readonly HealthSeverity Severity;
        public readonly string Message;

        public HealthIssue(HealthSeverity severity, string message)
        {
            Severity = severity;
            Message = message;
        }
    }

    /// <summary>一次网格健康检查的完整报告。</summary>
    public sealed class MeshHealthReport
    {
        public readonly List<HealthIssue> Issues = new List<HealthIssue>();

        public bool IsHealthy => Issues.Count == 0;

        public bool HasError
        {
            get
            {
                foreach (var i in Issues)
                    if (i.Severity == HealthSeverity.Error) return true;
                return false;
            }
        }

        public bool HasWarning
        {
            get
            {
                foreach (var i in Issues)
                    if (i.Severity == HealthSeverity.Warning) return true;
                return false;
            }
        }

        /// <summary>报告里出现过的最高严重级别（空报告视为 Info）。</summary>
        public HealthSeverity WorstSeverity
        {
            get
            {
                var worst = HealthSeverity.Info;
                foreach (var i in Issues)
                    if (i.Severity > worst) worst = i.Severity;
                return worst;
            }
        }

        public void Add(HealthSeverity severity, string message)
            => Issues.Add(new HealthIssue(severity, message));

        /// <summary>把所有问题拼成一行文本，便于写入 Console 日志。</summary>
        public string Summary()
        {
            if (IsHealthy) return LocValidator.NoIssues;

            var sb = new StringBuilder();
            for (int i = 0; i < Issues.Count; i++)
            {
                if (i > 0) sb.Append(LocValidator.IssueSeparator);
                sb.Append('[').Append(Tag(Issues[i].Severity)).Append("] ").Append(Issues[i].Message);
            }
            return sb.ToString();
        }

        public static string Tag(HealthSeverity s) => s switch
        {
            HealthSeverity.Error   => LocValidator.TagError,
            HealthSeverity.Warning => LocValidator.TagWarning,
            _                      => LocValidator.TagInfo,
        };
    }

    /// <summary>
    /// 生成 / 烘焙前的网格数据健康检查：统一扫描并汇报无法处理或会影响结果的非法数据。
    ///
    /// 判据刻意与 <see cref="OutlineSmoothNormalsCalculator"/> 对齐（同款 sin(θ) 退化判定、
    /// 同款位置量化），使「检查说没问题」与「计算实际能用」保持一致，避免两套标准打架。
    ///
    /// 说明：本工具的存储不依赖 UV 展开，因此【不】检查「三角内 UV 重叠」——那是烘焙
    /// 光照/法线贴图才关心的问题，在这里检查只会制造与本用途无关的误报。
    /// </summary>
    public static class OutlineMeshValidator
    {
        // 单个位置重合顶点数超过此值时预警：正常硬边角点通常只有个位数重合，
        // 数量异常大往往意味着网格本身有问题，或合并容差设得离谱。
        private const int CoincidentWarnThreshold = 64;

        // intendedSpace 的默认值与工具窗口、导入自动烘焙、StorageWriter 保持一致，
        // 都是切线空间 —— 全项目只有一个默认，省得各处对不上。
        public static MeshHealthReport Validate(Mesh mesh, StorageMode intendedMode,
                                                NormalSpace intendedSpace = NormalSpace.Tangent)
        {
            var report = new MeshHealthReport();

            if (mesh == null)
            {
                report.Add(HealthSeverity.Error, LocValidator.MeshNull);
                return report;
            }

            int vCount = mesh.vertexCount;
            if (vCount == 0)
            {
                report.Add(HealthSeverity.Error, LocValidator.MeshNoVertices(mesh.name));
                return report;
            }

            if (!mesh.isReadable)
            {
                report.Add(HealthSeverity.Warning, LocValidator.NotReadable);
            }

            // 顶点：非 Read/Write 的网格在运行期取不到顶点数组，此时跳过依赖顶点的检查。
            var vertices = mesh.vertices;
            bool vertsOk = vertices != null && vertices.Length == vCount;
            if (vertsOk)
            {
                int nanVerts = CountNonFinite(vertices);
                if (nanVerts > 0)
                    report.Add(HealthSeverity.Error, LocValidator.NonFiniteVertices(nanVerts));
            }

            // 法线：缺失只是警告（生成时会自动重算），但零向量 / NaN 会污染结果。
            var normals = mesh.normals;
            bool normalsOk = normals != null && normals.Length == vCount;
            if (!normalsOk)
            {
                report.Add(HealthSeverity.Warning, LocValidator.NoNormals);
            }
            else
            {
                int badNormals = CountBadNormals(normals);
                if (badNormals > 0)
                    report.Add(HealthSeverity.Warning, LocValidator.BadNormals(badNormals));
            }

            // ── 切线 ──────────────────────────────────────────────────
            // 两种用途，判据截然不同，不要混为一谈：
            //   · 存进切线【通道】（StorageMode.TangentSpace）—— 写入时会整个新建
            //     切线数组，原本缺不缺无所谓，仅作提示。
            //   · 存切线【空间】坐标（NormalSpace.Tangent）—— 切线是重建正交基的
            //     必需输入，缺失就根本编解不了码，直接报 Error。
            var tangents = mesh.tangents;
            bool tangentsOk = tangents != null && tangents.Length == vCount;

            if (intendedMode == StorageMode.TangentSpace && !tangentsOk)
                report.Add(HealthSeverity.Info, LocValidator.NoTangentsInfo);

            // 切线通道存储恒为对象空间（存进去的切线就是数据本身，没有基可言），
            // 故不参与本项检查。
            if (intendedSpace == NormalSpace.Tangent && intendedMode != StorageMode.TangentSpace)
            {
                if (!normalsOk)
                    report.Add(HealthSeverity.Error, LocValidator.TangentSpaceNeedsNormals);

                if (!tangentsOk)
                {
                    report.Add(HealthSeverity.Error, LocValidator.TangentSpaceNeedsTangents);
                }
                else if (normalsOk)
                {
                    int badTangents = CountDegenerateTangents(normals, tangents);
                    if (badTangents == vCount)
                        report.Add(HealthSeverity.Error,
                            LocValidator.AllTangentsDegenerate(vCount));
                    else if (badTangents > 0)
                        report.Add(HealthSeverity.Warning,
                            LocValidator.SomeTangentsDegenerate(badTangents, vCount));
                }
            }

            if (vertsOk)
                AnalyzeTopology(mesh, vertices, report);

            return report;
        }

        // ─────────────────────────────────────────────────────────────
        /// <summary>退化三角统计 + 单个位置重合顶点数统计。</summary>
        private static void AnalyzeTopology(Mesh mesh, Vector3[] vertices, MeshHealthReport report)
        {
            int[] triangles;
            try
            {
                triangles = mesh.triangles;
            }
            catch
            {
                return; // 非三角拓扑（线 / 点）：无三角可查，直接跳过
            }

            if (triangles != null && triangles.Length >= 3)
            {
                int degenerate = 0;
                int triCount = triangles.Length / 3;

                for (int i = 0; i < triangles.Length; i += 3)
                {
                    Vector3 p0 = vertices[triangles[i]];
                    Vector3 p1 = vertices[triangles[i + 1]];
                    Vector3 p2 = vertices[triangles[i + 2]];

                    Vector3 e1 = p1 - p0;
                    Vector3 e2 = p2 - p0;

                    // 与计算器一致：先看边长是否退化，再看相对 sin(θ) 是否过小（几乎共线）。
                    float denom = e1.magnitude * e2.magnitude;
                    if (denom < 1e-20f) { degenerate++; continue; }
                    if (Vector3.Cross(e1, e2).magnitude / denom < 1e-6f) degenerate++;
                }

                if (degenerate > 0)
                    report.Add(HealthSeverity.Warning,
                        LocValidator.DegenerateTriangles(degenerate, triCount));
            }

            int maxGroup = MaxCoincidentGroup(vertices);
            if (maxGroup > CoincidentWarnThreshold)
                report.Add(HealthSeverity.Warning, LocValidator.TooManyCoincident(maxGroup));
        }

        // ─────────────────────────────────────────────────────────────
        private static int CountNonFinite(Vector3[] points)
        {
            int n = 0;
            foreach (var p in points)
                if (!IsFinite(p)) n++;
            return n;
        }

        private static int CountBadNormals(Vector3[] normals)
        {
            int n = 0;
            foreach (var v in normals)
                if (!IsFinite(v) || v.sqrMagnitude < 1e-8f) n++;
            return n;
        }

        /// <summary>
        /// 统计无法构成正交基的切线数。判据与 <see cref="OutlineSmoothNormalsCodec"/>
        /// 的基构造（以及 .hlsl 的 OSN_TangentToObject）一致：Gram-Schmidt 去掉法线
        /// 分量后残量趋零即为退化。
        ///
        /// 额外查 tangent.w：手性为 0（或 NaN）会让副切线塌成零向量，编解码两侧都
        /// 没有单独防这一项 —— 保持两边逐行一致比各自打补丁更重要，统一由这里报出。
        /// </summary>
        private static int CountDegenerateTangents(Vector3[] normals, Vector4[] tangents)
        {
            int n = 0;
            for (int i = 0; i < tangents.Length; i++)
            {
                var normal = normals[i];
                if (!IsFinite(normal) || normal.sqrMagnitude < 1e-8f) { n++; continue; }

                var t4 = tangents[i];
                var raw = new Vector3(t4.x, t4.y, t4.z);
                // 写成 >= 而非 < ，NaN 的手性才会一并落进退化分支。
                if (!IsFinite(raw) || !(Mathf.Abs(t4.w) >= 0.5f)) { n++; continue; }

                normal = normal.normalized;
                var t = raw - normal * Vector3.Dot(normal, raw);
                if (t.magnitude < 1e-5f) n++;
            }
            return n;
        }

        /// <summary>用与计算器相同的量化方式统计「同一位置」上重合的顶点数，返回最大组的大小。</summary>
        private static int MaxCoincidentGroup(Vector3[] vertices)
        {
            float invTol = 1f / OutlineSmoothNormalsCalculator.DefaultMergeTolerance;
            var groups = new Dictionary<Vector3Int, int>(vertices.Length);
            int max = 0;

            foreach (var p in vertices)
            {
                if (!IsFinite(p)) continue;
                var key = new Vector3Int(
                    Mathf.RoundToInt(p.x * invTol),
                    Mathf.RoundToInt(p.y * invTol),
                    Mathf.RoundToInt(p.z * invTol));

                groups.TryGetValue(key, out int c);
                c++;
                groups[key] = c;
                if (c > max) max = c;
            }

            return max;
        }

        private static bool IsFinite(Vector3 v)
            => !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
                 float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
    }
}
