#!/usr/bin/env python3
"""Original illustrated inventory icons, authored with the existing Pillow art pipeline.

The CSV catalogs determine coverage and stable IDs; no gameplay data is generated.
Runtime uses the checked-in PNG/JSON atlas, not Python. Requires Pillow >= 10.
"""
import csv
import json
import math
import uuid
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT / "samples/HookIsland.Simulation/Data"
DEST = ROOT / "samples/TacticalGame.Unity/Assets/HookIsland/Resources/UI"
CELL, SCALE, COLUMNS = 128, 3, 8
INK, CREAM = "#17343d", "#fff0cf"
TIERS = ["#a17e56", "#64cfa5", "#57bde0", "#9c92e9", "#efa557", "#ffd576"]


class Canvas:
    def __init__(self):
        self.image = Image.new("RGBA", (CELL * SCALE, CELL * SCALE))
        self.d = ImageDraw.Draw(self.image)

    def box(self, box):
        return tuple(round(v * SCALE) for v in box)

    def line(self, points, color, width=3):
        pts = [(round(x * SCALE), round(y * SCALE)) for x, y in points]
        self.d.line(pts, fill=color, width=round(width * SCALE), joint="curve")
        radius = width / 2
        for x, y in (points[0], points[-1]):
            self.d.ellipse(self.box((x-radius, y-radius, x+radius, y+radius)), fill=color)

    def poly(self, points, fill, outline=INK, width=2.5):
        self.d.polygon([(round(x*SCALE), round(y*SCALE)) for x, y in points], fill=fill)
        if outline:
            self.line(points + [points[0]], outline, width)

    def ellipse(self, box, fill, outline=INK, width=2.5):
        self.d.ellipse(self.box(box), fill=fill, outline=outline, width=round(width*SCALE))

    def rect(self, box, fill, radius=3, outline=INK, width=2.5):
        self.d.rounded_rectangle(self.box(box), round(radius*SCALE), fill=fill,
                                 outline=outline, width=round(width*SCALE))

    def curve(self, points, color, width=3):
        a, b, c, d = points
        curve = []
        for i in range(31):
            t = i / 30; u = 1-t
            curve.append((u**3*a[0]+3*u*u*t*b[0]+3*u*t*t*c[0]+t**3*d[0],
                          u**3*a[1]+3*u*u*t*b[1]+3*u*t*t*c[1]+t**3*d[1]))
        self.line(curve, color, width)

    def finish(self):
        # All shapes are original; downsampling provides clean small-size silhouettes.
        return self.image.resize((CELL, CELL), Image.Resampling.LANCZOS)


def wood():
    c = Canvas()
    for x, y, length in [(26, 39, 59), (18, 59, 63), (33, 80, 58)]:
        c.poly([(x,y-11),(x+length,y-24),(x+length+9,y-8),(x+8,y+11)], "#98613b")
        c.poly([(x,y-11),(x+length,y-24),(x+length+1,y-18),(x+3,y-6)], "#dbad6b", None)
        c.line([(x+16,y-4),(x+length-2,y-15)], "#5f4631", 2)
        c.line([(x+26,y+1),(x+length+2,y-9)], "#c89455", 2)
        c.ellipse((x-4,y-11,x+15,y+14), "#e9bf80")
        c.ellipse((x+1,y-5,x+10,y+7), None, "#a7794a", 1.5)
        c.line([(x+5,y+1),(x+8,y+4)], "#a7794a", 1.5)
    c.line([(56,32),(61,48),(68,76),(72,90)], INK, 7)
    c.line([(56,32),(61,48),(68,76),(72,90)], "#e8ca92", 4)
    c.line([(61,36),(65,49),(73,77)], "#fff0cf", 1.2)
    return c.finish()


def bone():
    c = Canvas()
    c.poly([(36,66),(13,51),(14,87)], "#b7c9bd")
    c.poly([(35,66),(19,59),(20,78)], CREAM, None)
    for x, y in [(40,64),(54,59),(68,54)]:
        c.line([(x-2,y-16),(x+5,y),(x+10,y+16)], INK, 9)
        c.line([(x-2,y-16),(x+5,y),(x+10,y+16)], "#e4e7ce", 5)
        c.line([(x-2,y-16),(x+2,y-5)], CREAM, 2)
    c.line([(30,69),(91,49)], INK, 10)
    c.line([(30,68),(92,48)], CREAM, 6)
    c.poly([(81,35),(99,31),(114,42),(110,57),(96,65),(82,60),(77,50)], "#dfe2c8")
    c.poly([(82,36),(99,33),(110,42),(94,42),(81,49)], CREAM, None)
    c.ellipse((91,40,101,51), INK, None)
    c.line([(108,53),(99,57)], "#859d94", 2)
    return c.finish()


