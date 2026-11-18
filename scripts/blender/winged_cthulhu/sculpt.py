"""Fuse intersecting anatomical masses and soften their transitions at a controlled scale."""
import bpy
import bmesh
from mathutils.bvhtree import BVHTree

def fuse_core(collection,include=()):
    keys=('Cranium','Mantle','Girdle_','OrbitalSupport','OralMantle','Brow_','WingFinger_','WingClaw_','Forehead','Beard_','Face_Crease_','Cheek_Rib_')
    obs=[o for o in collection.objects if o.type=='MESH' and (o.name in include or any(k in o.name for k in keys))]
    bpy.ops.object.select_all(action='DESELECT')
    for ob in obs:ob.select_set(True)
    bpy.context.view_layer.objects.active=obs[0]
    bpy.ops.object.join();ob=bpy.context.object;ob.name='CR_WingedCthulhu_OrganicCore'
    oldverts=[v.co.copy() for v in ob.data.vertices]
    oldfaces=[tuple(p.vertices) for p in ob.data.polygons]
    oldmats=[p.material_index for p in ob.data.polygons]
    bvh=BVHTree.FromPolygons(oldverts,oldfaces)
    ob.data.remesh_voxel_size=.028
    ob.data.use_remesh_preserve_volume=True
    ob.data.use_remesh_preserve_attributes=True
    bpy.ops.object.voxel_remesh()
    sm=ob.modifiers.new('Sculpt transition relaxation','SMOOTH');sm.factor=.62;sm.iterations=5
    bpy.ops.object.modifier_apply(modifier=sm.name)
    bm=bmesh.new();bm.from_mesh(ob.data)
    bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.00001)
    # Voxel reconstruction can leave millimetre cavities and disconnected tip dust.
    seen=set();discard=[]
    for v in bm.verts:
        if v in seen:continue
        stack=[v];seen.add(v);group=[]
        while stack:
            u=stack.pop();group.append(u)
            for e in u.link_edges:
                w=e.other_vert(u)
                if w not in seen:seen.add(w);stack.append(w)
        if len(group)<60:discard.extend(group)
    if discard:bmesh.ops.delete(bm,geom=discard,context='VERTS')
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(ob.data);bm.free()
    for p in ob.data.polygons:
        _,_,idx,_=bvh.find_nearest(p.center)
        if idx is not None:p.material_index=oldmats[idx]
        p.use_smooth=True
    from .crown_seams import replace_lower_mantle
    replace_lower_mantle(ob)
    ob['process']='Closed voxel union of cranial, orbital, mantle, brow and wing-girdle volumes; relaxed seams'
    return ob
