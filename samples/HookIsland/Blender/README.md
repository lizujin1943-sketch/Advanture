# 钩索小岛 · Blender 场景

当前批准的源文件是 **[hook_island_v2.blend](hook_island_v2.blend)**，场景为 **`Hook Island | v2`**。Unity 的固定小岛原型继续使用这一版的烘焙导出资源。

场景包含不规则岛屿、中央圆形商店、椰子树和与岸边衔接的木码头，没有海水、海底或海中石头。矮草、小花和贴地碎石作为可编辑部件保留，四周沙岸可用于出钩。

## 编辑结构

| Collection | 内容 |
| --- | --- |
| 01 · Island terrain | 可编辑三角网格地面、独立沙岸底座 |
| 02 · Round hook shop | 圆形木墙、柜台、橙色屋顶、挂钩、灯笼、木桶 |
| 03 · Coconut tree | 弯曲树干、8 片叶子、2 个小椰果 |
| 04 · Shore-connected pier | 8 块栈桥板、5 块接岸引桥板、支柱、横梁和绳索 |
| 05 · Flowers grass and gravel | 低矮花草和少量嵌地碎石 |
| 06 · Lighting and cameras | 灯光、整体与局部正交相机 |
| 07 · Reference (hidden) | 参考层；参考图片打包在源文件内 |

草地分布根据参考图描绘，使用 `reference_data/grass_distribution.png` 和 `Reference_Plan` UV。`reference_data/` 中的参考输入、全部建模脚本及正式导出资源继续保留。

## 当前预览与几何记录

- [整体效果](renders/v2/hero_pass02.png)
- [参考俯视](renders/v2/reference_top_pass02.png)
- [v2 几何检查记录](scene_validation_v2.json)

记录中的岛面约 **31.922 × 26.702 米**，码头净宽 **2.95 米**，向岛内搭接 **1.4 米**。入口横向 41 个采样点的高差为 **−0.7～8.24 毫米**，另有 527 个引桥采样。场景包含 266 个网格对象、12,422 个多边形；这些是源模型统计，不是 Unity 导出后的三角形数。

## 重建与导出

日常修改从 `hook_island_v2.blend` 开始。需要从脚本重新搭建时，在新的 Blender 文件中执行 `rebuild_scene.py` 生成基础场景，再通过 `refine_fidelity.py` 的修整步骤得到 v2。基础脚本生成的 v1 文件、旧预览及旧检查记录已从仓库移除并忽略，避免与当前批准版本混淆；脚本本身仍是重建管线的一部分。

`export_unity.py` 仅导出 `Hook Island | v2`，在临时场景中应用修改器并烘焙材质，不保存或覆盖源文件。导出到 `../Exports/` 后，使用 `../Tools/prepare_unity_assets.py` 将模型和贴图送入 Unity。`export_manifest.json` 留在 `Exports/` 作为岸线与地图生成输入，无需复制进 Unity 资源目录。

详细导出步骤与既有 Unity 验证范围见[小岛说明](../README.md)。本目录的几何记录只描述 Blender 模型，不替代 Unity 碰撞、角色行走和联机验证。
