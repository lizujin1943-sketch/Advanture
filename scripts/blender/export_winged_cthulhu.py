"""Export/reimport the boss and publish only verified geometry and collision data."""
from pathlib import Path
import sys,json,hashlib,gzip,shutil
import bpy,numpy as np
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(Path(__file__).resolve().parent))
from animate_abyssal_watcher import assign,curves
from winged_cthulhu.rig import KEYS,count,name
OUT=ROOT/'source-assets/creatures/winged-cthulhu';EXPORT=OUT/'exports/unity'
ART=ROOT/'samples/TacticalGame.Unity/Assets/HookIsland/Art/WingedCthulhu'

def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def export():
    rig=bpy.data.objects['WC_Rig'];meshes=[o for o in bpy.data.objects if o.type=='MESH' and o.parent==rig]
    m=json.loads((EXPORT/'unity-manifest.json').read_text())
    assert len(bpy.data.actions)==24
    for ob in meshes:
        for v in ob.data.vertices:
            assert 1<=len(v.groups)<=4,(ob.name,v.index,len(v.groups))
            assert abs(sum(g.weight for g in v.groups)-1)<1e-5,(ob.name,v.index)
    assign(rig,bpy.data.actions['AW_Idle']);bpy.context.scene.frame_set(1)
    bpy.ops.object.select_all(action='DESELECT')
    for ob in [rig]+meshes:ob.hide_set(False);ob.hide_render=False;ob.select_set(True)
    bpy.context.view_layer.objects.active=rig
    bpy.context.scene.unit_settings.system='METRIC';bpy.context.scene.unit_settings.scale_length=1
    bpy.ops.export_scene.fbx(filepath=str(EXPORT/'WingedCthulhu.fbx'),use_selection=True,object_types={'ARMATURE','MESH'},
        use_mesh_modifiers=False,add_leaf_bones=False,use_armature_deform_only=False,
        bake_anim=True,bake_anim_use_all_bones=True,bake_anim_use_nla_strips=False,bake_anim_use_all_actions=True,
        bake_anim_force_startend_keying=True,bake_anim_step=1,bake_anim_simplify_factor=.02,
        global_scale=1,apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',axis_forward='-Z',axis_up='Y',
        bake_space_transform=False,mesh_smooth_type='FACE',use_tspace=True,use_triangles=True,
        path_mode='STRIP',embed_textures=False,use_custom_props=False)
    m['sourceSHA256']=digest(OUT/'CR_WingedCthulhu_animated.blend');m['fbxSHA256']=digest(EXPORT/'WingedCthulhu.fbx')
    m['textureSHA256']={p.name:digest(p) for p in (OUT/'runtime-textures').glob('*.png')}
    (EXPORT/'unity-manifest.json').write_text(json.dumps(m,indent=2))
    print('EXPORTED', (EXPORT/'WingedCthulhu.fbx').stat().st_size,flush=True)

