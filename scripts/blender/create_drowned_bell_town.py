"""从正式地形数据制作沉钟镇的可编辑 Blender 场景和 Unity FBX。

运行 Blender --background --factory-startup --python 本文件 -- --render。
先在 Unity 菜单 Tools / Hook Island / Town Art 导出布局。布局中的 X/Z 直接
对应 Blender 的 X/Y；可毁木带只放进预览集合，静态导出绝不跨过这些缺口。
"""
import hashlib
import json
import math
import random
import sys
from pathlib import Path

import bpy
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
from town_geometry import MeshBatch, camera, collection, make_material
from town_facades import dress_facade, dress_cover

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'source-assets/environments/drowned-bell-town'
EXPORT = ROOT / 'samples/TacticalGame.Unity/Assets/HookIsland/Art/Town'
PREVIEW = ROOT / 'artifacts/drowned-bell-town-art'
LAYOUT_TEXT = (SOURCE / 'layout.json').read_text(encoding='utf-8')
LAYOUT = json.loads(LAYOUT_TEXT)
RNG = random.Random(11062026)
HEIGHT = LAYOUT['groundHeight']

PALETTE = {
    'Stone': (.34, .43, .43), 'StoneLight': (.43, .51, .49),
    'StoneWarm': (.43, .46, .39), 'StoneDark': (.19, .28, .29),
    'Mortar': (.13, .21, .22), 'Limestone': (.62, .68, .57),
    'Wall': (.57, .59, .46), 'WallBlue': (.32, .46, .46),
    'WallRust': (.56, .35, .23), 'Wood': (.24, .16, .105),
    'WoodLight': (.44, .30, .17), 'WoodDeck': (.36, .27, .17),
    'Roof': (.10, .27, .27), 'RoofLight': (.14, .35, .33),
    'RoofRust': (.38, .19, .13), 'RoofRustLight': (.47, .26, .17),
    'Metal': (.09, .16, .18), 'Brass': (.55, .36, .12),
    'Window': (1.0, .57, .16), 'Glass': (.16, .41, .43),
    'Cloth': (.63, .38, .21), 'ClothCream': (.73, .68, .48),
    'Jade': (.24, .64, .52), 'DeepJade': (.10, .31, .30),
    'Moss': (.19, .32, .23), 'Water': (.06, .26, .29),
    'WaterLight': (.075, .29, .31), 'Foam': (.28, .48, .45),
}


def window(batch, x, y, bottom, width=1.0, height=1.5, lit=True):
    """南向窗的玻璃、窗台和木十字，各处深度都贴在既有墙体上。"""
    z = bottom + height / 2
    batch.box((x, y, z), (width + .2, .12, height + .2), 'Wood')
    batch.box((x, y - .075, z), (width, .03, height), 'Window' if lit else 'Glass')
    batch.box((x, y - .1, z), (.075, .06, height), 'Wood')
    batch.box((x, y - .1, z), (width, .06, .075), 'Wood')
    batch.box((x, y - .14, bottom - .12), (width + .4, .35, .18), 'Limestone')


def lantern(batch, x, y, z):
    batch.beam((x, y + .28, z + .5), (x, y - .28, z + .5), .08, 'Metal')
    batch.cylinder((x, y - .26, z + .5), (x, y - .26, z + .22), .035, 'Metal', 6)
    batch.box((x, y - .26, z), (.26, .26, .4), 'Window', bevel=.04)
    for level in (-.23, .23):
        batch.box((x, y - .26, z + level), (.38, .38, .08), 'Metal', bevel=.06)
    for side in (-1, 1):
        batch.beam((x + side * .14, y - .41, z - .2), (x + side * .14, y - .41, z + .2), .035, 'Metal')


