#!/usr/bin/env python3
"""Draw the original monochrome Hook Island HUD atlas (Pillow, no external art)."""
from pathlib import Path
import json
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
DEST = ROOT / 'samples/TacticalGame.Unity/Assets/HookIsland/Resources/UI'
NAMES = ['Coin','Wood','Bone','Heart','Stamina','Backpack','Build','Menu','Help','Shop',
         'SwimRing','Skull','Rescue','Shield','Hunger','Hook','Fish','Close','Recover','Check','Alert']
CELL, SUPER = 64, 4
atlas = Image.new('RGBA', (CELL*8, CELL*3))

for index,name in enumerate(NAMES):
    im=Image.new('RGBA',(CELL*SUPER,CELL*SUPER)); d=ImageDraw.Draw(im); unit=CELL*SUPER/32
    def pts(v): return [(round(x*unit),round(y*unit)) for x,y in v]
    def line(v,w=2.1,fill='white'):
        d.line(pts(v),fill=fill,width=round(w*unit),joint='curve')
        for x,y in (v[0],v[-1]):
            r=w/2;d.ellipse([(x-r)*unit,(y-r)*unit,(x+r)*unit,(y+r)*unit],fill=fill)
    def ellipse(box,w=2.1,fill=None): d.ellipse([round(v*unit) for v in box],outline='white',width=round(w*unit),fill=fill)
    def rect(box,r=2,w=2.1): d.rounded_rectangle([round(v*unit) for v in box],radius=round(r*unit),outline='white',width=round(w*unit))
    def poly(v): d.polygon(pts(v),fill='white')
    def arc(box,start,end,w=2.1):d.arc([round(v*unit) for v in box],start,end,fill='white',width=round(w*unit))
    if name=='Coin':
        ellipse((5,5,27,27));ellipse((9,9,23,23),1.5);line([(16,12),(16,20)])
    elif name=='Wood':
        line([(6,11),(23,6),(27,21),(10,26),(6,11)]);line([(11,14),(22,11)],1.6);line([(13,21),(23,18)],1.6);ellipse((15,14,19,18),1.4)
    elif name=='Bone':
        line([(7,16),(27,16)]);line([(8,16),(4,11),(4,21),(8,16)]);line([(13,11),(16,16),(13,21)]);line([(20,11),(23,16),(20,21)]);ellipse((25,13,30,19),1.6)
    elif name=='Heart':
        # Sample two cubic lobes and the pointed lower contour.
        v=[]
        for a,b,c,e in [((16,26),(0,17),(4,3),(13,7)),((13,7),(15,8),(16,10),(16,10)),((16,10),(25,-1),(35,12),(16,26))]:
            for k in range(21):
                t=k/20;u=1-t;v.append((u*u*u*a[0]+3*u*u*t*b[0]+3*u*t*t*c[0]+t*t*t*e[0],u*u*u*a[1]+3*u*u*t*b[1]+3*u*t*t*c[1]+t*t*t*e[1]))
        poly(v)
    elif name=='Stamina': poly([(18,3),(7,18),(15,18),(13,29),(26,13),(18,13)])
    elif name=='Backpack':
        rect((6,8,26,28),5);arc((11,3,21,14),180,360);rect((10,17,22,24),2,1.8);line([(12,12),(20,12)],1.7)
    elif name=='Build':
        line([(7,26),(20,10)],4);line([(13,6),(19,4),(28,12),(24,17),(13,6)],2.2)
    elif name=='Menu':
        for y in (9,16,23):line([(7,y),(25,y)])
    elif name=='Help':
        ellipse((4,4,28,28));arc((11,8,21,18),185,365);line([(20,16),(16,19),(16,20)]);ellipse((15,24,17,26),fill='white')
    elif name=='Shop':
        line([(5,13),(8,5),(24,5),(27,13)]);line([(5,13),(27,13)]);line([(7,15),(7,27),(25,27),(25,15)]);rect((13,19,20,27),0,1.6)
        for x in (10,16,22):line([(x,7),(x,12)],1.5)
    elif name=='SwimRing':
        ellipse((4,4,28,28));ellipse((10,10,22,22));
        for v in [[(8,8),(12,12)],[(24,8),(20,12)],[(8,24),(12,20)],[(24,24),(20,20)]]:line(v)
    elif name=='Skull':
        arc((5,4,27,26),150,390);line([(7,21),(10,23),(10,28),(22,28),(22,23),(25,21)]);ellipse((10,13,14,18),fill='white');ellipse((18,13,22,18),fill='white');line([(14,25),(14,27)],1.3);line([(18,25),(18,27)],1.3)
    elif name=='Rescue':
        ellipse((4,4,28,28));line([(16,10),(16,22)],3);line([(10,16),(22,16)],3)
    elif name=='Shield':line([(16,4),(26,8),(24,20),(16,28),(8,20),(6,8),(16,4)])
    elif name=='Hunger':
        line([(15,27),(15,8)]);line([(15,21),(8,17),(6,10)]);line([(15,17),(22,13),(24,6)]);line([(15,12),(11,7)],2)
    elif name=='Hook':
        line([(21,4),(21,20)]);arc((6,13,23,28),0,180);line([(7,21),(7,17),(11,20)]);ellipse((18,2,24,8),1.7)
    elif name=='Fish':
        line([(7,16),(2,10),(2,22),(7,16)]);ellipse((7,8,29,24));ellipse((23,13,25,15),fill='white');line([(14,8),(18,4),(21,8)],1.7)
    elif name=='Close':line([(9,9),(23,23)]);line([(9,23),(23,9)])
    elif name=='Recover':arc((5,5,27,27),40,315);line([(20,5),(26,5),(26,11)]);line([(16,11),(16,21)]);line([(12,17),(16,13),(20,17)])
    elif name=='Check':line([(7,16),(13,23),(26,9)],2.6)
    elif name=='Alert':line([(16,4),(29,27),(3,27),(16,4)]);line([(16,12),(16,18)]);ellipse((15,22,17,24),fill='white')
    im=im.resize((CELL,CELL),Image.Resampling.LANCZOS);atlas.alpha_composite(im,((index%8)*CELL,(index//8)*CELL))

DEST.mkdir(parents=True,exist_ok=True)
atlas.save(DEST/'HudIcons.png')
(ROOT/'docs/minimal-hud-icons.json').write_text(json.dumps({'cell':CELL,'columns':8,'rows':3,'icons':NAMES},indent=2)+'\n')
print(DEST/'HudIcons.png')
