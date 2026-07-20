using UnityEditor;
using UnityEngine;

namespace OutlineSmoothNormalsGenerator
{
    /// <summary>
    /// 描边 Shader 的自定义材质 Inspector 界面。
    /// </summary>
    public class OutlineShaderGUI : ShaderGUI
    {
        private static readonly Color ColorAccent = new Color(0.33f, 0.78f, 1f);

        // _BaseColorMode 的下拉选项。项数（14）超过 Shader 内联 [Enum(name,val,…)] 的
        // 7 组上限 —— 超了 Unity 就构造不出下拉、退化成裸数字输入框，因此在这里用
        // Popup 手绘。索引 = 属性浮点值（0..13 连续），顺序必须与 OSN_DebugBaseColor 一致。
        private static readonly string[] BaseColorModeOptions =
        {
            "Base Map",
            "Vertex Color",
            "Vertex Color RG",
            "Vertex Color GB",
            "Vertex Color BA",
            "Tangent Channel",
            "UV0",
            "UV1",
            "UV2",
            "UV3",
            "UV4",
            "UV5",
            "UV6",
            "UV7",
        };

        // _SmoothNormalSrc 的下拉选项。存储通道多达 8 个（TEXCOORD0..7）、连同其他模式
        // 共 11 项，超过 Shader 内联 [KeywordEnum] 的上限，故 Shader 改为运行时按 float
        // 分支、这里手绘下拉。索引 = 属性浮点值（顺序必须与 OSN_SelectSmoothNormalOS 一致；
        // 末尾追加 TexCoord4..7，前 7 项值保持不变）。
        private static readonly string[] SmoothNormalSrcOptions =
        {
            "Vertex Color",     // 0
            "Tangent Channel",  // 1  历史上叫 Tangent Space，与「存储空间」撞名，已改称通道
            "TexCoord0",      // 2
            "TexCoord1",      // 3
            "TexCoord2",      // 4
            "TexCoord3",      // 5
            "Vertex Normal",  // 6
            "TexCoord4",      // 7
            "TexCoord5",      // 8
            "TexCoord6",      // 9
            "TexCoord7",      // 10
        };

        public override void OnGUI(MaterialEditor matEditor, MaterialProperty[] props)
        {
            // 全程只经由 props 操作，因此天然支持多选编辑。
            EditorGUILayout.Space(4);
            DrawHeader(LocShaderGUI.HeaderBase);
            DrawProp(matEditor, props, "_BaseColor",  LocShaderGUI.PropBaseColor);
            DrawProp(matEditor, props, "_MainTex",    LocShaderGUI.PropMainTex);
            DrawEnumPopup(matEditor, props, "_BaseColorMode",
                          LocShaderGUI.PropBaseColorMode, BaseColorModeOptions);

            // 调试模式提示（仅 Demo Shader 有此属性；材质缺失时静默跳过）。
            var bcmProp = FindProperty("_BaseColorMode", props, false);
            if (bcmProp != null && (int)bcmProp.floatValue != 0)
                EditorGUILayout.HelpBox(LocShaderGUI.DebugModeHelp, MessageType.Info);

            // NPR 明暗（仅 Demo Shader 有这些属性；材质缺失时 DrawProp 会自动跳过）。
            EditorGUILayout.Space(8);
            DrawHeader(LocShaderGUI.HeaderNpr);
            DrawProp(matEditor, props, "_ShadeColor",     LocShaderGUI.PropShadeColor);
            DrawProp(matEditor, props, "_ShadeThreshold", LocShaderGUI.PropShadeThreshold);
            DrawProp(matEditor, props, "_ShadeSoftness",  LocShaderGUI.PropShadeSoftness);
            DrawProp(matEditor, props, "_RimColor",       LocShaderGUI.PropRimColor);
            DrawProp(matEditor, props, "_RimPower",       LocShaderGUI.PropRimPower);

            EditorGUILayout.Space(8);
            DrawHeader(LocShaderGUI.HeaderOutline);
            DrawProp(matEditor, props, "_OutlineColor",     LocShaderGUI.PropOutlineColor);
            DrawProp(matEditor, props, "_OutlineWidth",     LocShaderGUI.PropOutlineWidth);
            DrawProp(matEditor, props, "_OutlineWidthMode", LocShaderGUI.PropOutlineWidthMode);

            EditorGUILayout.Space(8);
            DrawHeader(LocShaderGUI.HeaderSmoothNormal);
            DrawEnumPopup(matEditor, props, "_SmoothNormalSrc",
                          LocShaderGUI.PropSmoothNormalSrc, SmoothNormalSrcOptions);

            // 顶点色模式才需要选通道对，其余模式下这个选项无意义。
            var srcProp = FindProperty("_SmoothNormalSrc", props, false);
            int mode = srcProp != null ? (int)srcProp.floatValue : 0;
            if (mode == 0)
                DrawProp(matEditor, props, "_VCChannel", LocShaderGUI.PropVcChannel);

            // 存储空间：与存储通道正交。切线通道（1）与顶点法线对照（6）下 Shader 恒按
            // 对象空间处理 —— 此时【置灰】而非隐藏。隐藏会让人以为「这个功能不存在」，
            // 且与工具窗口的处理方式不一致（那边也是置灰）。
            bool spaceApplies = mode != 1 && mode != 6;
            var spaceProp = FindProperty("_SmoothNormalSpace", props, false);
            if (spaceProp != null)
            {
                using (new EditorGUI.DisabledScope(!spaceApplies))
                    matEditor.ShaderProperty(spaceProp, LocShaderGUI.PropSmoothNormalSpace);
            }

            EditorGUILayout.Space(4);
            DrawSourceHint(mode);

            // 属性缺失必须显式报出，不能像其他可选属性那样静默跳过：文档推荐的生产
            // 用法就是「把 OUTLINE Pass 复制进自己的 Shader」，很容易漏掉这个新属性，
            // 而漏掉的后果是切线空间烘的数据被按对象空间解，描边整体偏斜且毫无提示。
            if (spaceProp == null)
                EditorGUILayout.HelpBox(LocShaderGUI.MissingSpacePropWarning, MessageType.Warning);
            else if (spaceApplies)
                DrawSpaceHint(spaceProp);
            else
                EditorGUILayout.HelpBox(LocShaderGUI.SpaceNotApplicable, MessageType.None);

            EditorGUILayout.Space(8);
            matEditor.RenderQueueField();
        }

