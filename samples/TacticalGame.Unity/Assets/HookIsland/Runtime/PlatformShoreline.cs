using System;
using System.Collections.Generic;
using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Water contact foam from confirmed tiles. This never changes the platform or simulation.</summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(IslandOcean))]
    public sealed class PlatformShoreline : MonoBehaviour
    {
        [Header("Platform edge foam and outward ripples")]
        [Range(.15f, 1.2f)] public float foamWidth = .5f;
        [Range(.6f, 3)] public float rippleReach = 1.6f;
        [Range(.25f, 1)] public float rippleSpacing = .65f;
        [Range(.05f, .8f)] public float rippleSpeed = .28f;
        [Range(0, 1)] public float foamStrength = 0;

        private const int PixelsPerMetre = 8;
        private const float TileSize = PlatformRules.TileSize / 1000f;
        private const float HalfTile = TileSize * .5f;
        private MeshRenderer waterRenderer;
        private MaterialPropertyBlock properties;
        private Texture2D distanceTexture;
        private PlatformTile[] tiles;
        private Vector4 shoreBounds;
        private bool settingsChanged;

        private void OnEnable()
        {
            waterRenderer = GetComponent<MeshRenderer>();
            if (tiles == null) tiles = PlatformRules.InitialTiles();
            Rebuild();
        }

        // OnValidate may run off the main thread; texture creation waits until Update.
        private void OnValidate() => settingsChanged = true;

        private void Update()
        {
            if (settingsChanged) Rebuild();
        }

        public void Synchronize(IReadOnlyList<PlatformTile> confirmedTiles)
        {
            tiles = new PlatformTile[confirmedTiles.Count];
            for (int i = 0; i < tiles.Length; i++) tiles[i] = confirmedTiles[i];
            if (isActiveAndEnabled) Rebuild();
        }

        private void Rebuild()
        {
            settingsChanged = false;
            foamWidth = Mathf.Clamp(foamWidth, .15f, 1.2f);
            rippleReach = Mathf.Clamp(rippleReach, .6f, 3);
            rippleSpacing = Mathf.Clamp(rippleSpacing, .25f, 1);
            rippleSpeed = Mathf.Clamp(rippleSpeed, .05f, .8f);
            foamStrength = Mathf.Clamp01(foamStrength);
            if (tiles == null) tiles = PlatformRules.InitialTiles();
            if (tiles.Length == 0)
            {
                ReleaseTexture();
                shoreBounds = new Vector4(0, 0, 80, 80);
                Apply();
                return;
            }

            int minX = tiles[0].X, maxX = minX, minZ = tiles[0].Z, maxZ = minZ;
            foreach (var tile in tiles)
            {
                minX = Math.Min(minX, tile.X); maxX = Math.Max(maxX, tile.X);
                minZ = Math.Min(minZ, tile.Z); maxZ = Math.Max(maxZ, tile.Z);
            }
            int cellPixels = Mathf.RoundToInt(TileSize * PixelsPerMetre);
            int radius = Mathf.CeilToInt((HalfTile + Mathf.Max(foamWidth, rippleReach) + .5f) * PixelsPerMetre);
            int stampSize = radius * 2;
            int width = (maxX - minX) * cellPixels + stampSize;
            int height = (maxZ - minZ) * cellPixels + stampSize;
            shoreBounds = new Vector4((minX + maxX) * HalfTile, (minZ + maxZ) * HalfTile,
                width / (float)PixelsPerMetre, height / (float)PixelsPerMetre);

            // Stamp one analytic rectangle-distance kernel per tile. The minimum is the
            // exact exterior distance to their union: no internal seams, rounded outer
            // corners, and support for concave edges and holes without tracing contours.
            var stamp = new ushort[stampSize * stampSize];
            for (int z = 0; z < stampSize; z++)
                for (int x = 0; x < stampSize; x++)
                {
                    float dx = Mathf.Abs((x - radius + .5f) / PixelsPerMetre) - HalfTile;
                    float dz = Mathf.Abs((z - radius + .5f) / PixelsPerMetre) - HalfTile;
                    float ox = Mathf.Max(0, dx), oz = Mathf.Max(0, dz);
                    float distance = Mathf.Sqrt(ox * ox + oz * oz) + Mathf.Min(Mathf.Max(dx, dz), 0);
                    stamp[z * stampSize + x] = (ushort)Mathf.RoundToInt(Mathf.Clamp01((distance + 16) / 32) * 65535);
                }
            var distances = new ushort[width * height];
            for (int i = 0; i < distances.Length; i++) distances[i] = ushort.MaxValue;
            foreach (var tile in tiles)
            {
                int startX = (tile.X - minX) * cellPixels, startZ = (tile.Z - minZ) * cellPixels;
                for (int z = 0; z < stampSize; z++)
                    for (int x = 0; x < stampSize; x++)
                    {
                        int index = (startZ + z) * width + startX + x;
                        ushort distance = stamp[z * stampSize + x];
                        if (distance < distances[index]) distances[index] = distance;
                    }
            }
            var pixels = new Color32[distances.Length];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32((byte)(distances[i] >> 8), (byte)distances[i], 0, 255);
            if (distanceTexture == null || distanceTexture.width != width || distanceTexture.height != height)
            {
                ReleaseTexture();
                // Linear RG encoding matches the island ocean's existing distance format.
                distanceTexture = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
                {
                    name = "Platform water contact distance",
                    hideFlags = HideFlags.HideAndDontSave,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
            }
            distanceTexture.SetPixels32(pixels);
            distanceTexture.Apply(false, false);
            Apply();
        }

        private void Apply()
        {
            if (waterRenderer == null) waterRenderer = GetComponent<MeshRenderer>();
            if (properties == null) properties = new MaterialPropertyBlock();
            waterRenderer.GetPropertyBlock(properties);
            properties.SetTexture("_ShoreDistance", distanceTexture != null ? distanceTexture : Texture2D.whiteTexture);
            properties.SetVector("_ShoreBounds", shoreBounds);
            properties.SetVector("_PlatformShore", new Vector4(foamWidth, rippleReach, rippleSpacing, 1));
            properties.SetVector("_PlatformShoreMotion", new Vector4(rippleSpeed, foamStrength, 0, 0));
            waterRenderer.SetPropertyBlock(properties);
        }

        private void OnDisable()
        {
            if (waterRenderer != null && properties != null)
            {
                waterRenderer.GetPropertyBlock(properties);
                properties.SetTexture("_ShoreDistance", Texture2D.whiteTexture);
                properties.SetVector("_ShoreBounds", new Vector4(0, 0, 80, 80));
                properties.SetVector("_PlatformShore", Vector4.zero);
                waterRenderer.SetPropertyBlock(properties);
            }
            ReleaseTexture();
        }

        private void ReleaseTexture()
        {
            if (distanceTexture == null) return;
            if (Application.isPlaying) Destroy(distanceTexture);
            else DestroyImmediate(distanceTexture);
            distanceTexture = null;
        }
    }
}
