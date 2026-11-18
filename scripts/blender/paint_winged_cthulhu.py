"""Repaint the saved sculpt with reference-derived pigment masks in Blender.

The reference is sampled to identify pigment and small brush marks, not connected
as a lit photograph/emission shader. Broad painted illumination is removed;
Cycles still shades the actual geometry. Front paint uses fixed mesh UVs and a
normal-based coverage attribute; unseen sides retain an authored 3D material.
This is a high-sculpt look-development source, not a runtime texture bake.
"""
from pathlib import Path
import sys
import json
import shutil
import hashlib
import numpy as np
import bpy

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'source-assets/creatures/winged-cthulhu'
TEX = OUT / 'textures'
REVIEW = OUT / 'review'
ART = ROOT / 'artifacts/winged-cthulhu'
sys.path.insert(0, str(Path(__file__).resolve().parent))
from winged_cthulhu.materials import rgb, ramp


def blur(a, sigma):
    """Periodic Gaussian; inputs have neutral/empty borders, no external library."""
    fy = np.fft.fftfreq(a.shape[0])[:, None]
    fx = np.fft.rfftfreq(a.shape[1])[None, :]
    filt = np.exp(-2 * np.pi**2 * sigma**2 * (fx*fx + fy*fy))
    return np.fft.irfft2(np.fft.rfft2(a) * filt, s=a.shape).astype(np.float32)


def smooth(a, low, high):
    t = np.clip((a-low)/(high-low), 0, 1)
    return t*t*(3-2*t)


def blend(a, b, fac):
    return a*(1-fac[..., None]) + np.asarray(b)*fac[..., None]


def write_texture(name, data, size, is_color):
    """Write explicitly encoded PNG, then reload with the correct shader space."""
    if data.ndim == 2:
        data = np.repeat(data[..., None], 3, axis=2)
    h, w = data.shape[:2]
    rgba = np.ones((h, w, 4), dtype=np.float32)
    rgba[..., :3] = np.clip(data, 0, 1)
    im = bpy.data.images.new(name+'_writer', w, h, alpha=True, is_data=True)
    im.colorspace_settings.name = 'Non-Color'
    im.pixels.foreach_set(rgba.ravel())
    if (w, h) != (size, size):
        im.scale(size, size)
    path = TEX / (name+'.png')
    im.filepath_raw = str(path)
    im.file_format = 'PNG'
    im.save()
    bpy.data.images.remove(im)
    result = bpy.data.images.load(str(path), check_existing=False)
    result.name = name
    result.colorspace_settings.name = 'sRGB' if is_color else 'Non-Color'
    result.pack()
    return result


