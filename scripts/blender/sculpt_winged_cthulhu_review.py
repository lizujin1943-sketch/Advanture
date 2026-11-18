"""Build the static reference-review sculpt from the project's radial high mesh in background Blender."""
from pathlib import Path
import math
import sys

import bpy
import bmesh
import numpy as np
from mathutils import Vector
from mathutils.kdtree import KDTree
from mathutils.bvhtree import BVHTree

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from create_winged_cthulhu import camera
from winged_cthulhu.geometry import Mesh, spline
from winged_cthulhu.crown import seat, SIDES
from winged_cthulhu.crown_seams import stitch

BASE = HERE.parents[1] / 'source-assets/creatures/winged-cthulhu'
REVIEW = BASE / 'review/clay-revision'
WORK = HERE.parents[1] / 'artifacts/cthulhu-clay-review'
PREFIX = 'CR_WingedCthulhu_'

# X, Y, Z and radius in metres. The first two control points and the cut
# ring are locked to the original crown. These paths start beyond the wrist.
POSE = {
    'A01': [(-2.70,-1.18,4.76,.55),(-4.05,-1.64,5.35,.65),(-5.42,-1.83,5.27,.64),
            (-6.46,-1.94,4.58,.54),(-6.82,-1.90,3.57,.40),(-6.60,-1.73,2.79,.30),
            (-6.01,-1.54,2.55,.22),(-5.56,-1.49,2.88,.15),(-5.59,-1.52,3.36,.075),
            (-5.87,-1.58,3.46,.012)],
    'A03': [(.92,-2.55,3.81,.65),(1.11,-3.03,3.00,.72),(1.36,-3.40,2.18,.66),
            (1.16,-3.63,1.25,.52),(.46,-3.82,.65,.39),(-.32,-3.77,.60,.27),
            (-.84,-3.58,.93,.19),(-.81,-3.41,1.39,.12),(-.49,-3.29,1.55,.065),(-.22,-3.18,1.35,.012)],
    'A02': [(-1.60,-2.72,3.81,.70),(-2.57,-3.12,4.12,.86),(-3.46,-3.38,3.77,.86),
            (-4.05,-3.57,2.96,.73),(-4.30,-3.71,1.99,.55),(-4.19,-3.65,1.13,.40),
            (-3.76,-3.46,.65,.28),(-3.23,-3.26,.67,.18),(-3.12,-3.12,.95,.09),(-3.36,-3.06,1.08,.012)],
    'A04': [(2.61,-1.28,4.14,.65),(3.54,-1.67,3.78,.78),(4.58,-2.13,4.23,.87),
            (5.55,-2.53,4.35,.80),(6.47,-2.80,3.66,.68),(6.80,-2.91,2.69,.51),
            (6.57,-3.00,1.72,.37),(5.86,-3.04,1.29,.25),(5.22,-3.01,1.56,.13),(5.41,-2.95,1.99,.012)],
    'S01': [(-2.55,1.36,4.28,.60),(-3.34,1.98,4.54,.58),(-3.86,2.57,5.47,.48),
            (-3.78,2.99,6.30,.37),(-3.18,3.32,6.67,.27),(-2.67,3.42,6.44,.20),
            (-2.49,3.43,6.02,.12),(-2.68,3.39,5.77,.065),(-2.89,3.36,5.85,.009)],
    'S03': [(1.28,2.51,3.72,.56),(1.98,3.43,3.43,.64),(2.46,4.36,3.37,.59),
            (2.60,5.00,3.85,.49),(2.47,5.32,4.59,.38),(2.11,5.27,5.21,.29),
            (1.73,5.03,5.46,.21),(1.52,4.82,5.25,.14),(1.56,4.71,4.94,.07),(1.78,4.75,4.87,.012)],
    'S04': [(2.63,1.34,4.27,.58),(3.66,1.98,4.38,.56),(4.66,2.54,4.77,.48),
            (5.20,2.97,5.50,.38),(5.08,3.28,6.16,.29),(4.56,3.38,6.45,.20),
            (4.11,3.35,6.24,.13),(4.00,3.28,5.90,.07),(4.22,3.19,5.79,.009)]
}
# Centres of the distal curls in the frontal plane, used to orient the sucker bed.
CURL = {'A01': (-6.04,3.42), 'A03': (-.19,1.22), 'A02': (-3.52,1.46), 'A04': (5.84,2.22),
        'S01': (-3.16,5.95), 'S03': (1.83,4.65), 'S04': (4.65,5.88)}


