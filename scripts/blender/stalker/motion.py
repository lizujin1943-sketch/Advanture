"""Planted quadrupedal locomotion and continuous pounce phases for the approved anatomy."""
import math
import bpy
from mathutils import Vector
from eldritch.geometry import vector

CLIPS = [('Idle', 90), ('Move', 48), ('Windup', 40), ('Attack', 22), ('Recover', 34),
         ('Hit', 15), ('Emerge', 50), ('Death', 50)]


def smooth(t):
    t = max(0, min(1, t)); return t * t * (3 - 2 * t)


def segment(rig, name, a, b):
    pose, rest = rig.pose.bones[name], rig.data.bones[name]
    start, end = vector(a), vector(b)
    rotation = (rest.tail_local - rest.head_local).rotation_difference(end - start)
    matrix = rotation.to_matrix().to_4x4() @ rest.matrix_local
    matrix.translation = start; pose.matrix = matrix
    bpy.context.view_layer.update()


def solve(rig, chain, ankle_offset):
    points = [Vector(p) for p in chain['points']]
    root, knee, ankle = points[:3]
    spine = rig.pose.bones['Spine']
    posed = spine.matrix @ spine.bone.matrix_local.inverted() @ vector(root)
    hip = Vector((posed.x, posed.z, -posed.y))
    target = ankle + ankle_offset
    upper, lower = (knee - root).length, (ankle - knee).length
    axis = (target - hip).normalized()
    distance = min((target - hip).length, (upper + lower) * .995)
    along = (upper * upper - lower * lower + distance * distance) / (2 * distance)
    bend = math.sqrt(max(.00001, upper * upper - along * along))
    pole = knee - root; pole = (pole - axis * pole.dot(axis)).normalized()
    joint = hip + axis * along + pole * bend
    names = chain['bones']
    segment(rig, names[0], hip, joint)
    segment(rig, names[1], joint, target)
    segment(rig, names[2], target, points[3] + target - ankle)


def pose(rig, model, clip, t):
    for bone in rig.pose.bones:
        bone.rotation_mode = 'XYZ'; bone.location = (0, 0, 0)
        bone.rotation_euler = (0, 0, 0); bone.scale = (1, 1, 1)
    phase = t * math.tau if clip in ('Idle', 'Move') else 0
    wind = smooth(t) if clip == 'Windup' else 1 - smooth(t / .40) if clip == 'Attack' else 0
    strike = math.sin(t * math.pi) * .8 + smooth(t) * .25 if clip == 'Attack' else .25 * (1 - smooth(t)) if clip == 'Recover' else 0
    hit = math.sin(math.pi * t) if clip == 'Hit' else 0
    emerge = 1 - smooth(t) if clip == 'Emerge' else 0
    dead = smooth(t) if clip == 'Death' else 0
    spine, head = rig.pose.bones['Spine'], rig.pose.bones['Head']
    bob = math.sin(phase * 2) * .012 if clip == 'Move' else 0
    displacement = Vector((0, bob - .11 * wind + .04 * strike - .18 * emerge - .38 * dead, .12 * strike - .045 * hit))
    spine.location = spine.bone.matrix_local.to_3x3().inverted() @ vector(displacement)
    breath = math.sin(phase) * .005
    spine.scale = (1 + breath, 1, 1 + breath)
    spine.rotation_euler.x = -.035 * wind + .04 * strike + .08 * hit + .16 * dead
    spine.rotation_euler.y = math.sin(phase) * .018 if clip == 'Move' else .06 * hit + .35 * dead
    head.rotation_euler.x = -.08 * wind + .13 * strike - .09 * hit + .17 * emerge + .20 * dead
    head.rotation_euler.z = math.sin(phase + .5) * .018 if clip in ('Idle', 'Move') else 0
    for index, chain in enumerate(model.chains):
        role = chain['role']
        for j, name in enumerate(chain['bones']):
            bone = rig.pose.bones[name]
            if role == 'tail':
                amp = .018 + j / len(chain['bones']) * .03
                bone.rotation_euler.z = math.sin(phase - j * .43) * amp + .025 * wind - .035 * strike + .035 * dead
                bone.rotation_euler.x = math.sin(phase - j * .45 + .5) * amp * .55 - .018 * strike
            elif role == 'feeler':
                bone.rotation_euler.x = math.sin(phase - j * .65 + index) * .065 + .07 * wind - .12 * strike
                bone.rotation_euler.z = math.cos(phase - j * .5 + index) * .04
    bpy.context.view_layer.update()
    for chain in model.chains:
        if chain['role'] != 'leg': continue
        name = chain['name']; front = name.startswith('Front'); left = name.endswith('L')
        offset = Vector((0, 0, 0))
        if clip == 'Move':
            cycle = (t + (0 if front == left else .5)) % 1
            stance = cycle < .64
            travel = 1 - 2 * cycle / .64 if stance else -math.cos((cycle - .64) / .36 * math.pi)
            lift = 0 if stance else math.sin((cycle - .64) / .36 * math.pi) * .10
            offset = Vector((0, lift, travel * .16))
        elif clip == 'Attack' and front:
            swing = math.sin(t * math.pi) ** 2
            offset.y = swing * (.16 if left else .23)
            offset.z = swing * (.24 if left else .35)
        elif clip == 'Death':
            offset.x = (-1 if left else 1) * dead * .15
        solve(rig, chain, offset)


def bake(rig, model):
    rig.animation_data_create(); actions = []
    for name, frames in CLIPS:
        action = bpy.data.actions.new('AbyssalStalker_' + name); action.use_fake_user = True
        rig.animation_data.action = action
        for frame in range(frames + 1):
            bpy.context.scene.frame_set(frame + 1)
            pose(rig, model, name, frame / frames)
            for bone in rig.pose.bones:
                for channel in ('location', 'rotation_euler', 'scale'):
                    bone.keyframe_insert(channel, frame=frame + 1, group=bone.name)
        actions.append(action)
    rig.animation_data.action = actions[0]; bpy.context.scene.frame_set(1)
    return actions
