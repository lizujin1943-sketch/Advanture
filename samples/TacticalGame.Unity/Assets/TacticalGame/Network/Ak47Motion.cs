using UnityEngine;

namespace Advanture.TacticalGame.NetworkView
{
    // Presentation curves are offsets from the exported rig's bind poses.
    internal sealed class Ak47Motion
    {
        private readonly Ak47Model model;
        private readonly bool local;
        private readonly Vector3 bindPosition;
        private readonly Quaternion bindRotation;
        private float age, kick, tilt, gait, stepPhase, landing, airborneBlend;
        private bool sampled, wasAirborne;
        private Vector3 lastPosition;
        private float lastYaw, lastPitch;
        private Vector2 sway;

        internal Ak47Motion(Ak47Model model, bool local)
        {
            this.model = model;
            this.local = local;
            bindPosition = model.Root.localPosition;
            bindRotation = model.Root.localRotation;
        }

        internal void Reset()
        {
            age = kick = tilt = gait = stepPhase = landing = airborneBlend = 0;
            sway = Vector2.zero;
            sampled = wasAirborne = false;
        }

        internal void SampleMovement(Transform viewpoint, bool airborne, float delta)
        {
            var position = viewpoint.position;
            var angles = viewpoint.eulerAngles;
            if (sampled && delta > .0001f)
            {
                Vector3 movement = position - lastPosition;
                movement.y = 0;
                float speed = Mathf.Min(movement.magnitude / delta, 9);
                float blend = 1 - Mathf.Exp(-9 * delta);
                gait = Mathf.Lerp(gait, airborne ? 0 : Mathf.Clamp01(speed / 6.5f), blend);
                stepPhase = Mathf.Repeat(stepPhase + delta * Mathf.Lerp(5.5f, 12.8f, gait), 2 * Mathf.PI);
                if (wasAirborne && !airborne) landing = 1;
                Vector2 turn = new Vector2(Mathf.DeltaAngle(lastPitch, angles.x), Mathf.DeltaAngle(lastYaw, angles.y));
                sway = Vector2.Lerp(sway, Vector2.ClampMagnitude(turn / delta * -.010f, 2.4f), blend);
            }
            sampled = true;
            wasAirborne = airborne;
            lastPosition = position;
            lastYaw = angles.y;
            lastPitch = angles.x;
            airborneBlend = Mathf.Lerp(airborneBlend, airborne ? 1 : 0, 1 - Mathf.Exp(-8 * delta));
        }

        internal void Advance(float delta)
        {
            age += delta;
            kick *= Mathf.Exp(-16 * delta);
            tilt *= Mathf.Exp(-12 * delta);
            landing *= Mathf.Exp(-8 * delta);
        }

        internal void Shoot(ulong sequence)
        {
            kick = Mathf.Min(1.36f, kick + 1);
            // A small, repeatable amount of roll distinguishes individual reports in a burst.
            float variation = ((sequence * 19UL % 31UL) / 30f - .5f) * 1.5f;
            tilt = Mathf.Clamp(tilt + variation, -1.35f, 1.35f);
        }

        internal void Pose(float reload)
        {
            float draw = 1 - Ease(age / .37f);
            float cant = Window(reload, .00f, .17f, .73f, .99f);
            float reach = Window(reload, .09f, .26f, .73f, .92f);
            float remove = Window(reload, .26f, .44f, .54f, .68f);
            float settle = Window(reload, .64f, .70f, .72f, .84f);
            float breath = Mathf.Sin(age * 1.67f);
            float step = Mathf.Sin(stepPhase);
            float bounce = Mathf.Cos(stepPhase * 2) * gait;
            float life = local ? 1 : .40f;
            float presentation = local ? 1 : .30f;
            float calm = 1 - cant * .85f;

            var offset = new Vector3(
                life * calm * (.007f * gait * step + .0016f * breath) - .032f * cant,
                life * calm * (.005f * bounce + .0025f * breath - .016f * landing) + .116f * cant * presentation - .087f * draw + .006f * settle,
                -.048f * kick + .14f * cant * presentation - .036f * draw);
            var angles = new Vector3(
                -6.2f * kick - 7f * cant + 19f * draw + life * (sway.x + 2.8f * airborneBlend + 2.8f * landing),
                -22f * cant + life * (sway.y + gait * step * .65f),
                tilt - 48f * cant - 6f * draw - 2.1f * settle + life * gait * step * .8f);
            model.Root.localPosition = bindPosition + offset;
            model.Root.localRotation = Quaternion.Euler(angles) * bindRotation;

            float lower = Window(reload, .35f, .49f, .52f, .66f);
            float returnArc = Window(reload, .51f, .58f, .61f, .70f);
            var drop = new Vector3(-.072f, -.133f, -.067f) * remove
                + new Vector3(.17f, -.38f, -.125f) * lower
                + new Vector3(.038f, .005f, .027f) * returnArc + Vector3.up * (.006f * settle);
            if (!local) drop *= .49f;
            var twist = Quaternion.Euler(-19f * remove, -8f * remove, 10f * remove);
            float passing = Window(reload, .09f, .16f, .18f, .26f)
                + Window(reload, .74f, .80f, .81f, .89f);
            var handTurn = Quaternion.Euler(-10f * passing, 6f * passing, -6f * passing);
            model.PoseParts(drop, twist, reach, handTurn, reload >= .25f && reload < .77f);
        }

        private static float Ease(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3 - 2 * value);
        }

        private static float Window(float t, float start, float full, float release, float end) =>
            Ease((t - start) / (full - start)) * (1 - Ease((t - release) / (end - release)));
    }
}
