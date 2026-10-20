# 钩索小岛 · Blender 场景

打开 **`hook_island_v1.blend`**，当前场景为 **`Hook Island | v1`**。

已按多视角概念图搭建不规则岛屿、中央圆形商店、椰子树和西南码头。没有海水、海底或海中石头。岛面只保留矮草、小花和贴地碎石，四周沙岸可以继续用于出钩玩法。

## 编辑结构

| Collection | 内容 |
| --- | --- |
| 01 · Island terrain | 可编辑三角网格地面、独立沙岸底座 |
| 02 · Round hook shop | 圆形木墙、弧形柜台、分面橙色屋顶、挂钩、灯笼、木桶 |
| 03 · Coconut tree | 弯曲树干、10 片叶子、2 个小椰果 |
| 04 · Shore-connected pier | 13 块栈桥板、5 块接岸引桥板、支柱、横梁和绳索 |
| 05 · Flowers grass and gravel | 低矮花草和少量嵌地碎石 |
| 06 · Lighting and cameras | 日光、补光和 7 个正交相机 |
| 07 · Reference (hidden) | 预留参考层；原参考图已打包到文件的 Images 数据中 |

商店、树、木板和装饰分别保留为可选取的模型部件。地面草沙过渡使用 `GrassDistance` 属性和程序化材质；未叠加悬浮草皮。

场景比例根据图片估算：岛面约 **31.9 × 26.7 米**。码头宽 **2.4 米**，引桥向岛内搭接 **2.1 米**。引桥首边每 10 厘米采样一次实际地形高程，入口高差约 **5.5–7.2 毫米**；下部梁和靠岸支柱嵌入岸坡。

## 实际模型渲染

- [整体透视效果](renders/island_hero.png)
- [垂直俯视](renders/island_top.png)
- [正面](renders/island_front.png)
- [左侧](renders/island_left.png)
- [右侧](renders/island_right.png)
- [码头衔接近景](renders/pier_connection.png)
- [商店近景](renders/shop_detail.png)

对应相机分别为 `Camera_Hero`、`Camera_Top`、`Camera_Front`、`Camera_Left`、`Camera_Right`、`Camera_Dock_Detail` 和 `Camera_Shop_Detail`。

渲染分辨率：整体 1600 × 1100，俯视 1400 × 1400，三张立面 1600 × 520，两个局部 1400 × 1050。切换相机重新渲染时，应同时使用对应画幅。灰蓝色背景来自 World 节点，不是海水模型。

## 构建与检查

脚本按地形、建筑树木、码头点缀、材质灯光分开保存。需要从头生成时，在新的 Blender 文件中通过 Text Editor 打开并执行 `rebuild_scene.py`。脚本检测到已有同名场景时会停止，不覆盖手工编辑。

`scene_validation.json` 记录实际几何检查结果，包括入口 41 个横向采样、引桥 527 个采样、椰果数量和无海水对象检查。当前约 1.35 万个多边形面，不包括修改器求值后的增量。

已检查模型几何和渲染；尚未接入 Unity 的角色移动、碰撞或联机玩法。材质是 Blender 节点材质，后续用于游戏引擎时需烘焙或重建对应材质。
