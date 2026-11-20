"""Continuous external stairs. Coordinates are in the finished scene, in metres.

Loaded alongside the compound's mesh, box and rod constructors. Each path runs
from the yard to the edge of the roof finish. A change of direction occurs on a
level part of the path. The very last riser ends at the start of that level part.
"""
from collections import namedtuple


AccessNode = namedtuple('AccessNode', 'xy z')
AccessPlan = namedtuple('AccessPlan', 'building nodes risers width mouth_axis face roof_bounds')
ACCESS_THICKNESS = .30
ACCESS_MATERIALS = {
    'concrete': 'FC_Concrete', 'trim': 'FC_Coping',
    'yellow': 'FC_FadedSafetyYellow', 'metal': 'FC_Galvanized',
}


def access_bounds(obj):
    p = [obj.matrix_world @ Vector(v) for v in obj.bound_box]
    return tuple(min(v[i] for v in p) for i in range(3)), tuple(max(v[i] for v in p) for i in range(3))


def stair_plans(scene):
    plans = []
    for building, code in [('A1', '03_A1'), ('B1', '04_B1'), ('C1', '05_C1')]:
        roof_lo, roof_hi = access_bounds(scene.objects[code + ' roof finish'])
        body_lo, body_hi = access_bounds(scene.objects[code + ' body'])
        site_z = access_bounds(scene.objects[code + ' foundation'])[0][2]
        z = roof_hi[2]
        east, north = Vector((1, 0)), Vector((0, 1))
        if building == 'C1':
            width = 2.40
            approach = Vector((-.45, .89)).normalized()
            elbow = Vector((12.35, -40.75))
            crest1 = elbow - approach * 1.55
            toe = crest1 - approach * (28 * .285)
            toe2 = elbow - east * 1.55
            crest2 = toe2 - east * (25 * .285)
            roof_turn = crest2 - east * 1.55
            mouth = Vector((roof_turn.x, roof_hi[1]))
            middle = z * 29 / 55
            nodes = [(toe, 0), (crest1, middle), (elbow, middle), (toe2, middle),
                     (crest2, z), (roof_turn, z), (mouth, z)]
            risers = [29, 0, 0, 26, 0, 0]
            axis, face = 0, body_hi[1]
        elif building == 'A1':
            width = 1.90
            elbow = Vector((8.10, 41.30))
            crest = elbow - east * 1.24
            toe = crest - east * (21 * .285)
            mouth = Vector((elbow.x, roof_lo[1]))
            nodes = [(toe, site_z), (crest, z), (elbow, z), (mouth, z)]
            risers = [22, 0, 0]
            axis, face = 0, body_lo[1]
        else:
            width = 2.00
            elbow = Vector((30.45, 6.20))
            crest = elbow - east * 1.30
            toe = crest - east * (32 * .285)
            roof_turn = Vector((elbow.x, 10.60))
            mouth = Vector((roof_hi[0], roof_turn.y))
            nodes = [(toe, site_z), (crest, z), (elbow, z), (roof_turn, z), (mouth, z)]
            risers = [33, 0, 0, 0]
            axis, face = 1, body_hi[0]
        plans.append(AccessPlan(building, [AccessNode(*n) for n in nodes], risers, width,
                                axis, face, (body_lo[axis], body_hi[axis])))
    return plans


def access_offsets(plan):
    """Intersection of the two offset edges at each elbow, including the inner edge."""
    directions = [(b.xy - a.xy).normalized() for a, b in zip(plan.nodes, plan.nodes[1:])]
    normals = [Vector((-d.y, d.x)) for d in directions]
    offsets = [normals[0]]
    for i in range(1, len(normals)):
        offsets.append((normals[i-1] + normals[i]) / (1 + directions[i-1].dot(directions[i])))
    return offsets + [normals[-1]]


