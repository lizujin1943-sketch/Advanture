# 来源、阅读覆盖与许可

CS 类竞技 FPS 的三份独立新增资料、研究范围和历史版本边界另见 [CS 来源](cs-sources.md)；不计入下列原书 94 页索引。

## 来源与改编

本 skill 依据 [The Level Design Book](https://book.leveldesignbook.com/)，由 Robert Yang 发起，与 Andrew Yoder 及其他贡献者共同编写。读取范围依据官方 [完整索引 llms.txt](https://book.leveldesignbook.com/llms.txt)，核验日期 **2026-10-16**。

这是中文归纳及面向 AI 的工作流改编，并非逐字翻译或原作者认可的官方版本。将章节重组为按任务检索的空间设计、战斗平衡、制作、试玩和案例参考；明确标注额外操作化建议，以及本仓库／程序生成扩展。

本 skill 的改编文字按 [CC BY-NC-SA 4.0](https://creativecommons.org/licenses/by-nc-sa/4.0/) 发布；分发或修改该文字须遵守署名、非商业及相同方式共享条款，详见原书 [许可页](https://book.leveldesignbook.com/appendix/license)。这一声明针对 skill 改编文字，不为用户另行创作的游戏或代码指定许可。

原书仅将未另署名的文本纳入上述许可。其引用、游戏截图、嵌入视频、第三方规格和代码可能有独立权利／许可；本 skill 不复制这些媒体或代码。Doom／Quake 数据仍应回查对应来源，MDL 页是 David Henry 的 GFDL 文档镜像，不能作为普通 CC 文本整体重发。

## 阅读范围的实际含义

- 当前官方索引共 **94 个页面**，包括正文、案例、WIP／stub、学习练习和资源目录；均已读取页面 Markdown 文本并核对主题。下表是逐页摘要，不把目录页和空章节算成完整理论。
- 书仍在编写。表中“正文”不表示整章定稿；未完成状态根据可见的 TODO、空标题、stub 标记记录，详细使用条件见各主题参考。
- **没有声称完整观看所有嵌入视频、检查所有图片，或读完所有外链书籍、论文与讲座。** 需要图像细节或准确技术数据时回到原页及原始来源。本次是当前索引文本覆盖，不是多媒体与外链穷尽阅读。
- 下载清单与页面哈希保存在本项目忽略提交的 `artifacts/level-design-book/manifest.json`；原始抓取仅作本地核验缓存，不作为 skill 的必需依赖。
- 以下链接指向可读网页；对应 `.md` 为本次文本来源。网站后续可能更新，重新引用当前内容时再次核对。

## 逐页索引

每行均为已读文本；“stub／WIP／目录”描述来源完成度，不是遗漏。

| # | 原文 | 内容与完成度 |
|---|---|---|
| 1 | [The Level Design Book](https://book.leveldesignbook.com/master) | 导读与全书结构；在线志愿项目持续编写，不是已经定稿的纸质专著。 |
| 2 | [What is level design](https://book.leveldesignbook.com/introduction) | 关卡设计定义、角色与游戏系统的关系；空间服务玩家行动，兼论工业分工。 |
| 3 | [How to make a level](https://book.leveldesignbook.com/process/overview) | 制作阶段与可变流程；按项目需要反复迭代，而非严格线性流水线。 |
| 4 | [Pre-production](https://book.leveldesignbook.com/process/preproduction) | 体验目标、设计支柱、原型、规划与协作；文档应帮助决策而非替代可玩成果。 |
| 5 | [Pacing](https://book.leveldesignbook.com/process/preproduction/pacing) | 节拍、因果、强度、教学和 set piece；强度曲线是启发式估计。 |
| 6 | [Research](https://book.leveldesignbook.com/process/preproduction/research) | 实地／虚拟考察、参考图、平面图、重建与研究问题；借用关系而非只收集漂亮图。 |
| 7 | [Worldbuilding](https://book.leveldesignbook.com/process/preproduction/worldbuilding) | 世界观、地图、历史、氛围与主题；细节服务体验，避免设定压倒可玩制作。 |
| 8 | [Scope](https://book.leveldesignbook.com/process/preproduction/scope) | 控制复杂度、优先级与时间；按实际制作成本和最小片段调整范围。 |
| 9 | [Combat](https://book.leveldesignbook.com/process/combat) | 核心动作、武器和经济系统先于战斗空间；主要假设实时 3D 动作／FPS。 |
| 10 | [Enemy design](https://book.leveldesignbook.com/process/combat/enemy) | 敌人角色、行为、外观信息与生态组合；部分内容仍为笔记或 TODO。 |
| 11 | [Encounter](https://book.leveldesignbook.com/process/combat/encounter) | 敌人组合、时序、信息、立足点与资源；多种玩家动机及案例，存在未展开内容。 |
| 12 | [Cover](https://book.leveldesignbook.com/process/combat/cover) | 遮挡、可见性、姿态、站位与移动选择；有提纲与 TODO，掩体数量／箱体建议须依机制解释。 |
| 13 | [Map balance](https://book.leveldesignbook.com/process/combat/balance) | PvP／PvE、公平、位置、时间、资源、对称与赛制；多处 WIP、空节和内部表述张力。 |
| 14 | [Layout](https://book.leveldesignbook.com/process/layout) | 草图、关系图、平面图与布局工具；表现意图而非证明实际玩家行为。 |
| 15 | [Flow](https://book.leveldesignbook.com/process/layout/flow) | 移动和决策的空间体验；区别心理学 flow state，允许有目的的摩擦。 |
| 16 | [Circulation](https://book.leveldesignbook.com/process/layout/flow/circulation) | 路径、连接和移动组织；把交通空间也视为体验的一部分。 |
| 17 | [Verticality](https://book.leveldesignbook.com/process/layout/flow/verticality) | 上下层关系、信息、风险与过渡；不是高度越多越好。 |
| 18 | [Critical path](https://book.leveldesignbook.com/process/layout/criticalpath) | 最低完成进程与理想路径模型；GoldenEye 提醒可先空间后任务。 |
| 19 | [Parti](https://book.leveldesignbook.com/process/layout/parti) | stub：简述核心布局概念与示例，不是完整 parti 方法。 |
| 20 | [Typology](https://book.leveldesignbook.com/process/layout/typology) | 空间类型与关系语言；大片未完成标题，不能当成熟模式库。 |
| 21 | [Gates](https://book.leveldesignbook.com/process/layout/typology/gates) | 硬／软门控与进程约束；短页，需结合机制和反馈应用。 |
| 22 | [Blockout](https://book.leveldesignbook.com/process/blockout) | 低成本可玩几何草稿；含 Firewatch、Goose Game 对普适白盒流程的反例。 |
| 23 | [Massing](https://book.leveldesignbook.com/process/blockout/massing) | 功能、体量、加减法、层级、围合与可读性；比例、厚度、转角等仍有提纲。 |
| 24 | [Landscape](https://book.leveldesignbook.com/process/blockout/massing/landscape) | 布局到地形白盒、指标再到美术；含 Alba 工具案例，多处 TODO，不能等同噪声生成教程。 |
| 25 | [Composition](https://book.leveldesignbook.com/process/blockout/massing/composition) | 空间构图与单帧构图之别；明确批评 leading lines，含未展开图例／saliency。 |
| 26 | [Prospect-refuge](https://book.leveldesignbook.com/process/blockout/massing/prospect-refuge) | 追溯并批评理论迁移；用视线、掩体和游戏状态解释安全，存在空标题。 |
| 27 | [Metrics](https://book.leveldesignbook.com/process/blockout/metrics) | 玩家／物理实测与建筑建议之别；相机和系统影响尺度，部分小节 TODO。 |
| 28 | [Modular kit design](https://book.leveldesignbook.com/process/blockout/metrics/modular) | 提纲型模块套件页：粒度、基本件和拼装压力测试；loopback 仅标题，步骤属于操作化。 |
| 29 | [Doom metrics](https://book.leveldesignbook.com/process/blockout/metrics/doom) | Doom 特定运动、武器和怪物指标；来源与数值按目标实现核验，不跨游戏泛化。 |
| 30 | [Quake metrics](https://book.leveldesignbook.com/process/blockout/metrics/quake) | Quake 1 特定碰撞、运动、难度和经济；vanilla／Copper／AD 等版本不能混写。 |
| 31 | [Wayfinding](https://book.leveldesignbook.com/process/blockout/wayfinding) | 定向、导航、地图认知、标识与反引导；百分比为非科学示意，部分章节 TODO。 |
| 32 | [Playtesting](https://book.leveldesignbook.com/process/blockout/playtesting) | 自测、开发者批评和大众试玩；准备、观察、复盘、分析，analytics 仍为空节。 |
| 33 | [Player persona](https://book.leveldesignbook.com/process/blockout/playtesting/persona) | stub／TODO：动机模型、Bartle 的 MUD 背景；不把所有玩家固定分型。 |
| 34 | [Scripting](https://book.leveldesignbook.com/process/scripting) | 关卡脚本、触发器、逻辑、演出与工具；部分任务／目标条目未展开。 |
| 35 | [(stub) Navigation](https://book.leveldesignbook.com/process/scripting/nav) | 明确 stub：列导航主题，不提供完整导航算法实现。 |
| 36 | [Doors](https://book.leveldesignbook.com/process/scripting/doors) | 门的类型、状态、复杂性与工作量；先判断需要哪种门及是否需要门。 |
| 37 | [Lighting](https://book.leveldesignbook.com/process/lighting) | 功能性灯光、类型、颜色、分轮与性能；数字和技术选型按目标引擎核验。 |
| 38 | [Three point lighting](https://book.leveldesignbook.com/process/lighting/three-point) | 主光、补光和轮廓光；受相机与物体关系限制，自由视角需多方向检查。 |
| 39 | [D6 lighting](https://book.leveldesignbook.com/process/lighting/d6-lighting) | 未完：焦点／框有说明，路径仅一句，后三类只有标题；不能补成原书六套完整策略。 |
| 40 | [Lighting for darkness](https://book.leveldesignbook.com/process/lighting/darkness) | 黑暗、对比与可读性；机械性需要黑暗时按玩法目标处理。 |
| 41 | [Environment Art](https://book.leveldesignbook.com/process/env-art) | 环境美术流程、资产、层级、聚类、迭代与可读性；按玩法允许差异。 |
| 42 | [Shape and color psychology](https://book.leveldesignbook.com/process/env-art/psychology) | 批评普适形状／颜色心理学；意义依赖上下文与文化，不是否定可读性。 |
| 43 | [Texturing](https://book.leveldesignbook.com/process/env-art/texturing) | 贴图、UV、材质尺度与制作方法；技术做法需与实际管线匹配。 |
| 44 | [Storytelling](https://book.leveldesignbook.com/process/env-art/storytelling) | 环境叙事、演出与可用的信息；Emily Short 等外部论述保留署名。 |
| 45 | [Optimization](https://book.leveldesignbook.com/process/env-art/optimization) | 先分析目标设备瓶颈，再选遮挡、LOD、合批等办法；历史预算不是现代通用标准。 |
| 46 | [Release](https://book.leveldesignbook.com/process/release) | 可玩包、说明、署名与展示；平台及压缩格式偏好带时代性。 |
| 47 | [Level design as culture](https://book.leveldesignbook.com/culture/overview) | 文化篇研究关卡设计的历史、社群与跨学科意义；导读页，说明该篇并非当前写作重点。 |
| 48 | [History of the level designer](https://book.leveldesignbook.com/culture/history-level-designer) | 从引擎与模组文化到作者声望、专业分工、封闭平台及地方性设计的职业史；实质长文，夹少量 TODO，历史分期与衰落判断属作者论述。 |
| 49 | [Zero player level design](https://book.leveldesignbook.com/culture/zero-player) | 借 TrackMania PF/PN、Auto-Mario 和 RNG 关卡讨论建筑编舞、玩家能动性与社群分类；实质文章，注明由 VGA Reader 文章修订。 |
| 50 | [(unfinished pages)](https://book.leveldesignbook.com/culture/unfinished-pages) | 列出五篇未完成文化主题；WIP 目录。 |
| 51 | [History of architecture](https://book.leveldesignbook.com/culture/unfinished-pages/history-of-architecture) | 用人群、地理、技术解释古代建筑，并提醒色彩、木构与日常建筑的史料偏差；WIP，后期时间段仅提纲，明确主要参考 Ching 等的建筑史。 |
| 52 | [Structural engineering primer](https://book.leveldesignbook.com/culture/unfinished-pages/structural-engineering-primer) | 拟摘要 Why Buildings Stand Up 并补游戏例子；纯 TODO stub。 |
| 53 | [History of environment art](https://book.leveldesignbook.com/culture/unfinished-pages/history-of-environment-art) | 从现代主义装饰争论到模型与电影布景传统；明确 unfinished stub。 |
| 54 | [History of furniture](https://book.leveldesignbook.com/culture/unfinished-pages/history-of-furniture) | 家具同时表达社会身份与游戏状态，按时代梳理坐具、储物与摆放；明确未完成笔记，参考 Edward Lucie-Smith，承认西方及富人偏差。 |
| 55 | [History of encounter design](https://book.leveldesignbook.com/culture/unfinished-pages/history-encounter-design) | 串联怪物摆放、巡逻领地、Halo 目标系统、动态导演与 Uncharted 协调器；WIP 长稿，大量内容为外部讲座的署名概述。 |
| 56 | [How to study a level](https://book.leveldesignbook.com/studies/overview) | 案例研究应分析优缺点、玩家行为并比较其他场地；导读，说明整个案例篇尚未充分展开。 |
| 57 | [Single player studies](https://book.leveldesignbook.com/studies/sp) | 列出 Assassins 与 Undead Burg；单人案例目录。 |
| 58 | [Undead Burg (Dark Souls 1)](https://book.leveldesignbook.com/studies/sp/undead-burg) | 以知识培养解释单向探索环、伏击递进、雾门预期与商人支路；明确 active development，Towers and Alleys 未写，不能称完整案例。 |
| 59 | [Assassins (Thief 1)](https://book.leveldesignbook.com/studies/sp/assassins) | 分析店铺突袭、随机跟踪路线、豪宅人物塑造与警报后的城市夺权；有完整论证的案例文章。 |
| 60 | [(STUB) The Cradle (Thief 3)](https://book.leveldesignbook.com/studies/sp/cradle) | 仅有 The Cradle 标题；明确 STUB。 |
| 61 | [(STUB) Sapienza (Hitman)](https://book.leveldesignbook.com/studies/sp/sapienza) | 仅有 Sapienza 标题；明确 STUB。 |
| 62 | [(STUB) Silent Cartographer (Halo 1)](https://book.leveldesignbook.com/studies/sp/silent-cartographer) | 仅有 Silent Cartographer 标题；明确 STUB。 |
| 63 | [Multiplayer studies](https://book.leveldesignbook.com/studies/mp) | 列出 Chill Out；多人案例目录。 |
| 64 | [Chill Out (Halo 1)](https://book.leveldesignbook.com/studies/mp/chill-out) | 结合护盾、窄门、单向传送、出生算法与沙发社交解释地图控制及摩擦；实质案例，明确改编自 Andrew Yoder 博文并获素材许可。 |
| 65 | [(STUB) de\_dust2 (Counter-Strike)](https://book.leveldesignbook.com/studies/mp/de_dust2) | 仅有 de_dust2 标题；明确 STUB。 |
| 66 | [Real world studies](https://book.leveldesignbook.com/studies/irl) | 列出 Disneyland；现实空间案例目录。 |
| 67 | [Disneyland (California, USA)](https://book.leveldesignbook.com/studies/irl/disneyland) | 先回顾地标、流线与叙事的传统业界解释，再转向公共生活私有化、怀旧和维护劳动；完整批评论证，关键论点分别引用 Rogers、Moore、Marling、Vanderbilt 等。 |
| 68 | [(STUB) Las Vegas (Nevada, USA)](https://book.leveldesignbook.com/studies/irl/stub-las-vegas-nevada-usa) | 拟从游戏设计视角读拉斯维加斯并总结 Learning From Las Vegas；明确 STUB。 |
| 69 | [Notes for educators](https://book.leveldesignbook.com/learning/notes) | 教学目标、工具取舍、十五周课程、具体批评与反思方法；实质教学指南，提醒全书仍在建设。 |
| 70 | [Project plans](https://book.leveldesignbook.com/learning/projects) | 推荐 Classic Combat 并说明本节暂被搁置；练习目录。 |
| 71 | [Classic Combat](https://book.leveldesignbook.com/learning/projects/classic-combat) | 研究既有游戏、绘制多方案、提案、单房间灰盒、至少三人测试、几何与物件各改一次、加秘密并反思；接近完整练习，仅示意图 TODO。 |
| 72 | [(Unfinished WIP pages)](https://book.leveldesignbook.com/learning/projects/unfinished-wip-pages) | 列出现代战斗、潜行、灯光、布局等练习；WIP 目录。 |
| 73 | [Modern Combat](https://book.leveldesignbook.com/learning/projects/unfinished-wip-pages/modern-combat) | 研究 Gallant 的 Uncharted 4 讲座、提出敌人角色与小规模分区遭遇；WIP，实际可实现工具和时长仍未定。 |
| 74 | [Modern Stealth](https://book.leveldesignbook.com/learning/projects/unfinished-wip-pages/modern-stealth) | 研究 Blacklist 感知与意识模型及信息反馈；WIP，仅研究阶段且问题列表未完。 |
| 75 | [Exercise: Direct Lighting](https://book.leveldesignbook.com/learning/projects/unfinished-wip-pages/exercise-direct-lighting) | 关闭预烘焙，手放直接光与补光训练判断；短 WIP 练习。 |
| 76 | [Exercise: Whiteboard 2D](https://book.leveldesignbook.com/learning/projects/unfinished-wip-pages/exercise-whiteboard-2d) | 参考 David Sirlin 收藏品文章，绘制平台关卡并标屏幕边界；WIP 任务提纲。 |
| 77 | [Level Design Portfolio](https://book.leveldesignbook.com/learning/projects/unfinished-wip-pages/level-design-portfolio) | 聚焦首页、项目截图视频、移动端与简历；WIP 建议笔记，工具及社交平台建议带时代性。 |
| 78 | [Design Test: Adaptation](https://book.leveldesignbook.com/learning/projects/unfinished-wip-pages/adaptation) | 以类型、体量、玩法、范围转译现实场地；短 WIP brief。 |
| 79 | [Exercise: Layout](https://book.leveldesignbook.com/learning/projects/unfinished-wip-pages/exercise-floorplan) | 以开门离开的五分钟关卡练习体验目标、节拍、parti 与泡泡图；WIP 提纲。 |
| 80 | [Exercise: Verticality](https://book.leveldesignbook.com/learning/projects/unfinished-wip-pages/verticality) | 用高度明暗分组、多层分图与过渡标注表达垂直布局；WIP 短练习。 |
| 81 | [Tools](https://book.leveldesignbook.com/appendix/tools) | 按引擎、可模组游戏、2D 编辑、艺术与规划整理工具；资源目录，强调复用成熟玩法以及能完成作品的工具。 |
| 82 | [TrenchBroom](https://book.leveldesignbook.com/appendix/tools/trenchbroom) | 刷子构建、MAP/BSP 接入、FGD、素材与脚本边界；实质工具/流水线指南与资源目录。 |
| 83 | [Assets & Resources](https://book.leveldesignbook.com/appendix/resources) | 列出免费模型、材质、灰盒贴图和 2D 资源；资源目录，天空盒部分为空清单。 |
| 84 | [Recommended talks](https://book.leveldesignbook.com/appendix/resources/talks) | 按一般设计、战斗与非线性空间列出署名免费讲座；资源目录，明确列入不代表穷尽。 |
| 85 | [Recommended books](https://book.leveldesignbook.com/appendix/resources/books) | 比较通识、工业实践、艺术参考与建筑书的适用范围及局限；带评论的资源目录，书目时间信息不可视为实时。 |
| 86 | [Quake resources](https://book.leveldesignbook.com/appendix/resources/quake) | 入门套件、社区、编译、WAD、模组、建模与 QuakeC 工具；资源目录兼指南，若干链接清单空缺，包含明确历史版本日期。 |
| 87 | [How to package a Quake map/mod](https://book.leveldesignbook.com/appendix/resources/quake/how-to-package) | 解释单图、完整模组、部分模组三类 ZIP 目录及大小写和覆盖约定；实质发布指南，规则属 Quake 引擎与社区语境。 |
| 88 | [File formats](https://book.leveldesignbook.com/appendix/resources/formats) | 仅介绍本节文件格式主题；目录性壳页。 |
| 89 | [FGD file format](https://book.leveldesignbook.com/appendix/resources/formats/fgd) | 点/实体/继承模板、属性、flags、模型预览及 TB 扩展；实质格式文档，强调无统一标准且 FGD 不实现运行时行为。 |
| 90 | [MAP file format](https://book.leveldesignbook.com/appendix/resources/formats/map) | 解释实体、worldspawn、凸刷平面、网格生成及 Valve 220 UV；实质格式文档，部分解析器列表为空，引用 Hajnoczi 等外部资料。 |
| 91 | [MDL file format](https://book.leveldesignbook.com/appendix/resources/formats/mdl) | 读取 Quake 1 小端二进制模型、压缩顶点、纹理接缝、组帧与插值，附 C/OpenGL 代码、法线和调色板；第三方 David Henry 存档镜像，文档注明 GFDL，代码另有宽松许可头。 |
| 92 | [Communities](https://book.leveldesignbook.com/appendix/communities) | 按游戏社群、综合论坛、环境美术、批评与播客分类；资源目录，强调具体游戏社群更利于可玩反馈。 |
| 93 | [About this book / authors](https://book.leveldesignbook.com/appendix/about) | 非营利志愿项目、写作与互动化路线、Robert Yang / Andrew Yoder 署名及第三方归属；项目介绍。 |
| 94 | [License / copyright](https://book.leveldesignbook.com/appendix/license) | 未另署名文本采用 CC BY-NC-SA 4.0，第三方素材归各原作者，提供引用格式；版权许可页，不能把全书全部内容一概视为同一许可。 |
