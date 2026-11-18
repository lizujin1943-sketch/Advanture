# 带翼克苏鲁：空间重建、动画和游戏资源

原始概念图见 `ref/user-reference.png`；推断的正、侧、后、俯视设计见 `ref/inferred-turnaround-v1.png`。后续用用户提供的 Tripo 正面、正侧、背面和顶视截图校准了头壳长度、八腕的分布和遮挡。隐藏的腕根、背部色斑及翼根细节仍有推断。

## 可编辑文件

- `CR_WingedCthulhu_clay_review.blend` 是本轮供选型的独立静态灰模。以原画及用户提供的 Tripo 正、侧、背、俯视截图为参考，重做了中央卷腕与邻腕的遮挡、后垂收尖的头壳，以及眉弓到额头和口须的过渡。打开后从肖像机位开始，可切换 `Clay_side`、`Clay_top`、`Clay_roots` 等相机。正反对照和眉眼近景在 [灰模审图目录](review/clay-revision/)。以后台 Blender 打开同目录的 `CR_WingedCthulhu_radial.blend`，运行 `scripts/blender/sculpt_winged_cthulhu_review.py` 可重建这份灰模和各视角。游戏网格已按这套腕形与头形重新绑定和采样。
- `CR_WingedCthulhu_radial.blend`：带程序材质和参考色素纹理的高模，八条主触腕围绕身体分布。
- `CR_WingedCthulhu_animated.blend`：游戏网格、唯一 UV、打包贴图、完整骨架和 24 个动作。Blender 5.3.0，米制，Z 向上、-Y 向前。
- `textures/`：高模绘制纹理；`runtime-textures/`：Unity 所用的烘焙 BaseColor / Roughness / Normal。
- `exports/unity/`：FBX、动作/碰撞清单、毫米整数采样与导出回读报告。
- `review/`：当前多角度实模、动作极端姿态、游戏网格检查及 Unity 场景截图。`silhouette-fit-field.npz` 是早期正面比例校正场，只用于翼部的轮廓校正，头身和口须保留三维空间的真实中线。

本轮灰模的主视角对照使用相同的相机、灯光与灰色材质。八条主腕仍按四前四后连接，闭合处的 320 对边缘点和双眼镜像在重读文件后复查。检查范围是静态高模，用户截图用于比对可见轮廓，遮挡下的腕路仍有推断。全模超出自动共面与悬空扫描的三角面预算，这两项采用多视角人工审图。

主触腕为 A01–A04（四条前侧攻击腕）与 S01–S04（四条后侧支撑腕）；角度依次为 -65、-24、24、65、-115、-158、158、115 度。腕根半径比旧版缩小 35%，逐渐过渡到未改动的腕尖。头壳到腕冠由同一张连续表面构成，左右眼和眼眶共用中线镜像。修改前后的同机位对比见 `review/anatomy-revision.jpg`。八条口须是独立的面部结构，不计入八条主腕。吸盘沿连续腹面布置；背面有真实腕根、触腕和吸盘。

本次把头下皮肤直接做成向八个方向分叉的腕冠，相邻腕之间共用浅蹼。断离面移到腕身，每条腕和身体在断离面处共用位置一致的 40 个采样点和同一套蒙皮权重。断后浅蹼留在身体上。见 `review/crown-revision.jpg`（背面同机位）、`review/crown-poses.jpg`（待机、下砸和断离）。

游戏可见主体 155,006 三角面，计入八个隐藏断腕残端共 162,686 三角面，使用 219 根骨骼和 20 个独立蒙皮对象。头身与腕各用 4K 贴图组，双翼用 2K 贴图组。眼睛及残端使用常量材质。运行时不依赖 Blender 程序节点；近共面接触在烘焙前通过最多约毫米级的局部几何错层修正，记录在 `runtime-geometry.json`。

原画机位的中央腕从面须后向前伸，下缘向内回卷。两侧前腕的吸盘沿弯曲内侧逐渐显露，后腕在另一层展开。后脑沿身体中线拉长、下垂并收尖，双眼以中线为基准对称。后脑实际后缘参与头部命中盒计算。原画、Tripo 截图、游戏灰模和烘焙后的正面见[同机位对照](review/production-comparison.jpg)，侧面见[游戏网格侧视](review/game-profile.png)。

