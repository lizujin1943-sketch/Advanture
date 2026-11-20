"""Original walnut and blued-steel exterior, assembled from shaped artist's shells."""
from math import pi, sin, sqrt
from atlas_layout import remap
from mathutils import Vector
from mesh_tools import oval, rounded
from receiver_art import grip, lower_trim, pressed_side


PIVOTS={
    'Frame':(0,0,0), 'WoodStock':(0,-.142,-.010),
    'CurvedMagazine':(0,.060,-.041), 'DetailSlide':(.037,.012,.028),
    'SupportHand':(-.086,.154,-.086), 'SupportCarry':(-.086,.154,-.086),
    'FiringHand':(.064,-.172,-.132),
    'Muzzle':(0,.677,.018),
}


def stock(w):
    w.group='WoodStock'
    start=len(w.parts)
    shape=lambda a,b:rounded(a,b,.33,w.corners)
    # Separate side planes and bevelled shoulders give the wood an oiled edge highlight.
    # The upper line is level through most of the comb, then turns up at the neck.
    sections=[(-.404,.004,-.112,.0255),(-.394,.006,-.111,.027),
              (-.372,.007,-.105,.028),(-.311,.008,-.086,.0275),
              (-.264,.008,-.071,.0255),(-.236,.003,-.062,.0235),
              (-.220,.000,-.057,.0215),(-.207,.001,-.053,.0205),
              (-.179,.014,-.043,.0215),(-.159,.022,-.036,.0230),
              (-.140,.023,-.031,.0240)]
    w.along('Walnut | straight comb and swept shoulder',[
        (f,(hi+lo)*.5,a,(hi-lo)*.5) for f,hi,lo,a in sections],'stock',shape)
    w.along('Heel | wrapped steel',[
        (-.417,-.055,.024,.055),(-.415,-.055,.026,.059),(-.412,-.055,.027,.060),
        (-.405,-.054,.027,.059),(-.401,-.054,.0258,.058)],'steel',shape,(.04,.19,.2,.83))
    w.along('Ferrule | stock junction',[
        (-.148,-.005,.0228,.026),(-.146,-.004,.0243,.0266),
        (-.142,-.004,.0246,.0268),(-.138,-.004,.024,.026)],'steel',shape)
    if w.detailed:
        for side in (-1,1):
            w.pin('Butt plate | pressed stud '+str(side),side*.0264,-.407,-.052,.0035,.0018)
        for i in range(6):
            z=-.091+i*.013
            w.stroke('Heel | grained traction %02d'%i,[(-.019,-.414,z),(-.009,-.4171,z+.0008),
                     (.009,-.4171,z+.0008),(.019,-.414,z)],[.0007,.0009,.0009,.0007],
                     'black',6,guide=(0,0,1),flatten=.72)
    for part in w.parts[start:]:
        for v in part.verts:
            v.y+=.011*max(0.,min(1.,(v.y+.232)/.083))


def top_profile(a,b):
    # A shallow folded crown with real side flats and thin continuous chamfers.
    return [(a*x,b*y) for x,y in (
        (-.91,-1),(.91,-1),(1,-.71),(1,.21),(.985,.47),(.94,.66),(.84,.82),
        (.72,.92),(.61,.98),(.52,1),(-.52,1),(-.61,.98),(-.72,.92),
        (-.84,.82),(-.94,.66),(-.985,.47),(-1,.21),(-1,-.71))]


def map_station(value,stations):
    for (a,lo),(b,hi) in zip(stations,stations[1:]):
        if value<=b:
            return lo+(value-a)*(hi-lo)/(b-a)
    return stations[-1][1]+value-stations[-1][0]


BODY_LENGTH=((-.170,-.155),(-.145,-.131),(-.092,-.100),(-.040,-.042),
             (.0,.0),(.060,.060),(.130,.171),(.168,.197))
FORE_LENGTH=((.083,.122),(.135,.177),(.155,.196),(.350,.370),(.374,.390),
             (.435,.492),(.470,.523),(.571,.619),(.594,.642),(.617,.649),
             (.642,.655),(.648,.660),(.675,.675))


