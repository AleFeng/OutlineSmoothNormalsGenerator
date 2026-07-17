# 更新日志

本文件遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/) 格式，
版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

`0.x` 为发布前的开发迭代，`1.0.0` 是首个公开版本。由于此前从未对外发布，
`0.x` 中的「修复」均针对内部早期实现，不涉及任何已发布版本的迁移。

## [1.0.0] - 2026-07-17

首个公开版本：打包为 UPM，文档与实际行为对齐。

### 新增

- **UPM 包**（`package.json`），可通过 git URL 安装。
- **Samples**，描边 Shader 按渲染管线分别提供，各自附带同一套对照演示场景：
  - `Outline Shader (URP) & Demo` —— Shader 名 `OutlineSmoothNormalsGenerator/Outline URP`。
  - `Outline Shader (Built-in RP) & Demo` —— Shader 名 `OutlineSmoothNormalsGenerator/Outline Built-in`（⚠ 未在 Built-in 环境下实测）。
- `CHANGELOG.md` 与包根 `LICENSE.md`。

### 变更

- **核心包不依赖任何渲染管线**。描边 Shader 移入 `Samples~`，因此包本身不引入
  URP 依赖 —— 工具本体是纯编辑器 C#，本就与管线无关，这一点现在名副其实。
- 两个 Sample 的 Shader **分开命名**（`… /Outline URP` 与 `… /Outline Built-in`），
  因此可以共存、互不干扰；代价是换管线时材质需重新指定 Shader。
- 文档全面修订，删去与实际行为不符的表述（Undo 承诺、`.fbx` 可保存、
  「内置 Built-in 版 Shader」的旧口径、不存在的「清除数据折叠面板」等）。

## [0.7.0] - 2026-07-17

性能。这一组没有正确性风险，因此放在所有修复之后。

### 修复

- **编辑器在大网格上卡死**：预览渲染（`BeginPreview` → `camera.Render` →
  `EndPreview`）此前没有 Repaint 守卫，Layout 和**每一次鼠标移动**都会分配
  RenderTexture 并跑一遍完整离屏渲染。现在只在 Repaint 执行。
- **每个事件都在拷贝整份网格数组**：`vertices`/`normals`/`triangles`/`colors32`/
  `tangents` 每次访问都是完整 marshal，而这些访问散落在 OnGUI 各处。现已统一缓存。
  其中最隐蔽的一处是 `DrawNormalsOverlay(r, GetDecodedSmoothNormals(), …)` ——
  被调方虽对非 Repaint 提前返回，但 C# 先求值实参，整份解码照样每个事件都跑。
- **`Texture2D` 泄漏**：静态纹理缓存缺 `HideFlags`，每次域重载都刷屏
  「Texture2D has been leaked」。
- **预览材质泄漏**：缺 `HideFlags`，且重建时不清理旧材质会留下孤儿对象。
- **预览只绘制第一个 SubMesh**，多材质模型看不全。
- **法线叠加层位置偏移且会溢出**：`GL.LoadPixelMatrix` 的原点是整个窗口渲染目标
  的左上角（含标签页头部），与 `OnGUI` 坐标系差一个 header 高度，导致线段整体
  上移；且 GL 不受 GUI 裁剪约束，放大后线段会画到侧边面板上。现改用工作在 GUI
  坐标系的 `Handles`，并在 CPU 端做 Liang-Barsky 精确裁剪。

### 变更

- 缓存复用 GUIStyle，每个事件的分配从 60+ 降至约 14。

## [0.6.0] - 2026-07-17

界面诚实度。

### 修复

- **切线状态徽标永不亮起**：判据为 `w != ±1`，但写入器与 `RecalculateTangents`
  产生的都是 `w = ±1`，该条件恒为假 ——「清除切线」按钮也因此永远不可点。
  改用正交性判据：真切线与顶点法线正交，而本工具存入的平滑法线与之大体同向。
- **UV 通道状态误报**：判据仅为「非空」，导致任何带贴图 UV 的网格都被报成
  「含平滑法线」；且状态卡片的两个参数传入了同一个表达式，使「有原始数据」
  一档不可达。现改用顶点属性维度判据（本工具写 3 分量，贴图 UV 通常 2 分量）。
- **顶点色检测两头不准**：`!= 128` 的判据对白色顶点色误报「有数据」，对编码值
  恰为 128 的顶点漏报。改用「取值是否有变化」——全部相同即为常量。

### 变更

- **通道状态措辞**：不再宣称「含平滑法线」。该判断在数据上**不可判定**
  （编码后就是普通数值，与任意顶点色/UV 无法区分），现在最高只说「可能是平滑法线」。

### 移除

- 死代码：从未赋值或读取的样式字段、从未读取的折叠状态字段、孤立的文档注释。

## [0.5.0] - 2026-07-17

数据安全。

### 新增

