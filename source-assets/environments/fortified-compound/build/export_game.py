"""Run with Blender -b EN_FortifiedCompound_Art.blend -P build/export_game.py.

Export one static render mesh per sector, physical surface proxies, material
swatches and the integer playing surface from the same evaluated geometry.
Blender (east, north, up) is Unity (X, Z, Y), in metres.
"""
import json
import math
import re
import struct
import sys
import zlib
from pathlib import Path

import bpy
import bmesh
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

ROOT = Path(__file__).resolve().parents[4]
OUT = ROOT / 'samples/TacticalGame.Unity/Assets/TacticalGame/Art/FortifiedCompound'
OUT.mkdir(parents=True, exist_ok=True)
scene = bpy.data.scenes['EN_FortifiedCompound']
bpy.context.window.scene = scene
graph = bpy.context.evaluated_depsgraph_get()
stage = bpy.data.scenes.new('Fortified compound game export')
palette = {}
groups = {}
supports, solids = [], []


def contact(name):
    name = re.sub(r'\.\d+$', '', name)
    if (name in ('Paved plaza', 'Plaza west ramp', 'C1 gate apron', 'B2 foundation', 'B2 | entry steps', 'Flag base step')
            or name.startswith(('Compound terrain', 'Plaza south stairs', 'Plaza east stairs'))
            or name.endswith(('upper landing', 'roof finish', 'continuous stair and landings',
                              ' floor', 'receiving apron', 'north exit apron'))):
        return 'floor'
    if (re.match(r'^(Wall \d+|Tower \d+ (shaft|plinth))$', name)
            or re.search(r'breach (east|west|north|south)$', name)
            or name in ('Raised square plaza', 'Wall pier', 'Wall buttress', 'Ramp curb',
                        'Plaza stair pier', 'Single flagpole', 'Flag plinth', 'Flag pedestal',
                        'HVAC steel casing', 'Canvas sandbag', 'B2 drum', 'Tree trunk',
                        'Warehouse horizontal tank', 'B1 utility tank', 'Medical bench top',
                        'Armory east wall', 'Utility shed', 'Supply shed', 'Collapsed concrete')
            or name.endswith((' body', ' foundation', 'facade pier', 'curved coping', 'foot',
                              'rounded roof parapet', 'curved warehouse walls', 'forecourt return',
                              ' pier', 'handrail', 'guard rail', ' jamb', 'transport shell',
                              'issue cupboard', 'stacking container', 'wall cabinet', 'enclosure joint east',
                              'enclosure joint west', 'structural rib', 'retained concrete canopy'))):
        return 'solid'
    return None


def rgb(color):
    # Unity Color properties use sRGB swatches and convert to scene-linear on upload.
    def gamma(v):
        return 12.92 * v if v < .0031308 else 1.055 * v ** (1 / 2.4) - .055
    return [round(gamma(max(0, min(1, v))), 5) for v in color[:3]]


def game_material(material):
    name = material.name if material else 'Unpainted'
    if name in palette:
        return palette[name][0]
    nodes = list(material.node_tree.nodes) if material and material.node_tree else []
    labels = {n.label: n for n in nodes if n.label}
    bsdf = next((n for n in nodes if n.type == 'BSDF_PRINCIPLED'), None)
    swatch = material.diffuse_color if material else (.4, .4, .4, 1)
    record = dict(id='FCM_%03d' % len(palette), title=name, dark=rgb(swatch), light=rgb(swatch),
                  paint=rgb(swatch), band=-1., tile='', meters=3., low=0., high=1., plaza=False,
                  rough=.76, metal=0.)
    if bsdf:
        record['metal'] = bsdf.inputs['Metallic'].default_value
        record['rough'] = bsdf.inputs['Roughness'].default_value
    mix = labels.get('Mineral colour') or next((n for n in nodes if n.type == 'MIX_RGB' and not n.inputs[1].is_linked and not n.inputs[2].is_linked), None)
    if mix:
        record.update(dark=rgb(mix.inputs[1].default_value), light=rgb(mix.inputs[2].default_value))
    if 'Scanned albedo range' in labels:
        n = labels['Scanned albedo range']
        img = n.inputs[0].links[0].from_node
        scale = img.inputs[0].links[0].from_node.inputs[1].default_value[0]
        record.update(tile=img.label.split(' / ')[0], meters=1 / scale,
                      low=n.inputs['From Min'].default_value, high=n.inputs['From Max'].default_value,
                      plaza='Cast slab joints' in labels, rough=.8)
    if 'Lower painted course' in labels:
        record.update(paint=rgb(labels['Paint texture'].inputs[1].default_value),
                      band=labels['Lower painted course'].inputs['From Min'].default_value+.003)
    simple = bpy.data.materials.new(record['id'])
    palette[name] = simple, record
    return simple


