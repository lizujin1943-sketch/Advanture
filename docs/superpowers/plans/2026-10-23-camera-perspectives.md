# 俯视／第一人称视角实施计划

> 历史实施记录：用户在本轮后续试玩中要求改用第三人称，当前行为以文末“第三人称迭代”和主设计文档为准。

> **For agentic workers:** 使用 superpowers:executing-plans，在本会话内实现并验证；不派发实现任务。

**Goal:** 钓鱼游戏可按 V 在现有俯视角与第一人称之间切换，移动、瞄准和界面操作保持可用。

**Architecture:** 独立的 Unity `FishingCameraView` 管理相机姿态、视角输入变换与鼠标捕获；Controller 仍只提交现有 HookInput。角色遮挡和准星属于本地表现，模拟、状态编码、网络协议不变。

**Tech Stack:** Unity 2022.3，现有旧 Input API、IMGUI 与纯 C# 联机适配。

**Spec:** 用户本轮“添加切换视角功能，增加第一人称视角”，及 `docs/hook-island-game-design.md` 的现有俯视和钩索规则。

## Global Constraints

- V 切换，默认俯视；第一人称鼠标看向、中心瞄准、相对视角 WASD。试玩后初值 FOV 75°、水平鼠标灵敏度 1.2、垂直倍率 0.75、初始向下 25°、俯仰 -75°～75°；可从 Controller Inspector 调整 FOV / 灵敏度 / 垂直倍率。
- 试玩后平台第一人称高度改为 1.5 m，可从 Controller Inspector 调整；旧小岛眼高保持约 1.6 m，游泳继续跟随角色。隐藏自己的身体遮挡，保留手、钓竿与绳索，其他玩家完整显示。
- 菜单、商店、背包、建造、失焦、断线、倒地或离房时释放鼠标；重新捕获的第一帧不读取鼠标转向或出钩，避免点穿和跳转。
- 相机锚点读已显示的本地角色位置；射程与装备读确认态；准星沿用现有有效目标辅助与权威命中验证。平视／仰视不能保留旧瞄准点。
- 保留工作区原有预瞄线冻结修改。上岸逻辑、相机网络同步和新的第一人称专属模型不在范围内。

## Review Focus

- 仰视、平视及俯仰极限时出钩方向正确，输入不包含无效数值或对角加速。
- 关闭界面、重新聚焦和退出房间后光标与输入状态正确。
- 本地身体隐藏不影响队友、装备生效、钓线或确认态。
- 切回俯视恢复投影、缩放与原有跟随，不停在角色头内。
- 预测中的装备 ID 不得用于确认态背包查表。

### Task 1: 视角与输入

**Files:** 新建 Runtime/Presentation/Camera/FishingCameraView.cs；修改 Runtime/Composition/IslandFishingController.cs；新增 Tests/Editor/FishingCameraViewTests.cs。
**Interfaces:** `FishingCameraView.Toggle(Vector3 facing, Vector3 feet)`、`Look(Vector2 delta)`、`Movement(Vector2 input)`、`Aim(Ray ray, Vector3 feet, float range)`、`Follow(Vector3 renderedFeet, Vector3 confirmedFeet, float dt)`、`SetPointerCapture(bool)`、`Reset()`、`Dispose()`；Controller 对外 `FirstPerson`、`TogglePerspective()`。

- [x] 验证切换恢复、相对移动、俯仰范围、仰视瞄准；观察失败后实现。
- [x] 接入 CaptureInput / 更新顺序／离房和失焦清理，沿用原有瞄准辅助及 HookInput。

### Task 2: 表现、入口和验证

**Files:** 修改 FishingPresentation.cs、GameplayHud.cs、ReelingStaminaOverlay.cs、HudLayout.cs、主设计和试玩说明。
**Interfaces:** `FishingPresentation.SetFirstPerson(int slot)`，`-1` 恢复全部角色显示；保持原 `Present` 签名与预瞄冻结逻辑。

- [x] 只隐藏本地身体；增加中心准星、V 提示与操作说明，自己的耐力／合钓提示在 HUD 附近显示。
- [x] Unity 编译及相关 Editor 测试；实际运行 V 切换与鼠标捕获；通过两个真实 UDP 连接验证不同视角的移动、出钩和确认状态一致。
- 人工验证边界：菜单／背包实机手感及双独立窗口操作未完整验收，详见下方记录。
- [x] 一次独立最终复核；记录修复、截图与验证边界。按 `docs/agent-workflow.md` 不跑纯表现无关的全仓库回归；若改到模拟或传输则扩大验证。

