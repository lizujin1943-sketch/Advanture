"""Reference-led revision 2. Run after the initial editable scene is built.

TOP controls the plan layout. The ISOMETRIC panel controls the primary camera
and material presentation. No ocean, water rocks, or baked object shadows.
"""
import ast
import math
from pathlib import Path
import random
import re
import bpy
from mathutils import Vector
import hook_island as H
import hook_island_assets as A
import hook_island_details as D

ROOT=H.ROOT
RNG=random.Random(6102)
RENDERS=ROOT/"renders"/"v2"
RENDERS.mkdir(parents=True,exist_ok=True)


def clear_collection(key):
    for o in list(H.COL[key].objects):bpy.data.objects.remove(o,do_unlink=True)


def world(pixel_x,pixel_y):
    return Vector(((pixel_x-390)*.049,(320-pixel_y)*.049))


def reference_ground():
    ground=H.SCENE.objects["Island_Ground_Editable"]
    uv=ground.data.uv_layers.get("Reference_Plan") or ground.data.uv_layers.new(name="Reference_Plan")
    for loop in ground.data.loops:
        p=ground.data.vertices[loop.vertex_index].co
        uv.data[loop.index].uv=((p.x/.049+390)/760,1-(320-p.y/.049)/650)
    mat=H.material("Ground v2 / traced short-grass distribution","B2BF2B",1)
    n,l=mat.node_tree.nodes,mat.node_tree.links
    bs=next(node for node in n if node.type=="BSDF_PRINCIPLED")
    tex=n.new("ShaderNodeTexCoord")
    uv_node=n.new("ShaderNodeUVMap");uv_node.uv_map="Reference_Plan"
    image=bpy.data.images.load(str(ROOT/"reference_data"/"grass_distribution.png"),check_existing=True)
    image.reload()
    image.colorspace_settings.name="Non-Color"
    image.pack()
    mask=n.new("ShaderNodeTexImage");mask.image=image
    mask.label="Measured reference grass/sand boundary"
    H.set_enum(mask,"extension","CLIP")
    l.new(uv_node.outputs[0],mask.inputs["Vector"])
    cells=n.new("ShaderNodeTexVoronoi");cells.inputs["Scale"].default_value=4.4
    l.new(tex.outputs["Object"],cells.inputs["Vector"])
    bw=n.new("ShaderNodeRGBToBW");l.new(cells.outputs["Color"],bw.inputs[0])
    palette=n.new("ShaderNodeValToRGB")
    palette.color_ramp.elements[0].color=H.rgba("A3B324")
    palette.color_ramp.elements[1].color=H.rgba("BAC637")
    l.new(bw.outputs[0],palette.inputs[0])
    broad=n.new("ShaderNodeTexNoise")
    broad.inputs["Scale"].default_value=.65;broad.inputs["Detail"].default_value=1
    l.new(tex.outputs["Object"],broad.inputs["Vector"])
    shade=n.new("ShaderNodeValToRGB")
    shade.color_ramp.elements[0].color=(.76,.84,.65,1)
    shade.color_ramp.elements[1].color=(1.08,1.07,.96,1)
    l.new(broad.outputs["Fac"],shade.inputs[0])
    tint=n.new("ShaderNodeMixRGB");H.set_enum(tint,"blend_type","MULTIPLY")
    tint.inputs[0].default_value=.42
    l.new(palette.outputs[0],tint.inputs[1]);l.new(shade.outputs[0],tint.inputs[2])
    sand_cells=n.new("ShaderNodeTexVoronoi");sand_cells.inputs["Scale"].default_value=2.2
    l.new(tex.outputs["Object"],sand_cells.inputs["Vector"])
    sand_bw=n.new("ShaderNodeRGBToBW");l.new(sand_cells.outputs["Color"],sand_bw.inputs[0])
    sand=n.new("ShaderNodeValToRGB")
    sand.color_ramp.elements[0].color=H.rgba("FBCB78")
    sand.color_ramp.elements[1].color=H.rgba("FFD389")
    l.new(sand_bw.outputs[0],sand.inputs[0])
    mix=n.new("ShaderNodeMixRGB");H.set_enum(mix,"blend_type","MIX")
    l.new(mask.outputs["Color"],mix.inputs[0])
    l.new(sand.outputs[0],mix.inputs[1]);l.new(tint.outputs[0],mix.inputs[2])
    l.new(mix.outputs[0],bs.inputs["Base Color"])
    ground.data.materials.clear();ground.data.materials.append(mat)
    ground["reference_uv"]="Reference TOP pixels mapped at 0.049 m/px"
    body=H.SCENE.objects["Island_Sand_Bank_And_Base"]
    count=len(H.OUTLINE)
    for v in body.data.vertices:
        if v.index>=count*2:v.co.z=-.18
        elif v.index>=count:v.co.z=-.055-.015*math.sin(v.index*1.3)
    for key,hexcolor in [("sand_side","EAC082"),("sand_bottom","D5AC70")]:
        m=H.MAT[key]
        bs=next(node for node in m.node_tree.nodes if node.type=="BSDF_PRINCIPLED")
        bs.inputs["Base Color"].default_value=H.rgba(hexcolor)
        m.diffuse_color=H.rgba(hexcolor)
    body.data.update()
    print("Reference grass/sand map applied; deep cut-away base removed.")


