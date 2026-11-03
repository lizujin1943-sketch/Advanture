"""BOSS 动作编排。这里的秒数是美术时间，不参与游戏伤害或帧同步结算。"""
import math
from mathutils import Vector, Quaternion, Matrix
from abyssal_common import ARMS, FRONT, count, bone_name, direction
from abyssal_pose import idle_path, sample_curve, world_path, set_arm as write_arm_pose

REFERENCE={k:sample_curve(k,idle_path(k)) for k in ARMS}

REFERENCE_TANGENTS={k:[(b-a).normalized() for a,b in zip(p,p[1:])] for k,p in REFERENCE.items()}
REFERENCE_FRAMES={k:[d.to_track_quat('Y','Z') for d in dirs] for k,dirs in REFERENCE_TANGENTS.items()}

def animation_rotations(key,points):
    """每节从已确认的切线与吸盘朝向做最小转动，避免每帧重新对齐世界 Z。

    该计算不依赖上一帧，循环、随机跳帧和反向播放一致。若以后动作让某节
    完全反向，需要作者给中间姿态来确定旋转轴，不能让数学库随意选翻腕方向。
    """
    result=[]
    for index,(a,b) in enumerate(zip(points,points[1:])):
        target=(b-a).normalized();reference=REFERENCE_TANGENTS[key][index]
        if reference.dot(target)<-.99999:
            raise ValueError(f'{key} 第 {index} 节接近完全反向，需补充中间姿态')
        result.append(reference.rotation_difference(target)@REFERENCE_FRAMES[key][index])
    return result

def set_arm(rig,key,points):
    write_arm_pose(rig,key,points,animation_rotations(key,points))

def clamp(x):return max(0.,min(1.,float(x)))
def ease(x):
    x=clamp(x);return x*x*(3-2*x)

def blend(a,b,w):
    """逐节转动方向并保持节长，避免卷曲姿态与舒展姿态直线混合时整腕缩短。"""
    w=clamp(w);result=[a[0].lerp(b[0],w)]
    for aa,ab,ba,bb in zip(a,a[1:],b,b[1:]):
        da=ab-aa;db=bb-ba
        turn=da.rotation_difference(db)
        rotation=Quaternion().slerp(turn,w)
        d=rotation@da.normalized()
        result.append(result[-1]+d*(da.length*(1-w)+db.length*w))
    return result

WINDUP={k:sample_curve(k,world_path(k,[(2.15,1.85,0),(4,4,0),(7,7,0),
    (10,10,0),(13,12,.1),(16.7,13.2,.2),(18.6,12.3,.1),(19.8,10,0)])) for k in FRONT}
IMPACT={k:sample_curve(k,world_path(k,[(2.15,1.85,0),(5,1.5,0),(9,1.1,0),
    (12,1.05,0),(15,1.0,0),(18,.95,0),(20.2,.75,0),(21.4,.55,0)])) for k in FRONT}
SWEEP={k:sample_curve(k,world_path(k,[(2.15,1.85,0),(5,2,0),(9,1.5,.5),
    (12,1.2,1),(16,1.05,2),(19,1,2.5),(20.5,.9,1),(20,1,-1.5),
    (18.3,1.2,-2),(17.3,1.4,-1)])) for k in FRONT}

def rotate_arm(points,angle):
    q=Quaternion((0,0,1),angle);anchor=points[0]
    return [anchor+q@(p-anchor) for p in points]

def slam(key,t):
    if t<1.2:return blend(REFERENCE[key],WINDUP[key],ease(t/1.2))
    if t<1.5:return blend(WINDUP[key],IMPACT[key],clamp((t-1.2)/.3)**2)
    if t<1.8:return IMPACT[key]
    return blend(IMPACT[key],REFERENCE[key],ease((t-1.8)/1.2))

