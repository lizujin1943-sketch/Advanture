# SDD ledger — plan: docs/superpowers/plans/2026-10-10-stage-a-implementation.md

- Ruling: 依据用户“按照文档开始实现”在当前目录直接推进，不重复请求计划审批；范围限制为阶段 A。
- Ruling: Advanture 是父仓库中的未跟踪子目录，当前分支 s_7_qa；只在本目录工作，不提交或操作父仓库，实施记录放在本目录。
- Ruling: 核心 netstandard2.1，工具 net8.0；无外部 NuGet 包，测试采用可返回失败退出码的控制台断言程序。未来接 CI 时可迁移至标准测试适配器。
- Pre-flight: Task 2 consumes Task 1 simulation/immutable input contracts; Task 3 consumes the same public contracts and recording APIs. Signatures are owned by the defining task.
- Environment: macOS 26.0 arm64, .NET SDK 8.0.403 / runtime 8.0.10. Windows 未实测。
- Task 1: complete — runner tests 先 0/13，再 13/13；构建 0 warnings/errors。
- Task 2: complete — checkpoint 先 0/12，再 12/12；replay 先 0/7，再 7/7；全量核心测试 32/32，构建 0 warnings/errors。
- Ruling: 公共构建属性与 NuGet.Config 放在项目根，使外部 samples/tools 共享配置。初次默认构建阻塞；关闭共享编译器、并行构建与构建服务器后同一测试工程 2～4 秒完成。
- Task 3 samples: 初始 1/8（跨模型拒绝已由核心实现），补齐实现后 7/8；逐步复算 xorshift32 发现测试第二个常量计算错误，修正为 358294691，累计值为 8，算法未改。
- Task 3 CLI: 先 0/4，再 4/4；样例与 CLI 共 12/12。
- Task 2 补充: 新实例恢复必须携带初始化数据，回归测试先 0/1 再 1/1；编码纳入该数据，核心共 33/33。
- Verification: ./scripts/verify.sh 退出码 0；核心 33/33，示例/CLI 12/12；两模型各 100000 帧、各 6 个检查点恢复、逐帧事件比较、每 30 帧状态摘要比较、跨进程重放通过。
- Task 3: complete — 1～4 参与者覆盖、两个模型、控制台、脚本和中英文代码/中文文档均已完成。
- Final review: 一次全量独立只读审查；审查员复跑 34 项核心、12 项示例/CLI 测试。没有发现计算/恢复主体缺陷，报告两个边界问题。
- Final: Ruling: 将审查的两项 Minor 按实际影响提升为 Important 并在一次修复中处理 — 导出成功却破坏录制、解码成功却无法重新编码会误导使用者 — 若判断过重，成本是两项小范围校验和回归测试，不扩展功能范围。
- Final: fixed 内部检查点字节上限 — checkpoint.rejects_envelope_larger_than_its_embedded_limit RED→GREEN。
- Final: fixed 报告覆盖导出文件 — cli.rejects_report_collisions_before_any_output_write RED→GREEN，覆盖两模型的三种导出文件以及规范化路径。
- Final: Ruling: 后续网络/成员/预测/可靠文件保存不纳入 A — 与用户当前范围一致 — 这些能力仍需要按 B～E 实现。
- Final: Ruling: Windows、Unity、IL2CPP 本次不宣称通过 — 当前仅有 macOS 执行证据 — 后续需实际环境验证。
- Final: Ruling: 游戏确定性、完整状态和独立工厂是接入契约 — 框架无法自动证明任意游戏规则符合契约，已在实现说明中明确 — 每个游戏仍需要自己的确定性测试。
- Final: Ruling: 整段重放失败不自动退回调用前状态 — 保持已文档化的逐帧执行语义，单次 Restore 仍为事务式 — 调用者必须丢弃或显式恢复失败的重放实例。
- Final: 文档复选项已按实测完成；没有遗留 Minor。保留本地文件和实施记录，不触发父仓库合并、推送或清理。
- Final verification: ./scripts/verify.sh 退出码 0；核心 35/35、示例/CLI 13/13、两次构建 0 warnings/errors；两模型各 100000 帧、各 6 个恢复点、跨进程重放通过。另比对每模型 3334 个周期摘要，全部相同。
- Final verification: 核心项目引用图不包含 samples/Unity/外部包；Markdown 相对文件链接有效。最初阻塞的构建会话已结束。

## 仓库迁移（2026-10-10）

- 用户指定正式目录为 `/Users/lizujin/Documents/project/git_project/Advanture`，此后的代码、文档和验证产物均写到该目录。此前关于父仓库的记录描述的是迁移前环境。
- 已按 SHA-256 校验迁移代码、文档和既有验证文件；保留目标 Git 仓库，合并忽略规则以纳入 7 个 csproj 和 1 个 sln。旧目录仅保留迁移指引。
- 新目录重新执行完整验收：35 项核心测试、13 项样例/CLI 测试、两模型各 100000 帧及新进程重放均通过；每个模型的 3334 个周期摘要与迁移前一致。
- 迁移清单、原目标 README/.gitignore 备份和验证日志保存在本地 `artifacts/migration/`；未提交或推送。