def add(group, mesh):
    obj = bpy.data.objects.new(group, mesh)
    stage.collection.objects.link(obj)
    groups.setdefault(group, []).append(obj)


for collection in scene.collection.children:
    if not collection.name.startswith('FC_'):
        continue
    sector = collection.name.split('.')[0]
    for obj in collection.objects:
        if obj.hide_render or obj.type not in ('MESH', 'CURVE', 'FONT'):
            continue
        evaluated = obj.evaluated_get(graph)
        mesh = bpy.data.meshes.new_from_object(evaluated, depsgraph=graph)
        if not mesh.polygons:
            bpy.data.meshes.remove(mesh)
            continue
        if obj.name == 'Red cloth flag':
            # The reference cloth is an open sheet. Give it two surfaces for real-time culling.
            bm = bmesh.new()
            bm.from_mesh(mesh)
            bmesh.ops.solidify(bm, geom=list(bm.faces), thickness=.004)
            bm.to_mesh(mesh)
            bm.free()
        mats = [game_material(slot.material) for slot in evaluated.material_slots]
        faces = np.empty(len(mesh.polygons), dtype=np.int32)
        mesh.polygons.foreach_get('material_index', faces)
        mesh.materials.clear()
        for mat in mats or [game_material(None)]:
            mesh.materials.append(mat)
        mesh.polygons.foreach_set('material_index', faces)
        mesh.transform(evaluated.matrix_world)
        coords = np.empty(len(mesh.vertices)*3, dtype=np.float32)
        mesh.vertices.foreach_get('co', coords)
        coords = coords.reshape(-1, 3)
        z = coords[:, 2]
        loops = np.empty(len(mesh.loops), dtype=np.int32)
        mesh.loops.foreach_get('vertex_index', loops)
        while len(mesh.uv_layers):
            mesh.uv_layers.remove(mesh.uv_layers[0])
        uv = mesh.uv_layers.new(name='PaintHeight')
        values = np.zeros((len(loops), 2), dtype=np.float32)
        values[:, 0] = (z[loops]-z.min()) / max(.0001, z.max()-z.min())
        uv.data.foreach_set('uv', values.ravel())
        add(sector, mesh)
        kind = contact(obj.name)
        if kind:
            # Use true outlines. A ring wall must retain its courtyard opening.
            mesh.calc_loop_triangles()
            indices = np.empty(len(mesh.loop_triangles)*3, dtype=np.int32)
            mesh.loop_triangles.foreach_get('vertices', indices)
            tree = BVHTree.FromPolygons(coords.tolist(), indices.reshape(-1, 3).tolist(), all_triangles=True)
            (supports if kind == 'floor' else solids).append((obj.name, tree, coords.min(axis=0), coords.max(axis=0)))
            add('COL_'+sector, mesh.copy())

# Sample footholds and cover separately so a blocked cell still occludes at its physical height.
cell, xmin, zmin, width, depth = .25, -40., -57., 320, 452
absent = -32768
floor = np.full((depth, width), absent, dtype=np.int16)
cover = floor.copy()
ledge = floor.copy()
sky = 32767
ceiling = np.full_like(floor, sky)
blocked = np.zeros_like(floor, dtype=bool)
for is_floor, objects in [(True, supports), (False, solids)]:
    for name, tree, lo, hi in objects:
        for row in range(max(0, math.floor((lo[1]-zmin)/cell)), min(depth, math.ceil((hi[1]-zmin)/cell))):
            north = zmin+(row+.5)*cell
            for col in range(max(0, math.floor((lo[0]-xmin)/cell)), min(width, math.ceil((hi[0]-xmin)/cell))):
                east = xmin+(col+.5)*cell
                point, normal, _, _ = tree.ray_cast((east,north,hi[2]+1), (0,0,-1))
                if 'continuous stair and landings' in name and (point is None or normal.z < .5):
                    # A cell centre can coincide with a vertical riser or its 9 mm bevel.
                    # Sample the neighbouring tread inside the same cell at either side of the lip.
                    nearby = [tree.ray_cast((east+x,north+y,hi[2]+1),(0,0,-1)) for x,y in
                              [(-.022,0),(.022,0),(0,-.022),(0,.022)]]
                    treads = [hit for hit in nearby if hit[0] is not None and hit[1].z > .5]
                    if treads:
                        point, normal, _, _ = max(treads,key=lambda hit:hit[0].z)
                if point is None:
                    continue
                top = round(point.z * 100)
                if is_floor:
                    if normal.z > .5:
                        floor[row,col] = max(floor[row,col], top)
                else:
                    base, _, _, _ = tree.ray_cast((east,north,lo[2]-1), (0,0,1))
                    bottom = round((base.z if base is not None else lo[2])*100)
                    foot = int(floor[row,col])
                    if bottom >= foot+190:
                        ceiling[row,col] = min(ceiling[row,col], bottom)
                    elif top > foot+13:
                        # Low, upward-facing solids can support a landing. The plaza coping
                        # is also accessible from its raised side, despite the drop outside.
                        if normal.z > .5 and (top <= foot+120 or 'curved coping' in name):
                            ledge[row,col] = max(ledge[row,col], top)
                        else:
                            blocked[row,col] = True
                    if bottom <= foot+190:
                        cover[row,col] = max(cover[row,col], top)
                if is_floor:
                    cover[row,col] = max(cover[row,col], top)
