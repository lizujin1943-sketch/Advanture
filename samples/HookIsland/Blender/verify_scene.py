"""Geometric checks for the Blender asset; not a Unity movement or collision test."""
import json
import re
from math import atan, degrees
from mathutils import Vector
import hook_island as H
import hook_island_details as D


def surface_height(obj,x,y):
    inverse=obj.matrix_world.inverted()
    origin=inverse @ Vector((x,y,12))
    direction=(inverse.to_3x3() @ Vector((0,0,-1))).normalized()
    hit,location,_,_=obj.ray_cast(origin,direction)
    return (obj.matrix_world @ location).z if hit else None


def verify():
    scene=H.SCENE
    ground=scene.objects["Island_Ground_Editable"]
    ramp=[o for o in H.COL["dock"].objects if o.name.startswith("Pier_Approach_Ramp_Board")]
    deck=[o for o in H.COL["dock"].objects if o.name.startswith("Pier_Deck_Plank")]
    seam=[]
    for j in range(41):
        u=-D.PIER_WIDTH/2+.002+(D.PIER_WIDTH-.004)*j/40
        p=D.pier_point(D.RAMP_START+.001,u)
        terrain=surface_height(ground,p.x,p.y)
        top=surface_height(ramp[0],p.x,p.y)
        if terrain is None or top is None:
            raise AssertionError("Missing terrain or approach at the pier entrance.")
        seam.append(top-terrain)
    clearances=[]
    for j in range(31):
        for k in range(17):
            s=D.RAMP_START+.002+(D.DECK_START-D.RAMP_START-.004)*j/30
            u=-D.PIER_WIDTH/2+.01+(D.PIER_WIDTH-.02)*k/16
            p=D.pier_point(s,u)
            terrain=surface_height(ground,p.x,p.y)
            hits=[surface_height(o,p.x,p.y) for o in ramp]
            hits=[z for z in hits if z is not None]
            if terrain is not None and hits:clearances.append(max(hits)-terrain)
    fruit=[o.name for o in H.COL["palm"].objects if re.fullmatch(r"Coconut_Fruit_\d\d",o.name)]
    forbidden=[o.name for o in scene.objects if o.type=="MESH" and any(s in o.name.lower() for s in ["ocean","water","offshore","underwater"])]
    mesh_objects=[o for o in scene.objects if o.type=="MESH"]
    zero_area=sum(1 for o in mesh_objects for p in o.data.polygons if p.area<1e-10)
    report={
        "scene":scene.name,
        "modeling_units":"metres (assumed from concept image)",
        "object_count":len(scene.objects),
        "mesh_count":len(mesh_objects),
        "polygon_count":sum(len(o.data.polygons) for o in mesh_objects),
        "island_top_dimensions_m":[round(v,3) for v in ground.dimensions],
        "coconut_count":len(fruit),
        "frond_count":len([o for o in H.COL["palm"].objects if re.fullmatch(r"Coconut_Frond_\d\d",o.name)]),
        "sea_or_offshore_meshes":forbidden,
        "pier_clear_width_m":D.PIER_WIDTH,
        "pier_inland_overlap_m":-D.RAMP_START,
        "deck_height_m":H.DECK_HEIGHT,
        "entrance_join_min_clearance_m":round(min(seam),5),
        "entrance_join_max_clearance_m":round(max(seam),5),
        "ramp_min_above_terrain_m":round(min(clearances),5),
        "ramp_board_count":len(ramp),
        "deck_plank_count":len(deck),
        "zero_area_face_count":zero_area,
        "checks":[
            "41 samples across pier/terrain entrance",
            "527 samples along full ramp width and length",
            "No water or offshore stone meshes",
            "Two coconuts; frond count recorded against the current revision",
            "Separate editable island/shop/tree/pier/dressing collections",
        ],
        "scope":"Geometry and rendered views only. Unity collision and multiplayer traversal not tested.",
    }
    assert len(fruit)==2 and not forbidden
    assert min(seam)>-.002 and max(seam)<.018, report
    assert min(clearances)>-.003, report
    assert len(deck)==H.COL["dock"].get("reference_plank_count",13) and len(ramp)==5
    filename="scene_validation_v2.json" if scene.name.endswith("v2") else "scene_validation.json"
    (H.ROOT/filename).write_text(json.dumps(report,ensure_ascii=False,indent=2)+"\n")
    print(json.dumps(report,ensure_ascii=False,indent=2))
    return report
