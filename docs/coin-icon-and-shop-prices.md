# 金币图标与商品价格

2026-10-30，仅修改 Unity 表现层。

- 统一货架在商品右上角显示配置表中的单价。读取当前本地玩家的确认态金币：大于或等于单价为亮绿色，不足为亮红色，每次绘制重新判断。颜色只表达金币是否足够，不代替库存、背包空间等购买校验。
- 装备物品格不显示等级；堆叠数量使用亮白色，在右下角显示，为 1 时隐藏。价格和数量均去掉黑色矩形底板，改用较大的 Arial 粗体数字和一像素细描边；按最终屏幕像素尺寸绘制，减少界面缩放造成的模糊。普通背包物品不显示价格。
- `HudPainter` 的金币入口使用独立彩色 `CoinIcon.png`，覆盖钱包、奖励提示及复活按钮。保留透明度动画和禁用态透明度，其他图标继续使用原来的单色图集。重新运行 `generate-hud-icons.py` 不会覆盖新金币。
- 资源：`samples/TacticalGame.Unity/Assets/HookIsland/Resources/UI/CoinIcon.png`。原始 PNG 为 1254×1254、RGBA，Unity 导入上限为 128，不压缩，Clamp 采样，无 mipmap。

## 美术生成记录

使用内置 `image_gen` 工具，`transparent_background=true`，无参考图。生成结果已复制到项目中，不依赖工具的临时位置。最终提示词如下：

> Create a single production-ready currency coin icon for a cheerful nautical survival fishing video game UI. Square composition, one unmistakable circular GOLD COIN centered and filling about 88% of the canvas height, no other objects. Almost front-facing coin with a subtly visible thick milled rim along the lower-right edge, warm amber side edge, strong raised double beveled gold outer rim, rich golden circular face and one very large bold embossed capital letter G in the center. G is the ONLY text. Upper-left bright warm highlight, restrained simple cel-shaded gradients, crisp silhouette and chunky clean shapes, premium polished 2D game inventory icon, readable at just 20-28 pixels. Main colors deep amber and saturated yellow gold with pale gold highlights and dark amber contours; no silver, no white letter, no outline-only token, no currency-stack, no stars, no sparkles, no glow, no ground shadow. The letter G should be a simple thick embossed relief with a contrasting dark amber inset outline so it remains readable at tiny sizes. Exactly one coin, transparent background outside coin, tight framing. No scene, no mockup, no UI, no labels, no watermark.

## 验证边界

- Unity 编译完成，控制台无错误；保留既有 `ParticleSystemRenderer.BakeMesh` 过时 API 警告和 MCP 重连警告。
- 确认运行时资源可加载，导入尺寸 128×128，透明设置正确且未压缩。
- 临时 Editor 外观预览调用正式 `HudPainter.Icon`、`BackpackOverlay.DrawItem` 与真实价格表：检查 20／22／24／32／48 px 金币、淡出／禁用透明度、100 金币买 100 商品为绿、99 为红，以及窄钓竿 5200 的价格与等级分行、食物价格与右下数量共存。预览未读写房间状态，检查后关闭。
- 按 `docs/agent-workflow.md` 的表现层验证范围，未运行模拟／联机回归、全量 `verify.sh` 或重新构建独立客户端。
- 数字可读性调整：Unity 编译及正式 `HookPlatform` 商店运行检查无错误；在缩小的 Game View 中检查无底板的价格、数量和图标间距，预览为 `artifacts/merchant-readable-numbers.png`。此次仅调整绘制方式，未重复运行模拟、联机和存档测试；检查后退出播放。
