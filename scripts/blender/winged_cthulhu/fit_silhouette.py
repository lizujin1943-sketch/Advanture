"""Smooth image-plane proportional editing field from two alpha silhouettes.

Runs with ordinary Python + NumPy/Pillow. Does not project or bake the concept onto geometry.
The field is later applied to X/Z coordinates of the full three-dimensional mesh, retaining depth.
"""
from pathlib import Path
import json
import numpy as np
from PIL import Image

ROOT=Path(__file__).resolve().parents[3]
ASSET=ROOT/'source-assets/creatures/winged-cthulhu'

def resize(a,w,h):
    return np.asarray(Image.fromarray(a.astype(np.float32)).resize((w,h),Image.Resampling.BILINEAR)).copy()

def blur(a,iterations=1):
    for _ in range(iterations):
        p=np.pad(a,((0,0),(1,1)),mode='edge');a=(p[:,:-2]+2*p[:,1:-1]+p[:,2:])*.25
        p=np.pad(a,((1,1),(0,0)),mode='edge');a=(p[:-2]+2*p[1:-1]+p[2:])*.25
    return a

def sample(a,x,y):
    h,w=a.shape;x=np.clip(x,0,w-1.001);y=np.clip(y,0,h-1.001)
    ix=x.astype(int);iy=y.astype(int);fx=x-ix;fy=y-iy
    return a[iy,ix]*(1-fx)*(1-fy)+a[iy,ix+1]*fx*(1-fy)+a[iy+1,ix]*(1-fx)*fy+a[iy+1,ix+1]*fx*fy

def main():
    a=np.asarray(Image.open(ASSET/'review/silhouette-base.png').getchannel('A'),dtype=float)>16
    b=np.asarray(Image.open(ASSET/'ref/user-reference.png').getchannel('A'),dtype=float)>16
    u=v=None
    for w in (96,192,384,768):
        h=w*2//3
        source=blur(resize(a,w,h),2);target=blur(resize(b,w,h),2)
        if u is None:u=np.zeros((h,w));v=u.copy()
        else:u=resize(u,w,h)*2;v=resize(v,w,h)*2
        yy,xx=np.mgrid[:h,:w]
        gy,gx=np.gradient(target)
        for _ in range(7):
            warped=sample(target,xx+u,yy+v)
            ix=sample(gx,xx+u,yy+v);iy=sample(gy,xx+u,yy+v);it=warped-source
            du=np.zeros_like(u);dv=np.zeros_like(v)
            for _ in range(55):
                au=blur(du);av=blur(dv)
                t=(ix*au+iy*av+it)/(.05**2+ix*ix+iy*iy)
                du=au-ix*t;dv=av-iy*t
            u+=np.clip(du,-1,1)*.65;v+=np.clip(dv,-1,1)*.65
            u=blur(u,2);v=blur(v,2)
    # Preserve positive local area. This is a restrained sculpt correction, not a folded warp.
    u=blur(u,10)*2;v=blur(v,10)*2
    u=np.clip(u,-56,56);v=np.clip(v,-56,56)
    yy,xx=np.mgrid[:u.shape[0],:u.shape[1]]
    # Never improve an image score by pushing geometry out of the reference frame.
    u*=np.where(u<0,np.clip((xx-1)/24,0,1),np.clip((u.shape[1]-2-xx)/24,0,1))
    v*=np.where(v<0,np.clip((yy-1)/32,0,1),np.clip((v.shape[0]-2-yy)/32,0,1))
    uy,ux=np.gradient(u/2);vy,vx=np.gradient(v/2)
    jac=(1+ux)*(1+vy)-uy*vx
    factor=1
    while jac.min()<.2:
        factor*=.9;u*=.9;v*=.9
        uy,ux=np.gradient(u/2);vy,vx=np.gradient(v/2);jac=(1+ux)*(1+vy)-uy*vx
    np.savez_compressed(ASSET/'review/silhouette-fit-field.npz',u=u.astype(np.float32),v=v.astype(np.float32))
    data={'method':'smooth multiscale alpha registration, applied as X/Z proportional editing','field_size':[768,512],'max_shift_px':float(np.hypot(u,v).max()),'min_area_jacobian':float(jac.min()),'safety_scale':factor,'depth_preserved':True}
    (ASSET/'review/silhouette-fit.json').write_text(json.dumps(data,indent=2))
    print(json.dumps(data))

if __name__=='__main__':main()