def rod(row):
    c = Canvas(); tier = int(row["tier"]); family = row["id"].split("_")[-1]
    tones = {"long":"#50b9dd", "fast":"#f17e67", "tough":"#a9b66a",
             "swiftlong":"#72d6b6", "deepline":"#7597cc", "balanced":"#dca861", "starter":"#b78552"}
    tone = tones[family]; metal = TIERS[tier]; heavy = family in ("tough", "deepline")
    top = 12 if family in ("long","deepline","swiftlong") else 23
    if tier == 0: top = 20
    shaft = [(49,107),(58,78),(67,49),(74,top+5),(82,top)]
    c.line(shaft, INK, 8 if heavy else 6)
    c.line(shaft, tone, 4.5 if heavy else 3)
    c.line([(54,92),(59,77),(63,63)], "#f5dd9e", 1.3)
    c.line([(48,110),(55,88)], INK, 13)
    c.line([(48,109),(55,88)], "#a57347" if tier < 3 else "#38535c", 9)
    for n in range(4):
        c.line([(46+n*1.7,105-n*5),(53+n*1.7,107-n*5)], "#e7c18b" if tier < 3 else metal, 1.7)
    if tier:
        reel = 12 if heavy else 9
        c.line([(56,80),(71,83)], INK, 6)
        c.ellipse((64-reel,82-reel,64+reel,82+reel), "#344c56")
        c.ellipse((67-reel,79-reel,65+reel,78+reel), metal)
        c.ellipse((62,75,71,84), "#31505a", CREAM, 1.3)
        c.line([(68,82),(78,88),(82,84)], INK, 3.5)
        c.line([(78,84),(83,86)], metal, 4)
    else:
        c.line([(57,74),(63,75)], CREAM, 3)
        c.line([(64,46),(70,47)], CREAM, 3)
    for y in range(top+12,67,16):
        x = 74-(y-top)*.25
        c.ellipse((x-1,y-2,x+5,y+4), None, "#f2e4b8" if tier else "#b8c8bb", 1.8)
    c.curve([(82,top),(100,23),(91,47),(96,65)], "#b9d0c7", 1.7)
    c.line([(96,65),(96,71)], INK, 3.6)
    c.curve([(96,71),(95,81),(84,79),(88,72)], INK, 3.6)
    c.line([(96,65),(96,71)], "#e4e9df", 1.8)
    c.curve([(96,71),(95,79),(86,78),(88,72)], "#e4e9df", 1.8)
    if family in ("fast","swiftlong"):
        c.poly([(59,59),(51,62),(57,46),(68,42),(62,54),(69,51)], tone, INK, 1.8)
    elif family == "tough":
        c.rect((53,54,69,66), tone, 3)
        c.poly([(57,57),(65,57),(64,62),(60,65),(57,62)], CREAM, None)
    elif family == "deepline":
        c.ellipse((44,67,60,88), "#334e6b", metal, 2)
        c.ellipse((48,71,56,84), None, "#b9cbd2", 1.5)
    elif family == "balanced":
        c.line([(57,67),(65,69)], metal, 5)
    if tier >= 3:
        c.line([(64,45),(70,47)], metal, 4)
        c.line([(60,61),(66,63)], metal, 4)
    if tier == 5:
        c.poly([(69,30),(74,24),(78,30),(73,36)], "#ffde80", INK, 1.5)
    return c.finish()


