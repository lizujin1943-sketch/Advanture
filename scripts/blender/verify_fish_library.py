"""Validate source skinning and animation, then reimport every FBX and GLB.

Blender --background source-assets/creatures/fish/FishLibrary.blend
        --python-exit-code 1 --python scripts/blender/verify_fish_library.py
"""
from pathlib import Path
import hashlib
import json
import math
import struct
import bpy
import bmesh
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[2]
SOURCE=Path(bpy.data.filepath).parent
MANIFEST=json.loads((SOURCE/'manifest.json').read_text())
OUTPUT=ROOT/'artifacts/fish-models'
OUTPUT.mkdir(parents=True,exist_ok=True)
reports=[]


def require(condition,message):
    if not condition:
        raise AssertionError(message)


def evaluated_vertices(obj):
    evaluated=obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
    data=evaluated.to_mesh()
    result=[vertex.co.copy() for vertex in data.vertices]
    evaluated.to_mesh_clear()
    return result


def glb_document(path):
    content=path.read_bytes()
    require(content[:4]==b'glTF','Not a glTF binary: '+str(path))
    length=struct.unpack_from('<I',content,12)[0]
    return json.loads(content[20:20+length])


require(len(MANIFEST['species'])==11,'Expected ten sea fish and one piranha')
require(MANIFEST['clip_count']==78,'Expected 78 independent clips')
fingerprints=set()
for entry in MANIFEST['species']:
    mesh=bpy.data.objects[entry['mesh']]
    rig=bpy.data.objects[entry['rig']]
    require(len(mesh.data.materials)==1,entry['id']+': material count')
    require(len(mesh.data.polygons)<=MANIFEST['triangle_budget_per_fish'],entry['id']+': triangle budget')
    require(all(len(p.vertices)==3 for p in mesh.data.polygons),entry['id']+': non-triangle face')
    require(mesh.data.uv_layers.active is not None,entry['id']+': missing UV')
    require(all(math.isfinite(c) and 0<=c<=1 for uv in mesh.data.uv_layers.active.data for c in uv.uv),
            entry['id']+': invalid UV')
    require(all(abs(s-1)<1e-7 for s in mesh.scale),entry['id']+': unapplied mesh scale')
    require(all(abs(s-1)<1e-7 for s in rig.scale),entry['id']+': unapplied rig scale')
    groups={group.index:group.name for group in mesh.vertex_groups}
    for vertex in mesh.data.vertices:
        weights=[group for group in vertex.groups if group.weight>0]
        require(1<=len(weights)<=2,entry['id']+': missing or excessive skin influences')
        require(abs(sum(group.weight for group in weights)-1)<1e-5,entry['id']+': unnormalized weights')
        require(all(groups[group.group] in rig.data.bones for group in weights),entry['id']+': orphan bone group')
    bm=bmesh.new()
    bm.from_mesh(mesh.data)
    boundaries=sum(not edge.is_manifold for edge in bm.edges)
    degenerate=sum(face.calc_area()<1e-12 for face in bm.faces)
    loose=sum(not vertex.link_faces for vertex in bm.verts)
    require(boundaries==0,entry['id']+f': {boundaries} non-manifold edges')
    require(degenerate==0,entry['id']+f': {degenerate} degenerate triangles')
    require(loose==0,entry['id']+': loose vertices')
    bm.free()
    coords=[tuple(round(c,7) for c in vertex.co) for vertex in mesh.data.vertices]
    fingerprint=hashlib.sha256(json.dumps(coords).encode()).hexdigest()
    require(fingerprint not in fingerprints,entry['id']+': duplicate geometry')
    fingerprints.add(fingerprint)
    tex=next(node for node in mesh.data.materials[0].node_tree.nodes if node.type=='TEX_IMAGE')
    require(tuple(tex.image.size)==tuple(entry['texture_size']),entry['id']+': texture resolution')
    require(tex.interpolation=='Closest',entry['id']+': texture filtering')
    require(tex.image.packed_file is not None,entry['id']+': source texture not packed')
    clip_reports=[]
    for clip in entry['clips']:
        action=bpy.data.actions[clip['action']]
        rig.animation_data.action=action
        start,end=action.frame_range
        bpy.context.scene.frame_set(int(start))
        first=evaluated_vertices(mesh)
        bpy.context.scene.frame_set(int(end))
        last=evaluated_vertices(mesh)
        seam=max((a-b).length for a,b in zip(first,last))
        if clip['loop']:
            require(seam<1e-5,entry['id']+': '+clip['name']+' loop seam')
        motion=0
        for phase in (.0,.125,.25,.375,.5,.625,.75,.875,1):
            frame=start+(end-start)*phase
            bpy.context.scene.frame_set(int(frame),subframe=frame-int(frame))
            root=rig.pose.bones['Root']
            require(root.location.length<1e-7,entry['id']+': root translation')
            require(abs(root.rotation_quaternion.angle)<1e-6,entry['id']+': root rotation')
            positions=evaluated_vertices(mesh)
            require(all(math.isfinite(c) for point in positions for c in point),entry['id']+': invalid deformation')
            motion=max(motion,max((a-b).length for a,b in zip(first,positions)))
        require(motion>.00005,entry['id']+': '+clip['name']+' is a static clip')
        clip_reports.append({'name':clip['name'],'loop':clip['loop'],
                             'loop_seam_m':round(seam,8),'max_motion_m':round(motion,6)})
    document=glb_document(SOURCE/entry['glb'])
    expected={clip['name'] for clip in entry['clips']}
    require({a['name'] for a in document.get('animations',[])}==expected,entry['id']+': GLB clips')
    require(len(document.get('skins',[]))==1,entry['id']+': GLB skin count')
    require(len(document.get('meshes',[]))==1,entry['id']+': GLB extra mesh')
    require(len(document.get('materials',[]))==1,entry['id']+': GLB material count')
    require(all(s.get('magFilter')==9728 and s.get('minFilter') in (9728,9984)
                for s in document.get('samplers',[])),entry['id']+': GLB texture sampler')
    require(all('bufferView' in image for image in document.get('images',[])),entry['id']+': external GLB image')
    reports.append({'id':entry['id'],'triangles':len(mesh.data.polygons),'bones':len(rig.data.bones),
                    'non_manifold_edges':boundaries,'degenerate_faces':degenerate,
                    'geometry_sha256':fingerprint,'clips':clip_reports})
    print('SOURCE_OK',entry['id'],flush=True)

