# Outline Smooth Normals Generator — User Guide

<p align="center">
  🌍
  <a href="./README.md">中文</a> |
  English |
  <a href="./README_JA.md">日本語</a>
</p>

Computes **smooth normals** for a mesh and bakes them into the mesh, for backface-extrusion outlines that fix cracking at hard edges.

The tool itself is pure editor C# and is **render-pipeline agnostic**. The outline shaders ship per-pipeline as Samples (URP / Built-in) — import the one you need.

---

## Directory Structure

```
com.alefeng.outlinesmoothnormalsgenerator/
├── package.json
├── CHANGELOG.md
├── LICENSE.md
├── README.md                                        ← Chinese usage doc
├── Editor/
│   ├── OutlineSmoothNormalsGeneratorWindow.cs       ← Main window (with embedded live preview)
│   ├── OutlineSmoothNormalsCalculator.cs            ← Smooth-normal core (angle-weighted + tolerance merge)
│   ├── OutlineSmoothNormalsCodec.cs                 ← Storage-format codec (C# mirror of the .hlsl)
│   ├── StorageWriter.cs                             ← Writes vertex color / tangent / TEXCOORD
│   ├── OutlineNormalsImportProcessor.cs             ← Auto-bake on import (AssetPostprocessor + hooks)
│   ├── OutlineNormalsSettings.cs                    ← Auto-bake config (persisted to ProjectSettings/)
│   ├── OutlineMeshValidator.cs                      ← Mesh health check
│   ├── OutlineShaderGUI.cs                          ← Custom material inspector for the outline shader
│   ├── Shader/OutlinePreview.shader                 ← Editor-preview only
│   └── OutlineSmoothNormalsGenerator.Editor.asmdef
├── Shader/
│   ├── OutlineSmoothNormals.hlsl                    ← THE single source of truth: decode + extrusion math
│   └── OutlineNPR.hlsl                              ← Demo basic NPR lighting math (cel shading + rim)
└── Samples~/
    ├── URP/          → Sample "Outline Shader (URP) & Demo"
    └── BuiltIn/      → Sample "Outline Shader (Built-in RP) & Demo"
```

`Shader/OutlineSmoothNormals.hlsl` is shared by the editor preview and both pipelines' outline shaders. There is exactly one copy of the decode math, so "preview looks right but the real render doesn't" is structurally impossible.

---

## Import an Outline Shader (Required)

> This document targets users who have **already installed the package**. For installation (UPM git URL / copying into `Packages/`), see the repository root README.

The core package **does not include** the outline shader — that would tie the package to a specific pipeline. In Package Manager, select this package → `Samples` → **import the one matching your pipeline**:

| Sample | Shader name |
|---|---|
| Outline Shader (URP) & Demo | `OutlineSmoothNormalsGenerator/Outline URP` |
| Outline Shader (Built-in RP) & Demo | `OutlineSmoothNormalsGenerator/Outline Built-in` |

They can coexist (different names), but a project has only one pipeline, so normally you only need the matching one.

> The tool itself is in the core package; the menu **`Tools > Smooth Normal Generator`** is available right after the package is installed. Importing a Sample only gets you the outline shader — it has nothing to do with whether the tool menu appears.

---

## Quick Start

![The "Smooth Normal Generator" tab: control panel on the left, data channel status top-right, live outline preview bottom-right](./Docs~/Images/tool_generate.png)

### 1. Open the Tool

`Tools > Smooth Normal Generator`

### 2. Pick a Target

Select a target and the tool reads it automatically. The target can be:

- A **scene object / model** (`.fbx` / `.obj`, etc.) **/ prefab** — each is **traversed across its whole hierarchy** (including children) to collect every mesh.
- A **Mesh asset** — a standalone `.asset` in the Project, or a Mesh sub-asset under an expanded FBX, used directly.

You can also drag any of the above into the "Target" field manually.

When there are multiple meshes, the target area shows a **scrollable checkbox list** ("Select All / Clear" on top, all selected by default when a new source loads, auto-deduplicated when one mesh is shared by several renderers). **Checking** decides which meshes Generate / Save / Save As act on, and which the preview shows; **clicking a mesh name** makes it the **focus** — the mesh info and channel status on the right reflect the focused mesh.

