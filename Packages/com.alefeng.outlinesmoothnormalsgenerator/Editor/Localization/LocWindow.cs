namespace OutlineSmoothNormalsGenerator
{
    /// <summary>
    /// 「平滑法线生成器」窗口（含「导入自动烘焙」页签）的界面文案。
    ///
    /// 【术语以三份 README 为准】
    /// README.md / README_EN.md / README_JA.md 的小节标题逐条对齐，本身就是一份现成的
    /// 三语术语表。界面上的名字必须和文档里的一致 —— 用户在文档里读到「保存方式」，
    /// 就得能在界面上找到「保存方式」。不要另起译法：
    ///   存储方式 = Storage Mode = 保存方式        存储空间 = Storage Space = 保存空間
    ///   切线空间 = Tangent Space = 接線空間       切线通道 = Tangent Channel = 接線チャンネル
    ///   平滑法线 = smooth normals = スムース法線（不是スムーズ）
    ///   烘焙 = bake = ベイク                      描边 = outline = アウトライン
    ///
    /// 【英文术语一律并列保留】
    /// 中文与日文的按钮标题保留英文原名（如「顶点色\nVertex Color」），英文档去掉重复
    /// 的那一行即可。纯英文标识（TEXCOORD0、SkinnedMeshRenderer、mesh.uv2、Read/Write）
    /// 三语一致，不翻译。
    /// </summary>
    internal static class LocWindow
    {
        // ═══════════════════════════════════════════════════════════════
        //  窗口标题与页签栏
        // ═══════════════════════════════════════════════════════════════
        /// <summary>Unity 窗口标签页上的文字。标签页很窄，三语都取短名。</summary>
        public static string WindowTitle => OutlineLocale.Pick(
            "平滑法线生成器", "Smooth Normals", "スムース法線");

        // 日文页签名取 README_JA 里【指代页签】的写法：
        //   页签 = スムース法線生成 / インポート自動ベイク（L107、L150、L272、L280）
        //   章题 = インポート時の自動ベイク（L270，另有锚点引用）
        // README_JA 全文没有「ジェネレーター」。产品全名由 HeaderSubtitle 的英文承担。
        // 英文用单数 Smooth Normal Generator —— 与 [MenuItem("Tools/Smooth Normal Generator")]
        // 及 README_EN:101/107/111/150 一致。复数的包全名由 HeaderTitle 承担。
        public static string TabGenerator => OutlineLocale.Pick(
            "平滑法线生成器", "Smooth Normal Generator", "スムース法線生成");

        public static string TabAutoBake => OutlineLocale.Pick(
            "导入自动烘焙", "Auto-Bake on Import", "インポート自動ベイク");

        // ═══════════════════════════════════════════════════════════════
        //  两处页签的标题区
        // ═══════════════════════════════════════════════════════════════
        // 英文档的主标题已经是包的英文全名，副标题若照抄就成了自我重复，
        // 因此英文副标题改写成一句功能说明。
        public static string HeaderTitle => OutlineLocale.Pick(
            "平滑法线生成器", "Outline Smooth Normals Generator", "スムース法線生成");

        public static string HeaderSubtitle => OutlineLocale.Pick(
            "Outline Smooth Normals Generator  •  Unity 2022.3+",
            "Smooth normals for backface outlines  •  Unity 2022.3+",
            "Outline Smooth Normals Generator  •  Unity 2022.3+");

        public static string AutoBakeHeaderTitle => OutlineLocale.Pick(
            "导入自动烘焙", "Auto-Bake on Import", "インポート自動ベイク");

        // 原文写的是「命中后缀的模型」，而 1.7.0 起命中条件已是后缀 + 文件夹两项，
        // 这里顺手改回与「命中规则」一致的说法。
        public static string AutoBakeHeaderSubtitle => OutlineLocale.Pick(
            "Auto-Bake On Import  •  命中规则的模型导入即烘焙平滑法线",
            "Auto-Bake On Import  •  Matching models get smooth normals baked on (re)import",
            "Auto-Bake On Import  •  判定ルールに合致したモデルはインポート時に" +
            "スムース法線をベイク");

        // ═══════════════════════════════════════════════════════════════
        //  通用按钮
        // ═══════════════════════════════════════════════════════════════
        public static string BtnOk => OutlineLocale.Pick("知道了", "Got it", "OK");

        public static string BtnCancel => OutlineLocale.Pick("取消", "Cancel", "キャンセル");

        // ═══════════════════════════════════════════════════════════════
        //  左栏 · 目标对象
        // ═══════════════════════════════════════════════════════════════
        public static string SectionTarget => OutlineLocale.Pick("目标对象", "Target", "対象");

        public static string TargetField => OutlineLocale.Pick("目标", "Target", "対象");

        public static string TargetFieldTooltip => OutlineLocale.Pick(
            "可以是：场景对象（含 MeshFilter / SkinnedMeshRenderer）、" +
            "模型文件（.fbx 等）、预制体，或直接选中一个 Mesh 资产",
            "Accepts a scene object (with MeshFilter / SkinnedMeshRenderer), " +
            "a model file (.fbx etc.), a prefab, or a Mesh asset directly.",
            "指定できるもの：シーンオブジェクト（MeshFilter / SkinnedMeshRenderer 付き）、" +
            "モデルファイル（.fbx など）、プレハブ、または Mesh アセット。");

        public static string MeshListCount(int selected, int total) => OutlineLocale.Fmt(
            "网格列表（已勾选 {0} / {1}）",
            "Mesh list ({0} / {1} selected)",
            "メッシュ一覧（{0} / {1} 選択中）", selected, total);

        public static string BtnSelectAll => OutlineLocale.Pick("全选", "Select All", "すべて選択");

        public static string BtnClearSelection => OutlineLocale.Pick("清空", "Clear", "クリア");

        public static string ChecklistHintSingle => OutlineLocale.Pick(
            "勾选网格纳入批量编辑，描边预览会显示所有勾选项；单击网格名把它设为" +
            "焦点 —— 右侧通道状态与网格信息显示焦点网格（列表中高亮的一行）。",
            "Check a mesh to include it in batch editing; the outline preview shows every " +
            "checked mesh. Click a mesh name to make it the focus — the channel status and " +
            "mesh info on the right always describe the focused mesh (the highlighted row).",
            "チェックしたメッシュがバッチ編集の対象になり、アウトラインプレビューには" +
            "チェックしたすべてが表示されます。メッシュ名をクリックするとフォーカスに" +
            "なります —— 右のチャンネル状態とメッシュ情報はフォーカスのメッシュ" +
            "（ハイライト行）のみを表示します。");

        public static string ChecklistHintMany(int selected) => OutlineLocale.Fmt(
            "已勾选 {0} 个网格：描边预览同时显示全部勾选项，「生成」「保存」" +
            "也作用于全部；右侧通道状态与网格信息只显示焦点网格（高亮行）。",
            "{0} meshes checked: the outline preview shows all of them, and Generate / Save " +
            "act on all of them. The channel status and mesh info on the right show only the " +
            "focused mesh (the highlighted row).",
            "{0} 個のメッシュをチェック中：アウトラインプレビューにはすべてが表示され、" +
            "「生成」「保存」もすべてに作用します。右のチャンネル状態とメッシュ情報は" +
            "フォーカスのメッシュ（ハイライト行）のみを表示します。", selected);

        public static string TargetNoMesh => OutlineLocale.Pick(
            "该对象不含可处理的 Mesh。请选择含 MeshFilter / " +
            "SkinnedMeshRenderer 的对象、模型 / 预制体，或一个 Mesh 资产。",
            "This object contains no usable Mesh. Pick an object with a MeshFilter / " +
            "SkinnedMeshRenderer, a model / prefab, or a Mesh asset.",
            "このオブジェクトには処理できる Mesh がありません。MeshFilter / " +
            "SkinnedMeshRenderer を持つオブジェクト、モデル / プレハブ、または " +
            "Mesh アセットを選んでください。");

        public static string TargetNone => OutlineLocale.Pick(
            "请选择一个对象：场景中的网格对象，或 Project 中的 Mesh / 模型 / 预制体资产。",
            "Select an object: a mesh object in the scene, or a Mesh / model / prefab asset " +
            "in the Project window.",
            "オブジェクトを選択してください：シーン内のメッシュオブジェクト、または " +
            "Project の Mesh / モデル / プレハブ アセット。");

        // 来源类型标签。MeshFilter / SkinnedMeshRenderer 是类型名，三语一致，不在此列。
        public static string SourceMeshAsset => OutlineLocale.Pick(
            "Mesh 资产", "Mesh asset", "Mesh アセット");

        public static string SourceModelOrPrefab => OutlineLocale.Pick(
            "模型 / 预制体资产", "Model / Prefab asset", "モデル / プレハブ アセット");

        public static string SourceSceneObject => OutlineLocale.Pick(
            "场景对象", "Scene object", "シーンオブジェクト");

        // ═══════════════════════════════════════════════════════════════
        //  左栏 · 网格信息
        // ═══════════════════════════════════════════════════════════════
        public static string SectionMeshInfo => OutlineLocale.Pick(
            "网格信息", "Mesh Info", "メッシュ情報");

        public static string MeshInfoNone => OutlineLocale.Pick(
            "无网格数据", "No mesh data", "メッシュデータなし");

        public static string MeshInfoVertices  => OutlineLocale.Pick("顶点数", "Vertices", "頂点数");
        public static string MeshInfoTriangles => OutlineLocale.Pick("三角面数", "Triangles", "三角形数");
        public static string MeshInfoSubMesh   => OutlineLocale.Pick("SubMesh 数", "SubMeshes", "サブメッシュ数");
        public static string MeshInfoNormals   => OutlineLocale.Pick("含法线", "Has Normals", "法線あり");
        public static string MeshInfoTangents  => OutlineLocale.Pick("含切线", "Has Tangents", "接線あり");
        public static string MeshInfoColors    => OutlineLocale.Pick("含顶点色", "Has Vertex Colors", "頂点カラーあり");

        /// <summary>
        /// TEXCOORD 行的取值。括号里是该通道的【元素个数】（即 GetUVs 取回的条目数，
        /// 有数据时恒等于顶点数），不是分量数 —— 沿用原有行为，英日译文照此写清单位。
        /// </summary>
        public static string MeshInfoUvCount(int count) => OutlineLocale.Fmt(
            "✓ ({0}个)", "✓ ({0} entries)", "✓（{0} 件）", count);

        // ═══════════════════════════════════════════════════════════════
        //  左栏底部 · 保存
        // ═══════════════════════════════════════════════════════════════
        public static string BtnRevert => OutlineLocale.Pick(
            "↺  还原本次修改", "↺  Revert this change", "↺  今回の変更を元に戻す");

        public static string BtnRevertTooltip => OutlineLocale.Pick(
            "把所有本次生成 / 清除过的网格退回到修改之前的状态。\n\n" +
            "这是本工具自己的会话快照，与 Unity 的 Undo 无关 —— Undo 不跟踪网格顶点数据。",
            "Roll every mesh generated / cleared in this session back to how it was before.\n\n" +
            "This is the tool's own session snapshot, unrelated to Unity's Undo — " +
            "Undo does not track mesh vertex data.",
            "このセッションで生成 / クリアしたすべてのメッシュを、変更前の状態に戻します。\n\n" +
            "これは本ツール独自のセッションスナップショットで、Unity の Undo とは無関係です " +
            "—— Undo はメッシュの頂点データを追跡しません。");

        public static string BtnSave => OutlineLocale.Pick("保存", "Save", "保存");

        public static string SaveTipNoSelection => OutlineLocale.Pick(
            "请在网格列表中勾选要保存的网格。",
            "Check the meshes you want to save in the mesh list.",
            "保存したいメッシュをメッシュ一覧でチェックしてください。");

        public static string SaveTipDirty(int count) => OutlineLocale.Fmt(
            "保存 {0} 个已修改的网格。",
            "Save {0} modified mesh(es).",
            "変更された {0} 個のメッシュを保存します。", count);

        public static string SaveTipBlockedExtra(int count) => OutlineLocale.Fmt(
            "另有 {0} 个为只读（模型 / FBX 子资产 / 内置），需逐个「另存为」。",
            "Another {0} are read-only (model / FBX sub-asset / built-in) and must be " +
            "duplicated one by one.",
            "他に {0} 個が読み取り専用（モデル / FBX サブアセット / 組み込み）で、" +
            "1 つずつ「独立した Mesh として保存」する必要があります。", count);

        public static string SaveTipBlockedOnly(int count) => OutlineLocale.Fmt(
            "{0} 个已修改的网格不可直接保存（模型 / FBX / 内置只读），\n" +
            "请逐个选中后用右侧「⧉ 另存为独立 Mesh…」。",
            "{0} modified mesh(es) cannot be saved in place (model / FBX / built-in are " +
            "read-only).\nSelect them one at a time and use \"⧉ Duplicate to standalone Mesh…\".",
            "{0} 個の変更済みメッシュはそのまま保存できません（モデル / FBX / 組み込みは" +
            "読み取り専用）。\n1 つずつ選択して右の「⧉ 独立した Mesh として保存…」を" +
            "使ってください。", count);

        public static string SaveTipSaved => OutlineLocale.Pick(
            "已保存，暂无新的修改。", "Saved. No new changes.", "保存済み。新しい変更はありません。");

        public static string SaveTipNothing => OutlineLocale.Pick(
            "当前没有需要保存的修改。", "Nothing to save right now.", "保存が必要な変更はありません。");

        public static string BtnDuplicate => OutlineLocale.Pick(
            "⧉  另存为独立 Mesh…", "⧉  Duplicate to standalone Mesh…", "⧉  独立した Mesh として保存…");

        public static string BtnDuplicateTooltip => OutlineLocale.Pick(
            "把所有勾选的网格复制成独立可写的 .asset。\n\n" +
            "勾选 1 个：弹对话框让你命名保存；\n" +
            "勾选多个：选一个目标文件夹，按各自网格名批量生成。\n\n" +
            "场景对象的网格会自动回填到对应组件；资产（Mesh / 模型 / 预制体）" +
            "只生成 .asset，请自行引用。",
            "Copy every checked mesh into a standalone writable .asset.\n\n" +
            "One checked: a dialog lets you name and save it.\n" +
            "Several checked: pick a target folder and they are generated in batch, " +
            "each named after its mesh.\n\n" +
            "Meshes from scene objects are assigned back to their components automatically; " +
            "for assets (Mesh / model / prefab) only the .asset is created — reference it yourself.",
            "チェックしたすべてのメッシュを、書き込み可能な独立した .asset に複製します。\n\n" +
            "1 つの場合：ダイアログで名前を付けて保存します。\n" +
            "複数の場合：保存先フォルダーを選ぶと、各メッシュ名で一括生成します。\n\n" +
            // 「回填」= 再割り当て（README_JA L128 / L379）。
            // 「差し戻す」は突き返す・却下する意で、新しく複製したメッシュを component に
            // 設定する動作を指せない。
            "シーンオブジェクトのメッシュは対応するコンポーネントへ自動的に再割り当てされます。" +
            "アセット（Mesh / モデル / プレハブ）は .asset を作るだけなので、参照はご自身で" +
            "設定してください。");

        public static string LogSaved(int count, string paths) => OutlineLocale.Fmt(
            "已保存 {0} 个网格：\n{1}",
            "Saved {0} mesh(es):\n{1}",
            "{0} 個のメッシュを保存しました：\n{1}", count, paths);

        public static string DialogPartialSaveTitle => OutlineLocale.Pick(
            "部分网格无法直接保存",
            "Some meshes cannot be saved in place",
            "一部のメッシュはそのまま保存できません");

        public static string DialogPartialSaveBody(int count, string names) => OutlineLocale.Fmt(
            "以下 {0} 个已修改的网格不可直接保存" +
            "（模型 / FBX 子资产 / 内置资源，均为只读）：\n\n{1}\n\n" +
            "请在列表中逐个选中它们，再用右侧「⧉ 另存为独立 Mesh…」复制成可写的 .asset。",
            "The following {0} modified mesh(es) cannot be saved in place " +
            "(model / FBX sub-asset / built-in resource — all read-only):\n\n{1}\n\n" +
            "Select them one at a time in the list, then use " +
            "\"⧉ Duplicate to standalone Mesh…\" to copy them into writable .assets.",
            "次の {0} 個の変更済みメッシュはそのまま保存できません" +
            "（モデル / FBX サブアセット / 組み込みリソースはいずれも読み取り専用）：\n\n{1}\n\n" +
            "一覧で 1 つずつ選択し、右の「⧉ 独立した Mesh として保存…」で書き込み可能な " +
            ".asset に複製してください。", count, names);

        public static string SavePanelTitle => OutlineLocale.Pick(
            "另存为独立 Mesh", "Duplicate to standalone Mesh", "独立した Mesh として保存");

        public static string SavePanelMessage => OutlineLocale.Pick(
            "新网格会自动替换到当前对象上（若来自场景对象）。",
            "The new mesh is assigned back to the current object automatically " +
            "(when it came from a scene object).",
            "新しいメッシュは（シーンオブジェクト由来の場合）現在のオブジェクトへ" +
            "自動的に差し替えられます。");

        public static string FolderPanelTitle(int count) => OutlineLocale.Fmt(
            "另存为独立 Mesh —— 为 {0} 个网格选择目标文件夹",
            "Duplicate to standalone Mesh — pick a target folder for {0} meshes",
            "独立した Mesh として保存 —— {0} 個のメッシュの保存先フォルダーを選択", count);

        public static string DialogInvalidPathTitle => OutlineLocale.Pick(
            "路径无效", "Invalid path", "無効なパス");

        public static string DialogInvalidPathBody => OutlineLocale.Pick(
            "请选择本工程 Assets 目录下的文件夹。",
            "Pick a folder inside this project's Assets directory.",
            "このプロジェクトの Assets ディレクトリ内のフォルダーを選んでください。");

        public static string LogDuplicatedReassigned(string path) => OutlineLocale.Fmt(
            "已复制为独立网格并替换到对象上：{0}",
            "Duplicated to a standalone mesh and assigned to the object: {0}",
            "独立したメッシュに複製し、オブジェクトへ差し替えました：{0}", path);

        public static string LogDuplicated(string path) => OutlineLocale.Fmt(
            "已复制为独立网格：{0}（当前目标是资产，未回填到组件，请自行引用）",
            "Duplicated to a standalone mesh: {0} (the target is an asset, so nothing was " +
            "assigned back — reference it yourself)",
            "独立したメッシュに複製しました：{0}（対象がアセットのため、コンポーネントへの" +
            "再割り当ては行っていません。参照はご自身で設定してください）", path);

        public static string LogDuplicatedMany(int total, string folder, int reassigned)
            => OutlineLocale.Fmt(
                "已把 {0} 个网格另存为独立资产到 {1}（其中 {2} 个已回填到场景组件）。",
                "Duplicated {0} mesh(es) into standalone assets under {1} " +
                "({2} of them assigned back to scene components).",
                "{0} 個のメッシュを {1} 配下の独立アセットとして保存しました" +
                "（うち {2} 個はシーンのコンポーネントへ再割り当て済み）。",
                total, folder, reassigned);

        // ═══════════════════════════════════════════════════════════════
        //  通道状态徽标
        // ═══════════════════════════════════════════════════════════════
        // 徽标前缀的 ● ▲ ○ ✕ 三语一致，只换文字 —— 颜色与符号承载的是同一套语义，
        // 换了符号等于换了配色约定。
        public static string StateLikelySmoothNormals => OutlineLocale.Pick(
            "● 可能是平滑法线", "● Likely smooth normals", "● スムース法線の可能性");

        // 英文必须逐字等于 README_EN.md:45 / 364 —— 那两处是「看到这串字就去重烘」的
        // 操作指引，字面对不上，用户就在界面里找不到它。README_EN 全文没有 legacy 一词。
        public static string StateLegacyFormat => OutlineLocale.Pick(
            "▲ 可能是旧版格式", "▲ Possibly an old format", "▲ 旧フォーマットの可能性");

        public static string StateHasData => OutlineLocale.Pick(
            "○ 有数据", "○ Has data", "○ データあり");

        public static string StateEmpty => OutlineLocale.Pick("✕ 空", "✕ Empty", "✕ 空");

        public static string ShortStateLikelySmoothNormals => OutlineLocale.Pick(
            "可能是法线", "Likely normals", "法線の可能性");

        public static string ShortStateLegacyFormat => OutlineLocale.Pick(
            "旧版格式", "Old format", "旧フォーマット");

        public static string ShortStateHasData => OutlineLocale.Pick(
            "有数据", "Has data", "データあり");

        public static string ShortStateEmpty => OutlineLocale.Pick("空", "Empty", "空");

        // ═══════════════════════════════════════════════════════════════
        //  左栏 · 存储方式
        // ═══════════════════════════════════════════════════════════════
        public static string SectionStorageMode => OutlineLocale.Pick(
            "存储方式", "Storage Mode", "保存方式");

        // 页签按钮：中文 / 日文保留英文术语并列，英文档去掉重复的那一行。
        public static string ModeTabVertexColor => OutlineLocale.Pick(
            "顶点色\nVertex Color", "Vertex Color", "頂点カラー\nVertex Color");

        public static string ModeTabTangentChannel => OutlineLocale.Pick(
            "切线通道\nTangent", "Tangent Channel", "接線チャンネル\nTangent");

        public static string ModeTabUV => OutlineLocale.Pick(
            "UV 通道\nUV Channel", "UV Channel", "UV チャンネル\nUV Channel");

        // 三种存储方式各自的取舍。三者存的都是完整三维方向，区别只在「占用哪块顶点数据、
        // 精度多少、跟什么冲突」—— 选型基本就是在回答「这个网格的哪块数据是空的」。
        public static string TooltipVertexColor => OutlineLocale.Pick(
            "把方向八面体编码后，存进选定的一对顶点色通道（RG / GB / BA）。\n\n" +
            "· 最省：只占 2 个 8-bit 通道，顶点带宽开销最小。\n" +
            "· 精度约 1°，远低于描边外扩能察觉的程度。\n" +
            "· 会覆盖选中的那两个通道 —— 若模型的顶点色另作他用（AO、遮罩、风力等）会冲突。\n" +
            "· 不动切线、不占 UV，法线贴图照常可用。",

            "Octahedral-encode the direction and store it in the selected pair of vertex " +
            "color channels (RG / GB / BA).\n\n" +
            "· Cheapest: two 8-bit channels, the smallest vertex bandwidth cost.\n" +
            "· About 1° of error — far below anything outline extrusion can reveal.\n" +
            "· Overwrites the selected pair — clashes when the model's vertex colors are " +
            "already used for something else (AO, masks, wind).\n" +
            "· Leaves the tangent and every UV alone, so normal maps keep working.",

            "方向を八面体エンコードして、選んだ頂点カラーチャンネルのペア（RG / GB / BA）" +
            "に格納します。\n\n" +
            "· 最小：8-bit チャンネル 2 つだけで、頂点帯域のコストを最も抑えられます。\n" +
            "· 精度は約 1°。アウトライン押し出しで知覚できる水準をはるかに下回ります。\n" +
            "· 選んだ 2 チャンネルを上書きします —— 頂点カラーを別用途（AO、マスク、風など）" +
            "に使っていると衝突します。\n" +
            "· 接線も UV も触らないので、ノーマルマップはそのまま使えます。");

        public static string TooltipTangentChannel => OutlineLocale.Pick(
            "把方向直接存进 tangent.xyz（w 恒为 1），不做任何压缩。\n\n" +
            "· 精度最高：三个完整 float，无编码误差。\n" +
            "· ⚠ 会覆盖网格的原始切线，采样法线贴图的 Shader（URP/Lit、Standard 等）" +
            "会因此拿到错误的 TBN，表现为法线贴图失效。仅在该网格不用法线贴图时选用。\n" +
            "· 恒为对象空间，且无需切线空间 —— Unity 会把 tangent.xyz 当方向一起蒙皮，" +
            "存进去的方向天然跟随骨骼动画。\n" +
            "· 若只是想避开顶点色，优先考虑 TEXCOORD 通道。",

            "Store the direction straight into tangent.xyz (w is always 1), with no " +
            "compression at all.\n\n" +
            "· Highest precision: three full floats, no encoding error.\n" +
            "· ⚠ Overwrites the mesh's original tangent, so shaders that sample normal maps " +
            "(URP/Lit, Standard, …) get a wrong TBN and the normal map breaks. Only pick this " +
            "when the mesh does not use normal maps.\n" +
            "· Always object space, and tangent space is not needed — Unity skins tangent.xyz " +
            "as a direction, so what you store follows the skeleton automatically.\n" +
            "· If you only want to keep vertex color free, prefer a TEXCOORD channel.",

            "方向をそのまま tangent.xyz に格納します（w は常に 1）。圧縮は一切行いません。\n\n" +
            "· 精度は最高：完全な float 3 つで、エンコード誤差がありません。\n" +
            "· ⚠ メッシュの元の接線を上書きするため、ノーマルマップをサンプリングする" +
            "シェーダー（URP/Lit、Standard など）が誤った TBN を受け取り、ノーマルマップが" +
            "機能しなくなります。そのメッシュでノーマルマップを使わない場合にのみ選んでください。\n" +
            "· 常にオブジェクト空間で、接線空間は不要です —— Unity は tangent.xyz を方向として" +
            "スキニングするので、格納した方向は自然にボーンアニメーションへ追従します。\n" +
            "· 頂点カラーを空けたいだけなら、TEXCOORD チャンネルを優先してください。");

        // 末行原本写的是「3 个 float / 顶点」—— 那是 1.6.x 三分量时代的残留，
        // 与本条第二行的「两个 float32，每顶点 8 字节」自相矛盾。此处一并改正。
        public static string TooltipUVChannel => OutlineLocale.Pick(
            "把方向按八面体编码存进指定 TEXCOORD 通道的 xy（TEXCOORD0–7 任选）。\n\n" +
            "· 两个 float32，每顶点 8 字节；往返角度误差约 5e-6°，可忽略。\n" +
            "· 冲突最少：不动顶点色、不动切线，与法线贴图和顶点色效果都能共存。\n" +
            "· ⚠ TEXCOORD0 就是主贴图 UV（mesh.uv），写入会毁掉贴图映射 —— 请选空闲通道，" +
            "默认 TEXCOORD1。\n" +
            "· 代价是多占一个 UV 通道的顶点带宽（2 个 float / 顶点）。",

            "Octahedral-encode the direction into the xy of the chosen TEXCOORD channel " +
            "(any of TEXCOORD0–7).\n\n" +
            "· Two float32s, 8 bytes per vertex; the round-trip angular error is about " +
            "5e-6° — negligible.\n" +
            "· Fewest clashes: leaves vertex color and the tangent alone, so it coexists with " +
            "normal maps and vertex-color effects.\n" +
            "· ⚠ TEXCOORD0 is the main texture UV (mesh.uv); writing there destroys the " +
            "texture mapping — pick a free channel, TEXCOORD1 by default.\n" +
            "· The cost is one more UV channel's worth of vertex bandwidth (2 floats / vertex).",

            "方向を八面体エンコードして、指定した TEXCOORD チャンネルの xy に格納します" +
            "（TEXCOORD0–7 から選択）。\n\n" +
            "· float32 が 2 つ、頂点あたり 8 バイト。往復の角度誤差は約 5e-6° で無視できます。\n" +
            "· 衝突が最も少ない：頂点カラーも接線も触らないため、ノーマルマップや頂点カラーの" +
            "効果と共存できます。\n" +
            "· ⚠ TEXCOORD0 はメインテクスチャ UV（mesh.uv）です。書き込むとテクスチャ" +
            "マッピングが壊れます —— 空いているチャンネルを選んでください（既定は TEXCOORD1）。\n" +
            "· 代償として、UV チャンネル 1 つ分の頂点帯域を追加で消費します（頂点あたり float 2 つ）。");

        // ═══════════════════════════════════════════════════════════════
        //  左栏 · 存储空间
        // ═══════════════════════════════════════════════════════════════
        public static string LabelStorageSpace => OutlineLocale.Pick(
            "存储空间", "Storage Space", "保存空間");

        public static string LabelStorageSpaceTooltip => OutlineLocale.Pick(
            "平滑法线写在哪个空间里 —— 与「存进哪个通道」是正交的两个维度。\n" +
            "悬停下方两个按钮可查看各自的适用场景。",
            "Which space the smooth normal is written in — an axis orthogonal to " +
            "\"which channel it goes into\".\n" +
            "Hover the two buttons below to see where each one applies.",
            "スムース法線をどの空間で書き込むか —— 「どのチャンネルに入れるか」とは" +
            "直交する軸です。\n下の 2 つのボタンにカーソルを合わせると、それぞれの適用場面を" +
            "確認できます。");

        public static string SpaceTabObject => OutlineLocale.Pick(
            "对象空间\nObject", "Object Space", "オブジェクト空間\nObject");

        public static string SpaceTabTangent => OutlineLocale.Pick(
            "切线空间\nTangent", "Tangent Space", "接線空間\nTangent");

        // 两种存储空间的说明走 Tooltip 而非常驻 HelpBox：后者占掉小半个左栏，
        // 且始终只有一种是当前相关的；Tooltip 让两种都能随时对比查看。
        public static string TooltipObjectSpace => OutlineLocale.Pick(
            "存绑定姿势下的对象空间方向，解码即用、开销最低。\n\n" +
            "仅适用于静态模型：顶点色 / TEXCOORD 不参与蒙皮，SkinnedMeshRenderer 上" +
            "外扩方向会停在绑定姿势，动画一跑描边就撕开。蒙皮模型请改用切线空间。",

            "Store the object-space direction in the bind pose. Decode and use it " +
            "directly — the cheapest option.\n\n" +
            "Static meshes only: vertex color / TEXCOORD are not skinned, so on a " +
            "SkinnedMeshRenderer the extrusion direction stays frozen in the bind pose and " +
            "the outline tears apart as soon as the animation plays. Use tangent space for " +
            "skinned meshes.",

            "バインドポーズにおけるオブジェクト空間の方向を格納します。デコードしてそのまま" +
            "使え、コストは最小です。\n\n" +
            "静的メッシュ専用：頂点カラー / TEXCOORD はスキニングされないため、" +
            "SkinnedMeshRenderer では押し出し方向がバインドポーズのまま止まり、" +
            "アニメーションを再生した途端にアウトラインが裂けます。スキンメッシュには" +
            "接線空間を使ってください。");

        public static string TooltipTangentSpace => OutlineLocale.Pick(
            "存相对每个顶点自身 TBN 的坐标，解码时用【蒙皮后】的法线与切线重建。\n\n" +
            "· 蒙皮模型也正确：切线空间坐标是蒙皮不变量，不受骨骼变换影响。\n" +
            "· 要求网格有合法切线（导入设置 Tangents ≠ None）；但不占用切线，法线贴图照常可用。\n" +
            "· 烘焙用的是当前的法线 / 切线数据。若之后以不同的切线生成方式重新导入模型，" +
            "已烘数据会静默失配，需重新烘焙 —— 建议配合「导入自动烘焙」页签使用。\n" +
            "· 材质的「存储空间」必须同步选为切线空间，否则描边方向整体偏斜。",

            "Store coordinates relative to each vertex's own TBN; decoding rebuilds the basis " +
            "from the [skinned] normal and tangent.\n\n" +
            "· Correct on skinned meshes too: tangent-space coordinates are a skinning " +
            "invariant, unaffected by bone transforms.\n" +
            "· Requires the mesh to have valid tangents (import setting Tangents ≠ None) — " +
            "but it does not occupy the tangent, so normal maps keep working.\n" +
            "· Baking uses the current normal / tangent data. Re-importing the model later " +
            "with a different tangent generation silently invalidates the baked data and you " +
            "must re-bake — pair this with the \"Auto-Bake on Import\" tab.\n" +
            // 材质上那个属性的英文名就叫 Smooth Normal Space（_SmoothNormalSpace）。
            // 英文里绝不能写 Storage Space —— 那是工具侧术语，还正好是本窗口自己的
            // 分区标题，用户会在当前面板里找而不去材质 Inspector 找。
            "· The material's \"Smooth Normal Space\" must be set to tangent space as well, " +
            "or the whole outline direction skews.",

            "各頂点自身の TBN を基準とした座標を格納し、デコード時に【スキニング後】の" +
            "法線と接線から基底を再構築します。\n\n" +
            "· スキンメッシュでも正しい：接線空間の座標はスキニング不変量で、ボーン変換の" +
            "影響を受けません。\n" +
            "· メッシュに正しい接線が必要です（インポート設定の Tangents ≠ None）。ただし" +
            "接線を占有しないので、ノーマルマップはそのまま使えます。\n" +
            "· ベイクには現在の法線 / 接線データを使います。後で異なる接線生成方式でモデルを" +
            "再インポートすると、ベイク済みデータは無言で食い違い、再ベイクが必要になります " +
            "—— 「インポート自動ベイク」タブとの併用を推奨します。\n" +
            // 日文同样不能写「保存空間」—— 理由与英文那条完全一样：那是本窗口自己的
            // 分区标题（LabelStorageSpace 日文即「保存空間」）。
            "· マテリアル側の「Smooth Normal Space」も接線空間に合わせる必要があります。" +
            "合わないとアウトラインの方向が全体的にずれます。");

        public static string StorageSpaceNotApplicable => OutlineLocale.Pick(
            "切线通道存储恒为对象空间：切线本身就是数据，没有基可供重建。\n" +
            "该模式也无需切线空间 —— Unity 会把 tangent.xyz 当方向一起蒙皮，" +
            "存进去的方向天然跟随骨骼动画。",

            "Tangent-channel storage is always object space: the tangent itself is the data, " +
            "so there is no basis left to rebuild from.\n" +
            "The mode does not need tangent space either — Unity skins tangent.xyz as a " +
            "direction, so what you store follows the skeleton automatically.",

            "接線チャンネルへの保存は常にオブジェクト空間です：接線そのものがデータなので、" +
            "再構築に使える基底が残りません。\n" +
            "このモードに接線空間は不要でもあります —— Unity は tangent.xyz を方向として" +
            "スキニングするため、格納した方向は自然にボーンアニメーションへ追従します。");

        // ═══════════════════════════════════════════════════════════════
        //  存储方式 · 顶点色
        // ═══════════════════════════════════════════════════════════════
        public static string LabelVcChannelPair => OutlineLocale.Pick(
            "存储通道对", "Channel pair", "保存チャンネルペア");

        public static string LabelVcChannelStatus => OutlineLocale.Pick(
            "顶点色通道数据状态", "Vertex color channel status", "頂点カラーチャンネルのデータ状態");

        /// <summary>RGBA 状态行的行名，如「R 通道」。</summary>
        public static string VcChannelName(string channel) => OutlineLocale.Fmt(
            "{0} 通道", "{0} channel", "{0} チャンネル", channel);

        // 存进去的是【八面体编码后】的两个参数，不是法线的原始分量 —— OctEncode 先按
        // L1 归一化、z<0 时还要 OctWrap，与 normal.x / normal.y 没有对应关系。
        // 原文写「法线 X」会把读者引向那个已被废弃的「存 XY + 重建 Z」方案。
        // ⚠ 分量与模式的对应，按 StorageWriter.WriteToVertexColor 的实际写入：
        //     RG → r=oct.x, g=oct.y ／ GB → g=oct.x, b=oct.y ／ BA → b=oct.x, a=oct.y
        //   所以 G 在 RG 下是 Y、在 GB 下是 X；B 在 GB 下是 Y、在 BA 下是 X。
        //   原文「八面体 X/Y（RG/GB 模式）」按位置读恰好是反的，这里改成「模式：分量」
        //   的显式写法，读法唯一，不再依赖两个并列表的顺序对齐。
        public static string VcRoleR => OutlineLocale.Pick(
            "RG 模式：八面体 X",
            "RG mode: octahedral X",
            "RG モード：八面体 X");

        public static string VcRoleG => OutlineLocale.Pick(
            "RG 模式：八面体 Y ／ GB 模式：八面体 X",
            "RG mode: octahedral Y / GB mode: octahedral X",
            "RG モード：八面体 Y ／ GB モード：八面体 X");

        public static string VcRoleB => OutlineLocale.Pick(
            "GB 模式：八面体 Y ／ BA 模式：八面体 X",
            "GB mode: octahedral Y / BA mode: octahedral X",
            "GB モード：八面体 Y ／ BA モード：八面体 X");

        public static string VcRoleA => OutlineLocale.Pick(
            "BA 模式：八面体 Y",
            "BA mode: octahedral Y",
            "BA モード：八面体 Y");

        public static string VcWillOverwrite(string role) => OutlineLocale.Fmt(
            "将覆盖写入  •  {0}", "Will overwrite  •  {0}", "上書きします  •  {0}", role);

        public static string VcWillWrite(string role) => OutlineLocale.Fmt(
            "将写入  •  {0}", "Will be written  •  {0}", "書き込みます  •  {0}", role);

        public static string VcHasDataNotTarget => OutlineLocale.Pick(
            "有数据（非当前写入通道）",
            "Has data (not the current write target)",
            "データあり（現在の書き込み先ではありません）");

        public static string VcNoData => OutlineLocale.Pick("无数据", "No data", "データなし");

        // 原文是「选定通道对的 XY 分量将被写入，Z 分量通过重建得到」—— 那描述的是
        // README.md:267 点名废弃的「存 XY + 重建 Z + 按法线定符号」方案（它恰恰会在
        // 硬边角上把描边裂开），与同文件 TooltipVertexColor 的「八面体编码」自相矛盾。
        // 三语一并改为八面体口径，与 UvModeHelp 的说法对齐。
        public static string VcModeHelp => OutlineLocale.Pick(
            "平滑法线按八面体编码写入选定通道对的两个分量（每顶点 2 字节），" +
            "解码时还原为完整方向。非激活通道原有数据不受影响。",

            "The smooth normal is octahedral-encoded into the two components of the selected " +
            "channel pair (2 bytes per vertex) and decoded back to a full direction. " +
            "Channels outside the pair keep their existing data.",

            "スムース法線を八面体エンコードして、選んだチャンネルペアの 2 成分に" +
            "書き込みます（頂点あたり 2 バイト）。デコード時に完全な方向へ復元します。" +
            "ペア以外のチャンネルの既存データはそのままです。");

        /// <summary>清除某一对顶点色通道的按钮，如「清除 RG」。</summary>
        public static string BtnClearPair(string pair) => OutlineLocale.Fmt(
            "清除 {0}", "Clear {0}", "{0} をクリア", pair);

        // ═══════════════════════════════════════════════════════════════
        //  存储方式 · 切线通道
        // ═══════════════════════════════════════════════════════════════
        public static string TangentWDesc => OutlineLocale.Pick(
            "恒为 1，不参与解码", "Always 1, not used when decoding", "常に 1。デコードには使いません");

        public static string TangentModeHelp => OutlineLocale.Pick(
            "本模式会【覆盖网格的原始切线】，采样法线贴图的 Shader（URP/Lit、Standard 等）" +
            "将因此得到错误的 TBN，表现为法线贴图失效。\n\n" +
            "仅在该网格不使用法线贴图时选用。若只是想避开顶点色，" +
            "优先考虑 TEXCOORD 通道。\n\n" +
            "优点：可存完整三个分量，无需压缩、精度最高。",

            "This mode [overwrites the mesh's original tangent], so shaders that sample " +
            "normal maps (URP/Lit, Standard, …) get a wrong TBN and the normal map breaks.\n\n" +
            "Only pick it when the mesh does not use normal maps. If you only want to keep " +
            "vertex color free, prefer a TEXCOORD channel.\n\n" +
            "Upside: three full components, no compression, the highest precision available.",

            "このモードは【メッシュの元の接線を上書きします】。ノーマルマップをサンプリング" +
            "するシェーダー（URP/Lit、Standard など）は誤った TBN を受け取り、ノーマルマップが" +
            "機能しなくなります。\n\n" +
            "そのメッシュでノーマルマップを使わない場合にのみ選んでください。頂点カラーを" +
            "空けたいだけなら、TEXCOORD チャンネルを優先してください。\n\n" +
            "利点：完全な 3 成分を圧縮なしで格納でき、精度が最も高くなります。");

        // 英文用 real 而非 normal：本条紧挨着连说三次 normal map 的告警框，
        // normal tangents 会被读成「法线的切线」。README_EN:166 / 384 同样用 real。
        public static string BtnRecalcTangents => OutlineLocale.Pick(
            "重算切线（恢复正常切线）",
            "Recalculate tangents (restore real tangents)",
            "接線を再計算（通常の接線に戻す）");

        // ═══════════════════════════════════════════════════════════════
        //  存储方式 · TEXCOORD 通道
        // ═══════════════════════════════════════════════════════════════
        public static string LabelUvStorageChannel => OutlineLocale.Pick(
            "存储通道", "Storage channel", "保存チャンネル");

        // UV 通道一律以 TEXCOORDn 命名，取值与 mesh.SetUVs(n) 的索引恒等对应。
        // 不用「UV1/UV2」这类叫法：Unity 自己的 mesh.uv2 就是 TEXCOORD1，名字和索引差一位。
        //
        // 这是本文件唯一返回【数组】的成员，因此必须自己按语言缓存 —— Popup 每帧都要读它，
        // 每次现建 8 个元素就是每帧 8 次分配。
        private static string[] _uvChannelNames;
        private static OutlineLanguage _uvChannelNamesLanguage;

        public static string[] UvChannelNames
        {
            get
            {
                if (_uvChannelNames != null && _uvChannelNamesLanguage == OutlineLocale.Current)
                    return _uvChannelNames;

                _uvChannelNamesLanguage = OutlineLocale.Current;
                _uvChannelNames = new[]
                {
                    OutlineLocale.Pick(
                        "TEXCOORD0  (mesh.uv — 主贴图 UV)",
                        "TEXCOORD0  (mesh.uv — main texture UV)",
                        "TEXCOORD0  (mesh.uv — メインテクスチャ UV)"),
                    "TEXCOORD1  (mesh.uv2)",
                    "TEXCOORD2  (mesh.uv3)",
                    "TEXCOORD3  (mesh.uv4)",
                    "TEXCOORD4  (mesh.uv5)",
                    "TEXCOORD5  (mesh.uv6)",
                    "TEXCOORD6  (mesh.uv7)",
                    "TEXCOORD7  (mesh.uv8)",
                };
                return _uvChannelNames;
            }
        }

        public static string UvSelectedOverwrite(string state) => OutlineLocale.Fmt(
            "当前选中，将覆盖写入（{0}）",
            "Selected — will overwrite ({0})",
            "選択中 —— 上書きします（{0}）", state);

        public static string UvSelectedWrite => OutlineLocale.Pick(
            "当前选中，将写入此通道",
            "Selected — will be written here",
            "選択中 —— このチャンネルに書き込みます");

        public static string BtnClear => OutlineLocale.Pick("清除", "Clear", "クリア");

        public static string UvRiskyHelp => OutlineLocale.Pick(
            "TEXCOORD0 是模型的主贴图 UV，该网格已有数据。写入会覆盖它并破坏贴图映射，" +
            "且影响所有使用此网格的对象。除非你确定该通道空闲，否则请改用 TEXCOORD1。",

            "TEXCOORD0 is the model's main texture UV and this mesh already has data there. " +
            "Writing overwrites it, destroys the texture mapping, and affects every object " +
            "using this mesh. Unless you are sure the channel is free, use TEXCOORD1 instead.",

            "TEXCOORD0 はモデルのメインテクスチャ UV で、このメッシュには既にデータが" +
            "あります。書き込むと上書きされてテクスチャマッピングが壊れ、このメッシュを使う" +
            "すべてのオブジェクトに影響します。空いていると確信できない限り、TEXCOORD1 を" +
            "使ってください。");

        public static string UvLegacyHelp(int channel) => OutlineLocale.Fmt(
            "TEXCOORD{0} 里是 3 分量数据，很可能是 1.6.x 及更早烘焙的旧格式平滑法线。\n" +
            "1.7.0 起改存 2 分量八面体，旧数据无法被新版 Shader 解码 —— 在这里重新生成一次" +
            "即可迁移，材质不用动。\n" +
            "注意这只是强信号而非判定：网格合并会把 UV 维度统一取最大，其他把 3 分量方向" +
            "写进 UV 的工具（植被风场、VAT 等）同样会命中。",

            "TEXCOORD{0} holds 3-component data — very likely smooth normals baked in the old " +
            "format by 1.6.x or earlier.\n" +
            "Since 1.7.0 the channel stores a 2-component octahedral encoding, and the old " +
            "data cannot be decoded by the new shader — regenerating here migrates it, with no " +
            "material change needed.\n" +
            "Note this is a strong hint, not a verdict: mesh combining unifies UV dimensions to " +
            "the maximum, and other tools that write 3-component directions into UVs " +
            "(vegetation wind, VAT, …) match it too.",

            "TEXCOORD{0} には 3 成分のデータがあり、1.6.x 以前でベイクされた旧フォーマットの" +
            "スムース法線である可能性が高いです。\n" +
            "1.7.0 からは 2 成分の八面体エンコードで保存し、旧データは新しいシェーダーでは" +
            "デコードできません —— ここで生成し直せば移行でき、マテリアルの変更は不要です。\n" +
            "これは強い手がかりであって判定ではない点に注意してください：メッシュ結合は UV の" +
            "次元を最大値に揃えますし、3 成分の方向を UV に書き込む他のツール" +
            "（植生の風、VAT など）も同様に該当します。", channel);

        public static string UvModeHelp => OutlineLocale.Pick(
            "平滑法线按八面体编码写入选定 TEXCOORD 通道的 xy 两个分量（每顶点 8 字节）。",
            "The smooth normal is octahedral-encoded into the xy of the selected TEXCOORD " +
            "channel (8 bytes per vertex).",
            "スムース法線を八面体エンコードして、選んだ TEXCOORD チャンネルの xy 2 成分に" +
            "書き込みます（頂点あたり 8 バイト）。");

        public static string DialogClearUv0Title => OutlineLocale.Pick(
            "清除主贴图 UV？", "Clear the main texture UV?", "メインテクスチャ UV をクリアしますか？");

        public static string DialogClearUv0Body(string meshName) => OutlineLocale.Fmt(
            "TEXCOORD0 是「{0}」的主贴图 UV（mesh.uv）。\n\n" +
            "清除后该网格的贴图映射会丢失，且影响所有使用此网格的对象。\n\n" +
            "确定要清除吗？",

            "TEXCOORD0 is the main texture UV (mesh.uv) of \"{0}\".\n\n" +
            "Clearing it loses this mesh's texture mapping and affects every object using " +
            "this mesh.\n\n" +
            "Clear it anyway?",

            "TEXCOORD0 は「{0}」のメインテクスチャ UV（mesh.uv）です。\n\n" +
            "クリアするとこのメッシュのテクスチャマッピングが失われ、このメッシュを使う" +
            "すべてのオブジェクトに影響します。\n\n" +
            "本当にクリアしますか？", meshName);

        public static string BtnConfirmClear => OutlineLocale.Pick(
            "确定清除", "Clear", "クリアする");

        // ═══════════════════════════════════════════════════════════════
        //  存储方式的简称（生成按钮、视口徽标、日志共用）
        // ═══════════════════════════════════════════════════════════════
        public static string ShortModeVertexColor => OutlineLocale.Pick(
            "顶点色", "Vertex Color", "頂点カラー");

        public static string ShortModeTangentChannel => OutlineLocale.Pick(
            "切线通道", "Tangent Channel", "接線チャンネル");

        public static string ShortSpaceTangent => OutlineLocale.Pick(
            "切线空间", "Tangent Space", "接線空間");

        public static string ShortSpaceObject => OutlineLocale.Pick(
            "对象空间", "Object Space", "オブジェクト空間");

        // ═══════════════════════════════════════════════════════════════
        //  左栏 · 生成平滑法线
        // ═══════════════════════════════════════════════════════════════
        public static string SectionGenerate => OutlineLocale.Pick("生成平滑法线", "Generate", "生成");

        public static string LabelMergeTolerance => OutlineLocale.Pick(
            "合并容差", "Merge Tolerance", "結合トレランス");

        public static string MergeToleranceTooltip => OutlineLocale.Pick(
            "距离在此范围内的顶点视为同一点，其面法线会被合并平均。\n\n" +
            "接缝顶点经 DCC 导出、FBX 浮点截断或缩放后往往会有 1e-6 量级的微小偏差，" +
            "容差过小会让它们无法合并、描边在接缝处仍然开裂。\n\n" +
            "容差必须远小于模型的最小真实特征尺寸，否则会把本应分开的顶点错误合并。",

            "Vertices within this distance are treated as one point and their face normals are " +
            "averaged together.\n\n" +
            "Seam vertices usually end up around 1e-6 apart after DCC export, FBX float " +
            "truncation or scaling; too small a tolerance leaves them unmerged and the outline " +
            "still cracks along the seam.\n\n" +
            "The tolerance must be far smaller than the model's smallest real feature, " +
            "or vertices that should stay separate get merged by mistake.",

            "この距離以内の頂点を同一点とみなし、面法線をまとめて平均します。\n\n" +
            "シームの頂点は DCC からの書き出し、FBX の float 丸め、スケーリングを経ると " +
            "1e-6 程度のわずかなずれを持つことが多く、トレランスが小さすぎると結合されず、" +
            "シームでアウトラインが割れたままになります。\n\n" +
            "トレランスはモデルの最小の実特徴サイズより十分小さくする必要があります。" +
            "さもないと本来分かれているべき頂点まで誤って結合されます。");

        public static string MergeToleranceTooLarge => OutlineLocale.Pick(
            "容差偏大，可能把本应分开的顶点错误合并，导致描边变形。",
            "The tolerance is large; vertices that should stay separate may be merged by " +
            "mistake, deforming the outline.",
            "トレランスが大きすぎます。本来分かれているべき頂点が誤って結合され、" +
            "アウトラインが変形する可能性があります。");

        /// <summary>大生成按钮。{0} = 目标通道简称，{1} = 「  ×N」或空串。</summary>
        public static string BtnGenerate(string mode, string countSuffix) => OutlineLocale.Fmt(
            "▶  生成平滑法线  →  {0}{1}",
            "▶  Generate Smooth Normals  →  {0}{1}",
            "▶  スムース法線を生成  →  {0}{1}", mode, countSuffix);

        public static string HealthCardTitle => OutlineLocale.Pick(
            "网格健康检查（焦点网格）",
            "Mesh Health Check (focused mesh)",
            "メッシュ健全性チェック（フォーカスのメッシュ）");

        // ── 生成前的两道二次确认 ──────────────────────────────────────
        public static string DialogUnhealthyTitle => OutlineLocale.Pick(
            "网格数据异常", "Mesh data problems", "メッシュデータの異常");

        public static string DialogUnhealthyBody(string mesh) => OutlineLocale.Fmt(
            "网格「{0}」存在无法处理的数据问题（详见「生成平滑法线」区域的健康检查），" +
            "生成结果可能不正确或失败。仍要继续吗？",

            "The mesh \"{0}\" has data problems that cannot be handled (see the health check in " +
            "the Generate section). The result may be wrong or the generation may fail. " +
            "Continue anyway?",

            "メッシュ「{0}」に処理できないデータの問題があります（「生成」欄の健全性チェックを" +
            "参照）。生成結果が正しくない、または失敗する可能性があります。続行しますか？", mesh);

        public static string BtnContinueAnyway => OutlineLocale.Pick(
            "仍要继续", "Continue", "続行する");

        public static string DialogOverwriteUv0Title => OutlineLocale.Pick(
            "覆盖主贴图 UV？", "Overwrite the main texture UV?", "メインテクスチャ UV を上書きしますか？");

        public static string DialogOverwriteUv0Body(int count) => OutlineLocale.Fmt(
            "当前存储通道是 TEXCOORD0（主贴图 mesh.uv）。\n\n" +
            "对勾选的 {0} 个网格写入平滑法线会覆盖各自的主贴图 UV，贴图映射将丢失，" +
            "且影响所有使用这些网格的对象。\n\n建议改用 TEXCOORD1。仍要继续吗？",

            "The current storage channel is TEXCOORD0 (the main texture UV, mesh.uv).\n\n" +
            "Writing smooth normals to the {0} checked mesh(es) overwrites each one's main " +
            "texture UV. The texture mapping is lost, and every object using those meshes is " +
            "affected.\n\nTEXCOORD1 is recommended instead. Continue anyway?",

            "現在の保存チャンネルは TEXCOORD0（メインテクスチャ UV、mesh.uv）です。\n\n" +
            "チェックした {0} 個のメッシュにスムース法線を書き込むと、それぞれのメイン" +
            "テクスチャ UV が上書きされ、テクスチャマッピングが失われます。これらの" +
            "メッシュを使うすべてのオブジェクトに影響します。\n\n" +
            "TEXCOORD1 の使用を推奨します。続行しますか？", count);

        public static string BtnOverwriteAnyway => OutlineLocale.Pick(
            "仍要覆盖", "Overwrite", "上書きする");

        // ── 生成流程的日志与进度 ──────────────────────────────────────
        public static string LogNothingChecked => OutlineLocale.Pick(
            "未勾选任何网格，请在目标列表中勾选要处理的网格。",
            "No mesh is checked — check the meshes you want to process in the target list.",
            "メッシュが 1 つもチェックされていません。対象一覧で処理したいメッシュを" +
            "チェックしてください。");

        public static string ProgressTitle => OutlineLocale.Pick(
            "生成平滑法线", "Generating smooth normals", "スムース法線を生成中");

        public static string ProgressBody(int index, int total, string mesh, int verts)
            => OutlineLocale.Fmt(
                "({0}/{1}) {2} —— {3} 顶点",
                "({0}/{1}) {2} — {3} vertices",
                "({0}/{1}) {2} —— {3} 頂点", index, total, mesh, verts);

        /// <summary>生成日志里的存储描述 —— 写清「数据落在哪、什么格式」。</summary>
        public static string LogStorageVertexColor(string pair) => OutlineLocale.Fmt(
            "顶点色 {0}（八面体 2×8bit）",
            "Vertex Color {0} (octahedral 2×8bit)",
            "頂点カラー {0}（八面体 2×8bit）", pair);

        public static string LogStorageTangent => OutlineLocale.Pick(
            "切线通道 tangent.xyz", "Tangent Channel tangent.xyz", "接線チャンネル tangent.xyz");

        public static string LogStorageUv(int channel) => OutlineLocale.Fmt(
            "TEXCOORD{0}（八面体 2×float）",
            "TEXCOORD{0} (octahedral 2×float)",
            "TEXCOORD{0}（八面体 2×float）", channel);

        public static string LogGenerated(string storage, string space, string mesh,
                                          int verts, string tolerance) => OutlineLocale.Fmt(
            "生成完成 → 模式: {0}, 空间: {1}, Mesh: {2}, 顶点数: {3}, 合并容差: {4}",
            "Generated → mode: {0}, space: {1}, mesh: {2}, vertices: {3}, merge tolerance: {4}",
            "生成完了 → 方式: {0}, 空間: {1}, Mesh: {2}, 頂点数: {3}, 結合トレランス: {4}",
            storage, space, mesh, verts, tolerance);

        public static string LogCanceled(int done, int total) => OutlineLocale.Fmt(
            "已取消生成：已处理 {0} / {1} 个网格。" +
            "已处理的网格数据已经改变但尚未保存，如需回退请点「还原」。",

            "Generation canceled: {0} of {1} meshes were processed. Those meshes have already " +
            "been modified but not saved — use Revert if you want to roll them back.",

            // 引号里必须是界面上真实存在的按钮文字。中文「还原」⊂「↺ 还原本次修改」、
            // 英文 Revert ⊂「↺ Revert this change」都成立，而日文按钮叫
            //「↺ 今回の変更を元に戻す」—— 界面上根本没有「復元」二字。
            "生成をキャンセルしました：{1} 個中 {0} 個のメッシュを処理済みです。" +
            "処理済みのメッシュはすでに変更されていますが未保存です。" +
            "元に戻すには「今回の変更を元に戻す」を使ってください。", done, total);

        public static string LogBatchDone(int count) => OutlineLocale.Fmt(
            "批量生成完成，共处理 {0} 个网格。",
            "Batch generation finished: {0} meshes processed.",
            "一括生成が完了しました：{0} 個のメッシュを処理しました。", count);

        // ── 快照还原 ──────────────────────────────────────────────────
        public static string LogRestored(int count) => OutlineLocale.Fmt(
            "已还原 {0} 个网格到本次修改之前的状态。",
            "Reverted {0} mesh(es) to the state before this session's changes.",
            "{0} 個のメッシュを、今回の変更前の状態に復元しました。", count);

        public static string LogUvDimFallback(string mesh, int channel, int dim)
            => OutlineLocale.Fmt(
                "网格「{0}」的 TEXCOORD{1} 分量数为 {2}，" +
                "无法原样还原（SetUVs 仅支持 2 / 3 / 4 分量），已按 2 分量写回。",

                "TEXCOORD{1} of the mesh \"{0}\" has {2} components and cannot be restored " +
                "as-is (SetUVs only supports 2 / 3 / 4 components); it was written back as 2.",

                "メッシュ「{0}」の TEXCOORD{1} は成分数が {2} で、そのままでは復元できません" +
                "（SetUVs は 2 / 3 / 4 成分のみ対応）。2 成分として書き戻しました。",
                mesh, channel, dim);

        // ═══════════════════════════════════════════════════════════════
        //  右栏上 · 数据通道状态总览
        // ═══════════════════════════════════════════════════════════════
        public static string SectionDataOverview => OutlineLocale.Pick(
            "数据通道状态总览", "Data Channel Overview", "データチャンネル状態の一覧");

        public static string CardVertexColorTitle => OutlineLocale.Pick(
            "顶点色  Vertex Color", "Vertex Color", "頂点カラー  Vertex Color");

        public static string CardVertexColorDesc => OutlineLocale.Pick(
            "平滑法线 → 选定通道对（八面体编码）",
            "Smooth normal → the selected channel pair (octahedral)",
            "スムース法線 → 選んだチャンネルペア（八面体エンコード）");

        public static string ChipVarying  => OutlineLocale.Pick("有变化", "Varying", "変化あり");
        public static string ChipConstant => OutlineLocale.Pick("常量", "Constant", "定数");

        // 卡片叫「切线 / Tangent / 接線」，不叫 Tangent Channel —— 后者是【存储方式】
        // 页签的名字（ModeTabTangentChannel），同一窗口里两个控件撞名会分不清指哪个。
        // 三份 README 的三张卡片也都写「顶点色 / 切线 / TEXCOORD」。
        public static string CardTangentTitle => OutlineLocale.Pick(
            "切线  Tangent", "Tangent", "接線  Tangent");

        public static string CardTangentDesc => OutlineLocale.Pick(
            "平滑法线 → tangent.xyz（对象空间，会覆盖原始切线）",
            "Smooth normal → tangent.xyz (object space, overwrites the original tangent)",
            "スムース法線 → tangent.xyz（オブジェクト空間、元の接線を上書き）");

        public static string ChipTangentWConst => OutlineLocale.Pick("恒为 1", "Always 1", "常に 1");

        public static string CardUvTitle => OutlineLocale.Pick(
            "TEXCOORD 通道", "TEXCOORD Channels", "TEXCOORD チャンネル");

        public static string CardUvDesc => OutlineLocale.Pick(
            "平滑法线 → 选定通道的 xy（八面体编码）",
            "Smooth normal → the xy of the selected channel (octahedral)",
            "スムース法線 → 選んだチャンネルの xy（八面体エンコード）");

        // ═══════════════════════════════════════════════════════════════
        //  右栏下 · 描边预览与参数
        // ═══════════════════════════════════════════════════════════════
        public static string SectionOutlinePreview => OutlineLocale.Pick(
            "描边预览", "Outline Preview", "アウトラインプレビュー");

        public static string PreviewNoneChecked => OutlineLocale.Pick(
            "请勾选要预览的 Mesh", "Check a Mesh to preview", "プレビューする Mesh をチェックしてください");

        public static string PreviewNoTarget => OutlineLocale.Pick(
            "请先选择 Mesh", "Select a Mesh first", "先に Mesh を選択してください");

        public static string ViewportHint => OutlineLocale.Pick(
            "左键旋转  |  滚轮缩放  |  中键平移",
            "Left drag: orbit  |  Wheel: zoom  |  Middle drag: pan",
            "左ドラッグ: 回転  |  ホイール: ズーム  |  中ドラッグ: パン");

        public static string ParamHeaderOutline   => OutlineLocale.Pick("描边参数", "Outline", "アウトライン");
        public static string ParamHeaderModel     => OutlineLocale.Pick("模型参数", "Model", "モデル");
        public static string ParamHeaderViewport  => OutlineLocale.Pick("视口参数", "Viewport", "ビューポート");
        public static string ParamHeaderNormalVis => OutlineLocale.Pick(
            "法线可视化", "Normal Visualization", "法線可視化");
        public static string ParamHeaderCamera    => OutlineLocale.Pick("相机控制", "Camera", "カメラ");

        public static string ToggleShowOutline => OutlineLocale.Pick("显示描边", "Show outline", "アウトラインを表示");
        public static string FieldOutlineColor => OutlineLocale.Pick("描边颜色", "Outline color", "アウトライン色");
        public static string FieldOutlineWidth => OutlineLocale.Pick("描边宽度", "Outline width", "アウトライン幅");

        public static string FieldWidthMode => OutlineLocale.Pick("宽度模式", "Width mode", "幅モード");

        public static string FieldWidthModeTooltip => OutlineLocale.Pick(
            "屏幕空间：描边等宽，不随距离变化；\n世界空间：按世界单位偏移，近大远小。",
            "Screen space: constant width regardless of distance.\n" +
            "World space: offset in world units, so it shrinks with distance.",
            // 日文用 スペース 而非 空間：空間 已被「保存空間（オブジェクト空間 / 接線空間）」
            // 占用，宽度模式再叫 ワールド空間 会与存储空间混淆。README_JA:195/395/533 同此。
            "スクリーンスペース：距離に関係なく一定の太さ。\n" +
            "ワールドスペース：ワールド単位でオフセットするため、遠いほど細く見えます。");

        // 与 UvChannelNames 同理：Popup 每帧读取，按语言缓存，不每帧新建数组。
        private static string[] _widthModeNames;
        private static OutlineLanguage _widthModeNamesLanguage;

        public static string[] WidthModeNames
        {
            get
            {
                if (_widthModeNames != null && _widthModeNamesLanguage == OutlineLocale.Current)
                    return _widthModeNames;

                _widthModeNamesLanguage = OutlineLocale.Current;
                _widthModeNames = new[]
                {
                    OutlineLocale.Pick("屏幕空间", "Screen Space", "スクリーンスペース"),
                    OutlineLocale.Pick("世界空间", "World Space", "ワールドスペース"),
                };
                return _widthModeNames;
            }
        }

        public static string ToggleShowBase   => OutlineLocale.Pick("显示模型", "Show model", "モデルを表示");
        public static string FieldBaseColor   => OutlineLocale.Pick("基础颜色", "Base color", "ベースカラー");
        // スムースネス（不是スムーズネス）—— 与 README_JA:142 / 197 及本文件头的规矩一致。
        public static string FieldSmoothness  => OutlineLocale.Pick("光滑度", "Smoothness", "スムースネス");
        public static string FieldMetallic    => OutlineLocale.Pick("金属度", "Metallic", "メタリック");
        public static string FieldBgColor     => OutlineLocale.Pick("背景颜色", "Background color", "背景色");

        public static string ToggleShowSmoothNormals => OutlineLocale.Pick(
            "显示平滑法线", "Show smooth normals", "スムース法線を表示");
        public static string FieldNormalLength => OutlineLocale.Pick(
            "法线长度", "Normal length", "法線の長さ");
        public static string FieldSmoothNormalColor => OutlineLocale.Pick(
            "平滑法线颜色", "Smooth normal color", "スムース法線の色");
        public static string ToggleShowOriginalNormals => OutlineLocale.Pick(
            "显示原始法线", "Show original normals", "元の法線を表示");
        public static string FieldOriginalNormalColor => OutlineLocale.Pick(
            "原始法线颜色", "Original normal color", "元の法線の色");

        public static string FieldOrbitX => OutlineLocale.Pick("水平旋转", "Yaw", "水平回転");
        public static string FieldOrbitY => OutlineLocale.Pick("垂直旋转", "Pitch", "垂直回転");
        public static string FieldZoom   => OutlineLocale.Pick("距离", "Distance", "距離");
        public static string BtnResetView => OutlineLocale.Pick("重置视角", "Reset view", "視点をリセット");

        // ── Scene 视图法线叠加 ────────────────────────────────────────
        public static string ToggleSceneOverlay => OutlineLocale.Pick(
            "在 Scene 视图中显示", "Show in Scene view", "Scene ビューに表示");

        public static string ToggleSceneOverlayTooltip => OutlineLocale.Pick(
            "把上面这两组法线同时画到 Scene 视图里，作用于【所有勾选的场景网格】。\n\n" +
            "SkinnedMeshRenderer 会取当前姿势 —— 播放动画时法线应始终贴着表面走；" +
            "关节处若扇形散开，就是数据烘的空间与材质选的对不上。\n\n" +
            "仅从场景对象发现的网格可画（直接选中 Mesh 资产的没有场景位置）。\n" +
            "开启后 Scene 视图每次重绘都会重算，仅建议排查时打开。",

            "Draw both sets of normals above into the Scene view, for [every checked scene " +
            "mesh].\n\n" +
            "A SkinnedMeshRenderer is evaluated in its current pose — while an animation plays " +
            "the normals should keep hugging the surface. If they fan out at the joints, the " +
            "space the data was baked in does not match the one the material selects.\n\n" +
            "Only meshes discovered from scene objects can be drawn (a directly selected Mesh " +
            "asset has no position in the scene).\n" +
            "Once enabled, every Scene view repaint recomputes this — turn it on only while " +
            "investigating.",

            "上の 2 組の法線を Scene ビューにも描画します。対象は【チェックしたすべての" +
            "シーンメッシュ】です。\n\n" +
            "SkinnedMeshRenderer は現在のポーズで評価されます —— アニメーション再生中、" +
            "法線は常に表面に沿っているはずです。関節で扇状に広がる場合は、データをベイクした" +
            "空間とマテリアルで選んだ空間が一致していません。\n\n" +
            "描画できるのはシーンオブジェクトから見つかったメッシュのみです" +
            "（直接選択した Mesh アセットはシーン上に位置を持ちません）。\n" +
            "有効にすると Scene ビューの再描画のたびに再計算します。調査時のみ有効に" +
            "することを推奨します。");

        public static string SceneOverlayNoSceneMesh => OutlineLocale.Pick(
            "勾选的条目里没有场景对象。直接选中的 Mesh 资产在场景中没有位置，无法叠加。",
            "None of the checked entries is a scene object. A directly selected Mesh asset has " +
            "no position in the scene, so nothing can be overlaid.",
            "チェックした項目にシーンオブジェクトがありません。直接選択した Mesh アセットは" +
            "シーン上に位置を持たないため、オーバーレイできません。");

        // ═══════════════════════════════════════════════════════════════
        //  「导入自动烘焙」页签
        // ═══════════════════════════════════════════════════════════════
        public static string AutoBakeIntro => OutlineLocale.Pick(
            "命中规则的模型，在导入 / 重导入时自动把平滑法线烘焙进网格。\n" +
            "非破坏性：改成不再命中、或关闭开关后重新导入，即恢复原始网格。",

            "Models that match the rules get smooth normals baked into their meshes on " +
            "(re)import.\n" +
            "Non-destructive: make them stop matching, or turn the switch off and re-import, " +
            "and the original mesh comes back.",

            "判定ルールに合致したモデルは、インポート / 再インポート時にスムース法線が" +
            "メッシュへ自動でベイクされます。\n" +
            "非破壊的です：合致しないように変更するか、スイッチを切って再インポートすれば、" +
            "元のメッシュに戻ります。");

        public static string ToggleAutoBakeEnabled => OutlineLocale.Pick(
            "启用导入时自动烘焙", "Enable auto-bake on import", "インポート時の自動ベイクを有効化");

        public static string SectionMatchRules => OutlineLocale.Pick(
            "命中规则", "Match Rules", "判定ルール");

        public static string SectionGenerateParams => OutlineLocale.Pick(
            "生成参数", "Generation Parameters", "生成パラメーター");

        // ── 命中条件 ──────────────────────────────────────────────────
        // 日文用「接尾辞」而非「サフィックス」：README_JA 全文 5 处一律用前者，
        // 用户按文档找「ファイル名の接尾辞で判定」得能在界面上找到同一个词。
        public static string MatchBySuffix => OutlineLocale.Pick(
            "文件名后缀", "Filename suffix", "ファイル名の接尾辞");

        public static string MatchBySuffixTooltip => OutlineLocale.Pick(
            "文件名（不含扩展名）以指定后缀结尾。",
            "The filename (without extension) ends with the given suffix.",
            "ファイル名（拡張子を除く）が指定した接尾辞で終わること。");

        public static string LabelSuffix => OutlineLocale.Pick("后缀", "Suffix", "接尾辞");

        public static string SuffixEmptyHint => OutlineLocale.Pick(
            "后缀为空 → 不命中任何模型",
            "Suffix is empty → matches no model",
            "接尾辞が空 → どのモデルにも該当しません");

        public static string SuffixExample(string suffix) => OutlineLocale.Fmt(
            "例：Hero{0}.fbx（大小写不敏感）",
            "e.g. Hero{0}.fbx (case-insensitive)",
            "例：Hero{0}.fbx（大文字小文字を区別しません）", suffix);

        public static string MatchByFolder => OutlineLocale.Pick(
            "文件夹路径", "Folder path", "フォルダーパス");

        public static string MatchByFolderTooltip => OutlineLocale.Pick(
            "资产位于指定文件夹（含其子目录）之下。",
            "The asset lives under the given folder (including its subfolders).",
            "アセットが指定したフォルダー（サブフォルダーを含む）の配下にあること。");

        public static string LabelFolder => OutlineLocale.Pick("文件夹", "Folder", "フォルダー");

        public static string FolderEmptyHint => OutlineLocale.Pick(
            "未指定文件夹 → 不命中任何模型",
            "No folder set → matches no model",
            "フォルダー未指定 → どのモデルにも該当しません");

        public static string FolderMissingWarning(string path) => OutlineLocale.Fmt(
            "配置的文件夹「{0}」已不存在（被删除或改名），当前不会命中任何模型。",
            "The configured folder \"{0}\" no longer exists (deleted or renamed), so nothing " +
            "matches right now.",
            "設定されたフォルダー「{0}」は存在しません（削除または名前変更）。" +
            "現在どのモデルにも該当しません。", path);

        public static string FolderSubdirHint(string folderName) => OutlineLocale.Fmt(
            "含子目录；「{0}2」「{0}_backup」等同级目录不会被误命中。",
            "Subfolders included; siblings such as \"{0}2\" or \"{0}_backup\" are not matched " +
            "by mistake.",
            "サブフォルダーを含みます。「{0}2」「{0}_backup」のような同階層のフォルダーが" +
            "誤って該当することはありません。", folderName);

        // ── 命中结果的一句话总结 ──────────────────────────────────────
        // 三种语言各写【完整整句】的模板，片段只作参数代入 —— 逐片段翻译再拼接，
        // 在英日语序下必然拼出不通的句子。
        public static string MatchSummaryNone => OutlineLocale.Pick(
            "两个条件都未启用 → 不会命中任何模型。请至少勾选一个。",
            "Neither condition is enabled → nothing will match. Tick at least one.",
            "どちらの条件も有効になっていません → 何も該当しません。少なくとも 1 つ" +
            "チェックしてください。");

        public static string MatchPartSuffixUnset => OutlineLocale.Pick(
            "文件名后缀（未填，当前不命中）",
            "the filename suffix (not set — matches nothing right now)",
            "ファイル名の接尾辞（未入力のため現在は該当しません）");

        public static string MatchPartSuffixSet(string suffix) => OutlineLocale.Fmt(
            "文件名以「{0}」结尾",
            "the filename ends with \"{0}\"",
            "ファイル名が「{0}」で終わる", suffix);

        public static string MatchPartFolderUnset => OutlineLocale.Pick(
            "文件夹（未指定，当前不命中）",
            "the folder (not set — matches nothing right now)",
            "フォルダー（未指定のため現在は該当しません）");

        public static string MatchPartFolderSet(string folder) => OutlineLocale.Fmt(
            "位于「{0}」之下",
            "the asset is under \"{0}\"",
            "アセットが「{0}」の配下にある", folder);

        public static string MatchSummaryBoth(string suffixPart, string folderPart)
            => OutlineLocale.Fmt(
                "同时满足这两项才烘焙：{0}，且{1}。",
                "Bakes only when both hold: {0}, and {1}.",
                "次の両方を満たす場合のみベイクします：{0}、かつ{1}。", suffixPart, folderPart);

        public static string MatchSummaryOne(string part) => OutlineLocale.Fmt(
            "命中条件：{0}。", "Match condition: {0}.", "判定条件：{0}。", part);

        // ── 自动烘焙的存储设置 ────────────────────────────────────────
        public static string LabelAutoStorageChannel => OutlineLocale.Pick(
            "存储通道", "Storage channel", "保存チャンネル");

        public static string LabelAutoVcChannelPair => OutlineLocale.Pick(
            "顶点色通道对", "Vertex color channel pair", "頂点カラーチャンネルペア");

        public static string LabelAutoUvChannel => OutlineLocale.Pick(
            "UV 通道 (TEXCOORDn)", "UV channel (TEXCOORDn)", "UV チャンネル (TEXCOORDn)");

        public static string AutoTangentOverwriteWarning => OutlineLocale.Pick(
            "切线通道会覆盖网格原始切线，法线贴图将失效。",
            "The tangent channel overwrites the mesh's original tangent, breaking normal maps.",
            "接線チャンネルはメッシュの元の接線を上書きするため、ノーマルマップが" +
            "機能しなくなります。");

        public static string LabelAutoStorageSpaceTooltip => OutlineLocale.Pick(
            "对象空间：解码即用，仅适用于静态模型。\n" +
            "切线空间：存相对顶点 TBN 的坐标，蒙皮模型也正确，需要网格有合法切线。",

            "Object space: decode and use directly; static meshes only.\n" +
            "Tangent space: stores coordinates relative to the vertex TBN, correct on skinned " +
            "meshes too, and requires the mesh to have valid tangents.",

            "オブジェクト空間：デコードしてそのまま使えます。静的メッシュ専用です。\n" +
            "接線空間：頂点の TBN を基準とした座標を格納します。スキンメッシュでも正しく、" +
            "メッシュに正しい接線が必要です。");

        public static string AutoTangentSpaceInfo => OutlineLocale.Pick(
            "切线空间：顶点色 / TEXCOORD 不参与蒙皮，存切线空间坐标才能让 " +
            "SkinnedMeshRenderer 的描边跟随骨骼动画。要求模型导入设置的 Tangents ≠ None。\n" +
            "烘焙在导入管线内进行，用的正是本次导入生成的切线，因此不会失配 —— " +
            "这也是切线空间存储推荐走自动烘焙的原因。\n" +
            "材质的「存储空间」需同步选为切线空间。",

            "Tangent space: vertex color / TEXCOORD are not skinned, so only tangent-space " +
            "coordinates let a SkinnedMeshRenderer's outline follow the skeleton. " +
            "Requires Tangents ≠ None in the model import settings.\n" +
            "Baking happens inside the import pipeline, using exactly the tangents this import " +
            "produced, so the two can never drift apart — that is why tangent-space storage is " +
            "recommended to go through auto-bake.\n" +
            "The material's \"Smooth Normal Space\" must be set to tangent space as well.",

            "接線空間：頂点カラー / TEXCOORD はスキニングされないため、接線空間の座標で" +
            "保存して初めて SkinnedMeshRenderer のアウトラインがボーンアニメーションへ" +
            "追従します。モデルのインポート設定で Tangents ≠ None が必要です。\n" +
            "ベイクはインポートパイプライン内で、まさにそのインポートが生成した接線を使って" +
            "行われるため、食い違いが起きません —— 接線空間での保存に自動ベイクを推奨するのは" +
            "このためです。\n" +
            "マテリアル側の「Smooth Normal Space」も接線空間に合わせる必要があります。");

        public static string AutoObjectSpaceWarning => OutlineLocale.Pick(
            "对象空间仅适用于静态模型：顶点色 / TEXCOORD 不参与蒙皮，" +
            "蒙皮模型的外扩方向会停在绑定姿势，动画一跑描边就撕开。",

            "Object space is for static meshes only: vertex color / TEXCOORD are not skinned, " +
            "so on a skinned mesh the extrusion direction stays frozen in the bind pose and the " +
            "outline tears apart as soon as the animation plays.",

            "オブジェクト空間は静的メッシュ専用です：頂点カラー / TEXCOORD はスキニング" +
            "されないため、スキンメッシュでは押し出し方向がバインドポーズのまま止まり、" +
            "アニメーションを再生した途端にアウトラインが裂けます。");

        public static string LabelAutoMergeToleranceTooltip => OutlineLocale.Pick(
            "位置距离在此范围内的顶点视为同一点；必须远小于模型最小特征尺寸。",
            "Vertices within this distance are treated as one point; it must be far smaller " +
            "than the model's smallest feature.",
            "この距離以内の頂点を同一点とみなします。モデルの最小特徴サイズより" +
            "十分小さくする必要があります。");

        public static string AutoSettingsPathNote => OutlineLocale.Pick(
            "配置保存于 ProjectSettings/OutlineSmoothNormals.asset（随工程纳入版本管理）",
            "Settings are stored in ProjectSettings/OutlineSmoothNormals.asset " +
            "(version-controlled with the project)",
            "設定は ProjectSettings/OutlineSmoothNormals.asset に保存されます" +
            "（プロジェクトと一緒にバージョン管理されます）");
    }
}
