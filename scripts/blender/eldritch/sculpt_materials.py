"""Skin-study materials: stable rest-space mottling, wrinkles and wet microstructure."""
import bpy


def material(name, color, roughness):
    mat = bpy.data.materials.new('Study_' + name)
    mat.use_nodes = True
    mat.diffuse_color = (*color, 1)
    bs = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    bs.inputs['Base Color'].default_value = (*color, 1)
    bs.inputs['Roughness'].default_value = roughness
    return mat, bs


def create():
    mats = []
    skin, bs = material('LivingSkin', (.13, .19, .17), .44)
    nodes, links = skin.node_tree.nodes, skin.node_tree.links
    rest = nodes.new('ShaderNodeAttribute'); rest.attribute_name = 'RestPosition'
    cloud = nodes.new('ShaderNodeTexNoise')
    cloud.inputs['Scale'].default_value = 9
    cloud.inputs['Detail'].default_value = 5
    cloud.inputs['Roughness'].default_value = .78
    links.new(rest.outputs['Vector'], cloud.inputs['Vector'])
    ramp = nodes.new('ShaderNodeValToRGB')
    ramp.color_ramp.elements[0].position = .20
    ramp.color_ramp.elements[0].color = (.020, .032, .028, 1)
    ramp.color_ramp.elements[1].position = .78
    ramp.color_ramp.elements[1].color = (.065, .085, .068, 1)
    mid = ramp.color_ramp.elements.new(.48); mid.color = (.035, .055, .045, 1)
    links.new(cloud.outputs['Fac'], ramp.inputs[0])
    tissue = nodes.new('ShaderNodeVertexColor'); tissue.layer_name = 'Tissue'
    blend = nodes.new('ShaderNodeMixRGB'); blend.blend_type = 'MULTIPLY'
    blend.inputs[0].default_value = .7
    links.new(ramp.outputs['Color'], blend.inputs[1]); links.new(tissue.outputs['Color'], blend.inputs[2])
    links.new(blend.outputs[0], bs.inputs['Base Color'])
    veins = nodes.new('ShaderNodeTexNoise'); veins.inputs['Scale'].default_value = 48
    veins.inputs['Detail'].default_value = 4; veins.inputs['Roughness'].default_value = .78
    links.new(rest.outputs['Vector'], veins.inputs['Vector'])
    wrinkle = nodes.new('ShaderNodeBump'); wrinkle.inputs['Strength'].default_value = .22
    wrinkle.inputs['Distance'].default_value = .002
    links.new(veins.outputs['Fac'], wrinkle.inputs['Height'])
    pores = nodes.new('ShaderNodeTexVoronoi'); pores.inputs['Scale'].default_value = 230
    links.new(rest.outputs['Vector'], pores.inputs['Vector'])
    micro = nodes.new('ShaderNodeBump'); micro.inputs['Strength'].default_value = .26
    micro.inputs['Distance'].default_value = .0006
    links.new(pores.outputs['Distance'], micro.inputs['Height']); links.new(wrinkle.outputs[0], micro.inputs['Normal'])
    links.new(micro.outputs[0], bs.inputs['Normal'])
    rough = nodes.new('ShaderNodeMapRange'); rough.inputs['To Min'].default_value = .51
    rough.inputs['To Max'].default_value = .64
    links.new(cloud.outputs['Fac'], rough.inputs[0]); links.new(rough.outputs[0], bs.inputs['Roughness'])
    bs.inputs['Subsurface Weight'].default_value = .045
    bs.inputs['Subsurface Scale'].default_value = .035
    bs.inputs['Subsurface Radius'].default_value = (.35, .15, .08)
    bs.inputs['Specular IOR Level'].default_value = .26
    bs.inputs['Coat Weight'].default_value = .025; bs.inputs['Coat Roughness'].default_value = .38
    mats.append(skin)
    for name, color, roughness in [
        ('Osteoderm', (.06, .072, .049), .54),
        ('WornIvory', (.21, .19, .12), .38),
        ('GillTissue', (.038, .012, .016), .36),
        ('MouthDepth', (.001, .0008, .0008), .82),
        ('DeadCornea', (.002, .003, .0025), .30),
        ('Iris', (.08, .095, .075), .32),
        ('Membrane', (.038, .054, .043), .46)]:
        mat, shader = material(name, color, roughness)
        if name == 'MouthDepth': shader.inputs['Specular IOR Level'].default_value = .08
        if name in ('DeadCornea', 'Iris'):
            shader.inputs['Coat Weight'].default_value = .18
            shader.inputs['Coat Roughness'].default_value = .23
        if name in ('GillTissue', 'Membrane'):
            shader.inputs['Subsurface Weight'].default_value = .09
            shader.inputs['Subsurface Scale'].default_value = .012
        if name in ('Osteoderm', 'WornIvory', 'GillTissue', 'Membrane'):
            ns, ls = mat.node_tree.nodes, mat.node_tree.links
            tex = ns.new('ShaderNodeTexNoise'); tex.inputs['Scale'].default_value = 95
            tex.inputs['Detail'].default_value = 3
            bump = ns.new('ShaderNodeBump'); bump.inputs['Strength'].default_value = .25
            bump.inputs['Distance'].default_value = .001
            ls.new(tex.outputs['Fac'], bump.inputs['Height']); ls.new(bump.outputs[0], shader.inputs['Normal'])
        mats.append(mat)
    return mats
