# Outline Smooth Normals Generator — 使用文档

<p align="center">
  🌍
  中文 |
  <a href="./README_EN.md">English</a> |
  <a href="./README_JA.md">日本語</a>
</p>

为 Mesh 计算**平滑法线**并烘焙进网格，用于背面外扩描边，解决硬边处描边断裂。

工具本体是纯编辑器 C#，**与渲染管线无关**。描边 Shader 按管线以 Sample 形式
分别提供（URP / Built-in），按需导入。

---

## 目录结构

```
com.alefeng.outlinesmoothnormalsgenerator/
├── package.json
├── CHANGELOG.md
├── LICENSE.md
├── README.md                                        ← 本文档
├── Editor/
│   ├── OutlineSmoothNormalsGeneratorWindow.cs       ← 主窗口（含内嵌实时预览）
│   ├── OutlineSmoothNormalsCalculator.cs            ← 平滑法线计算（角度加权 + 容差合并）
│   ├── OutlineSmoothNormalsCodec.cs                 ← 存储格式编解码（.hlsl 的 C# 镜像）
│   ├── StorageWriter.cs                             ← 写入顶点色 / 切线 / TEXCOORD
│   ├── OutlineNormalsImportProcessor.cs             ← 导入时自动烘焙（AssetPostprocessor + 扩展钩子）
│   ├── OutlineNormalsSettings.cs                    ← 自动烘焙配置（持久化到 ProjectSettings/）
│   ├── OutlineMeshValidator.cs                      ← 网格数据健康检查
│   ├── OutlineShaderGUI.cs                          ← 描边材质自定义 Inspector
│   ├── Shader/OutlinePreview.shader                 ← 编辑器预览专用
│   └── OutlineSmoothNormalsGenerator.Editor.asmdef
├── Shader/
│   ├── OutlineSmoothNormals.hlsl                    ← 解码 + 外扩数学的【唯一真源】
│   └── OutlineNPR.hlsl                              ← Demo 基础 NPR 光照数学（卡通明暗 + 边缘光）
└── Samples~/
    ├── URP/          → Sample「Outline Shader (URP) & Demo」
    └── BuiltIn/      → Sample「Outline Shader (Built-in RP) & Demo」
```

`Shader/OutlineSmoothNormals.hlsl` 被编辑器预览与两个管线的描边 Shader 共用。
解码数学只有这一份，因此「预览正常、实际渲染却不对」在结构上不可能发生。

---

## 导入描边 Shader（必需）

> 本文档面向**已安装本包**的使用者。安装方式（UPM git URL / 拷入 `Packages/`）见
> 仓库根目录 README。

核心包**不含**描边 Shader —— 那样会让包依赖某个特定管线。请在 Package Manager 里
选中本包 → `Samples` → **按你的项目管线导入其中一个**：

| Sample | Shader 名 |
|---|---|
| Outline Shader (URP) & Demo | `OutlineSmoothNormalsGenerator/Outline URP` |
| Outline Shader (Built-in RP) & Demo | `OutlineSmoothNormalsGenerator/Outline Built-in` |

两者可以共存（名字不同），但一个项目只有一个管线，通常只需导入对应的那个。

> 工具本体在核心包里，菜单 **`Tools > Smooth Normal Generator`** 在包安装后即可用；
> 导入 Sample 只是为了拿到描边 Shader，与工具菜单是否出现无关。

---

## 快速上手

![「平滑法线生成器」页签：左为控制面板，右上为数据通道状态、右下为实时描边预览](./Docs~/Images/tool_generate.png)

### 1. 打开工具

`Tools > Smooth Normal Generator`

### 2. 选择目标

选择目标，工具会自动读取。目标可以是：

- **场景对象 / 模型**（`.fbx` / `.obj` 等）**/ 预制体** —— 都会自动**遍历整个层级**（含子物体），
  收集其中全部网格。
- **Mesh 资产** —— Project 里的独立 `.asset`，或展开 FBX 选中的 Mesh 子资产，直接就是目标。

也可手动把上述任意一种拖入「目标」字段。

含多个网格时，目标区会列出一个**复选框滚动列表**（顶部「全选 / 清空」，加载新来源时默认
全选，同一网格被多个渲染器共用时自动去重）。**勾选**决定「生成 / 保存 / 另存为」作用于哪些
网格、也决定预览显示哪些；**单击网格名**把它设为**焦点** —— 右侧网格信息与通道状态显示焦点网格。

