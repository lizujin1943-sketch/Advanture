# 深渊凝视者 Unity 美术与确定性碰撞导出

正式来源为 `AbyssalWatcher.blend`；已批准的静态对照为 `AbyssalWatcher_ModelApproved.blend`。导出脚本只读这两个文件，生成前后比较 SHA256，不保存源模型。不包含抓取、举起、抛掷玩家动作，也不包含海面、舞台、比例参照、断肢预览副本或占位墨汁。

## 运行与接入

在正式仓库 `/Users/lizujin/Documents/project/git_project/Advanture` 执行：

```sh
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python scripts/blender/export_abyssal_unity.py
```

可分步使用 `-- --collision-only` 先产出碰撞和姿态缓存，随后 `-- --geometry` 继续生成模型。`-- --verify` 只重新导入 FBX 核对；`-- --face-manifest-only` 仅更新 Unity 所需表情采样。源文件变化时不允许旧采样与新模型混用。

Unity 编辑器入口是 `Advanture.HookIsland.Editor.AbyssalWatcherArtInstaller.Install()`。已有模型只需重新生成动作时使用 `RefreshAnimationClips()`；后者不会重导 FBX 和贴图。两个入口只写本目录对应的 Art 资产，不写地图或玩法配置。

正式 Unity 路径：

- `Assets/HookIsland/Art/AbyssalWatcher/AbyssalWatcher.prefab`
- `Assets/HookIsland/Art/AbyssalWatcher/AbyssalWatcher.fbx`
- `Assets/HookIsland/Art/AbyssalWatcher/Animations/AW_*.anim`
- `Assets/HookIsland/Art/AbyssalWatcher/Materials/AW_*.mat`
- `Assets/HookIsland/Art/AbyssalWatcher/Textures/`

顶层预制体为单位变换，含 `Model` 子物体；`Animator` 在 `Model` 上，默认关闭自动播放及根运动。游戏使用 `GetComponentInChildren<Animator>()` 获取采样目标，对其 `gameObject` 以模拟时间调用 `AnimationClip.SampleAnimation`。每个 `.anim` 已剥离非所属骨骼的曲线，可先采样待机，再覆盖单腕或头部动作。原始 FBX 内部的全部骨骼曲线用于交换与验证，游戏应使用提取后的 `.anim`。

## 坐标和骨架

- Unity 本地坐标 = Blender `(x, z, y)`；Unity `+Y` 向上、`-Z` 为正面；1 单位 = 1 米。
- 原点是海面中心。世界位置由调用方设置，碰撞采样没有加入 `(0, 0, 28)` 等场景偏移。
- Unity 导入后实际比例校正为 1、旋转校正为 `(0,0,0)`；Installer 仍会按实际导入骨架检验单位与左右方向。
- 根路径为 `Model/AW_Rig/ROOT`。`ROOT` 只在 `AW_Emerge` 与 `AW_Death` 中上下位移。
- 8 个腕控制根：`CTRL_A01` 至 `CTRL_A04`，`CTRL_S01` 至 `CTRL_S04`，均在 `ROOT` 下。
- 攻击腕每腕 24 个 `DEF_A01_00..23` 等变形骨；支撑腕每腕 12 个 `DEF_S01_00..11` 等变形骨。
- **导出骨架中每腕各节直接挂在同腕 `CTRL_<id>` 下，是同级骨。** 源骨架使用 Blender 特有的“不继承缩放”，FBX 常规父链会产生累积剪切；这个游戏用层级保留原骨名、绑定矩阵、蒙皮权重与每帧实际位置，避免该偏差。不要从 `DEF_<id>_00` 递归收集整腕，按前缀或 `CTRL_<id>` 收集。
- 头部是 `DEF_Body/DEF_Head`；眼骨为 `DEF_Eye_Center/L/R`，口瓣骨为 `DEF_Petal_00..11`。喷墨插槽 `SOCKET_Ink` 保留。4 个旧 `SOCKET_Grip_*` 已排除。
- 可独立控制的网格：`AW_Arm_<id>`、`AW_Membrane_<id>`、`AW_Stump_<id>`。动画从不写显示开关，断肢是否存在由游戏状态决定。

## 动作与表情

所有动作 30 Hz，帧数包含首尾帧；零起始时间为 `(sourceFrame - 1) / 30`。

