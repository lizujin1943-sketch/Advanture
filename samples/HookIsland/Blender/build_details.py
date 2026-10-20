"""Shore-connected pier and restrained, ankle-height island dressing."""
import math
import random
from mathutils import Vector
import hook_island as H

RNG = random.Random(842)
PIER_WIDTH = 2.4
DECK_START = -1.1
DECK_END = 3.2
RAMP_START = -2.1


def pier_point(s, u=0, z=0):
    p = H.SHORE + H.DOCK_DIR*s + H.DOCK_SIDE*u
    return Vector((p.x, p.y, z))


def build_pier():
    angle = math.atan2(H.DOCK_DIR.y, H.DOCK_DIR.x)-math.pi/2
    width = PIER_WIDTH
    # The first edge lies inside the sandy bank. Sample actual triangulated ground,
    # rather than the analytic height field, so there is no opening at the join.
    ground = H.SCENE.objects["Island_Ground_Editable"]
    def ground_z(s, u):
        p = pier_point(s, u)
        hit, loc, _, _ = ground.ray_cast(Vector((p.x,p.y,10)),Vector((0,0,-1)))
        if not hit:
            raise RuntimeError("Pier entrance is outside the island.")
        return loc.z
    # Five individually editable transverse approach boards form a shallow ramp.
    for i in range(5):
        s0 = RAMP_START + i/5*(DECK_START-RAMP_START)
        s1 = RAMP_START + (i+1)/5*(DECK_START-RAMP_START)
        vertices = []
        slices = 25
        for dz in (0,-.115):
            for s in (s0,s1):
                for j in range(slices):
                    u=-width/2+j/(slices-1)*width
                    t = (s-RAMP_START)/(DECK_START-RAMP_START)
                    # Samples every 10 cm follow the actual triangulated sand.
                    z = (ground_z(RAMP_START,u)+.006)*(1-t)+H.DECK_HEIGHT*t
                    vertices.append(pier_point(s,u,z+dz))
        faces = []
        for j in range(slices-1):
            faces.extend([(j,slices+j,slices+j+1,j+1),
                          (2*slices+j,2*slices+j+1,3*slices+j+1,3*slices+j),
                          (j,j+1,2*slices+j+1,2*slices+j),
                          (slices+j,3*slices+j,3*slices+j+1,slices+j+1)])
        faces.extend([(0,2*slices,3*slices,slices),
                      (slices-1,2*slices-1,4*slices-1,3*slices-1)])
        board = H.mesh(f"Pier_Approach_Ramp_Board_{i+1:02}",vertices,faces,H.MAT[f"wood{[1,2,1,4,2][i]}"],"dock")
        board["landward_overlap_m"] = -RAMP_START
    count = 13
    step = (DECK_END-DECK_START)/count
    for i in range(count):
        s = DECK_START+(i+.5)*step
        plank = H.box(f"Pier_Deck_Plank_{i+1:02}",
                      pier_point(s,0,H.DECK_HEIGHT-.075),
                      (width,step-.012,.15),H.MAT[f"wood{[1,2,0,2,4,1,2][i%7]}"],"dock",angle,.012)
        for u in (-.93,.93):
            H.lathe("Pier_Deck_Nail",pier_point(s,u,H.DECK_HEIGHT+.002),
                    [(0,.018),(.006,.018)],H.MAT["iron"],"dock",6)
    # Timber beams are immediately below the planks; all four supports reach
    # below the island base. The shore pair penetrates the bank like driven piles.
    for u in (-.91,.91):
        H.box("Pier_Longitudinal_Beam",pier_point((RAMP_START+DECK_END)/2,u,H.DECK_HEIGHT-.29),
              (.18,DECK_END-RAMP_START+.08,.28),H.MAT["wood3"],"dock",angle,.018)
    for s in (-.83,2.96):
        H.box("Pier_Cross_Beam",pier_point(s,0,H.DECK_HEIGHT-.36),
              (2.73,.24,.29),H.MAT["wood0"],"dock",angle,.02)
        for side in (-1,1):
            u = side*1.24
            bottom, top = -1.12,H.DECK_HEIGHT+.54
            post = H.box("Pier_Shore_Post" if s<0 else "Pier_Outer_Post",
                         pier_point(s,u,(bottom+top)/2),(.26,.26,top-bottom),
                         H.MAT["wood0"],"dock",angle,.025)
            H.box("Pier_Post_Cap",pier_point(s,u,top),(.33,.33,.13),
                  H.MAT["wood2"],"dock",angle,.018)
            # Loose hemp wraps around square posts, with small visible knots.
            points=[]
            for j in range(65):
                a=j/64*math.tau*3
                r=.18
                p=pier_point(s,u,H.DECK_HEIGHT+.20+j/64*.16)
                p += Vector((r*math.cos(a),r*math.sin(a),0))
                points.append(p)
            H.tube("Pier_Post_Rope_Wrap",points,.023,H.MAT["rope"],"dock",5)
            knot=pier_point(s+.13,u+side*.12,H.DECK_HEIGHT+.25)
            H.lathe("Pier_Rope_Knot",knot,[(-.04,.042),(.02,.055),(.06,.02)],H.MAT["rope"],"dock",7)
            # Short knee braces add a credible load path without fencing the deck.
            H.rod("Pier_Underdeck_Knee_Brace",pier_point(s,u,H.DECK_HEIGHT-.83),
                  pier_point(s-.48,u,H.DECK_HEIGHT-.26),.075,H.MAT["wood1"],"dock",6)
    # The reference has rope trim down the sides, leaving both ends open.
    for u in (-1.25,1.25):
        points=[]
        for j in range(25):
            t=j/24
            points.append(pier_point(-.83+(2.96+.83)*t,u,H.DECK_HEIGHT+.20-.10*math.sin(math.pi*t)))
        H.tube("Pier_Low_Side_Rope",points,.024,H.MAT["rope"],"dock",6)
    H.COL["dock"]["deck_top_m"] = H.DECK_HEIGHT
    H.COL["dock"]["clear_width_m"] = PIER_WIDTH
    H.COL["dock"]["entrance"] = "5 flush approach boards; entrance sampled from actual ground mesh."
    print("Pier built: 2.4 m wide, 13 deck boards, 1 m shore ramp, four driven posts and supporting beams.")


