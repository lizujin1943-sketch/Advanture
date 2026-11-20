"""Flat exterior value/colour study. The side is observed, the other views artist-inferred."""
from PIL import Image, ImageDraw, ImageFont
from atlas_layout import PROOFS

S=2
image=Image.new('RGB',(1120*S,592*S),'#deddd5')
draw=ImageDraw.Draw(image)
ink,mid,bright,wood,cut='#283133','#566365','#798380','#99603c','#71452e'

def poly(points,color):
    draw.polygon([(round(x*S),round(y*S)) for x,y in points],fill=color)

def line(points,color,width=1):
    draw.line([(round(x*S),round(y*S)) for x,y in points],fill=color,width=round(width*S),joint='curve')

def label(x,y,value,size,color=ink):
    draw.text((x*S,y*S),value,fill=color,font=ImageFont.load_default(size=size*S))

label(43,25,'FORTIFIED COMPOUND',16)
label(42,50,'Walnut and blued steel',32)
label(44,101,'Side-view study from the supplied image',16)
line([(44,130),(1076,130)],'#9da69d')
# Painted masses express the shoulder line, separation of the walnut pieces, and sweep.
poly([(72,202),(77,195),(189,194),(211,198),(223,197),(250,184),(276,178),
      (282,180),(280,211),(254,220),(237,225),(218,230),(89,271),(68,273)],wood)
poly([(67,273),(65,266),(69,203),(73,195),(79,195),(75,271)],ink)
poly([(80,250),(183,212),(223,201),(247,195),(229,207),(180,222)],cut)
poly([(274,180),(289,164),(310,158),(445,158),(465,154),(505,156),(512,162),
      (506,210),(490,216),(279,219),(273,213)],ink)
poly([(281,182),(300,165),(414,164),(421,168),(297,171)],mid)
line([(285,182),(488,177)],bright,2)
line([(287,210),(489,204)],mid,1)
poly([(300,218),(338,216),(331,238),(316,282),(315,289),(296,290),(281,285),(287,262)],cut)
line([(336,215),(338,237),(344,240),(373,240),(381,236),(384,211)],ink,4)
poly([(382,210),(432,208),(441,230),(451,250),(466,273),(486,292),(507,305),
      (486,336),(474,333),(449,313),(424,284),(407,255),(395,230)],ink)
for off in (7,17,28,37):
    line([(385+off,214),(397+off,244),(411+off,268),(429+off,289),(448+off,305),
          (462+off,314)],mid,2)
poly([(504,172),(514,169),(624,168),(637,169),(637,184),(629,187),(517,185),(505,184)],wood)
poly([(501,189),(522,186),(542,188),(609,187),(630,186),(641,187),(641,207),
      (634,210),(579,210),(553,212),(518,219),(507,220),(501,215)],wood)
line([(533,191),(551,193),(603,192)],cut,2)
poly([(638,174),(692,173),(703,174),(714,188),(790,188),(806,187),(808,180),
      (814,182),(815,198),(639,199),(640,194),(707,191),(693,183),(640,184)],ink)
line([(645,202),(788,202)],mid,2)
poly([(777,190),(781,173),(784,158),(787,152),(797,152),(801,154),(801,176),(797,188)],ink)
poly([(782,182),(787,176),(795,176),(795,182)],'#deddd5')
label(49,352,'SIDE',15)
label(50,376,'Straight cover and shoulder, narrow highlights, visible light between the two walnut pieces.',14)
line([(44,416),(1076,416)],'#a7aaa2')
label(48,443,'TOP  /  inferred',14)
poly([(65,509),(72,503),(165,505),(206,509),(247,506),(251,504),(387,504),(409,506),
      (431,505),(491,505),(503,508),(557,510),(593,510),(597,513),(593,516),
      (542,517),(503,517),(495,521),(430,520),(403,519),(388,522),(246,521),(212,518),
      (166,519),(74,521),(64,518)],ink)
poly([(69,510),(74,506),(185,509),(220,511),(244,508),(245,517),(220,516),(73,518)],wood)
poly([(426,508),(491,508),(498,511),(493,517),(428,517)],wood)
label(660,443,'REAR  /  inferred',14)
poly([(711,475),(738,475),(740,481),(738,531),(734,539),(710,538),(707,533),(708,480)],wood)
line([(709,482),(710,531),(713,537)],ink,3)
label(842,443,'FRONT  /  inferred',14)
poly([(892,489),(898,480),(910,480),(917,489),(914,515),(907,521),(897,518)],ink)
poly([(898,478),(895,466),(896,458),(901,457),(901,468),(906,469),(908,457),
      (913,457),(914,466),(912,480)],mid)
label(44,553,'Side silhouette supplied by the art reference. Width, rear, front and surface wear follow the game art direction.',14)
PROOFS.mkdir(parents=True,exist_ok=True)
image.resize((1120,592),Image.Resampling.LANCZOS).save(PROOFS/'SilhouetteStudy.png')