### 3. 确保网格可写

如果网格来自 `.fbx` 等模型文件，它是**只读的导入子资产** —— 写进去的数据会在下次
重导入时丢失。工具会检测这种情况并阻止保存，请点 **`⧉ 另存为独立 Mesh…`**
复制一份可写的 `.asset`。

> 选中的是**场景对象**时，新 `.asset` 会自动替换到对象上；选中的是**资产**（Mesh /
> 模型 / 预制体）时没有可回填的组件，只生成独立 `.asset`，请自行引用。
> 独立可写的 `.asset` Mesh 则本就可写，直接在第 4 步保存即可，无需另存。

### 4. 选择存储方式并生成

选好存储通道后点 **`▶ 生成平滑法线`**，再点 **`保存`** 落盘。

### 5. 预览

窗口右侧就是实时预览（**内嵌**，不是单独的窗口），显示所有**勾选**的网格（多选时按各自在
层级中的相对位置同屏摆放，相机自动兜住全部；未勾选时显示占位提示）：

- 左键拖动旋转，滚轮缩放，中键平移
- 可开启 **显示平滑法线**（绿）与 **显示原始法线**（蓝）叠加对比（针对焦点网格）
- 可调描边宽度、颜色、模型光滑度 / 金属度、背景色

预览与实际渲染共用同一份解码与外扩数学，所见即所得。

---

## 界面详解