def access_sections(plan, offsets):
    """Cross sections of each level turn. The inside and outside use independent
    tangent arcs so a short landing can keep the full width of its last tread.
    Interpolating the edges gives the same walking and guard paths in plan."""
    sections = []
    half = plan.width/2
    for i,node in enumerate(plan.nodes):
        if i==0 or i==len(plan.nodes)-1 or plan.risers[i-1] or plan.risers[i]:
            sections.append([(node.xy,offsets[i])])
            continue
        arriving = node.xy-plan.nodes[i-1].xy
        leaving = plan.nodes[i+1].xy-node.xy
        u,v = arriving.normalized(),leaving.normalized()
        angle = math.atan2(u.x*v.y-u.y*v.x,u.dot(v))
        if abs(angle)<.01:
            sections.append([(node.xy,offsets[i])])
            continue
        turn = 1 if angle>0 else -1
        tangent = math.tan(abs(angle)/2)
        available = min(arriving.length,leaving.length)-half*tangent-.075
        inside = min(.40,available/tangent)
        outer = min(.94,plan.width*.40)
        boundaries = []
        for sign in (-1,1):
            edge = node.xy+offsets[i]*(sign*half)
            r = inside if sign==turn else outer
            boundaries.append(corner_arc(edge,u,v,r,16))
        sections.append([((a+b)*.5,(b-a)/plan.width)
                         for (a,_),(b,_) in zip(*boundaries)])
    return sections


def access_deck(plan, offsets):
    """A single closed folded slab, including all steps, landings and turning corners."""
    # A station has a centre, a transverse offset vector, and the three levels at
    # its cross section: soffit, lower edge of the riser, upper edge of the riser.
    sections = access_sections(plan,offsets)
    first = plan.nodes[0]
    stations = [[first.xy, offsets[0], first.z - ACCESS_THICKNESS, first.z, first.z]]
    for i, count in enumerate(plan.risers):
        a, b = plan.nodes[i:i+2]
        if count:
            rise = (b.z - a.z) / count
            stations[-1][4] = a.z + rise
            for k in range(1, count):
                t = k / (count - 1)
                xy = a.xy.lerp(b.xy, t)
                soffit = (1-t)*a.z + t*b.z - ACCESS_THICKNESS
                stations.append([xy, offsets[i].lerp(offsets[i+1], t), soffit,
                                 a.z + k*rise, a.z + (k+1)*rise])
        else:
            for xy,across in sections[i+1]:
                stations.append([xy,across,b.z-ACCESS_THICKNESS,b.z,b.z])
    verts, faces, rings = [], [], []
    for xy, offset, low, before, after in stations:
        ring = []
        for side in (-1, 1):
            edge = xy + offset * (side * plan.width / 2)
            floor = len(verts)
            verts.extend([(*edge, low), (*edge, before)])
            if abs(after - before) > 1.e-5:
                verts.append((*edge, after))
            ring.append((floor, floor+1, len(verts)-1))
        rings.append(ring)
        l, r = ring
        if l[1] != l[2]:
            faces.append((l[1], r[1], r[2], l[2]))
    for (l, r), (nl, nr) in zip(rings, rings[1:]):
        faces.extend([(l[2], r[2], nr[1], nl[1]), (r[0], l[0], nl[0], nr[0])])
        # The intermediate vertex on each vertical edge also belongs to the
        # preceding side panel and riser. Retain it so there are no T-junctions.
        for p, q in ((l, nl), (r, nr)):
            side = [p[0], q[0], q[1], p[2]]
            if p[1] != p[2]:
                side.append(p[1])
            faces.append(tuple(side))
    front_l, front_r = rings[0]
    end_l, end_r = rings[-1]
    faces.extend([(front_l[0], front_r[0], front_r[1], front_l[1]),
                  (end_l[0], end_l[2], end_r[2], end_r[0])])
    deck = mesh(plan.building+' access | continuous stair and landings', verts, faces, 'concrete')
    deck.data.materials.append(M['trim'])
    for face in deck.data.polygons:
        if face.normal.z > .999:
            face.material_index = 1
    if DETAIL:
        bevel = deck.modifiers.new('Cast tread edges', 'BEVEL')
        bevel.width = .009
        bevel.segments = 2
    deck['clear_structural_width_m'] = plan.width
    deck['riser_counts'] = list(plan.risers)
    return deck