def emblem(batch, x, y, z, kind, scale=1):
    """服务建筑以大轮廓图案区分，在游玩视角下比小字更容易识别。"""
    batch.ring((x, y, z), scale * .72, .10, 'Brass', normal=(0, -1, 0), segments=12)
    if kind == 'Clinic':
        batch.box((x, y - .02, z), (.22 * scale, .06, scale), 'ClothCream')
        batch.box((x, y - .03, z), (scale, .06, .22 * scale), 'ClothCream')
    elif kind in ('Chapel', 'Archive'):
        # 两条折线形成异眼，竖瞳也是独立的大块面。
        for sign in (-1, 1):
            batch.beam((x - .5 * scale, y - .03, z), (x, y - .03, z + sign * .3 * scale), .06, 'Jade')
            batch.beam((x, y - .03, z + sign * .3 * scale), (x + .5 * scale, y - .03, z), .06, 'Jade')
        batch.box((x, y - .04, z), (.11 * scale, .07, .48 * scale), 'Brass')
    elif kind in ('Engine', 'Workshop'):
        batch.ring((x, y - .03, z), .38 * scale, .12, 'Jade', normal=(0, -1, 0), segments=8)
        for i in range(8):
            a = i * math.tau / 8
            batch.beam((x + math.cos(a) * .35 * scale, y - .04, z + math.sin(a) * .35 * scale),
                       (x + math.cos(a) * .53 * scale, y - .04, z + math.sin(a) * .53 * scale), .09, 'Brass')
    elif kind == 'Armory':
        for sign in (-1, 1):
            batch.beam((x - .4 * scale, y - .03, z - sign * .35 * scale),
                       (x + .4 * scale, y - .03, z + sign * .35 * scale), .12, 'ClothCream')
    else:
        for sign in (-1, 1):
            batch.beam((x - .46 * scale, y - .03, z - sign * .46 * scale),
                       (x + .46 * scale, y - .03, z + sign * .46 * scale), .06, 'Brass')
        batch.ring((x, y - .04, z), .22 * scale, .08, 'Jade', normal=(0, -1, 0), segments=8)


def create_ground(materials, groups):
    """按权威半米格铺石板。边缘厚度向下生长，地面高度仍为 0.45 米。"""
    cells = set()
    for rect in LAYOUT['ground']:
        for i in range(round(rect['width'] * 2)):
            cells.add((round(rect['x'] * 2) + i, round(rect['z'] * 2)))
    floor = MeshBatch('Town_Streets', groups['streets'], materials)
    piers = MeshBatch('Town_QuaysAndPiles', groups['structure'], materials)
    # 基底按整行合并，石板只保留顶面。避免每块薄地砖都产生完整六面体。
    for rect in LAYOUT['ground']:
        x0, y0, w, d = (rect[k] for k in ('x', 'z', 'width', 'depth'))
        floor.quad([(x0, y0, HEIGHT - .006), (x0 + w, y0, HEIGHT - .006),
                    (x0 + w, y0 + d, HEIGHT - .006), (x0, y0 + d, HEIGHT - .006)], 'Mortar')
    visited = set()
    for ix, iy in sorted(cells):
        if (ix, iy) in visited:
            continue
        # 合并成一米见方的石板；不完整的边缘回落到半米板。
        block = {(ix, iy), (ix + 1, iy), (ix, iy + 1), (ix + 1, iy + 1)}
        large = ix % 2 == 0 and iy % 2 == 0 and block <= cells
        used = block if large else {(ix, iy)}
        visited.update(used)
        size = 1 if large else .5
        x, y = ix / 2 + size / 2, iy / 2 + size / 2
        border = any((cx + dx, cy + dy) not in cells for cx, cy in used
                     for dx, dy in ((-1, 0), (1, 0), (0, -1), (0, 1)))
        variation = math.sin(x * .21) + math.cos(y * .19) + RNG.uniform(-.55, .55)
        color = 'StoneWarm' if variation > 1.35 else 'StoneLight' if variation > .3 else 'Stone'
        radius = math.hypot(x + 8, y + 12)
        if 19.8 < radius < 22.3:
            color = 'StoneDark'
        if border:
            color = 'Limestone' if (ix + iy) % 5 else 'Moss'
        half = (size - .018) / 2
        floor.quad([(x - half, y - half, HEIGHT), (x + half, y - half, HEIGHT),
                    (x + half, y + half, HEIGHT), (x - half, y + half, HEIGHT)], color)
    for ix, iy in sorted(cells):
        x, y = ix / 2, iy / 2
        for dx, dy in ((-1, 0), (1, 0), (0, -1), (0, 1)):
            if (ix + dx, iy + dy) in cells:
                continue
            cx, cy = x + .25 + dx * .24, y + .25 + dy * .24
            w, d = (.12, .5) if dx else (.5, .12)
            piers.box((cx, cy, -.22), (w, d, 1.3), 'StoneDark')
            piers.box((cx, cy, .20), (w + .025, d + .025, .16), 'Moss' if (ix + iy) % 9 == 0 else 'Limestone')
            if (ix * 3 + iy) % 13 == 0:
                piers.cylinder((cx - dx * .14, cy - dy * .14, -2.5), (cx - dx * .14, cy - dy * .14, .32), .16, 'Wood', 7)
    floor.finish()
    piers.finish()
    return cells