def smooth(lo, hi, x):
    t = np.clip((x-lo)/(hi-lo), 0, 1)
    return t*t*(3-2*t)


def coordinates(ob):
    vertices = np.empty((len(ob.data.vertices), 3), dtype=np.float32)
    ob.data.vertices.foreach_get('co', vertices.ravel())
    return vertices


def write_coordinates(ob, vertices):
    ob.data.vertices.foreach_set('co', np.asarray(vertices, dtype=np.float32).ravel())
    ob.data.update()


def rebuilt_sleeve(key, points, radii, collection, materials):
    # Use the body's exact rim frame and transport it along the new curve.
    # Unwrapped axial roll prevents a sudden half-turn on tightly curled tips.
    start = 21
    origin, tangent, normal, lateral, radius = seat(key)
    mesh = Mesh()
    params = []
    previous = None
    frames = []
    roll = 0.0
    curl_x, curl_z = CURL[key]
    for j in range(start,len(points)):
        f = j/(len(points)-1)
        p = points[j]
        r = max(.006,float(radii[j]))
        if j == start:
            p,t,n,b,r = origin,tangent,normal,lateral,radius
        else:
            t=(points[min(j+1,len(points)-1)]-points[j-1]).normalized()
            normal=tangent.rotation_difference(t) @ normal
            normal=(normal-t*normal.dot(t)).normalized()
            side=t.cross(normal).normalized()
            inward=Vector((curl_x-p.x,0,curl_z-p.z))
            if inward.length>0:
                inward.normalize()
            # The upper arch shows skin; the cup bed rotates toward the viewer inside the coil.
            view=-1 if key[0]=='A' else 1
            inward.y=view*(.24+.72*float(smooth(.42,.72,f)))
            desired=inward-t*inward.dot(t)
            if desired.length>.09:
                angle=math.atan2(desired.dot(side),desired.dot(normal))
                angle=roll+(angle-roll+math.pi)%math.tau-math.pi
                roll+=max(-.09,min(.09,angle-roll))
            angle=roll*float(smooth(.16,.35,f))
            n=normal*math.cos(angle)+side*math.sin(angle)
            b=t.cross(n).normalized()
        tangent=t
        ring=[]
        for k in range(SIDES):
            a=math.tau*k/SIDES
            muscle=1+.014*float(smooth(.17,.28,f))*math.cos(3*a+.6*f)
            ring.append(mesh.vert(p+r*muscle*(math.cos(a)*n+math.sin(a)*b),(k/SIDES,f)))
            params.append(f)
        if previous:
            for k in range(SIDES):
                q=(k+1)%SIDES
                underside = math.cos(math.tau*(k+.5)/SIDES)>.73
                mesh.face((previous[k],ring[k],ring[q],previous[q]),int(underside))
        else:
            cap=mesh.vert(origin+t*.145)
            params.append(f)
            for k in range(SIDES):
                mesh.face((ring[k],ring[(k+1)%SIDES],cap))
        previous=ring
        frames.append((p,t,n,b,r))
    cap=mesh.vert(points[-1])
    params.append(1.)
    for k in range(SIDES):
        mesh.face((previous[k],previous[(k+1)%SIDES],cap))
    # Each row advances independently by tissue distance, staggering the larger cups.
    traveled=0
    next_distance=[.55,.79]
    for j,(p,t,n,b,r) in enumerate(frames):
        if j:
            traveled+=(p-frames[j-1][0]).length
        if r<.048 or j>len(frames)-3:
            continue
        for row in range(2):
            if traveled<next_distance[row]:
                continue
            angle=(-.54,.54)[row]
            local=(n*math.cos(angle)+b*math.sin(angle)).normalized()
            before=len(mesh.v)
            lip=r*(.53 if key.startswith('A') else .48)
            mesh.cup(p+local*r*.99,local,t,lip,aspect=.94,segments=32)
            params.extend([(j+start)/(len(points)-1)]*(len(mesh.v)-before))
            next_distance[row]=traveled+max(.075,lip*2.02)
    result=mesh.finish(PREFIX+'ReviewArm_'+key,collection,materials)
    param=result.data.attributes.new('ArmParameter','FLOAT','POINT')
    param.data.foreach_set('value',np.asarray(params,dtype=np.float32))
    for poly in list(result.data.polygons)[:SIDES]:
        poly.use_smooth=False
    return result


