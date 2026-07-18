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
  <a href="Packages/com.alefeng.outlinesmoothnormalsgenerator/README.md">Full Docs</a>
</p>

# Outline Smooth Normals Generator
Outline Smooth Normals Generator is a `Unity` **editor tool** that automatically computes **smooth normals** for a mesh and bakes them into the vertex color, tangent, or a UV channel.  
It specifically solves the problem of **backface-outline cracking at hard edges**: toon / anime styles commonly generate outlines by "duplicating the model, extruding along the normals, and rendering only the back faces." But at cube corners, mechanical edges and other places where **normals are split**, the per-vertex normal directions are discontinuous, so the outline breaks apart and shows gaps.  
This tool takes the **vertices that share the same position** and computes a single **continuous extrusion direction** from an angle-weighted average of their face normals (the smooth normal). The original normals are kept for shading, while the outline closes smoothly along the model's silhouette.  
The tool itself is pure editor C# and is **render-pipeline agnostic**. The outline shaders are shipped per-pipeline as Samples (URP / Built-in) — import the one you need — so the package itself pulls in no pipeline dependency. A live outline preview is embedded in the tool window: what you see is what you get.

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
| Three storage modes | **Vertex color** (selectable RG / GB / BA channel pair, octahedral-encoded), **tangent** (`tangent.xyz`), and **TEXCOORD0–7** (8 channels). All store a full object-space direction — no compression ambiguity. |
| Live outline preview | Embedded preview viewport with left-drag orbit / scroll zoom / middle-drag pan; adjust outline width, color, plus model smoothness / metallic / base color and background color in real time. |
| Normal visualization compare | Overlay both the "smooth normal" and "original normal" line segments at once to directly compare the direction difference at hard edges and validate the result instantly. |
| Data channel overview | Shows in real time whether each channel is "● has smooth normals / ○ has raw data / ✕ empty", so you don't accidentally overwrite existing vertex-color or UV data. |
| Mesh info panel | At-a-glance vertex count, triangle count, sub-mesh count, and whether normals / tangents / vertex colors / each UV channel are present. |
| Data safety | **Blocks fake saves to read-only imported assets**, and offers "Duplicate to standalone Mesh" — one click copies a writable `.asset` and reassigns it onto the object. Plus a session snapshot: "Revert this change". |
| Merge tolerance | Seam vertices typically differ by ~1e-6 after DCC export / FBX float truncation; the tolerance (default 0.0001) still merges them correctly. |
| Outline shaders (Samples) | URP and Built-in versions shipped separately, each two-pass (outline + basic NPR shading) with a custom material inspector; switch the normal source and the outline width mode (screen / world space). |
| Broad compatibility | The target can be a scene object (`MeshFilter` / `SkinnedMeshRenderer`) or a Mesh / model / prefab asset in the Project; the generation logic is render-pipeline agnostic. |
| Localized UI | The editor UI ships in Chinese, with parameter hints and status indicators. |

### Why Smooth Normals
- **Plain-normal outline**: at hard edges the per-vertex normal directions disagree, so after extrusion the outline is misaligned and cracked — most visible at corners.
- **Smooth-normal outline**: vertices at the same position share one extrusion direction, so the outline closes smoothly along the silhouette; meanwhile shading still uses the original normals, so **normal lighting and normal maps are unaffected**.

The smooth normal is therefore stored only as extra "extrusion direction" data in a spare channel — a low-cost, non-intrusive solution.

## 💻 Requirements
- `Unity 2022.3` or newer (verified on `2022.3` and `6000.3`; this repository is maintained on `6000.3`).
- The **generation tool** (smooth-normal computation and writing) is pure editor C# and **does not depend on any render pipeline** — Built-in / URP / HDRP can all be used to bake the data.
- **The outline shaders ship per-pipeline as Samples** (URP / Built-in). The core package contains no shader, hence no pipeline dependency. HDRP isn't provided yet — see the [full documentation](Packages/com.alefeng.outlinesmoothnormalsgenerator/README.md) to port the outline Pass yourself; the decode logic is universal, only the render Pass needs adapting.
- `OutlinePreview.shader` is for in-editor preview only; do not use it in production.

## 📦 Installation
### Via UPM (recommended)
`Window > Package Manager` → `+` (top-left) → `Install package from git URL...` → paste:

