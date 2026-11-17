"""Render a saved source; CPU preview avoids waiting on first-use Metal compilation."""
from pathlib import Path
import bpy
import sys
import json

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'source-assets/creatures/winged-cthulhu'
args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
mode=args[0] if args else 'preview'
s=bpy.context.scene
if mode=='radial':
    s.render.engine='CYCLES';s.cycles.device='CPU';s.cycles.samples=40;s.cycles.use_denoising=True
    s.render.resolution_percentage=100;s.render.resolution_x=1536;s.render.resolution_y=1536
    for view in ('Front','Hero','Back'):
        s.camera=bpy.data.objects['CAM_Radial'+view]
        s.render.filepath=str(OUT/'review'/('radial-material-'+view.lower()+'.png'))
        bpy.ops.render.render(write_still=True)
elif mode=='preview':
    s.cycles.device='CPU';s.cycles.samples=16;s.render.resolution_percentage=50
    s.render.filepath=str(OUT/'review/preview.png');bpy.ops.render.render(write_still=True)
elif mode=='views':
    s.cycles.device='CPU';s.cycles.samples=64;s.render.resolution_percentage=100
    for cname,fname in [('CAM_Reference','reference-view'),('CAM_ThreeQuarter','three-quarter'),('CAM_Face','face-detail')]:
        s.camera=bpy.data.objects[cname];s.render.filepath=str(OUT/'review'/f'{fname}.png');bpy.ops.render.render(write_still=True)
elif mode=='clarity':
    s.cycles.device='CPU';s.cycles.samples=96;s.cycles.use_denoising=True
    s.render.resolution_percentage=100;s.render.use_simplify=False
    s.camera=bpy.data.objects['CAM_Reference']
    s.render.resolution_x=3840;s.render.resolution_y=2560
    s.render.filepath=str(OUT/'review/reference-view-3840.png');bpy.ops.render.render(write_still=True)
    s.camera=bpy.data.objects['CAM_Face']
    s.render.resolution_x=2048;s.render.resolution_y=1365
    s.render.filepath=str(OUT/'review/face-detail-2048.png');bpy.ops.render.render(write_still=True)
elif mode=='textures':
    s.cycles.device='CPU';s.cycles.samples=64;s.cycles.use_denoising=True
    s.render.resolution_percentage=100;s.render.use_simplify=False
    # Same lighting and camera as the before render; only authored surfaces change.
    for cname,fname,width,height in [
        ('CAM_Reference','texture-reference-view',2048,1365),
        ('CAM_ThreeQuarter','texture-three-quarter',1536,1024),
        ('CAM_Face','texture-face-detail',2048,1365),
    ]:
        s.camera=bpy.data.objects[cname]
        s.render.resolution_x=width;s.render.resolution_y=height
        s.render.filepath=str(OUT/'review'/f'{fname}.png');bpy.ops.render.render(write_still=True)
elif mode=='clay':
    s.render.engine='BLENDER_WORKBENCH';s.render.resolution_percentage=75
    s.display.shading.light='STUDIO';s.display.shading.color_type='SINGLE';s.display.shading.single_color=(.5,.5,.5)
    s.display.shading.show_shadows=True;s.display.shading.show_cavity=True
    for cname,fname in [('CAM_ThreeQuarter','clay-three-quarter'),('CAM_Side','clay-side')]:
        s.camera=bpy.data.objects[cname];s.render.filepath=str(OUT/'review'/f'{fname}.png');bpy.ops.render.render(write_still=True)
elif mode=='mask':
    s.render.engine='BLENDER_WORKBENCH';s.render.resolution_percentage=100
    s.render.film_transparent=True;s.render.image_settings.color_mode='RGBA'
    s.display.shading.light='FLAT';s.display.shading.color_type='SINGLE';s.display.shading.single_color=(1,1,1)
    bpy.data.objects['STUDIO_Backdrop'].hide_render=True
    s.camera=bpy.data.objects['CAM_Reference'];s.render.filepath=str(OUT/'review/silhouette.png')
    bpy.ops.render.render(write_still=True)
elif mode=='qa':
    sys.path.insert(0,'/Users/lizujin/.codex/skills/blender-asset-qa/scripts')
    import qa_check
    names=[o.name for o in bpy.data.collections['HIGH'].objects if o.type=='MESH']
    report=qa_check.run(objects=names,profile='render',uv=False,max_tris=300000)
    (OUT/'review/qa-high.json').write_text(json.dumps(report,indent=2),encoding='utf8')
    print(json.dumps({'pass':report['pass'],'errors':report['errors'],'warnings':report['warnings']}),flush=True)
print('REVIEW_DONE',mode,flush=True)