def create_house(building, materials, groups):
    bid, kind = building['id'], building['kind']
    x, y, w, d = (building[k] for k in ('x', 'z', 'width', 'depth'))
    front, back, top = y - d / 2, y + d / 2, HEIGHT + 5
    face_front = front - (.25 if building['enterable'] else 0)
    shell = MeshBatch('Building_%02d_%s' % (bid, kind), groups['buildings'], materials)
    details = MeshBatch('Details_%02d_%s' % (bid, kind), groups['buildings'], materials)
    roof = MeshBatch('Roof_%02d_%s' % (bid, kind), groups['buildings'], materials)
    wall = 'WallBlue' if bid in (3, 4, 8, 9) else 'WallRust' if bid in (5, 7) else 'Wall'
    for obstacle in LAYOUT['obstacles']:
        if obstacle['building'] != bid:
            continue
        shell.box((obstacle['x'], obstacle['z'], HEIGHT + obstacle['height'] / 2),
                  (obstacle['width'], obstacle['depth'], obstacle['height']), wall)
        shell.box((obstacle['x'], obstacle['z'], HEIGHT + .42),
                  (obstacle['width'] + .04, obstacle['depth'] + .04, .84), 'StoneDark')

    if kind == 'Dock':
        # 码头在玩法中完全开放，铺板和桩基只沿地面向下布置。
        for i in range(16):
            details.box((x - w / 2 + .25 + i * .5, y, HEIGHT + .002), (.47, d, .035),
                        'WoodDeck' if i % 3 else 'WoodLight')
        for side in (-1, 1):
            for yy in (front, y, back):
                details.cylinder((x + side * w / 2, yy, -3), (x + side * w / 2, yy, .43), .24, 'Wood', 8)
        # 门口地面嵌入罗盘，中央候船区保持开阔。
        details.ring((x, y, HEIGHT + .03), 1.4, .12, 'Brass')
        for side in (-1, 1):
            details.beam((x - side, y - 1, HEIGHT + .03), (x + side, y + 1, HEIGHT + .03), .06, 'Brass')
        details.finish()
        return

    # 柱梁位于已阻挡的墙面内。可进入建筑的 2.8m 南门洞保持贯通。
    for xx in (x - w / 2 + .15, x + w / 2 - .15):
        for yy in (front + .12, back - .12):
            details.box((xx, yy, HEIGHT + 2.5), (.22, .24, 5.05), 'Wood')
        details.box((xx, y, top - .18), (.24, d, .24), 'Wood')
    for z in (HEIGHT + 1.05, HEIGHT + 3.35, top - .16):
        details.box((x, back - .07, z), (w, .20, .15), 'Wood')
        if not building['enterable'] or z > 4.9:
            details.box((x, front + .04, z), (w, .18, .15), 'Wood')
    for xx in (x - w * .30, x + w * .30):
        window(details, xx, face_front - .08, HEIGHT + 1.6, min(1.15, w * .16), 1.5, lit=bid % 4 != 0)
        lantern(details, xx, front - .16, 4.6)
    for yy in (y - d * .27, y + d * .27):
        for side in (-1, 1):
            face_x = x + side * (w / 2 + (.25 if building['enterable'] else 0))
            details.box((face_x + side * .025, yy, 2.6), (.05, 1.2, 1.7), 'Glass')
            details.box((face_x + side * .06, yy, 2.6), (.06, .075, 1.8), 'Wood')
            details.box((face_x + side * .06, yy, 2.6), (.06, 1.2, .075), 'Wood')
    if not building['enterable']:
        details.box((x, front - .04, 1.9), (1.7, .07, 2.9), 'Wood')
        for n in range(5):
            details.box((x - .66 + n * .33, front - .09, 1.9), (.025, .025, 2.7), 'WoodLight')
        details.box((x + .5, front - .14, 1.8), (.08, .08, .25), 'Brass')
        roofmat = 'RoofRust' if kind in ('Workshop', 'Pawnshop') else 'Roof'
        roof.roof(x, y, w + .65, d + .7, top, 2.2 if w < 8 else 2.9, roofmat, 'Wood')
        # 山墙小窗与屋顶大坡面一起提供远距离识别点。
        window(details, x, front - .43, top + .2, .85, 1.0, True)
    else:
        # 开放的室内通过断裂山墙和墙顶压石表达屋顶遗失，保留俯视可读性。
        for side in (-1, 1):
            roof.box((x + side * (w / 2), y, top + .10), (.75, d + .5, .3), 'Limestone')
        roof.box((x, back, top + .10), (w + .5, .75, .3), 'Limestone')
        for xx in (x - w / 2 + 1, x + w / 2 - 1):
            roof.roof(xx, y, 1.5, d + .45, top + .2, .8, 'Roof', 'Limestone')
        for side in (-1, 1):
            details.box((x + side * 1.54, front - .05, 2.2), (.22, .32, 3.5), 'Limestone')
        details.beam((x - 1.45, front - .02, 3.9), (x, front - .02, 5.0), .20, 'Limestone')
        details.beam((x, front - .02, 5.0), (x + 1.45, front - .02, 3.9), .20, 'Limestone')
        # 内侧书柜和药柜嵌在北墙厚度内，不在房间里另添实心家具。
        for xx in (x - w * .30, x + w * .30):
            details.box((xx, back - .27, 2.0), (1.6, .08, 2.8), 'Wood')
            for z in (1.0, 1.8, 2.6):
                details.box((xx, back - .34, z), (1.6, .08, .09), 'Brass')
                for n in range(6):
                    details.box((xx - .65 + n * .23, back - .36, z + .25), (.14, .08, .4),
                                'ClothCream' if n % 2 else 'Jade')
        details.ring((x, y, HEIGHT + .009), min(w, d) * .29, .09, 'Brass', segments=24)

    details.box((x, front - .22, 4.25), (2.5, .16, 1.65), 'Wood', bevel=.08)
    emblem(details, x, front - .315, 4.25, kind)
    if kind in ('Commission', 'Pawnshop', 'Outpost', 'SwimShop'):
        # 帆布篷在头顶以上，所有支架固定在既有门面。
        for side in (-1, 1):
            details.beam((x + side * 1.8, front + .05, 4.0), (x + side * 1.8, front - 1.3, 3.45), .09, 'Wood')
        for n in range(6):
            x0 = x - 1.8 + n * .6
            details.quad([(x0, front, 4), (x0 + .6, front, 4), (x0 + .6, front - 1.3, 3.45), (x0, front - 1.3, 3.45)],
                         'Cloth' if n % 2 else 'ClothCream')
            details.box((x0 + .3, front - 1.3, 3.3), (.59, .04, .3), 'Cloth' if n % 2 else 'ClothCream')
    if kind in ('Engine', 'Workshop'):
        for side in (-1, 1):
            cx, cy = x + side * w * .29, y + d * .23
            details.box((cx, cy, top + 1.9), (.9, .9, 5), 'StoneDark', bevel=.08)
            details.box((cx, cy, top + 4.4), (1.25, 1.25, .3), 'Brass')
        details.cylinder((x - w * .42, front - .13, 1.6), (x - w * .42, front - .13, 3.7), .25, 'Brass', 10)
        details.cylinder((x - w * .42, front - .13, 3.7), (x + w * .42, front - .13, 3.7), .25, 'Brass', 10)
        for cx in (x - 1.25, x + 1.25):
            details.ring((cx, front - .43, 3.7), .38, .1, 'Metal', normal=(0, -1, 0), segments=10)
    if kind == 'Cartography':
        # 测绘站用八角观测亭和罗盘风向标区别于普通商铺。
        roof.cylinder((x, y, top + 1.7), (x, y, top + 4.0), 1.6, 'WallBlue', 8, 1.4)
        roof.cylinder((x, y, top + 4.0), (x, y, top + 5.2), 2.0, 'Roof', 8, .05)
        roof.cylinder((x, y, top + 5.0), (x, y, top + 6.4), .06, 'Brass', 6)
        roof.beam((x - 1, y, top + 5.8), (x + 1, y, top + 5.8), .08, 'Brass')
    if kind == 'Chapel':
        create_bell_tower(x, back - .05, top, roof, details)
    if kind == 'Archive':
        for xx in (x - w / 2, x + w / 2):
            roof.box((xx, back, top + 1.5), (1.4, 1.4, 3), 'StoneDark', bevel=.14)
            roof.cylinder((xx, back, top + 3), (xx, back, top + 4.2), 1.0, 'Roof', 5, .05)
        emblem(details, x, back - .31, 3.8, 'Archive', 1.8)
    dress_facade(building, details, roof, HEIGHT)
    shell.finish()
    details.finish()
    roof.finish()