def make_paint():
    source = bpy.data.images.load(str(OUT/'ref/user-reference.png'), check_existing=False)
    source.colorspace_settings.name = 'Non-Color'
    w, h = source.size
    rgba = np.empty(w*h*4, dtype=np.float32)
    source.pixels.foreach_get(rgba)
    rgba = rgba.reshape(h, w, 4)
    c = rgba[..., :3]
    r, g, b = [c[..., i] for i in range(3)]
    alpha = rgba[..., 3]
    lum = c @ np.array([.2126, .7152, .0722], np.float32)
    valid = ((g-r > .013) & (g-b > .01) & (alpha > .85) & (lum > .12)).astype(np.float32)
    # Low-frequency light/shadow is deliberately not copied into the albedo.
    local = blur(lum*valid, 13)/np.maximum(blur(valid, 13), .02)
    brush = np.clip((lum-local)/np.maximum(local, .18), -.48, .36)*valid
    olive = smooth(g-b, .062, .105)*smooth(r-b, .018, .055)*smooth(g, .38, .49)*valid
    # The slate chromatophores in the source are blue-biased, unlike the dermis.
    dorsal = smooth(b-g, -.008, .016)*smooth(g-r, .018, .045)
    dorsal *= (alpha > .85)*smooth(lum, .10, .19)*(1-smooth(lum, .43, .52))
    # Explicit head zone prevents a wing, hole or purple sucker becoming a spot.
    yy, xx = np.mgrid[0:h, 0:w]
    ref_y = h-1-yy
    head_zone = ((xx-811)/232)**2 + ((ref_y-253)/238)**2 < 1.03
    dorsal *= head_zone
    coverage = np.clip(blur(valid, 1.3)*1.2, 0, 1)
    # The entire visible crown is painted. Fading only by pigment class would
    # reveal a second, unrelated procedural spot underneath every painted edge.
    head_coverage = np.maximum(coverage, (head_zone & (alpha > .85)).astype(np.float32))
    skin = np.broadcast_to(np.array([86, 112, 104])/255, (h,w,3)).copy()
    skin += brush[..., None]*np.array([.25,.29,.23])
    skin = blend(skin, np.array([143,165,132])/255, olive)
    head = blend(skin, np.array([57,72,85])/255+brush[...,None]*.12, dorsal)
    # Small irregular edges survive normal lighting without large stone-like noise.
    height = .5 + brush*.075 + (olive-blur(olive, 2))*.06
    head_height = height + dorsal*.045 - (blur(dorsal, 2)-dorsal)*.05
    rough = np.clip(.49-brush*.12-olive*.025, .42, .57)
    maps = {}
    for key, data, size, color in [
        ('Skin_Albedo',skin,4096,True), ('Head_Albedo',head,4096,True),
        ('Skin_Coverage',coverage,2048,False), ('Head_Coverage',head_coverage,2048,False),
        ('Skin_Height',height,2048,False), ('Head_Height',head_height,2048,False),
        ('Skin_Roughness',rough,2048,False),
    ]:
        maps[key] = write_texture('T_WingedCthulhu_'+key,data,size,color)
    bpy.data.images.remove(source)
    return maps


def make_cup():
    n = 1024
    v,u = np.mgrid[0:n, 0:n].astype(np.float32)/(n-1)
    rad = np.sqrt((u-.5)**2+(v-.5)**2)
    theta = np.arctan2(v-.5,u-.5)
    # Pigment layers: mauve foot, rose bowl, pale fleshy annulus, small dark ostium.
    col = np.broadcast_to(np.array([156,119,138])/255,(n,n,3)).copy()
    col = blend(col,np.array([190,151,166])/255,np.exp(-((rad-.425)/.043)**2)*.8)
    col = blend(col,np.array([123,96,116])/255,1-smooth(rad,.047,.079))
    rings = .0025*np.sin(rad*420+np.sin(theta*9)*.7)*smooth(rad,.09,.2)
    col += rings[...,None]
    return {
        'Cup_Albedo': write_texture('T_WingedCthulhu_Cup_Albedo',col,1024,True),
        'Cup_Roughness': write_texture('T_WingedCthulhu_Cup_Roughness',.34+.045*np.sin(rad*12),1024,False),
    }


def make_membrane():
    n=1024
    t,s=np.mgrid[0:n,0:n].astype(np.float32)/(n-1)
    # Every lobe's SurfaceUV follows its wrist-to-edge tension direction.
    root=np.array([33,22,34])/255
    edge=np.array([130,92,111])/255
    a=smooth(t,.03,.99)**1.4
    color=root[None,None,:]*(1-a[...,None])+edge[None,None,:]*a[...,None]
    color=np.broadcast_to(color,(n,n,3)).copy()
    shade=.84+.16*np.sin(np.pi*s)
    color*=shade[...,None]
    fine=np.sin(s*100+2*np.sin(t*8))*.0018+np.sin(s*237-t*4)*.0012
    color+=fine[...,None]
    return {
        'Wing_Albedo':write_texture('T_WingedCthulhu_Wing_Albedo',color,2048,True),
        'Wing_Roughness':write_texture('T_WingedCthulhu_Wing_Roughness',.52+.04*np.sin(s*np.pi),1024,False),
    }