def ground_point(x,y,extra=0):
    ground=H.SCENE.objects["Island_Ground_Editable"]
    hit,p,_,_=ground.ray_cast(Vector((x,y,8)),Vector((0,0,-1)))
    return Vector((x,y,p.z+extra if hit else H.terrain_z(x,y)+extra))


def flower_patch(index,x,y):
    vertices,faces,indices=[],[],[]
    materials=[H.MAT["stem"],H.MAT["flower"],H.MAT["pollen"],H.MAT["leaf2"]]
    def face(coords,mat):
        n=len(vertices)
        vertices.extend(coords)
        faces.append(tuple(range(n,n+len(coords))))
        indices.append(mat)
    # A compact patch with varied flower spacing, height and tilt.
    for k in range(RNG.randint(8,12)):
        a=RNG.random()*math.tau
        r=RNG.random()**.6*.45
        p=ground_point(x+math.cos(a)*r,y+math.sin(a)*r,.004)
        height=RNG.uniform(.13,.23)
        c=p+Vector((RNG.uniform(-.035,.035),0,height))
        w=.012
        face([p+Vector((-w,0,0)),p+Vector((w,0,0)),c+Vector((w,0,0)),c-Vector((w,0,0))],0)
        for side in (-1,1):
            d=Vector((side*.105,.03,height*.48))
            middle=p+d*.48
            face([p+Vector((0,0,.03)),middle+Vector((0,.036,0)),p+d,middle-Vector((0,.036,0))],3)
        petal_length=RNG.uniform(.072,.11)
        for j in range(5):
            ang=j*math.tau/5+a
            d=Vector((math.cos(ang),math.sin(ang),0))
            s=Vector((-math.sin(ang),math.cos(ang),0))
            face([c+d*.021,c+d*petal_length*.68-s*.029+Vector((0,0,.014)),
                  c+d*petal_length+Vector((0,0,.006)),
                  c+d*petal_length*.68+s*.029+Vector((0,0,.014))],1)
        ring=[c+Vector((.035*math.cos(j*math.tau/7),.035*math.sin(j*math.tau/7),.012)) for j in range(7)]
        face(ring,2)
    obj=H.mesh(f"Low_Daisy_Cluster_{index:02}",vertices,faces,materials,"detail")
    for poly,mat in zip(obj.data.polygons,indices):poly.material_index=mat


