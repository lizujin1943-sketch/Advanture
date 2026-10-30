"""Assemble Blender renders into review sheets and a short animated preview."""
from pathlib import Path
import json
from PIL import Image, ImageDraw, ImageFont
from fish_reference_layout import LAYOUT

ROOT=Path(__file__).resolve().parents[2]
SOURCE=ROOT/'source-assets/creatures/fish'
OUTPUT=ROOT/'artifacts/fish-models'
manifest=json.loads((SOURCE/'manifest.json').read_text())
FONT='/System/Library/Fonts/Hiragino Sans GB.ttc'
if not Path(FONT).exists():
    FONT='/System/Library/Fonts/Supplemental/Arial.ttf'
def font(size):
    return ImageFont.truetype(FONT,size)

BG=(245,241,228,255)
INK=(41,50,56)
TEAL=(39,116,111)

def composite(image,size):
    image=image.copy()
    image.thumbnail(size,Image.Resampling.LANCZOS)
    return image


board=Image.new('RGBA',(1600,1160),BG)
draw=ImageDraw.Draw(board)
draw.text((45,24),'像素风海鱼 · 重做后的实际 3D 模型',font=font(36),fill=INK)
draw.text((47,78),'依据确认效果图逐种建形  /  立体眼部、厚嘴唇与鱼鳍  /  保留 78 个动作',font=font(20),fill=TEAL)
for index,entry in enumerate(manifest['species']):
    x=28+(index%4)*394
    y=128+(index//4)*336
    draw.rounded_rectangle((x,y,x+378,y+318),radius=12,fill=(252,249,239),outline=(221,221,205),width=1)
    image=composite(Image.open(OUTPUT/(entry['id']+'.png')).convert('RGBA'),(374,242))
    board.alpha_composite(image,(x+(378-image.width)//2,y+8+(242-image.height)//2))
    text=entry['name']
    draw.text((x+189,y+258),text,font=font(25),fill=INK,anchor='mm')
    draw.text((x+189,y+295),f"{entry['triangles']:,} 三角面 · 可编辑骨骼",font=font(16),fill=TEAL,anchor='mm')
x,y=28+3*394,128+2*336
draw.rounded_rectangle((x,y,x+378,y+318),radius=12,fill=(226,236,223),outline=(199,215,201),width=1)
draw.text((x+26,y+24),'独立动作',font=font(26),fill=INK)
for index,text in enumerate(['待机 / 游动 / 冲刺','跃起 / 入水','受钩挣扎 / 上岸扑腾','食人鱼：额外咬击','河鲀：受钩鼓起']):
    draw.text((x+27,y+78+index*40),text,font=font(21),fill=TEAL)
board.convert('RGB').save(OUTPUT/'fish-model-catalog.png')

# The approved image and the real render share a page for direct inspection.
# These are ordinary composites of source art and Blender output, not new art.
reference=Image.open(SOURCE/'Concept-Pixel.png').convert('RGBA')
comparison=Image.new('RGBA',(1480,1390),BG)
d=ImageDraw.Draw(comparison)
d.text((38,22),'效果图与实际模型对照',font=font(34),fill=INK)
d.text((380,86),'确认的像素效果图',font=font(24),fill=TEAL,anchor='mm')
d.text((1080,86),'重做后的 Blender 实际渲染',font=font(24),fill=TEAL,anchor='mm')
for row,entry_id in enumerate(('fish_03','fish_04','fish_08','piranha')):
    art=LAYOUT[entry_id]
    col,cy=art['cell']
    oy=[0,315,640][cy]
    concept=reference.crop((col*384,oy,col*384+384,oy+(265 if cy==0 else 290)))
    concept=composite(concept,(605,280))
    y=124+row*310
    comparison.alpha_composite(concept,(380-concept.width//2,y+(278-concept.height)//2))
    render=composite(Image.open(OUTPUT/(entry_id+'.png')).convert('RGBA'),(670,290))
    comparison.alpha_composite(render,(1080-render.width//2,y+(278-render.height)//2))
    d.line((45,y+296,1435,y+296),fill=(215,212,199),width=1)
comparison.convert('RGB').save(OUTPUT/'fish-reference-comparison.png')

# Two fish, four views, using the actual full-body meshes.
views=[('', '四分之三'),('_side','侧面'),('_front','正面'),('_top','俯视')]
angles=Image.new('RGBA',(1440,1040),BG)
d=ImageDraw.Draw(angles)
d.text((32,24),'实际模型 · 多角度检查',font=font(32),fill=INK)
for row,entry_id in enumerate(('fish_03','piranha')):
    for col,(suffix,label) in enumerate(views):
        x=col*360
        y=95+row*460
        image=composite(Image.open(OUTPUT/(entry_id+suffix+'.png')).convert('RGBA'),(352,372))
        angles.alpha_composite(image,(x+(360-image.width)//2,y+(372-image.height)//2))
        d.text((x+180,y+404),label,font=font(22),fill=TEAL,anchor='mm')
angles.convert('RGB').save(OUTPUT/'fish-model-angles.png')

directory=OUTPUT/'animation-frames'
frames=json.loads((directory/'frames.json').read_text())
names={'Swim':'游动','Leap':'跃起姿态','Dive':'入水姿态','Hooked':'受钩挣扎',
       'Flop':'上岸扑腾','Bite':'食人鱼咬击'}
animation=[]
for item in frames:
    frame=Image.new('RGBA',(840,276),BG)
    render=Image.open(directory/item['path']).convert('RGBA').crop((0,62,840,274))
    frame.alpha_composite(render,(0,35))
    d=ImageDraw.Draw(frame)
    for x,name in [(175,'赤尾鲷鱼'),(420,'刺球河鲀'),(665,'食人鱼')]:
        d.text((x,23),name,font=font(18),fill=TEAL,anchor='mm')
    d.text((420,257),names[item['clip']],font=font(23),fill=INK,anchor='mm')
    animation.append(frame.convert('RGB'))
animation[0].save(OUTPUT/'fish-actions.gif',save_all=True,append_images=animation[1:],
                  duration=110,loop=0,optimize=False,disposal=2)

poses=Image.new('RGB',(1680,828),BG[:3])
for index in range(6):
    poses.paste(animation[index*8+3],((index%2)*840,(index//2)*276))
poses.save(OUTPUT/'fish-animation-poses.png')
print('PREVIEWS_SAVED',OUTPUT/'fish-model-catalog.png',OUTPUT/'fish-actions.gif')
