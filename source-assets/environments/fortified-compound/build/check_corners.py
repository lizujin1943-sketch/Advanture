"""Verify the built corner profiles and all three approaches to the paved plaza."""
import json
import math
import runpy
import sys
from pathlib import Path

import bpy
from mathutils import Vector


def check(scene):
    model = runpy.run_path(str(Path(__file__).with_name('compound.py')))
    bounds = model['access_bounds']
    graph = bpy.context.evaluated_depsgraph_get()
    profiles, approaches, failures = {}, {}, []

    def profile(obj, radii):
        lo, hi = bounds(obj)
        surface = obj.evaluated_get(graph)
        world = obj.matrix_world
        local = world.inverted()
        errors = []
        corners = [(-1,-1), (1,-1), (1,1), (-1,1)]
        for (sx,sy), radius in zip(corners, radii):
            x = lo[0]+radius if sx<0 else hi[0]-radius
            y = lo[1]+radius if sy<0 else hi[1]-radius
            centre = Vector((x,y,(lo[2]+hi[2])*.5))
            for degrees in range(3,90,4):
                a = math.radians(degrees)
                normal = Vector((sx*math.cos(a),sy*math.sin(a),0))
                origin = centre+normal*(radius+.5)
                hit, point, _, _ = surface.ray_cast(local@origin, local.to_3x3()@(-normal))
                if not hit:
                    failures.append([obj.name,'missing arc',sx,sy,degrees])
                    continue
                error = abs((world@point-centre).length-radius)
                errors.append(error)
                if error>.01:
                    failures.append([obj.name,'arc error',sx,sy,degrees,round(error,5)])
        profiles[obj.name] = {'radii_m':list(radii), 'rays':len(errors),
                              'maximum_radial_error_m':round(max(errors,default=0),6)}

    for code, ident in [('03_A1','A1'),('04_B1','B1'),('05_C1','C1')]:
        body = scene.objects[code+' body']
        fascia = scene.objects[code+' flat roof']
        radii = model['WALL_RADII'][ident]
        profile(body,radii)
        offset = bounds(body)[0][0]-bounds(fascia)[0][0]
        profile(fascia,[round(r+offset,5) for r in radii])
    for name in ('Raised square plaza','Paved plaza'):
        profile(scene.objects[name],[model['PLAZA_RADIUS']]*4)

    paving = scene.objects['Paved plaza']
    lo, hi = bounds(paving)
    z = hi[2]
    for side in ('south','east','west'):
        entry = scene.objects['Plaza '+side+(' ramp' if side=='west' else ' stairs')]
        areas = {entry.name,paving.name}
        samples = []
        for j in range(9):
            if side=='south':
                samples.extend([(-9.5+9*j/8,lo[1]+d,z) for d in (-.08,-.025,.025,.08)])
                continue
            areas.add('Plaza '+side+' upper landing')
            x = hi[0] if side=='east' else lo[0]
            samples.extend([(x+d,18.7+1.6*j/8,z) for d in (-.10,-.025,.025,.10)])
            seam_y = bounds(entry)[1][1]
            toe_y = bounds(entry)[0][1]
            x = 10.85+3.2*j/8 if side=='east' else -24.22+2.98*j/8
            for d in (-.08,-.025,.025,.08):
                expected = z*(1+d/(seam_y-toe_y)) if side=='west' and d<0 else z
                samples.append((x,seam_y+d,expected))
        errors = []
        for x,y,expected in samples:
            hit, p, _, _, obj, _ = scene.ray_cast(
                graph,Vector((x,y,expected+2.05)),Vector((0,0,-1)),distance=2.30)
            if not hit or obj.name not in areas or abs(p.z-expected)>.016:
                failures.append([side,'transition',(round(x,3),round(y,3)),
                                 (obj.name,round(p.z,4)) if hit else None])
            if hit:
                errors.append(abs(p.z-expected))
        approaches[side] = {'rays':len(samples),'maximum_height_error_m':round(max(errors,default=0),6)}
    return {'profiles':profiles,'plaza_approaches':approaches,'failures':failures,'pass':not failures}


if __name__=='__main__':
    scene = bpy.data.scenes['EN_FortifiedCompound']
    bpy.context.window.scene = scene
    result = check(scene)
    if '--' in sys.argv:
        Path(sys.argv[sys.argv.index('--')+1]).write_text(json.dumps(result,indent=2)+'\n')
    print(json.dumps(result['plaza_approaches']))
    if result['failures']:
        raise RuntimeError(str(result['failures'][:12]))
