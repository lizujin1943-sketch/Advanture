"""可外部携带的原生程序纹理：无烘入灯光的颜色、粗糙度、法线和发光遮罩。"""
import bpy
import numpy as np
from abyssal_common import SOURCE,material_node

PALETTE={
    'Skin':((.095,.135,.123),.71), 'Belly':((.21,.27,.23),.65),
    'Cup':((.45,.49,.39),.76), 'Reef':((.46,.48,.38),.94),
    'ReefDark':((.24,.29,.25),.88), 'Cavity':((.005,.012,.01),.95),
    'Tooth':((.40,.46,.35),.62), 'Eye':((.13,.82,.65),.29),
    'Pupil':((.004,.015,.015),.32), 'Scar':((.06,.12,.10),.68),
    'Membrane':((.24,.33,.28),.70), 'Fissure':((.065,.42,.32),.5),
    'MouthFlesh':((.075,.062,.055),.43), 'MouthTip':((.14,.16,.12),.58),
}

def image_texture(name,array,noncolor=False):
    h,w,_=array.shape
    image=bpy.data.images.new('AW_'+name,width=w,height=h,alpha=True)
    if noncolor:image.colorspace_settings.name='Non-Color'
    image.pixels.foreach_set(array.astype(np.float32).ravel())
    image.filepath_raw=str(SOURCE/'textures'/('AW_'+name+'.png'))
    image.file_format='PNG';image.save()
    return image

