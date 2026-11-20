"""Paint the shared 2K trim atlas. Run with Python, NumPy and Pillow."""
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from atlas_layout import GAME, REGIONS, SIZE, TEXTURES


def noise(rng, w, h, nx, ny):
    seed = rng.normal(size=(max(3,ny),max(3,nx))).astype('float32')
    return np.asarray(Image.fromarray(seed).resize((w,h),Image.Resampling.BICUBIC))


def wear_lines(rng, w, h, count):
    layer = Image.new('L',(w,h))
    pen = ImageDraw.Draw(layer)
    for _ in range(count):
        x,y = rng.uniform(0,w),rng.uniform(0,h)
        span = rng.uniform(4,32)
        dx,dy = span*rng.uniform(.4,1.5),span*rng.uniform(-.65,.65)
        pen.line((x,y,x+dx,y+dy),fill=int(rng.uniform(35,180)),width=1)
    return np.asarray(layer.filter(ImageFilter.GaussianBlur(.35)))/255.


def tile(kind,w,h,seed):
    rng = np.random.default_rng(seed)
    v,u=np.mgrid[0:h,0:w]
    u,v=u/max(1,w-1),v/max(1,h-1)
    low=noise(rng,w,h,6,6)
    mid=noise(rng,w,h,max(4,w//46),max(4,h//53))
    fine=rng.normal(0,1,(h,w))
    if kind in ('stock','walnut'):
        bend=.57*np.sin(5.2*u+2.6*v)+.27*noise(rng,w,h,4,7)
        eye=np.sqrt((u-.71)**2*.65+(v-.57+.08*np.sin(4*u))**2*1.46+.003)
        field=v*11.7+eye*11.8+bend
        ring=np.sin(2*np.pi*field)
        latewood=np.maximum(ring-.28,0)**1.65
        directional=noise(rng,w,h,27,max(70,h//2))
        vessels=np.maximum(noise(rng,w,h,max(20,w//19),h//2)-.81,0)
        # Roughly book-matched walnut. Slow bands and scattered elongated pores
        # survive the mid mips; fine rays only become visible in an inspection view.
        broad_grain=np.sin(2*np.pi*(2.3*v+.31*u+.15*np.sin(3.8*u+1.4*v)))
        colour=12*low+8*broad_grain+4.5*mid+4.1*directional-30*latewood-12*vessels+1.8*fine
        base=(151,60,29) if kind=='stock' else (147,57,33)
        rgb=np.stack([base[i]+colour*k for i,k in enumerate((1,.78,.57))],axis=-1)
        height=directional*.44-vessels*.86-latewood*.30
        metallic=np.zeros_like(u)
        varnish=.62 if kind=='stock' else .76
        smooth=varnish+.030*mid-.065*latewood-.040*vessels
    elif kind in ('steel','mag','edge','black','blued','well'):
        polish=np.maximum(0,1-np.minimum.reduce([u,v,1-u,1-v])/.065)**2
        patina=noise(rng,w,h,13,11)
        brush=noise(rng,w,h,max(5,w//135),max(20,h//2))
        pits=np.maximum(fine-1.45,0)
        scratches=wear_lines(rng,w,h,int(w*h/1050))
        bright=np.clip((polish*.32+scratches)*(.8+.26*mid),0,1)
        bases={'steel':(48,51,60),'mag':(67,66,75),'edge':(101,109,111),'black':(34,39,40),
               'blued':(59,55,72),'well':(18,18,26)}
        factors={'steel':(.79,.62),'mag':(.81,.69),'edge':(.87,.61),'black':(.13,.32),
                 'blued':(.68,.57),'well':(.71,.65)}
        base=bases[kind]
        colour=3.5*low+2*mid+1.65*brush+1.6*fine-2*pits+bright*43
        if kind=='blued':
            colour=1.9*low+1.5*mid+.6*brush+.8*fine+bright*21+12*np.clip((u-.51)/.29,0,1)
        rgb=np.stack([base[i]+colour+(i-1)*patina*1.1 for i in range(3)],axis=-1)
        m,gloss=factors[kind]
        metallic=np.clip(m+.08*low+.28*bright,0,1)
        smooth=gloss+.045*patina+.032*mid+.16*bright-.017*pits
        height=.36*brush+.24*fine-pits*.7-scratches*2
        if kind=='blued':
            smooth=gloss+.016*patina+.014*mid+.065*bright
            height=.10*brush+.12*fine-.18*pits-.55*scratches
    elif kind in ('cloth','leather','rubber','grip'):
        pitch=5.2
        loom1=np.sin(2*np.pi*(u*w+v*h*.09)/pitch)
        loom2=np.sin(2*np.pi*(v*h-u*w*.12)/pitch)
        twill=np.sin(2*np.pi*(v*h*.93+u*w*.59)/13.)
        weave=.43*loom1+.32*loom2+.27*loom1*loom2+.24*twill
        if kind=='cloth':
            tone=6*low+4*mid+weave*5+fine*1.9
            base=(79,82,65)
            height=weave*1.35+fine*.2
            gloss=.21
        elif kind=='grip':
            # Both cheeks map into this same tile. Keep the collar, edge and heel polished.
            grain=noise(rng,w,h,68,92)
            upper=.177+.025*(1-u)
            lower=.870+.025*u
            left=.18+.010*np.cos(v*6)
            right=.72+.055*(v-.3)
            inside=np.minimum.reduce((u-left,right-u,(v-upper)*1.4,(lower-v)*1.4))
            panel=np.clip(inside/.011,0,1)
            boundary=np.exp(-((inside+.002)/.005)**2)
            lattice=np.minimum(np.sin(2*np.pi*(33*u+57*v)),np.sin(2*np.pi*(33*u-57*v)))
            stipple=np.maximum(lattice+.12*grain,0)
            tone=3.1*low+1.8*mid+2.4*grain+1.5*fine+panel*(7*stipple-2.4)-3*boundary
            badge=np.exp(-((np.abs(u-.536)/.06+np.abs(v-.147)/.014-1)/.18)**2)
            base=(69,49,65)
            height=.17*grain+.12*fine+panel*(2.7*stipple+.42*grain)-1.1*boundary+.8*badge
            gloss=.64-.22*panel+.04*boundary
        elif kind=='rubber':
            pebble=noise(rng,w,h,w//3,h//3)
            tone=3.7*mid+2.5*pebble+1.6*fine
            base=(42,45,42)
            height=.72*pebble+.20*fine
            gloss=.28
        else:
            grain=noise(rng,w,h,w//3,h//3)
            tone=6*low+4*mid+3*grain+fine*2
            base=(95,87,66)
            height=.68*grain+.38*weave+.18*fine
            gloss=.29
        rgb=np.stack([base[i]+tone*k for i,k in enumerate((1,.94,.76))],axis=-1)
        metallic=np.zeros_like(u)
        smooth=gloss+.03*mid+fine*.014
    else:
        base={'thread':(129,121,92),'oxide':(82,48,55),'mark':(62,69,71)}[kind]
        tone=3*mid+fine*2.
        rgb=np.stack([b+tone for b in base],axis=-1)
        metallic=np.zeros_like(u)+(.38 if kind=='mark' else .04)
        smooth=.35+low*.035
        height=.23*fine
        if kind=='mark':
            mask=Image.new('L',(w,h))
            pen=ImageDraw.Draw(mask)
            def badge(coords,level=255):
                pen.polygon([(a*w,b*h) for a,b in coords],fill=level)
            # Own stencil: three chipped bars and the two figures used on the depot prop.
            for off in (0,.109,.218):
                badge([(.11+off,.32),(.19+off,.32),(.26+off,.67),(.18+off,.67)],126)
            badge([(.55,.35),(.62,.29),(.66,.29),(.66,.68),(.605,.68),(.605,.38),(.55,.43)])
            badge([(.70,.30),(.93,.30),(.93,.36),(.816,.68),(.754,.68),(.875,.37),(.70,.37)])
            a=np.asarray(mask)/255.
            ink=np.where((a>.55)[...,None],np.array([173,167,141]),np.array([154,75,48]))
            weight=np.where(a>.1,.82,0)*np.clip(.92+fine*.14,0,1)
            rgb=rgb*(1-weight[...,None])+ink*weight[...,None]
            metallic*=1-weight
    dy,dx=np.gradient(height)
    n=np.stack((-dx*.25,dy*.25,np.ones_like(u)),axis=-1)
    n/=np.sqrt(np.sum(n*n,axis=-1,keepdims=True))
    m=np.dstack((metallic,metallic,metallic,smooth))
    return tuple(Image.fromarray(np.uint8(np.clip(x,0,255))) for x in (rgb,m*255,(n*.5+.5)*255))


def main():
    GAME.mkdir(parents=True,exist_ok=True)
    canvas=[Image.new(mode,(SIZE,SIZE)) for mode in ('RGB','RGBA','RGB')]
    for i,(key,(x0,y0,x1,y1)) in enumerate(REGIONS.items()):
        l,t,r,b=map(round,(x0*SIZE,(1-y1)*SIZE,x1*SIZE,(1-y0)*SIZE))
        for dst,src in zip(canvas,tile(key,r-l,b-t,4801+i*47)):
            dst.paste(src,(l,t))
    for dst,name in zip(canvas,TEXTURES):
        dst.save(GAME/name,compress_level=7)


if __name__=='__main__':
    main()
