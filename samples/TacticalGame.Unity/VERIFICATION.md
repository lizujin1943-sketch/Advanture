# Unity 双人联机试玩验证记录

日期：2026-10-16。环境：macOS / Apple Silicon（Apple M4 Pro）、.NET SDK 8.0.403、Unity 2022.3.62f1、Mono 独立程序、内置渲染管线、Input System 1.14.0。

本记录对应 `Yard07Network.unity`。此前本地试玩的 13 项测试及 ClientPrediction 旧逻辑失败记录保留在 [历史记录](VERIFICATION-SINGLEPLAYER.md)，不代表当前结果。

## 自动验收结果

| 验收 | 结果 |
| --- | --- |
| `./scripts/verify.sh` | 退出码 0，包含全部核心、工具、故障网络、UDP 和庭院示例验收 |
| 帧同步核心与诊断 | 109 / 109 |
| 存档与文件存储 | 23 / 23 |
| 示例与命令行 | 24 / 24 |
| 同步工具 | 6 / 6 |
| 可靠 UDP | 30 / 30 |
| UDP 集成与多进程 | 16 / 16 |
| 庭院纯 C# 规则与真实 UDP | 12 / 12 |
| Unity EditMode | 19 / 19，0 失败、0 跳过 |
| macOS Development Build | 成功，0 错误、0 警告，210.73 MB |
| 两个独立 Unity 进程 | 通过，服务器、A、B 在第 180 个确认帧的完整状态摘要一致 |

最终 Unity 测试作业：`237bee82c6a34d80ad4c3c2b8c6801b5`；构建作业：`build-9951c77e74`。完整框架最终日志见下方链接。本记录已包含随后补充的自动诊断验证。

### 独立双进程验证

运行 `python3 scripts/run-yard-pair.py --smoke`，两份程序使用不同的移动与朝向输入，通过真实本机 UDP 通信。房主进程内部的服务器与自己的客户端也各有独立模拟和 Socket。

最新报告目录：`artifacts/tactical-network/smoke-20261016-163000-93c0f1c1`。三个确认检查点的 SHA-256 都是：

```text
9ea9fb768612a9c6e4a84d232d4d75c0496529220ae6dfcf3b646672fff4d5cd
```

两端报告中的以下检查全部为 true：

- `sawRemoteMovement`：确认状态中另一玩家的位置确实变化。
- `sawRemoteTurning`：确认状态中另一玩家的朝向确实变化。
- `sawRenderedMovement`：另一玩家的 Unity 模型位置确实变化。
- `remoteInView`：另一玩家位于自己的相机视野内。

最终 A 位于 (-2500, -6500) 毫米、朝向 90°；B 位于 (2500, -6500) 毫米、朝向 270°。双方截图均已人工检查，能看到庭院和不同颜色的远端角色。脚本导出检查点后结束本局，两个进程正常退出。

会话包含随机身份，本次摘要不应硬编码成每次运行都相同；正确断言是同一次运行、同一确认帧的三端状态一致。

### 规则与异常覆盖

纯 C# 测试覆盖移动/加速速度、斜向限速、朝向与中性输入、扫掠碰撞、重置、非法输入拒绝、完整状态恢复重放、地图与状态校验、双方移动转向、等待第二人 16 秒后开局、拒绝第三人、地图不一致拒绝、断线后停止远端以及端口重用。

Unity 新增 4 项检查，覆盖不同角度下的量化输入是否合法、前进是否沿采样朝向、16 个导出障碍是否与场景碰撞盒一致，以及联机场景不再运行原本地物理运动组件。原 13 项本地试玩测试继续通过。

输入冻结与回滚继续由核心原有测试覆盖。本次纯 C# 房间测试额外检查一个本地帧不会被重复采样；示例发送队列强制饱和的单独压力用例尚未加入。

## 实际窗口操作

普通启动脚本已验证：脚本退出后，两个独立客户端仍保持运行，最初日志分别写入 `manual/host.log` 和 `manual/join.log`；补充自动诊断后改为每次运行独立的 `manual-日期-时间-随机编号/`。屏幕显示 B 已进入房间并能看到蓝色 A。

在普通客户端通过窗口工具执行并观察：点击画面后提示隐藏、出现准星；按 W 可向前移动；R 恢复出生位置和视角；Esc 释放控制后按 W 不再移动；释放期间确认帧继续增长；F3 可以显示和隐藏诊断信息。最终已将诊断信息隐藏，保留普通联机界面。

