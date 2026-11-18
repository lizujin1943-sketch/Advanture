# Blender 建模技能安装记录

2026-11-17 按用户要求，查询官方技能目录及 GitHub 原仓库，安装 18 个社区技能到 `/Users/lizujin/.codex/skills/`。本轮没有安装新的 Blender addon、没有更改 MCP 配置或修改怪物源资产。

## 来源与选择

通过 OpenAI `skill-installer` 的官方目录接口查询了 39 个精选技能，未找到名称含 Blender 的专项技能；查询结果保存在 `artifacts/blender-skills-20261117/official-catalog.json`。这只是本次官方精选目录的结果，不代表所有官方仓库均不存在 3D 相关技能。

采用两份 MIT 许可的 GitHub 原仓库：

| 来源 | 固定版本 | 安装范围 |
|---|---|---|
| [EL4CTEO/blender-skills](https://github.com/EL4CTEO/blender-skills) | 1.2.0；`b1b21dfb73bc40b874ebf2aa12645931bfed3523` | 17 个制作和交付技能；上游注明兼容 Blender 5.2 LTS+，在 5.3 验证 |
| [majidmanzarpour/blender-game-skills](https://github.com/majidmanzarpour/blender-game-skills) | `f0ef29385a03de139957e6f700b801cdc00b7e29` | `blender-image-to-3d`，参考图对照与资产制作流程 |

安装使用系统 `skill-installer/scripts/install-skill-from-github.py`，指定原仓库、固定 commit、选定目录和 `--method git`。没有覆盖已有同名技能，安装文件与检查过的源文件逐项 SHA256 对比一致。保留了上一轮项目内的 `blender-mcp` 技能，并增加按任务路由。

还查看了 `edemaistre/blender-expert-skills`，与上述技能存在名称和工作范围重叠，本次未叠加安装。没有采用强制低多边形风格或依赖外部付费生成服务的方案。

## 已安装能力

| 用途 | Skill |
|---|---|
| 参考图测量、同机位渲染对照、还原检查 | `blender-image-to-3d` |
| 生物雕刻、细分和重网格 | `blender-sculpting` |
| 甲壳、武器、道具及硬表面结构 | `blender-hardsurface-modeling` |
| 程序化结构与节点建模 | `blender-geometry-nodes` |
| 重拓扑、UV、纹素密度 | `blender-retopology-uv` |
| PBR、磨损细节和高低模烘焙 | `blender-pbr-materials`、`blender-texturing`、`blender-texture-baking` |
| 骨架、权重、动作、循环检查 | `blender-character-rigging`、`blender-animation` |
| LOD、面数、Unity 等引擎导出、质量检查 | `blender-game-optimization`、`blender-export-pipelines`、`blender-asset-qa` |
| bpy 执行与场景组织 | `blender-core`、`blender-scene-setup` |
| 灯光、展示渲染与完整制作流程 | `blender-rendering`、`blender-lighting-lookdev`、`blender-workflow-recipes` |

## 本机验证

- 所选 18 个技能的 52 个 Python 文件通过语法解析；上游技能结构、引用和脚本检查通过。
- 18 个安装目录的名称、描述及全部受版本控制文件验证通过，并与固定 commit 的文件逐项一致。
- 本机 Blender 5.3.0 运行上游测试：雕刻链 1 项、重拓扑与 UV 4 项、导出回读 2 项，共 7 项通过、0 失败、0 跳过。导出测试覆盖 Unity 在内的预设和批量导出。
- 安装后的 `blender-core` 实际发现 Blender 并输出环境报告。
- 安装后的参考图对照脚本通过相同与不同轮廓的测量检查。

安装清单、固定 commit、文件哈希、日志及测试产物在 `artifacts/blender-skills-20261117/`。验证使用独立后台进程与临时资产，没有改动正在打开的 Blender 场景。

这次验证的是安装完整性和关键脚本兼容性，没有逐项验证 18 个技能的所有分支，也没有制作新的高精度怪物或进行 Unity 实机美术验收。技能会在下一轮对话可用；制作效果仍须通过实际模型与设定图对照来确认。
