"""Baked in-place skeletal takes with phase-specific anticipation and recovery."""
import math
import bpy
from mathutils import Vector, Matrix
from geometry import vector

CLIPS = [('Idle', 90), ('Move', 36), ('Windup', 45), ('Attack', 18),
         ('Recover', 30), ('Hit', 12), ('Emerge', 45), ('Death', 36)]


def smooth(t):
    t = min(1, max(0, t))
    return t * t * (3 - 2 * t)


def segment(rig, name, a, b):
    bone = rig.pose.bones[name]
    rest = rig.data.bones[name]
    start, end = vector(a), vector(b)
    rotation = (rest.tail_local - rest.head_local).rotation_difference(end - start)
    orientation = rotation.to_matrix().to_4x4() @ rest.matrix_local
    orientation.translation = start
    bone.matrix = orientation
    # Children must solve against the updated parent pose, otherwise ankles drift
    # and toes pivot around the previous frame's knee.
    bpy.context.view_layer.update()


def offset(bone, point):
    return bone.bone.matrix_local.to_3x3().inverted() @ vector(point)


def feet(rig, chain, phase, bob, stride):
    """Two-bone leg solve keeps the ankle at its stance target while the torso bobs."""
    points = [Vector(p) for p in chain['points']]
    root, knee, ankle = points[:3]
    cycle = phase % 1
    stance = cycle < .62
    travel = 1 - cycle / .62 * 2 if stance else -math.cos((cycle - .62) / .38 * math.pi)
    lift = 0 if stance else math.sin((cycle - .62) / .38 * math.pi) * .12
    target = ankle + Vector((0, lift, travel * stride))
    hip = root + Vector((0, bob, 0))
    upper, lower = (knee - root).length, (ankle - knee).length
    axis = (target - hip).normalized()
    distance = min((target - hip).length, (upper + lower) * .997)
    along = (upper * upper - lower * lower + distance * distance) / (2 * distance)
    bend = max(.001, upper * upper - along * along) ** .5
    pole = knee - root
    pole = (pole - axis * pole.dot(axis)).normalized()
    joint = hip + axis * along + pole * bend
    names = chain['bones']
    segment(rig, names[0], hip, joint)
    segment(rig, names[1], joint, target)
    if len(names) > 2:
        tip = points[3] + target - ankle
        segment(rig, names[2], target, tip)


