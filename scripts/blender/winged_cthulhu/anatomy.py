"""Hand-authored silhouette landmarks from the user's supplied concept; no image projection."""
import math
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from .geometry import Mesh,P,spline

def tube(name,knots,col,m,belly=False,angle=0,cups=False,rows=1,steps=14,sides=40):
    g=Mesh();frames=g.tube(knots,sides,steps,belly,angle)
    if cups:g.suckers(frames,spacing=1.95,rows=rows,offset=0,start=.07,end=.99)
    return g.finish('CR_WingedCthulhu_'+name,col,[m['skin'],m['belly'],m['cups'],m['pits']])

def plate(name,bound,center,depth,bulge,col,mat,thick=.08):
    g=Mesh();g.patch(bound,center,depth,bulge,steps=5,rings=20,thickness=thick)
    return g.finish('CR_WingedCthulhu_'+name,col,[mat])

def build_body(col,m):
    g=Mesh()
    # One closed longitudinal surface: dome, mantle and arm seat. A ring or
    # a stack of overlapping ellipsoids would read as a collar in rear view.
    # Each section is (height, half width, half depth, dorsal offset).
    profile=[(3.91,.05,.06,0),(4.02,.68,.65,0),(4.23,1.08,1.04,0),
        (4.54,1.23,1.18,.02),(4.98,1.18,1.15,.06),(5.56,1.25,1.25,.16),
        (6.24,1.56,1.42,.29),(6.92,1.93,1.58,.39),(7.62,2.21,1.77,.48),
        (8.18,2.22,1.77,.53),(8.78,1.99,1.65,.54),(9.32,1.56,1.38,.53),
        (9.69,1.06,1.02,.52),(9.91,.58,.63,.51),(10.00,.21,.25,.50),
        (10.014,.04,.05,.50)]
    from .crown import trunk_angles
    angles=list(trunk_angles());n=len(angles);previous=None
    for z,rx,ry,cy in spline([Vector(row) for row in profile],7):

        row=[g.vert((rx*math.sin(a),cy-ry*math.cos(a),z)) for a in angles]
        if previous is None:
            pole=g.vert((0,cy,z-.005))
            for i in range(n):g.face((pole,row[i],row[(i+1)%n]),1)
        else:
            for i in range(n):
                j=(i+1)%n;a=angles[i]
                # Pigment boundary follows the temples rather than an equatorial belt.
                mi=0 if z>6.65+.34*math.cos(2*a)+.13*math.sin(3*a) else 1
                g.face((previous[i],previous[j],row[j],row[i]),mi)
        previous=row
    pole=g.vert((0,cy,z+.003))
    for i in range(n):g.face((row[i],pole,row[(i+1)%n]))
    g.finish('CR_WingedCthulhu_Cranium',col,[m['head'],m['skin']])
    # Muscular support connecting the wing girdle to the mantle.
    for name,k in [
        ('Girdle_L',[(750,405,.52,15),(724,377,.66,40),(679,331,.96,49),(591,282,1.05,35),(508,218,1.05,24),(465,81,1.12,20)]),
        ('Girdle_R',[(785,406,.52,15),(833,390,.62,45),(911,366,.78,51),(1008,320,.92,40),(1071,279,1.02,32),(1127,223,1.05,29),(1140,91,1.12,21)])]:tube(name,k,col,m)

