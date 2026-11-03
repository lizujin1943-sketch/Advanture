"""打开成品后设置易读的相机视图，并启动已有 Blender MCP 插件。"""
import bpy
from mathutils import Vector

if 'blender_mcp' not in bpy.context.preferences.addons:
    bpy.ops.preferences.addon_enable(module='blender_mcp')
if hasattr(bpy.types,'blendermcp_server') and bpy.types.blendermcp_server:
    if not bpy.types.blendermcp_server.running:bpy.types.blendermcp_server.start()
for obj in bpy.context.selected_objects:obj.select_set(False)
rig=bpy.data.objects.get('AW_Rig')
if rig:
    rig.select_set(True);bpy.context.view_layer.objects.active=rig
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            space=area.spaces.active;space.region_3d.view_perspective='CAMERA'
            space.shading.type='MATERIAL';space.overlay.show_overlays=False
print('Abyssal Watcher ready for inspection')
