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
        [SerializeField] private bool _autoBakeEnabled = false;

        // 命中规则用的文件名后缀（不含扩展名）。留空等于不命中任何模型。
        [SerializeField] private string _filenameSuffix = "_Outline";

        [SerializeField] private StorageMode _storageMode = StorageMode.VertexColor;
        [SerializeField] private VertexColorChannel _vcChannel = VertexColorChannel.Ba;
        [SerializeField] private int _uvChannel = 1; // TEXCOORD1，避开主贴图 UV

        [SerializeField] private float _mergeTolerance = OutlineSmoothNormalsCalculator.DefaultMergeTolerance;

        public bool AutoBakeEnabled
        {
            get => _autoBakeEnabled;
            set => _autoBakeEnabled = value;
        }

        public string FilenameSuffix
        {
            get => _filenameSuffix;
            set => _filenameSuffix = value;
        }

        public StorageMode StorageMode
        {
            get => _storageMode;
            set => _storageMode = value;
        }

        public VertexColorChannel VcChannel
        {
            get => _vcChannel;
            set => _vcChannel = value;
        }

        public int UvChannel
        {
            get => _uvChannel;
            set => _uvChannel = Mathf.Clamp(value, 0, 7);
        }

        public float MergeTolerance
        {
            get => _mergeTolerance;
            set => _mergeTolerance = Mathf.Clamp(value,
                OutlineSmoothNormalsCalculator.MinMergeTolerance,
                OutlineSmoothNormalsCalculator.MaxMergeTolerance);
        }

        /// <summary>把当前配置写回 <c>ProjectSettings/OutlineSmoothNormals.asset</c>。</summary>
        public void Save() => Save(true);
    }
}
