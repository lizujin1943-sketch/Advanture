"""Build a new art edition from the editable, rounded-corner compound baseline."""
from pathlib import Path
import math
import runpy
import sys
import bpy
from mathutils import Vector


HERE=Path(__file__).resolve().parent
ASSET=HERE.parents[1]
MASTER=ASSET/'EN_FortifiedCompound.blend'
EDITION=ASSET/'EN_FortifiedCompound_Art.blend'
DELIVERY=ASSET.parents[2]/'artifacts/fortified-compound-artpass-20261120'


def daylight(scene):
    sky=next(n for n in scene.world.node_tree.nodes if n.type=='BACKGROUND')
    sky.inputs[0].default_value=(.64,.78,1.00,1)
    sky.inputs[1].default_value=.49
    sun=next(o for o in scene.objects if o.type=='LIGHT' and o.data.type=='SUN')
    sun.rotation_euler=Vector((-.79,.81,-1.33)).to_track_quat('-Z','Y').to_euler()
    sun.data.energy=3.28
    sun.data.color=(1.00,.87,.70)
    sun.data.angle=math.radians(3.0)
    scene.view_settings.exposure=.15
    # Warm skylight bounces off the open forecourt into the three covered stores.
    for o in scene.objects:
        if o.type=='LIGHT' and o.name.startswith('Warm wall sconce'):
            o.data.energy=8
    scene.cycles.samples=144
    scene.cycles.use_denoising=True
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type=='VIEW_3D':
                sh=area.spaces.active.shading
                sh.color_type='MATERIAL'
                sh.show_shadows=True
                sh.show_cavity=True


def build(destination):
    if Path(bpy.data.filepath).resolve()!=MASTER.resolve():
        raise ValueError('Load the rounded-corner baseline before building the art edition.')
    if destination.resolve()==MASTER.resolve():
        raise ValueError('The baseline must be preserved.')
    scene=bpy.data.scenes['EN_FortifiedCompound']
    bpy.context.window.scene=scene
    surface=runpy.run_path(str(HERE/'surfaces.py'))
    palette=surface['make_palette'](ASSET)
    surface['assign'](scene,palette)
    fabricator=runpy.run_path(str(HERE/'fabrication.py'))['Fabricator']
    for script in ('depots.py','stores.py','comms.py'):
        runpy.run_path(str(HERE/script))['build'](scene,palette,fabricator)
    runpy.run_path(str(HERE/'views.py'))['install'](scene)
    daylight(scene)
    scene['art_edition']='Open-sided supply depots and a geodesic communications pavilion.'
    scene.camera=scene.objects['CAM_Reference']
    scene.render.filepath=bpy.path.relpath(str(DELIVERY/'compound-4k.png'))
    scene.render.film_transparent=True
    scene.render.image_settings.color_mode='RGBA'
    scene.render.resolution_x,scene.render.resolution_y,scene.render.resolution_percentage=3840,2160,100
    for image in bpy.data.images:
        if image.packed_file and image.filepath and Path(image.filepath).is_absolute():
            image.filepath=bpy.path.relpath(image.filepath)
    bpy.context.view_layer.update()
    bpy.context.preferences.filepaths.save_version=0
    destination.parent.mkdir(parents=True,exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(destination),compress=True)
    print('Saved art edition',destination,'scene objects',len(scene.objects),'geodesic panels',scene['geodesic_panels'],flush=True)


if __name__=='__main__':
    build(Path(sys.argv[sys.argv.index('--')+1]).resolve() if '--' in sys.argv else EDITION)
