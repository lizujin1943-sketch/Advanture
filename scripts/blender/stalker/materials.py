"""Distinct weathered chitin, flexible connective tissue and recessed sensory seams."""
import bpy


def make(name, color, rough):
    mat = bpy.data.materials.new('Stalker_' + name); mat.use_nodes = True
    mat.diffuse_color = (*color, 1)
    bs = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    bs.inputs['Base Color'].default_value = (*color, 1)
    bs.inputs['Roughness'].default_value = rough
    return mat, bs


def build():
    mats = []
    for name, color, rough in [
        ('Sinew', (.065, .030, .027), .43),
        ('Chitin', (.014, .021, .029), .54),
        ('WornEdge', (.13, .12, .092), .63),
        ('Claw', (.10, .102, .087), .37),
        ('Throat', (.004, .002, .002), .85),
        ('Gill', (.016, .065, .059), .52),
        ('Tooth', (.25, .21, .14), .51),
        ('Encrustation', (.25, .24, .18), .83)]:
        mat, bs = make(name, color, rough)
        nodes, links = mat.node_tree.nodes, mat.node_tree.links
        rest = nodes.new('ShaderNodeAttribute'); rest.attribute_name = 'RestPosition'
        tex = nodes.new('ShaderNodeTexNoise'); tex.inputs['Scale'].default_value = 22 if name == 'Chitin' else 32
        tex.inputs['Detail'].default_value = 5
        links.new(rest.outputs['Vector'], tex.inputs['Vector'])
        ramp = nodes.new('ShaderNodeValToRGB')
        ramp.color_ramp.elements[0].position = .18
        ramp.color_ramp.elements[0].color = (*(c * .65 for c in color), 1)
        ramp.color_ramp.elements[1].position = .8
        ramp.color_ramp.elements[1].color = (*(c * 1.4 for c in color), 1)
        links.new(tex.outputs['Fac'], ramp.inputs[0])
        links.new(ramp.outputs[0], bs.inputs['Base Color'])
        if name == 'Chitin':
            edge = nodes.new('ShaderNodeAttribute'); edge.attribute_name = 'ShellEdge'
            mix = nodes.new('ShaderNodeMixRGB')
            links.new(edge.outputs['Fac'], mix.inputs[0]); links.new(ramp.outputs[0], mix.inputs[1])
            mix.inputs[2].default_value = (.13, .12, .085, 1)
            links.new(mix.outputs[0], bs.inputs['Base Color'])
        grain = nodes.new('ShaderNodeTexNoise'); grain.inputs['Scale'].default_value = 180
        grain.inputs['Detail'].default_value = 3
        links.new(rest.outputs['Vector'], grain.inputs['Vector'])
        bump = nodes.new('ShaderNodeBump'); bump.inputs['Strength'].default_value = .3
        bump.inputs['Distance'].default_value = .0015 if name == 'Sinew' else .0025
        links.new(grain.outputs['Fac'], bump.inputs['Height']); links.new(bump.outputs[0], bs.inputs['Normal'])
        bs.inputs['Specular IOR Level'].default_value = .27
        if name == 'Sinew':
            bs.inputs['Subsurface Weight'].default_value = .07
            bs.inputs['Subsurface Scale'].default_value = .015
            bs.inputs['Coat Weight'].default_value = .07
            bs.inputs['Coat Roughness'].default_value = .33
        if name == 'Throat': bs.inputs['Specular IOR Level'].default_value = .02
        if name == 'Gill':
            bs.inputs['Emission Color'].default_value = (.015, .095, .075, 1)
            bs.inputs['Emission Strength'].default_value = .16
        mats.append(mat)
    return mats
