# 七号转运站 / Yard 07

简单的低多边形工业庭院，活动区 32 × 24 米。两个出生区、两个集装箱、四个箱子掩体和一座封闭仓库；全部在同一平面。适合先做 2～4 人射击原型测试。

- **打开并修改：** `Blender/yard_07.blend`
- **通用模型：** `Exports/yard_07.glb`（包含颜色材质）或 `Exports/yard_07.fbx`
- **独立碰撞体：** `Exports/yard_07_collision.fbx`
- **布局和碰撞尺寸：** `Data/yard_07.layout.json`
- **查看效果：** `Previews/yard_07_hero.png`、`yard_07_top.png`、`yard_07_eye.png`
- **设计说明：** [七号转运站设计](../../docs/maps/yard-07-design.md)
- **Unity 第一人称试玩：** [打开工程与操作说明](../TacticalGame.Unity/README.md)

![地图预览](Previews/yard_07_hero.png)

## 模型组织

| Blender 集合 | 内容 |
| --- | --- |
| `00_Ground` | 地面和基础 |
| `01_Architecture` | 边界、出生遮挡墙、封闭仓库 |
| `02_Containers` | 两个集装箱 |
| `03_Props` | 掩体木箱和边缘桶组 |
| `04_Markings` | 标线和标识 |
| `80_GameMarkers` | 两个出生点、中央参考点；均为标记，不含玩法 |
| `90_Collision` | 默认隐藏的盒形碰撞体 |
| `99_Presentation` | 预览相机、灯光、摄影背景；不导出到游戏模型 |

每个集合中的物体均可独立调整。碰撞体是简单盒子，不用于渲染；更换掩体位置后也要同步其碰撞体和布局数据。GLB 保留基础材质；FBX 导入不同引擎后可能需要重新指定材质。

Blender 中 Z 向上，JSON 也是此坐标系；GLB/FBX 导出使用 Y 向上。读取 JSON 时不要直接把 Z 当作引擎的平面坐标。模型尺寸按米制作，导入后以 8 米长的集装箱复核比例。

## 从脚本重新生成

使用 Blender 的后台模式重新构建以下资源，不会改动正在使用的 Blender 窗口。重新生成会覆盖本示例生成文件；手工修改后请先另存副本。

```sh
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python-exit-code 1 --python samples/TacticalMap/Tools/build_yard_07.py
```

重新渲染时加载模型，并选择 `hero`、`top` 或 `eye`：

```sh
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup samples/TacticalMap/Blender/yard_07.blend --python-exit-code 1 --python samples/TacticalMap/Tools/build_yard_07.py -- --stage render --view hero
```

这张地图与 `framework/` 无依赖，没有角色、射击和网络逻辑。仓库不可进入，屋顶不作为可玩区域；正式玩法接入后仍需设置玩家控制器和碰撞规则。

静态布局及 GLB 内容检查：

```sh
python3 samples/TacticalMap/Tools/validate_yard_07.py
```

GLB/FBX 导入复查：

```sh
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python-exit-code 1 --python samples/TacticalMap/Tools/verify_exports_blender.py
```

本次模型约 1.1 万个三角形，17 个盒形碰撞体。验证结果保存在 `Data/validation.json` 和 `Data/roundtrip-validation.json`；这些是静态与文件导出检查，不代表已经完成游戏内测试。
