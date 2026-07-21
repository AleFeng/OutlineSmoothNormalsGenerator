using UnityEditor;
using UnityEngine;

namespace OutlineSmoothNormalsGenerator
{
    /// <summary>
    /// 本包界面的绘制基元 —— 分段按钮、动作按钮，以及小节标题 / 状态行 / 标签这类
    /// 到处都在画的小控件。
    ///
    /// ── 为什么要有这个文件 ────────────────────────────────────────────────
    /// 「高亮分段按钮」此前有【五份】各自独立的实现：顶部页签、存储方式、存储空间、
    /// 顶点色通道对、语言切换。五份的视觉规则完全相同（选中 = 强调底 + 深色粗体字，
    /// 未选中 = 卡片底 + 灰字，hover 提亮一档），只有字号 / 内边距 / 高度不同 ——
    /// 也就是说改一次配色要改五处，且它们已经开始互相漂移（顶点色那一份的未选中
    /// 底色比另外四份浅一档）。「高亮动作按钮」同理，有三份（保存 / 另存为 / 生成）。
    ///
    /// 现在规则只写一遍，差异作为参数传入。
    ///
    /// ── 共享 GUIStyle 的纪律 ──────────────────────────────────────────────
    /// 下面每个按钮家族只持有【一个】GUIStyle 实例，每次调用前把该家族会用到的
    /// 属性【全部】重新赋值一遍。IMGUI 是立即模式，Button 在调用那一刻就把 style
    /// 读走了，所以这样做是安全的（详见 OutlineEditorStyles 的类头注释）。
    ///
    /// ⚠ 「全部重新赋值」不是可选项。少赋一个，上一次调用留下的值就会漏给下一次 ——
    ///   比如某个调用点设了 fixedHeight 而别的没设，那么它之后的所有同族按钮都会
    ///   被钉死成那个高度。所以本文件里高度一律走 GUILayout.Height / fixedHeight
    ///   二选一，且家族内保持一致，不混用。
    /// </summary>
    public static class OutlineEditorGUI
    {
        // ═══════════════════════════════════════════════════════════════
        //  分段按钮
        // ═══════════════════════════════════════════════════════════════

        #region 分段按钮

        private static GUIStyle _segment;

        /// <summary>GUI.skin.button 的原始内边距 —— 不指定 padding 的调用点要还原成它。</summary>
        private static RectOffset _segmentDefaultPadding;

        private static readonly RectOffset PaddingBlock    = new RectOffset(6, 6, 6, 6);
        private static readonly RectOffset PaddingLanguage = new RectOffset(4, 4, 2, 2);

        /// <summary>
        /// 分段按钮的统一实现。选中态 = 强调底 + 深色粗体字，未选中态 = 卡片底 + 灰字，
        /// hover 一律把底色提亮到 1.1 倍。
        /// </summary>
        /// <param name="padding">null 表示用 GUI.skin.button 的默认内边距。</param>
        private static bool DrawSegment(GUIContent content, bool active, int fontSize,
                                        RectOffset padding, bool wordWrap,
                                        params GUILayoutOption[] options)
        {
            if (_segment == null)
            {
                _segment = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleCenter };

                // ⚠ 必须【拷值】而不是留引用。GUIStyle.padding 返回的 RectOffset 是原生
                //   样式内存的活视图，不是快照 —— 直接存下这个引用的话，下面第一次
                //   `_segment.padding = PaddingBlock` 就会把「默认值」本身改成 (6,6,6,6)，
                //   之后所有不指定 padding 的调用点（页签、RG/GB/BA）都会跟着变形。
                var p = _segment.padding;
                _segmentDefaultPadding = new RectOffset(p.left, p.right, p.top, p.bottom);
            }

            var bg = active ? OutlineEditorStyles.Accent   : OutlineEditorStyles.Card;
            var fg = active ? OutlineEditorStyles.OnAccent : OutlineEditorStyles.TextDim;

            _segment.fontSize          = fontSize;
            _segment.fontStyle         = active ? FontStyle.Bold : FontStyle.Normal;
            _segment.padding           = padding ?? _segmentDefaultPadding;
            _segment.wordWrap          = wordWrap;
            _segment.normal.textColor  = fg;
            _segment.normal.background = OutlineEditorStyles.SolidTex(bg);
            _segment.hover.textColor   = fg;
            _segment.hover.background  = OutlineEditorStyles.SolidTex(bg * 1.1f);

            return GUILayout.Button(content, _segment, options);
        }

        /// <summary>窗口顶部页签：最大的一档，平分整行宽度。</summary>
        public static bool DrawSegmentTab(string label, bool active, float height)
            => DrawSegment(new GUIContent(label), active, 12, null, false,
                           GUILayout.Height(height), GUILayout.ExpandWidth(true));

        /// <summary>
        /// 存储方式 / 存储空间的方块选择。允许换行 —— 英文与日文的选项名比中文长得多，
        /// 不换行会在左栏拖窄时被裁掉。
        /// </summary>
        public static bool DrawSegmentBlock(GUIContent content, bool active, float height)
            => DrawSegment(content, active, 10, PaddingBlock, true, GUILayout.Height(height));

        /// <summary>顶点色通道对（RG / GB / BA）这类短标签的小方块。</summary>
        public static bool DrawSegmentChip(string label, bool active, float height)
            => DrawSegment(new GUIContent(label), active, 11, null, false, GUILayout.Height(height));

        /// <summary>语言切换的定宽小条。</summary>
        public static bool DrawSegmentLanguage(string label, bool active, float width, float height)
            => DrawSegment(new GUIContent(label), active, 10, PaddingLanguage, false,
                           GUILayout.Width(width), GUILayout.Height(height));

