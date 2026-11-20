"""Check access surfaces, display inventory and packed materials in a reopened art edition."""
import json
import sys
from pathlib import Path

import bpy


def check(scene):
    graph=bpy.context.evaluated_depsgraph_get()
    terrain=scene.objects['Compound terrain with northern terrace']
    faults=[]
    paths={}

    def terrain_z(x,y):
        hit,point,_,_=terrain.ray_cast((x,y,30),(0,0,-1))
        if not hit:
            raise ValueError(('No terrain',x,y))
        return point.z

    def sample(x,y,z,names):
        hit,point,_,_,obj,_=scene.ray_cast(graph,(x,y,z+2.05),(0,0,-1),distance=2.30)
        if not hit or obj.name not in names or abs(point.z-z)>.013:
            faults.append((round(x,3),round(y,3),round(z,3),
                           obj.name if hit else None,round(point.z,3) if hit else None))
        return abs(point.z-z) if hit else None

    for group,x,edge,floor,width,run,direction in [
        ('Equipment',-15.8,29.025,2.08,6.30,1.88,-1),
        ('Medical',9.3,29.025,2.08,6.16,1.88,-1),
        ('Armory',-29.1,-13.175,.28,6.048,2.1,-1),
        ('Armory',-25.48,-.825,.28,2.16,2.2,1),
    ]:
        name=group+' | '+('north exit apron' if direction>0 else 'receiving apron')
        errors=[]
        for lane in range(-4,5):
            px=x+lane*(width-.24)/8
            lip=terrain_z(px,edge+direction*run)+.008
            for i in range(1,41):
                t=(i-.5)/40
                errors.append(sample(px,edge+direction*run*t,(1-t)*floor+t*lip,{name}))
            for distance in (.045,.16,.45):
                py=edge-direction*distance
                errors.append(sample(px,py,floor,{group+' floor'}))
                py=edge+direction*(run+distance)
                errors.append(sample(px,py,terrain_z(px,py),{terrain.name}))
        paths[name]={'sample_count':len(errors),'tested_clear_width_m':round(width-.24,3),
                     'max_surface_error_m':round(max(v for v in errors if v is not None),5)}

    # Follow the stockroom's open east aisle from the display area to the north exit.
    for i in range(50):
        y=-10.9+i*9.96/49
        for lane in range(-4,5):
            sample(-25.28-.20*i/49+lane*.21,y,.28,{'Armory floor'})
    paths['Armory north aisle']={'sample_count':450,'tested_clear_width_m':1.68}

    # Walk from the pavement up all four risers, across the upper landing,
    # stopping at the face of the existing closed access door.
    step='B2 | entry steps'
    y0=-22.
    approach=[(-9.62,0.,{terrain.name}),(-9.55,0.,{terrain.name})]
    for lo,hi,top in ((-9.48,-9.08,.177),(-9.08,-8.68,.353),
                      (-8.68,-8.28,.530),(-8.28,-7.20,.703)):
        approach.extend((lo+(hi-lo)*t,top,{step}) for t in (.12,.35,.6,.86))
    for y,z,allowed in approach:
        for lane in range(-4,5):
            sample(21.1+lane*.3,y0+y,z,allowed)
    paths['B2 door approach']={'sample_count':len(approach)*9,'tested_clear_width_m':2.4,'risers':4}

    packed={}
    for group in ['asphalt_04','concrete_floor_worn_001','concrete_wall_009','preconcrete_wall_001']:
        for ch in ('Diffuse','Rough','Displacement'):
            name=group+' '+ch
            im=bpy.data.images.get(name)
            if not im or not im.packed_file or min(im.size)<=0:
                faults.append(('Packed map',name))
                continue
            packed[name]={'size':list(im.size),'space':im.colorspace_settings.name}
    inventory={'armory display':sum(o.get('single_display')=='armory' for o in scene.objects),
               'equipment vest':sum(o.name=='Hanging armor vest' for o in scene.objects),
               'first aid carry case':sum(o.name=='Single first aid case' for o in scene.objects)}
    for name,count in inventory.items():
        if count!=1:
            faults.append(('Representative display',name,count))
    for key in ('equipment','medical','armory','b2'):
        if not scene.objects.get('CAM_Art_'+key):
            faults.append(('Missing detail camera',key))
    if scene.get('geodesic_panels')!=160 or scene.camera.name!='CAM_Reference':
        faults.append(('Art scene identity',scene.name))
    if (scene.render.resolution_x,scene.render.resolution_y)!=(3840,2160) or not scene.render.film_transparent:
        faults.append(('Master render settings',))
    return {'pass':not faults,'faults':faults,'paths':paths,'headroom_m':2.05,
            'new_access_samples':sum(p['sample_count'] for p in paths.values()),
            'representative_displays':inventory,'radome_panels':scene.get('geodesic_panels'),
            'packed_PBR_images':packed}


if __name__=='__main__':
    scene=bpy.data.scenes['EN_FortifiedCompound']
    bpy.context.window.scene=scene
    result=check(scene)
    if '--' in sys.argv:
        Path(sys.argv[sys.argv.index('--')+1]).write_text(json.dumps(result,indent=2)+'\n')
    print('New access samples',result['new_access_samples'],'failures',len(result['faults']))
    if not result['pass']:
        raise RuntimeError(str(result['faults'][:12]))