def sweep(key,t):
    if t<1.2:return blend(REFERENCE[key],rotate_arm(SWEEP[key],-.35),ease(t/1.2))
    if t<2:return rotate_arm(SWEEP[key],-.35+.70*ease((t-1.2)/.8))
    return blend(rotate_arm(SWEEP[key],.35),REFERENCE[key],ease((t-2)/1.6))

def sever_pose(key):
    return [p+Vector((0,0,.22*math.sin(math.pi*i/(len(REFERENCE[key])-1))))
            for i,p in enumerate(REFERENCE[key])]

def idle(key,t,duration=6,intensity=1):
    phase=math.tau*t/duration;index=list(ARMS).index(key);result=[]
    for i,p in enumerate(REFERENCE[key]):
        f=i/(len(REFERENCE[key])-1)
        result.append(p+Vector((math.sin(phase+f*2+index)-math.sin(f*2+index),
            math.sin(phase+index*.8)-math.sin(index*.8),
            math.sin(phase+index*.5+f*3)-math.sin(index*.5+f*3)))*(.13*f*intensity))
    return result

def pulse(t,center,width):return ease(1-abs(t-center)/width)

def defaults(rig):
    """只重置表现控制，不覆盖 present_*（触手是否仍存在）。"""
    for n in ('ROOT','DEF_Head','DEF_Body'):rig.pose.bones[n].matrix_basis=Matrix.Identity(4)
    # ID 自定义属性会保留赋值类型。连续控制必须始终使用 float，否则动画会被量化为开关。
    for p,v in [('eye_glow',1.),('mouth_open',.08),('ink_fire',0.),('blink_Center',0.),('blink_L',0.),('blink_R',0.)]:rig[p]=v

def head(rig,nod=0,yaw=0,roll=0,breath=0):
    p=rig.pose.bones['DEF_Head']
    # 该骨骼局部 Y 对应世界 Z；局部 X 为点头轴。
    p.rotation_quaternion=Quaternion((1,0,0),nod)@Quaternion((0,1,0),yaw)@Quaternion((0,0,1),roll)
    p.scale=(1+breath,1+breath*.6,1+breath)

def root_height(rig,z):
    p=rig.pose.bones['ROOT'];p.location=p.bone.matrix_local.to_3x3().inverted()@Vector((0,0,z))

