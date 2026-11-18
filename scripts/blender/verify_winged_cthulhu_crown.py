"""Measure the 320 matching wrist points on evaluated, independently skinned meshes."""
from pathlib import Path
import hashlib,json,math,sys
import bpy
from mathutils.kdtree import KDTree
HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
from winged_cthulhu.crown import seat,IDS,SIDES
from animate_abyssal_watcher import assign
head=bpy.data.objects['AW_Head'];rig=bpy.data.objects['WC_Rig'];pairs={}
def tree(ob):
    kd=KDTree(len(ob.data.vertices))
    for v in ob.data.vertices:kd.insert(v.co,v.index)
    kd.balance();return kd
core=tree(head)
for key in IDS:
    arm=bpy.data.objects['AW_Arm_'+key];distal=tree(arm);p,t,n,b,r=seat(key);ids=[]
    for i in range(SIDES):
        a=math.tau*i/SIDES;v=p+(n*math.cos(a)+b*math.sin(a))*r
        _,h,dh=core.find(v);_,k,dk=distal.find(v)
        assert max(dh,dk)<.00002,(key,i,dh,dk)
        ids.append((h,k))
    pairs[key]=ids
report={}
for action in bpy.data.actions:
    assign(rig,action);maximum=0
    for fi in sorted(set([1,round(action.frame_range[1]),*range(9,round(action.frame_range[1]),8)])):
        bpy.context.scene.frame_set(fi);dg=bpy.context.evaluated_depsgraph_get()
        skin=head.evaluated_get(dg).data.vertices
        for key,ids in pairs.items():
            arm=bpy.data.objects['AW_Arm_'+key].evaluated_get(dg).data.vertices
            maximum=max(maximum,max((skin[h].co-arm[k].co).length for h,k in ids))
    report[action.name]=maximum*1000
print('MAX_SEAM_GAP_MM',max(report.values()),flush=True)
assert max(report.values())<.10,report
out=HERE.parents[1]/'source-assets/creatures/winged-cthulhu/review/crown-seams.json'
out.write_text(json.dumps({'sourceSHA256':hashlib.sha256(Path(bpy.data.filepath).read_bytes()).hexdigest(),
    'matchedVertices':SIDES*len(pairs),'sampling':'First, last and every eighth interior frame of each authored clip',
    'maximumGapMmByClip':report},indent=2)+'\n')