def planar_flats(piece):
    for i,face in enumerate(piece.faces):
        if len(face)==4:
            a,b=(piece.verts[k] for k in face[:2])
            if abs(a.x-b.x)<.000001 or abs(a.z-b.z)<.000001:
                piece.smooth[i]=False


def flatten_body(parts):
    for part in parts:
        for v in part.verts:
            v.x*=.79
            v.y=map_station(v.y,BODY_LENGTH)
            v.z=.003+.77*v.z


def reverse_cheek(w):
    side=-1
    x=side*.0337
    w.side('Sidewall | pressed cheek '+str(side),
           [(-.143,.020),(-.123,.026),(.122,.026),(.148,.014),(.141,-.024),
            (.098,-.020),(.047,-.023),(.004,-.027),(-.124,-.030),(-.143,-.021)],
           x,.0017,'steel',.00085,.0025)
    w.side('Cover | straight rolled lip '+str(side),
           [(-.138,.027),(.103,.030),(.124,.034),(.126,.029),(.102,.025),(-.136,.022)],
           side*.0346,.0021,'edge',.00065,.0015)
    # A deeply die-pressed lozenge and a two-height cheek give the blued plate
    # readable reflections even when every shell has the same grey material.
    shallow=[(-.037,.000),(-.029,.009),(.025,.010),(.039,.004),(.040,-.003),
             (.029,-.010),(-.024,-.010),(-.036,-.006)]
    rings=[]
    for reach,scale in ((.0335,.81),(.0354,1),(.0390,.92),(.0395,.78),(.0357,.65)):
        rings.append([(side*reach,.067+f*scale,-.009+h*scale) for f,h in shallow])
    w.part('Receiver | recessed lozenge '+str(side)).rings(rings,'steel',
                                                          cap_region='black')
    w.side('Cover | shadow facet '+str(side),[(-.096,.032),(-.084,.051),(-.077,.054),
           (-.002,.053),(.009,.047),(.008,.036),(-.016,.032)],
           side*.0329,.0028,'black',.0009,.0018)
    w.side('Cover | stepped cheek '+str(side),[(-.007,.034),(-.003,.046),(.010,.055),
           (.022,.057),(.091,.057),(.105,.051),(.104,.034)],
           side*.0331,.0033,'steel',.0016,.0024)
    w.side('Cover | upper facet '+str(side),[(.009,.047),(.020,.054),(.091,.054),
           (.099,.051),(.095,.047)],side*.0360,.0015,'mag',.0008,.0013)
    for j,(f,h,r) in enumerate([(-.132,.012,.005),(-.125,-.018,.0045),(.116,.013,.0053),
                                 (.129,-.015,.0044),(.097,-.024,.0034)]):
        if w.detailed or j in (0,2):
            w.pin('Body | domed fastener %s.%s'%(side,j),side*.035,f,h,r,.0024,'edge' if j in (0,3) else 'steel')
    w.side('Inventory | mark plate',[(-.092,.016),(-.043,.016),(-.042,-.0006),(-.093,-.0006)],
           -.0354,.0011,'mag',.00055,.0015,'mark')


def frame(w):
    w.group='Frame'
    start=len(w.parts)
    outline=[(-.151,.015),(-.118,.044),(.120,.044),(.151,.031),(.165,.007),
             (.158,-.021),(.140,-.023),(.094,-.024),(.049,-.026),
             (-.006,-.030),(-.078,-.034),(-.139,-.039),(-.149,-.037)]
    w.side('Body | radiused stamped slab',outline,0,.0338,'steel',.003,.006)
    crown=w.along('Cover | pressed crown',[
        (-.152,.015,.024,.008),(-.144,.020,.026,.012),(-.119,.035,.032,.022),
        (-.103,.043,.0345,.026),(-.076,.044,.0345,.026),(.036,.045,.034,.026),
        (.098,.044,.034,.025),(.123,.039,.032,.019),(.133,.034,.030,.016)],'steel',top_profile,(.04,.96,.03,.96))
    for point in crown.verts:
        point.x*=.963
        point.z+=.0005+.0030*max(0,min(1,(point.y+.080)/.18))
    planar_flats(crown)
    if w.detailed:
        for s in (-1,1):
            w.stroke('Cover | long pressed fold '+str(s),
                     [(s*.0209,-.086,.0680),(s*.0212,-.074,.0687),
                      (s*.0211,.038,.0688),(s*.0208,.082,.0683)],
                     [.0007,.0011,.00105,.00065],'steel',8,guide=(1,0,0),flatten=.58)
    reverse_cheek(w)
    flatten_body(w.parts[start:])
    grip(w)
    lower_trim(w)
    pressed_side(w)