def create_bell_tower(x, y, base, batch, detail):
    """钟塔脚落在既有礼拜堂北墙上，向上提供全图地标。"""
    batch.box((x, y, base + 2.7), (3.4, 1.8, 5.4), 'StoneDark', bevel=.12)
    for level in (base, base + 2.8, base + 5.4):
        batch.box((x, y, level), (4.0, 2.4, .28), 'Limestone')
    for side in (-1, 1):
        batch.box((x + side * 1.45, y, base + 7.5), (.45, 1.8, 4.0), 'Limestone', bevel=.05)
    batch.beam((x - 1.45, y, base + 9.3), (x, y, base + 10.8), .38, 'Limestone')
    batch.beam((x, y, base + 10.8), (x + 1.45, y, base + 9.3), .38, 'Limestone')
    batch.roof(x, y, 4.2, 3.1, base + 10.4, 2.8, 'Roof', 'Brass')
    batch.cylinder((x, y, base + 7), (x, y, base + 8.4), 1.0, 'Brass', 12, .36)
    batch.cylinder((x, y, base + 6.85), (x, y, base + 7.1), 1.15, 'Brass', 12)
    batch.cylinder((x, y, base + 8.2), (x, y, base + 9.8), .08, 'Metal', 8)
    batch.cylinder((x, y, base + 6.45), (x, y, base + 7.8), .13, 'Metal', 8)
    emblem(detail, x, y - .94, base + 2.9, 'Chapel', 1.5)
    # 顶部的歪斜尖脊与眼纹共同表现海中旧神崇拜。
    batch.beam((x, y, base + 13.0), (x + .55, y, base + 14.4), .20, 'Brass')