### 3. Make Sure the Mesh Is Writable

If the mesh comes from a `.fbx` or other model file, it is a **read-only imported sub-asset** — anything written to it is lost on the next reimport. The tool detects this and blocks the save; click **`⧉ Duplicate to standalone Mesh…`** to make a writable `.asset`.

> When a **scene object** is selected, the new `.asset` is reassigned onto the object automatically; when an **asset** (Mesh / model / prefab) is selected there is no component to write back to, so only a standalone `.asset` is created — reference it yourself. A standalone, writable `.asset` mesh is already writable — just save it in step 4, no duplication needed.

### 4. Choose a Storage Mode and Generate

Pick a storage channel, click **`▶ Generate Smooth Normals`**, then **`Save`** to write to disk.

### 5. Preview

The right side of the window is the live preview (**embedded**, not a separate window), showing all **checked** meshes (multi-selected meshes are laid out at their relative positions in the hierarchy and the camera frames them all; a placeholder is shown when nothing is checked):

- Left-drag to orbit, scroll to zoom, middle-drag to pan
- Toggle **Show Smooth Normals** (green) and **Show Original Normals** (blue) to overlay and compare (focused mesh)
- Adjust outline width / color, model smoothness / metallic, background color

The preview shares the exact same decode and extrusion math as the real render — what you see is what you get.

---

## UI Reference