def front_window(w):
    # A closed, bevelled silhouette ring; the window reads through either side.
    outer=[(.562,.019),(.569,.038),(.579,.076),(.582,.086),(.586,.090),
           (.600,.090),(.604,.085),(.608,.065),(.611,.030),(.616,.019)]
    inner=[(.579,.041),(.5785,.044),(.5785,.049),(.5805,.0518),(.5835,.053),
           (.595,.053),(.598,.052),(.5993,.049),(.5993,.044),(.5972,.0415)]
    rows=[]
    for (fo,ho),(fi,hi) in zip(outer,inner):
        width=.012 if ho>.070 else .013
        rows.append([(x,fo+(fi-fo)*t,ho+(hi-ho)*t) for x,t in
                     ((-width+.0012,0),(width-.0012,0),(width,.06),(width,.90),
                      (width-.0012,1),(-width+.0012,1),(-width,.90),(-width,.06))])
    w.part('Nose | crown window').rings(rows,'steel',(.18,.90,.22,.82),smooth=False,closed=True)
    if w.detailed:
        for side in (-1,1):
            w.pin('Nose | crown inset '+str(side),side*.0118,.592,.072,.0059,.0012,'mag')
            w.side('Nose | crown lip '+str(side),[(.5825,.081),(.584,.085),(.600,.085),
                   (.602,.0815),(.601,.080),(.583,.080)],side*.0121,.00075,'mag',.00035,.00075)