def uv_and_coverage(ob):
    me=ob.data
    v=np.empty(len(me.vertices)*3,np.float32);me.vertices.foreach_get('co',v);v=v.reshape(-1,3)
    mat=np.asarray(ob.matrix_world)
    world=v@mat[:3,:3].T+mat[:3,3]
    # Fixed reference camera; no camera-dependent material coordinates.
    uv=np.column_stack((world[:,0]/15.6+.5,(world[:,2]-5.12)/10.4+.5))
    if ob.get('portrait_paint'):
        # In the painting the eyes are to the right of the crown's centre.
        # Register the facial pigment there and taper the shift up the dome.
        height=world[:,2]
        uv[:,0]+=(.090-.059*smooth(height,7.0,9.9))*smooth(height,5.1,6.8)
    ids=np.empty(len(me.loops),np.int32);me.loops.foreach_get('vertex_index',ids)
    layer=me.uv_layers.get('ReferencePaintUV') or me.uv_layers.new(name='ReferencePaintUV')
    layer.uv.foreach_set('vector',uv[ids].astype(np.float32).ravel())
    normals=np.empty(len(me.vertices)*3,np.float32);me.vertices.foreach_get('normal',normals)
    normals=normals.reshape(-1,3)@np.linalg.inv(mat[:3,:3])
    normals/=np.maximum(np.linalg.norm(normals,axis=1)[:,None],1e-8)
    coverage=smooth(-normals[:,1],-.15,.25)
    attr=me.attributes.get('ReferencePaintCoverage') or me.attributes.new('ReferencePaintCoverage','FLOAT','POINT')
    attr.data.foreach_set('value',coverage.astype(np.float32))
    me.update()


def paint_dorsal_marks():
    """Paint olive chromatophores along the two sides of the coiling wrist's back."""
    # The old frontal projection contains silhouettes of overlapping oral
    # tendrils. Their shadows must not cross onto separate turning surfaces.
    for name,strength in [('PAINT_Skin_TealAndOlive',.20),('PAINT_Cartilage_Green',.16)]:
        surface=bpy.data.materials[name].node_tree
        shader=next(n for n in surface.nodes if n.type=='BSDF_PRINCIPLED')
        paint_mix=shader.inputs['Base Color'].links[0].from_node
        weight=paint_mix.inputs[0].links[0].from_socket
        opacity=surface.nodes.new('ShaderNodeMath')
        opacity.operation='MULTIPLY'
        surface.links.new(weight,opacity.inputs[0])
        opacity.inputs[1].default_value=strength
        surface.links.new(opacity.outputs[0],paint_mix.inputs[0])
        bump=next((n for n in surface.nodes if n.type=='BUMP'),None)
        if bump:
            bump.inputs['Strength'].default_value*=strength
    mat=bpy.data.materials['PAINT_Skin_TealAndOlive']
    nodes,links=mat.node_tree.nodes,mat.node_tree.links
    shader=next(n for n in nodes if n.type=='BSDF_PRINCIPLED')
    previous=shader.inputs['Base Color'].links[0].from_socket
    y,x=np.mgrid[0:2048,0:1024].astype(np.float32)
    u=x/1023
    v=y/2047
    mask=np.zeros_like(u)
    for i,f in enumerate((.29,.39,.51,.62,.74,.84,.91)):
        for row,azimuth in enumerate((.34,.64)):
            center=azimuth+.043*np.sin(2.1*i+row)
            distance=v-f-.024*row
            width=.054-.021*f
            length=.028-.012*f+.005*np.cos(i*1.6+row)
            edge=((u-center-distance*.7)/width)**2+(distance/length)**2
            edge+=.18*np.sin(u*93+v*127)*np.cos(v*79-u*47)+.070*np.sin(279*u-361*v)
            mask=np.maximum(mask,(1-smooth(edge,.87,1.045))*(.74+.13*np.sin(i+row)))
    rgba=np.ones((2048,1024,4),np.float32)
    rgba[:,:,:3]=mask[:,:,None]
    paint=bpy.data.images.new('Longitudinal olive markings',1024,2048,is_data=True)
    paint.pixels.foreach_set(rgba.ravel())
    paint.pack()
    uv=nodes.new('ShaderNodeUVMap')
    uv.uv_map='SurfaceUV'
    spots=image_node(nodes,links,paint,uv,'Dorsal wrist pigment')
    arm=nodes.new('ShaderNodeAttribute')
    arm.attribute_name='ArmParameter'
    gate=nodes.new('ShaderNodeMath')
    gate.operation='GREATER_THAN'
    links.new(arm.outputs['Fac'],gate.inputs[0])
    gate.inputs[1].default_value=.18
    weight=nodes.new('ShaderNodeMath')
    weight.operation='MULTIPLY'
    links.new(gate.outputs[0],weight.inputs[0])
    links.new(spots.outputs['Color'],weight.inputs[1])
    mix=nodes.new('ShaderNodeMixRGB')
    links.new(weight.outputs[0],mix.inputs[0])
    links.new(previous,mix.inputs[1])
    mix.inputs[2].default_value=rgb('a0ad84')
    links.new(mix.outputs[0],shader.inputs['Base Color'])


