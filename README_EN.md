<!-- ![](Documents/banner.gif) Effect banner (GIF / image placeholder — add your own) -->

<p align="center">
  <img alt="GitHub Release" src="https://img.shields.io/github/v/release/AleFeng/OutlineSmoothNormalsGenerator?color=blue">
  <img alt="GitHub Downloads (all assets, all releases)" src="https://img.shields.io/github/downloads/AleFeng/OutlineSmoothNormalsGenerator/total?color=green">
  <img alt="Unity Version" src="https://img.shields.io/badge/Unity-2022.3%2B-black?logo=unity">
  <img alt="GitHub Repo License" src="https://img.shields.io/badge/license-MIT-blueviolet">
  <img alt="GitHub Repo Issues" src="https://img.shields.io/github/issues/AleFeng/OutlineSmoothNormalsGenerator?color=yellow">
</p>

<p align="center">
  🌍
  <a href="./README.md">中文</a> |
  English |
  <a href="./README_JA.md">日本語</a>
</p>

<p align="center">
  📥
  <a href="#-installation">Installation</a> |
  <a href="#-quick-start">Quick Start</a> |
  <a href="Assets/Plugins/OutlineSmoothNormalsGenerator/README.md">Full Docs</a>
</p>

# Outline Smooth Normals Generator
Outline Smooth Normals Generator (internally named `SmoothNormalTool`) is a `Unity` **editor tool** that automatically computes **smooth normals** for a mesh and bakes them into the vertex color, tangent, or a UV channel.  
It specifically solves the problem of **backface-outline cracking at hard edges**: toon / anime styles commonly generate outlines by "duplicating the model, extruding along the normals, and rendering only the back faces." But at cube corners, mechanical edges and other places where **normals are split**, the per-vertex normal directions are discontinuous, so the outline breaks apart and shows gaps.  
This tool takes the **vertices that share the same position** and computes a single **continuous extrusion direction** from an angle-weighted average of their face normals (the smooth normal). The original normals are kept for shading, while the outline closes smoothly along the model's silhouette.  
The tool itself is pure editor C# and is **render-pipeline agnostic** (Built-in / URP / HDRP can all be used to generate the data). It also ships with a Built-in RP outline shader and a live preview window, ready to use out of the box.

<!-- ![](Documents/outline_compare.png) Left: plain-normal outline (cracked hard edges)  Right: smooth-normal outline (continuous & closed) -->

