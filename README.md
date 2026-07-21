![alt text](./Packages/com.alefeng.outlinesmoothnormalsgenerator/Docs~/Images/banner.png)

<p align="center">
  <img alt="GitHub Release" src="https://img.shields.io/github/v/release/AleFeng/OutlineSmoothNormalsGenerator?color=blue">
  <img alt="GitHub Downloads (all assets, all releases)" src="https://img.shields.io/github/downloads/AleFeng/OutlineSmoothNormalsGenerator/total?color=green">
  <img alt="Unity Version" src="https://img.shields.io/badge/Unity-2022.3%2B-black?logo=unity">
  <img alt="GitHub Repo License" src="https://img.shields.io/badge/license-MIT-blueviolet">
  <img alt="GitHub Repo Issues" src="https://img.shields.io/github/issues/AleFeng/OutlineSmoothNormalsGenerator?color=yellow">
</p>

<p align="center">
  🌍
  中文 |
  <a href="./README_EN.md">English</a> |
  <a href="./README_JA.md">日本語</a>
</p>

<p align="center">
  📥
  <a href="#-安装">安装</a> |
  <a href="#-快速开始">快速开始</a> |
  <a href="Packages/com.alefeng.outlinesmoothnormalsgenerator/README.md">详细文档</a>
</p>

# Outline Smooth Normals Generator - 平滑法线描边生成器
Outline Smooth Normals Generator 是一款面向 `Unity` 的**编辑器工具**，用于为网格自动计算**平滑法线**，并烘焙到顶点色、切线或 UV 通道中。  
它专门解决**背面外扩描边（Backface Outline）在硬边处断裂**的问题：卡通 / 二次元风格常用「复制模型、沿法线外扩、只渲染背面」的方式生成描边，但在立方体棱角、机械边缘等**法线被拆分（Split Normals）** 的位置，逐顶点法线方向不连续，描边会出现裂缝与断口。  
本工具通过把**同一位置的顶点**按角度加权平均出一条**连续的外扩方向**（平滑法线），在保留原始法线用于着色的同时，让描边沿模型轮廓平滑闭合。  
工具本体为纯 Editor C#，**与渲染管线无关**；描边 Shader 按管线以 Sample 形式分别提供（URP / Built-in），按需导入，因此插件包本身不引入任何管线依赖。工具内嵌实时描边预览，所见即所得。

![alt text](./Packages/com.alefeng.outlinesmoothnormalsgenerator/Docs~/Images/comp_cube.png)

