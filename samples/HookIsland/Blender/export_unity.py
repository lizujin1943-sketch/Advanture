"""Bake the approved v2 scene on temporary copies, then export a Unity FBX.

Run setup(), bake_group(name) for each GROUPS entry, then finish() in Blender.
The source scene, meshes, materials and .blend file are never modified.
"""
from pathlib import Path
import json
import math
import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "Exports"
GROUPS = {
    "Ground": ("01 · Island terrain", "Island_Ground_Editable", 2048),
    "SandBank": ("01 · Island terrain", "Island_Sand_Bank_And_Base", 512),
    "Shop": ("02 · Round hook shop", None, 2048),
    "Palm": ("03 · Coconut tree", None, 2048),
    "Pier": ("04 · Shore-connected pier", None, 2048),
    "Dressing": ("05 · Flowers grass and gravel", None, 1024),
}
SOURCE = None
TEMP = None
OBJECTS = {}
REPORT = {}


def enum(rna, prop, value):
    ids = [i.identifier for i in rna.properties[prop].enum_items]
    if value not in ids:
        raise ValueError((prop, value, ids))
    return value


def select(objects):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]


def setup():
    global SOURCE, TEMP, REPORT
    SOURCE = bpy.context.scene
    if SOURCE.name != "Hook Island | v2":
        raise RuntimeError("Select the approved Hook Island | v2 scene first.")
    if bpy.data.scenes.get("_HookIsland_Unity_Export"):
        raise RuntimeError("An export scene already exists; finish or clean it first.")
    OUT.mkdir(parents=True, exist_ok=True)
    TEMP = bpy.data.scenes.new("_HookIsland_Unity_Export")
    TEMP.render.engine = SOURCE.render.engine
    TEMP.cycles.samples = 16
    TEMP.render.bake.margin = 12
    TEMP.unit_settings.scale_length = 1
    depsgraph = bpy.context.evaluated_depsgraph_get()
    materials = {}
    REPORT = {"source": bpy.data.filepath, "groups": {}, "axis_markers": True}
    for key, (collection, exact, size) in GROUPS.items():
        copies = []
        originals = [o for o in bpy.data.collections[collection].objects
                     if o.type == "MESH" and (exact is None or o.name == exact)]
        for src in originals:
            mesh = bpy.data.meshes.new_from_object(src.evaluated_get(depsgraph),
                                                  preserve_all_data_layers=True,
                                                  depsgraph=depsgraph)
            obj = bpy.data.objects.new("_Export_" + src.name, mesh)
            obj.matrix_world = src.matrix_world.copy()
            TEMP.collection.objects.link(obj)
            copies.append(obj)
            # Keep each original object's generated 3D coordinates after joining.
            attr = mesh.attributes.new("SourceGenerated", "FLOAT_VECTOR", "POINT")
            xyz = [v.co for v in mesh.vertices]
            mn = Vector(tuple(min(v[i] for v in xyz) for i in range(3)))
            mx = Vector(tuple(max(v[i] for v in xyz) for i in range(3)))
            for v, a in zip(mesh.vertices, attr.data):
                a.vector = tuple((v.co[i] - mn[i]) / max(mx[i]-mn[i], .000001)
                                 for i in range(3))
            for slot in obj.material_slots:
                mat = slot.material
                if mat is None:
                    continue
                if mat.name not in materials:
                    clone = mat.copy()
                    materials[mat.name] = clone
                    nodes, links = clone.node_tree.nodes, clone.node_tree.links
                    coord = nodes.new("ShaderNodeAttribute")
                    coord.attribute_name = "SourceGenerated"
                    for node in list(nodes):
                        if node.type == "TEX_COORD":
                            for link in list(node.outputs["Generated"].links):
                                links.new(coord.outputs["Vector"], link.to_socket)
                    bs = next(n for n in nodes if n.type == "BSDF_PRINCIPLED")
                    emit = nodes.new("ShaderNodeEmission")
                    color = bs.inputs["Base Color"]
                    if color.is_linked:
                        links.new(color.links[0].from_socket, emit.inputs["Color"])
                    else:
                        emit.inputs["Color"].default_value = color.default_value
                    emit.inputs["Strength"].default_value = 1
                    output = next(n for n in nodes if n.type == "OUTPUT_MATERIAL")
                    links.new(emit.outputs[0], output.inputs["Surface"])
                slot.material = materials[mat.name]
        bpy.context.window.scene = TEMP
        select(copies)
        bpy.ops.object.join()
        obj = bpy.context.object
        obj.name = "Island_" + key
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        OBJECTS[key] = obj
        if key == "Ground":
            uv = obj.data.uv_layers.get("Reference_Plan")
            obj.data.uv_layers.active = uv
            uv.active_render = True
        else:
            for uv in list(obj.data.uv_layers):
                obj.data.uv_layers.remove(uv)
            obj.data.uv_layers.new(name="Unity_Atlas")
            bpy.ops.object.mode_set(mode="EDIT")
            bpy.ops.mesh.select_all(action="SELECT")
            bpy.ops.uv.smart_project(angle_limit=math.radians(66),
                                    island_margin=.012, scale_to_bounds=True)
            bpy.ops.object.mode_set(mode="OBJECT")
        REPORT["groups"][key] = {
            "source_objects": len(originals), "vertices": len(obj.data.vertices),
            "polygons": len(obj.data.polygons), "texture_size": size,
            "albedo": key + "_Albedo.png", "occlusion": key + "_AO.png",
        }
    # Calibrated coordinate markers remove guesswork in Unity's FBX conversion.
    for name, p in [("Origin", (0, 0, 0)), ("X", (1, 0, 0)),
                    ("Y", (0, 1, 0)), ("Z", (0, 0, 1))]:
        obj = bpy.data.objects.new("__BlenderAxis_" + name, None)
        TEMP.collection.objects.link(obj)
        obj.location = p
    source_ground = SOURCE.objects["Island_Ground_Editable"]
    # The lower rim follows the same XY outline as the top. Export only XY.
    body = SOURCE.objects["Island_Sand_Bank_And_Base"]
    boundary = [list(v.co[:2]) for v in body.data.vertices
                if abs(v.co.z - .035) < .0001]
    REPORT["shoreline_blender_xy"] = boundary
    REPORT["camera_blender"] = list(SOURCE.objects["Camera_Hero"].location)
    REPORT["camera_target_blender"] = [0, -.5, .5]
    REPORT["sun_blender"] = [10, 8, 18]
    (OUT / "export_manifest.json").write_text(json.dumps(REPORT, indent=2)+"\n")
    bpy.context.window.scene = SOURCE
    print("Export copies prepared:", {k: v["source_objects"] for k,v in REPORT["groups"].items()})
    print("Shoreline vertices:", len(boundary))


