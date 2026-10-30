# 明亮卡通海面（2026-10-28）

## 参考与还原方向

参考对象是 Codex7 的《海盗法则 / Pirate Code》，见[官方游戏页](https://play.google.com/store/apps/details?hl=en_GB&id=com.happyfish.piratecode)。实际查看了以下 5 张游戏截图；以两张热带海域画面的青绿色作为主色，以冰海画面辅助观察波纹和倒影的细节。

| 参考图 | 观察重点 |
| --- | --- |
| [热带海域：船只与远岛](https://imag.malavida.com/mvimgbig/download-fs/pirate-code-26602-6.jpg) | 明亮青绿色中景、较深的前景、短而密的小波纹 |
| [热带海域：环形岛屿](https://imag.malavida.com/mvimgbig/download-fs/pirate-code-26602-7.jpg) | 岛屿与船的倒影随水面破碎，青绿与蓝色之间柔和过渡 |
| [冰海：岛屿和航行船只](https://s3.elespanol.com/k/imgp/cms/elespanol/8617853.jpg?p=w:1706/h:960&h=0lJbCFBg) | 波纹尺度、天空亮部的柔和反射 |
| [冰海：另一航行视角](https://imag.malavida.com/mvimgbig/download-fs/pirate-code-26602-2.jpg) | 小波纹的方向性和远处海面的层次 |
| [冰海：建筑与光柱倒影](https://minireview.io/common/uploads/review/4547930327037880901fee6879f4a7b0.jpg) | 物体倒影的纵向延伸与随波扰动 |

这些画面的共同特征是连续的细小波纹和反光，海面本身没有明显的硬边多边形拼块。没有复制或下载参考游戏的贴图；新增波纹贴图由仓库脚本生成。

## 已接入的效果

- 移除原来的 Voronoi 硬边色块，用连续噪声形成宽而柔和的青绿深浅变化。
- 两层有风向的小波纹以不同速度流动，叠加缓慢的小幅起伏。第二层偏转约 30°，加入低频扰动减弱重复排列。
- 增加柔和天空反光、细碎阳光高光，以及平台、商船、鱼等实际物体的平面倒影。倒影随法线扰动，斜视时反射更强。
- 远处海面逐渐过渡到浅青色；扩大显示用水面，第三人称不会直接看到原先水面网格的边缘。
- 保留已有平台岸边波纹、鱼影、水冠、圆形扩散波纹及尾后水花。

接入场景为 `Assets/HookIsland/Scenes/HookPlatform.unity`。场景、材质和场景生成器均已配置；没有重新生成玩法地图数据，也没有迁移旧的 `HookIsland` 场景配置。

## 参数与资源职责

| 配置 | 当前值 |
| --- | --- |
| 浅水色 RGB | `(0.10, 0.73, 0.66)` |
| 深水色 RGB | `(0.025, 0.40, 0.47)` |
| 泡沫色 RGB | `(0.85, 0.98, 0.96)` |
| 波纹缩放 / 强度 | `0.18 / 0.26` |
| 反射强度 / 阳光高光强度 | `0.48 / 0.35` |
| 几何波高 / 速度 | `0.035 m / 0.75` |
| 平面反射贴图 | `512 × 512`，上限可配 `128–1024` |

`IslandOcean` 管理颜色、波纹和动画参数；`IslandOceanReflection` 单独管理反射相机和渲染贴图；`PlatformShoreline` 继续根据确认态地块更新岸边遮罩。各组件写入前读取已有的 MaterialPropertyBlock，互相保留参数。

反射相机和贴图在关闭组件或退出场景时释放；使用固定方形贴图，切换不同宽高比的视图时复用资源。没有有效反射贴图时，Shader 使用天空反光。反射可通过关闭 `IslandOceanReflection` 降低开销。

`scripts/generate-ocean-ripples.py` 用固定随机种子生成无缝 `512 × 512` 波纹贴图。RG 保存线性坡度，B 保存高度；Unity 导入关闭 sRGB，开启重复、Mipmaps、三线性过滤和 4 倍各向异性过滤。

波浪、倒影、天空变化均属于本地表现，不读取或修改权威状态、金币、鱼的位置、碰撞、网络命令或存档。

## 验证

- Unity `2022.3.62f1`、Built-in、Linear、macOS Metal：脚本编译通过，海水 Shader 支持且编译诊断为空，最终运行检查没有 Error 日志。
- 正常单人房间走现有 UDP 开局流程，实际查看俯视和第三人称画面；鱼、浮木、水冠和圆形入水波纹正常显示。验证后退出 Play，恢复已保存的俯视相机。
- 现有 `PlatformShorelineTests` **3/3 通过**，覆盖凹边/孔洞、扩建/重置/远处地块和动画更新时保留遮罩。
- 一次性实际渲染检查通过：不同视口宽高比复用同一张反射贴图；关闭后释放、重新开启后重建；岸边遮罩保留；反转剔除与水面可见性恢复。反射投影与相机视口坐标误差约 `2.98e-8`。
- 验证记录：[verification.json](../artifacts/cartoon-ocean/verification.json)。截图过程中观察到 Metal depth load/store 提示；未进行独立的 Metal 性能分析。
- 本次只改表现层，未跑完整 `scripts/verify.sh`、四客户端一致性或发布构建，也未做不同显卡的帧率测试。

## 实际运行截图

俯视：

![俯视实际运行](../artifacts/cartoon-ocean/captures/ocean-final-game-overhead.png)

第三人称：

![第三人称实际运行](../artifacts/cartoon-ocean/captures/ocean-final-game-third-person.png)

改动前：

![原海面](../artifacts/cartoon-ocean/captures/ocean-before-overhead.png)