def shop():
    clear_collection("shop")
    H.SHOP=Vector((0,1.05,0))
    A.build_shop()
    x,y,z=H.SHOP
    old=H.SCENE.objects["Shop_Orange_Faceted_Roof"]
    bpy.data.objects.remove(old,do_unlink=True)
    # Twelve broad panels and a low, gently flared eave match the reference.
    roof=H.lathe("Shop_Orange_Faceted_Roof",(x,y,z),
                 [(2.24,2.18),(2.42,1.83),(3.52,.16)],
                 [H.MAT[f"roof{i}"] for i in range(6)],"shop",12,H.FRONT)
    pattern=[2,0,1,3,1,0,2,0,4,2,0,3]
    for p in roof.data.polygons:
        if p.index>0:p.material_index=pattern[(p.index-1)%12]
    eave=H.SCENE.objects["Shop_Eave_Underside"];eave.scale.x=eave.scale.y=1.04
    # The reference barrel is open, with pale hoops and a dark inner well.
    for o in list(H.COL["shop"].objects):
        if any(o.name.startswith(prefix) for prefix in ["Shop_Barrel_Lid","Barrel_Lid_Seam","Barrel_Bung"]):
            bpy.data.objects.remove(o,do_unlink=True)
    hoop=H.material("Barrel v2 / light wood binding","C7A375",.65,.12)
    for o in H.COL["shop"].objects:
        if o.name.startswith("Shop_Barrel_Metal_Hoop"):
            o.data.materials.clear();o.data.materials.append(hoop)
        if o.name.startswith(("Lantern_Cap","Lantern_Hanger")):
            o.data.materials.clear();o.data.materials.append(H.MAT["brass"])
    barrel=A.local(2.02,-.63,0)
    bz=H.terrain_z(barrel.x,barrel.y)
    H.lathe("Shop_Barrel_Inner_Well",(barrel.x,barrel.y,bz),[(.78,.35),(.80,.35)],H.MAT["dark"],"shop",14)
    rim=[]
    for i in range(29):
        a=i*math.tau/28
        rim.append((barrel.x+.373*math.cos(a),barrel.y+.373*math.sin(a),bz+1.075))
    H.tube("Shop_Barrel_Open_Rim",rim,.035,H.MAT["wood0"],"shop",5)
    # A small open metal bowl and a mug occupy the reference's front counter.
    pos=A.local(-.57,-1.32,z+1.11)
    bowl=H.lathe("Shop_Counter_Metal_Bowl",pos,
                [(0,.11),(.06,.22),(.17,.27),(.185,.25),(.07,.19)],
                H.MAT["iron"],"shop",12)
    pos=A.local(.57,-1.36,z+1.11)
    H.lathe("Shop_Counter_Wooden_Mug",pos,[(0,.10),(.24,.115),(.25,.09),(.05,.07)],H.MAT["wood1"],"shop",10)
    # Fine dark seams under the orange rim give the roof a crafted edge.
    H.tube("Shop_Orange_Rim_Edge",
           [(x+2.18*math.cos(H.FRONT+i*math.tau/12),y+2.18*math.sin(H.FRONT+i*math.tau/12),z+2.24) for i in range(13)],
           .025,H.MAT["roof4"],"shop",5)
    print("Shop adjusted: broad roof panels, open barrel, warm lantern and counter details.")


