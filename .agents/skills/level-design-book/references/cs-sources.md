# CS 专项：来源、定位与适用边界

研究日期：2026-10-16。以下三份是独立新增来源，不属于原书 94 页正文；本 skill 提供原创中文综合与工作方法，**不复制或再分发原幻灯片、截图、视频、完整字幕或文章全文**。这些原材料权利归作者／权利人；原书的 CC BY-NC-SA 许可不替代第三方许可。本 skill 的中文改编继续沿用主文件声明的许可，原资料不因此被重新许可。

## GDC 2015：技能与战略选择

作者：Salvatore “Volcano” Garozzo、Shawn “FMPONE” Snelling。

- [正式演讲页：Community Level Design for Competitive CS:GO](https://www.gdcvault.com/play/1021868/Community-Level-Design-for-Competitive)
- [演示稿索引](https://book.leveldesignbook.com/appendix/resources/talks)，另见 [52 页 PDF](https://firebasestorage.googleapis.com/v0/b/gitbook-x-prod.appspot.com/o/spaces%2F-LtVT8pJjInrrHVCovzy%2Fuploads%2FMBSI3YYkCaAcUQ9gVvDF%2FCompetitiveCSGO_GarozzoSnelling.pdf?alt=media&token=80dd4152-8a00-4648-bda3-58ffdd72c97a)，文件名 `CompetitiveCSGO_GarozzoSnelling.pdf`，封面题名 **Emphasizing Player Skill and Strategic Choices in CS:GO Level Design**。
- [GDC 视频](https://www.youtube.com/watch?v=Y5RZP52jYy4)。同索引另链的 2014 NYU PRACTICE 演讲不是本次 2015 演讲，不混淆版本。

已逐页提取并视觉核对全部 **52 页**幻灯片，包括纯图页；正文引用的 p 为 PDF 页码。同时读取官方播放器全段音频 **24 分 08 秒**，完成本地自动转写并从开场到致谢／掌声逐段阅读，与幻灯片交叉核对；音频末尾无后续问答。转写存在识别误差，因此不作为准确逐字引文，也不据此填入技术数值。关键示例另作抽帧核对，不声称逐帧观看整段视频。

| 页码 | 内容／图示与在 skill 中的用途 |
|---|---|
| 1–6 | 标题、作者经验、作品与战略深度导入；履历不当作原则的独立证明。 |
| 7–9 | 当时规则概述、Cache 默认部署、四处卡口与中央战线图；不把回合秒数或四卡口移植为规定。 |
| 10–12 | 同时危险方向过多的问题；Cache A 常见防守位置与视线叠加图，用于分解威胁检查。 |
| 13–16 | 适度高差、队友堆叠／双架、CS1.5 人塔、Shroud 站位；旧版本例子不证明本项目可达／合法。 |
| 17–18 | 技巧跳的技能收益与边界；Cache 三步跳路径图展示难度、危险和时间成本。 |
| 19–21 | 穿墙机制、知识收益、强位反制；Crown 门两侧穿点图，不推导为所有门侧都可穿。 |
| 22–25 | 包区设计，Cache A 安全包、Main 包、Middle 包；转换成安包风险与守包方向关系。 |
| 26–32 | 美术与竞技功能的妥协、Cache 旧新画面对比、角色轮廓与辨识；不等同于“越亮越公平”。 |
| 33–39 | Dust2 极简几何、低饱和背景与出口对比；关于留存／受欢迎的解释作为作者主张，不当因果研究结论。 |
| 40–44 | 极简的局限与长处、Season 和建筑参考；艺术主题仍应成立，不一律白墙。 |
| 45–50 | 细节／天空盒的功能，Cache、Season、Overpass 远景；转化为投掷参考和视觉噪声检查。 |
| 51–52 | 形式与功能的收束画面及作者联系方式；无额外配方。 |

口头补充定位（近似时间，非逐字引文）：2:30–4:11 的三路／四卡口、默认展开与 CT 前压信息；4:45–5:39 的交叉火力和过多掩体；9:24–10:59 的技巧跳替代与金属表面噪声；11:00–12:12 的穿点代价；12:12–13:54 的包位取舍；14:36–16:11 的职业玩家反馈与场景情绪妥协；22:31–23:50 的天空盒投掷参照。

适用边界：2015 CS:GO 制作经验。不是 CS2 技术规范、当前赛事地图分析，也不是证明每个 Cache／Crown 站位在新规则下成立。演讲内 Shroud 实战与末尾投掷参考示例仅抽帧核对，不冒充完整比赛／外链视频分析。

## Dave Johnston：The Making Of: Dust 2

[作者原文](https://www.johnsto.co.uk/design/making-dust2/)，2003-01-29，页面更新 2019-07-01。已读完整文章至 Thanks，并核对文内 26 个不同图片地址的图示（部分为缩略图）；没有将外链视频及其他文章算作本篇已读内容。

| 原文章节 | 提炼重点／限制 |
|---|---|
| Introduction | 续作的预期与主题约束，不能把作者最初的信心不足写成成功秘诀。 |
| Sticking to a Theme：Arches / Road / Trim / Sun / Design | 分区、截视线、路线识别、克制装饰、熟悉元素与不同距离交火；饰边次数、昼景及纹理属于 Dust 美术选择。 |
| Keeping It Simple / Starting Out | 紧凑连接与战术余地、迅速草图；“八字”是作者对 Dust 的抽象并用于续作，不是所有竞技地图的三路定律。 |
| The Early Dust 3 / Alpha | 纸面到可玩时调整比例；长 CT 出生区含 Worldcraft 范围限制原因；包点只有入口和箱子仍可欠佳；外部反馈增加目标箭头。 |
| Beta | 平台、箱体、墙洞连接和楼梯逐步产生新选择；是设计迭代叙述，非受控实验或固定数量配方。 |
| Release | Jess Cliffe 协助调整出生与目标；2001 原版发布后未改布局的说法有版本范围，不能延伸为各代 Dust2 永远不改。 |
| Condition Zero / Source / Global Offensive | 改版的美术、比例、通道、掩体和视线变化；支持美术改动需要玩法回归，不把文章评价冒充当代数据。 |
| 20th Anniversary Fun / Elsewhere / Thanks | 2019 复古版与社区移植、参与者署名；不额外推出平衡原则。 |

## World of Level Design：多人布局教程

AlexG／World of Level Design，[How to Design Multiplayer Gameplay Map Layouts (Complete In-Depth Guide)](https://worldofleveldesign.com/categories/csgo-tutorials/csgo-how-to-design-gameplay-map-layouts.php)，2013-12-10。已读全部文章正文至 BSP block-in 结尾，并核对 29 张正文配图；网页导航、广告／课程推广不算设计理论。嵌入教程视频和链接出去的其他教程不属于本次完整文章阅读范围。

| 原文章节 | 提炼重点／限制 |
|---|---|
| Getting Started / Know Your Game | 模式、主题、机制先行；有目的地玩双方，笔记记录路线、时机、交火距离；不照抄示例手写秒数。 |
| Scenario / Starting the Layout | 爆破和人质的攻守身份不同；粗草图和参考服务布局，不强制精美设计图。原文轻视 DM 布局思考的说法不采纳。 |
| Main Path Routes | 先画主路、出生和目标，再分联络路；“最多三路”和同文 Dust2 四路示例不一致，不转成硬限制。 |
| Choke Points | 首次接触、防线与攻守到点时序、不同距离交火；稍早到点和入口数量是旧游戏的经验起点。 |
| Connecting Path Routes | 受阻后的变线选择、进攻／防守侧分别检查；不要求每条主路横连，也不无条件禁止所有卡口附近连接。 |
| Objectives and Spawn Points / Putting It All Together | 随连接和卡口调整重访出生／目标；纸面快速迭代后进入可玩白盒，不让草图代替测试。 |

其“守方不推进”等概括不能覆盖真实攻防策略；地图举例、热度和竞赛适用性均是当时经验，不是当前比赛地图池。人质模式允许更动态的交火位置，也不意味着无需时间测试。

## 综合与新增工作方法如何引用

正文 `[GDC] / [D2] / [WoLD]` 指上述来源相应页／章节；`[操作化]` 明确是本 skill 为项目设计的建议，包括出生时间范围、条件胜率、完整回合阶段、资源分组、声音与投掷边界实测等。需要准确历史细节回查原文；需要当前 CS／自研游戏数值回查目标版本代码、文档与实测，不用这份来源索引补造数据。

原始缓存、文本提取、逐页渲染和验证记录位于项目忽略目录 `artifacts/cs-level-design/`，不是 skill 运行依赖，不随 skill 分发。