- **另存为独立 Mesh**：一键把网格复制成可写的 `.asset` 并自动替换到对象上。
  这是不可写网格（FBX 子资产 / 内置资源）唯一能真正保存的路径。
- **还原本次修改**：生成 / 清除前自动抓取会话快照，可一键回退。

### 修复

- **谎报保存成功**：对 `.fbx` 导入的网格，`GetAssetPath` 返回**非空**路径，于是
  空值判断放行，但那是不可变的导入子资产 —— `SaveAssetIfDirty` 对它是空操作，
  数据会在下次重导入时丢失，而界面照样显示「✓ 保存完成」并打日志「已保存」。
  现在会准确判定可写性（导入子资产 / 内置资源 / 非资源），阻止假保存并给出出路。
- **清除切线会破坏法线贴图**：原实现把 `mesh.tangents` 置为 `null`，使网格彻底
  失去切线，影响每个引用该 sharedMesh 的对象且无从恢复。改为重算出真实切线。
- **切换选中会静默丢失未保存警告**：保存状态被无条件重置，用户会以为改动已落盘。
  现在状态跟着网格走，切走再切回警告仍在。
- **选中非网格对象后状态错位**：组件引用被清空但旧网格仍挂着，界面显示渲染器为
  「—」，生成按钮却仍对上一个网格开火。

### 变更

- **撤回 Undo 承诺**：`Undo.RecordObject` 并不可靠地跟踪网格顶点数据，对不可变的
  导入子资产更是完全无效。此前文档承诺的「全流程支持 Undo」是一张无法兑现的
  空头支票，现改为提供工具自己的会话快照。
- 切线模式的说明由「兼容大多数标准 Shader」改为明确告警 —— 覆盖 `tangent.xyz`
  恰恰是对标准 Shader 兼容性破坏最大的操作，原文说反了。

## [0.4.0] - 2026-07-17

顶点合并。

### 新增

- **合并容差**（默认 `0.0001`，可调）。

### 修复

- **接缝顶点不合并**：顶点分组用的是逐分量**精确**浮点相等（`IEquatable<Vector3>`，
  并非 `Vector3 ==` 的近似比较）。而接缝顶点经 DCC 导出、FBX 浮点截断或缩放后
  往往只差 1e-6 量级 —— 于是工具在它最该起作用的接缝上静默失效。改用容差量化。
  文档此前声称的「精度 0.0001」至此才成为事实。
- **无法线的网格抛异常**：`mesh.normals` 在未导入法线时返回的是**空数组**而非
  `null`，直接索引会抛 `IndexOutOfRangeException`。现在会自动重算并给出提示。
- **退化三角形判据失效**：边向量被乘以 1000「提升精度」，但叉积随后即被归一化，
  缩放对方向毫无影响；它唯一的实际后果是把 `sqrMagnitude` 放大 1e12，使阈值
  `1e-10` 等效为 `1e-22`，狭长三角形因此全部混过检查并向结果注入噪声。
  改用与模型尺度无关的相对判据。
- 计算过程中对同一份网格数组的重复 marshal。

### 已知局限

- 格点取整仍会把恰好跨越格边界的两点分开。要完全正确需邻格探查或并查集。
  取整是标准做法，代价是容差必须远小于模型的最小真实特征尺寸。

## [0.3.0] - 2026-07-17

存储格式重构。**破坏性变更**。

### 变更

- **平滑法线改为存完整的对象空间三维方向**，不再做半球压缩：
  - 顶点色 → 选定通道对，**八面体编码**（全球面双射，8-bit 下误差约 1°）
  - 切线 → `tangent.xyz` 直接存，`w` 恒为 1
  - TEXCOORD → `uv.xyz` 直接存
- ⚠ 任何用早期内部版本烘焙过的网格都**必须重新烘焙**。

### 修复

- **硬边角点描边开裂**（本次重构的根本原因）：旧格式存 XY、用 `sqrt` 重建 Z、
  再按顶点法线点积修正符号。该方案在角点上**必然**失效 —— 以立方体角
  `(1,1,-1)` 为例，其平滑法线 Z 为负，而该角上三个位置重合的顶点法线分别是
  `(1,0,0)`、`(0,1,0)`、`(0,0,-1)`，符号启发式只对第三个翻转，于是同一条平滑
  法线被解码成两个不同结果，描边恰好在它本该修复的角上裂开。这不是解码器的
  bug，而是格式本身不可修 —— 只能换格式。
- **重复生成会损坏网格**：切线模式原本以 `mesh.tangents` 为基做空间变换，而写入
  正是覆盖 `tangent.xyz` —— 第二次生成读到的「基」其实是上一次写入的法线。
  现在写入器不再读取自身的输出，幂等性由构造保证。
- **切线模式编解码基不一致**：编码用网格原始切线，解码却只能用顶点法线重建
  （原始切线已被覆盖、无从恢复），两者对不上，结果本就是错的。
