# Cartoon Sky and Distant Scenery Implementation Plan

> **For agentic workers:** Execute inline; this is a local presentation change in the existing Unity scene.

**Goal:** 参考《海盗法则》的天空、云和远景，延续明亮青绿的卡通海面；按最新反馈把云改成圆润柔和的轮廓。

**Architecture:** 天空与海面共用方向采样函数，海面按实际反射方向读取天空；平面反射贴图只渲染物体；远岛使用离线生成的静态彩色网格。所有内容只属于 Unity 表现层。

**Tech Stack:** Unity 2022.3 Built-in、ShaderLab/CG、Editor 资源生成。

**Spec:** 用户本轮“美化一下天空盒和天空，并增加一些白云；以及美化一下远景效果”。

## Constraints and review focus

- 只修改正式工程 `samples/TacticalGame.Unity`；保留已有海面与玩法修改。
- 不改变地图数据、权威状态、网络协议、碰撞、鱼或资源分布。
- 云在 360° 转向时连续；倒影与天空一致；海面边缘融入地平线。
- 远景没有碰撞和玩法组件，不进入鱼钩目标；资源保存为普通项目资产。
- 实际检查第三人称、俯视和抬头视角，结束后恢复编辑状态。

## Tasks

- [x] 新增可调天空材质 Shader：蓝青渐变、暖色地平线、柔和白云、轻微太阳光晕、慢速云漂移。
- [x] 更新反射和海平线颜色接入；现有岸边 MaterialPropertyBlock 保持完整。
- [x] 新增编辑器远景生成器与远景 Shader：疏落的远岛、浅滩和棕榈轮廓；保存网格和材质。
- [x] 当前场景和未来场景生成器同时接入，保存场景，不重建玩法地图数据。
- [x] Unity 编译和 Shader 诊断，实际运行截图，检查反射与无碰撞配置，记录验证边界。

按仓库工作方式，本次不跑完整网络验证；不为颜色或形状写镜像断言测试。

实施与验证结果见 [cartoon-sky-implementation.md](../../cartoon-sky-implementation.md)。
