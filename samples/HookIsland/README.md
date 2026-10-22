# 钩索小岛

当前钓鱼平台请打开 `HookPlatform.unity`。背包与材料共用格子，每人拥有独立金币；Tab / I 切换角色和背包，E 开启附近岸上大宝箱或购物，F 拾取。共享委托在捕获后自动推进并获得团队诱饵，海域按委托进度开放鱼群并提供恢复机会。实现与验收说明见仓库 `docs/backpack-implementation.md`、`docs/treasure-chests-implementation.md`、`docs/commissions-implementation.md`、`docs/ocean-progression-implementation.md`。以下岛屿场景说明保留旧基础版规则。

- [玩法设计文档](../../docs/hook-island-game-design.md)：钩索捕鱼、合作整蛊、商店成长与整局循环。
- [可玩联机基础版](../../docs/hook-island-playable.md)：1～4 人房间、操作、运行和验证方法。
- [平台地块实现](../../docs/hook-platform-implementation.md)：`HookPlatform.unity`、木板／鱼骨扩建、个人材料与多人同步。
- [倒地复活实现](../../docs/revival-implementation.md)：岸上靠近按住 R 免费扶起；倒地 60 秒后死亡，个人付费复活与全队失败同步。
- 当前批准的 Blender 源场景：[hook_island_v2.blend](Blender/hook_island_v2.blend)。
- Unity 场景：`../TacticalGame.Unity/Assets/HookIsland/Scenes/HookIsland.unity`。
- [Unity 场景说明](../TacticalGame.Unity/Assets/HookIsland/README.md)。

Blender 源场景保持纯岛屿；海面在 Unity 场景中实现。现已接入 1～4 人移动、瞄准出钩、拉鱼上岸与共享金币。靠近商店按 E，可用全队金币为自己升级射程和出钩速度，各 3 级，购买与效果随联机同步。左键也能钩住队友拉到身边，右键提前放开；购物与释放后的 3 秒保护同步生效。怪物、抢鱼与落水救援留待后续实现。

鱼群现在分布于近、中、远海，持续潜游、前跃和落水。初始钩索射程 8 米、出钩速度 9 米/秒；射程升级为 12 / 16 / 20 米，逐步解锁原本够不到的高奖励鱼。所有轨迹和命中仍由同一套整数帧同步模拟执行。

## 重新导出

在 Blender 打开 `Blender/hook_island_v2.blend`，选择 `Hook Island | v2` 场景。通过 Python Console 执行：

```python
import importlib.util, sys
path = bpy.path.abspath("//export_unity.py")
spec = importlib.util.spec_from_file_location("island_unity_export", path)
exporter = importlib.util.module_from_spec(spec)
sys.modules["island_unity_export"] = exporter
spec.loader.exec_module(exporter)
exporter.setup()
for group in exporter.GROUPS:
    exporter.bake_group(group)
exporter.finish()
```

脚本在临时场景中复制并应用模型修改器，保留原物体的程序纹理坐标，烘焙颜色与 AO，按地面、沙岸、商店、树、码头、点缀合并为六组，然后导出 FBX。导出结束后回到原场景；不保存或覆盖源 `.blend`。

从仓库根目录运行：

```sh
python3 samples/HookIsland/Tools/prepare_unity_assets.py
```

需要 Python 的 NumPy 与 Pillow。此步骤把 FBX 和贴图复制到 Unity 的 `Assets/HookIsland/Art/`，并从实际岸线生成 512×512 的距离贴图，用于浅海颜色与泡沫定位。

最后在 Unity 菜单选择 **Hook Island → Create or Rebuild Island Scene**。重建会更新生成的小岛预制体和 `HookIsland.unity`；需要保留的手工调整请先另存。随后运行 `./scripts/sync-hook-island.sh`，待编译完成，再选择 **Tools → Hook Island → Set Up Playable Fishing** 恢复玩法入口。

## 本次验证

2026-10-20，在 Unity 2022.3.62f1 / macOS / Built-in / Linear 下完成：

- 六组 FBX 网格、六张颜色贴图、六张 AO 贴图和海面着色器导入。
- 岛面尺寸约 31.922×26.702 米，与 Blender 一致；模型 22,620 个三角形。
- 地面、商店、码头使用导入模型的静态 MeshCollider。
- Play 模式运行超过 60 秒，无 Unity Console Error；海面材质时间与游戏时间同步。
- 对比固定相机的两张运行截图，海域画面确实变化。
- 岛屿相机临时使用较远的阴影范围，渲染后恢复原工程的阴影距离。
- Unity 场景重新打开后的材质、贴图和 Shader 检查通过。

美术检查报告与实际 Unity 渲染位于仓库 `artifacts/hook-island-unity/`。捕鱼、码头行走和联机的验证记录另存于 `artifacts/hook-island-playable/`，详见[可玩基础版说明](../../docs/hook-island-playable.md)。

当前房间支持房主提前开始、预留空位晚加入及同应用内“重新连接”。规则／状态／房间版本 22，状态 HISE，输入 10、地图 HIMF；拒绝旧构建及旧档。实现和实际验证范围见仓库 `docs/live-room-recovery-implementation.md`。