菜单鼠标点击**尚未可靠验收**。检查发现工程最初只启用新版 Input System，而 IMGUI 不接收该后端的运行时输入；已按工程内 Input System 官方文档改为 Both、重启 Unity、重新构建。之后日志证明菜单收到输入事件，键盘可以编辑文本框。但本机自动窗口工具的点击位置仍有偏差，例如屏幕高度 640 时生成了 y=705 的点击，另一次生成了负坐标，不能把这些失败点击当作真实鼠标测试。创建、加入、离开按钮的实鼠标操作与鼠标转向手感仍需人工确认。

可在 Development Build 加 `-yard-input-trace` 输出 IMGUI 鼠标/键盘事件诊断，默认关闭；本次证据见 [输入事件日志](../../artifacts/tactical-network/input-trace.log)。

## 本次修复和边界

- `ClientPrediction` 恢复先冻结本地输入的实现，去掉缺少本地输入仍补猜推进的旧路径。该输入冻结逻辑与已有正确提交一致；随后新增的 `FrameSync.Diagnostics` 也没有引入 Unity 或玩法依赖。
- 新客户端使用自己的连接经过时间，避免在程序启动 16 秒后加入时，把应用运行时间误当作握手超时。
- 自然断线即释放服务端和客户端 Socket，保留诊断状态。回归测试在修复前确实报端口占用，修复后无需先 Dispose 就能重新绑定两个端口。
- 联机场景重建前先询问是否保存当前场景，取消后不修改地图或目标场景。
- 地图和玩法仅在 samples；框架可继续独立构建。

本次实测是同一台 Mac 上的回环 UDP，不等于已经完成跨电脑局域网、Windows、跨架构、IL2CPP、公网或长时间稳定性测试。当前试玩房间固定两人；未实现中途加入、断线恢复、Steam 和游戏存档菜单。

## 本地证据

这些是仓库内的生成产物，不提交 Git：

- [完整框架与试玩验收日志](../../artifacts/tactical-network/diagnostics-framework-verify.log)
- [庭院纯 C# 测试日志](../../artifacts/tactical-network/tactical-tests.log)
- [Unity 19 项最终测试报告](../../artifacts/tactical-network/diagnostics-tests-final.json)
- [macOS 最终构建报告](../../artifacts/tactical-network/diagnostics-build-final.json)
- [macOS 双人试玩程序](../../artifacts/tactical-network/Yard07Network.app)
- [双进程验收结论](../../artifacts/tactical-network/smoke-20261016-163000-93c0f1c1/result.json)
- [服务器检查点报告](../../artifacts/tactical-network/smoke-20261016-163000-93c0f1c1/host.json.server.json)
- [客户端 A 报告](../../artifacts/tactical-network/smoke-20261016-163000-93c0f1c1/host.json) / [客户端 B 报告](../../artifacts/tactical-network/smoke-20261016-163000-93c0f1c1/join.json)
- [A 看到 B 的画面](../../artifacts/tactical-network/smoke-20261016-163000-93c0f1c1/host.json.png) / [B 看到 A 的画面](../../artifacts/tactical-network/smoke-20261016-163000-93c0f1c1/join.json.png)
- [Unity 导入程序集摘要](../../artifacts/tactical-network/unity-libraries.sha256)

## 自动诊断增补

- 核心新增 8 项文件诊断测试：正常进度/回滚/退出、无预测与有预测两条状态分歧路径、初态前失败、核心执行失败、结束幂等与运行隔离、写入失败不打断模拟、服务器最终快照和未完成标记。
- 人为让一个客户端每帧多加 1，触发第 1 帧真实摘要不一致。导出的 `problem.fsrp` 用正常模块重放得到服务器摘要，用故障模块重放得到本机错误摘要，问题帧和前后快照均可解码。
- Unity 新增 2 项真实房间测试：服务器与两个客户端退出时保存最新状态；地图不一致导致初态前失败，之后离开仍保留首次原因。
- 两个独立 macOS 客户端通过普通诊断入口写出 3 份结束摘要和确认快照，均为 `SessionClosed`，第 180 帧摘要与原有验收报告一致，`desyncDetected=false`。客户端 A/B 回滚次数为 178/180；没有误报不同步。
- 每局目录独立；程序日志能定位诊断路径，默认无需按 F3。具体文件含义见 [自动诊断说明](../../docs/session-diagnostics.md)。

本次没有把人工注入的分歧测试当作实际玩家遇到的错误，也没有将新的自动运行代替此前 16:11 的手动试玩记录。旧试玩未记录的状态无法补回。