```
https://github.com/AleFeng/OutlineSmoothNormalsGenerator.git?path=/Packages/com.alefeng.outlinesmoothnormalsgenerator
```

This installs the latest commit on `main`. **To pin a version, append `#<tag>` at the very end of the URL** — it must come after `?path=`:

```
https://github.com/AleFeng/OutlineSmoothNormalsGenerator.git?path=/Packages/com.alefeng.outlinesmoothnormalsgenerator#1.3.0
```

See [Releases](https://github.com/AleFeng/OutlineSmoothNormalsGenerator/releases) for available tags.

### Import an outline shader (required)
The core package **does not include** the outline shader. After installing, select the
package in Package Manager → `Samples` → **import the one matching your pipeline**:

| Sample | Shader name |
| --- | --- |
| Outline Shader (URP) & Demo | `OutlineSmoothNormalsGenerator/Outline URP` |
| Outline Shader (Built-in RP) & Demo | `OutlineSmoothNormalsGenerator/Outline Built-in` |

They can coexist (different names), but a project has only one pipeline, so normally you
only need the matching one. Each Sample includes a comparison demo scene.

### Other methods
You can also download the repo and copy the whole `Packages/com.alefeng.outlinesmoothnormalsgenerator`
folder into your project's **`Packages/` directory** (not `Assets/`) — Unity picks it up
automatically as a local package.

`Samples~` won't be imported by Unity (directories ending in `~` are ignored), so copy the
contents of `Samples~/URP/` (or `Samples~/BuiltIn/`) anywhere under `Assets/` manually.
**The shader needs no edits** — the `Packages/…` path it uses for the shared library resolves
under this layout too.

Once installed, the menu bar shows **`Tools → Smooth Normal Generator`**.

## 🚀 Quick Start
Below is the shortest path. **The complete parameter reference and shader sampling code are in the [full documentation](Packages/com.alefeng.outlinesmoothnormalsgenerator/README.md).**

### 1. Open the Tool
Menu bar → `Tools → Smooth Normal Generator` to open the "Smooth Normal Generator" window.

### 2. Pick a Target and Storage Mode
- Select a target and the tool reads it automatically. It can be a **scene object** (with `MeshFilter` / `SkinnedMeshRenderer`), or a **Mesh asset**, **model** (`.fbx`, etc.), or **prefab** in the Project; you can also drag it into the "Target" field manually. Selecting a scene object, model, or prefab **traverses the whole hierarchy** to collect every mesh in it; when there is more than one, the target section shows a **checkbox list** ("Select All / Clear" on top, all selected by default) — **check multiple meshes to edit them together**: Generate / Save / Save As act on every checked mesh, and the preview shows all checked meshes at once.
- In "Storage Mode", choose **Vertex Color / Tangent / TEXCOORD** (see [Three Storage Modes](#-three-storage-modes)). The "Data Channel Overview" on the right tells you whether the target channel already holds data.

### 3. Generate and Preview
- Click **`▶ Generate Smooth Normals`** to write the data into `sharedMesh`.
- Inspect the result live in the "Outline Preview" viewport: left-drag to orbit, scroll to zoom, middle-drag to pan, and toggle "Show Smooth Normals / Original Normals" to overlay and compare. The preview shares the exact same decode math as the real render — what you see is what you get.
- Not happy with the result? Click **`↺ Revert this change`**.

### 4. Save
Click the **`Save`** button at the bottom-left to write the changes back to the `.asset`.

> ⚠️ **A mesh inside a `.fbx` (or other model file) is a read-only imported sub-asset** — anything written to it is lost on the next reimport. The tool detects this and blocks the save; click **`⧉ Duplicate to standalone Mesh…`** first to make a writable `.asset`. When the target is a **scene object** it is reassigned onto the object automatically; when the target is an **asset** (Mesh / model / prefab) only the standalone `.asset` is created — reference it yourself.
>
> ⚠️ Modifying `sharedMesh` affects every object referencing that mesh. To affect a single object only, use the same duplicate flow.

<!-- ![](Documents/quickstart.gif) Quick start: select → generate → preview -->

## 🧩 Three Storage Modes
Smooth normals are always stored as a **full object-space direction** — no hemisphere packing, hence no sign ambiguity and no mis-decoding at hard-edge corners.

| Mode | Storage location | When to use |
| --- | --- | --- |
| **Vertex Color** | RG / GB / **BA** (default) channel pair of `color`, octahedral-encoded | First choice when vertex color is free. Two 8-bit components, ~1° error. |
| **Tangent** | `tangent.xyz` (`w` is always 1) | Full float precision. ⚠ **Overwrites the original tangent and breaks normal mapping** — only use it when the mesh has no normal map. |
| **TEXCOORD** | `xyz` of `TEXCOORD0`–`TEXCOORD7` | Recommended when vertex color is occupied; 8 channels. Full float precision. |

> Channels are always named `TEXCOORDn`, identity-mapped to the `mesh.SetUVs(n)` index — Unity's own `mesh.uv2` is actually `TEXCOORD1`, so the "UV1/UV2" naming is trivially off by one.
>
> ⚠️ **`TEXCOORD0` is the main texture UV.** Writing to it destroys the texture mapping. The default is `TEXCOORD1`; writing to `TEXCOORD0` requires an explicit confirmation.

## 🎨 Using the Outline In-Game
After importing the Sample for your pipeline (two passes: Pass0 backface-extrusion outline + Pass1 basic NPR shading):

1. Create a new material and set its shader to `OutlineSmoothNormalsGenerator/Outline URP` (or `... /Outline Built-in`).
2. In the material inspector's **Smooth Normal Source**, pick the **same** storage channel you used when generating; for vertex color mode, also set **Vertex Color Channel** to the same pair.
3. Adjust outline color and width. The **width mode** can be **screen space** (uniform width, independent of distance) or **world space** (offset in world units, shrinking with distance).

The `VertexNormal` mode extrudes along the raw vertex normals — the "without this tool" look, handy for a direct comparison.

If you use your own main material, just copy the **OUTLINE Pass** into it. Prefer `#include`-ing the package's `Shader/OutlineSmoothNormals.hlsl` over hand-rolling the decode — see the [full documentation](Packages/com.alefeng.outlinesmoothnormalsgenerator/README.md#shader-中读取平滑法线) for the per-channel sampling code.

## 📖 Full Documentation
This README is the overall introduction and quick start. **The complete usage guide** — details of each storage mode, channel selection, shader sampling code, clearing data, caveats, etc. — lives in the in-plugin documentation:

👉 **[Packages/com.alefeng.outlinesmoothnormalsgenerator/README.md](Packages/com.alefeng.outlinesmoothnormalsgenerator/README.md)** (Chinese)

## 📁 Project Structure
```
Packages/com.alefeng.outlinesmoothnormalsgenerator/     ← package root
├── package.json  CHANGELOG.md  LICENSE.md
├── README.md                                     ← Detailed usage docs
├── Editor/
│   ├── OutlineSmoothNormalsGeneratorWindow.cs    ← Main window (with embedded live preview)
│   ├── OutlineSmoothNormalsCalculator.cs         ← Smooth-normal core (angle-weighted + tolerance merge)
│   ├── OutlineSmoothNormalsCodec.cs              ← Storage format codec (C# mirror of the .hlsl)
│   ├── StorageWriter.cs                          ← Writes vertex color / tangent / TEXCOORD
│   ├── OutlineShaderGUI.cs                       ← Custom material inspector
│   ├── Shader/OutlinePreview.shader              ← Editor-only preview shader
│   └── OutlineSmoothNormalsGenerator.Editor.asmdef
├── Shader/
│   └── OutlineSmoothNormals.hlsl                 ← THE single source of truth: decode + extrusion math
└── Samples~/
    ├── URP/                                      ← Sample: URP outline shader + demo scene
    └── BuiltIn/                                  ← Sample: Built-in outline shader + demo scene
```

> `OutlineSmoothNormals.hlsl` is shared by the editor preview and both pipelines' outline shaders. There is exactly one copy of the decode math, which makes "preview looks right but the real render doesn't" structurally impossible.

## 📋 Roadmap
- Provide an HDRP version of the outline shader.
- Batch processing of multiple meshes / entire folders.
- More outline styles (depth-aware width, per-material colors, etc.).

## 📄 License
This project is open-sourced under the [MIT License](LICENSE) and is free for commercial and non-commercial use.
