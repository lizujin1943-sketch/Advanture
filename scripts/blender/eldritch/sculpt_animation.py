"""Anatomical study takes: asymmetric strikes, articulated digits and delayed fin waves."""
import math
import bpy
import animation


CLIPS = [('Idle', 90), ('Move', 42), ('Windup', 45), ('Attack', 18),
         ('Recover', 30), ('Hit', 15), ('Emerge', 45), ('Death', 45)]


def pose(rig, model, clip, t):
    looping = clip in ('Idle', 'Move')
    base = clip if clip in ('Idle', 'Move', 'Hit', 'Emerge', 'Death') else 'Idle'
    animation.pose(rig, model, 0, base, t if looping or base != 'Idle' else 0)
    smooth = animation.smooth
    wind = smooth(t) if clip == 'Windup' else 1 - smooth(t / .45) if clip == 'Attack' else 0
    strike = smooth(t / .72) if clip == 'Attack' else 1 - smooth(t) if clip == 'Recover' else 0
    phase = t * math.tau if looping else 0
    spine = rig.pose.bones['Spine']
    spine.rotation_euler.x += -.23 * wind + .26 * strike
    spine.rotation_euler.z += -.11 * wind + .13 * strike
    head = rig.pose.bones['Head']
    head.rotation_euler.x += -.10 * wind + .15 * strike
    head.rotation_euler.z += .055 * wind - .065 * strike
    for chain in model.chains:
        role = chain['role']
        left = 'L' in chain['name']
        sign = -1 if left else 1
        for j, name in enumerate(chain['bones']):
            bone = rig.pose.bones[name]
            if role == 'arm':
                if clip in ('Idle', 'Move', 'Windup', 'Attack', 'Recover'):
                    # Determine handedness by chain name: leg/arm list order is interleaved.
                    bone.rotation_euler.x = math.sin(phase - j * .25) * sign * (.17 if clip == 'Move' else .025)
                    bone.rotation_euler.z = math.sin(phase - .4 - j * .35) * .023
                power = .28 if left else 1.0
                bone.rotation_euler.x += power * (-wind * (.90 if j == 0 else .4) + strike * (.95 if j == 0 else .35))
                bone.rotation_euler.z += sign * power * (wind * .22 - strike * .18)
            elif role == 'finger':
                curl = .045 + .018 * math.sin(phase - j * .7 + len(name))
                curl += .11 * wind - .025 * strike
                if clip == 'Death': curl += smooth(t) * .13
                bone.rotation_euler.x += curl
            elif role == 'fin':
                bone.rotation_euler.x = math.sin(phase - j * .8 + (1 if left else 2)) * (.035 + j * .012)
                bone.rotation_euler.z = sign * (-.08 * wind + .11 * strike) * (j + 1) / 3
            elif role == 'tentacle' and not looping:
                bone.rotation_euler.x += -.11 * wind + .20 * strike
    bpy.context.view_layer.update()
    if clip in ('Idle', 'Windup', 'Attack', 'Recover'):
        for chain in model.chains:
            if chain['role'] == 'leg': animation.feet(rig, chain, .31, 0, 0)


def bake(rig, model):
    rig.animation_data_create()
    actions = []
    scene = bpy.context.scene
    for name, frames in CLIPS:
        action = bpy.data.actions.new(model.name + '_' + name)
        action.use_fake_user = True
        rig.animation_data.action = action
        for frame in range(frames + 1):
            scene.frame_set(frame + 1)
            pose(rig, model, name, frame / frames)
            for bone in rig.pose.bones:
                for channel in ('location', 'rotation_euler', 'scale'):
                    bone.keyframe_insert(channel, frame=frame + 1, group=bone.name)
        actions.append(action)
    rig.animation_data.action = actions[0]
    scene.frame_set(1)
    return actions