The tool window has two tabs at the top: **Smooth Normal Generator** (manual workflow) and **Auto-Bake On Import** (see [Auto-Bake on Import](#auto-bake-on-import)). This section walks top-to-bottom through every region of the "Smooth Normal Generator" tab — cross-reference the screenshot in [Quick Start](#quick-start) above: **the left column is the control panel; the right column shows data channel status on top and the live preview & parameters below.**

### Left Panel · Target
- **Target**: the current source object / asset; the `×` on the right clears it. You can drag a scene object, Mesh, model, or prefab directly into this field.
- **Mesh list (N / M selected)**: every mesh discovered from the source. `Select All` / `Clear` on top toggle the checkboxes at once.
  - The **checkbox** decides which meshes are batch-processed — Generate / Save / Save As act on **all checked meshes**, and the preview shows all of them at once.
  - **Clicking a mesh name** makes it the **focus** (highlighted row): the "Mesh Info", "Data Channel Status", and the normal-visualization overlay in the preview all apply only to the focused mesh.
  - Each row is tagged with its renderer type (`MeshFilter` / `SkinnedMeshRenderer`); below the focused mesh, two more tags show its renderer and mesh name.
  - The info bar below states the current checked count and the "batch acts on all, status shows the focus only" rule.

### Left Panel · Mesh Info (collapsible)
Expand to see the focused mesh's **vertex count, triangle count, sub-mesh count**, plus whether it has **normals / tangents / vertex colors** and whether each **TEXCOORD0–7** channel holds data (with component count). Use it to quickly check whether the target channel is already occupied before writing.

### Left Panel · Storage Mode
Three tabs — **Vertex Color / Tangent / UV Channel** — decide where the smooth normal is written (the three modes are described right below):
- **Vertex Color**: pick an `RG / GB / BA` channel pair; the "Vertex Color Channel Status" below marks, per channel, the components **about to be overwritten** (e.g. in BA mode B←normal X, A←normal Y), while unselected channels keep their original values; `Clear RG / GB / BA` wipes the data by channel pair.
- **Tangent**: writes the direction straight into `tangent.xyz`; ⚠ this overwrites the original tangent — use "Recalculate Tangents" in the panel to restore real tangents when needed.
- **UV Channel**: choose `TEXCOORD0–7` from the dropdown (each can be cleared individually); choosing `TEXCOORD0` (the main texture UV) asks for confirmation.

### Left Panel · Generate
- The **Merge Tolerance** slider (default `0.0001`; see [Merge Tolerance](#merge-tolerance)).
- The **▶ Generate Smooth Normals** button: its label shows the current storage channel and checked count live (e.g. "→ Vertex Color ×6"); clicking it computes and writes into `sharedMesh` for every checked mesh.
- If the focused mesh has data problems, a **health check** card appears at the top of this region (see [Mesh Health Check](#mesh-health-check)); on errors you're asked to confirm before generating.

### Left Panel Bottom · Save
The `↺ Revert this change`, `Save`, and `⧉ Duplicate to standalone Mesh…` buttons fixed at the bottom — see [Data Safety](#data-safety) for what they do and their caveats.

### Right Panel Top · Data Channel Overview
Three cards — Vertex Color / Tangent / TEXCOORD — mark each channel's status ("has data / empty", etc.; criteria and wording in [Data Status](#data-status)). Always reflects the **focused** mesh.

### Right Panel Bottom · Outline Preview & Parameters
The embedded live preview: **left-drag to orbit, scroll to zoom, middle-drag to pan**. The badge in the top-left (e.g. "● Vertex Color mode") is a **read-only** indicator of which channel the preview is decoding from, following the "Storage Mode" on the left. The preview shares one decode / extrusion math with the production shader — what you see is what you get. The parameter panel on the right, top to bottom:

| Group | Parameter | Description |
|---|---|---|
| **Outline** | Show Outline | Turn the outline Pass on / off. |
| | Outline Color | The outline's color. |
| | Outline Width | `0.001–0.1`, same scale as the shader's `_OutlineWidth` — directly comparable. |
| | Width Mode | **Screen space** (uniform width, distance-independent) / **World space** (offset in world units, shrinking with distance). |
| **Model** | Show Model | Turn the base model on / off (turn it off to see only the outline). |
| | Base Color / Smoothness / Metallic | Preview material look; Smoothness and Metallic range `0–1`. |
| **Viewport** | Background Color | Preview background color. |
| **Normal Visualization** | Show Smooth Normals | Overlay green smooth-normal line segments. |
| | Normal Length | Segment length, `0.005–0.5`. |
| | Smooth Normal Color | Color of the smooth-normal segments. |
| | Show Original Normals | Overlay blue original normals to compare direction differences at hard edges. |
| | Original Normal Color | Color of the original-normal segments. |
| **Camera** | Yaw / Pitch / Distance | Set the view precisely (you can also drag / scroll in the viewport). |
| | Reset View | Reset the angles and auto-frame all checked meshes. |

> The normal-visualization overlay is drawn only for the **focused** mesh; the preview render shows **all checked** meshes (laid out at their relative positions in the hierarchy when multi-selected).

---

## Auto-Bake on Import

![The "Auto-Bake On Import" tab: enable switch, match suffix, storage mode, and merge tolerance; config stored under ProjectSettings/](./Docs~/Images/tool_auto.png)

Beyond the manual workflow above, the tool can **bake automatically on import**: as soon as a model that matches the rule is (re)imported, smooth normals are written into the mesh — no need to run the tool manually, no need to duplicate a standalone Mesh.

**Non-destructive**: the write happens during import, on the mesh being imported, and persists with the import output; remove the suffix or turn the switch off and reimport to restore the original mesh.

### Enable & Configure

Open the **"Auto-Bake On Import" tab** at the top of the tool window:

| Setting | Description |
|---|---|
| **Enable auto-bake on import** | Master switch, off by default. |
| **Filename suffix** | Match rule: bake when the filename (without extension) ends with this, case-insensitive. Default `_Outline`, e.g. `Hero_Outline.fbx`. |
| **Storage mode** | Vertex color / tangent / `TEXCOORD0`–`7`, same meaning as the manual workflow; the shader must read the same channel. |
| **Merge tolerance** | Same as the "Merge Tolerance" section below. |

The config persists to `ProjectSettings/OutlineSmoothNormals.asset`, versioned with the project so the whole team shares one setting.

> After changing the config, already-imported models are not re-baked automatically — just reimport them (right-click `Reimport`) once.

### Extension Hooks (advanced)

For private pipelines that the built-in "filename suffix + three storage modes" can't cover, two static delegates let you take over, falling back to the defaults when unset. Typically assign them once on load with `[InitializeOnLoadMethod]`:

```csharp
using UnityEditor;
using OutlineSmoothNormalsGenerator;

static class MyOutlineAutoBake
{
    [InitializeOnLoadMethod]
    static void Register()
    {
        // Custom match rule: decide by folder / label / import settings, replacing the filename suffix
        OutlineNormalsImportProcessor.ShouldBakeRule = (assetPath, importer) =>
            assetPath.StartsWith("Assets/Characters/");

        // Custom storage: receive the mesh and the object-space smooth normals, encode / write them yourself
        OutlineNormalsImportProcessor.CustomStorageWriter = (mesh, smoothNormals) =>
        {
            // …your own write logic; the shader reads it back in the matching format…
        };
    }
}
```

### Mesh Health Check

Before generating / baking, the tool scans the mesh data and reports anything it can't process or that would affect the result (missing normals, zero / NaN normals, degenerate triangles, too many coincident vertices at one position, Read/Write disabled, etc.). In the manual workflow the report shows as a card at the top of the "Generate" region (with a confirm before generating on errors); during auto-bake it is written to the Console and meshes with errors are skipped.

---

### About TEXCOORD Naming

There are 8 channels (`TEXCOORD0`–`TEXCOORD7`), always referred to as **`TEXCOORDn`**, in **identity correspondence** with the `mesh.SetUVs(n)` index.

Avoiding the "UV1 / UV2" naming is deliberate: Unity's own `mesh.uv2` is actually `TEXCOORD1`, so the name and index are naturally off by one — very easy to get wrong.

> ⚠️ **`TEXCOORD0` is the model's main texture UV** (`mesh.uv`). Writing to it destroys the texture mapping and affects every object referencing that sharedMesh. The default is `TEXCOORD1`; only pick `TEXCOORD0` when that channel is genuinely free (e.g. a procedural mesh), and the tool asks for confirmation.

---

## Merge Tolerance

Vertices at the "same position" are merged and averaged. But seam vertices typically differ by ~1e-6 after DCC export, FBX float truncation, or scaling — with exact equality they wouldn't merge, and the tool would silently fail at exactly the seams where it matters most. Hence the **merge tolerance** (default `0.0001`).

> **Known limitation**: internally the position is quantized to an integer grid by the tolerance. Two points that happen to straddle a grid boundary are still split apart. Fully correct behavior would need neighbor-cell probing or union-find. Rounding is the standard approach, at the cost that **the tolerance must be far smaller than the model's smallest real feature size** — too large a tolerance wrongly merges vertices that should stay separate and deforms the outline.

---

## Data Status

The "Data Channel Overview" on the right shows each channel's status:

- `● Likely smooth normals` — a strong heuristic matched
- `○ Has data` — has data, but can't tell whether it's smooth normals
- `✕ Empty` — the channel has no data

> Why it caps at "**likely**": whether a channel actually holds smooth normals **cannot be determined from the data** — once encoded it's just ordinary numbers, indistinguishable from any vertex color / texture UV. Claiming certainty would be lying. The TEXCOORD heuristic is relatively reliable (this tool writes 3 components, texture UVs are usually 2); vertex color only reports "has / no data".

---

## Data Safety

| Button | Effect |
|---|---|
| `Save` | Writes the changes of **all checked** meshes back to their `.asset`. When a mesh isn't writable it **blocks** and explains why — never a fake success. |
| `⧉ Duplicate to standalone Mesh…` | Copies **all checked** meshes into writable `.asset`s (a name dialog when one is checked; a folder picker + batch when several are), auto-reassigning onto scene objects. The only way out for non-writable meshes. |
| `↺ Revert this change` | Roll back to the state before this generate / clear. |

> **This tool does not rely on Unity's Undo.** `Undo.RecordObject` does not reliably track mesh vertex data, and is entirely ineffective for immutable imported sub-assets. "Revert this change" is the tool's own session snapshot — treat it as authoritative; the snapshot is invalidated when you close the window or switch targets.

Clearing is inside each storage mode's panel (vertex color clears by channel pair; tangent restores real tangents via "Recalculate Tangents"; TEXCOORD clears per channel).

---

## Using the Outline In-Game

### Method 1: Use the Sample shader directly

1. Create a new material for the model and set its shader to `OutlineSmoothNormalsGenerator/Outline URP` (or `... /Outline Built-in`).
2. In **Smooth Normal Source**, pick the **same** storage channel you used when generating (vertex color / tangent / `TEXCOORD0`–`TEXCOORD7`). For vertex color mode, also set **Vertex Color Channel** to the same pair used when baking.
3. Adjust outline color and width. **Width Mode** can be **screen space** (uniform width, distance-independent) or **world space** (offset in world units, shrinking with distance).

The `VertexNormal` mode extrudes along the raw vertex normals — the "without this tool" look, handy for a direct comparison.

The shader has two passes: `OUTLINE` (front-face-culled extruded outline) + `FORWARD` (basic NPR: two-step cel shading + rim, tunable in the material inspector). The material's **Base Color Mode** also offers a set of **debug options** that show the smooth-normal data directly as color (vertex color / tangent / `UV0`–`UV7`, unlit) for eyeballing the generated result; switch back to **Base Map** for production.

### Method 2: Merge the outline Pass into your own shader (recommended)

Real projects usually have their own main material. Just copy the shader's **`OUTLINE` Pass** wholesale into it.

---

## Reading the Smooth Normal in a Shader

**Prefer `#include`-ing the shared library** over hand-rolling the decode — that was exactly the source of a string of early "preview doesn't match the real render" defects in this plugin.

```hlsl
#include "Packages/com.alefeng.outlinesmoothnormalsgenerator/Shader/OutlineSmoothNormals.hlsl"

// Pick one of the three, matching the mode you baked with:
float3 smoothNormalOS = OSN_DecodeVertexColor(v.color, _VCChannel);  // vertex color (octahedral)
float3 smoothNormalOS = OSN_DecodeTangent(v.tangent);                // tangent
float3 smoothNormalOS = OSN_DecodeTexCoord(v.uv1.xyz);               // TEXCOORD1

// Extrude (URP; for Built-in swap the two Transforms for
// UnityObjectToWorldNormal / UnityObjectToClipPos)
float3 normalWS = TransformObjectToWorldNormal(smoothNormalOS);
float4 clipPos  = TransformObjectToHClip(v.positionOS.xyz);
o.positionCS    = OSN_ApplyOutlineOffset(clipPos, normalWS, _OutlineWidth);
```

`OSN_ApplyOutlineOffset` handles three things that are easy to get wrong: transforming the normal with the inverse-transpose matrix (otherwise the outline skews under non-uniform scale), taking the offset direction in **clip space** (otherwise it's affected by FOV / aspect ratio), and guarding against a zero-length direction (otherwise a normal facing straight at the camera makes `normalize` produce NaN and the GPU drops the whole triangle).

### If You'd Rather Not include

Tangent and TEXCOORD modes store the object-space direction directly — just normalize it:

```hlsl
float3 smoothNormalOS = normalize(v.tangent.xyz);  // or normalize(v.uv1.xyz)
```

Vertex color mode is octahedral-encoded and needs decoding:

```hlsl
float3 OctDecode(float2 f)
{
    f = f * 2.0 - 1.0;
    float3 n = float3(f.x, f.y, 1.0 - abs(f.x) - abs(f.y));
    float  t = saturate(-n.z);
    n.xy += (n.xy >= 0.0) ? -t : t;
    return normalize(n);
}
// BA channel pair:
float3 smoothNormalOS = OctDecode(v.color.ba);
```

---

## Caveats

- Smooth-normal computation is based on **vertex position merging**, tolerance default `0.0001` (see "Merge Tolerance" above).
- Modifying `sharedMesh` affects **every** object using that mesh. To affect a single object only, duplicate it first with "Duplicate to standalone Mesh".
- **Tangent mode overwrites the mesh's original tangents**, so shaders sampling a normal map get a wrong TBN.
- `Editor/Shader/OutlinePreview.shader` is for editor preview only — don't use it in production. If it's missing (usually an incomplete package install), the preview degrades to flat color with no outline offset.
- The storage format is a full object-space direction. Meshes baked with an internal version prior to `1.0.0` must be **re-baked**.

---

## License

MIT License — free for commercial and non-commercial use.