| 动作 | 数量 | 秒数 | 帧数 | 主要所有权 |
| --- | ---: | ---: | ---: | --- |
| `AW_Emerge` | 1 | 4 | 121 | 全身、ROOT、表情 |
| `AW_Idle` | 1 | 6 | 181 | 全身、表情；循环 |
| `AW_Slam_A01..04` | 4 | 3 | 91 | 对应单腕 |
| `AW_Sweep_A01..04` | 4 | 3.6 | 109 | 对应单腕 |
| `AW_Ink` | 1 | 3.2 | 97 | 头、眼、口、表情 |
| `AW_Hit` | 1 | 1 | 31 | 头、眼、口、表情 |
| `AW_Enrage` | 1 | 3 | 91 | 全身、表情 |
| `AW_EnragedIdle` | 1 | 5 | 151 | 全身、表情；循环 |
| `AW_Sever_A01..04/S01..04` | 8 | 2.6 | 79 | 对应单腕；显示开关由玩法决定 |
| `AW_Death` | 1 | 6 | 181 | 全身、ROOT、表情 |
| `AW_Combination` | 1 | 3.6 | 109 | A01、A03、头部组合示例 |

总共 24 个。精确骨骼所有权、预警/命中等美术标记保存在 `unity-manifest.json`；标记不触发伤害。姿态曲线冗余点按最大 `0.000005` 数值误差去除，保持 30 Hz 样本的表现精度。

`AW_Head` 形态键为 `Ink_Aperture` 与 `Close_Center/L/R`；`AW_MouthCavity` 有 `Ink_Aperture`。FBX 存完整形态几何，但当前 Blender FBX 导出器会将 driver 权重采为常量，因此 **Unity Installer 从清单中的 `mouthOpen/blinkCenter/blinkL/blinkR` 原始逐帧值显式写入 `.anim` 的 0–100 形态权重**。不要直接使用原 FBX 形态权重曲线。眼睛亮度也由源 `eyeGlow` 数组生成材质动画。眼骨退入眼睑和口瓣移动已正常烘在 FBX 骨骼曲线中。

## 模型和材质

模型含 44 个蒙皮网格、185 个骨骼、138,677 个顶点、272,518 个三角面。原头部从 194,341 顶点简化到 48,656，保留连续口腔、三只眼和向后扫出的鱿鱼式头壳。头部抽样表面距离误差最大约 1.27 毫米；腕皮/吸盘约 8.33 毫米；礁石附件约 11.96 毫米。

材质按 `unity-manifest.json` 每个网格的 `materials` 列表明确绑定。`AbyssalSurface.shader` 使用原外部颜色、粗糙度、法线和发光贴图；`AW_FaceSkin` 读取原 `TissueWeight` 顶点属性还原连续口内渐变，避免多边形色块边界。原 Blender 的亚毫米程序颗粒用法线贴图近似；未逐项重建 Blender 的次表面散射。脸部权重按线性数据导出，不当作 sRGB 显示色。

## 确定性碰撞数据

`collision-manifest.json` 定义索引；两个二进制文件均为无头、小端有符号 int32。所有坐标和半径使用整数毫米。

`collision-joints.bin`：帧优先 → 腕优先 → 点优先，每点 `(x,y,z,radiusMm)`。腕顺序固定 `A01,A02,A03,A04,S01,S02,S03,S04`，攻击腕 25 点、支撑腕 13 点，共 152 点，帧跨度 2,432 字节。每片段的 `offsetBytes` 和 `frameCount` 在清单中。每点是各骨 head，最后一点为末骨 tail；相邻点定义胶囊段。半径按源截面半径 ×1.18 近似覆盖皮肤与吸盘。

`collision-body.bin`：每帧 40 字节，10 个 int32：`headMinX,headMinY,headMinZ,headMaxX,headMaxY,headMaxZ,eyeX,eyeY,eyeZ,eyeRadius`。`bodyOffsetBytes` 在各片段清单中。

Idle 第 0 帧头部范围为 `[-3621,-600,-2288]..[3516,10000,5311]` mm；中央眼球代理为中心 `[0,6120,-1639]`、半径 `940` mm。每帧数据会随出场、死亡和眼骨移动。碰撞是保守代理，不是逐三角形检测。

攻击方位从 `-Z` 向 `+X` 计：A01 −65°、A02 −24°、A03 +24°、A04 +65°。下砸 impact 第 45 个零起始帧的末端分别约为 `(-19.395,.55,-9.044)`、`(-8.704,.55,-19.550)`、`(8.704,.55,-19.550)`、`(19.395,.55,-9.044)` 米。不要把卷曲待机的末端方向当作预警扇区方向。

## 验证边界

`unity-export-verification.json` 是后台 Blender 的独立 FBX 回读报告：24 个片段均抽样，腕骨 head 最大坐标偏差 1 mm；显式源形态值应用后头部边界差约 2.78 mm、眼中心差约 0.502 mm；所有网格保留蒙皮、头部保留 TissueWeight 和 4 个表情形态，源文件哈希不变。

Unity Installer 会另外生成 Art 目录的 `unity-import-report.json`，检验实际导入后的单位、前向、左右、腕骨对照以及生成后的表情曲线。最终游戏视角、实际材质观感和完整战斗玩法由主任务在 Unity 场景中验收；本导出脚本不将后台回读当成游戏验收。
