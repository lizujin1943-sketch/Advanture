"""乌贼轮廓的有机头部：后倾长囊、嵌入式眼窝与连续口周。

此模块定义同一张头部表面的高度函数。眼睑和唇口的外缘使用相同函数，
避免各自按一个椭球摆放造成悬空眼圈、硬眉甲和口器拼接缝。
"""
import math
import bpy
from mathutils import Vector, Matrix, noise
from abyssal_common import MeshBuilder
from abyssal_mouth import create_mouth

EYE_LAYOUT={'Center':(0,6.12,.40,.94,0),
            'L':(2.03,5.53,.53,.16,-.24),
            'R':(-2.03,5.53,.51,.17,.24)}

def head_profile(z):
    # 头冠模拟后倾、侧扁的外套膜；最大宽度下移，向后上方连续收细。
    # 不是把球体拉高：截面中心沿纵轴后移，宽度与厚度也分别控制。
    levels=[(-.6,.02,.02),(.2,1.65,1.30),(1.8,2.55,1.85),(3.4,3.20,2.24),
            (5.2,3.36,2.30),(6.4,3.55,2.40),(7.25,3.36,2.55),
            (8.30,2.76,2.10),(9.10,1.85,1.35),(9.62,.95,.70),
            (9.90,.39,.28),(10,.02,.02)]
    z=max(levels[0][0],min(levels[-1][0],z))
    i=next((j for j in range(len(levels)-1) if levels[j][0]<=z<=levels[j+1][0]),len(levels)-2)
    a,b=levels[i],levels[i+1];prev=levels[max(0,i-1)];nxt=levels[min(len(levels)-1,i+2)]
    t=(z-a[0])/(b[0]-a[0]);dt=b[0]-a[0]
    def channel(k):
        m0=(b[k]-prev[k])/(b[0]-prev[0]);m1=(nxt[k]-a[k])/(nxt[0]-a[0])
        return max(.015,(2*t**3-3*t*t+1)*a[k]+(t**3-2*t*t+t)*m0*dt+(-2*t**3+3*t*t)*b[k]+(t**3-t*t)*m1*dt)
    return channel(1),channel(2)

def center_at(z):
    q=max(0,min(1,(z-4.7)/5.3));q=q*q/(.55+.45*q)
    return -.42*q,.12+5.0*q

def mantle_surface(a,z):
    """附件与头冠共用截面，避免头型调整后礁壳留在旧椭球上而悬空。"""
    rx,ry=head_profile(z);cx,cy=center_at(z);p=section_power(z)
    co=math.cos(a);si=math.sin(a)
    x=cx+rx*math.copysign(abs(co)**(2/p),co)
    y=cy+ry*math.copysign(abs(si)**(2/p),si)
    if si<0:
        frontal=max(0,min(1,(abs(si)-.38)/.34));frontal=frontal*frontal*(3-2*frontal)
        y=y*(1-frontal)+front_surface(x,z)*frontal
    return Vector((x,y,z))

def mantle_normal(a,z):
    across=mantle_surface(a+.005,z)-mantle_surface(a-.005,z)
    up=mantle_surface(a,z+.005)-mantle_surface(a,z-.005)
    return across.cross(up).normalized()

def section_power(z):return 2.0+.35*gaussian(z-6.8,2.0)

def base_surface(x,z):
    rx,ry=head_profile(z);cx,cy=center_at(z)
    p=section_power(z)
    return cy-ry*max(.001,1-abs((x-cx)/max(rx,.01))**p)**(1/p)

def gaussian(v,width):return math.exp(-(v/width)**2)

def make_fold(points,width,height):
    """将造型控制点采样成连续软褶；厚褶与一侧的窄沟共同形成组织厚度。"""
    dense=[]
    for j in range(len(points)-1):
        p0=Vector(points[max(0,j-1)]);p1=Vector(points[j])
        p2=Vector(points[j+1]);p3=Vector(points[min(len(points)-1,j+2)])
        for k in range(8):
            t=k/8
            q=.5*((2*p1)+(-p0+p2)*t+(2*p0-5*p1+4*p2-p3)*t*t+(-p0+3*p1-3*p2+p3)*t*t*t)
            dense.append(q)
    dense.append(Vector(points[-1]));segments=[]
    for j,(a,b) in enumerate(zip(dense,dense[1:])):
        d=b-a
        segments.append((a.x,a.y,d.x,d.y,1/max(d.length_squared,1e-9),j/(len(dense)-1)))
    xs=[p[0] for p in points];zs=[p[1] for p in points]
    return segments,width,height,(min(xs)-width*3,max(xs)+width*3,min(zs)-width*3,max(zs)+width*3)