# Clearance for the character radius includes cells outside the physical island.
occupied = np.pad(blocked | (floor == absent), 1, constant_values=True)
ledge_neighbours = np.pad(ledge, 1, constant_values=absent)
overhead_neighbours = np.pad(ceiling, 1, constant_values=sky)
margin = np.zeros_like(blocked)
for_feet = floor.copy()
for dr in range(3):
    for dc in range(3):
        margin |= occupied[dr:dr+depth,dc:dc+width]
        np.maximum(for_feet, ledge_neighbours[dr:dr+depth, dc:dc+width], out=for_feet)
        np.minimum(ceiling, overhead_neighbours[dr:dr+depth, dc:dc+width], out=ceiling)
for_feet[margin | (ceiling.astype(np.int32) - for_feet < 190)] = absent
columns = np.stack((for_feet, cover, ceiling), axis=-1).astype('<i2')
compressor = zlib.compressobj(9, zlib.DEFLATED, -15)
packed = compressor.compress(columns.tobytes())+compressor.flush()
# Facing the flag and radar from the raised plaza. Both slots have clear floor and headroom.
spawns = [(-8250, 7400, 2550), (-2250, 7400, 33500)]
header = struct.pack('<I6iB', 0x34504d54, -40000,-57000,40000,56000,-1000,24000, len(spawns))
for x,z,yaw in spawns:
    assert for_feet[int((z/1000-zmin)/cell),int((x/1000-xmin)/cell)] == 205
    header += struct.pack('<iiH',x,z,yaw)
(OUT/'FortifiedCompound.bytes').write_bytes(header + struct.pack('<H6i',0,-40000,-57000,250,width,depth,len(packed)) + packed)
if '--terrain-only' in sys.argv:
    print(f'FORT_TERRAIN_OK grid={len(packed)} walk={np.sum(for_feet!=absent)}')
    sys.exit(0)
(OUT/'Surfaces.json').write_text(json.dumps(dict(materials=[v[1] for v in palette.values()]),indent=2))

bpy.context.window.scene = stage
for name, items in groups.items():
    bpy.ops.object.select_all(action='DESELECT')
    for obj in items:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = items[0]
    if len(items)>1:
        bpy.ops.object.join()
    items[0].name = name
    items[0].data.name = name
for name, pos in [('AxisOrigin',(0,0,0)),('AxisEast',(10,0,0)),('AxisNorth',(0,10,0)),('AxisUp',(0,0,10))]:
    marker = bpy.data.objects.new(name, None)
    stage.collection.objects.link(marker)
    marker.location = pos
bpy.ops.export_scene.fbx(filepath=str(OUT/'FortifiedCompound.fbx'), use_selection=False,
                         object_types={'EMPTY','MESH'}, use_mesh_modifiers=False, bake_anim=False,
                         axis_forward='-Z',axis_up='Y',apply_scale_options='FBX_SCALE_ALL',
                         bake_space_transform=True, add_leaf_bones=False, mesh_smooth_type='FACE')
print(f'FORT_EXPORT_OK meshes={len(groups)} materials={len(palette)} grid={len(packed)} walk={np.sum(floor!=absent)}')
