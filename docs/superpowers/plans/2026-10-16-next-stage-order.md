# 后续实施安排

更新：2026-10-16。按用户决定暂缓 Windows/macOS 跨系统验证，先完成 Unity 双客户端接入。

## 已完成

1. 本地输入冻结：每帧先明确记录自己的操作或中性输入，再预测其他玩家；缺少本地输入时暂停预测。当前完整框架验收通过。
2. 七号转运站双人试玩：独立纯 C# 游戏模拟、房间宿主、Unity 输入和显示已接通。两个真实独立客户端可互见移动与转向，同一确认帧与服务器状态一致。实现边界和运行方式见 [接入说明](../../yard-network-implementation.md)，验收见 [Unity 记录](../../../samples/TacticalGame.Unity/VERIFICATION.md)。

## 后续建议顺序

1. 在当前可运行试玩上确认实际鼠标手感、局域网体验和需要的表现平滑；按需求接入框架已有存读档能力，使玩家能通过菜单保存并重开。
2. 实现中途加入：发送一份确认状态，再补齐其后的输入帧；加入者追上房间进度后进入正常模拟。
3. 实现掉线恢复：恢复原玩家身份、分配新的控制代数、补状态与历史输入。
4. 接 Steam 房间、好友邀请和平台传输；保持框架不依赖 Steam。
5. 扩大性能、故障网络和长时间运行验证，再按发布需求完善工具。

Windows/macOS 跨系统与跨架构验证仍是待办，暂缓不代表通过；在商业游戏发布前应补齐。当前验收使用 macOS 的编辑器、.NET 控制台和 Mono 独立程序。

## 工程边界

- 通用代码放在 `framework/`；它可以不加载任何游戏示例或 Unity 单独构建。
- 通用 Unity 程序集入口在 `adapters/Unity`。
- 当前示例使用 `samples/TacticalGame.Simulation`、`samples/TacticalGame.Network` 与 `samples/TacticalGame.Unity`，不是早期计划中的 GridUnity。
- Unity 工程为 `/Users/lizujin/Documents/project/git_project/Advanture/samples/TacticalGame.Unity`，版本 2022.3.62f1，MCP 已连接并核实该工程。
- 当前房间固定两人；框架原有 1～4 人能力不等于试玩已实现四人界面。

依据：[工程拆分与游戏接入](../specs/2026-10-10-framework-integration-design.md)、[总体实施顺序](../specs/2026-10-10-deterministic-frame-sync-design.md)、[本次双客户端设计](../specs/2026-10-16-yard-network-design.md)。
