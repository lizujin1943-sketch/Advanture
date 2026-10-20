# 钩索小岛：多视角建模参考

- 生成方式：内置 image_gen，基于现有场景图进行多视角生成和局部修正。
- 场景依据：`hook-island-map-reference-v11.png`。
- 视图：俯视 TOP、斜俯视 ISOMETRIC、正面 FRONT、左侧 LEFT、右侧 RIGHT。
- 方向约定：俯视图上方为北；正面从南向北看，左侧从西向东看，右侧从东向西看。
- 布局依据：中央小型圆形商店；椰子树位于其北偏西侧；码头位于西南岸。
- 用途：Blender 场景的轮廓、布置、风格和高度比例参考。由概念图推演的视图，不含实测尺寸，建模时以统一模型校准最终投影。
- 保留要求：低多边形风格、平缓宽阔岛面、自然短草、低矮小花与碎石、两个小椰果、无遮挡的环岛活动空间。

## 首轮生成提示词

Create ONE high-resolution landscape multi-view 3D environment modeling reference sheet, using the attached island image as the sole design reference. This is the SAME island viewed from different directions for rebuilding in Blender, not a redesign. Use-case: stylized-concept / multi-view environment reference. Preserve the polished low-poly game art, rich warm materials, island proportions, and especially the natural very short grass surface of the reference.

SHEET LAYOUT:
A clean, carefully composed landscape reference board, ideally 3072 x 2048. Five distinct, generous panels with clear whitespace between them. Top row takes approximately 65 percent of the sheet height: large true TOP view at left, large ISOMETRIC view at right. Bottom row takes approximately 35 percent: three equally scaled elevation views labeled FRONT, LEFT, RIGHT. Small restrained uppercase sans-serif labels exactly "TOP", "ISOMETRIC", "FRONT", "LEFT", "RIGHT", placed outside the art. No other text, no dimensions, no annotations, no UI, no border ornamentation. Subtle desaturated blue-gray studio backdrop; beautifully saturated asset colors and natural sunlight, not washed-out white toy renders. Each view must include the entire island and the entire tree, dock, and roof with comfortable margins.

CRITICAL CAMERA REQUIREMENTS:
TOP is a genuine orthographic plan: camera straight overhead at precisely 90 degrees looking vertically down. No perspective and no oblique angle. Roof reads as a circle, tree crown is seen from above, all coastline is visible, wooden pier footprint visible.
ISOMETRIC is an elevated three-quarter presentation matching the supplied reference's feel.
FRONT, LEFT, RIGHT are genuine horizontal orthographic elevations, NOT additional aerial views and NOT three-quarter views. Camera at horizontal eye level parallel to the ground, no lens perspective; show the ground profile, hut facade/profile, tree height, and pier supports above and just beneath the waterline. Only the thin horizontal land profile is visible in these three elevations, not a large exposed top surface. Render at the same scale across the three elevation panels.

CONSISTENCY / COORDINATE SYSTEM:
Reconstruct a single coherent island and rotate the camera around it. All five views share the exact same island shape, hut footprint, tree position and lean, dock location, grass boundaries, flowers, stones, materials and relative sizes. Use north toward top in the plan, south toward bottom. FRONT camera looks from south to north; LEFT from west to east; RIGHT from east to west.
A compact circular shop is near the center. One coconut tree grows immediately north/northwest of the hut, with its gentle lean retained. One short wooden pier is attached to the southwest shoreline and projects southwest into the water. The shop opening and curved counter face generally south/southwest toward the pier. One barrel stands adjacent to the east/right side of the shop opening. Do not relocate objects between panels. In opposing side views the apparent relative left/right positions should change correctly, not repeat the same camera image or mirror the whole scene arbitrarily.

DESIGN TO PRESERVE:
- A generous, broad, low, irregularly shaped tropical island, with an elegant asymmetric gently lobed coastline and shallow inward curves, as in the reference. Broad connected playable space. Never a perfect circle, narrow crescent, tiny island, deep split, or raised plateau.
- Mostly flat grassy ground with a broad gently sloping warm pale sandy edge. Very small exposed angular coastline stones sit below the walking surface; no raised boulders on the island. Low terrain profile, never a thick floating slab or tall cliff.
- Grass is the attached reference's flat, very short warm yellow-olive green ground cover: unified calm broad areas with subtle low-contrast organic tonal variation, irregular feathered contact with sand, tiny scattered low tufts. NO dense upright grass blades, shaggy lawn, separated raised turf carpets, geometric green islands, harsh green patchwork, repetitive triangles, pebble-like grass dots, or dark mottled noise. Keep the sand paths gentle and organically worn.
- Sparse tiny white/yellow flowers in a few loose clusters, occasional miniature flattened embedded gray gravel; preserve wide clear running and hook-casting room around the shore. No shrubs, ferns, bushes, or large ground foliage.
- ONE small circular timber shop with a warm bright orange faceted conical roof, small cap, wooden vertical wall planks, one integrated curved service counter and service hatch, lantern and modest tools, one adjacent wooden barrel. Preserve the reference hut's proportions and small footprint relative to the much larger island.
- ONE slender curved coconut tree immediately behind-left of the hut with green faceted fronds and EXACTLY TWO SMALL coconuts tucked under the crown, not covering the leaves.
- ONE small warm timber pier with transverse planks, four short rope-wrapped corner posts, and vertical wooden supports.
- Warm clean daylight, subtle ambient occlusion, legible low-poly faceted geometry, believable wood grain, sand and short grass material treatment. Match the reference's appealing game environment quality.