def crate(batch, x, y, bottom, width, depth, height):
    batch.box((x, y, bottom + height / 2), (width, depth, height), 'WoodLight', bevel=.045)
    for side in (-1, 1):
        yy = y + side * (depth / 2 + .025)
        for level in (bottom + .12, bottom + height - .12):
            batch.box((x, yy, level), (width, .08, .14), 'Wood')
        for xx in (x - width / 2 + .12, x + width / 2 - .12):
            batch.box((xx, yy, bottom + height / 2), (.14, .08, height), 'Wood')
        batch.beam((x - width / 2 + .12, yy, bottom + .14),
                   (x + width / 2 - .12, yy, bottom + height - .14), .13, 'Wood')


def create_cover(materials, groups):
    for obstacle in LAYOUT['obstacles']:
        if obstacle['building']:
            continue
        index = obstacle['id']
        x, y, w, d, h = (obstacle[k] for k in ('x', 'z', 'width', 'depth', 'height'))
        batch = MeshBatch('Cover_%02d' % index, groups['cover'], materials)
        if index == 26:  # 工坊货场中的供能机组。
            batch.box((x, y, HEIGHT + h / 2), (w, d, h), 'Metal', bevel=.10)
            for side in (-1, 1):
                batch.cylinder((x + side * 1.0, y - 1.8, 2.5), (x + side * 1.0, y + 1.8, 2.5), .72, 'Brass', 12)
                batch.ring((x + side * 1.0, y - 1.82, 2.5), .6, .12, 'Jade', normal=(0, -1, 0))
            for n in range(8):
                batch.box((x - 1.7 + n * .48, y, HEIGHT + h + .02), (.10, d - .3, .08), 'Brass')
        elif index == 29:  # 档案前庭的纪念碑采用现有三米掩体。
            batch.box((x, y, HEIGHT + h / 2), (w, d, h), 'StoneDark', bevel=.09)
            batch.box((x, y, HEIGHT + h - .12), (w + .1, d + .1, .24), 'Limestone')
            emblem(batch, x, y - d / 2 - .04, HEIGHT + 1.65, 'Archive', 1.3)
            for side in (-1, 1):
                batch.box((x + side * (w / 2 - .2), y, HEIGHT + h / 2), (.18, d + .05, h), 'Limestone')
        elif index == 25:  # 断帆广场，翻覆船体贴合原有长形障碍。
            batch.box((x, y, HEIGHT + h / 2), (w, d, h), 'Wood', bevel=.14)
            for i in range(9):
                yy = y - d / 2 + .22 + i * (d - .44) / 8
                for side in (-1, 1):
                    batch.box((x + side * (w / 2 + .025), yy, HEIGHT + h / 2), (.10, .14, h), 'WoodLight')
                batch.box((x, yy, HEIGHT + h + .06), (w, .14, .10), 'WoodLight')
            for n in range(6):
                for side in (-1, 1):
                    batch.box((x + side * (w / 2 + .01), y, HEIGHT + .2 + n * .48), (.04, d, .025), 'Metal')
            batch.beam((x, y - d / 2, HEIGHT + h + .1), (x, y + d / 2, HEIGHT + h + .1), .20, 'Brass')
        elif index in (27, 28):
            crate(batch, x, y, HEIGHT, w, d, h)
            # 摊位篷顶只位于掩体正上方。
            for side in (-1, 1):
                batch.beam((x + side * (w / 2 - .12), y, HEIGHT),
                           (x + side * (w / 2 - .12), y, HEIGHT + 3.0), .11, 'Wood')
            batch.roof(x, y, w, d, HEIGHT + 2.7, .5, 'RoofRust', 'Wood')
        elif index < 30:
            crate(batch, x, y, HEIGHT, w, d, h)
            batch.box((x, y, HEIGHT + h + .03), (w * .65, d * .8, .06), 'Cloth')
        else:
            # 出怪遮挡体仍是完整实心轮廓，面板、货箱和绑带只做表面装饰。
            batch.box((x, y, HEIGHT + h / 2), (w, d, h), 'StoneDark' if index % 3 == 0 else 'Wood')
            for n in range(5):
                z = HEIGHT + .15 + n * (h - .3) / 4
                batch.box((x, y, z), (w + .02, d + .02, .10), 'Limestone' if index % 3 == 0 else 'WoodLight')
            batch.box((x, y, HEIGHT + h + .05), (w + .1, d + .1, .1), 'StoneLight')
        dress_cover(batch, x, y, w, d, h, index, HEIGHT)
        batch.finish()


