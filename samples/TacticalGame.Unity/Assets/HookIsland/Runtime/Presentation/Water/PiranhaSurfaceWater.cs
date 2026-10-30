using System;
using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Surface contact and short, soft swimming wakes, sampled from confirmed enemies only.</summary>
    internal sealed class PiranhaSurfaceWater : IDisposable
    {
        private readonly EnemyConfig config;
        private readonly FishVisualCatalog art;
        private readonly FishWaterEffectsSettings settings;
        private readonly WaterParticleBuffers buffers;
        private readonly WaterParticleBuffers.Layer ripples, wake, spray;
        private const int TrailSlots = 6, TrailInterval = 6;
        private readonly Observation[] observations;
        private readonly Trail[] trails;
        private readonly bool[] seen;
        private bool observed;
        private ulong lastStep;
        private struct Observation { public bool Placed; public Vector3 Position; public int Weight; public ulong Step; }
        private struct Trail { public bool Active; public Vector3 Position; public ulong Step; public float Length, Strength; }

        internal PiranhaSurfaceWater(Transform parent, EnemyConfig config, FishVisualCatalog art)
        {
            this.config = config; this.art = art; settings = FishWaterEffectsSettings.LoadRequired();
            buffers = new WaterParticleBuffers(parent, settings, "Piranha surface water (confirmed)");
            int capacity = Mathf.Max(16, config.PiranhaCount * 8);
            ripples = buffers.PlaneLayer("Body ripples", 4, capacity);
            wake = buffers.PlaneLayer("Tail wake", 2, capacity);
            spray = buffers.BillboardLayer("Tail spray", 0, capacity);
            observations = new Observation[config.PiranhaCount];
            trails = new Trail[config.PiranhaCount * TrailSlots]; seen = new bool[config.PiranhaCount];
        }
        internal float BodyHeight(ulong step, int id)
            => settings.piranhaBodyHeight + Mathf.Sin((step % 30000) / (float)HookGame.Rate * 2.6f + id) * .012f;

        private static Vector3 Goal(Piranha fish, HookState state)
        {
            if (fish.Target >= 0 && fish.Target < state.Players.Count)
            {
                var target = state.Players[fish.Target]; return new Vector3(target.X / 1000f, 0, target.Z / 1000f);
            }
            var patrol = EnemyPlacement.PatrolGoal(fish); return new Vector3(patrol.X / 1000f, 0, patrol.Z / 1000f);
        }
        internal static Vector3 Direction(Piranha fish, HookState state)
        {
            var delta = Goal(fish, state) - new Vector3(fish.X / 1000f, 0, fish.Z / 1000f);
            return delta.sqrMagnitude > .000001f ? delta.normalized : Vector3.forward;
        }
        internal void Present(HookState state)
        {
            if (observed && state.Steps < lastStep) Clear();
            Array.Clear(seen, 0, seen.Length);
            buffers.Begin();
            foreach (var fish in state.Enemies.Piranhas)
            {
                if (fish.Id < 0 || fish.Id >= observations.Length) continue;
                seen[fish.Id] = true;
                // A hooked, airborne, escaping or absent fish cannot leave a swimming wake.
                if (fish.RespawnTicks != 0 || fish.Owner >= 0 || fish.Escape.Active) { Forget(fish.Id); continue; }
                var position = new Vector3(fish.X / 1000f, settings.surfaceHeight, fish.Z / 1000f);
                var direction = Direction(fish, state); var side = Vector3.Cross(Vector3.up, direction);
                float yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
                float length = art.piranha.baseLengthMetres * art.displayScale * Mathf.Pow(Mathf.Max(1000, fish.WeightGrams) / 1000f, 1f / 3);
                float clock = (state.Steps % 30000) / (float)HookGame.Rate;
                for (int i = 0; i < 2; i++)
                {
                    float phase = Mathf.Repeat(clock * .7f + fish.Id * .19f + i * .5f, 1);
                    float expansion = 1 + phase * .2f;
                    ripples.Add(position + Vector3.up * (i * .002f), new Vector3(length * .8f * expansion, 1, length * 1.16f * expansion),
                        Tint(settings.foamTint, (1 - phase) * .3f), new Vector3(0, yaw, 0));
                }
                float speed = (fish.Target >= 0 ? config.PiranhaSpeed : config.PatrolSpeed) / 1000f;
                float strength = settings.piranhaWakeStrength * Mathf.Clamp(speed / 1.2f, .4f, 1.25f);
                Observe(fish, state.Steps, position, length, speed, strength);
                float stop = fish.Target >= 0 ? config.BiteRange / 2000f : .05f;
                if ((Goal(fish, state) - new Vector3(position.x, 0, position.z)).sqrMagnitude <= stop * stop) continue;
                if (speed <= 0) continue;
                for (int i = 0; i < 6; i++)
                {
                    float phase = Mathf.Repeat(clock / .36f + fish.Id * .23f + i / 6f, 1);
                    float age = phase * .36f, sign = i % 2 == 0 ? -1 : 1;
                    var point = position - direction * (length * .43f + speed * age * .65f);
                    point += side * sign * length * (.04f + phase * .15f);
                    point.y += Mathf.Sin(phase * Mathf.PI) * length * .12f;
                    float size = length * (.038f + i % 3 * .01f) * (1 - phase * .45f);
                    spray.Add(point, new Vector3(size, size * 1.25f, size),
                        Tint(settings.dropletTint, strength * Mathf.Sin(phase * Mathf.PI) * .65f), Vector3.zero);
                }
            }
            for (int id = 0; id < seen.Length; id++) if (!seen[id]) Forget(id);
            for (int i = 0; i < trails.Length; i++)
            {
                var trail = trails[i]; if (!trail.Active) continue;
                float t = (state.Steps - trail.Step) / (HookGame.Rate * Mathf.Max(.1f, settings.piranhaWakeLifetime));
                if (t >= 1) { trails[i] = default; continue; }
                float diameter = trail.Length * (.18f + .95f * t);
                wake.Add(trail.Position, new Vector3(diameter, 1, diameter),
                    Tint(settings.foamTint, trail.Strength * (1 - Mathf.SmoothStep(0, 1, t)) * .38f), Vector3.zero);
            }
            buffers.Flush(); observed = true; lastStep = state.Steps;
        }
        private void Observe(Piranha fish, ulong step, Vector3 position, float length, float speed, float strength)
        {
            var previous = observations[fish.Id];
            if (previous.Placed && step == previous.Step) return;
            ulong delta = previous.Placed ? step - previous.Step : 0;
            bool continuous = previous.Placed && fish.WeightGrams == previous.Weight && delta > 0 && delta <= (ulong)settings.observationGapTicks;
            var movement = position - previous.Position; movement.y = 0;
            if (continuous && movement.magnitude <= speed * delta / HookGame.Rate + .1f)
            {
                if (movement.sqrMagnitude > .000001f)
                    for (ulong emitted = (previous.Step / TrailInterval + 1) * TrailInterval; emitted <= step; emitted += TrailInterval)
                    {
                        // Leave circles at the actual confirmed path, not parented to a rotating fish.
                        float fraction = (emitted - previous.Step) / (float)delta;
                        var point = Vector3.Lerp(previous.Position, position, fraction) - movement.normalized * length * .42f;
                        point.y = settings.surfaceHeight;
                        int slot = fish.Id * TrailSlots + (int)(emitted / TrailInterval % TrailSlots);
                        trails[slot] = new Trail { Active = true, Position = point, Step = emitted, Length = length, Strength = strength };
                    }
            }
            else Forget(fish.Id);
            observations[fish.Id] = new Observation { Placed = true, Position = position, Weight = fish.WeightGrams, Step = step };
        }
        private void Forget(int id)
        { observations[id] = default; Array.Clear(trails, id * TrailSlots, TrailSlots); }
        private static Color Tint(Color color, float alpha) { color.a *= alpha; return color; }
        internal void Clear()
        {
            Array.Clear(observations, 0, observations.Length); Array.Clear(trails, 0, trails.Length);
            observed = false; lastStep = 0; buffers.Clear();
        }
        public void Dispose() { buffers.Dispose(); }
    }
}
