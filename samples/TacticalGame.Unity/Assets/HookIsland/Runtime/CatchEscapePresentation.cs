using System;
using System.Collections.Generic;
using Advanture.HookIsland.Simulation;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Advanture.HookIsland
{
    /// <summary>Deterministic poses and pooled water effects sampled from the confirmed escape tick.</summary>
    public sealed class CatchEscapePresentation : IDisposable
    {
        private readonly Transform root;
        private readonly Material lineMaterial, waterMaterial;
        private readonly Mesh dropletMesh;
        private readonly Dictionary<int, Splash> splashes = new Dictionary<int, Splash>();
        private sealed class Splash
        {
            public Transform Root;
            public LineRenderer Ring;
            public readonly Transform[] Drops = new Transform[8];
        }

        public CatchEscapePresentation(Transform parent, Shader lineShader)
        {
            root = new GameObject("Catch escape effects").transform; root.SetParent(parent, false);
            lineMaterial = new Material(lineShader) { name = "Escape splash ring" };
            waterMaterial = new Material(Shader.Find("Hook Island/Baked Color Surface")) { name = "Escape water drops", color = new Color(.72f, .97f, 1) };
            dropletMesh = new Mesh { name = "Low poly water drop" };
            dropletMesh.vertices = new[] { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
            dropletMesh.triangles = new[] { 0,4,3, 0,2,4, 0,5,2, 0,3,5, 1,3,4, 1,4,2, 1,2,5, 1,5,3 };
            dropletMesh.RecalculateNormals(); dropletMesh.RecalculateBounds();
        }

        public void Present(int id, CatchEscapeState escape, Transform body, float heightOffset = .12f)
        {
            var tail = body.Find("Tail");
            if (!escape.Active)
            {
                if (tail != null) tail.localRotation = Quaternion.identity;
                if (splashes.TryGetValue(id, out var previous)) previous.Root.gameObject.SetActive(false);
                return;
            }
            CatchEscapeMotion.Position(escape, out int x, out int y, out int z);
            body.position = new Vector3(x / 1000f, y / 1000f + heightOffset, z / 1000f);
            body.rotation = Rotation(escape);
            if (tail != null) tail.localRotation = Quaternion.Euler(0, Mathf.Sin((escape.Tick - 1) * 2.15f) * 55, 0);
            bool splash = escape.Tick >= CatchEscapeMotion.SplashTick;
            if (!splash)
            {
                if (splashes.TryGetValue(id, out var previous)) previous.Root.gameObject.SetActive(false);
                return;
            }
            if (!splashes.TryGetValue(id, out var effect)) { effect = CreateSplash(id); splashes.Add(id, effect); }
            effect.Root.gameObject.SetActive(true);
            effect.Root.position = new Vector3(escape.LandingX / 1000f, .09f, escape.LandingZ / 1000f);
            float t = (escape.Tick - CatchEscapeMotion.SplashTick) / (float)CatchEscapeMotion.SubmergeTicks;
            float radius = .25f + 1.3f * t;
            effect.Ring.startColor = effect.Ring.endColor = new Color(.84f, 1, 1, 1 - t);
            for (int i = 0; i < 32; i++)
            {
                float angle = i * Mathf.PI * 2 / 32;
                effect.Ring.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius));
            }
            for (int i = 0; i < effect.Drops.Length; i++)
            {
                float angle = (i + .5f) * Mathf.PI / 4, distance = .18f + t * (i % 2 == 0 ? 1.3f : .95f);
                effect.Drops[i].localPosition = new Vector3(Mathf.Cos(angle) * distance, .03f + 4 * (.7f + .15f * (i % 2)) * t * (1 - t), Mathf.Sin(angle) * distance);
                effect.Drops[i].localScale = new Vector3(.065f, .13f, .065f) * (1 - t);
            }
        }

        public static Quaternion Rotation(CatchEscapeState escape)
        {
            var direction = new Vector3(escape.LandingX - escape.OriginX, 0, escape.LandingZ - escape.OriginZ);
            if (direction.sqrMagnitude < 1) direction = Vector3.forward;
            float age = escape.Tick - 1;
            if (age < CatchEscapeMotion.StruggleTicks)
                return Quaternion.LookRotation(direction) * Quaternion.Euler(Mathf.Sin(age * 1.8f) * 16, Mathf.Sin(age * 2.1f) * 22, Mathf.Sin(age * 2.5f) * 42);
            CatchEscapeMotion.Position(escape, out int x, out int y, out int z);
            var next = escape; next.Tick = Mathf.Min(next.Tick + 1, CatchEscapeMotion.TotalTicks);
            CatchEscapeMotion.Position(next, out int nx, out int ny, out int nz);
            float pitch = -Mathf.Atan2(ny - y, Mathf.Max(1, new Vector2(nx - x, nz - z).magnitude)) * Mathf.Rad2Deg;
            return Quaternion.LookRotation(direction) * Quaternion.Euler(pitch, 0, Mathf.Sin(age * 1.9f) * 18);
        }

        private Splash CreateSplash(int id)
        {
            var effect = new Splash { Root = new GameObject("Escape splash " + id).transform };
            effect.Root.SetParent(root, false);
            effect.Ring = effect.Root.gameObject.AddComponent<LineRenderer>();
            effect.Ring.sharedMaterial = lineMaterial; effect.Ring.useWorldSpace = false; effect.Ring.loop = true;
            effect.Ring.positionCount = 32; effect.Ring.startWidth = effect.Ring.endWidth = .055f;
            for (int i = 0; i < effect.Drops.Length; i++)
            {
                var drop = new GameObject("Water drop " + i).transform; drop.SetParent(effect.Root, false);
                drop.gameObject.AddComponent<MeshFilter>().sharedMesh = dropletMesh;
                drop.gameObject.AddComponent<MeshRenderer>().sharedMaterial = waterMaterial;
                effect.Drops[i] = drop;
            }
            return effect;
        }
        public void Clear() { foreach (var splash in splashes.Values) splash.Root.gameObject.SetActive(false); }
        public void Dispose() { Destroy(root.gameObject); Destroy(lineMaterial); Destroy(waterMaterial); Destroy(dropletMesh); }
        private static void Destroy(Object value) { if (Application.isPlaying) Object.Destroy(value); else Object.DestroyImmediate(value); }
    }
}
