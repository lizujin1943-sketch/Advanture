# 项目工作目录

本项目的正式仓库与后续产出目录是：

`/Users/lizujin/Documents/project/git_project/Advanture`

- 所有代码、文档、脚本、测试结果和构建产物都在此目录及其子目录中维护。
- 旧目录 `/Users/lizujin/Documents/project/game/Advanture` 已迁移，仅保留定位指引；不要在旧目录继续开发。
- 如果会话仍从旧目录启动，后续工具调用显式使用这里的绝对路径作为工作目录。
- 帧同步核心保持纯 C#，独立于 Unity、Steam 和具体玩法；游戏示例放在 `samples/`。
- 完整验证入口为 `./scripts/verify.sh`；当前实现和验证范围见 `docs/stage-a-implementation.md` 、`docs/stage-b-implementation.md` 、`docs/stage-e-implementation.md` 与最新的 `docs/stage-c-d-implementation.md`；文件保存约定见 `docs/stage-d-storage.md`。
- `.build/`、`bin/`、`obj/` 和 `artifacts/` 是本地生成内容，不提交到 Git。

## 游戏实现约束

- 本项目按正式游戏开发，阶段性功能也须满足可维护性、可扩展性和多人一致性要求。
- 遵循单一职责、里氏替换、开闭等设计原则；优先通过组合与配置扩展，不为尚无需求的功能堆叠继承或抽象。
- 将确定性玩法规则、状态与结算、网络命令交付、Unity 输入及表现分开。Unity 显示和预览不得直接修改权威状态。
- 资源扣除与生成结果必须作为同一事务处理；联机重试须可去重，失败请求不能隐式重放，恢复存档后保持既有结算结果。
- 自动游玩、注入资源等验证工具不得作为正式场景的常驻玩法组件或进入发布构建；开发构建中的自动验证必须显式启用。
- 阶段性未实现的系统要记录范围及接入边界，不用测试特判代替正式玩法，不将局部验收描述为完整游戏已完成。

## 工作方式

改动前先读要调用的函数实现；分清确认态与预测态；验证成本要和改动半径匹配，跳过的验证要说明。细节和实际踩过的坑见 `docs/agent-workflow.md`。

Git 提交说明使用中文；按用户要求自动提交时也遵循此约定。

## 三维模型还原流程

- 用户确认的默认流程：先根据原始参考补齐正面、侧面、背面、俯视等多视角设定，标明不可见部分的推断，再进行三维建模；不能仅沿正面图片堆叠形体。
- 建模前明确肢体数量、根部连接、前后深度和空间分布。触腕等放射状结构必须从俯视、背面检查分布和连接；生成设定图只作设计参考，不能代替准确的三维结构检查。
- 有机衔接是灰模验收项：从最暴露接头的角度看近景，皮肤轮廓和高光应顺着根部自然转入肢体。出现插管感或无设计依据的环形硬折时，先修形再贴图；甲片分界、关节褶皱等有意的硬边按设定保留。可动衔接还要在最终游戏网格的极端姿态复查。
- 按大形与连接、多角度灰模、细节与贴图、骨骼与动画、引擎接入的顺序推进。验收至少包含正面、侧面、背面和俯视实模，不以单张正面渲染代替立体还原。
- 按已授权范围持续制作；用户已要求设计后实现时，不重复请求泛化的制作许可。未展示的部位和未验证的动画、引擎行为必须明确说明。
- 按图制作或收到“不像、糊、接头生硬”的反馈时，用[模型还原与造型优化技能](.agents/skills/blender-reference-fidelity/SKILL.md)做同机位诊断；导出与游戏接入见[游戏资产制作流程](.agents/skills/blender-mcp/references/game-assets.md)。

## Unity MCP

- 本仓库 Unity 工程是 `samples/TacticalGame.Unity`，不是仓库根目录。
- 使用 Unity MCP 前先读取 `mcpforunity://instances`，选择该工程对应的实例，再读取 `mcpforunity://project/info` 核实工程绝对路径；不要仅凭默认实例操作。
- 只操作 `/Users/lizujin/Documents/project/git_project/Advanture/samples/TacticalGame.Unity`。工程未连接时先建立连接，不操作其他已打开工程。
- 连接与安装说明见 `samples/TacticalGame.Unity/README.md` 的 Unity MCP 一节。
