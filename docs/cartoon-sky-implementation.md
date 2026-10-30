# 卡通天空与远景（2026-10-28）

## 参考与最终方向

沿用《海盗法则 / Pirate Code》的参考方向，实际查看了前一轮海面文档中的 [5 张参考图](cartoon-ocean-implementation.md)：蓝青色天空、象牙白的宽云带、浅金色过渡和淡粉色地平线，远处用沙色、暖褐和灰紫色岩岛形成层次。

根据最新反馈，云改为高低、大小与疏密错落的圆润云团，四周轮廓都用曲线，边缘羽化，内部明暗使用柔和渐变。移除原先固定纬度的四层排列及过直的云底。参考图只用于观察风格；项目中的 Shader 和远景网格自行生成。

## 接入与配置

- `IslandSky.shader` / `IslandSky.mat`：渐变、天空半球上 24 组候选云团、缓慢漂移和轻微日光。候选云团按覆盖率筛选，以不同高度和尺寸分布于 360°，没有经度接缝。`Cloud Group Size Inverse` 默认 `0.6`，`Cloud Edge Softness` 默认 `0.1`，可在材质中调整。
- `IslandSky.cginc`：天空与海水共用云团形状、颜色和漂移时间；海面天空底色仍按 `reflect(-viewDirection, waterNormal)` 采样。近处云影使用世界坐标下的有限高度云层，避免俯视正交相机的平行视线几乎始终采到同一片无云天空；低角度视线平滑回到方向采样。没有将透视天空贴图铺到正交海面上。`IslandOcean.skyReflectionBlur` 默认 `0.08` 弧度，柔化水中云影并抑制细碎亮斑。
- 少量云影补充配置：`cloudReflectionStrength = 1.5`（由 `0.7` 提高）、`cloudReflectionCoverage = 0.4`、`cloudReflectionHeight = 12 m`。覆盖率筛选同一批天空候选云团，实际数量不超过天空配置；近处是风格化云层位置，不追求无限远天空盒的逐像素镜像。云影使用较平缓的水面法线，避免碎成密集闪点；覆盖率、强度和高度分别可调，强度设为 0 可隐藏云影。强度范围为 `0–2`，合成 Alpha 限制在 1 以内，增强时保留原有柔和遮罩。正式场景、材质和组件默认值已保存。
- `IslandOceanReflection` 的透明反射贴图只渲染远岛、商店等物体，再按 Alpha 合入天空反光。保留原有岸边 MaterialPropertyBlock、倒影投影、波浪扰动与 Metal 贴图方向。
- `DistantIslandMesh` 在编辑器生成 7 份静态网格，每份 1,620 顶点、540 三角形，总计 11,340 顶点、3,780 三角形。包含岩岛、附属小岛和棕榈轮廓。
- `DistantScenery.shader` 使用顶点颜色、方向光和局部距离雾。没有更改全局雾，以免影响近处鱼和平台。
- 远景位于 `Distant islands (scenery)`，仅含 Transform、MeshFilter、MeshRenderer；没有碰撞和玩法组件，使用 Ignore Raycast 层。它们是背景景观，不是可登陆地图。

正式场景 `Assets/HookIsland/Scenes/HookPlatform.unity` 已保存配置。相机使用 Skybox，远裁剪面为 360 m，显示用海面宽约 800 m。`HookPlatformSceneBuilder` 同时接入，之后生成场景也会包含这些表现资源。

编辑器菜单 `Tools/Hook Island/Install Sky and Distant Scenery` 可补齐配置，重复执行不会增加远岛或覆盖已有远岛摆放。`Rebuild Distant Island Meshes` 重新生成网格并保留资产 GUID；操作均要求先退出 Play。没有重新生成 `PlatformMap.bytes`，没有修改确定性玩法、网络和存档规则。

## 验证与边界