def pose_arm(ob):
    key=ob['limb_id']
    source=[Vector(p) for p in ob['centerline']]
    source_r=np.array(ob['centerline_radii'])
    controls=[tuple(p) for p in ob['control_points'][:2]]+POSE[key]
    points=spline([Vector(p[:3]) for p in controls],14)
    radii=np.array(spline([p[3] for p in controls],14))
    assert len(points)==len(source)
    start,end=21,42
    origin,tangent,_,_,radius=seat(key)
    length=(origin-points[end]).length
    endpoint=points[end].copy()
    end_tangent=(points[end+1]-points[end-1]).normalized()
    points[:start]=source[:start]
    radii[:start]=source_r[:start]
    for j in range(start,end):
        u=(j-start)/(end-start)
        points[j]=((2*u**3-3*u*u+1)*origin+(u**3-2*u*u+u)*length*tangent+
                   (-2*u**3+3*u*u)*endpoint+(u**3-u*u)*length*end_tangent)
        radii[j]=radius+(radii[end]-radius)*float(smooth(0,1,u))
    result=rebuilt_sleeve(key,points,radii,ob.users_collection[0],list(ob.data.materials))
    for tag in ('limb_id','azimuth_degrees'):
        result[tag]=ob[tag]
    result['centerline']=[list(p) for p in points]
    result['centerline_radii']=list(radii)
    result['control_points']=[list(points[i])+[float(radii[i])] for i in range(0,len(points),14)]
    result.parent=ob.parent
    name=ob.name
    bpy.data.objects.remove(ob,do_unlink=True)
    result.name=name


def sculpt_head(ob):
    xyz = coordinates(ob)
    x,y,z = xyz.T.copy()
    side = np.abs(x)
    # Extend the dorsal surface into one swept mantle. Stretch concentrates
    # near the sagittal crest so the posterior tip tapers in plan and rear views.
    body = smooth(5.70,7.15,z)*(1-smooth(1.80,2.65,side))
    back = np.maximum(0,(y+.20)/2.58)
    sweep = body * back**1.68
    crest = body * back**7.2 * np.exp(-(x/.65)**2)
    xyz[:,1] += 2.97*sweep+.51*crest
    xyz[:,2] -= 1.78*sweep+.63*crest
    xyz[:,0] *= 1-.105*sweep
    # The supplied rear and profile views show a low, narrowing mantle tip.
    keel = smooth(3.48,5.8,xyz[:,1])
    spine = np.exp(-(x/.48)**2)*keel**2
    xyz[:,0] *= 1-.31*keel
    xyz[:,2] -= .38*keel+.56*spine
    xyz[:,1] += .16*spine
    # Bring temples around the skull; side fins and lateral brow blades
    # used to be coplanar with the eyes even far outside the cheek.
    face = (1-smooth(-1.02,-.45,y))*smooth(6.0,6.65,z)
    xyz[:,1] += face*(.99*smooth(.75,2.85,side))
    xyz[:,0] *= 1-.19*face*smooth(.85,2.8,side)
    xyz[:,2] -= .17*face*smooth(1.0,2.5,side)
    # Pull the brow shell out over the orbit, while the lower socket recedes.
    line = 7.14+.54*side
    brow = np.exp(-((z-line)/.235)**2)*np.exp(-((side-.63)/.78)**4)
    xyz[:,1] -= .19*brow*face
    if '_Eye_' in ob.name:
        axis = 7.125+.52*(side-.64)
        xyz[:,2] = axis + 1.12*(xyz[:,2]-axis)
        xyz[:,1] -= .040+.050*np.exp(-((side-.64)/.27)**2)
    write_coordinates(ob,xyz)


