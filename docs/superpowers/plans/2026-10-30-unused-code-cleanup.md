# 无用代码清理

目标：删除已被替代、没有有效调用的实现及完全重复的测试，保持现有玩法、资源、联机与存档行为。

## 清理依据

| 对象 | 依据 | 保留的实现 / 覆盖 |
| --- | --- | --- |
| `scripts/blender/fish_geometry.py` 第一版鱼身、鱼鳍和贴图生成器 | 正式构建入口已改用 `fish_reference_geometry`；旧入口及其专用辅助函数无外部调用 | 共用网格、坐标转换、蒙皮工具；新版模型、骨骼、动画生成器 |
| `HudPainter.StaminaColor` | 无调用；当前体力弧使用独立配色 | 当前体力显示逻辑 |
| `GameplayHud.ObjectivesCollapsed` | 无调用的转发属性 | 任务栏实际展开、收起逻辑 |
| `IslandFishingController.SaveDirectory` | 无调用的转发属性 | `IslandSaveCoordinator.DirectoryPath` 及全部存档流程 |
| `ComboHudTests.ComboHasSpaceAwayFromTools` 的四组分辨率 | `HudLayoutTests.VisibleControlsFitAndDoNotOverlapAtSupportedWindowSizes` 覆盖同样分辨率、全部边界及更多控件重叠检查 | 布局测试、三项连击动画测试 |
| `scripts/**/__pycache__/*.pyc` 共 18 个文件 | 执行验证时发现已入库的自动生成字节码；均有对应 Python 源码 | 保留源码，补充忽略规则，防止再次入库 |

## 执行步骤

1. 记录清理前的鱼模型、UV、蒙皮、骨骼及全部动作采样输出。
2. 删除上述实现和重复测试，同步鱼资源文档。
3. 对比清理后的 Blender 输出；检查 Python 语法、Unity 编译及相关 EditMode 测试。
4. 运行现有纯模拟回归，进行一次独立代码复核，记录结果与未覆盖范围。

## 边界

- 不修改模拟规则、协议、存档格式、场景、预制体和 `.meta` GUID。
- 不删除 Unity 回调、序列化数据、开发构建显式启用的 smoke 入口和其共用 pilot。
- 不删除仍有断言价值的失败测试；已知角色手臂穿插测试不在本次修复范围。
- 本次无需增加仅验证“符号不存在”的测试；使用现有覆盖及清理前后输出对比。
- 不重跑全仓网络压力 / 四客户端验收；本次未修改相关实现。

验证记录位于本地忽略目录 `artifacts/code-cleanup/`。

## 执行结果

- 删除第一版生成器及专用辅助函数，共减少 331 行 Python；保留的函数和 `Surface` 方法与清理前 AST 一致。
- 删除 3 个无调用的 C# 成员和 4 个重复分辨率测试用例，保留覆盖范围更大的布局测试。
- 删除 18 个已入库的 `.pyc` 缓存，每个文件均保留对应 `.py` 源码；忽略规则检查通过。
- Blender 独立后台生成的 11 种鱼，网格、UV、蒙皮权重、骨骼、贴图哈希与清理前一致；78 个动作各采样 9 个时刻，输出一致（浮点数据比较精度为小数点后 6 位）。未重新导出或替换正式资源。
- 11 个 Blender Python 脚本语法检查通过；纯模拟测试重新编译后 360 / 360 通过。
- Unity 2022.3.62f1 编译通过，HookIsland EditMode 清理前 155 / 157 通过，清理后 151 / 153 通过。移除的正好是 4 个重复用例，其余逐项结果一致，无新增失败；原有粒子 `BakeMesh` API 弃用警告仍保留。
- 两个原有失败仍保留：`FisherAnimationTests.ForearmsAndHandsStayOutsideBodyAcrossCastAndLocomotionBlends`（Hand_L 与 Spine 穿插）；`FishingCameraViewTests.ResetRestoresProjectionAndMouseAfterThirdPerson`（批处理下鼠标锁定状态为 None）。本次不将其当作无用测试删除。
- 本轮未运行完整 `./scripts/verify.sh`、四客户端试玩和发布构建；未修改对应规则、网络、持久化实现或发布入口。
- 独立只读复核通过：核对调用、序列化边界、保留函数 AST、重复测试覆盖及前后验证证据，未发现需要修正的问题。两项既有失败的修复与未执行的全量验收继续按上述边界记录。

## 文件与目录清理

用户后续要求继续剔除无用文件和文件夹，本轮按实际用途处理：

