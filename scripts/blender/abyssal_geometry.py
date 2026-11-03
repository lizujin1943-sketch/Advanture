"""根据概念图制作完整体积与可独立断开的八腕，细节随蒙皮一起变形。"""
import math
import random
import bisect
import bpy
from mathutils import Vector,Matrix
from abyssal_common import *
from abyssal_face import EYES,MOUTH,head_profile,mantle_surface,mantle_normal,create_head,create_face


def arm_weights(key,t):
    """按骨头中心平滑混合相邻两节；吸盘整只用同一组权重，避免扭成薄片。"""
    edges=bone_edges(key);centers=[(a+b)*.5 for a,b in zip(edges,edges[1:])]
    pos=t*length(key);i=bisect.bisect_right(centers,pos)-1
    a=max(0,min(count(key)-1,i));b=max(0,min(count(key)-1,i+1))
    if a==b:return {bone_name(key,a):1.0}
    f=(pos-centers[a])/(centers[b]-centers[a])
    return {bone_name(key,a):1-f,bone_name(key,b):f}





def shard(b,center,scale,rotation,wt,mat=0,rng=None):
    rng=rng or random.Random(1);c=Vector(center)
    base=[];top=[];n=7
    for i in range(n):
        a=math.tau*i/n;f=rng.uniform(.8,1.12)
        base.append(b.vertex(c+rotation@Vector((math.cos(a)*scale[0]*f,math.sin(a)*scale[1]*f,-scale[2]*.35)),(i/n,0),wt))
        top.append(b.vertex(c+rotation@Vector((math.cos(a)*scale[0]*.67*f,math.sin(a)*scale[1]*.75*f,scale[2]*(.3+rng.random()*.3))),(i/n,1),wt))
    tip=b.vertex(c+rotation@Vector((scale[0]*.20,scale[1]*.1,scale[2])),(.5,.5),wt)
    b.face(tuple(reversed(base)),mat)
    for i in range(n):
        j=(i+1)%n;b.face((base[i],base[j],top[j],top[i]),mat);b.face((top[i],top[j],tip),mat if mat>1 or i%3 else 1)

def create_reef(collection,mats):
    rng=random.Random(401);b=MeshBuilder();wt={'DEF_Head':1}
    def patch(a,z,width,height,depth):
        """圆钝、风化的钙质薄壳贴在真实头冠截面上，不用尖晶体近似礁甲。"""
        nu=36;rows=[];rx,ry=head_profile(z)
        for j in range(7):
            f=.08+.92*j/6;row=[]
            for i in range(nu):
                theta=math.tau*i/nu
                irregular=1+.095*math.sin(5*theta+a*7)+.06*math.sin(9*theta+z)
                dz=height*f*math.sin(theta)*irregular
                aa=a+width/max(ry,1)*f*math.cos(theta)*irregular
                zz=z+dz;p=mantle_surface(aa,zz);normal=mantle_normal(aa,zz)
                h=depth*(.32+.68*(1-f*f))+.027*math.sin(theta*7+f*5)
                row.append(b.vertex(p+normal*h,(i/nu,f),wt))
            rows.append(row)
        b.face(rows[0])
        for aa,bb in zip(rows,rows[1:]):
            for i in range(nu):n=(i+1)%nu;b.face((aa[i],bb[i],bb[n],aa[n]))
        # 外缘进入皮肤，闭合薄壳的背面，保留独立可编辑的附着体。
        back=[]
        for vi in rows[-1]:
            p=Vector(b.vertices[vi]);q=p-mantle_normal(a,z)*(depth*.5+.08)
            back.append(b.vertex(q,(.5,.5),wt))
        for i in range(nu):n=(i+1)%nu;b.face((rows[-1][i],back[i],back[n],rows[-1][n]))
        b.face(tuple(reversed(back)))
    # 生物左侧(+X)不对称的大块礁甲；两侧不能复制成对称王冠。
    for j in range(24):
        z=rng.uniform(4.1,8.95);a=rng.uniform(-.48,.55)
        patch(a,z,rng.uniform(.32,.65),rng.uniform(.35,.70),rng.uniform(.15,.36))
    for j in range(10):
        z=5.4+j*.35
        patch(math.pi-.06,z,.25,.29,.12)
    # 冠顶有顺着后脑的嵴，枝状礁骨仍属于附着物。
    for j in range(3):
        a=.05+j*.10;z=8.8-j*.48;c=mantle_surface(a,z);n=mantle_normal(a,z)
        points=[c+n*(.1+t*.44)+Vector((.015*math.sin(t*2+j),t*.14,t*.22)) for t in [k/8 for k in range(9)]]
        b.tube(points,[.15*(1-k/9)**.8+.015 for k in range(9)],weights=wt,sides=18)
    for a,z,r in [(-.39,7.8,.43),(-.21,6.7,.34),(-.27,8.6,.35),(-.22,5.7,.25)]:
        n=mantle_normal(a,z);c=mantle_surface(a,z)+n*.14
        b.cup(c,n,r,weights=wt,segments=28)
    for j in range(70):
        z=rng.uniform(3.8,8.3);a=rng.uniform(-.6,.8)
        n=mantle_normal(a,z);c=mantle_surface(a,z)+n*.02
        b.cup(c,n,rng.uniform(.08,.21),weights=wt,segments=24)
    return b.build('AW_Reef',collection,[mats['Reef'],mats['ReefDark']],True)

