# Demo（开发用验证台 · 不随插件发布）

这里是两个渲染管线各自的描边测试项目。**在这里编辑与验证，通过后再拷贝进
`Samples~`。**

```
Assets/Demo/
├── URP/                    → 拷贝为 Samples~/URP
│   ├── Outline.shader          手写（Shader 名：OutlineSmoothNormalsGenerator/Outline URP）
│   ├── Meshes/                 DemoBuilder 生成
│   ├── Materials/              DemoBuilder 生成
│   └── OutlineDemo.unity       DemoBuilder 生成
└── BuiltIn/                → 拷贝为 Samples~/BuiltIn
    └── （同上，Shader 名：OutlineSmoothNormalsGenerator/Outline Built-in）
```

`Assets/Editor/DemoBuilder.cs` 是生成脚本，**不进包**。

## 生成 / 重新生成

| 菜单 | 作用 |
|---|---|
| `Tools > Outline Demo > Build URP Demo` | 生成 URP 那套的网格、材质、场景 |
| `Tools > Outline Demo > Build Built-in Demo` | 生成 Built-in 那套 |
| `Tools > Outline Demo > Build Both` | 两套都生成 |
| `Tools > Outline Demo > Re-bake Meshes` | 只用默认容差重烘焙网格，不重建场景 |

每套场景里三个立方体并排，相机对准 −Z 角：

- **Cube_Smooth**（黑描边）—— 已烘焙平滑法线，描边应当**闭合**。
- **Cube_Raw**（红描边）—— 走 `VertexNormal` 模式，硬边处描边应当**断裂**。这是「没用本工具」的对照。
- **Cube_Jitter**（蓝描边）—— 顶点带 ±1e-5 抖动，模拟 FBX 浮点截断。用来验证**合并容差**：
  在工具窗口里选中它，容差拉到下限 `1e-6` 再生成 → 接缝应**裂开**；调回 `1e-4` → 应**重新闭合**。

## 为什么两个 Shader 不同名

`OutlineSmoothNormalsGenerator/Outline **URP**` 与 `... **Built-in**` 必须分开命名 ——
两者要同时存在于本项目里以便编辑，同名会造成 Shader 重定义，Unity 会任选其一并告警。

代价：换管线时材质要重新指定 Shader。收益：两个 Sample 可以共存、互不干扰。

## 如何验证 Built-in 版

Built-in 版在本项目（URP）下**能编译但不会渲染** —— URP 只绘制 `UniversalForward`
等 Pass，找不到 `Always` / `ForwardBase`。这正是本插件最初那个 bug 的现象。

要真正验证它，需临时把项目切回 Built-in：

1. `Project Settings > Graphics` → 把 **Render Pipeline Asset** 清空（设为 None）。
2. `Project Settings > Quality` → 各质量档位的 **Render Pipeline Asset** 也清空。
3. 打开 `Assets/Demo/BuiltIn/OutlineDemo.unity` 查看。
4. 验证完记得改回 URP 资产。

## 拷贝进 Samples~ 的步骤

**直接整个目录拷过去即可，不需要改任何内容。**

1. 把 `Assets/Demo/URP/` 整个拷贝到
   `Packages/com.alefeng.outlinesmoothnormalsgenerator/Samples~/URP/`
   （`BuiltIn/` 同理）。**连 `.meta` 一起拷** —— 场景 → 材质 → Shader 的引用靠 GUID，
   丢了 `.meta` 就会全部断掉变洋红。
2. 完事。拷完可以用 `diff` 确认两边逐字节相同。

> **为什么不用再改 `#include`：** 插件以 embedded package 的形式放在仓库的
> `Packages/com.alefeng.outlinesmoothnormalsgenerator/` 下，因此
> `Packages/com.alefeng.outlinesmoothnormalsgenerator/Shader/OutlineSmoothNormals.hlsl`
> 这条路径在**开发期**（Unity 把它识别为本地包）与**用户安装后**（UPM 包）**都成立**。
> Demo 与 Samples~ 的 shader 因此可以逐字节相同。
>
> 早期把插件放在 `Assets/` 下时并非如此：开发期只能用相对路径，装成包后又跨不过
> `Assets/` 与 `Library/PackageCache/` 的边界，于是每次拷贝都要手改一行 —— 而
> 「手工同步」正是这个项目历史上所有 bug 的同一种成因，所以把它从根上去掉了。

3. `Samples~` 里的目录名必须与 `package.json` 的 `samples[].path` 一致（`Samples~/URP`、
   `Samples~/BuiltIn`）；界面上显示的是 `displayName`，与目录名无关。
