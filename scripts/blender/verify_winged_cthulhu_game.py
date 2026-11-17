"""Numeric asset checks of the actual baked and skinned delivery mesh."""
from pathlib import Path
import bpy,sys,json,hashlib
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,'/Users/lizujin/.codex/skills/blender-asset-qa/scripts')
import qa_check
rig=bpy.data.objects['WC_Rig']
names=[o.name for o in bpy.data.objects if o.type=='MESH' and o.parent==rig and not o.name.startswith('AW_Stump_')]
report=qa_check.run(objects=names,profile='game',uv=True,tol=.012,eps=.0002)
report['sourceSHA256']=hashlib.sha256(Path(bpy.data.filepath).read_bytes()).hexdigest()
(ROOT/'source-assets/creatures/winged-cthulhu/review/qa-game.json').write_text(json.dumps(report,indent=2))
print(json.dumps({'pass':report['pass'],'errors':report['errors'],'warnings':report['warnings']},ensure_ascii=False),flush=True)
assert report['pass'],report['errors']