def access_tube(name, corners, radius):
    """Form a continuous bent tube, including the short returns into the jambs."""
    path = [corners[0]]
    for a, b, c in zip(corners, corners[1:], corners[2:]):
        incoming, outgoing = b-a, c-b
        if incoming.normalized().dot(outgoing.normalized()) > .9999:
            continue
        trim = min(.16, incoming.length*.32, outgoing.length*.32)
        p = b-incoming.normalized()*trim
        q = b+outgoing.normalized()*trim
        path.append(p)
        for j in range(1, 7):
            t = j/6
            path.append(p*((1-t)**2) + b*(2*t*(1-t)) + q*(t*t))
    path.append(corners[-1])
    ring_size = 12
    verts, faces = [], []
    for j, p in enumerate(path):
        prev = (p-path[j-1]).normalized() if j else (path[1]-p).normalized()
        nxt = (path[j+1]-p).normalized() if j+1 < len(path) else prev
        tangent = (prev+nxt).normalized()
        side = tangent.cross(Vector((0, 0, 1))).normalized()
        up = tangent.cross(side).normalized()
        for k in range(ring_size):
            a = 2*pi*k/ring_size
            verts.append(p + radius*(side*cos(a)+up*sin(a)))
        if j:
            start = (j-1)*ring_size
            for k in range(ring_size):
                a, b = start+k, start+(k+1)%ring_size
                faces.append((a,b,b+ring_size,a+ring_size))
    faces.extend([tuple(reversed(range(ring_size))),tuple(range(len(verts)-ring_size,len(verts)))])
    tube = mesh(name, verts, faces, 'yellow')
    for f in tube.data.polygons:
        f.use_smooth = len(f.vertices) == 4


def access_guards(plan, offsets):
    nodes = plan.nodes
    sections = access_sections(plan,offsets)
    for sign, side_name in [(-1, 'right'), (1, 'left')]:
        name = plan.building+' access | '+side_name+' '
        lane = sign*(plan.width/2-.12)
        edge = [[p+off*lane for p,off in group] for group in sections]
        # Set the first post in the middle of the first tread. Return the far end
        # outward into the jamb at the parapet centre, clear of the walking route.
        cut = .1425/(nodes[1].xy-nodes[0].xy).length
        edge[0][0] = edge[0][0].lerp(edge[1][0], cut)
        heights = [n.z for n in nodes]
        heights[0] += (nodes[1].z-nodes[0].z)*cut
        edge[-1][0][1-plan.mouth_axis] = plan.face
        rail_end = edge[-1][0]+offsets[-1]*(sign*.16)
        for h, rad, label in [(1.035,.037,'handrail'),(.545,.024,'guard rail')]:
            corners = [Vector((*p,z+h)) for group,z in zip(edge,heights) for p in group]
            access_tube(name+label, corners+[Vector((*rail_end, nodes[-1].z+h))], rad)
        # Anchor sloping posts on tread centres. Flat posts sit on the same slab as
        # their adjoining flights; omit end positions in the elbow bend radius.
        posts = []
        flat_path = []
        for i, count in enumerate(plan.risers):
            a,b = nodes[i:i+2]
            ea, eb = edge[i][-1],edge[i+1][0]
            if count:
                steps = count-1
                positions = sorted({0, steps-1, *range(4, steps-2, 4)})
                rise = (b.z-a.z)/count
                aa = a.xy + offsets[i]*(sign*(plan.width/2-.12))
                for k in positions:
                    t = (k+.5)/steps
                    p = aa.lerp(eb, t)
                    posts.append((p, a.z+(k+1)*rise, a.z+(b.z-a.z)*t))
            else:
                if not flat_path:
                    flat_path.append(ea)
                flat_path.extend(edge[i+1])
                if i+1==len(plan.risers) or plan.risers[i+1]:
                    length=sum((q-p).length for p,q in zip(flat_path,flat_path[1:]))
                    count_flat=max(1,math.ceil(length/1.23))
                    for j in range(count_flat):
                        dist=length*(j+.5)/count_flat
                        for p,q in zip(flat_path,flat_path[1:]):
                            side_length=(q-p).length
                            if dist<=side_length:
                                posts.append((p.lerp(q,dist/side_length),a.z,a.z))
                                break
                            dist-=side_length
                    flat_path=[]
        for p, floor, pitch in posts:
            box(name+'anchor plate', (*p, floor+.015), (.105,.105,.030), 'metal', .006)
            rod(name+'stanchion', (*p, floor+.028), (*p, pitch+1.032), .031, 'yellow', 12)