工具窗口顶部有两个页签：**平滑法线生成器**（手动流程）与 **导入自动烘焙**（见[导入时自动烘焙](#导入时自动烘焙可选)）。
本节自上而下说明「平滑法线生成器」页签的每个功能区 —— 布局对照上文[快速上手](#快速上手)里的截图：
**左栏是控制面板，右栏上半是数据通道状态、下半是实时预览与参数。**

### 左栏 · 目标对象
- **目标**：当前来源对象 / 资产，右侧 `×` 可清空。可把场景对象、Mesh、模型、预制体直接拖入此字段。
- **网格列表（已选 N / M）**：从来源里发现的全部网格。顶部 `全选` / `清空` 一键切换勾选。
  - **勾选框**决定哪些网格纳入批量处理 —— 「生成 / 保存 / 另存为」作用于**所有勾选项**，预览也同屏显示所有勾选项。
  - **单击网格名**把它设为**焦点**（高亮行）：右侧「网格信息」「数据通道状态」与预览里的「法线可视化叠加」都只针对焦点网格。
  - 每行标注渲染器类型（`MeshFilter` / `SkinnedMeshRenderer`）；焦点网格下方另有两枚标签显示其渲染器与网格名。
  - 下方信息条提示当前勾选数量与「批量作用于全部、状态只看焦点」的规则。

### 左栏 · 网格信息（可折叠）
展开后一览焦点网格的：**顶点数、三角面数、SubMesh 数**，以及是否含**法线 / 切线 / 顶点色**、各 **TEXCOORD0–7** 通道是否有数据（含分量数）。用来在写入前快速确认目标通道是否已被占用。

### 左栏 · 存储方式
三个页签 **顶点色 / 切线空间 / UV 通道**，决定平滑法线写到哪（原理详见[存储方式](#存储方式)）：
- **顶点色**：选 `RG / GB / BA` 通道对；下方「顶点色通道数据状态」逐通道标出**将被覆盖写入**的分量（如 BA 模式下 B←法线 X、A←法线 Y），未选中的通道保持原值不动；`清除 RG / GB / BA` 按通道对抹掉数据。
- **切线空间**：把方向直接写进 `tangent.xyz`；⚠ 会覆盖原始切线，需要恢复时用面板里的「重算切线」重新算出真实切线。
- **UV 通道**：下拉选 `TEXCOORD0–7`（可逐通道清除）；选 `TEXCOORD0`（主贴图 UV）会二次确认。

### 左栏 · 生成平滑法线
- **合并容差**滑杆（默认 `0.0001`，原理详见[合并容差](#合并容差)）。
- **▶ 生成平滑法线** 大按钮：文字实时显示当前存储通道与勾选数量（如「→ 顶点色 ×6」），点击即对所有勾选网格计算并写入 `sharedMesh`。
- 若焦点网格数据有问题，本区顶部会浮现**健康检查**卡片（详见[网格健康检查](#网格健康检查)）；存在 Error 时生成前会二次确认。

### 左栏底部 · 保存
固定在底部的 `↺ 还原本次修改`、`保存`、`⧉ 另存为独立 Mesh…` 三个按钮，作用与注意事项见[数据安全](#数据安全)。

### 右栏上 · 数据通道状态总览
顶点色 / 切线 / TEXCOORD 三张卡片，逐通道标注「有数据 / 空」等状态（判据与措辞详见[数据状态](#数据状态)）。始终反映**焦点**网格。

### 右栏下 · 描边预览与参数
内嵌实时预览：**左键旋转、滚轮缩放、中键平移**；左上角徽标（如「● 顶点色 模式」）**只读**地指示预览当前从哪个通道解码，跟随左侧「存储方式」。预览与生产 Shader 共用同一份解码 / 外扩数学，所见即所得。右侧参数面板自上而下：

| 分组 | 参数 | 说明 |
|---|---|---|
| **描边参数** | 显示描边 | 开 / 关描边 Pass。 |
| | 描边颜色 | 描边的颜色。 |
| | 描边宽度 | `0.001–0.1`，与 Shader 的 `_OutlineWidth` 同尺度，可直接对照。 |
| | 宽度模式 | **屏幕空间**（等宽、不随距离变化）/ **世界空间**（按世界单位偏移、近大远小）。 |
| **模型参数** | 显示模型 | 开 / 关基础模型（只想看描边时可关）。 |
| | 基础颜色 / 光滑度 / 金属度 | 预览材质外观；光滑度、金属度范围 `0–1`。 |
| **视口参数** | 背景颜色 | 预览背景色。 |
| **法线可视化** | 显示平滑法线 | 叠加绿色平滑法线线段。 |
| | 法线长度 | 线段长度，`0.005–0.5`。 |
| | 平滑法线颜色 | 平滑法线线段颜色。 |
| | 显示原始法线 | 叠加蓝色原始法线，用于对比硬边处的方向差异。 |
| | 原始法线颜色 | 原始法线线段颜色。 |
| **相机控制** | 水平旋转 / 垂直旋转 / 距离 | 精确设定视角（也可直接在视口拖拽 / 滚轮）。 |
| | 重置视角 | 复位角度并自动兜住所有勾选的网格。 |

> 法线可视化的叠加线段只画**焦点**网格；而预览渲染会显示**所有勾选**的网格（多选时按各自在层级中的相对位置摆放）。

---

## 导入时自动烘焙

![「导入自动烘焙」页签：启用开关、命中后缀、存储方式与合并容差，配置存于 ProjectSettings/](./Docs~/Images/tool_auto.png)

除了上面的手动流程，工具还能在模型**导入时自动烘焙**：命中规则的模型一旦（重）导入，
平滑法线就被自动写进网格 —— 无需手动跑工具、无需另存独立 Mesh。

**非破坏性**：写入发生在导入过程中、针对正在导入的网格，随导入产物落盘；去掉后缀或
关闭开关后重新导入，即恢复原始网格。

### 开启与配置

打开工具窗口顶部的 **「导入自动烘焙」页签**：

| 配置 | 说明 |
|---|---|
| **启用导入时自动烘焙** | 总开关，默认关闭。 |
| **文件名后缀** | 命中规则：文件名（不含扩展名）以此结尾即烘焙，大小写不敏感。默认 `_Outline`，如 `Hero_Outline.fbx`。 |
| **存储方式** | 顶点色 / 切线 / `TEXCOORD0`–`7`，与手动流程含义一致；Shader 端要选同一通道。 |
| **合并容差** | 同下文「合并容差」一节。 |

配置持久化到 `ProjectSettings/OutlineSmoothNormals.asset`，随工程纳入版本管理，团队共享一致设置。

> 改了配置后，已导入的模型不会自动重烘 —— 对它们重新导入（右键 `Reimport`）一次即可。

### 扩展钩子（进阶）

内置的「文件名后缀 + 三种存储」覆盖不了的私有管线，可用两个 static 委托接管，空则回退
默认。一般用 `[InitializeOnLoadMethod]` 在加载时赋值一次：

```csharp
using UnityEditor;
using OutlineSmoothNormalsGenerator;

static class MyOutlineAutoBake
{
    [InitializeOnLoadMethod]
    static void Register()
    {
        // 自定义命中规则：按目录 / 标签 / 导入设置决定，取代文件名后缀
        OutlineNormalsImportProcessor.ShouldBakeRule = (assetPath, importer) =>
            assetPath.StartsWith("Assets/Characters/");

        // 自定义存储：拿到网格与对象空间平滑法线，自行编码 / 写入
        OutlineNormalsImportProcessor.CustomStorageWriter = (mesh, smoothNormals) =>
        {
            // ……团队自己的写入逻辑，Shader 端按对应格式读取……
        };
    }
}
```

### 网格健康检查

生成 / 烘焙前，工具会扫描网格数据并汇报无法处理或影响结果的问题（缺法线、零向量 / NaN
法线、退化三角、单点重合顶点过多、未开启 Read/Write 等）。手动流程里，报告以卡片显示在
「生成」区域顶部（存在 Error 时生成前二次确认）；自动烘焙时写入 Console 日志，Error 网格
自动跳过。

---

### 关于 TEXCOORD 命名

共 8 个通道（`TEXCOORD0`–`TEXCOORD7`），一律以 **`TEXCOORDn`** 称呼，与 `mesh.SetUVs(n)`
的索引**恒等对应**。

不用「UV1 / UV2」这类叫法是有原因的：Unity 自己的 `mesh.uv2` 其实是 `TEXCOORD1`，
名字与索引天然差一位，极易搞错。

> ⚠️ **`TEXCOORD0` 就是模型的主贴图 UV**（`mesh.uv`）。写入它会毁掉贴图映射，
> 且影响所有引用该 sharedMesh 的对象。默认选的是 `TEXCOORD1`；若该网格的
> `TEXCOORD0` 确实空闲（如程序化网格）才可选用，工具会要求二次确认。

---

## 合并容差

「位置相同」的顶点会被合并平均。但接缝顶点经 DCC 导出、FBX 浮点截断或缩放后，
往往只差 1e-6 量级 —— 用精确相等判断的话它们不会合并，工具就在最该起作用的接缝上
静默失效了。因此提供**合并容差**（默认 `0.0001`）。

> **已知局限**：内部按容差把位置量化到整数格。恰好跨越格边界的两点仍会被分开。
> 要完全正确需邻格探查或并查集。取整是标准做法，代价是**容差必须远小于模型的
> 最小真实特征尺寸** —— 容差过大会把本应分开的顶点错误合并、导致描边变形。

---

## 数据状态

右侧「数据通道状态总览」显示各通道状态：

- `● 可能是平滑法线` — 强启发式命中
- `○ 有数据` — 有数据，但无法判断是不是平滑法线
- `✕ 空` — 该通道无数据

> 为什么最高只到「**可能**」：一个通道里装的到底是不是平滑法线，**从数据上不可
> 判定** —— 编码后就是一组普通数值，与任意顶点色 / 贴图 UV 无法区分。宣称能判定
> 就是在骗你。TEXCOORD 的判据相对可靠（本工具写 3 分量，贴图 UV 通常 2 分量），
> 顶点色则只报「有 / 无」。

---

## 数据安全

| 按钮 | 作用 |
|---|---|
| `保存` | 把**所有勾选**网格的改动写回各自 `.asset`。网格不可写时会**阻止**并说明原因，绝不谎报成功。 |
| `⧉ 另存为独立 Mesh…` | 把**所有勾选**的网格复制成可写的 `.asset`（勾 1 个弹命名框，勾多个选文件夹批量生成），场景对象自动回填组件。不可写网格唯一的出路。 |
| `↺ 还原本次修改` | 回退到本次生成 / 清除之前的状态。 |

> **本工具不依赖 Unity 的 Undo。** `Undo.RecordObject` 并不可靠地跟踪网格顶点数据，
> 对不可变的导入子资产更是完全无效。「还原本次修改」是工具自己的会话快照，
> 请以它为准 —— 关闭窗口或切换目标后快照即失效。

清除操作在各存储模式的面板内（顶点色可按通道对清除；切线可「重算切线」恢复出
真实切线；TEXCOORD 可逐通道清除）。

---

## 在游戏中使用描边

### 方法一：直接用 Sample 里的 Shader

1. 为模型新建材质，Shader 选 `OutlineSmoothNormalsGenerator/Outline URP`
   （或 `... /Outline Built-in`）。
2. 在 **Smooth Normal Source** 中选择与**生成时一致**的存储通道（顶点色 / 切线 /
   `TEXCOORD0`–`TEXCOORD7`）。顶点色模式还需把 **Vertex Color Channel** 设成与烘焙时相同的通道对。
3. 调整描边颜色与宽度。**宽度模式**可选 **屏幕空间**（等宽，不随距离变化）或 **世界空间**（按世界单位偏移，近大远小）。

`VertexNormal` 模式沿原始顶点法线外扩，即「未使用本工具」的对照效果，可用来直观对比。

Shader 是两个 Pass：`OUTLINE`（剔除正面的外扩描边）+ `FORWARD`（基础 NPR：
卡通两段式明暗 + 边缘光，可在材质面板调参）。材质面板的 **Base Color Mode** 还提供一组
**调试档位**，把平滑法线数据直接当颜色显示（顶点色 / 切线 / `UV0`–`UV7`，不经光照），
便于肉眼核对生成结果；生产时切回 **Base Map**。

### 方法二：把描边 Pass 并入你自己的 Shader（推荐）

实际项目通常有自己的主材质。把 Sample 里 Shader 的 **`OUTLINE` Pass** 整段复制进去即可。

---

## Shader 中读取平滑法线

**推荐直接 include 共享库**，不要自己抄一份解码 —— 那正是这个插件早期一系列
「预览与实际不一致」缺陷的根源。

```hlsl
#include "Packages/com.alefeng.outlinesmoothnormalsgenerator/Shader/OutlineSmoothNormals.hlsl"

// 按你烘焙时用的模式三选一：
float3 smoothNormalOS = OSN_DecodeVertexColor(v.color, _VCChannel);  // 顶点色（八面体）
float3 smoothNormalOS = OSN_DecodeTangent(v.tangent);                // 切线
float3 smoothNormalOS = OSN_DecodeTexCoord(v.uv1.xyz);               // TEXCOORD1

// 外扩（URP 写法；Built-in 把两个 Transform 换成
// UnityObjectToWorldNormal / UnityObjectToClipPos 即可）
float3 normalWS = TransformObjectToWorldNormal(smoothNormalOS);
float4 clipPos  = TransformObjectToHClip(v.positionOS.xyz);
o.positionCS    = OSN_ApplyOutlineOffset(clipPos, normalWS, _OutlineWidth);
```

`OSN_ApplyOutlineOffset` 内部做了三件容易写错的事：用逆转置矩阵变换法线（否则
非均匀缩放下描边会倾斜）、在**裁剪空间**取偏移方向（否则受 FOV / 宽高比影响）、
以及对零长度方向做保护（否则法线正对相机时 `normalize` 产生 NaN，GPU 会直接丢弃
整个三角形）。

### 不想 include 的话

切线与 TEXCOORD 模式存的就是对象空间方向，直接归一化即可：

```hlsl
float3 smoothNormalOS = normalize(v.tangent.xyz);  // 或 normalize(v.uv1.xyz)
```

顶点色模式是八面体编码，需要解码：

```hlsl
float3 OctDecode(float2 f)
{
    f = f * 2.0 - 1.0;
    float3 n = float3(f.x, f.y, 1.0 - abs(f.x) - abs(f.y));
    float  t = saturate(-n.z);
    n.xy += (n.xy >= 0.0) ? -t : t;
    return normalize(n);
}
// BA 通道对：
float3 smoothNormalOS = OctDecode(v.color.ba);
```

---

## 注意事项

- 平滑法线的计算基于**顶点位置合并**，容差默认 `0.0001`（见上文「合并容差」）。
- 修改 `sharedMesh` 会影响**所有**使用该网格的对象。只想作用于单个对象时，
  请先用「另存为独立 Mesh」复制一份。
- **切线模式会覆盖网格原始切线**，采样法线贴图的 Shader 将得到错误的 TBN。
- `Editor/Shader/OutlinePreview.shader` 仅供编辑器预览，不要用于生产。若它缺失
  （通常意味着包安装不完整），预览会退化为纯色、没有描边偏移。
- 存储格式为对象空间完整方向。用 `1.0.0` 之前的内部版本烘焙过的网格必须**重新烘焙**。

---

## 许可

MIT License — 自由使用于商业和非商业项目。