def trunk_point(t):
    # Crown and root placement are read separately from the TOP reference.
    return Vector((-2.15-1.62*t+.27*math.sin(math.pi*t),
                   1.55+2.76*t-.15*math.sin(math.pi*t),
                   H.terrain_z(-2.15,1.55)+5.45*t))


def palm():
    clear_collection("palm")
    verts,faces=[],[]
    sides,steps=9,13
    for i in range(steps+1):
        t=i/steps;c=trunk_point(t)
        r=.37*(1-t)+.145*t
        for j in range(sides):
            a=j*math.tau/sides+.12
            verts.append(c+Vector((r*math.cos(a),r*math.sin(a),0)))
    faces.append(tuple(range(sides-1,-1,-1)))
    for i in range(steps):
        for j in range(sides):
            faces.append((i*sides+j,i*sides+(j+1)%sides,(i+1)*sides+(j+1)%sides,(i+1)*sides+j))
    faces.append(tuple(steps*sides+j for j in range(sides)))
    obj=H.mesh("Coconut_Curved_Trunk",verts,faces,[H.MAT[f"wood{i}"] for i in range(4)],"palm")
    for poly in obj.data.polygons:poly.material_index=[0,0,1,0,2,0,1,3,0][poly.index%sides]
    for i in range(1,steps):
        t=i/steps;r=.37*(1-t)+.145*t+.012
        H.lathe(f"Coconut_Growth_Ring_{i:02}",trunk_point(t),
                [(-.027,r),(.014,r*.96)],H.MAT["wood3"],"palm",sides)
    crown=trunk_point(1)
    H.lathe("Coconut_Crown_Base",crown,[(0,.19),(.28,.24),(.36,.08)],H.MAT["leaf0"],"palm",8)
    leafmat=[H.MAT[f"leaf{i}"] for i in range(5)]
    # Eight unequal broad leaves; each leaflet is a calm facet, with narrow cuts.
    lengths=[3.2,3.35,2.9,2.75,2.9,3.15,2.85,3.15]
    lifts=[.58,.88,1.15,1.0,.60,.62,.45,.62]
    drops=[.90,.60,.42,.65,1.12,1.75,1.85,1.50]
    for k in range(8):
        a=k*math.tau/8+.08
        direction=Vector((math.cos(a),math.sin(a),0))
        side=Vector((-math.sin(a),math.cos(a),0))
        def mid(t):
            return crown+direction*(lengths[k]*t)+Vector((0,0,.18+lifts[k]*math.sin(math.pi*t)-drops[k]*t*t))
        def edge(t,sign):
            width=.64*math.sin(math.pi*t)**.62
            return mid(t)+side*sign*width-Vector((0,0,.13*math.sin(math.pi*t)))
        v=[];f=[];materials=[]
        for j in range(7):
            t0=j/7;t1=(j+1)/7
            for sign in (-1,1):
                a0=min(1,t0+.004);a1=max(0,t1-.004)
                def inner(t):
                    width=.64*math.sin(math.pi*t)**.62
                    return mid(t)+side*sign*width*.46-Vector((0,0,.05*math.sin(math.pi*t)))
                index=len(v)
                v.extend([inner(t0),edge(a0,sign),edge(a1,sign),inner(t1)])
                f.append(tuple(index+q for q in ([0,1,2,3] if sign<0 else [3,2,1,0])))
                materials.append((0 if sign<0 else 1)+(1 if k in (1,2) else 0))
                points=[mid(t0),inner(t0),inner(t1),mid(t1)]
                clean=[]
                for point in points:
                    if not clean or (point-clean[-1]).length>1e-7:clean.append(point)
                if len(clean)>2 and (clean[-1]-clean[0]).length<1e-7:clean.pop()
                index=len(v);v.extend(clean)
                ids=list(range(index,index+len(clean)))
                f.append(tuple(ids if sign<0 else ids[::-1]))
                materials.append((0 if sign<0 else 1)+(1 if k in (1,2) else 0))
        leaf=H.mesh(f"Coconut_Frond_{k+1:02}",v,f,leafmat,"palm")
        for face,mat in zip(leaf.data.polygons,materials):face.material_index=mat
        H.tube(f"Coconut_Frond_Midrib_{k+1:02}",[mid(j/16)+Vector((0,0,.015)) for j in range(17)],
               .022,H.MAT["leaf2"],"palm",5)
    for i,offset in enumerate([(-.23,-.26,-.30),(.21,-.23,-.30)]):
        c=crown+Vector(offset)
        profile=[(.31*math.sin(-math.pi/2+j*math.pi/10),
                  max(.012,.235*math.cos(-math.pi/2+j*math.pi/10))) for j in range(11)]
        o=H.lathe(f"Coconut_Fruit_{i+1:02}",c,profile,H.MAT["coconut"],"palm",10)
        o.rotation_euler=(.10,-.18 if i else .16,0)
        H.rod("Coconut_Fruit_Stem",c+Vector((0,0,.25)),crown,.032,H.MAT["leaf0"],"palm",6)
    print("Palm reconstructed: reference root/crown positions, 8 broad fronds and 2 small visible fruit.")


