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

> ⚠️ **每次拷贝都必须改一行 `#include`。**

1. 把 `Assets/Demo/URP/` 整个拷贝到
   `Assets/Plugins/OutlineSmoothNormalsGenerator/Samples~/URP/`
   （`BuiltIn/` 同理）。**连 `.meta` 一起拷** —— 场景 → 材质 → Shader 的引用靠 GUID，
   丢了 `.meta` 就会全部断掉变洋红。
2. 打开拷贝过去的 `Outline.shader`，把共享 include 的路径从**相对路径**改为**包路径**：

   ```hlsl
   // 开发时（插件在 Assets/ 下，只能用相对路径）：
   #include "../../Plugins/OutlineSmoothNormalsGenerator/Shader/OutlineSmoothNormals.hlsl"

   // Samples~ 里（作为 UPM 包安装后，只有 Packages/ 虚拟路径可解析）：
   #include "Packages/com.alefeng.outlinesmoothnormalsgenerator/Shader/OutlineSmoothNormals.hlsl"
   ```

   两条路径不可能同时成立：开发项目里没有那个包名，装成包之后又跨不过
   `Assets/` 与 `Library/PackageCache/` 的边界。这也是为什么不把 `.hlsl` 复制进
   每个 Sample —— 那会变成三份副本，而「解码数学只有一份」正是这次重构的核心。
3. `Samples~` 里的目录名必须与 `package.json` 的 `samples[].path` 一致（`Samples~/URP`、
   `Samples~/BuiltIn`）；界面上显示的是 `displayName`，与目录名无关。
