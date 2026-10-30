# 鱼模型导入与替换实施计划

> 使用 writing-plans 记录接入边界，在当前会话连续实施和检查。用户已明确要求“导入并替换”，无需再次确认。

**Goal:** 将已确认并重做的 10 种海鱼、食人鱼和 78 个动作导入正式 Unity 工程，替换运行时基础几何鱼。

**Architecture:** 可重复运行的编辑器导入器生成点采样贴图材质、Generic 模型预制体及只含美术信息的资源目录。独立鱼模型视图拥有动画图；普通鱼和敌人表现从确认状态选择模型、采样姿态，世界坐标与结算继续由原有模拟负责。

**Tech Stack:** Unity 2022.3.62f1，现有内置渲染管线表面 Shader，FBX，Animation Playables。

**Spec:** 用户本轮“导入并替换”；`docs/fish-models-animation.md` 的源资源约定。

## 约束与重点

- 只操作 `samples/TacticalGame.Unity`；不修改纯 C# 模拟、协议、数值和存档。
- 保留源像素图案，使用 Point、无有损压缩，关闭根运动。
- 逐鱼种映射，不根据鱼池索引固定鱼种；同一鱼池刷新后可换模型。
- 导入器保留资产 GUID，可重复执行；不保存、重建或覆盖当前场景。
- 动作由确认状态时间采样；恢复或晚加入能直接显示当前姿态，不触发伤害或奖励。
- 删除表现时销毁动画图，重开局清除旧实例，避免残留或双重渲染。

## 实施

### 1. 先验证交付边界

- [x] 添加集成检查：实际游戏鱼包含带纹理的蒙皮模型，按确认鱼种更新，播放时世界位置和确认状态字节不变。
- [x] 在替换前运行检查，确认因旧几何鱼缺少蒙皮模型而失败。

### 2. 资源与表现接入

- [x] `Runtime/Data/Visuals/FishVisualCatalog.cs`：独立鱼种美术配置、预制体与动作引用。
- [x] `Runtime/Presentation/Fish/FishModelView.cs`：模型实例、材质等级提示、手动采样及动画图释放。
- [x] `Editor/Art/FishArtInstaller.cs`：从仓库源文件复制 FBX/PNG，设置导入、材质、预制体和资源目录。
- [x] `Runtime/Presentation/Fish/FishAppearance.cs`：将确认鱼状态映射为姿态。
- [x] `FishingPresentation.cs` 与 `EnemyPresentation.cs`：替换普通鱼和食人鱼，保留水面轨迹、逃脱和敌人效果。

### 3. 验收与交付

- [x] Unity 编译通过，运行新集成检查及相邻表现测试。
- [x] 检查 11 个预制体、78 个动作、尺寸朝向、Point 纹理和材质引用。
- [x] 在 Unity 中渲染模型并运行正式场景，检查实际鱼与控制台。
- [x] 更新制作文档的接入状态，保存截图与验证结果到 `artifacts/fish-unity/`。

本次只改表现和美术资源；按 `docs/agent-workflow.md` 不跑四客户端一致性与全仓 `verify.sh`。不会创建工作树或提交夹杂其他任务的工作区。

## 结果

11 个模型、78 个动作已接入；鱼接入、敌人表现及恢复相关 10 项检查通过。资产审计、重复导入 GUID 稳定性和正式场景运行检查通过。较宽测试中角色 Hand_L 穿插与相机移动两项失败已独立复现并记录，未改动它们的实现。详细证据和验证边界见 `docs/fish-models-animation.md` 与 `artifacts/fish-unity/`。
