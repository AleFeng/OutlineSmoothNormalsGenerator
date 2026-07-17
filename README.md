<!-- ![](Documents/banner.gif) 效果横幅（GIF / 图片占位，可自行补充） -->

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
  <a href="Assets/Plugins/OutlineSmoothNormalsGenerator/README.md">详细文档</a>
</p>

# Outline Smooth Normals Generator - 平滑法线描边生成器
Outline Smooth Normals Generator（内部命名 `SmoothNormalTool`）是一款面向 `Unity` 的**编辑器工具**，用于为网格自动计算**平滑法线**，并烘焙到顶点色、切线或 UV 通道中。  
它专门解决**背面外扩描边（Backface Outline）在硬边处断裂**的问题：卡通 / 二次元风格常用「复制模型、沿法线外扩、只渲染背面」的方式生成描边，但在立方体棱角、机械边缘等**法线被拆分（Split Normals）** 的位置，逐顶点法线方向不连续，描边会出现裂缝与断口。  
本工具通过把**同一位置的顶点**按角度加权平均出一条**连续的外扩方向**（平滑法线），在保留原始法线用于着色的同时，让描边沿模型轮廓平滑闭合。  
工具本体为纯 Editor C#，**与渲染管线无关**（Built-in / URP / HDRP 均可用于生成数据）；同时内置一套 Built-in RP 的描边 Shader 与实时预览窗口，开箱即用。

<!-- ![](Documents/outline_compare.png) 左：普通法线描边（硬边断裂）  右：平滑法线描边（连续闭合） -->

