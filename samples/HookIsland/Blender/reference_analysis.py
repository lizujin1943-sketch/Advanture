"""Measure the provided reference and derive a ground-cover mask, not baked lighting.

Uses only the user's image. The resulting mask encodes grass/sand placement;
shop, palm, shadows, and water are not projected onto the model as color.
"""
from pathlib import Path
import ast
from collections import deque
import json
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT=Path(__file__).resolve().parent
SOURCE=ROOT.parents[2]/"docs/concepts/hook-island-multiview-v1.png"
OUT=ROOT/"reference_data"


def small_hole_fill(mask,maximum=120):
    height,width=mask.shape
    seen=mask.copy()
    result=mask.copy()
    for y,x in zip(*np.where(~seen)):
        if seen[y,x]:continue
        queue=deque([(y,x)]);seen[y,x]=True
        pixels=[];border=False
        while queue:
            yy,xx=queue.popleft();pixels.append((yy,xx))
            border |= yy==0 or xx==0 or yy==height-1 or xx==width-1
            for ny,nx in ((yy-1,xx),(yy+1,xx),(yy,xx-1),(yy,xx+1)):
                if 0<=ny<height and 0<=nx<width and not seen[ny,nx]:
                    seen[ny,nx]=True;queue.append((ny,nx))
        if not border and len(pixels)<=maximum:
            for yy,xx in pixels:result[yy,xx]=True
    return result


def build():
    OUT.mkdir(exist_ok=True)
    source=Image.open(SOURCE).convert("RGB")
    plan=np.asarray(source)[:650,:760].astype(float)
    tree=ast.parse((ROOT/"build_hook_island.py").read_text())
    trace=ast.literal_eval(next(n.value for n in tree.body if isinstance(n,ast.Assign) and
                               any(isinstance(t,ast.Name) and t.id=="TRACE" for t in n.targets)))
    island=Image.new("L",(760,650),0)
    ImageDraw.Draw(island).polygon(trace,fill=255)
    r,g,b=plan[:,:,0],plan[:,:,1],plan[:,:,2]
    green=(g>r*.94)&(b<r*.70)&(r>42)&(g>58)&(np.asarray(island)>0)
    # Preserve the hand-drawn scallops, while closing holes left by tiny blossoms.
    image=Image.fromarray(np.uint8(green)*255)
    image=image.filter(ImageFilter.MaxFilter(3)).filter(ImageFilter.MinFilter(3))
    filled=small_hole_fill(np.asarray(image)>127,maximum=110)
    # The palm crown/trunk are occluders, not bare-earth markings. Repair only
    # their narrow silhouettes where adjacent visible pixels prove grass beneath.
    repair=Image.fromarray(np.uint8(filled)*255)
    draw=ImageDraw.Draw(repair)
    draw.ellipse((303,210,333,248),fill=255)
    draw.polygon([(315,238),(329,239),(336,263),(323,265)],fill=255)
    draw.ellipse((274,260,302,281),fill=255)
    filled=np.asarray(repair)>127
    mask=Image.fromarray(np.uint8(filled)*255).filter(ImageFilter.GaussianBlur(.45))
    mask.save(OUT/"grass_distribution.png")
    measurements={
        "reference":str(SOURCE),
        "plan_pixel_bounds":[0,0,760,650],
        "pixel_to_world":{"x":"(pixel_x-390)*0.049","y":"(320-pixel_y)*0.049"},
        "mask_purpose":"Grass/sand distribution only, without source colors or shadows.",
        "palette_samples":{},
        "known_multiview_inconsistency":"The FRONT shop and tree are larger relative to island width than in TOP. TOP governs layout; ISOMETRIC governs the primary presentation. This discrepancy prevents a literal pixel-identical fit to every panel by one orthographic 3D model.",
    }
    for name,(x,y,radius) in {
        "grass_north":(270,130,12),"grass_east":(577,345,12),
        "sand":(468,193,10),"roof_bright":(402,277,5),
        "roof_dark":(371,290,5),"trunk":(338,270,4),
    }.items():
        measurements["palette_samples"][name]=np.median(
            plan[y-radius:y+radius,x-radius:x+radius].reshape(-1,3),axis=0).tolist()
    (OUT/"reference_measurements.json").write_text(json.dumps(measurements,ensure_ascii=False,indent=2)+"\n")
    print("Wrote a reference-aligned grass mask and measured palette.")


if __name__=="__main__":build()
