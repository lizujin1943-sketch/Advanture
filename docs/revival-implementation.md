# 倒地、救援与付费复活

规则来源：玩家确认队友救起免费、倒地 60 秒后死亡、死亡后各自支付复活费、全队死亡且无人付得起自己的费用才失败。正式场景为 `samples/TacticalGame.Unity/Assets/HookIsland/Scenes/HookPlatform.unity`。

## 操作与结算

- 血量归零进入倒地，不能移动、游泳、出钩、交易或建造，装备和背包保留，金币不变。
- 队友与倒地者都在有效地面上，距离不超过 1.5 米且中间没有障碍／水面时，按住 **R 3 秒**免费救起，恢复 30% 最大生命（至少 1 点）。不补耐力或饥饿，耐力恢复重新等待。
- 松开 R、移动、出钩、开菜单、受伤、被拖拽、击退、离开距离或施救者离线会中断进度。多人同时施救不加速；换人后从头开始。
- 水中倒地先用钓竿拖上有效地面，再近身救起。拖拽不会治疗，60 秒倒计时照常推进。救援恰好完成的那一帧优先于超时死亡。
- 60 秒到期进入死亡。死亡时解除钩索连接及残余击退，此后不能继续被钩救或近身救起。
- 死亡页显示个人余额与费用，点击付费复活后回到出生点附近可站立且无人占据的位置。默认满血、满耐力，保留装备、背包和饥饿。不足全额或找不到落脚点时不扣款。
- 费用在最终死亡时锁定，实际复活成功时扣一次：`10 + floor(本局模拟秒数 / 300) × 5 + floor(死亡时个人金币 × 10%)`。等待不会继续涨价，不使用队友钱包。
- 所有人均已死亡，且没有任何人能支付自己的锁定费用，才显示失败。全队倒地不立即失败；有人正好付得起也不失败。

## 配置

`Assets/HookIsland/Data/RevivalSettings.asset` 由 `FishingSettings.revival` 引用。创建房间时写入确定性地图配置，所有客户端必须一致。

| 字段 | 默认值 | 含义 |
|---|---:|---|
| `downedSeconds` | 60 秒 | 倒地救援窗口 |
| `rescueSeconds` | 3 秒 | 连续施救时间 |
| `rescueRangeMetres` | 1.5 米 | 近身施救范围 |
| `rescueHealthFraction` | 0.30 | 免费救起恢复生命比例 |
| `baseFee` | 10 G | 固定费用初值 |
| `growthFee` | 5 G | 每个完整时间段增加的费用 |
| `growthIntervalSeconds` | 300 秒 | 固定费用增长间隔 |
| `balanceFeeFraction` | 0.10 | 死亡时个人余额的收费比例 |
| `respawnHealthFraction` | 1.00 | 付费复活生命比例 |
| `respawnStaminaFraction` | 1.00 | 付费复活耐力比例 |

时间按 30 Hz 房间模拟帧计算；现实离线时间不计入。金额向下取整并防止溢出。

## 实现边界

纯 C# 的 `RevivalSystem` 管理生命周期和原子结算，`RevivalStateCodec` 保存并校验倒计时、施救者、死亡报价、支出账本、死亡代号和请求回执。Unity 的 `RevivalController` 只提交输入，`RevivalOverlay` 只呈现确认状态。复活扣款不进入通用帧同步核心。

付费请求绑定死亡代号并使用递增请求号；成功和失败都保存回执。重复发送、旧生命的请求及读档重发不会再次扣款，失败后需新的主动请求。救援状态在水怪的击退／离线状态读完后统一校验。

规则／状态／房间协议升级到 v12，输入版本为 7、地图格式为 8。旧版本快照和不一致的房间配置明确拒绝；没有添加跨版本迁移、运行中热加入或断线重连机制。

开发验收驱动需显式 `-island-revival-smoke 1`，只在 Editor／Development 中编译，正常启动不自动游玩，正式发布包排除该驱动。它通过正常输入钓鱼赚金币、落水、钩救、近身施救和付费复活，不向正式玩法注入资源。

## 验证记录

- Unity EditMode **50/50** 通过：配置资产和地图字节一致、配置改变会拒绝混联、输入组合与付费意图保留、施救提示保持当前目标。结果：`artifacts/revival/unity-tests-final.json`。
- 最终开发包的四个独立 Unity 客户端完成普通输入驱动的落水、钩救、免费扶起、钓鱼赚币和付费复活。在第 **2292** 帧关闭并保存，四端与服务端完整 checkpoint 一致：`e40600aefc98349b3618470f2f390e7186ba61b0c21c76c7a021a77ca508634f`。P2 被钩救并免费扶起，复活支出 0；P3 捕鱼收入 22 G，个人复活支出 12 G，余额 10 G。每端只有一个显式启用的验收驱动。
- 四端报告、完整快照及倒地／施救／死亡页截图：`artifacts/hook-island-playable/revival-smoke-20261021-170413-d037a2/`。运行命令：`python3 scripts/run-island-party.py --players 4 --revival`。
- Development 和 Release 构建均为 **0 错误、0 警告**。开发包：`artifacts/hook-island-playable/HookPlatform.app`；正式包：`artifacts/revival/release/HookPlatform.app`。正式包程序集保留复活控制器、界面和配置，未包含任何 `SmokeDriver`／`SmokePilot` 类型，记录见 `artifacts/revival/release-audit.log`。
- 一次独立代码审查没有发现严重或重要问题；修复了持续施救时提示可能跳到更近队友的问题。先用测试重现，再修正为优先显示仍有效的持续施救目标，失效后重新选取；RED 记录见 `artifacts/revival/target-hint-red.json`。
- `./scripts/verify.sh` 完整通过，退出码 0、无失败用例；复活专项 **26/26**，同时覆盖原有帧同步、持久化、UDP、平台、钓竿、背包、角色、游泳和水怪。结果：`artifacts/revival/full-verify.log`。复活专项四客户端在第 **2290** 帧完整状态一致，个人支出 12 G；另一个四客户端用例验证全员最终死亡且各自余额不足才失败。
- 首次合入发现复活测试手工构造的水球距离不是最新水怪规则下的完整帧步长，已修正测试数据，并保留生产校验；该受伤中断救援用例及修正后的全量验证均通过。
