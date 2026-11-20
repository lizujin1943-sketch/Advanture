"""Check the evaluated stairs in a delivered blend. Run headless with -- report.json."""
import json
import math
import runpy
import sys
from pathlib import Path

import bmesh
import bpy
from mathutils import Vector


def check(scene):
    model = runpy.run_path(str(Path(__file__).with_name('compound.py')))
    graph = bpy.context.evaluated_depsgraph_get()
    report, failures = {}, []

    for plan in model['stair_plans'](scene):
        offsets = model['access_offsets'](plan)
        sections = model['access_sections'](plan,offsets)
        deck = scene.objects[plan.building+' access | continuous stair and landings']
        evaluated = deck.evaluated_get(graph)
        bm = bmesh.new()
        bm.from_mesh(evaluated.data)
        bad_edges = sum(not e.is_manifold or not e.is_contiguous for e in bm.edges)
        bad_faces = sum(f.calc_area() <= 1e-9 for f in bm.faces)
        volume = bm.calc_volume(signed=True)
        bm.free()
        if bad_edges or bad_faces or volume <= 0:
            failures.append([plan.building, 'deck surface', bad_edges, bad_faces, volume])

        mouth = plan.nodes[-1]
        forward = (mouth.xy-plan.nodes[-2].xy).normalized()
        roof = next(o for o in scene.objects if o.name.endswith(plan.building+' roof finish'))
        terrain = scene.objects['Compound terrain with northern terrace']
        sample_count, max_error, mouth_deltas = 0, 0., []
        rises, goings = [], []
        # Sample nine lines across each flight and the entire curved turn,
        # including both ends of every landing and both sides of each threshold.
        width = plan.width - .40
        lanes = [width * j/8 for j in range(-4,5)]

        def probe(p, z, allowed):
            nonlocal sample_count, max_error
            sample_count += 1
            ok, location, _, _, obj, _ = scene.ray_cast(
                graph, Vector((*p,z+2.05)), Vector((0,0,-1)), distance=2.30)
            error = abs(location.z-z) if ok else float('inf')
            if not ok or obj.name not in allowed or error>.012:
                failures.append([plan.building,'walk',tuple(round(v,4) for v in p),
                                 round(z,4), obj.name if ok else None,
                                 round(location.z,4) if ok else None])
            if ok:
                max_error = max(max_error,error)
                return location.z
            return None

        for i, steps in enumerate(plan.risers):
            a,b = plan.nodes[i:i+2]
            length = (b.xy-a.xy).length
            if steps:
                rise = (b.z-a.z)/steps
                going = length/(steps-1)
                rises.append(rise)
                goings.append(going)
                if rise > .18 or going < .275:
                    failures.append([plan.building, 'pitch', rise, going])
                # Near the nosing and back edge as well as in the middle of each tread.
                rows = [((k+portion)/(steps-1),a.z+(k+1)*rise) for k in range(steps-1)
                        for portion in (.10,.50,.90)]
                for t,z in rows:
                    centre = a.xy.lerp(b.xy,t)
                    for lane in lanes:
                        probe(centre+offsets[i]*lane,z,{deck.name})
            else:
                path = [sections[i][-1],*sections[i+1]]
                for (p,n),(q,m) in zip(path,path[1:]):
                    for lane in lanes:
                        start,end = p+n*lane,q+m*lane
                        count = max(1,math.ceil((end-start).length/.095))
                        for j in range(count):
                            probe(start.lerp(end,(j+.5)/count),a.z,{deck.name})
        first = plan.nodes[0]
        first_dir = (plan.nodes[1].xy-first.xy).normalized()
        for lane in lanes:
            probe(first.xy-first_dir*.06+offsets[0]*lane,first.z,{terrain.name})
            p = mouth.xy+offsets[-1]*lane
            before = probe(p-forward*.065,mouth.z,{deck.name})
            after = probe(p+forward*.065,mouth.z,{roof.name})
            if before is not None and after is not None:
                mouth_deltas.append(abs(after-before))
            for d in (.30,.65,1.10):
                probe(p+forward*d,mouth.z,{roof.name})
        report[plan.building] = {
            'risers_per_flight': [i for i in plan.risers if i],
            'riser_height_m':[round(x,5) for x in rises],
            'tread_going_m':[round(x,5) for x in goings],
            'structural_width_m':plan.width,'tested_clear_span_m':round(width,3),
            'headroom_tested_m':2.05,'walking_surface_rays':sample_count,
            'max_walking_height_error_m':round(max_error,6),
            'max_roof_threshold_difference_m':round(max(mouth_deltas),6) if mouth_deltas else None,
            'nonmanifold_or_inconsistent_edges':bad_edges,'degenerate_faces':bad_faces,
            'deck_volume_m3':round(volume,3),
        }
    return {'buildings':report,'failures':failures,'pass':not failures}


if __name__=='__main__':
    scene = bpy.data.scenes['EN_FortifiedCompound']
    bpy.context.window.scene=scene
    result = check(scene)
    if '--' in sys.argv:
        Path(sys.argv[sys.argv.index('--')+1]).write_text(json.dumps(result,indent=2)+'\n')
    print(json.dumps(result['buildings'],indent=2))
    if result['failures']:
        raise RuntimeError(str(result['failures'][:12]))
