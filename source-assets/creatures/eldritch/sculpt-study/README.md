# 鳃民形体重做样模

用户否定上一轮普通怪物的简约造型与生硬线条后，先制作单只连续皮肤的造型样本，评估轮廓、组织连接与动画表现。

`DeepOneSculpt.blend` 是独立的 Blender 5.2 源文件，包含身体、鳍膜/牙齿等附件、骨架、8 段动作，以及中性棚拍照明。它不是已经导入游戏的资源。上一轮怪物库仍在上级目录，避免未经美术审视就批量替换整个图鉴。

重建入口：

```sh
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python scripts/blender/create_deep_one_sculpt.py
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python scripts/blender/review_deep_one_sculpt.py -- --render --motion
```

以上从仓库根目录运行，输出放在 `artifacts/eldritch-sculpt-study/`。创建脚本只用于新的后台 Blender 进程，不应在用户正在编辑的场景中直接执行。

验证边界：检查源文件可重开、权重归一、实际蒙皮变化、循环首尾、根骨原地、攻击阶段衔接，以及主体不被鳍和触须误绑定。还需人工看动作姿势与近景形体。当前高模、程序化材质和临时权重是制作资料，仍需游戏网格与贴图制作后再进行 Unity 导入验证。
