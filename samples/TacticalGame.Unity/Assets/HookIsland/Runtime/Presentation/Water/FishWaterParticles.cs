using System;
using System.Collections.Generic;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Four shared world-space particle buffers, explicitly sampled by their presenter.</summary>
    internal sealed class FishWaterParticles : IDisposable
    {
        private readonly WaterParticleBuffers buffers;
        internal readonly WaterParticleBuffers.Layer Bubbles, Foam, Droplets, Crowns;
        internal FishWaterParticles(Transform parent, FishWaterEffectsSettings settings, int fishCount)
        {
            buffers = new WaterParticleBuffers(parent, settings, "Fish water particles (confirmed)");
            int capacity = Mathf.Max(64, fishCount * 48);
            Bubbles = buffers.PlaneLayer("Swimming bubbles", 1, capacity);
            Foam = buffers.PlaneLayer("Surface foam", 2, capacity);
            Droplets = buffers.BillboardLayer("Water droplets", 0, capacity);
            Crowns = buffers.MeshLayer("Splash crowns", 3, Crown(), Mathf.Max(32, fishCount * 2));
        }
        internal void Begin() { buffers.Begin(); }
        internal void Flush() { buffers.Flush(); }
        internal void Clear() { buffers.Clear(); }
        private static Mesh Crown()
        {
            const int segments = 36;
            var vertices = new List<Vector3>(); var colors = new List<Color32>();
            var uv = new List<Vector2>(); var triangles = new List<int>();
            float[] radius = { .12f, .25f, .41f, .53f, .59f }, height = { 0, .07f, .17f, .24f, .22f };
            // One curved, gently uneven water sheet replaces the six rigid pointed petals.
            // UVs let the shader soften both the foot and the lip rather than expose a hard silhouette.
            for (int row = 0; row < radius.Length; row++) for (int segment = 0; segment <= segments; segment++)
            {
                float u = segment / (float)segments, v = row / (float)(radius.Length - 1);
                float angle = u * Mathf.PI * 2;
                float variation = Mathf.Sin(angle * 5 + .4f) * .06f + Mathf.Sin(angle * 9) * .025f;
                float r = radius[row] * (1 + variation * v);
                vertices.Add(new Vector3(Mathf.Cos(angle) * r, height[row] * (1 + variation * 2 * v), Mathf.Sin(angle) * r));
                colors.Add(Color.Lerp(new Color(.72f,.94f,1,.45f), Color.white, v));
                uv.Add(new Vector2(u, v));
                if (row < radius.Length - 1 && segment < segments)
                {
                    int a = row * (segments + 1) + segment, b = a + segments + 1;
                    triangles.Add(a); triangles.Add(b); triangles.Add(a+1);
                    triangles.Add(a+1); triangles.Add(b); triangles.Add(b+1);
                }
            }
            var mesh = new Mesh { name = "Curved soft splash sheet" };
            mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        public void Dispose() { buffers.Dispose(); }
    }
}