def orbital_fold(high, side, name, knots, depth_ratio=1):
    """Cross sections follow the lid around the eyeball, tapering inside the existing tissue."""
    points = spline([Vector((side*x,y,z)) for x,y,z,r in knots],12)
    radii = spline([r for x,y,z,r in knots],12)
    g = Mesh()
    rows = []
    for i,(p,r) in enumerate(zip(points,radii)):
        t = (points[min(i+1,len(points)-1)]-points[max(0,i-1)]).normalized()
        front = Vector((0,-1,0))
        n = (front-t*t.dot(front)).normalized()
        b = t.cross(n)
        row = []
        for k in range(32):
            a = math.tau*k/32
            row.append(g.vert(p+max(.004,r)*(depth_ratio*math.cos(a)*n+math.sin(a)*b)))
        if rows:
            for k in range(32):
                j=(k+1)%32
                g.face((rows[-1][k],row[k],row[j],rows[-1][j]))
        rows.append(row)
    for end in (0,-1):
        center = g.vert(points[end])
        for k in range(32):
            g.face((center,rows[end][k],rows[end][(k+1)%32]))
    obj = g.finish(PREFIX+name+('_R' if side>0 else '_L'),high,[])
    obj.parent = bpy.data.objects[PREFIX.rstrip('_')]
    return obj


def sculpt_orbits(high, painted=False):
    for ob in list(high.objects):
        if ob.type=='MESH' and not ob.get('limb_id'):
            sculpt_head(ob)
    brow = [(.045,-1.89,7.16,.032),(.17,-2.00,7.18,.110),(.41,-2.02,7.38,.165),
            (.71,-1.99,7.61,.204),(1.03,-1.87,7.76,.173),(1.31,-1.60,7.92,.138),
            (1.51,-1.38,8.16,.09),(1.60,-1.24,8.39,.014)]
    lid = [(.23,-1.84,7.00,.018),(.37,-1.90,6.92,.040),(.59,-1.94,6.915,.055),
           (.81,-1.91,7.00,.065),(.98,-1.83,7.20,.062),(1.07,-1.73,7.36,.049),
           (1.02,-1.65,7.48,.014)]
    # The lids turn into the temples. A second fold shells the brow into the
    # forehead, and the cheek slopes into the first fleshy roots of the beard.
    crest=[(.10,-1.92,7.22,.065),(.27,-1.89,7.42,.17),(.46,-1.66,7.65,.17),
           (.61,-1.39,7.90,.135),(.67,-1.18,8.16,.065),(.63,-1.04,8.37,.009)]
    bridge=[(.16,-1.78,7.08,.07),(.18,-1.83,6.89,.205),(.28,-1.90,6.68,.225),
            (.42,-1.98,6.43,.216),(.53,-2.00,6.20,.12),(.54,-1.98,6.04,.021)]
    malar=[(.98,-1.40,7.12,.07),(.93,-1.57,6.90,.193),(.86,-1.66,6.68,.235),
           (.88,-1.72,6.47,.22),(1.02,-1.72,6.24,.16),(1.21,-1.66,6.08,.038)]
    folds=[]
    for side in (-1,1):
        for name,knots,depth in [('Brow',brow,1.08),('Lid',lid,.87),('Crest',crest,1.2),
                                 ('OralBridge',bridge,1.20),('Cheek',malar,1.28)]:
            folds.append(orbital_fold(high,side,name,knots,depth))
    nasal=[(.018,-1.83,7.09,.035),(.02,-1.92,6.94,.114),(.023,-1.99,6.78,.19),
           (-.025,-2.03,6.63,.262),(-.13,-2.12,6.39,.287),(-.21,-2.19,6.17,.218),
           (-.24,-2.24,5.91,.045)]
    folds.append(orbital_fold(high,1,'NasalMantle',nasal,.95))
    if painted:
        for fold in folds:
            cartilage=any(s in fold.name for s in ('Brow','Crest','Lid'))
            material='PAINT_Cartilage_Green' if cartilage else 'PAINT_Skin_TealAndOlive'
            fold.data.materials.append(bpy.data.materials[material])
    for side in ('L','R'):
        fin=high.objects[PREFIX+'CheekFin_'+side]
        xyz=coordinates(fin)
        xyz[:,1]-=.13
        write_coordinates(fin,xyz)
        folds.append(fin)
    core = high.objects[PREFIX+'OrganicCore']
    fuse_face(core,folds,painted)
    round_posterior_mantle(core)


