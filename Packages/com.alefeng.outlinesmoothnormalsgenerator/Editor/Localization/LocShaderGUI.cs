namespace OutlineSmoothNormalsGenerator
{
    /// <summary>
    /// 描边材质自定义 Inspector（<see cref="OutlineShaderGUI"/>）的文案。
    ///
    /// 【材质属性名的处理规则】
    /// ShaderGUI 会用自己的 label 覆盖掉 Shader 里声明的显示名，所以这里选什么就显示什么。
    /// 规则分两档：
    ///
    ///   · 三份 README 里【按英文名指名道姓】的属性 —— 英文与日文一律保持 Shader 声明的
    ///     英文原名，不翻译。文档写「**Smooth Normal Source** で、生成時と同じ保存チャンネル
    ///     を選びます」，界面上就得有一个字面写着 Smooth Normal Source 的字段，否则用户
    ///     照着文档根本找不到。涉及：
    ///       Smooth Normal Source / Vertex Color Channel / Smooth Normal Space / Base Color Mode
    ///
    ///   · 其余属性 —— 英文取 Shader 声明的原名（与 Shader 源码、材质的默认显示一致），
    ///     日文用日语译名。这些属性文档里只作描述性提及，不存在「照着字面找」的问题。
    ///
    /// Shader 声明见 Samples~/URP/Outline.shader 的 Properties 块：
    ///   _BaseColor("Base Color") _MainTex("Albedo") _BaseColorMode("Base Color Mode")
    ///   _ShadeColor("Shade Tint") _ShadeThreshold("Shade Threshold") _ShadeSoftness("Shade Softness")
    ///   _RimColor("Rim Color") _RimPower("Rim Power")
    ///   _OutlineColor("Outline Color") _OutlineWidth("Outline Width") _OutlineWidthMode("Outline Width Mode")
    ///   _SmoothNormalSrc("Smooth Normal Source") _VCChannel("Vertex Color Channel")
    ///   _SmoothNormalSpace("Smooth Normal Space")
    ///
    /// 两个下拉的【选项】数组（BaseColorModeOptions / SmoothNormalSrcOptions）不在这里 ——
    /// 它们与 Shader 属性的浮点取值一一对应，翻译等于切断与文档的对应关系，保持英文。
    /// </summary>
    internal static class LocShaderGUI
    {
        // ═══════════════════════════════════════════════════════════════
        //  分区标题
        // ═══════════════════════════════════════════════════════════════
        public static string HeaderBase => OutlineLocale.Pick(
            "基础设置", "Base", "基本設定");

        public static string HeaderNpr => OutlineLocale.Pick(
            "NPR 明暗", "NPR Shading", "NPR シェーディング");

        public static string HeaderOutline => OutlineLocale.Pick(
            "描边设置", "Outline", "アウトライン設定");

        // 分区名不能与其下的属性名 Smooth Normal Source 撞字，否则同屏出现两行一样的字。
        public static string HeaderSmoothNormal => OutlineLocale.Pick(
            "平滑法线来源", "Smooth Normals", "スムース法線のソース");

        // ═══════════════════════════════════════════════════════════════
        //  属性标签
        // ═══════════════════════════════════════════════════════════════
        public static string PropBaseColor => OutlineLocale.Pick(
            "基础颜色", "Base Color", "ベースカラー");

        public static string PropMainTex => OutlineLocale.Pick(
            "贴图", "Albedo", "アルベド");

        // 以下四个：README 三语都按英文名引用，英日一律保持英文原名。
        public static string PropBaseColorMode => OutlineLocale.Pick(
            "基础色模式", "Base Color Mode", "Base Color Mode");

        public static string PropSmoothNormalSrc => OutlineLocale.Pick(
            "存储通道", "Smooth Normal Source", "Smooth Normal Source");

        public static string PropVcChannel => OutlineLocale.Pick(
            "顶点色通道对", "Vertex Color Channel", "Vertex Color Channel");

        public static string PropSmoothNormalSpace => OutlineLocale.Pick(
            "存储空间", "Smooth Normal Space", "Smooth Normal Space");

        public static string PropShadeColor => OutlineLocale.Pick(
            "暗部色调", "Shade Tint", "シェードティント");

        public static string PropShadeThreshold => OutlineLocale.Pick(
            "明暗阈值", "Shade Threshold", "シェードしきい値");

        public static string PropShadeSoftness => OutlineLocale.Pick(
            "明暗过渡", "Shade Softness", "シェードソフトネス");

        public static string PropRimColor => OutlineLocale.Pick(
            "边缘光颜色", "Rim Color", "リムカラー");

        public static string PropRimPower => OutlineLocale.Pick(
            "边缘光范围", "Rim Power", "リムパワー");

        public static string PropOutlineColor => OutlineLocale.Pick(
            "描边颜色", "Outline Color", "アウトライン色");

        public static string PropOutlineWidth => OutlineLocale.Pick(
            "描边宽度", "Outline Width", "アウトライン幅");

        public static string PropOutlineWidthMode => OutlineLocale.Pick(
            "宽度模式", "Outline Width Mode", "幅モード");

        // ═══════════════════════════════════════════════════════════════
        //  说明与告警
        // ═══════════════════════════════════════════════════════════════
        public static string DebugModeHelp => OutlineLocale.Pick(
            "调试模式：把平滑法线数据直接当颜色显示（不经光照）。" +
            "切线为 [-1,1]→[0,1]，UV 取 xy 作 RG、B=0；" +
            "顶点色 RG/GB/BA 只显示对应通道对（另一通道置 0，BA 的 A 借 R）。生产时请切回 Base Map。",

            "Debug mode: shows the smooth normal data straight as color, with no lighting. " +
            "The tangent is remapped [-1,1]→[0,1]; a UV takes xy as RG with B=0; " +
            "vertex color RG/GB/BA shows only that channel pair (the third is set to 0, and " +
            "BA borrows R for A). Switch back to Base Map for production.",

            "デバッグモード：スムース法線データをライティングなしでそのまま色として表示します。" +
            "接線は [-1,1]→[0,1] に再マップ、UV は xy を RG として B=0、" +
            "頂点カラー RG/GB/BA は該当するチャンネルペアのみ表示します" +
            "（残り 1 つは 0、BA は A に R を流用）。製品時は Base Map に戻してください。");

        public static string MissingSpacePropWarning => OutlineLocale.Pick(
            "当前 Shader 没有 _SmoothNormalSpace 属性，描边将一律按【对象空间】解码。\n" +
            "若这是自定义 Shader，请从示例 Shader 把这三处一并复制过去：Properties 块里的 " +
            "_SmoothNormalSpace、CBUFFER/uniform 声明、以及 OUTLINE Pass 里的 " +
            "OSN_ResolveSmoothNormalSpace 调用。否则按切线空间烘焙的数据无法正确解码。",

            "This shader has no _SmoothNormalSpace property, so the outline is always decoded " +
            "as [object space].\n" +
            "If this is a custom shader, copy all three pieces over from the sample shader: " +
            "_SmoothNormalSpace in the Properties block, the CBUFFER/uniform declaration, and " +
            "the OSN_ResolveSmoothNormalSpace call inside the OUTLINE pass. Otherwise data " +
            "baked in tangent space cannot be decoded correctly.",

            "このシェーダーには _SmoothNormalSpace プロパティがないため、アウトラインは常に" +
            "【オブジェクト空間】としてデコードされます。\n" +
            "カスタムシェーダーの場合は、サンプルシェーダーから次の 3 か所をまとめてコピー" +
            "してください：Properties ブロックの _SmoothNormalSpace、CBUFFER/uniform 宣言、" +
            "そして OUTLINE パス内の OSN_ResolveSmoothNormalSpace 呼び出し。" +
            "さもないと接線空間でベイクしたデータを正しくデコードできません。");

        public static string SpaceNotApplicable => OutlineLocale.Pick(
            "该存储通道恒为对象空间，「存储空间」不适用（Shader 会忽略此项）。",
            "This storage channel is always object space, so Smooth Normal Space does not " +
            "apply (the shader ignores it).",
            "この保存チャンネルは常にオブジェクト空間のため、Smooth Normal Space は" +
            "適用されません（シェーダーはこの項目を無視します）。");

        // ── 各存储通道档的说明 ────────────────────────────────────────
        // 顶点色档与工具窗口的 LocWindow.VcModeHelp 说的是同一件事，口径必须一致：
        // 存的是【八面体编码的两个参数】，不是法线的原始 XY。
        public static string SrcHintVertexColor => OutlineLocale.Pick(
            "读取顶点色中选定通道对的两个分量（八面体编码），解码还原为完整方向。" +
            "请与生成时选择的通道对保持一致。",

            "Reads the two components of the selected vertex color channel pair (octahedral) " +
            "and decodes them back to a full direction. Keep it the same as the pair you " +
            "picked when generating.",

            "選んだ頂点カラーチャンネルペアの 2 成分（八面体エンコード）を読み取り、" +
            "完全な方向へデコードします。生成時に選んだペアと一致させてください。");

        public static string SrcHintTangentChannel => OutlineLocale.Pick(
            "读取 tangent.xyz 中存储的平滑法线。注意：该模式会覆盖网格原始切线，法线贴图将失效。\n" +
            "该模式恒为对象空间，且无需切线空间 —— Unity 会把 tangent.xyz 当方向一起蒙皮，" +
            "存进去的方向天然跟随骨骼动画。",

            "Reads the smooth normal stored in tangent.xyz. Note: this mode overwrites the " +
            "mesh's original tangent, which breaks normal maps.\n" +
            "It is always object space and does not need tangent space — Unity skins " +
            "tangent.xyz as a direction, so what you store follows the skeleton automatically.",

            "tangent.xyz に格納されたスムース法線を読み取ります。注意：このモードはメッシュの" +
            "元の接線を上書きするため、ノーマルマップが機能しなくなります。\n" +
            "常にオブジェクト空間で、接線空間は不要です —— Unity は tangent.xyz を方向として" +
            "スキニングするため、格納した方向は自然にボーンアニメーションへ追従します。");

        public static string SrcHintVertexNormal => OutlineLocale.Pick(
            "不使用平滑法线，直接沿原始顶点法线外扩 —— 即「未使用本工具」的对照效果，" +
            "硬边处描边会断裂。",

            "Does not use smooth normals at all; extrudes along the original vertex normal — " +
            "the \"without this tool\" reference, where the outline breaks at hard edges.",

            "スムース法線を使わず、元の頂点法線に沿って押し出します —— 「本ツールを使わない」" +
            "場合の比較用で、ハードエッジでアウトラインが途切れます。");

        public static string SrcHintUnknown => OutlineLocale.Pick(
            "未知模式。", "Unknown mode.", "不明なモードです。");

        /// <summary>
        /// 8 个 TEXCOORD 档的说明 —— 统一在一处生成，避免像 1.7.0 之前那样抄成 8 份、
        /// 其中 4 份还把「xy」写成了「xyz」。
        ///
        /// 末尾那句迁移提示是刻意固定挂着的：材质面板是描边出问题时最先被打开的地方，
        /// 而 1.6.x 及更早的旧数据在 GPU 侧无从检测，这里是唯一能提醒到人的位置。
        /// </summary>
        public static string TexCoordHint(int texCoordIndex, string meshProperty)
            => OutlineLocale.Fmt(
                "读取 TEXCOORD{0}（即 {1}）的 xy，八面体编码。\n" +
                "⚠ 自 1.7.0 起该通道为两分量八面体；1.6.x 及更早烘焙的三分量数据无法解码，" +
                "必须重新烘焙。",

                "Reads the xy of TEXCOORD{0} ({1}), octahedral-encoded.\n" +
                "⚠ Since 1.7.0 this channel holds a two-component octahedral encoding; " +
                "three-component data baked by 1.6.x or earlier cannot be decoded and must be " +
                "re-baked.",

                "TEXCOORD{0}（{1}）の xy を読み取ります（八面体エンコード）。\n" +
                "⚠ 1.7.0 以降、このチャンネルは 2 成分の八面体エンコードです。" +
                "1.6.x 以前でベイクした 3 成分データはデコードできないため、" +
                "ベイクし直す必要があります。", texCoordIndex, meshProperty);

        /// <summary>TEXCOORD0 档在通用说明之后追加的一句。</summary>
        public static string TexCoord0Extra => OutlineLocale.Pick(
            "\n注意：该通道通常被贴图占用。",
            "\nNote: this channel is normally taken by the texture mapping.",
            "\n注意：このチャンネルは通常テクスチャマッピングに使われています。");

        /// <summary>TEXCOORD0 档说明里 mesh 属性的写法（含「主贴图 UV」这句中文提示）。</summary>
        public static string MeshUv0Name => OutlineLocale.Pick(
            "mesh.uv，主贴图 UV", "mesh.uv, the main texture UV", "mesh.uv、メインテクスチャ UV");

        // ── 存储空间的说明 ────────────────────────────────────────────
        // 这个值必须与生成时的选择一致，且无法从数据本身推断出来 —— 两种空间存的都只是
        // 一条单位方向，选错不会报错，只会让描边整体偏斜。所以把「选错的症状」写清楚。
        public static string SpaceHintTangent => OutlineLocale.Pick(
            "切线空间：用【蒙皮后】的法线与切线重建 TBN 再还原，" +
            "SkinnedMeshRenderer 上描边会正确跟随骨骼动画。要求网格有合法切线。\n" +
            "若模型的平滑法线是按对象空间烘的，选这里会让描边整体偏斜。",

            "Tangent Space: rebuilds the TBN from the [skinned] normal and tangent before " +
            "restoring the direction, so the outline follows bone animation correctly on a " +
            "SkinnedMeshRenderer. Requires the mesh to have valid tangents.\n" +
            "If the model's smooth normals were baked in object space, picking this skews the " +
            "whole outline.",

            "接線空間：【スキニング後】の法線と接線から TBN を再構築してから復元するため、" +
            "SkinnedMeshRenderer でもアウトラインがボーンアニメーションへ正しく追従します。" +
            "メッシュに正しい接線が必要です。\n" +
            "モデルのスムース法線がオブジェクト空間でベイクされている場合、" +
            "ここを選ぶとアウトライン全体が傾きます。");

        public static string SpaceHintObject => OutlineLocale.Pick(
            "对象空间：解码即用，开销最低，但仅静态模型正确。\n" +
            "顶点色 / TEXCOORD 不参与蒙皮，SkinnedMeshRenderer 上外扩方向会停在" +
            "绑定姿势，动画一跑描边就撕开 —— 那种情况请重新按切线空间烘焙并改选切线空间。",

            "Object Space: decode and use directly, the cheapest option, but only correct for " +
            "static meshes.\n" +
            "Vertex color / TEXCOORD are not skinned, so on a SkinnedMeshRenderer the " +
            "extrusion direction stays frozen in the bind pose and the outline tears apart as " +
            "soon as the animation plays — in that case re-bake in tangent space and switch " +
            "this to Tangent Space.",

            "オブジェクト空間：デコードしてそのまま使え、コストは最小ですが、" +
            "正しいのは静的メッシュのみです。\n" +
            "頂点カラー / TEXCOORD はスキニングされないため、SkinnedMeshRenderer では" +
            "押し出し方向がバインドポーズのまま止まり、アニメーションを再生した途端に" +
            "アウトラインが裂けます —— その場合は接線空間でベイクし直し、" +
            "ここも Tangent Space に切り替えてください。");
    }
}
