"""Metre-scaled materials for the compound art edition. Colours are sRGB swatches."""
import bpy


def colour(hex_value):
    values = [int(hex_value[i:i+2],16)/255 for i in (0,2,4)]
    return tuple(v/12.92 if v<=.04045 else ((v+.055)/1.055)**2.4 for v in values)+(1,)


class Surface:
    def __init__(self,name,swatch,metal=0):
        self.mat = bpy.data.materials.new('FC Art | '+name)
        self.mat.diffuse_color = colour(swatch)
        self.mat.use_nodes = True
        self.tree = self.mat.node_tree
        self.bs = next(n for n in self.tree.nodes if n.type=='BSDF_PRINCIPLED')
        self.bs.inputs['Base Color'].default_value = colour(swatch)
        self.bs.inputs['Metallic'].default_value = metal
        self.coords = self.node('ShaderNodeNewGeometry').outputs['Position']

    def node(self,kind,label=''):
        node = self.tree.nodes.new(kind)
        node.label = label
        return node

    def feed(self,source,target):
        if isinstance(source,bpy.types.NodeSocket):
            self.tree.links.new(source,target)
        else:
            target.default_value = source

    def op(self,operation,a,b=0,label=''):
        n = self.node('ShaderNodeMath',label)
        n.operation = operation
        self.feed(a,n.inputs[0])
        self.feed(b,n.inputs[1])
        return n.outputs[0]

    def remap(self,socket,low,high,left,right,label=''):
        n = self.node('ShaderNodeMapRange',label)
        for key,v in zip(('Value','From Min','From Max','To Min','To Max'),(socket,low,high,left,right)):
            self.feed(v,n.inputs[key])
        return n.outputs[0]

    def mix(self,f,a,b,label=''):
        n = self.node('ShaderNodeMixRGB',label)
        for i,v in enumerate((f,a,b)):
            self.feed(colour(v) if isinstance(v,str) else v,n.inputs[i])
        return n.outputs[0]

    def scale(self,vector):
        n = self.node('ShaderNodeVectorMath')
        n.operation = 'MULTIPLY'
        self.feed(self.coords,n.inputs[0])
        n.inputs[1].default_value = vector
        return n.outputs[0]

    def noise(self,scale,coord=None,detail=3):
        n = self.node('ShaderNodeTexNoise')
        self.feed(coord if coord is not None else self.coords,n.inputs['Vector'])
        n.inputs['Scale'].default_value = scale
        n.inputs['Detail'].default_value = detail
        return n.outputs['Fac']

    def scan(self,folder,channel,coord):
        path = folder/(channel+'.jpg')
        im = bpy.data.images.load(str(path),check_existing=True)
        im.name = folder.name+' '+channel
        im.colorspace_settings.name = 'sRGB' if channel=='Diffuse' else 'Non-Color'
        if not im.packed_file:
            im.pack()
        n = self.node('ShaderNodeTexImage',folder.name+' / '+channel)
        n.image = im
        n.projection = 'BOX'
        n.projection_blend = .18
        self.feed(coord,n.inputs['Vector'])
        return n.outputs['Color']

    def finish(self,base,rough,height=None,distance=.012):
        self.feed(base,self.bs.inputs['Base Color'])
        self.feed(rough,self.bs.inputs['Roughness'])
        if height is not None:
            n = self.node('ShaderNodeBump')
            n.inputs['Strength'].default_value = .45
            n.inputs['Distance'].default_value = distance
            self.feed(height,n.inputs['Height'])
            self.feed(n.outputs['Normal'],self.bs.inputs['Normal'])
        # Lay out the actual socket dependencies so the packaged shaders are editable.
        out = next(n for n in self.tree.nodes if n.type=='OUTPUT_MATERIAL')
        depths = {}
        def visit(node,layer):
            if depths.get(node,-1)>=layer:
                return
            depths[node] = layer
            for socket in node.inputs:
                for link in socket.links:
                    visit(link.from_node,layer+1)
        visit(out,0)
        rows = {}
        for node,layer in depths.items():
            row = rows.get(layer,0)
            node.location = (-layer*275,-row*350)
            rows[layer] = row+1
        return self.mat


def finish_metal(name,dark,light,rough,metal=0):
    s = Surface(name,light,metal)
    fleck = s.noise(28,detail=2)
    mask = s.remap(s.noise(2.2),.23,.78,.20,.86)
    return s.finish(s.mix(mask,dark,light),s.remap(fleck,.18,.8,rough-.08,rough+.09),fleck,.0025)