- Unity 2022.3.62f1、Built-in、Linear、macOS Metal：脚本编译完成；天空、远景和海面 Shader 均受支持，最终天空与海面 Shader 诊断为空，最终 Console 查询无 Error / Warning。编译重载期间出现过一次 MCP WebSocket 重连警告，连接恢复后继续完成验证。
- 定位原因：Unity 正交反射相机里的天空盒仍产生透视方向变化，投到海面后像平贴白片。一次性方向着色检查测得上下采样的 RGB 距离为 `0.22294`；平水面的正交反射本应使用平行视线。
- 上一轮方向采样回归（增补有限高度云层前）：用临时隔离平面，与天空 Shader 沿真实反射方向渲染的结果比较。正交、透视各检查 `0° / 90°` 两个方向，每方向 9 个像素；最大 RGB 误差 `0.00306`，正交视图的天空反光颜色范围为 `0`。这也解释了俯视下云影消失的原因。旧检查代码仅位于 `artifacts/cloud-reflection-fix/reflection-regression.cs`，没有接入正式场景；有限高度云层不再采用“正交画面云色恒定”的旧预期。
- 正常开单人房间，实际查看第三人称、俯视、抬头和转向效果；没有注入资源或更改权威状态。天空和海面倒影已经实际渲染。
- 一次性检查确认：7 个远景 Renderer、0 Collider、0 MonoBehaviour；海面更新后岸边距离贴图、范围参数、反射贴图保留；反射相机使用透明 SolidColor，反射贴图同时包含 Alpha 为 0 的天空背景和 Alpha 为 1 的物体。
- 安装器重复执行仍为 7 个远景。验证后退出 Play，恢复保存的俯视相机；临时截图相机和贴图已释放。
- 本轮记录位于 [verification.json](../artifacts/cloud-reflection-fix/verification.json)，前一轮远景接入记录保留于 [cartoon-sky/verification.json](../artifacts/cartoon-sky/verification.json)。运行时静态合批会使多个 Renderer 引用同一合并网格，网格预算按编辑状态资产逐份统计。
- 本次仅改表现层，未运行完整 `scripts/verify.sh`、四客户端验证或发布构建，未做 GPU 性能分析及跨显卡验证。

### 少量云影增补验收

2026-10-28 在正确 Unity 实例中完成脚本编译，海面与天空 Shader 均受支持，Shader 诊断为空；Console 无 Error，有既有粒子 `BakeMesh` 过期 API 警告。临时相机按正式场景俯视相机，以及现有第三人称的 75° 视野、25° 俯角、3.1 m 距离取景，并以 90° 方位查看地平线。截图实际查看了云影边缘、水面留白和转向效果，没有移动正式相机或改动玩法状态。临时相机、贴图均已释放。

同一取景下，通过打开／关闭云覆盖进行一次性像素对比：修正前超过 `0.012` 线性通道差值的像素约 `0.00068%`，增补后约 `5.04%`，最大差值约 `0.051`。该数值只描述这次俯视取景，随视角与云漂移变化；覆盖率配置 `0.4` 不表示屏幕的 40% 被白云覆盖。记录与截图位于 `artifacts/cloud-atmosphere/`。本轮没有运行 Play 实玩或联机回归，改动不进入确定性状态。

后续提高可见度：仅将强度从 `0.7` 调到 `1.5`，云团数量、分布、高度和 `0.08` 的柔化参数不变。临时相机在同一帧分别渲染关闭云影、旧强度、新强度；最大线性通道对比约从 `0.0471` 提高到 `0.1020`，平均对比约为原来的 `2.13` 倍。已实际查看增强后的俯视画面，Unity 编译通过，海面 Shader 诊断为空；对比截图位于 `artifacts/cloud-atmosphere-contrast/`。仅改表现层，未运行 Play 实玩、联机或框架全量回归。

### 云团与云影的缓慢运动

天空和水面继续在 `IslandSky.cginc` 中共用同一漂移函数与运行时间。默认 `Cloud Drift = 0.35`，对应约 `1.44°/分钟` 的方位漂移，连续计算，不按时间分段重生云团。没有增加候选云团或反射覆盖率，也没有提高亮度或锐度。

`IslandOcean.cloudReflectionWarp` / 海面材质 `Cloud Reflection Wave Warp (Metres)` 默认 `0.18 m`，范围 `0–1 m`，已保存到正式场景与材质；后续生成场景时组件默认值同样生效。它以两组平滑波形轻微扭曲云影的世界坐标，默认水速下周期约为 `18.6 s / 22.6 s`；细小波纹对云影法线的影响由 `0.25` 降到 `0.1`，保留轻微折射，减少细碎闪动。`0.08` 弧度的柔化、`1.5` 的强度和 `0.4` 的反射覆盖率保持不变。

验证：Unity 编译通过，天空与海面 Shader 受支持，诊断均为空。Play 模式下临时相机分别录取俯视海面和地平线，每个视角 60 帧，覆盖实际游戏时间约 `23.88 s`；已查看首尾画面的云团位置和云影形变。相机固定，未调整游戏时间倍率，也未注入玩法输入或资源。岸边距离贴图与物体倒影参数保留，录取后临时相机均已释放。记录位于 [cloud-drift/verification.json](../artifacts/cloud-drift/verification.json)，[连续画面预览](../artifacts/cloud-drift/cloud-motion.gif) 按实际采样时间播放，循环接缝属于录像边界。仅改 Unity 表现层，未运行联机一致性、框架全量回归、发布构建或 GPU 性能分析。

## 最终场景截图

俯视海面（临时截图相机复制实际游戏相机，无覆盖层 HUD）：

![提高可见度后的少量柔和云影](../artifacts/cloud-atmosphere-contrast/after.png)

抬头并转向查看云团与远岛（临时截图相机，从第三人称位置调整俯仰角与方位角）：

![云团与远岛](../artifacts/cloud-reflection-fix/captures/final-game-horizon-yaw90.png)
