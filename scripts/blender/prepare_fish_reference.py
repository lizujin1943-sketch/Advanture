"""Prepare native-resolution reference UV atlases for the Blender art build.

This extracts the approved concept, retaining its authored pixels. Body-only
patches remove features that are rebuilt as separate, skinned 3D anatomy.
Run with the host Python (Pillow); Blender itself needs no extra packages.
"""
from collections import deque
from pathlib import Path
import json
import math
from PIL import Image, ImageDraw, ImageFilter
from fish_reference_layout import LAYOUT

ROOT=Path(__file__).resolve().parents[2]
SOURCE=ROOT/'source-assets/creatures/fish'
OUT=SOURCE/'reference-atlases'
OUT.mkdir(parents=True,exist_ok=True)
concept=Image.open(SOURCE/'Concept-Pixel.png').convert('RGB')


def silhouette(image):
    w,h=image.size
    pixels=image.load()
    possible=set()
    for y in range(h):
        for x in range(w):
            c=pixels[x,y]
            if min(c)<135 or max(c)-min(c)>75:
                possible.add(y*w+x)
    largest=set()
    while possible:
        seed=possible.pop()
        group={seed}
        queue=deque([seed])
        while queue:
            p=queue.popleft()
            for q in (p-1,p+1,p-w,p+w):
                if q in possible and abs(q%w-p%w)<=1:
                    possible.remove(q)
                    group.add(q)
                    queue.append(q)
        if len(group)>len(largest):
            largest=group
    mask=Image.new('L',(w,h),0)
    for p in largest:
        mask.putpixel((p%w,p//w),255)
    # Close antialiased pinholes, then fill the cream-coloured interior.
    mask=mask.filter(ImageFilter.MaxFilter(3)).filter(ImageFilter.MinFilter(3))
    exterior=mask.copy()
    # A fin may touch the crop boundary. All border-connected cream is outside;
    # one flood seed would mistake the wedge beside a clipped tail for skin.
    border=[(x,y) for x in range(w) for y in (0,h-1)]
    border += [(x,y) for x in (0,w-1) for y in range(h)]
    for point in border:
        if exterior.getpixel(point)==0:
            ImageDraw.floodfill(exterior,point,128)
    mask=exterior.point(lambda p:0 if p==128 else 255)
    return mask


def extend_edges(image,mask):
    """Colour dilation prevents cream background on a mesh silhouette seam."""
    pixels=image.load()
    bits=mask.load()
    w,h=image.size
    reached=bytearray(w*h)
    queue=deque()
    for y in range(h):
        for x in range(w):
            if bits[x,y]:
                reached[y*w+x]=1
                if x==0 or y==0 or x==w-1 or y==h-1 or any(
                        not bits[a,b] for a,b in ((x-1,y),(x+1,y),(x,y-1),(x,y+1))):
                    queue.append((x,y))
    while queue:
        x,y=queue.popleft()
        for a,b in ((x-1,y),(x+1,y),(x,y-1),(x,y+1)):
            if 0<=a<w and 0<=b<h and not reached[b*w+a]:
                reached[b*w+a]=1
                pixels[a,b]=pixels[x,y]
                queue.append((a,b))
    return image


def clean_body(image,art):
    if not art.get('puffer'):
        # Facial relief is sculpted into the body itself. A continuous native
        # projection avoids either duplicate eyes or rectangular repair patches.
        return image.copy()
    result=image.copy()
    pix=result.load()
    w,h=image.size
    if art.get('puffer'):
        # A continuous skin for the genuinely spherical, two-eye frontal fish.
        # Palette and clustered pixel marks follow the source; no feature
        # rectangles or a duplicated front-view face on the back of its body.
        for y in range(h):
            for x in range(w):
                bx,by=x//3,y//3
                boundary=127+5*math.sin(bx*.24)+3*math.sin(bx*.61)
                if y<boundary:
                    t=max(0,min(1,(y-35)/90))
                    a,b=(159,99,30),(238,177,62)
                else:
                    t=max(0,min(1,(y-145)/125))
                    a,b=(255,236,186),(218,191,143)
                color=tuple(round(a[k]*(1-t)+b[k]*t) for k in range(3))
                cellx,celly=bx//7,by//7
                dx=(bx%7)-(2+(cellx+celly*3)%3)
                dy=(by%7)-(2+(cellx*3+celly)%3)
                if y<boundary-6 and dx*dx+dy*dy<2+((cellx+celly*3)%4):
                    color=(128,76,31) if (cellx+celly)%3 else (175,108,34)
                elif (bx*5+by*13)%31==0:
                    color=tuple(min(255,c+15) for c in color)
                pix[x,y]=color
    return result


palette=[(236,160,49),(255,239,192),(141,88,35),(240,158,27),
         (247,188,69),(108,61,32),(36,21,32),(255,242,207),
         (18,12,22),(255,255,239),(238,100,59),(137,45,35),
         (72,29,56),(251,190,72),(75,104,130),(255,218,123)]
palette += [tuple(round(c*.75) for c in color) for color in palette]

for species_id,art in LAYOUT.items():
    col,row=art['cell']
    y=[0,315,640][row]
    crop=concept.crop((col*384,y,col*384+384,y+300))
    mask=silhouette(crop)
    extended=extend_edges(crop.copy(),mask)
    body=clean_body(extended,art)
    atlas=Image.new('RGB',(1024,512),(37,28,41))
    atlas.paste(body,(0,0))
    atlas.paste(extended,(512,0))
    d=ImageDraw.Draw(atlas)
    for i,color in enumerate(palette):
        d.rectangle((i*32,416,i*32+31,479),fill=color)
    atlas.save(OUT/f'{species_id}_reference.png')
    mask.save(OUT/f'{species_id}_mask.png')
    print('REFERENCE_ATLAS',species_id)

(OUT/'provenance.json').write_text(json.dumps({
    'source':'../Concept-Pixel.png',
    'method':'Native concept pixel UVs and edge colour dilation; puffer skin redrawn from the reference palette for its frontal 3D eyes. No new concept synthesis.',
    'atlas_size':[1024,512],
    'body_region':[0,0,384,300], 'anatomy_region':[512,0,384,300],
    'species':list(LAYOUT),
},ensure_ascii=False,indent=2)+'\n')