def access_supports(plan):
    prefix = plan.building+' access | '
    z = plan.nodes[-1].z
    ground = plan.nodes[0].z
    if plan.building == 'C1':
        middle = plan.nodes[2]
        for dy in (-.63,.63):
            top = middle.z-ACCESS_THICKNESS
            box(prefix+'middle pier', (middle.xy.x,middle.xy.y+dy,(ground+top)/2),
                (.48,.42,top-ground), 'concrete', .025)
    if plan.building == 'B1':
        for idx in (2,3):
            p = plan.nodes[idx].xy
            top = z-ACCESS_THICKNESS
            box(prefix+'gallery pier', (*p,(ground+top)/2), (.52,.52,top-ground), 'concrete', .025)
    # A pair of tapered corbels cantilever from the actual building face to the
    # far side of the upper elbow. No support projects across the walkway.
    u = (plan.nodes[-2].xy-plan.nodes[-1].xy).normalized()
    v = Vector((-u.y,u.x))
    inside = plan.nodes[-1].xy.copy()
    inside[1-plan.mouth_axis] = plan.face
    inside -= u*.04
    length = (plan.nodes[-2].xy-inside).dot(u) + plan.width*.38
    top = z-ACCESS_THICKNESS
    for s in (-1,1):
        start = inside + v*(s*(plan.width/2-.32))
        outline = [(0,top-1.50),(.24,top-1.50),(length,top-.22),(length,top),(0,top)]
        profile(prefix+'roof corbel', outline, .28, (*start,0), 'concrete', math.atan2(u.y,u.x))


# Specific to migrating previously delivered files. Names are scoped to the
# owned building collection, so other manual objects and collections survive.
LEGACY_ACCESS = {
    'A1': ('A1 straight stair','A1 landing','A1 front parapet','A1 east parapet rear'),
    'B1': ('B1 stair','B1 upper landing','B1 landing support','B1 landing bridge',
           'B1 upper rail','B1 east parapet rear'),
    'C1': ('C1 lower flight','C1 upper flight','C1 middle landing','C1 landing pier',
           'C1 roof landing','C1 rear parapet','C1 middle platform','C1 roof entry'),
}


def rebuild_stair_access(scene, detailed=True):
    global S,C,DETAIL,M
    bpy.context.view_layer.update()
    plans = stair_plans(scene)
    by_mat = {slot.material.name.split('.')[0]:slot.material for o in scene.objects if o.type=='MESH'
              for slot in o.material_slots if slot.material and o.get('owner')==TAG}
    # In a blockout some materials have been created but never assigned yet.
    scene_materials = {key:by_mat[mat] if mat in by_mat else M[key] for key,mat in ACCESS_MATERIALS.items()
                       if detailed or key in ('concrete','trim')}
    S,DETAIL,M = scene,detailed,{**M,**scene_materials}
    for plan, code in zip(plans, ('03_A1','04_B1','05_C1')):
        C = scene.objects[code+' roof finish'].users_collection[0]
        for o in list(C.objects):
            if o.get('owner')==TAG and o.name.startswith(LEGACY_ACCESS[plan.building]+(plan.building+' access | ',)):
                bpy.data.objects.remove(o,do_unlink=True)
        offsets = access_offsets(plan)
        access_deck(plan, offsets)
        if detailed:
            access_guards(plan, offsets)
        roof_parapet(scene,plan,code)
        access_supports(plan)
    bpy.context.view_layer.update()
    return plans