WATER / ELEVATIONS:
TOP and ISOMETRIC can include a narrow turquoise ring of shallow transparent sea, submerged low-poly stones, and restrained white shoreline foam, cropped softly into the board background. Leave coastline and full footprint unobstructed. Elevations show a clean low turquoise waterline with enough transparency directly underneath to see the shallow sandy bank and short pier supports; do not hide land profile behind waves, do not add a large deep underwater cross-section. Water is subordinate to the modeling reference.

EXCLUDE:
No characters, creatures, fish, monsters, collectible coins, floating chests, hook lines, large surface rocks, extra buildings, extra trees, extra piers, bushes, game UI, decorative diagrams or invented measurements. Do not change the orange conical roof into an awning or rectangular building. The top-down and horizontal elevation cameras must be unambiguous. Deliver the finished five-view reference sheet only.

## 俯视角修正提示词

Edit this five-panel island modeling reference sheet. Make ONE localized correction: replace only the upper-left panel labeled TOP with a TRUE 90-DEGREE VERTICAL OVERHEAD ORTHOGRAPHIC PLAN of the same island. Preserve the entire upper-right ISOMETRIC panel, all three bottom FRONT / LEFT / RIGHT panels, their captions, layout, backdrop and colors.

The existing upper-left view is incorrect because it still visibly shows the shop's front wall, service opening and counter as a vertical facade, and the tall tree trunk. Those visible elevations must disappear from the TOP panel. Render the camera pointing straight down, its optical axis exactly perpendicular to the ground plane. Architectural plan render, zero pitch, zero perspective. It must look clearly different from the unchanged upper-right three-quarter view.

Concrete visual requirements for the corrected TOP panel:
- The orange conical roof is a perfect circular plan footprint with triangular radial roof facets converging at its central cap. There is NO visible vertical shop wall, NO visible shop service window, and NO visible frontal counter facade. Any counter extending beyond the roof is seen only as its flat top, never a frontal face.
- The coconut tree is viewed vertically down onto its crown: a radial arrangement of green fronds covers most of its trunk; the trunk must not appear as a standing tall stem. The offset of the bent crown relative to the base is modest and consistent with the supplied asset.
- The pier is a flat rectangle projected diagonally southwest from shore, viewed from directly overhead; tops of planks and four posts visible, no standing underwater supports or frontal deck thickness. Its plan footprint remains on the southwest edge.
- The entire coastline, warm sand, short natural grass, sparse flowers and flat gravel are viewed directly from above. Flat planar ground and roof circles are not foreshortened.
- Maintain the same generous irregular island footprint, central small circular shop, coconut tree north/northwest of it, pier at southwest, broad clear shoreline, rich turquoise shallow-water rim. Reconstruct the true unforeshortened plan from the reference, while retaining the exact recognizable broad asymmetric lobes and shallow coves; do not make the island circular, square, or skinny.
- Maintain all relative object locations, colors and scale. The hut remains small compared with the island. Smooth very short olive-green grass remains natural as shown, no shaggy turf, no extra props.
- Fit the whole plan and sea rim inside the existing upper-left panel with margins and keep the TOP label below it.
- Polished low-poly game environment style, matching the sheet. No added text, no grid, no dimensions, no graphic circles, no stylization changes.

All other four panels must remain visually unchanged. Deliver the corrected complete five-view sheet.

## 左右侧面码头位置修正提示词

Localized correction to the attached five-view island reference sheet. Keep the TOP, ISOMETRIC, and FRONT panels unchanged. Keep the LEFT and RIGHT panel trees, huts, shop openings, labels, grass, coastline material, and backdrop unchanged. Correct ONLY the pier placement in the two bottom side-elevation panels so that all projections describe the same southwest pier.

BOTTOM MIDDLE PANEL, labeled LEFT:
The camera is west of the island, looking east. North is screen-left; south is screen-right. Keep the tree on the LEFT of the hut. The southwest pier must instead be at the RIGHT end of this panel's island, extending screen-right from the shore. REMOVE the existing pier at the LEFT end, restore water there, and place the one same small four-post wooden pier at the RIGHT end. Exactly ONE pier in this panel. Its planks and posts remain the same size as before.

BOTTOM RIGHT PANEL, labeled RIGHT:
The camera is east of the island, looking west. North is screen-right; south is screen-left. Keep the tree on the RIGHT of the hut. The southwest pier must be at the LEFT end of this panel's island, extending screen-left from the shore. REMOVE the existing pier at the RIGHT end, restore water there, and place the one same small four-post wooden pier at the LEFT end. Exactly ONE pier in this panel. Its planks and posts remain the same size as before.

Left/right elevation cameras remain perfectly horizontal and orthographic. Keep the same low ground silhouette and waterline. The LEFT and RIGHT views should depict the same island at equal scale, with equal overall ground width. Maintain the small circular orange-roof shop and single coconut tree. Both tree crowns keep two small coconuts. Do not mirror or rotate either entire panel, and do not change tree or hut positions; only move the two side-elevation piers to their correct ends.

All other image content, layout, colors, and the exact five captions must stay unchanged. Output the finished complete reference board with this correction only.
