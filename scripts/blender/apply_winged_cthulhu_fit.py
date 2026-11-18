"""Apply the recorded smooth silhouette correction to genuine 3D surfaces, once only."""
from pathlib import Path
import bpy
import numpy as np

ROOT=Path(__file__).resolve().parents[2]
ASSET=ROOT/'source-assets/creatures/winged-cthulhu'
root=bpy.data.objects['CR_WingedCthulhu']
if root.get('silhouette_fit_applied'):raise RuntimeError('Fit already applied; rebuild the base source first')
data=np.load(ASSET/'review/silhouette-fit-field.npz');u=data['u'];v=data['v']

def sample(a,x,y):
    h,w=a.shape;x=np.clip(x,0,w-1.001);y=np.clip(y,0,h-1.001)
    ix=x.astype(int);iy=y.astype(int);fx=x-ix;fy=y-iy
    return a[iy,ix]*(1-fx)*(1-fy)+a[iy,ix+1]*fx*(1-fy)+a[iy+1,ix]*(1-fx)*fy+a[iy+1,ix+1]*fx*fy

for ob in bpy.data.collections['HIGH'].objects:
    if ob.type!='MESH':continue
    co=np.empty(len(ob.data.vertices)*3,dtype=np.float32);ob.data.vertices.foreach_get('co',co);co=co.reshape(-1,3)
    # Authoring meshes have identity transforms. Camera is orthographic, width=15.6 m.
    assert all(abs(a-1)<1e-6 for a in ob.scale) and ob.location.length<1e-6
    px=(co[:,0]/15.6+.5)*768;py=(.5-(co[:,2]-5.12)/10.4)*512
    # The frontal silhouette field describes wings, not the paired facial anatomy.
    w=np.clip((np.abs(co[:,0])-2.25)/.9,0,1)*np.clip((co[:,1]-.2)/.36,0,1)
    w=w*w*(3-2*w)
    co[:,0]+=w*sample(u,px,py)*15.6/1536
    co[:,2]-=w*sample(v,px,py)*10.4/1024
    ob.data.vertices.foreach_set('co',co.ravel());ob.data.update()
root['silhouette_fit_applied']=True
root['silhouette_fit_method']='Smooth X/Z proportional edit using reference alpha; Y depth retained'
bpy.ops.wm.save_as_mainfile(filepath=str(ASSET/'CR_WingedCthulhu_master.blend'))
print('Applied recorded non-folding silhouette fit',flush=True)
