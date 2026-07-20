using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using StorageMode = OutlineSmoothNormalsGenerator.OutlineSmoothNormalsGeneratorWindow.StorageMode;
using NormalSpace = OutlineSmoothNormalsGenerator.OutlineSmoothNormalsGeneratorWindow.NormalSpace;

namespace OutlineSmoothNormalsGenerator
{
    /// <summary>
    /// 导入时自动烘焙：命中规则的模型在（重）导入时，自动把平滑法线烘焙进网格。
    ///
    /// 【非破坏性】写入发生在 <see cref="OnPostprocessModel"/> 内、针对正在导入的网格，
    /// 结果随本次导入产物一并落盘；去掉后缀或关闭开关后重新导入即恢复原始网格。
    /// 这也天然避免了死循环：写入是本次导入的一部分，不会再触发一次重导入。
    ///
    /// 【复用】计算与写入直接调用 <see cref="OutlineSmoothNormalsCalculator"/> 与
    /// <see cref="StorageWriter"/>，与手动窗口、生产描边 shader 的编码完全一致。
    ///
    /// 【可扩展】两个 static 委托允许团队接私有管线，空则回退默认；
    /// 一般用 <c>[InitializeOnLoadMethod]</c> 在加载时赋值一次。
    /// </summary>
    public class OutlineNormalsImportProcessor : AssetPostprocessor
    {
        // ═══════════════════════════════════════════════════════════════
        //  扩展钩子（空则走默认）
        // ═══════════════════════════════════════════════════════════════
        /// <summary>
        /// 自定义命中规则：给定资产路径与其 <see cref="ModelImporter"/>，返回是否烘焙。
        /// 赋值后完全取代「文件名后缀」默认规则（后缀配置随之失效）。
        /// </summary>
        public static Func<string, ModelImporter, bool> ShouldBakeRule;

        /// <summary>
        /// 自定义存储写法：拿到网格与算好的对象空间平滑法线，自行决定写到哪、怎么编码。
        /// 赋值后完全取代「配置里选的内置三种存储」——「存储空间」配置随之失效，
        /// 传入的恒为对象空间方向，需要切线空间请自行调用
        /// <see cref="OutlineSmoothNormalsCodec.ObjectToTangent"/>。
        /// </summary>
        public static Action<Mesh, Vector3[]> CustomStorageWriter;

        // 改这里的返回值可让 Unity 视为导入逻辑变更、强制重新导入并重烘所有命中模型。
        // 2：新增「存储空间」，且默认值为切线空间 —— 已烘模型的数据语义随之改变，
        //    必须重烘，否则会拿旧的对象空间数据去按切线空间解，描边整体偏斜。
        public override uint GetVersion() => 2;

