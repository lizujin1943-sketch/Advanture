"""Persistent cameras for comparing the four remodelled buildings."""
from mathutils import Vector
import bpy


VIEWS = {
    'equipment': ((-15.6,30.6,3.77),(3.6,-15.8,5.60),35),
    'medical': ((9.3,30.8,3.74),(-3.43,-15.6,5.57),36),
    'armory': ((-29.2,-6.7,2.51),(6.9,-17.4,7.83),34),
    'b2': ((21.1,-22.0,4.18),(11.55,-23.8,10.65),35),
}


def install(scene):
    collection = bpy.data.collections.new('FC_91_Architecture_Cameras')
    scene.collection.children.link(collection)
    cameras = {}
    for name,(point,offset,lens) in VIEWS.items():
        data = bpy.data.cameras.new('Art '+name)
        camera = bpy.data.objects.new('CAM_Art_'+name,data)
        collection.objects.link(camera)
        camera.location = Vector(point)+Vector(offset)
        camera.rotation_euler = (-Vector(offset)).to_track_quat('-Z','Y').to_euler()
        data.lens = lens
        data.clip_start = .05
        data.clip_end = 350
        cameras[name] = camera
    return cameras
