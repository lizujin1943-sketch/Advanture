using System;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Bounded, shared particle buffers for already-confirmed water contacts.</summary>
    public sealed class FirearmWaterImpactView : IDisposable
    {
        private struct Burst
        {
            internal bool Active;
            internal Vector3 Position, Direction;
            internal float Started;
            internal uint Seed;
        }

        private readonly FirearmWaterImpactSettings settings;
        private readonly WaterParticleBuffers buffers;
        private readonly WaterParticleBuffers.Layer spray, crowns, ripples;
        private readonly Burst[] bursts;
        private readonly int dropletCount;
        private int next;
        public float SurfaceHeight => settings.surface.surfaceHeight;

        public FirearmWaterImpactView(Transform parent, FirearmWaterImpactSettings settings = null)
        {
            this.settings = settings ?? FirearmWaterImpactSettings.LoadRequired();
            int capacity = Mathf.Clamp(this.settings.capacity, 8, 64);
            dropletCount = Mathf.Clamp(this.settings.droplets, 4, 16);
            bursts = new Burst[capacity];
            buffers = new WaterParticleBuffers(parent, this.settings.surface, "Confirmed bullet water impacts");
            spray = buffers.BillboardLayer("Bullet water spray", 0, capacity * (dropletCount + 2));
            crowns = buffers.MeshLayer("Bullet water crowns", 3, FishWaterParticles.Crown(), capacity);
            ripples = buffers.PlaneLayer("Bullet water ripples", 2, capacity * 2);
        }

        public void Add(Vector3 position, Vector3 direction, float now, uint seed)
        {
            position.y = SurfaceHeight;
            direction.y = 0;
            bursts[next] = new Burst { Active = true, Position = position, Direction = direction.normalized,
                Started = now, Seed = seed };
            next = (next + 1) % bursts.Length;
        }

        public void Present(float now)
        {
            buffers.Begin();
            for (int i = 0; i < bursts.Length; i++)
            {
                var burst = bursts[i];
                if (!burst.Active) continue;
                float age = now - burst.Started;
                if (age < 0 || age > Mathf.Max(settings.rippleLifetime + settings.secondRippleDelay, .8f))
                { bursts[i] = default; continue; }
                Splash(burst, age);
                for (int ring = 0; ring < 2; ring++)
                {
                    float progress = (age - ring * settings.secondRippleDelay) / settings.rippleLifetime;
                    if (progress < 0 || progress >= 1) continue;
                    float expansion = 1 - Mathf.Pow(1 - progress, 1.6f);
                    float diameter = Mathf.Lerp(.12f, settings.rippleDiameter, expansion);
                    float opacity = (1 - Mathf.SmoothStep(0, 1, progress)) * (ring == 0 ? 1 : .65f);
                    // Equal X/Z size gives a real circle; perspective alone makes it elliptical.
                    ripples.Add(burst.Position + Vector3.up * (ring * .003f), new Vector3(diameter, 1, diameter),
                        Alpha(settings.rippleTint, opacity), Vector3.zero);
                }
            }
            buffers.Flush();
        }

        private void Splash(Burst burst, float age)
        {
            const float gravity = 10;
            if (age < settings.crownLifetime)
            {
                float t = age / settings.crownLifetime;
                float width = Mathf.Lerp(.20f, .55f, t);
                crowns.Add(burst.Position, new Vector3(width, .2f + Mathf.Sin(t * Mathf.PI) * .6f, width),
                    Alpha(settings.splashTint, 1 - t), Vector3.zero);
                // A small soft jet reads as a water strike without solid-looking shards.
                float height = .07f + Mathf.Sin(t * Mathf.PI) * settings.splashHeight;
                spray.Add(burst.Position + Vector3.up * height * .42f,
                    new Vector3(.12f * (1 - t * .5f), height, .1f),
                    Alpha(settings.splashTint, 1 - t), Vector3.zero);
            }
            for (int i = 0; i < dropletCount; i++)
            {
                float t = age - Fraction(burst.Seed, i + 30) * .025f;
                float up = Mathf.Sqrt(2 * gravity * settings.splashHeight * Mathf.Lerp(.45f, 1, Fraction(burst.Seed, i)));
                float lifetime = 2 * up / gravity;
                if (t < 0 || t >= lifetime) continue;
                float angle = (i + Fraction(burst.Seed, i + 10) * .4f) * Mathf.PI * 2 / dropletCount;
                var radial = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                var position = burst.Position + (radial * settings.sprayRadius + burst.Direction * .045f) * (t / lifetime);
                position.y += up * t - gravity * t * t * .5f;
                float fade = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.55f, 1, t / lifetime));
                float size = Mathf.Lerp(.035f, .065f, Fraction(burst.Seed, i + 20));
                spray.Add(position, new Vector3(size, size * 1.6f, size),
                    Alpha(settings.splashTint, fade), new Vector3(0, 0, -radial.x * 22));
            }
        }

        private static Color Alpha(Color color, float alpha) { color.a *= alpha; return color; }
        private static float Fraction(uint seed, int stream)
        {
            uint value = unchecked(seed ^ (uint)(stream + 1) * 374761393u);
            value = unchecked((value ^ (value >> 13)) * 1274126177u);
            return (value & 65535) / 65535f;
        }
        public void Clear() { Array.Clear(bursts, 0, bursts.Length); next = 0; buffers.Clear(); }
        public void Dispose() { buffers.Dispose(); }
    }
}
