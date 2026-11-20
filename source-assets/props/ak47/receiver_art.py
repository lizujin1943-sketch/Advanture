"""Exterior relief and grip sculpture for the depot prop, in the artist's coordinate frame."""
from math import copysign, cos, hypot, pi, sin

from atlas_layout import remap
from mesh_tools import inset, oval, round_outline, rounded


def grip(w):
    # A short, nearly square shoulder flows into the swept palm and rounded heel.
    # Height, back, front, half breadth, forward drop at the heel.
    sections = [
        (-.0155,-.1075,-.0510,.0155,0), (-.0200,-.1100,-.0474,.0190,0),
        (-.0240,-.1129,-.0464,.0218,0), (-.0260,-.1136,-.0457,.0230,0),
        (-.0287,-.1137,-.0457,.0240,0), (-.0318,-.1128,-.0458,.0243,0),
        (-.0340,-.1119,-.0460,.0227,0), (-.0376,-.1102,-.0460,.0201,0),
        (-.0456,-.1075,-.0460,.0203,0),
        (-.0514,-.1084,-.0460,.0200,0), (-.0543,-.1092,-.0477,.0198,0),
        (-.0564,-.1098,-.0495,.0196,0), (-.0574,-.1102,-.0540,.0194,0),
        (-.0594,-.1109,-.0573,.0191,0), (-.0630,-.1122,-.0586,.0190,0),
        (-.0790,-.1180,-.0644,.0188,0), (-.1020,-.1262,-.0731,.0185,0),
        (-.1212,-.1328,-.0798,.0185,0), (-.1308,-.1351,-.0831,.0188,.0012),
        (-.1366,-.1354,-.0836,.0197,.0027), (-.1412,-.1345,-.0823,.0211,.0045),
        (-.1440,-.1325,-.0815,.0220,.0061), (-.1465,-.1300,-.0816,.0214,.0071),
        (-.1485,-.1257,-.0837,.0192,.0079), (-.1493,-.1200,-.0886,.0138,.0079),
    ]
    count = 32 if w.detailed else 16
    rings = []
    side_uv = []
    for height, back, front, width, drop in sections:
        row, coords = [], []
        for j in range(count):
            angle = 2*pi*j/count
            across, along = sin(angle), cos(angle)
            t = .5 + .5*copysign(abs(along)**.76, along)
            edge_curve=.67+.14*max(0,along)
            row.append((width*copysign(abs(across)**edge_curve, across), back+(front-back)*t, height-drop*t))
            # Both outward cheeks get the grain field in full, with a polished perimeter.
            u = abs(1-2*j/count)
            v = (height-sections[-1][0])/(sections[0][0]-sections[-1][0])
            coords.append(remap('grip',u,v))
        rings.append(row)
        side_uv.append(coords)
    shell = w.part('Grip | continuous moulded resin').rings(rings,'grip')
    for i in range(len(rings)-1):
        for j in range(count):
            k = (j+1) % count
            shell.uv[i*count+j] = (side_uv[i][j],side_uv[i][k],side_uv[i+1][k],side_uv[i+1][j])


