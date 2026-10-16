"""对简单地图做静态检查：尺寸、通路、出生视线和 GLB 导出内容。

无需安装额外 Python 库。通路检查用角色半径扩张盒形障碍，保守地判断
角色是否能绕行；它不模拟跳跃、射击或实际游戏中的碰撞控制器。
"""
from collections import deque
import hashlib
import json
import math
from pathlib import Path
import struct

ROOT = Path(__file__).resolve().parents[1]
layout = json.loads((ROOT/'Data/yard_07.layout.json').read_text())
lo = layout['playable_bounds']['min']
hi = layout['playable_bounds']['max']
radius = layout['assumed_player']['radius']
height = layout['assumed_player']['standing_height']
eye = layout['assumed_player']['eye_height']
step = .25

# 地面本身不作为平面障碍；高度范围与站立角色相交的实体才参与检测。
blocks = []
for c in layout['colliders']:
    center, size = c['center'], c['size']
    assert all(math.isfinite(v) for v in center + size)
    assert all(v > 0 for v in size)
    low = [center[i]-size[i]/2 for i in range(3)]
    high = [center[i]+size[i]/2 for i in range(3)]
    if high[2] > .04 and low[2] < height:
        blocks.append((c['name'], low, high))


def free(x, y):
    if not lo[0]+radius < x < hi[0]-radius or not lo[1]+radius < y < hi[1]-radius:
        return False
    return not any(a[0]-radius <= x <= b[0]+radius and a[1]-radius <= y <= b[1]+radius for _,a,b in blocks)


def position(node):
    return lo[0]+(node[0]+.5)*step, lo[1]+(node[1]+.5)*step


def cell(point):
    return int((point[0]-lo[0])/step), int((point[1]-lo[1])/step)


nx,ny = (round((hi[i]-lo[i])/step) for i in range(2))
walkable = {(i,j) for i in range(nx) for j in range(ny) if free(*position((i,j)))}
start=cell(layout['spawns'][0]['position'])
assert start in walkable
queue=deque([start])
distance={start:0}
while queue:
    p=queue.popleft()
    for q in ((p[0]+1,p[1]),(p[0]-1,p[1]),(p[0],p[1]+1),(p[0],p[1]-1)):
        if q in walkable and q not in distance:
            distance[q]=distance[p]+1
            queue.append(q)

checks={}
for spawn in layout['spawns']:
    p=spawn['position']
    assert free(*p[:2]), f"Spawn intersects obstacle: {spawn['name']}"
    assert cell(p) in distance, f"Spawn disconnected: {spawn['name']}"
    checks[spawn['name']]=True
for name,p in layout['route_checkpoints'].items():
    assert free(*p[:2]) and cell(p) in distance, f'Route checkpoint disconnected: {name}'
    checks['reachable_'+name]=True
assert len(distance)==len(walkable), 'Walkable region contains disconnected pockets'


def segment_hits(a,b,low,high):
    # 线段与三维盒相交，用于检查两出生位置在站立视线高度的直线遮挡。
    near,far=0.,1.
    for axis in range(3):
        d=b[axis]-a[axis]
        if abs(d)<1e-9:
            if not low[axis] <= a[axis] <= high[axis]:
                return False
        else:
            t1,t2=(low[axis]-a[axis])/d,(high[axis]-a[axis])/d
            near=max(near,min(t1,t2))
            far=min(far,max(t1,t2))
            if near>far:
                return False
    return True


a=layout['spawns'][0]['position'][:2]+[eye]
b=layout['spawns'][1]['position'][:2]+[eye]
blockers=[name for name,low,high in blocks if segment_hits(a,b,low,high)]
assert blockers, 'Spawn points have direct line of sight'
checks['spawn_direct_view_blocked']=True

raw=(ROOT/'Exports/yard_07.glb').read_bytes()
magic,version,total=struct.unpack_from('<4sII',raw)
assert magic==b'glTF' and version==2 and total==len(raw)
length,kind=struct.unpack_from('<I4s',raw,12)
assert kind==b'JSON'
gltf=json.loads(raw[20:20+length])
node_names={node.get('name','') for node in gltf['nodes']}
assert {'Spawn_Team_A','Spawn_Team_B','Center_Reference'} <= node_names
assert not any(name.startswith(('COL_','Camera_','Presentation_','Studio_')) for name in node_names)
assert not gltf.get('cameras')
assert not gltf.get('images'), 'Map should have no external texture dependency'
triangles=sum(gltf['accessors'][p['indices']]['count']//3 for m in gltf['meshes'] for p in m['primitives'])
assert triangles < 25000, f'Unexpectedly large simple map: {triangles} triangles'
checks['glb_contains_game_markers_only']=True
checks['glb_has_no_external_textures']=True

files=['Blender/yard_07.blend','Exports/yard_07.glb','Exports/yard_07.fbx','Exports/yard_07_collision.fbx']
exports={}
for filename in files:
    path=ROOT/filename
    assert path.stat().st_size>100
    exports[filename]={'bytes':path.stat().st_size,'sha256':hashlib.sha256(path.read_bytes()).hexdigest()}
report={'result':'passed','checks':checks,'grid_cell_meters':step,'player_radius_meters':radius,
        'connected_walkable_cells':len(distance),'spawn_view_blockers':blockers,
        'glb_triangles':triangles,'files':exports,
        'limitations':['Static box/grid and export checks only. No engine collision or multiplayer playtest.']}
(ROOT/'Data/validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
print(json.dumps(report,ensure_ascii=False,indent=2))