def bake_group(key):
    bpy.context.window.scene = TEMP
    obj = OBJECTS[key]
    size = GROUPS[key][2]
    select([obj])
    for suffix, kind, resolution in [("Albedo", "EMIT", size),
                                      ("AO", "AO", min(size, 1024))]:
        image = bpy.data.images.new("Unity_" + key + "_" + suffix,
                                    width=resolution, height=resolution, alpha=False)
        image.colorspace_settings.name = "sRGB" if suffix == "Albedo" else "Non-Color"
        targets = []
        for mat in set(obj.data.materials):
            node = mat.node_tree.nodes.new("ShaderNodeTexImage")
            node.image = image
            mat.node_tree.nodes.active = node
            targets.append((mat, node))
        bpy.ops.object.bake(type=enum(bpy.ops.object.bake.get_rna_type(), "type", kind))
        image.filepath_raw = str(OUT / (key + "_" + suffix + ".png"))
        image.file_format = enum(image.bl_rna, "file_format", "PNG")
        image.save()
        for mat, node in targets:
            mat.node_tree.nodes.remove(node)
        print(key, suffix, "saved", resolution)
    bpy.context.window.scene = SOURCE


def finish():
    bpy.context.window.scene = TEMP
    for key, obj in OBJECTS.items():
        for suffix in ("Albedo", "AO"):
            if not (OUT / (key + "_" + suffix + ".png")).exists():
                raise RuntimeError("Missing bake: "+key+" "+suffix)
        mat = bpy.data.materials.new("Island_" + key)
        mat.use_nodes = True
        bs = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
        bs.inputs["Roughness"].default_value = .85
        image = mat.node_tree.nodes.new("ShaderNodeTexImage")
        image.image = bpy.data.images.load(str(OUT / (key + "_Albedo.png")),check_existing=True)
        mat.node_tree.links.new(image.outputs["Color"], bs.inputs["Base Color"])
        obj.data.materials.clear()
        obj.data.materials.append(mat)
        for poly in obj.data.polygons:
            poly.material_index = 0
    select(list(TEMP.objects))
    rna = bpy.ops.export_scene.fbx.get_rna_type()
    bpy.ops.export_scene.fbx(
        filepath=str(OUT / "hook_island_v2.fbx"), use_selection=True,
        global_scale=1, apply_unit_scale=True,
        apply_scale_options=enum(rna, "apply_scale_options", "FBX_SCALE_UNITS"),
        axis_forward=enum(rna, "axis_forward", "-Z"),
        axis_up=enum(rna, "axis_up", "Y"),
        object_types={"MESH","EMPTY"}, bake_anim=False, use_mesh_modifiers=True,
        mesh_smooth_type=enum(rna, "mesh_smooth_type", "FACE"),
    )
    (OUT / "export_manifest.json").write_text(json.dumps(REPORT, indent=2)+"\n")
    bpy.context.window.scene = SOURCE
    for obj in list(TEMP.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    bpy.data.scenes.remove(TEMP)
    print("UNITY_EXPORT_OK", str(OUT / "hook_island_v2.fbx"))
    print("Original scene restored, original blend file not changed.")