def forehead_plates(material):
    # Pixel landmarks on the original 1536 x 1024 concept. The varying outlines
    # carry forward onto the temples via the fixed reference UV and face normal.
    polygons=[
        (197,40,14,3,-21),(226,35,17,4,-17),(265,31,23,5,-5),(177,67,26,11,-32),
        (229,54,17,7,-13),(231,80,24,14,-18),(126,111,26,16,-44),(184,110,26,21,-48),
        (245,120,24,21,15),(86,151,23,14,-62),(144,155,23,21,-66),(215,167,32,26,30),
        (275,172,25,15,63),(109,191,19,12,-52),(300,224,22,9,72),(395,175,19,9,82),
        (378,118,17,7,56),(421,164,22,6,62)]
    olive=[(277,51,21,6,0),(319,76,26,13,18),(338,120,23,15,54),(347,158,11,9,50),(352,180,8,5,80)]
    y,x=np.mgrid[0:286,0:476].astype(np.float32)
    mask=np.zeros((1024,1536,4),np.float32)
    mask[:,:,3]=1
    for component,marks in enumerate((polygons,olive)):
        region=np.zeros_like(x)
        for cx,cy,rx,ry,degrees in marks:
            theta=np.deg2rad(degrees)
            dx,dy=x-cx,y-cy
            u=(dx*np.cos(theta)+dy*np.sin(theta))/rx
            v=(-dx*np.sin(theta)+dy*np.cos(theta))/ry
            angle=np.arctan2(v,u)
            edge=np.sqrt(u*u+v*v)+.045*np.sin(3*angle+.17*cx)+.032*np.sin(7*angle-.2*cy)+.018*np.cos(11*angle)
            region=np.maximum(region,1-smooth(edge,.93,1.045))
        mask[-286:,570:1046,component]=region[::-1]
    im=bpy.data.images.new('Reference forehead plate shapes',1536,1024,is_data=True)
    im.pixels.foreach_set(mask.ravel())
    im.pack()
    ns,ln=material.nodes,material.links
    shader=next(n for n in ns if n.type=='BSDF_PRINCIPLED')
    uv=ns.new('ShaderNodeUVMap');uv.uv_map='ReferencePaintUV'
    tex=image_node(ns,ln,im,uv,'Slate plates and olive forehead markings')
    channels=ns.new('ShaderNodeSeparateColor')
    ln.new(tex.outputs['Color'],channels.inputs[0])
    coverage=ns.new('ShaderNodeAttribute');coverage.attribute_name='ReferencePaintCoverage'
    pigment=shader.inputs['Base Color'].links[0].from_socket
    relief=[]
    for output,col,strength in zip(('Red','Green'),('35404c','a2b18a'),(.87,.68)):
        gate=ns.new('ShaderNodeMath');gate.operation='MULTIPLY'
        ln.new(coverage.outputs['Fac'],gate.inputs[0]);ln.new(channels.outputs[output],gate.inputs[1])
        relief.append(gate.outputs[0])
        opacity=ns.new('ShaderNodeMath');opacity.operation='MULTIPLY'
        ln.new(gate.outputs[0],opacity.inputs[0]);opacity.inputs[1].default_value=strength
        mix=ns.new('ShaderNodeMixRGB')
        ln.new(opacity.outputs[0],mix.inputs[0]);ln.new(pigment,mix.inputs[1])
        mix.inputs[2].default_value=rgb(col);pigment=mix.outputs[0]
    ln.new(pigment,shader.inputs['Base Color'])
    high=ns.new('ShaderNodeMath');high.operation='MAXIMUM'
    for i,val in enumerate(relief):ln.new(val,high.inputs[i])
    bump=ns.new('ShaderNodeBump');bump.inputs['Distance'].default_value=.026
    bump.inputs['Strength'].default_value=.43
    ln.new(shader.inputs['Normal'].links[0].from_socket,bump.inputs['Normal'])
    ln.new(high.outputs[0],bump.inputs['Height']);ln.new(bump.outputs[0],shader.inputs['Normal'])


