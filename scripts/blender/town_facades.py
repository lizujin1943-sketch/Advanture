"""建筑表面细节：窗框、石砌墙脚、木梁和海风痕迹，不增加道路障碍。"""
import math


def dress_facade(building, batch, roof, ground):
    x, y, w, d = (building[k] for k in ('x', 'z', 'width', 'depth'))
    bid, kind = building['id'], building['kind']
    front, back = y - d / 2, y + d / 2
    # 可进入建筑的布局坐标标的是墙中心，墙厚 0.5 米；贴面须越过外侧 0.25 米。
    wall_offset = .25 if building['enterable'] else 0
    # 长侧墙按木骨架分格；墙脚砖与窗台均贴着现有墙面。
    for side in (-1, 1):
        face = x + side * (w / 2 + wall_offset + .045)
        for level in (ground + 1.02, ground + 3.65):
            batch.box((face, y, level), (.10, d, .14), 'Wood')
        for j in range(5):
            cy = front + .22 + j * (d - .44) / 4
            batch.box((face, cy, ground + 2.5), (.12, .14, 4.9), 'Wood')
        for row in range(3):
            for j in range(max(2, round(d / 1.1))):
                length = d / max(2, round(d / 1.1))
                cy = front + length * (j + .5)
                batch.box((face, cy, ground + .15 + row * .27), (.075, length - .045, .23),
                          'Stone' if (row + j + bid) % 4 else 'Moss')
        for cy in (y - d * .27, y + d * .27):
            for edge in (-1, 1):
                batch.box((face + side * .05, cy + edge * .68, 2.6), (.12, .14, 1.9), 'Limestone')
            for z in (1.65, 3.55):
                batch.box((face + side * .05, cy, z), (.16, 1.55, .15), 'Limestone')
            # 百叶板错开明窗，保留大块暖色玻璃。
            for edge in (-1, 1):
                batch.box((face, cy + edge * 1.0, 2.6), (.10, .42, 1.7), 'Roof')
                for k in range(5):
                    batch.box((face + side * .06, cy + edge * 1.0, 1.9 + k * .30), (.05, .4, .045), 'RoofLight')
        # 从屋檐垂下的零碎海藻均在头部以上，避免看似能挡路的装饰。
        for j in range(3):
            cy = front + (j + .8) * d / 4
            for k in range(4):
                length = .13 + .04 * ((j + k + bid) % 3)
                batch.quad([(face + side * .08, cy + .12 * math.sin(k), ground + 4.9 - k * .27),
                            (face + side * .10, cy + .18, ground + 4.8 - k * .27),
                            (face + side * .12, cy + .10, ground + 4.8 - k * .27 - length)], 'Moss')

    if building['enterable']:
        # 门洞两侧砌石，不跨越 2.8 米的南门通行面。
        for side in (-1, 1):
            for row in range(8):
                cx = x + side * 1.65
                batch.box((cx, front - wall_offset - .06, ground + .27 + row * .48), (.35, .10, .40), 'Limestone')
        for side in (-1, 1):
            cx = x + side * (w / 2 - .16)
            for cy in (front + 2.2, back - 2.0):
                # 内侧的细高竖窗/壁龛，仍完全嵌在墙厚之内。
                batch.box((cx - side * .10, cy, 3.0), (.045, .9, 2.4), 'Metal')
                batch.box((cx - side * .13, cy, 3.0), (.035, .45, 2.1), 'Jade' if kind == 'Chapel' else 'Window')
                for z in (2.35, 3.0, 3.65):
                    batch.box((cx - side * .15, cy, z), (.04, .75, .06), 'Brass')
    else:
        # 山墙木桁架与不同朝向的烟囱为小商铺提供更清楚的轮廓。
        top, rise = ground + 5, 2.2 if w < 8 else 2.9
        for yy in (front - .44, back + .44):
            for sign in (-1, 1):
                batch.beam((x + sign * (w / 2 - .25), yy, top + .1), (x, yy, top + rise - .1), .14, 'WoodLight')
            batch.beam((x, yy, top), (x, yy, top + rise - .1), .12, 'WoodLight')
        if kind not in ('Engine', 'Workshop', 'Cartography'):
            cx, cy = x - w * .28, y + d * .26
            roof.box((cx, cy, top + 1.1), (.7, .8, 2.8), 'WallRust', bevel=.07)
            roof.box((cx, cy, top + 2.6), (.95, 1.05, .22), 'Limestone')
            roof.box((cx, cy, top + 2.73), (.55, .65, .04), 'Metal')

    # 墙面斑驳用贴面块而不是噪声贴图，让低多边形画面仍然清楚。
    for side in (-1, 1):
        cx = x + side * w * .38
        for row in range(3):
            batch.box((cx + side * row * .07, front - wall_offset - .065, ground + .30 + row * .27),
                      (.8 - row * .12, .035, .21), 'StoneWarm')


def dress_cover(batch, x, y, w, d, h, index, ground):
    """既有遮挡体按港口货垛/旧石墙处理，细节仍收在障碍轮廓附近。"""
    if index < 30:
        return
    for side in (-1, 1):
        face = y + side * (d / 2 + .025)
        for n in range(3):
            xx = x - w * .34 + n * w * .34
            batch.box((xx, face, ground + h / 2), (.08, .05, h), 'Metal' if index % 3 else 'Mortar')
        if index % 3:
            batch.box((x, face + side * .04, ground + h * .55), (.62, .035, .50), 'ClothCream')
            batch.box((x, face + side * .065, ground + h * .55), (.36, .025, .08), 'Wood')
    # 折叠苫布紧贴货垛顶面，不把装饰箱堆成新的碰撞高度。
    if index % 3:
        batch.box((x, y, ground + h + .10), (w * .68, d * .85, .12), 'Roof' if index % 2 else 'Cloth')
        for n in (-1, 1):
            batch.box((x + n * w * .19, y, ground + h + .18), (.055, d * .85, .035), 'WoodLight')
