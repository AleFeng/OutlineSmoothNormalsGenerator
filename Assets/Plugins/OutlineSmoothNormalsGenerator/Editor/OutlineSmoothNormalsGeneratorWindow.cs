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
        private MeshSnapshot _snapshot;

        private void CaptureSnapshot(Mesh mesh)
        {
            if (!mesh) return;

            var snap = new MeshSnapshot
            {
                Mesh     = mesh,
                Colors   = mesh.colors32?.Clone() as Color32[],
                Tangents = mesh.tangents?.Clone() as Vector4[],
                Uvs      = new List<Vector4>[4],
            };
            for (int i = 0; i < 4; i++)
            {
                var list = new List<Vector4>();
                mesh.GetUVs(i, list);
                snap.Uvs[i] = list;
            }
            _snapshot = snap;
        }

        private void RestoreSnapshot()
        {
            if (_snapshot == null || _snapshot.Mesh != _targetMesh) return;

            // 空数组/空列表即代表「该通道原本就没有数据」，赋回去正好清空。
            _targetMesh.colors32 = _snapshot.Colors;
            _targetMesh.tangents = _snapshot.Tangents;
            for (int i = 0; i < 4; i++)
                _targetMesh.SetUVs(i, _snapshot.Uvs[i]);

            EditorUtility.SetDirty(_targetMesh);
            RefreshDataStatus();

            _snapshot = null;
            _dirtyMeshes.Remove(_targetMesh);
            _saveState = SaveState.Clean;
            Repaint();
            Debug.Log($"[SmoothNormal] 已还原网格「{_targetMesh.name}」到本次生成之前的状态。");
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

        /// <summary>不可写时给出准确的原因与出路，绝不含糊其辞。</summary>
        private static string DescribeWritability(MeshWritability w) => w switch
        {
            MeshWritability.ImportedSubAsset =>
                "该网格是模型文件（.fbx 等）导入生成的子资产，属于只读数据。\n\n" +
                "写入的平滑法线不会被保存 —— 下次重导入模型、改动 .meta 或重建 Library 时都会丢失。\n\n" +
                "请点击「另存为独立 Mesh」，复制一份可写的 .asset 再使用。",
            MeshWritability.BuiltIn =>
                "该网格是 Unity 内置资源（如 Cube / Sphere），只读，无法保存。\n\n" +
                "请点击「另存为独立 Mesh」，复制一份可写的 .asset 再使用。",
            MeshWritability.NotAnAsset =>
                "该网格不是项目中的资源文件（可能由脚本在运行时生成）。\n\n" +
                "请点击「另存为独立 Mesh」，先把它存成 .asset。",
            _ => string.Empty,
        };

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

        private ChannelState _vertexColorState;
        private ChannelState _tangentState;
        private readonly ChannelState[] _uvStates = new ChannelState[4];

        // 顶点色各通道是否承载了逐顶点变化的数据
        private bool _hasVcr, _hasVcg, _hasVcb, _hasVca;

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
        private bool _foldoutMeshInfo = true;

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
        private void OnGUI()
        {
            InitStyles();

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
        
        private void DrawDivider()
        {
            var dividerRect = new Rect(_dividerX, 0, 4, position.height);
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
            var dividerRect = new Rect(_dividerX - 2, 0, 8, position.height);
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
            var go  = Selection.activeGameObject;
            var mf  = go ? go.GetComponent<MeshFilter>() : null;
            var smr = go ? go.GetComponent<SkinnedMeshRenderer>() : null;

            // 只在选中【含网格】的对象时切换目标，且四个字段一起更新。
            // 此前 _meshFilter/_skinnedMeshRenderer 是无条件赋值的，而
            // _targetObject/_targetMesh 只在 if 内更新 —— 选中一个非网格对象后，
            // 组件引用变 null 但旧网格还挂着，界面显示渲染器为「—」，
            // 生成按钮却仍然对着上一个网格开火。
            if (mf || smr)
            {
                _targetObject        = go;
                _meshFilter          = mf;
                _skinnedMeshRenderer = smr;
                RefreshTargetMesh();
            }
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

            var writability = _targetMesh
                ? GetWritability(_targetMesh, out _)
                : MeshWritability.NotAnAsset;
            bool writable = _targetMesh && writability == MeshWritability.Writable;

            // ── 第一行：还原 + 保存 ──────────────────────────────────
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);

            // 还原：仅在本次会话确实抓到过快照时可用。
            GUI.enabled = _snapshot != null && _snapshot.Mesh == _targetMesh;
            if (GUILayout.Button(new GUIContent("↺  还原本次修改",
                    "把网格恢复到本次生成之前的状态。\n\n" +
                    "这是本工具自己的快照，与 Unity 的 Undo 无关 —— Undo 不跟踪网格顶点数据。"),
                    GUILayout.Width(110), GUILayout.Height(26)))
                RestoreSnapshot();
            GUI.enabled = true;

            Color  btnColor;
            string btnLabel;
            bool   canSave;

            if (!writable && _targetMesh)
            {
                btnColor = ColorGray;
                btnLabel = "⛔  不可保存";
                canSave  = false;
            }
            else
            {
                switch (_saveState)
                {
                    case SaveState.NeedSave:
                        btnColor = ColorWarning; btnLabel = "⚠  需要保存"; canSave = true;
                        break;
                    case SaveState.Saved:
                        btnColor = ColorSuccess; btnLabel = "✓  保存完成"; canSave = false;
                        break;
                    default:
                        btnColor = ColorGray;    btnLabel = "—  无修改";   canSave = false;
                        break;
                }
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
            if (GUILayout.Button(btnLabel, style))
                SaveMeshAsset();
            GUI.enabled = true;

            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();

            // ── 第二行：另存为独立 Mesh ──────────────────────────────
            GUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);

            GUI.enabled = _targetMesh;
            // 网格不可写时，这是唯一出路，因此高亮它。
            var dupColor = (!writable && _targetMesh) ? ColorAccent : ColorCard * 1.5f;
            var dupStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize    = 11,
                fontStyle   = (!writable && _targetMesh) ? FontStyle.Bold : FontStyle.Normal,
                fixedHeight = 24,
                normal      = { textColor = (!writable && _targetMesh)
                                    ? new Color(0.05f, 0.05f, 0.08f) : new Color(0.75f, 0.78f, 0.82f),
                                background = MakeTex(2, 2, dupColor) },
                hover       = { textColor = new Color(0.05f, 0.05f, 0.08f),
                                background = MakeTex(2, 2, dupColor * 1.12f) },
            };
            if (GUILayout.Button(new GUIContent("⧉  另存为独立 Mesh…",
                    "复制一份可写的 .asset 网格，并自动替换到当前对象上。"), dupStyle))
                DuplicateMeshToAsset();
            GUI.enabled = true;

            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(6);
        }

        private void SaveMeshAsset()
        {
            if (!_targetMesh) return;

            var writability = GetWritability(_targetMesh, out string path);
            if (writability != MeshWritability.Writable)
            {
                // 绝不在这条路径上报告成功 —— 数据确实没有落盘。
                string reason = DescribeWritability(writability);
                Debug.LogError($"[SmoothNormal] 无法保存网格「{_targetMesh.name}」：\n{reason}");
                if (EditorUtility.DisplayDialog("无法保存", reason, "另存为独立 Mesh…", "取消"))
                    DuplicateMeshToAsset();
                return;
            }

            AssetDatabase.SaveAssetIfDirty(_targetMesh);
            AssetDatabase.Refresh();

            _dirtyMeshes.Remove(_targetMesh);
            _saveState = SaveState.Saved;
            Repaint();
            Debug.Log($"[SmoothNormal] 已保存 Mesh 资源：{path}");
        }

        /// <summary>
        /// 复制当前网格为独立的 .asset 并替换到对象上。
        /// 这是不可写网格（FBX 子资产 / 内置资源）唯一能真正保存的路径。
        /// </summary>
        private void DuplicateMeshToAsset()
        {
            if (!_targetMesh) return;

            // 若原网格在 Assets 下，默认存到它旁边，省得用户到处找。
            string dir = "Assets";
            string srcPath = AssetDatabase.GetAssetPath(_targetMesh);
            if (!string.IsNullOrEmpty(srcPath) && srcPath.StartsWith("Assets/"))
                dir = Path.GetDirectoryName(srcPath)?.Replace('\\', '/') ?? "Assets";

            string savePath = EditorUtility.SaveFilePanelInProject(
                "另存为独立 Mesh",
                $"{_targetMesh.name}_SmoothNormals",
                "asset",
                "新网格会自动替换到当前对象上。",
                dir);
            if (string.IsNullOrEmpty(savePath)) return;

            var copy = Instantiate(_targetMesh);
            copy.name = Path.GetFileNameWithoutExtension(savePath);
            AssetDatabase.CreateAsset(copy, savePath);
            AssetDatabase.SaveAssets();

            // 记录【组件】的 Undo —— 这个是真的有效，
            // 不像 Undo.RecordObject 对网格顶点数据那样形同虚设。
            if (_meshFilter)
            {
                Undo.RecordObject(_meshFilter, "Assign Duplicated Mesh");
                _meshFilter.sharedMesh = copy;
                EditorUtility.SetDirty(_meshFilter);
            }
            else if (_skinnedMeshRenderer)
            {
                Undo.RecordObject(_skinnedMeshRenderer, "Assign Duplicated Mesh");
                _skinnedMeshRenderer.sharedMesh = copy;
                EditorUtility.SetDirty(_skinnedMeshRenderer);
            }

            _snapshot = null;
            RefreshTargetMesh();
            _saveState = SaveState.Saved;
            Repaint();
            Debug.Log($"[SmoothNormal] 已复制为独立网格并替换到对象上：{savePath}");
        }

        /// <summary>标记 Mesh 已被修改，需要保存。</summary>
        private void MarkDirty()
        {
            if (_targetMesh) _dirtyMeshes.Add(_targetMesh);
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
            var iconRect = GUILayoutUtility.GetRect(36, 36, GUILayout.Width(36));
            DrawHexIcon(iconRect, ColorAccent);

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

            DrawBigStatusCard(
                "TEXCOORD 通道",
                "平滑法线 → 选定通道的 xyz（对象空间）",
                uvOverall,
                new[]
                {
                    ("TEXCOORD0", ShortState(_uvStates[0])),
                    ("TEXCOORD1", ShortState(_uvStates[1])),
                    ("TEXCOORD2", ShortState(_uvStates[2])),
                    ("TEXCOORD3", ShortState(_uvStates[3])),
                },
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
            var badgeStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 9,
                fontStyle = FontStyle.Bold,
                normal = { textColor = badgeColor },
                alignment = TextAnchor.MiddleRight,
            };
            GUILayout.Label(badgeText, badgeStyle, GUILayout.Width(104));
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();

            // Desc
            var descStyle = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.6f, 0.65f, 0.72f) } };
            GUILayout.Label(desc, descStyle);
            GUILayout.Space(4);

            // Sub-items grid
            EditorGUILayout.BeginHorizontal();
            foreach (var (label, note) in items)
            {
                DrawChannelChip(label, note, active, accentColor);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(10);
            EditorGUILayout.EndVertical();
        }

        private void DrawChannelChip(string label, string note, bool active, Color accentColor)
        {
            var chipBg = active ? new Color(accentColor.r * 0.2f, accentColor.g * 0.2f, accentColor.b * 0.2f, 0.8f)
                                : new Color(0.12f, 0.13f, 0.16f);
            var chipStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(6, 6, 4, 4),
                margin = new RectOffset(2, 2, 0, 0),
                normal = { background = MakeTex(2, 2, chipBg) }
            };

            EditorGUILayout.BeginVertical(chipStyle, GUILayout.Width(80));
            var lStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 9,
                normal = { textColor = active ? accentColor : new Color(0.5f, 0.5f, 0.6f) },
                alignment = TextAnchor.MiddleCenter,
            };
            var nStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 8,
                normal = { textColor = new Color(0.5f, 0.55f, 0.62f) },
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
            };
            GUILayout.Label(label, lStyle);
            GUILayout.Label(note, nStyle);
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
        #endregion
        
        #region UI 目标对象
        private GameObject _targetObject;
        private Mesh _targetMesh;
        private MeshFilter _meshFilter;
        private SkinnedMeshRenderer _skinnedMeshRenderer;
        
        private void DrawTargetSection()
        {
            DrawSectionHeader("目标对象", "◉");
            EditorGUILayout.BeginVertical(_dataCardStyle);

            EditorGUI.BeginChangeCheck();
            var newObj = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent("GameObject", "含有 MeshFilter 或 SkinnedMeshRenderer 的对象"),
                _targetObject, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck() && newObj != _targetObject)
            {
                _targetObject = newObj;
                if (_targetObject)
                {
                    _meshFilter = _targetObject.GetComponent<MeshFilter>();
                    _skinnedMeshRenderer = _targetObject.GetComponent<SkinnedMeshRenderer>();
                    RefreshTargetMesh();
                }
                else
                {
                    _meshFilter = null;
                    _skinnedMeshRenderer = null;
                    _targetMesh = null;
                    RefreshDataStatus();
                }
            }

            if (_targetObject)
            {
                EditorGUILayout.BeginHorizontal();
                string rendererType = _meshFilter ? "MeshFilter" :
                                      _skinnedMeshRenderer ? "SkinnedMeshRenderer" : "—";
                DrawTag(rendererType, ColorAccent);
                if (_targetMesh) DrawTag(_targetMesh.name, ColorCard * 1.4f);
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.HelpBox("请选择场景中含有网格的 GameObject", MessageType.Info);
            }

            EditorGUILayout.EndVertical();
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

        private void RefreshDataStatus()
        {
            if (!_targetMesh)
            {
                _hasVcr = _hasVcg = _hasVcb = _hasVca = false;
                _vertexColorState = ChannelState.Empty;
                _tangentState     = ChannelState.Empty;
                for (int i = 0; i < 4; i++) _uvStates[i] = ChannelState.Empty;
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

            for (int ch = 0; ch < 4; ch++)
                _uvStates[ch] = DetectUVChannelState(ch);
        }
        
        /// <summary>
        /// 刷新 目标Mesh数据。
        /// </summary>
        private void RefreshTargetMesh()
        {
            if (_meshFilter)
                _targetMesh = _meshFilter.sharedMesh;
            else if (_skinnedMeshRenderer)
                _targetMesh = _skinnedMeshRenderer.sharedMesh;
            else
                _targetMesh = null;

            // 保存状态跟着网格走：切走再切回时，未保存的警告必须还在。
            // 无条件重置成 Clean 会静默丢掉警告，让用户以为改动已经落盘。
            _saveState = (_targetMesh && _dirtyMeshes.Contains(_targetMesh))
                ? SaveState.NeedSave
                : SaveState.Clean;

            // 快照只对抓取时的那个网格有效。
            if (_snapshot != null && _snapshot.Mesh != _targetMesh)
                _snapshot = null;

            if (_targetMesh)
            {
                var b = _targetMesh.bounds;
                _previewPivot = b.center;
                _previewZoom  = b.size.magnitude * 1.6f;
            }

            RefreshDataStatus();
        }
        #endregion
        
        #region UI Mesh信息列表
        private void DrawMeshInfoSection()
        {
            _foldoutMeshInfo = DrawFoldout(_foldoutMeshInfo, "网格信息", "▦");
            if (!_foldoutMeshInfo) return;

            EditorGUILayout.BeginVertical(_dataCardStyle);

            if (!_targetMesh)
            {
                GUILayout.Label("无网格数据", _subHeaderStyle);
            }
            else
            {
                DrawInfoRow("顶点数", _targetMesh.vertexCount.ToString("N0"));
                DrawInfoRow("三角面数", (_targetMesh.triangles.Length / 3).ToString("N0"));
                DrawInfoRow("SubMesh 数", _targetMesh.subMeshCount.ToString());
                DrawInfoRow("含法线", _targetMesh.normals?.Length > 0 ? "✓" : "✗");
                DrawInfoRow("含切线", _targetMesh.tangents?.Length > 0 ? "✓" : "✗");
                DrawInfoRow("含顶点色", _targetMesh.colors32?.Length > 0 ? "✓" : "✗");

                var uvList = new List<Vector4>();
                for (int ch = 0; ch < 4; ch++)
                {
                    _targetMesh.GetUVs(ch, uvList);
                    DrawInfoRow($"TEXCOORD{ch}", uvList.Count > 0 ? $"✓ ({uvList.Count}个)" : "—");
                }
            }

            EditorGUILayout.EndVertical();
        }
        #endregion
        
        #region UI 存储方式
        public enum StorageMode { VertexColor, TangentSpace, UV }

        public enum VertexColorChannel
        {
            Rg,   // R=法线X  G=法线Y
            Gb,   // G=法线X  B=法线Y
            Ba,   // B=法线X  A=法线Y
        }
        
        private StorageMode _storageMode = StorageMode.VertexColor;
        // Vertex color channel pair
        private VertexColorChannel _vcChannel = VertexColorChannel.Ba;

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
            DrawVcChannelTab("RG", VertexColorChannel.Rg);
            DrawVcChannelTab("GB", VertexColorChannel.Gb);
            DrawVcChannelTab("BA", VertexColorChannel.Ba);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(8);

            // ── RGBA 各通道状态 ──────────────────────────────────────
            GUILayout.Label("顶点色通道数据状态", new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.55f, 0.6f, 0.68f) } });
            GUILayout.Space(2);

            // 当前选中的通道对写入的是哪两个通道
            bool rIsWrite = _vcChannel == VertexColorChannel.Rg;
            bool gIsWrite = _vcChannel == VertexColorChannel.Rg || _vcChannel == VertexColorChannel.Gb;
            bool bIsWrite = _vcChannel == VertexColorChannel.Gb || _vcChannel == VertexColorChannel.Ba;
            bool aIsWrite = _vcChannel == VertexColorChannel.Ba;

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
            for (int i = 0; i < 4; i++)
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

            bool canGenerate = _targetMesh;
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

            if (GUILayout.Button($"▶  生成平滑法线  →  {modeLabel}", btnStyle))
                TryGenerateSmoothNormals();

            GUI.enabled = true;
            EditorGUILayout.EndVertical();
        }
        #endregion
        
        #region UI 预览描边渲染
        // ─────────────────────────────────────────────────────────────
        //  Inline Preview
        // ─────────────────────────────────────────────────────────────
        private PreviewRenderUtility _previewUtil;
        private Material _previewBaseMat;
        private Material _previewOutlineMat;
        private Material _normalLineMat;

        // camera orbit
        private Vector2 _previewOrbit  = new Vector2(30f, -20f);
        private float   _previewZoom   = 3f;
        private Vector3 _previewPivot  = Vector3.zero;
        private bool    _previewDragging;
        private Vector2 _previewLastMouse;

        // outline params
        private float _outlineWidth  = 0.015f;   // 与 Outline.shader 的默认值一致
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

        private static readonly int PropSrcBlend = Shader.PropertyToID("_SrcBlend");
        private static readonly int PropDstBlend = Shader.PropertyToID("_DstBlend");
        private static readonly int PropCull     = Shader.PropertyToID("_Cull");
        private static readonly int PropZWrite   = Shader.PropertyToID("_ZWrite");

        private static readonly int PropGlossiness  = Shader.PropertyToID("_Glossiness");
        private static readonly int PropMetallic     = Shader.PropertyToID("_Metallic");
        private static readonly int PropOutlineColor = Shader.PropertyToID("_OutlineColor");
        private static readonly int PropOutlineWidth = Shader.PropertyToID("_OutlineWidth");
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
            if (!_targetMesh)
            {
                EditorGUI.DrawRect(r, new Color(0.11f, 0.12f, 0.15f));
                var s = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = new Color(0.4f, 0.45f, 0.5f) },
                };
                GUI.Label(r, "请先选择 Mesh", s);
                return;
            }

            HandlePreviewCameraControl(r);

            _previewUtil.BeginPreview(r, GUIStyle.none);
            _previewUtil.camera.backgroundColor = _previewBgColor;

            var camPos = _previewPivot + Quaternion.Euler(_previewOrbit.y, _previewOrbit.x, 0) * new Vector3(0, 0, _previewZoom);
            _previewUtil.camera.transform.position = camPos;
            _previewUtil.camera.transform.LookAt(_previewPivot);

            if (_showBase && _previewBaseMat)
            {
                UpdatePreviewBaseMat();
                _previewUtil.DrawMesh(_targetMesh, Matrix4x4.identity, _previewBaseMat, 0);
            }
            if (_showOutline && _previewOutlineMat)
            {
                UpdatePreviewOutlineMat();
                _previewUtil.DrawMesh(_targetMesh, Matrix4x4.identity, _previewOutlineMat, 0);
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
            var bs = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = ColorAccent },
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
            };
            GUI.Label(new Rect(badgeRect.x + 6, badgeRect.y, badgeRect.width, badgeRect.height), $"● {modeLabel}", bs);

            // Overlay: smooth normals
            if (_showNormals)
                DrawNormalsOverlay(r, GetDecodedSmoothNormals(), _normalColor);

            // Overlay: original normals
            if (_showOriginalNormals)
                DrawNormalsOverlay(r, _targetMesh.normals, _originalNormalColor);

            // Overlay: hint
            var hintRect = new Rect(r.x, r.yMax - 22, r.width, 22);
            EditorGUI.DrawRect(hintRect, new Color(0.05f, 0.06f, 0.08f, 0.72f));
            var hs = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.5f, 0.55f, 0.62f) },
                alignment = TextAnchor.MiddleCenter,
            };
            GUI.Label(hintRect, "左键旋转  |  滚轮缩放  |  中键平移", hs);
        }

        /// <summary>
        /// 用 GL 在预览视口上叠加绘制法线方向线段。
        /// normals 为对象空间法线数组，与 mesh.vertices 一一对应。
        /// </summary>
        private void DrawNormalsOverlay(Rect r, Vector3[] normals, Color color)
        {
            if (_previewUtil?.camera == null || _targetMesh == null) return;
            if (Event.current.type != EventType.Repaint) return;
            if (normals == null || normals.Length != _targetMesh.vertexCount) return;

            var verts = _targetMesh.vertices;
            var cam   = _previewUtil.camera;
            int step  = Mathf.Max(1, verts.Length / 512);

            // 懒初始化 GL 画线材质
            if (!_normalLineMat)
            {
                var shader = Shader.Find("Hidden/Internal-Colored");
                _normalLineMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                _normalLineMat.SetInt(PropSrcBlend, (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _normalLineMat.SetInt(PropDstBlend, (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                _normalLineMat.SetInt(PropCull,     (int)UnityEngine.Rendering.CullMode.Off);
                _normalLineMat.SetInt(PropZWrite,   0);
            }

            _normalLineMat.SetPass(0);

            GL.PushMatrix();
            GL.LoadPixelMatrix(0, position.width, position.height, 0);
            GL.Begin(GL.LINES);
            GL.Color(color);

            for (int i = 0; i < verts.Length; i += step)
            {
                Vector3 vpO = cam.WorldToViewportPoint(verts[i]);
                Vector3 vpE = cam.WorldToViewportPoint(verts[i] + normals[i] * _normalLength);

                if (vpO.z <= 0 || vpE.z <= 0) continue;

                GL.Vertex3(r.x + vpO.x * r.width, r.y + (1f - vpO.y) * r.height, 0);
                GL.Vertex3(r.x + vpE.x * r.width, r.y + (1f - vpE.y) * r.height, 0);
            }

            GL.End();
            GL.PopMatrix();
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
                            case VertexColorChannel.Rg: x = c.r; y = c.g; break;
                            case VertexColorChannel.Gb: x = c.g; y = c.b; break;
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
                _previewOrbit.y += delta.y * 0.5f;
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
                if (_targetMesh) { _previewPivot = _targetMesh.bounds.center; _previewZoom = _targetMesh.bounds.size.magnitude * 1.6f; }
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
            if (_normalLineMat)     DestroyImmediate(_normalLineMat);
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
            var litShader = FindLitShader();
            _previewBaseMat = new Material(litShader);
            ApplyBaseMatParams();

            var outlineShader = Shader.Find("OutlineSmoothNormalsGenerator/OutlinePreview") ?? Shader.Find("Unlit/Color");
            _previewOutlineMat = new Material(outlineShader);
            if (_previewOutlineMat.HasProperty(PropOutlineColor)) _previewOutlineMat.SetColor(PropOutlineColor, _outlineColor);
            if (_previewOutlineMat.HasProperty(PropOutlineWidth)) _previewOutlineMat.SetFloat(PropOutlineWidth, _outlineWidth);
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
            if (_storageMode == StorageMode.UV && IsRiskyUVChannel(_uvChannel) &&
                !EditorUtility.DisplayDialog(
                    "覆盖主贴图 UV？",
                    $"TEXCOORD0 是「{_targetMesh.name}」的主贴图 UV（mesh.uv），且当前已有数据。\n\n" +
                    "写入平滑法线会覆盖它，该网格的贴图映射将丢失，且影响所有使用此网格的对象。\n\n" +
                    "建议改用 TEXCOORD1。仍要继续吗？",
                    "仍要覆盖", "取消"))
                return;

            GenerateSmoothNormals();
        }

        private void GenerateSmoothNormals()
        {
            if (!_targetMesh) return;

            var smoothNormals = OutlineSmoothNormalsCalculator.Calculate(_targetMesh, _mergeTolerance);
            if (smoothNormals == null) return;   // 具体原因已由 Calculate 打印

            // 计算成功、真要动数据之前才抓快照。
            CaptureSnapshot(_targetMesh);

            switch (_storageMode)
            {
                case StorageMode.VertexColor:
                    StorageWriter.WriteToVertexColor(_targetMesh, smoothNormals, _vcChannel);
                    break;
                case StorageMode.TangentSpace:
                    StorageWriter.WriteToTangent(_targetMesh, smoothNormals);
                    break;
                case StorageMode.UV:
                    StorageWriter.WriteToUV(_targetMesh, smoothNormals, _uvChannel);
                    break;
            }

            EditorUtility.SetDirty(_targetMesh);
            RefreshDataStatus();
            MarkDirty();
            Repaint();


            Debug.Log($"[SmoothNormal] 生成完成 → 模式: {_storageMode}, Mesh: {_targetMesh.name}, " +
                      $"顶点数: {_targetMesh.vertexCount}, 合并容差: {_mergeTolerance:G}");
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

            _stylesInitialized = true;
        }

        private void DrawSectionHeader(string titleName, string icon)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            var s = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                normal = { textColor = ColorAccent },
            };
            GUILayout.Label($"{icon}  {titleName}", s);
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
            var dotStyle = new GUIStyle(GUI.skin.label) { normal = { textColor = dotColor }, fontSize = 14 };
            GUILayout.Label("●", dotStyle, GUILayout.Width(18));
            var lStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 10,
                normal = { textColor = Color.white }
            };
            GUILayout.Label(label, lStyle, GUILayout.Width(100));
            var dStyle = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = dotColor } };
            GUILayout.Label(desc, dStyle);
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

        private GUIStyle GetInnerCardStyle() => new GUIStyle(GUI.skin.box)
        {
            padding = new RectOffset(8, 8, 6, 6),
        };

        private static Dictionary<Color, Texture2D> _texCache = new Dictionary<Color, Texture2D>();
        private static Texture2D MakeTex(int w, int h, Color col)
        {
            if (_texCache.TryGetValue(col, out var cached) && cached) return cached;
            var tex = new Texture2D(w, h);
            var pixels = new Color[w * h];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = col;
            tex.SetPixels(pixels);
            tex.Apply();
            _texCache[col] = tex;
            return tex;
        }
        #endregion
    }
}