- `WriteToUV` 把 2 分量 UV 无谓地撑成 4 分量，白白翻倍顶点缓冲占用。

### 移除

- `ConvertToTangentSpace`：若编解码统一使用同一组由顶点法线导出的正交基，
  该变换在数学上**恒等于原向量**，纯属多余。
- `FixNormalZ`（共三份实现）：随半球压缩格式一同废止。

## [0.2.0] - 2026-07-17

通道命名与材质面板。

### 新增

- **`VertexNormal` 模式**：直接沿原始顶点法线外扩，即「未使用本工具」的对照效果。
- **写入 TEXCOORD0 的警告与二次确认**：TEXCOORD0 就是模型的主贴图 UV，此前
  生成与逐行「清除」都会一键静默毁掉贴图映射，且影响所有引用该 sharedMesh 的对象。

### 修复

- **UV 通道整体错位一格**：工具的「UV1」写入 TEXCOORD0，Shader 的「UV1」却读取
  TEXCOORD1。根因是 Unity 自身的 `mesh.uv2` 即 `TEXCOORD1`，名字与索引天然差一位。
- **「UV4」分支与「UV3」逐字重复**，选 UV4 实际读的是 UV3 的数据；且没有任何
  分支读取 TEXCOORD0。
- 材质面板提示中凭空捏造的「UV5 (TEXCOORD3.zw)」—— 该模式不存在，且提示内容与
  正上方的下拉标签自相矛盾。
- 材质面板在属性缺失时会抛 `ArgumentException`（误用了强制版 `FindProperty`）。

### 变更

- **通道命名全链路统一为 `TEXCOORDn`**：工具下拉、`mesh.SetUVs` 索引、Shader
  关键字、面板提示恒等对应，消除差一位的空间。
- 描边宽度上限由 `0.15` 收敛至 `0.1`，与 Shader 的 `Range(0, 0.1)` 对齐 ——
  此前超出部分会被静默截断。
- 「顶点色通道对」选项仅在顶点色模式下显示。

## [0.1.0] - 2026-07-17

让描边在本项目里第一次真正渲染出来。

### 新增

- **`Shader/OutlineSmoothNormals.hlsl`** —— 解码与外扩数学的唯一真源，由生产
  Shader 与编辑器预览共用。
- 对照演示场景与可复现的资产生成脚本，作为后续每一步的验证台。

### 修复

- **描边 Shader 在 URP 下完全不渲染**：Shader 是 Built-in 写法
  （`LightMode = "ForwardBase"` / `"Always"`、`UnityCG.cginc`），而 URP 只绘制
  `UniversalForward` 等 Pass —— 两个 Pass 连同 `FallBack "Diffuse"` 全被静默跳过，
  物体完全不可见。所有其他生产端缺陷都藏在这一条后面，无法被观察到。
- **预览与实际渲染不一致**（同一套数学被抄成三份后各自漂移）：
  - 生产端硬编码顶点色 B/A 通道，无视用户选择的 RG/GB 通道对；
  - 生产端缺少 Z 符号修正，与预览结果不同；
  - 生产端外扩算法不同：用非逆转置矩阵（非均匀缩放下描边倾斜）、在视图空间
    取方向（忽略 FOV/宽高比），且 `normalize()` 未做零向量保护 —— 法线正对
    相机时产生 NaN，GPU 会直接丢弃整个三角形。
  三方现共用同一份 `.hlsl`，漂移在结构上不再可能发生。

### 变更

- **描边 Shader 移出 `Editor/` 目录**：`Editor` 是 Unity 的特殊文件夹，其下资源
  不会进入播放器构建 —— 生产用 Shader 放在那里会导致材质在构建后失效。
- `shader_feature` → `shader_feature_local_vertex`：不再占用全局关键字槽位。

[1.0.0]: https://github.com/AleFeng/OutlineSmoothNormalsGenerator/releases/tag/1.0.0
[0.7.0]: https://github.com/AleFeng/OutlineSmoothNormalsGenerator/releases/tag/0.7.0
[0.6.0]: https://github.com/AleFeng/OutlineSmoothNormalsGenerator/releases/tag/0.6.0
[0.5.0]: https://github.com/AleFeng/OutlineSmoothNormalsGenerator/releases/tag/0.5.0
[0.4.0]: https://github.com/AleFeng/OutlineSmoothNormalsGenerator/releases/tag/0.4.0
[0.3.0]: https://github.com/AleFeng/OutlineSmoothNormalsGenerator/releases/tag/0.3.0
[0.2.0]: https://github.com/AleFeng/OutlineSmoothNormalsGenerator/releases/tag/0.2.0
[0.1.0]: https://github.com/AleFeng/OutlineSmoothNormalsGenerator/releases/tag/0.1.0