def create_arms(collection,mats):
    result=[]
    for key in ARMS:
        b=MeshBuilder();d=direction(key);s=side(key);up=Vector((0,0,1));r0=root(key)
        n=144 if key in FRONT else 108;sides=24;rows=[]
        for j in range(n+1):
            t=j/n;r=radius(t);c=r0+d*length(key)*t;row=[]
            for k in range(sides):
                a=math.tau*k/sides
                rib=1+.022*math.sin(j*.53+math.cos(a)*3)
                p=c+r*rib*(s*math.cos(a)+up*math.sin(a)*.90)
                row.append(b.vertex(p,(k/sides,t*3),arm_weights(key,t)))
            rows.append(row)
        for j,(aa,bb) in enumerate(zip(rows,rows[1:])):
            for k in range(sides):
                kk=(k+1)%sides;b.face((aa[k],aa[kk],bb[kk],bb[k]),1 if k>sides//2 else 0)
        # 真正的双侧封口：近端暗肉盘和末梢闭合，断肢不会露出背面。
        b.face(tuple(reversed(rows[0])),3);b.face(rows[-1],0)
        cups=48 if key in FRONT else 36
        for j in range(cups):
            t=.035+.925*j/(cups-1);r=radius(t);c=r0+d*length(key)*t
            for sign in [-1,1]:
                normal=(s*sign*.48-up*.88).normalized()
                b.cup(c+normal*r*.91,normal,r*(.38 if j<30 else .32),2,arm_weights(key,t),14)
        # 沿外脊的小型不规则礁片和藤壶簇，不把所有皮肤变成岩石。
        rng=random.Random(800+list(ARMS).index(key))
        for j in range(20):
            t=.08+j*.041;r=radius(t);c=r0+d*length(key)*t+up*r*.80
            shard(b,c,(r*.57,r*.7,r*.30),Matrix.Rotation(math.radians(-ARMS[key]),3,'Z'),arm_weights(key,t),4,rng)
        for t in [.15,.31,.55]:
            r=radius(t)
            for j in range(5):
                c=r0+d*(length(key)*t+j*.13)+s*r*.60+up*r*.63
                b.cup(c,(s*.6+up*.7),r*.15,4,arm_weights(key,t),9)
        obj=b.build('AW_Arm_'+key,collection,[mats['Skin'],mats['Belly'],mats['Cup'],mats['Scar'],mats['Reef']])
        obj['limb_id']=key;obj['role']='attack' if key in FRONT else 'support';result.append(obj)
        # 本体残端仅由躯干骨驱动，与断开的腕骨没有共享顶点。
        b=MeshBuilder();b.tube([r0-d*.3,r0+d*.08],[.94,.89],weights={'DEF_Body':1},sides=24)
        result.append(b.build('AW_Stump_'+key,collection,[mats['Scar']]))
        # 单腕独立薄膜：不跨接下一条触手，边缘沿同一腕渐变权重。
        b=MeshBuilder()
        for sign in [-1,1]:
            rows=[]
            for j in range(28):
                t=.015+j*.011;r=radius(t);c=r0+d*length(key)*t
                width=(1-t/.34)*.55*(.55+.45*abs(math.cos(j*.62)))
                rows.append([b.vertex(c+s*sign*(r*.85+width*q)-up*(.14+q*.24),(q,t*3),arm_weights(key,t)) for q in [0,1]])
            for aa,bb in zip(rows,rows[1:]):b.face((aa[0],bb[0],bb[1],aa[1]))
        membrane=b.build('AW_Membrane_'+key,collection,[mats['Membrane']]);membrane['limb_id']=key
        solid=membrane.modifiers.new('Membrane thickness','SOLIDIFY');solid.thickness=.025
        result.append(membrane)
    return result

def build_geometry(collection,mats):
    return [create_head(collection,mats)]+create_face(collection,mats)+[create_reef(collection,mats)]+create_arms(collection,mats)