def grass_tuft(index,x,y,scale=1):
    p=ground_point(x,y,.004)
    verts,faces=[],[]
    for j in range(5):
        ang=j*math.tau/5+RNG.random()*.5
        d=Vector((math.cos(ang),math.sin(ang),0))
        s=Vector((-math.sin(ang),math.cos(ang),0))
        h=RNG.uniform(.11,.23)*scale
        b=p+d*RNG.uniform(0,.045)
        n=len(verts)
        verts.extend([b-s*.032*scale,b+s*.032*scale,
                      b+d*.048*scale+Vector((0,0,h*.6)),
                      b+d*.13*scale+Vector((0,0,h))])
        faces.extend([(n,n+1,n+2),(n,n+2,n+3)])
    o=H.mesh(f"Short_Grass_Tuft_{index:03}",verts,faces,
             [H.MAT["leaf1"],H.MAT["leaf2"],H.MAT["leaf4"]],"detail")
    for f in o.data.polygons:f.material_index=RNG.randrange(3)


def gravel_patch(index,x,y):
    verts,faces=[],[]
    for j in range(RNG.randint(3,5)):
        p=ground_point(x+RNG.uniform(-.29,.29),y+RNG.uniform(-.20,.20),-.022)
        radius=RNG.uniform(.085,.16)
        h=RNG.uniform(.035,.065)
        n=len(verts)
        for z,r in [(0,1),(h,.8),(h+.018,.43)]:
            for k in range(7):
                a=k*math.tau/7
                verts.append(p+Vector((radius*r*math.cos(a),radius*.75*r*math.sin(a),z)))
        for r in range(2):
            for k in range(7):
                faces.append((n+r*7+k,n+r*7+(k+1)%7,n+(r+1)*7+(k+1)%7,n+(r+1)*7+k))
        faces.append(tuple(n+14+k for k in range(7)))
    H.mesh(f"Embedded_Gravel_Cluster_{index:02}",verts,faces,H.MAT["stone"],"detail")


def build_ground_dressing():
    flowers=[(-6.7,8.5),(-10,3.2),(-7.4,-.8),(-7.1,-4.6),(-2.6,-5.8),
             (2.5,-7.6),(6.4,-9.3),(10.7,-6.7),(10.1,-2.6),(6.5,1.2),
             (2.1,5.3),(-2.4,8.2),(5.6,-4.2),(-11.5,4.9)]
    index=0
    for x,y in flowers:
        if H.inside((x,y)) and H.grass_distance(x,y)>.15:
            index+=1
            flower_patch(index,x,y)
    tuft_count=0
    attempts=0
    while tuft_count<80 and attempts<3000:
        attempts+=1
        x=RNG.uniform(-15,15);y=RNG.uniform(-12,12)
        if not H.inside((x,y)) or H.grass_distance(x,y)<.2:continue
        if (Vector((x,y))-Vector(H.SHOP[:2])).length<3:continue
        # Bias a little towards the edges; keep the main floor visibly playable.
        if H.shore_distance(x,y)>5.5 and RNG.random()<.55:continue
        tuft_count+=1
        grass_tuft(tuft_count,x,y,RNG.uniform(.75,1.1))
    gravel=[(-4.3,-3.2),(-2.0,-4.0),(1.6,-2.0),(4.1,-5.5),(8.9,-7.2),
            (8.6,-.8),(-8.2,6.0),(-9,-2.9),(-4.1,6.6),(2.7,3.9),(-5.5,-5.5)]
    for i,(x,y) in enumerate(gravel):
        if H.inside((x,y)) and H.shore_distance(x,y)>.9:
            gravel_patch(i+1,x,y)
    print(f"Low dressing complete: {index} daisy clusters, {tuft_count} short tufts, embedded pebble clusters.")


def build():
    build_pier()
    build_ground_dressing()
    H.save_scene()
