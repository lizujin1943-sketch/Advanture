---
name: code-structure-map
description: Use when the user asks to draw, visualize, document, explain, review, or refactor code structure — architecture diagrams, module or project dependency graphs, namespace/type maps, class diagrams, call chains, sequence diagrams, layering checks, or "how is this codebase organized". 适用于画代码结构图、架构图、模块依赖图、类图、时序图、分层评审、依赖方向检查、新人代码导览，或判断某次改动会波及哪些模块。Also use when a diagram already exists but may be stale, when someone asks "which projects does this touch", or when a design doc needs a structure figure. Do not use for level/map design or UI layout.
---

# 代码结构图：从可验证的事实到一张能据以决策的图

一张结构图的价值不在于好看，而在于它能支撑某个决定：这次改动会不会跨层、新人该先读哪几个文件、这段依赖是不是循环、重构该先切哪里。没有决策目标的图会退化成目录截图。

因此本 skill 的核心约束是：**图上每条边都要有出处**。代码结构图是取证结果，不是想象。宁可少画一条边并标注未验证，也不要画一条读代码时会被推翻的边。

## 先确定要回答的问题

不同问题对应不同视图，选错视图是最常见的失败方式。先问清楚用途，再动笔：

| 要回答的问题 | 视图 | 典型触发语 |
|---|---|---|
| 系统由哪些模块组成，谁依赖谁 | 模块／项目依赖图（flowchart） | "整体架构"、"模块划分" |
| 分层是否被破坏，有没有反向依赖 | 依赖图 + 分层标注 + 违规边高亮 | "分层评审"、"循环依赖" |
| 某功能由哪些类协作完成 | 类图（classDiagram）或协作图 | "这段怎么实现的" |
| 一次请求／一帧的时序 | 时序图（sequenceDiagram） | "调用链"、"消息流" |
| 状态机、协议阶段 | 状态图（stateDiagram-v2） | "状态流转" |
| 目录与代码量分布 | 表格或 treemap 式清单 | "代码都堆在哪" |
| 数据从输入到落盘的路径 | flowchart，带数据形状标注 | "数据流" |

一个请求只交付一到两张图。把七种视图全画一遍通常说明还没想清楚要回答什么。

## 证据规则

画之前，先把"确定"和"推测"分开：

- **确定**：项目引用、`asmdef` 引用、命名空间、类型声明与继承、实际文件路径、行号。这些可以从文件直接读出来。
- **推测**：运行时调用顺序、动态分发、事件订阅、反射与依赖注入的连接、跨进程消息的真实路由。这些需要读实现或运行才能确认。
- **常见误判**：把 `using` 当成依赖边（它只是名字可见性）；把目录结构当成架构（目录经常是历史遗留）；把接口实现当成调用（实现关系不等于调用关系）；把 Mermaid 里画出来的箭头当成已生效的约束。

推测性的内容可以画，但必须让读者看得出来。用虚线表示推测边，或在图下的说明里写清"以下三条边来自对 `Foo.Handle` 的阅读，未运行验证"。

## 执行顺序

1. **锁定范围**。确认要画的是整个仓库、某个解决方案、某个程序集，还是某次改动涉及的子树。范围过大时先画边界，再对边界内单独展开。不要默认"整个仓库"。
2. **机械取证**。先跑脚本拿到项目图、类型清单和代码量分布（见下节），不要凭记忆或文件名猜结构。脚本输出是索引，不是结论。
3. **读关键文件**。对图中每个关键节点，至少打开入口文件确认职责；对每条推测边，找到对应代码位置。读到的与脚本冲突时，以源码为准，并记录冲突。
4. **选视图并控制规模**。单图节点控制在 15～25 个以内；超过就分层：先画模块级，再对感兴趣的模块单独出一张细化图。节点太多时读者只会得到一张无法验证的图。
5. **画图**。默认输出 Mermaid，直接放进 Markdown，让图和文档一起演进。需要交付图片时再渲染（见 references/validation.md）。
6. **标注边界**。写明图的来源范围、生成方式、验证程度，以及"本图不包含什么"。一张没有边界的图会被后来者误当成全景。
7. **自检**。按 references/validation.md 的清单过一遍：语法能否渲染、边是否都有出处、是否漏了关键反向依赖、规模是否失控。

## 本仓库的取证脚本

`scripts/scan_structure.py` 只用 Python 标准库，负责机械部分；判断仍由你完成。