def backpack(row):
    c = Canvas(); level = int(row["level"])
    colors = ["#a88b61","#be984f","#519b9b","#5ca58a","#5a93b6","#6b9fbe","#8276af","#696c94","#cc974d","#c47f47"]
    color = colors[level-1]; trim = "#f2d694" if level >= 8 else "#c6af79"
    c.curve([(47,29),(45,7),(80,8),(80,29)], INK, 9)
    c.curve([(47,28),(46,13),(78,13),(79,28)], trim, 4)
    c.rect((28,30,38,104), "#405f64", 7)
    c.rect((88,30,99,104), "#405f64", 7)
    if level >= 3:
        c.rect((16,64,37,100), color, 6)
        c.rect((19,65,33,73), trim, 2, None)
    if level >= 4:
        c.rect((91,64,113,102), color, 6)
        c.rect((94,65,110,74), trim, 2, None)
    c.rect((30,29,97,114), "#2d5158", 13)
    c.rect((29,25,94,110), color, 13)
    c.rect((34,29,44,95), "#ffffff22", 6, None)
    c.rect((29,24,94,58), color, 12)
    c.line([(39,29),(83,29)], "#e8deb27f", 2)
    c.line([(38,58),(86,58)], INK, 2)
    for x in (44,75):
        c.rect((x,48,x+7,73), "#795c3b", 2, INK, 1.5)
        c.rect((x-1,58,x+8,66), trim, 1, INK, 1.5)
    if level < 4:
        c.rect((41,77,81,102), "#304d554f", 5)
        c.line([(46,82),(76,82)], trim, 2)
    else:
        for x in (37,65):
            c.rect((x,78,x+24,104), color, 5)
            c.rect((x+2,78,x+22,85), trim, 2, INK, 1.5)
    if level == 1:
        c.poly([(66,34),(81,37),(78,48),(63,45)], "#d4be86", INK, 1.3)
        c.line([(68,36),(65,43)], "#90744c", 1)
    if level >= 5:
        c.line([(102,64),(106,45)], INK, 6)
        c.line([(102,63),(106,45)], trim, 3)
        c.ellipse((99,39,113,51), None, trim, 3)
    if level >= 6:
        c.rect((23,17,104,34), "#47616b", 7)
        c.line([(31,21),(93,21)], "#87a3a5", 2)
        for x in (42,80): c.rect((x,16,x+6,35), trim, 2, INK, 1.3)
    if level >= 8:
        c.poly([(34,60),(41,60),(41,69),(34,72)], trim, None)
        c.poly([(83,60),(90,60),(90,72),(83,69)], trim, None)
    if level >= 9:
        c.ellipse((53,35,71,53), trim, INK, 2)
        c.poly([(62,37),(67,45),(62,43),(57,49)], "#325866", None)
    if level == 10:
        c.rect((13,70,26,103), "#93b6af", 4)
        c.rect((16,66,23,72), trim, 1)
    return c.finish()


def ring(row):
    c = Canvas(); level = int(row["level"])
    colors = ["#73685a","#dc874e","#f2aa55","#66b6b0","#6acb9c","#5899c5","#9b86c7","#d97e7e","#e8be6d","#ebbe5e"]
    color = colors[level-1]
    if level >= 3:
        c.ellipse((10,24,117,110), None, INK, 7)
        c.ellipse((10,24,117,110), None, "#decea9", 3)
    c.ellipse((18,29,110,111), "#304851")
    c.ellipse((15,20,110,105), color)
    if level >= 2:
        # Broad bands remain recognizable when reduced to a 32px inventory cell.
        for box in ((51,21,73,42),(51,83,73,104),(16,54,36,76),(90,54,110,76)):
            c.rect(box, "#f7e7c1", 3, None)
    c.ellipse((36,38,91,89), "#3c6068", INK, 2)
    c.ellipse((40,43,87,85), (0,0,0,0), INK, 2.5)
    c.curve([(25,49),(30,31),(43,28),(50,29)], "#fff4d0", 3)
    c.curve([(80,95),(93,91),(101,81),(103,78)], "#304b6050", 3)
    if level == 1:
        c.poly([(27,71),(41,77),(35,89),(22,83)], "#c6ac78", INK, 1.5)
        c.line([(26,78),(36,82)], "#76613f", 1.5)
        c.line([(80,27),(87,34),(82,40)], "#3d473e", 2)
    if level >= 4:
        for x in (19,96): c.rect((x,47,x+8,65), "#355963", 3, INK, 1.8)
    if level >= 5:
        c.poly([(62,24),(68,30),(62,35),(57,30)], "#315663", None)
    if level >= 6:
        for x in (27,91): c.line([(x,84),(x+7,89)], "#e9d082", 3)
    if level >= 8:
        for x in (27,88): c.ellipse((x,30,x+9,39), "#d9c17e", INK, 1.2)
    if level >= 9:
        c.ellipse((55,85,72,101), "#36596a", "#ffedb3", 1.8)
        c.poly([(63,88),(66,94),(63,97),(60,93)], "#fff0a9", None)
    if level == 10:
        c.line([(41,28),(44,23),(46,29)], "#fff4cc", 2)
        c.line([(82,28),(85,23),(87,30)], "#fff4cc", 2)
    return c.finish()