def round_posterior_mantle(core):
    """Carry the dome behind the wing root, fading into the pointed rear tip."""
    xyz = coordinates(core)
    x, y, z = xyz.T.copy()
    # The reference has a high convex back above a nearly level ventral shelf.
    # Lift the roof of each cross-section without lowering it onto the rear arms.
    along = smooth(.25, 3.65, y) * (1 - smooth(4.05, 5.68, y))
    medial = 1 - smooth(1.40, 2.08, np.abs(x))
    roof = smooth(-.65, .72, z - (7.85 - .37*y))
    xyz[:, 2] += .86 * along * medial * roof
    write_coordinates(core, xyz)


def fuse_face(core, folds, painted):
    # Remesh to blend new eyelid, forehead and muzzle volumes. Graft the
    # original authored eight-way fork back so the wrist interfaces stay exact.
    source=bmesh.new()
    source.from_mesh(core.data)
    tissue=source.verts.layers.float['CrownTissue']
    fork_vertices=[v for v in source.verts if v[tissue]>.5]
    fork_index={v:i for i,v in enumerate(fork_vertices)}
    fork_points=[v.co.copy() for v in fork_vertices]
    fork_faces=[tuple(fork_index[v] for v in f.verts) for f in source.faces if all(v in fork_index for v in f.verts)]
    bmesh.ops.delete(source,geom=fork_vertices,context='VERTS')
    rim=[e for e in source.edges if e.is_boundary]
    assert rim and all(abs(v.co.z-5.75)<.004 for e in rim for v in e.verts)
    # A short temporary cap keeps the remesher from welding oral tendrils
    # to the wrist cuffs. Its entire lower section is removed after remesh.
    extrusion=bmesh.ops.extrude_edge_only(source,edges=rim)
    cap_verts=[v for v in extrusion['geom'] if isinstance(v,bmesh.types.BMVert)]
    for v in cap_verts:
        v.co.x*=.933
        v.co.y=(v.co.y-.19)*.951+.14
        v.co.z-=.27
    bmesh.ops.holes_fill(source,edges=[e for e in source.edges if e.is_boundary])
    bmesh.ops.recalc_face_normals(source,faces=list(source.faces))
    source.to_mesh(core.data)
    source.free()
    bpy.ops.object.select_all(action='DESELECT')
    for ob in [core]+folds:
        ob.select_set(True)
    bpy.context.view_layer.objects.active=core
    bpy.ops.object.join()
    if painted:
        slots=list(core.data.materials)
        skin=next(i for i,m in enumerate(slots) if 'Skin_Teal' in m.name)
        original=BVHTree.FromPolygons([v.co.copy() for v in core.data.vertices],
                                      [p.vertices[:] for p in core.data.polygons])
        indices=np.empty(len(core.data.polygons),np.int32)
        core.data.polygons.foreach_get('material_index',indices)
    core.data.remesh_voxel_size=.023
    core.data.use_remesh_preserve_attributes=False
    bpy.ops.object.voxel_remesh()
    sm=core.modifiers.new('Relax sculpted orbital transitions','SMOOTH')
    sm.factor=.72
    sm.iterations=11
    bpy.ops.object.modifier_apply(modifier=sm.name)
    mesh=bmesh.new()
    mesh.from_mesh(core.data)
    tissue=mesh.verts.layers.float.get('CrownTissue') or mesh.verts.layers.float.new('CrownTissue')
    z=5.71
    origin=Vector((0,.18,z))
    bmesh.ops.bisect_plane(mesh,geom=list(mesh.verts)+list(mesh.edges)+list(mesh.faces),
                           plane_no=Vector((0,0,1)),plane_co=origin,dist=.000001)
    seed=min((f for f in mesh.faces if abs(f.calc_center_median().x)<.20 and abs(f.calc_center_median().y)<.2 and 5.32<f.calc_center_median().z<5.70),
             key=lambda f:f.calc_center_median().z)
    remove={seed}
    work=[seed]
    while work:
        for e in work.pop().edges:
            for face in e.link_faces:
                if face not in remove and face.calc_center_median().z<z-.000001:
                    work.append(face)
                    remove.add(face)
    bmesh.ops.delete(mesh,geom=list(remove),context='FACES')
    neck=[v for v in mesh.verts if v.is_boundary]
    assert neck and max(abs(v.co.z-z) for v in neck)<.002
    bmesh.ops.remove_doubles(mesh,verts=neck,dist=.001)
    neck=[v for v in mesh.verts if v.is_boundary]
    new=[mesh.verts.new(p) for p in fork_points]
    for v in new:
        v[tissue]=1.
    for ids in fork_faces:
        mesh.faces.new([new[i] for i in ids])
    fork_rim=[v for v in new if v.is_boundary]
    assert len(fork_rim)==160
    # Intermediate neck rings follow the original taper. A single long
    # triangular transition here produces a visible horizontal band in profile.
    from winged_cthulhu.crown import trunk_angles
    angles=list(trunk_angles())
    previous=fork_rim
    for h in (5.61,5.66):
        dz=h-5.56
        ring=[mesh.verts.new(((1.25+.38*dz)*math.sin(a),
                             .16+.19*dz-(1.25+.23*dz)*math.cos(a),h)) for a in angles]
        stitch(mesh,previous,ring,origin,Vector((1,0,0)),Vector((0,1,0)),0)
        previous=ring
    stitch(mesh,neck,previous,origin,Vector((1,0,0)),Vector((0,1,0)),0)
    # The core is one continuous sculpture. Isolated voxel flecks can
    # survive on a thin fin edge and collapse to loose triangles in the LOD.
    connected={new[0]}
    pending=[new[0]]
    while pending:
        vertex=pending.pop()
        for edge in vertex.link_edges:
            neighbor=edge.other_vert(vertex)
            if neighbor not in connected:
                connected.add(neighbor)
                pending.append(neighbor)
    flecks=[v for v in mesh.verts if v not in connected]
    if flecks:
        bmesh.ops.delete(mesh,geom=flecks,context='VERTS')
    blend=[v for v in mesh.verts if 5.58<v.co.z<6.04 and (v.co-Vector((0,.18,v.co.z))).length<1.55]
    for _ in range(8):
        bmesh.ops.smooth_vert(mesh,verts=blend,factor=.48,use_axis_x=True,use_axis_y=True,use_axis_z=True)
    bmesh.ops.recalc_face_normals(mesh,faces=list(mesh.faces))
    for face in mesh.faces:
        face.smooth=True
        if painted:
            if any(v[tissue]>.5 for v in face.verts):
                face.material_index=skin
            else:
                face.material_index=int(indices[original.find_nearest(face.calc_center_median())[2]])
    assert not any(e.is_boundary for e in mesh.edges)
    mesh.to_mesh(core.data)
    mesh.free()
    if painted:
        # Remesh can drop slots on some Blender versions. Never clear a
        # populated slot list here, which would also reset the polygon indices.
        assert list(core.data.materials)==slots
    core.data.update()


