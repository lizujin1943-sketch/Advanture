"""真实渲染动作关键帧和合集序列。预览字幕、海面、环台均不进入模型导出。"""
from pathlib import Path
import json
import sys
import bpy
from mathutils import Vector
sys.path.insert(0,str(Path(__file__).resolve().parent))
from abyssal_common import SOURCE, EVIDENCE, aim, material_node
from animate_abyssal_watcher import activate_clip
from abyssal_stage import set_view

TITLES={'Emerge':'登场 · 从海下升起','Idle':'待机 · 呼吸与触手起伏','Slam':'下砸 · 蓄力 / 砸落 / 收回',
        'Sweep':'横扫 · 低位扇形扫击','Ink':'喷墨 · 口器张开与前向喷吐','Hit':'受击 · 短促后缩',
        'Enrage':'狂暴 · 眼光增强','EnragedIdle':'狂暴待机','Combination':'组合 · 两腕攻击与喷墨',
        'Sever':'断肢 · 独立落水下沉','Death':'死亡 · 保留断腕、熄眼沉海'}


def prepare():
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE/'AbyssalWatcher.blend'))
    scene=bpy.context.scene;scene.render.resolution_x=960;scene.render.resolution_y=640
    scene.render.resolution_percentage=100;scene.eevee.taa_render_samples=24
    scene.render.image_settings.file_format='PNG';scene.render.image_settings.compression=20
    scene.render.use_file_extension=True
    camera=bpy.data.objects['Camera_Arena'];camera.location=(24,-42,29);aim(camera,(0,-2,3.7));camera.data.ortho_scale=49
    stage=bpy.data.collections['02_Sea_and_scale_reference']
    font=bpy.data.fonts.load('/System/Library/Fonts/Supplemental/Arial Unicode.ttf')
    data=bpy.data.curves.new('Preview_AnimationCaption','FONT');data.font=font;data.size=1
    obj=bpy.data.objects.new('Preview_AnimationCaption',data);stage.objects.link(obj)
    mat=bpy.data.materials.new('Preview_Caption');mat.use_nodes=True
    node=material_node(mat);node.inputs['Base Color'].default_value=(.75,.95,.91,1)
    node.inputs['Emission Color'].default_value=(.75,.95,.91,1);node.inputs['Emission Strength'].default_value=1
    data.materials.append(mat)
    return scene,obj


def caption(scene,obj,clip):
    kind=clip.split('_')[1];limb=clip.split('_')[2] if len(clip.split('_'))>2 else ''
    obj.data.body=TITLES[kind]+('  '+limb if limb else '')
    obj.parent=scene.camera;obj.rotation_euler=(0,0,0)
    scale=scene.camera.data.ortho_scale;height=scale*scene.render.resolution_y/scene.render.resolution_x
    obj.location=(-scale*.465,height*.43,-10);obj.scale=(scale*.016,)*3


def view(scene,clip):
    kind=clip.split('_')[1]
    camera='Arena' if kind in ('Slam','Sweep','Combination') else 'FaceThreeQuarter' if kind in ('Ink','Hit','Enrage') else 'Hero'
    set_view(scene,camera)
    # 动画镜头加大纵向余量，保证抬腕和下沉过程不被裁切。
    if camera=='Hero':bpy.data.objects['Camera_Hero'].data.ortho_scale=37
    for n in ('Preview_ScalePier','Preview_Fisher Rig','Preview_Fisher Body','Preview_Backpack'):
        if bpy.data.objects.get(n):bpy.data.objects[n].hide_render=camera!='Hero'


def render_stills(scene,text):
    folder=EVIDENCE/'animation-stills';folder.mkdir(exist_ok=True)
    samples=[('AW_Idle',46),('AW_Slam_A01',37),('AW_Slam_A01',46),('AW_Sweep_A04',49),
             ('AW_Ink',44),('AW_Enrage',70),('AW_Sever_A02',26),('AW_Death',100)]
    for clip,frame in samples:
        activate_clip(clip);view(scene,clip);scene.frame_set(frame);caption(scene,text,clip)
        scene.render.filepath=str(folder/f'{clip}-{frame:03d}.png');bpy.ops.render.render(write_still=True)
        print('STILL',clip,frame,flush=True)


def render_movie(scene,text):
    manifest=json.loads((SOURCE/'exports/animation-manifest.json').read_text());show=manifest['showcase']
    folder=EVIDENCE/'animation-frames';folder.mkdir(exist_ok=True);activate_clip('AW_Showcase')
    for index,frame in enumerate(range(1,show['frame_end'],2)):
        segment=next(s for s in show['segments'] if s['frame_start']<=frame<=s['frame_end'])
        clip=segment['clip'];view(scene,clip);scene.frame_set(frame);caption(scene,text,clip)
        scene.render.filepath=str(folder/f'{index:04d}.png');bpy.ops.render.render(write_still=True)
        if index%30==0:print('PREVIEW_PROGRESS',index,frame,clip,flush=True)


if __name__=='__main__':
    scene,text=prepare()
    if '--movie' in sys.argv:render_movie(scene,text)
    else:render_stills(scene,text)