## 📜 目录
- [Outline Smooth Normals Generator - 平滑法线描边生成器](#outline-smooth-normals-generator---平滑法线描边生成器)
  - [📜 目录](#-目录)
  - [简介](#简介)
    - [项目特性](#项目特性)
    - [为什么需要平滑法线](#为什么需要平滑法线)
  - [💻 环境要求](#-环境要求)
  - [📦 安装](#-安装)
    - [使用 UPM（推荐）](#使用-upm推荐)
    - [导入描边 Shader（必需）](#导入描边-shader必需)
    - [其他方式](#其他方式)
  - [🚀 快速开始](#-快速开始)
    - [1. 打开工具](#1-打开工具)
    - [2. 选择目标与存储方式](#2-选择目标与存储方式)
    - [3. 生成并预览](#3-生成并预览)
    - [4. 保存](#4-保存)
  - [📥 导入时自动烘焙](#-导入时自动烘焙)
  - [🧩 三种存储方式](#-三种存储方式)
  - [🧭 存储空间](#-存储空间)
  - [🎨 在游戏中使用描边](#-在游戏中使用描边)
  - [📖 详细文档](#-详细文档)
  - [📁 目录结构](#-目录结构)
  - [📋 待办事项](#-待办事项)
  - [📄 许可](#-许可)

## 简介
描边（Outline）是卡通渲染中最常见的需求之一。最通用的实现是**背面外扩法**：把模型沿顶点法线方向向外「膨胀」一圈，并只渲染背面，露出的部分即为描边。  
这种方式简单高效，但强依赖顶点法线的**连续性**。当模型存在硬边（相邻面共享位置但法线不同，即 Split Normals）时，同一个位置会有多条朝向不同的法线，外扩后彼此错开，描边就会在棱角处**开裂、断线**。  

Outline Smooth Normals Generator 通过一套编辑器工具解决这个问题：

1. **计算平滑法线** —— 把网格中**位置相同**的顶点视为一组，对它们所属面的法线做**角度加权平均**，得到一条跨越硬边、连续一致的「外扩方向」。
2. **烘焙进网格** —— 把平滑法线写入**顶点色 / 切线 / UV** 中的一处，不改动用于光照的原始法线。
3. **Shader 采样外扩** —— 描边 Shader 在顶点阶段读取这条平滑法线做屏幕空间等宽偏移，得到平滑闭合的描边。

整个过程在编辑器内完成，支持**实时预览、法线可视化对比、通道状态检查、Undo 与单通道清除**。

![alt text](./Packages/com.alefeng.outlinesmoothnormalsgenerator/Docs~/Images/tool_generate.png)

### 项目特性
| 特性 | 描述 |
| --- | --- |
| 角度加权平滑法线 | 按顶点「位置相等」分组，对面法线按夹角加权平均，得到跨硬边连续的外扩方向，从根本上消除描边断裂。并自动修正背面 / 双面网格的绕序朝向。 |
| 三种存储方式 | **顶点色**（RG / GB / BA 通道对可选，八面体编码）、**切线通道**（`tangent.xyz`）、**TEXCOORD0–7**（8 个通道，八面体编码）。存的都是完整三维方向，不做半球压缩，无符号歧义。 |
| 存储空间 | 与「存进哪个通道」正交的另一维度：方向可写在**对象空间**或**切线空间**（默认切线空间）。切线空间坐标是**蒙皮不变量**，解决顶点色 / TEXCOORD 不参与蒙皮导致的 `SkinnedMeshRenderer` **描边撕裂**；不占用切线，法线贴图照常可用。 |
| 导入时自动烘焙 | 命中规则 ——**文件名后缀**（默认 `_Outline`）与**文件夹路径**，各自可勾选、同时勾选取交集 —— 的模型在（重）导入时自动烘焙平滑法线，**非破坏性**、无需手动操作。工具窗口「导入自动烘焙」页签配置（存 `ProjectSettings/`），另有自定义命中规则 / 自定义存储两个扩展委托。 |
| 网格健康检查 | 生成 / 烘焙前扫描并汇报缺法线、退化三角、NaN、单点重合顶点过多、未开启 Read/Write 等问题；Error 生成前二次确认，自动烘焙时自动跳过。 |
| 实时描边预览 | 内嵌预览视口，左键旋转 / 滚轮缩放 / 中键平移；实时调节描边宽度、颜色、模型光滑度 / 金属度 / 基础色与背景色。 |
| 法线可视化对比 | 可同时叠加绘制「平滑法线」与「原始法线」线段，直观对比硬边处的方向差异，即时验证生成效果。 |
| Scene 视图法线叠加 | 把上述两组线段画到场景中的真实对象上；`SkinnedMeshRenderer` 按**当前姿势**求值，因而可在动画播放时验证切线空间数据。顶点数多时自动抽稀，并在面板上标明采样比例。 |
| 数据通道状态总览 | 实时显示各通道是「● 含平滑法线 / ○ 有原始数据 / ✕ 空」，避免误覆盖已有的顶点色或 UV 数据。 |
| 网格信息面板 | 一览顶点数、三角面数、SubMesh 数，以及是否含法线 / 切线 / 顶点色 / 各 UV 通道。 |
| 数据安全 | 会**阻止对只读导入资产的假保存**，并提供「另存为独立 Mesh」一键复制可写 `.asset` 并回填到对象；另有会话快照「还原本次修改」。 |
| 合并容差 | 接缝顶点经 DCC 导出 / FBX 浮点截断后往往差 1e-6 量级，容差（默认 0.0001）让它们仍能正确合并。 |
| 描边 Shader（Sample） | 按管线分别提供 URP / Built-in 两版，两 Pass（描边 + 基础 NPR 着色）+ 自定义材质面板，可切换法线来源与描边宽度模式（屏幕 / 世界空间）。 |
| 广泛兼容 | 目标可以是场景对象（`MeshFilter` / `SkinnedMeshRenderer`），也可以是 Project 里的 Mesh / 模型 / 预制体资产；生成逻辑与渲染管线无关。 |
| 三语界面 | 编辑器 UI 支持**简体中文 / English / 日本語**，两个页签的标题区各有一个切换按钮，默认中文。语言偏好存 `EditorPrefs`（按机器保存，不进版本管理）。为表述准确，`TEXCOORD1`、`SkinnedMeshRenderer`、`Smooth Normal Space` 等英文术语在三种语言下**始终原样保留**。参数含说明与状态提示。 |

### 为什么需要平滑法线
- **普通法线描边**：在硬边处逐顶点法线方向不一致，外扩后描边错位、开裂，棱角处尤其明显。
- **平滑法线描边**：位置相同的顶点共享同一条外扩方向，描边沿轮廓平滑闭合；而着色仍使用原始法线，**不影响正常光照与法线贴图**。

因此平滑法线只作为「外扩方向」的额外数据存进空闲通道，是一种低成本、非侵入的解决方案。

## 💻 环境要求
- `Unity 2022.3` 或更新版本（已在 `2022.3` 与 `6000.3` 实测；本仓库基于 `6000.3` 维护）。
- **生成工具**（平滑法线计算与写入）为纯 Editor C#，**不依赖任何渲染管线**，Built-in / URP / HDRP 均可用于烘焙数据。
- **描边 Shader 按管线以 Sample 提供**（URP / Built-in），核心包不含 Shader，因此不引入任何管线依赖。HDRP 暂未提供，可参照[详细文档](Packages/com.alefeng.outlinesmoothnormalsgenerator/README.md)自行移植描边 Pass —— 解码逻辑通用，仅渲染 Pass 需适配。
- `OutlinePreview.shader` 仅用于编辑器内预览，请勿用于生产。

## 📦 安装
### 使用 UPM（推荐）
`Window > Package Manager` → 左上角 `+` → `Install package from git URL...` → 粘贴：

```
https://github.com/AleFeng/OutlineSmoothNormalsGenerator.git?path=/Packages/com.alefeng.outlinesmoothnormalsgenerator
```

这样装的是 `main` 的最新提交。**要固定版本，把 `#<tag>` 加在整条 URL 的最末尾**（必须在 `?path=` 之后）：

```
https://github.com/AleFeng/OutlineSmoothNormalsGenerator.git?path=/Packages/com.alefeng.outlinesmoothnormalsgenerator#1.8.1
```

可用的 tag 见 [Releases](https://github.com/AleFeng/OutlineSmoothNormalsGenerator/releases)。

### 导入描边 Shader（必需）
核心包**不含**描边 Shader。装好后在 Package Manager 里选中本包 → `Samples` →
**按你的项目管线导入其中一个**：

| Sample | Shader 名 |
| --- | --- |
| Outline Shader (URP) & Demo | `OutlineSmoothNormalsGenerator/Outline URP` |
| Outline Shader (Built-in RP) & Demo | `OutlineSmoothNormalsGenerator/Outline Built-in` |

两者可以共存（名字不同），但一个项目只有一个管线，通常只需导入对应的那个。
每个 Sample 都自带一个对照演示场景。

### 其他方式
也可以下载仓库，把 `Packages/com.alefeng.outlinesmoothnormalsgenerator` 整个文件夹拷进
你项目的 **`Packages/` 目录**（不是 `Assets/`）—— Unity 会自动把它识别为本地包。

此时 `Samples~` 不会被 Unity 导入（`~` 结尾的目录会被忽略），需要手动把
`Samples~/URP/`（或 `Samples~/BuiltIn/`）里的内容拷到 `Assets/` 下任意位置。
**Shader 无需改动** —— 它引用共享库用的 `Packages/…` 路径在这种放法下同样成立。

安装成功后，菜单栏会出现 **`Tools → Smooth Normal Generator`**。

## 🚀 快速开始
下面是最短路径的使用流程，**更完整的参数说明与 Shader 采样代码见 [详细文档](Packages/com.alefeng.outlinesmoothnormalsgenerator/README.md)**。

### 1. 打开工具
菜单栏 → `Tools → Smooth Normal Generator`，打开「平滑法线生成器」窗口。

### 2. 选择目标与存储方式
- 选择目标，工具会自动读取。目标可以是：**场景对象**（含 `MeshFilter` / `SkinnedMeshRenderer`），或 Project 里的 **Mesh 资产**、**模型**（`.fbx` 等）、**预制体**；也可手动拖入「目标」字段。选中场景对象、模型或预制体时，工具会**遍历整个层级**收集其中全部网格；含多个网格时，目标区列出**复选框列表**（顶部「全选 / 清空」，默认全选），可**勾选多个网格一起处理** —— 「生成 / 保存 / 另存为」作用于全部勾选项，预览也同屏显示全部勾选的网格。
- 在「存储方式」中选择 **顶点色 / 切线通道 / TEXCOORD**（详见 [三种存储方式](#-三种存储方式)）。右侧「数据通道状态总览」会提示目标通道是否已有数据。
- 下方的「存储空间」保持默认的**切线空间**即可（详见 [存储空间](#-存储空间)）—— 蒙皮模型必须用它。⚠ 「存储方式」与「存储空间」这两项之后都要在材质上选成一致，否则描边不对且不会报错。

### 3. 生成并预览
- 点击 **`▶ 生成平滑法线`**，数据即写入 `sharedMesh`。
- 在右侧「描边预览」视口实时查看效果：左键旋转、滚轮缩放、中键平移，并可开启「显示平滑法线 / 原始法线」叠加对比。预览与实际渲染共用同一份解码数学，所见即所得。
- 若结果不理想，可点 **`↺ 还原本次修改`** 回退。

### 4. 保存
点击左下角 **`保存`** 按钮，把修改写回 `.asset`。

> ⚠️ **`.fbx` 等模型文件里的网格是只读的导入子资产** —— 写进去的数据会在下次重导入时丢失。工具会检测并阻止保存，请先点 **`⧉ 另存为独立 Mesh…`** 复制一份可写的 `.asset`。选中的是**场景对象**时它会自动替换到对象上；选中的是**资产**（Mesh / 模型 / 预制体）时只生成独立 `.asset`，请自行引用。
>
> ⚠️ 修改 `sharedMesh` 会影响所有引用该网格的对象。只想作用于单个对象时，同样用「另存为独立 Mesh」。

<!-- ![](Documents/quickstart.gif) 快速开始：选择 → 生成 → 预览 -->

## 📥 导入时自动烘焙
除了上面的手动流程，工具还能在模型**导入时自动烘焙**：把模型文件名改成带约定后缀（默认 `_Outline`，如 `Hero_Outline.fbx`），它一旦（重）导入，平滑法线就被自动写进网格 —— 无需打开工具、无需另存独立 Mesh。**非破坏性**：去掉后缀或关闭开关后重新导入，即恢复原始网格。

在工具窗口顶部的 **「导入自动烘焙」页签** 里开启并配置：启用开关、命中规则（**文件名后缀**与**文件夹路径**，各自可勾选、同时勾选取交集）、存储方式（顶点色 / 切线通道 / `TEXCOORD0`–`7`）、[存储空间](#-存储空间)（对象空间 / 切线空间，默认切线空间；选切线通道存储时不适用、自动置灰）、合并容差。配置持久化到 `ProjectSettings/OutlineSmoothNormals.asset`，随工程纳入版本管理、团队共享一致设置。

![导入自动烘焙页签](./Packages/com.alefeng.outlinesmoothnormalsgenerator/Docs~/Images/tool_auto.png)

生成 / 烘焙前还会做**网格健康检查**（缺法线、退化三角、NaN、单点重合顶点过多等），有问题即时告警、Error 的网格自动跳过。若要按目录 / 标签接入私有管线，或改用自定义存储格式，可用两个扩展委托接管 —— 详见[详细文档](Packages/com.alefeng.outlinesmoothnormalsgenerator/README.md#导入时自动烘焙)。

## 🧩 三种存储方式
平滑法线存的是**完整三维方向**，不做半球压缩 —— 因此没有符号歧义，硬边角点也不会解码错。方向本身写在对象空间还是切线空间可选，**默认切线空间**（见[存储空间](#-存储空间)）。

| 模式 | 存储位置 | 适用场景 |
| --- | --- | --- |
| **顶点色 Vertex Color** | `color` 的 RG / GB / **BA**（默认）通道对，八面体编码 | 顶点色空闲时的首选。2 个 8-bit 分量，误差约 1°。 |
| **切线通道 Tangent Channel** | `tangent.xyz`（`w` 恒为 1） | 完整 float 精度。⚠ **会覆盖原始切线、破坏法线贴图**，仅在该网格不用法线贴图时选用。 |
| **TEXCOORD** | `TEXCOORD0`–`TEXCOORD7` 的 `xy`，八面体编码 | 顶点色被占用时的推荐选择，共 8 个通道。2 个 float 分量，误差约 5e-6°。 |

> 通道一律以 `TEXCOORDn` 称呼，与 `mesh.SetUVs(n)` 索引恒等对应 —— Unity 自己的 `mesh.uv2` 其实是 `TEXCOORD1`，用「UV1/UV2」的叫法极易差一位。
>
> ⚠️ **`TEXCOORD0` 就是主贴图 UV**，写入会毁掉贴图映射。默认选 `TEXCOORD1`；确需写入 `TEXCOORD0` 时工具会要求二次确认。

## 🧭 存储空间
与「存进哪个通道」**正交**的另一个维度：方向本身写在哪个空间里。

| 存储空间 | 存什么 | 适用 |
| --- | --- | --- |
| **对象空间 Object Space** | 绑定姿势下的对象空间方向，解码即用 | 仅静态模型 |
| **切线空间 Tangent Space** | 相对每个顶点自身 TBN 的坐标，解码时用**蒙皮后**的法线与切线重建 | 静态与蒙皮模型都正确（**默认**） |

`SkinnedMeshRenderer` 蒙皮时 Unity 只变换 POSITION / NORMAL / TANGENT，**COLOR 与 TEXCOORD 原样传递、不参与蒙皮** —— 存在这两处的对象空间方向会停在绑定姿势，关节一弯描边就撕开。切线空间坐标是蒙皮不变量，因此成为默认。它**不占用**切线（切线只作重建用的「基」），法线贴图照常可用，但要求模型导入设置里 `Tangents` ≠ `None`；选**切线通道**存储时该选项不适用、自动置灰。

> ⚠️ 工具与材质上的「存储空间」必须**选成一致**，否则描边整体偏斜且不会报任何错。

> ⚠️ **从 `1.4.x` 升级的破坏性变更**：`1.5.0` 起存储空间默认切线空间，材质的 **Smooth Normal Space** 也随之默认 `Tangent Space`，而旧数据都是按对象空间烘的。二选一修正：**重新烘焙一次**（推荐，顺带获得蒙皮支持），或把材质该项改回 `Object Space`。走「导入时自动烘焙」的模型会自动重烘，无需干预。详见[详细文档](Packages/com.alefeng.outlinesmoothnormalsgenerator/README.md#存储空间)。

## 🎨 在游戏中使用描边
从 Sample 导入对应管线的 Shader 后（两 Pass：Pass0 背面外扩描边 + Pass1 基础 NPR 着色）：

1. 为模型新建材质，Shader 选择 `OutlineSmoothNormalsGenerator/Outline URP`（或 `... /Outline Built-in`）。
2. 在材质面板的 **Smooth Normal Source** 中选择与**生成时一致**的存储通道；顶点色模式还需把 **Vertex Color Channel** 设成相同的通道对。
3. 把 **Smooth Normal Space** 设成与生成时一致的[存储空间](#-存储空间)（默认 `Tangent Space`）。⚠ 这一项选错**不会报错**，只是描边整体偏斜。
4. 调整描边颜色与宽度。**宽度模式**可选 **屏幕空间**（等宽，不随距离变化）或 **世界空间**（按世界单位偏移，近大远小）。

`VertexNormal` 模式沿原始顶点法线外扩，即「未使用本工具」的对照效果，可直观对比。

如果你使用自己的主材质，包内提供了现成的 **`OUTLINE` Pass 模板**，接入只需两步：把 6 个描边属性加进 `Properties`，再复制一段十来行的 `Pass{}`（URP 与 Built-in 各一份）—— 不必抄任何解码代码，后续库升级时你的 Shader 会跟着一起更新。Demo 的两个描边 Shader 走的就是这条路径。完整步骤、注意事项，以及需要完全掌控顶点着色器时的写法，见[详细文档](Packages/com.alefeng.outlinesmoothnormalsgenerator/README.md#在游戏中使用描边)。

## 📖 详细文档
本 README 面向整体介绍与快速上手。**完整的使用说明**——每种存储方式的细节、通道选择、Shader 采样代码、清除数据、注意事项等——请见插件内文档：

👉 **[Packages/com.alefeng.outlinesmoothnormalsgenerator/README.md](Packages/com.alefeng.outlinesmoothnormalsgenerator/README.md)**

## 📁 目录结构
```
Packages/com.alefeng.outlinesmoothnormalsgenerator/     ← 包根
├── package.json  CHANGELOG.md  LICENSE.md
├── README.md                                     ← 详细使用文档
├── Editor/
│   ├── OutlineSmoothNormalsGeneratorWindow.cs    ← 主窗口（含内嵌实时预览）
│   ├── OutlineSmoothNormalsCalculator.cs         ← 平滑法线计算（角度加权 + 容差合并）
│   ├── OutlineSmoothNormalsCodec.cs              ← 存储格式编解码（.hlsl 的 C# 镜像）
│   ├── StorageWriter.cs                          ← 写入顶点色 / 切线 / TEXCOORD
│   ├── OutlineShaderGUI.cs                       ← 描边材质自定义 Inspector
│   ├── Shader/OutlinePreview.shader              ← 编辑器预览专用
│   └── OutlineSmoothNormalsGenerator.Editor.asmdef
├── Shader/
│   └── OutlineSmoothNormals.hlsl                 ← 解码 + 外扩数学的【唯一真源】
└── Samples~/
    ├── URP/                                      ← Sample：URP 描边 Shader + 演示场景
    └── BuiltIn/                                  ← Sample：Built-in 描边 Shader + 演示场景
```

> `OutlineSmoothNormals.hlsl` 被编辑器预览与两个管线的描边 Shader 共用。解码数学只有这一份，因此「预览正常、实际渲染却不对」在结构上不可能发生。

## 📋 待办事项
- 提供 HDRP 版本的描边 Shader。
- 批量处理多个网格 / 整个文件夹。
- 更多描边样式（深度感知宽度、按材质分色等）。

## 📄 许可
本项目基于 [MIT License](LICENSE) 开源，可自由用于商业与非商业项目。
