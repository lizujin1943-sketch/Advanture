# Game Code Folder Organization Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement this mechanical organization in the current official checkout.

**Goal:** 按数据、玩法逻辑、网络存档、输入和显示职责归类钓鱼游戏文件，保留现有功能与资源引用。

**Architecture:** 保留三个现有项目/程序集边界；在各项目内部增加职责目录，再按功能分组。Unity 脚本由 AssetDatabase 连同原 .meta 移动，纯 C# 文件按清单移动；类名、命名空间、程序集引用、资源加载路径、CSV 内嵌资源名称和所有方法内容不变。

**Tech Stack:** C#、SDK-style .NET projects、Unity 2022.3 AssetDatabase、Python 文件清单校验。

**Spec:** 用户本轮目录归类要求；AGENTS.md；docs/agent-workflow.md；docs/confirmed-presentation-implementation.md。

## Global Constraints

- 只移动 samples/HookIsland.Simulation、samples/HookIsland.Network 及 Assets/HookIsland 的 Runtime / Editor 脚本，框架和美术资源不重新分组。
- 保留既有未提交工作，不重置或批量提交；当前目录快照作为本次基线。
- 数据 CSV 留在原 Data 路径；Resources、场景、Prefab 和 .asmdef 位置不变。
- 不以目录命名冒充新的程序集隔离。含有多种类型的既有文件按主要责任归类，明确记录混合类型边界。
- 这次是字节不变的搬迁，不新增玩法测试；验证文件完整性、GUID、显式 Compile 引用、编译和已有回归。按仓库成本规则不重复执行长时间全量 UDP 流程。

## Review Focus

- .meta 或脚本 GUID 丢失导致场景、配置引用断开：前后哈希与 Unity 资源加载核对。
- 显式 Compile 路径遗漏导致测试项目少编译或无法编译：更新引用并编译全部 HookIsland 测试项目。
- CSV 内嵌资源路径或程序集名称变化导致配置表无法加载：保持文件位置/项目设置，运行既有模拟测试。
- 脚本内容随移动误改，改变联机、存档或预测行为：每个移动文件与基线逐字节比较。
- 文档旧路径误导后续开发：更新完整路径引用，提供职责说明和可验证的目录索引。

### Task 1: 记录边界与搬迁清单

- [x] 读取项目引用、目录统计、关键类型职责与所有显式源码引用。
- [x] 生成 artifacts/code-organization/moves.json，记录 240 个文件的旧/新路径、内容哈希和 Unity GUID；保留 baseline。
- [x] 按清单移动纯 C# 文件，使用 Unity AssetDatabase 移动脚本。

### Task 2: 接通引用与目录说明

- [x] 更新测试工程的 Compile 路径及文档中的现行源码路径。
- [x] 编写 docs/game-code-organization.md，说明各目录职责和新增文件归属规则，并在游戏 README 增加入口。
- [x] 核对 240 个源码、125 个 .meta 与基线一致；原路径无遗留重复文件。

### Task 3: 验证与独立审查

- [x] 编译 HookIsland.Network 和所有 HookIsland 测试项目，运行既有模拟/输入/存档测试。
- [x] 同步游戏程序集，Unity 编译及已有 EditMode 测试；记录已知独立动画失败，不改变测试断言。
- [x] 核对 Unity GUID 解析、场景组件、ScriptableObject 加载和 Resources 路径。
- [x] 独立审查搬迁完整性及更新记录，报告验证范围。
