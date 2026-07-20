using UnityEditor;
using UnityEngine;
using StorageMode = OutlineSmoothNormalsGenerator.OutlineSmoothNormalsGeneratorWindow.StorageMode;
using VertexColorChannel = OutlineSmoothNormalsGenerator.OutlineSmoothNormalsGeneratorWindow.VertexColorChannel;
using NormalSpace = OutlineSmoothNormalsGenerator.OutlineSmoothNormalsGeneratorWindow.NormalSpace;

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

        // ── 命中条件 ─────────────────────────────────────────────────
        // 两个条件各自可开关，同时开启时取【交集】：每勾一个就多加一道约束。
        // 默认「只开后缀」，与引入文件夹匹配之前的行为完全一致。
        [SerializeField] private bool matchBySuffix = true;
        [SerializeField] private bool matchByFolder;

        // 文件名后缀（不含扩展名）。留空等于不命中任何模型。
        [SerializeField] private string filenameSuffix = "_Outline";

        // 文件夹的资产路径（形如 Assets/Characters）。留空等于不命中任何模型 ——
        // 与「后缀留空」的既有语义一致，空配置绝不能变成「命中一切」。
        [SerializeField] private string folderPath = "";

        [SerializeField] private StorageMode storageMode = StorageMode.VertexColor;
        [SerializeField] private VertexColorChannel vcChannel = VertexColorChannel.BA;
        [SerializeField] private int uvChannel = 1; // TEXCOORD1，避开主贴图 UV

        // 与手动窗口同样默认切线空间：自动烘焙常用于整批角色模型，蒙皮是常态。
        [SerializeField] private NormalSpace normalSpace = NormalSpace.Tangent;

        [SerializeField] private float mergeTolerance = OutlineSmoothNormalsCalculator.DefaultMergeTolerance;

        public bool AutoBakeEnabled
        {
            get => autoBakeEnabled;
            set => autoBakeEnabled = value;
        }

        public bool MatchBySuffix
        {
            get => matchBySuffix;
            set => matchBySuffix = value;
        }

        public bool MatchByFolder
        {
            get => matchByFolder;
            set => matchByFolder = value;
        }

        /// <summary>
        /// 是否至少启用了一个命中条件。
        ///
        /// 单独拎出来是因为它挡着一个真实的坑：命中判断是各条件取【交集】，
        /// 而 AND 在【零个条件】上是恒真的 —— 两个都不勾时若直接折叠 &amp;&amp;，
        /// 结果会是「命中全工程的每一个模型」并把它们统统改写。
        /// </summary>
        public bool HasAnyMatchCondition => matchBySuffix || matchByFolder;

        public string FilenameSuffix
        {
            get => filenameSuffix;
            set => filenameSuffix = value;
        }

        /// <summary>
        /// 文件夹匹配的根路径。setter 统一分隔符并去掉结尾斜杠，
        /// 使「Assets\Characters\」与「Assets/Characters」落到同一个值。
        /// </summary>
        public string FolderPath
        {
            get => folderPath;
            set => folderPath = string.IsNullOrEmpty(value)
                ? ""
                : value.Replace('\\', '/').TrimEnd('/');
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

        /// <summary>
        /// 用户选择的存储空间【原样值】。UI 绑定这个。
        ///
        /// 这里刻意【不做】「切线通道恒为对象空间」的归一：归一放进 getter 会让
        /// <c>NormalSpace = NormalSpace</c> 不再是恒等操作，而 IMGUI 的
        /// <c>x = EnumPopup(..., x)</c> 正是这种读回写 —— <see cref="EditorGUI.DisabledScope"/>
        /// 只禁用交互、不阻止赋值，于是切到切线通道的那一帧就会把用户原本选的
        /// 切线空间静默擦成对象空间并落盘，切回来时已经找不回来了。
        /// 归一改由 <see cref="EffectiveNormalSpace"/> 承担。
        /// </summary>
        public NormalSpace NormalSpace
        {
            get => normalSpace;
            set => normalSpace = value;
        }

        /// <summary>
        /// 实际生效的存储空间 —— 烘焙与校验一律用这个。
        /// 切线通道存储恒为对象空间：切线本身就是数据，没有基可供重建，也无此必要。
        /// </summary>
        public NormalSpace EffectiveNormalSpace
            => storageMode == StorageMode.TangentSpace ? NormalSpace.Object : normalSpace;

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
