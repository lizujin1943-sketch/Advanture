# 钩索小岛 · Unity 场景

当前钓鱼平台请打开 `HookPlatform.unity`。背包与材料共用格子，每人拥有独立金币；Tab / I 切换角色和背包，E 购物，F 拾取。实现与验收说明见仓库 `docs/backpack-implementation.md`。以下岛屿场景说明保留旧基础版规则。

新增平台入口：**`Assets/HookIsland/Scenes/HookPlatform.unity`**。Play 后按 **B** 进入铺地，**1／2** 选择木板／鱼骨块，左键确认，右键取消预览。配方默认各花费自己的 4 份材料，详见[平台地块实现](../../../../docs/hook-platform-implementation.md)。以下说明保留给原岛屿场景。

打开 **`Assets/HookIsland/Scenes/HookIsland.unity`**，点击 **Play**，选择“先自己玩”或创建 2～4 人联机房间。WASD 移动，鼠标瞄准，左键出钩，右键收回空钩或放开队友；鱼被拉上岸后自动获得全队金币。左键命中队友会将其拉到身边，岸边与障碍会阻止继续拖拽，释放后有 3 秒保护。靠近中央商店按 **E**，用共享金币为自己升级射程和出钩速度，各可升 3 级；购物期间受保护，联机同步扣款、等级与拉拽。完整连接和验证方法见仓库 [联机基础版说明](../../../../docs/hook-island-playable.md)。

鱼会先在水下游动，再向前跃起并落水，只有跃出水面时可钩中。初始射程 8 米、速度 9 米/秒；需靠近岸边、沿鱼影方向预判出钩。射程 Lv.1 / Lv.2 分别解锁中海青鱼 / 远海金鱼，远海鱼不会进入初始射程。滚轮可缩放视角，观察更远的鱼群。

## 场景内容

| 对象 | 内容 |
| --- | --- |
| Hook Island | Blender v2 导出的六组模型：地面、沙岸、商店、椰子树、码头、花草碎石 |
| Ocean | 160×160 米海面，轻微顶点起伏、移动色块波纹、浅海渐变、沿岸泡沫 |
| Island Camera | 与 Blender 主视角对应的正交相机 |
| Warm Sun | 与 Blender 对应方向的日光与实时阴影 |
| Island Fishing | 输入、联机房间、中文界面和运行时角色 / 鱼 / 钩绳 |
| Pier Walk Surface | 码头斜坡与木板之间连续的不可见行走面 |

可复用岛屿预制体为 `Prefabs/HookIsland.prefab`。模型是六个大部件；如需修改单个柜台、木板或叶片，请回到 Blender 源场景修改并重新导出。

## 材质和颜色

Blender 的程序化草地分布、木材与纯色材质已烘焙为六张颜色贴图，另有六张 AO 贴图。Unity 使用 Built-in 管线的 `Hook Island/Baked Color Surface` 着色器，并保持项目的 Linear 色彩空间。法线使用 FBX 导入结果，保留低多边形分面。

主要颜色、纹理分布和光照方向已对照 Blender 渲染调整。两种渲染器的光照、金属反光和灯笼发光并非完全一致；此版本以整体颜色、低多边形外观和运行成本为优先。

地面草纹与静态 AO 写在贴图中；物体投射的日光阴影仍由 Unity 实时计算。移动商店或树之后，地面 AO 中对应的接触痕迹需要重新烘焙。

## 海面调节

选中 **Ocean → Island Ocean**：

- **Shallow / Deep Color**：近岸和外海颜色。
- **Wave Height**：起伏幅度，默认 0.035 米。
- **Wave Speed**：波纹和泡沫的运动速度。
- **Foam Width**：岸边泡沫带宽度，默认 0.85 米。
- **Shallow Distance**：近岸渐变范围，默认 6 米。
- **Preview In Editor**：开关 Scene 视图动态预览。

海面采用独立的 GPU 表现，没有海水碰撞、浮力、反射相机或流体模拟。岸线距离贴图与当前岛的位置、形状对应；移动整个岛屿或更改轮廓后，需要更新距离贴图或相应的水面坐标映射。

## 重建与文件位置

Unity 菜单 **Hook Island → Create or Rebuild Island Scene** 会重建生成场景。先保存手工调整的副本；编辑器脚本会检测未保存的场景。重建后选择 **Tools → Hook Island → Set Up Playable Fishing** 重新添加玩法入口。

- 原始 Blender：`samples/HookIsland/Blender/hook_island_v2.blend`
- 导出脚本：`samples/HookIsland/Blender/export_unity.py`
- 资源复制与岸线生成：`samples/HookIsland/Tools/prepare_unity_assets.py`
- 场景构建：`Assets/HookIsland/Editor/HookIslandSceneBuilder.cs`
- 核心玩法文档：仓库 `docs/hook-island-game-design.md`

小岛资源与脚本都位于 `Assets/HookIsland/`，有独立程序集。既有 Yard 场景和默认构建场景列表不变。

当前房间支持房主提前开始、预留空位晚加入及同应用内“重新连接”。规则／状态／房间版本 22，状态 HISE，输入 10、地图 HIMF；拒绝旧构建及旧档。实现和实际验证范围见仓库 `docs/live-room-recovery-implementation.md`。