## 执行记录

- Ruling: 本轮优先处理新视角请求；此前整体玩法验收明确搁置，不将旧日志当本轮结果。工作区基线保留在 artifacts/camera-perspectives/baseline。
- Ruling: 键位、视野与灵敏度属于可回调初值，按已说明默认实现，不再为这些常规选择追加确认。
- Pre-flight: CameraView 不读取装备或模拟状态，Controller 负责提供确认射程和本地表现锚点；Presentation 的隐藏只影响 Renderer，不影响状态。UI 读取 Controller.FirstPerson；各层无协议变更。

- Task 1 complete: 新增 FishingCameraView 与 Controller.Camera 局部适配。首轮相机行为用例观察到 3 失败 / 1 通过，再实现后转绿；投影／缩放恢复、移动转换、平视／仰视目标、Reset 光标均覆盖。
- Task 2: 第一人称本地遮挡、准星、V 提示、帮助、底部合钓／耐力提示已实现；显示用例确认队友、手／竿、装备 activeSelf 和编码状态不变。
- Review: 独立复核发现近水平视线与 ±90 m 分量裁剪会把 20° 出钩偏成 45°。新增跨水平线用例先复现 25° 偏差，再将第一人称海面交点按射程收拢；通过向量差比较避免 acos 的小角度浮点噪声。
- Verification: Unity 编译通过；最终 25 / 25 EditMode 测试通过（FishingCameraViewTests、HudLayoutTests、RecoverySceneTests），结果 artifacts/camera-perspectives/editor-tests.json。新增双真实 UDP 连接用例运行超过 60 个确认帧，分别使用第一人称／俯视转换移动并各出钩 1 次，共同确认帧编码一致。最初用例向队友出钩触发正常拖拽，改为向外出钩以隔离移动检查。
- Manual: Unity 平台场景单人实际按 V 进入第一人称，检查钓竿、海面、平台和本地身体遮挡；录得 Locked 鼠标及切回 None。后续 UI 自动操作被窗口状态变化／工具超时打断，未将菜单／背包实机手感列为已完整验收。
- Scope: 不改模拟、协议、传输和存档；未运行全仓库 verify.sh，也未做不同电脑局域网与双独立窗口人工联机验收。两连接验证为 Editor 中真实 UDP 会话，不将其描述为两台机器实测。
- Ruling: 补一份当前源码的 macOS 开发试玩包到 artifacts/camera-perspectives/HookPlatform.app；不覆盖既有试玩包。只按显式启动参数启用已有验证 pilot。
- Build: HookPlatform 场景的 macOS Development 构建成功，0 error / 0 warning，耗时 5.92 秒；记录 artifacts/camera-perspectives/build-result.txt。独立包尚未完成完整人工验收；编辑器中的运行画面与真实 UDP 测试结果见上。
- 2026-10-23 试玩调参：按用户确认，平台镜头高度从 0.8 m 提到 1.5 m，初始俯视从 15° 改为 25°；水平灵敏度从 2 降到 1.2，垂直倍率 0.75（等效 0.9），FOV 保持 75°。高度和垂直倍率加入 Inspector；两场景保存明确参数，修正 Unity 热重载保留的旧灵敏度。旧小岛眼高仍为 1.6 m。
- 调参验证：Unity 编译通过，既有 9 项 FishingCameraViewTests 全部完成且无失败（包括双 UDP 会话），没有为常量调参新增测试。macOS Development 构建成功，0 error / 0 warning，耗时 4.19 秒；产物和记录在 artifacts/camera-comfort。未跑全仓库 verify.sh，未重复完整人工联机验收；这轮操作手感留给用户试玩确认。

## 第三人称迭代