def apparel(row):
    c = Canvas(); slot = row["slot"]
    if slot == "Hat":
        c.ellipse((12,67,116,101), "#344e57")
        c.ellipse((9,60,113,92), "#c9a661")
        c.poly([(30,68),(35,36),(46,27),(76,27),(90,39),(96,71),(78,79),(45,78)], "#e4c47d")
        c.poly([(74,29),(87,39),(94,69),(78,76),(78,39)], "#b3985f", None)
        c.poly([(31,61),(46,67),(79,68),(94,60),(95,71),(78,79),(45,78),(30,69)], "#467c79")
        c.rect((50,62,75,77), "#b7cbbe", 3)
        c.poly([(62,65),(69,69),(62,74),(56,70)], "#edf0d5", None)
        c.line([(39,39),(48,33),(68,33)], "#fff0b2", 3)
    elif slot == "Clothes":
        c.poly([(36,26),(51,21),(74,21),(89,27),(116,57),(100,74),(90,63),(94,110),(32,110),(35,63),(23,76),(9,57)], "#dc9756")
        c.poly([(75,24),(89,28),(112,58),(102,68),(84,45),(87,104),(64,109),(64,38)], "#bd7345", None)
        c.poly([(49,22),(62,37),(51,50),(37,28)], "#fff0c3")
        c.poly([(75,22),(62,37),(74,50),(89,28)], "#ecd2a0")
        c.line([(62,38),(62,105)], INK, 3)
        c.line([(66,49),(66,105)], "#e8ba77", 1.5)
        for x in (38,71):
            c.rect((x,72,x+18,94), "#ba794b", 3, INK, 1.5)
            c.line([(x+2,75),(x+16,75)], CREAM, 1.5)
        for y in (57,68,100): c.ellipse((57,y,60,y+3), CREAM, None)
        c.line([(17,57),(26,65)], "#f5cb84", 3)
    else:
        for x, y in [(53,30),(14,52)]:
            c.poly([(x+8,y),(x+32,y),(x+35,y+24),(x+51,y+31),(x+52,y+46),(x+42,y+51),(x,y+50),(x-5,y+40),(x+4,y+25)], "#467f82")
            c.poly([(x+3,y+40),(x+44,y+40),(x+52,y+36),(x+52,y+46),(x+42,y+51),(x,y+50),(x-5,y+40)], "#efe2be")
            c.poly([(x+12,y+7),(x+29,y+7),(x+32,y+28),(x+15,y+30)], "#78b6b2", None)
            for n in range(3): c.line([(x+14,y+12+n*6),(x+30,y+14+n*6)], CREAM, 2)
            c.line([(x+7,y+47),(x+39,y+47)], "#718d8a", 1.5)
    return c.finish()


def rows(name):
    with (DATA/name).open(encoding="utf-8") as stream:
        return list(csv.DictReader(stream))


def main():
    entries = [("material_wood", wood()), ("material_fishbone", bone())]
    for name, draw in [("rods.csv",rod),("backpacks.csv",backpack),("swim-rings.csv",ring),("apparel.csv",apparel)]:
        entries.extend((row["id"], draw(row)) for row in rows(name))
    assert len(entries) == len({key for key, _ in entries}), "Duplicate icon IDs"
    atlas = Image.new("RGBA", (COLUMNS*CELL, math.ceil(len(entries)/COLUMNS)*CELL))
    manifest = {"width": atlas.width, "height": atlas.height, "entries": []}
    for i, (key, icon) in enumerate(entries):
        x, y = i % COLUMNS * CELL, i // COLUMNS * CELL
        atlas.alpha_composite(icon, (x,y))
        # Tight bounds allow a tall rod to fill a 1x4 footprint without stretching.
        left, top, right, bottom = icon.getchannel("A").point(lambda a: 255 if a > 12 else 0).getbbox()
        left, top, right, bottom = max(0,left-2), max(0,top-2), min(CELL,right+2), min(CELL,bottom+2)
        manifest["entries"].append({"id": key, "x":x+left,"y":y+top,"width":right-left,"height":bottom-top})
    DEST.mkdir(parents=True, exist_ok=True)
    atlas.save(DEST/"ItemIcons.png")
    (DEST/"ItemIconMap.json").write_text(json.dumps(manifest, indent=2)+"\n")
    metadata = DEST/"ItemIcons.png.meta"
    if not metadata.exists():
        template = (DEST/"HudIcons.png.meta").read_text()
        template = template.replace("10624b2cb47ae47c29c27b4d7824ae8d", uuid.uuid5(uuid.NAMESPACE_URL, "Advanture/UI/ItemIcons").hex)
        template = template.replace("maxTextureSize: 512", "maxTextureSize: 1024")
        metadata.write_text(template)
    print(f"Created {len(entries)} original item icons: {atlas.width}x{atlas.height}, {DEST/'ItemIcons.png'}")


if __name__ == "__main__":
    main()
