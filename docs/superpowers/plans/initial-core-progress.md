# SDD ledger — plan: docs/superpowers/plans/2026-10-15-initial-core-completion.md

- 2026-10-15：用户确认仅补齐初版剩余内容；初版范围已审阅，恢复 C/D 并补 B 的成员能力。
- 基线：分支 f_1，HEAD a9d9290，起始工作区干净。完整验收已启动，日志 artifacts/initial-core/baseline.log。
- Ruling: 使用用户指定的正式目录，不另建 worktree；目录约束优先于技能默认隔离流程。
- Ruling: 已有规范与用户“继续实现”的授权覆盖本次实现，不重复要求阶段批准；保留可审阅的计划与进度。
- Pre-flight: 成员状态影响检查点、重放、确认帧和预测比较；由主实现者统一修改并先完成。
- Pre-flight: 文件存储只消费 CheckpointCodec 的公开接口和不变 Checkpoint，不修改成员/预测代码，可独立并行实现。
- Pre-flight: 载入重开必须生成新 S[0] 后再分发；保持 ClientSession 的开局快照 Tick=0 条件。
- Ruling: 保留服务器当前最终确认规则，客户端回滚不把迟到操作补执行到别的帧。

## 实施与验证记录

- 基线完整运行 125 项全部通过，日志 `artifacts/initial-core/baseline.log`。
- 成员事件与动态成员状态进入完整检查点、历史窗口、重放和最终输入；会话/检查点/录制升级为 v2，旧版明确拒绝。
- 预测为可选模式：有界完整快照、从差异前一帧恢复、跨 Poll 重算、稳定展示状态、事件失效/确认；确认出口与存档仍只使用确认状态。
- 回归修复：批量补交本地输入时不能只记录最早一帧；若这帧已有最终结果，仍须重算后续受影响帧。改为有界 dirty tick 集合，并补齐回滚过程中再次纠正的测试。
- 独立存档子任务完成 23 项测试；FileSystem 使用同目录候选文件、刷新、回读校验、原子替换和独占路径锁。无目录 fsync 或格式迁移承诺。
- 读档重开保留累计时间和稳定身份，生成新 SessionId、增加控制代数并清空旧历史；保存失败保持 Saving 冻结状态，可重试。
- Counter 旧单操作 API 保留，新增最多 16 条有序操作的输入编码；11 项新增行为测试通过。
- 真实 UDP 客户端启用预测，手动输入用固定提前帧调度；房间正常结束先保存；运行中加入明确拒绝；成员断开由下一确认帧停用。
- 2026-10-15 完整 `scripts/verify.sh` 返回 0：94 核心、23 存储、24 示例/CLI、6 同步 CLI、30 可靠 UDP、8 Socket/真实进程，共 185 项行为测试通过；另有 3 项故障网络自检。所有构建 0 警告/0 错误。
- 两模型各 100000 帧、6 次状态恢复与新进程重放通过；两模型各四客户端 10000 帧通过。两模型 × 1/2/4 客户端共 6 组预测故障网络场景通过，导出的 6 份录制分别在新进程重放通过。
- 预测网络报告 `artifacts/initial-core/prediction/report.json`：60/90/240 帧；四人两个模型回滚次数均为 32/33/36/30，各客户端最终确认、预测、展示位置一致。网络实测丢包、重复和乱序均发生，队列未触及上限。
- 真实进程 `save-reload` 两轮：累计帧 20 → 40、世界总值 15 → 30、会话身份改变；`disconnect`：B 第 10 帧离开，服务器与 A 继续至 40 帧且完整摘要一致，存档内 B 停用。
- 当前完整证据：`artifacts/initial-core/full-verification.log`、`verification.json`；各子工具输出仍保留在原 artifacts/stage-a、stage-b、stage-e 和 initial-core 目录。
- 适用边界：仅 macOS arm64 / .NET SDK 8.0.403 实测；Windows 跨系统验收、Unity/IL2CPP、公网与实际游戏长稳未运行。中途加入、完整重连、Steam/Unity 接入、自适应调度和快照优化后置。
- 独立审查完成：initial_core_final_review 只读审查全部未提交修改与新增文件，Critical/Important/Minor 均为 0，可以交付本次初版代码补齐；未修改 HEAD、索引或分支。

- Review ruling：公开 Activate 表示启用同一控制者并保留版本；更换控制者必须 Rebind+1。当前 ServerSession 仅在开局发送 Activate，断开的旧连接不可恢复。补明注释与文档，不扩大运行中重连范围。

- Review ruling：运行中加入、完整重连和自动整状态重同步继续后置；不将这次补齐解释为相关功能已交付。
- Review ruling：Windows 与跨系统确定性待真实环境验证；macOS 结果不替代该项验收。
- Review ruling：文件保存保证限定为正常原子提交和提交前失败保留旧档，不包含目录 fsync、断电持久性、路径别名或不遵守锁的外部写入者。
- Review ruling：明确的数量/编码字节预算不代表 CLR 实际堆峰值或商业游戏性能；实际游戏长稳和发行验证留到接入负载后执行。
- 最终检查：所有修改文档的相对文件链接有效，新增 C# 文件无尾随空白，git diff --check 通过。审查后的修改仅澄清注释和文档，不改变已通过完整验收的执行行为。未自动提交或推送。
