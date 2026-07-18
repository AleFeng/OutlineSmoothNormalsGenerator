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

        public override void OnGUI(MaterialEditor matEditor, MaterialProperty[] props)
        {
            // 全程只经由 props 操作，因此天然支持多选编辑。
            EditorGUILayout.Space(4);
            DrawHeader("基础设置");
            DrawProp(matEditor, props, "_BaseColor",  "基础颜色");
            DrawProp(matEditor, props, "_MainTex",    "贴图");

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
            DrawProp(matEditor, props, "_SmoothNormalSrc", "存储通道");

            // 顶点色模式才需要选通道对，其余模式下这个选项无意义。
            var srcProp = FindProperty("_SmoothNormalSrc", props, false);
            int mode = srcProp != null ? (int)srcProp.floatValue : 0;
            if (mode == 0)
                DrawProp(matEditor, props, "_VCChannel", "顶点色通道对");

            EditorGUILayout.Space(4);
            DrawSourceHint(mode);

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
        /// 各模式的说明。索引必须与 Shader 里 _SmoothNormalSrc 的 KeywordEnum 顺序一致：
        /// 0=VertexColor 1=TangentSpace 2..5=TexCoord0..3 6=VertexNormal。
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
                    hint = "读取 tangent.xyz 中存储的平滑法线。注意：该模式会覆盖网格原始切线，法线贴图将失效。";
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
                default:
                    hint = "未知模式。";
                    type = MessageType.Error;
                    break;
            }

            EditorGUILayout.HelpBox(hint, type);
        }
    }
}