- 用户反馈第一人称仍不好操作，授权尝试第三人称。本轮将 V 改为俯视／第三人称双向切换，移除第一人称身体隐藏；角色、装备、钓线和脚下标记完整显示。
- 实现者选定初值：跟随支点高于脚底 1 米，后退距离 3.1 米，右偏 0.6 米，初始俯视 25°。默认镜头约在角色后方 2.8 米、脚底上方 2.3 米。滚轮调距范围 2～5 米，俯视限制 0°～65°，沿用视野 75°及水平 1.2／垂直 0.9 的灵敏度。
- 镜头围绕支点旋转，而不是固定位置只转朝向；Look / Zoom 在采样准星射线前同步更新位置。水面瞄准使用镜头射线与角色射程，运动仍只提交现有 HookInput。保留原有预瞄线冻结块。
- 验证：Unity 编译无错误／警告，28 / 28 EditMode 测试通过（FishingCameraViewTests、HudLayoutTests、RecoverySceneTests）。覆盖角色完整显示且位于准星旁、肩后视差、转向后立即瞄准、游泳时镜头保持在水上、低头不反向出钩、滚轮限距及两个真实 UDP 客户端确认状态一致。结果 artifacts/third-person-camera/editor-tests.json。
- 实际运行：通过正常单人 HostRoom 和 TogglePerspective 入口进入平台第三人称后暂停，捕获实际 Platform Camera 画面，确认人物、平台边缘与前方水面可见。截图 artifacts/third-person-camera/third-person-default.png。结束该验证会话后构建，不留下验证组件。
- 范围：未改模拟、协议、存档；未跑全仓库 verify.sh，也未重复双独立窗口人工联机。本轮尚无镜头碰撞或建筑遮挡淡化；用户操作手感待试玩反馈。
- 构建：第三人称版 macOS Development 构建成功，0 error / 0 warning，耗时 3.30 秒；试玩包 artifacts/third-person-camera/HookPlatform.app。

## 第三人称鼠标转向排查

- 用户确认鼠标移动时镜头完全不转。检查 Mouse X / Mouse Y 配置存在且工程启用两套输入；运行时第三人称、窗口焦点、鼠标捕获均正常，未有输入轴异常。用户配合移动鼠标后，采样 2,641 个游戏帧，2,224 帧处于 Locked，转向跳过标记仅 1 帧，镜头 yaw 未发生有效变化。原始记录 artifacts/third-person-turning/input-before-focused.csv。
- 最小调整：SetPointerCapture 每帧仍检查是否应捕获，但仅在实际锁定状态或可见状态变化时写 Cursor，移除重复锁定这一可疑输入重置来源。保留切换／重新聚焦首帧抑制；未改输入轴、灵敏度、角色转向规则或权威状态。
- 验证：Unity 编译无代码错误，既有 FishingCameraViewTests 12 / 12 通过，包含两真实 UDP 客户端确认状态一致；不为五行捕获状态调整新增重复实现的测试。结果 artifacts/third-person-turning/editor-tests.json。未跑全仓库 verify.sh，因为未改模拟、协议、传输或存档。
- 实机边界：CUA 的截图和鼠标输入均超时，不能据此声称完成了真实鼠标验证。调整后的观察期间没有收到有效鼠标移动，角色随后被水怪击倒，CameraControlsActive 因倒地正常关闭；该轮不计作转向通过。用户的实机复测答复仍待确认。
- 构建：macOS Development 成功，0 error / 1 warning，3.44 秒。警告为 Unity “uncompiled code changes”，最后一次代码变动仅为注释，无导入器／构建后处理修改。试玩包 artifacts/third-person-turning/HookPlatform.app。

## CS 式自由转向与输入入口修复

- 用户复测明确反馈上一版仍完全不转，并要求像 CS 一样完全控制镜头朝向。本轮保留第三人称机位，水平连续转向，俯仰改为 -89°～89°；WASD 沿镜头水平朝向，移动不自动回正，准星和出钩方向继续跟随镜头。大幅抬头时限制镜头最低高度，不限制视线朝向。
- 先补回归：通过 Input System 真实设备事件队列送入相对位移，旧的 `Input.GetAxisRaw` 入口没有响应；原有俯仰限制也无法抬头越过水平线。首轮 14 项相机测试 12 通过、2 失败，确认新增用例覆盖了此前直接调用 Look 所遗漏的输入入口。
- 修复：新增 `FishingCameraInput` 从 `Mouse.current.delta` 读取位移，并保持原灵敏度刻度；Runtime 与 Editor 测试程序集引用工程已有的 Unity.InputSystem。保留鼠标锁定状态切换及恢复焦点首帧抑制，不改权威模拟、协议或存档。
- 自动验证：最终 FishingCameraViewTests、HudLayoutTests、RecoverySceneTests 共 30 / 30 通过，含两个真实 UDP 客户端的移动、出钩与共同确认帧状态一致性；完整红／绿结果见 `artifacts/camera-freelook/editor-tests.json`。
- 运行验证：正常 HostRoom 单人进入平台，使用 Unity GameView.Focus 获取输入焦点；仅在本次验证期间注册 Input System 动态更新回调，注入 60 帧左右／上下相对位移，随后 10 帧不注入，通过正常 Controller 输入链路处理。70 帧均处于可控制、已捕获状态；旧轴始终为 0，新入口及镜头持续变化，实际俯仰到达向上 89°，瞄准点随之更新。尾段仍收到设备位移，不能当作“物理鼠标静止”验证；独立 Editor 用例已验证无事件时不会重复转向。原始数据 `artifacts/camera-freelook/controller-input.csv`。
- 验证回调已自动移除，结束临时 Play 会话后构建；没有写入场景组件或发布构建。检查了大幅低头及抬头的相机截图，位于 Unity 工程 `.build/camera-freelook/`；抬头所见为场景现有纯色天空背景。
- 范围：实际输入链路验证使用设备事件注入，物理鼠标手感仍需用户试玩；未做双独立窗口人工操作或跨电脑联机。仅改本地输入与表现，未运行全仓库 `verify.sh`。
- 构建：macOS Development 成功，0 error / 0 warning，耗时 3.62 秒；试玩包 `artifacts/camera-freelook/HookPlatform.app`，构建结果 `artifacts/camera-freelook/build-result.json`。Unity 已退出验证会话，停留平台场景 Edit Mode，可重新 Play 后按 V 测试。

