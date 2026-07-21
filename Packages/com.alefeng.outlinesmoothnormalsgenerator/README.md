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

## 从 1.6.x 及更早升级

**只用顶点色或切线通道存储的项目不受影响**，可以跳过本节 —— 那两种格式一个字节没变。

`1.7.0` 把 **UV 通道**的存储格式从「三分量原始方向」改成了「两分量八面体编码」。
新版 Shader 无法解码旧数据，**且这件事在 GPU 侧检测不出来** —— 顶点装配会把缺失分量补 0，
两分量数据与「z 恰好为 0」的三分量数据在着色器里逐位相同，任何判据都存在真实反例。
所以没有自动兼容的余地，只能重新烘焙。

症状是**描边方向整体错乱**，不报任何错。

### 按你的接入方式对号入座

| 你的用法 | 升级包之后 | 你要做的 |
| --- | --- | --- |
| **用 Pass 模板**，或调用 `OSN_GetSmoothNormalOS` / `OSN_DecodeTexCoord` | 解码逻辑随包升级，**立刻开始按新格式解旧数据** | 重新烘焙一次 |
| **照「极简写法」手写** `normalize(v.uv1.xyz)` | **不受影响**，你没调本库的解码器 | ⚠ 见下方单独说明 |
| 用 `CustomStorageWriter` 委托自定义存储 | 不受影响，编解码全在你自己手里 | 无 |

> ⚠️ **手写 `normalize(v.uv1.xyz)` 的项目最容易踩坑**：升级包时一切正常，
> 于是很容易以为自己不在受影响之列 —— 但**只要重新烘焙一次就会坏**。
> 重烘之前请先把那一行改成 `OSN_OctDecode(v.uv1.xy)`，两件事必须一起做。

### 哪些网格会被自动迁移

