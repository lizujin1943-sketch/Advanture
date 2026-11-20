"""Lay out the actual release-mesh renders for an art review. Requires Pillow and NumPy."""
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageOps
from atlas_layout import PROJECT, PROOFS

RENDERS=PROJECT/'.build/ak47-art/renders'
INK='#eff0e7'
SUB='#aab3ab'
ORANGE='#c7794b'


def typeface(size,bold=False):
    base=Path('/System/Library/Fonts/Supplemental')
    face=base/('Arial Bold.ttf' if bold else 'Arial.ttf')
    return ImageFont.truetype(str(face),size) if face.exists() else ImageFont.load_default(size=size)


def label(image,xy,words,size,colour=INK,bold=False):
    ImageDraw.Draw(image).text(xy,words,font=typeface(size,bold),fill=colour,anchor='lt')


def backdrop(w,h,style='dark'):
    if style=='light':
        edge,centre=(105,115,110),(142,152,142)
    elif style=='detail':
        edge,centre=(38,43,43),(64,70,65)
    else:
        edge,centre=(21,27,28),(54,63,59)
    y,x=np.mgrid[0:h,0:w]
    spot=np.exp(-2.3*((x/w-.53)**2/.78+(y/h-.49)**2/.67))
    fine=np.random.default_rng(149).normal(0,.60,(h,w))
    rgb=np.stack([edge[c]+spot*(centre[c]-edge[c])+fine for c in range(3)],axis=-1)
    return Image.fromarray(rgb.clip(0,255).astype('uint8'))


def render(name,size=None,style='dark',fit=False):
    image=Image.open(RENDERS/(name+'.png')).convert('RGBA')
    if size:
        if fit:
            image=ImageOps.fit(image,size,method=Image.Resampling.LANCZOS)
        else:
            box=image.getchannel('A').getbbox()
            if box:
                image=image.crop(box)
            factor=min((size[0]-24)/image.width,(size[1]-24)/image.height)
            image=image.resize((max(1,round(image.width*factor)),max(1,round(image.height*factor))),
                               Image.Resampling.LANCZOS)
    bg=backdrop(*(size or image.size),style)
    x,y=(bg.width-image.width)//2,(bg.height-image.height)//2
    bg.paste(image,(x,y),image)
    return bg


def put(board,name,x,y,w,h,style='detail',fit=True):
    board.paste(render(name,(w,h),style,fit),(x,y))


def save(image,name):
    image.save(PROOFS/name,quality=92,subsampling=0,optimize=True)


def art():
    hero=render('hero')
    save(hero,'AK47_Hero.jpg')
    w,h=1600,1040
    board=Image.new('RGB',(w,h),'#192022')
    mark=ImageDraw.Draw(board)
    label(board,(46,28),'FORTIFIED COMPOUND',17,SUB,True)
    label(board,(46,60),'AK-47',46,INK,True)
    label(board,(207,83),'Walnut and blued steel',20,SUB)
    mark.line((46,119,1554,119),fill='#56615b',width=1)
    # Keep the entire silhouette visible. The cutout sits in the same field as its
    # background so the heavy end and thin muzzle retain a continuous read.
    board.paste(render('hero',(1508,570)),(46,130))
    mark.line((46,713,1554,713),fill='#56615b',width=1)
    cells=[('receiver','Pressed steel','Detail of the receiver'),('walnut','Oiled walnut','Carved fore-end'),
           ('dorsal','Hand pose','Gloves and reinforced cuffs')]
    gap=14
    width=(1508-2*gap)//3
    for i,(shot,title,note) in enumerate(cells):
        x=46+i*(width+gap)
        put(board,shot,x,732,width,223)
        label(board,(x,974),title,21,INK,True)
        label(board,(x,1004),note,16,SUB)
    save(board,'AK47_Armory.jpg')


def turnaround():
    board=Image.new('RGB',(1600,1100),'#d3d2c5')
    ink,muted='#272f2d','#535e57'
    label(board,(46,29),'FORTIFIED COMPOUND',16,muted,True)
    label(board,(46,59),'Exterior form',39,ink,True)
    label(board,(335,81),'Orthographic views of the exported game mesh',18,muted)
    draw=ImageDraw.Draw(board)
    draw.line((46,113,1554,113),fill='#989e92')
    put(board,'clay-profile',46,132,1012,440,'light',False)
    label(board,(60,149),'PROFILE',16,ink,True)
    put(board,'clay-front',1075,132,233,440,'light',False)
    label(board,(1088,149),'FRONT',16,ink,True)
    put(board,'clay-back',1321,132,233,440,'light',False)
    label(board,(1334,149),'REAR',16,ink,True)
    put(board,'clay-top',46,590,1012,185,'light',False)
    label(board,(60,606),'TOP',16,ink,True)
    put(board,'wire',46,792,1012,265,'detail',False)
    label(board,(60,810),'TRIANGLES',16,INK,True)
    put(board,'clay-dorsal',1075,590,479,467,'light',True)
    label(board,(1088,606),'HAND CONTACT',16,ink,True)
    save(board,'AK47_Turnaround.jpg')


def grip():
    board=Image.new('RGB',(1510,812),'#192022')
    label(board,(40,25),'FORTIFIED COMPOUND',16,SUB,True)
    label(board,(40,55),'Grip study',39,INK,True)
    label(board,(245,76),'Walnut, field canvas and reinforced suede',17,SUB)
    ImageDraw.Draw(board).line((40,107,1470,107),fill='#536059')
    put(board,'dorsal',40,127,704,590,'detail',True)
    put(board,'hands',760,127,710,590,'detail',True)
    label(board,(40,741),'Grip and fore-end contact',22,INK,True)
    label(board,(760,741),'Reverse side',22,INK,True)
    save(board,'AK47_Grip.jpg')


def closed_grip():
    board=Image.new('RGB',(1510,740),'#192022')
    label(board,(40,25),'FORTIFIED COMPOUND',16,SUB,True)
    label(board,(40,55),'Alternate hand pose',38,INK,True)
    ImageDraw.Draw(board).line((40,107,1470,107),fill='#536059')
    put(board,'carry',40,125,704,532,'detail',True)
    put(board,'carry-reverse',760,125,710,532,'detail',True)
    label(board,(40,682),'Palm and thumb',22,INK,True)
    label(board,(760,682),'Finger pads',22,INK,True)
    save(board,'AK47_Carry.jpg')


if __name__=='__main__':
    PROOFS.mkdir(parents=True,exist_ok=True)
    art()
    turnaround()
    grip()
    closed_grip()