## 第三人称操作手感调整（2026-10-28）

- 根据本轮“鼠标转向太慢、默认更俯视”的反馈，水平灵敏度 `1.2 → 3`，垂直倍率 `0.75 → 0.85`。设备相对位移仍按原输入单位读取，不乘帧时间，不增加鼠标惯性；实际每像素水平转向约 `0.12° → 0.3°`，垂直约 `0.09° → 0.255°`。
- 初始俯角 `25° → 40°`，新增 Controller Inspector 的 `Third Person Initial Pitch`；相机距离 `3.1 → 3.6 m`，肩侧偏移 `0.6 → 0.45 m`，支点高度仍为 `1 m`，FOV 仍为 `75°`。正式平台场景和旧小岛场景均保存明确配置，场景生成采用相同组件默认值。
- V 切回第三人称时保留本轮上一次的 yaw、pitch 和缩放距离；离房或重开通过现有 `Reset` 清除记忆并恢复配置的初始距离。相机仍立即更新后再采样瞄准，不自动回正。
- 保持鼠标可见、完整俯仰范围与滚轮 `2–5 m` 调距；菜单、商店、背包、建造、失焦和钩救时沿用现有输入屏蔽及恢复首帧保护。没有修改共享玩法、网络协议和存档状态。
- 回归记录：新增“切回保留角度与距离”“新回合恢复初始缩放”两项行为用例，旧实现先复现两项失败（分别出现 `127.31°` 角度重置及 `0.7 m` 距离残留），再实现修复；为新的默认构图更新既有相机用例的校准输入和几何预期。
- 验证：Unity 编译完成，最终控制台无错误／警告；FishingCameraViewTests、HudLayoutTests、RecoverySceneTests 整组复测 `33 / 33` 通过，包含鼠标设备事件、无输入停止转向、相对移动及两个真实 UDP 客户端移动／出钩／共同确认状态检查。结果在 `artifacts/third-person-feel/editor-tests.json`。首轮曾有一项 UDP 移动断言失败（角色 X 仍在出生点），相机方向转换及精确输入路径检查正确，单独复测和整组复测均通过；没有放宽断言或修改网络代码，也未将这次未复现的问题宣称为已修复。
- 场景检查：读取正式平台 Controller 确认灵敏度 `3`、垂直倍率 `0.85`、俯角 `40°`、距离 `3.6 m`、肩偏移 `0.45 m` 已加载。通过正常 HostRoom 与 TogglePerspective 入口进入单人第三人称，暂停在存活角色出生位置，检查实际游戏窗口及相机画面，角色、平台边缘和前方海面均可见；截图 `artifacts/third-person-feel/third-person-default.png`。验证房间已结束，临时 Editor 回调已自动移除，Unity 返回正式平台 Edit Mode。
- 边界：设备事件回归通过，尚未由用户确认物理鼠标的主观手感；未做跨电脑或双独立窗口人工联机，未运行全仓库 `verify.sh` 或生成新的试玩包。本轮仅修改本地相机输入参数与视角记忆，网络／模拟／存档代码均未变动。

## 默认进入第三人称（2026-10-28）

