"""Editable low-poly island reconstructed from hook-island-multiview-v1.png.

Run inside Blender, or import and call the individual build stages through MCP.
No downloads, add-on dependencies, ocean, or underwater rocks.
"""
from pathlib import Path
import math
import random
import json
import bpy
from mathutils import Vector
from mathutils.geometry import delaunay_2d_cdt
from mathutils import noise

ROOT = Path(__file__).resolve().parent
REFERENCE = ROOT.parents[2] / "docs/concepts/hook-island-multiview-v1.png"
SEED = 2117
RNG = random.Random(SEED)
COL = {}
MAT = {}
SCENE = None
SHOP = Vector((0.15, 1.3, 0))
FRONT = -math.pi / 2 - math.radians(12)

# Hand-traced from the TOP panel. Coordinates are converted to editable metres.
TRACE = [
    (256, 53), (310, 62), (348, 87), (365, 118), (404, 145),
    (467, 160), (514, 184), (546, 223), (570, 253), (627, 279),
    (678, 325), (711, 383), (719, 439), (698, 489), (674, 536),
    (635, 573), (585, 595), (539, 595), (492, 570), (445, 546),
    (394, 525), (331, 507), (278, 489), (231, 470), (190, 447),
    (153, 425), (135, 393), (137, 353), (156, 321), (158, 290),
    (139, 264), (100, 247), (75, 226), (69, 203), (90, 171),
    (132, 144), (169, 119), (190, 84), (219, 64),
]


