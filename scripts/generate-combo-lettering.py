#!/usr/bin/env python3
"""Build the project's original Hook Rush numeral font. Requires fonttools==4.67.0.

The checked-in TTF is the runtime asset; Python/fonttools are authoring tools only.
Outlines are authored here, rather than traced from a third-party font. Coordinates
use a 100-unit, top-down cap box, then gain a 13.5-degree forward lean.
"""
from datetime import datetime, timezone
from pathlib import Path

from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.ttLib import TTFont


# First contour is the ink outline; subsequent contours are counters (holes).
# Wide stems, clipped corners and an asymmetrical waist make the small HUD legible.
OUTLINES = {
    "0": [
        [(12, 0), (65, 0), (78, 14), (78, 85), (64, 100), (10, 100), (0, 88), (0, 15)],
        [(27, 24), (51, 24), (55, 29), (55, 70), (49, 77), (26, 77), (23, 72), (23, 31)],
    ],
    "1": [[(8, 18), (29, 0), (56, 0), (56, 76), (74, 76), (68, 100),
            (4, 100), (10, 76), (27, 76), (27, 30), (9, 40)]],
    "2": [[(6, 0), (65, 0), (79, 14), (79, 40), (60, 60), (29, 76), (79, 76),
            (72, 100), (0, 100), (0, 73), (48, 42), (53, 37), (53, 25),
            (24, 25), (20, 36), (0, 36), (0, 14)]],
    "3": [[(0, 0), (65, 0), (79, 14), (79, 39), (69, 49), (79, 60), (79, 85),
            (64, 100), (0, 100), (5, 76), (49, 76), (54, 71), (54, 62),
            (26, 62), (31, 40), (52, 40), (54, 36), (54, 26), (49, 23), (0, 23)]],
    "4": [[(29, 0), (56, 0), (26, 55), (50, 55), (50, 30), (76, 30), (76, 100),
            (50, 100), (50, 79), (0, 79), (0, 58)]],
    "5": [[(7, 0), (78, 0), (71, 24), (27, 24), (27, 39), (64, 39), (78, 53),
            (78, 85), (63, 100), (0, 100), (6, 76), (49, 76), (53, 72),
            (53, 63), (49, 59), (0, 59), (0, 18)]],
    "6": [
        [(17, 0), (74, 0), (68, 24), (28, 24), (24, 29), (24, 40), (64, 40),
         (78, 55), (78, 85), (64, 100), (14, 100), (0, 86), (0, 19)],
        [(25, 62), (49, 62), (54, 67), (54, 74), (49, 78), (28, 78), (24, 74), (24, 66)],
    ],
    "7": [[(0, 0), (80, 0), (80, 20), (45, 100), (15, 100), (49, 25), (0, 25)]],
    "8": [
        [(13, 0), (64, 0), (78, 14), (78, 39), (68, 49), (80, 61), (80, 86),
         (65, 100), (13, 100), (0, 86), (0, 61), (11, 49), (1, 39), (1, 15)],
        [(27, 22), (51, 22), (55, 26), (55, 36), (51, 40), (27, 40), (23, 36), (23, 27)],
        [(27, 61), (53, 61), (56, 65), (56, 75), (52, 79), (27, 79), (23, 75), (23, 66)],
    ],
    "9": [
        [(14, 0), (64, 0), (78, 15), (78, 84), (62, 100), (4, 100), (9, 76),
         (49, 76), (54, 71), (54, 60), (15, 60), (0, 45), (0, 15)],
        [(28, 23), (50, 23), (54, 27), (54, 34), (50, 39), (27, 39), (23, 35), (23, 27)],
    ],
    "x": [[(0, 36), (23, 36), (33, 54), (47, 36), (69, 36), (45, 68),
           (64, 100), (40, 100), (31, 82), (17, 100), (-5, 100), (18, 68)]],
}


def glyph(contours):
    pen = TTGlyphPen(None)
    all_points = []
    for index, contour in enumerate(contours):
        points = [(round((x + (100 - y) * .24 + 8) * 8), (100 - y) * 8)
                  for x, y in contour]
        area = sum(a[0] * b[1] - b[0] * a[1]
                   for a, b in zip(points, points[1:] + points[:1]))
        # TrueType ink contours are clockwise, counters counterclockwise.
        if (index == 0 and area > 0) or (index > 0 and area < 0):
            points.reverse()
        pen.moveTo(points[0])
        for point in points[1:]:
            pen.lineTo(point)
        pen.closePath()
        all_points.extend(points)
    left = min((p[0] for p in all_points), default=0)
    advance = max((p[0] for p in all_points), default=240) + 48
    return pen.glyph(), (advance, left)


def main():
    root = Path(__file__).resolve().parents[1]
    output = root / "samples/TacticalGame.Unity/Assets/HookIsland/Resources/UI/HookRushNumerals.ttf"
    builder = FontBuilder(1000, isTTF=True)
    names = {c: "numeral" + c if c.isdigit() else "multiply" for c in OUTLINES}
    builder.setupGlyphOrder([".notdef", "space", *names.values()])
    builder.setupCharacterMap({32: "space", **{ord(c): name for c, name in names.items()}})
    empty = TTGlyphPen(None).glyph()
    glyphs, metrics = {".notdef": empty, "space": empty}, {".notdef": (600, 0), "space": (240, 0)}
    for char, contours in OUTLINES.items():
        glyphs[names[char]], metrics[names[char]] = glyph(contours)
    builder.setupGlyf(glyphs)
    builder.setupHorizontalMetrics(metrics)
    builder.setupHorizontalHeader(ascent=850, descent=-150, lineGap=0, caretSlopeRise=1000, caretSlopeRun=240)
    builder.setupNameTable({
        "familyName": "Hook Rush", "styleName": "Heavy Oblique",
        "uniqueFontIdentifier": "Advanture.HookRush.1.0", "fullName": "Hook Rush Heavy Oblique",
        "psName": "HookRush-HeavyOblique", "version": "Version 1.000",
        "copyright": "Original numeral outlines authored for the Advanture game project.",
        "description": "Forward-cut arcade numerals for confirmed fishing combos; 0-9 and x.",
    })
    builder.setupOS2(sTypoAscender=850, sTypoDescender=-150, sTypoLineGap=0,
                     usWinAscent=900, usWinDescent=150, usWeightClass=900,
                     usWidthClass=5, fsType=0, fsSelection=0x21, sxHeight=512, sCapHeight=800)
    builder.setupPost(italicAngle=-13.5)
    builder.setupMaxp()
    builder.font["head"].macStyle = 3
    timestamp = int(datetime(2026, 10, 27, tzinfo=timezone.utc).timestamp()) + 2082844800
    builder.font["head"].created = builder.font["head"].modified = timestamp
    builder.font.recalcTimestamp = False
    output.parent.mkdir(parents=True, exist_ok=True)
    builder.save(output)
    with TTFont(output) as result:
        assert all(ord(c) in result.getBestCmap() for c in "0123456789x")
        assert len(result.getGlyphOrder()) == 13
    print(f"{output}: {output.stat().st_size} bytes; 11 original glyphs")


if __name__ == "__main__":
    main()