def check_roots(high):
    core = high.objects[PREFIX+'OrganicCore']
    tree = KDTree(len(core.data.vertices))
    for v in core.data.vertices:
        tree.insert(v.co,v.index)
    tree.balance()
    results = []
    for arm in sorted((o for o in high.objects if o.get('limb_id')),key=lambda o:o['limb_id']):
        key=arm['limb_id']
        origin,_,n,b,r=seat(key)
        arm_tree = KDTree(len(arm.data.vertices))
        for v in arm.data.vertices:
            arm_tree.insert(v.co,v.index)
        arm_tree.balance()
        for i in range(SIDES):
            angle=math.tau*i/SIDES
            p=origin+r*(n*math.cos(angle)+b*math.sin(angle))
            results.append(max(tree.find(p)[2],arm_tree.find(p)[2]))
    assert len(results)==320 and max(results)<.00001,(len(results),max(results))
    return max(results)


def clay_scene(scene):
    # Workbench and solid viewport use the same neutral clay and cavity light.
    scene.render.engine='BLENDER_WORKBENCH'
    scene.render.film_transparent=False
    scene.render.image_settings.file_format='PNG'
    scene.render.image_settings.color_mode='RGB'
    scene.render.resolution_percentage=100
    scene.view_settings.view_transform='Standard'
    scene.view_settings.exposure=0
    scene.display.render_aa='16'
    def shade(s):
        s.light='STUDIO'
        s.studio_light='paint.sl'
        s.color_type='OBJECT'
        s.show_shadows=False
        s.show_cavity=True
        s.cavity_type='BOTH'
        s.cavity_ridge_factor=.65
        s.cavity_valley_factor=1.1
        s.curvature_ridge_factor=.55
        s.curvature_valley_factor=.80
        s.background_type='WORLD'
        s.show_specular_highlight=True
    shade(scene.display.shading)
    scene.world.color=(.042,.051,.057)
    for ob in bpy.data.collections['HIGH'].objects:
        value=.51
        if '_Eye_' in ob.name: value=.67
        if '_Socket_' in ob.name: value=.36
        ob.color=(value,value,value,1)
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type=='VIEW_3D':
                space=area.spaces.active
                space.shading.type='SOLID'
                shade(space.shading)
                space.overlay.show_overlays=False
                space.region_3d.view_perspective='CAMERA'
                space.region_3d.view_camera_zoom=10


