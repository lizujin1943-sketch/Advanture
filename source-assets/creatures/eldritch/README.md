# 沉钟镇 · 普通敌人模型与动画

`EldritchLibrary.blend` 是可编辑源文件，包含 10 个独立模型集合、骨骼、材质、80 个动作与预览灯光。`exports/` 中的 FBX 供 Unity 使用，`manifest.json` 记录种类、面数、骨骼数量和动作时长。预览地面和灯光不会导出到游戏。

本轮覆盖潮痕眷属、逆棘奔兽、圣螺守卫、裂瞳观测者、溺钟引路者、腐潮囊、无面唱诗者、千嗣母巢、折影行者、星骸执钟者。母巢幼体复用缩小的潮痕眷属；大章鱼关底 Boss 继续使用 `../abyssal-watcher/` 的独立模型和动作。

每种敌人有 Idle、Move、Windup、Attack、Recover、Hit、Emerge、Death 八个动作。待机、移动可循环；其余动作由确认的战斗阶段控制。模型使用原地动画，不包含权威位移、碰撞体或伤害事件。

造型强调湿润皮肤、鳃裂、甲壳、口腔和局部生物荧光。双足、四足、蛛足采用不同步态；软体使用多节触须，末端运动有相位延迟。网格约 1.56 万至 3.27 万三角形，每个模型 12～47 根骨骼，顶点最多受两根骨骼影响。

## 重新制作与接入

在仓库根目录执行：

```sh
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python scripts/blender/create_eldritch_library.py
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python scripts/blender/verify_eldritch_library.py
```

随后在目标 Unity 工程执行 `Tools → Hook Island → Install Animated Eldritch`。导入器保留资源 GUID，按材质名称匹配 FBX 子网格，设置 Generic 动画和双骨骼蒙皮，并生成运行时所需的 `Resources/EldritchVisuals.asset`。正式资源已随工程保存，不需要每次启动重新生成。

修改模型：`scripts/blender/eldritch/species.py`；网格和蒙皮：`geometry.py`；动作：`animation.py`；皮肤贴图和材质：`materials.py`。源文件、FBX、贴图均为本项目制作，不依赖在线生成服务。

## 预览与验证

`Tools → Hook Island → Export Animated Enemy Portraits` 输出实际 Unity 模型图鉴，不改地图数据。Blender 动作预览可运行 `verify_eldritch_library.py -- --render`，输出位于 `artifacts/eldritch-rework/`。

本轮验证记录见 `docs/eldritch-blender-rework.md`。当前资源仍适合继续进行比例、皮肤和动画节奏的美术迭代。
