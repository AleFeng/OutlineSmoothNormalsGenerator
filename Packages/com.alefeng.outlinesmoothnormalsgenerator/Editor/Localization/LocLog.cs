namespace OutlineSmoothNormalsGenerator
{
    /// <summary>
    /// 非窗口代码（导入自动烘焙、存储写入、平滑法线计算）打到 Console 的文案。
    ///
    /// 【前缀不在这里】三个文件各自的 `[…]` 前缀保持原样，不随本地化改动 ——
    /// 目前它们并不统一（`[OutlineSmoothNormals]` / `[平滑法线]`，窗口那边还是
    /// `[SmoothNormal]`），统一它属于单独的改动，已记入待办，混进翻译提交只会
    /// 让 diff 说不清自己在做什么。
    ///
    /// 【存储格式的描述复用 LocWindow】导入日志里的「存到哪个通道、什么格式」与
    /// 工具窗口生成日志说的是同一件事，共用 LocWindow.LogStorage* 与
    /// LocWindow.ShortSpace*，免得两处各写一份、日后措辞飘掉。
    /// </summary>
    internal static class LocLog
    {
        // ═══════════════════════════════════════════════════════════════
        //  导入自动烘焙（OutlineNormalsImportProcessor）
        // ═══════════════════════════════════════════════════════════════
        public static string SkipMesh(string mesh, string assetPath, string reason)
            => OutlineLocale.Fmt(
                "跳过网格「{0}」（{1}）：{2}",
                "Skipped the mesh \"{0}\" ({1}): {2}",
                "メッシュ「{0}」（{1}）をスキップしました：{2}", mesh, assetPath, reason);

        public static string CalcFailedReason => OutlineLocale.Pick(
            "平滑法线计算失败（缺法线且无法重算）。",
            "the smooth normal calculation failed (no normals, and they cannot be recomputed).",
            "スムース法線の計算に失敗しました（法線がなく、再計算もできません）。");

        public static string BakedWithWarnings(string mesh, string report) => OutlineLocale.Fmt(
            "网格「{0}」已烘焙，但有告警：{1}",
            "The mesh \"{0}\" was baked, but with warnings: {1}",
            "メッシュ「{0}」をベイクしましたが、警告があります：{1}", mesh, report);

        public static string CustomStorageTarget => OutlineLocale.Pick(
            "自定义存储", "custom storage", "カスタム保存");

        public static string AutoBakeDone(string assetPath, int baked, string target)
            => OutlineLocale.Fmt(
                "自动烘焙 {0}：{1} 个网格 → {2}",
                "Auto-baked {0}: {1} mesh(es) → {2}",
                "{0} を自動ベイクしました：{1} 個のメッシュ → {2}", assetPath, baked, target);

        public static string AutoBakeSkippedSuffix(int skipped) => OutlineLocale.Fmt(
            "（跳过 {0} 个）", " ({0} skipped)", "（{0} 個スキップ）", skipped);

        /// <summary>没有跳过任何网格时，日志句末的收尾标点。</summary>
        public static string AutoBakeDoneSuffix => OutlineLocale.Pick("。", ".", "。");

        /// <summary>DescribeTarget 的最终拼装：{0} = 通道与格式，{1} = 存储空间。</summary>
        public static string TargetChannelWithSpace(string channel, string space)
            => OutlineLocale.Fmt("{0}（{1}）", "{0} ({1})", "{0}（{1}）", channel, space);

        // ═══════════════════════════════════════════════════════════════
        //  切线空间转换退回（StorageWriter）
        // ═══════════════════════════════════════════════════════════════
        // 三种缺失组合（缺法线 / 缺切线 / 两者都缺）分别写成整句，不再由代码拼词 ——
        // 原实现是把「法线」「与」「切线」按条件拼起来，那在英日语序下拼不出通顺的句子。
        public static string TangentSpaceFallbackNoNormals(string mesh) => OutlineLocale.Fmt(
            "网格「{0}」缺少法线，无法构造切线空间基，已退回【对象空间】写入。",
            "The mesh \"{0}\" has no normals, so no tangent-space basis could be built; " +
            "it was written in [object space] instead.",
            "メッシュ「{0}」に法線がないため接線空間の基底を構築できず、" +
            "【オブジェクト空間】で書き込みました。", mesh);

        public static string TangentSpaceFallbackNoTangents(string mesh) => OutlineLocale.Fmt(
            "网格「{0}」缺少切线，无法构造切线空间基，已退回【对象空间】写入。",
            "The mesh \"{0}\" has no tangents, so no tangent-space basis could be built; " +
            "it was written in [object space] instead.",
            "メッシュ「{0}」に接線がないため接線空間の基底を構築できず、" +
            "【オブジェクト空間】で書き込みました。", mesh);

        public static string TangentSpaceFallbackNeither(string mesh) => OutlineLocale.Fmt(
            "网格「{0}」缺少法线与切线，无法构造切线空间基，已退回【对象空间】写入。",
            "The mesh \"{0}\" has neither normals nor tangents, so no tangent-space basis " +
            "could be built; it was written in [object space] instead.",
            "メッシュ「{0}」に法線と接線の両方がないため接線空間の基底を構築できず、" +
            "【オブジェクト空間】で書き込みました。", mesh);

        public static string TangentSpaceFallbackAdvice => OutlineLocale.Pick(
            "请在模型导入设置中开启切线生成后重新烘焙，" +
            "或把材质的「存储空间」改为对象空间 —— 否则描边方向会整体偏斜。",

            "Enable tangent generation in the model import settings and re-bake, or switch the " +
            "material's \"Smooth Normal Space\" to object space — otherwise the whole outline " +
            "direction is skewed.",

            "モデルのインポート設定で接線の生成を有効にしてベイクし直すか、マテリアルの " +
            "「Smooth Normal Space」をオブジェクト空間に変更してください —— " +
            "さもないとアウトラインの方向が全体的にずれます。");

        // ═══════════════════════════════════════════════════════════════
        //  平滑法线计算（OutlineSmoothNormalsCalculator）
        // ═══════════════════════════════════════════════════════════════
        public static string NoNormalsNotReadable(string mesh) => OutlineLocale.Fmt(
            "网格「{0}」没有法线数据，且未开启 Read/Write，无法自动重算。" +
            "请在模型导入设置中启用法线导入。",

            "The mesh \"{0}\" has no normal data and Read/Write is disabled, so they cannot be " +
            "recomputed. Enable normal import in the model import settings.",

            "メッシュ「{0}」に法線データがなく、Read/Write も無効なため自動で再計算できません。" +
            "モデルのインポート設定で法線のインポートを有効にしてください。", mesh);

        public static string NoNormalsRecalculated(string mesh) => OutlineLocale.Fmt(
            "网格「{0}」没有法线数据，已自动重算法线。" +
            "如需精确控制，请在模型导入设置中启用法线导入。",

            "The mesh \"{0}\" has no normal data; normals were recomputed automatically. " +
            "For precise control, enable normal import in the model import settings.",

            "メッシュ「{0}」に法線データがないため、法線を自動で再計算しました。" +
            "正確に制御したい場合は、モデルのインポート設定で法線のインポートを" +
            "有効にしてください。", mesh);
    }
}
