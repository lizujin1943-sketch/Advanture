using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Advanture.HookIsland
{
    /// <summary>Bounded drawing buffers. Presenters, rather than Unity's particle clock, own time.</summary>
    internal sealed class WaterParticleBuffers : IDisposable
    {
        internal sealed class Layer
        {
            internal readonly ParticleSystem System;
            private readonly ParticleSystem.Particle[] particles;
            private int count;
            internal Layer(ParticleSystem system, int capacity) { System = system; particles = new ParticleSystem.Particle[capacity]; }
            internal void Begin() { count = 0; }
            internal void Add(Vector3 position, Vector3 size, Color color, Vector3 rotation)
            {
                if (count == particles.Length || color.a <= .001f) return;
                particles[count++] = new ParticleSystem.Particle
                {
                    position = position, startSize3D = size, startColor = color, rotation3D = rotation,
                    startLifetime = 1000, remainingLifetime = 1000, randomSeed = (uint)count
                };
            }
            internal void Flush() { System.SetParticles(particles, count); }
        }

        private readonly Transform root;
        private readonly FishWaterEffectsSettings settings;
        private readonly List<Object> resources = new List<Object>();
        private readonly List<Layer> layers = new List<Layer>();
        private Mesh plane;
        internal WaterParticleBuffers(Transform parent, FishWaterEffectsSettings settings, string name)
        {
            this.settings = settings;
            root = new GameObject(name).transform; root.SetParent(parent, false);
        }
        internal Layer PlaneLayer(string name, int style, int capacity)
        {
            if (plane == null) { plane = Plane(); resources.Add(plane); }
            return Create(name, style, plane, capacity);
        }
        internal Layer BillboardLayer(string name, int style, int capacity) => Create(name, style, null, capacity);
        internal Layer MeshLayer(string name, int style, Mesh mesh, int capacity)
        { resources.Add(mesh); return Create(name, style, mesh, capacity); }
        internal void Begin() { foreach (var layer in layers) layer.Begin(); }
        internal void Flush() { foreach (var layer in layers) layer.Flush(); }
        internal void Clear() { Begin(); Flush(); }

        private Layer Create(string name, int style, Mesh mesh, int capacity)
        {
            var obj = new GameObject(name); obj.transform.SetParent(root, false);
            var system = obj.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            system.useAutoRandomSeed = false; system.randomSeed = 1;
            var main = system.main;
            main.playOnAwake = false; main.loop = false; main.maxParticles = capacity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            main.simulationSpeed = 0; main.startSpeed = 0; main.startSize3D = true; main.startRotation3D = true;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = system.emission; emission.enabled = false;
            var shape = system.shape; shape.enabled = false;
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = mesh == null ? ParticleSystemRenderMode.Billboard : ParticleSystemRenderMode.Mesh;
            if (mesh != null) renderer.mesh = mesh;
            renderer.alignment = mesh == null ? ParticleSystemRenderSpace.View : ParticleSystemRenderSpace.World;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
            { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV });
            var material = new Material(settings.particleShader) { name = name + " material" };
            material.SetFloat("_Style", style); renderer.sharedMaterial = material; resources.Add(material);
            system.Play(); system.Pause();
            var layer = new Layer(system, capacity); layers.Add(layer); return layer;
        }
        private static Mesh Plane()
        {
            var mesh = new Mesh { name = "Water particle plane" };
            mesh.vertices = new[] { new Vector3(-.5f,0,-.5f), new Vector3(.5f,0,-.5f), new Vector3(.5f,0,.5f), new Vector3(-.5f,0,.5f) };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            // Unity 2022 mesh particles require packed UNorm8 colors for the color stream.
            mesh.colors32 = new[] { (Color32)Color.white, (Color32)Color.white, (Color32)Color.white, (Color32)Color.white };
            mesh.triangles = new[] { 0,2,1,0,3,2 }; mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        public void Dispose() { Release(root.gameObject); foreach (var item in resources) Release(item); }
        private static void Release(Object value) { if (Application.isPlaying) Object.Destroy(value); else Object.DestroyImmediate(value); }
    }
}
