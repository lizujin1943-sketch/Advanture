# 深渊凝视者：第一版动作预览

乌贼式头型已获用户确认。当前交付 Blender 动作和实际渲染预览；本轮不制作抓取、卷起、举起或抛掷玩家。

## 打开与播放

用 Blender 5.2 兼容版本打开 `AbyssalWatcher.blend`，在视图区按空格播放 `AW_Showcase` 合集。制作时间线为 30 帧/秒，与游戏帧同步逻辑帧无关。模型正面 -Y、Z 向上、一单位一米。

`AbyssalWatcher_ModelApproved.blend` 是不含动作的已确认静态基线；动画制作从该文件读取，不重新生成造型。修改模型前请保留这一基线。

| 内容 | 位置或名称 |
| --- | --- |
| 23 个基础动作 | 登场、普通/狂暴待机、四腕各自下砸与横扫、喷墨、受击、进入狂暴、八腕断离、死亡 |
| 组合演示 | `AW_Combination`：两条攻击腕加喷墨 |
| 34 秒展示合集 | `AW_Showcase`，包含残缺状态下的死亡 |
| 动作和事件清单 | `exports/animation-manifest.json`，帧范围、秒数、循环、所属骨骼与关键时刻 |
| 模型集合 | `01_AbyssalWatcher_EXPORT` |
| 独立断肢 | `04_DetachedLimbs_EXPORT`，八个 `AW_Detached_<编号>` 骨架及网格 |
| 展示环境 | `02_Sea_and_scale_reference`、`03_Arena_validation_12_to_20m` |
| 喷墨占位预览 | `05_AnimationPreview_ONLY`，不属于正式 BOSS 导出 |

要切换单个片段，可在 Blender 的 Python Console 执行：

```python
import sys
sys.path.insert(0, '/Users/lizujin/Documents/project/git_project/Advanture/scripts/blender')
from animate_abyssal_watcher import activate_clip, set_limb_present
activate_clip('AW_Slam_A01')
```

其他示例：`AW_Sweep_A04`、`AW_Ink`、`AW_Sever_A02`、`AW_Death`、`AW_Showcase`。这个入口会同时切换断肢副本、设置播放范围和事件标记；仅在 Action 下拉框更换动作无法自动完成这些辅助步骤。

残缺状态的预览：`set_limb_present('A02', False)`，然后 `activate_clip('AW_Death', preserve_missing=True)`。`present_*` 表示腕是否存在，任何动作都不写入该状态；`sever_*` 只用于本轮固定参考姿态断离演示。以后游戏中应在断裂瞬间捕获当前腕姿态，再独立播放落水过程。

## 当前头型：乌贼式长囊

按用户“先做精模”“头、眼、口不自然”“太圆”“头型更像乌贼”的意见，头冠改为向后上方延伸、两侧略扁、末端收细的长囊。减少人脸式额头与横眉，浅褶沿纵向延伸，头冠边缘保留窄软缘；三眼、放射状口器、偏侧礁壳和八腕仍保留。

外形借鉴乌贼外套膜及沿侧面的软缘，参考 [Monterey Bay Aquarium 的乌贼资料](https://www.montereybayaquarium.org/animals-the-ocean/animals-a-to-z/common-cuttlefish)。这是幻想 BOSS 的造型借鉴，不是乌贼解剖复原。

头皮、眼窝、眼睑和口周由同一连续曲面塑形；眼面跟随头冠倾斜，口腔按相同边界拆成可编辑分件。口内纵深约 **2.86 米**，十二片厚肉质齿瓣向内部收拢。侧面的礁壳与藤壶跟随实际头冠表面定位，避免改头型后仍停留在旧球体的位置。

口部形态仍保留设计中的放射状口器，可作为后续张口喷墨的造型基础。本轮已增加张口喷墨预览，游戏规则另行接入。

当前头部主体约 19.4 万顶点，面数优先满足造型检查。皮肤细粒包含 Blender 材质节点，口内渐变使用 `TissueWeight` 顶点数据；尚未进行游戏低模重拓扑、细节烘焙或性能优化。动画使用眼睑形态键、角膜退入和口器骨骼驱动，游戏导出时需另行烘焙或转换。

## 验证与范围

重新打开最终动画源文件后，23 个基础片段、循环首尾、单腕分区、最大覆盖、八腕独立断离、残缺死亡及贴图路径检查通过。连续眼光/眨眼、合集切段及完整攻击过程的截面旋转也通过复验；独立复核未发现剩余重要问题。静态基线另行检查八腕结构、权重、头口共用边界和角膜法线。结果分别在仓库 `artifacts/abyssal-watcher/animation-verification.json` 与 `structure-verification.json`。

实际关键帧在 `artifacts/abyssal-watcher/animation-stills/`，合集视频为 `artifacts/abyssal-watcher/animation-preview.mp4`（34 秒、15 帧/秒，制作源动画仍为 30 帧/秒），已完整解码 510 帧，并提取 12 个时刻检查整体过程。动作时序见仓库 `docs/concepts/abyssal-watcher/animation-design.md`。

本轮不包括正式伤害、平台破坏、墨汁危险区、运行时断肢捕获、FBX 导出及 Unity 接入。喷墨是方向和启停示意，尚非正式特效。高精度网格、Blender 驱动与材质节点仍需后续游戏资产优化与转换。没有修改玩法或帧同步代码，因此未运行 .NET/UDP 全量测试。

## 重新制作和验证

在仓库根目录执行：

```sh
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python-exit-code 1 --python scripts/blender/animate_abyssal_watcher.py
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python-exit-code 1 --python scripts/blender/verify_abyssal_animations.py
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python-exit-code 1 --python scripts/blender/verify_abyssal_watcher.py
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python-exit-code 1 --python scripts/blender/render_abyssal_animations.py
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python-exit-code 1 --python scripts/blender/render_abyssal_animations.py -- --movie
```

视频编码使用 `python3 scripts/blender/encode_abyssal_preview.py`，需要 `ffmpeg`，或在本地 `artifacts/abyssal-watcher/python-tools` 安装 `imageio-ffmpeg`。编码后完整解码验证，报告为 `video-verification.json`。

`animate_abyssal_watcher.py` 会覆盖动画文件及清单，保留静态基线；手工修改过的动画应先另存。原 `create_abyssal_watcher.py` 是静态模型生成入口，会覆盖主模型，不用于日常重做动画。贴图在 `textures/`，移动资产时整体复制目录。