def pier():
    clear_collection("dock")
    D.PIER_WIDTH=2.95
    D.DECK_START=-.6
    D.DECK_END=3.2
    D.RAMP_START=-1.4
    ground=H.SCENE.objects["Island_Ground_Editable"]
    def ease(t):
        t=max(0,min(1,t));return t*t*(3-2*t)
    # A graded abutment beneath the entrance preserves the external shoreline
    # while supporting a short, level approach instead of a twisted ramp.
    for vertex in ground.data.vertices:
        xy=Vector(vertex.co[:2])-H.SHORE
        s=xy.dot(H.DOCK_DIR);u=xy.dot(H.DOCK_SIDE)
        weight=ease((2.05-abs(u))/.50)*ease((s+3.0)/.90)*ease((-.35-s)/.55)
        if weight>0:
            vertex.co.z=max(vertex.co.z,vertex.co.z*(1-weight)+.745*weight)
    ground.data.update()
    bpy.context.view_layer.update()
    # Reuse the proven sampled approach builder, replacing its reference-specific
    # plank and post parameters without modifying the archived first version.
    source=ast.parse((ROOT/"build_details.py").read_text())
    node=next(n for n in source.body if isinstance(n,ast.FunctionDef) and n.name=="build_pier")
    class Parameters(ast.NodeTransformer):
        values={13:8,-.83:-.38,1.24:1.53,-1.12:-.30,.26:.34,.33:.42,
                2.73:3.28,.91:1.13,-.91:-1.13,.93:1.15,-.93:-1.15,
                .18:.23,1.25:1.55,-1.25:-1.55}
        def visit_Constant(self,node):
            if isinstance(node.value,str) and node.value.startswith("Pier built:"):
                return ast.copy_location(ast.Constant("Pier built: 2.95 m wide, 8 deck boards, a short sampled approach and four low posts."),node)
            # Negative constants are UnaryOp AST nodes and are handled separately.
            if type(node.value) in (int,float) and node.value in self.values:
                return ast.copy_location(ast.Constant(self.values[node.value]),node)
            return node
        def visit_UnaryOp(self,node):
            try:value=ast.literal_eval(node)
            except Exception:return self.generic_visit(node)
            if value in self.values:
                return ast.copy_location(ast.Constant(self.values[value]),node)
            return self.generic_visit(node)
    node=Parameters().visit(node)
    code=ast.fix_missing_locations(ast.Module(body=[node],type_ignores=[]))
    exec(compile(code,str(ROOT/"build_details.py"),"exec"),D.__dict__)
    D.build_pier()
    for o in list(H.COL["dock"].objects):
        if o.name.startswith("Pier_Low_Side_Rope"):bpy.data.objects.remove(o,do_unlink=True)
    H.COL["dock"]["entrance"]="Short, sampled approach over a graded sand abutment."
    H.COL["dock"]["reference_plank_count"]=8
    # Restrained hand-shaped grain marks run across each broad board.
    grain=H.material("Pier v2 / timber grain","985125",.94)
    step=(D.DECK_END-D.DECK_START)/8
    for index in range(8):
        s=D.DECK_START+(index+.5)*step
        for line in range(2):
            points=[]
            for j in range(14):
                u=-1.35+j/13*2.65
                wave=.013*math.sin(j*.8+index)+.006*math.cos(j*1.7)
                points.append(D.pier_point(s+(-.10 if line else .08)+wave,u,H.DECK_HEIGHT+.003))
            H.tube(f"Pier_Wood_Grain_{index+1:02}_{line+1}",points,.006,grain,"dock",4)
    print("Pier matched to the reference: 8 broad planks, wider deck, short posts, no side rail.")


