using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OutlineSmoothNormalsGenerator.Demo
{
    /// <summary>
    /// 开发期演示资产生成器（不随插件发布，Phase 8 会拆进 Samples~）。
    ///
    /// 生成一套可复现的描边验证台：
    ///   Assets/Demo/Meshes/DemoCube_Smooth.asset  ← 已烘焙平滑法线
    ///   Assets/Demo/Meshes/DemoCube_Raw.asset     ← 未烘焙，用于 A/B 对照
    ///   Assets/Demo/Materials/M_Outline_Smooth.mat
    ///   Assets/Demo/Materials/M_Outline_Raw.mat
    ///   Assets/Demo/OutlineDemo.unity             ← 相机对准 −Z 角
    ///
    /// 网格来源是 Unity 内置 Cube：24 顶点、逐面拆分法线，每个角上有 3 个位置重合
    /// 但法线不同的顶点 —— 正是描边断裂与 Z 符号重建失败的现场。
    /// 它同时也是「内置不可变网格」的样本，用来验证保存路径（C6）。
    /// </summary>
    public static class DemoBuilder
    {
        private const string DemoRoot  = "Assets/Demo";
        private const string MeshDir   = DemoRoot + "/Meshes";
        private const string MatDir    = DemoRoot + "/Materials";
        private const string ScenePath = DemoRoot + "/OutlineDemo.unity";

        private const string OutlineShaderName = "OutlineSmoothNormalsGenerator/Outline";

        // 立方体间距，让两个对照物体不重叠。
        private const float Spacing = 1.6f;

        [MenuItem("Tools/Outline Demo/Build Demo Assets")]
        public static void BuildAll()
        {
            EnsureDir(MeshDir);
            EnsureDir(MatDir);

            var smoothMesh = CreateCubeMeshAsset("DemoCube_Smooth");
            var rawMesh    = CreateCubeMeshAsset("DemoCube_Raw");
            if (!smoothMesh || !rawMesh) return;   // 具体原因已由 CreateCubeMeshAsset 打印

            // 只烘焙 Smooth 那份；Raw 保持原样作为对照。
            BakeSmoothNormals(smoothMesh);

            var smoothMat = CreateOutlineMaterial("M_Outline_Smooth", Color.black);
            var rawMat    = CreateOutlineMaterial("M_Outline_Raw",    new Color(1f, 0.25f, 0.2f));
            if (!smoothMat || !rawMat) return;     // Shader 找不到时不要继续搭场景

            BuildScene(smoothMesh, rawMesh, smoothMat, rawMat);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[DemoBuilder] 演示资产已生成 → {ScenePath}\n" +
                      $"  Smooth: {smoothMesh.name} (已烘焙 顶点色 BA)\n" +
                      $"  Raw   : {rawMesh.name} (未烘焙)");
        }

        /// <summary>只重新烘焙 Smooth 网格。每个 Phase 改完存储格式后用它重跑。</summary>
        [MenuItem("Tools/Outline Demo/Re-bake Smooth Mesh")]
        public static void RebakeOnly()
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>($"{MeshDir}/DemoCube_Smooth.asset");
            if (!mesh)
            {
                Debug.LogError("[DemoBuilder] 找不到 DemoCube_Smooth.asset，请先执行 Build Demo Assets。");
                return;
            }
            BakeSmoothNormals(mesh);
            AssetDatabase.SaveAssets();
            Debug.Log("[DemoBuilder] 已重新烘焙 DemoCube_Smooth。");
        }

        // ─────────────────────────────────────────────────────────────
        /// <summary>
        /// 复制 Unity 内置 Cube 网格为独立 .asset。
        /// 内置网格是不可变资源，必须复制出来才能写入 —— 这正是 Phase 5 要自动化的流程。
        /// </summary>
        private static Mesh CreateCubeMeshAsset(string assetName)
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
            string path = $"{MeshDir}/{assetName}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(copy, path);
            return copy;
        }

        /// <summary>调用插件本体的计算 + 写入，走的是与工具窗口完全相同的代码路径。</summary>
        private static void BakeSmoothNormals(Mesh mesh)
        {
            var smoothNormals = OutlineSmoothNormalsCalculator.Calculate(mesh);
            StorageWriter.WriteToVertexColor(
                mesh, smoothNormals,
                OutlineSmoothNormalsGeneratorWindow.VertexColorChannel.Ba);
            EditorUtility.SetDirty(mesh);
        }

        private static Material CreateOutlineMaterial(string assetName, Color outlineColor)
        {
            var shader = Shader.Find(OutlineShaderName);
            if (!shader)
            {
                Debug.LogError($"[DemoBuilder] 找不到 Shader「{OutlineShaderName}」。");
                return null;
            }

            var mat = new Material(shader) { name = assetName };
            if (mat.HasProperty("_OutlineColor")) mat.SetColor("_OutlineColor", outlineColor);
            if (mat.HasProperty("_OutlineWidth")) mat.SetFloat("_OutlineWidth", 0.03f);
            if (mat.HasProperty("_BaseColor"))    mat.SetColor("_BaseColor", new Color(0.82f, 0.82f, 0.85f));

            string path = $"{MatDir}/{assetName}.mat";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        // ─────────────────────────────────────────────────────────────
        /// <summary>
        /// 以 Additive 方式新建场景并填充，避免动到你当前打开的场景。
        /// </summary>
        private static void BuildScene(Mesh smoothMesh, Mesh rawMesh, Material smoothMat, Material rawMat)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

            var camGo = new GameObject("Main Camera", typeof(Camera));
            camGo.tag = "MainCamera";
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags      = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.53f, 0.81f, 0.98f);
            cam.fieldOfView     = 35f;
            // 对准 (+X, +Y, −Z) 角：平滑法线 Z 为负，正是 Z 重建 + 符号修正失效的地方。
            camGo.transform.position = new Vector3(2.6f, 2.2f, -3.4f);
            camGo.transform.LookAt(new Vector3(0f, 0f, -0.15f));

            var lightGo = new GameObject("Directional Light", typeof(Light));
            var light = lightGo.GetComponent<Light>();
            light.type      = LightType.Directional;
            light.intensity = 1.1f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var smoothGo = CreateCubeObject("Cube_Smooth (已烘焙)", smoothMesh, smoothMat,
                                            new Vector3(-Spacing * 0.5f, 0f, 0f));
            var rawGo = CreateCubeObject("Cube_Raw (未烘焙)", rawMesh, rawMat,
                                         new Vector3(Spacing * 0.5f, 0f, 0f));

            foreach (var go in new[] { camGo, lightGo, smoothGo, rawGo })
                SceneManager.MoveGameObjectToScene(go, scene);

            EditorSceneManager.SaveScene(scene, ScenePath);
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
