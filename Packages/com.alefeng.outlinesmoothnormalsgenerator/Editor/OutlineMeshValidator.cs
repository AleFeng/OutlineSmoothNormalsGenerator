using System.Collections.Generic;
using System.Text;
using UnityEngine;
using StorageMode = OutlineSmoothNormalsGenerator.OutlineSmoothNormalsGeneratorWindow.StorageMode;

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
            if (IsHealthy) return "无异常";

            var sb = new StringBuilder();
            for (int i = 0; i < Issues.Count; i++)
            {
                if (i > 0) sb.Append("；");
                sb.Append('[').Append(Tag(Issues[i].Severity)).Append("] ").Append(Issues[i].Message);
            }
            return sb.ToString();
        }

        public static string Tag(HealthSeverity s) => s switch
        {
            HealthSeverity.Error   => "错误",
            HealthSeverity.Warning => "警告",
            _                      => "提示",
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

        public static MeshHealthReport Validate(Mesh mesh, StorageMode intendedMode)
        {
            var report = new MeshHealthReport();

            if (mesh == null)
            {
                report.Add(HealthSeverity.Error, "网格为空（null）。");
                return report;
            }

            int vCount = mesh.vertexCount;
            if (vCount == 0)
            {
                report.Add(HealthSeverity.Error, $"网格「{mesh.name}」没有顶点。");
                return report;
            }

            if (!mesh.isReadable)
            {
                report.Add(HealthSeverity.Warning,
                    "网格未开启 Read/Write：编辑器内通常仍可处理，但缺失数据时无法自动重算，" +
                    "建议在模型导入设置中开启。");
            }

            // 顶点：非 Read/Write 的网格在运行期取不到顶点数组，此时跳过依赖顶点的检查。
            var vertices = mesh.vertices;
            bool vertsOk = vertices != null && vertices.Length == vCount;
            if (vertsOk)
            {
                int nanVerts = CountNonFinite(vertices);
                if (nanVerts > 0)
                    report.Add(HealthSeverity.Error, $"有 {nanVerts} 个顶点坐标为 NaN / Inf。");
            }

            // 法线：缺失只是警告（生成时会自动重算），但零向量 / NaN 会污染结果。
            var normals = mesh.normals;
            if (normals == null || normals.Length != vCount)
            {
                report.Add(HealthSeverity.Warning,
                    "缺少顶点法线：生成时会自动重算，结果可能不如导入法线精确。");
            }
            else
            {
                int badNormals = CountBadNormals(normals);
                if (badNormals > 0)
                    report.Add(HealthSeverity.Warning, $"有 {badNormals} 条顶点法线为零向量或 NaN。");
            }

            // 切线：只有「切线空间」存储会用到；缺失只是提示（写入时会新建）。
            if (intendedMode == StorageMode.TangentSpace)
            {
                var tangents = mesh.tangents;
                if (tangents == null || tangents.Length != vCount)
                    report.Add(HealthSeverity.Info, "网格无切线：切线空间存储会新建切线数据。");
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
                        $"有 {degenerate}/{triCount} 个退化（零面积 / 共线）三角，这些面不参与平滑法线计算。");
            }

            int maxGroup = MaxCoincidentGroup(vertices);
            if (maxGroup > CoincidentWarnThreshold)
                report.Add(HealthSeverity.Warning,
                    $"存在单个位置重合多达 {maxGroup} 个顶点，可能是网格异常或合并容差设置不当。");
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
