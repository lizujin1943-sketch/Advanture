# 像素风海鱼资源

`FishLibrary.blend` 可用 Blender 5.2.1 打开。包含完整的 10 种海鱼和食人鱼、可编辑骨骼、像素贴图，以及分开的动作片段。所有贴图已经打包进 Blender 文件；FBX 嵌入贴图，GLB 内嵌贴图。外置 PNG 在 `exports/textures/`，便于后续修改。

`Concept-Pixel.png` 保存本次采用的像素风设计图。当前为第二版：逐种描绘原图中的鱼身截面、鱼鳍轮廓、眼部和嘴部位置，再制作有完整体积的网格。使用原图的像素花纹投射 UV；河鲀采用正面双眼结构和依照原图配色重绘的连续鱼皮。模型实际渲染、效果图对照与动态预览在资源包的 `previews/` 中。

每条鱼一个材质和一张 1024 × 512 点采样贴图，实际模型为 4,440～8,608 个三角面。眼部有鱼身表面起伏；厚嘴唇、下颌、牙齿、鱼鳍和长棘具有几何厚度；皇带鱼采用 S 形静止姿态及沿曲线布置的脊柱。参考图、轮廓标记和贴图不作为平面卡片放进模型。

Blender 源文件中的鱼按展示位置排列。每条鱼的 FBX、GLB 导出时已经复位到原点，缩放为 1，尺寸按米计算；Blender 鱼头朝 -Y、Z 向上，glTF 转为 +Z 朝前、Y 向上。`manifest.json` 记录文件映射、模型尺寸、面数、骨骼数和动作信息。

## 动作

| 名称 | 时长 | 循环 | 用途 |
| --- | --- | --- | --- |
| Idle | 2 秒 | 是 | 缓慢摆尾、轻微开合嘴和胸鳍 |
| Swim | 1 秒 | 是 | 从身体向尾巴传播的摆尾 |
| Burst | 0.8 秒 | 是 | 加快、大幅摆尾 |
| Leap | 0.8 秒 | 否 | 抬头与小幅扭身的跃起姿态 |
| Dive | 0.6 秒 | 否 | 低头、收鳍入水 |
| Hooked | 0.8 秒 | 是 | 强烈侧摆、张嘴挣扎；河鲀同时鼓起 |
| Flop | 1.2 秒 | 是 | 侧翻、弯曲身体、扑腾 |
| Bite | 0.6 秒 | 否 | 仅食人鱼：张嘴前摇、快速闭合、恢复 |

每个动作以 30 fps 烘焙，包含首尾边界帧。普通鱼各 7 个、食人鱼 8 个，总计 78 个。每条鱼骨架中的 NLA 轨道按动作命名；源文件默认静音 NLA 轨道并播放 `Swim`，在 Action Editor 中可切换其他动作。FBX / GLB 的动作名为上表英文名。

所有动作都不含根位移。跃起、入水、被钩拖拽的世界坐标、方向和速度由游戏模拟决定；模型动画负责姿态。模型不带碰撞体、伤害动画事件或捕获结算脚本。

## Unity 导入与替换

- FBX 使用 Generic 骨架导入方式。按 `manifest.json` 给循环片段启用 Loop Time，跃起、入水和咬击不循环。
- PNG 使用 Point 过滤，不使用有损纹理压缩；关闭会模糊像素边缘的双线性过滤。材质使用一张 Albedo、零金属度、高粗糙度。
- 关闭 Apply Root Motion。现有确认状态控制位置、朝向、鱼种、重量缩放、状态切换；不得用动画事件修改权威状态。
- 已接入正式工程 `samples/TacticalGame.Unity`，替换普通鱼和食人鱼。编辑器菜单 **Tools → Hook Island → Install Animated Fish** 从本目录的 FBX / PNG 重建 Unity 美术资源，保留既有 GUID，不重建场景。
- Unity 资源位于 `Assets/HookIsland/Art/Fish/`，鱼种目录为 `Assets/HookIsland/Resources/FishVisuals.asset`。运行时使用现有场景表面 Shader 和手动采样动画；等级提示通过材质属性块设置，不复制每条鱼的材质。
- Unity 实际渲染和接入检查位于仓库 `artifacts/fish-unity/`；当前接入与验证边界见 `docs/fish-models-animation.md`。资源压缩包仍为 Blender / FBX / GLB 源资源包，Unity 工程中的运行时接入代码在仓库维护。

## 重建

在仓库根目录执行：

```sh
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python-exit-code 1 --python scripts/blender/create_fish_library.py
/Applications/Blender.app/Contents/MacOS/Blender --background source-assets/creatures/fish/FishLibrary.blend --python-exit-code 1 --python scripts/blender/verify_fish_library.py
/Applications/Blender.app/Contents/MacOS/Blender --background source-assets/creatures/fish/FishLibrary.blend --python-exit-code 1 --python scripts/blender/render_fish_previews.py
/Applications/Blender.app/Contents/MacOS/Blender --background source-assets/creatures/fish/FishLibrary.blend --python-exit-code 1 --python scripts/blender/render_fish_previews.py -- --animation
python3 scripts/blender/compose_fish_previews.py
```

Blender 制作与验证均在独立后台进程运行，不打开或覆盖用户正在编辑的 Blender 场景。

若修改参考图或像素图集，先执行 `python3 scripts/blender/prepare_fish_reference.py`，再运行上述 Blender 命令。`scripts/blender/fish_reference_layout.py` 保存各鱼种的造型标记；`reference-atlases/provenance.json` 记录图集来源。
