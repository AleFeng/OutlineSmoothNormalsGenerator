using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace OutlineSmoothNormalsGenerator
{
    public class OutlineSmoothNormalsGeneratorWindow : EditorWindow
    {
        // ─────────────────────────────────────────────────────────────
        //  Save state
        // ─────────────────────────────────────────────────────────────
        private enum SaveState { Clean, NeedSave, Saved }
        private SaveState _saveState = SaveState.Clean;

        /// <summary>
        /// 记录哪些网格被改过但还没保存。保存状态必须跟着【网格】走而不是
        /// 只保留一个全局状态 —— 否则切换选中对象时警告会被静默清掉，
        /// 用户会以为改动已经落盘。
        /// </summary>
        private readonly HashSet<Mesh> _dirtyMeshes = new HashSet<Mesh>();

        // ─────────────────────────────────────────────────────────────
        //  会话级快照
        // ─────────────────────────────────────────────────────────────
        /// <summary>
        /// 生成前的网格快照，供「还原本次修改」使用。
        ///
        /// 为什么不用 Undo：Undo.RecordObject 依赖 SerializedObject 差分，
        /// 而网格几何存在打包的原生数据块里，顶点数组的撤销并不可靠；对
        /// 不可变的 FBX 导入子资产更是完全无能为力。此前 README 承诺的
        /// 「全流程支持 Undo」是一张无法兑现的空头支票，故改为自备快照。
        ///
        /// 只快照本工具会写的通道，不碰顶点/三角形等几何数据。
        /// </summary>
        private class MeshSnapshot
        {
            public Mesh Mesh;
            public Color32[] Colors;
            public Vector4[] Tangents;

            /// <summary>
            /// 各 TEXCOORD 通道的原始内容。一律以 Vector4 捕获：GetUVs 会把缺的分量
            /// 补 0，是无损的；反过来若用 Vector2 捕获，3 分量数据的 z 会被直接丢掉。
            /// </summary>
            public List<Vector4>[] Uvs;

            /// <summary>
            /// 各 TEXCOORD 通道的原始分量数（0 = 该通道原本为空）。
            /// 还原必须按它分派，理由见 <see cref="RestoreUvChannel"/>。
            /// </summary>
            public int[] UvDims;
        }

        /// <summary>
        /// 按【网格】保存快照 —— 批量编辑时一次生成会动多个网格，「还原本次修改」
        /// 必须能把它们全部退回去，否则只还原其中一个是数据安全上的漏洞。
        /// 按来源切换 / 还原后清空，焦点在同一来源内切换时保留。
        /// </summary>
        private readonly Dictionary<Mesh, MeshSnapshot> _snapshots = new Dictionary<Mesh, MeshSnapshot>();

        private void CaptureSnapshot(Mesh mesh)
        {
            if (!mesh) return;

            var snap = new MeshSnapshot
            {
                Mesh     = mesh,
                Colors   = mesh.colors32?.Clone() as Color32[],
                Tangents = mesh.tangents?.Clone() as Vector4[],
                Uvs      = new List<Vector4>[UvChannelCount],
                UvDims   = new int[UvChannelCount],
            };
            for (int i = 0; i < UvChannelCount; i++)
            {
                var list = new List<Vector4>();
                mesh.GetUVs(i, list);
                snap.Uvs[i]    = list;
                snap.UvDims[i] = mesh.GetVertexAttributeDimension(
                    UnityEngine.Rendering.VertexAttribute.TexCoord0 + i);
            }
            // 覆盖写入：始终以「本次生成 / 清除之前」的状态为准。
            _snapshots[mesh] = snap;
        }

        /// <summary>把所有有快照的网格退回到本次修改之前，并清空快照。</summary>
        private void RestoreSnapshots()
        {
            if (_snapshots.Count == 0) return;

            int n = 0;
            foreach (var snap in _snapshots.Values)
            {
                var mesh = snap.Mesh;
                if (!mesh) continue;

                // 空数组即代表「该通道原本就没有数据」，赋回去正好清空。
                mesh.colors32 = snap.Colors;
                mesh.tangents = snap.Tangents;
                for (int i = 0; i < UvChannelCount; i++)
                    RestoreUvChannel(mesh, i, snap.Uvs[i], snap.UvDims[i]);

                EditorUtility.SetDirty(mesh);
                _dirtyMeshes.Remove(mesh);
                n++;
            }

            _snapshots.Clear();
            _saveState = SaveState.Clean;
            RefreshDataStatus();
            Repaint();
            Debug.Log($"{LocLog.Prefix} {LocWindow.LogRestored(n)}");
        }

        /// <summary>清空 UV 通道用的共享空列表 —— SetUVs 只读取入参，可安全复用。</summary>
        private static readonly List<Vector2> EmptyUvList = new List<Vector2>();

        /// <summary>
        /// 按【原始分量数】写回一个 TEXCOORD 通道。
        ///
        /// 不能一律用 <c>SetUVs(i, List&lt;Vector4&gt;)</c>：Unity 严格按传入列表的类型
        /// 设定该通道的分量数，那样还原一次就会把网格上每个非空 UV 通道统统撑成
        /// 4 分量 —— 包括 FBX 的主贴图 UV，顶点缓冲无声翻倍，而且 SetDirty 之后
        /// 对 .asset 网格会直接落盘。
        ///
        /// 更要命的是分量数是本工具识别 UV 里装的是什么的唯一线索（见
        /// <see cref="DetectUVChannelState"/>），被还原改掉之后就再也认不出来了。
        /// </summary>
        private static void RestoreUvChannel(Mesh mesh, int channel, List<Vector4> data, int dim)
        {
            // 空列表即代表「该通道原本就没有数据」，写回去正好清空。
            if (data == null || data.Count == 0)
            {
                mesh.SetUVs(channel, EmptyUvList);
                return;
            }

            switch (dim)
            {
                case 4:
                    mesh.SetUVs(channel, data);
                    break;

                case 3:
                {
                    var v3 = new List<Vector3>(data.Count);
                    for (int i = 0; i < data.Count; i++)
                        v3.Add(new Vector3(data[i].x, data[i].y, data[i].z));
                    mesh.SetUVs(channel, v3);
                    break;
                }

                default:
                {
                    // SetUVs 只有 Vector2 / 3 / 4 三个重载。dim 为 1 或其他异常值时按
                    // 2 分量写回 —— 宁可少一个分量，也好过撑成 4 分量污染顶点缓冲。
                    if (dim != 2)
                        Debug.LogWarning(
                            $"{LocLog.Prefix} {LocWindow.LogUvDimFallback(mesh.name, channel, dim)}",
                            mesh);

                    var v2 = new List<Vector2>(data.Count);
                    for (int i = 0; i < data.Count; i++)
                        v2.Add(new Vector2(data[i].x, data[i].y));
                    mesh.SetUVs(channel, v2);
                    break;
                }
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  网格可写性
        // ─────────────────────────────────────────────────────────────
        private enum MeshWritability
        {
            /// <summary>独立 .asset，写入后可以真正保存。</summary>
            Writable,
            /// <summary>由模型导入器（.fbx 等）生成的子资产：写入不会持久化。</summary>
            ImportedSubAsset,
            /// <summary>Unity 内置资源（Cube/Sphere 等），只读。</summary>
            BuiltIn,
            /// <summary>运行时生成、尚未存盘的网格。</summary>
            NotAnAsset,
        }

        /// <summary>
        /// 判断网格能否真正被保存。
        ///
        /// ⚠ 关键点：对 .fbx 里的网格，AssetDatabase.GetAssetPath 会返回一个
        ///   【非空】路径，但那是不可变的导入子资产 —— SaveAssetIfDirty 对它
        ///   是空操作，数据会在下次重导入时被源文件重新生成而丢失。
        ///   仅凭路径非空就报告「保存成功」，正是此前谎报的根源。
        /// </summary>
        private static MeshWritability GetWritability(Mesh mesh, out string path)
        {
            path = AssetDatabase.GetAssetPath(mesh);

            if (string.IsNullOrEmpty(path))
                return MeshWritability.NotAnAsset;

            // 内置资源位于 Library/unity default resources 之类，不在 Assets/ 下。
            if (!path.StartsWith("Assets/"))
                return MeshWritability.BuiltIn;

            if (AssetImporter.GetAtPath(path) is ModelImporter)
                return MeshWritability.ImportedSubAsset;

            return MeshWritability.Writable;
        }

        // ─────────────────────────────────────────────────────────────
        //  Layout
        // ─────────────────────────────────────────────────────────────
        private Vector2 _leftScroll;
        private Vector2 _rightScroll;
        // 初始值与可拖范围都在文件上方的「分栏尺寸」一节。
        private float _dividerX = DefaultDividerX;
        private bool _isDraggingDivider;
        
        // ─────────────────────────────────────────────────────────────
        //  Data status
        // ─────────────────────────────────────────────────────────────
        /// <summary>
        /// 通道状态。刻意【不】提供「确认含平滑法线」这一档：
        /// 从数据上无法判定一个通道里装的到底是不是平滑法线（尤其是顶点色，
        /// 编码后就是普通的 [0,1] 数值）。宣称能判定就是在骗用户，
        /// 所以最高只到「可能是」。
        /// </summary>
        private enum ChannelState
        {
            /// <summary>通道无数据。</summary>
            Empty,
            /// <summary>有数据，但无法判断是不是平滑法线。</summary>
            HasData,
            /// <summary>强启发式命中，很可能是本工具写入的平滑法线。</summary>
            LikelySmoothNormals,
            /// <summary>
            /// 仅 TEXCOORD 通道：疑似 1.6.x 及更早写入的三分量平滑法线，1.7.0 无法解码。
            ///
            /// 这是本工具能给出的唯一迁移信号 —— 着色器侧对新旧格式无从分辨
            /// （顶点装配把缺失分量补 0，两者 xyz 逐位相同）。因此措辞必须停在
            /// 「可能」：网格合并会把 UV 维度统一取最大，别的把三分量方向写进
            /// UV 的工具（植被风场、VAT、其他描边方案）同样会命中这个判据。
            /// 【绝不可】拿它当自动迁移或自动重写的触发条件。
            /// </summary>
            LegacyUVFormat,
        }

        // Unity 网格最多 8 个 UV 通道（TEXCOORD0..7），工具对全部可读写。
        private const int UvChannelCount = 8;

        private ChannelState _vertexColorState;
        private ChannelState _tangentState;
        private readonly ChannelState[] _uvStates = new ChannelState[UvChannelCount];

        // 顶点色各通道是否承载了逐顶点变化的数据
        private bool _hasVcr, _hasVcg, _hasVcb, _hasVca;

        // ─────────────────────────────────────────────────────────────
        //  网格数据缓存
        // ─────────────────────────────────────────────────────────────
        /// <summary>
        /// 网格数据的缓存快照。
        ///
        /// 每次访问 mesh.vertices / normals / triangles / colors32 / tangents，
        /// Unity 都会从原生层【完整拷贝一份数组】。这些访问原本散落在 OnGUI 里
        /// （网格信息面板、状态卡片、法线叠加层），于是每一个事件 —— 包括每次
        /// 鼠标移动 —— 都会触发十几次整网格 marshal。十万顶点的网格上，光是把
        /// 鼠标划过窗口就会卡死。
        ///
        /// 统一在目标网格切换 / 数据变更时取一次，OnGUI 只读这里。
        /// </summary>
        private class MeshCache
        {
            public int VertexCount;
            public int TriangleCount;
            public int SubMeshCount;
            public bool HasNormals;
            public bool HasTangents;
            public bool HasColors;
            /// <summary>各 TEXCOORD 通道的分量数（0 = 该通道为空，否则为 2 / 3 / 4）。</summary>
            public readonly int[] UvDims = new int[UvChannelCount];

            public Vector3[] Vertices;   // 法线叠加层用
            public Vector3[] Normals;
        }
        private MeshCache _meshCache;

        /// <summary>
        /// 数据版本号。写入 / 清除 / 还原后递增，用来让解码缓存失效。
        /// </summary>
        private int _dataVersion;

        // 解码结果缓存：解码依赖存储模式、通道选择与存储空间，因此 key 要带上它们。
        private Vector3[] _decodedCache;
        private (Mesh mesh, StorageMode mode, VertexColorChannel vc, int uv, NormalSpace space, int version) _decodedKey;

        private void RebuildMeshCache()
        {
            if (!_targetMesh)
            {
                _meshCache    = null;
                _decodedCache = null;
                return;
            }

            var c = new MeshCache
            {
                VertexCount  = _targetMesh.vertexCount,
                SubMeshCount = _targetMesh.subMeshCount,
                Vertices     = _targetMesh.vertices,
                Normals      = _targetMesh.normals,
            };
            c.TriangleCount = _targetMesh.triangles.Length / 3;
            c.HasNormals    = c.Normals != null && c.Normals.Length > 0;
            c.HasTangents   = _targetMesh.tangents?.Length > 0;
            c.HasColors     = _targetMesh.colors32?.Length > 0;

            // 记【分量数】而不是元素个数。此前存的是 GetUVs 取回的条目数 —— 通道非空时
            // 它恒等于顶点数，八个通道显示同一个数字，信息量为零；三份 README 描述的
            // 也一直是分量数。
            //
            // 顺带省掉 8 次 GetUVs：那是把整份 UV 数组 marshal 到托管侧，只为数一下长度。
            // GetVertexAttributeDimension 读的是顶点布局元数据，不拷贝任何数组。
            for (int i = 0; i < UvChannelCount; i++)
                c.UvDims[i] = _targetMesh.GetVertexAttributeDimension(
                    UnityEngine.Rendering.VertexAttribute.TexCoord0 + i);

            _meshCache    = c;
            _decodedCache = null;
        }

        /// <summary>按当前模式解码平滑法线，结果带缓存 —— 每次调用都全量解码是 OnGUI 里的重灾区。</summary>
        private Vector3[] GetDecodedSmoothNormalsCached()
        {
            var key = (_targetMesh, _storageMode, _vcChannel, _uvChannel, _normalSpace, _dataVersion);
            if (_decodedCache != null && _decodedKey.Equals(key)) return _decodedCache;

            // 内嵌预览渲的是绑定姿势的静态网格，数据源与姿势源是同一个。
            _decodedCache = DecodeSmoothNormals(_targetMesh, _targetMesh,
                                                _storageMode, _vcChannel, _uvChannel, _normalSpace);
            _decodedKey   = key;
            return _decodedCache;
        }

        // ─────────────────────────────────────────────────────────────
        //  Foldouts
        // ─────────────────────────────────────────────────────────────
        private bool _foldoutMeshInfo;

        // ═══════════════════════════════════════════════════════════════
        //  窗口尺寸 —— 调整窗口大小只改这一处
        // ═══════════════════════════════════════════════════════════════
        // 这一个值同时决定「最小尺寸」与「初始尺寸」：Unity 会把新开的窗口撑到
        // minSize，所以设了下限也就等于设了首次打开时的大小。
        // 下限不能再小了：左右两栏 + 内嵌预览视口再挤就会开始互相压掉。
        private static readonly Vector2 DefaultWindowSize = new (1120, 800);

        // ═══════════════════════════════════════════════════════════════
        //  分栏尺寸 —— 调整左右两栏只改这一处
        // ═══════════════════════════════════════════════════════════════
        //  横向结构（分隔线可由用户拖动）：
        //
        //    │←── 左栏 = _dividerX ──→│││←────────── 右栏 = 窗口宽 - _dividerX ─────────→│
        //    │      控制面板          │分│  预览视口（弹性，吃掉所有余量） │ 参数栏（定宽）│
        //
        //  左栏宽度可拖，右栏是剩下的；右栏内部只有【参数栏是定宽的】，视口拿走余量。
        //  所以窗口拉宽 = 视口变大，参数栏与左栏都不跟着变。

        /// <summary>左栏初始宽度。</summary>
        private const float DefaultDividerX = 520f;

        /// <summary>左栏可拖到的最窄宽度：再窄，控件就开始互相压掉了。</summary>
        private const float MinDividerX = 300f;

        /// <summary>
        /// 右栏里参数栏的固定宽度。
        ///
        /// 从 220 提到 256：英文 / 日文的参数名比中文长约一半（「显示平滑法线」6 字
        /// 对 "Show smooth normals" 19 字符），220 下英文标签会挤掉数值输入框。
        /// 代价全部由预览视口承担：最小窗口（860）+ 初始分隔线（420）下视口由 202px
        /// 缩到 166px，把分隔线拖到下限 300 则回到 246px。
        /// </summary>
        private const float PreviewParamPanelWidth = 240f;

        /// <summary>预览视口再窄就没有意义了，分隔线的可拖范围以它为准。</summary>
        private const float MinPreviewWidth = 80f;

        /// <summary>
        /// 右栏至少要留出的宽度 = 视口下限 + 定宽参数栏 + 两侧间隙，
        /// 也就是分隔线能拖到的最右位置所留下的余量。
        ///
        /// ⚠ 必须跟着 PreviewParamPanelWidth 走，不能写死。此前分隔线钳位里写的是常量
        /// 250，而参数栏加宽到 256 之后，【一栏本身就比整个右栏的保留量还宽】——
        /// 分隔线拖到上限时参数栏必定被窗口右缘裁掉，且与窗口多宽无关。
        /// </summary>
        private const float MinRightPanelWidth = MinPreviewWidth + PreviewParamPanelWidth + 18f;

        /// <summary>
        /// 参数栏内所有字段的标签宽度。
        ///
        /// Unity 默认的 labelWidth 是按【整个窗口宽度】算的（currentViewWidth * 0.45），
        /// 与这一栏实际只有 256px 毫无关系 —— 窗口拉宽反而会让标签算得比栏还宽，
        /// 标签整条被裁。这里显式钉死，让三种语言下的表现都是确定的。
        ///
        /// 130 的依据：本栏最长的标签是英文 "Show original normals" / "Original normal color"，
        /// 各约 120px，留 10px 余量。剩给滑杆 / 取色器的是 256 - 16(卡片内边距) - 130 = 110px。
        /// </summary>
        private const float PreviewParamLabelWidth = 130f;

        [MenuItem("Tools/Smooth Normal Generator")]
        public static void ShowWindow()
        {
            var win = GetWindow<OutlineSmoothNormalsGeneratorWindow>(LocWindow.WindowTitle);
            win.minSize = DefaultWindowSize;
            win.Show();
        }

        // ═══════════════════════════════════════════════════════════════
        private void OnEnable()
        {
            // 与 Selection / SceneView 的订阅同样的纪律：必须与 OnDisable 严格成对。
            OutlineLocale.Changed += OnLocaleChanged;
            ApplyLocale();

            Selection.selectionChanged += OnSelectionChanged;
            OnSelectionChanged();
            SetupPreviewRenderer();
        }

        private void OnDisable()
        {
            OutlineLocale.Changed -= OnLocaleChanged;
            Selection.selectionChanged -= OnSelectionChanged;
            TearDownSceneOverlay();
            TearDownPreviewRenderer();
        }

        /// <summary>
        /// 语言变更后的收尾。界面文案本身每帧现取，不需要处理；这里只管两件
        /// 【一次性写入后就不再更新】的东西：
        ///   · 窗口标签页标题 —— GetWindow 只在首次创建时写一次；
        ///   · 网格健康检查报告 —— MeshHealthReport 存的是已拼好的句子，不重算
        ///     就会一直停在旧语言（本包唯一需要主动失效的缓存）。
        /// </summary>
        private void OnLocaleChanged()
        {
            ApplyLocale();
            RefreshHealthReport();
            Repaint();
        }

        private void ApplyLocale()
        {
            titleContent.text = LocWindow.WindowTitle;
        }

        #region Scene 视图法线叠加
        // ═══════════════════════════════════════════════════════════════
        //  把平滑法线画到 Scene 视图里 —— 内嵌预览看的是绑定姿势的静态网格，
        //  而「切线空间存储让蒙皮描边不撕开」这个卖点只有在动画播放时才验证得了。
        //  这一层就是为此存在的：SkinnedMeshRenderer 走 BakeMesh 取当前姿势，
        //  法线应当始终贴着表面走；关节处若扇形散开，就是数据烘在对象空间而材质
        //  按切线空间解（或反之）。
        //
        //  刻意做成本窗口的一个开关，而不是给用户物体挂 MonoBehaviour：
        //  本包是纯编辑器程序集、零运行时占用，加组件就得为它单开 Runtime 程序集。
        // ═══════════════════════════════════════════════════════════════

        /// <summary>每帧线段总数上限。超出后统一抽稀，并在面板上如实标注比例。</summary>
        private const int MaxSceneLines = 12000;

        private bool _showInSceneView;

        /// <summary>BakeMesh 的复用目标 —— 每帧新建 Mesh 会让内存飙升。</summary>
        private Mesh _bakedPoseMesh;

        private readonly List<Vector3> _sceneLineBuffer = new List<Vector3>();

        /// <summary>本次绘制使用的采样步长。每帧在 OnSceneOverlayGUI 里重算。</summary>
        private int _sceneSampleStep = 1;

        /// <summary>
        /// 静态网格的解码结果缓存。蒙皮网格【不缓存】—— 切线空间还原依赖当前姿势，
        /// 每帧都会变。缓存整体按存储配置与数据版本失效。
        /// </summary>
        private readonly Dictionary<Mesh, Vector3[]> _sceneDecodeCache = new Dictionary<Mesh, Vector3[]>();
        private (StorageMode mode, VertexColorChannel vc, int uv, NormalSpace space, int version) _sceneCacheKey;

        /// <summary>
        /// 静态网格的顶点 / 原始法线缓存。mesh.vertices 每次访问都会从原生层完整
        /// 拷贝一份数组，而 Scene 视图只要鼠标动就重绘 —— 不缓存的话每帧都在给 GC
        /// 生产几 MB 垃圾。蒙皮网格同样不缓存（BakeMesh 的产物每帧都变）。
        /// </summary>
        private readonly Dictionary<Mesh, (Vector3[] verts, Vector3[] normals)> _sceneGeoCache =
            new Dictionary<Mesh, (Vector3[], Vector3[])>();

        private void SetSceneOverlayEnabled(bool value)
        {
            if (_showInSceneView == value) return;
            _showInSceneView = value;

            // 订阅严格成对：漏退订会让窗口关闭后 Scene 里仍在画线，而且把窗口实例
            // 挂在事件链上泄漏掉 —— 表现为「关了工具线还在，且再也去不掉」。
            if (value) SceneView.duringSceneGui += OnSceneOverlayGUI;
            else       SceneView.duringSceneGui -= OnSceneOverlayGUI;

            SceneView.RepaintAll();
        }

        private void TearDownSceneOverlay()
        {
            if (_showInSceneView)
            {
                SceneView.duringSceneGui -= OnSceneOverlayGUI;
                _showInSceneView = false;
                SceneView.RepaintAll();
            }

            if (_bakedPoseMesh)
            {
                DestroyImmediate(_bakedPoseMesh);
                _bakedPoseMesh = null;
            }
            _sceneDecodeCache.Clear();
            _sceneGeoCache.Clear();
        }

        /// <summary>能画进 Scene 的条目：已勾选、网格还在、且有场景中的宿主组件。</summary>
        private static bool IsSceneDrawable(MeshEntry e)
            => e != null && e.Selected && e.Mesh && e.Owner;

        private int SceneDrawableVertexCount()
        {
            int total = 0;
            foreach (var e in _meshEntries)
                if (IsSceneDrawable(e)) total += e.Mesh.vertexCount;
            return total;
        }

        /// <summary>
        /// 抽稀步长在【所有网格上统一】计算：各自按自己的顶点数抽稀的话，密网格
        /// 被抽得厉害、疏网格全画，看到的疏密差纯属假象。
        /// </summary>
        private int SceneSampleStepFor(int totalVerts)
        {
            int linesPerVert = (_showNormals ? 1 : 0) + (_showOriginalNormals ? 1 : 0);
            if (totalVerts <= 0 || linesPerVert == 0) return 1;
            return Mathf.Max(1, Mathf.CeilToInt(totalVerts * linesPerVert / (float)MaxSceneLines));
        }

        private void OnSceneOverlayGUI(SceneView view)
        {
            if (!_showInSceneView) return;
            if (Event.current.type != EventType.Repaint) return;
            if (!_showNormals && !_showOriginalNormals) return;

            int totalVerts = SceneDrawableVertexCount();
            if (totalVerts == 0) return;

            _sceneSampleStep = SceneSampleStepFor(totalVerts);
            RefreshSceneDecodeCacheKey();

            foreach (var e in _meshEntries)
                if (IsSceneDrawable(e)) DrawSceneNormalsFor(e);
        }

        /// <summary>存储配置或数据版本一变，静态网格的解码缓存整体作废。</summary>
        private void RefreshSceneDecodeCacheKey()
        {
            var key = (_storageMode, _vcChannel, _uvChannel, _normalSpace, _dataVersion);
            if (_sceneCacheKey.Equals(key)) return;
            _sceneDecodeCache.Clear();
            _sceneGeoCache.Clear();
            _sceneCacheKey = key;
        }

        private void DrawSceneNormalsFor(MeshEntry e)
        {
            Mesh dataMesh = e.Mesh;
            Mesh poseMesh = dataMesh;
            bool skinned  = false;

            if (e.Owner is SkinnedMeshRenderer smr)
            {
                if (!_bakedPoseMesh)
                    _bakedPoseMesh = new Mesh
                    {
                        name      = "OSN_ScenePoseTemp",
                        hideFlags = HideFlags.HideAndDontSave,
                    };

                // useScale: false —— 烘出的顶点留在渲染器局部空间、不含缩放，
                // 缩放交给下面的 localToWorldMatrix 施加，正好各算一次。
                // （若发现带缩放的角色法线长度翻倍或减半，就是这两者重复 / 漏算了。）
                smr.BakeMesh(_bakedPoseMesh, false);
                poseMesh = _bakedPoseMesh;
                skinned  = true;
            }

            Vector3[] verts, rawNormals, decoded;
            if (skinned)
            {
                // 蒙皮网格每帧都变，缓存无意义。
                verts      = poseMesh.vertices;
                rawNormals = _showOriginalNormals ? poseMesh.normals : null;
                decoded    = _showNormals
                    ? DecodeSmoothNormals(dataMesh, poseMesh, _storageMode, _vcChannel, _uvChannel, _normalSpace)
                    : null;
            }
            else
            {
                var geo = GetSceneGeoCached(dataMesh);
                verts      = geo.verts;
                rawNormals = geo.normals;
                decoded    = _showNormals ? GetSceneDecodedCached(dataMesh) : null;
            }

            if (verts == null || verts.Length != dataMesh.vertexCount) return;

            var l2W = e.Owner.transform.localToWorldMatrix;

            if (_showNormals)          DrawSceneNormalLines(verts, decoded,    l2W, _normalColor);
            if (_showOriginalNormals)  DrawSceneNormalLines(verts, rawNormals, l2W, _originalNormalColor);
        }

        private (Vector3[] verts, Vector3[] normals) GetSceneGeoCached(Mesh mesh)
        {
            if (_sceneGeoCache.TryGetValue(mesh, out var cached)) return cached;

            var geo = (mesh.vertices, mesh.normals);
            _sceneGeoCache[mesh] = geo;
            return geo;
        }

        /// <summary>静态网格：解码结果与姿势无关，缓存起来，否则每次 Scene 重绘都要全量解一遍。</summary>
        private Vector3[] GetSceneDecodedCached(Mesh mesh)
        {
            if (_sceneDecodeCache.TryGetValue(mesh, out var cached)) return cached;

            var decoded = DecodeSmoothNormals(mesh, mesh, _storageMode, _vcChannel, _uvChannel, _normalSpace);
            _sceneDecodeCache[mesh] = decoded;   // null 也缓存 —— 解不出来这件事同样不必重复求证
            return decoded;
        }

        /// <summary>「法线可视化」面板末尾的 Scene 视图开关与状态回显。</summary>
        private void DrawSceneOverlayUI()
        {
            bool next = EditorGUILayout.Toggle(
                new GUIContent(LocWindow.ToggleSceneOverlay, LocWindow.ToggleSceneOverlayTooltip),
                _showInSceneView);
            SetSceneOverlayEnabled(next);

            if (!_showInSceneView) return;

            int drawable = _meshEntries.Count(IsSceneDrawable);
            if (drawable == 0)
            {
                EditorGUILayout.HelpBox(LocWindow.SceneOverlayNoSceneMesh, MessageType.Info);
                return;
            }

            // 抽稀此前是【静默】的：Scene 里线段变稀时，用户无从判断是数据出了问题
            // 还是只是被抽掉了 —— 而这恰恰是三份 README 承诺「如实写出采样比例」要
            // 防的误判。
            //
            // 现算而不用 OnSceneOverlayGUI 里存下的 _sceneSampleStep：那一份要等
            // Scene 视图重绘一次才更新，刚勾上网格、或刚改完两个「显示…法线」开关的
            // 那一帧里还是旧值，面板会短暂地说谎。这里的计算只是把勾选项的顶点数
            // 加一遍，代价可以忽略。
            int step = SceneSampleStepFor(SceneDrawableVertexCount());
            if (step > 1)
                EditorGUILayout.HelpBox(LocWindow.SceneOverlaySampleNote(step), MessageType.None);
        }

        private void DrawSceneNormalLines(Vector3[] verts, Vector3[] dirs, Matrix4x4 l2W, Color color)
        {
            if (dirs == null || dirs.Length != verts.Length) return;

            _sceneLineBuffer.Clear();
            for (int i = 0; i < verts.Length; i += _sceneSampleStep)
            {
                Vector3 p = l2W.MultiplyPoint3x4(verts[i]);
                Vector3 n = l2W.MultiplyVector(dirs[i]);
                _sceneLineBuffer.Add(p);
                _sceneLineBuffer.Add(p + n.normalized * _normalLength);
            }

            if (_sceneLineBuffer.Count == 0) return;

            // 必须批量提交：逐条 Handles.DrawLine 在几万顶点下会直接卡死 Scene 视图。
            var prev = Handles.color;
            Handles.color = color;
            Handles.DrawLines(_sceneLineBuffer.ToArray());
            Handles.color = prev;
        }
        #endregion

        #region UI 主界面
        /// <summary>窗口顶部页签。</summary>
        public enum WindowTab { Generator, AutoBake }
        private WindowTab _activeTab = WindowTab.Generator;

        private const float TabBarHeight = 34f;
        // 页签栏下沿的 y（= 生成器页签内容区顶部）。分隔线用它作起点，避免竖线穿过页签栏。
        private float _contentTop;

        private Vector2 _autoBakeScroll;

        /// <summary>
        /// 把分隔线钳进合法范围。窗口很窄时下限优先 —— 宁可右栏被挤，也不能让左栏
        /// 塌到控件互相压掉。
        /// </summary>
        private float ClampDividerX(float x)
            => Mathf.Clamp(x, MinDividerX,
                           Mathf.Max(MinDividerX, position.width - MinRightPanelWidth));

        private void OnGUI()
        {
            // 每帧钳一次，而不是只在拖动时钳：先把窗口拉宽、把分隔线拖到很右，再把
            // 窗口缩回去，_dividerX 会一直停在那个对当前窗口非法的值上。
            _dividerX = ClampDividerX(_dividerX);

            DrawTabBar();

            switch (_activeTab)
            {
                case WindowTab.AutoBake:
                    DrawAutoBakeTab();
                    break;
                default:
                    DrawGeneratorTab();
                    break;
            }
        }

        /// <summary>「平滑法线生成器」页签：左控制栏 + 右预览的原有两栏布局。</summary>
        private void DrawGeneratorTab()
        {
            EditorGUILayout.BeginHorizontal();
            {
                // ── Left panel ──────────────────────────────────────
                EditorGUILayout.BeginVertical(GUILayout.Width(_dividerX));
                _leftScroll = EditorGUILayout.BeginScrollView(_leftScroll);
                DrawLeftPanel();
                EditorGUILayout.EndScrollView();
                DrawSaveButton();          // 固定在左栏底部，ScrollView 外
                EditorGUILayout.EndVertical();

                // ── Divider ─────────────────────────────────────────
                DrawDivider();

                // ── Right panel ─────────────────────────────────────
                EditorGUILayout.BeginVertical();

                // 数据状态总览放在 ScrollView 内
                _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll,
                    GUILayout.ExpandHeight(false), GUILayout.MaxHeight(position.height * 0.4f));
                DrawRightPanelTop();
                EditorGUILayout.EndScrollView();

                // 预览区放在 ScrollView 外，直接占满剩余高度
                DrawPreviewLaunchPanel();

                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndHorizontal();

            HandleDividerDrag();
        }

        // ═══════════════════════════════════════════════════════════════
        //  顶部页签栏
        // ═══════════════════════════════════════════════════════════════
        private void DrawTabBar()
        {
            EditorGUILayout.BeginHorizontal();
            DrawWindowTab(LocWindow.TabGenerator, WindowTab.Generator);
            DrawWindowTab(LocWindow.TabAutoBake, WindowTab.AutoBake);
            EditorGUILayout.EndHorizontal();

            var barRect = GUILayoutUtility.GetLastRect();
            // 页签栏下沿画一条强调线；记录内容区顶部供分隔线定位。
            EditorGUI.DrawRect(new Rect(0, barRect.yMax, position.width, 2), OutlineEditorStyles.Accent);
            _contentTop = barRect.yMax + 2;
            GUILayout.Space(2);
        }

        private void DrawWindowTab(string label, WindowTab tab)
        {
            if (OutlineEditorGUI.DrawSegmentTab(label, _activeTab == tab, TabBarHeight)
                && _activeTab != tab)
            {
                _activeTab = tab;
                GUI.FocusControl(null);
                Repaint();
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  「导入自动烘焙」页签
        // ═══════════════════════════════════════════════════════════════
        /// <summary>
        /// 命中条件区：两个条件各自可开关，同时开启时取【交集】。
        /// 一个都不勾时必须明确告知「不命中任何模型」—— 这正是 DefaultRule 里
        /// 特意挡掉的那个「AND 在零条件上恒真」的坑，UI 侧也要把它说清楚。
        /// </summary>
        private static void DrawMatchConditionsUI(OutlineNormalsSettings s)
        {
            s.MatchBySuffix = EditorGUILayout.ToggleLeft(
                new GUIContent(LocWindow.MatchBySuffix, LocWindow.MatchBySuffixTooltip),
                s.MatchBySuffix);

            using (new EditorGUI.DisabledScope(!s.MatchBySuffix))
            {
                EditorGUI.indentLevel++;
                s.FilenameSuffix = EditorGUILayout.TextField(LocWindow.LabelSuffix, s.FilenameSuffix);
                EditorGUILayout.LabelField(
                    " ",
                    string.IsNullOrEmpty(s.FilenameSuffix)
                        ? LocWindow.SuffixEmptyHint
                        : LocWindow.SuffixExample(s.FilenameSuffix),
                    EditorStyles.miniLabel);
                EditorGUI.indentLevel--;
            }

            GUILayout.Space(4);

            s.MatchByFolder = EditorGUILayout.ToggleLeft(
                new GUIContent(LocWindow.MatchByFolder, LocWindow.MatchByFolderTooltip),
                s.MatchByFolder);

            using (new EditorGUI.DisabledScope(!s.MatchByFolder))
            {
                EditorGUI.indentLevel++;
                DrawFolderField(s);
                EditorGUI.indentLevel--;
            }

            GUILayout.Space(4);
            DrawMatchSummary(s);
        }

        /// <summary>
        /// 用对象槽拖文件夹，而不是让用户手打路径：
        /// 手打的路径拼错了不会报错，只会静默地一个模型都不命中。
        /// </summary>
        private static void DrawFolderField(OutlineNormalsSettings s)
        {
            var current = string.IsNullOrEmpty(s.FolderPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<DefaultAsset>(s.FolderPath);

            var picked = EditorGUILayout.ObjectField(LocWindow.LabelFolder, current,
                                                     typeof(DefaultAsset), false);
            if (picked != current)
            {
                string path = picked ? AssetDatabase.GetAssetPath(picked) : "";
                // DefaultAsset 也能装下非文件夹资产（如 .dll），拖错了就忽略。
                s.FolderPath = AssetDatabase.IsValidFolder(path) ? path : "";
            }

            if (string.IsNullOrEmpty(s.FolderPath))
            {
                EditorGUILayout.LabelField(" ", LocWindow.FolderEmptyHint, EditorStyles.miniLabel);
                return;
            }

            // 配置里的文件夹被删掉 / 改名后，对象槽会显示成 None 而路径还留着，
            // 表现为「看起来没配，实际一个都不命中」。这种沉默最难查，明说出来。
            if (!AssetDatabase.IsValidFolder(s.FolderPath))
            {
                EditorGUILayout.HelpBox(LocWindow.FolderMissingWarning(s.FolderPath),
                                        MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField(
                " ",
                LocWindow.FolderSubdirHint(Path.GetFileName(s.FolderPath)),
                EditorStyles.miniLabel);
        }

        /// <summary>把当前两个开关的组合结果用一句人话说出来。</summary>
        private static void DrawMatchSummary(OutlineNormalsSettings s)
        {
            if (!s.HasAnyMatchCondition)
            {
                EditorGUILayout.HelpBox(LocWindow.MatchSummaryNone, MessageType.Warning);
                return;
            }

            // 片段只作参数代入，整句由各语言自己的模板负责 —— 逐片段翻译再拼，
            // 在英日语序下必然拼出不通的句子。
            string suffixPart = string.IsNullOrEmpty(s.FilenameSuffix)
                ? LocWindow.MatchPartSuffixUnset
                : LocWindow.MatchPartSuffixSet(s.FilenameSuffix);
            string folderPart = string.IsNullOrEmpty(s.FolderPath)
                ? LocWindow.MatchPartFolderUnset
                : LocWindow.MatchPartFolderSet(s.FolderPath);

            string text;
            if (s.MatchBySuffix && s.MatchByFolder)
                text = LocWindow.MatchSummaryBoth(suffixPart, folderPart);
            else if (s.MatchBySuffix)
                text = LocWindow.MatchSummaryOne(suffixPart);
            else
                text = LocWindow.MatchSummaryOne(folderPart);

            EditorGUILayout.HelpBox(text, MessageType.None);
        }

        private void DrawAutoBakeTab()
        {
            var s = OutlineNormalsSettings.instance;

            _autoBakeScroll = EditorGUILayout.BeginScrollView(_autoBakeScroll);

            DrawAutoBakeHeader();

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginVertical(OutlineEditorStyles.DataCard,
                GUILayout.Width(Mathf.Min(600f, position.width - 40f)));

            EditorGUILayout.HelpBox(LocWindow.AutoBakeIntro, MessageType.Info);

            GUILayout.Space(6);

            EditorGUI.BeginChangeCheck();

            s.AutoBakeEnabled = EditorGUILayout.ToggleLeft(LocWindow.ToggleAutoBakeEnabled,
                                                          s.AutoBakeEnabled);

            using (new EditorGUI.DisabledScope(!s.AutoBakeEnabled))
            {
                GUILayout.Space(8);
                OutlineEditorGUI.DrawSectionHeader(LocWindow.SectionMatchRules, "◈");
                DrawMatchConditionsUI(s);

                GUILayout.Space(8);
                OutlineEditorGUI.DrawSectionHeader(LocWindow.SectionStorageMode, "◈");
                // Popup 而非 EnumPopup：后者显示的是枚举成员名，三语下恒为英文，
                // 且与生成器页签同一枚举的显示对不上。详见 LocWindow.StorageModeNames。
                s.StorageMode = (StorageMode)EditorGUILayout.Popup(
                    LocWindow.LabelAutoStorageChannel, (int)s.StorageMode,
                    LocWindow.StorageModeNames);
                switch (s.StorageMode)
                {
                    case StorageMode.VertexColor:
                        s.VcChannel = (VertexColorChannel)EditorGUILayout.EnumPopup(
                            LocWindow.LabelAutoVcChannelPair, s.VcChannel);
                        break;
                    case StorageMode.UV:
                        s.UvChannel = EditorGUILayout.IntSlider(
                            LocWindow.LabelAutoUvChannel, s.UvChannel, 0, 7);
                        break;
                    case StorageMode.TangentSpace:
                        EditorGUILayout.HelpBox(LocWindow.AutoTangentOverwriteWarning,
                                                MessageType.Warning);
                        break;
                }

                // 存储空间：与存储通道正交。切线通道恒为对象空间，故禁用。
                using (new EditorGUI.DisabledScope(s.StorageMode == StorageMode.TangentSpace))
                    s.NormalSpace = (NormalSpace)EditorGUILayout.Popup(
                        new GUIContent(LocWindow.LabelStorageSpace,
                                       LocWindow.LabelAutoStorageSpaceTooltip),
                        (int)s.NormalSpace, LocWindow.NormalSpaceNames);

                if (s.StorageMode != StorageMode.TangentSpace)
                {
                    if (s.NormalSpace == NormalSpace.Tangent)
                        EditorGUILayout.HelpBox(LocWindow.AutoTangentSpaceInfo, MessageType.Info);
                    else
                        EditorGUILayout.HelpBox(LocWindow.AutoObjectSpaceWarning, MessageType.Warning);
                }

                GUILayout.Space(8);
                OutlineEditorGUI.DrawSectionHeader(LocWindow.SectionGenerateParams, "◈");
                s.MergeTolerance = EditorGUILayout.FloatField(
                    new GUIContent(LocWindow.LabelMergeTolerance,
                                   LocWindow.LabelAutoMergeToleranceTooltip),
                    s.MergeTolerance);
            }

            if (EditorGUI.EndChangeCheck())
                s.Save();

            EditorGUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(10);
            EditorGUILayout.LabelField(LocWindow.AutoSettingsPathNote,
                                       EditorStyles.centeredGreyMiniLabel);

            EditorGUILayout.EndScrollView();
        }

        private void DrawAutoBakeHeader()
        {
            GUILayout.Space(10);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(12);

            DrawHeaderIcon();

            GUILayout.Space(10);
            EditorGUILayout.BeginVertical();
            GUILayout.Space(4);
            GUILayout.Label(LocWindow.AutoBakeHeaderTitle, OutlineEditorStyles.Header);
            GUILayout.Label(LocWindow.AutoBakeHeaderSubtitle, OutlineEditorStyles.SubHeader);
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();

            GUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(HeaderTextIndent);
            OutlineLocale.DrawSwitch();
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(10);
        }
        
        private void DrawDivider()
        {
            var dividerRect = new Rect(_dividerX, _contentTop, 4, position.height - _contentTop);
            EditorGUI.DrawRect(dividerRect, OutlineEditorStyles.Border);

            // Hover highlight
            if (dividerRect.Contains(Event.current.mousePosition))
            {
                EditorGUI.DrawRect(dividerRect, OutlineEditorStyles.Accent * 0.6f);
                EditorGUIUtility.AddCursorRect(dividerRect, MouseCursor.ResizeHorizontal);
            }
        }
        
        private void HandleDividerDrag()
        {
            var dividerRect = new Rect(_dividerX - 2, _contentTop, 8, position.height - _contentTop);
            var e = Event.current;

            if (e.type == EventType.MouseDown && dividerRect.Contains(e.mousePosition))
                _isDraggingDivider = true;
            if (e.type == EventType.MouseUp)
                _isDraggingDivider = false;
            if (_isDraggingDivider && e.type == EventType.MouseDrag)
            {
                _dividerX = ClampDividerX(e.mousePosition.x);
                Repaint();
            }
        }
        
        private void OnSelectionChanged()
        {
            // 只在新选择【含可处理网格】时才切换目标；否则保留当前目标不动
            // —— 选中一个无关对象不应把已选好的网格清掉（此前的老问题：组件引用变
            // null 但旧网格还挂着，界面显示渲染器为「—」，生成按钮却仍对上一个网格开火）。
            // 来源可以是场景 GameObject，也可以是 Project 里的 Mesh / 模型 / 预制体资产。
            SetTargetSource(Selection.activeObject);
            Repaint();
        }

        #region UI 左侧界面
        private void DrawLeftPanel()
        {
            DrawWindowHeader();
            GUILayout.Space(12);
            DrawTargetSection();
            GUILayout.Space(6);
            DrawMeshInfoSection();
            GUILayout.Space(6);
            DrawStorageModeSection();
            GUILayout.Space(6);
            DrawGenerateSection();
            GUILayout.Space(6);
        }

        private void DrawSaveButton()
        {
            EditorGUI.DrawRect(new Rect(0, position.height - 74, _dividerX, 1), OutlineEditorStyles.Border);
            GUILayout.Space(6);

            // ── 第一行：还原 + 保存 ──────────────────────────────────
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);

            // 还原：本次会话抓到过快照就可用；批量生成会跨多个网格抓快照，一并退回。
            GUI.enabled = _snapshots.Count > 0;
            // MinWidth 而非 Width：英文 / 日文的按钮文字比中文长，定死宽度会被裁掉。
            if (GUILayout.Button(new GUIContent(LocWindow.BtnRevert, LocWindow.BtnRevertTooltip),
                    GUILayout.MinWidth(110), GUILayout.Height(26)))
                RestoreSnapshots();
            GUI.enabled = true;

            // 「保存」按钮固定文字，只切换可用状态；「需要保存 / 已保存 / 不可直接保存」
            // 通过按钮颜色与 tooltip 表达，而不是改按钮文字。
            // 注意：不可写时右侧「另存为」会高亮为唯一出路 —— 即使禁用态 tooltip 不弹，
            // 也能靠它把「怎么保存」引导到位。
            // 「保存」按钮反映【勾选集合】的聚合状态，而非单个焦点网格 —— 焦点网格
            // 已保存、但别的勾选网格还脏时，按钮绝不能显示成灰、让人以为都落盘了。
            var checkedMeshes = SelectedMeshes();
            int dirtyWritable = 0, dirtyBlocked = 0;
            foreach (var m in checkedMeshes)
            {
                if (!_dirtyMeshes.Contains(m)) continue;
                if (GetWritability(m, out _) == MeshWritability.Writable) dirtyWritable++;
                else                                                      dirtyBlocked++;
            }

            Color  btnColor;
            string btnTip;
            bool   canSave;

            if (checkedMeshes.Count == 0)
            {
                btnColor = OutlineEditorStyles.Gray;
                btnTip   = LocWindow.SaveTipNoSelection;
                canSave  = false;
            }
            else if (dirtyWritable > 0)
            {
                btnColor = OutlineEditorStyles.Warning;
                btnTip   = LocWindow.SaveTipDirty(dirtyWritable) +
                           (dirtyBlocked > 0 ? "\n" + LocWindow.SaveTipBlockedExtra(dirtyBlocked) : "");
                canSave  = true;
            }
            else if (dirtyBlocked > 0)
            {
                btnColor = OutlineEditorStyles.Gray;
                btnTip   = LocWindow.SaveTipBlockedOnly(dirtyBlocked);
                canSave  = false;
            }
            else
            {
                btnColor = _saveState == SaveState.Saved ? OutlineEditorStyles.Success : OutlineEditorStyles.Gray;
                btnTip   = _saveState == SaveState.Saved ? LocWindow.SaveTipSaved : LocWindow.SaveTipNothing;
                canSave  = false;
            }

            GUI.enabled = canSave;
            if (OutlineEditorGUI.DrawAccentButton(
                    new GUIContent(LocWindow.BtnSave, btnTip), btnColor,
                    canSave ? OutlineEditorStyles.OnAccent : OutlineEditorStyles.TextDisabled,
                    fontSize: 11, height: 26f, bold: true))
                SaveCheckedMeshes();
            GUI.enabled = true;

            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();

            // ── 第二行：另存为独立 Mesh（作用于所有勾选项）─────────────
            GUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);

            GUI.enabled = SelectedCount > 0;
            // 有勾选的网格不可直接保存时，这是唯一出路，因此高亮它。
            bool highlightDup = dirtyBlocked > 0;
            var dupColor = highlightDup ? OutlineEditorStyles.Accent : OutlineEditorStyles.Card * 1.5f;
            if (OutlineEditorGUI.DrawAccentButton(
                    new GUIContent(LocWindow.BtnDuplicate, LocWindow.BtnDuplicateTooltip), dupColor,
                    highlightDup ? OutlineEditorStyles.OnAccent : OutlineEditorStyles.TextOnNeutral,
                    fontSize: 11, height: 24f, bold: highlightDup))
                DuplicateMeshToAsset();
            GUI.enabled = true;

            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(6);
        }

        /// <summary>批量保存勾选集合里所有「已修改且可写」的网格，绝不谎报成功。</summary>
        private void SaveCheckedMeshes()
        {
            var checkedMeshes = SelectedMeshes();
            if (checkedMeshes.Count == 0) return;

            var saved   = new List<string>();
            var blocked = new List<Mesh>();
            foreach (var mesh in checkedMeshes)
            {
                if (!_dirtyMeshes.Contains(mesh)) continue;   // 只保存有改动的

                if (GetWritability(mesh, out string path) != MeshWritability.Writable)
                {
                    blocked.Add(mesh);   // 只读网格：不可直接保存
                    continue;
                }

                AssetDatabase.SaveAssetIfDirty(mesh);
                _dirtyMeshes.Remove(mesh);
                saved.Add(path);
            }

            if (saved.Count > 0)
            {
                AssetDatabase.Refresh();
                _saveState = AnyCheckedDirty() ? SaveState.NeedSave : SaveState.Saved;
                Debug.Log($"{LocLog.Prefix} {LocWindow.LogSaved(saved.Count, string.Join("\n", saved))}");
            }

            // 只读网格只能走「另存为」，绝不在这里谎报成功。
            if (blocked.Count > 0)
            {
                string names = string.Join("\n", blocked.Select(m => "· " + m.name));
                string msg = LocWindow.DialogPartialSaveBody(blocked.Count, names);
                Debug.LogError($"{LocLog.Prefix} {msg}");
                EditorUtility.DisplayDialog(LocWindow.DialogPartialSaveTitle, msg, LocWindow.BtnOk);
            }

            Repaint();
        }

        /// <summary>
        /// 把所有【勾选】的网格复制成独立可写的 .asset，并（对场景对象）回填到组件上。
        /// 这是不可写网格（FBX 子资产 / 内置资源）唯一能真正保存的路径。
        ///   勾选 1 个：弹对话框让用户命名并保存到指定文件；
        ///   勾选多个：选一个工程内文件夹，按各自网格名批量生成（自动去重命名）。
        /// </summary>
        private void DuplicateMeshToAsset()
        {
            var entries = _meshEntries.Where(e => e.Selected && e.Mesh).ToList();
            if (entries.Count == 0) return;

            if (entries.Count == 1) DuplicateSingleWithDialog(entries[0]);
            else                    DuplicateManyToFolder(entries);
        }

        /// <summary>单个网格：沿用「命名并保存到指定文件」的对话框，路径体验最好。</summary>
        private void DuplicateSingleWithDialog(MeshEntry entry)
        {
            // 若原网格在 Assets 下，默认存到它旁边，省得用户到处找。
            string dir = "Assets";
            string srcPath = AssetDatabase.GetAssetPath(entry.Mesh);
            if (!string.IsNullOrEmpty(srcPath) && srcPath.StartsWith("Assets/"))
                dir = Path.GetDirectoryName(srcPath)?.Replace('\\', '/') ?? "Assets";

            string savePath = EditorUtility.SaveFilePanelInProject(
                LocWindow.SavePanelTitle,
                $"{entry.Mesh.name}_SmoothNormals",
                "asset",
                LocWindow.SavePanelMessage,
                dir);
            if (string.IsNullOrEmpty(savePath)) return;

            bool reassigned = DuplicateEntryToPath(entry, savePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            SelectMeshEntry(_meshIndex);   // 焦点若指向被替换的条目，刷新到副本
            _saveState = SaveState.Saved;
            Repaint();
            Debug.Log(LocLog.Prefix + " " + (reassigned
                ? LocWindow.LogDuplicatedReassigned(savePath)
                : LocWindow.LogDuplicated(savePath)));
        }

        /// <summary>多个网格：选一个工程内文件夹，按各自网格名批量另存。</summary>
        private void DuplicateManyToFolder(List<MeshEntry> entries)
        {
            string abs = EditorUtility.SaveFolderPanel(
                LocWindow.FolderPanelTitle(entries.Count), "Assets", "");
            if (string.IsNullOrEmpty(abs)) return;

            // 必须落在工程 Assets 目录内，否则 AssetDatabase 无法处理。
            string dataPath = Application.dataPath.Replace('\\', '/');
            abs = abs.Replace('\\', '/');
            if (abs != dataPath && !abs.StartsWith(dataPath + "/"))
            {
                EditorUtility.DisplayDialog(LocWindow.DialogInvalidPathTitle,
                    LocWindow.DialogInvalidPathBody, LocWindow.BtnOk);
                return;
            }
            string folderRel = "Assets" + abs.Substring(dataPath.Length);

            int total = 0, reassignedCount = 0;
            foreach (var entry in entries)
            {
                if (!entry.Mesh) continue;
                // 去重命名：多个同名网格（如都叫 Cube）也不会互相覆盖。
                string path = AssetDatabase.GenerateUniqueAssetPath(
                    $"{folderRel}/{entry.Mesh.name}_SmoothNormals.asset");
                if (DuplicateEntryToPath(entry, path)) reassignedCount++;
                total++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            SelectMeshEntry(Mathf.Clamp(_meshIndex, 0, _meshEntries.Count - 1));
            _saveState = SaveState.Saved;
            Repaint();
            Debug.Log($"{LocLog.Prefix} {LocWindow.LogDuplicatedMany(total, folderRel, reassignedCount)}");
        }

        /// <summary>
        /// 把一个网格条目复制成 projectPath 处的独立 .asset：创建资产、（对场景对象）
        /// 回填组件、并就地把列表条目替换成副本。返回是否回填了组件。
        /// 不负责 SaveAssets / Refresh / 收尾 —— 由调用方统一处理，批量时只刷一次。
        /// </summary>
        private bool DuplicateEntryToPath(MeshEntry entry, string projectPath)
        {
            var copy = Instantiate(entry.Mesh);
            copy.name = Path.GetFileNameWithoutExtension(projectPath);
            AssetDatabase.CreateAsset(copy, projectPath);

            // 记录【组件】的 Undo —— 这个是真的有效，不像 Undo.RecordObject 对网格
            // 顶点数据那样形同虚设。仅【场景对象】有可回填的组件；来自模型 / 预制体
            // 资产、或直选 Mesh 资产时 Owner 为 null，只生成独立 .asset，由用户自行引用。
            bool reassigned = false;
            if (entry.Owner is MeshFilter mf)
            {
                Undo.RecordObject(mf, "Assign Duplicated Mesh");
                mf.sharedMesh = copy;
                EditorUtility.SetDirty(mf);
                reassigned = true;
            }
            else if (entry.Owner is SkinnedMeshRenderer smr)
            {
                Undo.RecordObject(smr, "Assign Duplicated Mesh");
                smr.sharedMesh = copy;
                EditorUtility.SetDirty(smr);
                reassigned = true;
            }

            // 后续生成 / 保存都切到这份可写的副本上；原网格的快照 / 脏标记不再适用。
            _snapshots.Remove(entry.Mesh);
            _dirtyMeshes.Remove(entry.Mesh);

            var newEntry = new MeshEntry
            {
                Mesh          = copy,
                Label         = copy.name,
                Owner         = reassigned ? entry.Owner : null,
                Selected      = true,                 // 保持在勾选集合里
                PreviewMatrix = entry.PreviewMatrix,  // 副本占据原网格位置，沿用其预览变换
            };
            int idx = _meshEntries.IndexOf(entry);
            if (idx >= 0) _meshEntries[idx] = newEntry;
            else          _meshEntries.Add(newEntry);
            return reassigned;
        }

        /// <summary>
        /// 标记焦点 Mesh 已被修改，需要保存。用于焦点范围的清除操作。
        /// 同时把它并入勾选集合 —— 否则清除了一个未勾选的焦点网格，它会变脏
        /// 却不在「保存」的批量范围内，无从落盘。
        /// </summary>
        private void MarkDirty()
        {
            if (_targetMesh)
            {
                _dirtyMeshes.Add(_targetMesh);
                var entry = _meshEntries.FirstOrDefault(e => e.Mesh == _targetMesh);
                if (entry != null) entry.Selected = true;
            }
            _saveState = SaveState.NeedSave;
            Repaint();
        }

        private void DrawWindowHeader()
        {
            var rect = EditorGUILayout.BeginVertical();

            GUILayout.Space(8);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(12);

            // Icon bar
            DrawHeaderIcon();

            GUILayout.Space(10);
            EditorGUILayout.BeginVertical();
            GUILayout.Space(4);
            GUILayout.Label(LocWindow.HeaderTitle, OutlineEditorStyles.Header);
            GUILayout.Label(LocWindow.HeaderSubtitle, OutlineEditorStyles.SubHeader);
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            // 语言切换：缩进到标题文字的左缘（12 空白 + 图标 + 10 空白）。
            GUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(HeaderTextIndent);
            OutlineLocale.DrawSwitch();
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(8);
            EditorGUILayout.EndVertical();

            // Accent line
            EditorGUI.DrawRect(new Rect(0, rect.yMax + 7, _dividerX, 2), OutlineEditorStyles.Accent);
        }
        #endregion

        #region 右侧界面
        private void DrawRightPanelTop()
        {
            GUILayout.Space(12);
            OutlineEditorGUI.DrawSectionHeader(LocWindow.SectionDataOverview, "◈");
            GUILayout.Space(4);
            DrawDataStatusCards();
            GUILayout.Space(8);
        }

        /// <summary>
        /// 通道状态的配色。徽标、UV 通道状态点、其他任何要给状态上色的地方都走这里 ——
        /// 此前徽标与状态点各写一份 switch，两份对「空」给出的灰还差了一点点
        /// （(0.4,0.4,0.5) 对 (0.4,0.42,0.48)），纯属抄漏。
        /// </summary>
        private static Color StateColor(ChannelState state) => state switch
        {
            ChannelState.LikelySmoothNormals => OutlineEditorStyles.Success,
            ChannelState.LegacyUVFormat      => OutlineEditorStyles.Danger,
            ChannelState.HasData             => OutlineEditorStyles.Warning,
            _                                => OutlineEditorStyles.Gray,
        };

        /// <summary>通道状态的完整文案（数据状态卡右上角的徽标）。</summary>
        private static string StateText(ChannelState state) => state switch
        {
            ChannelState.LikelySmoothNormals => LocWindow.StateLikelySmoothNormals,
            ChannelState.LegacyUVFormat      => LocWindow.StateLegacyFormat,
            ChannelState.HasData             => LocWindow.StateHasData,
            _                                => LocWindow.StateEmpty,
        };

        /// <summary>通道状态的简短文案（chip、状态行这类窄处）。</summary>
        private static string ShortState(ChannelState state) => state switch
        {
            ChannelState.LikelySmoothNormals => LocWindow.ShortStateLikelySmoothNormals,
            ChannelState.LegacyUVFormat      => LocWindow.ShortStateLegacyFormat,
            ChannelState.HasData             => LocWindow.ShortStateHasData,
            _                                => LocWindow.ShortStateEmpty,
        };

        private void DrawDataStatusCards()
        {
            // ── Vertex Color ─────────────────────────────────────────
            string varying = LocWindow.ChipVarying, constant = LocWindow.ChipConstant;
            DrawBigStatusCard(
                LocWindow.CardVertexColorTitle,
                LocWindow.CardVertexColorDesc,
                _vertexColorState,
                new[]
                {
                    (LocWindow.VcChannelName("R"), _hasVcr ? varying : constant),
                    (LocWindow.VcChannelName("G"), _hasVcg ? varying : constant),
                    (LocWindow.VcChannelName("B"), _hasVcb ? varying : constant),
                    (LocWindow.VcChannelName("A"), _hasVca ? varying : constant),
                },
                OutlineEditorStyles.Success
            );

            GUILayout.Space(6);

            // ── Tangent ──────────────────────────────────────────────
            DrawBigStatusCard(
                LocWindow.CardTangentTitle,
                LocWindow.CardTangentDesc,
                _tangentState,
                new[]
                {
                    ("Tangent XYZ", ShortState(_tangentState)),
                    ("Tangent W", LocWindow.ChipTangentWConst),
                },
                OutlineEditorStyles.Warning
            );

            GUILayout.Space(6);

            // ── UV Channels ──────────────────────────────────────────
            // 旧格式优先冒泡到总览：它是唯一需要用户动手的状态，被「有数据」盖住
            // 就等于没提醒。（1.7.0 起 UV 通道不会再出现 LikelySmoothNormals ——
            // 两分量八面体与贴图 UV 无从区分，判据说明见 DetectUVChannelState。）
            var uvOverall = _uvStates.Contains(ChannelState.LegacyUVFormat)
                ? ChannelState.LegacyUVFormat
                : (_uvStates.Any(s => s != ChannelState.Empty) ? ChannelState.HasData : ChannelState.Empty);

            var uvItems = new (string, string)[UvChannelCount];
            for (int i = 0; i < UvChannelCount; i++)
                uvItems[i] = ($"TEXCOORD{i}", ShortState(_uvStates[i]));

            DrawBigStatusCard(
                LocWindow.CardUvTitle,
                LocWindow.CardUvDesc,
                uvOverall,
                uvItems,
                OutlineEditorStyles.Accent
            );
        }

        #region UI 数据卡
        private void DrawBigStatusCard(string titleName, string desc, ChannelState state,
                                       (string label, string note)[] items, Color accentColor)
        {
            bool active = state != ChannelState.Empty;

            var bgRect = EditorGUILayout.BeginVertical();
            EditorGUI.DrawRect(new Rect(bgRect.x, bgRect.y, 3, bgRect.height + 10), active ? accentColor : OutlineEditorStyles.Border);

            GUILayout.Space(8);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(10);

            EditorGUILayout.BeginVertical();

            // Title row
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(titleName, OutlineEditorStyles.CardTitle);
            GUILayout.FlexibleSpace();

            // Status badge
            // MinWidth 而非 Width：徽标是本次最长的一批文字，英文「▲ Possibly an old format」
            // 约 115px、日文「▲ 旧フォーマットの可能性」约 100px，定死 104 会把它们裁掉。
            // 前面有 FlexibleSpace 顶着，右对齐的位置不受影响。
            GUILayout.Label(StateText(state), OutlineEditorStyles.Badge(StateColor(state)),
                            GUILayout.MinWidth(104));
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();

            // Desc
            GUILayout.Label(desc, OutlineEditorStyles.MiniDesc);
            GUILayout.Space(4);

            // Sub-items grid：每行最多 4 个，超出换行（TEXCOORD 有 8 个，一行放不下）。
            const int perRow = 4;
            for (int i = 0; i < items.Length; i += perRow)
            {
                EditorGUILayout.BeginHorizontal();
                for (int j = i; j < Mathf.Min(i + perRow, items.Length); j++)
                    OutlineEditorGUI.DrawChannelChip(items[j].label, items[j].note, active, accentColor);
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(10);
            EditorGUILayout.EndVertical();
        }

        #endregion
        #endregion
        #endregion

        #region UI 标题Icon
        /// <summary>页签头部左上角六边形标志的统一尺寸 —— 以「平滑法线生成器」头部的图标为准。</summary>
        private const float HeaderIconSize = 36f;

        /// <summary>
        /// 头部标题文字的左缘 = 左空白 12 + 图标 + 图标与文字间距 10。
        /// 两处头部的语言切换按钮都缩进到这里，与标题左对齐。
        /// </summary>
        private const float HeaderTextIndent = 12f + HeaderIconSize + 10f;

        /// <summary>
        /// 绘制页签头部左上角的六边形标志。两处头部（生成器 / 导入自动烘焙）统一调用此方法，
        /// 尺寸与外观完全一致；此前自动烘焙头部用的是 28px、显得更粗，故收敛到这里。
        /// </summary>
        private void DrawHeaderIcon()
        {
            var iconRect = GUILayoutUtility.GetRect(HeaderIconSize, HeaderIconSize,
                GUILayout.Width(HeaderIconSize));
            OutlineEditorGUI.DrawHexIcon(iconRect, OutlineEditorStyles.Accent);
        }
        #endregion
        
        #region UI 目标对象
        private Object _targetSource;   // 选中来源：场景 GameObject / 模型 / 预制体 / Mesh 资产
        private Mesh _targetMesh;       // 焦点网格：右侧网格信息 / 通道状态 / 预览显示它，清除操作也针对它。
                                        // 生成 / 保存作用于【勾选集合】SelectedMeshes()，未必只有它。
        private Component _targetOwner; // 焦点网格所属的组件（MeshFilter / SkinnedMeshRenderer）；
                                        // 仅【场景对象】非空，用于另存后回填，资产直选时为 null
        private readonly List<MeshEntry> _meshEntries = new List<MeshEntry>();
        private int _meshIndex;           // 焦点网格：右侧网格信息 / 通道状态 / 预览显示它
        private Vector2 _meshListScroll;  // 多网格复选列表的滚动位置

        /// <summary>从一个来源里发现的一条网格候选。</summary>
        // 用 class 而非 struct：列表里要就地翻转 Selected 复选状态，
        // struct 装在 List 里改字段得整条替换，class 直接改即可。
        private class MeshEntry
        {
            public Mesh Mesh;
            public string Label;    // 列表显示，如 "Body (SkinnedMeshRenderer)"
            public Component Owner; // 引用它的、可回填的组件；资产来源时为 null
            public bool Selected;   // 勾选 = 纳入批量编辑（生成 / 保存 / 预览作用于所有勾选项）
            public Matrix4x4 PreviewMatrix; // 相对根对象的变换：多网格预览按各自位置摆放，
                                            // 否则会全叠在原点。务必显式赋值 —— Matrix4x4
                                            // 的默认值是全零而非单位阵。
        }

        /// <summary>当前勾选、可处理的网格集合（生成 / 保存的作用对象）。</summary>
        private List<Mesh> SelectedMeshes() =>
            _meshEntries.Where(e => e.Selected && e.Mesh).Select(e => e.Mesh).Distinct().ToList();

        /// <summary>勾选的网格数量。</summary>
        private int SelectedCount => _meshEntries.Count(e => e.Selected && e.Mesh);

        /// <summary>勾选集合里是否有未保存的网格。</summary>
        private bool AnyCheckedDirty() =>
            _meshEntries.Any(e => e.Selected && e.Mesh && _dirtyMeshes.Contains(e.Mesh));

        /// <summary>全选 / 清空。</summary>
        private void SetAllSelected(bool value)
        {
            foreach (var e in _meshEntries)
                if (e.Mesh) e.Selected = value;
            OnSelectionSetChanged();
        }

        /// <summary>勾选集合变化后：刷新保存状态、把预览取景到新的勾选集合。</summary>
        private void OnSelectionSetChanged()
        {
            _saveState = AnyCheckedDirty() ? SaveState.NeedSave
                       : (_saveState == SaveState.Saved ? SaveState.Saved : SaveState.Clean);
            // 用户主动增减预览内容，相机随之自动兜住全部勾选项。
            FramePreviewToChecked();
            Repaint();
        }

        /// <summary>当前焦点网格条目（右侧信息 / 通道状态 / 预览法线叠加针对它），无则 null。</summary>
        private MeshEntry FocusEntry() =>
            (_meshIndex >= 0 && _meshIndex < _meshEntries.Count) ? _meshEntries[_meshIndex] : null;

        /// <summary>把预览相机取景到所有勾选网格的合并包围盒（按各自 PreviewMatrix 变换后）。</summary>
        private void FramePreviewToChecked()
        {
            bool has = false;
            Bounds combined = default;
            foreach (var e in _meshEntries)
            {
                if (!e.Selected || !e.Mesh) continue;
                var b = TransformBounds(e.Mesh.bounds, e.PreviewMatrix);
                if (!has) { combined = b; has = true; }
                else combined.Encapsulate(b);
            }
            if (!has) return;   // 没有勾选：保持当前取景不动

            _previewPivot = combined.center;
            _previewZoom  = Mathf.Max(0.1f, combined.size.magnitude * 1.6f);
        }

        /// <summary>把对象空间 AABB 用矩阵变换成世界空间 AABB（变换中心 + 三个半轴累加绝对值）。</summary>
        private static Bounds TransformBounds(Bounds b, Matrix4x4 m)
        {
            var center = m.MultiplyPoint3x4(b.center);
            var ext = b.extents;
            var ax = m.MultiplyVector(new Vector3(ext.x, 0, 0));
            var ay = m.MultiplyVector(new Vector3(0, ext.y, 0));
            var az = m.MultiplyVector(new Vector3(0, 0, ext.z));
            var newExt = new Vector3(
                Mathf.Abs(ax.x) + Mathf.Abs(ay.x) + Mathf.Abs(az.x),
                Mathf.Abs(ax.y) + Mathf.Abs(ay.y) + Mathf.Abs(az.y),
                Mathf.Abs(ax.z) + Mathf.Abs(ay.z) + Mathf.Abs(az.z));
            return new Bounds(center, newExt * 2f);
        }

        /// <summary>
        /// 从一次选择中发现所有可处理的网格。支持：
        /// GameObject（场景对象、模型 / 预制体资产 —— 都遍历整个层级取全部网格）、
        /// 以及直接选中的 Mesh 资产（.asset 或 FBX 里的 Mesh 子资产）。
        /// </summary>
        private static List<MeshEntry> DiscoverMeshes(Object sel)
        {
            var list = new List<MeshEntry>();
            if (!sel) return list;

            // 1) 直接是一个 Mesh：Project 里的独立 .asset，或展开 FBX 选中的 Mesh 子资产。
            //    只有它一个，预览摆在原点即可。
            if (sel is Mesh mesh)
            {
                AddEntry(list, mesh, null, null, Matrix4x4.identity);
                return list;
            }

            // 2) 一个 GameObject：场景实例，或 Project 里的模型 / 预制体资产。
            //    两者一致处理：遍历整个层级（含未激活子物体），收集其中全部网格。
            //    场景子物体的组件可回填、资产层级里的组件不回填 —— 由 AddEntry
            //    按组件是否持久化自动区分。
            //    同时记录「相对根对象的变换」= root⁻¹ · 子对象 localToWorld，供多网格
            //    预览按各自位置摆放（否则全叠在原点，多选预览毫无意义）。
            if (sel is GameObject go)
            {
                var rootInv = go.transform.worldToLocalMatrix;
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                    AddEntry(list, mf.sharedMesh, mf, "MeshFilter",
                             rootInv * mf.transform.localToWorldMatrix);
                foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    AddEntry(list, smr.sharedMesh, smr, "SkinnedMeshRenderer",
                             rootInv * smr.transform.localToWorldMatrix);
            }
            return list;
        }

        /// <summary>把一条网格加入候选列表；去重，并只对【场景组件】保留可回填的 Owner。</summary>
        private static void AddEntry(List<MeshEntry> list, Mesh mesh, Component owner, string ownerKind,
                                     Matrix4x4 previewMatrix)
        {
            if (!mesh) return;
            if (list.Any(e => e.Mesh == mesh)) return;   // 同一网格被多个渲染器共用时只列一次

            // 资产层级里的组件不做回填（改动模型 / 预制体资产过于脆弱），
            // Owner 只在【场景对象】上才有意义。
            var backfillOwner = (owner && !EditorUtility.IsPersistent(owner)) ? owner : null;
            string label = owner ? $"{owner.gameObject.name} ({ownerKind})" : mesh.name;
            list.Add(new MeshEntry
            {
                Mesh = mesh, Label = label, Owner = backfillOwner, PreviewMatrix = previewMatrix,
            });
        }
        
        private void DrawTargetSection()
        {
            OutlineEditorGUI.DrawSectionHeader(LocWindow.SectionTarget, "◉");
            EditorGUILayout.BeginVertical(OutlineEditorStyles.DataCard);

            EditorGUI.BeginChangeCheck();
            var newObj = EditorGUILayout.ObjectField(
                new GUIContent(LocWindow.TargetField, LocWindow.TargetFieldTooltip),
                _targetSource, typeof(Object), true);
            if (EditorGUI.EndChangeCheck() && newObj != _targetSource)
            {
                if (!newObj)
                {
                    ClearTarget();
                }
                else if (!SetTargetSource(newObj))
                {
                    // 拖进来的东西里没有可处理的网格：仍显示它，但在下方给出提示。
                    _targetSource = newObj;
                    _meshEntries.Clear();
                    SelectMeshEntry(0);
                }
            }

            if (_targetSource)
            {
                // 多网格来源（模型 / 预制体 / 多渲染器场景对象）：复选列表批量编辑。
                if (_meshEntries.Count > 1)
                    DrawMeshChecklist();

                if (_targetMesh)
                {
                    EditorGUILayout.BeginHorizontal();
                    OutlineEditorGUI.DrawTag(DescribeTargetSource(), OutlineEditorStyles.Accent);
                    OutlineEditorGUI.DrawTag(_targetMesh.name, OutlineEditorStyles.Card * 1.4f);
                    EditorGUILayout.EndHorizontal();

                    if (_meshEntries.Count > 1)
                    {
                        int sel = SelectedCount;
                        EditorGUILayout.HelpBox(
                            sel <= 1 ? LocWindow.ChecklistHintSingle : LocWindow.ChecklistHintMany(sel),
                            MessageType.Info);
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox(LocWindow.TargetNoMesh, MessageType.Warning);
                }
            }
            else
            {
                EditorGUILayout.HelpBox(LocWindow.TargetNone, MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// 多网格来源的复选列表：顶部「全选 / 清空」，下面是可滚动的复选行。
        /// 复选框 = 是否纳入批量编辑；单击行名 = 设为焦点（右侧面板显示它），
        /// 两者互不影响。
        /// </summary>
        private void DrawMeshChecklist()
        {
            // ── 顶部：计数 + 全选 / 清空 ──────────────────────────────
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(LocWindow.MeshListCount(SelectedCount, _meshEntries.Count),
                            OutlineEditorStyles.MiniDesc);
            GUILayout.FlexibleSpace();
            // MinWidth：英文「Select All」、日文「すべて選択」都比中文的两个字宽。
            if (GUILayout.Button(LocWindow.BtnSelectAll, EditorStyles.miniButtonLeft,
                                 GUILayout.MinWidth(44)))
                SetAllSelected(true);
            if (GUILayout.Button(LocWindow.BtnClearSelection, EditorStyles.miniButtonRight,
                                 GUILayout.MinWidth(44)))
                SetAllSelected(false);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(2);

            // ── 滚动复选列表（限高，超出滚动）────────────────────────
            const float rowH = 20f;
            float viewH = Mathf.Clamp(_meshEntries.Count * rowH + 6f, rowH + 6f, 148f);
            _meshListScroll = EditorGUILayout.BeginScrollView(_meshListScroll, GUILayout.Height(viewH));

            for (int i = 0; i < _meshEntries.Count; i++)
            {
                var entry = _meshEntries[i];
                if (!entry.Mesh) continue;
                bool focused = i == _meshIndex;

                var rowRect = EditorGUILayout.BeginHorizontal();
                // 焦点行高亮：先铺底色，控件随后画在上面。
                if (focused && Event.current.type == EventType.Repaint)
                {
                    var rowHighlight = OutlineEditorStyles.Accent;
                    rowHighlight.a = 0.14f;
                    EditorGUI.DrawRect(rowRect, rowHighlight);
                }

                // 复选框：纳入批量编辑。
                bool chk = EditorGUILayout.Toggle(entry.Selected, GUILayout.Width(16));
                if (chk != entry.Selected)
                {
                    entry.Selected = chk;
                    OnSelectionSetChanged();
                }

                // 行名：单击设为焦点，不改变勾选状态。用 label 样式的按钮当作整行热区。
                if (GUILayout.Button((focused ? "▸ " : "    ") + entry.Label,
                                     OutlineEditorStyles.ListRow(focused)))
                    SelectMeshEntry(i);

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
        }

        /// <summary>目标来源的简短描述标签。</summary>
        private string DescribeTargetSource()
        {
            if (_targetOwner is MeshFilter)          return "MeshFilter";
            if (_targetOwner is SkinnedMeshRenderer) return "SkinnedMeshRenderer";
            if (_targetSource is Mesh)               return LocWindow.SourceMeshAsset;
            if (_targetSource is GameObject go)
                return EditorUtility.IsPersistent(go)
                    ? LocWindow.SourceModelOrPrefab : LocWindow.SourceSceneObject;
            return "Mesh";
        }
        
        /// <summary>
        /// 刷新 对象数据
        /// </summary>
        /// <summary>
        /// 判断某个 8-bit 顶点色通道是否承载了逐顶点信息。
        ///
        /// 判据是「取值是否有变化」，而不是旧的「是否 != 128」。旧判据两头都不准：
        ///   - 假阳性：导入时带白色顶点色 (255,255,255,255) 的网格，四个通道全被
        ///     报成「有数据」。
        ///   - 假阴性：编码值恰好等于 128 的顶点会被当成「空」。
        /// 全部顶点取值相同 ⇒ 该通道是常量，几乎可以肯定没承载逐顶点数据。
        /// </summary>
        private static bool HasVaryingData(Color32[] colors, System.Func<Color32, byte> selector)
        {
            if (colors == null || colors.Length == 0) return false;
            byte first = selector(colors[0]);
            for (int i = 1; i < colors.Length; i++)
                if (selector(colors[i]) != first) return true;
            return false;
        }

        /// <summary>
        /// 切线通道状态。
        ///
        /// 旧判据是 w != ±1，但写入器保留原始 w、RecalculateTangents 也给 ±1，
        /// 于是它【恒为 false】：徽标永远不亮，「清除切线」按钮永远点不动。
        ///
        /// 新判据基于正交性：真正的切线按构造与顶点法线正交，|dot(T,N)| ≈ 0；
        /// 而本工具存进 tangent.xyz 的是平滑法线，与顶点法线大体同向，|dot| 接近 1。
        /// 这是统计判断，但远胜于一个恒假的条件。
        /// </summary>
        private ChannelState DetectTangentState()
        {
            var tangents = _targetMesh.tangents;
            if (tangents == null || tangents.Length == 0) return ChannelState.Empty;

            var normals = _targetMesh.normals;
            if (normals == null || normals.Length != tangents.Length) return ChannelState.HasData;

            int sampled = 0, nonOrthogonal = 0;
            int step = Mathf.Max(1, tangents.Length / 256);
            for (int i = 0; i < tangents.Length; i += step)
            {
                var t = new Vector3(tangents[i].x, tangents[i].y, tangents[i].z);
                if (t.sqrMagnitude < 1e-8f) continue;
                sampled++;
                if (Mathf.Abs(Vector3.Dot(t.normalized, normals[i].normalized)) > 0.3f)
                    nonOrthogonal++;
            }

            if (sampled == 0) return ChannelState.HasData;
            return nonOrthogonal / (float)sampled > 0.5f
                ? ChannelState.LikelySmoothNormals
                : ChannelState.HasData;
        }

        /// <summary>
        /// TEXCOORD 通道状态。
        ///
        /// 1.7.0 起本工具写【2 分量】八面体坐标，与贴图 UV 在维度和值域上都一样，
        /// 数值上完全无从区分 —— 所以新格式的数据最高只能报「有数据」。这是改用
        /// 两分量换来省 4 字节/顶点的真实代价。
        ///
        /// 与此同时，原本用来认「可能是平滑法线」的那个信号恰好反了过来：
        /// 【3 分量且近似单位长】现在意味着这是 1.6.x 及更早写入的旧格式数据，1.7.0 无法
        /// 解码、必须重新烘焙。这是全链路唯一能提醒到人的地方，因此把它保留下来
        /// 改判为 <see cref="ChannelState.LegacyUVFormat"/>。
        ///
        /// 它只是强信号，不是判定 —— 会误命中的真实情形见该枚举成员的注释。
        /// </summary>
        private ChannelState DetectUVChannelState(int channel)
        {
            var attr = UnityEngine.Rendering.VertexAttribute.TexCoord0 + channel;
            int dim  = _targetMesh.GetVertexAttributeDimension(attr);
            if (dim == 0) return ChannelState.Empty;
            if (dim != 3) return ChannelState.HasData;

            var list = new List<Vector3>();
            _targetMesh.GetUVs(channel, list);
            if (list.Count == 0) return ChannelState.Empty;

            int sampled = 0, unitLike = 0;
            int step = Mathf.Max(1, list.Count / 256);
            for (int i = 0; i < list.Count; i += step)
            {
                sampled++;
                if (Mathf.Abs(list[i].sqrMagnitude - 1f) < 0.05f) unitLike++;
            }

            return sampled > 0 && unitLike / (float)sampled > 0.9f
                ? ChannelState.LegacyUVFormat
                : ChannelState.HasData;
        }

        /// <summary>焦点网格的健康检查报告（随数据状态一起刷新）；无目标时为 null。</summary>
        private MeshHealthReport _healthReport;

        private void RefreshDataStatus()
        {
            // 网格数据变了：重建缓存并让解码缓存失效。
            _dataVersion++;
            RebuildMeshCache();

            if (!_targetMesh)
            {
                _hasVcr = _hasVcg = _hasVcb = _hasVca = false;
                _vertexColorState = ChannelState.Empty;
                _tangentState     = ChannelState.Empty;
                for (int i = 0; i < UvChannelCount; i++) _uvStates[i] = ChannelState.Empty;
                _healthReport = null;
                return;
            }

            // ── 顶点色 ───────────────────────────────────────────────
            var colors = _targetMesh.colors32;
            _hasVcr = HasVaryingData(colors, c => c.r);
            _hasVcg = HasVaryingData(colors, c => c.g);
            _hasVcb = HasVaryingData(colors, c => c.b);
            _hasVca = HasVaryingData(colors, c => c.a);

            // 顶点色经八面体编码后是普通的 [0,1] 数值，与任意顶点色在数值上
            // 无法区分，因此这里只报告「有 / 无」，不做没有根据的猜测。
            _vertexColorState = (colors != null && colors.Length > 0)
                ? ChannelState.HasData
                : ChannelState.Empty;

            _tangentState = DetectTangentState();

            for (int ch = 0; ch < UvChannelCount; ch++)
                _uvStates[ch] = DetectUVChannelState(ch);

            _healthReport = OutlineMeshValidator.Validate(_targetMesh, _storageMode, _normalSpace);
        }
        
        /// <summary>
        /// 把一次选择设为当前目标：发现其中的网格，成功则切换（并重置到第一个网格）。
        /// 若该来源不含可处理网格，返回 false 且不改动当前目标。
        /// </summary>
        private bool SetTargetSource(Object source)
        {
            // 同一来源重复触发（Selection 事件会反复回调）：保留已设置的勾选与焦点，
            // 否则用户在场景里随手一点就会把精心勾好的复选列表重置掉。
            if (source && source == _targetSource && _meshEntries.Count > 0) return true;

            var entries = DiscoverMeshes(source);
            if (entries.Count == 0) return false;

            _targetSource = source;
            _meshEntries.Clear();
            _meshEntries.AddRange(entries);

            // 换了来源，旧快照不再适用（还原是会话级、按来源清空）。
            _snapshots.Clear();

            // 初始化即【全选】：默认对来源里的全部网格生效，预览也一并显示；
            // 不想批量处理时再手动取消勾选即可。
            foreach (var e in _meshEntries)
                if (e.Mesh) e.Selected = true;

            SelectMeshEntry(0);
            FramePreviewToChecked();   // 新来源加载后把相机兜住全部勾选项
            return true;
        }

        /// <summary>切换焦点网格（右侧信息 / 通道状态 / 预览显示它），不改变勾选集合。</summary>
        private void SelectMeshEntry(int index)
        {
            if (_meshEntries.Count == 0)
            {
                _meshIndex   = 0;
                _targetMesh  = null;
                _targetOwner = null;
            }
            else
            {
                _meshIndex   = Mathf.Clamp(index, 0, _meshEntries.Count - 1);
                _targetMesh  = _meshEntries[_meshIndex].Mesh;
                _targetOwner = _meshEntries[_meshIndex].Owner;
            }
            AfterTargetMeshChanged();
        }

        /// <summary>清空目标（ObjectField 被置空时）。</summary>
        private void ClearTarget()
        {
            _targetSource = null;
            _meshEntries.Clear();
            _snapshots.Clear();
            SelectMeshEntry(0);   // 会把 _targetMesh / _targetOwner 归零并收尾
        }

        /// <summary>
        /// 焦点网格变更后的统一收尾：保存状态、预览取景、数据状态。
        /// （快照按来源/还原清空，不随焦点切换处理。）
        /// </summary>
        private void AfterTargetMeshChanged()
        {
            // 保存状态跟着【勾选集合】走：只要有勾选的网格未保存，警告就得在。
            // 无条件重置成 Clean 会静默丢掉警告，让用户以为改动已经落盘。
            // （快照按来源/还原清空，不随焦点切换清除 —— 批量还原要跨网格生效。）
            _saveState = AnyCheckedDirty() ? SaveState.NeedSave : SaveState.Clean;

            // 预览取景不在这里做 —— 切换焦点（单击网格名去看它的通道状态）不该挪动
            // 相机。取景只在「来源加载 / 勾选集合变化 / 重置视角」时发生。
            RefreshDataStatus();
        }
        #endregion
        
        #region UI Mesh信息列表
        private void DrawMeshInfoSection()
        {
            _foldoutMeshInfo = OutlineEditorGUI.DrawFoldout(_foldoutMeshInfo, LocWindow.SectionMeshInfo, "▦");
            if (!_foldoutMeshInfo) return;

            EditorGUILayout.BeginVertical(OutlineEditorStyles.DataCard);

            // 全部读缓存。这里原本每帧都会 marshal 一次 triangles（整份索引数组！）
            // 外加 normals / tangents / colors32。（UV 那 8 次 GetUVs 已随「改显示
            // 分量数」一并去掉 —— 现在读的是顶点布局元数据，不再拷贝数组。）
            if (_meshCache == null)
            {
                GUILayout.Label(LocWindow.MeshInfoNone, OutlineEditorStyles.SubHeader);
            }
            else
            {
                OutlineEditorGUI.DrawInfoRow(LocWindow.MeshInfoVertices,  _meshCache.VertexCount.ToString("N0"));
                OutlineEditorGUI.DrawInfoRow(LocWindow.MeshInfoTriangles, _meshCache.TriangleCount.ToString("N0"));
                OutlineEditorGUI.DrawInfoRow(LocWindow.MeshInfoSubMesh,   _meshCache.SubMeshCount.ToString());
                OutlineEditorGUI.DrawInfoRow(LocWindow.MeshInfoNormals,   _meshCache.HasNormals  ? "✓" : "✗");
                OutlineEditorGUI.DrawInfoRow(LocWindow.MeshInfoTangents,  _meshCache.HasTangents ? "✓" : "✗");
                OutlineEditorGUI.DrawInfoRow(LocWindow.MeshInfoColors,    _meshCache.HasColors   ? "✓" : "✗");

                for (int ch = 0; ch < UvChannelCount; ch++)
                {
                    int dim = _meshCache.UvDims[ch];
                    OutlineEditorGUI.DrawInfoRow($"TEXCOORD{ch}", dim > 0 ? LocWindow.MeshInfoUvDim(dim) : "—");
                }
            }

            EditorGUILayout.EndVertical();
        }
        #endregion
        
        #region UI 存储方式
        public enum StorageMode { VertexColor, TangentSpace, UV }
        
        /// <summary>
        /// 顶点色通道，组合类型。
        /// </summary>
        public enum VertexColorChannel
        {
            RG,   // R=法线X  G=法线Y
            GB,   // G=法线X  B=法线Y
            BA,   // B=法线X  A=法线Y
        }

        /// <summary>
        /// 平滑法线写在哪个空间里 —— 与 <see cref="StorageMode"/>（写进哪个通道）正交的维度。
        /// 取值必须与 Shader/OutlineSmoothNormals.hlsl 的 OSN_SPACE_* 一致。
        ///
        /// Object   绑定姿势下的对象空间方向，解码即用。静态模型用它最省。
        /// Tangent  相对该顶点自身 TBN 的坐标，解码时用【蒙皮后】的法线与切线重建基。
        ///          顶点色 / TEXCOORD 不参与蒙皮，对象空间方向在 SkinnedMeshRenderer
        ///          上会停留在绑定姿势、导致描边随动画撕开；切线空间坐标是蒙皮不变量，
        ///          因此是默认值。仅对顶点色 / TEXCOORD 有意义 —— 切线存储模式下会自噬
        ///          掉重建所需的基，且本就无此必要（Unity 会蒙皮 tangent.xyz）。
        /// </summary>
        public enum NormalSpace { Object, Tangent }

        private StorageMode _storageMode = StorageMode.VertexColor;
        // Vertex color channel pair
        private VertexColorChannel _vcChannel = VertexColorChannel.BA;

        // 默认切线空间：它对静态模型与蒙皮模型都正确，而对象空间只对静态模型正确。
        // 让默认值覆盖更广的情形，用户不必先踩一次「动画一跑描边就撕开」才知道要改。
        private NormalSpace _normalSpace = NormalSpace.Tangent;

        // UV 通道一律以 TEXCOORDn 命名，取值与 mesh.SetUVs(n) 的索引恒等对应。
        // 不用「UV1/UV2」这类叫法：Unity 自己的 mesh.uv2 就是 TEXCOORD1，
        // 名字和索引差一位，此前工具与 Shader 正是因此整体错开了一格。
        // 通道名（含「TEXCOORD0 是主贴图 UV」这句提示）随语言变化，移到
        // LocWindow.UvChannelNames 按语言缓存 —— 详见那里的注释。
        private int _uvChannel = 1; // 默认 TEXCOORD1，避开主贴图 UV

        private void DrawStorageModeSection()
        {
            OutlineEditorGUI.DrawSectionHeader(LocWindow.SectionStorageMode, "◈");
            EditorGUILayout.BeginVertical(OutlineEditorStyles.DataCard);

            // Tabs
            EditorGUILayout.BeginHorizontal();
            DrawModeTab(LocWindow.ModeTabVertexColor,    StorageMode.VertexColor,
                        LocWindow.TooltipVertexColor);
            DrawModeTab(LocWindow.ModeTabTangentChannel, StorageMode.TangentSpace,
                        LocWindow.TooltipTangentChannel);
            DrawModeTab(LocWindow.ModeTabUV,             StorageMode.UV,
                        LocWindow.TooltipUVChannel);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(8);

            switch (_storageMode)
            {
                case StorageMode.VertexColor:
                    DrawVertexColorModeUI();
                    break;
                case StorageMode.TangentSpace:
                    DrawTangentModeUI();
                    break;
                case StorageMode.UV:
                    DrawUVModeUI();
                    break;
            }

            GUILayout.Space(8);
            DrawNormalSpaceUI();

            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// 存储空间选择 —— 与「存进哪个通道」正交的另一维度，故独立成块画在模式 UI 之下。
        ///
        /// 切线通道模式下整块禁用：那时切线本身就是数据，没有基可供重建，转换会自噬。
        /// 该模式也不需要 —— Unity 会把 tangent.xyz 当方向一起蒙皮。
        /// </summary>
        private void DrawNormalSpaceUI()
        {
            bool applicable = _storageMode != StorageMode.TangentSpace;

            GUILayout.Label(
                new GUIContent(LocWindow.LabelStorageSpace, LocWindow.LabelStorageSpaceTooltip),
                OutlineEditorStyles.FieldCaption);
            GUILayout.Space(2);

            using (new EditorGUI.DisabledScope(!applicable))
            {
                EditorGUILayout.BeginHorizontal();
                DrawNormalSpaceTab(LocWindow.SpaceTabObject,  NormalSpace.Object,
                                   LocWindow.TooltipObjectSpace);
                DrawNormalSpaceTab(LocWindow.SpaceTabTangent, NormalSpace.Tangent,
                                   LocWindow.TooltipTangentSpace);
                EditorGUILayout.EndHorizontal();
            }

            // 「为什么整块是灰的」必须常驻可见 —— 那是个反常状态，藏进 Tooltip
            // 等于要用户先去悬停一个点不动的按钮才能知道原因。
            // 两个选项各自的适用场景则走 Tooltip，见 TooltipObjectSpace / TooltipTangentSpace。
            if (!applicable)
            {
                GUILayout.Space(4);
                EditorGUILayout.HelpBox(LocWindow.StorageSpaceNotApplicable, MessageType.Info);
            }
        }

        private void DrawNormalSpaceTab(string label, NormalSpace space, string tooltip)
        {
            if (OutlineEditorGUI.DrawSegmentBlock(new GUIContent(label, tooltip),
                                                  _normalSpace == space, 34f))
            {
                _normalSpace = space;
                RefreshHealthReport();
            }
        }

        private void DrawModeTab(string label, StorageMode mode, string tooltip)
        {
            if (OutlineEditorGUI.DrawSegmentBlock(new GUIContent(label, tooltip),
                                                  _storageMode == mode, 42f))
            {
                _storageMode = mode;
                RefreshHealthReport();
            }
        }

        /// <summary>
        /// 只重算健康检查报告。它的判据同时依赖存储模式与存储空间（切线空间的基校验
        /// 以二者为门），而 RefreshDataStatus 只在切换目标网格 / 生成 / 清除时触发 ——
        /// 光切页签不刷新的话，面板会一直显示上一次选择下的结论，最坏是缓存着一份
        /// 「无异常」而当前选择其实是 Error，用户看不到任何警告。
        ///
        /// 不复用 RefreshDataStatus：通道状态、UV 统计那些都与这两个字段无关，
        /// 每次点页签全量重扫没有必要。
        /// </summary>
        private void RefreshHealthReport()
        {
            _healthReport = _targetMesh != null
                ? OutlineMeshValidator.Validate(_targetMesh, _storageMode, _normalSpace)
                : null;
        }

        #region UI 存储方式-顶点色
        /// <summary>
        /// 顶点色模式 UI：选择 RG / GB / BA 存储对，并用颜色指示 RGBA 各通道的数据状态。
        /// </summary>
        private void DrawVertexColorModeUI()
        {
            EditorGUILayout.BeginVertical(OutlineEditorStyles.InnerCard);

            // ── 通道选择 ────────────────────────────────────────────
            GUILayout.Label(LocWindow.LabelVcChannelPair, OutlineEditorStyles.FieldCaption);
            GUILayout.Space(2);
            EditorGUILayout.BeginHorizontal();
            DrawVcChannelTab("RG", VertexColorChannel.RG);
            DrawVcChannelTab("GB", VertexColorChannel.GB);
            DrawVcChannelTab("BA", VertexColorChannel.BA);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(8);

            // ── RGBA 各通道状态 ──────────────────────────────────────
            GUILayout.Label(LocWindow.LabelVcChannelStatus, OutlineEditorStyles.FieldCaption);
            GUILayout.Space(2);

            // 当前选中的通道对写入的是哪两个通道
            bool rIsWrite = _vcChannel == VertexColorChannel.RG;
            bool gIsWrite = _vcChannel == VertexColorChannel.RG || _vcChannel == VertexColorChannel.GB;
            bool bIsWrite = _vcChannel == VertexColorChannel.GB || _vcChannel == VertexColorChannel.BA;
            bool aIsWrite = _vcChannel == VertexColorChannel.BA;

            DrawVcChannelStatus(LocWindow.VcChannelName("R"), _hasVcr, rIsWrite, LocWindow.VcRoleR);
            DrawVcChannelStatus(LocWindow.VcChannelName("G"), _hasVcg, gIsWrite, LocWindow.VcRoleG);
            DrawVcChannelStatus(LocWindow.VcChannelName("B"), _hasVcb, bIsWrite, LocWindow.VcRoleB);
            DrawVcChannelStatus(LocWindow.VcChannelName("A"), _hasVca, aIsWrite, LocWindow.VcRoleA);

            GUILayout.Space(4);
            EditorGUILayout.HelpBox(LocWindow.VcModeHelp, MessageType.None);

            // ── 清除按钮 ─────────────────────────────────────────────
            GUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            OutlineEditorGUI.DrawClearChannelButton(LocWindow.BtnClearPair("RG"), _hasVcr || _hasVcg, () => ClearVertexColorChannels(true, true, false, false));
            OutlineEditorGUI.DrawClearChannelButton(LocWindow.BtnClearPair("GB"), _hasVcg || _hasVcb, () => ClearVertexColorChannels(false, true, true, false));
            OutlineEditorGUI.DrawClearChannelButton(LocWindow.BtnClearPair("BA"), _hasVcb || _hasVca, () => ClearVertexColorChannels(false, false, true, true));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        /// <summary>单个通道选择 Tab 按钮</summary>
        private void DrawVcChannelTab(string label, VertexColorChannel target)
        {
            if (OutlineEditorGUI.DrawSegmentChip(label, _vcChannel == target, 26f))
                _vcChannel = target;
        }

        /// <summary>绘制单个 RGBA 通道的状态行</summary>
        private void DrawVcChannelStatus(string channelName, bool hasData, bool isWriteTarget, string roleDesc)
        {
            Color dotColor;
            string desc;

            if (isWriteTarget)
            {
                // 当前选中要写入的通道
                dotColor = Color.white;
                desc     = hasData ? LocWindow.VcWillOverwrite(roleDesc)
                                   : LocWindow.VcWillWrite(roleDesc);
            }
            else if (hasData)
            {
                dotColor = OutlineEditorStyles.Warning;   // 黄色：有数据但不是写入目标
                desc     = LocWindow.VcHasDataNotTarget;
            }
            else
            {
                dotColor = new Color(0.4f, 0.42f, 0.48f);   // 灰色：空
                desc     = LocWindow.VcNoData;
            }

            OutlineEditorGUI.DrawStatusIndicator(channelName, desc, dotColor);
        }
        #endregion

        #region UI 存储方式-切线通道
        /// <summary>
        /// 切线通道模式 UI：tangent.xyz 直接存对象空间平滑法线，w 恒为 1。
        /// 该模式会覆盖网格原始切线，必须明确告警。
        ///
        /// 注意与「存储空间 = 切线空间」区分：那是把方向写成相对 TBN 的坐标，
        /// 存进顶点色 / TEXCOORD，切线保持原样、法线贴图照常可用。本模式恰恰相反。
        /// </summary>
        private void DrawTangentModeUI()
        {
            EditorGUILayout.BeginVertical(OutlineEditorStyles.InnerCard);
            bool tangentLikely = _tangentState == ChannelState.LikelySmoothNormals;
            OutlineEditorGUI.DrawStatusIndicator("Tangent XYZ", ShortState(_tangentState), tangentLikely);
            OutlineEditorGUI.DrawStatusIndicator("Tangent W", LocWindow.TangentWDesc, tangentLikely);
            GUILayout.Space(4);

            // 此处原本写的是「兼容大多数标准 Shader」—— 恰好说反了。
            // 覆盖 tangent.xyz 正是对标准 Shader 兼容性破坏最大的做法。
            EditorGUILayout.HelpBox(LocWindow.TangentModeHelp, MessageType.Warning);

            GUILayout.Space(4);
            OutlineEditorGUI.DrawClearChannelButton(LocWindow.BtnRecalcTangents,
                                   _tangentState != ChannelState.Empty, ClearTangents);
            EditorGUILayout.EndVertical();
        }
        #endregion

        #region UI 存储方式-UV通道
        /// <summary>
        /// TEXCOORD0 就是模型的主贴图 UV（mesh.uv）。写入它会毁掉贴图映射，
        /// 且影响所有引用该 sharedMesh 的对象，因此需要显式确认。
        /// </summary>
        private bool IsRiskyUVChannel(int channel) => channel == 0 && _uvStates[0] != ChannelState.Empty;

        /// <summary>
        /// UV 通道模式 UI：选择 TEXCOORD 通道，展示各通道数据状态，
        /// 并对「写入主贴图 UV」这一破坏性操作给出警告。
        /// </summary>
        private void DrawUVModeUI()
        {
            EditorGUILayout.BeginVertical(OutlineEditorStyles.InnerCard);

            // 通道名本身就很长（「TEXCOORD0  (mesh.uv — main texture UV)」近 200px），
            // 而默认 labelWidth 会先吃掉 150px，剩给下拉的还不到 100px。这里把标签压到
            // 刚够放下「存储通道 / Storage channel / 保存チャンネル」，把宽度让给内容。
            float prevLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 92f;
            try
            {
                _uvChannel = EditorGUILayout.Popup(LocWindow.LabelUvStorageChannel, _uvChannel,
                                                   LocWindow.UvChannelNames);
            }
            finally
            {
                EditorGUIUtility.labelWidth = prevLabelWidth;
            }

            GUILayout.Space(4);
            for (int i = 0; i < UvChannelCount; i++)
            {
                bool isSelected = i == _uvChannel;
                var  state      = _uvStates[i];
                bool hasData    = state != ChannelState.Empty;

                string desc;
                Color  dotColor;
                if (isSelected)
                {
                    desc     = hasData ? LocWindow.UvSelectedOverwrite(ShortState(state))
                                       : LocWindow.UvSelectedWrite;
                    dotColor = Color.white;
                }
                else
                {
                    desc     = ShortState(state);
                    dotColor = StateColor(state);
                }

                // 状态行 + 右侧清除按钮
                EditorGUILayout.BeginHorizontal();
                OutlineEditorGUI.DrawStatusIndicator($"TEXCOORD{i}", desc, dotColor);
                GUILayout.FlexibleSpace();
                int capturedIndex = i;
                GUI.enabled = hasData;
                if (GUILayout.Button(LocWindow.BtnClear, GUILayout.MinWidth(44), GUILayout.Height(16)))
                    TryClearUV(capturedIndex);
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();
            }

            GUILayout.Space(4);

            bool risky  = IsRiskyUVChannel(_uvChannel);
            bool legacy = _uvStates[_uvChannel] == ChannelState.LegacyUVFormat;

            if (risky)
            {
                EditorGUILayout.HelpBox(LocWindow.UvRiskyHelp, MessageType.Error);
            }

            // 旧格式提示与主贴图 UV 警告可以同时成立，各自独立显示。
            if (legacy)
            {
                EditorGUILayout.HelpBox(LocWindow.UvLegacyHelp(_uvChannel), MessageType.Warning);
            }
            else if (!risky)
            {
                EditorGUILayout.HelpBox(LocWindow.UvModeHelp, MessageType.None);
            }
            EditorGUILayout.EndVertical();
        }

        /// <summary>清除前对主贴图 UV 做二次确认，避免一键静默毁掉贴图映射。</summary>
        private void TryClearUV(int channel)
        {
            if (channel == 0 &&
                !EditorUtility.DisplayDialog(
                    LocWindow.DialogClearUv0Title,
                    LocWindow.DialogClearUv0Body(_targetMesh.name),
                    LocWindow.BtnConfirmClear, LocWindow.BtnCancel))
                return;

            ClearUV(channel);
        }
        #endregion
        #endregion

        #region UI 生成平滑法线
        private float _mergeTolerance = OutlineSmoothNormalsCalculator.DefaultMergeTolerance;

        /// <summary>
        /// 生成平滑法线的 UI 区域：合并容差 + 一个大按钮，
        /// 按钮显示当前选定的存储方式与目标通道。仅在有有效目标网格时可点击。
        /// </summary>
        private void DrawGenerateSection()
        {
            OutlineEditorGUI.DrawSectionHeader(LocWindow.SectionGenerate, "◈");

            EditorGUILayout.BeginVertical(OutlineEditorStyles.DataCard);

            DrawHealthCard();

            int  selCount    = SelectedCount;
            bool canGenerate = selCount > 0;
            GUI.enabled = canGenerate;

            // ── 合并容差 ─────────────────────────────────────────────
            _mergeTolerance = EditorGUILayout.Slider(
                new GUIContent(LocWindow.LabelMergeTolerance, LocWindow.MergeToleranceTooltip),
                _mergeTolerance,
                OutlineSmoothNormalsCalculator.MinMergeTolerance,
                OutlineSmoothNormalsCalculator.MaxMergeTolerance);

            if (_mergeTolerance > 0.005f)
            {
                EditorGUILayout.HelpBox(LocWindow.MergeToleranceTooLarge, MessageType.Warning);
            }

            GUILayout.Space(4);

            // Big generate button
            // wordWrap：英文的「▶ Generate Smooth Normals → Vertex Color ×3」比中文长近一倍，
            // 左栏拖到下限（300）时按钮里放不下。不换行就会被硬裁掉半个通道名 ——
            // 而通道名恰恰是这颗按钮最需要看清的部分。44px 高度足够容纳两行 13px 文字。
            string countSuffix = selCount > 1 ? $"  ×{selCount}" : "";

            if (OutlineEditorGUI.DrawAccentButton(
                    new GUIContent(LocWindow.BtnGenerate(ShortModeLabel(), countSuffix)),
                    canGenerate ? OutlineEditorStyles.Accent : Color.gray,
                    OutlineEditorStyles.OnAccent,
                    fontSize: 13, height: 44f, bold: true, wordWrap: true))
                TryGenerateSmoothNormals();

            GUI.enabled = true;
            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// 在「生成」区域顶部展示焦点网格的健康检查报告；无异常时不占位。
        /// 数据在 RefreshDataStatus 时算好并缓存，这里只负责画。
        /// </summary>
        private void DrawHealthCard()
        {
            var report = _healthReport;
            if (report == null || report.IsHealthy) return;

            bool prevEnabled = GUI.enabled;
            GUI.enabled = true;

            var msgType = report.HasError   ? MessageType.Error
                        : report.HasWarning ? MessageType.Warning
                        :                     MessageType.Info;

            string body = LocWindow.HealthCardTitle;
            foreach (var issue in report.Issues)
                body += "\n•  " + issue.Message;

            EditorGUILayout.HelpBox(body, msgType);
            GUILayout.Space(6);

            GUI.enabled = prevEnabled;
        }
        #endregion

        #region UI 预览描边渲染
        // ─────────────────────────────────────────────────────────────
        //  Inline Preview
        // ─────────────────────────────────────────────────────────────
        private PreviewRenderUtility _previewUtil;
        private Material _previewBaseMat;
        private Material _previewOutlineMat;

        /// <summary>法线叠加层的线段缓冲，复用以避免每次 Repaint 重新分配。</summary>
        private readonly List<Vector3> _normalLineBuffer = new List<Vector3>();

        // camera orbit
        private Vector2 _previewOrbit  = new Vector2(30f, -20f);
        private float   _previewZoom   = 3f;
        private Vector3 _previewPivot  = Vector3.zero;
        private bool    _previewDragging;
        private Vector2 _previewLastMouse;

        // outline params
        private float _outlineWidth  = 0.015f;   // 与 Outline.shader 的默认值一致
        private int   _outlineWidthMode;         // 0 = 屏幕空间, 1 = 世界空间
        private Color _outlineColor  = Color.white;
        private bool  _showBase      = true;
        private bool  _showOutline   = true;
        private Color _baseColor     = new Color(0.8f, 0.8f, 0.8f);
        private Color _previewBgColor = new Color(0.53f, 0.81f, 0.98f);
        private float _smoothness    = 0.5f;
        private float _metallic;

        // normal visualization
        private bool  _showNormals          = true;
        private float _normalLength         = 0.05f;
        private Color _normalColor          = new Color(0.2f, 1f, 0.4f);

        // original normal visualization
        private bool  _showOriginalNormals;
        private Color _originalNormalColor  = new Color(0.3f, 0.5f, 1f);


        private static readonly int PropGlossiness  = Shader.PropertyToID("_Glossiness");
        private static readonly int PropMetallic     = Shader.PropertyToID("_Metallic");
        private static readonly int PropOutlineColor = Shader.PropertyToID("_OutlineColor");
        private static readonly int PropOutlineWidth = Shader.PropertyToID("_OutlineWidth");
        private static readonly int PropOutlineWidthMode = Shader.PropertyToID("_OutlineWidthMode");
        private static readonly int PropStorageMode  = Shader.PropertyToID("_StorageMode");
        private static readonly int PropUVChannel    = Shader.PropertyToID("_UVChannel");
        private static readonly int PropVcChannel    = Shader.PropertyToID("_VCChannel");
        private static readonly int PropNormalSpace  = Shader.PropertyToID("_NormalSpace");
        
        private void DrawPreviewLaunchPanel()
        {
            if (_previewUtil == null) SetupPreviewRenderer();

            // Section header（在 ScrollView 外，固定高度）
            GUILayout.Space(4);
            OutlineEditorGUI.DrawSectionHeader(LocWindow.SectionOutlinePreview, "◉");
            GUILayout.Space(4);

            float paramW   = PreviewParamPanelWidth;
            float totalW   = position.width - _dividerX - 16f;
            float previewW = Mathf.Max(MinPreviewWidth, totalW - paramW - 2f);

            // 用 GUILayoutUtility.GetRect + ExpandHeight 让 Layout 分配所有剩余高度
            EditorGUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));

            // ── Viewport ──────────────────────────────────────────────
            var previewRect = GUILayoutUtility.GetRect(previewW, previewW,
                GUILayout.Width(previewW), GUILayout.ExpandHeight(true));
            DrawInlineViewport(previewRect);

            // ── Divider ───────────────────────────────────────────────
            EditorGUI.DrawRect(new Rect(previewRect.xMax, previewRect.y, 2, previewRect.height), OutlineEditorStyles.Border);

            // ── Params ────────────────────────────────────────────────
            EditorGUILayout.BeginVertical(GUILayout.Width(paramW));
            DrawInlinePreviewParams();
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }
        
        private void DrawInlineViewport(Rect r)
        {
            // 预览显示【勾选】的网格：没有勾选就不渲染任何东西。
            if (SelectedCount == 0)
            {
                EditorGUI.DrawRect(r, new Color(0.11f, 0.12f, 0.15f));
                GUI.Label(r, _meshEntries.Count > 0
                    ? LocWindow.PreviewNoneChecked : LocWindow.PreviewNoTarget,
                    OutlineEditorStyles.ViewportEmpty);
                return;
            }

            // 输入必须在所有事件上处理，否则相机操作会失灵。
            HandlePreviewCameraControl(r);

            // 除此以外的一切只在 Repaint 做。此前整段（BeginPreview → camera.Render
            // → EndPreview）没有任何守卫，于是 Layout 和每一次 MouseMove 都会分配
            // 一张 RenderTexture 并跑一遍完整的离屏渲染。
            if (Event.current.type != EventType.Repaint) return;

            _previewUtil.BeginPreview(r, GUIStyle.none);
            _previewUtil.camera.backgroundColor = _previewBgColor;

            var camPos = _previewPivot + Quaternion.Euler(_previewOrbit.y, _previewOrbit.x, 0) * new Vector3(0, 0, _previewZoom);
            _previewUtil.camera.transform.position = camPos;
            _previewUtil.camera.transform.LookAt(_previewPivot);

            if (_showBase && _previewBaseMat)
            {
                ApplyBaseMatParams();
                DrawCheckedMeshes(_previewBaseMat);
            }
            if (_showOutline && _previewOutlineMat)
            {
                UpdatePreviewOutlineMat();
                DrawCheckedMeshes(_previewOutlineMat);
            }

            _previewUtil.camera.Render();
            var tex = _previewUtil.EndPreview();
            GUI.DrawTexture(r, tex, ScaleMode.StretchToFill, false);

            // Overlay: mode badge
            // 切线通道恒为对象空间，标注出来只会让人以为它可选，故留空。
            string spaceLabel = _storageMode == StorageMode.TangentSpace ? "" :
                                " · " + (_normalSpace == NormalSpace.Tangent
                                    ? LocWindow.ShortSpaceTangent : LocWindow.ShortSpaceObject);

            // 底板宽度按文字实测，并夹在视口内：此前写死 220px，既比中文实际所需宽出
            // 一大截，又会在视口变窄时越过右缘、糊到右侧参数栏的小标题上。
            var badgeStyle   = OutlineEditorStyles.ViewportBadge(OutlineEditorStyles.Accent);
            var badgeContent = new GUIContent($"● {ShortModeLabel()}{spaceLabel}");
            float badgeW     = Mathf.Min(badgeStyle.CalcSize(badgeContent).x + 12f,
                                         Mathf.Max(0f, r.width - 12f));
            var badgeRect    = new Rect(r.x + 6, r.y + 6, badgeW, 20);
            EditorGUI.DrawRect(badgeRect, new Color(0.05f, 0.06f, 0.08f, 0.82f));
            GUI.Label(new Rect(badgeRect.x + 6, badgeRect.y, badgeRect.width, badgeRect.height),
                      badgeContent, badgeStyle);

            // 法线叠加层只针对【焦点】网格（右侧数据缓存 _meshCache 也只缓存它），
            // 且仅当焦点网格已勾选、确实在预览中时才画 —— 否则会把线段叠到一个根本
            // 没渲染的网格上。用焦点网格自己的 PreviewMatrix 把顶点摆到与渲染一致的位置。
            // 守卫已提到本方法顶部 —— 此前是 DrawNormalsOverlay(r, 未缓存的解码结果, …)，
            // 被调方虽对非 Repaint 提前返回，但 C# 先求值实参，整份解码照样每个事件都跑。
            var focus = FocusEntry();
            if (focus != null && focus.Selected)
            {
                var m = focus.PreviewMatrix;
                if (_showNormals)
                    DrawNormalsOverlay(r, GetDecodedSmoothNormalsCached(), _normalColor, m);
                if (_showOriginalNormals)
                    DrawNormalsOverlay(r, _meshCache?.Normals, _originalNormalColor, m);
            }

            // Overlay: hint
            // 用 CalcSize 实测宽度，放不下就整条不画 —— 这是居中不换行的单行 Label，
            // 装不下时两端会被同时裁掉，中间剩半句话。英文 / 日文比中文长得多，
            // 而这条只是操作说明、缺了不影响任何功能，宁可不显示。
            // 实测而不是估算：字体度量随编辑器主题与 DPI 变，估算迟早会错。
            var hint      = new GUIContent(LocWindow.ViewportHint);
            var hintStyle = OutlineEditorStyles.Hint;
            if (hintStyle.CalcSize(hint).x <= r.width - 8f)
            {
                var hintRect = new Rect(r.x, r.yMax - 22, r.width, 22);
                EditorGUI.DrawRect(hintRect, new Color(0.05f, 0.06f, 0.08f, 0.72f));
                GUI.Label(hintRect, hint, hintStyle);
            }
        }

        /// <summary>
        /// 把所有【勾选】的网格提交给预览渲染器，各自按 PreviewMatrix 摆到相对根对象的位置上。
        /// 逐 SubMesh 提交：多材质模型此前只能预览到第一个 SubMesh。
        /// </summary>
        private void DrawCheckedMeshes(Material mat)
        {
            foreach (var e in _meshEntries)
            {
                if (!e.Selected || !e.Mesh) continue;
                for (int i = 0; i < e.Mesh.subMeshCount; i++)
                    _previewUtil.DrawMesh(e.Mesh, e.PreviewMatrix, mat, i);
            }
        }

        /// <summary>
        /// 在预览视口上叠加绘制法线方向线段。
        /// normals 为对象空间法线数组，与 mesh.vertices 一一对应。
        ///
        /// 用 Handles 而不是 GL.LoadPixelMatrix 画：后者是直接写投影矩阵的，
        /// 其原点是【整个窗口渲染目标】的左上角 —— 包含标签页头部，而 OnGUI
        /// 的坐标系原点在头部下方。两者差一个 header 高度，会让线段整体上移。
        /// Handles.BeginGUI 用的就是 OnGUI 的坐标系，与 r 天然对齐。
        /// （OutlineEditorGUI.DrawHexIcon 一直是这么画的。）
        /// </summary>
        private void DrawNormalsOverlay(Rect r, Vector3[] normals, Color color, Matrix4x4 meshMatrix)
        {
            if (_previewUtil?.camera == null || _meshCache == null) return;
            if (Event.current.type != EventType.Repaint) return;
            if (normals == null || normals.Length != _meshCache.VertexCount) return;

            var verts = _meshCache.Vertices;   // 缓存，不再每次 marshal 整份顶点数组
            if (verts == null || verts.Length != normals.Length) return;

            var cam  = _previewUtil.camera;
            int step = Mathf.Max(1, verts.Length / 512);

            _normalLineBuffer.Clear();
            for (int i = 0; i < verts.Length; i += step)
            {
                // 顶点与法线都经 meshMatrix 变换，摆到与渲染网格一致的位置。
                Vector3 p = meshMatrix.MultiplyPoint3x4(verts[i]);
                Vector3 n = meshMatrix.MultiplyVector(normals[i]);
                Vector3 vpO = cam.WorldToViewportPoint(p);
                Vector3 vpE = cam.WorldToViewportPoint(p + n * _normalLength);

                if (vpO.z <= 0 || vpE.z <= 0) continue;

                var a = new Vector2(r.x + vpO.x * r.width, r.y + (1f - vpO.y) * r.height);
                var b = new Vector2(r.x + vpE.x * r.width, r.y + (1f - vpE.y) * r.height);

                // Handles 不受 GUI 裁剪约束，必须自己裁：否则模型放大后线段会
                // 一路画到右侧参数面板和左栏上面去。
                if (!ClipLineToRect(ref a, ref b, r)) continue;

                _normalLineBuffer.Add(a);
                _normalLineBuffer.Add(b);
            }

            if (_normalLineBuffer.Count == 0) return;

            Handles.BeginGUI();
            var prevColor = Handles.color;
            Handles.color = color;
            Handles.DrawLines(_normalLineBuffer.ToArray());
            Handles.color = prevColor;
            Handles.EndGUI();
        }

        /// <summary>
        /// Liang-Barsky 线段裁剪。把线段裁到 rect 内并写回端点；
        /// 返回 false 表示线段完全落在 rect 外。
        ///
        /// 用 CPU 裁剪而不是 GL.Viewport：后者用的是渲染目标的实际像素
        /// （左下原点），在高 DPI 屏上还要额外处理 pixelsPerPoint 缩放，
        /// 平白引入一类与本功能无关的坐标 bug。
        /// </summary>
        private static bool ClipLineToRect(ref Vector2 p0, ref Vector2 p1, Rect rect)
        {
            float t0 = 0f, t1 = 1f;
            float dx = p1.x - p0.x;
            float dy = p1.y - p0.y;

            for (int edge = 0; edge < 4; edge++)
            {
                float p, q;
                switch (edge)
                {
                    case 0:  p = -dx; q = p0.x - rect.xMin; break; // 左
                    case 1:  p =  dx; q = rect.xMax - p0.x; break; // 右
                    case 2:  p = -dy; q = p0.y - rect.yMin; break; // 上
                    default: p =  dy; q = rect.yMax - p0.y; break; // 下
                }

                if (Mathf.Approximately(p, 0f))
                {
                    if (q < 0f) return false;   // 与该边平行且在外侧
                    continue;
                }

                float t = q / p;
                if (p < 0f)
                {
                    if (t > t1) return false;
                    if (t > t0) t0 = t;
                }
                else
                {
                    if (t < t0) return false;
                    if (t < t1) t1 = t;
                }
            }

            var dir = new Vector2(dx, dy);
            var clipped0 = p0 + dir * t0;
            var clipped1 = p0 + dir * t1;
            p0 = clipped0;
            p1 = clipped1;
            return true;
        }

        /// <summary>
        /// 从 Mesh 按当前存储模式和通道选择，CPU 解码平滑法线（对象空间）。
        /// 供法线可视化叠加层使用。
        /// 
        /// 必须与 Shader/OutlineSmoothNormals.hlsl 的解码保持一致（经由
        /// OutlineSmoothNormalsCodec），否则叠加的线段会与实际描边对不上。
        /// 三种模式存的都是完整三维方向，因此解码不再需要顶点法线参与，
        /// 也不存在任何符号歧义。
        /// 
        /// 切线空间存储时还需再经一次 TBN 还原，用的是 <paramref name="poseMesh"/> 上的
        /// 法线 / 切线 —— 内嵌预览传绑定姿势的网格本身，Scene 视图叠加则传 BakeMesh
        /// 出来的当前姿势网格，于是同一份代码既能看静态模型也能看动画中的蒙皮模型。
        /// </summary>
        /// <param name="dataMesh">
        /// 编码数据的来源。顶点色与 TEXCOORD 不参与蒙皮，永远取共享网格即可。
        /// </param>
        /// <param name="poseMesh">
        /// 当前姿势下的法线 / 切线来源；非蒙皮时与 <paramref name="dataMesh"/> 相同。
        /// 切线通道存储模式的数据本身也从这里取 —— Unity 会把 tangent.xyz 当方向一起
        /// 蒙皮，取蒙皮后的值正是那个模式的意义所在。
        /// </param>
        /// <param name="mode"></param>
        /// <param name="vcChannel"></param>
        /// <param name="uvChannel"></param>
        /// <param name="space"></param>
        private static Vector3[] DecodeSmoothNormals(
            Mesh dataMesh, Mesh poseMesh,
            StorageMode mode, VertexColorChannel vcChannel, int uvChannel, NormalSpace space)
        {
            if (!dataMesh || !poseMesh) return null;

            int vCount = dataMesh.vertexCount;
            if (poseMesh.vertexCount != vCount) return null;   // BakeMesh 理应等长，不等就别猜
            var result = new Vector3[vCount];

            switch (mode)
            {
                // ── 顶点色（八面体编码）──────────────────────────────
                case StorageMode.VertexColor:
                {
                    var colors = dataMesh.colors32;
                    if (colors == null || colors.Length != vCount) return null;
                    for (int i = 0; i < vCount; i++)
                    {
                        var c = colors[i];
                        byte x, y;
                        switch (vcChannel)
                        {
                            case VertexColorChannel.RG: x = c.r; y = c.g; break;
                            case VertexColorChannel.GB: x = c.g; y = c.b; break;
                            default:                    x = c.b; y = c.a; break; // Ba
                        }
                        var oct = new Vector2(
                            OutlineSmoothNormalsCodec.UnpackUNorm(x),
                            OutlineSmoothNormalsCodec.UnpackUNorm(y));
                        result[i] = OutlineSmoothNormalsCodec.OctDecode(oct);
                    }
                    break;
                }

                // ── 切线（tangent.xyz 直接是对象空间法线）────────────
                case StorageMode.TangentSpace:
                {
                    var tangents = poseMesh.tangents;
                    if (tangents == null || tangents.Length != vCount) return null;
                    for (int i = 0; i < vCount; i++)
                    {
                        var t = tangents[i];
                        result[i] = new Vector3(t.x, t.y, t.z).normalized;
                    }
                    break;
                }

                // ── TEXCOORD 通道（uv.xy 八面体编码）─────────────────
                case StorageMode.UV:
                {
                    // 只认 2 分量。0 = 空，3 = 旧版格式，4 = 别人的自定义数据 ——
                    // 一律【整层不画】，而不是硬按八面体去解：那会画出一片明显错误
                    // 的方向，比不画更误导。旧格式的说明由通道状态卡与 DrawUVModeUI
                    // 负责，这里只管别画错。
                    int dim = dataMesh.GetVertexAttributeDimension(
                        UnityEngine.Rendering.VertexAttribute.TexCoord0 + uvChannel);
                    if (dim != 2) return null;

                    var uvList = new List<Vector2>();
                    dataMesh.GetUVs(uvChannel, uvList);
                    if (uvList.Count != vCount) return null;
                    for (int i = 0; i < vCount; i++)
                        result[i] = OutlineSmoothNormalsCodec.OctDecode(uvList[i]);
                    break;
                }
            }

            // 切线空间 → 对象空间。切线通道模式不参与（存进去的就是对象空间方向）。
            if (space == NormalSpace.Tangent && mode != StorageMode.TangentSpace)
            {
                var normals  = poseMesh.normals;
                var tangents = poseMesh.tangents;
                if (normals == null || normals.Length != vCount ||
                    tangents == null || tangents.Length != vCount)
                    return null;   // 缺基就无法还原，叠加层整体不画，好过画出错误方向

                for (int i = 0; i < vCount; i++)
                    result[i] = OutlineSmoothNormalsCodec.TangentToObject(result[i], normals[i], tangents[i]);
            }

            return result;
        }

        private void HandlePreviewCameraControl(Rect r)
        {
            var e = Event.current;
            if (!r.Contains(e.mousePosition)) return;

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                _previewDragging = true;
                _previewLastMouse = e.mousePosition;
                e.Use();
            }
            if (e.type == EventType.MouseUp && e.button == 0)
            {
                _previewDragging = false;
                e.Use();
            }
            if (_previewDragging && e.type == EventType.MouseDrag && e.button == 0)
            {
                var delta = e.mousePosition - _previewLastMouse;
                _previewOrbit.x += delta.x * 0.5f;
                _previewOrbit.y -= delta.y * 0.5f;
                _previewOrbit.y  = Mathf.Clamp(_previewOrbit.y, -89f, 89f);
                _previewLastMouse = e.mousePosition;
                Repaint(); e.Use();
            }
            if (e.type == EventType.ScrollWheel)
            {
                _previewZoom = Mathf.Clamp(_previewZoom + e.delta.y * _previewZoom * 0.05f, 0.1f, 100f);
                Repaint(); e.Use();
            }
            if (e.type == EventType.MouseDrag && e.button == 2)
            {
                var delta = e.delta * (0.004f * _previewZoom);
                var cam = _previewUtil?.camera;
                if (cam)
                    _previewPivot -= cam.transform.right * delta.x - cam.transform.up * delta.y;
                Repaint(); e.Use();
            }
        }

        // 参数栏的宽度与标签宽度（PreviewParamPanelWidth / PreviewParamLabelWidth）
        // 已统一放到文件上方的「分栏尺寸」一节，与窗口尺寸、分隔线范围摆在一起。

        private void DrawInlinePreviewParams()
        {
            // labelWidth 是全局编辑器状态，必须还原 —— 否则会污染同一帧里后画的
            // 其他面板（Inspector、其他 EditorWindow）。用 try/finally 兜住中途异常。
            float prevLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = PreviewParamLabelWidth;
            try
            {
                DrawInlinePreviewParamsBody();
            }
            finally
            {
                EditorGUIUtility.labelWidth = prevLabelWidth;
            }
        }

        private void DrawInlinePreviewParamsBody()
        {
            // 描边参数
            GUILayout.Space(6);
            OutlineEditorGUI.DrawParamHeader(LocWindow.ParamHeaderOutline);
            EditorGUILayout.BeginVertical(OutlineEditorStyles.InnerCard);
            _showOutline = EditorGUILayout.Toggle(LocWindow.ToggleShowOutline, _showOutline);
            GUI.enabled = _showOutline;
            EditorGUI.BeginChangeCheck();
            _outlineColor = EditorGUILayout.ColorField(LocWindow.FieldOutlineColor, _outlineColor);
            // 上限与 Outline.shader 的 _OutlineWidth Range(0, 0.1) 保持一致：
            // 两边现在用同一套外扩数学，数值必须可直接对照。
            _outlineWidth = EditorGUILayout.Slider(LocWindow.FieldOutlineWidth, _outlineWidth, 0.001f, 0.1f);
            _outlineWidthMode = EditorGUILayout.Popup(
                new GUIContent(LocWindow.FieldWidthMode, LocWindow.FieldWidthModeTooltip),
                _outlineWidthMode, LocWindow.WidthModeNames);
            if (EditorGUI.EndChangeCheck()) Repaint();
            GUI.enabled = true;
            EditorGUILayout.EndVertical();

            // 模型参数
            GUILayout.Space(4);
            OutlineEditorGUI.DrawParamHeader(LocWindow.ParamHeaderModel);
            EditorGUILayout.BeginVertical(OutlineEditorStyles.InnerCard);
            _showBase = EditorGUILayout.Toggle(LocWindow.ToggleShowBase, _showBase);
            GUI.enabled = _showBase;
            EditorGUI.BeginChangeCheck();
            _baseColor  = EditorGUILayout.ColorField(LocWindow.FieldBaseColor, _baseColor);
            _smoothness = EditorGUILayout.Slider(LocWindow.FieldSmoothness, _smoothness, 0f, 1f);
            _metallic   = EditorGUILayout.Slider(LocWindow.FieldMetallic, _metallic, 0f, 1f);
            if (EditorGUI.EndChangeCheck()) Repaint();
            GUI.enabled = true;
            EditorGUILayout.EndVertical();

            // 视口参数
            GUILayout.Space(4);
            OutlineEditorGUI.DrawParamHeader(LocWindow.ParamHeaderViewport);
            EditorGUILayout.BeginVertical(OutlineEditorStyles.InnerCard);
            EditorGUI.BeginChangeCheck();
            _previewBgColor = EditorGUILayout.ColorField(LocWindow.FieldBgColor, _previewBgColor);
            if (EditorGUI.EndChangeCheck()) Repaint();
            EditorGUILayout.EndVertical();

            // 法线可视化
            GUILayout.Space(4);
            OutlineEditorGUI.DrawParamHeader(LocWindow.ParamHeaderNormalVis);
            EditorGUILayout.BeginVertical(OutlineEditorStyles.InnerCard);
            EditorGUI.BeginChangeCheck();

            // 平滑法线
            _showNormals  = EditorGUILayout.Toggle(LocWindow.ToggleShowSmoothNormals, _showNormals);
            GUI.enabled   = _showNormals;
            _normalLength = EditorGUILayout.Slider(LocWindow.FieldNormalLength, _normalLength, 0.005f, 0.5f);
            _normalColor  = EditorGUILayout.ColorField(LocWindow.FieldSmoothNormalColor, _normalColor);
            GUI.enabled   = true;

            EditorGUILayout.Space(2);

            // 原始法线
            _showOriginalNormals = EditorGUILayout.Toggle(LocWindow.ToggleShowOriginalNormals, _showOriginalNormals);
            GUI.enabled          = _showOriginalNormals;
            _originalNormalColor = EditorGUILayout.ColorField(LocWindow.FieldOriginalNormalColor, _originalNormalColor);
            GUI.enabled          = true;

            if (EditorGUI.EndChangeCheck()) Repaint();

            EditorGUILayout.Space(4);
            DrawSceneOverlayUI();

            EditorGUILayout.EndVertical();

            // 相机控制
            GUILayout.Space(4);
            OutlineEditorGUI.DrawParamHeader(LocWindow.ParamHeaderCamera);
            EditorGUILayout.BeginVertical(OutlineEditorStyles.InnerCard);
            EditorGUI.BeginChangeCheck();
            _previewOrbit.x = EditorGUILayout.Slider(LocWindow.FieldOrbitX, _previewOrbit.x, -180f, 180f);
            _previewOrbit.y = EditorGUILayout.Slider(LocWindow.FieldOrbitY, _previewOrbit.y, -89f, 89f);
            _previewZoom    = EditorGUILayout.Slider(LocWindow.FieldZoom, _previewZoom, 0.1f, 20f);
            if (EditorGUI.EndChangeCheck()) Repaint();
            if (GUILayout.Button(LocWindow.BtnResetView))
            {
                _previewOrbit = new Vector2(30f, -20f);
                FramePreviewToChecked();   // 兜住所有勾选的网格
                Repaint();
            }
            EditorGUILayout.EndVertical();
            GUILayout.FlexibleSpace();
        }

        // ─────────────────────────────────────────────────────────────
        private void SetupPreviewRenderer()
        {
            _previewUtil = new PreviewRenderUtility();
            _previewUtil.camera.fieldOfView    = 30f;
            _previewUtil.camera.nearClipPlane  = 0.01f;
            _previewUtil.camera.farClipPlane   = 1000f;
            _previewUtil.camera.backgroundColor = _previewBgColor;
            _previewUtil.camera.clearFlags     = CameraClearFlags.SolidColor;
            _previewUtil.lights[0].intensity   = 1.1f;
            _previewUtil.lights[0].transform.rotation = Quaternion.Euler(50, -30, 0);
            _previewUtil.lights[1].intensity   = 0.4f;
            BuildPreviewMaterials();
        }

        private void TearDownPreviewRenderer()
        {
            if (_previewUtil != null) { _previewUtil.Cleanup(); _previewUtil = null; }
            if (_previewBaseMat)    DestroyImmediate(_previewBaseMat);
            if (_previewOutlineMat) DestroyImmediate(_previewOutlineMat);
        }

        private static readonly int PropBaseColor  = Shader.PropertyToID("_BaseColor");
        private static readonly int PropSmoothness = Shader.PropertyToID("_Smoothness");

        // 按渲染管线寻找可用的 Lit shader
        private static Shader FindLitShader()
        {
            var s = Shader.Find("Universal Render Pipeline/Lit");
            if (s) return s;
            s = Shader.Find("Standard");
            if (s) return s;
            return Shader.Find("Unlit/Color");
        }

        private void BuildPreviewMaterials()
        {
            // 重入保护：DrawPreviewLaunchPanel 里有 `if (_previewUtil == null) SetupPreviewRenderer();`，
            // 不清理旧材质就重建会把上一对材质变成孤儿。
            if (_previewBaseMat)    DestroyImmediate(_previewBaseMat);
            if (_previewOutlineMat) DestroyImmediate(_previewOutlineMat);

            var litShader = FindLitShader();
            _previewBaseMat = new Material(litShader) { hideFlags = HideFlags.HideAndDontSave };
            ApplyBaseMatParams();

            var outlineShader = Shader.Find("OutlineSmoothNormalsGenerator/OutlinePreview") ?? Shader.Find("Unlit/Color");
            _previewOutlineMat = new Material(outlineShader) { hideFlags = HideFlags.HideAndDontSave };

            // 直接调用更新方法，而不是在这里再抄一遍属性写入。此前这里只设了
            // 颜色 / 宽度 / 宽度模式三项，漏掉存储方式那四项 —— 虽然每次重绘前都会
            // 调 UpdatePreviewOutlineMat 补上，看不出问题，但「新建的材质是半配好的」
            // 本身就是个等着被踩的坑。
            UpdatePreviewOutlineMat();
        }

        /// <summary>属性存在才写。预览材质可能落到 URP Lit / Standard / Unlit 任一个上，
        /// 各自的属性集不同，写不存在的属性会刷警告。</summary>
        private static void SetIfHas(Material mat, int prop, float value)
        {
            if (mat.HasProperty(prop)) mat.SetFloat(prop, value);
        }

        private static void SetIfHas(Material mat, int prop, Color value)
        {
            if (mat.HasProperty(prop)) mat.SetColor(prop, value);
        }

        private void ApplyBaseMatParams()
        {
            if (!_previewBaseMat) return;

            // 颜色：URP 用 _BaseColor，Built-in 用 _Color（即 .color）
            if (_previewBaseMat.HasProperty(PropBaseColor))
                _previewBaseMat.SetColor(PropBaseColor, _baseColor);
            else
                _previewBaseMat.color = _baseColor;

            // 光滑度：URP 用 _Smoothness，Built-in 用 _Glossiness
            if (_previewBaseMat.HasProperty(PropSmoothness))
                _previewBaseMat.SetFloat(PropSmoothness, _smoothness);
            else if (_previewBaseMat.HasProperty(PropGlossiness))
                _previewBaseMat.SetFloat(PropGlossiness, _smoothness);

            SetIfHas(_previewBaseMat, PropMetallic, _metallic);
        }

        private void UpdatePreviewOutlineMat()
        {
            if (!_previewOutlineMat) return;

            SetIfHas(_previewOutlineMat, PropOutlineColor,     _outlineColor);
            SetIfHas(_previewOutlineMat, PropOutlineWidth,     _outlineWidth);
            SetIfHas(_previewOutlineMat, PropOutlineWidthMode, _outlineWidthMode);
            SetIfHas(_previewOutlineMat, PropStorageMode,      (float)_storageMode);
            SetIfHas(_previewOutlineMat, PropUVChannel,        _uvChannel);
            SetIfHas(_previewOutlineMat, PropVcChannel,        (float)_vcChannel);

            // 切线通道模式恒为对象空间：预览必须跟实际解码一致，不能把 UI 上那个
            // 已被禁用（但仍保留着上次选择）的 _normalSpace 原样喂过去。
            SetIfHas(_previewOutlineMat, PropNormalSpace,
                     _storageMode == StorageMode.TangentSpace ? 0f : (float)_normalSpace);
        }
        #endregion

        #region 平滑法线 写入与清除
        /// <summary>
        /// 生成前对破坏性操作做二次确认。目前唯一需要拦的是写入 TEXCOORD0
        /// （主贴图 UV）—— 它会毁掉贴图映射且影响所有引用该网格的对象。
        /// </summary>
        private void TryGenerateSmoothNormals()
        {
            var targets = SelectedMeshes();
            if (targets.Count == 0)
            {
                Debug.LogWarning($"{LocLog.Prefix} {LocWindow.LogNothingChecked}");
                return;
            }

            // 健康检查：任一勾选网格存在无法处理的数据（Error）时，先给一次二次确认。
            var unhealthy = targets.FirstOrDefault(
                m => OutlineMeshValidator.Validate(m, _storageMode, _normalSpace).HasError);
            if (unhealthy != null &&
                !EditorUtility.DisplayDialog(
                    LocWindow.DialogUnhealthyTitle,
                    LocWindow.DialogUnhealthyBody(unhealthy.name),
                    LocWindow.BtnContinueAnyway, LocWindow.BtnCancel))
                return;

            // 写入 TEXCOORD0（主贴图 UV）是唯一需要拦的破坏性操作。批量时对所有
            // TEXCOORD0 已有数据的勾选网格一并确认一次。
            bool anyRiskyUV = _storageMode == StorageMode.UV && _uvChannel == 0 &&
                targets.Any(m => m.GetVertexAttributeDimension(
                    UnityEngine.Rendering.VertexAttribute.TexCoord0) > 0);
            if (anyRiskyUV &&
                !EditorUtility.DisplayDialog(
                    LocWindow.DialogOverwriteUv0Title,
                    LocWindow.DialogOverwriteUv0Body(targets.Count),
                    LocWindow.BtnOverwriteAnyway, LocWindow.BtnCancel))
                return;

            GenerateSmoothNormals(targets);
        }

        /// <summary>
        /// 单个网格的顶点数超过这个量级，才值得为它单独弹进度条。
        /// 小网格瞬间就算完，弹一下只会闪屏。批量（多于一个网格）则一律显示。
        /// </summary>
        private const int ProgressBarVertexThreshold = 50000;

        /// <summary>
        /// 生成日志里的存储描述 —— 写清「数据落在哪、什么格式」。
        ///
        /// 事后排查描边异常时，Console 这一行常常是唯一还留着的线索：
        /// 存储方式 / 存储空间 / 数据格式三者但凡与材质对不上，都不会报错，
        /// 只会让描边偏斜或撕开。
        /// </summary>
        private string DescribeStorageForLog() => _storageMode switch
        {
            StorageMode.VertexColor  => LocWindow.LogStorageVertexColor(_vcChannel.ToString()),
            StorageMode.TangentSpace => LocWindow.LogStorageTangent,
            _                        => LocWindow.LogStorageUv(_uvChannel),
        };

        /// <summary>
        /// 当前存储通道的简称。生成按钮与预览视口徽标共用一份 —— 两处各写一遍，
        /// 迟早会出现按钮说「顶点色」而徽标说别的。
        /// </summary>
        private string ShortModeLabel() => _storageMode switch
        {
            StorageMode.VertexColor  => LocWindow.ShortModeVertexColor,
            StorageMode.TangentSpace => LocWindow.ShortModeTangentChannel,
            _                        => $"TEXCOORD{_uvChannel}",
        };

        /// <summary>对勾选集合里的每个网格按当前存储模式生成并写入平滑法线。</summary>
        private void GenerateSmoothNormals(List<Mesh> targets)
        {
            bool showProgress = targets.Count > 1 ||
                                (targets.Count == 1 && targets[0] &&
                                 targets[0].vertexCount > ProgressBarVertexThreshold);

            int ok = 0;
            bool canceled = false;

            // finally 不可省：中途抛异常而不清进度条，Unity 会一直卡在那条模态进度条上，
            // 界面完全不响应，除了重启编辑器没有别的办法。
            try
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    var mesh = targets[i];

                    // 放在计算【之前】：这样进度条描述的是「正要处理谁」，而不是
                    // 「刚处理完谁」——卡住时用户看到的才是真正的元凶。
                    if (showProgress && EditorUtility.DisplayCancelableProgressBar(
                            LocWindow.ProgressTitle,
                            LocWindow.ProgressBody(i + 1, targets.Count, mesh.name, mesh.vertexCount),
                            i / (float)targets.Count))
                    {
                        canceled = true;
                        break;
                    }

                    var smoothNormals = OutlineSmoothNormalsCalculator.Calculate(mesh, _mergeTolerance);
                    if (smoothNormals == null) continue;   // 具体原因已由 Calculate 打印

                    // 计算成功、真要动数据之前才抓快照（每个网格各存一份，供批量还原）。
                    CaptureSnapshot(mesh);

                    switch (_storageMode)
                    {
                        case StorageMode.VertexColor:
                            StorageWriter.WriteToVertexColor(mesh, smoothNormals, _vcChannel, _normalSpace);
                            break;
                        case StorageMode.TangentSpace:
                            StorageWriter.WriteToTangent(mesh, smoothNormals);
                            break;
                        case StorageMode.UV:
                            StorageWriter.WriteToUV(mesh, smoothNormals, _uvChannel, _normalSpace);
                            break;
                    }

                    EditorUtility.SetDirty(mesh);
                    _dirtyMeshes.Add(mesh);
                    ok++;
                    // 存储空间同样要记进日志：它决定材质该怎么解，事后排查描边偏斜时
                    // 这是第一个要确认的信息。切线通道模式恒为对象空间，照实记录。
                    var loggedSpace = _storageMode == StorageMode.TangentSpace ? NormalSpace.Object : _normalSpace;
                    string spaceLabel = loggedSpace == NormalSpace.Tangent
                        ? LocWindow.ShortSpaceTangent : LocWindow.ShortSpaceObject;
                    Debug.Log($"{LocLog.Prefix} {LocWindow.LogGenerated(DescribeStorageForLog(), spaceLabel, mesh.name, mesh.vertexCount, _mergeTolerance.ToString("G"))}");
                }
            }
            finally
            {
                if (showProgress) EditorUtility.ClearProgressBar();
            }

            if (ok > 0) _saveState = SaveState.NeedSave;
            RefreshDataStatus();   // 焦点网格
            Repaint();

            // 取消是「停在当前这个网格之前」，已处理的那些【保留】改动 —— 回滚它们
            // 需要的快照已经抓好了，交给用户用「还原」决定，比替他撤销更可控。
            if (canceled)
                Debug.LogWarning($"{LocLog.Prefix} {LocWindow.LogCanceled(ok, targets.Count)}");
            else if (ok > 1)
                Debug.Log($"{LocLog.Prefix} {LocWindow.LogBatchDone(ok)}");
        }

        // ─────────────────────────────────────────────────────────────
        /// <summary>
        /// 就地改写焦点网格的统一入口 —— 三个「清除」操作共用这套前后处理。
        ///
        /// 前：抓快照（清除同样是破坏性的，「还原本次修改」必须对它一样有效）+ 记 Undo。
        /// 后：SetDirty → 刷新数据状态 → 标脏 → 重绘。
        ///
        /// 抽出来是因为漏掉其中任何一步都不会报错，只会静默地坏掉一件事：
        /// 少抓快照 = 还不回去，少 SetDirty = 存不下来，少 MarkDirty = 保存按钮不亮。
        /// </summary>
        /// <param name="undoName">Undo 栈里显示的操作名。</param>
        /// <param name="mutate">真正动数据的那一步。</param>
        private void MutateFocusMesh(string undoName, System.Action<Mesh> mutate)
        {
            if (!_targetMesh) return;

            CaptureSnapshot(_targetMesh);
            Undo.RecordObject(_targetMesh, undoName);

            mutate(_targetMesh);

            EditorUtility.SetDirty(_targetMesh);
            RefreshDataStatus();
            MarkDirty();
            Repaint();
        }

        private void ClearVertexColorChannels(bool clearR, bool clearG, bool clearB, bool clearA)
            => MutateFocusMesh("Clear Vertex Color Channels", mesh =>
            {
                int vCount = mesh.vertexCount;
                var existing = mesh.colors32;
                bool hasExisting = existing != null && existing.Length == vCount;
                var colors = new Color32[vCount];
                for (int i = 0; i < vCount; i++)
                {
                    byte r = (hasExisting && !clearR) ? existing[i].r : (byte)128;
                    byte g = (hasExisting && !clearG) ? existing[i].g : (byte)128;
                    byte b = (hasExisting && !clearB) ? existing[i].b : (byte)128;
                    byte a = (hasExisting && !clearA) ? existing[i].a : (byte)128;
                    colors[i] = new Color32(r, g, b, a);
                }
                mesh.colors32 = colors;
            });

        /// <summary>
        /// 重算切线，把网格还原成「正常」状态。
        ///
        /// 刻意【不】把 tangents 设成 null —— 那会让网格彻底失去切线，所有采样法线贴图的
        /// Shader 都会得到未定义的 TBN，且影响每一个引用该 sharedMesh 的对象，
        /// 除了重导入模型没有任何恢复手段。
        /// </summary>
        private void ClearTangents()
            => MutateFocusMesh("Recalculate Tangents", mesh => mesh.RecalculateTangents());

        private void ClearUV(int ch)
            => MutateFocusMesh($"Clear TEXCOORD{ch}", mesh => mesh.SetUVs(ch, (List<Vector2>)null));
        #endregion
    }
}