def create_street_details(materials, groups, cells):
    batch = MeshBatch('Town_InlaidRoutesAndRitual', groups['streets'], materials)
    for building in LAYOUT['buildings']:
        x, y = building['entranceX'], building['entranceZ']
        # 金色门槛贴着地表，表明门口服务范围。
        batch.box((x, y, HEIGHT + .008), (2.4, .35, .016), 'Brass')
        for side in (-1, 1):
            batch.box((x + side * 1.25, y + .3, HEIGHT + .008), (.10, 1.0, .016), 'Brass')
    # 仪式场石环只在石面上画纹，避开四条可毁木带。
    for r in (20.35, 21.65):
        batch.ring((-8, -12, HEIGHT + .008), r, .08, 'Brass', segments=160)
    for i in range(16):
        angle = i * math.tau / 16
        x, y = -8 + math.cos(angle) * 21, -12 + math.sin(angle) * 21
        batch.box((x, y, HEIGHT + .012), (.16, .7, .018), 'Jade', angle=angle - math.pi / 2)
    # 四个原有木带维修点仍在外环同一位置，以矮绞盘替换原来的金色方块。
    for strip in LAYOUT['wood']:
        if strip['segment'] != 3:
            continue
        x, y = -8 + strip['dx'] * 21, -12 + strip['dz'] * 21
        batch.cylinder((x, y, HEIGHT), (x, y, HEIGHT + .22), .29, 'Metal', 10)
        batch.cylinder((x, y, HEIGHT + .18), (x, y, HEIGHT + .34), .22, 'Brass', 10)
        batch.ring((x, y, HEIGHT + .35), .28, .065, 'Jade', segments=12)
        batch.beam((x - .27, y, HEIGHT + .36), (x + .27, y, HEIGHT + .36), .065, 'WoodLight')
    for area in LAYOUT['areas']:
        x, y, w, d = (area[k] for k in ('x', 'z', 'width', 'depth'))
        # 边界附近的排水格栅增加尺度感，中心仍留给战斗。
        for side in (-1, 1):
            for n in range(5):
                cx, cy = x - w * .25 + n * w / 8, y + side * (d / 2 - .7)
                if (math.floor(cx * 2), math.floor(cy * 2)) not in cells:
                    continue
                batch.box((cx, cy, HEIGHT + .006), (.65, .28, .012), 'Metal')
                for j in range(4):
                    batch.box((cx - .24 + j * .16, cy, HEIGHT + .015), (.04, .28, .012), 'Brass')
    batch.finish()


