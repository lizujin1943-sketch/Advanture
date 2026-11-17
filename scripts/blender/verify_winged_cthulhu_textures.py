"""Validate the material revision without claiming a runtime asset delivery."""
from pathlib import Path
import bpy
import numpy as np
import hashlib
import json
import sys

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'source-assets/creatures/winged-cthulhu'
BACK=ROOT/'artifacts/winged-cthulhu/pre-texture-refinement.blend'
sys.path.insert(0,'/Users/lizujin/.codex/skills/blender-asset-qa/scripts')
import qa_check


def digest(me):
    co=np.empty(len(me.vertices)*3,np.float32);me.vertices.foreach_get('co',co)
    loops=np.empty(len(me.loops),np.int32);me.loops.foreach_get('vertex_index',loops)
    starts=np.empty(len(me.polygons),np.int32);me.polygons.foreach_get('loop_start',starts)
    h=hashlib.sha256();h.update(co.tobytes());h.update(loops.tobytes());h.update(starts.tobytes())
    return h.hexdigest()


def main():
    if not bpy.app.background:raise RuntimeError('Background validation only')
    meshes=[o for o in bpy.data.collections['HIGH'].objects if o.type=='MESH']
    current={o.data.name:digest(o.data) for o in meshes}
    with bpy.data.libraries.load(str(BACK),link=False) as (src,dst):
        names=tuple(name for name in src.meshes if name in current)
        # Blender replaces elements of the list assigned to dst.meshes with IDs.
        # Keep immutable source names separate from that loader-owned list.
        dst.meshes=list(names)
    original=dict(zip(names,[digest(m) for m in dst.meshes]))
    same_geometry=current==original
    if not same_geometry:
        print(json.dumps({'current':current,'original':original,'loaded':len(dst.meshes)},default=str),flush=True)
    assert same_geometry,'Texture task unexpectedly changed mesh coordinates or topology'
    for me in dst.meshes:bpy.data.meshes.remove(me)
    material_errors=[]
    for ob in meshes:
        assert ob.data.uv_layers.get('ReferencePaintUV'),ob.name
        assert ob.data.attributes.get('ReferencePaintCoverage'),ob.name
        for mat in ob.data.materials:
            for n in mat.node_tree.nodes:
                if n.type!='TEX_IMAGE':continue
                if not n.image:material_errors.append('Empty texture: '+mat.name);continue
                im=n.image
                if im.name=='user-reference.png':material_errors.append('Unprocessed concept used as shader: '+mat.name)
                if not im.packed_file:material_errors.append('Not packed: '+im.name)
                data=any(x in im.name for x in ['Coverage','Height','Roughness'])
                if im.colorspace_settings.is_data!=data:material_errors.append('Wrong color space: '+im.name)
    assert not material_errors,material_errors
    qa=qa_check.run(objects=[o.name for o in meshes],profile='render',uv=False,max_tris=300000)
    qa['texture_revision']={
        'geometry_matches_pre_texture_sha256':same_geometry,
        'mesh_count':len(meshes),
        'material_errors':material_errors,
        'note':'Coverage masks are intentionally Non-Color and drive Mix factors, not albedo colors. Fixed paint UV is not a unique runtime unwrap.',
    }
    (OUT/'review/qa-textures.json').write_text(json.dumps(qa,indent=2),encoding='utf8')
    assert qa['pass'],qa['errors']
    print(json.dumps({'pass':qa['pass'],'same_geometry':same_geometry,'errors':qa['errors'],'warnings':qa['warnings']}),flush=True)


if __name__=='__main__':main()