def build_arms(col,m):
    # Back arms are drawn first but use actual depth, rather than painter's-order tricks.
    arms=[
        ('Arm_Back_L',[(726,470,.3,62),(612,494,.38,51),(515,517,.42,41),(432,467,.48,32),(423,399,.38,23),(466,371,.25,18),(520,383,.18,14),(535,413,.12,8),(520,426,.1,1)],-.2,True,1),
        ('Arm_Back_R',[(951,589,.45,57),(1084,587,.48,49),(1204,578,.5,40),(1337,566,.4,37),(1401,512,.35,30),(1382,460,.23,24),(1330,438,.1,21),(1281,460,.0,15),(1280,488,-.1,8),(1301,490,-.12,1)],.3,True,1),
        ('Arm_Lower_Back_L',[(718,660,.1,52),(592,757,.24,43),(416,829,.34,35),(258,814,.24,31),(174,850,.1,24),(161,907,0,17),(181,937,-.05,10),(209,927,-.1,1)],.3,True,1),
        ('Arm_Lower_Back_Mid',[(832,618,.25,49),(736,738,.12,40),(639,832,-.05,30),(619,909,-.15,20),(645,959,-.2,11),(667,956,-.2,1)],-.25,True,1),
        ('Arm_Side_L',[(705,588,.2,76),(549,587,.15,67),(413,553,-.25,61),(291,481,-.65,62),(182,483,-1.0,55),(105,559,-1.15,43),(80,654,-1.3,35),(105,741,-1.4,27),(159,770,-1.45,20),(198,742,-1.5,14),(197,713,-1.5,8),(177,701,-1.5,1)],-.6,True,2),
        ('Arm_Side_L_Inner',[(529,590,.0,45),(414,587,-.4,38),(324,592,-.7,31),(292,640,-.7,25),(315,679,-.8,17),(346,668,-.9,10),(344,640,-.9,1)],.4,True,1),
        ('Arm_Side_R_Inner',[(957,554,.3,47),(1108,570,.15,40),(1243,647,.0,33),(1301,730,-.1,24),(1294,790,-.15,16),(1264,807,-.2,8),(1258,785,-.2,1)],-.4,True,1),
        ('Arm_Central',[(801,591,-.65,75),(831,687,-1.05,72),(902,783,-1.5,64),(933,886,-1.8,55),(908,958,-1.95,42),(839,982,-2,32),(769,963,-2,24),(744,925,-2,18),(765,900,-2,13),(799,899,-2,7),(812,914,-2,1)],.6,True,2),
        ('Arm_Foreground_R',[(934,673,-.95,77),(1010,766,-1.35,83),(1110,761,-1.6,90),(1225,647,-1.95,92),(1331,621,-2.0,83),(1423,692,-2.05,68),(1470,785,-2.1,54),(1447,879,-2.2,40),(1383,933,-2.3,29),(1312,941,-2.3,23),(1275,919,-2.35,17),(1280,888,-2.4,11),(1305,865,-2.4,6),(1326,874,-2.4,1)],.8,True,2),
        ('Arm_Foreground_L',[(775,644,-1.45,69),(738,689,-1.65,76),(639,634,-1.95,96),(545,627,-2.25,105),(447,686,-2.5,98),(391,792,-2.6,80),(389,903,-2.65,59),(427,972,-2.7,40),(481,990,-2.75,27),(514,975,-2.75,16),(511,954,-2.75,8),(489,953,-2.75,1)],-.85,True,2),
    ]
    for name,k,a,c,r in arms:tube(name,k,col,m,True,a,c,r,steps=16,sides=48)

def build_face(col,m):
    # The painting is a three-quarter portrait. Use one anatomical orbit and
    # reflect its finished surface across the skull midline (world X = 0).
    axis=914
    def pair(ob,right):
        twin=ob.copy();twin.data=ob.data.copy();twin.name='CR_WingedCthulhu_'+right
        for v in twin.data.vertices:v.co.x=-v.co.x
        twin.data.flip_normals();twin.data.update();col.objects.link(twin)
    def landmarks(seq):return [(x-axis+768,*rest) for x,*rest in seq]
    def paired_plate(name,outline,center,depth,bulge,material,thick=.08):
        ob=plate(name+'_L',landmarks(outline),landmarks([center])[0],depth,bulge,col,material,thick)
        pair(ob,name+'_R')
    g=Mesh()
    for side in (-1,1):g.ellipsoid((side*.61,-.91,6.90),(.88,.90,.61),64,40)
    g.finish('CR_WingedCthulhu_OrbitalSupport',col,[m['skin']])
    paired_plate('CheekFin',[(803,298),(756,269),(711,233),(666,212),(679,251),(628,277),(670,290),(652,328),(638,357),(672,347),(646,397),(699,366),(722,396),(742,355),(783,352),(810,332)],(739,315),-1.05,.35,m['ridge'])
    ribs=[[(798,326,-1.25,11),(741,290,-1.34,11),(675,272,-1.08,3),(630,278,-1.07,.5)],
          [(788,335,-1.35,10),(726,316,-1.38,8),(678,324,-1.12,4),(640,356,-1.07,.5)],
          [(779,349,-1.38,10),(724,346,-1.35,8),(684,367,-1.14,5),(647,397,-1.06,.5)]]
    for i,k in enumerate(ribs):pair(tube('Cheek_Rib_L'+str(i),landmarks(k),col,m,steps=9,sides=24),'Cheek_Rib_R'+str(i))
    paired_plate('Socket',[(798,283),(827,281),(863,302),(897,322),(884,341),(849,340),(817,325)],(850,316),-1.65,.25,m['socket'])
    paired_plate('Eye',[(818,294),(836,299),(856,310),(883,325),(861,329),(838,321),(826,311)],(851,314),-1.90,.11,m['eye'],.025)
    paired_plate('Brow',[(624,174),(678,208),(735,224),(729,159),(750,182),(785,206),(812,244),(835,266),(858,277),(881,303),(907,321),(895,334),(874,330),(850,313),(821,297),(796,288),(767,267),(724,250),(680,225)],(811,271),-1.80,.29,m['ridge'])
    # Paired lower eyelid and forehead relief. Fit the latter to the skin.
    vv=[];ff=[]
    for ob in col.objects:
        if ob.type=='MESH' and any(s in ob.name for s in ('Cranium','OrbitalSupport','Brow_')):
            off=len(vv);vv.extend(v.co.copy() for v in ob.data.vertices);ff.extend(tuple(off+v for v in f.vertices) for f in ob.data.polygons)
    surface=BVHTree.FromPolygons(vv,ff);crease=[]
    for q in spline([Vector(k) for k in landmarks([(864,223,1),(876,264,3),(895,295,3),(902,318,1)])],7):
        hit,_,_,_=surface.ray_cast(P(q.x,q.y,-10),Vector((0,1,0)))
        if hit is not None:crease.append((q.x,q.y,hit.y+.008,q.z))
    if len(crease)>1:pair(tube('Face_Crease_L',crease,col,m,steps=2,sides=24),'Face_Crease_R')
    pair(tube('Face_Crease_Lid_L',landmarks([(810,334,-1.64,3),(832,347,-1.70,6),(860,347,-1.73,5),(878,339,-1.77,1)]),col,m),'Face_Crease_Lid_R')
    g=Mesh();g.ellipsoid((0,-1.35,6.43),(.78,.56,.78),72,48)
    g.finish('CR_WingedCthulhu_OralMantle',col,[m['skin']])

