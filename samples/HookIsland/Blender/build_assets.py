"""Shop and coconut tree, built as individual editable low-poly parts."""
import math
from mathutils import Vector
import hook_island as H


def sector(name, center, inner, outer, a0, a1, z0, z1, material, group, steps=1):
    vertices=[]
    for z in (z0,z1):
        for r in (inner,outer):
            for i in range(steps+1):
                a=a0+(a1-a0)*i/steps
                vertices.append((center[0]+r*math.cos(a),center[1]+r*math.sin(a),z))
    n=steps+1
    faces=[]
    for i in range(steps):
        faces.extend([
            (i,i+1,n+i+1,n+i),
            (2*n+i,3*n+i,3*n+i+1,2*n+i+1),
            (i,2*n+i,2*n+i+1,i+1),
            (n+i,n+i+1,3*n+i+1,3*n+i),
        ])
    faces.extend([(0,n,3*n,2*n),(n-1,3*n-1,4*n-1,2*n-1)])
    return H.mesh(name,vertices,faces,material,group)


def local(x,y,z):
    angle=H.FRONT+math.pi/2
    return Vector((H.SHOP.x+x*math.cos(angle)-y*math.sin(angle),
                   H.SHOP.y+x*math.sin(angle)+y*math.cos(angle),z))


def timber_ring(name,radius,z,thickness=.075):
    pts=[(H.SHOP.x+radius*math.cos(i*math.tau/36),H.SHOP.y+radius*math.sin(i*math.tau/36),z) for i in range(37)]
    H.tube(name,pts,thickness,H.MAT["wood3"],"shop",6)


def build_barrel(center,base_z):
    cx,cy=center
    profiles=[(0,.38),(.10,.405),(.53,.47),(.96,.42),(1.08,.37)]
    for j in range(14):
        a0=j*math.tau/14+.008
        a1=(j+1)*math.tau/14-.008
        vertices=[]
        for z,r in profiles:
            vertices.extend([(cx+r*math.cos(a0),cy+r*math.sin(a0),base_z+z),
                             (cx+r*math.cos(a1),cy+r*math.sin(a1),base_z+z)])
        faces=[(2*i,2*i+1,2*i+3,2*i+2) for i in range(len(profiles)-1)]
        H.mesh(f"Shop_Barrel_Stave_{j+1:02}",vertices,faces,H.MAT[f"wood{j%5}"],"shop")
    for index,(z,r) in enumerate([(.16,.425),(.55,.483),(.94,.436)]):
        H.lathe(f"Shop_Barrel_Metal_Hoop_{index+1}",(cx,cy,base_z),[(z-.035,r),(z+.035,r)],H.MAT["iron"],"shop",14)
    H.lathe("Shop_Barrel_Lid",(cx,cy,base_z),[(1.055,.365),(1.08,.365)],H.MAT["wood1"],"shop",14)
    for x in [-.19,0,.19]:
        extent=math.sqrt(.35**2-x*x)
        H.box("Barrel_Lid_Seam",(cx+x,cy,base_z+1.083),(.012,extent*2,.006),H.MAT["dark"],"shop")
    H.lathe("Barrel_Bung",(cx+.12,cy+.09,base_z+1.08),[(0,.05),(.02,.05)],H.MAT["wood3"],"shop",8)