def dressing():
    clear_collection("detail")
    D.RNG=random.Random(842)
    flowers=[(263,152),(187,240),(231,295),(241,377),(552,306),(607,397),(442,461),(545,516)]
    for i,(px,py) in enumerate(flowers):
        p=world(px,py);D.flower_patch(i+1,p.x,p.y)
    # Fewer, readable low clumps instead of a scatter of isolated needles.
    tufts=[(437,202),(582,303),(584,345),(646,391),(541,473),
           (591,513),(481,480),(300,404),(208,365),(202,330),(282,331),
           (360,340),(332,270),(390,428),(523,433),(570,481)]
    for i,(px,py) in enumerate(tufts):
        p=world(px,py);D.grass_tuft(i+1,p.x,p.y,1.15)
        o=H.SCENE.objects[f"Short_Grass_Tuft_{i+1:03}"]
        # Vertices are in world coordinates; broaden around the tuft's own root.
        for v in o.data.vertices:
            v.co.x=p.x+(v.co.x-p.x)*1.45
            v.co.y=p.y+(v.co.y-p.y)*1.45
    stones=[(209,331),(336,450),(479,378),(583,458),(496,521),(350,422),(449,354),(570,550)]
    for i,(px,py) in enumerate(stones):
        p=world(px,py);D.gravel_patch(i+1,p.x,p.y)
    print("Flower and tuft placements now follow the TOP panel.")


def lighting():
    s=H.SCENE
    config=Path(bpy.app.binary_path).parents[1]/"Resources"/"5.2"/"datafiles"/"colormanagement"/"config.ocio"
    if config.exists():
        names=re.findall(r"!<View> \{name: ([^,}]+)",config.read_text())
        standard=next((name for name in names if name.lower()=="standard"),None)
        if standard:s.view_settings.view_transform=standard
    s.view_settings.exposure=0
    sun=next(o for o in H.COL["rig"].objects if o.type=="LIGHT" and o.data.type=="SUN")
    sun.location=(10,8,18)
    sun.rotation_euler=(-sun.location).to_track_quat("-Z","Y").to_euler()
    sun.data.energy=3.0;sun.data.angle=.045;sun.data.color=H.rgba("FFF3DB")[:3]
    for o in H.COL["rig"].objects:
        if o.type=="LIGHT" and o.data.type=="AREA" and o.name.startswith(("Sky","Soft")):
            o.data.energy=180
    backgrounds=[n for n in s.world.node_tree.nodes if n.type=="BACKGROUND"]
    backgrounds[0].inputs["Strength"].default_value=.32
    for bg in backgrounds[1:]:
        bg.inputs["Color"].default_value=H.rgba("8BAEC4")
        bg.inputs["Strength"].default_value=1
    for name,p in [("Shop interior warm bounce",A.local(0,-.5,H.SHOP.z+1.97)),
                   ("Lantern local glow",A.local(-1.72,-.95,H.SHOP.z+1.64))]:
        o=s.objects.get(name)
        if o:o.location=p
    camera=s.objects["Camera_Hero"]
    camera.location=(0,-42,49)
    camera.rotation_euler=(Vector((0,-.5,.5))-camera.location).to_track_quat("-Z","Y").to_euler()
    camera.data.ortho_scale=37.6
    # A camera with the exact pixel-to-world calibration of the TOP panel.
    name="Camera_Reference_Top"
    camera=s.objects.get(name)
    if camera is None:
        data=s.objects["Camera_Top"].data.copy();data.name=name
        camera=bpy.data.objects.new(name,data);H.COL["rig"].objects.link(camera)
    camera.location=(-.49,-.245,55)
    camera.rotation_euler=(0,0,0)
    camera.data.ortho_scale=760*.049
    s.camera=s.objects["Camera_Hero"]
    s.render.resolution_x=1600;s.render.resolution_y=1100
    s.cycles.samples=64
    print("Warm reference light direction and saturated display transform set.")


def save():
    H.SCENE["revision"]="v2 reference fidelity pass"
    bpy.context.view_layer.update()
    bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/"hook_island_v2.blend"))


def build():
    reference_ground()
    shop()
    palm()
    pier()
    dressing()
    lighting()
    save()
