# Fortified Compound — Blender 重建

主文件：[`EN_FortifiedCompound.blend`](EN_FortifiedCompound.blend)。当前活动场景为 `EN_FortifiedCompound`，Cycles / Metal、AgX、3840 × 2160、透明背景。保留 Blender 原始启动场景，不在其中修改用户对象。

这是实际网格组成的可编辑渲染场景；没有下载、提取或导入 Tripo 网页模型，也没有把原图当作朝相机的平面替代几何。网页只用于多视角观察。参考图被打包为可选相机背景，默认关闭，不参与渲染。

## 仓库与 B-2 美术精修版

[打开精修版 Blender 场景](EN_FortifiedCompound_Art.blend)　·　[4K 透明底全图](../../../artifacts/fortified-compound-artpass-20261120/compound-4k.png)　·　[四栋建筑近景](../../../artifacts/fortified-compound-artpass-20261120/building-details.jpg)　·　[初版与精修版同机位对照](../../../artifacts/fortified-compound-artpass-20261120/building-comparison.jpg)

三个半开放仓库共用混凝土、金属托梁和修补屋面的构造。军火库用低矮叠箱组织库存，长箱上平放一件展示道具。北侧出口与库内通道由一段缓坡连接。装备库放置了一套悬挂防具和打包台。医疗库的周转箱、墙架和急救台按高度分层。两个北侧仓库的卸货坡面嵌在地坪内，库门与广场之间留有转向空间。

B-2 使用 160 块复合板拼成通信罩，下部有法兰、设备带、百叶和采光窗。屋顶设有检修口，入口雨篷覆盖四级门前台阶。混凝土、沥青和铺装采用按实际场景尺寸投射的贴图，广场的板缝顺着原有圆角收边。

`FC_91_Architecture_Cameras` 集合保存四台建筑相机。主相机仍为 `CAM_Reference`，可直接按 F12 渲染 3840 × 2160 透明 PNG。要重建这版，请从仓库根目录用 Blender 加载初版，并执行下面的脚本。重建默认写入 `EN_FortifiedCompound_Art.blend`，命令末尾可加 `-- 新文件名.blend` 指定其他输出。

```sh
blender -b source-assets/environments/fortified-compound/EN_FortifiedCompound.blend --python-exit-code 1 -P source-assets/environments/fortified-compound/build/artpass/build.py
```

实模验收覆盖仓库装卸口、军火库北侧通道和 B-2 入口，共采样 2,268 处，并复查了 A-1、B-1、C-1 屋顶楼梯及广场入口。分栋检查记录中包含每个构件的三角面数。为便于编辑，贴合的分件保留内部接触面，地形底面在地坪下 0.40 米。Unity 碰撞、LOD 与目标设备性能仍待引擎验收。[仓库与 B-2 通行记录](../../../artifacts/fortified-compound-artpass-20261120/checks/warehouse-and-b2-access.json) · [四栋网格检查](../../../artifacts/fortified-compound-artpass-20261120/checks/summary.json) · [屋顶楼梯](../../../artifacts/fortified-compound-artpass-20261120/checks/roof-access.json) · [广场和圆角](../../../artifacts/fortified-compound-artpass-20261120/checks/plaza-and-corners.json)

