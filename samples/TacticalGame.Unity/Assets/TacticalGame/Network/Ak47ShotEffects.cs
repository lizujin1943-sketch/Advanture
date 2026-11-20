using Advanture.Tactical.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Advanture.TacticalGame.NetworkView
{
    /// <summary>确认射击的瞬时表现。世界粒子和曳迹各自保存发射位置，不随后续的相机或角色移动。</summary>
    internal sealed class Ak47ShotEffects
    {
        private const float FlashLifetime = .045f;
        private const float EffectLifetime = .70f;
        private const int SmokeTile = 4, DustTile = 5, TracerTile = 6, ChipTile = 7;
        private static readonly int TileId = Shader.PropertyToID("_Tile");
        private static readonly int GainId = Shader.PropertyToID("_Gain");
        private static readonly int OpacityId = Shader.PropertyToID("_Opacity");

        private enum Contact { None, Environment, Character }

        private sealed class Flight
        {
            internal LineRenderer Line;
            internal Vector3 Start, End, Direction, SurfaceFacing;
            internal float Distance, StreakLength, FirstHead, Speed, Arrival, Age;
            internal uint Seed;
            internal Contact Pending;
        }

        // 仅为本次表现取样。开火不会影响 Unity 的全局随机序列。
        private struct Variation
        {
            private uint value;
            internal Variation(uint seed) { value = seed == 0 ? 1u : seed; }
            internal float Range(float min, float max)
            {
                value ^= value << 13;
                value ^= value >> 17;
                value ^= value << 5;
                return min + (max - min) * ((value >> 8) / 16777216f);
            }
        }

        private readonly Transform muzzle;
        private readonly Camera worldCamera, weaponCamera;
        private readonly GameObject worldRoot, flash;
        private readonly Light weaponLight;
        private readonly MeshRenderer flashRenderer;
        private readonly MeshFilter flashFilter;
        private readonly Mesh[] flashes = new Mesh[2];
        private readonly Material material;
        private readonly MaterialPropertyBlock flashProperties = new MaterialPropertyBlock();
        private readonly Flight[] flights = new Flight[3];
        private readonly ParticleSystem smoke, dust, chips, character;
        private readonly ParticleSystem[] emitters;
        private int nextFlight;
        private float flashAge, flashSize, timeLeft;
        private bool disposed;

        internal Ak47ShotEffects(Transform owner, Transform muzzle, bool firstPerson, Camera worldCamera, Camera weaponCamera)
        {
            this.muzzle = muzzle;
            this.worldCamera = worldCamera;
            this.weaponCamera = firstPerson ? weaponCamera : null;
            material = Resources.Load<Material>("Weapons/AK47/FX/ShotEffects");
            if (material == null) throw new MissingReferenceException("缺少武器视效材质 Weapons/AK47/FX/ShotEffects。");

            int flashLayer = this.weaponCamera != null ? 31 : muzzle.gameObject.layer;
            flash = Child("MuzzleFlash", muzzle, flashLayer);
            flashFilter = flash.AddComponent<MeshFilter>();
            flashRenderer = flash.AddComponent<MeshRenderer>();
            ConfigureRenderer(flashRenderer);
            flashProperties.SetFloat(GainId, 1.12f);
            for (int i = 0; i < flashes.Length; i++) flashes[i] = BuildFlash(i);
            flashFilter.sharedMesh = flashes[0];
            flash.SetActive(false);
            if (this.weaponCamera != null)
            {
                // 用一次局部顶点补光短暂照亮手套和木件。仅视角模型所在的专用层接收此光。
                var lightObject = Child("WeaponLight", flash.transform, flashLayer);
                lightObject.transform.localPosition = new Vector3(0, .035f, -.065f);
                weaponLight = lightObject.AddComponent<Light>();
                weaponLight.type = LightType.Point;
                weaponLight.renderMode = LightRenderMode.ForceVertex;
                weaponLight.cullingMask = 1 << flashLayer;
                weaponLight.shadows = LightShadows.None;
                weaponLight.bounceIntensity = 0;
                weaponLight.range = .82f;
                weaponLight.color = new Color(1f, .76f, .53f);
            }

            worldRoot = Child("ShotEffects", owner, 0);
            for (int i = 0; i < flights.Length; i++)
            {
                var line = Child("ShotTrace" + i, worldRoot.transform, 0).AddComponent<LineRenderer>();
                ConfigureRenderer(line);
                SetSprite(line, TracerTile, 1.25f);
                line.useWorldSpace = true;
                line.alignment = LineAlignment.View;
                line.textureMode = LineTextureMode.Stretch;
                line.positionCount = 2;
                line.startColor = new Color(1f, 1f, 1f, .66f);
                line.endColor = Color.white;
                line.enabled = false;
                flights[i] = new Flight { Line = line };
            }
            smoke = CreateEmitter("MuzzleSmoke", SmokeTile, 12, 2.7f, 0, .12f);
            dust = CreateEmitter("ImpactDust", DustTile, 24, 2.3f, 0, .06f);
            chips = CreateEmitter("ImpactChips", ChipTile, 32, .57f, .64f, 0);
            character = CreateEmitter("CharacterHit", SmokeTile, 8, 1.35f, 0, 0);
            emitters = new[] { smoke, dust, chips, character };
        }

        internal void Fire(CombatState state, ulong sequence)
        {
            if (disposed) return;
            uint seed = unchecked((uint)(sequence ^ (sequence >> 32)) * 2654435761u) ^ 0xa239bc1du;
            var variation = new Variation(seed);
            timeLeft = EffectLifetime;
            // 同帧互换命中时模型已隐藏，最后一发仍按快照的世界射段表现。
            Vector3 start = state.IsAlive ? WorldMuzzle() : new Vector3(state.ShotX, state.ShotY, state.ShotZ) * .001f;
            Vector3 end = new Vector3(state.EndX, state.EndY, state.EndZ) * .001f;
            // 在整数空间先相减，以保留大坐标处射程封顶附近的毫米差值。
            Vector3 confirmedRay = new Vector3(state.EndX - state.ShotX, state.EndY - state.ShotY, state.EndZ - state.ShotZ) * .001f;
            float range = confirmedRay.magnitude;
            Vector3 direction = range > .001f ? confirmedRay / range : muzzle.forward;
            Contact hit = Classify(state, range);
            bool ahead = Vector3.Dot(end - start, direction) > .06f;
            if (!ahead && hit != Contact.None) start = end - direction * Mathf.Min(.025f, range);
            if (state.IsAlive)
            {
                flashAge = 0;
                flashSize = variation.Range(.90f, 1.10f);
                flashFilter.sharedMesh = flashes[(int)(sequence & 1)];
                flash.transform.localRotation = Quaternion.AngleAxis(variation.Range(0, 360), Vector3.forward);
                flash.SetActive(true);
                UpdateFlash();
                for (int i = 0; i < 2; i++)
                {
                    Vector3 drift = muzzle.forward * variation.Range(.33f, .66f) + Vector3.up * variation.Range(.17f, .33f);
                    drift += muzzle.right * variation.Range(-.15f, .15f);
                    Emit(smoke, start, drift, variation.Range(.069f, .105f), variation.Range(.27f, .39f),
                        variation.Range(0, 360), new Color(1f, 1f, 1f, i == 0 ? .38f : .24f));
                }
            }
            if (range < .001f || !ahead)
            {
                EmitContact(hit, end, -direction, seed);
                return;
            }

            var f = flights[nextFlight];
            nextFlight = (nextFlight + 1) % flights.Length;
            f.Start = start;
            f.End = end;
            f.Distance = Vector3.Distance(start, end);
            f.Direction = (end - start) / f.Distance;
            f.SurfaceFacing = -direction;
            f.StreakLength = Mathf.Min(2.1f, f.Distance * .28f);
            f.FirstHead = f.StreakLength * .65f;
            f.Arrival = Mathf.Lerp(.035f, .115f, Mathf.Clamp01(f.Distance / (CombatState.ShotRangeMillimeters * .001f)));
            f.Speed = (f.Distance - f.FirstHead) / f.Arrival;
            f.Age = 0;
            f.Pending = hit;
            f.Seed = seed;
            f.Line.enabled = true;
            DrawFlight(f);
        }

        private Vector3 WorldMuzzle()
        {
            if (weaponCamera == null || worldCamera == null) return muzzle.position;
            // 两台相机使用不同的 FOV。把武器枪口的屏幕像素及深度还原到世界相机。
            Vector3 screen = weaponCamera.WorldToScreenPoint(muzzle.position);
            return screen.z > 0 ? worldCamera.ScreenToWorldPoint(screen) : muzzle.position;
        }

        private static Contact Classify(CombatState state, float range)
        {
            if (state.LastHitSlot >= 0 || state.LastHitCreature >= 0) return Contact.Character;
            // 快照没有环境标志；方向表与三个端点分量均经整数舍入。封顶附近宁可略去尘屑。
            float maximum = CombatState.ShotRangeMillimeters * .001f;
            return range > .001f && range < maximum - .01f ? Contact.Environment : Contact.None;
        }

        internal void Tick(float delta)
        {
            if (disposed || timeLeft <= 0 || delta <= 0) return;
            if (delta >= timeLeft) { Reset(); return; }
            timeLeft -= delta;
            if (flash.activeSelf)
            {
                flashAge += delta;
                if (flashAge >= FlashLifetime) flash.SetActive(false);
                else UpdateFlash();
            }

            // 跨越着弹时刻的低帧率画面，按实际到达时间先后推进粒子。每条曳迹只产生一次撞击。
            float remaining = delta;
            while (remaining > 0)
            {
                float step = remaining;
                foreach (var f in flights)
                    if (f.Line.enabled && f.Pending != Contact.None)
                        step = Mathf.Min(step, Mathf.Max(0, f.Arrival - f.Age));
                if (step > 0)
                    foreach (var emitter in emitters)
                        if (emitter.particleCount > 0) emitter.Simulate(step, false, false, false);
                foreach (var f in flights)
                {
                    if (!f.Line.enabled) continue;
                    f.Age += step;
                    if (f.Pending != Contact.None && f.Age >= f.Arrival - .000001f)
                    {
                        EmitContact(f.Pending, f.End, f.SurfaceFacing, f.Seed);
                        f.Pending = Contact.None;
                    }
                    DrawFlight(f);
                }
                remaining -= step;
            }
        }

        private void UpdateFlash()
        {
            float t = flashAge / FlashLifetime;
            float width = flashSize * Mathf.Lerp(1f, .53f, t);
            float length = flashSize * Mathf.Lerp(.87f, 1.16f, Mathf.Min(1, 3 * t));
            flash.transform.localScale = new Vector3(width, width, length);
            flashProperties.SetFloat(OpacityId, 1 - Mathf.SmoothStep(0, 1, t));
            flashRenderer.SetPropertyBlock(flashProperties);
            if (weaponLight != null) weaponLight.intensity = 1.15f * (1 - t) * (1 - t);
        }

        private static void DrawFlight(Flight f)
        {
            float head = f.FirstHead + f.Speed * f.Age;
            float tail = Mathf.Max(0, head - f.StreakLength);
            if (tail >= f.Distance) { f.Line.enabled = false; return; }
            f.Line.SetPosition(0, f.Start + f.Direction * tail);
            f.Line.SetPosition(1, f.Start + f.Direction * Mathf.Min(head, f.Distance));
            float width = Mathf.Min(.16f, .037f + head * .0016f);
            f.Line.startWidth = width * .63f;
            f.Line.endWidth = width;
        }

        private void EmitContact(Contact hit, Vector3 end, Vector3 facing, uint seed)
        {
            if (hit == Contact.None) return;
            var variation = new Variation(seed ^ 0x74f628bdu);
            // 快照没有表面法线；碎屑向射线来向散开，撞击中心始终取确认端点。
            Vector3 reference = Mathf.Abs(facing.y) < .92f ? Vector3.up : Vector3.right;
            Vector3 side = Vector3.Cross(reference, facing).normalized;
            Vector3 up = Vector3.Cross(facing, side);
            Vector3 center = end + facing * .025f;
            if (hit == Contact.Character)
            {
                Color fabric = new Color(.70f, .80f, .86f, .64f);
                for (int i = 0; i < 2; i++)
                    Emit(character, center, facing * .21f + side * variation.Range(-.28f, .28f),
                        variation.Range(.09f, .14f), variation.Range(.12f, .18f), variation.Range(0, 360), fabric);
                return;
            }

            for (int i = 0; i < 3; i++)
            {
                float angle = variation.Range(0, 2 * Mathf.PI);
                Vector3 spread = side * Mathf.Cos(angle) + up * Mathf.Sin(angle);
                var drift = facing * variation.Range(.19f, .58f) + spread * variation.Range(.19f, .55f);
                Emit(dust, center + spread * .024f, drift, variation.Range(.14f, .23f), variation.Range(.27f, .43f),
                    variation.Range(0, 360), new Color(1, 1, 1, variation.Range(.54f, .77f)));
            }
            for (int i = 0; i < 5; i++)
            {
                float angle = variation.Range(0, 2 * Mathf.PI);
                Vector3 spread = side * Mathf.Cos(angle) + up * Mathf.Sin(angle);
                var drift = facing * variation.Range(.48f, 1.34f) + spread * variation.Range(.51f, 1.6f) + Vector3.up * .23f;
                Emit(chips, center, drift, variation.Range(.020f, .046f), variation.Range(.19f, .34f),
                    variation.Range(0, 360), Color.white);
            }
        }

        private static void Emit(ParticleSystem system, Vector3 point, Vector3 velocity, float size, float lifetime, float angle, Color color)
        {
            var particle = new ParticleSystem.EmitParams
            {
                position = point, velocity = velocity, startSize = size, startLifetime = lifetime,
                rotation = angle, startColor = color
            };
            system.Emit(particle, 1);
        }

        private ParticleSystem CreateEmitter(string name, int tile, int capacity, float growth, float gravity, float fadeIn)
        {
            var emitter = Child(name, worldRoot.transform, 0).AddComponent<ParticleSystem>();
            emitter.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = emitter.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            main.gravityModifier = gravity;
            main.maxParticles = capacity;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            emitter.useAutoRandomSeed = false;
            emitter.randomSeed = (uint)(47 + capacity);
            var emission = emitter.emission;
            emission.enabled = false;
            var shape = emitter.shape;
            shape.enabled = false;
            var size = emitter.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 1, 1, growth));
            var gradient = new Gradient();
            var alpha = fadeIn > 0
                ? new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, fadeIn), new GradientAlphaKey(.68f, .45f), new GradientAlphaKey(0, 1) }
                : new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(.82f, .44f), new GradientAlphaKey(0, 1) };
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, alpha);
            var color = emitter.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(gradient);
            var renderer = emitter.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.maxParticleSize = .25f;
            ConfigureRenderer(renderer);
            SetSprite(renderer, tile, 1);
            emitter.Simulate(0, false, true, false);
            return emitter;
        }

        private void ConfigureRenderer(Renderer renderer)
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private static Vector4 Tile(int index) => new Vector4(.25f, .5f, .25f * (index % 4), index < 4 ? .5f : 0);

        private static void SetSprite(Renderer renderer, int tile, float gain)
        {
            var block = new MaterialPropertyBlock();
            block.SetVector(TileId, Tile(tile));
            block.SetFloat(GainId, gain);
            renderer.SetPropertyBlock(block);
        }

        private static Mesh BuildFlash(int variant)
        {
            // 两层长短、纹理各异的火舌保留侧视纵深；倾斜的小片薄焰补足沿轴视角。
            var vertices = new Vector3[12];
            var uvs = new Vector2[12];
            var colors = new Color32[12];
            var triangles = new int[18];
            void Card(int card, int tile, Vector3 center, Vector3 horizontal, Vector3 vertical, byte opacity)
            {
                int i = card * 4, k = card * 6;
                vertices[i] = center - horizontal - vertical;
                vertices[i + 1] = center + horizontal - vertical;
                vertices[i + 2] = center + horizontal + vertical;
                vertices[i + 3] = center - horizontal + vertical;
                Vector4 rect = Tile(tile);
                uvs[i] = new Vector2(rect.z, rect.w);
                uvs[i + 1] = new Vector2(rect.z + rect.x, rect.w);
                uvs[i + 2] = new Vector2(rect.z + rect.x, rect.w + rect.y);
                uvs[i + 3] = new Vector2(rect.z, rect.w + rect.y);
                for (int n = 0; n < 4; n++) colors[i + n] = new Color32(255, 255, 255, opacity);
                triangles[k] = i; triangles[k + 1] = i + 1; triangles[k + 2] = i + 2;
                triangles[k + 3] = i; triangles[k + 4] = i + 2; triangles[k + 5] = i + 3;
            }
            int crown = variant * 2, jet = crown + 1;
            int secondJet = variant == 0 ? 3 : 1;
            Card(0, jet, Vector3.forward * .156f, Vector3.forward * .161f, new Vector3(.105f, .014f, 0), 236);
            Card(1, secondJet, new Vector3(.003f, 0, .123f), Vector3.forward * .129f, Vector3.up * .096f, 188);
            Card(2, crown, Vector3.forward * .047f, Vector3.right * .098f, new Vector3(0, .088f, .024f), 153);
            return new Mesh { name = "Shot flash " + variant, vertices = vertices, uv = uvs, colors32 = colors, triangles = triangles };
        }

        private static GameObject Child(string name, Transform parent, int layer)
        {
            var child = new GameObject(name) { layer = layer, hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild };
            child.transform.SetParent(parent, false);
            return child;
        }

        internal void Reset()
        {
            if (disposed || timeLeft <= 0) return;
            timeLeft = 0;
            flash.SetActive(false);
            foreach (var f in flights) { f.Pending = Contact.None; f.Line.enabled = false; }
            foreach (var emitter in emitters) emitter.Clear(false);
        }

        internal void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (flash != null) { flash.SetActive(false); Release(flash); }
            if (worldRoot != null) { worldRoot.SetActive(false); Release(worldRoot); }
            foreach (var mesh in flashes) Release(mesh);
        }

        private static void Release(Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Object.Destroy(value);
            else Object.DestroyImmediate(value);
        }
    }
}