def paint_ventral_rails():
    # Violet muscle grades back into green skin towards each shoulder.
    tree=bpy.data.materials['PAINT_Ventral_DustyMauve'].node_tree
    ns,ln=tree.nodes,tree.links
    shader=next(n for n in ns if n.type=='BSDF_PRINCIPLED')
    uv=ns.new('ShaderNodeUVMap');uv.uv_map='ArmBakeLayout'
    axes=ns.new('ShaderNodeSeparateXYZ');ln.new(uv.outputs[0],axes.inputs[0])
    dist=ns.new('ShaderNodeMath');dist.operation='PINGPONG'
    ln.new(axes.outputs['X'],dist.inputs[0]);dist.inputs[1].default_value=.5
    tones=ramp(ns,[(.31,'556c61'),(.345,'655c60'),(.388,'806779'),(.46,'765b6c'),(.50,'705665')])
    ln.new(dist.outputs[0],tones.inputs[0])
    ln.new(tones.outputs[0],shader.inputs['Base Color'])


def paint_mantle_and_cups():
    """Carry the forehead pattern into the darker lateral and rear mantle seen in profile."""
    head=bpy.data.materials['PAINT_Cranium_SlateChromatophores'].node_tree
    forehead_plates(head)
    paint_ventral_rails()
    nodes,links=head.nodes,head.links
    shader=next(n for n in nodes if n.type=='BSDF_PRINCIPLED')
    previous=shader.inputs['Base Color'].links[0].from_socket
    position=nodes.new('ShaderNodeNewGeometry').outputs['Position']
    axis=nodes.new('ShaderNodeSeparateXYZ')
    links.new(position,axis.inputs[0])
    posterior=nodes.new('ShaderNodeMapRange')
    posterior.interpolation_type='SMOOTHSTEP'
    for socket,value in [('From Min',-.7),('From Max',2.6),('To Min',0.),('To Max',.65)]:
        posterior.inputs[socket].default_value=value
    links.new(axis.outputs['Y'],posterior.inputs['Value'])
    grain=nodes.new('ShaderNodeTexNoise')
    grain.inputs['Scale'].default_value=3.1
    grain.inputs['Detail'].default_value=3.5
    links.new(position,grain.inputs['Vector'])
    pigment=ramp(nodes,[(.21,'344746'),(.52,'405357'),(.81,'4d6560')])
    links.new(grain.outputs['Fac'],pigment.inputs[0])
    fade=nodes.new('ShaderNodeMixRGB')
    links.new(posterior.outputs['Result'],fade.inputs[0])
    links.new(previous,fade.inputs[1])
    links.new(pigment.outputs[0],fade.inputs[2])
    links.new(fade.outputs[0],shader.inputs['Base Color'])
    # The lip and recessed bowl share the same rose base map. A darker
    # inner tissue keeps the funnel legible under diffuse game lighting.
    bowl=bpy.data.materials['PAINT_Suckers_MauveBowl'].node_tree
    shader=next(n for n in bowl.nodes if n.type=='BSDF_PRINCIPLED')
    previous=shader.inputs['Base Color'].links[0].from_socket
    tint=bowl.nodes.new('ShaderNodeMixRGB')
    tint.blend_type='MULTIPLY'
    tint.inputs[0].default_value=1.
    tint.inputs[2].default_value=(.75,.65,.74,1.)
    bowl.links.new(previous,tint.inputs[1])
    bowl.links.new(tint.outputs[0],shader.inputs['Base Color'])