- 删除 `artifacts/` 下过期的阶段性 `.app` 验收包；保留启动脚本默认使用的岛屿、平台、庭院联机包，文档使用的庭院单机包，以及本地最新的平台开发包和发布包。这些是既有历史构建，不表示已由当前源码重新打包。
- 将 Unity 工程外层 `Captures/` 的 19 张验收截图归入本地生成产物：12 张已有完全相同的 `artifacts/` 副本，删除重复文件；7 张独有截图保存在 `artifacts/unity-captures/`，然后移除原 `Captures/` 目录。补充 `/Captures/` 忽略规则；此目录不在 `Assets/` 内，也没有代码或文档读取它。
- 移除空的 `project/`、Python 缓存目录，以及本次删除后变空的输出父目录。
- 正式资源及 Blender 源文件、游戏存档、地图参考页、验收报告和其他独有截图保留。详细文件清单与保护文件哈希见 `artifacts/code-cleanup/file-cleanup-manifest.json`。
- 这一轮只处理运行时资源目录外的文件，无需重复编译、模拟或联机测试；验证启动入口、截图副本、保护文件哈希、资源 GUID 及 Git 差异。
- 完成结果：删除 38 个旧构建包（7,381 个包内文件）、12 张重复截图，另移除 19 个空目录；合计释放约 6.43 GB（5.99 GiB）。6 个保留包的启动文件存在且可执行，19 张截图内容均有保存，867 个保护文件哈希不变，Unity `.meta` 均有对应目标，`git diff --check` 通过。结果见 `artifacts/code-cleanup/file-cleanup-result.json`；本轮没有重新编译或运行游戏测试。

## 深入清理：保留两套原型，仅移除失效内容

用户确认庭院射击示例与固定小岛旧玩法都要保留。基于 181 个 Unity C# 文件的语义引用检查、资源 GUID 引用及导出管线调用，追加处理如下：

| 删除内容 | 判定依据及保留边界 |
| --- | --- |
| 8 个无效 C# 成员 | 无使用者的 `Ak47View.IsFlashing`、`FirstPersonMotor.View`、控制器 `TogglePerspective` 转发方法、`MerchantSmokePilot.Arrival`；只写不读的 `FishingPresentation.camera`、`white`、`ComboHudAnimation.observed`、装备页 `body`。保留实际相机输入、枪口表现、商人验证和连击动画 |
| 表现层多余相机参数及测试装配 | 更新 8 个构造调用；移除 3 个不再需要的测试相机，保留水粒子烘焙、相机操作及敌人表现需要的相机。未删除测试用例或断言 |
| 旧鱼类造型参数 | 移除会被参考轮廓覆盖的 `SLEEK` / `ROUND`、初始 profile，以及没有读取者的旧配色、鳍型、表情等字段；保留鱼种、长度、动作清单及河鲀鼓起参数。移除新版生成器的无用导入、局部变量与参数 |
| 旧小岛源文件与预览 | 当前批准源文件是 v2；删除 v1 `.blend`、v1 检查记录、7 张 v1 预览和 v2 第一轮预览。保留 v2 源文件、最终两张预览、参考输入、全部建模 / 检查 / 导出脚本与 Unity 固定小岛场景 |
| Unity 内的重复导出清单 | `Art/Models/export_manifest.json` 与 `Exports/` 原件完全相同，无代码或 GUID 使用者；删除副本和对应 `.meta`，导入工具不再复制该副本。原件继续用于岸线贴图和地图生成 |

这一轮新增删除 12 个文件，共 13,059,618 字节（约 13.06 MB），同步修改 Blender README 和精确忽略规则，避免旧阶段产物重新入库。未删除通用帧同步、当前地图、存档兼容实现和仍有效的开发验证入口。

验证证据位于 `artifacts/deep-cleanup/`：

- 7 个 Unity 项目程序集按各自源文件、条件编译符号、C# 9 和实际 Unity 引用，使用 Roslyn 分别编译并输出临时 DLL，全部成功；未替换 Unity 的程序集。现有序列化字段未赋值、粒子 API 弃用警告仍存在，独立编译测试程序集另有 NUnit 旧 mscorlib 版本匹配警告。
- 纯模拟回归重新编译运行，360 / 360 通过。
- 11 种鱼的模型、UV、蒙皮、骨架和贴图结果与清理前完全一致；78 个动作各采样 9 个时刻，浮点输出按小数点后 6 位比较一致。
- 导入工具清理前后在独立输出目录执行，14 个正式模型 / 贴图（包括岸线距离贴图）字节一致，只少了无用清单副本；21 个 Python 脚本语法检查通过。
- Blender 后台只读打开保留的 v2 源文件成功，279 个对象、266 个网格、12,422 个多边形及 7 个分类集合与现有记录一致。
- 739 个保留资源 / 框架文件的哈希未变；框架、模拟、联机与适配器目录没有改动。删除清单 GUID 无引用、Unity 无孤立 `.meta`、文档链接及忽略规则检查通过。
- 当前 Unity 工程已被编辑器打开，独立批处理被工程占用检查终止，因此本轮未重跑 Unity EditMode、四客户端实机或发布构建。上一轮的两个既有 EditMode 失败记录仍保留，不以本轮编译成功代替运行结果。
