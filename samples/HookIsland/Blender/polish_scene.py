"""Final material detailing and sunlit asset presentation, with no environment mesh."""
import bpy
from mathutils import Vector
import hook_island as H


def add_shop_lights():
    import hook_island_assets as A
    for name,kind,location,target,power,size,color in [
        ("Shop interior warm bounce","AREA",A.local(0,-.5,H.SHOP.z+1.97),A.local(0,.6,H.SHOP.z+1.0),22,.9,"FFE5B7"),
        ("Lantern local glow","POINT",A.local(-1.72,-.95,H.SHOP.z+1.64),None,4,.15,"FFD08A"),
    ]:
        if H.SCENE.objects.get(name):continue
        data=bpy.data.lights.new(name,H.enum_id(bpy.types.Light,"type",kind))
        data.energy=power
        data.color=H.rgba(color)[:3]
        if hasattr(data,"normalize"):data.normalize=True
        if kind=="AREA":data.size=size
        else:data.shadow_soft_size=size
        obj=bpy.data.objects.new(name,data)
        H.COL["rig"].objects.link(obj)
        obj.location=location
        if target is not None:
            obj.rotation_euler=(target-obj.location).to_track_quat("-Z","Y").to_euler()


def apply():
    scene=H.SCENE
    ground=scene.objects["Island_Ground_Editable"]
    attribute=ground.data.attributes["GrassDistance"]
    for vert,item in zip(ground.data.vertices,attribute.data):
        item.value=H.grass_distance(vert.co.x,vert.co.y)
    mat=ground.data.materials[0]
    nodes,links=mat.node_tree.nodes,mat.node_tree.links
    coords=next(n for n in nodes if n.type=="TEX_COORD")
    grass=next(n for n in nodes if n.type=="VALTORGB" and n.color_ramp.elements[0].position>.1)
    grass.color_ramp.elements[0].color=H.rgba("799C26")
    grass.color_ramp.elements[1].color=H.rgba("B6C440")
    grass.color_ramp.elements[0].position=.23
    grass.color_ramp.elements[1].position=.77
    # Small irregular color facets read as short groundcover instead of cloudy turf.
    cells=nodes.new("ShaderNodeTexVoronoi")
    cells.label="Small irregular turf facets"
    cells.inputs["Scale"].default_value=5.4
    links.new(coords.outputs["Object"],cells.inputs["Vector"])
    bw=nodes.new("ShaderNodeRGBToBW")
    links.new(cells.outputs["Color"],bw.inputs[0])
    tint=nodes.new("ShaderNodeValToRGB")
    tint.color_ramp.elements[0].color=(.66,.76,.46,1)
    tint.color_ramp.elements[1].color=(1.08,1.07,.98,1)
    links.new(bw.outputs[0],tint.inputs[0])
    facet=nodes.new("ShaderNodeMixRGB")
    H.set_enum(facet,"blend_type","MULTIPLY")
    facet.inputs[0].default_value=.42
    links.new(grass.outputs[0],facet.inputs[1])
    links.new(tint.outputs[0],facet.inputs[2])
    groundmix=next(n for n in nodes if n.type=="MIX_RGB" and n!=facet)
    links.new(facet.outputs[0],groundmix.inputs[2])
    # Direct sunlight supplies clear local shadows; the fill remains gentle.
    for obj in H.COL["rig"].objects:
        if obj.type!="LIGHT":continue
        data=obj.data
        if data.node_tree:
            emission=next((n for n in data.node_tree.nodes if n.type=="EMISSION"),None)
            if emission:
                emission.inputs["Color"].default_value=(1,1,1,1)
                emission.inputs["Strength"].default_value=1
        if hasattr(data,"normalize"):data.normalize=True
        if data.type=="SUN":
            data.energy=3
            data.angle=.055
            data.color=H.rgba("FFF6E8")[:3]
        else:
            data.energy=350
    world=scene.world
    bg=next(n for n in world.node_tree.nodes if n.type=="BACKGROUND")
    bg.inputs["Strength"].default_value=.28
    # A plain blue-grey camera backdrop; it is a shader, not a sea or ground plane.
    output=next(n for n in world.node_tree.nodes if n.type=="OUTPUT_WORLD")
    camera_bg=world.node_tree.nodes.new("ShaderNodeBackground")
    camera_bg.label="Plain studio backdrop, no water"
    camera_bg.inputs["Color"].default_value=H.rgba("ABC6D3")
    camera_bg.inputs["Strength"].default_value=.8
    path=world.node_tree.nodes.new("ShaderNodeLightPath")
    mix=world.node_tree.nodes.new("ShaderNodeMixShader")
    world.node_tree.links.new(path.outputs["Is Camera Ray"],mix.inputs[0])
    world.node_tree.links.new(bg.outputs[0],mix.inputs[1])
    world.node_tree.links.new(camera_bg.outputs[0],mix.inputs[2])
    world.node_tree.links.new(mix.outputs[0],output.inputs["Surface"])
    # Select Cycles if present; keep the available engine on other Blender builds.
    try:
        scene.render.engine="CYCLES"
        scene.cycles.samples=64
        scene.cycles.use_denoising=True
    except TypeError:
        pass
    scene.camera=scene.objects["Camera_Hero"]
    scene.camera.location=(0,-42,38)
    scene.camera.rotation_euler=(Vector((0,-.8,.5))-scene.camera.location).to_track_quat("-Z","Y").to_euler()
    scene.camera.data.ortho_scale=37.6
    scene.render.resolution_x=1600
    scene.render.resolution_y=1100
    scene.render.resolution_percentage=100
    add_shop_lights()
    H.focus_view()
    print("Final materials and daylight applied; scene contains no ocean or offshore rocks.")