# 顺着外套膜纵轴与口周的组织走向塑形，移除人脸式横向怒眉和额头分区。
# 这几组大褶决定体积，细纹与斑点不能代替它们。
FOLDS=[]
for side in [-1,1]:
    def path(points):
        return [(side*x+(.10 if side<0 else 0)*math.sin(z),z+(.07 if side>0 else -.06)*math.sin(x*2)) for x,z in points]
    for points,w,h in [
        ([(.22,9.65),(.56,8.6),(.69,7.5),(.54,6.7)],.24,.12),
        ([(.75,9.35),(1.38,8.30),(1.76,7.3),(1.73,6.1)],.33,.14),
        ([(1.52,8.9),(2.22,7.85),(2.73,6.7),(2.55,5.93)],.29,.12),
        ([(3.12,5.90),(2.80,5.13),(2.22,4.60),(1.45,4.08)],.27,.19),
        ([(1.67,5.18),(1.36,4.64),(1.20,3.96),(1.34,3.24)],.19,.16),
        ([(2.73,4.37),(2.35,3.65),(1.9,2.97),(1.67,2.3)],.26,.16),
    ]:
        FOLDS.append(make_fold(path(points),w,h*(1 if side>0 else .88)))

def fold_relief(x,z):
    relief=0.0
    for segments,width,height,bounds in FOLDS:
        if not bounds[0]<x<bounds[1] or not bounds[2]<z<bounds[3]:continue
        best=1e10;signed=0;along=0
        for ax,az,dx,dz,inv,t0 in segments:
            px=x-ax;pz=z-az;t=max(0,min(1,(px*dx+pz*dz)*inv))
            qx=px-t*dx;qz=pz-t*dz;distance=qx*qx+qz*qz
            if distance<best:
                best=distance;signed=math.copysign(math.sqrt(distance),dx*qz-dz*qx)
                along=t0+t/len(segments)
        taper=math.sin(math.pi*max(0,min(1,along)))**.50
        relief-=height*math.exp(-best/(width*width))*taper
        relief+=height*.45*gaussian(signed-width*.92,width*.25)*taper
    return relief

def mouth_metric(x,z):
    a=math.atan2((z-3.4)/1.10,x/.98)
    width=.98*(1-.12*math.sin(a)+.035*math.sin(3*a+.4))
    return math.sqrt((x/width)**2+((z-3.40)/1.10)**2),a

def front_surface(x,z):
    """大形、眉部软组织与眼/口的凹陷都在头冠本体内形成。"""
    y=base_surface(x,z)
    # 由额部向中央眼汇聚的两条软组织隆起与浅中缝，没有外贴的硬质眉条。
    y-=.08*gaussian(abs(x)-(.66+.12*(z-6.6)),.32)*gaussian(z-7.4,1.9)
    y+=.035*gaussian(x,.22)*gaussian(z-8.2,1.0)
    # 纵向浅褶沿着长囊流动，眼区通过厚皮肤自然过渡。
    y+=fold_relief(x,z)
    y+=.045*gaussian(x+1.65+.32*(z-6.8),.26)*gaussian(z-7.7,1.5)
    y+=.09*gaussian(abs(x)-1.44,.24)*gaussian(z-4.65,.65)
    for ex,ez,w,h,angle in EYE_LAYOUT.values():
        dx=x-ex;dz=z-ez;c=math.cos(angle);s=math.sin(angle)
        u=c*dx-s*dz;v=s*dx+c*dz
        a=w;b=h
        r=math.sqrt((u/a)**2+(v/b)**2)
        y+=.60*math.exp(-(r/.77)**8)
        y-=.105*math.exp(-((r-1.05)/.24)**2)
        # 侧眼上方是厚软组织，不让左右小眼像贴上去的发光饰件。
        if w>h:y-=.07*gaussian(u,w*1.35)*gaussian(v-.31,.22)
    m,a=mouth_metric(x,z)
    y-=.115*math.exp(-((m-1.035)/.19)**2)*(1+.12*math.sin(a*5))
    if m<1:
        t=min(1,(1-m)/.78);fade=t*t*(3-2*t)
        # 喉底退入头部内部；深度不是用一张黑圆片伪造。
        y=y*(1-fade)+.60*fade
        y+=.10*math.sin(12*a+.5*fade)*math.sin(math.pi*fade)*m
    return y