def fore_end(w):
    w.group='Frame'
    start=len(w.parts)
    span=(.04,.97,.03,.95)
    lower=lambda a,b:rounded(a,b,.40,w.corners)
    # The underside steps down at the receiver, then recedes into a long flatter toe.
    lower_stations=[(.133,-.001,.026,.023),(.142,-.005,.029,.025),
        (.155,-.005,.031,.028),(.171,-.002,.0320,.026),(.184,-.0007,.0323,.024),
        (.193,.0002,.0325,.0228),(.199,.001,.0325,.022),
        (.213,.002,.0330,.0205),(.233,.003,.0330,.020),(.285,.003,.0327,.0195),
        (.295,.003,.0325,.0195),(.301,.003,.0324,.0195),(.307,.003,.0323,.0195),
        (.315,.003,.0321,.0195),(.327,.003,.0314,.0195),
        (.357,.002,.0292,.0198),(.369,.003,.027,.0189)]
    carving=((.133,0),(.176,0),(.184,.12),(.193,.93),(.203,1),(.285,1),
             (.295,.89),(.301,.44),(.307,.07),(.315,0),(.369,0))
    rows=[]
    for f,h,a,b in lower_stations:
        b+=.002
        h+=.002
        cut=.0100*max(0.,min(1.,map_station(f,carving)))
        side=[(.68*a,-b),(.86*a,-.95*b),(.97*a,-.76*b),(a,-.42*b),
              (a,-.28*b),(a-.23*cut,-.15*b),(a-.79*cut,.05*b),(a-cut,.32*b),
              (a-.87*cut,.49*b),(a-.44*cut,.62*b),(a,.74*b),(.91*a,.89*b),(.68*a,b)]
        profile=side+[(-x,z) for x,z in reversed(side)]
        rows.append([(x,f,h+z) for x,z in profile])
    lower_wood=w.part('Fore-end | sculpted recessed walnut').rings(rows,'walnut',span)
    planar_flats(lower_wood)
    upperstations=[(.149,.041,.019,.009),(.159,.047,.021,.012),(.170,.049,.023,.013),
        (.183,.050,.024,.012),(.198,.0515,.0245,.0112),(.229,.0515,.025,.0112),
        (.241,.0485,.0247,.014),(.252,.0485,.0247,.0141),
        (.263,.0515,.0245,.0112),(.310,.0515,.0236,.0111),
        (.333,.050,.0227,.0128),(.355,.049,.022,.0126),(.365,.046,.020,.010)]
    upper_wood=w.along('Fore-end | upper walnut',upperstations,'walnut',
            lambda a,b:rounded(a,b,.42,w.corners),(.07,.92,.13,.83))
    planar_flats(upper_wood)
    w.side('Fore-end | recessed shadow',[(.154,.044),(.350,.044),(.355,.033),(.345,.027),
           (.329,.0254),(.311,.0263),(.274,.0273),(.259,.0268),(.228,.0255),(.214,.0283),
           (.178,.0295),(.165,.027),(.154,.029)],0,.0124,'black',.0006,.0022)
    for i,(f0,f1) in enumerate(((.146,.1606),(.345,.3576))):
        w.case('Fore-end | inner shadow bridge '+str(i),[(f0,.028,.012,.0115),
               (f0+.002,.029,.015,.0125),(f1-.002,.029,.015,.0125),
               (f1,.029,.012,.0115)],'black',.25)
    for i,(f0,f1,a,b,h) in enumerate([(.1319,.151,.031,.0288,-.0008),(.354,.372,.030,.0223,.004)]):
        w.along('Fore-end | pressed ferrule '+str(i),[(f0,h,a-.002,b-.002),
                (f0+.002,h,a,b),(f1-.002,h,a,b),(f1,h,a-.0015,b-.0015)],
                'steel',lower,(.30,.58,.12,.96))
    for i,f in enumerate((.158,.355)):
        w.along('Fore-end | upper cap '+str(i),[(f-.003,.048,.0218,.0118),
                (f-.001,.048,.024,.014),(f+.002,.048,.024,.014),
                (f+.004,.048,.022,.012)],'steel',
                lambda a,b:rounded(a,b,.36,w.corners),(.21,.40,.22,.86))
    w.along('Nose | stepped dark silhouette',[
        (.355,.018,.020,.020),(.368,.018,.020,.020),(.374,.018,.015,.015),(.400,.018,.013,.012),
        (.439,.018,.013,.012),(.446,.018,.014,.013),(.465,.018,.014,.013),(.470,.018,.0125,.0125),
        (.568,.018,.013,.013),(.578,.018,.015,.015),(.610,.018,.015,.015),(.617,.018,.012,.012),
        (.643,.018,.012,.012),(.648,.018,.014,.014)],'steel')
    spar=w.along('Nose | upper rib',[(.350,.052,.012,.012),(.369,.054,.0127,.0137),
            (.381,.054,.0125,.0135),(.438,.054,.0125,.0135),(.449,.046,.0108,.0092)],
            'mag',lambda a,b:rounded(a,b,.33,w.corners))
    planar_flats(spar)
    if w.detailed:
        for side in (-1,1):
            w.side('Nose | oblong side bevel '+str(side),[(.377,.050),(.377,.056),(.381,.0605),
                   (.408,.0605),(.414,.0576),(.412,.053),(.408,.050),(.381,.049)],
                   side*.0123,.0013,'steel',.00065,.0011)
            w.side('Nose | inset strip '+str(side),[(.380,.052),(.380,.055),(.383,.0576),
                   (.406,.0576),(.409,.056),(.408,.053),(.405,.0518)],
                   side*.0133,.00065,'mag',.00032,.0006)
    w.side('Nose | diagonal collar',[(.430,.046),(.435,.067),(.448,.067),(.471,.027),
           (.479,.024),(.478,.007),(.456,.006),(.454,.028),(.441,.041)],0,.018,'steel',.0023,.0035)
    w.along('Nose | sight collar',[(.571,.018,.015,.015),(.576,.018,.019,.018),
            (.598,.018,.019,.019),(.604,.018,.016,.016)],'mag')
    front_window(w)
    w.along('Nose | sealed finial',[(.633,.018,.0113,.0113),(.639,.018,.0127,.0127),
            (.648,.018,.0127,.0127),(.650,.018,.0141,.0141),(.655,.018,.0146,.0146),
            (.671,.018,.0146,.0146),(.675,.018,.0131,.0131)],'steel')
    w.along('Nose | dark solid inset',[(.6741,.018,.0107,.0107),(.6753,.018,.0104,.0104),
            (.6756,.018,.0096,.0096)],'black')
    w.along('Nose | upper cuff',[(.373,.054,.0128,.0134),(.374,.054,.0132,.0142),
            (.3757,.054,.0132,.0142),(.3766,.054,.0128,.0135)],'steel',
            lambda a,b:rounded(a,b,.34,w.corners))
    w.stroke('Nose | lower silhouette line',[(0,.371,-.0015),(0,.388,-.0005),(0,.453,-.0005),
             (0,.590,-.0005),(0,.661,-.0004),(0,.6754,-.0004)],
             [.0031,.0031,.0031,.0031,.0031,.0033],'mag',10 if w.detailed else 6)
    w.case('Nose | lower rail tie',[(.447,.005,.0052,.0070),(.449,.004,.0064,.0080),
            (.459,.004,.0064,.0080),(.461,.005,.0053,.0070)],'steel',.24)
    cheek=w.side('Back crown | sloped cheek',[(.079,.036),(.092,.052),(.109,.071),(.132,.072),
           (.143,.063),(.150,.060),(.157,.060),(.158,.002),(.141,.002),(.131,.020),
           (.106,.031)],0,.0309,'steel',.00083,.0026)
    for point in cheek.verts:
        slope=max(0.,min(1.,(point.z-.018)/.055))
        point.x*=1-.26*slope
    if w.detailed:
        for side in (-1,1):
            w.pin('Back crown | fittings '+str(side),side*.0265,.138,.046,.0046,.0013,'steel')
    w.side('Back crown | stepped bed',[(.079,.046),(.079,.058),(.085,.066),(.099,.066),
           (.105,.072),(.139,.072),(.141,.063),(.157,.063),(.157,.046)],
           0,.0221,'mag',.0011,.0014)
    for side in (-1,1):
        w.side('Back crown | stepped shoulder '+str(side),
               [(.087,.060),(.087,.075),(.092,.079),(.123,.079),(.126,.075),
                (.126,.071),(.141,.071),(.141,.061)],side*.015,.0051,'steel',.0008,.0011)
    if w.detailed:
        w.side('Back crown | inset leaf',[(.093,.077),(.097,.081),(.123,.081),
               (.128,.077),(.128,.073),(.096,.073)],0,.0103,'steel',.0006,.0010)
        for i in range(4):
            f=.101+i*.0059
            w.stroke('Back crown | index '+str(i),[(-.007,f,.081),(-.003,f,.081)],
                     [.00065,.00065],'thread',6,guide=(0,1,0))

    for part in w.parts[start:]:
        for v in part.verts:
            v.y=map_station(v.y,FORE_LENGTH)
            if part.name.startswith('Nose |'):
                v.x*=.81
                v.z=.018+(v.z-.018)*(.88 if 'crown' in part.name else .83)
            elif part.name.startswith('Back crown'):
                v.x*=.80
                v.z=.003+.79*v.z