        private void DrawHeader(string title)
        {
            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                normal   = { textColor = ColorAccent },
            };
            EditorGUILayout.LabelField($"── {title}", style);
        }

        private void DrawProp(MaterialEditor me, MaterialProperty[] props, string name, string label)
        {
            var prop = FindProperty(name, props, false);
            if (prop != null)
                me.ShaderProperty(prop, label);
        }

        /// <summary>
        /// 把一个 Float 属性画成枚举下拉。用于选项数超过 Shader 内联 [Enum] 上限（7 组）
        /// 的属性 —— 那种情况直接 ShaderProperty 会退化成裸数字输入框。
        /// 全程只经由 prop 操作，天然支持多选编辑与 Undo。
        /// </summary>
        private void DrawEnumPopup(MaterialEditor me, MaterialProperty[] props,
                                   string name, string label, string[] options)
        {
            var prop = FindProperty(name, props, false);
            if (prop == null) return;

            int cur = Mathf.Clamp(Mathf.RoundToInt(prop.floatValue), 0, options.Length - 1);
            EditorGUI.showMixedValue = prop.hasMixedValue;
            EditorGUI.BeginChangeCheck();
            int next = EditorGUILayout.Popup(label, cur, options);
            if (EditorGUI.EndChangeCheck())
            {
                me.RegisterPropertyChangeUndo(label);
                prop.floatValue = next;
            }
            EditorGUI.showMixedValue = false;
        }

        // TEXCOORD 各档的说明文案已移到 LocShaderGUI.TexCoordHint（仍是统一在一处生成）。

        /// <summary>
        /// 各模式的说明。索引必须与 SmoothNormalSrcOptions / OSN_SelectSmoothNormalOS 一致：
        /// 0=VertexColor 1=TangentSpace 2..5=TexCoord0..3 6=VertexNormal 7..10=TexCoord4..7。
        /// </summary>
        private void DrawSourceHint(int mode)
        {
            string hint;
            var type = MessageType.Info;

            switch (mode)
            {
                case 0:
                    hint = LocShaderGUI.SrcHintVertexColor;
                    break;
                case 1:
                    hint = LocShaderGUI.SrcHintTangentChannel;
                    type = MessageType.Warning;
                    break;
                case 2:
                    hint = LocShaderGUI.TexCoordHint(0, LocShaderGUI.MeshUv0Name)
                           + LocShaderGUI.TexCoord0Extra;
                    type = MessageType.Warning;
                    break;
                case 3:
                    hint = LocShaderGUI.TexCoordHint(1, "mesh.uv2");
                    break;
                case 4:
                    hint = LocShaderGUI.TexCoordHint(2, "mesh.uv3");
                    break;
                case 5:
                    hint = LocShaderGUI.TexCoordHint(3, "mesh.uv4");
                    break;
                case 6:
                    hint = LocShaderGUI.SrcHintVertexNormal;
                    break;
                case 7:
                    hint = LocShaderGUI.TexCoordHint(4, "mesh.uv5");
                    break;
                case 8:
                    hint = LocShaderGUI.TexCoordHint(5, "mesh.uv6");
                    break;
                case 9:
                    hint = LocShaderGUI.TexCoordHint(6, "mesh.uv7");
                    break;
                case 10:
                    hint = LocShaderGUI.TexCoordHint(7, "mesh.uv8");
                    break;
                default:
                    hint = LocShaderGUI.SrcHintUnknown;
                    type = MessageType.Error;
                    break;
            }

            EditorGUILayout.HelpBox(hint, type);
        }

        /// <summary>
        /// 存储空间的说明。这个值必须与生成时的选择一致，且无法从数据本身推断出来
        /// —— 两种空间存的都只是一条单位方向，选错不会报错，只会让描边整体偏斜。
        /// 因此这里把「选错的症状」写清楚，方便对着现象反查。
        /// </summary>
        private void DrawSpaceHint(MaterialProperty prop)
        {
            if ((int)prop.floatValue == 1)
                EditorGUILayout.HelpBox(LocShaderGUI.SpaceHintTangent, MessageType.Info);
            else
                EditorGUILayout.HelpBox(LocShaderGUI.SpaceHintObject, MessageType.None);
        }
    }
}
