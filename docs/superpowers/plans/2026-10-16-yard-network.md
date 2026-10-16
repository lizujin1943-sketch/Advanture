# Yard07 双客户端帧同步实施计划

> **For agentic workers:** Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** 两个 Unity 客户端通过现有帧同步框架互见移动和转向。

**Architecture:** 纯 C# 模拟 + 纯 C# 示例联网宿主 + Unity 输入显示；通用框架不依赖后两者。

**Tech Stack:** .NET 8 构建、netstandard2.1 类库、Unity 2022.3.62f1、Input System 1.14、现有可靠 UDP。

**Spec:** ../specs/2026-10-16-yard-network-design.md

## Global Constraints

- 全部文件位于正式仓库；不提交用户已有改动，不创建新工作树或替换现有 Unity 工程。
- 本机/可信局域网双人固定名单，30 Hz、提前 3 帧、先冻结本地输入。
- 代码有中文职责和关键流程注释，游戏规则仅在 samples；Unity DLL 从源码构建。
- 当前以完成用户授权的接入为目标，连续实施，不增加重复设计审批。

## Review Focus

- 输入队列暂满：保留原字节重试，不能重复采样或消耗重置操作。
- 鼠标无变化、缺失输入、失焦：保留朝向且停止移动；后台网络不能停止。
- 两人未到齐、第三人进入、断线：不提前推进，不破坏已有房间，有界退出。
- 大步位移和斜向碰撞：不穿薄墙，余数恢复后仍可复现。
- 本机两个进程：各有独立模拟/Socket，不能只在一个世界里移动两个 Transform。

## Task 1：恢复本地输入冻结约束

Files: framework/src/FrameSync.Sessions/ClientPrediction.cs；现有 PredictionTests。

- [x] 运行原有测试并保存失败基线。
- [x] 针对缺少本地输入就停止的规则修复，清理本地补猜标脏路径，保留中文注释。
- [x] 运行核心测试，确认全部通过。

## Task 2：独立游戏模拟

Files: samples/TacticalGame.Simulation/{TacticalInput,TacticalState,TacticalMap,TacticalGame}.cs；tools/TacticalGame.Tests。

Interfaces: TacticalGame.CreateModule()、TacticalInput.Encode/Decode、TacticalMap.Encode/Decode；状态只包含整数和稳定玩家编号。

- [x] 先写并运行速度、朝向、中性、碰撞、编码、恢复重放测试，观察失败。
- [x] 实现毫米整数模拟、输入验证和状态/地图编码。
- [x] 运行这些测试；不得引用 Unity。

## Task 3：双人房间与输入驱动

Files: samples/TacticalGame.Network/{RoomProtocol,TacticalHost,TacticalClient}.cs；RoomTransport 在 RoomProtocol 内，输入冻结驱动在 TacticalClient 内；tools/TacticalGame.Tests 网络用例。

Interfaces: TacticalHost(map,port).Poll(now)、TacticalClient(map,host,port,slot).Poll(now,sample)、Client.Session、Host.Session；失败和 Dispose 均释放 Socket。

- [x] 写真实 UDP 双客户端、等待开局、第三人拒绝、关闭重建和不重复采样测试并观察失败；冻结/背压使用核心既有用例，示例背压分支经过代码审查。
- [x] 实现控制通道与会话通道分流、完整握手、状态和超时、先冻结再预测的输入驱动。
- [x] 实际运行网络用例并比较各端同帧检查点哈希。

## Task 4：Unity 接入与可视化

Files: adapters/Unity；scripts/sync-unity-libraries.sh；samples/TacticalGame.Unity/Assets/TacticalGame/Network；Editor 联机场景导出；Tests 场景测试。

- [x] 导入独立程序集；先补场景与地图一致性检查。
- [x] 创建联机场景，加入房间菜单、输入采样、双方角色显示、相机和诊断状态。
- [x] 用单独验证组件驱动开发构建的自动输入；正式运行路径仍使用相同客户端/服务器。
- [x] 编译，运行原有和新增 Unity 测试，构建并启动两个独立进程，核对哈希和角色画面。

## Task 5：完整验收与交付

- [x] 运行 ./scripts/verify.sh 及新游戏测试、双进程 Unity 验收；保存 artifacts/tactical-network。
- [x] 独立审查依赖边界、冻结输入、断线和碰撞处理；必要修复先有回归测试。
- [x] 更新 README、验证记录和实施顺序，给出两个客户端启动方式。

## 执行记录

- 初始检查：已有 ClientPrediction 的未提交内容恢复了旧本地补猜逻辑；本次只修复与接入直接相关的这部分约束，其他既有变更保留。
- 沿用用户指定正式目录和已打开 Unity 工程；不自动提交 Git。

- 2026-10-16：核心 101 项及完整 verify 通过；试玩纯 C# 12 项通过；Unity 17 项通过；macOS 双进程 180 确认帧三端摘要一致，两端均观察到远端移动、转向和模型显示。

- 独立审查提出的自然断线端口未释放、重建场景可能丢失未保存修改两项已修复并复审通过。端口回归先复现 Address already in use，再验证修复通过。
- 实际窗口已验证点击取得角色控制、W 移动、R 重置、Esc 停止移动与后台继续同步。IMGUI 输入配置改为 Both 后重启并重建；本机窗口工具的菜单点击坐标有偏差，因此菜单按钮的实鼠标操作仍保留为人工验证项，不记为通过。
- 实现文档、Unity README、验证记录及后续顺序已更新，详见 ../../yard-network-implementation.md。
