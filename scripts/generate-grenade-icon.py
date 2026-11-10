#!/usr/bin/env python3
"""用现有物品图标绘图工具生成手雷图标；不生成或修改玩法数据。需要 Pillow。"""
import importlib.util
import re
import uuid
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def main():
    spec = importlib.util.spec_from_file_location("item_icons", ROOT / "scripts/generate-item-icons.py")
    icons = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(icons)
    c = icons.Canvas()

    # 与场景模型一致：橄榄绿弹体、三道深色环纹、金属压柄与拉环。
    # 大轮廓优先，保证在获得提示的 24 像素尺寸下仍能识别。
    c.ellipse((25, 10, 50, 36), None, icons.INK, 7)
    c.ellipse((25, 10, 50, 36), None, "#b6c9c8", 3.5)
    c.line([(46, 24), (61, 31)], "#7f9da5", 5)
    c.rect((49, 24, 75, 44), "#526873", radius=4, width=3)
    c.line([(54, 29), (70, 29)], "#b6c9c8", 3)
    c.poly([(44, 40), (76, 40), (91, 53), (96, 77), (91, 99),
            (77, 114), (47, 114), (32, 101), (27, 78), (31, 55)], "#718548", width=4)
    c.poly([(76, 43), (88, 55), (92, 77), (87, 97), (75, 109),
            (65, 109), (75, 92), (79, 69)], "#4c6137", None)
    c.line([(42, 51), (36, 62), (33, 78), (38, 96)], "#b3c179", 4)
    for y, x0, x1 in [(61, 31, 91), (80, 30, 94), (98, 35, 88)]:
        c.line([(x0, y), (x1, y)], "#354b3b", 4)
        c.line([(x0 + 6, y - 3), (x1 - 9, y - 3)], "#94a762", 2)
    c.line([(58, 46), (56, 105)], "#51683c", 3)

    # 压柄位于弹体右侧，金属亮边和拉环共同区分药瓶等相近轮廓。
    c.poly([(54, 22), (80, 24), (91, 38), (103, 89), (96, 93),
            (81, 43), (73, 34), (54, 32)], "#718e98", width=3)
    c.line([(57, 25), (78, 27), (87, 40), (99, 86)], "#cfdbd5", 2.5)
    c.ellipse((61, 25, 67, 31), "#dce6da", None)

    dest = ROOT / "samples/TacticalGame.Unity/Assets/HookIsland/Resources/UI/GrenadeIcon.png"
    c.finish().save(dest)
    meta = dest.with_suffix(".png.meta")
    if not meta.exists():
        # 沿用已有独立图标的透明、无 Mipmap 与 Clamp 导入设置，并保留稳定 GUID。
        template = dest.with_name("FryingPanIcon.png.meta").read_text()
        guid = uuid.uuid5(uuid.NAMESPACE_URL, "Advanture/UI/GrenadeIcon").hex
        contents = re.sub(r"^guid: .*", "guid: " + guid, template, count=1, flags=re.MULTILINE)
        meta.write_text("\n".join(line.rstrip() for line in contents.splitlines()) + "\n")
    print(dest)


if __name__ == "__main__":
    main()