提高了 `AssetPostprocessor` 的版本号，因此**开启了[导入时自动烘焙](#导入时自动烘焙)、
且经由模型导入器产出的网格**会在升级后自动重新导入并重烘。

**以下两类不会**，必须手动重烘一次：

- 用工具窗口**手动烘焙**的网格；
- **另存为独立 `.asset`** 的 Mesh（它们不是导入产物）。

### 怎么确认哪个网格还是旧格式

打开工具窗口选中网格，看右栏「数据通道状态总览」：分量数为 3 的 TEXCOORD 通道会被标成
**`▲ 可能是旧版格式`**，在该通道上重新生成一次即可迁移，材质不用动。

这只是**强提示而非判定** —— 网格合并会把 UV 维度统一取最大，其他把三分量方向写进 UV 的
工具（植被风场、VAT 等）同样会命中。反过来，迁移后的两分量数据与普通贴图 UV 在数值上
完全无法区分，只会显示「有数据」。

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
│   ├── Localization/                                ← 界面文案（中 / 英 / 日）
│   │   ├── OutlineLocale.cs                         ← 语言切换核心（EditorPrefs + 切换控件）
│   │   ├── LocWindow.cs                             ← 工具窗口文案
│   │   ├── LocValidator.cs                          ← 网格健康检查文案
│   │   ├── LocShaderGUI.cs                          ← 材质面板文案
│   │   └── LocLog.cs                                ← Console 日志文案 + 统一前缀
│   ├── Shader/OutlinePreview.shader                 ← 编辑器预览专用
│   └── OutlineSmoothNormalsGenerator.Editor.asmdef
├── Shader/                                          ← ★ 公开接口，可直接 include 进你的 Shader
│   ├── OutlineSmoothNormals.hlsl                    ← 解码 + 空间还原 + 外扩数学的【唯一真源】
│   ├── OutlinePassCommon.hlsl                       ← 描边 Pass 模板主体（不直接 include）
│   ├── OutlinePassURP.hlsl                          ← 描边 Pass 模板 · URP 适配层
│   ├── OutlinePassBuiltIn.hlsl                      ← 描边 Pass 模板 · Built-in 适配层
│   └── Demo/
│       └── OutlineNPR.hlsl                          ← Demo 专用 NPR 光照（卡通明暗 + 边缘光），生产用不到
└── Samples~/
    ├── URP/          → Sample「Outline Shader (URP) & Demo」
    └── BuiltIn/      → Sample「Outline Shader (Built-in RP) & Demo」
```

**`Shader/` 根目录下的四个文件是给你直接用的**，接入方式见[在游戏中使用描边](#在游戏中使用描边)。
只有 `Demo/` 子目录里的东西是「仅为把演示场景渲染得好看点」而存在的，生产 Shader 不需要碰。

`OutlineSmoothNormals.hlsl` 被编辑器预览、Pass 模板、以及两个 Demo 描边 Shader 共用。
解码数学只有这一份，因此「预览正常、实际渲染却不对」在结构上不可能发生 —— 你的 Shader
走 include 而非复制，同样是为了这一点：库升级时你的 Shader 跟着一起对。

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

选好[存储方式](#存储方式)（写进哪个通道）与[存储空间](#存储空间)（写在哪个空间，
**默认切线空间**，蒙皮模型必须用它）后，点 **`▶ 生成平滑法线`**，再点 **`保存`** 落盘。

> 这两项之后都要在材质上**选成一样的**，否则描边不对且不会报错。

### 5. 预览

窗口右侧就是实时预览（**内嵌**，不是单独的窗口），显示所有**勾选**的网格（多选时按各自在
层级中的相对位置同屏摆放，相机自动兜住全部；未勾选时显示占位提示）：

- 左键拖动旋转，滚轮缩放，中键平移
- 可开启 **显示平滑法线**（绿）与 **显示原始法线**（蓝）叠加对比（针对焦点网格）
- 可调描边宽度、颜色、模型光滑度 / 金属度、背景色

预览与实际渲染共用同一份解码与外扩数学，所见即所得。

---

## 界面详解

工具窗口顶部有两个页签：**平滑法线生成器**（手动流程）与 **导入自动烘焙**（见[导入时自动烘焙](#导入时自动烘焙)）。
本节自上而下说明「平滑法线生成器」页签的每个功能区 —— 布局对照上文[快速上手](#快速上手)里的截图：
**左栏是控制面板，右栏上半是数据通道状态、下半是实时预览与参数。**

### 界面语言

两个页签的标题区下方各有一个 **`中文` / `English` / `日本語`** 切换按钮，点哪个立刻切到哪个，
左右两栏、提示框、对话框、Console 日志与描边材质的 Inspector 一并跟着变。**默认中文。**

- 语言偏好存在 **`EditorPrefs`**（键 `OutlineSmoothNormals.Language`），是**按机器**保存的：
  不进版本管理，不会因为你切了语言就让协作者的工程产生 diff；换一个 Unity 工程打开本包，
  语言仍保持。
- 两个页签各画一个按钮，但选的是同一个值 —— 在哪边切都一样。
- 为表述准确，**英文术语在三种语言下始终原样保留**：`TEXCOORD1 (mesh.uv2)`、
  `SkinnedMeshRenderer`、`tangent.xyz`、`Read/Write` 这类标识不翻译；中文与日文的
  存储方式按钮也保留并列的英文名（如 `顶点色 / Vertex Color`、`頂点カラー / Vertex Color`）。
- 材质面板上 **`Smooth Normal Source` / `Vertex Color Channel` / `Smooth Normal Space` /
  `Base Color Mode` 四个属性在三种语言下都显示英文原名** —— 三份文档（含本文）
  都是按这些名字指路的，翻译了反而找不到。其余材质属性英文取 Shader 里声明的原名、
  日文用日语译名。

> 菜单路径 `Tools > Smooth Normal Generator` 无法本地化（Unity 的 `MenuItem` 要求常量字符串），
> 三种语言下都是英文。

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
三个页签 **顶点色 / 切线通道 / UV 通道**，决定平滑法线写到哪（取舍详见[存储方式](#存储方式)，按钮上悬停也有说明）：
- **顶点色**：选 `RG / GB / BA` 通道对；下方「顶点色通道数据状态」逐通道标出**将被覆盖写入**的分量（如 BA 模式下 B←八面体 X、A←八面体 Y），未选中的通道保持原值不动；`清除 RG / GB / BA` 按通道对抹掉数据。
- **切线通道**：把方向直接写进 `tangent.xyz`；⚠ 会覆盖原始切线，需要恢复时用面板里的「重算切线」重新算出真实切线。
- **UV 通道**：下拉选 `TEXCOORD0–7`（可逐通道清除）；选 `TEXCOORD0`（主贴图 UV）会二次确认。

### 左栏 · 存储空间
存储方式面板下方的两个页签 **对象空间 / 切线空间**，决定方向本身写在哪个空间里 ——
与「写到哪个通道」是**正交**的两件事（原理详见[存储空间](#存储空间)，按钮上悬停有详细说明）。

- **默认切线空间**，因为它对静态模型与蒙皮模型都正确。
- 选**切线通道**存储时整块置灰：那时切线本身就是数据，没有基可供重建，也无此必要。
- ⚠ 材质上的「存储空间」必须与这里**选一致**，否则描边整体偏斜且不会报任何错。

### 左栏 · 生成平滑法线
- **合并容差**滑杆（默认 `0.0001`，原理详见[合并容差](#合并容差)）。
- **▶ 生成平滑法线** 大按钮：文字实时显示当前存储通道与勾选数量（如「→ 顶点色 ×6」），点击即对所有勾选网格计算并写入 `sharedMesh`。
- 若焦点网格数据有问题，本区顶部会浮现**健康检查**卡片（详见[网格健康检查](#网格健康检查)）；存在 Error 时生成前会二次确认。

### 左栏底部 · 保存
固定在底部的 `↺ 还原本次修改`、`保存`、`⧉ 另存为独立 Mesh…` 三个按钮，作用与注意事项见[数据安全](#数据安全)。

### 右栏上 · 数据通道状态总览
顶点色 / 切线 / TEXCOORD 三张卡片，逐通道标注「有数据 / 空」等状态（判据与措辞详见[数据状态](#数据状态)）。始终反映**焦点**网格。

### 右栏下 · 描边预览与参数
内嵌实时预览：**左键旋转、滚轮缩放、中键平移**；左上角徽标（如「● 顶点色 · 切线空间」）**只读**地指示预览当前按哪个通道、哪个空间解码，跟随左侧的「存储方式」与「存储空间」。预览与生产 Shader 共用同一份解码 / 外扩数学，所见即所得。右侧参数面板自上而下：

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
| | **在 Scene 视图中显示** | 把上面两组法线同时画进 Scene 视图，详见下一节。 |
| **相机控制** | 水平旋转 / 垂直旋转 / 距离 | 精确设定视角（也可直接在视口拖拽 / 滚轮）。 |
| | 重置视角 | 复位角度并自动兜住所有勾选的网格。 |

> 内嵌预览里的叠加线段只画**焦点**网格；而预览渲染会显示**所有勾选**的网格（多选时按各自在层级中的相对位置摆放）。

### Scene 视图法线叠加

内嵌预览渲的是**绑定姿势**的静态网格，看不出蒙皮差异 —— 而[切线空间存储](#存储空间)的
全部意义就在于「蒙皮动画下描边不撕开」。勾上「**在 Scene 视图中显示**」即可在真实场景里
验证它：

- 作用于**所有勾选的场景网格**（直接选中的 Mesh 资产没有场景位置，会被跳过并提示）。
- `SkinnedMeshRenderer` 取**当前姿势** —— **播放动画**时法线应始终贴着表面走。
  关节处若像扇子一样散开，就是数据烘的空间与材质选的对不上。
- 顶点数过多时会自动抽稀，面板上会**如实写出**「已按 1/N 采样显示」。
- 每次 Scene 重绘都会重算，仅建议排查时打开。关闭工具窗口即自动停止绘制。

---

## 存储方式

平滑法线要写进网格的某块顶点数据里。三种方式存的都是**完整三维方向**，区别只在
「占用哪块数据、精度多少、跟什么冲突」—— 选型基本就是在回答「这个网格哪块数据是空的」。

| 存储方式 | 精度 | 占用 | 主要冲突 |
| --- | --- | --- | --- |
| **顶点色** | 约 1°（八面体编码，8-bit × 2） | 最省，2 个字节通道 | 覆盖选中的通道对；模型顶点色另作他用（AO / 遮罩 / 风力）时会撞 |
| **切线通道** | 3 个完整 float | 整条 `tangent` | ⚠ 覆盖原始切线 → **法线贴图失效** |
| **UV 通道** | 约 5e-6°（八面体编码，float × 2） | 一个 UV 通道（2 float / 顶点） | 最少；⚠ 但 `TEXCOORD0` 是主贴图 UV，写入会毁掉贴图映射 |

顶点色的 1° 误差远低于描边外扩能察觉的程度，**默认即可**。需要法线贴图就别用切线通道；
顶点色已被占用就换 `TEXCOORD1` 及以后的空闲通道。

> 八面体编码是全球面双射，不做半球压缩，因此没有符号歧义 —— 硬边角点上多个位置重合、
> 法线各异的顶点也能解码出同一条方向。这正是早期「存 XY + 重建 Z + 按法线定符号」方案
> 会在它本该修复的角上把描边裂开的原因。

> **UV 通道自 `1.7.0` 起改存两分量八面体**（此前是三分量原始方向）。两个 `float32` 的
> 八面体往返角度误差约 `5e-6°`，与直接存 `xyz` 的 `5e-7°` 同属浮点舍入噪声那一档，
> 换来每顶点省 4 字节。**`1.6.x` 及更早烘的 UV 数据在 `1.7.0` 下无法解码**，见
> [从 1.6.x 及更早升级](#从-16x-及更早升级)。

> ⚠️ 表里的精度是**编辑器内**的值。Unity 的 `Project Settings → Player → Vertex Compression`
> 默认会在**构建时**把切线与除 lightmap UV 外的 TEXCOORD 压到 `fp16`，届时切线通道与 UV 通道
> 的实际误差约 `6e-3°`（仍远优于顶点色的 1°，不影响选型）。顶点色本就是 8-bit，不受影响。

---

## 存储空间

与「写进哪个通道」**正交**的另一个维度：方向本身写在哪个空间里。

| 存储空间 | 存什么 | 适用 |
| --- | --- | --- |
| **对象空间** | 绑定姿势下的对象空间方向，解码即用 | 仅静态模型 |
| **切线空间** | 相对每个顶点自身 TBN 的坐标，解码时用**蒙皮后**的法线与切线重建 | 静态与蒙皮模型都正确（**默认**） |

### 为什么蒙皮模型需要切线空间

`SkinnedMeshRenderer` 蒙皮时，Unity 会变换 **POSITION / NORMAL / TANGENT**，但
**COLOR 与 TEXCOORD 原样传递、不参与蒙皮**。

于是顶点色 / TEXCOORD 里存的对象空间方向会「顶点跟着骨骼走、外扩方向却停在绑定姿势」，
关节一弯描边就撕开。

切线空间坐标则是**蒙皮不变量**：设 `S = a·T + b·B + c·N`，蒙皮对该顶点近似施加一个旋转
`R`，而 `N`、`T` 都被 Unity 一并变换，于是 `a·T' + b·B' + c·N' = R·S` —— 正是蒙皮后应有的
方向，而 `(a, b, c)` 恒定不变。这与法线贴图能在骨骼动画上正常工作是同一个道理。

### 使用要点

- **需要网格有合法切线**：模型导入设置里 `Tangents` 不能是 `None`。但它**不占用**切线，
  法线贴图照常可用 —— 切线在这里是「基」，不是存储位置。
- **切线通道存储不适用**：那会覆盖掉重建基所必需的切线本身。它也不需要 —— Unity 会把
  `tangent.xyz` 当方向一起蒙皮，存进去的对象空间方向天然跟随动画。工具里选中该模式时
  「存储空间」会自动置灰。
- ⚠ **材质必须同步选一致**。两种空间存的都只是一条单位方向，从数据本身分辨不出来，
  选错不报错、只是描边整体偏斜。
- ⚠ **重新导入模型可能失配**：烘焙用的是当时的法线 / 切线数据，若之后以不同的切线生成
  方式重新导入，已烘数据会静默失配。**因此切线空间推荐配合[导入时自动烘焙](#导入时自动烘焙)使用**
  —— 烘焙发生在导入管线内、切线生成之后，天然同源。
- 精度不受影响：编码的是单位方向，换个空间还是单位方向，顶点色仍是约 1°。

> UV 退化处（三个 UV 共线或重合）切线为零向量或与法线共线，构不成正交基。这类顶点会被
> [网格健康检查](#网格健康检查)按比例报出，描边在其上退化为沿原始顶点法线外扩。

---

## 导入时自动烘焙

![「导入自动烘焙」页签：启用开关、命中规则、存储方式与合并容差，配置存于 ProjectSettings/](./Docs~/Images/tool_auto.png)

除了上面的手动流程，工具还能在模型**导入时自动烘焙**：命中规则的模型一旦（重）导入，
平滑法线就被自动写进网格 —— 无需手动跑工具、无需另存独立 Mesh。

**非破坏性**：写入发生在导入过程中、针对正在导入的网格，随导入产物落盘；改成不再命中或
关闭开关后重新导入，即恢复原始网格。

### 开启与配置

打开工具窗口顶部的 **「导入自动烘焙」页签**：

| 配置 | 说明 |
|---|---|
| **启用导入时自动烘焙** | 总开关，默认关闭。 |
| **命中规则** | 两个可独立勾选的条件，详见下一节。 |
| **存储方式** | 顶点色 / 切线通道 / `TEXCOORD0`–`7`，与手动流程含义一致；Shader 端要选同一通道。详见[存储方式](#存储方式)。 |
| **存储空间** | 对象空间 / 切线空间，默认切线空间；Shader 端要选同一空间。详见[存储空间](#存储空间)。选切线通道存储时该项不适用、自动置灰。 |
| **合并容差** | 同下文「合并容差」一节。 |

配置持久化到 `ProjectSettings/OutlineSmoothNormals.asset`，随工程纳入版本管理，团队共享一致设置。

> 改了配置后，已导入的模型不会自动重烘 —— 对它们重新导入（右键 `Reimport`）一次即可。

### 命中规则

两个条件各自可勾选，**同时勾选时取交集**（都满足才烘焙）：

| 条件 | 说明 |
|---|---|
| **按文件名后缀** | 文件名（不含扩展名）以指定后缀结尾，大小写不敏感。默认开启，后缀默认 `_Outline`，如 `Hero_Outline.fbx`。 |
| **按文件夹路径** | 资产位于指定文件夹**及其子目录**之下。用对象槽把文件夹拖进去即可。 |

例：两个都勾、后缀 `_Outline`、文件夹 `Assets/Characters`，则只有
`Assets/Characters/**/Hero_Outline.fbx` 这样的模型会被烘焙。

- 文件夹用**路径前缀**匹配到目录分隔符为止，因此 `Assets/Characters2/`、
  `Assets/Old/Characters_backup/` 这类同级目录**不会**被误命中。
- 任一条件**留空即视为不命中**（后缀为空、或未指定文件夹）；**两个都不勾也不命中任何模型**。
  空配置绝不会被解读成「命中一切」—— 否则在你还没填完设置的那一刻，全工程的模型就已经被改写了。
- 把模型**改名加上后缀**或**拖进目标文件夹**都不会触发 Unity 重导入，工具会侦测到这类
  移动并自动补一次重导入。

### 扩展钩子（进阶）

内置的「命中规则 + 三种存储」覆盖不了的私有管线，可用两个 static 委托接管，空则回退
默认。一般用 `[InitializeOnLoadMethod]` 在加载时赋值一次：

```csharp
using UnityEditor;
using OutlineSmoothNormalsGenerator;

static class MyOutlineAutoBake
{
    [InitializeOnLoadMethod]
    static void Register()
    {
        // 自定义命中规则：按标签 / 导入设置等决定，取代内置的后缀与文件夹条件
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
- `▲ 可能是旧版格式` — 仅 TEXCOORD：分量数为 3，疑似 `1.6.x` 及更早烘的数据，需重新烘焙
- `○ 有数据` — 有数据，但无法判断是不是平滑法线
- `✕ 空` — 该通道无数据

> 为什么最高只到「**可能**」：一个通道里装的到底是不是平滑法线，**从数据上不可
> 判定** —— 编码后就是一组普通数值，与任意顶点色 / 贴图 UV 无法区分。宣称能判定
> 就是在骗你。
>
> `1.7.0` 之后 TEXCOORD 的情况变了：本工具写两分量，而贴图 UV 通常也是两分量，
> 于是**新格式的数据只能报「有数据」**。原本用来认平滑法线的那个信号
> （3 分量 + 近似单位长）反倒成了「这是旧版格式」的提示 —— 但它同样只是
> 强提示，网格合并会把 UV 维度统一取最大，别的把三分量方向写进 UV 的工具也会命中。
> 顶点色一如既往只报「有 / 无」。

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
2. 在 **Smooth Normal Source** 中选择与**生成时一致**的存储通道（顶点色 / 切线通道 /
   `TEXCOORD0`–`TEXCOORD7`）。顶点色模式还需把 **Vertex Color Channel** 设成与烘焙时相同的通道对。
3. 把 **Smooth Normal Space** 设成与生成时一致的[存储空间](#存储空间)（默认 `Tangent Space`）。
   ⚠ 这一项选错**不会报错**，只是描边整体偏斜 —— 排查描边异常时请先确认它。
4. 调整描边颜色与宽度。**宽度模式**可选 **屏幕空间**（等宽，不随距离变化）或 **世界空间**（按世界单位偏移，近大远小）。

`VertexNormal` 模式沿原始顶点法线外扩，即「未使用本工具」的对照效果，可用来直观对比。

Shader 是两个 Pass：`OUTLINE`（剔除正面的外扩描边）+ `FORWARD`（基础 NPR：
卡通两段式明暗 + 边缘光，可在材质面板调参）。材质面板的 **Base Color Mode** 还提供一组
**调试档位**，把平滑法线数据直接当颜色显示（顶点色 / 切线 / `UV0`–`UV7`，不经光照），
便于肉眼核对生成结果；生产时切回 **Base Map**。

### 方法二：用 Pass 模板接入你自己的 Shader（推荐）

实际项目通常有自己的主材质。包内提供了现成的 `OUTLINE` Pass 模板，**接入只需两步、
不必抄任何解码代码**；后续库升级时你的 Shader 跟着一起更新，不用再手动补。

Demo 的两个描边 Shader 走的就是这条路径 —— 模板出问题，Demo 会第一时间暴露。

#### 第 1 步：加上描边的 6 个属性

ShaderLab 不支持宏，`Properties` 这段只能复制：

```shaderlab
[Header(Outline)]
_OutlineColor   ("Outline Color", Color) = (0,0,0,1)
[PowerSlider(3.0)]
_OutlineWidth   ("Outline Width", Range(0, 0.1)) = 0.015
[Enum(Screen Space, 0, World Space, 1)]
_OutlineWidthMode ("Outline Width Mode", Float) = 0
_SmoothNormalSrc ("Smooth Normal Source", Float) = 0
[Enum(RG, 0, GB, 1, BA, 2)]
_VCChannel      ("Vertex Color Channel", Float) = 2
[Enum(Object Space, 0, Tangent Space, 1)]
_SmoothNormalSpace ("Smooth Normal Space", Float) = 1
```

`_SmoothNormalSrc` 有 11 档、超过 `[KeywordEnum]` 上限，所以没写 `[Enum]`。想要下拉
菜单就把 Shader 末尾的 `CustomEditor` 设为本包的自定义 Inspector：

```shaderlab
CustomEditor "OutlineSmoothNormalsGenerator.OutlineShaderGUI"
```

#### 第 2 步：加一个 Pass

**URP** —— 整段复制：

```shaderlab
Pass
{
    Name "OUTLINE"
    Tags { "LightMode" = "SRPDefaultUnlit" }

    Cull Front          // 只画背面，露出的边缘即描边
    ZWrite On
    ZTest LEqual

    HLSLPROGRAM
    #pragma vertex   OSN_OutlineVert
    #pragma fragment OSN_OutlineFrag

    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.alefeng.outlinesmoothnormalsgenerator/Shader/OutlineSmoothNormals.hlsl"

    CBUFFER_START(UnityPerMaterial)
        // ↓↓ 你自己的材质属性，必须与其他 Pass 的 CBUFFER 完全一致
        float4 _BaseColor;
        float4 _BaseMap_ST;
        // ↑↑
        OSN_OUTLINE_MATERIAL_FIELDS
    CBUFFER_END

    #include "Packages/com.alefeng.outlinesmoothnormalsgenerator/Shader/OutlinePassURP.hlsl"
    ENDHLSL
}
```

**Built-in** —— 整段复制：

```shaderlab
Pass
{
    Name "OUTLINE"
    Tags { "LightMode" = "Always" }

    Cull Front
    ZWrite On
    ZTest LEqual

    CGPROGRAM
    #pragma vertex   OSN_OutlineVert
    #pragma fragment OSN_OutlineFrag

    #include "UnityCG.cginc"
    #include "Packages/com.alefeng.outlinesmoothnormalsgenerator/Shader/OutlineSmoothNormals.hlsl"

    // Built-in 没有 SRP Batcher，直接当普通 uniform 声明即可，不需要 CBUFFER。
    OSN_OUTLINE_MATERIAL_FIELDS

    #include "Packages/com.alefeng.outlinesmoothnormalsgenerator/Shader/OutlinePassBuiltIn.hlsl"
    ENDCG
}
```

#### 四个必须注意的点

- **include 顺序不能变。** `OutlineSmoothNormals.hlsl` 要在属性声明**之前**（声明用到了
  它里面的宏），Pass 模板要在**之后**（模板里的顶点着色器要用到这些 uniform）。顺序错了
  直接编译报错。

- **SRP Batcher（仅 URP）：`UnityPerMaterial` 各 Pass 必须逐字一致。** 少写一项、顺序不同
  都会让 batcher **静默失效** —— 不报错、只掉性能，最难查。所以描边属性要用
  `OSN_OUTLINE_MATERIAL_FIELDS` 拼进那**唯一一份** CBUFFER，并且**你的每个 Pass 都要
  加这个宏**、放在相同位置。

- **描边 Pass 要排在基础渲染 Pass 之前。** 它 `Cull Front + ZWrite On`，先写好背面深度，
  正面才能正常盖住内侧。

- **`LightMode`**：URP 用 `SRPDefaultUnlit`，描边 Pass 会被自动渲染，无需 Renderer Feature；
  Built-in 用 `Always`，表示该 Pass 与光照无关、每个物体只渲染一次（用 `ForwardBase`
  会让它参与逐光源渲染而被重复绘制）。

只想换描边颜色的算法（比如让描边随贴图变化）？别改模板 —— 自己写一个 frag，
`#pragma fragment` 指向它即可，`OSN_OutlineVert` 照用。

---

## Shader 中读取平滑法线

上面的 Pass 模板已经够用。本节写给**需要完全掌控顶点着色器**的人 —— 比如描边要叠加
顶点动画、或者项目对顶点带宽敏感、不愿声明模板里那 8 个 TEXCOORD。

**无论如何都建议 include 共享库**，不要自己抄一份解码 —— 那正是这个插件早期一系列
「预览与实际不一致」缺陷的根源。

### 通用写法（材质上可运行时切换存储方式）

```hlsl
#include "Packages/com.alefeng.outlinesmoothnormalsgenerator/Shader/OutlineSmoothNormals.hlsl"

// ① 取平滑法线：解码 + 空间还原一次完成
float3 smoothNormalOS = OSN_GetSmoothNormalOS(
    _SmoothNormalSrc, _SmoothNormalSpace, v.color, v.tangent,
    v.uv0.xyz, v.uv1.xyz, v.uv2.xyz, v.uv3.xyz,
    v.uv4.xyz, v.uv5.xyz, v.uv6.xyz, v.uv7.xyz,
    v.normal, _VCChannel);

// ② 外扩（URP 写法；Built-in 把两个 Transform 换成
//    UnityObjectToWorldNormal / UnityObjectToClipPos 即可）
float3 normalWS = TransformObjectToWorldNormal(smoothNormalOS);
float4 clipPos  = TransformObjectToHClip(v.positionOS.xyz);
o.positionCS    = OSN_ApplyOutlineOffset(clipPos, normalWS, _OutlineWidth, _OutlineWidthMode);
```

`OSN_GetSmoothNormalOS` 内部是「解码 → 按存储空间还原」两步。这两步**必须成对出现**，
而漏掉后一步不会报任何错、只是描边整体偏斜 —— 所以合成了一个函数。若你确实需要分开调用，
它们分别是 `OSN_SelectSmoothNormalOS` 与 `OSN_ResolveSmoothNormalSpace`。

`OSN_ApplyOutlineOffset` 内部做了三件容易写错的事：用逆转置矩阵变换法线（否则非均匀
缩放下描边会倾斜）、在**裁剪空间**取偏移方向（否则受 FOV / 宽高比影响）、以及对零长度
方向做保护（否则法线正对相机时 `normalize` 产生 NaN，GPU 会直接丢弃整个三角形）。
最后一个参数是宽度模式：`0` 屏幕空间（等宽）、`1` 世界空间（近大远小）。

### 极简写法（存储方式写死在 Shader 里）

生产项目往往全项目统一一种存储方式。这时不必保留材质上的运行时切换，也就不必声明
8 个 TEXCOORD —— 直接调对应的解码器，零分支：

```hlsl
// 例：全项目统一用「顶点色 BA + 切线空间」
// 顶点输入只需要 POSITION / NORMAL / TANGENT / COLOR，UV 一个都不用声明
float3 smoothNormalOS = OSN_OctDecode(v.color.ba);                       // 顶点色 BA 解码
smoothNormalOS = OSN_TangentToObject(smoothNormalOS, v.normal, v.tangent); // 切线空间 → 对象空间
```

对象空间存储则省掉第二行。换成 TEXCOORD 存储就把第一行换成 `OSN_OctDecode(v.uv1.xy)`
（该通道存的是两分量八面体，与顶点色同一套编码，只是精度高得多）；换成切线通道存储
换成 `normalize(v.tangent.xyz)`（那种模式恒为对象空间，也不需要第二行）。

> ⚠️ **从 `1.6.x` 及更早升级的项目注意**：这一行此前是 `normalize(v.uv1.xyz)`。
> 因为你没有调用本库的解码器，升级包**不会**让它立刻出错 —— 但**一旦重新烘焙就会坏**。
> 改成 `OSN_OctDecode(v.uv1.xy)` 与重新烘焙这两件事必须一起做，
> 详见[从 1.6.x 及更早升级](#从-16x-及更早升级)。

材质面板上的 `Smooth Normal Source` / `Smooth Normal Space` 此时形同虚设，可以不声明
这两个属性 —— 但**务必在 Shader 里注释写明你写死的是哪种组合**，否则日后换存储方式时
无从查起。

### 完全不想 include 的话

只有切线通道模式**在对象空间存储下**能直接归一化拿到方向：

```hlsl
float3 smoothNormalOS = normalize(v.tangent.xyz);
```

顶点色与 TEXCOORD 都是八面体编码，需要解码 —— 两者共用同一个函数，只是取值的位置和
精度不同（顶点色是 8-bit × 2，TEXCOORD 是 float × 2）：

```hlsl
float3 OctDecode(float2 f)
{
    f = f * 2.0 - 1.0;
    float3 n = float3(f.x, f.y, 1.0 - abs(f.x) - abs(f.y));
    float  t = saturate(-n.z);
    n.xy += (n.xy >= 0.0) ? -t : t;
    return normalize(n);
}

float3 smoothNormalOS = OctDecode(v.color.ba);  // 顶点色 BA 通道对
float3 smoothNormalOS = OctDecode(v.uv1.xy);    // 或 TEXCOORD1
```

⚠ 若烘焙时用的是默认的**切线空间**，还得自己重建 TBN 还原一次 —— 那正是
`OSN_TangentToObject` 做的事，自己抄容易在 Gram-Schmidt 正交化和手性 `tangent.w` 上出错。

**这条路不推荐**，而且已经有两次前车之鉴：抄出去的代码不随库升级，`1.5.0` 新增存储空间时
所有手抄的 Shader 都得手动补一步，漏了不报错、只是描边悄悄歪掉；`1.7.0` 改了 TEXCOORD 的
编码格式，手抄 `normalize(v.uv1.xyz)` 的项目同样得手动改成上面这个写法。

---

## 注意事项

- 平滑法线的计算基于**顶点位置合并**，容差默认 `0.0001`（见上文「合并容差」）。
- 修改 `sharedMesh` 会影响**所有**使用该网格的对象。只想作用于单个对象时，
  请先用「另存为独立 Mesh」复制一份。
- **切线通道模式会覆盖网格原始切线**，采样法线贴图的 Shader 将得到错误的 TBN。
- `Editor/Shader/OutlinePreview.shader` 仅供编辑器预览，不要用于生产。若它缺失
  （通常意味着包安装不完整），预览会退化为纯色、没有描边偏移。
- 存储的一律是**完整三维方向**，不做半球压缩；写在对象空间还是切线空间由
  [存储空间](#存储空间)决定。
- **`1.7.0` 起 TEXCOORD 通道存两分量八面体**，与贴图 UV 在维度和值域上都无法区分，
  因此通道状态只能报「有数据」；反过来「分量数为 3」成了识别旧版数据的提示，
  但同样只是强提示而非判定（见[数据状态](#数据状态)）。
- ⚠ **从 `1.6.x` 及更早升级**：UV 存储的数据格式已变更，必须重新烘焙 ——
  完整迁移步骤见[从 1.6.x 及更早升级](#从-16x-及更早升级)。只用顶点色或切线通道的项目不受影响。
- ⚠ **从 `1.4.x` 升级**：`1.5.0` 起存储空间默认切线空间，材质的 **Smooth Normal Space**
  也随之默认 `Tangent Space`，而旧数据都是按对象空间烘的 —— 请**重新烘焙一次**，
  或把材质该项改回 `Object Space`。走[导入时自动烘焙](#导入时自动烘焙)的模型会自动重烘，无需干预。
- 用 `1.0.0` 之前的内部版本烘焙过的网格必须**重新烘焙**。

---

## 许可

MIT License — 自由使用于商业和非商业项目。