        #endregion

        // ═══════════════════════════════════════════════════════════════
        //  动作按钮
        // ═══════════════════════════════════════════════════════════════

        #region 动作按钮

        private static GUIStyle _accentButton;

        /// <summary>
        /// 带彩色底板的动作按钮（保存 / 另存为 / 生成）。hover 一律把底色提亮到 1.12 倍，
        /// 且 hover 时字色转深 —— 底色提亮后浅色字会糊掉。
        /// </summary>
        /// <param name="background">底色。禁用态由调用方自行传灰色，本方法不做判断。</param>
        /// <param name="textColor">常态字色。亮底上传 OnAccent，暗底上传浅色。</param>
        public static bool DrawAccentButton(GUIContent content, Color background, Color textColor,
                                            int fontSize, float height, bool bold,
                                            bool wordWrap = false)
        {
            _accentButton ??= new GUIStyle(GUI.skin.button);

            _accentButton.fontSize          = fontSize;
            _accentButton.fontStyle         = bold ? FontStyle.Bold : FontStyle.Normal;
            _accentButton.fixedHeight       = height;
            _accentButton.wordWrap          = wordWrap;
            _accentButton.normal.textColor  = textColor;
            _accentButton.normal.background = OutlineEditorStyles.SolidTex(background);
            _accentButton.hover.textColor   = OutlineEditorStyles.OnAccent;
            _accentButton.hover.background  = OutlineEditorStyles.SolidTex(background * 1.12f);

            return GUILayout.Button(content, _accentButton);
        }

        /// <summary>「清除通道」类小按钮：可用时偏红，禁用时转灰。</summary>
        public static void DrawClearChannelButton(string label, bool enabled, System.Action onClick)
        {
            GUI.enabled = enabled;
            if (GUILayout.Button(label, OutlineEditorStyles.ClearButton(enabled)))
                onClick?.Invoke();
            GUI.enabled = true;
        }

        #endregion

        // ═══════════════════════════════════════════════════════════════
        //  标题
        // ═══════════════════════════════════════════════════════════════

        #region 标题

        /// <summary>小节标题，如「◈  存储方式」。</summary>
        public static void DrawSectionHeader(string titleName, string icon)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            GUILayout.Label($"{icon}  {titleName}", OutlineEditorStyles.SectionHeader);
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>可折叠的小节标题。返回折叠后的新状态。</summary>
        public static bool DrawFoldout(bool state, string titleName, string icon)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            bool result = EditorGUILayout.Foldout(state, $"{icon}  {titleName}", true,
                                                  OutlineEditorStyles.Foldout);
            EditorGUILayout.EndHorizontal();
            return result;
        }

        /// <summary>预览参数栏的分组标题。</summary>
        public static void DrawParamHeader(string headerText)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(2);
            GUILayout.Label(headerText, OutlineEditorStyles.ParamHeader);
            EditorGUILayout.EndHorizontal();
        }

        #endregion

        // ═══════════════════════════════════════════════════════════════
        //  信息与状态
        // ═══════════════════════════════════════════════════════════════

        #region 信息与状态

        /// <summary>「网格信息」里的一行「名称 …… 取值」，行尾带一条细分割线。</summary>
        public static void DrawInfoRow(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(label, OutlineEditorStyles.InfoRowLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label(value, OutlineEditorStyles.InfoRowValue, GUILayout.Width(120));
            EditorGUILayout.EndHorizontal();

            // 细分割线
            var line = OutlineEditorStyles.Border;
            line.a = 0.6f;
            EditorGUI.DrawRect(GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true)), line);
        }

        /// <summary>状态行：圆点 + 通道名 + 说明。<paramref name="active"/> 决定绿 / 灰。</summary>
        public static void DrawStatusIndicator(string label, string desc, bool active)
            => DrawStatusIndicator(label, desc,
                                   active ? OutlineEditorStyles.Success : OutlineEditorStyles.Gray);

        /// <summary>状态行：圆点与说明文字同色。</summary>
        public static void DrawStatusIndicator(string label, string desc, Color dotColor)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("●", OutlineEditorStyles.Dot(dotColor), GUILayout.Width(18));
            GUILayout.Label(label, OutlineEditorStyles.IndicatorLabel, GUILayout.Width(100));
            GUILayout.Label(desc, OutlineEditorStyles.IndicatorDesc(dotColor));
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>目标来源标签（「场景对象」「Mesh 资产」这类小色块）。</summary>
        public static void DrawTag(string text, Color background)
            => GUILayout.Label(text, OutlineEditorStyles.Tag(background));

        /// <summary>数据状态卡里的一个通道 chip：上方通道名，下方一行注释。</summary>
        public static void DrawChannelChip(string label, string note, bool active, Color accentColor)
        {
            var chipBg = active
                ? new Color(accentColor.r * 0.2f, accentColor.g * 0.2f, accentColor.b * 0.2f, 0.8f)
                : new Color(0.12f, 0.13f, 0.16f);

            EditorGUILayout.BeginVertical(OutlineEditorStyles.ChipBox(chipBg), GUILayout.Width(80));
            GUILayout.Label(label, OutlineEditorStyles.ChipLabel(
                active ? accentColor : new Color(0.5f, 0.5f, 0.6f)));
            GUILayout.Label(note, OutlineEditorStyles.ChipNote);
            EditorGUILayout.EndVertical();
        }

        #endregion

        // ═══════════════════════════════════════════════════════════════
        //  图形
        // ═══════════════════════════════════════════════════════════════

        #region 图形

        /// <summary>页签头部左上角的六边形标志：描边六边形 + 中心实心点。</summary>
        public static void DrawHexIcon(Rect r, Color c)
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
    }
}
