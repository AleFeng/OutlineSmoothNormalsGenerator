namespace OutlineSmoothNormalsGenerator
{
    /// <summary>
    /// 网格健康检查（<see cref="OutlineMeshValidator"/>）的条目文案。
    ///
    /// 这些字符串会被【拼进 MeshHealthReport 并缓存】，同时出现在两个地方：
    /// 生成区顶部的健康检查卡片，以及自动烘焙跳过 / 告警时的 Console 日志。
    ///
    /// ⚠ 因为报告存的是已成文的句子，切换语言后【不重算就不会变】。生成器窗口在
    /// OnLocaleChanged 里调 RefreshHealthReport() 正是为此 —— 本包唯一需要主动
    /// 失效的缓存。改动这里的任何成员时，别把那条链路弄断。
    ///
    /// 术语与三份 README 的「网格健康检查 / Mesh Health Check / メッシュ健全性チェック」
    /// 一节保持一致。
    /// </summary>
    internal static class LocValidator
    {
        // ── 报告级 ────────────────────────────────────────────────────
        public static string NoIssues => OutlineLocale.Pick("无异常", "No issues", "異常なし");

        public static string TagError   => OutlineLocale.Pick("错误", "Error", "エラー");
        public static string TagWarning => OutlineLocale.Pick("警告", "Warning", "警告");
        public static string TagInfo    => OutlineLocale.Pick("提示", "Info", "情報");

        /// <summary>多条问题拼成一行时的分隔符。中文用全角分号，英文用分号加空格。</summary>
        public static string IssueSeparator => OutlineLocale.Pick("；", "; ", "；");

        // ── 网格本身 ──────────────────────────────────────────────────
        public static string MeshNull => OutlineLocale.Pick(
            "网格为空（null）。", "The mesh is null.", "メッシュが null です。");

        public static string MeshNoVertices(string mesh) => OutlineLocale.Fmt(
            "网格「{0}」没有顶点。",
            "The mesh \"{0}\" has no vertices.",
            "メッシュ「{0}」に頂点がありません。", mesh);

        public static string NotReadable => OutlineLocale.Pick(
            "网格未开启 Read/Write：编辑器内通常仍可处理，但缺失数据时无法自动重算，" +
            "建议在模型导入设置中开启。",

            "The mesh has Read/Write disabled: the editor can usually still process it, but " +
            "missing data cannot be recomputed automatically — consider enabling it in the " +
            "model import settings.",

            "メッシュの Read/Write が無効です：エディター内では通常そのまま処理できますが、" +
            "データが欠けている場合に自動で再計算できません。モデルのインポート設定で" +
            "有効化することを推奨します。");

        public static string NonFiniteVertices(int count) => OutlineLocale.Fmt(
            "有 {0} 个顶点坐标为 NaN / Inf。",
            "{0} vertex positions are NaN / Inf.",
            "{0} 個の頂点座標が NaN / Inf です。", count);

        // ── 法线 ──────────────────────────────────────────────────────
        public static string NoNormals => OutlineLocale.Pick(
            "缺少顶点法线：生成时会自动重算，结果可能不如导入法线精确。",
            "No vertex normals: they are recomputed during generation, which may be less " +
            "accurate than the imported normals.",
            "頂点法線がありません：生成時に自動で再計算しますが、インポートされた法線ほど" +
            "正確でない場合があります。");

        public static string BadNormals(int count) => OutlineLocale.Fmt(
            "有 {0} 条顶点法线为零向量或 NaN。",
            "{0} vertex normals are zero vectors or NaN.",
            "{0} 本の頂点法線がゼロベクトルまたは NaN です。", count);

        // ── 切线 ──────────────────────────────────────────────────────
        public static string NoTangentsInfo => OutlineLocale.Pick(
            "网格无切线：写入切线通道时会新建切线数据。",
            "The mesh has no tangents: writing to the tangent channel creates new tangent data.",
            "メッシュに接線がありません：接線チャンネルへの書き込み時に接線データを新規作成します。");

        public static string TangentSpaceNeedsNormals => OutlineLocale.Pick(
            "切线空间存储需要顶点法线作为重建基，但网格缺少法线。" +
            "请在模型导入设置中开启法线导入 / 计算，或改用对象空间存储。",

            "Tangent-space storage needs vertex normals to rebuild the basis, but the mesh has " +
            "none. Enable normal import / calculation in the model import settings, or switch " +
            "to object-space storage.",

            "接線空間での保存には基底の再構築に頂点法線が必要ですが、メッシュにありません。" +
            "モデルのインポート設定で法線のインポート / 計算を有効にするか、" +
            "オブジェクト空間での保存に切り替えてください。");

        public static string TangentSpaceNeedsTangents => OutlineLocale.Pick(
            "切线空间存储需要切线作为重建基，但网格缺少切线。" +
            "请在模型导入设置中把 Tangents 设为 Calculate 或 Import，或改用对象空间存储。",

            "Tangent-space storage needs tangents to rebuild the basis, but the mesh has none. " +
            "Set Tangents to Calculate or Import in the model import settings, or switch to " +
            "object-space storage.",

            "接線空間での保存には基底の再構築に接線が必要ですが、メッシュにありません。" +
            "モデルのインポート設定で Tangents を Calculate または Import にするか、" +
            "オブジェクト空間での保存に切り替えてください。");

        public static string AllTangentsDegenerate(int total) => OutlineLocale.Fmt(
            "全部 {0} 条切线都无法构成正交基（零向量 / 与法线共线 / 手性为 0），" +
            "切线空间存储不可用，请检查模型 UV 或改用对象空间存储。",

            "All {0} tangents fail to form an orthogonal basis (zero vector / collinear with " +
            "the normal / zero handedness). Tangent-space storage is unusable — check the " +
            "model's UVs or switch to object-space storage.",

            "{0} 本すべての接線が正規直交基底を構成できません（ゼロベクトル / 法線と平行 / " +
            "ハンドネスが 0）。接線空間での保存は使用できません。モデルの UV を確認するか、" +
            "オブジェクト空間での保存に切り替えてください。", total);

        // 三种成因的【后果不同】，不能一句话概括：
        //   零向量 / 与法线共线 —— TryBuildBasis 返回 false，编解码两侧都退回顶点法线；
        //   手性异常          —— TryBuildBasis 只看 Gram-Schmidt 残量，不检查 tangent.w，
        //                        基照样「构造成功」，只是副切线 b = cross(n,t) * w 塌成零，
        //                        结果是丢掉 Y 分量后的一个【错误方向】，而非顶点法线。
        public static string SomeTangentsDegenerate(int bad, int total) => OutlineLocale.Fmt(
            "有 {0}/{1} 条切线无法构成正交基（零向量 / 与法线共线 / 手性异常），多因 UV 退化所致。" +
            "前两种情形下这些顶点的描边会退化为沿原始顶点法线外扩；手性异常的顶点则会解出错误方向。",

            "{0} of {1} tangents fail to form an orthogonal basis (zero vector / collinear with " +
            "the normal / bad handedness), usually because of degenerate UVs. In the first two " +
            "cases the outline at those vertices falls back to extruding along the original " +
            "vertex normal; vertices with bad handedness decode to a wrong direction instead.",

            "{1} 本中 {0} 本の接線が正規直交基底を構成できません（ゼロベクトル / 法線と平行 / " +
            "ハンドネスの異常）。多くは UV の退化が原因です。前の 2 つの場合、これらの頂点の" +
            "アウトラインは元の頂点法線に沿った押し出しにフォールバックします。" +
            "ハンドネスが異常な頂点は、代わりに誤った方向にデコードされます。", bad, total);

        // ── 拓扑 ──────────────────────────────────────────────────────
        public static string DegenerateTriangles(int bad, int total) => OutlineLocale.Fmt(
            "有 {0}/{1} 个退化（零面积 / 共线）三角，这些面不参与平滑法线计算。",
            "{0} of {1} triangles are degenerate (zero area / collinear); they do not " +
            "contribute to the smooth normal calculation.",
            "{1} 個中 {0} 個の三角形が退化しています（面積ゼロ / 共線）。" +
            "これらの面はスムース法線の計算に寄与しません。", bad, total);

        public static string TooManyCoincident(int count) => OutlineLocale.Fmt(
            "存在单个位置重合多达 {0} 个顶点，可能是网格异常或合并容差设置不当。",
            "Up to {0} vertices coincide at a single position — the mesh may be malformed, or " +
            "the merge tolerance may be set wrong.",
            "1 つの位置に最大 {0} 個の頂点が重なっています。メッシュの異常か、" +
            "結合トレランスの設定が不適切な可能性があります。", count);
    }
}
