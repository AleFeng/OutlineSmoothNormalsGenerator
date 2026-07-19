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
            public List<Vector4>[] Uvs;
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
            };
            for (int i = 0; i < UvChannelCount; i++)
            {
                var list = new List<Vector4>();
                mesh.GetUVs(i, list);
                snap.Uvs[i] = list;
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

                // 空数组/空列表即代表「该通道原本就没有数据」，赋回去正好清空。
                mesh.colors32 = snap.Colors;
                mesh.tangents = snap.Tangents;
                for (int i = 0; i < UvChannelCount; i++)
                    mesh.SetUVs(i, snap.Uvs[i]);

                EditorUtility.SetDirty(mesh);
                _dirtyMeshes.Remove(mesh);
                n++;
            }

            _snapshots.Clear();
            _saveState = SaveState.Clean;
            RefreshDataStatus();
            Repaint();
            Debug.Log($"[SmoothNormal] 已还原 {n} 个网格到本次修改之前的状态。");
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
        private float _dividerX = 420f;
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
            public readonly int[] UvCounts = new int[UvChannelCount];

            public Vector3[] Vertices;   // 法线叠加层用
            public Vector3[] Normals;
        }
        private MeshCache _meshCache;

        /// <summary>
        /// 数据版本号。写入 / 清除 / 还原后递增，用来让解码缓存失效。
        /// </summary>
        private int _dataVersion;

        // 解码结果缓存：解码依赖存储模式与通道选择，因此 key 要带上它们。
        private Vector3[] _decodedCache;
        private (Mesh mesh, StorageMode mode, VertexColorChannel vc, int uv, int version) _decodedKey;

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

            var uvList = new List<Vector4>();
            for (int i = 0; i < UvChannelCount; i++)
            {
                _targetMesh.GetUVs(i, uvList);
                c.UvCounts[i] = uvList.Count;
            }

            _meshCache    = c;
            _decodedCache = null;
        }

        /// <summary>按当前模式解码平滑法线，结果带缓存 —— 每次调用都全量解码是 OnGUI 里的重灾区。</summary>
        private Vector3[] GetDecodedSmoothNormalsCached()
        {
            var key = (_targetMesh, _storageMode, _vcChannel, _uvChannel, _dataVersion);
            if (_decodedCache != null && _decodedKey.Equals(key)) return _decodedCache;

            _decodedCache = GetDecodedSmoothNormals();
            _decodedKey   = key;
            return _decodedCache;
        }

        // ─────────────────────────────────────────────────────────────
        //  Styles
        // ─────────────────────────────────────────────────────────────
        private GUIStyle _headerStyle;
        private GUIStyle _subHeaderStyle;
        private GUIStyle _dataCardStyle;
        private bool _stylesInitialized;

        // ─────────────────────────────────────────────────────────────
        //  Foldouts
        // ─────────────────────────────────────────────────────────────
        private bool _foldoutMeshInfo;

        // ─────────────────────────────────────────────────────────────
        //  Colors
        // ─────────────────────────────────────────────────────────────
        private static readonly Color ColorAccent = new Color(0.33f, 0.78f, 1f);
        private static readonly Color ColorSuccess = new Color(0.35f, 0.85f, 0.47f);
        private static readonly Color ColorWarning = new Color(1f, 0.78f, 0.25f);
        private static readonly Color ColorGray = new Color(0.4f, 0.42f, 0.48f);
        private static readonly Color ColorCard = new Color(0.18f, 0.20f, 0.24f);
        private static readonly Color ColorBorder = new Color(0.28f, 0.30f, 0.36f);

        // ═══════════════════════════════════════════════════════════════
        [MenuItem("Tools/Smooth Normal Generator")]
        public static void ShowWindow()
        {
            var win = GetWindow<OutlineSmoothNormalsGeneratorWindow>("平滑法线生成器");
            win.minSize = new Vector2(820, 560);
            win.Show();
        }

        // ═══════════════════════════════════════════════════════════════
        private void OnEnable()
        {
            Selection.selectionChanged += OnSelectionChanged;
            OnSelectionChanged();
            SetupPreviewRenderer();
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
            TearDownPreviewRenderer();
        }

        #region UI 主界面
        /// <summary>窗口顶部页签。</summary>
        public enum WindowTab { Generator, AutoBake }
        private WindowTab _activeTab = WindowTab.Generator;

        private const float TabBarHeight = 34f;
        // 页签栏下沿的 y（= 生成器页签内容区顶部）。分隔线用它作起点，避免竖线穿过页签栏。
        private float _contentTop;

        private Vector2 _autoBakeScroll;

        private void OnGUI()
        {
            InitStyles();
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
            DrawWindowTab("平滑法线生成器", WindowTab.Generator);
            DrawWindowTab("导入自动烘焙", WindowTab.AutoBake);
            EditorGUILayout.EndHorizontal();

            var barRect = GUILayoutUtility.GetLastRect();
            // 页签栏下沿画一条强调线；记录内容区顶部供分隔线定位。
            EditorGUI.DrawRect(new Rect(0, barRect.yMax, position.width, 2), ColorAccent);
            _contentTop = barRect.yMax + 2;
            GUILayout.Space(2);
        }

        private void DrawWindowTab(string label, WindowTab tab)
        {
            bool active = _activeTab == tab;
            var bg = active ? ColorAccent : ColorCard;
            var fg = active ? new Color(0.05f, 0.05f, 0.08f) : new Color(0.65f, 0.70f, 0.78f);

            var style = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                fontStyle = active ? FontStyle.Bold : FontStyle.Normal,
                normal = { textColor = fg, background = MakeTex(2, 2, bg) },
                hover  = { textColor = fg, background = MakeTex(2, 2, bg * 1.1f) },
                alignment = TextAnchor.MiddleCenter,
            };

            if (GUILayout.Button(label, style, GUILayout.Height(TabBarHeight), GUILayout.ExpandWidth(true))
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
        private void DrawAutoBakeTab()
        {
            var s = OutlineNormalsSettings.instance;

            _autoBakeScroll = EditorGUILayout.BeginScrollView(_autoBakeScroll);

            DrawAutoBakeHeader();

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginVertical(_dataCardStyle,
                GUILayout.Width(Mathf.Min(600f, position.width - 40f)));

            EditorGUILayout.HelpBox(
                "命中文件名后缀的模型，在导入 / 重导入时自动把平滑法线烘焙进网格。\n" +
                "非破坏性：去掉后缀或关闭开关后重新导入，即恢复原始网格。",
                MessageType.Info);

            GUILayout.Space(6);

            EditorGUI.BeginChangeCheck();

            s.AutoBakeEnabled = EditorGUILayout.ToggleLeft("启用导入时自动烘焙", s.AutoBakeEnabled);

            using (new EditorGUI.DisabledScope(!s.AutoBakeEnabled))
            {
                GUILayout.Space(8);
                DrawSectionHeader("命中规则", "◈");
                s.FilenameSuffix = EditorGUILayout.TextField("文件名后缀", s.FilenameSuffix);
                EditorGUILayout.LabelField(
                    " ",
                    string.IsNullOrEmpty(s.FilenameSuffix)
                        ? "后缀为空 → 不命中任何模型"
                        : $"例：Hero{s.FilenameSuffix}.fbx 会被命中（大小写不敏感）",
                    EditorStyles.miniLabel);

                GUILayout.Space(8);
                DrawSectionHeader("存储方式", "◈");
                s.StorageMode = (StorageMode)EditorGUILayout.EnumPopup("存储通道", s.StorageMode);
                switch (s.StorageMode)
                {
                    case StorageMode.VertexColor:
                        s.VcChannel = (VertexColorChannel)EditorGUILayout.EnumPopup("顶点色通道对", s.VcChannel);
                        break;
                    case StorageMode.UV:
                        s.UvChannel = EditorGUILayout.IntSlider("UV 通道 (TEXCOORDn)", s.UvChannel, 0, 7);
                        break;
                    case StorageMode.TangentSpace:
                        EditorGUILayout.HelpBox("切线空间会覆盖网格原始切线，法线贴图将失效。", MessageType.Warning);
                        break;
                }

                GUILayout.Space(8);
                DrawSectionHeader("生成参数", "◈");
                s.MergeTolerance = EditorGUILayout.FloatField(
                    new GUIContent("合并容差",
                        "位置距离在此范围内的顶点视为同一点；必须远小于模型最小特征尺寸。"),
                    s.MergeTolerance);
            }

            if (EditorGUI.EndChangeCheck())
                s.Save();

            EditorGUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(10);
            EditorGUILayout.LabelField(
                "配置保存于 ProjectSettings/OutlineSmoothNormals.asset（随工程纳入版本管理）",
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
            GUILayout.Label("导入自动烘焙", _headerStyle);
            GUILayout.Label("Auto-Bake On Import  •  命中后缀的模型导入即烘焙平滑法线", _subHeaderStyle);
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
            GUILayout.Space(10);
        }
        
        private void DrawDivider()
        {
            var dividerRect = new Rect(_dividerX, _contentTop, 4, position.height - _contentTop);
            EditorGUI.DrawRect(dividerRect, ColorBorder);

            // Hover highlight
            if (dividerRect.Contains(Event.current.mousePosition))
            {
                EditorGUI.DrawRect(dividerRect, ColorAccent * 0.6f);
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
                _dividerX = Mathf.Clamp(e.mousePosition.x, 300, position.width - 250);
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
            EditorGUI.DrawRect(new Rect(0, position.height - 74, _dividerX, 1), ColorBorder);
            GUILayout.Space(6);

            // ── 第一行：还原 + 保存 ──────────────────────────────────
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);

            // 还原：本次会话抓到过快照就可用；批量生成会跨多个网格抓快照，一并退回。
            GUI.enabled = _snapshots.Count > 0;
            if (GUILayout.Button(new GUIContent("↺  还原本次修改",
                    "把所有本次生成 / 清除过的网格退回到修改之前的状态。\n\n" +
                    "这是本工具自己的会话快照，与 Unity 的 Undo 无关 —— Undo 不跟踪网格顶点数据。"),
                    GUILayout.Width(110), GUILayout.Height(26)))
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
                btnColor = ColorGray;
                btnTip   = "请在网格列表中勾选要保存的网格。";
                canSave  = false;
            }
            else if (dirtyWritable > 0)
            {
                btnColor = ColorWarning;
                btnTip   = $"保存 {dirtyWritable} 个已修改的网格。" +
                           (dirtyBlocked > 0
                               ? $"\n另有 {dirtyBlocked} 个为只读（模型 / FBX 子资产 / 内置），需逐个「另存为」。"
                               : "");
                canSave  = true;
            }
            else if (dirtyBlocked > 0)
            {
                btnColor = ColorGray;
                btnTip   = $"{dirtyBlocked} 个已修改的网格不可直接保存（模型 / FBX / 内置只读），\n" +
                           "请逐个选中后用右侧「⧉ 另存为独立 Mesh…」。";
                canSave  = false;
            }
            else
            {
                btnColor = _saveState == SaveState.Saved ? ColorSuccess : ColorGray;
                btnTip   = _saveState == SaveState.Saved ? "已保存，暂无新的修改。" : "当前没有需要保存的修改。";
                canSave  = false;
            }

            GUI.enabled = canSave;
            var style = new GUIStyle(GUI.skin.button)
            {
                fontSize    = 11,
                fontStyle   = FontStyle.Bold,
                fixedHeight = 26,
                normal      = { textColor = canSave ? new Color(0.05f, 0.05f, 0.08f) : new Color(0.55f, 0.58f, 0.62f),
                                background = MakeTex(2, 2, btnColor) },
                hover       = { textColor = new Color(0.05f, 0.05f, 0.08f),
                                background = MakeTex(2, 2, btnColor * 1.12f) },
            };
            if (GUILayout.Button(new GUIContent("保存", btnTip), style))
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
            var dupColor = highlightDup ? ColorAccent : ColorCard * 1.5f;
            var dupStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize    = 11,
                fontStyle   = highlightDup ? FontStyle.Bold : FontStyle.Normal,
                fixedHeight = 24,
                normal      = { textColor = highlightDup
                                    ? new Color(0.05f, 0.05f, 0.08f) : new Color(0.75f, 0.78f, 0.82f),
                                background = MakeTex(2, 2, dupColor) },
                hover       = { textColor = new Color(0.05f, 0.05f, 0.08f),
                                background = MakeTex(2, 2, dupColor * 1.12f) },
            };
            if (GUILayout.Button(new GUIContent("⧉  另存为独立 Mesh…",
                    "把所有勾选的网格复制成独立可写的 .asset。\n\n" +
                    "勾选 1 个：弹对话框让你命名保存；\n" +
                    "勾选多个：选一个目标文件夹，按各自网格名批量生成。\n\n" +
                    "场景对象的网格会自动回填到对应组件；资产（Mesh / 模型 / 预制体）" +
                    "只生成 .asset，请自行引用。"), dupStyle))
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
                Debug.Log($"[SmoothNormal] 已保存 {saved.Count} 个网格：\n{string.Join("\n", saved)}");
            }

            // 只读网格只能走「另存为」，绝不在这里谎报成功。
            if (blocked.Count > 0)
            {
                string names = string.Join("\n", blocked.Select(m => "· " + m.name));
                string msg = $"以下 {blocked.Count} 个已修改的网格不可直接保存" +
                             "（模型 / FBX 子资产 / 内置资源，均为只读）：\n\n" + names + "\n\n" +
                             "请在列表中逐个选中它们，再用右侧「⧉ 另存为独立 Mesh…」复制成可写的 .asset。";
                Debug.LogError($"[SmoothNormal] {msg}");
                EditorUtility.DisplayDialog("部分网格无法直接保存", msg, "知道了");
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
                "另存为独立 Mesh",
                $"{entry.Mesh.name}_SmoothNormals",
                "asset",
                "新网格会自动替换到当前对象上（若来自场景对象）。",
                dir);
            if (string.IsNullOrEmpty(savePath)) return;

            bool reassigned = DuplicateEntryToPath(entry, savePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            SelectMeshEntry(_meshIndex);   // 焦点若指向被替换的条目，刷新到副本
            _saveState = SaveState.Saved;
            Repaint();
            Debug.Log(reassigned
                ? $"[SmoothNormal] 已复制为独立网格并替换到对象上：{savePath}"
                : $"[SmoothNormal] 已复制为独立网格：{savePath}（当前目标是资产，未回填到组件，请自行引用）");
        }

        /// <summary>多个网格：选一个工程内文件夹，按各自网格名批量另存。</summary>
        private void DuplicateManyToFolder(List<MeshEntry> entries)
        {
            string abs = EditorUtility.SaveFolderPanel(
                $"另存为独立 Mesh —— 为 {entries.Count} 个网格选择目标文件夹", "Assets", "");
            if (string.IsNullOrEmpty(abs)) return;

            // 必须落在工程 Assets 目录内，否则 AssetDatabase 无法处理。
            string dataPath = Application.dataPath.Replace('\\', '/');
            abs = abs.Replace('\\', '/');
            if (abs != dataPath && !abs.StartsWith(dataPath + "/"))
            {
                EditorUtility.DisplayDialog("路径无效",
                    "请选择本工程 Assets 目录下的文件夹。", "知道了");
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
            Debug.Log($"[SmoothNormal] 已把 {total} 个网格另存为独立资产到 {folderRel}" +
                      $"（其中 {reassignedCount} 个已回填到场景组件）。");
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
            GUILayout.Label("平滑法线生成器", _headerStyle);
            GUILayout.Label("Outline Smooth Normals Generator  •  Unity 2022.3+", _subHeaderStyle);
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(8);
            EditorGUILayout.EndVertical();

            // Accent line
            EditorGUI.DrawRect(new Rect(0, rect.yMax + 7, _dividerX, 2), ColorAccent);
        }
        #endregion

        #region 右侧界面
        private void DrawRightPanelTop()
        {
            GUILayout.Space(12);
            DrawSectionHeader("数据通道状态总览", "◈");
            GUILayout.Space(4);
            DrawDataStatusCards();
            GUILayout.Space(8);
        }

        /// <summary>把通道状态翻译成徽标的颜色与文字。</summary>
        private static (Color color, string text) DescribeState(ChannelState state)
        {
            switch (state)
            {
                case ChannelState.LikelySmoothNormals:
                    return (ColorSuccess, "● 可能是平滑法线");
                case ChannelState.HasData:
                    return (ColorWarning, "○ 有数据");
                default:
                    return (new Color(0.4f, 0.4f, 0.5f), "✕ 空");
            }
        }

        private static string ShortState(ChannelState state) => state switch
        {
            ChannelState.LikelySmoothNormals => "可能是法线",
            ChannelState.HasData             => "有数据",
            _                                => "空",
        };

        private void DrawDataStatusCards()
        {
            // ── Vertex Color ─────────────────────────────────────────
            DrawBigStatusCard(
                "顶点色  Vertex Color",
                "平滑法线 → 选定通道对（八面体编码）",
                _vertexColorState,
                new[]
                {
                    ("R 通道", _hasVcr ? "有变化" : "常量"),
                    ("G 通道", _hasVcg ? "有变化" : "常量"),
                    ("B 通道", _hasVcb ? "有变化" : "常量"),
                    ("A 通道", _hasVca ? "有变化" : "常量"),
                },
                ColorSuccess
            );

            GUILayout.Space(6);

            // ── Tangent ──────────────────────────────────────────────
            DrawBigStatusCard(
                "切线  Tangent",
                "平滑法线 → tangent.xyz（对象空间，会覆盖原始切线）",
                _tangentState,
                new[] { ("Tangent XYZ", ShortState(_tangentState)), ("Tangent W", "恒为 1") },
                ColorWarning
            );

            GUILayout.Space(6);

            // ── UV Channels ──────────────────────────────────────────
            var uvOverall = _uvStates.Contains(ChannelState.LikelySmoothNormals)
                ? ChannelState.LikelySmoothNormals
                : (_uvStates.Any(s => s != ChannelState.Empty) ? ChannelState.HasData : ChannelState.Empty);

            var uvItems = new (string, string)[UvChannelCount];
            for (int i = 0; i < UvChannelCount; i++)
                uvItems[i] = ($"TEXCOORD{i}", ShortState(_uvStates[i]));

            DrawBigStatusCard(
                "TEXCOORD 通道",
                "平滑法线 → 选定通道的 xyz（对象空间）",
                uvOverall,
                uvItems,
                ColorAccent
            );
        }

        #region UI 数据卡
        private void DrawBigStatusCard(string titleName, string desc, ChannelState state,
                                       (string label, string note)[] items, Color accentColor)
        {
            bool active = state != ChannelState.Empty;

            var bgRect = EditorGUILayout.BeginVertical();
            EditorGUI.DrawRect(new Rect(bgRect.x, bgRect.y, 3, bgRect.height + 10), active ? accentColor : ColorBorder);

            GUILayout.Space(8);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(10);

            EditorGUILayout.BeginVertical();

            // Title row
            EditorGUILayout.BeginHorizontal();
            var titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 11, normal = { textColor = Color.white } };
            GUILayout.Label(titleName, titleStyle);
            GUILayout.FlexibleSpace();

            // Status badge
            var (badgeColor, badgeText) = DescribeState(state);
            GUILayout.Label(badgeText, BadgeLabelStyle(badgeColor), GUILayout.Width(104));
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();

            // Desc
            var descStyle = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.6f, 0.65f, 0.72f) } };
            GUILayout.Label(desc, descStyle);
            GUILayout.Space(4);

            // Sub-items grid：每行最多 4 个，超出换行（TEXCOORD 有 8 个，一行放不下）。
            const int perRow = 4;
            for (int i = 0; i < items.Length; i += perRow)
            {
                EditorGUILayout.BeginHorizontal();
                for (int j = i; j < Mathf.Min(i + perRow, items.Length); j++)
                    DrawChannelChip(items[j].label, items[j].note, active, accentColor);
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(10);
            EditorGUILayout.EndVertical();
        }

        private void DrawChannelChip(string label, string note, bool active, Color accentColor)
        {
            var chipBg = active ? new Color(accentColor.r * 0.2f, accentColor.g * 0.2f, accentColor.b * 0.2f, 0.8f)
                                : new Color(0.12f, 0.13f, 0.16f);
            _chipBoxStyle.normal.background = MakeTex(2, 2, chipBg);

            EditorGUILayout.BeginVertical(_chipBoxStyle, GUILayout.Width(80));
            _chipLabelStyle.normal.textColor = active ? accentColor : new Color(0.5f, 0.5f, 0.6f);
            GUILayout.Label(label, _chipLabelStyle);
            GUILayout.Label(note, _chipNoteStyle);
            EditorGUILayout.EndVertical();
        }
        #endregion
        #endregion
        #endregion

        #region UI 标题Icon
        private void DrawHexIcon(Rect r, Color c)
        {
            Handles.BeginGUI();
            Handles.color = c;
            var center = new Vector2(r.x + r.width / 2, r.y + r.height / 2);
            float s = r.width * 0.42f;
            var pts = new Vector3[7];
            for (int i = 0; i < 6; i++)
            {
                float a = Mathf.PI / 2 + i * Mathf.PI / 3;
                pts[i] = new Vector3(center.x + Mathf.Cos(a) * s, center.y + Mathf.Sin(a) * s, 0);
            }
            pts[6] = pts[0];
            Handles.DrawAAPolyLine(2f, pts);
            // Inner dot
            Handles.DrawSolidDisc(center, Vector3.forward, s * 0.25f);
            Handles.EndGUI();
        }

        /// <summary>页签头部左上角六边形标志的统一尺寸 —— 以「平滑法线生成器」头部的图标为准。</summary>
        private const float HeaderIconSize = 36f;

        /// <summary>
        /// 绘制页签头部左上角的六边形标志。两处头部（生成器 / 导入自动烘焙）统一调用此方法，
        /// 尺寸与外观完全一致；此前自动烘焙头部用的是 28px、显得更粗，故收敛到这里。
        /// </summary>
        private void DrawHeaderIcon()
        {
            var iconRect = GUILayoutUtility.GetRect(HeaderIconSize, HeaderIconSize,
                GUILayout.Width(HeaderIconSize));
            DrawHexIcon(iconRect, ColorAccent);
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
            DrawSectionHeader("目标对象", "◉");
            EditorGUILayout.BeginVertical(_dataCardStyle);

            EditorGUI.BeginChangeCheck();
            var newObj = EditorGUILayout.ObjectField(
                new GUIContent("目标", "可以是：场景对象（含 MeshFilter / SkinnedMeshRenderer）、" +
                                       "模型文件（.fbx 等）、预制体，或直接选中一个 Mesh 资产"),
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
                    DrawTag(DescribeTargetSource(), ColorAccent);
                    DrawTag(_targetMesh.name, ColorCard * 1.4f);
                    EditorGUILayout.EndHorizontal();

                    if (_meshEntries.Count > 1)
                    {
                        int sel = SelectedCount;
                        EditorGUILayout.HelpBox(
                            sel <= 1
                                ? "勾选网格纳入批量编辑，描边预览会显示所有勾选项；单击网格名把它设为" +
                                  "焦点 —— 右侧通道状态与网格信息显示焦点网格（列表中高亮的一行）。"
                                : $"已勾选 {sel} 个网格：描边预览同时显示全部勾选项，「生成」「保存」" +
                                  "也作用于全部；右侧通道状态与网格信息只显示焦点网格（高亮行）。",
                            MessageType.Info);
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("该对象不含可处理的 Mesh。请选择含 MeshFilter / " +
                                            "SkinnedMeshRenderer 的对象、模型 / 预制体，或一个 Mesh 资产。",
                                            MessageType.Warning);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("请选择一个对象：场景中的网格对象，或 Project 中的 " +
                                        "Mesh / 模型 / 预制体资产。", MessageType.Info);
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
            var cntStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.6f, 0.65f, 0.72f) },
            };
            GUILayout.Label($"网格列表（已勾选 {SelectedCount} / {_meshEntries.Count}）", cntStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("全选", EditorStyles.miniButtonLeft, GUILayout.Width(44)))  SetAllSelected(true);
            if (GUILayout.Button("清空", EditorStyles.miniButtonRight, GUILayout.Width(44))) SetAllSelected(false);
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
                    EditorGUI.DrawRect(rowRect, new Color(ColorAccent.r, ColorAccent.g, ColorAccent.b, 0.14f));

                // 复选框：纳入批量编辑。
                bool chk = EditorGUILayout.Toggle(entry.Selected, GUILayout.Width(16));
                if (chk != entry.Selected)
                {
                    entry.Selected = chk;
                    OnSelectionSetChanged();
                }

                // 行名：单击设为焦点，不改变勾选状态。用 label 样式的按钮当作整行热区。
                var labelStyle = new GUIStyle(EditorStyles.label)
                {
                    fontStyle = focused ? FontStyle.Bold : FontStyle.Normal,
                    normal    = { textColor = focused ? Color.white : new Color(0.72f, 0.76f, 0.82f) },
                    hover     = { textColor = Color.white },
                    alignment = TextAnchor.MiddleLeft,
                };
                if (GUILayout.Button((focused ? "▸ " : "    ") + entry.Label, labelStyle))
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
            if (_targetSource is Mesh)               return "Mesh 资产";
            if (_targetSource is GameObject go)
                return EditorUtility.IsPersistent(go) ? "模型 / 预制体资产" : "场景对象";
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
        /// 「这个 UV 里装的是不是平滑法线」严格来说不可判定 —— 一组落在合理范围内的
        /// 贴图坐标与法线数据在数值上无法区分。但有一个很强的信号：本工具写入的是
        /// 【3 分量】单位向量，而贴图 UV 几乎总是 2 分量。因此仅在「维度为 3 且近似
        /// 单位长」时才说「可能是」，其余一律只说「有数据」。
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
                ? ChannelState.LikelySmoothNormals
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

            _healthReport = OutlineMeshValidator.Validate(_targetMesh, _storageMode);
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
            _foldoutMeshInfo = DrawFoldout(_foldoutMeshInfo, "网格信息", "▦");
            if (!_foldoutMeshInfo) return;

            EditorGUILayout.BeginVertical(_dataCardStyle);

            // 全部读缓存。这里原本每帧都会 marshal 一次 triangles（整份索引数组！）
            // 外加 normals / tangents / colors32 与 8 次 GetUVs。
            if (_meshCache == null)
            {
                GUILayout.Label("无网格数据", _subHeaderStyle);
            }
            else
            {
                DrawInfoRow("顶点数", _meshCache.VertexCount.ToString("N0"));
                DrawInfoRow("三角面数", _meshCache.TriangleCount.ToString("N0"));
                DrawInfoRow("SubMesh 数", _meshCache.SubMeshCount.ToString());
                DrawInfoRow("含法线", _meshCache.HasNormals ? "✓" : "✗");
                DrawInfoRow("含切线", _meshCache.HasTangents ? "✓" : "✗");
                DrawInfoRow("含顶点色", _meshCache.HasColors ? "✓" : "✗");

                for (int ch = 0; ch < UvChannelCount; ch++)
                {
                    int n = _meshCache.UvCounts[ch];
                    DrawInfoRow($"TEXCOORD{ch}", n > 0 ? $"✓ ({n}个)" : "—");
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
        
        private StorageMode _storageMode = StorageMode.VertexColor;
        // Vertex color channel pair
        private VertexColorChannel _vcChannel = VertexColorChannel.BA;

        // UV 通道一律以 TEXCOORDn 命名，取值与 mesh.SetUVs(n) 的索引恒等对应。
        // 不用「UV1/UV2」这类叫法：Unity 自己的 mesh.uv2 就是 TEXCOORD1，
        // 名字和索引差一位，此前工具与 Shader 正是因此整体错开了一格。
        private int _uvChannel = 1; // 默认 TEXCOORD1，避开主贴图 UV
        private readonly string[] _uvChannelNames =
        {
            "TEXCOORD0  (mesh.uv — 主贴图 UV)",
            "TEXCOORD1  (mesh.uv2)",
            "TEXCOORD2  (mesh.uv3)",
            "TEXCOORD3  (mesh.uv4)",
            "TEXCOORD4  (mesh.uv5)",
            "TEXCOORD5  (mesh.uv6)",
            "TEXCOORD6  (mesh.uv7)",
            "TEXCOORD7  (mesh.uv8)",
        };
        
        private void DrawStorageModeSection()
        {
            DrawSectionHeader("存储方式", "◈");
            EditorGUILayout.BeginVertical(_dataCardStyle);

            // Tabs
            EditorGUILayout.BeginHorizontal();
            DrawModeTab("顶点色\nVertex Color", StorageMode.VertexColor);
            DrawModeTab("切线空间\nTangent", StorageMode.TangentSpace);
            DrawModeTab("UV 通道\nUV Channel", StorageMode.UV);
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

            EditorGUILayout.EndVertical();
        }

        private void DrawModeTab(string label, StorageMode mode)
        {
            bool active = _storageMode == mode;
            var bgColor = active ? ColorAccent : ColorCard;
            var fgColor = active ? new Color(0.05f, 0.05f, 0.08f) : new Color(0.65f, 0.70f, 0.78f);

            var style = new GUIStyle(GUI.skin.button)
            {
                fontSize = 10,
                fontStyle = active ? FontStyle.Bold : FontStyle.Normal,
                normal = { textColor = fgColor, background = MakeTex(2, 2, bgColor) },
                hover = { textColor = fgColor, background = MakeTex(2, 2, bgColor * 1.1f) },
                padding = new RectOffset(6, 6, 6, 6),
                wordWrap = true,
                alignment = TextAnchor.MiddleCenter,
            };

            if (GUILayout.Button(label, style, GUILayout.Height(42))) _storageMode = mode;
        }

        #region UI 存储方式-顶点色
        /// <summary>
        /// 顶点色模式 UI：选择 RG / GB / BA 存储对，并用颜色指示 RGBA 各通道的数据状态。
        /// </summary>
        private void DrawVertexColorModeUI()
        {
            EditorGUILayout.BeginVertical(GetInnerCardStyle());

            // ── 通道选择 ────────────────────────────────────────────
            GUILayout.Label("存储通道对", new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.55f, 0.6f, 0.68f) } });
            GUILayout.Space(2);
            EditorGUILayout.BeginHorizontal();
            DrawVcChannelTab("RG", VertexColorChannel.RG);
            DrawVcChannelTab("GB", VertexColorChannel.GB);
            DrawVcChannelTab("BA", VertexColorChannel.BA);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(8);

            // ── RGBA 各通道状态 ──────────────────────────────────────
            GUILayout.Label("顶点色通道数据状态", new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.55f, 0.6f, 0.68f) } });
            GUILayout.Space(2);

            // 当前选中的通道对写入的是哪两个通道
            bool rIsWrite = _vcChannel == VertexColorChannel.RG;
            bool gIsWrite = _vcChannel == VertexColorChannel.RG || _vcChannel == VertexColorChannel.GB;
            bool bIsWrite = _vcChannel == VertexColorChannel.GB || _vcChannel == VertexColorChannel.BA;
            bool aIsWrite = _vcChannel == VertexColorChannel.BA;

            DrawVcChannelStatus("R 通道", _hasVcr, rIsWrite, "法线 X（RG 模式）");
            DrawVcChannelStatus("G 通道", _hasVcg, gIsWrite, "法线 X/Y（RG/GB 模式）");
            DrawVcChannelStatus("B 通道", _hasVcb, bIsWrite, "法线 X/Y（GB/BA 模式）");
            DrawVcChannelStatus("A 通道", _hasVca, aIsWrite, "法线 Y（BA 模式）");

            GUILayout.Space(4);
            EditorGUILayout.HelpBox("选定通道对的 XY 分量将被写入，Z 分量通过重建得到。非激活通道原有数据不受影响。", MessageType.None);

            // ── 清除按钮 ─────────────────────────────────────────────
            GUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            DrawClearChannelButton("清除 RG", _hasVcr || _hasVcg, () => ClearVertexColorChannels(true, true, false, false));
            DrawClearChannelButton("清除 GB", _hasVcg || _hasVcb, () => ClearVertexColorChannels(false, true, true, false));
            DrawClearChannelButton("清除 BA", _hasVcb || _hasVca, () => ClearVertexColorChannels(false, false, true, true));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        /// <summary>单个通道选择 Tab 按钮</summary>
        private void DrawVcChannelTab(string label, VertexColorChannel target)
        {
            bool active = _vcChannel == target;
            var bgColor = active ? ColorAccent : new Color(0.22f, 0.24f, 0.28f);
            var fgColor = active ? new Color(0.05f, 0.05f, 0.08f) : new Color(0.65f, 0.70f, 0.78f);
            var style = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                fontStyle = active ? FontStyle.Bold : FontStyle.Normal,
                normal = { textColor = fgColor, background = MakeTex(2, 2, bgColor) },
                hover  = { textColor = fgColor, background = MakeTex(2, 2, bgColor * 1.1f) },
                fixedHeight = 26,
            };
            if (GUILayout.Button(label, style))
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
                desc     = hasData ? $"将覆盖写入  •  {roleDesc}" : $"将写入  •  {roleDesc}";
            }
            else if (hasData)
            {
                dotColor = ColorWarning;   // 黄色：有数据但不是写入目标
                desc     = "有数据（非当前写入通道）";
            }
            else
            {
                dotColor = new Color(0.4f, 0.42f, 0.48f);   // 灰色：空
                desc     = "无数据";
            }

            DrawStatusIndicator(channelName, desc, dotColor);
        }
        #endregion

        #region UI 存储方式-切线空间
        /// <summary>
        /// 切线模式 UI：tangent.xyz 直接存对象空间平滑法线，w 恒为 1。
        /// 该模式会覆盖网格原始切线，必须明确告警。
        /// </summary>
        private void DrawTangentModeUI()
        {
            EditorGUILayout.BeginVertical(GetInnerCardStyle());
            bool tangentLikely = _tangentState == ChannelState.LikelySmoothNormals;
            DrawStatusIndicator("Tangent XYZ", ShortState(_tangentState), tangentLikely);
            DrawStatusIndicator("Tangent W", "恒为 1，不参与解码", tangentLikely);
            GUILayout.Space(4);

            // 此处原本写的是「兼容大多数标准 Shader」—— 恰好说反了。
            // 覆盖 tangent.xyz 正是对标准 Shader 兼容性破坏最大的做法。
            EditorGUILayout.HelpBox(
                "本模式会【覆盖网格的原始切线】，采样法线贴图的 Shader（URP/Lit、Standard 等）" +
                "将因此得到错误的 TBN，表现为法线贴图失效。\n\n" +
                "仅在该网格不使用法线贴图时选用。若只是想避开顶点色，" +
                "优先考虑 TEXCOORD 通道。\n\n" +
                "优点：可存完整三个分量，无需压缩、精度最高。",
                MessageType.Warning);

            GUILayout.Space(4);
            DrawClearChannelButton("重算切线（恢复正常切线）",
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
            EditorGUILayout.BeginVertical(GetInnerCardStyle());
            _uvChannel = EditorGUILayout.Popup("存储通道", _uvChannel, _uvChannelNames);
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
                    desc     = hasData ? $"当前选中，将覆盖写入（{ShortState(state)}）" : "当前选中，将写入此通道";
                    dotColor = Color.white;
                }
                else
                {
                    desc     = ShortState(state);
                    dotColor = state switch
                    {
                        ChannelState.LikelySmoothNormals => ColorSuccess,
                        ChannelState.HasData             => ColorWarning,
                        _                                => ColorGray,
                    };
                }

                // 状态行 + 右侧清除按钮
                EditorGUILayout.BeginHorizontal();
                DrawStatusIndicator($"TEXCOORD{i}", desc, dotColor);
                GUILayout.FlexibleSpace();
                int capturedIndex = i;
                GUI.enabled = hasData;
                if (GUILayout.Button("清除", GUILayout.Width(44), GUILayout.Height(16)))
                    TryClearUV(capturedIndex);
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();
            }

            GUILayout.Space(4);
            if (IsRiskyUVChannel(_uvChannel))
            {
                EditorGUILayout.HelpBox(
                    "TEXCOORD0 是模型的主贴图 UV，该网格已有数据。写入会覆盖它并破坏贴图映射，" +
                    "且影响所有使用此网格的对象。除非你确定该通道空闲，否则请改用 TEXCOORD1。",
                    MessageType.Error);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "平滑法线 XY 分量存入选定 TEXCOORD 通道的 xy 分量，Z 分量通过 sqrt 重建。",
                    MessageType.None);
            }
            EditorGUILayout.EndVertical();
        }

        /// <summary>清除前对主贴图 UV 做二次确认，避免一键静默毁掉贴图映射。</summary>
        private void TryClearUV(int channel)
        {
            if (channel == 0 &&
                !EditorUtility.DisplayDialog(
                    "清除主贴图 UV？",
                    $"TEXCOORD0 是「{_targetMesh.name}」的主贴图 UV（mesh.uv）。\n\n" +
                    "清除后该网格的贴图映射会丢失，且影响所有使用此网格的对象。\n\n" +
                    "确定要清除吗？",
                    "确定清除", "取消"))
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
            DrawSectionHeader("生成平滑法线", "◈");

            EditorGUILayout.BeginVertical(_dataCardStyle);

            DrawHealthCard();

            int  selCount    = SelectedCount;
            bool canGenerate = selCount > 0;
            GUI.enabled = canGenerate;

            // ── 合并容差 ─────────────────────────────────────────────
            _mergeTolerance = EditorGUILayout.Slider(
                new GUIContent("合并容差",
                    "距离在此范围内的顶点视为同一点，其面法线会被合并平均。\n\n" +
                    "接缝顶点经 DCC 导出、FBX 浮点截断或缩放后往往会有 1e-6 量级的微小偏差，" +
                    "容差过小会让它们无法合并、描边在接缝处仍然开裂。\n\n" +
                    "容差必须远小于模型的最小真实特征尺寸，否则会把本应分开的顶点错误合并。"),
                _mergeTolerance,
                OutlineSmoothNormalsCalculator.MinMergeTolerance,
                OutlineSmoothNormalsCalculator.MaxMergeTolerance);

            if (_mergeTolerance > 0.005f)
            {
                EditorGUILayout.HelpBox(
                    "容差偏大，可能把本应分开的顶点错误合并，导致描边变形。",
                    MessageType.Warning);
            }

            GUILayout.Space(4);

            // Big generate button
            var btnStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                fixedHeight = 44,
                normal = { textColor = new Color(0.05f, 0.05f, 0.08f), background = MakeTex(2, 2, canGenerate ? ColorAccent : Color.gray) },
                hover = { textColor = new Color(0.05f, 0.05f, 0.08f), background = MakeTex(2, 2, canGenerate ? ColorAccent * 1.1f : Color.gray) },
            };

            string modeLabel = _storageMode == StorageMode.VertexColor ? "顶点色" :
                               _storageMode == StorageMode.TangentSpace ? "切线空间" : $"TEXCOORD{_uvChannel}";
            string countSuffix = selCount > 1 ? $"  ×{selCount}" : "";

            if (GUILayout.Button($"▶  生成平滑法线  →  {modeLabel}{countSuffix}", btnStyle))
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

            string body = "网格健康检查（焦点网格）";
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
        
        private void DrawPreviewLaunchPanel()
        {
            if (_previewUtil == null) SetupPreviewRenderer();

            // Section header（在 ScrollView 外，固定高度）
            GUILayout.Space(4);
            DrawSectionHeader("描边预览", "◉");
            GUILayout.Space(4);

            float paramW   = 220f;
            float totalW   = position.width - _dividerX - 16f;
            float previewW = Mathf.Max(80f, totalW - paramW - 2f);

            // 用 GUILayoutUtility.GetRect + ExpandHeight 让 Layout 分配所有剩余高度
            EditorGUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));

            // ── Viewport ──────────────────────────────────────────────
            var previewRect = GUILayoutUtility.GetRect(previewW, previewW,
                GUILayout.Width(previewW), GUILayout.ExpandHeight(true));
            DrawInlineViewport(previewRect);

            // ── Divider ───────────────────────────────────────────────
            EditorGUI.DrawRect(new Rect(previewRect.xMax, previewRect.y, 2, previewRect.height), ColorBorder);

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
                var s = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = new Color(0.4f, 0.45f, 0.5f) },
                };
                GUI.Label(r, _meshEntries.Count > 0 ? "请勾选要预览的 Mesh" : "请先选择 Mesh", s);
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

            // 绘制所有【勾选】的网格，各自按 PreviewMatrix 摆到相对根对象的位置上。
            // 逐 SubMesh 绘制：多材质模型此前只能预览到第一个 SubMesh。
            if (_showBase && _previewBaseMat)
            {
                UpdatePreviewBaseMat();
                foreach (var e in _meshEntries)
                {
                    if (!e.Selected || !e.Mesh) continue;
                    for (int i = 0; i < e.Mesh.subMeshCount; i++)
                        _previewUtil.DrawMesh(e.Mesh, e.PreviewMatrix, _previewBaseMat, i);
                }
            }
            if (_showOutline && _previewOutlineMat)
            {
                UpdatePreviewOutlineMat();
                foreach (var e in _meshEntries)
                {
                    if (!e.Selected || !e.Mesh) continue;
                    for (int i = 0; i < e.Mesh.subMeshCount; i++)
                        _previewUtil.DrawMesh(e.Mesh, e.PreviewMatrix, _previewOutlineMat, i);
                }
            }

            _previewUtil.camera.Render();
            var tex = _previewUtil.EndPreview();
            GUI.DrawTexture(r, tex, ScaleMode.StretchToFill, false);

            // Overlay: mode badge
            var badgeRect = new Rect(r.x + 6, r.y + 6, 150, 20);
            EditorGUI.DrawRect(badgeRect, new Color(0.05f, 0.06f, 0.08f, 0.82f));
            string modeLabel = _storageMode == StorageMode.VertexColor ? "顶点色 模式" :
                               _storageMode == StorageMode.TangentSpace ? "切线空间 模式" :
                               $"TEXCOORD{_uvChannel} 模式";
            GUI.Label(new Rect(badgeRect.x + 6, badgeRect.y, badgeRect.width, badgeRect.height),
                      $"● {modeLabel}", ViewportBadgeStyle(ColorAccent));

            // 法线叠加层只针对【焦点】网格（右侧数据缓存 _meshCache 也只缓存它），
            // 且仅当焦点网格已勾选、确实在预览中时才画 —— 否则会把线段叠到一个根本
            // 没渲染的网格上。用焦点网格自己的 PreviewMatrix 把顶点摆到与渲染一致的位置。
            // 守卫已提到本方法顶部 —— 此前是 DrawNormalsOverlay(r, GetDecodedSmoothNormals(), …)，
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
            var hintRect = new Rect(r.x, r.yMax - 22, r.width, 22);
            EditorGUI.DrawRect(hintRect, new Color(0.05f, 0.06f, 0.08f, 0.72f));
            GUI.Label(hintRect, "左键旋转  |  滚轮缩放  |  中键平移", HintLabelStyle());
        }

        /// <summary>
        /// 在预览视口上叠加绘制法线方向线段。
        /// normals 为对象空间法线数组，与 mesh.vertices 一一对应。
        ///
        /// 用 Handles 而不是 GL.LoadPixelMatrix 画：后者是直接写投影矩阵的，
        /// 其原点是【整个窗口渲染目标】的左上角 —— 包含标签页头部，而 OnGUI
        /// 的坐标系原点在头部下方。两者差一个 header 高度，会让线段整体上移。
        /// Handles.BeginGUI 用的就是 OnGUI 的坐标系，与 r 天然对齐。
        /// （同文件的 DrawHexIcon 一直是这么画的。）
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
        /// </summary>
        private Vector3[] GetDecodedSmoothNormals()
        {
            if (_targetMesh == null) return null;
            int vCount = _targetMesh.vertexCount;
            var result = new Vector3[vCount];

            switch (_storageMode)
            {
                // ── 顶点色（八面体编码）──────────────────────────────
                case StorageMode.VertexColor:
                {
                    var colors = _targetMesh.colors32;
                    if (colors == null || colors.Length != vCount) return null;
                    for (int i = 0; i < vCount; i++)
                    {
                        var c = colors[i];
                        byte x, y;
                        switch (_vcChannel)
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
                    var tangents = _targetMesh.tangents;
                    if (tangents == null || tangents.Length != vCount) return null;
                    for (int i = 0; i < vCount; i++)
                    {
                        var t = tangents[i];
                        result[i] = new Vector3(t.x, t.y, t.z).normalized;
                    }
                    break;
                }

                // ── TEXCOORD 通道（uv.xyz 直接是对象空间法线）────────
                case StorageMode.UV:
                {
                    var uvList = new List<Vector3>();
                    _targetMesh.GetUVs(_uvChannel, uvList);
                    if (uvList.Count != vCount) return null;
                    for (int i = 0; i < vCount; i++)
                        result[i] = uvList[i].normalized;
                    break;
                }
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

        private void DrawInlinePreviewParams()
        {
            // 描边参数
            GUILayout.Space(6);
            DrawPreviewParamHeader("描边参数");
            EditorGUILayout.BeginVertical(GetInnerCardStyle());
            _showOutline = EditorGUILayout.Toggle("显示描边", _showOutline);
            GUI.enabled = _showOutline;
            EditorGUI.BeginChangeCheck();
            _outlineColor = EditorGUILayout.ColorField("描边颜色", _outlineColor);
            // 上限与 Outline.shader 的 _OutlineWidth Range(0, 0.1) 保持一致：
            // 两边现在用同一套外扩数学，数值必须可直接对照。
            _outlineWidth = EditorGUILayout.Slider("描边宽度", _outlineWidth, 0.001f, 0.1f);
            _outlineWidthMode = EditorGUILayout.Popup(
                new GUIContent("宽度模式",
                    "屏幕空间：描边等宽，不随距离变化；\n世界空间：按世界单位偏移，近大远小。"),
                _outlineWidthMode, new[] { "屏幕空间", "世界空间" });
            if (EditorGUI.EndChangeCheck()) Repaint();
            GUI.enabled = true;
            EditorGUILayout.EndVertical();

            // 模型参数
            GUILayout.Space(4);
            DrawPreviewParamHeader("模型参数");
            EditorGUILayout.BeginVertical(GetInnerCardStyle());
            _showBase = EditorGUILayout.Toggle("显示模型", _showBase);
            GUI.enabled = _showBase;
            EditorGUI.BeginChangeCheck();
            _baseColor  = EditorGUILayout.ColorField("基础颜色", _baseColor);
            _smoothness = EditorGUILayout.Slider("光滑度", _smoothness, 0f, 1f);
            _metallic   = EditorGUILayout.Slider("金属度", _metallic, 0f, 1f);
            if (EditorGUI.EndChangeCheck()) Repaint();
            GUI.enabled = true;
            EditorGUILayout.EndVertical();

            // 视口参数
            GUILayout.Space(4);
            DrawPreviewParamHeader("视口参数");
            EditorGUILayout.BeginVertical(GetInnerCardStyle());
            EditorGUI.BeginChangeCheck();
            _previewBgColor = EditorGUILayout.ColorField("背景颜色", _previewBgColor);
            if (EditorGUI.EndChangeCheck()) Repaint();
            EditorGUILayout.EndVertical();

            // 法线可视化
            GUILayout.Space(4);
            DrawPreviewParamHeader("法线可视化");
            EditorGUILayout.BeginVertical(GetInnerCardStyle());
            EditorGUI.BeginChangeCheck();

            // 平滑法线
            _showNormals  = EditorGUILayout.Toggle("显示平滑法线", _showNormals);
            GUI.enabled   = _showNormals;
            _normalLength = EditorGUILayout.Slider("法线长度", _normalLength, 0.005f, 0.5f);
            _normalColor  = EditorGUILayout.ColorField("平滑法线颜色", _normalColor);
            GUI.enabled   = true;

            EditorGUILayout.Space(2);

            // 原始法线
            _showOriginalNormals = EditorGUILayout.Toggle("显示原始法线", _showOriginalNormals);
            GUI.enabled          = _showOriginalNormals;
            _originalNormalColor = EditorGUILayout.ColorField("原始法线颜色", _originalNormalColor);
            GUI.enabled          = true;

            if (EditorGUI.EndChangeCheck()) Repaint();
            EditorGUILayout.EndVertical();

            // 相机控制
            GUILayout.Space(4);
            DrawPreviewParamHeader("相机控制");
            EditorGUILayout.BeginVertical(GetInnerCardStyle());
            EditorGUI.BeginChangeCheck();
            _previewOrbit.x = EditorGUILayout.Slider("水平旋转", _previewOrbit.x, -180f, 180f);
            _previewOrbit.y = EditorGUILayout.Slider("垂直旋转", _previewOrbit.y, -89f, 89f);
            _previewZoom    = EditorGUILayout.Slider("距离", _previewZoom, 0.1f, 20f);
            if (EditorGUI.EndChangeCheck()) Repaint();
            if (GUILayout.Button("重置视角"))
            {
                _previewOrbit = new Vector2(30f, -20f);
                FramePreviewToChecked();   // 兜住所有勾选的网格
                Repaint();
            }
            EditorGUILayout.EndVertical();
            GUILayout.FlexibleSpace();
        }

        private void DrawPreviewParamHeader(string headerText)
        {
            var s = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 10,
                normal = { textColor = ColorAccent },
            };
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(2);
            GUILayout.Label(headerText, s);
            EditorGUILayout.EndHorizontal();
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
            if (_previewOutlineMat.HasProperty(PropOutlineColor)) _previewOutlineMat.SetColor(PropOutlineColor, _outlineColor);
            if (_previewOutlineMat.HasProperty(PropOutlineWidth)) _previewOutlineMat.SetFloat(PropOutlineWidth, _outlineWidth);
            if (_previewOutlineMat.HasProperty(PropOutlineWidthMode)) _previewOutlineMat.SetFloat(PropOutlineWidthMode, _outlineWidthMode);
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

            if (_previewBaseMat.HasProperty(PropMetallic))
                _previewBaseMat.SetFloat(PropMetallic, _metallic);
        }

        private void UpdatePreviewBaseMat()
        {
            ApplyBaseMatParams();
        }

        private void UpdatePreviewOutlineMat()
        {
            if (!_previewOutlineMat) return;
            if (_previewOutlineMat.HasProperty(PropOutlineColor)) _previewOutlineMat.SetColor(PropOutlineColor, _outlineColor);
            if (_previewOutlineMat.HasProperty(PropOutlineWidth)) _previewOutlineMat.SetFloat(PropOutlineWidth, _outlineWidth);
            if (_previewOutlineMat.HasProperty(PropOutlineWidthMode)) _previewOutlineMat.SetFloat(PropOutlineWidthMode, _outlineWidthMode);
            if (_previewOutlineMat.HasProperty(PropStorageMode))  _previewOutlineMat.SetFloat(PropStorageMode,  (float)_storageMode);
            if (_previewOutlineMat.HasProperty(PropUVChannel))    _previewOutlineMat.SetFloat(PropUVChannel,    _uvChannel);
            if (_previewOutlineMat.HasProperty(PropVcChannel))    _previewOutlineMat.SetFloat(PropVcChannel,    (float)_vcChannel);
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
                Debug.LogWarning("[SmoothNormal] 未勾选任何网格，请在目标列表中勾选要处理的网格。");
                return;
            }

            // 健康检查：任一勾选网格存在无法处理的数据（Error）时，先给一次二次确认。
            var unhealthy = targets.FirstOrDefault(
                m => OutlineMeshValidator.Validate(m, _storageMode).HasError);
            if (unhealthy != null &&
                !EditorUtility.DisplayDialog(
                    "网格数据异常",
                    $"网格「{unhealthy.name}」存在无法处理的数据问题（详见「生成平滑法线」区域的健康检查），" +
                    "生成结果可能不正确或失败。仍要继续吗？",
                    "仍要继续", "取消"))
                return;

            // 写入 TEXCOORD0（主贴图 UV）是唯一需要拦的破坏性操作。批量时对所有
            // TEXCOORD0 已有数据的勾选网格一并确认一次。
            bool anyRiskyUV = _storageMode == StorageMode.UV && _uvChannel == 0 &&
                targets.Any(m => m.GetVertexAttributeDimension(
                    UnityEngine.Rendering.VertexAttribute.TexCoord0) > 0);
            if (anyRiskyUV &&
                !EditorUtility.DisplayDialog(
                    "覆盖主贴图 UV？",
                    "当前存储通道是 TEXCOORD0（主贴图 mesh.uv）。\n\n" +
                    $"对勾选的 {targets.Count} 个网格写入平滑法线会覆盖各自的主贴图 UV，贴图映射将丢失，" +
                    "且影响所有使用这些网格的对象。\n\n建议改用 TEXCOORD1。仍要继续吗？",
                    "仍要覆盖", "取消"))
                return;

            GenerateSmoothNormals(targets);
        }

        /// <summary>对勾选集合里的每个网格按当前存储模式生成并写入平滑法线。</summary>
        private void GenerateSmoothNormals(List<Mesh> targets)
        {
            int ok = 0;
            foreach (var mesh in targets)
            {
                var smoothNormals = OutlineSmoothNormalsCalculator.Calculate(mesh, _mergeTolerance);
                if (smoothNormals == null) continue;   // 具体原因已由 Calculate 打印

                // 计算成功、真要动数据之前才抓快照（每个网格各存一份，供批量还原）。
                CaptureSnapshot(mesh);

                switch (_storageMode)
                {
                    case StorageMode.VertexColor:
                        StorageWriter.WriteToVertexColor(mesh, smoothNormals, _vcChannel);
                        break;
                    case StorageMode.TangentSpace:
                        StorageWriter.WriteToTangent(mesh, smoothNormals);
                        break;
                    case StorageMode.UV:
                        StorageWriter.WriteToUV(mesh, smoothNormals, _uvChannel);
                        break;
                }

                EditorUtility.SetDirty(mesh);
                _dirtyMeshes.Add(mesh);
                ok++;
                Debug.Log($"[SmoothNormal] 生成完成 → 模式: {_storageMode}, Mesh: {mesh.name}, " +
                          $"顶点数: {mesh.vertexCount}, 合并容差: {_mergeTolerance:G}");
            }

            if (ok > 0) _saveState = SaveState.NeedSave;
            RefreshDataStatus();   // 焦点网格
            Repaint();

            if (ok > 1) Debug.Log($"[SmoothNormal] 批量生成完成，共处理 {ok} 个网格。");
        }

        // ─────────────────────────────────────────────────────────────
        private void ClearVertexColorChannels(bool clearR, bool clearG, bool clearB, bool clearA)
        {
            if (!_targetMesh) return;
            // 清除同样是破坏性的，「还原本次修改」必须对它一样有效。
            CaptureSnapshot(_targetMesh);
            Undo.RecordObject(_targetMesh, "Clear Vertex Color Channels");
            int vCount = _targetMesh.vertexCount;
            var existing = _targetMesh.colors32;
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
            _targetMesh.colors32 = colors;
            EditorUtility.SetDirty(_targetMesh);
            RefreshDataStatus();
            MarkDirty();
            Repaint();
        }
        private void ClearTangents()
        {
            if (!_targetMesh) return;
            CaptureSnapshot(_targetMesh);
            Undo.RecordObject(_targetMesh, "Recalculate Tangents");

            // 不要设成 null —— 那会让网格彻底失去切线，所有采样法线贴图的
            // Shader 都会得到未定义的 TBN，且影响每一个引用该 sharedMesh 的
            // 对象，除了重导入模型没有任何恢复手段。
            // 重算出一份真实切线，把网格还原成「正常」状态。
            _targetMesh.RecalculateTangents();
            EditorUtility.SetDirty(_targetMesh);
            RefreshDataStatus();
            MarkDirty();
            Repaint();
        }
        private void ClearUV(int ch)
        {
            if (!_targetMesh) return;
            CaptureSnapshot(_targetMesh);
            Undo.RecordObject(_targetMesh, $"Clear TEXCOORD{ch}");
            _targetMesh.SetUVs(ch, (List<Vector2>)null);
            EditorUtility.SetDirty(_targetMesh);
            RefreshDataStatus();
            MarkDirty();
            Repaint();
        }
        #endregion
        
        #region 辅助方法
        private void InitStyles()
        {
            if (_stylesInitialized) return;

            _headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                normal = { textColor = Color.white },
            };

            _subHeaderStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 9,
                normal = { textColor = new Color(0.5f, 0.6f, 0.7f) },
            };

            _dataCardStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(10, 10, 8, 8),
                margin = new RectOffset(8, 8, 2, 2),
            };

            _innerCardStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(8, 8, 6, 6),
            };

            _sectionHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                normal = { textColor = ColorAccent },
            };

            // 卡片徽标：右对齐
            _badgeLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 9,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight,
            };

            // 视口左上角徽标：左对齐
            _viewportBadgeStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
            };

            _hintLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.5f, 0.55f, 0.62f) },
                alignment = TextAnchor.MiddleCenter,
            };

            _chipBoxStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(6, 6, 4, 4),
                margin  = new RectOffset(2, 2, 0, 0),
            };
            _chipLabelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 9,
                alignment = TextAnchor.MiddleCenter,
            };
            _chipNoteStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 8,
                normal = { textColor = new Color(0.5f, 0.55f, 0.62f) },
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
            };

            _dotStyle            = new GUIStyle(GUI.skin.label) { fontSize = 14 };
            _indicatorLabelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 10,
                normal = { textColor = Color.white },
            };
            _indicatorDescStyle  = new GUIStyle(EditorStyles.miniLabel);

            _stylesInitialized = true;
        }

        // ── 复用的样式 ───────────────────────────────────────────────
        // IMGUI 是立即模式：Label/Box 在调用时就会读取 style，因此复用同一个
        // 对象、每次只改颜色是安全的。此前每个事件都要 new 60+ 个 GUIStyle 与
        // RectOffset，光 GetInnerCardStyle() 每次 OnGUI 就跑 8 次。
        private GUIStyle _innerCardStyle;
        private GUIStyle _sectionHeaderStyle;
        private GUIStyle _badgeLabelStyle;
        private GUIStyle _viewportBadgeStyle;
        private GUIStyle _hintLabelStyle;
        private GUIStyle _chipBoxStyle;
        private GUIStyle _chipLabelStyle;
        private GUIStyle _chipNoteStyle;
        private GUIStyle _dotStyle;
        private GUIStyle _indicatorLabelStyle;
        private GUIStyle _indicatorDescStyle;

        /// <summary>卡片右侧徽标（右对齐）。</summary>
        private GUIStyle BadgeLabelStyle(Color c)
        {
            _badgeLabelStyle.normal.textColor = c;
            return _badgeLabelStyle;
        }

        /// <summary>视口左上角徽标（左对齐）。</summary>
        private GUIStyle ViewportBadgeStyle(Color c)
        {
            _viewportBadgeStyle.normal.textColor = c;
            return _viewportBadgeStyle;
        }

        private GUIStyle HintLabelStyle() => _hintLabelStyle;

        private void DrawSectionHeader(string titleName, string icon)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            GUILayout.Label($"{icon}  {titleName}", _sectionHeaderStyle);
            EditorGUILayout.EndHorizontal();
        }

        private bool DrawFoldout(bool state, string titleName, string icon)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            var s = new GUIStyle(EditorStyles.foldout)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                normal = { textColor = ColorAccent },
                onNormal = { textColor = ColorAccent },
            };
            bool result = EditorGUILayout.Foldout(state, $"{icon}  {titleName}", true, s);
            EditorGUILayout.EndHorizontal();
            return result;
        }

        private void DrawInfoRow(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            var lStyle = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = Color.white } };
            var vStyle = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = Color.white }, alignment = TextAnchor.MiddleRight };
            GUILayout.Label(label, lStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label(value, vStyle, GUILayout.Width(120));
            EditorGUILayout.EndHorizontal();

            // 细分割线
            var lineRect = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(lineRect, new Color(0.28f, 0.30f, 0.36f, 0.6f));
        }

        private void DrawStatusIndicator(string label, string desc, bool active)
        {
            DrawStatusIndicator(label, desc, active ? ColorSuccess : new Color(0.4f, 0.42f, 0.48f));
        }

        private void DrawStatusIndicator(string label, string desc, Color dotColor)
        {
            EditorGUILayout.BeginHorizontal();

            _dotStyle.normal.textColor = dotColor;
            GUILayout.Label("●", _dotStyle, GUILayout.Width(18));

            GUILayout.Label(label, _indicatorLabelStyle, GUILayout.Width(100));

            _indicatorDescStyle.normal.textColor = dotColor;
            GUILayout.Label(desc, _indicatorDescStyle);

            EditorGUILayout.EndHorizontal();
        }

        private void DrawTag(string text, Color bg)
        {
            var s = new GUIStyle(GUI.skin.box)
            {
                fontSize = 9,
                padding = new RectOffset(5, 5, 2, 2),
                margin = new RectOffset(2, 2, 2, 2),
                normal = { background = MakeTex(2, 2, bg), textColor = Color.white },
            };
            GUILayout.Label(text, s);
        }

        private void DrawClearChannelButton(string label, bool enabled, System.Action onClick)
        {
            GUI.enabled = enabled;
            var style = new GUIStyle(GUI.skin.button)
            {
                fontSize = 10,
                normal = { textColor = enabled ? new Color(1f, 0.55f, 0.45f) : new Color(0.4f, 0.42f, 0.48f) },
            };
            if (GUILayout.Button(label, style))
                onClick?.Invoke();
            GUI.enabled = true;
        }

        private GUIStyle GetInnerCardStyle() => _innerCardStyle;

        private static readonly Dictionary<Color, Texture2D> TEXCache = new Dictionary<Color, Texture2D>();
        private static Texture2D MakeTex(int w, int h, Color col)
        {
            if (TEXCache.TryGetValue(col, out var cached) && cached) return cached;

            // HideAndDontSave 是必需的：否则这些纹理会在每次域重载时触发
            // 「Texture2D has been leaked」刷屏。
            var tex = new Texture2D(w, h) { hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color[w * h];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = col;
            tex.SetPixels(pixels);
            tex.Apply();
            TEXCache[col] = tex;
            return tex;
        }
        #endregion
    }
}
