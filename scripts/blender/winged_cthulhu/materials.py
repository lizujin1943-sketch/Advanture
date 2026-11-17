"""Layered wet organic materials; procedural source, not a baked runtime deliverable."""
import bpy

def rgb(hexstr):
    vals=[int(hexstr[i:i+2],16)/255 for i in (0,2,4)]
    return tuple(v/12.92 if v<=.04045 else ((v+.055)/1.055)**2.4 for v in vals)+(1,)

def base(name,color,rough=.5):
    m=bpy.data.materials.new(name);m.use_nodes=True;m.diffuse_color=rgb(color)
    n=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    n.inputs['Base Color'].default_value=rgb(color);n.inputs['Roughness'].default_value=rough
    n.inputs['Specular IOR Level'].default_value=.32
    return m,n

def ramp(nodes,stops):
    n=nodes.new('ShaderNodeValToRGB')
    for i,(pos,col) in enumerate(stops):
        e=n.color_ramp.elements[i] if i<2 else n.color_ramp.elements.new(pos)
        e.position=pos;e.color=rgb(col)
    return n

def organic(name,colors,scale=4,rough=.5,bump=.017):
    m,p=base(name,colors[0][1],rough);ns=m.node_tree.nodes;ln=m.node_tree.links
    tex=ns.new('ShaderNodeTexCoord')
    noise=ns.new('ShaderNodeTexNoise');noise.inputs['Scale'].default_value=scale;noise.inputs['Detail'].default_value=3.4;noise.inputs['Roughness'].default_value=.68
    ln.new(tex.outputs['Object'],noise.inputs['Vector'])
    co=ramp(ns,colors);ln.new(noise.outputs['Fac'],co.inputs['Fac']);ln.new(co.outputs['Color'],p.inputs['Base Color'])
    pores=ns.new('ShaderNodeTexNoise');pores.inputs['Scale'].default_value=95;pores.inputs['Detail'].default_value=2
    ln.new(tex.outputs['Object'],pores.inputs['Vector'])
    bm=ns.new('ShaderNodeBump');bm.inputs['Strength'].default_value=.22;bm.inputs['Distance'].default_value=bump
    ln.new(pores.outputs['Fac'],bm.inputs['Height']);ln.new(bm.outputs['Normal'],p.inputs['Normal'])
    p.inputs['Subsurface Weight'].default_value=.025
    return m

