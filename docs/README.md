# 自用帧同步框架：文档入口

这套框架供你自己的不同游戏重复使用，使用它的游戏可以商业发行。框架按内部公共代码库维护，不按出售或对外提供开发工具包的产品设计。

当前 A～E 的初版代码链路已接通：独立模拟、输入同步、预测回滚、存档重开、自研 UDP，并补齐固定名单的成员事件和停用。最新实际接口和阅读顺序见 [C/D 实现说明](stage-c-d-implementation.md)，文件存储细节见 [D 存储说明](stage-d-storage.md)。运行入口见 [项目 README](../README.md)，故障网络验证见 [PredictionHarness](../tools/PredictionHarness/README.md)。

[A 实现说明](stage-a-implementation.md)、[B 实现说明](stage-b-implementation.md)、[E 实现说明](stage-e-implementation.md) 保留各阶段历史与分层原理；以 C/D 说明中的当前接口和边界为准。Windows 跨系统验收仍待实测，不能把本次 macOS 通过视为整个发布验证已完成。方案文档建议按下表阅读：

| 顺序 | 文档 | 主要回答 |
| --- | --- | --- |
| 1 | [初版核心模块与范围：先读这一篇](superpowers/specs/2026-10-10-initial-core-scope-design.md) | 初版实现哪些模块？每一步怎样跑起来？哪些能力现在必须做，哪些后补？ |
| 2 | [总体方案](superpowers/specs/2026-10-10-deterministic-frame-sync-design.md) | 房主与客户端各做什么？输入如何同步？回滚、存档和重连怎样工作？ |
| 3 | [独立帧同步框架技术方案](superpowers/specs/2026-10-10-frame-sync-core-design.md) | 核心数据怎样存？服务器如何确认？客户端如何预测、回滚和恢复？ |
| 4 | [工程拆分与游戏接入](superpowers/specs/2026-10-10-framework-integration-design.md) | 哪些代码属于框架？游戏要实现哪些接口？第二个游戏怎样接入？ |
| 5 | [可靠 UDP：先讲原理，再看协议](superpowers/specs/2026-10-10-reliable-udp-design.md) | 丢包如何补发？怎样拆分大消息、限制发送速度和控制内存？ |

正文先解释概念与流程，具体代码名称和算法约束放在后面。运行配置有时写作 Profile，含义只是“一组一起使用的参数”。

初版范围由第一篇确定；帧同步内部设计与实现约定集中维护在第三篇。可靠 UDP 独立维护自己的传输协议，具体游戏通过接入接口提供玩法。

后续排期更新（2026-10-16）：按用户要求，先暂缓第 2 项 Windows/macOS 跨系统验证，双客户端 Unity 接入现已完成，最新范围见 [联机实现说明](yard-network-implementation.md)。暂缓不代表通过；具体顺序见 [后续实施安排](superpowers/plans/2026-10-16-next-stage-order.md)。

初版完整目标（A～E）：纯 C# 无画面运行、Windows/macOS、单机与 1～4 人、预测回滚、基础存读档、自研 UDP；参与者在开局前加入。随后再接 Unity、途中加入和掉线恢复，之后接 Steam。核心不规定玩法、坐标、实体模型或发行平台。

游戏美术示例：[七号转运站：简单低多边形地图](maps/yard-07-design.md)，模型与预览见 [TacticalMap](../samples/TacticalMap/README.md)。这是独立的游戏资源，不属于帧同步核心。

Unity 双人试玩：[七号转运站](../samples/TacticalGame.Unity/README.md)，现已接入帧同步，可互见移动与转向。先读 [接入实现说明与源码阅读顺序](yard-network-implementation.md)，再看 [验证记录](../samples/TacticalGame.Unity/VERIFICATION.md)。原本地场景保留。

联机排查：[自动诊断日志与不同步快照](session-diagnostics.md)，介绍日志目录、结束状态、回滚与不同步的区别，以及问题帧重放。
