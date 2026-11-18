"""Numeric asset checks of the actual baked and skinned delivery mesh."""
from pathlib import Path
import bpy,sys,json,hashlib
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,'/Users/lizujin/.codex/skills/blender-asset-qa/scripts')
import qa_check
rig=bpy.data.objects['WC_Rig']
names=[o.name for o in bpy.data.objects if o.type=='MESH' and o.parent==rig and not o.name.startswith('AW_Stump_')]
report=qa_check.run(objects=names,profile='game',uv=True,tol=.012,eps=.0002)
# The wing membrane and its visible struts must respond to the same distal
# bones. Verify actual weighted vertices and a sampled, non-rest pose.
sys.path.insert(0,str(Path(__file__).resolve().parent))
from animate_abyssal_watcher import assign
assign(rig,bpy.data.actions['AW_EnragedIdle'])
bpy.context.scene.frame_set(36)
fingers={}
for side in ('L','R'):
    for digit in range(4):
        name=f'DEF_WingDigit_{side}_{digit}'
        bone=rig.pose.bones[name]
        assert bone.parent.name=='DEF_WingTip_'+side
        for ob in (bpy.data.objects['AW_Head'],bpy.data.objects['WC_Wing_'+side]):
            group=ob.vertex_groups[name]
            assert any(any(g.group==group.index and g.weight>.42 for g in v.groups) for v in ob.data.vertices), (name,ob.name)
        fingers[name]=round(abs(bone.rotation_quaternion.angle),5)
        assert fingers[name]>.035,name
report['wingDigitFlexRadiansAtEnragedIdle36']=fingers
arms=[ob for ob in bpy.data.objects if ob.name.startswith('AW_Arm_')]
assert len(arms)==8
report['integratedSuckers']={ob['limb_id']:ob['sucker_count'] for ob in arms}
for arm in arms:
    assert report['objects'][arm.name]['islands']==1,arm.name
report['sourceSHA256']=hashlib.sha256(Path(bpy.data.filepath).read_bytes()).hexdigest()
(ROOT/'source-assets/creatures/winged-cthulhu/review/qa-game.json').write_text(json.dumps(report,indent=2))
print(json.dumps({'pass':report['pass'],'errors':report['errors'],'warnings':report['warnings']},ensure_ascii=False),flush=True)
assert report['pass'],report['errors']

# Arm-severable skins must meet through motion, not just in bind pose.
import runpy
runpy.run_path(str(Path(__file__).with_name('verify_winged_cthulhu_crown.py')))
