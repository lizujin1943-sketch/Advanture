"""Art-only species configuration. No economy or deterministic gameplay values."""

COMMON_CLIPS = {
    'Idle': (60, True), 'Swim': (30, True), 'Burst': (24, True),
    'Leap': (24, False), 'Dive': (18, False),
    'Hooked': (24, True), 'Flop': (36, True),
}

# Profile: longitudinal position, lateral radius, vertical radius, centre height.
SLEEK = [(-.64,.025,.035,0),(-.48,.09,.12,0),(-.28,.14,.19,.01),
         (0,.18,.23,.025),(.24,.18,.22,.025),(.43,.13,.17,.01),(.55,.065,.10,0)]
ROUND = [(-.58,.03,.06,0),(-.42,.10,.20,0),(-.23,.18,.31,0),
         (0,.23,.38,0),(.23,.23,.35,.015),(.43,.17,.26,0),(.53,.09,.15,-.025)]

SPECIES = [
    dict(id='fish_01',slug='SilverMullet',label='Silver Mullet',name='银背鲻鱼',
         length=.42,profile=SLEEK,tail='fork',fin='split',expression='sleepy',
         colors=['9AAFA6','E6E4CA','566A43','899F72','D6C781','667C64'],pattern='scales'),
    dict(id='fish_02',slug='BlueMackerel',label='Blue Mackerel',name='蓝纹鲭鱼',
         length=.46,profile=[(x*1.06,y*.83,z*.9,h) for x,y,z,h in SLEEK],
         tail='fork',fin='sharp',expression='keen',
         colors=['3A9FD4','E0E4CA','164A88','3184C1','7BCDE2','133F68'],pattern='stripes'),
    dict(id='fish_03',slug='RedBream',label='Red Bream',name='赤尾鲷鱼',
         length=.38,profile=ROUND,tail='fork',fin='crown',expression='pout',
         colors=['EF755A','F6D6B5','C2443F','E95841','FFB46B','C75848'],pattern='scales'),
    dict(id='fish_04',slug='SpottedGrouper',label='Spotted Grouper',name='斑点石斑鱼',
         length=.48,profile=[(-.63,.05,.10,0),(-.46,.16,.23,0),(-.2,.26,.32,0),
           (.09,.30,.35,0),(.34,.29,.32,0),(.51,.24,.25,-.03),(.60,.15,.19,-.055)],
         tail='round',fin='scallop',expression='grumpy',mouth=1.55,
         colors=['B99648','E4CF87','6A5733','9B792F','DEC36A','594524'],pattern='spots'),
    dict(id='fish_05',slug='Puffer',label='Pufferfish',name='刺球河鲀',
         length=.34,profile=[(-.49,.02,.04,0),(-.36,.14,.22,0),(-.20,.27,.36,0),
           (0,.35,.40,0),(.23,.31,.35,0),(.40,.20,.24,0),(.47,.10,.14,0)],
         tail='round',fin='small',expression='surprised',spikes=True,
         colors=['E8BC50','FFF1C7','AD7B31','E6A83C','FFE28C','865528'],pattern='spots'),
    dict(id='fish_06',slug='Parrotfish',label='Jade Parrotfish',name='翡翠鹦嘴鱼',
         length=.39,profile=[(x*.95,y*.93,z*.91,h) for x,y,z,h in ROUND],
         tail='round',fin='scallop',expression='happy',mouth=1.12,beak=True,
         colors=['27B8A6','9BE5BC','197C85','279CAA','F5A34C','368C80'],pattern='scales'),
    dict(id='fish_07',slug='MoonPomfret',label='Moon Pomfret',name='月纹鲳鱼',
         length=.42,profile=[(-.58,.025,.055,0),(-.40,.07,.22,0),(-.18,.11,.40,0),
           (.02,.13,.45,0),(.23,.12,.36,0),(.43,.09,.20,0),(.52,.045,.11,0)],
         tail='crescent',fin='sickle',expression='gentle',
         colors=['9BBAD7','E3E7D8','516D9C','496FAB','AFCFD7','E9DFAB'],pattern='moon'),
    dict(id='fish_08',slug='Lionfish',label='Violet Lionfish',name='紫鳍狮子鱼',
         length=.42,profile=[(x*.9,y*.9,z*.8,h) for x,y,z,h in ROUND],
         tail='round',fin='lion',expression='pout',
         colors=['995AA2','E3C9CA','623577','A05EA0','EDCBAB','F0D5B9'],pattern='bands'),
    dict(id='fish_09',slug='GoldenSailfish',label='Golden Sailfish',name='金帆旗鱼',
         length=.68,profile=[(x*1.12,y*.82,z*.8,h) for x,y,z,h in SLEEK],
         tail='crescent',fin='sail',expression='keen',bill=True,
         colors=['2F8DC2','DAE1CA','233D78','ECC14B','3E75B1','98D9DF'],pattern='dashes'),
    dict(id='fish_10',slug='Ribbonfish',label='Starlight Ribbonfish',name='星辉皇带鱼',
         length=.90,profile=[(-1.38,.012,.025,-.09),(-1.2,.025,.085,-.12),
          (-.98,.035,.13,-.06),(-.75,.045,.16,.04),(-.50,.055,.17,.11),
          (-.25,.065,.18,.08),(0,.085,.20,0),(.23,.12,.22,.01),(.43,.10,.19,.02),(.53,.055,.115,0)],
         tail='ribbon',fin='ribbon',expression='gentle',
         colors=['B8B7E6','E3E2EF','777FB8','DF596F','F4B9BA','F6EAC1'],pattern='stars'),
    dict(id='piranha',slug='Piranha',label='Piranha',name='食人鱼',
         length=.43,profile=[(-.58,.025,.06,0),(-.40,.12,.22,0),(-.18,.22,.31,.02),
          (.08,.27,.34,.03),(.30,.28,.31,.02),(.48,.23,.24,-.02),(.59,.15,.18,-.06)],
         tail='fork',fin='crown',expression='angry',mouth=1.7,teeth=True,
         colors=['DE672E','F0AF52','803833','A9432A','EF8F37','A44829'],pattern='scales'),
]

for species in SPECIES:
    species['clips'] = dict(COMMON_CLIPS)
    if species['id'] == 'piranha':
        species['clips']['Bite'] = (18, False)