def beard_strands():
    strands=[
        ('Beard_Back_L',[(832,356,-1.48,22),(772,381,-1.35,24),(711,410,-1.32,22),(674,451,-1.3,17),(661,491,-1.25,13),(677,513,-1.2,7),(697,505,-1.2,1)]),
        ('Beard_Back_R',[(977,366,-1.28,26),(1016,408,-1.42,30),(1088,436,-1.4,25),(1137,472,-1.3,21),(1142,514,-1.25,15),(1123,547,-1.2,8),(1103,535,-1.2,1)]),
        ('Beard_Outer_L',[(847,367,-1.75,26),(786,400,-1.85,28),(764,453,-1.9,27),(773,485,-1.9,24),(733,514,-1.88,20),(711,556,-1.86,16),(721,599,-1.8,11),(752,610,-1.8,5),(766,597,-1.8,1)]),
        ('Beard_Outer_R',[(963,379,-1.73,30),(1005,439,-1.75,32),(1021,509,-1.83,29),(1075,566,-1.84,24),(1120,626,-1.85,19),(1115,676,-1.86,13),(1085,692,-1.85,7),(1068,677,-1.84,1)]),
        ('Beard_Inner_L',[(876,370,-1.95,31),(836,415,-2.03,35),(823,474,-2.06,32),(845,524,-2.1,25),(804,574,-2.12,21),(796,621,-2.13,17),(828,650,-2.13,13),(855,678,-2.13,10),(855,711,-2.14,6),(840,724,-2.14,1)]),
        ('Beard_Inner_R',[(944,371,-1.91,31),(974,428,-2.05,34),(977,487,-2.13,31),(949,544,-2.15,27),(962,598,-2.2,25),(1000,651,-2.24,20),(1009,696,-2.25,14),(983,717,-2.27,7),(961,706,-2.26,1)]),
        ('Beard_Center',[(905,360,-2.05,27),(893,410,-2.25,34),(885,463,-2.35,36),(907,522,-2.39,34),(938,572,-2.42,29),(945,626,-2.44,23),(930,676,-2.46,16),(912,702,-2.45,8),(914,671,-2.44,1)]),
        ('Beard_Side_L',[(799,380,-1.4,19),(744,432,-1.46,23),(728,488,-1.51,21),(698,529,-1.55,18),(673,587,-1.55,13),(693,631,-1.55,8),(720,644,-1.55,1)]),
    ]
    return [(name,[(x-146,y,d,r) for x,y,d,r in ks]) for name,ks in strands]

def build_beard(col,m):
    for name,k in beard_strands():tube(name,k,col,m,steps=17,sides=40)
