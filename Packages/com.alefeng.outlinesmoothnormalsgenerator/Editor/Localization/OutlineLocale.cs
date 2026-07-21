using UnityEditor;
using UnityEngine;

namespace OutlineSmoothNormalsGenerator
{
    /// <summary>界面语言。取值即 EditorPrefs 里存的整数，不要重排。</summary>
    public enum OutlineLanguage
    {
        Chinese  = 0,
        English  = 1,
        Japanese = 2,
    }

    /// <summary>
    /// 编辑器界面的三语（中 / 英 / 日）支撑。
    ///
    /// 用法是在文案类里写 <c>OutlineLocale.Pick("中文", "English", "日本語")</c>，
    /// 调用侧只引用具名成员（如 <see cref="LocWindow.SectionTarget"/>）—— 拼错名字是
    /// 编译错误，不会像「中文原文当 key」那样静默退回中文。
    ///
    /// 【偏好存 EditorPrefs 而非 OutlineNormalsSettings】
    /// 后者的资产在 ProjectSettings/ 下随工程进版本管理。语言是每个人自己的偏好，
    /// 提交上去只会让协作者之间反复互相覆盖。EditorPrefs 按机器存，还能跨工程保持一致。
    ///
    /// 【为什么不做字符串缓存】
    /// Pick 的三个分支返回的都是编译期折叠好的字面量，取一个字段引用而已，零分配；
    /// 只有走 Fmt 的成员会分配，而那些位置本来就在用 $"..." 每帧拼串。唯一的例外是
    /// 返回数组的成员（见 LocWindow.UvChannelNames），那种必须自己按语言缓存。
    /// </summary>
    public static class OutlineLocale
    {
        private const string PrefKey = "OutlineSmoothNormals.Language";

        // null = 尚未从 EditorPrefs 读过。域重载会把它清空，下次访问自动重读。
        private static OutlineLanguage? _current;

        /// <summary>当前界面语言。默认中文；赋值会立即持久化并重绘所有编辑器窗口。</summary>
        public static OutlineLanguage Current
        {
            get
            {
                if (_current == null)
                {
                    int raw = EditorPrefs.GetInt(PrefKey, (int)OutlineLanguage.Chinese);
                    // 手改过 EditorPrefs、或将来枚举缩减时，越界值不该让界面整体空白。
                    _current = (OutlineLanguage)Mathf.Clamp(raw, (int)OutlineLanguage.Chinese,
                                                                 (int)OutlineLanguage.Japanese);
                }
                return _current.Value;
            }
            set
            {
                if (_current == value) return;
                _current = value;
                EditorPrefs.SetInt(PrefKey, (int)value);
                Changed?.Invoke();
                RepaintAllEditorWindows();
            }
        }

        /// <summary>
        /// 语言变更通知。订阅方负责让【已成文并缓存下来的字符串】失效 —— 本包里
        /// 只有网格健康检查报告属于这一类（MeshHealthReport 存的是拼好的句子，
        /// 不重算就会一直停在旧语言）。
        ///
        /// 订阅必须与退订严格成对，否则窗口关掉后仍挂在事件链上、连实例一起泄漏。
        /// </summary>
        public static event System.Action Changed;

        public static string Pick(string zh, string en, string ja) => Current switch
        {
            OutlineLanguage.English  => en,
            OutlineLanguage.Japanese => ja,
            _                        => zh,
        };

        /// <summary>
        /// 带参数的文案。三种语言各写【完整整句】的模板，占位符按各自语序摆放 ——
        /// 不要把句子拆成片段再拼，中英日语序不同，拼出来的句子必然别扭。
        /// </summary>
        public static string Fmt(string zh, string en, string ja, params object[] args)
            => string.Format(Pick(zh, en, ja), args);

        // ═══════════════════════════════════════════════════════════════
        //  切换控件
        // ═══════════════════════════════════════════════════════════════
        private const float SegmentWidth  = 62f;
        private const float SegmentHeight = 18f;

        /// <summary>
        /// 三段式语言切换按钮。两个页签的标题区各画一份，视觉沿用窗口内既有的
        /// 高亮分段按钮语汇。按钮文字恒为各语言的自称，不随当前语言翻译 ——
        /// 界面已经看不懂的时候，「English」比「英语」更找得到。
        /// </summary>
        public static void DrawSwitch()
        {
            EditorGUILayout.BeginHorizontal();
            DrawSegment("中文",    OutlineLanguage.Chinese);
            DrawSegment("English", OutlineLanguage.English);
            DrawSegment("日本語",  OutlineLanguage.Japanese);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawSegment(string label, OutlineLanguage lang)
        {
            bool active = Current == lang;

            // 视觉与窗口内其他四组分段按钮同源，见 OutlineEditorGUI。
            if (OutlineEditorGUI.DrawSegmentLanguage(label, active, SegmentWidth, SegmentHeight)
                && !active)
            {
                Current = lang;
                GUI.FocusControl(null);
            }
        }

        /// <summary>
        /// 切换语言后刷新所有编辑器窗口。
        ///
        /// 光靠 <see cref="Changed"/> 不够：订阅它的只有生成器窗口，而材质 Inspector
        /// （OutlineShaderGUI）同样显示本地化文案却无从订阅 —— 不主动刷一遍，它会一直
        /// 停在旧语言，直到用户碰它才重绘。
        ///
        /// 用 Resources.FindObjectsOfTypeAll 而不是 UnityEditorInternal.InternalEditorUtility
        /// .RepaintAllViews()：后者在 internal 命名空间下，为一次重绘引入非公开 API 不划算。
        /// 正在销毁中的窗口会被 if (w) 挡掉。
        /// </summary>
        private static void RepaintAllEditorWindows()
        {
            foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
                if (w) w.Repaint();
        }
    }
}
