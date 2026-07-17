using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OutlineSmoothNormalsGenerator.Demo
{
    /// <summary>
    /// 演示资产生成器（开发工具，不随插件发布）。
    ///
    /// 为每个渲染管线各生成一套【自包含】的验证台，目录结构与最终 Sample 一致：
    ///
    ///   Assets/Demo/URP/       ← 拷贝为 Samples~/URP
    ///   Assets/Demo/BuiltIn/   ← 拷贝为 Samples~/BuiltIn
    ///     ├── Outline.shader   （手写，本脚本不生成）
    ///     ├── Meshes/          （本脚本生成）
    ///     ├── Materials/       （本脚本生成）
    ///     └── OutlineDemo.unity（本脚本生成）
    ///
    /// 每套场景里三个立方体并排，相机对准它们的 −Z 角：
    ///   DemoCube_Smooth  已烘焙平滑法线（黑描边）   → 描边应当闭合
    ///   DemoCube_Raw     走 VertexNormal（红描边）  → 硬边处描边应当断裂
    ///   DemoCube_Jitter  接缝带位置抖动（蓝描边）   → 用于验证合并容差
    ///
    /// 网格来源是 Unity 内置 Cube：24 顶点、逐面拆分法线，每个角上有 3 个位置重合
    /// 但法线不同的顶点 —— 正是描边断裂的现场，也是「重建 Z + 符号修正」这种
    /// 半球压缩方案必然失效的地方。
    /// 它同时也是「内置不可变网格」的样本，用来验证保存路径。
    ///
    /// 拷贝进 Samples~ 的完整流程见 Assets/Demo/README.md。
    /// </summary>
    public static class DemoBuilder
    {
        private const string DemoRoot = "Assets/Demo";

        // 两个管线的 Shader 必须【不同名】：它们要同时存在于本项目里以便编辑，
        // 同名会造成 Shader 重定义，Unity 会任选其一并告警。
        private const string UrpShaderName     = "OutlineSmoothNormalsGenerator/Outline URP";
        private const string BuiltInShaderName = "OutlineSmoothNormalsGenerator/Outline Built-in";

        // 立方体间距，让对照物体不重叠。
        private const float Spacing = 1.6f;

        /// <summary>
        /// 抖动立方体的位置扰动幅度。取 1e-5：
        ///   - 默认容差 1e-4 → 量化到同一格 → 合并成功 → 描边闭合。
        ///   - 容差调到下限 1e-6 → 落入不同格 → 合并失败 → 接缝重新开裂。
        /// 这是唯一能证明「合并容差」真的在起作用的场景 —— 内置立方体的顶点
        /// 位置是精确的 ±0.5，重合点完全相同，任何容差都能合并，验证不了。
        /// </summary>
        private const float SeamJitter = 0.00001f;

        // ─────────────────────────────────────────────────────────────
        [MenuItem("Tools/Outline Demo/Build URP Demo")]
        public static void BuildUrp() => Build("URP", UrpShaderName);

        [MenuItem("Tools/Outline Demo/Build Built-in Demo")]
        public static void BuildBuiltIn() => Build("BuiltIn", BuiltInShaderName);

        [MenuItem("Tools/Outline Demo/Build Both")]
        public static void BuildBoth()
        {
            BuildUrp();
            BuildBuiltIn();
        }

        /// <summary>
        /// 用默认容差重新烘焙两套 Demo 里已烘焙的网格。改完存储格式后用它重跑，
        /// 不必重建整个场景。
        ///
        /// 注意：要验证【合并容差】请改用工具窗口（Tools > Smooth Normal Generator），
        /// 选中 Cube_Jitter 后调节容差再生成 —— 这里走的是默认容差。
        /// </summary>
        [MenuItem("Tools/Outline Demo/Re-bake Meshes")]
        public static void RebakeAll()
        {
            foreach (var variant in new[] { "URP", "BuiltIn" })
            foreach (var assetName in new[] { "DemoCube_Smooth", "DemoCube_Jitter" })
            {
                string path = $"{DemoRoot}/{variant}/Meshes/{assetName}.asset";
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (!mesh)
                {
                    Debug.LogWarning($"[DemoBuilder] 找不到 {path}，跳过。请先执行 Build。");
                    continue;
                }
                BakeSmoothNormals(mesh);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[DemoBuilder] 已用默认容差重新烘焙两套 Demo 网格。");
        }

        // ─────────────────────────────────────────────────────────────
        /// <param name="variant">子目录名，同时也是 Sample 的目录名（URP / BuiltIn）。</param>
        /// <param name="shaderName">该管线对应的描边 Shader 名。</param>
        private static void Build(string variant, string shaderName)
        {
            string root    = $"{DemoRoot}/{variant}";
            string meshDir = $"{root}/Meshes";
            string matDir  = $"{root}/Materials";
            string scene   = $"{root}/OutlineDemo.unity";

            var shader = Shader.Find(shaderName);
            if (!shader)
            {
                Debug.LogError($"[DemoBuilder] 找不到 Shader「{shaderName}」，" +
                               $"请确认 {root}/Outline.shader 存在且已编译通过。");
                return;
            }

            EnsureDir(meshDir);
            EnsureDir(matDir);

            var smoothMesh = CreateCubeMeshAsset(meshDir, "DemoCube_Smooth");
            var rawMesh    = CreateCubeMeshAsset(meshDir, "DemoCube_Raw");
            var jitterMesh = CreateCubeMeshAsset(meshDir, "DemoCube_Jitter", SeamJitter);
            if (!smoothMesh || !rawMesh || !jitterMesh) return;

            // Raw 保持原样作为对照，另两份都烘焙。
            BakeSmoothNormals(smoothMesh);
            BakeSmoothNormals(jitterMesh);

            var smoothMat = CreateOutlineMaterial(matDir, shader, "M_Outline_Smooth",
                                                  Color.black, useSmoothNormals: true);
            var rawMat    = CreateOutlineMaterial(matDir, shader, "M_Outline_Raw",
                                                  new Color(1f, 0.25f, 0.2f), useSmoothNormals: false);
            var jitterMat = CreateOutlineMaterial(matDir, shader, "M_Outline_Jitter",
                                                  new Color(0.2f, 0.45f, 1f), useSmoothNormals: true);

            BuildScene(scene, smoothMesh, rawMesh, jitterMesh, smoothMat, rawMat, jitterMat);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[DemoBuilder] {variant} 演示资产已生成 → {scene}\n" +
                      $"  Shader: {shaderName}\n" +
                      $"  Smooth: 已烘焙 顶点色 BA\n" +
                      $"  Raw   : 未烘焙，走 VertexNormal 对照\n" +
                      $"  Jitter: 接缝抖动 ±{SeamJitter:G}，用于验证合并容差");
        }

        // ─────────────────────────────────────────────────────────────
        /// <param name="jitter">
        /// 位置扰动幅度（世界单位）。大于 0 时给每个顶点施加随机偏移，
        /// 模拟 DCC 导出 / FBX 浮点截断造成的接缝顶点微小差异 —— 真实模型里
        /// 重合顶点极少是逐位相同的，这正是合并容差存在的理由。
        /// </param>
        private static Mesh CreateCubeMeshAsset(string meshDir, string assetName, float jitter = 0f)
        {
            // 直接取内置网格，不经由 CreatePrimitive —— 后者会把物体塞进当前打开的场景并弄脏它。
            var builtIn = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            if (!builtIn)
            {
                Debug.LogError("[DemoBuilder] 取不到内置 Cube.fbx 网格。");
                return null;
            }

            var copy = Object.Instantiate(builtIn);
            copy.name = assetName;

            if (jitter > 0f)
            {
                // 固定种子，保证每次生成的抖动完全一致、结果可复现。
                var rng   = new System.Random(20260717);
                var verts = copy.vertices;
                for (int i = 0; i < verts.Length; i++)
                {
                    verts[i] += new Vector3(
                        (float)(rng.NextDouble() * 2.0 - 1.0) * jitter,
                        (float)(rng.NextDouble() * 2.0 - 1.0) * jitter,
                        (float)(rng.NextDouble() * 2.0 - 1.0) * jitter);
                }
                copy.vertices = verts;
            }

            string path = $"{meshDir}/{assetName}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(copy, path);
            return copy;
        }

        /// <summary>调用插件本体的计算 + 写入，走的是与工具窗口完全相同的代码路径。</summary>
        private static void BakeSmoothNormals(Mesh mesh)
        {
            var smoothNormals = OutlineSmoothNormalsCalculator.Calculate(mesh);
            if (smoothNormals == null) return;   // 具体原因已由 Calculate 打印

            StorageWriter.WriteToVertexColor(
                mesh, smoothNormals,
                OutlineSmoothNormalsGeneratorWindow.VertexColorChannel.Ba);
            EditorUtility.SetDirty(mesh);
        }

        /// <param name="useSmoothNormals">
        /// true  → 读顶点色 BA 里烘焙的平滑法线（工具生效的一组）。
        /// false → 走 VertexNormal 模式用原始顶点法线，即「没用本工具」的对照组。
        /// </param>
        private static Material CreateOutlineMaterial(string matDir, Shader shader, string assetName,
                                                      Color outlineColor, bool useSmoothNormals)
        {
            var mat = new Material(shader) { name = assetName };
            if (mat.HasProperty("_OutlineColor")) mat.SetColor("_OutlineColor", outlineColor);
            if (mat.HasProperty("_OutlineWidth")) mat.SetFloat("_OutlineWidth", 0.03f);
            if (mat.HasProperty("_BaseColor"))    mat.SetColor("_BaseColor", new Color(0.82f, 0.82f, 0.85f));

            // new Material() 不会自动启用 KeywordEnum 的默认关键字 —— 不显式设置的话
            // shader 会落到 #else 分支，两个立方体就长得一模一样，对照失效。
            // _SmoothNormalSrc 的取值必须与 Shader 的 KeywordEnum 顺序一致：
            // 0=VertexColor 1=TangentSpace 2..5=TexCoord0..3 6=VertexNormal
            if (useSmoothNormals)
            {
                mat.SetFloat("_SmoothNormalSrc", 0f);              // VertexColor
                mat.EnableKeyword("_SMOOTHNORMALSRC_VERTEXCOLOR");
                mat.SetFloat("_VCChannel", 2f);                    // 2 = BA，与烘焙时一致
            }
            else
            {
                mat.SetFloat("_SmoothNormalSrc", 6f);              // VertexNormal
                mat.EnableKeyword("_SMOOTHNORMALSRC_VERTEXNORMAL");
            }

            string path = $"{matDir}/{assetName}.mat";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        // ─────────────────────────────────────────────────────────────
        /// <summary>
        /// 以 Additive 方式新建场景并填充，避免动到你当前打开的场景。
        /// </summary>
        private static void BuildScene(string scenePath, Mesh smoothMesh, Mesh rawMesh, Mesh jitterMesh,
                                       Material smoothMat, Material rawMat, Material jitterMat)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

            var camGo = new GameObject("Main Camera", typeof(Camera));
            camGo.tag = "MainCamera";
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags      = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.53f, 0.81f, 0.98f);
            cam.fieldOfView     = 35f;
            // 对准各立方体的 (+X, +Y, −Z) 角：该角平滑法线的 Z 为负，正是旧的
            // 「重建 Z + 符号修正」方案必然失效的位置。距离按三个立方体的跨度取。
            camGo.transform.position = new Vector3(3.4f, 2.8f, -4.8f);
            camGo.transform.LookAt(new Vector3(0f, 0f, -0.1f));

            var lightGo = new GameObject("Directional Light", typeof(Light));
            var light = lightGo.GetComponent<Light>();
            light.type      = LightType.Directional;
            light.intensity = 1.1f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var smoothGo = CreateCubeObject("Cube_Smooth (已烘焙)", smoothMesh, smoothMat,
                                            new Vector3(-Spacing, 0f, 0f));
            var rawGo = CreateCubeObject("Cube_Raw (原始法线对照)", rawMesh, rawMat,
                                         new Vector3(0f, 0f, 0f));
            var jitterGo = CreateCubeObject("Cube_Jitter (接缝抖动 · 验证合并容差)", jitterMesh, jitterMat,
                                            new Vector3(Spacing, 0f, 0f));

            foreach (var go in new[] { camGo, lightGo, smoothGo, rawGo, jitterGo })
                SceneManager.MoveGameObjectToScene(go, scene);

            EditorSceneManager.SaveScene(scene, scenePath);
            EditorSceneManager.CloseScene(scene, true);
        }

        private static GameObject CreateCubeObject(string goName, Mesh mesh, Material mat, Vector3 pos)
        {
            var go = new GameObject(goName, typeof(MeshFilter), typeof(MeshRenderer));
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            go.transform.position = pos;
            return go;
        }

        private static void EnsureDir(string dir)
        {
            if (Directory.Exists(dir)) return;
            Directory.CreateDirectory(dir);
            AssetDatabase.Refresh();
        }
    }
}