def mag_path(t):
    # Seat the rolled top under the outer lip. The correction blends out before the heel.
    fade=max(0,min(1,(t-.55)/.38))
    rise=max(0,1-t/.38)
    f=.076+.023*t+.113*t*t+.0133*(1-fade*fade*(3-2*fade))
    h=-.029-.215*t+.027*t*t+.0125*rise*rise
    df=.023+.226*t-.0133*6*fade*(1-fade)/.38
    dh=-.215+.054*t-.025*rise/.38
    length=sqrt(df*df+dh*dh)
    return f,h,-dh/length,df/length


MAG_SIDE=.80
MAG_EDGE=.87


def mag_edge(across):
    return across*MAG_EDGE*(.935 if across<0 else 1.)


def carry_point(x,across,length):
    # Art frame on the curved panel for the alternate hand's authored contact.
    f,h,ef,eh=mag_path(.43)
    across=mag_edge(across)
    return Vector((x*MAG_SIDE,f+across*ef+length*eh,h+across*eh-length*ef))


CARRY_WRIST=carry_point(-.061,-.066,.020)
PIVOTS['CarrySocket']=tuple(CARRY_WRIST)


def magazine(w):
    w.group='CurvedMagazine'
    def ring(t,width,depth):
        f,h,ef,eh=mag_path(t)
        return [(x,f+mag_edge(d)*ef,h+mag_edge(d)*eh) for x,d in rounded(width*MAG_SIDE,depth,.19,w.corners)]
    # Each long flute is part of the stamped surface. The inward and outward slopes
    # span different widths, giving each channel a crescent highlight as it turns.
    marks=[(-1,.73),(-.984,.89),(-.946,.98)]
    polished=[]
    flute_profile=((-.215,0),(-.155,-.54),(-.075,-.88),(-.020,-.86),
                   (.052,.58),(.098,.78),(.160,.50),(.215,0)) if w.detailed else (
                   (-.215,0),(-.020,-.86),(.082,.76),(.160,.47),(.215,0))
    for j in range(4):
        centre=-.69+j*.45
        for rel,raised in flute_profile:
            if rel==-.020:
                polished.append(len(marks))
            marks.append((centre+rel,raised))
    marks.extend([(.945,.98),(.984,.89),(1,.73)])
    rows=[]
    n=21 if w.detailed else 8
    ends=[.065,.090,.908,.929,.942] if w.detailed else [.065,.908,.942]
    stations=sorted(set([.985*i/n for i in range(n+1)]+ends))
    for t in stations:
        f,h,ef,eh=mag_path(t)
        radius=(.0255+.0017*sin(pi*t))*MAG_SIDE
        depth=.050+.0014*sin(pi*t)-.002*t
        relief=.0047*max(0.,min(1.,(t-.065)/.04,(.937-t)/.018))
        side=[]
        for k,(d,r) in enumerate(marks):
            x=radius*r if k<3 or k>=len(marks)-3 else radius+relief*r
            offset=mag_edge(d*depth)
            side.append((x,f+offset*ef,h+offset*eh))
        rows.append(side+[(-x,a,b) for x,a,b in reversed(side)])
    body=w.part('Magazine | four sculpted flutes').rings(rows,'mag')
    count=2*len(marks)
    for i,(t,end) in enumerate(zip(stations,stations[1:])):
        for k in polished:
            for face in (k,count-k-2):
                pos=i*count+face
                body.smooth[pos]=False
                if .10<t<.916:
                    u=.15+.67*t
                    un=.15+.67*end
                    body.uv[pos]=tuple(remap('steel',p,q) for p,q in
                                       ((u,.52),(u,.62),(un,.62),(un,.52)))
    for j,(levels,widths,ds) in enumerate([
            ((.006,.020,.052,.066),(.0255,.0282,.0282,.0264),(.050,.051,.051,.0503)),
            ((.918,.929,.942,.981,.992,1.0),(.026,.031,.0327,.0327,.0337,.029),
             (.0485,.051,.051,.051,.052,.049))]):
        w.part('Magazine | rolled rim '+str(j)).rings([ring(t,a,b) for t,a,b in zip(levels,widths,ds)],
                                                      'steel',(.12,.84,.11,.83))
    toe=[(.969,.0324,.0509),(.973,.0331,.052),(.981,.0336,.0527),(.9891,.0327,.0518)]
    w.part('Magazine | burnished heel edge').rings([ring(t,a,b) for t,a,b in toe],
                                                 'edge',(.21,.74,.22,.73))

    for side in (-1,1):
        for j,offset in enumerate((-.030,-.010,.012,.031)):
            if not w.detailed and j in (0,3):
                continue
            rows=[]
            for t,spread in ((.937,.65),(.942,1),(.960,1),(.964,.65)):
                f,h,ef,eh=mag_path(t)
                profile=[(-1,-.0008),(-1,.0002),(-.64,.0009),(.68,.0009),(1,.0002),(1,-.0008)]
                row=[]
                for d,b in profile:
                    along=mag_edge(offset+d*.0035*spread)
                    row.append((side*(.0262+b),f+along*ef,h+along*eh))
                rows.append(row)
            w.part('Magazine | heel stamping %s.%s'%(side,j)).rings(rows,'mag',(.22,.76,.30,.64))
