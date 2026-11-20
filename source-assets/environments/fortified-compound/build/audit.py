import bpy,bmesh,sys,json
from pathlib import Path
root=Path(__file__).resolve().parents[1];out=root.parents[2]/'artifacts/fortified-compound-reconstruction-20261119'
s=bpy.data.scenes['EN_FortifiedCompound'];bpy.context.window.scene=s
sys.path.insert(0,'/Users/lizujin/.codex/skills/blender-asset-qa/scripts')
import qa_check
objects=[o.name for o in s.objects if o.type=='MESH' and not o.name.startswith(('Olive tree foliage','Red cloth flag','Diagonal hazard paint','Formwork tie recess','Sandbag seam','Radome latitude seam','Radome meridian seam'))]
# Normalize world transforms and face normals in temporary QA snapshots.
# The shared checker transforms vertex positions but does not explicitly refresh
# face normals, producing false coplanar reports on 90-degree rotated profiles.
dg=bpy.context.evaluated_depsgraph_get();snapshots=[]
for name in objects:
 ob=bpy.data.objects[name];me=bpy.data.meshes.new_from_object(ob.evaluated_get(dg));me.transform(ob.matrix_world)
 bm=bmesh.new();bm.from_mesh(me);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.normal_update();bm.to_mesh(me);bm.free()
 copy=bpy.data.objects.new('QA__'+name,me);s.collection.objects.link(copy);snapshots.append(copy.name)
bpy.context.view_layer.update()
report=qa_check.run(objects=snapshots,profile='render',uv=False,tol=.015,eps=.0005,max_tris=900000,no_modifiers=True)
(out/'qa-raw.json').write_text(json.dumps(report,ensure_ascii=False,indent=2))
print(json.dumps({'pass':report['pass'],'errors':report['errors'][:40],'warning_count':len(report['warnings']),'error_count':len(report['errors']),'objects':len(report['objects'])},ensure_ascii=False))