def build_shop():
    x,y=H.SHOP[:2]
    z=H.terrain_z(x,y)+.015
    H.SHOP.z=z
    H.lathe("Shop_Round_Floor",(x,y,z),[(0,1.57),(.12,1.57)],H.MAT["wood3"],"shop",36)
    # Individual staves retain the compact round footprint and actual open service hatch.
    for i in range(36):
        a0=i*math.tau/36+.006
        a1=(i+1)*math.tau/36-.006
        middle=(a0+a1)/2
        front_distance=abs((middle-H.FRONT+math.pi)%math.tau-math.pi)
        wood=H.MAT[f"wood{[0,1,2,1,3,1,4,1,2][i%9]}"]
        sector(f"Shop_Lower_Plank_{i+1:02}",(x,y),1.43,1.55,a0,a1,z+.10,z+1.00,wood,"shop")
        if front_distance>math.radians(57):
            sector(f"Shop_Upper_Plank_{i+1:02}",(x,y),1.44,1.55,a0,a1,z+1.00,z+2.28,wood,"shop")
    timber_ring("Shop_Base_Binding",1.565,z+.20,.065)
    timber_ring("Shop_Top_Binding",1.56,z+2.22,.075)
    # Serving ledge is a real curved annular board assembly.
    for i in range(14):
        a0=H.FRONT-math.radians(61)+i*math.radians(122)/14+.002
        a1=H.FRONT-math.radians(61)+(i+1)*math.radians(122)/14-.002
        sector(f"Shop_Curved_Counter_Board_{i+1:02}",(x,y),1.15,1.83,a0,a1,z+.99,z+1.105,H.MAT[f"wood{(i%3)+1}"],"shop",2)
    for side in (-1,1):
        a=H.FRONT+side*math.radians(58)
        p=Vector((x+1.52*math.cos(a),y+1.52*math.sin(a),z))
        H.rod("Shop_Service_Post_L" if side<0 else "Shop_Service_Post_R",p+Vector((0,0,.1)),p+Vector((0,0,2.35)),.105,H.MAT["wood1"],"shop",8)
    # Eave underside and sixteen clean orange roof panels.
    H.lathe("Shop_Eave_Underside",(x,y,z),[(2.20,1.90),(2.31,2.08)],H.MAT["wood3"],"shop",16,H.FRONT)
    roofmats=[H.MAT[f"roof{i}"] for i in range(6)]
    roof=H.lathe("Shop_Orange_Faceted_Roof",(x,y,z),[(2.28,2.09),(2.57,1.55),(3.55,.15)],roofmats,"shop",16,H.FRONT)
    pattern=[0,1,3,1,2,0,3,5,1,0,4,2,0,3,1,2]
    for poly in roof.data.polygons:
        if poly.index>0:
            poly.material_index=pattern[(poly.index-1)%16]
    H.lathe("Shop_Roof_Hex_Finial",(x,y,z),[(3.53,.205),(3.75,.205),(3.81,.13)],H.MAT["wood0"],"shop",8)
    # Shallow framing below the eave emphasizes the open front.
    for side in (-1,1):
        H.rod("Shop_Eave_Bracket",local(side*1.22,-.63,z+1.76),local(side*1.55,-.90,z+2.28),.065,H.MAT["wood2"],"shop",6)
    for level in (.78,1.47):
        sector("Shop_Interior_Curved_Shelf",(x,y),.88,1.44,H.FRONT+math.radians(70),H.FRONT+math.radians(290),z+level,z+level+.07,H.MAT["wood2"],"shop",20)
    # Rear door is fully modeled, so side/back views remain useful.
    door_angle=H.FRONT+math.pi
    doorcenter=Vector((x+1.57*math.cos(door_angle),y+1.57*math.sin(door_angle),z+1.03))
    H.box("Shop_Rear_Door",doorcenter,(.75,.08,1.78),H.MAT["wood3"],"shop",door_angle+math.pi/2,.025)
    for offset in [-.25,0,.25]:
        p=doorcenter+Vector((math.cos(door_angle+math.pi/2)*offset,math.sin(door_angle+math.pi/2)*offset,0))
        H.box("Rear_Door_Plank",p,(.22,.095,1.70),H.MAT["wood1"],"shop",door_angle+math.pi/2,.01)
    # Metal hook and rope merchandise, small pottery and one side barrel.
    potprofile=[(0,.13),(.05,.17),(.21,.16),(.28,.09),(.34,.085)]
    for index,(px,py,scale) in enumerate([(-.65,.42,1.1),(.15,.73,.9),(.68,.45,1.15),(-.62,-1.18,.8)]):
        pos=local(px,py,z+(1.55 if py>0 else 1.11))
        mat=H.MAT["bottle"] if index%2 else H.MAT["pot"]
        H.lathe(f"Shop_Pottery_{index+1}",pos,[(h*scale,r*scale) for h,r in potprofile],mat,"shop",10)
    for index,px in enumerate([-.63,.34,.91]):
        top=local(px,-1.20,z+2.16)
        points=[top,top+Vector((0,0,-.30))]
        for j in range(14):
            a=math.pi+j*math.pi*1.4/13
            points.append(top+Vector((.10+.10*math.cos(a),0,-.30+.10*math.sin(a))))
        H.tube(f"Shop_Display_Hook_{index+1}",points,.027,H.MAT["iron"],"shop",7)
    coil=[]
    for i in range(100):
        t=i/99
        a=t*math.tau*3.8
        r=.09+.19*t
        p=local(.18+r*math.cos(a),-1.47+r*.65*math.sin(a),z+1.125)
        coil.append(p)
    H.tube("Shop_Counter_Coil_of_Rope",coil,.021,H.MAT["rope"],"shop",5)
    lantern=local(-1.67,-.84,z+1.64)
    H.box("Lantern_Glow_Core",lantern,(.19,.19,.30),H.MAT["lantern"],"shop",0,.018)
    for h in [-.19,.19]:
        H.box("Lantern_Cap",lantern+Vector((0,0,h)),(.27,.27,.065),H.MAT["iron"],"shop",0,.015)
    for dx in [-.1,.1]:
        for dy in [-.1,.1]:
            H.rod("Lantern_Corner",lantern+Vector((dx,dy,-.16)),lantern+Vector((dx,dy,.16)),.016,H.MAT["brass"],"shop",5)
    H.tube("Lantern_Hanger",[lantern+Vector((0,0,.22)),lantern+Vector((0,0,.44)),lantern+Vector((.14,.06,.52)),local(-1.33,-.70,z+2.20)],.024,H.MAT["iron"],"shop",6)
    barrel=local(2.02,-.63,0)
    build_barrel(barrel[:2],H.terrain_z(barrel.x,barrel.y))
    print("Round shop complete: open curved counter, roof panels, interior, rear door, lantern, hooks and barrel.")