## 📜 Table of Contents
- [Introduction](#introduction)
  - [Features](#features)
  - [Why Smooth Normals](#why-smooth-normals)
- [💻 Requirements](#-requirements)
- [📦 Installation](#-installation)
- [🚀 Quick Start](#-quick-start)
  - [1. Open the Tool](#1-open-the-tool)
  - [2. Pick a Target and Storage Mode](#2-pick-a-target-and-storage-mode)
  - [3. Generate and Preview](#3-generate-and-preview)
  - [4. Save](#4-save)
- [🧩 Three Storage Modes](#-three-storage-modes)
- [🎨 Using the Outline In-Game](#-using-the-outline-in-game)
- [📖 Full Documentation](#-full-documentation)
- [📁 Project Structure](#-project-structure)
- [📋 Roadmap](#-roadmap)
- [📄 License](#-license)

## Introduction
Outlines are one of the most common needs in toon rendering. The most universal implementation is the **backface-extrusion method**: the model is "inflated" outward along its vertex normals, and only the back faces are rendered, so the exposed rim becomes the outline.  
This approach is simple and efficient, but it depends heavily on the **continuity** of vertex normals. When a model has hard edges (adjacent faces sharing a position but with different normals, i.e. split normals), a single position carries several normals pointing in different directions; after extrusion they spread apart, and the outline **cracks and breaks** at corners.  

Outline Smooth Normals Generator solves this with an editor workflow:

1. **Compute smooth normals** — group the mesh vertices that share the **same position**, and take an **angle-weighted average** of the face normals they belong to, producing a single continuous "extrusion direction" that spans across hard edges.
2. **Bake into the mesh** — write the smooth normal into one of **vertex color / tangent / UV**, without touching the original normals used for lighting.
3. **Sample & extrude in the shader** — the outline shader reads this smooth normal in the vertex stage and applies a screen-space, uniform-width offset, yielding a smooth, closed outline.

The whole process happens inside the editor, with **live preview, normal-visualization comparison, channel-status checks, Undo, and per-channel clearing**.

<!-- ![](Documents/window_overview.png) Main window: parameters on the left + data overview & live preview on the right -->

### Features
| Feature | Description |
| --- | --- |
| Angle-weighted smooth normals | Groups vertices by "equal position" and averages face normals weighted by angle, producing a continuous extrusion direction across hard edges that eliminates outline cracking at the source. Also auto-corrects winding orientation for back-facing / double-sided meshes. |
| Three storage modes | **Vertex color** (selectable RG / GB / BA channel pair), **tangent space** (`tangent.xyz`), and **UV channels** (UV1–UV4). Choose freely based on your pipeline and which channels are already occupied. |
| Live outline preview | Embedded preview viewport with left-drag orbit / scroll zoom / middle-drag pan; adjust outline width, color, plus model smoothness / metallic / base color and background color in real time. |
| Normal visualization compare | Overlay both the "smooth normal" and "original normal" line segments at once to directly compare the direction difference at hard edges and validate the result instantly. |
| Data channel overview | Shows in real time whether each channel is "● has smooth normals / ○ has raw data / ✕ empty", so you don't accidentally overwrite existing vertex-color or UV data. |
| Mesh info panel | At-a-glance vertex count, triangle count, sub-mesh count, and whether normals / tangents / vertex colors / each UV channel are present. |
| Non-destructive & undoable | Fully Undo-friendly; supports **per-channel clearing** and one-click **saving back to the mesh asset** (`.fbx` / `.asset`). |
| Built-in outline shader | Ships a Built-in RP two-pass outline shader (outline + forward lighting) with a custom material inspector, and lets you switch between the three normal sources with one click. |
| Broad compatibility | Supports both `MeshFilter` and `SkinnedMeshRenderer`; the generation logic is render-pipeline agnostic. |
| Localized UI | The editor UI ships in Chinese, with parameter hints and status indicators. |

### Why Smooth Normals
- **Plain-normal outline**: at hard edges the per-vertex normal directions disagree, so after extrusion the outline is misaligned and cracked — most visible at corners.
- **Smooth-normal outline**: vertices at the same position share one extrusion direction, so the outline closes smoothly along the silhouette; meanwhile shading still uses the original normals, so **normal lighting and normal maps are unaffected**.

The smooth normal is therefore stored only as extra "extrusion direction" data in a spare channel — a low-cost, non-intrusive solution.

## 💻 Requirements
- `Unity 2022.3` or newer (this repository is currently maintained on `Unity 6000.3`).
- The **generation tool** (smooth-normal computation and writing) is pure editor C# and **does not depend on any render pipeline** — Built-in / URP / HDRP can all be used to bake the data.
- The **bundled `Outline.shader` targets the Built-in Render Pipeline**. For URP / HDRP, follow the [full documentation](Assets/Plugins/OutlineSmoothNormalsGenerator/README.md) to port the outline Pass into your pipeline's shader — the storage and decode logic is universal, only the render Pass needs adapting.
- `OutlinePreview.shader` is for in-editor preview only; do not use it in production.

## 📦 Installation
The tool is a drop-in `Assets` plugin (not a UPM package). Pick any method:

**Option 1: Copy the folder**
1. Download or clone this repository.
2. Copy the entire `Assets/Plugins/OutlineSmoothNormalsGenerator` folder anywhere under your project's `Assets/`.

**Option 2: Download a package**
1. Download the latest package from the [Releases](https://github.com/AleFeng/OutlineSmoothNormalsGenerator/releases) page.
2. Import the `.unitypackage` into your project.

**Option 3: Clone the whole repo**
```
git clone git@github.com:AleFeng/OutlineSmoothNormalsGenerator.git
```

Unity compiles it automatically with no extra dependencies. Once installed, the menu bar shows **`Tools → Smooth Normal Generator`**.

## 🚀 Quick Start
Below is the shortest path. **The complete parameter reference and shader sampling code are in the [full documentation](Assets/Plugins/OutlineSmoothNormalsGenerator/README.md).**

### 1. Open the Tool
Menu bar → `Tools → Smooth Normal Generator` to open the "Smooth Normal Generator" window.

### 2. Pick a Target and Storage Mode
- In the **Hierarchy**, select an object with a `MeshFilter` or `SkinnedMeshRenderer` and the tool reads it automatically; or drag it into the "Target Object" field manually.
- In "Storage Mode", choose **Vertex Color / Tangent Space / UV Channel** (see [Three Storage Modes](#-three-storage-modes)). The "Data Channel Overview" on the right tells you whether the target channel already holds data.

### 3. Generate and Preview
- Click **`▶ Generate Smooth Normals`** to write the data into `sharedMesh` (Undo supported).
- Inspect the result live in the "Outline Preview" viewport: left-drag to orbit, scroll to zoom, middle-drag to pan, and toggle "Show Smooth Normals / Original Normals" to overlay and compare.

### 4. Save
Click the **`Save`** button at the bottom-left to write the changes back to the mesh asset file (`.fbx` / `.asset`).

> ⚠️ Modifying `sharedMesh` affects every object referencing that mesh. To affect a single object only, duplicate the mesh first.

<!-- ![](Documents/quickstart.gif) Quick start: select → generate → preview -->

## 🧩 Three Storage Modes
The smooth normal's XY components are packed into one channel, and the Z component is reconstructed in the shader via `sqrt`, so it only occupies two float components.

| Mode | Storage location | When to use |
| --- | --- | --- |
| **Vertex Color** | RG / GB / **BA** (default) channel pair of `color` | The first choice when vertex color isn't used by other effects; lowest read cost. |
| **Tangent Space** | `tangent.xyz` (`tangent.w` keeps the flip flag) | When you need compatibility with the standard tangent-space workflow / normal maps. |
| **UV Channel** | `xy` of `UV1–UV4` | When vertex color / tangent is already occupied, or you need higher precision. |

> The three modes are independent and can be cleared selectively; the tool detects existing data to avoid accidental overwrites.

## 🎨 Using the Outline In-Game
The simplest way is to use the built-in **`SmoothNormalTool/Outline`** (Built-in RP, two passes: Pass0 backface-extrusion outline + Pass1 forward lighting):

1. Create a new material for the model and set its shader to `SmoothNormalTool/Outline`.
2. In the material inspector's "Smooth Normal Source", pick the **same** storage channel you used when generating.
3. Adjust outline color and width (screen-space uniform-width offset that doesn't change with distance).

If you use your own main material, just copy the **OUTLINE Pass** from `Outline.shader` into it; URP / HDRP users do the same, porting that Pass to their pipeline's syntax. The per-channel shader decode code is in the [full documentation](Assets/Plugins/OutlineSmoothNormalsGenerator/README.md#shader-中读取平滑法线).

## 📖 Full Documentation
This README is the overall introduction and quick start. **The complete usage guide** — details of each storage mode, channel selection, shader sampling code, clearing data, caveats, etc. — lives in the in-plugin documentation:

👉 **[Assets/Plugins/OutlineSmoothNormalsGenerator/README.md](Assets/Plugins/OutlineSmoothNormalsGenerator/README.md)** (Chinese)

## 📁 Project Structure
```
Assets/Plugins/OutlineSmoothNormalsGenerator/
├── Editor/
│   ├── SmoothNormalGeneratorWindow.cs   ← Main editor window (with embedded live preview)
│   ├── SmoothNormalCalculator.cs        ← Smooth-normal core (angle-weighted averaging)
│   ├── StorageWriter.cs                 ← Data writing (vertex color / tangent / UV)
│   ├── OutlineShaderGUI.cs              ← Custom material inspector for the outline shader
│   ├── Shader/
│   │   ├── Outline.shader               ← Production outline shader (three modes, Built-in RP)
│   │   └── OutlinePreview.shader        ← Editor-only preview shader
│   └── SmoothNormalTool.Editor.asmdef
└── README.md                            ← Detailed usage docs
```

## 📋 Roadmap
- Provide URP / HDRP versions of the outline shader.
- Batch processing of multiple meshes / entire folders.
- More outline styles (depth-aware width, per-material colors, etc.).

## 📄 License
This project is open-sourced under the [MIT License](LICENSE) and is free for commercial and non-commercial use.