def assign_clay(high):
    for label,value in [('Skin',.33),('Orbit',.17),('Eye',.52)]:
        name='Study clay '+label
        mat=bpy.data.materials.get(name) or bpy.data.materials.new(name)
        mat.diffuse_color=(value,value,value,1)
        mat.use_nodes=True
        shader=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
        shader.inputs['Base Color'].default_value=(value,value,value,1)
        shader.inputs['Roughness'].default_value=.46 if label=='Eye' else .59
        for ob in high.objects:
            tag='Eye' if '_Eye_' in ob.name else 'Orbit' if '_Socket_' in ob.name else 'Skin'
            if ob.type=='MESH' and tag==label:
                ob.data.materials.clear()
                ob.data.materials.append(mat)


def studio(scene, cam):
    from create_winged_cthulhu import aim
    scene.render.engine='CYCLES'
    scene.cycles.device='CPU'
    scene.cycles.samples=24
    scene.cycles.use_denoising=True
    scene.view_settings.view_transform='AgX'
    scene.view_settings.look='AgX - Medium High Contrast'
    scene.view_settings.exposure=-.12
    bg=next(n for n in scene.world.node_tree.nodes if n.type=='BACKGROUND')
    bg.inputs[0].default_value=(.12,.138,.15,1)
    bg.inputs[1].default_value=.45
    center=Vector((0,0,5.15))
    rotation=cam.rotation_euler.to_quaternion()
    for name,offset,power,size in [('Key softbox',(-7,10,8),2600,6.0),('Warm fill',(7,1,8),400,7),
                                   ('Sea rim',(3,8,-7),2400,6),('Lower bounce',(-2,-5,6),155,6)]:
        light=bpy.data.objects[name]
        # Keep the entire emitter outside the silhouette, including the side-facing coils.
        light.location=center+rotation @ (2*Vector(offset))
        light.data.color=(1,1,1)
        light.data.size=2*size
        light.data.energy=4*power
        aim(light,center)


def render_views(views, directory, draft):
    scene=bpy.context.scene
    for name, cam, width, height in views:
        scene.camera=cam
        if not draft:
            studio(scene,cam)
        scene.render.resolution_x=width//(2 if draft else 1)
        scene.render.resolution_y=height//(2 if draft else 1)
        scene.render.filepath=str(directory/(name+'.png'))
        bpy.ops.render.render(write_still=True)