        // ═══════════════════════════════════════════════════════════════
        //  导入回调
        // ═══════════════════════════════════════════════════════════════
        private void OnPostprocessModel(GameObject root)
        {
            var settings = OutlineNormalsSettings.instance;
            if (!settings.AutoBakeEnabled) return;

            var importer = assetImporter as ModelImporter;

            bool shouldBake = ShouldBakeRule != null
                ? ShouldBakeRule(assetPath, importer)
                : DefaultRule(assetPath, settings.FilenameSuffix);
            if (!shouldBake) return;

            var meshes = CollectMeshes(root);
            if (meshes.Count == 0) return;

            int baked = 0, skipped = 0;
            foreach (var mesh in meshes)
            {
                // 先体检：只有确实无法处理（Error）才跳过；告警照常烘焙，随汇总日志提示。
                var report = OutlineMeshValidator.Validate(mesh, settings.StorageMode, settings.EffectiveNormalSpace);
                if (report.HasError)
                {
                    skipped++;
                    Debug.LogWarning($"[OutlineSmoothNormals] 跳过网格「{mesh.name}」（{assetPath}）：{report.Summary()}");
                    continue;
                }

                var smoothNormals = OutlineSmoothNormalsCalculator.Calculate(mesh, settings.MergeTolerance);
                if (smoothNormals == null)
                {
                    skipped++;
                    Debug.LogWarning($"[OutlineSmoothNormals] 跳过网格「{mesh.name}」（{assetPath}）：" +
                                     "平滑法线计算失败（缺法线且无法重算）。");
                    continue;
                }

                if (CustomStorageWriter != null)
                    CustomStorageWriter(mesh, smoothNormals);
                else
                    WriteByMode(mesh, smoothNormals, settings);

                baked++;

                if (report.HasWarning)
                    Debug.LogWarning($"[OutlineSmoothNormals] 网格「{mesh.name}」已烘焙，但有告警：{report.Summary()}");
            }

            if (baked > 0)
            {
                string target = CustomStorageWriter != null ? "自定义存储" : DescribeTarget(settings);
                Debug.Log($"[OutlineSmoothNormals] 自动烘焙 {assetPath}：{baked} 个网格 → {target}" +
                          (skipped > 0 ? $"（跳过 {skipped} 个）" : "。"));
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  默认命中规则：文件名（不含扩展名）以配置后缀结尾，大小写不敏感
        // ═══════════════════════════════════════════════════════════════
        private static bool DefaultRule(string assetPath, string suffix)
            => MatchesSuffix(assetPath, suffix);

        private static bool MatchesSuffix(string assetPath, string suffix)
        {
            if (string.IsNullOrEmpty(suffix)) return false;
            string name = Path.GetFileNameWithoutExtension(assetPath);
            return !string.IsNullOrEmpty(name) &&
                   name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }

        // ═══════════════════════════════════════════════════════════════
        //  网格收集 / 写入
        // ═══════════════════════════════════════════════════════════════
        private static List<Mesh> CollectMeshes(GameObject root)
        {
            var seen = new HashSet<Mesh>();
            var list = new List<Mesh>();

            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                AddMesh(mf.sharedMesh, seen, list);
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                AddMesh(smr.sharedMesh, seen, list);

            return list;
        }

        private static void AddMesh(Mesh mesh, HashSet<Mesh> seen, List<Mesh> list)
        {
            if (mesh != null && seen.Add(mesh)) list.Add(mesh);
        }

        private static void WriteByMode(Mesh mesh, Vector3[] smoothNormals, OutlineNormalsSettings s)
        {
            switch (s.StorageMode)
            {
                case StorageMode.VertexColor:
                    StorageWriter.WriteToVertexColor(mesh, smoothNormals, s.VcChannel, s.EffectiveNormalSpace);
                    break;
                case StorageMode.TangentSpace:
                    StorageWriter.WriteToTangent(mesh, smoothNormals);
                    break;
                case StorageMode.UV:
                    StorageWriter.WriteToUV(mesh, smoothNormals, s.UvChannel, s.EffectiveNormalSpace);
                    break;
            }
        }

        // 存储空间一并写进汇总日志：它决定材质该怎么解，排查描边偏斜时是第一条要确认的信息。
        // 用 EffectiveNormalSpace 而非 NormalSpace —— 日志要记的是实际生效的值。
        private static string DescribeTarget(OutlineNormalsSettings s)
        {
            string channel = s.StorageMode switch
            {
                StorageMode.VertexColor  => $"顶点色 {s.VcChannel}",
                StorageMode.TangentSpace => "切线通道",
                StorageMode.UV           => $"TEXCOORD{s.UvChannel}",
                _                        => s.StorageMode.ToString(),
            };
            string space = s.EffectiveNormalSpace == NormalSpace.Tangent ? "切线空间" : "对象空间";
            return $"{channel}（{space}）";
        }

        // ═══════════════════════════════════════════════════════════════
        //  重命名 / 移动侦测（可选）
        //
        //  把已有模型改名成命中后缀（或移动进来）不会触发模型重导入，OnPostprocessModel
        //  因此不会执行。这里补一手：侦测到「改名后命中、改名前不命中」的模型就强制重导，
        //  从而触发烘焙。自定义规则下后缀语义失效，跳过本侦测。
        // ═══════════════════════════════════════════════════════════════
        private static void OnPostprocessAllAssets(
            string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths)
        {
            var settings = OutlineNormalsSettings.instance;
            if (!settings.AutoBakeEnabled || string.IsNullOrEmpty(settings.FilenameSuffix)) return;
            if (ShouldBakeRule != null) return;

            List<string> toReimport = null;
            for (int i = 0; i < movedAssets.Length; i++)
            {
                string to = movedAssets[i];
                string from = movedFromAssetPaths[i];
                if (MatchesSuffix(to, settings.FilenameSuffix) &&
                    !MatchesSuffix(from, settings.FilenameSuffix) &&
                    AssetImporter.GetAtPath(to) is ModelImporter)
                {
                    (toReimport ??= new List<string>()).Add(to);
                }
            }

            if (toReimport == null) return;

            // 延迟到本次资源处理结束后再触发，避免在回调内重入导入管线。
            EditorApplication.delayCall += () =>
            {
                foreach (var path in toReimport)
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            };
        }
    }
}