def create_background(materials, groups):
    """远景仅位于玩法边界之外，作为水上小镇的天际线。"""
    batch = MeshBatch('Town_DistantRuins', groups['background'], materials)
    for cx, cy, height in [(-62, 79, 10), (-34, 85, 16), (31, 83, 12), (77, 79, 19), (92, 34, 9)]:
        batch.cylinder((cx, cy, -3), (cx, cy, 1.4), 5.8, 'StoneDark', 7, 4.5)
        for side in (-1, 1):
            batch.cylinder((cx + side * 2.5, cy, .7), (cx + side * 2.2, cy + .25, height), .9, 'Stone', 7, .65)
            batch.box((cx + side * 2.2, cy + .25, height), (1.7, 1.6, .7), 'Limestone', bevel=.12)
        batch.beam((cx - 2.2, cy, height), (cx + .1, cy, height + 2.6), .6, 'StoneLight')
        batch.beam((cx + .1, cy, height + 2.6), (cx + 2.2, cy, height), .6, 'StoneLight')
        for k in range(5):
            a = k * math.tau / 5
            batch.cylinder((cx + math.cos(a) * 4, cy + math.sin(a) * 4, -.8),
                           (cx + math.cos(a) * 4.2, cy + math.sin(a) * 4.2, .6), .8, 'Moss', 5, .3)
    batch.finish()


def create_preview(materials, group, scene):
    water = MeshBatch('Preview_Ocean', group, materials)
    water.box((0, 0, -.13), (600, 600, .10), 'Water')
    # 少量水纹仅用于 Blender 展示。游戏继续使用自己的动态海水。
    for i in range(180):
        x, y = RNG.uniform(-100, 100), RNG.uniform(-85, 100)
        water.quad([(x, y, -.072), (x + 1.8, y + .08, -.072),
                    (x + 2.5, y + .25, -.072), (x + .5, y + .19, -.072)], 'WaterLight')
    water.finish()
    wood = MeshBatch('Preview_DestructibleWood_ExcludedFromExport', group, materials)
    for strip in LAYOUT['wood']:
        angle = math.atan2(-strip['dx'], strip['dz'])
        for i in range(6):
            offset = -.83 + i * .333
            wood.box((strip['x'] + strip['dx'] * offset, strip['z'] + strip['dz'] * offset, .38),
                     (1.98, .31, .14), 'WoodDeck' if i % 2 else 'WoodLight', angle=angle)
    wood.finish()
    world = bpy.data.worlds.new('Town mist daylight')
    world.use_nodes = True
    background = next(n for n in world.node_tree.nodes if n.type == 'BACKGROUND')
    background.inputs['Color'].default_value = (.38, .49, .55, 1)
    background.inputs['Strength'].default_value = .45
    scene.world = world
    data = bpy.data.lights.new('Town late-afternoon sun', 'SUN')
    data.energy = 2.1
    data.color = (1.0, .83, .63)
    data.angle = .13
    obj = bpy.data.objects.new(data.name, data)
    group.objects.link(obj)
    obj.rotation_euler = (math.radians(28), math.radians(-32), math.radians(-28))
    cameras = {
        'Overview': camera('Camera_Overview', (128, -162, 156), (0, 5, 1), 186, scene),
        'Harbor': camera('Camera_Harbor', (-73, -64, 25), (-52, -30, 2), 47, scene),
        'Chapel': camera('Camera_Chapel', (31, -12, 32), (6, 18, 7), 46, scene),
        'Engine': camera('Camera_Engine', (86, -42, 27), (57, -13, 3), 40, scene),
        'Top': camera('Camera_Top', (0, 0, 200), (0, 0, 0), 174, scene),
    }
    scene.camera = cameras['Overview']
    # 设置安全枚举值前读取本机 Blender 的可选项，兼容不同渲染器版本。
    try:
        scene.render.engine = 'BLENDER_EEVEE_NEXT'
    except TypeError:
        try:
            scene.render.engine = 'BLENDER_EEVEE'
        except TypeError:
            scene.render.engine = 'CYCLES'
            scene.cycles.samples = 24
    if hasattr(scene, 'eevee') and hasattr(scene.eevee, 'taa_render_samples'):
        scene.eevee.taa_render_samples = 64
    transforms = [i.identifier for i in scene.view_settings.bl_rna.properties['view_transform'].enum_items]
    if 'AgX' in transforms:
        scene.view_settings.view_transform = 'AgX'
    looks = [i.identifier for i in scene.view_settings.bl_rna.properties['look'].enum_items]
    if 'AgX - Medium High Contrast' in looks:
        scene.view_settings.look = 'AgX - Medium High Contrast'
    scene.render.resolution_x = 1600
    scene.render.resolution_y = 1120
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.render.film_transparent = False
    return cameras