- 本地玩家确认入场且角色表现就绪后，自动采用第三人称。每轮仅初始化一次，后续按 V 切换俯视角不会被自动切回；离房、重新开局及组件停用时重置初始化标记，下一次入场恢复默认第三人称。
- Unity 正式平台场景窗口也对齐实际入场的第三人称机位，采用透视投影、40° 俯角及 75° 视野，便于直接查看角色与周围海面。
- 验证：Unity 编译完成，控制台无错误／警告；通过正常 HostRoom 入口确认自动进入第三人称，手动切换俯视后继续运行 10 帧仍保留选择，离房后重新创建房间再次自动进入第三人称。记录见 `artifacts/default-third-person/entry-check.json`。临时观察回调已自动移除，验证房间已关闭，编辑器返回 Edit Mode。
- 本次仅修改本地相机初始化与编辑器观察机位，未运行全仓库 `verify.sh`、跨电脑联机或重新构建试玩包。

## 第三人称光标瞄准（2026-10-29）

- 第三人称不再把屏幕中心当作指针，改为与俯视角一致读取鼠标光标位置。相机从该位置发射射线，既有水面落点、射程约束及目标辅助均使用同一个光标；本地角色沿既有平滑转向对准该目标，出钩通过既有 HookInput 交付同一目标点。出钩期间仍保留已经接受的发射方向。
- 操作前重新选择 `TacticalGame.Unity@c147ce230fd064b2`，核实工程绝对路径为本仓库 `samples/TacticalGame.Unity`，活动场景为 HookPlatform。最初发现连接指向另一工程时只读取了路径，没有在该工程运行代码、修改场景或进入 Play Mode。
- 验证：Unity 编译完成且控制台无错误／警告；既有 FishingCameraViewTests `16 / 16` 通过，包含两真实 UDP 客户端移动、出钩及确认状态一致性。正常 HostRoom 入场后，从 Controller 实际输入链路读取非中心光标 `(634,905)`，窗口 `2006×1270`；实际瞄准点与光标射线落点的平面误差为 `0 m`，与旧中心瞄准点相差 `2.34429 m`。观察时角色仍处于 Ready，并按既有转向插值接近目标。结果见 `artifacts/cursor-aim/`。
- 验证回调已移除，临时房间已结束，返回 Edit Mode；没有加入常驻验证组件。本次仅修改 Unity 本地输入选点及说明，未改共享模拟、网络协议或存档，未运行全仓库 `verify.sh`、跨电脑人工联机或重新构建试玩包。

## CS 式鼠标捕获与转向（2026-10-29）

- 用户最终确认采用标准 CS 操作：游玩时锁定并隐藏鼠标，用设备相对位移连续转动镜头，打开界面时释放并显示鼠标。第三人称机位及无十字准星的显示保留；瞄准改为相机视口中央，俯视视角仍沿可见光标瞄准。WASD 沿镜头水平朝向移动，沿用斜向限速、无惯性转向及既有灵敏度。
- `FishingCameraInput` 读取工程已有 Input System 的相对位移；每个本地相机单独跟踪输入启停，重新捕获时丢弃首帧位移。鼠标锁定仅在状态变化时设置；菜单、背包、商店、建造、失焦、倒地、钩救及离房沿用输入屏蔽，恢复时避免跳转。没有修改确定性模拟、协议、存档或收益结算。
- 回归先复现“锁定位置不变但设备发生位移时镜头不转”和“第三人称未捕获鼠标”两项失败。实现后 FishingCameraViewTests、HudLayoutTests、RecoverySceneTests 共 `34 / 34` 通过，覆盖停止位移后停止转向、暂停／恢复、视角相对移动，以及两个真实 UDP 客户端的移动、出钩和确认状态一致。结果在 `artifacts/cursor-camera-follow/cs-editor-tests.json`。编译期间曾出现既有 FishWaterEffectsTests 的 BakeMesh 过时 API 警告，最终运行控制台无错误／警告。
- 通过正常 HostRoom 进入正式平台，以临时 Input System 相对设备事件经过实际 Controller 输入链路观察 38 帧：20 次 `(6,2)` 位移使 yaw `45° → 81°`、pitch `40° → 29.8°`，随后无输入时角度保持不变；正常 OpenMenu 后鼠标释放且转向停止，关闭后重新捕获且没有角度跳变。入场初始化后的中心瞄准平面误差为 `0 m`。记录在 `artifacts/cursor-camera-follow/cs-controller-device-events.csv` 和 `cs-device-summary.json`。
- 原生窗口自动拖动没有产生可用的相对设备位移，该尝试不算物理鼠标转向验收；实际鼠标的主观手感仍待用户试玩。上述运行验证使用临时设备事件，回调在观察结束自动移除，不写入场景或构建。验证房间结束并返回平台 Edit Mode；本次未跑全仓库 `verify.sh`、跨电脑人工联机或生成新试玩包。
