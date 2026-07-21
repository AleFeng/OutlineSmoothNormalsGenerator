using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace OutlineSmoothNormalsGenerator
{
    /// <summary>
    /// 本包全部编辑器界面的配色与 GUIStyle 单一来源。
    ///
    /// ── 为什么要有这个文件 ────────────────────────────────────────────────
    /// 此前配色在三个文件里各定义一遍（生成器窗口、OutlineLocale、OutlineShaderGUI），
    /// 改一个强调色要记得改三处；而 GUIStyle 一半在窗口的 InitStyles 里复用、
    /// 另一半散在绘制方法里【每帧新建】。两件事都收敛到这里。
    ///
    /// ── 为什么是静态、且可以跨窗口共享 ────────────────────────────────────
    /// IMGUI 是立即模式：Label / Button 在调用的那一刻就把 style 的内容读走了，
    /// 之后再改这个对象不会影响已经画完的控件。所以「共用一个实例、每次调用前
    /// 只改颜色」是安全的 —— 下面那些 <c>Xxx(Color)</c> 形式的方法正是这么用的。
    /// 同时开两个生成器窗口也没有问题，它们本来就是顺序绘制的。
    ///
    /// ⚠ 样式建好后不会随编辑器主题（Pro / Personal）切换而重建。这与此前
    ///   InitStyles 的 _stylesInitialized 行为一致（也从不失效），不是本次引入的；
    ///   真要支持的话得挂 EditorApplication 的皮肤变更回调，代价大于收益。
    ///
    /// ⚠ 所有属性都依赖 EditorStyles / GUI.skin，只能在 OnGUI 期间访问。
    /// </summary>
    public static class OutlineEditorStyles
    {
        // ═══════════════════════════════════════════════════════════════
        //  配色
        // ═══════════════════════════════════════════════════════════════

        #region 主色板

        /// <summary>强调色：选中态、小节标题、分隔强调线。</summary>
        public static readonly Color Accent = new Color(0.33f, 0.78f, 1f);

        /// <summary>成功 / 命中：状态点、徽标。</summary>
        public static readonly Color Success = new Color(0.35f, 0.85f, 0.47f);

        /// <summary>警告：有数据但非写入目标、会被覆盖。</summary>
        public static readonly Color Warning = new Color(1f, 0.78f, 0.25f);

        /// <summary>危险：旧格式数据、破坏性操作。</summary>
        public static readonly Color Danger = new Color(1f, 0.45f, 0.40f);

        /// <summary>中性灰：空通道、禁用态。</summary>
        public static readonly Color Gray = new Color(0.4f, 0.42f, 0.48f);

        /// <summary>卡片 / 未选中分段按钮的底色。</summary>
        public static readonly Color Card = new Color(0.18f, 0.20f, 0.24f);

        /// <summary>描边与分隔线。</summary>
        public static readonly Color Border = new Color(0.28f, 0.30f, 0.36f);

        #endregion

        #region 文字色

        /// <summary>画在 <see cref="Accent"/> 等亮底上的深色字。</summary>
        public static readonly Color OnAccent = new Color(0.05f, 0.05f, 0.08f);

        /// <summary>未选中分段按钮的字色。</summary>
        public static readonly Color TextDim = new Color(0.65f, 0.70f, 0.78f);

        /// <summary>字段小标题（「存储空间」「通道对」这类一行说明）。</summary>
        public static readonly Color TextCaption = new Color(0.55f, 0.6f, 0.68f);

        /// <summary>卡片描述、列表计数。</summary>
        public static readonly Color TextDesc = new Color(0.6f, 0.65f, 0.72f);

        /// <summary>次要提示：视口操作提示、chip 注释。</summary>
        public static readonly Color TextMuted = new Color(0.5f, 0.55f, 0.62f);

        /// <summary>禁用的动作按钮上的字色，明显暗于常态，一眼看得出点不动。</summary>
        public static readonly Color TextDisabled = new Color(0.55f, 0.58f, 0.62f);

        /// <summary>中性（未高亮）动作按钮上的字色 —— 可点，只是不抢视线。</summary>
        public static readonly Color TextOnNeutral = new Color(0.75f, 0.78f, 0.82f);

        #endregion

        // ═══════════════════════════════════════════════════════════════
        //  纯色纹理
        // ═══════════════════════════════════════════════════════════════

        #region 纯色纹理

        private static readonly Dictionary<Color, Texture2D> TexCache = new Dictionary<Color, Texture2D>();

        /// <summary>
        /// 取一张该颜色的 2×2 纯色纹理，用作按钮 / 标签的背景。带缓存。
        ///
        /// HideAndDontSave 是必需的：否则这些纹理会在每次域重载时触发
        /// 「Texture2D has been leaked」刷屏。
        /// </summary>
        public static Texture2D SolidTex(Color col)
        {
            if (TexCache.TryGetValue(col, out var cached) && cached) return cached;

            var tex = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color[4];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = col;
            tex.SetPixels(pixels);
            tex.Apply();
            TexCache[col] = tex;
            return tex;
        }

        #endregion

        // ═══════════════════════════════════════════════════════════════
        //  样式库 —— 固定外观
        // ═══════════════════════════════════════════════════════════════

        #region 容器

        private static GUIStyle _dataCard;
        /// <summary>一级卡片：小节的外框。</summary>
        public static GUIStyle DataCard => _dataCard ??= new GUIStyle(GUI.skin.box)
        {
            padding = new RectOffset(10, 10, 8, 8),
            margin  = new RectOffset(8, 8, 2, 2),
        };

        private static GUIStyle _innerCard;
        /// <summary>二级卡片：卡片内部再分块时用，内边距更小。</summary>
        public static GUIStyle InnerCard => _innerCard ??= new GUIStyle(GUI.skin.box)
        {
            padding = new RectOffset(8, 8, 6, 6),
        };

        #endregion

        #region 标题

        private static GUIStyle _header;
        /// <summary>页签头部大标题。</summary>
        public static GUIStyle Header => _header ??= new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 14,
            normal   = { textColor = Color.white },
        };

        private static GUIStyle _subHeader;
        /// <summary>页签头部副标题。</summary>
        public static GUIStyle SubHeader => _subHeader ??= new GUIStyle(EditorStyles.miniLabel)
        {
            fontSize = 9,
            normal   = { textColor = new Color(0.5f, 0.6f, 0.7f) },
        };

        private static GUIStyle _sectionHeader;
        /// <summary>小节标题（「◈ 存储方式」这一级）。</summary>
        public static GUIStyle SectionHeader => _sectionHeader ??= new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 11,
            normal   = { textColor = Accent },
        };

        private static GUIStyle _paramHeader;
        /// <summary>预览参数栏的分组标题，比 <see cref="SectionHeader"/> 小一号。</summary>
        public static GUIStyle ParamHeader => _paramHeader ??= new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 10,
            normal   = { textColor = Accent },
        };

        private static GUIStyle _foldout;
        /// <summary>可折叠小节的标题。</summary>
        public static GUIStyle Foldout => _foldout ??= new GUIStyle(EditorStyles.foldout)
        {
            fontSize  = 11,
            fontStyle = FontStyle.Bold,
            normal    = { textColor = Accent },
            onNormal  = { textColor = Accent },
        };

        #endregion

        #region 正文与说明

        private static GUIStyle _cardTitle;
        /// <summary>数据状态卡的标题。</summary>
        public static GUIStyle CardTitle => _cardTitle ??= new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 11,
            normal   = { textColor = Color.white },
        };

        private static GUIStyle _miniDesc;
        /// <summary>卡片描述与列表计数这类灰色小字。</summary>
        public static GUIStyle MiniDesc => _miniDesc ??= new GUIStyle(EditorStyles.miniLabel)
        {
            normal = { textColor = TextDesc },
        };

        private static GUIStyle _fieldCaption;
        /// <summary>字段上方的一行小标题（「存储空间」「通道对」）。</summary>
        public static GUIStyle FieldCaption => _fieldCaption ??= new GUIStyle(EditorStyles.miniLabel)
        {
            normal = { textColor = TextCaption },
        };

        private static GUIStyle _hint;
        /// <summary>预览视口底部的居中操作提示。</summary>
        public static GUIStyle Hint => _hint ??= new GUIStyle(EditorStyles.miniLabel)
        {
            normal    = { textColor = TextMuted },
            alignment = TextAnchor.MiddleCenter,
        };

        private static GUIStyle _viewportEmpty;
        /// <summary>预览视口无内容时的居中占位文字。</summary>
        public static GUIStyle ViewportEmpty => _viewportEmpty ??= new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal    = { textColor = new Color(0.4f, 0.45f, 0.5f) },
        };

        #endregion

        #region 信息行

        private static GUIStyle _infoRowLabel;
        /// <summary>「网格信息」左侧的字段名。</summary>
        public static GUIStyle InfoRowLabel => _infoRowLabel ??= new GUIStyle(EditorStyles.miniLabel)
        {
            normal = { textColor = Color.white },
        };

        private static GUIStyle _infoRowValue;
        /// <summary>「网格信息」右侧的取值，右对齐。</summary>
        public static GUIStyle InfoRowValue => _infoRowValue ??= new GUIStyle(EditorStyles.miniLabel)
        {
            normal    = { textColor = Color.white },
            alignment = TextAnchor.MiddleRight,
        };

        #endregion

        #region 状态指示器

        private static GUIStyle _indicatorLabel;
        /// <summary>状态行左侧的通道名。</summary>
        public static GUIStyle IndicatorLabel => _indicatorLabel ??= new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 10,
            normal   = { textColor = Color.white },
        };

        private static GUIStyle _chipNote;
        /// <summary>chip 下方的注释小字。</summary>
        public static GUIStyle ChipNote => _chipNote ??= new GUIStyle(EditorStyles.miniLabel)
        {
            fontSize  = 8,
            normal    = { textColor = TextMuted },
            alignment = TextAnchor.MiddleCenter,
            wordWrap  = true,
        };

        #endregion

        // ═══════════════════════════════════════════════════════════════
        //  样式库 —— 随调用变色
        // ═══════════════════════════════════════════════════════════════
        //  这些每次调用前改一次颜色再返回同一个实例。安全性见类头注释。

        #region 随调用变色

        private static GUIStyle _badge;
        /// <summary>数据状态卡右上角的徽标，右对齐。</summary>
        public static GUIStyle Badge(Color c)
        {
            _badge ??= new GUIStyle(GUI.skin.label)
            {
                fontSize  = 9,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight,
            };
            _badge.normal.textColor = c;
            return _badge;
        }

        private static GUIStyle _viewportBadge;
        /// <summary>预览视口左上角的存储方式徽标，左对齐。</summary>
        public static GUIStyle ViewportBadge(Color c)
        {
            _viewportBadge ??= new GUIStyle(EditorStyles.miniLabel)
            {
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
            };
            _viewportBadge.normal.textColor = c;
            return _viewportBadge;
        }

        private static GUIStyle _dot;
        /// <summary>状态行最左的圆点。</summary>
        public static GUIStyle Dot(Color c)
        {
            _dot ??= new GUIStyle(GUI.skin.label) { fontSize = 14 };
            _dot.normal.textColor = c;
            return _dot;
        }

        private static GUIStyle _indicatorDesc;
        /// <summary>状态行右侧的说明文字，颜色与圆点一致。</summary>
        public static GUIStyle IndicatorDesc(Color c)
        {
            _indicatorDesc ??= new GUIStyle(EditorStyles.miniLabel);
            _indicatorDesc.normal.textColor = c;
            return _indicatorDesc;
        }

        private static GUIStyle _chipBox;
        /// <summary>通道 chip 的外框。</summary>
        public static GUIStyle ChipBox(Color background)
        {
            _chipBox ??= new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(6, 6, 4, 4),
                margin  = new RectOffset(2, 2, 0, 0),
            };
            _chipBox.normal.background = SolidTex(background);
            return _chipBox;
        }

        private static GUIStyle _chipLabel;
        /// <summary>chip 内的通道名。</summary>
        public static GUIStyle ChipLabel(Color c)
        {
            _chipLabel ??= new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize  = 9,
                alignment = TextAnchor.MiddleCenter,
            };
            _chipLabel.normal.textColor = c;
            return _chipLabel;
        }

        private static GUIStyle _tag;
        /// <summary>目标区的来源标签（「场景对象」「Mesh 资产」）。</summary>
        public static GUIStyle Tag(Color background)
        {
            _tag ??= new GUIStyle(GUI.skin.box)
            {
                fontSize = 9,
                padding  = new RectOffset(5, 5, 2, 2),
                margin   = new RectOffset(2, 2, 2, 2),
                normal   = { textColor = Color.white },
            };
            _tag.normal.background = SolidTex(background);
            return _tag;
        }

        private static GUIStyle _clearButton;
        /// <summary>「清除通道」类小按钮：可用时偏红，禁用时转灰。</summary>
        public static GUIStyle ClearButton(bool enabled)
        {
            _clearButton ??= new GUIStyle(GUI.skin.button) { fontSize = 10 };
            _clearButton.normal.textColor = enabled ? new Color(1f, 0.55f, 0.45f) : Gray;
            return _clearButton;
        }

        private static GUIStyle _listRow;
        /// <summary>网格复选列表的行名 —— 用 label 样式的按钮当整行热区，焦点行加粗转白。</summary>
        public static GUIStyle ListRow(bool focused)
        {
            _listRow ??= new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
                hover     = { textColor = Color.white },
            };
            _listRow.fontStyle        = focused ? FontStyle.Bold : FontStyle.Normal;
            _listRow.normal.textColor = focused ? Color.white : new Color(0.72f, 0.76f, 0.82f);
            return _listRow;
        }

        #endregion
    }
}