```bash
S=.agents/skills/code-structure-map/scripts/scan_structure.py

# 项目/程序集依赖图（.csproj 的 ProjectReference + Unity 的 .asmdef）
python3 "$S" projects --root . --focus framework --collapse-unity

# 某子树的类型与继承关系（classDiagram）
python3 "$S" types --path framework/src/FrameSync.Runtime --max-nodes 30

# 命名空间与类型数量分布
python3 "$S" namespaces --path samples/HookIsland.Simulation

# 各目录的代码量，用于判断"这块有多大"
python3 "$S" tree --path framework/src --depth 1
```

交付前用第二个脚本核对图是否还成立：

```bash
V=.agents/skills/code-structure-map/scripts/verify_diagram.py
python3 "$V" docs/architecture-overview.md --root .
```

它会检查图里提到的文件是否还存在、类型名是否真实、以及代码里有的项目依赖是否漏画。它只能发现机械性的过期，语义正确性仍要人判断。

参数说明：`--format text|json` 便于二次处理，`--out` 直接写文件，`--no-external` 去掉外部基类，`--title` 加图注。`--focus` 默认严格聚焦，跨边界的依赖会聚合成"范围外依赖／依赖本范围"两个节点；需要多看几跳时用 `--context N`。`--collapse-unity` 把 Unity 自动生成的 `.csproj` 折叠掉，只保留作者维护的 `.asmdef`，否则每个程序集会出现两次。脚本会跳过 `.build/`、`artifacts/`、`bin/`、`obj/`、`Library/` 等生成目录，避免把副本当成第二套架构。

脚本的已知边界，交付时要如实说明：

- 只认声明，不做语义分析；泛型约束、条件编译、`partial` 合并是启发式处理。
- Unity 的 `.asmdef` 引用按名称解析，指向包（如 `Unity.InputSystem`）的引用会标为 `unresolved`，这是正常的，不是 bug。
- 不解析 `.csproj` 里的 `PackageReference`，NuGet 外部依赖需要自己补。
- 项目多、耦合密时整仓图几乎必然变成连线噪声；这是结构本身的信号，应该按 `--focus` 拆图，而不是调大画布。

## 输出位置与格式

- 结构图优先内联进已有文档（例如 `docs/` 下的实现说明），让图和它描述的代码在同一处被维护。
- 独立的架构总览另建文件，文件名说明范围，例如 `docs/architecture-overview.md`；不要写 `docs/diagram.md` 这种无法判断时效的名字。
- 不要放进 `.build/`、`artifacts/`、`bin/`、`obj/`，这些是本地生成内容，不提交。
- 每张图上方写一行来源与范围，图下写"未包含什么"和验证程度。读者需要知道该不该信任它。

## 图要跟随代码演进

结构图最容易的失败不是画错，而是过期。因此：

- 优先画变化慢的东西（模块边界、依赖方向、职责划分），少画变化快的细节（具体方法签名、字段）。
- 图里引用真实路径和类型名，方便读者直接跳转核对。
- 如果这张图依赖脚本输出，把生成命令写进图注；下次刷新时重跑而不是手工修补。
- 发现图与代码不一致时，先确认哪边是对的，再同时更新两者；不要只改图。

## 反模式

- **目录即架构**：`Assets/Scripts/UI` 这种目录名很少能反映真实依赖，边必须来自引用和代码。
- **全量类图**：把 147 个类型塞进一张图，读者只会放弃。给出分层入口，让人按需下钻。
- **无来源箭头**：没有文件出处的箭头，会在评审时被当成事实引用。
- **混合粒度**：同一张图里既有程序集又有私有方法，读者无法判断抽象层级。
- **把生成图当真相**：脚本能证明"存在引用"，不能证明"运行时确实调用"。
- **循环依赖静默通过**：发现分层反向依赖时，明确标出来并给出处置建议，不要画成和其他边一样。

详细的视图配方与可复制模板见 [视图配方](references/view-recipes.md)，Mermaid 语法坑与渲染校验见 [语法与校验](references/validation.md)。

本 skill 的两个脚本：

| 脚本 | 用途 |
|---|---|
| `scripts/scan_structure.py` | 从代码生成依赖图、类图、命名空间分布、代码量清单 |
| `scripts/verify_diagram.py` | 检查已有 Mermaid 图是否与代码一致，发现过期节点和漏画的依赖 |
