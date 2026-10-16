# 历史记录：联机接入前的 Unity 本地试玩

日期：2026-10-16。环境：macOS / Apple Silicon，Unity 2022.3.62f1，内置渲染管线，Input System 1.14.0。

## 已完成

- 从 `samples/TacticalMap/Exports` 导入可视 FBX 和碰撞 FBX。
- 生成场景、16 种地图材质、天空材质、灯光和第一人称角色预制体。
- 地图包含 17 个盒形碰撞体；集装箱导入尺寸为 8 × 2.65 × 2.8 米（Unity X/Y/Z）。
- Unity 批处理生成场景成功，日志包含 `YARD07_BUILD_OK`，退出码为 0。
- 在 Unity 中渲染并检查第一人称相机预览，确认模型、配色和视野方向。
- 本机许可证问题已解除；编辑器已打开正确工程并进入 Play 模式。
- 最终代码在编辑器中重新运行 Unity EditMode 测试：**13 / 13 通过**，失败和跳过均为 0。
- 构建 macOS 独立试玩程序 `artifacts/tactical-unity/Yard07.app`：成功，**0 错误、0 警告**，构建报告大小 210.48 MB。
- 独立试玩程序已实际启动，地图、中文提示与准星显示正常。

### 测试覆盖

| 测试 | 检查内容 |
| --- | --- |
| ForwardInputMovesAtWalkingSpeed | 前进速度正确 |
| DiagonalDoesNotMoveFasterThanForward | 斜向移动不加速 |
| SprintMovesFasterThanWalking | Shift 对应的加速输入有效 |
| LookingRightChangesWalkingDirection | 转身后按新朝向移动 |
| LookingUpIsClampedAndDoesNotMakePlayerFly | 俯仰限位且水平移动不受抬头影响 |
| GravitySettlesOnFloorWithoutFallingThrough | 重力落地且不穿地面 |
| CharacterStopsBeforeWall | 胶囊角色被墙体阻挡 |
| RespawnRestoresPositionAndView | 重置位置与视角 |
| SameInputHasSameDistanceAtDifferentFrameRates | 不同画面帧率下相同行走时间距离一致 |
| CollisionBoxesMatchVisibleWallsFloorAndCover | 实际场景可见模型与碰撞体对齐 |
| SpawnIsClearAndCameraIsAtStandingEyeHeight | 出生点无障碍物重叠且相机高度正确 |
| ImportedContainerHasRealWorldScale | FBX 换轴后比例正确 |
| ActualSpawnWallBlocksSprint | 实际地图出生墙可以阻挡加速移动 |

运动测试使用真实 `CharacterController`，场景测试直接打开生成的 Yard07 场景。最终测试作业为 `1bea4be1df294075a82f8544afdbbe6d`；构建作业为 `build-eae4a879a3`。

本次复验发现：编辑器已经打开 Yard07 时，原运动测试与地图共用默认物理世界，测试角色撞到了地图中央箱子，导致 3 项测试失败。现为每项运动测试创建独立的预览场景，并确认其拥有独立物理世界；结束后关闭场景并释放所有测试物体。修复后重新运行全部 13 项，全部通过。该修改只影响测试隔离，没有改变运行时移动代码。

实现按测试驱动完成：空运动实现时 8 项失败、1 项通过；加入运动后剩余 2 项重置测试失败；完成重置与场景检查后 13 项全部通过。

## 独立程序交互验证

在 macOS 独立试玩程序中，通过系统窗口操作执行并观察了以下流程：

| 操作 | 观察结果 |
| --- | --- |
| 点击画面 | 中央提示隐藏，出现准星，可开始移动 |
| W / S / A / D | 前进、后退、左右平移均改变角色位置 |
| 持续向仓库墙体移动 | 没有进入封闭仓库，角色沿墙面移动 |
| Shift + W | 可以移动；加速的具体速度由自动测试验证，未从画面对比测量 |
| R | 回到初始出生位置并恢复初始视角 |
| Esc 后继续按 W | 恢复“点击画面开始探索”提示，位置保持不变 |
| 再次点击后按 W | 提示消失，恢复移动 |
| 中文显示 | 标题、操作提示和进入提示均正常显示 |

### 尚未覆盖

- 自动窗口操作的鼠标拖动没有让锁定状态下的视角转动，因此不能把它当作真实鼠标输入验收通过。左右转向、上下视角、俯仰限位和重置的计算已由自动测试验证；实际鼠标相对位移输入与手感仍需人工试玩确认。
- 切换应用后的鼠标释放，以及不同窗口尺寸下的显示，尚未完成可靠的交互验证。
- Windows 尚未实测。当前实现没有接入帧同步网络，不代表多人同步已验证。

早先的许可证错误已不再阻塞编辑器、测试和构建；此前的错误日志只作为历史排查记录保留。

## 仓库完整验收

已运行仓库入口 `./scripts/verify.sh`。第一组框架测试 **90 项通过、11 项失败**，命令退出码为 1；后续验证阶段因脚本提前退出而未运行。

失败均来自现有 `ClientPrediction`。本次开始前该文件已有未提交修改；本次地图试玩没有修改 `framework/`。具体失败如下，不能把 Unity 测试通过解释为整个仓库验收通过。

| 失败用例 | 预期 / 实际 |
| --- | --- |
| prediction.freeze_missing_local_input_stops_before_execution | 0 / 4 |
| prediction.freeze_neutral_is_explicit_immutable_and_matches_final | 1 / 2 |
| prediction.freeze_gap_blocks_later_input_until_filled | 1 / 4 |
| prediction.freeze_rejected_submission_does_not_unlock_prediction | 0 / 2 |
| prediction.freeze_final_frame_does_not_need_local_submission | 1 / 4 |
| prediction.freeze_future_final_does_not_skip_local_gap | 0 / 2 |
| prediction.inactive_local_member_only_requires_input_after_activation | 1 / 3 |
| prediction.local_response_precedes_confirmation | 0 / 1 |
| prediction.remote_input_restores_preceding_frame_and_replays | 1 / 0 |
| prediction.identical_bytes_with_different_origin_still_corrects_checkpoint | 1 / 0 |
| prediction.final_overrides_queued_local_input_without_speculative_local_execution | 2 / 3 |

## 本地证据

以下位于仓库忽略的生成目录，不提交到 Git：

- [最终 Unity 测试结果：13 / 13](../../artifacts/tactical-unity/tests-editor-final.json)
- [隔离修复前的 3 项测试失败](../../artifacts/tactical-unity/tests-editor-before-isolation.json)
- [最终 macOS 构建结果](../../artifacts/tactical-unity/build-macos-final.json)
- [macOS 试玩程序](../../artifacts/tactical-unity/Yard07.app)
- [初次批处理测试结果](../../artifacts/tactical-unity/tests-editmode.xml)
- [初次批处理测试日志](../../artifacts/tactical-unity/tests-editmode.log)
- [场景生成日志](../../artifacts/tactical-unity/build-scene.log)
- [第一人称渲染预览](../../artifacts/tactical-unity/unity-first-person.png)
- [首次编辑器运行日志](../../artifacts/tactical-unity/editor-play.log)
- [历史许可证错误](../../artifacts/tactical-unity/build-player.log)
- [框架完整验收日志](../../artifacts/tactical-unity/framework-verify.log)
