using UnityEditor;
using UnityEngine;
using StorageMode = OutlineSmoothNormalsGenerator.OutlineSmoothNormalsGeneratorWindow.StorageMode;
using VertexColorChannel = OutlineSmoothNormalsGenerator.OutlineSmoothNormalsGeneratorWindow.VertexColorChannel;

namespace OutlineSmoothNormalsGenerator
{
    /// <summary>
    /// 「导入时自动烘焙」的工程级配置。
    ///
    /// 用 <see cref="ScriptableSingleton{T}"/> 持久化到 <c>ProjectSettings/</c> 目录，
    /// 而非 <c>Assets/</c>：随工程走、可纳入版本管理、不在资源树里留下多余资产。
    ///
    /// 存储方式 / 顶点色通道 / UV 通道复用手动工具那一套枚举（<see cref="StorageMode"/>、
    /// <see cref="VertexColorChannel"/>），保证自动管线与手动窗口的编码语义完全一致。
    /// </summary>
    [FilePath("ProjectSettings/OutlineSmoothNormals.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class OutlineNormalsSettings : ScriptableSingleton<OutlineNormalsSettings>
    {
        // 默认关闭：自动烘焙会改写导入网格，必须由用户显式开启。
        [SerializeField] private bool autoBakeEnabled;

        // 命中规则用的文件名后缀（不含扩展名）。留空等于不命中任何模型。
        [SerializeField] private string filenameSuffix = "_Outline";

        [SerializeField] private StorageMode storageMode = StorageMode.VertexColor;
        [SerializeField] private VertexColorChannel vcChannel = VertexColorChannel.BA;
        [SerializeField] private int uvChannel = 1; // TEXCOORD1，避开主贴图 UV

        [SerializeField] private float mergeTolerance = OutlineSmoothNormalsCalculator.DefaultMergeTolerance;

        public bool AutoBakeEnabled
        {
            get => autoBakeEnabled;
            set => autoBakeEnabled = value;
        }

        public string FilenameSuffix
        {
            get => filenameSuffix;
            set => filenameSuffix = value;
        }

        public StorageMode StorageMode
        {
            get => storageMode;
            set => storageMode = value;
        }

        public VertexColorChannel VcChannel
        {
            get => vcChannel;
            set => vcChannel = value;
        }

        public int UvChannel
        {
            get => uvChannel;
            set => uvChannel = Mathf.Clamp(value, 0, 7);
        }

        public float MergeTolerance
        {
            get => mergeTolerance;
            set => mergeTolerance = Mathf.Clamp(value,
                OutlineSmoothNormalsCalculator.MinMergeTolerance,
                OutlineSmoothNormalsCalculator.MaxMergeTolerance);
        }

        /// <summary>把当前配置写回 <c>ProjectSettings/OutlineSmoothNormals.asset</c>。</summary>
        public void Save() => Save(true);
    }
}