侧视修正集中在翼根后方。以绑定姿态的游戏网格测量，沿身体中线在 Y = 3.5 m 处向上、向下求交，头壳截面厚度从 2.79 m 增至 3.65 m。下缘和后尖保留原位，上缘在翼根后延续弧度再向尾端收束。[用户截图、Tripo 和两版游戏模型的侧视对照](review/mantle-profile-comparison.jpg)。

## 动作和游戏接口

24 个动作保留现有 AW_ 名称：出现、待机、四路下砸、四路横扫、喷墨、受击、狂暴、狂暴待机、八路断肢、死亡和组合预览。下砸与横扫先由腕根带动，梢端依次展开并跟随回收。喷墨时口须围绕新的面部中线张开。翅膀和口须有延迟次级运动，待机与狂暴待机循环端点一致。`SOCKET_Ink` 随头部移动。

Unity 使用确认帧按 30 Hz 直接采样，Animator 不自主推进。各攻击腕只覆盖自己的骨链，断肢网格可单独关闭。碰撞采样与同一骨架同一动作一起生成，发布时校验源文件、FBX、检查报告的哈希；替换 `BossCollisions.bin.gz` 后同步内容哈希，防止旧新资产的客户端混合联机。

Unity 编辑器入口：`Tools → Hook Island → Install Winged Cthulhu Boss Art`。替换已有 `BossSettings` 的模型和动作，保留玩法参数。`Capture Winged Cthulhu In Town` 在临时表现对象上采样并截图，不写权威状态、不保存场景改动。最终验证记录见仓库 `docs/winged-cthulhu-boss-replacement.md`。

## 重建

从仓库根目录，用独立后台 Blender 运行；每步输入都是上一步输出：

```sh
BLENDER=/Applications/Blender.app/Contents/MacOS/Blender
ASSET=source-assets/creatures/winged-cthulhu
"$BLENDER" --background "$ASSET/CR_WingedCthulhu_radial.blend" --python-exit-code 1 --python scripts/blender/sculpt_winged_cthulhu_review.py -- --production
"$BLENDER" --background artifacts/cthulhu-production/painted-sculpt.blend --python-exit-code 1 --python scripts/blender/prepare_winged_cthulhu_game.py
"$BLENDER" --background "$ASSET/CR_WingedCthulhu_game_mesh.blend" --python-exit-code 1 --python scripts/blender/animate_winged_cthulhu.py
"$BLENDER" --background "$ASSET/CR_WingedCthulhu_animated.blend" --python-exit-code 1 --python scripts/blender/verify_winged_cthulhu_game.py
"$BLENDER" --background "$ASSET/CR_WingedCthulhu_animated.blend" --python-exit-code 1 --python scripts/blender/export_winged_cthulhu.py -- export
"$BLENDER" --background --factory-startup --python-exit-code 1 --python scripts/blender/export_winged_cthulhu.py -- verify
"$BLENDER" --background --factory-startup --python-exit-code 1 --python scripts/blender/export_winged_cthulhu.py -- publish
./scripts/sync-hook-island.sh
```

检查脚本使用本机安装的 `blender-asset-qa` 技能；发布须先有匹配当前文件哈希的网格检查和 FBX 回读报告。更换工作站时调整该技能路径。建模、动画、烘焙和导出不需要付费生成服务。

早期来源链仍保留脚本：`create_winged_cthulhu.py` → `apply_winged_cthulhu_fit.py` → `paint_winged_cthulhu.py` → `rebuild_winged_cthulhu_radial.py`。早期 master/textured 与无骨架 game_mesh 是可再生成的本地中间文件，不提交；最终高模和动画源文件提交。旧正面图没有作为当前还原度证据。

## 保留的边界

画面之外的形体结合 Tripo 多视角校准。正面对照仍能看出原画的口须穿插更密、翼膜的垂坠更明显，背面斑纹依据截图推断。减面使用轮廓误差约束，不是人工全四边面重拓扑；暂无 LOD 和移动端性能验收。头部使用简化代理，触腕使用保守胶囊采样，双眼沿用现有的一个整体弱点接口。工作台动作视频用于检查变形；最终材质以真实渲染和 Unity 截图为准。
