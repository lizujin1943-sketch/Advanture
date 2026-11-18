---
name: blender-mcp
description: "通过 Blender MCP 制作、修改和检查 3D 模型、材质、绑定、动画及游戏导出，或维护 Blender 与 MCP 工具链。适用于实际 Blender 资产操作；纯设定图生成不使用本技能。"
---

# Blender MCP

先读取 `get_addon_status` 和 `get_scene_info`，确认实际 Blender 版本、插件协议、当前文件及场景。工具名以当前会话发现的能力为准；`user_prompt` 使用用户原话。通过 `execute_blender_code` 执行小块 bpy 操作，使用 `look` 检查实际结果。

## 工作文件与版本

- 操作前确认当前文件和 `bpy.data.is_dirty`。切换文件或升级程序前保护未保存工作，不用 `read_factory_settings` 清空用户现场。
- 源 `.blend`、烘焙纹理和引擎导出分别维护；升级验证使用副本，不自动用新版覆盖所有旧工程。遵循当前仓库的源资产和产物目录约定。
- 本地已验证组合是 Blender 5.3.0、`mcp-for-blender` 2.3.0、addon 2.0 / protocol 16（2026-11-17）。这只是已验证基线，不代表后续最新版本；维护步骤见 [toolchain.md](references/toolchain.md)。

## 脚本与视觉检查

- 节点按 `node.type` 查找，不依赖本地化名称；先读取实际输入插槽和 RNA 参数，再赋值。材质颜色写进着色器输入，不能只改 `diffuse_color`。
- 尽量使用 `bpy.data`、bmesh 和属性访问。需要 `bpy.ops` 时显式设置活动对象、选择和上下文，编辑模式用 `try/finally` 退出。
- Blender 5 的动画使用 action slots/channelbags；用 `keyframe_insert` 插帧，通过 `bpy_extras.anim_utils.action_get_channelbag_for_slot` 访问曲线，不使用已移除的 `Action.fcurves`。
- 造型用多角度检查比例与轮廓；材质用实际灯光渲染检查；动画检查多个关键帧、接触点、穿插和循环接缝。单张截图不能证明动画流畅。
- 用户要求按设定还原时，使用同角度、近似焦距和灯光的对照。明确区分参考图板、材质试样、可变形模型和已导入游戏的资产；不能把图板或渲染图当成已完成模型。游戏资产制作检查见 [game-assets.md](references/game-assets.md)。
- 按图还原默认先补多视角设计，再检查空间结构、建模贴图、绑定动画和引擎接入；遵循用户确认的顺序，不只匹配正面。肢体数量、根部和前后分布必须通过背面与俯视实模检查。
- 有机根部在灰模阶段须通过[衔接近景验收](references/game-assets.md#有机衔接近景验收)：用侧光看轮廓和高光能否从主体顺着根部转入肢体。减面和蒙皮后用同机位复查，不能以网格封闭或没有裂缝代替造型验收。

## 官方专题指南

MCP 2.3.0 包内保留了七份指南，但此版本没有注册 `get_guide` 或 `guide://` 资源。不要反复调用不存在的工具。需要专题细节时，可从已安装包按需读取，例如：

```sh
/Users/lizujin/.local/bin/uvx --from mcp-for-blender==2.3.0 python -c 'from blender_mcp.guides import get; print(get("animation"))'
```

可选主题：`bpy`（API 兼容）、`scene`（布景）、`materials`（材质）、`retopology`（拓扑）、`rigging`（绑定）、`animation`（动作）、`level-design`（关卡）。只读当前需要的主题，不预加载整套手册。官方建议也须与实际 API 和已安装扩展核对，例如不要假定 Rigify 一定已安装。

## 已安装的社区制作技能

2026-11-17 从 GitHub 原仓库安装到 `~/.codex/skills/`，来源和验证记录见仓库 `docs/blender-skills.md`。在当前环境发现相应技能后，按任务使用，不一次加载全部：

- 按已确认的设定图还原：`blender-image-to-3d`，用于同角度对照、轮廓测量和导出回读；单幅透视图的遮挡部位仍需明确标注推断。
- 对照原画修造型、贴图和变形时，使用[模型还原与造型优化](../blender-reference-fidelity/SKILL.md)，按同机位灰模、材质和游戏画面定位差距。
- 生物造型与甲壳：`blender-sculpting`、`blender-hardsurface-modeling`、`blender-geometry-nodes`。
- 可变形拓扑与贴图：`blender-retopology-uv`、`blender-pbr-materials`、`blender-texturing`、`blender-texture-baking`。
- 骨架与动作：`blender-character-rigging`、`blender-animation`。
- 引擎交付：`blender-game-optimization`、`blender-export-pipelines`、`blender-asset-qa`。
- 通用执行、场景与展示：`blender-core`、`blender-scene-setup`、`blender-rendering`、`blender-lighting-lookdev`、`blender-workflow-recipes`。

这些技能提供制作流程与辅助脚本，不替代生物结构设计或视觉还原检查。自动重拓扑的结果也须检查关节面流，不能直接当成合格动画网格。