def paint_surface_detail():
    """Spatial pigment and relief baked at atlas resolution on the actual sculpt."""
    tissues = [
        ('PAINT_Cranium_SlateChromatophores', (1.06,.82,1.10), 13.0, .064, .0015),
        ('PAINT_Skin_TealAndOlive', (1.72,.88,.84), 16.0, .065, .0012),
        ('PAINT_Cartilage_Green', (1.17,.76,1.62), 12.0, .115, .0023),
        ('PAINT_Membrane_PlumGradient', (2.10,1.00,.51), 10.0, .050, .0009),
    ]
    for name, directions, scale, contrast, relief in tissues:
        material=bpy.data.materials[name]
        nodes,links=material.node_tree.nodes,material.node_tree.links
        shader=next(n for n in nodes if n.type=='BSDF_PRINCIPLED')
        position=nodes.new('ShaderNodeNewGeometry').outputs['Position']
        stretch=nodes.new('ShaderNodeVectorMath')
        stretch.operation='MULTIPLY'
        links.new(position,stretch.inputs[0])
        stretch.inputs[1].default_value=directions
        warp=nodes.new('ShaderNodeTexNoise')
        warp.inputs['Scale'].default_value=scale*.63
        warp.inputs['Detail'].default_value=3
        links.new(stretch.outputs[0],warp.inputs['Vector'])
        offset=nodes.new('ShaderNodeVectorMath')
        offset.operation='SCALE'
        links.new(warp.outputs['Color'],offset.inputs[0])
        offset.inputs['Scale'].default_value=.9/scale
        coords=nodes.new('ShaderNodeVectorMath')
        coords.operation='ADD'
        links.new(stretch.outputs[0],coords.inputs[0])
        links.new(offset.outputs[0],coords.inputs[1])
        cells=nodes.new('ShaderNodeTexVoronoi')
        cells.feature='DISTANCE_TO_EDGE'
        cells.inputs['Scale'].default_value=scale
        links.new(coords.outputs[0],cells.inputs['Vector'])
        bevel=nodes.new('ShaderNodeValToRGB')
        bevel.color_ramp.interpolation='EASE'
        for stop,where,value in zip(bevel.color_ramp.elements,(.014,.105),(.16,.94)):
            stop.position=where
            stop.color=(value,value,value,1.)
        links.new(cells.outputs['Distance'],bevel.inputs[0])
        existing=shader.inputs['Base Color'].links[0].from_socket
        glaze=nodes.new('ShaderNodeMixRGB')
        glaze.blend_type='MULTIPLY'
        glaze.inputs[0].default_value=contrast
        links.new(existing,glaze.inputs[1])
        links.new(bevel.outputs[0],glaze.inputs[2])
        links.new(glaze.outputs[0],shader.inputs['Base Color'])
        pebble=nodes.new('ShaderNodeBump')
        pebble.inputs['Distance'].default_value=relief
        pebble.inputs['Strength'].default_value=.47
        if shader.inputs['Normal'].is_linked:
            links.new(shader.inputs['Normal'].links[0].from_socket,pebble.inputs['Normal'])
        links.new(bevel.outputs[0],pebble.inputs['Height'])
        links.new(pebble.outputs[0],shader.inputs['Normal'])
        # Preserve the reference pigment at portrait distance. A subtle sheen
        # difference follows its edges on the forehead and major olive spots.
        luminance=nodes.new('ShaderNodeRGBToBW')
        links.new(existing,luminance.inputs[0])
        tone=nodes.new('ShaderNodeMapRange')
        tone.interpolation_type='SMOOTHSTEP'
        for socket,value in [('From Min',.064),('From Max',.18),('To Min',.57),('To Max',.42)]:
            tone.inputs[socket].default_value=value
        links.new(luminance.outputs[0],tone.inputs['Value'])
        links.new(tone.outputs[0],shader.inputs['Roughness'])
        if 'Cranium' in name or 'Skin' in name:
            pigment_edge=nodes.new('ShaderNodeBump')
            pigment_edge.inputs['Distance'].default_value=.028 if 'Cranium' in name else .012
            pigment_edge.inputs['Strength'].default_value=.38
            links.new(tone.outputs[0],pigment_edge.inputs['Height'])
            links.new(pebble.outputs[0],pigment_edge.inputs['Normal'])
            links.new(pigment_edge.outputs[0],shader.inputs['Normal'])



def image_node(ns,ln,im,uv,label):
    node=ns.new('ShaderNodeTexImage');node.image=im;node.label=label;node.interpolation='Linear';node.extension='EXTEND'
    ln.new(uv.outputs['UV'],node.inputs['Vector'])
    return node