def verify():
    assert bpy.app.background
    manifest=json.loads((EXPORT/'unity-manifest.json').read_text());cm=json.loads((EXPORT/'collision-manifest.json').read_text())
    expected=np.fromfile(EXPORT/'collision-joints.bin',dtype='<i4').reshape(-1,152,4)
    body=np.fromfile(EXPORT/'collision-body.bin',dtype='<i4').reshape(-1,10)
    # This isolated process may discard its own startup file; the live user scene is untouched.
    for ob in list(bpy.data.objects):bpy.data.objects.remove(ob,do_unlink=True)
    for a in list(bpy.data.actions):bpy.data.actions.remove(a)
    bpy.ops.import_scene.fbx(filepath=str(EXPORT/'WingedCthulhu.fbx'),use_anim=True,automatic_bone_orientation=False,ignore_leaf_bones=False)
    rig=next(o for o in bpy.data.objects if o.type=='ARMATURE');skins=[o for o in bpy.data.objects if o.type=='MESH']
    assert len(skins)==manifest['meshCount'] and len(rig.data.bones)==manifest['boneCount']
    errors=[];maxeye=0
    for spec in cm['clips']:
        candidates=[a for a in bpy.data.actions if a.name.endswith(spec['name']) and any(f.data_path.startswith('pose.bones') for f in curves(a))]
        assert len(candidates)==1,(spec['name'],[a.name for a in candidates]);assign(rig,candidates[0]);maxerror=0
        for fi in sorted({0,9,36,45,min(60,spec['frameCount']-1),spec['frameCount']//2,spec['frameCount']-1}):
            if fi>=spec['frameCount']:continue
            bpy.context.scene.frame_set(fi+1);bpy.context.view_layer.update();j=0
            for key in KEYS:
                for bi in range(count(key)):
                    p=rig.matrix_world@rig.pose.bones[name(key,bi)].head;actual=np.array([p.x,p.z,p.y])*1000
                    exp=expected[spec['offsetBytes']//2432+fi,j+bi,:3]
                    maxerror=max(maxerror,float(np.abs(actual-exp).max()))
                j+=count(key)+1
            ev=bpy.data.objects['AW_Eye_Center'].evaluated_get(bpy.context.evaluated_depsgraph_get())
            coords=np.asarray([tuple(ev.matrix_world@v.co) for v in ev.data.vertices]);center=(coords.min(axis=0)+coords.max(axis=0))*.5
            target=body[spec['bodyOffsetBytes']//40+fi,6:9]
            maxeye=max(maxeye,float(np.abs(center[[0,2,1]]*1000-target).max()))
        assert maxerror<4,(spec['name'],maxerror)
        errors.append({'clip':spec['name'],'maxJointErrorMm':maxerror})
    assert maxeye<5,maxeye
    report={'status':'PASS','fbxSHA256':digest(EXPORT/'WingedCthulhu.fbx'),'sourceSHA256':manifest['sourceSHA256'],'clips':errors,'maxEyeErrorMm':maxeye,'meshCount':len(skins),'boneCount':len(rig.data.bones),'scope':'FBX roundtrip and sampled joints/eye; engine validation is separate'}
    (EXPORT/'roundtrip-verification.json').write_text(json.dumps(report,indent=2))
    print('ROUNDTRIP_PASS',max(x['maxJointErrorMm'] for x in errors),maxeye,flush=True)

def publish():
    m=json.loads((EXPORT/'unity-manifest.json').read_text());qa=json.loads((EXPORT/'roundtrip-verification.json').read_text())
    assert qa['status']=='PASS' and m['fbxSHA256']==digest(EXPORT/'WingedCthulhu.fbx')
    assert qa['fbxSHA256']==m['fbxSHA256'] and qa['sourceSHA256']==m['sourceSHA256']
    assert m['sourceSHA256']==digest(OUT/'CR_WingedCthulhu_animated.blend')
    meshqa=json.loads((OUT/'review/qa-game.json').read_text())
    assert meshqa['pass'] and meshqa['sourceSHA256']==m['sourceSHA256']
    ART.mkdir(parents=True,exist_ok=True);(ART/'Textures').mkdir(exist_ok=True)
    for p in [EXPORT/'WingedCthulhu.fbx',EXPORT/'unity-manifest.json']:shutil.copy2(p,ART/p.name)
    for p in (OUT/'runtime-textures').glob('*.png'):shutil.copy2(p,ART/'Textures'/p.name)
    raw=(EXPORT/'collision-joints.bin').read_bytes()+(EXPORT/'collision-body.bin').read_bytes()
    assert len(raw)==5917968
    target=ROOT/'samples/HookIsland.Simulation/Data/BossCollisions.bin.gz';target.write_bytes(gzip.compress(raw,compresslevel=9,mtime=0))
    path=ROOT/'samples/HookIsland.Simulation/Logic/Combat/BossPoseData.cs';content=path.read_text()
    import re
    updated,n=re.subn(r'internal const string Hash="[0-9a-f]{64}";', 'internal const string Hash="'+hashlib.sha256(raw).hexdigest()+'";',content)
    assert n==1;path.write_text(updated)
    print('PUBLISHED_VERIFIED_ASSET',hashlib.sha256(raw).hexdigest(),flush=True)

if __name__=='__main__':
    mode=sys.argv[sys.argv.index('--')+1] if '--' in sys.argv else 'export'
    {'export':export,'verify':verify,'publish':publish}[mode]()
