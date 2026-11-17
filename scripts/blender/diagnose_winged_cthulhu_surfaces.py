"""Print exact near-coplanar triangle pairs, for targeted authoring corrections."""
import bpy,sys,json
from mathutils.bvhtree import BVHTree
sys.path.insert(0,'/Users/lizujin/.codex/skills/blender-asset-qa/scripts')
from qa_check import _point_in_tri
for ob in bpy.data.objects:
    if ob.type!='MESH' or ob.name.startswith('AW_Stump_') or ob.parent is None or ob.parent.type!='ARMATURE':continue
    me=ob.data;me.calc_loop_triangles();v=[x.co.copy() for x in me.vertices];ts=[t.vertices[:] for t in me.loop_triangles];bvh=BVHTree.FromPolygons(v,ts);found=set()
    for ti,t in enumerate(ts):
        a,b,c=[v[x] for x in t];n=(b-a).cross(c-a).normalized();cen=(a+b+c)/3
        for p in (cen,cen.lerp(a,.6),cen.lerp(b,.6),cen.lerp(c,.6)):
            for _,_,tj,_ in bvh.find_nearest_range(p,.0002):
                if tj==ti or tj is None or set(t)&set(ts[tj]):continue
                aa,bb,cc=[v[x] for x in ts[tj]];nn=(bb-aa).cross(cc-aa).normalized()
                if n.dot(nn)<.999:continue
                q=p-nn*nn.dot(p-aa)
                if _point_in_tri(q,aa,bb,cc,nn):found.add(tuple(sorted((ti,tj))))
    for ti,tj in sorted(found):
        ta,tb=me.loop_triangles[ti],me.loop_triangles[tj]
        print(json.dumps({'object':ob.name,'faces':[ta.polygon_index,tb.polygon_index],'centers':[list(sum((v[i] for i in t),v[0]*0)/3) for t in (ts[ti],ts[tj])],'vertices':[list(ts[ti]),list(ts[tj])]}),flush=True)