def rgba(value):
    """sRGB hex to Blender's linear shader colour."""
    value = value.lstrip("#")
    c = [int(value[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(v / 12.92 if v <= .04045 else ((v + .055) / 1.055) ** 2.4 for v in c) + (1,)


def enum_id(owner, prop, wanted):
    prop = owner.bl_rna.properties[prop]
    items = [i.identifier for i in prop.enum_items]
    if wanted not in items:
        raise ValueError(f"{wanted} unavailable; valid values: {items}")
    return wanted


def set_enum(owner, prop, wanted):
    setattr(owner, prop, enum_id(owner, prop, wanted))


def collection(name):
    c = bpy.data.collections.new(name)
    SCENE.collection.children.link(c)
    return c


def material(name, color, roughness=.7, metal=0, grain=False, emission=0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    m.diffuse_color = rgba(color)
    bs = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bs.inputs["Base Color"].default_value = rgba(color)
    bs.inputs["Roughness"].default_value = roughness
    bs.inputs["Metallic"].default_value = metal
    if emission:
        bs.inputs["Emission Color"].default_value = rgba(color)
        bs.inputs["Emission Strength"].default_value = emission
    if grain:
        n, l = m.node_tree.nodes, m.node_tree.links
        tc = n.new("ShaderNodeTexCoord")
        mult = n.new("ShaderNodeVectorMath")
        mult.operation = enum_id(mult, "operation", "MULTIPLY")
        mult.inputs[1].default_value = (9, 9, 1.2)
        l.new(tc.outputs["Generated"], mult.inputs[0])
        tex = n.new("ShaderNodeTexNoise")
        tex.inputs["Scale"].default_value = 2.3
        tex.inputs["Detail"].default_value = 1.5
        l.new(mult.outputs["Vector"], tex.inputs["Vector"])
        ramp = n.new("ShaderNodeValToRGB")
        base = rgba(color)
        ramp.color_ramp.elements[0].color = tuple(v * .79 for v in base[:3]) + (1,)
        ramp.color_ramp.elements[1].color = tuple(min(v * 1.08, 1) for v in base[:3]) + (1,)
        l.new(tex.outputs["Fac"], ramp.inputs[0])
        l.new(ramp.outputs[0], bs.inputs["Base Color"])
    return m


def mesh(name, verts, faces, materials, group):
    data = bpy.data.meshes.new(name + "_Mesh")
    data.from_pydata(verts, [], faces)
    data.update()
    obj = bpy.data.objects.new(name, data)
    COL[group].objects.link(obj)
    if not isinstance(materials, (list, tuple)):
        materials = [materials]
    for m in materials:
        if m is not None:
            data.materials.append(m)
    return obj


def bevel(obj, width=.025):
    mod = obj.modifiers.new("Small crafted edges", enum_id(bpy.types.Modifier, "type", "BEVEL"))
    mod.width = width
    mod.segments = 1
    return obj


def box(name, center, size, mat, group, angle=0, edge=0):
    x, y, z = [v / 2 for v in size]
    verts = [(-x,-y,-z), (x,-y,-z), (x,y,-z), (-x,y,-z),
             (-x,-y,z), (x,-y,z), (x,y,z), (-x,y,z)]
    faces = [(0,3,2,1), (0,1,5,4), (1,2,6,5), (2,3,7,6), (3,0,4,7), (4,5,6,7)]
    o = mesh(name, verts, faces, mat, group)
    o.location = center
    o.rotation_euler.z = angle
    if edge:
        bevel(o, edge)
    return o


def lathe(name, center, profile, mat, group, sides=12, phase=0):
    verts = []
    for z, radius in profile:
        for j in range(sides):
            a = phase + math.tau * j / sides
            verts.append((radius * math.cos(a), radius * math.sin(a), z))
    faces = [tuple(range(sides - 1, -1, -1))]
    for i in range(len(profile) - 1):
        for j in range(sides):
            k = (j + 1) % sides
            faces.append((i*sides+j, i*sides+k, (i+1)*sides+k, (i+1)*sides+j))
    faces.append(tuple((len(profile) - 1) * sides + j for j in range(sides)))
    o = mesh(name, verts, faces, mat, group)
    o.location = center
    return o


def rod(name, a, b, radius, mat, group, sides=8, radius_end=None):
    a, b = Vector(a), Vector(b)
    length = (b - a).length
    o = lathe(name, a, [(0, radius), (length, radius if radius_end is None else radius_end)], mat, group, sides)
    o.rotation_euler = (b - a).to_track_quat("Z", "Y").to_euler()
    return o


def tube(name, points, radius, mat, group, sides=6):
    points = [Vector(p) for p in points]
    verts, faces = [], []
    for i, p in enumerate(points):
        tangent = points[min(i + 1, len(points)-1)] - points[max(i - 1, 0)]
        q = tangent.to_track_quat("Z", "Y")
        for j in range(sides):
            a = math.tau * j / sides
            v = p + q @ Vector((radius*math.cos(a), radius*math.sin(a), 0))
            verts.append(v)
    for i in range(len(points)-1):
        for j in range(sides):
            k = (j+1) % sides
            faces.append((i*sides+j, i*sides+k, (i+1)*sides+k, (i+1)*sides+j))
    faces.extend([tuple(range(sides-1,-1,-1)), tuple((len(points)-1)*sides+j for j in range(sides))])
    return mesh(name, verts, faces, mat, group)


def smooth_outline(points, samples=4):
    v = [Vector(((x - 390)*.049, (320-y)*.049)) for x, y in points]
    result = []
    for i in range(len(v)):
        a,b,c,d = v[(i-1)%len(v)],v[i],v[(i+1)%len(v)],v[(i+2)%len(v)]
        for j in range(samples):
            t = j/samples
            result.append((2*b + (-a+c)*t + (2*a-5*b+4*c-d)*t*t + (-a+3*b-3*c+d)*t*t*t)*.5)
    return result


OUTLINE = smooth_outline(TRACE)


def inside(p):
    x, y = p[:2]
    result = False
    for a, b in zip(OUTLINE, OUTLINE[1:] + OUTLINE[:1]):
        if (a.y > y) != (b.y > y):
            if x < (b.x-a.x)*(y-a.y)/(b.y-a.y)+a.x:
                result = not result
    return result


def segment_distance(p, a, b):
    p, a, b = Vector(p[:2]), Vector(a[:2]), Vector(b[:2])
    ab = b-a
    t = max(0, min(1, (p-a).dot(ab)/max(ab.length_squared, 1e-8)))
    return (p-(a+t*ab)).length


def shore_distance(x, y):
    p = Vector((x,y))
    return min(segment_distance(p, a, b) for a,b in zip(OUTLINE, OUTLINE[1:]+OUTLINE[:1]))


def terrain_z(x, y):
    d = shore_distance(x,y)
    t = max(0, min(1, d/1.65))
    t = t*t*(3-2*t)
    return .035 + .69*t + .032*math.sin(x*.34)*math.sin(y*.29)*t


def signed_distance(x,y):
    d = shore_distance(x,y)
    return d if inside((x,y)) else -d


DOCK_DIR = Vector((-.62, -.7846018098))
DOCK_START = Vector((-3.8, -3.2))
lo, hi = 0., 20.
for _ in range(38):
    mid = (lo+hi)/2
    p = DOCK_START + DOCK_DIR*mid
    if inside(p):
        lo = mid
    else:
        hi = mid
SHORE = DOCK_START + DOCK_DIR*((lo+hi)/2)
DOCK_SIDE = Vector((-DOCK_DIR.y, DOCK_DIR.x))
DECK_HEIGHT = .80


def init_scene():
    global SCENE
    if bpy.data.scenes.get("Hook Island | v1"):
        raise RuntimeError("Scene already exists. Use the existing module stages rather than rebuilding over edits.")
    SCENE = bpy.data.scenes.new("Hook Island | v1")
    bpy.context.window.scene = SCENE
    set_enum(SCENE.unit_settings,"system","METRIC")
    SCENE.unit_settings.scale_length=1.0
    for key, name in [
        ("land","01 · Island terrain"),
        ("shop","02 · Round hook shop"),
        ("palm","03 · Coconut tree"),
        ("dock","04 · Shore-connected pier"),
        ("detail","05 · Flowers grass and gravel"),
        ("rig","06 · Lighting and cameras"),
        ("ref","07 · Reference (hidden)"),
    ]:
        COL[key] = collection(name)
    COL["ref"].hide_render = True
    COL["ref"].hide_viewport = True
    for i, color in enumerate(["A86431","B8753F","C18348","93552C","D39555"]):
        MAT[f"wood{i}"] = material(f"Timber / warm {i}",color,grain=True)
    for i, color in enumerate(["EC7D21","F58F2A","E8741C","F39B37","D86D20","F7A043"]):
        MAT[f"roof{i}"] = material(f"Roof / orange facet {i}",color,.72)
    for i, color in enumerate(["597B22","74992D","8CAA35","648726","A6BB43"]):
        MAT[f"leaf{i}"] = material(f"Coconut frond / {i}",color,.82)
    MAT["rope"] = material("Natural hemp rope","D2AA65",.9)
    MAT["iron"] = material("Forged metal","3F5053",.38,.55)
    MAT["brass"] = material("Worn brass","CC9B45",.34,.6)
    MAT["dark"] = material("Deep wood shadow","49321D",.92)
    MAT["sand_side"] = material("Cut sand edge","CDA66B",.96)
    MAT["sand_bottom"] = material("Island underside","A58558",1)
    MAT["flower"] = material("Daisy petals","FFF8DB",.82)
    MAT["pollen"] = material("Daisy golden centre","F2C535",.83)
    MAT["stem"] = material("Low stems","68852D",.88)
    MAT["stone"] = material("Tiny embedded gravel","AAA494",.94)
    MAT["lantern"] = material("Lantern warm glow","FFD165",.3,emission=2.7)
    MAT["pot"] = material("Clay pottery","A87852",.85)
    MAT["bottle"] = material("Teal glazed pottery","4A8F87",.3)
    MAT["coconut"] = material("Two small coconut husks","A7792A",.8)
    SCENE["reference_image"] = str(REFERENCE)
    SCENE["intent"] = "Four-player hook fishing island; sea and underwater rocks deliberately omitted."
    SCENE["units_note"] = "Metres are modeling assumptions; gameplay movement/collision not tested."
    print("Created independent scene and editable collections.")


def ground_material():
    m = material("Ground / natural short grass to sand","AAB744",.96)
    n,l = m.node_tree.nodes,m.node_tree.links
    bs = next(n for n in n if n.type=="BSDF_PRINCIPLED")
    texcoord = n.new("ShaderNodeTexCoord")
    attr = n.new("ShaderNodeAttribute")
    attr.attribute_name = "GrassDistance"
    edge = n.new("ShaderNodeTexNoise")
    edge.inputs["Scale"].default_value = 3.4
    edge.inputs["Detail"].default_value = 1.4
    l.new(texcoord.outputs["Object"],edge.inputs["Vector"])
    mul = n.new("ShaderNodeMath"); set_enum(mul,"operation","MULTIPLY")
    mul.inputs[1].default_value=.7
    l.new(edge.outputs["Fac"],mul.inputs[0])
    add = n.new("ShaderNodeMath"); set_enum(add,"operation","ADD")
    l.new(attr.outputs["Fac"],add.inputs[0]); l.new(mul.outputs[0],add.inputs[1])
    mask = n.new("ShaderNodeMapRange")
    mask.inputs["From Min"].default_value=.27
    mask.inputs["From Max"].default_value=.44
    mask.clamp=True
    l.new(add.outputs[0],mask.inputs["Value"])
    broad=n.new("ShaderNodeTexNoise")
    broad.inputs["Scale"].default_value=.62
    broad.inputs["Detail"].default_value=1.7
    l.new(texcoord.outputs["Object"],broad.inputs["Vector"])
    grass=n.new("ShaderNodeValToRGB")
    grass.color_ramp.elements[0].position=.18
    grass.color_ramp.elements[0].color=rgba("97AA39")
    grass.color_ramp.elements[1].position=.82
    grass.color_ramp.elements[1].color=rgba("BCC958")
    sand=n.new("ShaderNodeValToRGB")
    sand.color_ramp.elements[0].color=rgba("E7C78A")
    sand.color_ramp.elements[1].color=rgba("F8DEA1")
    l.new(broad.outputs["Fac"],grass.inputs[0])
    l.new(broad.outputs["Fac"],sand.inputs[0])
    mix=n.new("ShaderNodeMixRGB"); set_enum(mix,"blend_type","MIX")
    l.new(mask.outputs[0],mix.inputs[0]);l.new(sand.outputs[0],mix.inputs[1]);l.new(grass.outputs[0],mix.inputs[2])
    l.new(mix.outputs[0],bs.inputs["Base Color"])
    # Minute relief conveys grass and fine sand without raised lawn patches.
    fine=n.new("ShaderNodeTexNoise")
    fine.inputs["Scale"].default_value=34
    fine.inputs["Detail"].default_value=1
    l.new(texcoord.outputs["Object"],fine.inputs["Vector"])
    bump=n.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value=.12
    bump.inputs["Distance"].default_value=.018
    l.new(fine.outputs["Fac"],bump.inputs["Height"]);l.new(bump.outputs[0],bs.inputs["Normal"])
    return m


def grass_distance(x,y):
    edge = shore_distance(x,y) - (1.65 + .50*math.sin(x*.82+y*.40) + .42*math.sin(y*1.1-x*.32))
    clearing = (Vector((x,y))-Vector(SHOP[:2])).length - (2.32+.16*math.sin(x*1.6+y))
    # A gently curved sand approach ties the shop to the shore-end of the pier.
    path=[Vector(SHOP[:2])+Vector((-.2,-1.8)),Vector((-2.3,-2.2)),Vector((-4.4,-4.0)),SHORE-DOCK_DIR*2]
    dist=min(segment_distance((x,y),a,b) for a,b in zip(path,path[1:]))
    pathgap=dist-(.43+.18*math.sin(y*.5+x*.2))
    return min(edge,clearing,pathgap)


def build_terrain():
    points=list(OUTLINE)
    for j in range(53):
        y=-13.8+j*.55
        for i in range(61):
            x=-16.2+i*.55
            x+=RNG.uniform(-.13,.13); yy=y+RNG.uniform(-.13,.13)
            if inside((x,yy)) and shore_distance(x,yy)>.18:
                points.append(Vector((x,yy)))
    count=len(OUTLINE)
    edges=[(i,(i+1)%count) for i in range(count)]
    verts2,_,faces,_,_,_=delaunay_2d_cdt(points,edges,[list(range(count))],1,1e-6,False)
    verts=[(p.x,p.y,terrain_z(p.x,p.y)) for p in verts2]
    ground=mesh("Island_Ground_Editable",verts,faces,ground_material(),"land")
    attr=ground.data.attributes.new("GrassDistance","FLOAT","POINT")
    for i,p in enumerate(verts2):
        attr.data[i].value=grass_distance(p.x,p.y)
    ground["coastline_source"]="Hand-traced TOP silhouette from supplied multi-view reference."
    ground["playable_surface"]="Nearly flat interior, gently descending sandy perimeter."
    # Continuous closed low terrain body, without rock geometry or an ocean mesh.
    rim=list(OUTLINE)
    center=sum(rim,Vector((0,0)))/len(rim)
    rings=[
        [(p.x,p.y,.035) for p in rim],
        [(p.x+(p.x-center.x)*.016,p.y+(p.y-center.y)*.016,-.33-.06*math.sin(i*1.3)) for i,p in enumerate(rim)],
        [(center.x+(p.x-center.x)*.985,center.y+(p.y-center.y)*.985,-1.05) for p in rim],
    ]
    bodyverts=[p for ring in rings for p in ring]
    bodyfaces=[]
    for r in range(2):
        for i in range(count):
            j=(i+1)%count
            bodyfaces.extend([(r*count+i,r*count+j,(r+1)*count+j),
                              (r*count+i,(r+1)*count+j,(r+1)*count+i)])
    bodyfaces.append(tuple(2*count+i for i in range(count-1,-1,-1)))
    body=mesh("Island_Sand_Bank_And_Base",bodyverts,bodyfaces,[MAT["sand_side"],MAT["sand_bottom"]],"land")
    body.data.polygons[-1].material_index=1
    print(f"Terrain: {len(verts)} vertices, {len(faces)} top faces. Shore connection {list(SHORE)}")


def setup_cameras_lighting():
    world=bpy.data.worlds.new("Soft coastal daylight (no ocean)")
    world.use_nodes=True
    bg=next(n for n in world.node_tree.nodes if n.type=="BACKGROUND")
    bg.inputs["Color"].default_value=rgba("C9D5DB")
    bg.inputs["Strength"].default_value=.7
    SCENE.world=world
    def light(name,kind,loc,energy,color,size):
        d=bpy.data.lights.new(name,enum_id(bpy.types.Light,"type",kind))
        d.energy=energy;d.color=rgba(color)[:3]
        o=bpy.data.objects.new(name,d);COL["rig"].objects.link(o);o.location=loc
        o.rotation_euler=(Vector((0,0,0))-o.location).to_track_quat("-Z","Y").to_euler()
        if hasattr(d,"size"):d.size=size
        return o
    sun=light("Sun · warm late morning","SUN",(-12,-10,20),2.4,"FFF0D0",5)
    sun.data.angle=math.radians(12)
    light("Sky fill","AREA",(2,-6,18),1700,"DDEFFD",16)
    light("Soft backlight","AREA",(6,12,13),2100,"FFF0D9",12)
    cameras=[
        ("Camera_Hero",(0,-42,38),(0,0,1.1),39),
        ("Camera_Top",(0,0,55),(0,0,0),39),
        ("Camera_Front",(0,-55,4),(0,0,3),39),
        ("Camera_Left",(-55,0,4),(0,0,3),35),
        ("Camera_Right",(55,0,4),(0,0,3),35),
        ("Camera_Dock_Detail",tuple(Vector((SHORE.x,SHORE.y,0))+Vector((8,-10,7))),tuple(Vector((SHORE.x,SHORE.y,.4))+Vector((DOCK_DIR.x,DOCK_DIR.y,0))),10),
        ("Camera_Shop_Detail",(8,-13,10),(0,1.8,2.5),13),
    ]
    for name,location,target,scale in cameras:
        data=bpy.data.cameras.new(name)
        set_enum(data,"type","ORTHO")
        data.ortho_scale=scale
        o=bpy.data.objects.new(name,data);COL["rig"].objects.link(o)
        o.location=location
        o.rotation_euler=(Vector(target)-o.location).to_track_quat("-Z","Y").to_euler()
    # Exactly horizontal side cameras; TOP is a genuine vertical projection.
    for name,loc,target in [
        ("Camera_Front",(0,-55,3),(0,0,3)),
        ("Camera_Left",(-55,0,3),(0,0,3)),
        ("Camera_Right",(55,0,3),(0,0,3)),
    ]:
        o=bpy.data.objects[name];o.location=loc
        o.rotation_euler=(Vector(target)-o.location).to_track_quat("-Z","Y").to_euler()
    SCENE.camera=bpy.data.objects["Camera_Hero"]
    SCENE.render.resolution_x=1600
    SCENE.render.resolution_y=1200
    SCENE.render.resolution_percentage=100
    set_enum(SCENE.render.image_settings,"file_format","PNG")
    SCENE.render.film_transparent=False
    SCENE.eevee.taa_render_samples=64
    SCENE.eevee.use_fast_gi=True
    SCENE.eevee.use_raytracing=True


def save_scene():
    ROOT.mkdir(parents=True,exist_ok=True)
    (ROOT/"renders").mkdir(exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/"hook_island_v1.blend"))
    print("Saved",ROOT/"hook_island_v1.blend")


def focus_view():
    camera=SCENE.camera
    for window in bpy.context.window_manager.windows:
        if window.scene==SCENE:
            for area in window.screen.areas:
                if area.type=="VIEW_3D":
                    space=area.spaces.active
                    space.region_3d.view_location=(0,0,1)
                    space.region_3d.view_rotation=camera.rotation_euler.to_quaternion()
                    space.region_3d.view_distance=42
                    set_enum(space.region_3d,"view_perspective","ORTHO")
                    set_enum(space.shading,"type","MATERIAL")
                    space.shading.use_scene_lights=True
                    space.shading.use_scene_world=True
                    space.overlay.show_overlays=False
                    space.show_region_ui=False


def stage_one():
    init_scene()
    build_terrain()
    setup_cameras_lighting()
    focus_view()
    save_scene()


if __name__ == "__main__":
    stage_one()