def lower_trim(w):
    # All three silhouettes sit on the same filled, static frame in the game mesh.
    contour = [(-.0452,-.0165),(-.0452,-.0281),(-.0451,-.0464),
               (-.0449,-.0543),(-.0438,-.0586),(-.0414,-.0627),
               (-.0378,-.0653),(-.0336,-.0664),(-.0228,-.0664),
               (-.0028,-.0653),(.0096,-.0643),(.0155,-.0630),
               (.0196,-.0605),(.0213,-.0572),(.0215,-.0506),(.0215,-.0165)]
    w.stroke('Guard | flat swept band',[(0,f,h-.0015) for f,h in contour],[.0084]*len(contour),
             'steel',flatten=.235,profile=lambda a,b:rounded(a,b,.56,2 if w.detailed else 1))
    curve=[(-.0240,-.016,.0090,.0044),(-.025,-.023,.0082,.0043),(-.0271,-.0276,.0059,.0041),
           (-.0281,-.032,.0039,.0036),(-.0287,-.037,.0029,.0034),(-.0279,-.0415,.00235,.0031),
           (-.0257,-.0456,.0020,.0028),(-.022,-.0495,.0017,.0025),(-.017,-.0530,.00125,.0019),
           (-.0122,-.0551,.00070,.0013),(-.0114,-.0554,.00035,.0007)]
    rings=[]
    for i,(f,h,depth,width) in enumerate(curve):
        prev,nxt=curve[max(0,i-1)],curve[min(len(curve)-1,i+1)]
        df,dh=nxt[0]-prev[0],nxt[1]-prev[1]
        length=hypot(df,dh)
        rings.append([(x,f-d*dh/length,h+d*df/length) for x,d in
                      oval(width,depth,16 if w.detailed else 8,.83)])
    w.part('Guard | curved cast silhouette').rings(rings,'blued')
    shoulder = [(.0171,-.0160),(.0254,-.0123),(.0487,-.0107),(.0520,-.0173),
                (.0462,-.0306),(.0445,-.0464),(.0412,-.0539),(.0318,-.0720),
                (.0321,-.0619),(.0335,-.0553),(.0246,-.0580),(.0188,-.0564)]
    w.side('Guard | front shoulder and tapered edge',shoulder,0,.0130,'blued',.00155,.0022)
    w.side('Guard | shoulder cheek',[(.019,-.021),(.042,-.019),(.0431,-.0363),
           (.0398,-.0522),(.0242,-.0556),(.0202,-.0527)],.0125,.0017,'blued',.0010,.0024)
    w.pin('Guard | shoulder dimple',.0138,.0329,-.0431,.0027,.0011,'steel')
    w.side('Body | seat above the magazine',[(.036,-.012),(.055,-.010),(.149,-.009),
           (.148,-.020),(.094,-.024),(.055,-.029),(.041,-.026)],
           0,.0246,'steel',.0014,.0021)


