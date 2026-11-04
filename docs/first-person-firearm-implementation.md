# 枪械第一人称视角

2026-11-03。正式场景：`samples/TacticalGame.Unity/Assets/HookIsland/Scenes/HookPlatform.unity`。

## 使用方式

- 持有并选中枪械时自动进入第一人称。新开局默认手持沙漠之鹰；1 切主武器、2 切手枪、3 切近战，已装备武器留在各自槽位。
- 鼠标转头；WASD 按相机的水平方向移动；左键开火，R 换弹。斜向移动、抬头或低头不会改变移动速度。
- 枪械准星固定在相机视口中央。屏幕内显示独立的枪和双手，以及举枪、后坐力、滑套和换弹动作。
- 切换到平底锅等非枪械工具后，恢复之前的俯视／第三人称模式及缩放。第三人称保留原来的俯仰角，水平方向沿用持枪时的转向。
- 持枪期间 C 不切换模式，滚轮不拉远镜头；非持枪时 C 与滚轮保持原有操作；V 用于次数回血。
- 背包、菜单、商店打开或窗口失焦时释放鼠标；恢复控制时丢弃第一份相对鼠标位移，避免视角突然跳动。倒地、离房、重开和组件停用会清理第一人称表现。

## 代码职责

| 文件（相对 `Assets/HookIsland/Runtime`） | 职责 |
| --- | --- |
| `Presentation/Camera/FishingCameraView.cs` | 保存普通视角选择；持枪时将镜头放到本地角色眼睛高度；转换移动方向和瞄准落点。 |
| `Presentation/Camera/FirearmCameraSettings.cs` | 配置眼高、进入时俯角、视野角和视野角过渡时间；不进入网络和存档。 |
| `Input/FishingPlayerInput.cs` | 从确认态判断当前工具；第一／第三人称共用中心射线、相对鼠标和按视角移动的规则。 |
| `Presentation/Characters/FirstPersonAvatarVisibility.cs` | 仅在本客户端隐藏本地完整身体，保留动画及节点；退出时恢复每个渲染器原来的开关。 |
| `Presentation/Characters/FirstPersonFirearmView.cs` | 独立的近处枪械与双手显示实例；复用现有枪械资源，读取同一把枪的预测动作及已去重确认开火事件。 |
| `Presentation/FishingPresentation.cs` | 管理本地第一人称与世界角色表现；将对应的实际显示枪口交给原有确认弹迹效果。 |
| `Composition/IslandFishingController.cs` | 按“输入 → 网络推进 → 世界表现 → 镜头跟随 → 第一人称表现 → 确认反馈”的顺序装配。 |
| `Presentation/UI/Hud/FirearmReticleHud.cs` | 使用与输入相同的相机视口中心绘制准星。 |

## 与确定性玩法的边界

框架、游戏模拟、输入协议和存档格式均未修改。装备身份读取确认态，避免预测换装回滚导致镜头抖动；本地脚底位置跟随已经渲染的预测角色，获得及时移动反馈。

第一人称最初接入时使用二维平面命中；后续 Boss 版本已改为传递量化的上下瞄准方向，使用三维整数射线命中实体与主眼。枪口和手部显示位置仍只影响画面，不参与命中结算。2026-11-04 新增的 AWM、巴雷特使用该射线实现远距离开镜瞄准；开镜只放大本地相机并降低转向灵敏度，详情及验证见[长枪实施记录](long-guns-implementation.md)。

近处手枪使用独立显示实例，其他玩家仍看到完整世界角色和原有持枪动画。近处手枪不参与碰撞、投影和水面反射。当前没有新增独立武器渲染相机或贴墙收枪系统。

## 验证

相机回归覆盖俯视／第三人称进入与恢复、滚轮／V 约束、游泳眼高、中心射线、不同屏幕比例、转向与移动方向，以及重置。

表现回归覆盖仅隐藏本地角色、保留同伴、恢复原先隐藏标记、枪械随镜头转向、换弹动画、清理后重入，以及状态字节不变。最终运行结果记录在 `artifacts/first-person/`。

本次不改确定性玩法和网络，因此不运行整个仓库的 `scripts/verify.sh`。

### 本次验收结果

- Unity 2022.3.62f1、macOS 编辑器编译通过。
- 相机、枪械场景、控制器集成及新增第一人称表现检查共 **41 项通过**，包含已有的两个 UDP 客户端移动一致性检查。
- 完整 EditMode 回归为 **202 / 207 通过，5 项失败**。下面列出的失败保留原样，没有用本次功能修改掩盖。
- 在正式 `HookPlatform` 场景创建单人本地网络房间，用正常输入和装备命令完成：第三人称 → 选中枪械 → 第一人称 → 开火 → 换弹 → 切回鱼竿 → 第三人称 → 离房。眼高为脚底以上 1.03 米，弹药依次为 7 → 6 → 7；离房后第一人称关闭、鼠标锁定释放。
- 已查看实际相机截图，确认角色身体没有挡住镜头，近处手枪位于画面右下方。试玩待机期间角色被螃蟹击倒时，也观察到镜头退出第一人称。

| 未通过的既有检查 | 当前失败位置与原因 |
| --- | --- |
| `EnemySceneTests.FormalPlatformPackagesEnemyRules` | 检查要求开启水怪，当前场景配置 `monsterEnabled` 为 0；该配置在本次修改前已处于修改状态。 |
| `EnemySceneTests.ViewsRenderConfirmedEnemiesWithoutPhysicsOrStateMutation` | 同一水怪配置关闭后，预期的 1 个水怪表现实际为 0。 |
| `FisherAnimationTests.ForearmsAndHandsStayOutsideBodyAcrossCastAndLocomotionBlends` | 原完整角色动画在 sample 24 的左手顶点穿入身体；检查直接实例化原角色，并不经过第一人称手部显示。 |
| `RestartSceneTests.NewRoundClearsAllPendingActionsAndWindowsBeforeSampling` | 测试通过反射写入旧的 `heldTarget` 字段，但当前代码中它已是属性，导致空引用。 |
| `RevivalSceneTests.RescueInputPreservesMovementAndCancelAndDoesNotMixTransactions` | 同样反射旧 `heldTarget` 字段导致空引用。 |

完整结果：`artifacts/first-person/editmode-results.json`；场景连续验证：`artifacts/first-person/playmode-validation.txt`；相机截图：`artifacts/first-person/first-person.png`。这些生成产物不提交到 Git。

截图时还记录到 MCP 插件 `ScreenshotUtility.CaptureCompositedAfterFrame` 调用 `EditorApplication.Step()` 导致的 PlayerLoop 递归错误，堆栈保存为 `artifacts/first-person/editor-tool-errors.json`。这次没有修改第三方插件；相机截图成功返回，随后连续玩法验证通过。

本次没有运行两个独立游戏窗口、发布构建、Windows 实机和手动鼠标手感验收；编辑器内场景验证使用临时自动输入回调，完成后已移除并退出 Play Mode，没有向正式场景添加自动测试组件。以上失败原因来自当前文件与堆栈核查，没有另外切换仓库版本做基线对照。
