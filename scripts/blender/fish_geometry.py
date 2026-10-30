"""Closed lightweight meshes, explicit skin weights and nearest-sampled pixel atlases."""
from pathlib import Path
import math
import struct
import zlib
import bpy
import bmesh
from mathutils import Vector
from mathutils.geometry import tessellate_polygon


def v(point):
    """Fish space: X nose, Y width, Z up; Blender: -Y nose, Z up."""
    x, y, z = point
    return Vector((y, -x, z))


def rgb(value):
    return tuple(int(value[i:i+2], 16) for i in (0, 2, 4))


def mix(a, b, t):
    return tuple(round(x*(1-t)+y*t) for x,y in zip(a,b))


def swatch(index):
    return ((index % 16 + .5)/16, (index//16+.5)/8)


def write_png(path, width, height, pixels):
    """Write the authored sRGB texels without an external imaging dependency."""
    def chunk(tag, data):
        return struct.pack('>I', len(data))+tag+data+struct.pack('>I',zlib.crc32(tag+data)&0xffffffff)
    rows = b''.join(b'\0'+bytes(pixels[y*width*4:(y+1)*width*4]) for y in reversed(range(height)))
    path.write_bytes(b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',width,height,8,6,0,0,0))+
                     chunk(b'IDAT',zlib.compress(rows,9))+chunk(b'IEND',b''))


def make_material(spec, directory):
    base, belly, back, fin, edge, marking = [rgb(c) for c in spec['colors']]
    palette = [base,belly,back,fin,edge,marking,(30,27,39),(255,245,209),
               (12,17,29),(255,255,240),(219,126,89),(132,61,57),
               mix(fin,(20,20,39),.32),mix(base,(255,247,215),.25),
               mix(base,(31,31,50),.3),(255,222,125)]
    palette += [mix(c,(255,246,223),.28) for c in palette[:16]]
    width,height=256,128
    pixels=[]
    for y in range(height):
        for x in range(width):
            if y<32:
                color=palette[(y//16)*16+x//16]
            elif x>=128:
                u=(x-128)/127
                w=(y-32)/95
                color=fin
                if w>.86 or u<.07 or u>.94:
                    color=edge
                elif (int(u*11)+int(w*2))%4==0:
                    color=mix(fin,back,.32)
                elif (x//3+y//4)%7==0:
                    color=mix(fin,edge,.35)
                if spec['fin']=='lion' and int((u+w*.14)*10)%2==0:
                    color=edge
            else:
                u=x/127
                t=(y-32)/96
                vertical=math.sin(t*math.tau)
                if vertical>.45:
                    color=mix(base,back,.65 if vertical<.85 else .9)
                elif vertical<-.3:
                    color=mix(base,belly,.65 if vertical>-.75 else 1)
                else:
                    color=base
                pattern=spec['pattern']
                if pattern=='stripes' and vertical>-.05 and u<.88:
                    if (x//4+abs((y//3)%6-3))%4==0:
                        color=marking
                elif pattern=='spots' and vertical>-.6:
                    cellx,celly=x//17,y//14
                    dx=x%17-(5+(celly*3+cellx)%7)
                    dy=y%14-(4+(cellx*2)%5)
                    if dx*dx+dy*dy<(10+(cellx+celly)%3*8):
                        color=marking
                elif pattern=='bands':
                    color=marking if (x//7+(y//11)%2)%3==0 else color
                elif pattern=='moon':
                    dx=(x-74)/17
                    dy=(y-(35 if y<65 else 80))/18
                    if .38<dx*dx+dy*dy<1 and dx<.35:
                        color=marking
                elif pattern=='stars':
                    sx,sy=(x-8)%23-7,(y-37)%25-8
                    if (abs(sx)<=1 and abs(sy)<=4) or (abs(sy)<=1 and abs(sx)<=4):
                        color=marking
                elif pattern=='dashes' and vertical>-.3:
                    if x%18<3 and (y//4)%3==0:
                        color=marking
                elif pattern=='scales' and u<.85 and vertical>-.65:
                    if (x+(y//5%2)*4)%9<3 and y%5==0:
                        color=mix(color,edge,.3)
                # Small consistent blocks; no per-pixel random noise.
                if x//4%7==0 and y//3%4==0:
                    color=mix(color,belly,.12)
            pixels.extend((*color,255))
    path=Path(directory)/f"{spec['id']}_albedo.png"
    write_png(path,width,height,pixels)
    image=bpy.data.images.load(str(path),check_existing=False)
    image.name=spec['id']+'_PixelAtlas'
    image.colorspace_settings.name='sRGB'
    mat=bpy.data.materials.new(spec['id']+'_PixelMaterial')
    mat.use_nodes=True
    bsdf=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    bsdf.inputs['Roughness'].default_value=.88
    bsdf.inputs['Metallic'].default_value=0
    tex=mat.node_tree.nodes.new('ShaderNodeTexImage')
    tex.image=image
    tex.interpolation='Closest'
    mat.node_tree.nodes.active=tex
    mat.node_tree.links.new(tex.outputs['Color'],bsdf.inputs['Base Color'])
    mat.diffuse_color=tuple(c/255 for c in base)+(1,)
    return mat,path


class Surface:
    def __init__(self):
        self.vertices=[]
        self.faces=[]
        self.uvs=[]
        self.weights=[]

    def vertex(self, point, weights):
        self.vertices.append(tuple(v(point)))
        self.weights.append(weights if isinstance(weights,dict) else {weights:1.0})
        return len(self.vertices)-1

    def face(self, indices, uv=None, color=None):
        self.faces.append(tuple(indices))
        self.uvs.append(uv if uv is not None else [swatch(color if color is not None else 0)]*len(indices))

    def ellipsoid(self, center, radii, bone, color, segments=12, rings=7):
        cx,cy,cz=center
        top=self.vertex((cx,cy,cz+radii[2]),bone)
        grid=[]
        for i in range(1,rings):
            phi=math.pi*i/rings
            row=[]
            for j in range(segments):
                theta=math.tau*j/segments
                row.append(self.vertex((cx+radii[0]*math.sin(phi)*math.cos(theta),
                    cy+radii[1]*math.sin(phi)*math.sin(theta),cz+radii[2]*math.cos(phi)),bone))
            grid.append(row)
        bottom=self.vertex((cx,cy,cz-radii[2]),bone)
        for j in range(segments):
            k=(j+1)%segments
            self.face((top,grid[0][j],grid[0][k]),color=color)
            self.face((bottom,grid[-1][k],grid[-1][j]),color=color)
        for i in range(len(grid)-1):
            for j in range(segments):
                k=(j+1)%segments
                self.face((grid[i][j],grid[i+1][j],grid[i+1][k],grid[i][k]),color=color)

    def tube(self, points, radius, bone, color, sides=6):
        rows=[]
        for i,point in enumerate(points):
            axis=Vector(points[min(i+1,len(points)-1)])-Vector(points[max(0,i-1)])
            axis.normalize()
            tangent=axis.cross(Vector((0,0,1)))
            if tangent.length<.01:
                tangent=axis.cross(Vector((0,1,0)))
            tangent.normalize()
            other=axis.cross(tangent).normalized()
            row=[]
            for j in range(sides):
                offset=radius*(tangent*math.cos(math.tau*j/sides)+other*math.sin(math.tau*j/sides))
                row.append(self.vertex(Vector(point)+offset,bone))
            rows.append(row)
        self.face(tuple(reversed(rows[0])),color=color)
        self.face(rows[-1],color=color)
        for a,b in zip(rows,rows[1:]):
            for j in range(sides):
                k=(j+1)%sides
                self.face((a[j],a[k],b[k],b[j]),color=color)

    def cone(self, base, tip, radius, bone, color, sides=6):
        axis=(Vector(tip)-Vector(base)).normalized()
        u=axis.cross(Vector((0,0,1)))
        if u.length<.01:
            u=axis.cross(Vector((0,1,0)))
        u.normalize()
        w=axis.cross(u).normalized()
        ring=[self.vertex(Vector(base)+radius*(math.cos(math.tau*j/sides)*u+
                     math.sin(math.tau*j/sides)*w),bone) for j in range(sides)]
        end=self.vertex(tip,bone)
        self.face(tuple(reversed(ring)),color=color)
        for j in range(sides):
            self.face((ring[j],ring[(j+1)%sides],end),color=color)

    def fin(self, points, bone, thickness=.018, normal=(0,1,0)):
        normal=Vector(normal).normalized()
        front=[]
        back=[]
        for point in points:
            weights=bone(point[0]) if callable(bone) else bone
            front.append(self.vertex(Vector(point)+normal*thickness*.5,weights))
            back.append(self.vertex(Vector(point)-normal*thickness*.5,weights))
        pointsv=[Vector(p) for p in points]
        # Tessellation also handles the concave fork and lionfish fan outline.
        lookup={tuple(p):i for i,p in enumerate(pointsv)}
        spans=[max(p[a] for p in points)-min(p[a] for p in points) for a in range(3)]
        axes=sorted(range(3),key=lambda a:spans[a],reverse=True)[:2]
        mins=[min(p[a] for p in points) for a in axes]
        def uv(index):
            point=points[index]
            u=(point[axes[0]]-mins[0])/max(spans[axes[0]],.001)
            w=(point[axes[1]]-mins[1])/max(spans[axes[1]],.001)
            return (.505+u*.488,.255+w*.737)
        for tri in tessellate_polygon([pointsv]):
            # Blender 5.2 returns indices; earlier versions returned vectors.
            ids=[p if isinstance(p,int) else lookup[tuple(p)] for p in tri]
            self.face([front[i] for i in ids],uv=[uv(i) for i in ids])
            ids.reverse()
            self.face([back[i] for i in ids],uv=[uv(i) for i in ids])
        for i in range(len(points)):
            j=(i+1)%len(points)
            self.face((front[i],back[i],back[j],front[j]),color=12)

    def object(self, name, collection, material, rig):
        mesh=bpy.data.meshes.new(name+'_Geometry')
        mesh.from_pydata(self.vertices,[],self.faces)
        mesh.update()
        uv=mesh.uv_layers.new(name='PixelUV')
        for polygon,coords in zip(mesh.polygons,self.uvs):
            for loop,coord in zip(polygon.loop_indices,coords):
                uv.data[loop].uv=coord
            polygon.use_smooth=True
        bm=bmesh.new()
        bm.from_mesh(mesh)
        bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
        bmesh.ops.triangulate(bm,faces=list(bm.faces))
        bm.to_mesh(mesh)
        bm.free()
        mesh.update()
        obj=bpy.data.objects.new(name,mesh)
        collection.objects.link(obj)
        obj.data.materials.append(material)
        for name in sorted({k for weights in self.weights for k in weights}):
            group=obj.vertex_groups.new(name=name)
            for index,weights in enumerate(self.weights):
                if weights.get(name,0)>.000001:
                    group.add([index],weights[name],'REPLACE')
        obj.parent=rig
        modifier=obj.modifiers.new('Fish skin','ARMATURE')
        modifier.object=rig
        return obj


def radius_at(spec,x):
    profile=spec['profile']
    for a,b in zip(profile,profile[1:]):
        if x<=b[0]:
            t=max(0,min(1,(x-a[0])/(b[0]-a[0])))
            return tuple(a[i]*(1-t)+b[i]*t for i in (1,2,3))
    return tuple(profile[-1][i] for i in (1,2,3))


def body_weights(spec,x):
    if x>=.25:
        return {'Head':1.0}
    if x>.06:
        t=(x-.06)/.19
        return {'Head':t,'Body':1-t}
    centres=spec['_centres']
    if x>=centres[0][1]:
        return {centres[0][0]:1.0}
    for (name1,a),(name2,b) in zip(centres,centres[1:]):
        if x>=b:
            t=(a-x)/(a-b)
            return {name1:1-t,name2:t}
    return {centres[-1][0]:1.0}


def add_body(surface,spec):
    profile=spec['profile']
    segments=16
    rows=[]
    xmin,xmax=profile[0][0],profile[-1][0]
    # Intermediate rings prevent a long coarse segment collapsing under a bend.
    refined=[]
    for a,b in zip(profile,profile[1:]):
        count=max(1,math.ceil((b[0]-a[0])/.12))
        for k in range(count):
            t=k/count
            refined.append(tuple(a[j]*(1-t)+b[j]*t for j in range(4)))
    refined.append(profile[-1])
    for x,ry,rz,zc in refined:
        rows.append([surface.vertex((x,ry*math.cos(math.tau*j/segments),
                   zc+rz*math.sin(math.tau*j/segments)),body_weights(spec,x)) for j in range(segments)])
    for i in range(len(rows)-1):
        ua=.005+.49*(refined[i][0]-xmin)/(xmax-xmin)
        ub=.005+.49*(refined[i+1][0]-xmin)/(xmax-xmin)
        for j in range(segments):
            k=(j+1)%segments
            va=.251+.748*j/segments
            vb=.251+.748*(j+1)/segments
            surface.face((rows[i][j],rows[i+1][j],rows[i+1][k],rows[i][k]),
                         uv=[(ua,va),(ub,va),(ub,vb),(ua,vb)])
    for row,point,reverse in [(rows[0],profile[0],True),(rows[-1],profile[-1],False)]:
        pole=surface.vertex((point[0],0,point[3]),body_weights(spec,point[0]))
        for j in range(segments):
            ids=(pole,row[j],row[(j+1)%segments])
            surface.face(tuple(reversed(ids)) if reverse else ids,color=0)


def add_tail(surface,spec):
    x=spec['profile'][0][0]
    style=spec['tail']
    if style=='ribbon':
        surface.fin([(x+.04,0,-.075),(x-.23,0,-.10),(x-.16,0,0),
                     (x-.23,0,.12),(x+.04,0,.065)],'Tail',.011)
    elif style=='round':
        points=[(x+.035,0,-.06)]
        points += [(x-.14-.19*math.cos(a),0,.235*math.sin(a)) for a in
                   [math.radians(i) for i in range(-85,86,17)]]
        points.append((x+.035,0,.06))
        surface.fin(points,'Tail',.025)
    else:
        height=.30 if style=='crescent' else .25
        surface.fin([(x+.04,0,-.045),(x-.24,0,-height),(x-.34,0,-height*1.10),
                     (x-.19,0,0),(x-.34,0,height*1.10),(x-.24,0,height),(x+.04,0,.045)],'Tail')


def add_fins(surface,spec):
    style=spec['fin']
    if style=='ribbon':
        # A continuous crest shares body weights instead of bending as a rigid sheet.
        profile=spec['profile']
        for a,b in zip(profile,profile[1:]):
            za=a[3]+a[2]*.91
            zb=b[3]+b[2]*.91
            surface.fin([(a[0],0,za),(b[0],0,zb),(b[0],0,zb+.085),
                         (a[0],0,za+.09)],lambda x:body_weights(spec,x),.012)
        for n in range(3):
            surface.tube([(.36-n*.055,0,.20),(.48-n*.05,0,.40+n*.04),
                          (.35-n*.10,0,.60+n*.025),(.05-n*.13,0,.66-n*.04)],
                         .010,'Head',3,5)
    else:
        start,end=.29,-.45
        if style=='small':
            start,end=-.18,-.38
        if style=='split':
            surface.fin([(.22,0,.19),(.13,0,.44),(-.06,0,.31),(-.15,0,.22)],'Dorsal')
            surface.fin([(-.24,0,.20),(-.31,0,.35),(-.46,0,.12)],'Dorsal')
        elif style in ('sail','sickle'):
            base1=radius_at(spec,start)[1]*.90
            base2=radius_at(spec,end)[1]*.90
            height=.73 if style=='sail' else .74
            surface.fin([(start,0,base1),(start-.05,0,height),(.02,0,height*.94),
                         (-.22,0,height*.71),(end,0,base2)],'Dorsal',.018)
        else:
            count=7 if style=='lion' else 6
            points=[(start,0,radius_at(spec,start)[1]*.88)]
            for i in range(count):
                x=start+(end-start)*(i+.2)/count
                base=radius_at(spec,x)[1]*.92
                peak=.32 if style=='lion' else (.17 if style in ('crown','scallop') else .22)
                if style=='small':
                    peak=.12
                points += [(x,0,base+peak*(1-.3*i/count)),
                           (x+(end-start)*.43/count,0,base+peak*.36)]
            points.append((end,0,radius_at(spec,end)[1]*.85))
            surface.fin(points,'Dorsal',.018)
        low=-radius_at(spec,-.02)[1]*.80
        if style=='sickle':
            surface.fin([(.12,0,low),(-.12,0,-.73),(-.28,0,-.51),(-.40,0,-.13)],'Ventral')
        else:
            surface.fin([(.04,0,low),(-.17,0,low-.16),(-.35,0,low+.07)],'Ventral')
    # Side fins have enough thickness to stay visible from above and either side.
    for sign,side in [(1,'L'),(-1,'R')]:
        ry=radius_at(spec,.08)[0]
        root=(.12,sign*ry*.84,-.05)
        if style=='lion':
            points=[root]
            for i in range(8):
                a=math.radians(-30+i*18)
                length=.60 if i%2==0 else .51
                points.append((.1-length*math.cos(a),sign*(ry+.23+math.sin(a)*.08),
                               -.05+length*math.sin(a)))
            surface.fin(points,'Pectoral_'+side,.018,normal=(0,1,.35*sign))
        else:
            reach=.16 if style=='small' else .24
            points=[root,(-.02,sign*(ry+reach),-.09),(-.26,sign*(ry+reach*.8),-.21),
                    (-.16,sign*(ry+.025),-.16)]
            surface.fin(points,'Pectoral_'+side,.021,normal=(.1,.35*sign,1))


def add_face(surface,spec):
    expression=spec['expression']
    head=spec['profile'][-1][0]
    eye_x=head*.59
    radius,width_z,zc=radius_at(spec,eye_x)
    eye_z=zc+width_z*.39
    eye_size=.10 if expression!='surprised' else .115
    if spec['id']=='fish_10':
        eye_size=.088
    for sign in (-1,1):
        y=sign*(radius*.87+.025)
        surface.ellipsoid((eye_x,y,eye_z),(eye_size,eye_size*.58,eye_size*1.1),'Head',6,12,6)
        surface.ellipsoid((eye_x+.004,y+sign*.024,eye_z+.003),
                          (eye_size*.87,eye_size*.49,eye_size*.94),'Head',7,12,6)
        pupil_x=eye_x+.022
        pupil_z=eye_z+(-.015 if expression in ('sleepy','grumpy') else .002)
        surface.ellipsoid((pupil_x,y+sign*.066,pupil_z),
                          (eye_size*.38,.018,eye_size*.51),'Head',8,10,5)
        surface.ellipsoid((pupil_x+.008,y+sign*.080,pupil_z+.023),
                          (.012,.008,.015),'Head',9,6,4)
        # An upper lid makes sleepy and hostile faces different at small sizes.
        if expression in ('sleepy','grumpy','angry','keen'):
            tilt=.045 if expression in ('angry','keen') else .016
            z=eye_z+(.043 if expression!='sleepy' else .015)
            surface.tube([(eye_x-.09,y+sign*.055,z+tilt),
                          (eye_x,y+sign*.078,z),
                          (eye_x+.085,y+sign*.049,z-tilt)],.023,'Head',2,6)
        # A short dark gill mark on each cheek, away from the eyeball.
        surface.tube([(eye_x-.10,sign*radius*.98,eye_z-.055),
                      (eye_x-.13,sign*radius*.99,eye_z-.13)],.009,'Head',14,5)
    size=spec.get('mouth',1)
    mouth_x=head+.022
    mouth_z=-.045 if expression!='surprised' else -.024
    width=.070*size
    height=.042*size
    if spec.get('teeth'):
        width=.18
        height=.14
    surface.ellipsoid((mouth_x+.009,0,mouth_z),(.025,width*.94,height*.92),'Head',6,12,6)
    for lower in (False,True):
        angles=[math.pi*i/8+(math.pi if lower else 0) for i in range(9)]
        points=[(mouth_x+.023,math.cos(a)*width,mouth_z+math.sin(a)*height) for a in angles]
        surface.tube(points,.023*size,'Jaw' if lower else 'Head',
                     4 if spec.get('beak') else 10,6)
    surface.ellipsoid((mouth_x-.005,0,mouth_z-height*.94),
                      (.085 if spec.get('teeth') else .06,width*.95,
                       .085 if spec.get('teeth') else .04*size),
                      'Jaw',1 if spec.get('teeth') else 0,12,6)
    if spec.get('beak'):
        surface.ellipsoid((mouth_x+.036,0,mouth_z+.037),(.058,.062,.041),'Head',4,10,6)
        surface.ellipsoid((mouth_x+.028,0,mouth_z-.042),(.044,.059,.028),'Jaw',4,10,6)
    if spec.get('teeth'):
        for lower in (False,True):
            for i in range(5):
                y=(i-2)*.066
                z=mouth_z+(-height*.80 if lower else height*.80)
                surface.cone((mouth_x+.042,y,z),
                    (mouth_x+.062,y,z+(.092 if lower else -.092)),.032,
                    'Jaw' if lower else 'Head',7,5)


def create_mesh(spec,collection,material,rig):
    surface=Surface()
    add_body(surface,spec)
    add_tail(surface,spec)
    add_fins(surface,spec)
    add_face(surface,spec)
    if spec.get('bill'):
        x=spec['profile'][-1][0]
        surface.cone((x-.015,0,.025),(x+.76,0,.06),.038,'Head',2,8)
    if spec.get('spikes'):
        for x in (-.27,-.10,.07,.24):
            ry,rz,zc=radius_at(spec,x)
            for j in range(8):
                a=math.tau*(j+.22)/8
                normal=Vector((.05,math.cos(a),math.sin(a)))
                base=Vector((x,ry*math.cos(a)*.98,zc+rz*math.sin(a)*.98))
                surface.cone(base,base+normal*.073,.023,body_weights(spec,x),4,5)
    obj=surface.object(spec['id']+'_Mesh',collection,material,rig)
    return obj
