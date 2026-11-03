"""从同一份模型输出结构参考，避免多个设定视图描述不同的生物。"""
from pathlib import Path
import sys
import bpy
sys.path.insert(0,str(Path(__file__).resolve().parent))
from abyssal_common import *
from abyssal_stage import set_view

bpy.ops.wm.open_mainfile(filepath=str(SOURCE/'AbyssalWatcher.blend'))
scene=bpy.context.scene
for name in ['Front','Back','Left','Right','Top','Bottom']:
    if name in ('Top','Bottom'):bpy.data.objects['Camera_'+name].data.ortho_scale=40
    set_view(scene,name,isolated=True)
    scene.render.filepath=str(REFERENCES/(name.lower()+'.png'))
    bpy.ops.render.render(write_still=True)
print('AW: six structural views rendered from the same static asset')