精修版内嵌了四组 CC0 扫描材质的颜色、粗糙度与高度图。沥青由 Jenelle van Heerden、Sergej Majboroda 制作，墙面由 Charlotte Baglioni 制作，磨损地坪与预制混凝土由 Dimitrios Savva、Rico Cilliers 制作。[Asphalt 04](https://polyhaven.com/a/asphalt_04) · [Concrete Wall 009](https://polyhaven.com/a/concrete_wall_009) · [Concrete Floor Worn 001](https://polyhaven.com/a/concrete_floor_worn_001) · [Preconcrete Wall 001](https://polyhaven.com/a/preconcrete_wall_001) · [Poly Haven 许可](https://polyhaven.com/license)

## 交付

- [4K 透明底实模渲染](../../../artifacts/fortified-compound-rounded-corners-20261120/fortified-compound-4k.png)
- [建筑与广场圆角的同机位对照](../../../artifacts/fortified-compound-rounded-corners-20261120/corner-comparison.jpg)
- [中央广场全貌](../../../artifacts/fortified-compound-rounded-corners-20261120/plaza-overview.png) · [A-1 屋顶入口](../../../artifacts/fortified-compound-rounded-corners-20261120/a1-after.png) · [B-1 屋顶入口](../../../artifacts/fortified-compound-rounded-corners-20261120/b1-after.png)
- [C-1 楼梯中间转角和屋顶出口修前、修后对照](../../../artifacts/fortified-compound-stairs-20261120/stair-comparison.jpg)
- [A-1、B-1、C-1 逐级通行面检查](../../../artifacts/fortified-compound-rounded-corners-20261120/roof-access-check.json)
- [建筑轮廓与广场三个入口的实模测量](../../../artifacts/fortified-compound-rounded-corners-20261120/corner-check.json)
- [参考图与最终渲染对比](../../../artifacts/fortified-compound-reconstruction-20261119/reference-comparison.jpg)
- [俯视](../../../artifacts/fortified-compound-rounded-corners-20261120/view-top.png) · [正面](../../../artifacts/fortified-compound-rounded-corners-20261120/view-front.png) · [侧面](../../../artifacts/fortified-compound-rounded-corners-20261120/view-right.png) · [背面](../../../artifacts/fortified-compound-rounded-corners-20261120/view-back.png) · [三分之四视角](../../../artifacts/fortified-compound-rounded-corners-20261120/view-three-quarter.png)
- [C-1 屋顶通路近景](../../../artifacts/fortified-compound-rounded-corners-20261120/detail-c1-access.png)
- [功能库近景](../../../artifacts/fortified-compound-rounded-corners-20261120/warehouse-detail.png)
- [制作说明与推断边界](asset-brief.md)
- [建模前多视角结构图](ref/multiview-spec.svg)
- [完整迭代和检查记录](../../../artifacts/fortified-compound-reconstruction-20261119/REVIEW.md)

## 编辑

集合按地形、周界、广场、A-1、B-1、C-1、B-2、装备库、医疗库、军火库、小棚、破损、树木和表面细节拆分。`FC_90_Inspection_Cameras` 中包含正面、背面、左右、俯视、三分之四和细部相机。主相机为 `CAM_Reference`。

材料均为 Blender 节点，混凝土的纹理与轻微凹凸为程序化生成。主参考图和标识字体已打包。尺寸为参考推断的工作尺度，非测量结果；隐藏的背墙和承重构造按结构逻辑补全。

## 重建和渲染

在 Blender Python Console 中运行：

```python
import runpy
ns = runpy.run_path('/Users/lizujin/Documents/project/git_project/Advanture/source-assets/environments/fortified-compound/build/compound.py')
ns['build'](True)
```

这会重新生成本任务拥有的场景及对象；编辑前请另存手动修改的版本。`compound.py` 管理基础结构，`details.py` 管理构件，`refine.py` 记录对照修正，`finish.py` 管理交付相机和配置。`render_delivery.py` 输出 4K 与多视图，`proof.py` 制作真实渲染对照图。重建脚本目前使用本机 DIN Condensed 字体路径；已保存的 blend 包含字体，可直接打开渲染。

## 验收范围

建筑墙身、屋檐及女儿墙共用转角圆心。广场台身和铺装的四个外角半径均为 2.32 米。楼梯平台和扶手采用较小的圆弧，广场东西两侧的上平台与南侧末级踏步按铺装顶面收口。生成逻辑见 [architectural_corners.py](build/architectural_corners.py)。[check_corners.py](build/check_corners.py) 对保存的实模检查八个轮廓和广场三处通行接缝。

本轮检查覆盖 363 件相关构件，合计 200,604 个求值后三角面。检查器报告零处表面重叠闪烁、悬空或退化面。为便于单独编辑，屋面与女儿墙等分件保留相接的内部底面和顶面。地形底板向地面下延伸 0.40 米。这两类构造对应报告中的提示。每件构件的三角面数和检查结果见[网格检查原始记录](../../../artifacts/fortified-compound-rounded-corners-20261120/mesh-check.json)。

11 月 20 日更新了三座建筑的屋顶楼梯。每套踏步和水平转角共用一张封闭网格，扶手沿两侧转入女儿墙端柱。`build/stair_access.py` 生成这些构件。`build/check_stairs.py` 在实际求值网格上沿九条线逐级检查踏面、转角和屋顶门槛，包括 2.05 米的上方空间。这次检查覆盖 Blender 几何，角色碰撞仍属后续引擎接入范围。

已进行八轮可追溯的渲染对照，补齐三维背面并检查了楼梯、平台和围墙衔接。最终 alpha 轮廓 IoU 为 **0.9271**，仅表示轮廓重叠，不是整个画面的还原百分比。旧化纹理、破口碎块、部分支柱和道具仍与原图不同，不能称为逐像素复刻。

本次是渲染版环境，不包含游戏碰撞、LOD、烘焙 UV、光照贴图或 Unity 验证。基础网格审查没有悬空构件、反面封闭体、退化面或游离顶点；通用检查器仍报告若干构件内部共面接触，原始报告保留，未把它声明为全项通过或游戏发布网格。