def pose(rig, model, kind, clip, t):
    for bone in rig.pose.bones:
        bone.rotation_mode = 'XYZ'
        bone.location = (0, 0, 0)
        bone.rotation_euler = (0, 0, 0)
        bone.scale = (1, 1, 1)
    wave = math.sin(t * math.tau)
    spine, head = rig.pose.bones['Spine'], rig.pose.bones['Head']
    breathing = math.sin(t * math.tau) * .018
    spine.scale = (1 + breathing, 1 + breathing * .45, 1 + breathing)
    head.rotation_euler.x = math.sin(t * math.tau + .6) * .025
    bob = 0
    anticipation = smooth(t) if clip == 'Windup' else 0
    strike = math.sin(min(1, t / .32) * math.pi / 2) * (1 - smooth((t - .30) / .70)) if clip == 'Attack' else 0
    recovery = 1 - smooth(t) if clip == 'Recover' else 0
    if clip == 'Move':
        bob = abs(math.sin(t * math.tau * 2)) * .022
        spine.location = offset(spine, (0, bob, 0))
        spine.rotation_euler.z = wave * .045
        head.rotation_euler.x = -math.sin(t * math.tau * 2) * .045
    if kind in (3, 4, 6, 8, 9):
        spine.location += offset(spine, (0, wave * .025, 0))
    spine.rotation_euler.x += anticipation * -.18 + strike * .26 + recovery * .10
    head.rotation_euler.x += anticipation * -.12 + strike * .20
    if kind == 1:
        head.rotation_euler.x += anticipation * .38 - strike * .18
        spine.location += offset(spine, (0, -anticipation * .055, -anticipation * .06 + strike * .10))
    if kind == 5:
        inflation = anticipation * .16 + strike * -.12
        spine.scale = (1 + breathing + inflation, 1 + breathing - inflation * .25, 1 + breathing + inflation)
    if kind in (3, 4, 6, 9):
        head.scale = (1 + anticipation * .035, 1 + anticipation * .035, 1 + anticipation * .035)
    for i, chain in enumerate(model.chains):
        role = chain['role']
        for j, name in enumerate(chain['bones']):
            bone = rig.pose.bones[name]
            phase = t * math.tau - j * .72 + i * 1.38
            if role in ('tentacle', 'robe', 'ray'):
                amplitude = .07 if role == 'robe' else .11
                bone.rotation_euler.x = math.sin(phase) * amplitude
                bone.rotation_euler.z = math.cos(phase + .7) * amplitude * .7
                bone.rotation_euler.x += anticipation * (.13 + j * .025) - strike * .26
            elif role == 'arm':
                sign = -1 if i % 2 else 1
                bone.rotation_euler.x = wave * (.20 if clip == 'Move' else .03) * sign
                bone.rotation_euler.x += -anticipation * (.65 if j == 0 else .28) + strike * (.9 if j == 0 else .35)
                bone.rotation_euler.z = math.sin(phase) * .035
                if kind == 6:
                    bone.rotation_euler.z += sign * (-anticipation * .42 - strike * .20)
                if kind == 2:
                    bone.rotation_euler.x += -anticipation * .35 + strike * .45
            elif role in ('leg', 'spider') and clip == 'Move':
                continue
            elif role in ('leg', 'spider'):
                bone.rotation_euler.x = math.sin(phase) * .018 + anticipation * .045
    bpy.context.view_layer.update()
    if clip == 'Move':
        legs = [chain for chain in model.chains if chain['role'] in ('leg', 'spider')]
        for i, chain in enumerate(legs):
            phase = t + ((0, .5, .5, 0)[i] if len(legs) == 4 else (i % 2) * .5)
            feet(rig, chain, phase, bob, .12 if kind == 2 else .18)
    if clip == 'Hit':
        kick = math.sin(min(1, t / .23) * math.pi / 2) * (1 - smooth((t - .2) / .8))
        spine.rotation_euler.x = -.22 * kick
        spine.rotation_euler.z = .13 * kick
        head.rotation_euler.x = -.30 * kick
    if clip == 'Emerge':
        amount = 1 - smooth(t)
        spine.rotation_euler.x += amount * .55
        spine.scale *= 1 - amount * .13
        for chain in model.chains:
            if chain['role'] in ('tentacle', 'robe', 'ray'):
                for name in chain['bones']:
                    rig.pose.bones[name].rotation_euler.x += amount * .35
    if clip == 'Death':
        collapse = smooth(t / .75)
        spine.location += offset(spine, (0, -collapse * .30, 0))
        spine.rotation_euler.x += collapse * 1.0
        spine.rotation_euler.z = collapse * .30
        head.rotation_euler.x += collapse * .45
        for i, chain in enumerate(model.chains):
            for j, name in enumerate(chain['bones']):
                bone = rig.pose.bones[name]
                bone.rotation_euler.x += collapse * .28 + math.sin(t * 17 - j + i) * (1 - t) * .07
    bpy.context.view_layer.update()


def bake(rig, model, kind):
    rig.animation_data_create()
    actions = []
    scene = bpy.context.scene
    for name, frames in CLIPS:
        action = bpy.data.actions.new(model.name + '_' + name)
        action.use_fake_user = True
        rig.animation_data.action = action
        for frame in range(frames + 1):
            scene.frame_set(frame + 1)
            pose(rig, model, kind, name, frame / frames)
            for bone in rig.pose.bones:
                bone.keyframe_insert('location', frame=frame + 1, group=bone.name)
                bone.keyframe_insert('rotation_euler', frame=frame + 1, group=bone.name)
                bone.keyframe_insert('scale', frame=frame + 1, group=bone.name)
        actions.append(action)
    rig.animation_data.action = actions[0]
    scene.frame_set(1)
    return actions
