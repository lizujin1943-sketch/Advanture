# AK-47 游戏外观

`AK47.blend` 是可编辑母本，采用本项目原创的核桃木、发蓝钢与帆布设计。文件内按动画节点分集合保存独立的外观零件，三张贴图已打包。母本默认用接近侧面的浅三分之四视角展示枪身。要查看手部，在当前视图层的大纲视图中展开场景集合，通过眼睛图标显示 Art | FiringHand 与 Art | SupportHand，或显示闭合姿势 Art | SupportCarry。手套掌心和五指形成连续表面，袖口、护片与前臂分别可编辑。

资源目录位于 `samples/TacticalGame.Unity/Assets/TacticalGame/Resources/Weapons/AK47/`。第一人称 `AK47_View.fbx` 带向肩部延伸的完整衣袖，远端 `AK47_World.fbx` 使用简化网格与短袖口。每份模型有七个网格组，托持与抓握两种左手姿势共用手腕原点，运行时择一显示，每帧显示六组。各组共享 `AK47_Surface`，每组只有一个材质槽。

| 贴图 | Unity 读法 |
| --- | --- |
| `AK47_BaseColor.png` | sRGB 基色 |
| `AK47_MetalSmooth.png` | 线性数据，R 为金属度，A 为光滑度 |
| `AK47_Normal.png` | 切线空间法线，绿色通道为 +Y |

三张贴图均为 2048 × 2048。木托和护木使用相互独立、沿着轮廓布置的木纹区域，钢板、压筋、缝制细节则复用各自的饰条区域，UV 的局部重叠是这一材质布局的一部分。

## 复现

在仓库根目录执行下列命令。美术源已用 Blender 5.3、Python 3、NumPy 和 Pillow 生成验证。macOS 的 Blender 可执行文件通常位于 `/Applications/Blender.app/Contents/MacOS/Blender`。

```sh
python3 source-assets/props/ak47/studies.py
python3 source-assets/props/ak47/paint_atlas.py
blender -b --factory-startup --python-exit-code 1 -P source-assets/props/ak47/build.py
blender -b --factory-startup --python-exit-code 1 -P source-assets/props/ak47/review.py
python3 source-assets/props/ak47/proof_board.py
```

`build.py` 在导出阶段适配本工程启用 Bake Axis Conversion 的 ModelImporter，让 Unity 模型的前方沿 +Z、上方沿 +Y。弹匣以连接处为原点，左右手以手腕为原点。`Muzzle` 供视听效果定位。`SupportHand` 对应托持姿势，`SupportCarry` 对应闭合手势。在道具的绑定姿势下，`CarrySocket` 标记闭合手势的手腕位置，后续相对运动由表现层驱动。

`review.py` 在独立场景回读两份 FBX，核对节点、枢轴、面片、封闭性、UV、材质槽、尺度和三组手势接触，再由回读网格生成实模图。参考照片提供侧面轮廓。枪托、护木和机匣的横向厚度、枪身两端外观与手套姿势采用项目美术设定，另由俯视、前视与背视灰模复核。预览与 `ModelQA.json` 保存到 `artifacts/fortified-compound-game/ak47/`，可复用的单机渲染保存在 `.build/ak47-art/renders/`。