def build():
    skin=organic('Skin — blue green mottled dermis',[(.16,'354f49'),(.39,'465f56'),(.58,'597166'),(.77,'738373')],4.2,.49,.009)
    ns=skin.node_tree.nodes;ln=skin.node_tree.links;p=next(n for n in ns if n.type=='BSDF_PRINCIPLED')
    original=p.inputs['Base Color'].links[0].from_socket
    tex=next(n for n in ns if n.type=='TEX_COORD')
    mapping=ns.new('ShaderNodeVectorMath');mapping.operation='MULTIPLY';mapping.inputs[1].default_value=(1.35,1.1,.65)
    ln.new(tex.outputs['Object'],mapping.inputs[0])
    v=ns.new('ShaderNodeTexVoronoi');v.inputs['Scale'].default_value=1.8;v.inputs['Randomness'].default_value=.9
    warp=ns.new('ShaderNodeTexNoise');warp.inputs['Scale'].default_value=7.5;warp.inputs['Detail'].default_value=3
    ln.new(tex.outputs['Object'],warp.inputs['Vector'])
    strength=ns.new('ShaderNodeVectorMath');strength.operation='SCALE';strength.inputs['Scale'].default_value=.21
    ln.new(warp.outputs['Color'],strength.inputs[0])
    add=ns.new('ShaderNodeVectorMath');add.operation='ADD';ln.new(mapping.outputs['Vector'],add.inputs[0]);ln.new(strength.outputs['Vector'],add.inputs[1]);ln.new(add.outputs['Vector'],v.inputs['Vector'])
    mask=ns.new('ShaderNodeValToRGB');mask.color_ramp.elements[0].position=.22;mask.color_ramp.elements[0].color=(1,1,1,1);mask.color_ramp.elements[1].position=.29;mask.color_ramp.elements[1].color=(0,0,0,1)
    ln.new(v.outputs['Distance'],mask.inputs['Fac'])
    mix=ns.new('ShaderNodeMixRGB');mix.blend_type='MIX';mix.inputs[2].default_value=rgb('919e76')
    ln.new(original,mix.inputs[1]);ln.new(mask.outputs['Color'],mix.inputs[0]);ln.new(mix.outputs[0],p.inputs['Base Color'])
    belly=organic('Ventral tissue — deep dusty mauve',[(.2,'352b3c'),(.5,'5a4052'),(.8,'856474')],6,.53)
    cups=organic('Sucker lip — lavender flesh',[(.2,'806271'),(.47,'aa8392'),(.8,'c5a2b0')],11,.43,.007)
    pits=organic('Sucker recessed bowl',[(.2,'7e616f'),(.6,'977583'),(.85,'b697a1')],10,.5,.006)
    membrane=organic('Wing membrane — aubergine',[(.15,'2e2435'),(.4,'503548'),(.7,'775366'),(.9,'957488')],2.6,.56,.009)
    ridge=organic('Cartilage and brow ridges',[(.18,'3e574b'),(.5,'708467'),(.8,'9da784')],5,.47)
    socket=organic('Eye socket wet crease',[(.2,'101c1b'),(.8,'293b33')],7,.38)
    eye,p=base('Eyes — sulfur bioluminescence','d3f24f',.3)
    p.inputs['Emission Color'].default_value=rgb('dfff48');p.inputs['Emission Strength'].default_value=1.4
    head=organic('Cranium — slate dorsal chromatophores',[(.18,'43574f'),(.5,'586a5a'),(.8,'708068')],5.4,.53,.009)
    ns=head.node_tree.nodes;ln=head.node_tree.links;p=next(n for n in ns if n.type=='BSDF_PRINCIPLED')
    orig=p.inputs['Base Color'].links[0].from_socket
    tex=next(n for n in ns if n.type=='TEX_COORD')
    vor=ns.new('ShaderNodeTexVoronoi');vor.inputs['Scale'].default_value=1.8
    warp=ns.new('ShaderNodeTexNoise');warp.inputs['Scale'].default_value=9;warp.inputs['Detail'].default_value=2
    ln.new(tex.outputs['Object'],warp.inputs['Vector'])
    amp=ns.new('ShaderNodeVectorMath');amp.operation='SCALE';amp.inputs['Scale'].default_value=.085;ln.new(warp.outputs['Color'],amp.inputs[0])
    add=ns.new('ShaderNodeVectorMath');add.operation='ADD';ln.new(tex.outputs['Object'],add.inputs[0]);ln.new(amp.outputs['Vector'],add.inputs[1]);ln.new(add.outputs['Vector'],vor.inputs['Vector'])
    mask=ns.new('ShaderNodeValToRGB');mask.color_ramp.elements[0].position=.46;mask.color_ramp.elements[0].color=(1,1,1,1);mask.color_ramp.elements[1].position=.51;mask.color_ramp.elements[1].color=(0,0,0,1)
    ln.new(vor.outputs['Distance'],mask.inputs['Fac'])
    mix=ns.new('ShaderNodeMixRGB');mix.inputs[2].default_value=rgb('34444b')
    ln.new(orig,mix.inputs[1]);ln.new(mask.outputs['Color'],mix.inputs[0]);ln.new(mix.outputs[0],p.inputs['Base Color'])
    detail=next(n for n in ns if n.type=='TEX_NOISE' and abs(n.inputs['Scale'].default_value-5.4)<.01)
    dark=ramp(ns,[(.2,'2c3d43'),(.8,'41535a')]);ln.new(detail.outputs['Fac'],dark.inputs['Fac']);ln.new(dark.outputs['Color'],mix.inputs[2])
    old_normal=p.inputs['Normal'].links[0].from_socket
    plate_bump=ns.new('ShaderNodeBump');plate_bump.inputs['Strength'].default_value=.28;plate_bump.inputs['Distance'].default_value=.025
    ln.new(old_normal,plate_bump.inputs['Normal']);ln.new(mask.outputs['Color'],plate_bump.inputs['Height']);ln.new(plate_bump.outputs['Normal'],p.inputs['Normal'])
    return dict(skin=skin,head=head,belly=belly,cups=cups,pits=pits,membrane=membrane,ridge=ridge,socket=socket,eye=eye)
