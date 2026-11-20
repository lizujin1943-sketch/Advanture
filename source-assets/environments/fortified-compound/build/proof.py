"""Reference/render analysis and contact sheets; never substitutes generated imagery for Blender evidence."""
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
import numpy as np,json
root=Path(__file__).resolve().parents[1];out=root.parents[2]/'artifacts/fortified-compound-reconstruction-20261119'
ref=Image.open(root/'ref/target.png').convert('RGBA');final=Image.open(out/'fortified-compound-4k.png').convert('RGBA');small=final.resize(ref.size,Image.Resampling.LANCZOS)
a=np.array(ref)[:,:,3]>128;b=np.array(small)[:,:,3]>128
metrics={'reference_size':ref.size,'render_size':final.size,'render_mode':final.mode,'silhouette_IoU':float((a&b).sum()/(a|b).sum()),'metric_scope':'Only alpha-mask overlap. NOT a texture, feature, topology or whole-image fidelity score.','alpha_range':list(final.getchannel('A').getextrema())}
(out/'comparison-metrics.json').write_text(json.dumps(metrics,ensure_ascii=False,indent=2))
fontpath='/System/Library/Fonts/Supplemental/Arial.ttf'
font=ImageFont.truetype(fontpath,28)
def composite(im):
 bg=Image.new('RGBA',im.size,(218,218,213,255));bg.alpha_composite(im);return bg.convert('RGB')
def tile(canvas,im,box,label):
 x,y,w,h=box;im=composite(im).copy();im.thumbnail((w,h-55),Image.Resampling.LANCZOS)
 canvas.paste(im,(x+(w-im.width)//2,y+55+(h-55-im.height)//2));ImageDraw.Draw(canvas).text((x+18,y+15),label,fill=(40,45,48),font=font)
canvas=Image.new('RGB',(3344,1020),(238,238,234));tile(canvas,ref,(0,0,1672,1020),'REFERENCE / supplied final image');tile(canvas,small,(1672,0,1672,1020),'BLENDER / actual 3D render, iteration 08');canvas.save(out/'reference-comparison.jpg',quality=94)
canvas=Image.new('RGB',(2400,1420),(238,238,234))
for i,(name,label) in enumerate([('view-top.png','TOP / complete footprint'),('view-three-quarter.png','THREE QUARTER / actual geometry'),('view-front.png','FRONT / south'),('view-back.png','BACK / north (hidden details inferred)'),('view-left.png','LEFT / west'),('view-right.png','RIGHT / east')]):tile(canvas,Image.open(out/name).convert('RGBA'),((i%3)*800,(i//3)*710,800,710),label)
canvas.save(out/'multiview-review.jpg',quality=94)
final.resize((1672,940),Image.Resampling.LANCZOS).save(out/'08-final-preview.png')
print(json.dumps(metrics))