def new_material(name,color,rough):
    mat=bpy.data.materials.new(name)
    mat.diffuse_color=rgb(color)
    p=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    p.inputs['Base Color'].default_value=rgb(color)
    p.inputs['Roughness'].default_value=rough
    p.inputs['Specular IOR Level'].default_value=.24
    p.inputs['Subsurface Weight'].default_value=.015
    return mat,p,mat.node_tree.nodes,mat.node_tree.links


def skin_material(name,maps,head=False,ridge=False):
    mat,p,ns,ln=new_material(name,'5b7368' if not ridge else '687e68',.49)
    uv=ns.new('ShaderNodeUVMap');uv.uv_map='ReferencePaintUV'
    coord=ns.new('ShaderNodeTexCoord')
    n=ns.new('ShaderNodeTexNoise');n.inputs['Scale'].default_value=6.5;n.inputs['Detail'].default_value=2
    ln.new(coord.outputs['Object'],n.inputs['Vector'])
    col=ramp(ns,[(.15,'4e6862'),(.5,'5e786b'),(.85,'718576')] if not ridge else [(.15,'536e5e'),(.5,'748871'),(.85,'8b9a7a')])
    ln.new(n.outputs['Fac'],col.inputs[0])
    # Fallback has restrained pigmentation, not the old random camouflage spots.
    if head:
        vor=ns.new('ShaderNodeTexVoronoi');vor.inputs['Scale'].default_value=1.45
        ln.new(coord.outputs['Object'],vor.inputs['Vector'])
        r=ramp(ns,[(.27,'394b56'),(.42,'394b56'),(.47,'607669'),(.65,'657b6d')])
        ln.new(vor.outputs['Distance'],r.inputs[0]);fallback=r.outputs[0]
    else:fallback=col.outputs[0]
    prefix='Head' if head else 'Skin'
    paint=image_node(ns,ln,maps[prefix+'_Albedo'],uv,'Pigment placement and small brush marks from reference')
    mask=image_node(ns,ln,maps[prefix+'_Coverage'],uv,'Material-aware reference mask')
    attr=ns.new('ShaderNodeAttribute');attr.attribute_name='ReferencePaintCoverage'
    mult=ns.new('ShaderNodeMath');mult.operation='MULTIPLY';ln.new(mask.outputs['Color'],mult.inputs[0]);ln.new(attr.outputs['Fac'],mult.inputs[1])
    mix=ns.new('ShaderNodeMixRGB');ln.new(mult.outputs[0],mix.inputs[0]);ln.new(fallback,mix.inputs[1]);ln.new(paint.outputs['Color'],mix.inputs[2]);ln.new(mix.outputs[0],p.inputs['Base Color'])
    rough=image_node(ns,ln,maps['Skin_Roughness'],uv,'Roughness — data, not color')
    ln.new(rough.outputs['Color'],p.inputs['Roughness'])
    ht=image_node(ns,ln,maps[prefix+'_Height'],uv,'Small pigment-edge relief; no image displacement')
    bump=ns.new('ShaderNodeBump');bump.inputs['Strength'].default_value=.17;bump.inputs['Distance'].default_value=.018
    ln.new(ht.outputs['Color'],bump.inputs['Height']);ln.new(bump.outputs[0],p.inputs['Normal'])
    mat['paint_method']='Fixed UV pigment mask; broad source illumination removed; 3D-lit Principled'
    mat['inferred']='Side/back pigment from procedural continuation; no back reference'
    return mat


def uv_material(name,maps,prefix,rough):
    mat,p,ns,ln=new_material(name,'a27c8c' if prefix=='Cup' else '705063',rough)
    uv=ns.new('ShaderNodeUVMap');uv.uv_map='SurfaceUV'
    c=image_node(ns,ln,maps[prefix+'_Albedo'],uv,'Authored tissue pigment')
    r=image_node(ns,ln,maps[prefix+'_Roughness'],uv,'Smooth tissue roughness')
    ln.new(c.outputs['Color'],p.inputs['Base Color']);ln.new(r.outputs['Color'],p.inputs['Roughness'])
    p.inputs['Subsurface Weight'].default_value=.035 if prefix=='Cup' else .012
    return mat


