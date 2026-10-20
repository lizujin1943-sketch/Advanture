# 钩索小岛

- [玩法设计文档](../../docs/hook-island-game-design.md)：钩索捕鱼、合作整蛊、商店成长与整局循环。
- [可玩联机基础版](../../docs/hook-island-playable.md)：1～4 人房间、操作、运行和验证方法。
- 当前批准的 Blender 源场景：[hook_island_v2.blend](Blender/hook_island_v2.blend)。
- Unity 场景：`../TacticalGame.Unity/Assets/HookIsland/Scenes/HookIsland.unity`。
- [Unity 场景说明](../TacticalGame.Unity/Assets/HookIsland/README.md)。

Blender 源场景保持纯岛屿；海面在 Unity 场景中实现。现已接入 1～4 人移动、瞄准出钩、拉鱼上岸与共享金币。商店交易、怪物和整蛊玩法留待后续实现。

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
