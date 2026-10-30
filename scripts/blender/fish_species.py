"""Art-only species configuration. No economy or deterministic gameplay values."""

COMMON_CLIPS = {
    'Idle': (60, True), 'Swim': (30, True), 'Burst': (24, True),
    'Leap': (24, False), 'Dive': (18, False),
    'Hooked': (24, True), 'Flop': (36, True),
}

# Geometry, silhouette and palette come from fish_reference_layout and the reference atlases.
SPECIES = [
    dict(id='fish_01',slug='SilverMullet',name='银背鲻鱼',length=0.42),
    dict(id='fish_02',slug='BlueMackerel',name='蓝纹鲭鱼',length=0.46),
    dict(id='fish_03',slug='RedBream',name='赤尾鲷鱼',length=0.38),
    dict(id='fish_04',slug='SpottedGrouper',name='斑点石斑鱼',length=0.48),
    dict(id='fish_05',slug='Puffer',name='刺球河鲀',length=0.34,spikes=True),
    dict(id='fish_06',slug='Parrotfish',name='翡翠鹦嘴鱼',length=0.39),
    dict(id='fish_07',slug='MoonPomfret',name='月纹鲳鱼',length=0.42),
    dict(id='fish_08',slug='Lionfish',name='紫鳍狮子鱼',length=0.42),
    dict(id='fish_09',slug='GoldenSailfish',name='金帆旗鱼',length=0.68),
    dict(id='fish_10',slug='Ribbonfish',name='星辉皇带鱼',length=0.9),
    dict(id='piranha',slug='Piranha',name='食人鱼',length=0.43),
]

for species in SPECIES:
    species['clips'] = dict(COMMON_CLIPS)
    if species['id'] == 'piranha':
        species['clips']['Bite'] = (18, False)
