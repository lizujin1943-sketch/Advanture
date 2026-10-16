"""重新导入导出文件，核对尺寸、出生点与碰撞体数量。仅后台执行。"""
import json
from pathlib import Path
import bpy

ROOT=Path(__file__).resolve().parents[1]
assert bpy.app.background and not bpy.data.filepath, '请在新的后台 Blender 进程运行。'


def fresh():
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj,do_unlink=True)


def near(actual,expected):
    return all(abs(a-b)<.001 for a,b in zip(actual,expected))


results={}
for extension in ('glb','fbx'):
    fresh()
    filename=ROOT/f'Exports/yard_07.{extension}'
    if extension=='glb':
        bpy.ops.import_scene.gltf(filepath=str(filename))
    else:
        bpy.ops.import_scene.fbx(filepath=str(filename))
    # Blender 导入器会恢复 Blender 的 Z 向上坐标，核对实际米制尺寸。
    container=bpy.data.objects.get('Container_North_Body')
    assert container and near(container.dimensions,(8,2.8,2.65)), 'Container scale changed after import'
    for name,expected in [('Spawn_Team_A',(-13.5,0,0)),('Spawn_Team_B',(13.5,0,0))]:
        marker=bpy.data.objects.get(name)
        assert marker and near(marker.matrix_world.translation,expected), 'Spawn position changed after import'
    assert not any(o.type in {'CAMERA','LIGHT'} or o.name.startswith('COL_') for o in bpy.context.scene.objects)
    results[extension]={'passed':True,'container_dimensions_meters':list(container.dimensions),
                       'objects':len(bpy.context.scene.objects),'spawn_markers':2}

fresh()
bpy.ops.import_scene.fbx(filepath=str(ROOT/'Exports/yard_07_collision.fbx'))
objects=list(bpy.context.scene.objects)
assert len(objects)==17 and all(o.name.startswith('COL_') for o in objects)
results['collision_fbx']={'passed':True,'boxes':len(objects)}
(ROOT/'Data/roundtrip-validation.json').write_text(json.dumps(results,indent=2)+'\n')
print('ROUNDTRIP_CHECK',json.dumps(results))