def main():
    if not bpy.app.background or Path(bpy.data.filepath).name!='CR_WingedCthulhu_radial.blend':
        raise RuntimeError('Open the radial sculpt in a separate background Blender process')
    draft='--draft' in sys.argv
    WORK.mkdir(parents=True,exist_ok=True)
    out=WORK if draft else REVIEW
    out.mkdir(parents=True,exist_ok=True)
    scene=bpy.context.scene
    high=bpy.data.collections['HIGH']
    if '--production' in sys.argv:
        for key in POSE:
            pose_arm(high.objects[PREFIX+'Arm_'+key])
        sculpt_orbits(high,painted=True)
        check_roots(high)
        core=high.objects[PREFIX+'OrganicCore']
        core['portrait_paint']=True
        # The cranium alone defines the rear extent of the body hit proxy.
        cranial=[p for p in core.data.polygons if 'Cranium' in core.data.materials[p.material_index].name]
        core['mantle_rear']=max(core.data.vertices[i].co.y for p in cranial for i in p.vertices)
        camera('CAM_SculptPortrait',(-7.4,-32,8.8),(0,0,5.13),16.2)
        camera('CAM_SculptProfile',(32,0,5.1),(0,0,5.1),13.6)
        output=HERE.parents[1]/'artifacts/cthulhu-production'
        output.mkdir(parents=True,exist_ok=True)
        bpy.context.preferences.filepaths.save_version=0
        bpy.ops.wm.save_as_mainfile(filepath=str(output/'painted-sculpt.blend'),compress=True)
        print('PAINTED_SCULPT_READY',core['mantle_rear'],flush=True)
        return
    view_specs=[
        ('portrait',(-7.4,-32,8.8),(0,0,5.13),16.2,1650,1130),
        ('front',(0,-32,5.1),(0,0,5.1),16.1,1500,1040),
        ('side',(32,0,5.1),(0,0,5.1),13.6,1280,1100),
        ('back',(0,32,6.8),(0,0,5.1),16.0,1500,1040),
        ('top',(0,0,36),(0,0,0),16.15,1500,1100),
        ('eyes',(-1.0,-32,8.7),(0,-.8,7.17),6.1,1280,750),
        ('roots',(0,-.01,-28),(0,0,4.25),15.8,1500,1040)]
    views=[(name,camera('Clay_'+name,pos,target,scale),w,h) for name,pos,target,scale,w,h in view_specs]
    clay_scene(scene)
    assign_clay(high)
    render_views([('previous-'+n,c,w,h) for n,c,w,h in views if n in ('portrait','eyes')],out,draft)
    for key in POSE:
        pose_arm(high.objects[PREFIX+'Arm_'+key])
    sculpt_orbits(high)
    clay_scene(scene)
    error=check_roots(high)
    scene['review_seams']='320 static crown and arm rim positions checked; maximum error %.9f m'%error
    scene['review_scope']='Static clay study based on user concept and Tripo multi-view screenshots'
    scene.camera=views[0][1]
    scene.render.resolution_x,scene.render.resolution_y=views[0][2:]
    scene.render.filepath='//review/clay-revision/portrait.png'
    if not draft:
        # Keep the review file self-contained, exclusively neutral and easy to orbit.
        assign_clay(high)
        studio(scene,scene.camera)
        active=high.objects[PREFIX+'OrganicCore']
        bpy.ops.object.select_all(action='DESELECT')
        bpy.context.view_layer.objects.active=active
        for image in list(bpy.data.images):
            if image.source=='FILE' and image.users==0:
                bpy.data.images.remove(image)
        bpy.ops.outliner.orphans_purge(do_recursive=True)
        bpy.context.preferences.filepaths.save_version=0
        bpy.ops.wm.save_as_mainfile(filepath=str(BASE/'CR_WingedCthulhu_clay_review.blend'),compress=True)
    render_views(views,out,draft)
    print('CLAY_REVIEW_DONE',len(views),error,flush=True)


if __name__=='__main__':
    main()