def main():
    for path in (SOURCE, EXPORT, PREVIEW):
        path.mkdir(parents=True, exist_ok=True)
    # 仅在本脚本的独立 --background --factory-startup 进程使用。
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    scene = bpy.context.scene
    scene.name = 'Drowned Bell Town | Authored Low Poly'
    scene.unit_settings.system = 'METRIC'
    scene.unit_settings.scale_length = 1
    materials = {key: make_material(key, color, .8 if key == 'Window' else .15 if key == 'Jade' else 0)
                 for key, color in PALETTE.items()}
    groups = {key: collection(name) for key, name in [
        ('streets', '01 - Stone streets and ritual'), ('buildings', '02 - Twelve service buildings'),
        ('cover', '03 - Authoritative combat cover'), ('structure', '04 - Quays and stilt foundations'),
        ('background', '05 - Distant sea ruins'), ('preview', '90 - Preview only')
    ]}
    cells = create_ground(materials, groups)
    for b in LAYOUT['buildings']:
        create_house(b, materials, groups)
    create_cover(materials, groups)
    create_street_details(materials, groups, cells)
    create_background(materials, groups)

    # 与已有岛屿管线相同的坐标锚点，在 Unity 导入后逐一核对米制和轴向。
    markers = []
    for name, position in [('Origin', (0, 0, 0)), ('X', (1, 0, 0)), ('Y', (0, 1, 0)), ('Z', (0, 0, 1))]:
        obj = bpy.data.objects.new('__BlenderAxis_' + name, None)
        scene.collection.objects.link(obj)
        obj.location = position
        markers.append(obj)
    exported = [o for group in groups.values() if group != groups['preview'] for o in group.objects]
    manifest = {
        'version': 1,
        'layoutHash': hashlib.sha256(LAYOUT_TEXT.strip().encode('utf-8')).hexdigest(),
        'materials': [{'name': 'Town_' + key, 'color': dict(zip(('r', 'g', 'b', 'a'), (*rgb, 1)))}
                      for key, rgb in PALETTE.items()],
        'meshes': [{'name': o.name, 'materials': [m.name for m in o.data.materials],
                    'vertices': len(o.data.vertices), 'triangles': sum(len(p.vertices) - 2 for p in o.data.polygons)}
                   for o in exported if o.type == 'MESH'],
        'buildingCount': len(LAYOUT['buildings']),
        'coverCount': sum(o['building'] == 0 for o in LAYOUT['obstacles']),
        'groundCellCount': len(cells),
    }
    (EXPORT / 'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    bpy.ops.object.select_all(action='DESELECT')
    for obj in exported + markers:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = exported[0]
    rna = bpy.ops.export_scene.fbx.get_rna_type()
    def enum(prop, wanted):
        choices = [i.identifier for i in rna.properties[prop].enum_items]
        if wanted not in choices:
            raise ValueError((prop, wanted, choices))
        return wanted
    bpy.ops.export_scene.fbx(filepath=str(EXPORT / 'DrownedBellTown.fbx'), use_selection=True,
        global_scale=1, apply_unit_scale=True, apply_scale_options=enum('apply_scale_options', 'FBX_SCALE_UNITS'),
        axis_forward=enum('axis_forward', '-Z'), axis_up=enum('axis_up', 'Y'),
        object_types={'MESH', 'EMPTY'}, bake_anim=False, use_mesh_modifiers=True,
        mesh_smooth_type=enum('mesh_smooth_type', 'FACE'), add_leaf_bones=False)
    cameras = create_preview(materials, groups['preview'], scene)
    bpy.ops.object.select_all(action='DESELECT')
    for area in bpy.context.screen.areas if bpy.context.screen else []:
        if area.type == 'VIEW_3D':
            area.spaces.active.region_3d.view_perspective = 'CAMERA'
            area.spaces.active.shading.type = 'MATERIAL'
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE / 'DrownedBellTown.blend'), compress=True)
    print('TOWN_ART_MODEL_READY', json.dumps({
        'meshes': len(manifest['meshes']), 'triangles': sum(m['triangles'] for m in manifest['meshes']),
        'ground_cells': len(cells), 'buildings': manifest['buildingCount'], 'covers': manifest['coverCount']
    }), flush=True)
    if '--render' in sys.argv:
        for name, view in cameras.items():
            scene.camera = view
            scene.render.filepath = str(PREVIEW / (name.lower() + '.png'))
            bpy.ops.render.render(write_still=True)
            print('TOWN_ART_RENDERED', name, flush=True)


if __name__ == '__main__':
    main()
