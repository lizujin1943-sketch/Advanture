using System;
using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Read-only water feedback for confirmed fish motion. No scheduled future landings.</summary>
    public sealed class FishWaterEffects : IDisposable
    {
        private readonly IslandMap map;
        private readonly FishVisualCatalog art;
        private readonly FishWaterEffectsSettings settings;
        private readonly FishWaterParticles particles;
        private readonly Observation[] observations;
        private readonly Burst[] bursts;
        private bool observed;
        private ulong lastStep;

        private struct Observation
        {
            public bool Placed;
            public uint Generation;
            public int Species, Weight, Timer, Heading;
            public Point Home;
            public FishPhase Phase;
            public CatchEscapeState Escape;
        }
        private struct Burst
        {
            public bool Active, Landing;
            public ulong Step;
            public Vector3 Position, Direction;
            public float Scale;
            public uint Seed;
        }

        public FishWaterEffects(Transform parent, IslandMap map, FishVisualCatalog art)
        {
            this.map = map; this.art = art; settings = FishWaterEffectsSettings.LoadRequired();
            observations = new Observation[map.FishSpots.Count]; bursts = new Burst[observations.Length * 2];
            particles = new FishWaterParticles(parent, settings, observations.Length);
        }

        public void Present(HookState confirmed)
        {
            if (observed && confirmed.Steps < lastStep) Clear();
            bool advanced = !observed || confirmed.Steps > lastStep;
            ulong delta = observed ? confirmed.Steps - lastStep : 0;
            particles.Begin();
            for (int i = 0; i < observations.Length; i++)
            {
                var fish = confirmed.Fish[i]; var home = OceanRules.Home(map, confirmed, i);
                var heading = FishMotion.Direction(fish, home);
                var direction = new Vector3(heading.X / 1000f, 0, heading.Z / 1000f).normalized;
                int species = map.Fishing == null ? i % art.species.Length : fish.Species;
                var definition = art.species[species];
                float size = map.Fishing == null ? 1 : Mathf.Pow(Mathf.Max(1000, fish.WeightGrams) / 1000f, 1f / 3);
                float scale = settings.effectScale * Mathf.Clamp(Mathf.Sqrt(definition.baseLengthMetres * art.displayScale * size), .65f, 1.55f);
                if (definition.shadowShape == FishShadowShape.Rounded) scale *= 1.1f;
                var previous = observations[i];
                bool sameFish = previous.Placed && previous.Generation == fish.Generation && previous.Species == species &&
                    previous.Weight == fish.WeightGrams && previous.Home.X == home.X && previous.Home.Z == home.Z;
                if (!sameFish) { bursts[i * 2] = default; bursts[i * 2 + 1] = default; }
                // Seed from the first snapshot without replaying history. Repeated renders
                // of a confirmed frame reconstruct the same buffers instead of emitting again.
                if (advanced && sameFish && delta <= (ulong)settings.observationGapTicks)
                {
                    bool crossing = !fish.Escape.Active && !previous.Escape.Active && previous.Heading == fish.Heading &&
                        fish.Timer >= 0 && (ulong)fish.Timer <= delta;
                    if (crossing && previous.Phase == FishPhase.Swimming && fish.Phase == FishPhase.Jumping)
                        Start(i, confirmed.Steps, fish.Timer, home, direction, scale, false, fish.Generation);
                    if (crossing && previous.Phase == FishPhase.Jumping && fish.Phase == FishPhase.Diving)
                        Start(i, confirmed.Steps, fish.Timer, home, direction, scale, true, fish.Generation);
                    var escape = fish.Escape;
                    if (escape.Active && previous.Escape.Active && previous.Escape.Tick < CatchEscapeMotion.SplashTick &&
                        escape.Tick >= CatchEscapeMotion.SplashTick &&
                        previous.Escape.OriginX == escape.OriginX && previous.Escape.OriginZ == escape.OriginZ &&
                        previous.Escape.LandingX == escape.LandingX && previous.Escape.LandingZ == escape.LandingZ)
                    {
                        int age = escape.Tick - CatchEscapeMotion.SplashTick;
                        if (confirmed.Steps >= (ulong)age)
                            bursts[i * 2 + 1] = MakeBurst(confirmed.Steps - (ulong)age,
                                new Vector3(escape.LandingX / 1000f, settings.surfaceHeight, escape.LandingZ / 1000f),
                                direction, scale, true, (uint)i ^ fish.Generation);
                    }
                }
                if (advanced || !sameFish)
                    observations[i] = new Observation { Placed = true, Generation = fish.Generation, Species = species,
                        Weight = fish.WeightGrams, Home = home, Phase = fish.Phase, Timer = fish.Timer, Heading = fish.Heading, Escape = fish.Escape };
                if (!fish.Escape.Active && (fish.Phase == FishPhase.Swimming || fish.Phase == FishPhase.Diving))
                    Swim(fish, direction, scale);
            }
            for (int i = 0; i < bursts.Length; i++)
            {
                var burst = bursts[i]; if (!burst.Active) continue;
                float age = (confirmed.Steps - burst.Step) / (float)HookGame.Rate;
                if (age > settings.rippleLifetime + .32f) { bursts[i] = default; continue; }
                Splash(burst, age);
            }
            particles.Flush(); observed = true; lastStep = confirmed.Steps;
        }

        private void Start(int id, ulong step, int age, Point home, Vector3 direction, float scale, bool landing, uint generation)
        {
            if (step < (ulong)age) return;
            // FishMotion's actual crossing offsets, independent of where the fish is now.
            var point = new Vector3(home.X / 1000f, settings.surfaceHeight, home.Z / 1000f) + direction * (landing ? 2.4f : -1.2f);
            bursts[id * 2 + (landing ? 1 : 0)] = MakeBurst(step - (ulong)age, point, direction, scale, landing, (uint)id ^ generation);
        }
        private static Burst MakeBurst(ulong step, Vector3 position, Vector3 direction, float scale, bool landing, uint seed)
            => new Burst { Active = true, Step = step, Position = position, Direction = direction, Scale = scale,
                Landing = landing, Seed = seed * 73856093u ^ (uint)step * 19349663u };

        private void Swim(Fish fish, Vector3 direction, float scale)
        {
            var position = new Vector3(fish.X / 1000f, settings.surfaceHeight, fish.Z / 1000f);
            var side = Vector3.Cross(Vector3.up, direction);
            bool diving = fish.Phase == FishPhase.Diving;
            float visibility = diving ? 1 - Mathf.Clamp01(fish.Timer / 16f) : Mathf.SmoothStep(0, 1, fish.Timer / 9f);
            float yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            for (int j = 0; j < settings.swimBubbleCount; j++)
            {
                float age = ((fish.Timer + j * 7 + fish.Id * 3) % 25) / 30f;
                float offset = Mathf.Sin(j * 2.4f + fish.Id) * .16f * scale;
                var point = position - direction * (.12f * scale + age * 1.2f) + side * offset;
                point.y += j % 2 * .003f;
                float diameter = (.07f + j % 3 * .025f + age * .04f) * scale;
                particles.Bubbles.Add(point, new Vector3(diameter, 1, diameter),
                    Alpha(settings.bubbleTint, visibility * (1 - age)), Vector3.zero);
            }
            // Broad, sparse broken arcs replace the old angular white whiskers.
            if (!diving && fish.Timer > FishMotion.SwimTicks - 14)
            {
                float progress = (fish.Timer - (FishMotion.SwimTicks - 14)) / 14f;
                for (int j = 0; j < 2; j++)
                {
                    float width = (.36f + j * .22f + progress * .12f) * scale;
                    particles.Foam.Add(position - direction * (.15f + .24f * j) * scale,
                        new Vector3(width, 1, width * .8f), Alpha(settings.foamTint, progress * .22f), new Vector3(0, yaw + j * 43, 0));
                }
            }
        }

        private void Splash(Burst burst, float age)
        {
            float scale = burst.Scale;
            float yaw = Mathf.Atan2(burst.Direction.x, burst.Direction.z) * Mathf.Rad2Deg;
            int count = burst.Landing ? settings.landingDroplets : settings.takeoffDroplets;
            for (int j = 0; j < count; j++)
            {
                float t = age - Fraction(burst.Seed, j + 50) * .035f;
                float up = (1.8f + Fraction(burst.Seed, j) * 1.0f) * Mathf.Sqrt(scale);
                float lifetime = up / 4.5f;
                if (t < 0 || t > lifetime) continue;
                float angle = (j + Fraction(burst.Seed, j + 20) * .35f) * Mathf.PI * 2 / count;
                var radial = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                float horizontal = (.65f + Fraction(burst.Seed, j + 30) * .85f) * scale;
                var point = burst.Position + radial * (.12f * scale + horizontal * t);
                if (!burst.Landing) point += burst.Direction * t * .45f;
                point.y += up * t - 4.5f * t * t;
                float fade = FadeAfter(.65f, t / lifetime);
                float width = (.075f + Fraction(burst.Seed, j + 40) * .055f) * scale;
                particles.Droplets.Add(point, new Vector3(width, width * (1.35f + .3f * (1 - t / lifetime)), width) * (.5f + .5f * fade),
                    Alpha(settings.dropletTint, fade), new Vector3(0, 0, -radial.x * 30));
            }
            float crownLife = settings.crownLifetime * (burst.Landing ? 1 : .8f);
            if (age < crownLife)
            {
                float t = age / crownLife;
                float width = (.58f + .88f * t) * scale;
                float height = (.16f + Mathf.Sin(Mathf.Sqrt(t) * Mathf.PI) * (burst.Landing ? .5f : .4f)) * scale;
                particles.Crowns.Add(burst.Position, new Vector3(width, height, width * .85f),
                    Alpha(settings.foamTint, FadeAfter(.3f, t)), new Vector3(0, yaw + 17, 0));
            }
            int rings = burst.Landing ? 3 : 2;
            float rippleLife = settings.rippleLifetime * (burst.Landing ? 1 : .7f);
            for (int j = 0; j < rings; j++)
            {
                float t = (age - j * .16f) / rippleLife;
                if (t < 0 || t >= 1) continue;
                float progress = 1 - Mathf.Pow(1 - t, 1.7f);
                float diameter = (.45f + (2.0f + j * .15f) * progress) * scale * (burst.Landing ? settings.landingRippleScale : 1);
                var point = burst.Position + Vector3.up * (j * .004f);
                particles.Foam.Add(point, new Vector3(diameter, 1, diameter),
                    Alpha(settings.foamTint, FadeAfter(.25f, t) * (j == 0 ? .95f : .65f)), new Vector3(0, yaw + j * 61, 0));
            }
        }
        private static Color Alpha(Color tint, float amount) { tint.a *= amount; return tint; }
        private static float FadeAfter(float start, float progress)
            => 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(start, 1, progress));
        private static float Fraction(uint seed, int stream)
        {
            uint value = seed ^ (uint)(stream + 1) * 374761393u;
            value = (value ^ (value >> 13)) * 1274126177u; return (value & 65535) / 65535f;
        }
        public void Clear()
        {
            Array.Clear(observations, 0, observations.Length); Array.Clear(bursts, 0, bursts.Length);
            observed = false; lastStep = 0; particles.Clear();
        }
        public void Dispose() { particles.Dispose(); }
    }
}