EYES={k:(Vector((x,base_surface(x,z)-.015,z)),w,h,a)
      for k,(x,z,w,h,a) in EYE_LAYOUT.items()}
MOUTH=Vector((0,base_surface(0,3.4)-.04,3.4))

def create_head(collection,mats):
    b=MeshBuilder();nu=512;nv=384;rings=[]
    for j in range(1,nv):
        phi=math.pi*j/nv;z=4.7+5.3*math.cos(phi);rx,ry=head_profile(z);cx,cy=center_at(z)
        row=[]
        for i in range(nu):
            a=math.tau*i/nu
            # 圆滑的大体积叠加柔软纵褶，避免把球体的经纬网格当作低模刻面。
            pole_fade=min(1,rx/.5,ry/.5)
            mantle=(.030*math.sin(7*a+z*.42)+.016*math.sin(13*a-z*.6))*pole_fade
            micro=0
            p=section_power(z);co=math.cos(a);si=math.sin(a)
            x=cx+(rx+mantle+micro)*math.copysign(abs(co)**(2/p),co)
            y=cy+(ry+mantle+micro)*math.copysign(abs(si)**(2/p),si)
            if math.sin(a)<0:
                frontal=max(0,min(1,(abs(math.sin(a))-.38)/.34))
                frontal=frontal*frontal*(3-2*frontal)
                y=y*(1-frontal)+front_surface(x,z)*frontal
            # 无规则细起伏替代整齐的斜向波纹，近看才读出皮肤厚度。
            tissue=noise.noise(Vector((x*1.2,y*1.2,z*1.2)))*.046
            tissue+=noise.noise(Vector((x*3.5,y*3.5,z*3.5)))*.014
            tissue+=noise.noise(Vector((x*12,y*12,z*12)))*.003
            x+=tissue*math.cos(a)*pole_fade;y+=tissue*math.sin(a)*pole_fade
            h=min(1,max(0,(z-1.5)/2.5))
            row.append(b.vertex((x,y,z),(i/nu,j/nv),{'DEF_Head':h,'DEF_Body':1-h}))
        rings.append(row)
    cavity_faces=[]
    for aa,bb in zip(rings,rings[1:]):
        for i in range(nu):
            n=(i+1)%nu;ids=(aa[i],bb[i],bb[n],aa[n])
            center=sum((Vector(b.vertices[k]) for k in ids),Vector())*.25
            m,_=mouth_metric(center.x,center.z)
            inside=i>=nu//2 and m<.94
            b.face(ids,2 if inside and m<.43 else 1 if inside else 0)
            if inside:cavity_faces.append(len(b.faces)-1)
    cx,cy=center_at(10)
    top=b.vertex((cx,cy,10),(.5,0),{'DEF_Head':1})
    bottom=b.vertex((0,.26,-.6),(.5,1),{'DEF_Body':1})
    for i in range(nu):
        n=(i+1)%nu;b.face((top,rings[0][i],rings[0][n]));b.face((bottom,rings[-1][n],rings[-1][i]))
    # 先计算整张连续表面的平滑法线，再按口腔材质拆成两个可编辑部件。
    # 两件在共同边界使用完全相同的坐标与法线，不额外叠加眼圈和口环。
    materials=[mats['FaceSkin']]*3
    full=b.build('AW_SurfaceWork',collection,materials,True)
    normals=[v.normal.copy() for v in full.data.vertices]
    cavity_set=set(cavity_faces)
    def extract(name,indices):
        part=MeshBuilder();remap={};source_ids=[]
        for fi in indices:
            ids=[]
            for vi in b.faces[fi]:
                if vi not in remap:
                    remap[vi]=part.vertex(b.vertices[vi],b.uv[vi],b.weights[vi]);source_ids.append(vi)
                ids.append(remap[vi])
            part.face(ids,b.face_mats[fi])
        obj=part.build(name,collection,materials,True)
        obj.data.normals_split_custom_set_from_vertices([normals[i] for i in source_ids])
        attribute=obj.data.color_attributes.new(name='TissueWeight',type='FLOAT_COLOR',domain='POINT')
        colors=[]
        for v,source_id in zip(obj.data.vertices,source_ids):
            m,_=mouth_metric(v.co.x,v.co.z)
            front=source_id<len(rings)*nu and source_id%nu>=nu//2
            blend=max(0,min(1,(.99-m)/.28)) if front else 0
            throat=max(0,min(1,(.65-m)/.38)) if front else 0
            colors.extend((blend*blend*(3-2*blend),throat*throat*(3-2*throat),0,1))
        attribute.data.foreach_set('color',colors)
        return obj
    obj=extract('AW_Head',[i for i in range(len(b.faces)) if i not in cavity_set])
    extract('AW_MouthCavity',cavity_faces)
    mesh=full.data;bpy.data.objects.remove(full,do_unlink=True);bpy.data.meshes.remove(mesh)
    obj['design_style']='organic_high_detail';obj['surface_role']='cuttlefish_inspired_swept_mantle'
    return obj