## 📜 目录
- [简介](#简介)
  - [项目特性](#项目特性)
  - [为什么需要平滑法线](#为什么需要平滑法线)
- [💻 环境要求](#-环境要求)
- [📦 安装](#-安装)
- [🚀 快速开始](#-快速开始)
  - [1. 打开工具](#1-打开工具)
  - [2. 选择目标与存储方式](#2-选择目标与存储方式)
  - [3. 生成并预览](#3-生成并预览)
  - [4. 保存](#4-保存)
- [🧩 三种存储方式](#-三种存储方式)
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

<!-- ![](Documents/window_overview.png) 工具主界面：左侧参数 + 右侧数据总览与实时预览 -->

### 项目特性
| 特性 | 描述 |
| --- | --- |
| 角度加权平滑法线 | 按顶点「位置相等」分组，对面法线按夹角加权平均，得到跨硬边连续的外扩方向，从根本上消除描边断裂。并自动修正背面 / 双面网格的绕序朝向。 |
| 三种存储方式 | **顶点色**（RG / GB / BA 通道对可选）、**切线空间**（`tangent.xyz`）、**UV 通道**（UV1~UV4）。可按管线需求与已占用通道自由选择。 |
| 实时描边预览 | 内嵌预览视口，左键旋转 / 滚轮缩放 / 中键平移；实时调节描边宽度、颜色、模型光滑度 / 金属度 / 基础色与背景色。 |
| 法线可视化对比 | 可同时叠加绘制「平滑法线」与「原始法线」线段，直观对比硬边处的方向差异，即时验证生成效果。 |
| 数据通道状态总览 | 实时显示各通道是「● 含平滑法线 / ○ 有原始数据 / ✕ 空」，避免误覆盖已有的顶点色或 UV 数据。 |
| 网格信息面板 | 一览顶点数、三角面数、SubMesh 数，以及是否含法线 / 切线 / 顶点色 / 各 UV 通道。 |
| 非破坏 & 可撤销 | 全流程支持 Undo，可**单通道清除**数据，并一键**保存回 Mesh 资源**（`.fbx` / `.asset`）。 |
| 内置描边 Shader | 附带 Built-in RP 两 Pass 描边 Shader（描边 + 前向光照）与自定义材质面板，支持三种法线来源一键切换。 |
| 广泛兼容 | 同时支持 `MeshFilter` 与 `SkinnedMeshRenderer`；生成逻辑与渲染管线无关。 |
| 中文界面 | 编辑器 UI 全中文，参数含说明与状态提示。 |

### 为什么需要平滑法线
- **普通法线描边**：在硬边处逐顶点法线方向不一致，外扩后描边错位、开裂，棱角处尤其明显。
- **平滑法线描边**：位置相同的顶点共享同一条外扩方向，描边沿轮廓平滑闭合；而着色仍使用原始法线，**不影响正常光照与法线贴图**。

因此平滑法线只作为「外扩方向」的额外数据存进空闲通道，是一种低成本、非侵入的解决方案。

## 💻 环境要求
- `Unity 2022.3` 或更新版本（本仓库当前基于 `Unity 6000.3` 维护）。
- **生成工具**（平滑法线计算与写入）为纯 Editor C#，**不依赖任何渲染管线**，Built-in / URP / HDRP 均可用于烘焙数据。
- **内置的 `Outline.shader` 为 Built-in Render Pipeline 版本**。若使用 URP / HDRP，请参照[详细文档](Assets/Plugins/OutlineSmoothNormalsGenerator/README.md)将描边 Pass 移植到你的管线 Shader 中——存储与解码逻辑通用，仅渲染 Pass 需适配。
- `OutlinePreview.shader` 仅用于编辑器内预览，请勿用于生产。

## 📦 安装
本工具是一个即拷即用的 `Assets` 内插件（非 UPM 包），选择任一方式安装：

**方式一：拷贝文件夹**
1. 下载或克隆本仓库。
2. 将 `Assets/Plugins/OutlineSmoothNormalsGenerator` 整个文件夹拷贝到你的项目 `Assets/` 下的任意位置。

**方式二：下载安装包**
1. 在 [Releases](https://github.com/AleFeng/OutlineSmoothNormalsGenerator/releases) 页面下载最新安装包。
2. 将 `.unitypackage` 导入你的项目。

**方式三：克隆整个仓库**
```
git clone git@github.com:AleFeng/OutlineSmoothNormalsGenerator.git
```

Unity 会自动编译，无需额外依赖。安装成功后，菜单栏会出现 **`Tools → Smooth Normal Generator`**。

## 🚀 快速开始
下面是最短路径的使用流程，**更完整的参数说明与 Shader 采样代码见 [详细文档](Assets/Plugins/OutlineSmoothNormalsGenerator/README.md)**。

### 1. 打开工具
菜单栏 → `Tools → Smooth Normal Generator`，打开「平滑法线生成器」窗口。

### 2. 选择目标与存储方式
- 在 **Hierarchy** 中点选一个含 `MeshFilter` 或 `SkinnedMeshRenderer` 的对象，工具会自动读取；也可手动拖入「目标对象」字段。
- 在「存储方式」中选择 **顶点色 / 切线空间 / UV 通道**（详见 [三种存储方式](#-三种存储方式)）。右侧「数据通道状态总览」会提示目标通道是否已有数据。

### 3. 生成并预览
- 点击 **`▶ 生成平滑法线`**，数据即写入 `sharedMesh`（支持 Undo）。
- 在右侧「描边预览」视口实时查看效果：左键旋转、滚轮缩放、中键平移，并可开启「显示平滑法线 / 原始法线」叠加对比。

### 4. 保存
点击左下角 **`保存`** 按钮，将修改写回 Mesh 资源文件（`.fbx` / `.asset`）。

> ⚠️ 修改 `sharedMesh` 会影响所有引用该网格的对象。若只想作用于单个对象，建议先复制一份 Mesh。

<!-- ![](Documents/quickstart.gif) 快速开始：选择 → 生成 → 预览 -->

## 🧩 三种存储方式
平滑法线的 XY 分量被压缩存入一处通道，Z 分量在 Shader 中通过 `sqrt` 重建，因此只占用两个浮点分量。

| 模式 | 存储位置 | 适用场景 |
| --- | --- | --- |
| **顶点色 Vertex Color** | `color` 的 RG / GB / **BA**（默认）通道对 | 顶点色未被其他效果占用时的首选，读取成本最低。 |
| **切线空间 Tangent Space** | `tangent.xyz`（`tangent.w` 保留翻转标记） | 需要与标准切线空间流程 / 法线贴图兼容时。 |
| **UV 通道 UV Channel** | `UV1~UV4` 的 `xy` | 顶点色 / 切线已被占用，或需要更高精度时。 |

> 三种方式互相独立、可选择性清除，工具会检测并避免误覆盖已有数据。

## 🎨 在游戏中使用描边
最简单的方式是直接使用内置的 **`SmoothNormalTool/Outline`**（Built-in RP，两 Pass：Pass0 背面外扩描边 + Pass1 前向光照）：

1. 为模型新建材质，Shader 选择 `SmoothNormalTool/Outline`。
2. 在材质面板的「平滑法线来源」中选择与**生成时一致**的存储通道。
3. 调整描边颜色与宽度（屏幕空间等宽偏移，不随距离变化）。

如果你使用自己的主材质，只需把 `Outline.shader` 的 **OUTLINE Pass** 复制进去即可；URP / HDRP 用户同理，把该 Pass 移植为对应管线写法。各通道的 Shader 解码代码见[详细文档](Assets/Plugins/OutlineSmoothNormalsGenerator/README.md#shader-中读取平滑法线)。

## 📖 详细文档
本 README 面向整体介绍与快速上手。**完整的使用说明**——每种存储方式的细节、通道选择、Shader 采样代码、清除数据、注意事项等——请见插件内文档：

👉 **[Assets/Plugins/OutlineSmoothNormalsGenerator/README.md](Assets/Plugins/OutlineSmoothNormalsGenerator/README.md)**

## 📁 目录结构
```
Assets/Plugins/OutlineSmoothNormalsGenerator/
├── Editor/
│   ├── SmoothNormalGeneratorWindow.cs   ← 主编辑器窗口（含内嵌实时预览）
│   ├── SmoothNormalCalculator.cs        ← 平滑法线计算核心（角度加权平均）
│   ├── StorageWriter.cs                 ← 数据写入（顶点色 / 切线 / UV）
│   ├── OutlineShaderGUI.cs              ← 描边材质自定义 Inspector
│   ├── Shader/
│   │   ├── Outline.shader               ← 生产用描边 Shader（三种模式，Built-in RP）
│   │   └── OutlinePreview.shader        ← 编辑器预览专用 Shader
│   └── SmoothNormalTool.Editor.asmdef
└── README.md                            ← 详细使用文档
```

## 📋 待办事项
- 提供 URP / HDRP 版本的描边 Shader。
- 批量处理多个网格 / 整个文件夹。
- 更多描边样式（深度感知宽度、按材质分色等）。

## 📄 许可
本项目基于 [MIT License](LICENSE) 开源，可自由用于商业与非商业项目。