def apply_motion(rig,kind,t,key=None):
    """给定动作时间写入所属分区，单腕动作不写头部和其他触手。"""
    if kind in ('Slam','Sweep'):
        set_arm(rig,key,(slam if kind=='Slam' else sweep)(key,t));return
    if kind=='Sever':
        set_arm(rig,key,blend(REFERENCE[key],sever_pose(key),ease(t/.3)))
        rig['sever_'+key]=int(t>=.3);return
    defaults(rig)
    if kind in ('Idle','EnragedIdle'):
        duration=6 if kind=='Idle' else 5;power=1 if kind=='Idle' else 1.65
        phase=math.tau*t/duration
        head(rig,.018*math.sin(phase)*power,.025*math.sin(phase)*power,0,.012*math.sin(phase)*power)
        for k in ARMS:set_arm(rig,k,idle(k,t,duration,power))
        for eye,offset in [('Center',0),('L',.08),('R',.13)]:rig['blink_'+eye]=pulse(t,duration*.42+offset,.17)
        rig['eye_glow']=1. if kind=='Idle' else 2.6
    elif kind=='Emerge':
        f=ease(t/4);root_height(rig,-13*(1-f))
        head(rig,.08*(1-f));rig['eye_glow']=ease((t-1.8)/1.6)
        for k in ARMS:
            folded=[p+Vector((0,0,-1.7*i/(len(REFERENCE[k])-1))) for i,p in enumerate(REFERENCE[k])]
            set_arm(rig,k,blend(folded,REFERENCE[k],f))
    elif kind=='Ink':
        opening=ease(t/.95)*(1-ease((t-1.9)/1.3))
        head(rig,-.055*opening+.065*pulse(t,1.45,.40))
        rig['mouth_open']=.08+.92*opening
        rig['ink_fire']=ease((t-1.2)/.12)*(1-ease((t-1.65)/.15))
        rig['eye_glow']=1+.7*opening
    elif kind=='Hit':
        hit=pulse(t,.18,.18);head(rig,-.055*hit,0,.015*hit)
        rig['blink_Center']=hit*.65
    elif kind=='Enrage':
        e=ease(t/3);shudder=math.sin(t*18)*math.sin(math.pi*clamp(t/3))
        head(rig,-.055*math.sin(math.pi*clamp(t/3)),0,.018*shudder)
        rig['eye_glow']=1+1.6*e;rig['mouth_open']=.08+.45*math.sin(math.pi*clamp(t/3))
        for k in ARMS:
            pts=idle(k,t,3,1.7)
            if k in FRONT:pts=[p+Vector((0,0,.7*math.sin(math.pi*clamp(t/3))*i/(len(pts)-1))) for i,p in enumerate(pts)]
            set_arm(rig,k,pts)
    elif kind=='Death':
        collapse=ease(t/2.5);root_height(rig,-14*ease((t-2.3)/3.7))
        head(rig,.22*collapse,0,.07*collapse)
        rig['eye_glow']=1-ease(t/1.2)
        for eye in ('Center','L','R'):rig['blink_'+eye]=ease(t/1.7)
        for k in ARMS:
            relaxed=[Vector((p.x,p.y,p.z*(1-.85*i/(len(REFERENCE[k])-1)))) for i,p in enumerate(REFERENCE[k])]
            set_arm(rig,k,blend(REFERENCE[k],relaxed,collapse))
    elif kind=='Combination':
        apply_motion(rig,'Ink',clamp(t/3.6)*3.2)
        set_arm(rig,'A01',slam('A01',min(3,t)))
        set_arm(rig,'A03',sweep('A03',t))

def registry():
    """关键时刻用 Blender 帧标记；JSON 同时写秒数，避免误作游戏逻辑帧。"""
    result={}
    def add(kind,seconds,key=None,events=(),loop=False):
        name='AW_'+kind+('_'+key if key else '')
        result[name]={'kind':kind,'limb':key,'frame_start':1,'frame_end':round(seconds*30)+1,
            'duration_seconds':seconds,'loop':loop,
            'events':[{'name':n,'frame':round(t*30)+1,'seconds':t} for n,t in events]}
    add('Emerge',4,events=[('emerge_complete',4)])
    add('Idle',6,loop=True)
    for k in FRONT:
        add('Slam',3,k,[('telegraph',0),('strike_start',1.2),('impact',1.5),('recover',1.8)])
        add('Sweep',3.6,k,[('telegraph',0),('sweep_start',1.2),('sweep_end',2)])
    add('Ink',3.2,events=[('ink_start',1.2),('ink_end',1.8)])
    add('Hit',1);add('Enrage',3,events=[('enraged',3)]);add('EnragedIdle',5,loop=True)
    for k in ARMS:add('Sever',2.6,k,[('detach',.3)])
    add('Death',6,events=[('eyes_off',1.2),('sink_start',2.3),('submerged',6)])
    add('Combination',3.6)
    return result

FACE_PROPERTIES=['eye_glow','mouth_open','ink_fire','blink_Center','blink_L','blink_R']

def owners(data):
    kind=data['kind'];key=data.get('limb')
    if kind in ('Slam','Sweep','Sever'):
        return [bone_name(key,i) for i in range(count(key))],(['sever_'+key] if kind=='Sever' else [])
    bones=['DEF_Head','ROOT']
    if kind not in ('Ink','Hit'):
        keys=('A01','A03') if kind=='Combination' else ARMS
        bones += [bone_name(k,i) for k in keys for i in range(count(k))]
    return bones,FACE_PROPERTIES
