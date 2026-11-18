"""Tileable skin microstructure, damp cavities and restrained bioluminescence."""
import math
import bpy
import numpy as np


def image(name, values, folder, color=True, alpha=None):
    height, width = values.shape[:2]
    img = bpy.data.images.new(name, width, height, alpha=True)
    img.colorspace_settings.name = 'sRGB' if color else 'Non-Color'
    rgba = np.ones((height, width, 4), dtype=np.float32)
    if values.ndim == 2:
        rgba[:, :, :3] = values[:, :, None]
    else:
        rgba[:, :, :3] = values
    if alpha is not None:
        rgba[:, :, 3] = alpha
    img.pixels.foreach_set(rgba.reshape(-1))
    img.filepath_raw = str(folder / (name + '.png'))
    img.file_format = 'PNG'
    img.save()
    img.pack()
    return img


def textures(folder):
    n = 512
    rng = np.random.default_rng(7412)
    yy, xx = np.mgrid[0:n, 0:n] / n
    field = np.zeros((n, n))
    for cells, amplitude in ((5, .32), (13, .20), (39, .12), (117, .05)):
        grid = rng.random((cells, cells))
        x, y = xx * cells, yy * cells
        xi, yi = x.astype(int), y.astype(int)
        u, v = x - xi, y - yi
        u, v = u * u * (3 - 2 * u), v * v * (3 - 2 * v)
        noise = (grid[yi % cells, xi % cells] * (1 - u) * (1 - v)
                 + grid[yi % cells, (xi + 1) % cells] * u * (1 - v)
                 + grid[(yi + 1) % cells, xi % cells] * (1 - u) * v
                 + grid[(yi + 1) % cells, (xi + 1) % cells] * u * v)
        field += amplitude * noise
    creases = np.power(np.abs(np.sin(xx * math.tau * 27 + np.sin(yy * math.tau * 5))), 18)
    pores = np.power(np.clip(rng.random((n, n)) - .89, 0, 1) / .11, 2)
    height = field - .045 * creases - .055 * pores
    color = np.clip(.31 + field * 1.1 - creases * .09 - pores * .14, 0, 1)
    dx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * 6
    dy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * 6
    normals = np.stack((-dx, -dy, np.ones_like(dx)), axis=-1)
    normals /= np.linalg.norm(normals, axis=-1, keepdims=True)
    roughness = np.clip(.22 + field * .55 + pores * .2, .15, .65)
    image('AbyssSkin_MetallicSmoothness', np.zeros_like(roughness), folder, False, 1 - roughness)
    return {'albedo': image('AbyssSkin_Albedo', color, folder),
            'normal': image('AbyssSkin_Normal', normals * .5 + .5, folder, False),
            'roughness': image('AbyssSkin_Roughness', roughness, folder, False)}


def create(name, palette, maps, index):
    colors = [(*palette, 1), (*(c * .60 for c in palette), 1), (.28, .26, .20, 1),
              (.14, .055, .05, 1), (.008, .012, .012, 1),
              ((.29, .45, .055, 1) if index == 5 else (.055, .36, .23, 1))]
    result = []
    for slot, (label, color) in enumerate(zip(['Skin', 'Shell', 'Ivory', 'Flesh', 'Cavity', 'Lumen'], colors)):
        mat = bpy.data.materials.new(name + '_' + label)
        mat.use_nodes = True
        mat.diffuse_color = color
        nodes, links = mat.node_tree.nodes, mat.node_tree.links
        bsdf = next(n for n in nodes if n.type == 'BSDF_PRINCIPLED')
        bsdf.inputs['Base Color'].default_value = color
        bsdf.inputs['Roughness'].default_value = [.36, .49, .43, .22, .30, .19][slot]
        bsdf.inputs['Subsurface Weight'].default_value = .07 if slot in (0, 3) else 0
        if slot < 4:
            tex = nodes.new('ShaderNodeTexImage')
            tex.image = maps['albedo']
            mix = nodes.new('ShaderNodeMixRGB')
            mix.blend_type = 'MULTIPLY'
            mix.inputs[0].default_value = 1
            mix.inputs[2].default_value = color
            links.new(tex.outputs['Color'], mix.inputs[1])
            links.new(mix.outputs[0], bsdf.inputs['Base Color'])
            normal_tex = nodes.new('ShaderNodeTexImage')
            normal_tex.image = maps['normal']
            normal = nodes.new('ShaderNodeNormalMap')
            normal.inputs['Strength'].default_value = .55
            links.new(normal_tex.outputs['Color'], normal.inputs['Color'])
            links.new(normal.outputs['Normal'], bsdf.inputs['Normal'])
            rough = nodes.new('ShaderNodeTexImage')
            rough.image = maps['roughness']
            links.new(rough.outputs['Color'], bsdf.inputs['Roughness'])
        if slot == 5:
            bsdf.inputs['Emission Color'].default_value = color
            bsdf.inputs['Emission Strength'].default_value = .65
        result.append(mat)
    return result