def aperture(a,w,h):
    """中央竖向、侧眼横向的杏仁形开口，角点过渡圆滑。"""
    if h>w:return Vector((w*math.sin(a)*abs(math.sin(a))**.35,0,h*math.cos(a)))
    return Vector((w*math.cos(a),0,h*math.sin(a)*abs(math.sin(a))**.45))

def create_eye(name,collection,mats):
    c,w,h,angle=EYES[name];rot=Matrix.Rotation(angle,3,'Y');parts=[];segments=128
    b=MeshBuilder();rows=[];wt={f'DEF_Eye_{name}':1}
    def eye_face(ids):
        # 竖眼轮廓与横眼轮廓的参数绕序相反，统一让角膜正面朝 -Y。
        b.face(tuple(reversed(ids)) if h>w else ids)
    for j in range(33):
        f=1-j/33;row=[]
        for i in range(segments):
            a=math.tau*i/segments;q=aperture(a,w,h)*f
            p=c+rot@q
            # 眼面跟随新的后倾头冠；固定在竖直平面会让眼睛上端浮出皮肤。
            p.y=base_surface(p.x,p.z)-.025-.16*(1-f*f)
            # UV 对应眼球局部平面；虹膜、瞳孔在角膜下，不再用黑色小球冒充瞳孔。
            row.append(b.vertex(p,(.5+.49*q.x/w,.5+.49*q.z/h),wt))
        rows.append(row)
    for aa,bb in zip(rows,rows[1:]):
        for i in range(segments):n=(i+1)%segments;eye_face((aa[i],aa[n],bb[n],bb[i]))
    center=b.vertex(c+Vector((0,-.16,0)),(.5,.5),wt)
    for i in range(segments):eye_face((rows[-1][i],rows[-1][(i+1)%segments],center))
    eye_face(tuple(reversed(rows[0])))
    parts.append(b.build('AW_Eye_'+name,collection,[mats['Eye']]))

    return parts

def create_face(collection,mats):
    objects=[bpy.data.objects['AW_MouthCavity']]
    for name in EYES:objects.extend(create_eye(name,collection,mats))
    objects.extend(create_mouth(collection,mats,MOUTH,front_surface))
    objects.extend(create_mantle_fringe(collection,mats))
    return objects

def create_mantle_fringe(collection,mats):
    """沿外套膜两侧的一圈窄软缘，帮助侧面读出乌贼轮廓；不是新增翅膀。"""
    objects=[]
    for sign,key in [(-1,'R'),(1,'L')]:
        b=MeshBuilder();rows=[];a=0 if sign>0 else math.pi
        for j in range(97):
            t=j/96;z=5.75+4.08*t;c=mantle_surface(a,z);row=[]
            width=.04+.34*math.sin(math.pi*t)**.65
            for k in range(9):
                u=k/8
                p=c+Vector((sign*(u*width-.035),.02-u*.10,u*(.055*math.sin(t*47)+.025*math.sin(t*101))))
                row.append(b.vertex(p,(u,t),{'DEF_Head':1}))
            rows.append(row)
        for aa,bb in zip(rows,rows[1:]):
            for k in range(8):b.face((aa[k],aa[k+1],bb[k+1],bb[k]))
        obj=b.build('AW_MantleFringe_'+key,collection,[mats['Skin']])
        solid=obj.modifiers.new('Soft mantle edge thickness','SOLIDIFY');solid.thickness=.045
        objects.append(obj)
    return objects