def build_materials():
    (SOURCE/'textures').mkdir(parents=True,exist_ok=True)
    n=1024;y,x=np.mgrid[0:n,0:n]/n
    # 周期函数使边界连续；细纹凹凸与大块明暗分离，便于后续手绘替换。
    rng=np.random.default_rng(6501)
    def cloud(cells):
        grid=rng.uniform(-1,1,(cells,cells));gx=x*cells;gy=y*cells
        ix=gx.astype(int);iy=gy.astype(int);fx=gx-ix;fy=gy-iy
        fx=fx*fx*(3-2*fx);fy=fy*fy*(3-2*fy)
        a=grid[iy%cells,ix%cells]*(1-fx)+grid[iy%cells,(ix+1)%cells]*fx
        b=grid[(iy+1)%cells,ix%cells]*(1-fx)+grid[(iy+1)%cells,(ix+1)%cells]*fx
        return a*(1-fy)+b*fy
    noise=cloud(9)*.55+cloud(28)*.30+cloud(84)*.15
    crack=np.clip((cloud(22)-.50)*6,0,1)
    height=cloud(60)*.04+cloud(180)*.025+noise*.04-crack*.030
    dy,dx=np.gradient(height);norm=np.stack((-dx*7,-dy*7,np.ones_like(dx)),axis=-1)
    norm/=np.linalg.norm(norm,axis=-1)[...,None]
    normal=image_texture('Surface_Normal',np.dstack((norm*.5+.5,np.ones_like(x))),True)
    mats={}
    for name,(color,rough) in PALETTE.items():
        organic=name not in ('Eye','Pupil','Fissure','Cavity')
        factor=1+noise*(.22 if organic else .035)-crack*(.19 if organic else 0)
        base=np.dstack(tuple(np.clip(factor*c,0,1) for c in color)+(np.ones_like(x),))
        eye_emission=None
        if name=='Eye':
            # 在同一张角膜下画虹膜和狭长瞳孔，去掉原先外凸的小黑球。
            u=(x-.5)*2;v=(y-.5)*2;rad=np.sqrt(u*u+v*v);angle=np.arctan2(v,u)
            fibers=.5+.5*np.sin(angle*89+rad*24+np.sin(angle*31)*2)
            detail=.5+.5*np.sin(angle*179-rad*53)
            rim=np.clip((1-rad)*4,0,1)
            strength=(.40+.40*fibers+.20*detail)*rim
            width=.105*np.maximum(0,1-(v/.91)**2)**.72
            slit=np.clip((width-np.abs(u))/.012+.5,0,1)*(np.abs(v)<.91)
            rgb=np.stack((.018+strength*.12,.045+strength*.48,.035+strength*.34),axis=-1)
            rgb=rgb*(1-slit[...,None])+np.array([.001,.003,.003])*slit[...,None]
            base=np.dstack((rgb,np.ones_like(x)))
            eye_emission=rgb*rim[...,None]*(1-slit[...,None])
        col=image_texture(name+'_BaseColor',base)
        r=np.clip(rough+noise*.07,.08,1)
        roughimg=image_texture(name+'_Roughness',np.dstack((r,r,r,np.ones_like(x))),True)
        mat=bpy.data.materials.new('AW_'+name);mat.use_nodes=True;mat.diffuse_color=(*color,1)
        bs=material_node(mat);nodes=mat.node_tree.nodes;links=mat.node_tree.links
        c=nodes.new('ShaderNodeTexImage');c.image=col;c.label='可携带颜色贴图';links.new(c.outputs['Color'],bs.inputs['Base Color'])
        t=nodes.new('ShaderNodeTexImage');t.image=roughimg;links.new(t.outputs['Color'],bs.inputs['Roughness'])
        if organic:
            nt=nodes.new('ShaderNodeTexImage');nt.image=normal
            nm=nodes.new('ShaderNodeNormalMap');nm.inputs['Strength'].default_value=.24 if name!='Reef' else .55
            links.new(nt.outputs['Color'],nm.inputs['Color']);links.new(nm.outputs['Normal'],bs.inputs['Normal'])
        if name in ('Eye','Fissure'):
            glow=np.clip(1-np.sqrt(((x-.5)*1.8)**2+((y-.5)*1.8)**2),0,1)**1.3
            if name=='Fissure':glow=np.ones_like(x)
            em=image_texture(name+'_Emission',np.dstack((eye_emission,np.ones_like(x))) if name=='Eye' else np.dstack(tuple(glow*c for c in color)+(np.ones_like(x),)))
            e=nodes.new('ShaderNodeTexImage');e.image=em;links.new(e.outputs['Color'],bs.inputs['Emission Color'])
            bs.inputs['Emission Strength'].default_value=.70 if name=='Eye' else .65
        if name=='Eye':
            bs.inputs['Coat Weight'].default_value=.055;bs.inputs['Coat Roughness'].default_value=.21
            bs.inputs['Specular IOR Level'].default_value=.12
        if name in ('Skin','Belly','MouthFlesh'):
            bs.inputs['Coat Weight'].default_value=.025;bs.inputs['Coat Roughness'].default_value=.40
            bs.inputs['Subsurface Weight'].default_value=.035
            # 实际尺寸的细密皮肤颗粒。仅作用于法线，不把高光画进颜色贴图。
            coords=nodes.new('ShaderNodeTexCoord');grain=nodes.new('ShaderNodeTexNoise')
            grain.inputs['Scale'].default_value=18;grain.inputs['Detail'].default_value=3
            grain.inputs['Roughness'].default_value=.72
            links.new(coords.outputs['Object'],grain.inputs['Vector'])
            bump=nodes.new('ShaderNodeBump');bump.inputs['Strength'].default_value=.32
            bump.inputs['Distance'].default_value=.028
            links.new(grain.outputs['Fac'],bump.inputs['Height'])
            links.new(nm.outputs['Normal'],bump.inputs['Normal']);links.new(bump.outputs['Normal'],bs.inputs['Normal'])
        mats[name]=mat
    # 口内颜色按同一张连续网格的顶点权重渐变，避免按多边形换材质产生阶梯状口缘。
    face=mats['Skin'].copy();face.name='AW_FaceSkin';nodes=face.node_tree.nodes;links=face.node_tree.links
    bs=material_node(face);source=bs.inputs['Base Color'].links[0].from_socket
    vertex=nodes.new('ShaderNodeVertexColor');vertex.layer_name='TissueWeight'
    separate=nodes.new('ShaderNodeSeparateColor');links.new(vertex.outputs['Color'],separate.inputs['Color'])
    flesh=nodes.new('ShaderNodeMixRGB');flesh.inputs[2].default_value=(.075,.062,.055,1)
    links.new(source,flesh.inputs[1]);links.new(separate.outputs['Red'],flesh.inputs[0])
    throat=nodes.new('ShaderNodeMixRGB');throat.inputs[2].default_value=(.002,.005,.004,1)
    links.new(flesh.outputs[0],throat.inputs[1]);links.new(separate.outputs['Green'],throat.inputs[0])
    links.new(throat.outputs[0],bs.inputs['Base Color'])
    # 喉底吸光，避免黑色底面仍被正面灯打成一个灰色圆盘。
    inverse=nodes.new('ShaderNodeMath');inverse.operation='SUBTRACT';inverse.inputs[0].default_value=1
    links.new(separate.outputs['Green'],inverse.inputs[1])
    for socket,strength in [('Specular IOR Level',.28),('Coat Weight',.025),('Subsurface Weight',.035)]:
        multiply=nodes.new('ShaderNodeMath');multiply.operation='MULTIPLY';multiply.inputs[1].default_value=strength
        links.new(inverse.outputs[0],multiply.inputs[0]);links.new(multiply.outputs[0],bs.inputs[socket])
    mats['FaceSkin']=face
    return mats