def scan_material(root,name,texture,scale,palette,values,rough,depth,band=None,plaza=False):
    s = Surface(name,palette[1])
    uv = s.scale((1/scale,)*3)
    folder = root/'textures'/texture
    scan = s.scan(folder,'Diffuse',uv)
    height = s.scan(folder,'Displacement',uv)
    gloss = s.scan(folder,'Rough',uv)
    tone = s.remap(scan,*values,.07,.96,'Scanned albedo range')
    base = s.mix(tone,*palette,'Mineral colour')
    if texture=='concrete_wall_009':
        # Real flaked aggregate appears in local islands, at a different repeat
        # distance to the formwork. Most of the cast plane stays intact.
        spalls=s.scan(root/'textures'/'preconcrete_wall_001','Diffuse',s.scale((.61,.61,.61)))
        spall_dark=s.remap(spalls,.20,.35,.82,0)
        patch=s.remap(s.noise(.55,detail=4),.39,.61,0,.88)
        base=s.mix(s.op('MULTIPLY',patch,spall_dark),base,'797564','Local weathering')
    if texture in ('concrete_floor_worn_001','asphalt_04'):
        dust=s.remap(s.noise(.17,detail=4),.34,.70,0,.18,'Broad patches of deposited dust')
        base=s.mix(dust,base,'a39a84')
    if band:
        tc = s.node('ShaderNodeTexCoord')
        sep = s.node('ShaderNodeSeparateXYZ')
        s.feed(tc.outputs['Generated'],sep.inputs[0])
        z = sep.outputs['Z']
        edge = s.op('ADD',z,s.op('MULTIPLY',s.noise(5),.022))
        cutoff,paint = band
        inside = s.remap(edge,cutoff-.003,cutoff+.005,1,0,'Lower painted course')
        aging = s.remap(s.noise(1.45,s.scale((1.0,1.0,.72))),.30,.72,.65,.94)
        inside = s.op('MULTIPLY',inside,aging)
        pigment = s.mix(s.remap(tone,0,1,.08,.37),paint,base,'Paint texture')
        base = s.mix(inside,base,pigment,'Surviving paint')
        dirt = s.op('MULTIPLY',s.remap(z,.003,.12,.45,0),s.noise(1.7))
        base = s.mix(dirt,base,'514d40','Damp at foot')
    if plaza:
        axes = s.node('ShaderNodeSeparateXYZ')
        s.feed(s.coords,axes.inputs[0])
        x = s.op('ADD',axes.outputs['X'],5.25)
        y = s.op('SUBTRACT',axes.outputs['Y'],7.4)
        def joint(v,size):
            frac = s.op('FRACT',s.op('DIVIDE',v,size))
            return s.op('MULTIPLY',s.op('MINIMUM',frac,s.op('SUBTRACT',1.,frac)),size)
        joints = s.op('MINIMUM',joint(x,5.25),joint(y,4.475))
        seam = s.remap(joints,.007,.028,.72,0,'Cast slab joints')
        slab=s.node('ShaderNodeCombineXYZ','Individual paving stones')
        for axis,coord,length in [('X',x,5.25),('Y',y,4.475)]:
            s.feed(s.op('FLOOR',s.op('DIVIDE',coord,length)),slab.inputs[axis])
        random=s.node('ShaderNodeTexWhiteNoise')
        s.feed(slab.outputs[0],random.inputs['Vector'])
        base=s.mix(s.remap(random.outputs['Value'],0,1,.015,.15),base,'746f65','Slab variation')
        # Signed distance from the tangent-arc outer edge of the existing mesh.
        dx = s.op('SUBTRACT',s.op('ABSOLUTE',x),15.75-2.32)
        dy = s.op('SUBTRACT',s.op('ABSOLUTE',y),17.9-2.32)
        px,py = s.op('MAXIMUM',dx,0),s.op('MAXIMUM',dy,0)
        corner = s.op('SQRT',s.op('ADD',s.op('MULTIPLY',px,px),s.op('MULTIPLY',py,py)))
        edge = s.op('SUBTRACT',2.32,s.op('ADD',corner,s.op('MINIMUM',s.op('MAXIMUM',dx,dy),0)))
        border = s.remap(edge,.74,.77,.76,0,'Darker perimeter course')
        base = s.mix(border,base,s.mix(tone,'767973','9b9a87'))
        base = s.mix(seam,base,'535653')
        height = s.op('SUBTRACT',height,s.op('MULTIPLY',seam,.72))
    return s.finish(base,s.remap(gloss,0,1,rough-.16,min(1,rough+.10)),height,depth)