def pressed_side(w):
    stations=[(-.135,.0200,-.028),(-.133,.0235,-.030),(-.117,.0275,-.030),
              (-.078,.0283,-.0276),(-.047,.0293,-.0265),(-.008,.0309,-.0243),
              (.032,.0324,-.0221),(.046,.0328,-.0212),(.055,.0334,-.0205),
              (.068,.0340,-.0196),(.100,.0360,-.0174),(.162,.0340,-.0144),
              (.173,.0290,-.0132),(.179,.0200,-.0080)]
    def slope(f,h):
        blend=max(0,min(1,(f-.031)/.038))
        return blend*(.0014-.11*(h-.004))
    rows=[]
    for f,hi,lo in stations:
        mid=(lo+hi)*.5
        face=[(.0246,hi-.0012),(.0269+slope(f,hi),hi),(.0279+slope(f,hi),hi-.0008),
              (.02825+slope(f,hi),hi-.002),(.02838+slope(f,mid),mid),
              (.02820+slope(f,lo),lo+.0014),(.02722+slope(f,lo),lo),(.02465,lo+.0009)]
        rows.append([(x,f,z) for x,z in face])
    w.part('Sidewall | contoured blued sheet').rings(rows,'blued')
    opening=round_outline([(-.040,.029),(-.030,.039),(-.019,.044),(.060,.049),
                           (.067,.043),(.058,.030)],.0021,2 if w.detailed else 1)
    rows=[[(depth,p.x,p.y) for p in inset(opening,cut)] for depth,cut in
          ((.0241,.0008),(.0286,0),(.0292,.00065),(.0289,.0016),(.02735,.0026))]
    w.part('Cover | shadow in folded lip').rings(rows,'blued',cap_region='well')
    w.side('Cover | swept rear shoulder',[(-.131,.024),(-.123,.036),(-.112,.0425),
           (-.039,.045),(-.019,.0433),(-.0286,.0359),(-.0344,.0284),(-.054,.0275)],
           .0258,.0027,'blued',.0011,.0032)
    # The raised side motif has a rounded heel and an almost level lower edge.
    badge = [(-.0685,.0109),(-.0668,.0167),(-.0627,.0209),(-.0568,.0226),
             (-.0469,.0217),(-.0377,.0192),(-.0286,.0184),(-.0203,.0242),
             (-.0111,.0259),(.0047,.0234),(.0213,.0192),(.0345,.0168),
             (.0390,.0172),(.0407,.0214),(.0440,.0219),(.0555,.0181),(.0575,.0173),(.0565,.0121),
             (.0437,.0084),(.0412,.0043),(.0337,.0018),(-.0419,.0018),
             (-.061,.0010),(-.0668,.0043)]
    relief=w.side('Sidewall | broad swept relief',badge,.0302,.0026,'blued',.00095,.0025)
    for v in relief.verts:
        v.x+=.038*(v.z-.009)+.00032*sin((v.y+.063)*19)
    w.side('Sidewall | folded lower edge',[(-.0627,.0026),(-.0286,.0026),(.0055,.0034),
           (.0321,.0043),(.0362,.0068),(.0421,.0076),(.0421,.0043),(.0637,.0034),
           (.0678,.0026),(.0662,-.0007),(.0371,-.0007),(.0296,.0010),(-.0618,-.0015)],
           .0297,.0024,'mag',.00085,.0013)
    if w.detailed:
        ridge = [(-.052,.0200),(-.041,.0173),(-.0318,.0158),(-.027,.0160),
                 (-.020,.0193),(-.012,.0214),(.008,.0172),(.026,.0138)]
        w.stroke('Sidewall | soft pressed crest',[(.0325,f,h) for f,h in ridge],
                 [.0007,.0011,.0013,.00145,.0015,.00135,.0010,.00055],
                 'blued',8,guide=(1,0,0),flatten=.83)
        w.stroke('Sidewall | turned front lip',[(.0328,.0403,.0150),(.0335,.0428,.0143),
                 (.03385,.0515,.0129),(.0342,.0561,.0124)],[.00065,.0010,.00105,.00064],
                 'mag',8,guide=(1,0,0),flatten=.88)

    # The boss is a single convex surface. Concentric changes in slope carry its highlight.
    rings = []
    for depth,r in ((.0313,.0064),(.03265,.00825),(.0336,.0080),(.03395,.0067),
                    (.0346,.0059),(.0358,.0047),(.0367,.0030),(.0370,.0015)):
        rings.append([(depth,-.0573+a,.0126+b) for a,b in oval(r,r,24 if w.detailed else 12)])
    boss=w.part('Sidewall | burnished round boss').rings(rings,'mag',cap_region='edge')
    count=len(rings[0])
    for i in range(5,len(rings)-1):
        for j in range(count):
            boss.uv[i*count+j]=tuple(remap('edge',u,v) for u,v in (
                (.68+.035*i,.19+.50*j/count),(.68+.035*i,.19+.50*(j+1)/count),
                (.715+.035*i,.19+.50*(j+1)/count),(.715+.035*i,.19+.50*j/count)))
    for name,f,h,r,shade in (
            ('rear',-.115,.0192,.0029,'mag'),('centre',-.0851,.0043,.0025,'mag'),
            ('lower',-.0311,-.0113,.0046,'steel'),('forward',.061,-.0050,.0022,'blued')):
        w.pin('Sidewall | small stud '+name,.0280+slope(f,h),f,h,r,.0020,shade)
    w.pin('Sidewall | worn enamel fleck',.0299,-.0311,-.0113,.0026,.0006,'oxide')

    # A depressed oval pressed into a closed skin, with its base covering the filled prop.
    rings = []
    for depth,scale in ((.0271,1.03),(.0286,1.03),(.0305,.94),(.0309,.81),
                        (.02945,.70),(.0289,.64)):
        rings.append([(depth+slope(.0874+a,.0025+b),.0874+a,.0025+b) for a,b in
                      rounded(.0173*scale,.0090*scale,.78,4 if w.detailed else 2)])
    w.part('Sidewall | oval impression').rings(rings,'blued',cap_region='steel')

    w.group='DetailSlide'
    w.side('Slide | upper stepped cheek',[(.055,.029),(.057,.041),(.064,.048),
           (.0745,.0525),(.095,.0533),(.117,.050),(.121,.035),(.102,.030)],
           .0259,.0032,'mag',.00165,.0029)
    w.side('Slide | narrow return lip',[(.057,.0308),(.059,.0359),(.067,.038),
           (.116,.038),(.120,.0355),(.119,.0315)],.0301,.0022,'blued',.0009,.00125)
    w.group='Frame'