# Reimport into fresh empty scenes; success in the source does not prove the exports.
roundtrips=[]
for entry in MANIFEST['species']:
    for format in ('fbx','glb'):
        bpy.ops.wm.read_factory_settings(use_empty=True)
        path=SOURCE/entry[format]
        if format=='fbx':
            bpy.ops.import_scene.fbx(filepath=str(path),use_anim=True)
        else:
            bpy.ops.import_scene.gltf(filepath=str(path))
        rigs=[obj for obj in bpy.context.scene.objects if obj.type=='ARMATURE']
        # glTF's Blender importer creates an extra Icosphere as a custom bone
        # shape. It is not an exported model mesh or a skinned renderer.
        meshes=[obj for obj in bpy.context.scene.objects if obj.type=='MESH' and
                any(mod.type=='ARMATURE' for mod in obj.modifiers)]
        require(len(rigs)==1 and len(meshes)==1,entry['id']+': '+format+' object count')
        rig,mesh=rigs[0],meshes[0]
        require(len(rig.data.bones)==entry['bones'],entry['id']+': '+format+' bone count')
        require(len(mesh.data.materials)==1,entry['id']+': '+format+' material count')
        actions=list(bpy.data.actions)
        require(len(actions)==len(entry['clips']),entry['id']+': '+format+' action count')
        names=[action.name for action in actions]
        for clip in entry['clips']:
            require(any(name==clip['name'] or name.endswith('|'+clip['name']) for name in names),
                    entry['id']+': '+format+' missing '+clip['name']+' in '+str(names))
        rig.data.pose_position='REST'
        bpy.context.view_layer.update()
        points=[mesh.matrix_world @ vertex.co for vertex in mesh.data.vertices]
        extent=max(max(point[i] for point in points)-min(point[i] for point in points) for i in range(3))
        require(abs(extent-entry['length_m'])<.005,entry['id']+': '+format+f' scale: {extent}')
        require(all(vertex.groups for vertex in mesh.data.vertices),entry['id']+': '+format+' unweighted vertices')
        rig.data.pose_position='POSE'
        for track in rig.animation_data.nla_tracks:
            track.mute=True
        imported_clips=[]
        for clip in entry['clips']:
            action=next(action for action in actions if action.name==clip['name'] or
                        action.name.endswith('|'+clip['name']))
            rig.animation_data.action=action
            start,end=action.frame_range
            first=None
            motion=0
            seam=0
            for phase in (0,.125,.25,.375,.5,.625,.75,.875,1):
                frame=start+(end-start)*phase
                bpy.context.scene.frame_set(int(frame),subframe=frame-int(frame))
                positions=evaluated_vertices(mesh)
                require(all(math.isfinite(c) for point in positions for c in point),
                        entry['id']+': '+format+' invalid animation')
                if first is None:
                    first=positions
                difference=max((a-b).length for a,b in zip(first,positions))
                motion=max(motion,difference)
                if phase==1:
                    seam=difference
            require(motion>.00005,entry['id']+': '+format+' static imported '+clip['name'])
            if clip['loop']:
                require(seam<.0001,entry['id']+': '+format+' imported loop seam '+clip['name'])
            duration=(end-start)/bpy.context.scene.render.fps
            require(abs(duration-clip['duration_seconds'])<.04,
                    entry['id']+': '+format+' wrong duration '+clip['name'])
            imported_clips.append({'name':clip['name'],'duration_s':round(duration,5),
                                   'motion_m':round(motion,6),'seam_m':round(seam,8)})
        roundtrips.append({'id':entry['id'],'format':format,'clip_count':len(actions),
                           'bone_count':len(rig.data.bones),'max_extent_m':round(extent,5),
                           'clips':imported_clips})
        print('ROUNDTRIP_OK',entry['id'],format,len(actions),'clips',flush=True)

result={'passed':True,'blender':bpy.app.version_string,'species_count':len(reports),
        'clip_count':MANIFEST['clip_count'],'source_checks':reports,'roundtrips':roundtrips,
        'min_triangles':min(r['triangles'] for r in reports),
        'max_triangles':max(r['triangles'] for r in reports),
        'scope':'Art source, skin deformation, cyclic animation, embedded textures, FBX and GLB roundtrip. No Unity gameplay integration.'}
(OUTPUT/'verification.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n')
print('FISH_LIBRARY_VERIFIED',len(reports),'fish',MANIFEST['clip_count'],'clips',len(roundtrips),'roundtrips',flush=True)