def make_palette(root):
    wall = 'concrete_wall_009'
    floor = 'concrete_floor_worn_001'
    def concrete(name,a,b,band=None):
        return scan_material(root,name,wall,2.85,(a,b),(.270,.367),.82,.019,band)
    m = {
        'cast':concrete('Cast limestone concrete','877f70','cfc5ad'),
        'cap':concrete('Pale precast coping','9b9483','dfd5bd'),
        'foot':concrete('Weathered plinth','5a5a50','8a8776'),
        'facade':concrete('Main facades · aged lower course','958a77','c8bca1',(.28,'626c64')),
        'wall':concrete('Enclosure · patinated base','827e72','beb7a2',(.26,'62685e')),
        'equipment':concrete('Equipment · petrol blue paint','9d9380','d1c7b2',(.47,'507779')),
        'medical':concrete('Medical · sage enamel','a9a18c','d8d0bc',(.48,'79a19a')),
        'armory':concrete('Stores · oxide paint','948673','cbb79a',(.44,'9d6247')),
        'b2wall':concrete('Comms · mineral render','9a9280','c8bfa9',(.25,'52726f')),
        'yard':scan_material(root,'Rolled yard asphalt','asphalt_04',4.04,('555b5c','87847b'),(.10,.33),.82,.026),
        'paving':scan_material(root,'Jointed plaza paving',floor,3.4,('928e83','cfc4af'),(.078,.111),.77,.019,plaza=True),
        'shopfloor':scan_material(root,'Sealed workshop floor',floor,3.0,('67716c','9e9f91'),(.079,.112),.68,.013),
        'apron':scan_material(root,'Boarded loading apron',floor,2.8,('777b71','aaa796'),(.079,.112),.83,.020),
        'roof':scan_material(root,'Mineral felt roofing',floor,2.5,('566262','858883'),(.078,.112),.84,.009),
        'broken':scan_material(root,'Fractured aggregate','preconcrete_wall_001',2.0,('82786a','c3af95'),(.25,.67),.91,.045),
    }
    finishes = [
        ('iron','Graphite structural steel','303f42','536061',.58,0),
        ('galv','Galvanised wear surfaces','969c97','b9bfb8',.51,1),
        ('teal','Painted petrol-blue steel','355958','537978',.54,0),
        ('amber','Faded ochre safety paint','9d783c','ccab66',.67,0),
        ('oxide','Red oxide primer','85533e','b07a55',.78,0),
        ('olive','Olive transport case','58604b','81866c',.67,0),
        ('canvas','Woven field canvas','575b48','8a8769',.88,0),
        ('webbing','Woven olive webbing','464c3d','706f52',.90,0),
        ('casecap','Case moulding','434c46','666f62',.61,0),
        ('ivory','Warm ivory enamel','c1c1ad','e9e1c6',.51,0),
        ('rubber','Gaskets and cable','272f30','414746',.80,0),
        ('red','Emergency vermilion','9c4136','c35d4b',.55,0),
        ('wood','Oiled plywood','855f42','ad865e',.71,0),
        ('glass','Smoked blue-green glazing','223c41','365358',.23,.16),
        ('ink','Weathered stencil','525a50','686e63',.83,0),
        ('dome0','Radome · chalk','c6c5af','e0d9c3',.64,0),
        ('dome1','Radome · pale grey','c4c7bb','dddcc9',.65,0),
        ('dome2','Radome · warm grey','bbbcae','d4d2be',.63,0),
        ('domejoint','Radome · elastomer joint','6e7672','858980',.77,0),
    ]
    for key,name,a,b,rough,metal in finishes:
        m[key] = finish_metal(name,a,b,rough,metal)
    return m


def assign(scene,palette):
    replacements = {'FC_Concrete':'cast','FC_Coping':'cap','FC_Foundation':'foot',
                    'FC_Pavement':'yard','FC_Roofing':'roof','FC_Galvanized':'iron',
                    'FC_FadedSafetyYellow':'amber','FC_Charcoal':'rubber','FC_DomeWhite':'ivory',
                    'FC_OliveDrab':'olive','FC_Sandbags':'canvas','FC_MedicalCase':'red',
                    'FC_SmokedGlass':'glass','FC_FadedIdentifier':'ink'}
    for o in scene.objects:
        if o.type not in {'MESH','FONT'}:
            continue
        for slot in o.material_slots:
            if slot.material:
                key = replacements.get(slot.material.name.split('.')[0])
                if key:
                    slot.material = palette[key]
        name = o.name
        choice = None
        if name=='Paved plaza':
            choice='paving'
        elif name in ('03_A1 body','04_B1 body','05_C1 body'):
            choice='facade'
        elif name.startswith('Wall ') and name[5:7].isdigit() or name.startswith('Tower ') and 'shaft' in name:
            choice='wall'
        elif name.endswith('curved warehouse walls'):
            choice=name.split()[0].lower()
        elif name=='Armory east wall':
            choice='armory'
        elif name in ('Equipment floor','Medical floor','Armory floor'):
            choice='shopfloor'
        elif name=='B2 drum':
            choice='b2wall'
        elif name.startswith(('Gallery mullion','Warehouse horizontal tank','B1 utility tank','B2 west uplink','B2 east uplink')):
            if any(t in name for t in ('band','saddle','mount','tripod','feed','receiver')):
                continue
            choice='galv' if 'uplink' not in name else 'ivory'
        elif name.startswith('Collapsed concrete'):
            choice='broken'
        elif name.startswith(('Hanging armor vest','Vest pouch')):
            choice='canvas'
        elif name.startswith(('Equipment case','Ammunition crate','Medical supply case','Long weapon transport case')):
            if 'clasp' in name:
                choice='galv'
            elif name.startswith('Medical supply case') and not any(k in name for k in ('corner','handle','rim')):
                choice='teal'
        if choice:
            o.material_slots[0].material=palette[choice]