def trunk_center(t):
    x=-1.87-1.32*t+.22*math.sin(math.pi*t)
    y=2.74+.55*t
    z=H.terrain_z(-1.87,2.74)+5.45*t
    return Vector((x,y,z))


def build_palm():
    verts,faces=[],[]
    materials=[H.MAT["wood0"],H.MAT["wood3"],H.MAT["wood1"],H.MAT["wood2"]]
    for i in range(19):
        t=i/18
        c=trunk_center(t)
        radius=.30*(1-t)+.145*t
        # A continuous gently bowed trunk with broad faceted growth sections.
        for j in range(9):
            a=math.tau*j/9+.15*math.sin(i*.5)
            verts.append(c+Vector((radius*math.cos(a),radius*math.sin(a),0)))
    faces.append(tuple(range(8,-1,-1)))
    for i in range(18):
        for j in range(9):
            faces.append((i*9+j,i*9+(j+1)%9,(i+1)*9+(j+1)%9,(i+1)*9+j))
    faces.append(tuple(18*9+j for j in range(9)))
    trunk=H.mesh("Coconut_Curved_Trunk",verts,faces,materials,"palm")
    for poly in trunk.data.polygons:
        poly.material_index=[0,2,0,1,0,2,0,3,0][poly.index%9]
    for i in range(1,17):
        t=i/18
        radius=.30*(1-t)+.145*t+.012
        c=trunk_center(t)
        H.lathe(f"Coconut_Growth_Ring_{i:02}",c,[(-.022,radius),(.022,radius*.99)],H.MAT["wood3"],"palm",9)
    crown=trunk_center(1)
    H.lathe("Coconut_Crown_Base",crown-Vector((0,0,.12)),[(0,.19),(.28,.28),(.45,.10)],H.MAT["leaf0"],"palm",9)
    leafmats=[H.MAT[f"leaf{i}"] for i in range(5)]
    for k in range(10):
        angle=math.tau*k/10+.14
        direction=Vector((math.cos(angle),math.sin(angle),0))
        side=Vector((-math.sin(angle),math.cos(angle),0))
        length=[3.1,2.75,2.9,3.25,2.65,3.1,3.35,2.9,2.55,3.0][k]
        lift=[.7,.9,.65,.8,.72,.68,.5,.8,.85,.7][k]
        def frond_center(t):
            return crown+direction*(length*t)+Vector((0,0,.12+lift*math.sin(math.pi*t)-.98*t*t))
        vertices=[]
        rows=19
        for i in range(rows):
            t=i/(rows-1)
            c=frond_center(t)
            width=.56*math.sin(math.pi*t)**.72
            if i%2==1:width*=.80
            vertices.extend([c-side*width-Vector((0,0,.11*math.sin(math.pi*t))),
                             c+Vector((0,0,.025)),
                             c+side*width-Vector((0,0,.13*math.sin(math.pi*t)))])
        triangles=[]
        for i in range(rows-1):
            a=i*3;b=(i+1)*3
            triangles.extend([(a,b,b+1),(a,b+1,a+1),(a+1,b+1,b+2),(a+1,b+2,a+2)])
        frond=H.mesh(f"Coconut_Frond_{k+1:02}",vertices,triangles,leafmats,"palm")
        for poly in frond.data.polygons:
            poly.material_index=([1,2,0,1][poly.index%4]+(k%2))%5
        H.tube(f"Coconut_Frond_Midrib_{k+1:02}",[frond_center(i/12)+Vector((0,0,.03)) for i in range(13)],.017,H.MAT["leaf2"],"palm",5)
    # Exactly two modest fruit, tucked beneath the fronds and visible from the front.
    for index,offset in enumerate([(-.20,-.18,-.28),(.18,-.21,-.31)]):
        c=crown+Vector(offset)
        profile=[]
        for j in range(9):
            a=-math.pi/2+math.pi*j/8
            profile.append((.27*math.sin(a),max(.015,.195*math.cos(a))))
        fruit=H.lathe(f"Coconut_Fruit_{index+1:02}",c,profile,H.MAT["coconut"],"palm",10)
        fruit.rotation_euler=(.1,(-.14 if index else .16),.1)
        H.rod("Coconut_Fruit_Stem",c+Vector((0,0,.23)),crown,.035,H.MAT["leaf0"],"palm",6)
    print("Coconut tree complete: bowed trunk, 10 notched low-poly fronds, exactly two small fruit.")


def build():
    build_shop()
    build_palm()
    H.save_scene()