def assign(maps):
    skins={
        'Skin —':skin_material('PAINT_Skin_TealAndOlive',maps),
        'Cranium —':skin_material('PAINT_Cranium_SlateChromatophores',maps,head=True),
        'Cartilage and':skin_material('PAINT_Cartilage_Green',maps,ridge=True),
        'Sucker lip —':uv_material('PAINT_Suckers_RoseLip',maps,'Cup',.34),
        'Sucker recessed':uv_material('PAINT_Suckers_MauveBowl',maps,'Cup',.37),
        'Wing membrane —':uv_material('PAINT_Membrane_PlumGradient',maps,'Wing',.53),
    }
    belly,p,ns,ln=new_material('PAINT_Ventral_DustyMauve','674c62',.5)
    skins['Ventral tissue —']=belly
    for ob in bpy.data.collections['HIGH'].objects:
        if ob.type!='MESH':continue
        uv_and_coverage(ob)
        for i,mat in enumerate(ob.data.materials):
            for prefix,new in skins.items():
                if mat.name.startswith(prefix):ob.data.materials[i]=new;break
    # Eyes remain the only luminous surface.
    eye=next(m for m in bpy.data.materials if m.name.startswith('Eyes —'))
    p=next(n for n in eye.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    p.inputs['Emission Strength'].default_value=2.3


def main():
    if not bpy.app.background:raise RuntimeError('Run isolated against the saved master.')
    if bpy.data.objects.get('CR_WingedCthulhu') is None:raise RuntimeError('Wrong sculpt file')
    if bpy.data.objects['CR_WingedCthulhu'].get('paint_revision'):
        raise RuntimeError('Start from the pre-paint sculpt, not an already repainted file')
    TEX.mkdir(exist_ok=True);ART.mkdir(exist_ok=True)
    backup=ART/'pre-texture-refinement.blend'
    if not backup.exists():shutil.copy2(bpy.data.filepath,backup)
    for fname in ('reference-view.png','face-detail.png','three-quarter.png'):
        target=ART/('pre-texture-'+fname)
        if not target.exists():shutil.copy2(REVIEW/fname,target)
    print('AUTHORING_REFERENCE_PIGMENTS',flush=True)
    maps=make_paint();maps.update(make_cup());maps.update(make_membrane())
    print('ASSIGNING_FIXED_MESH_UV_AND_MATERIALS',flush=True)
    assign(maps)
    root=bpy.data.objects['CR_WingedCthulhu'];root['paint_revision']='Reference pigment masks v2'
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type=='VIEW_3D':
                area.spaces.active.region_3d.view_perspective='CAMERA'
                area.spaces.active.region_3d.view_camera_zoom=24
                area.spaces.active.shading.type='MATERIAL'
    manifest={
        'method':'Reference-derived pigment masks and high-frequency paint; no full photograph/emission projection',
        'source_resolution':[1536,1024],
        'resolution_note':'4096 maps are storage/interpolation resolution, not 4096 pixels of source evidence',
        'color_management':'Albedo sRGB, coverage/height/roughness Non-Color',
        'coverage':'Fixed front mesh UVs; inferred side/back 3D pigment; no runtime UV bake',
        'geometry_changed':False,
        'textures':[{'file':Path(im.filepath).name,'size':list(im.size),'color_space':im.colorspace_settings.name,'packed':bool(im.packed_file)} for im in maps.values()],
        'source_sha256':hashlib.sha256((OUT/'ref/user-reference.png').read_bytes()).hexdigest(),
    }
    (OUT/'texture-manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf8')
    s=bpy.context.scene;s.camera=bpy.data.objects['CAM_Reference'];s.cycles.device='CPU'
    s.cycles.samples=64;s.render.resolution_x=1536;s.render.resolution_y=1024;s.render.resolution_percentage=100
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'CR_WingedCthulhu_textured.blend'))
    s.cycles.samples=24;s.render.resolution_percentage=65
    s.render.filepath=str(ART/'texture-preview.png');bpy.ops.render.render(write_still=True)
    print('PAINT_DONE',flush=True)


if __name__=='__main__':main()
