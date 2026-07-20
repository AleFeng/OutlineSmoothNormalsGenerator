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
            DrawHeader("基础设置");
            DrawProp(matEditor, props, "_BaseColor",  "基础颜色");
            DrawProp(matEditor, props, "_MainTex",    "贴图");
            DrawEnumPopup(matEditor, props, "_BaseColorMode", "基础色模式", BaseColorModeOptions);

            // 调试模式提示（仅 Demo Shader 有此属性；材质缺失时静默跳过）。
            var bcmProp = FindProperty("_BaseColorMode", props, false);
            if (bcmProp != null && (int)bcmProp.floatValue != 0)
                EditorGUILayout.HelpBox(
                    "调试模式：把平滑法线数据直接当颜色显示（不经光照）。" +
                    "切线为 [-1,1]→[0,1]，UV 取 xy 作 RG、B=0；" +
                    "顶点色 RG/GB/BA 只显示对应通道对（另一通道置 0，BA 的 A 借 R）。生产时请切回 Base Map。",
                    MessageType.Info);

            // NPR 明暗（仅 Demo Shader 有这些属性；材质缺失时 DrawProp 会自动跳过）。
            EditorGUILayout.Space(8);
            DrawHeader("NPR 明暗");
            DrawProp(matEditor, props, "_ShadeColor",     "暗部色调");
            DrawProp(matEditor, props, "_ShadeThreshold", "明暗阈值");
            DrawProp(matEditor, props, "_ShadeSoftness",  "明暗过渡");
            DrawProp(matEditor, props, "_RimColor",       "边缘光颜色");
            DrawProp(matEditor, props, "_RimPower",       "边缘光范围");

            EditorGUILayout.Space(8);
            DrawHeader("描边设置");
            DrawProp(matEditor, props, "_OutlineColor", "描边颜色");
            DrawProp(matEditor, props, "_OutlineWidth",  "描边宽度");
            DrawProp(matEditor, props, "_OutlineWidthMode", "宽度模式");

            EditorGUILayout.Space(8);
            DrawHeader("平滑法线来源");
            DrawEnumPopup(matEditor, props, "_SmoothNormalSrc", "存储通道", SmoothNormalSrcOptions);

            // 顶点色模式才需要选通道对，其余模式下这个选项无意义。
            var srcProp = FindProperty("_SmoothNormalSrc", props, false);
            int mode = srcProp != null ? (int)srcProp.floatValue : 0;
            if (mode == 0)
                DrawProp(matEditor, props, "_VCChannel", "顶点色通道对");

            // 存储空间：与存储通道正交。切线通道（1）与顶点法线对照（6）下 Shader 恒按
            // 对象空间处理 —— 此时【置灰】而非隐藏。隐藏会让人以为「这个功能不存在」，
            // 且与工具窗口的处理方式不一致（那边也是置灰）。
            bool spaceApplies = mode != 1 && mode != 6;
            var spaceProp = FindProperty("_SmoothNormalSpace", props, false);
            if (spaceProp != null)
            {
                using (new EditorGUI.DisabledScope(!spaceApplies))
                    matEditor.ShaderProperty(spaceProp, "存储空间");
            }

            EditorGUILayout.Space(4);
            DrawSourceHint(mode);

            // 属性缺失必须显式报出，不能像其他可选属性那样静默跳过：文档推荐的生产
            // 用法就是「把 OUTLINE Pass 复制进自己的 Shader」，很容易漏掉这个新属性，
            // 而漏掉的后果是切线空间烘的数据被按对象空间解，描边整体偏斜且毫无提示。
            if (spaceProp == null)
                EditorGUILayout.HelpBox(
                    "当前 Shader 没有 _SmoothNormalSpace 属性，描边将一律按【对象空间】解码。\n" +
                    "若这是自定义 Shader，请从示例 Shader 把这三处一并复制过去：Properties 块里的 " +
                    "_SmoothNormalSpace、CBUFFER/uniform 声明、以及 OUTLINE Pass 里的 " +
                    "OSN_ResolveSmoothNormalSpace 调用。否则按切线空间烘焙的数据无法正确解码。",
                    MessageType.Warning);
            else if (spaceApplies)
                DrawSpaceHint(spaceProp);
            else
                EditorGUILayout.HelpBox(
                    "该存储通道恒为对象空间，「存储空间」不适用（Shader 会忽略此项）。",
                    MessageType.None);

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
                    hint = "读取顶点色中选定通道对的 xy，Z 分量由 xy 重建。请与生成时选择的通道对保持一致。";
                    break;
                case 1:
                    hint = "读取 tangent.xyz 中存储的平滑法线。注意：该模式会覆盖网格原始切线，法线贴图将失效。\n" +
                           "该模式恒为对象空间，且无需切线空间 —— Unity 会把 tangent.xyz 当方向一起蒙皮，" +
                           "存进去的方向天然跟随骨骼动画。";
                    type = MessageType.Warning;
                    break;
                case 2:
                    hint = "读取 TEXCOORD0（即 mesh.uv，主贴图 UV）的 xy。注意：该通道通常被贴图占用。";
                    type = MessageType.Warning;
                    break;
                case 3:
                    hint = "读取 TEXCOORD1（即 mesh.uv2）的 xy。";
                    break;
                case 4:
                    hint = "读取 TEXCOORD2（即 mesh.uv3）的 xy。";
                    break;
                case 5:
                    hint = "读取 TEXCOORD3（即 mesh.uv4）的 xy。";
                    break;
                case 6:
                    hint = "不使用平滑法线，直接沿原始顶点法线外扩 —— 即「未使用本工具」的对照效果，硬边处描边会断裂。";
                    break;
                case 7:
                    hint = "读取 TEXCOORD4（即 mesh.uv5）的 xyz。";
                    break;
                case 8:
                    hint = "读取 TEXCOORD5（即 mesh.uv6）的 xyz。";
                    break;
                case 9:
                    hint = "读取 TEXCOORD6（即 mesh.uv7）的 xyz。";
                    break;
                case 10:
                    hint = "读取 TEXCOORD7（即 mesh.uv8）的 xyz。";
                    break;
                default:
                    hint = "未知模式。";
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
                EditorGUILayout.HelpBox(
                    "切线空间：用【蒙皮后】的法线与切线重建 TBN 再还原，" +
                    "SkinnedMeshRenderer 上描边会正确跟随骨骼动画。要求网格有合法切线。\n" +
                    "若模型的平滑法线是按对象空间烘的，选这里会让描边整体偏斜。",
                    MessageType.Info);
            else
                EditorGUILayout.HelpBox(
                    "对象空间：解码即用，开销最低，但仅静态模型正确。\n" +
                    "顶点色 / TEXCOORD 不参与蒙皮，SkinnedMeshRenderer 上外扩方向会停在" +
                    "绑定姿势，动画一跑描边就撕开 —— 那种情况请重新按切线空间烘焙并改选切线空间。",
                    MessageType.None);
        }
    }
}
