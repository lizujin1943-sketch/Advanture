using System;
using System.Collections.Generic;
using Advanture.HookIsland.Simulation;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Advanture.HookIsland
{
    /// <summary>Renders confirmed tiles and their buildable frontier; never changes gameplay state.</summary>
    internal sealed class PlatformPresentation : IDisposable
    {
        private readonly Transform root;
        private readonly IReadOnlyDictionary<TileKind, GameObject> prefabs;
        private readonly List<GameObject> tiles = new List<GameObject>();
        private readonly List<PlatformTile> rendered = new List<PlatformTile>();
        private readonly List<LineRenderer> frontier = new List<LineRenderer>();
        private readonly Material lineMaterial;
        private readonly PlatformShoreline shoreline;
        private int frontierUsed;
        public int TileCount => tiles.Count;

        public PlatformPresentation(Transform parent, IReadOnlyDictionary<TileKind, GameObject> tilePrefabs,
            PlatformShoreline shoreline)
        {
            prefabs = tilePrefabs;
            this.shoreline = shoreline;
            root = new GameObject("Synchronized platform").transform;
            root.SetParent(parent, false);
            lineMaterial = new Material(Shader.Find("Hook Island/Fishing Line"));
        }

        public void Synchronize(IReadOnlyList<PlatformTile> state)
        {
            bool changed = state.Count != tiles.Count;
            for (int i = 0; i < state.Count; i++)
            {
                var tile = state[i];
                if (i < tiles.Count && (rendered[i].Kind != tile.Kind || rendered[i].X != tile.X || rendered[i].Z != tile.Z))
                {
                    Object.Destroy(tiles[i]);
                    tiles[i] = null;
                    changed = true;
                }
                if (i == tiles.Count)
                {
                    tiles.Add(null);
                    rendered.Add(tile);
                }
                if (tiles[i] != null) continue;
                if (!prefabs.TryGetValue(tile.Kind, out var prefab) || prefab == null)
                    throw new InvalidOperationException("Missing tile prefab: " + tile.Kind);
                tiles[i] = Object.Instantiate(prefab, root);
                tiles[i].name = $"{tile.Kind} ({tile.X},{tile.Z})";
                tiles[i].transform.localPosition = new Vector3(tile.X * 2, 0, tile.Z * 2);
                rendered[i] = tile;
            }
            while (tiles.Count > state.Count)
            {
                int i = tiles.Count - 1;
                Object.Destroy(tiles[i]);
                tiles.RemoveAt(i);
                rendered.RemoveAt(i);
            }
            if (changed)
            {
                RefreshFrontier(state);
                shoreline?.Synchronize(state);
            }
        }

        public void ShowFrontier(bool buildMode)
        {
            for (int i = 0; i < frontier.Count; i++) frontier[i].enabled = buildMode && i < frontierUsed;
        }

        private void RefreshFrontier(IReadOnlyList<PlatformTile> state)
        {
            var occupied = new HashSet<Vector2Int>();
            foreach (var tile in state) occupied.Add(new Vector2Int(tile.X, tile.Z));
            var candidates = new HashSet<Vector2Int>();
            var neighbours = new[] { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down };
            foreach (var tile in state)
                foreach (var offset in neighbours)
                {
                    var cell = new Vector2Int(tile.X, tile.Z) + offset;
                    if (PlatformRules.InBounds(cell.x, cell.y) && !occupied.Contains(cell)) candidates.Add(cell);
                }
            frontierUsed = 0;
            foreach (var cell in candidates)
            {
                if (frontierUsed == frontier.Count)
                {
                    var line = NewLine("Adjacent cell boundary", .018f);
                    line.startColor = line.endColor = new Color(.7f, .97f, 1, .32f);
                    frontier.Add(line);
                }
                SetSquare(frontier[frontierUsed++], new Vector3(cell.x * 2, .08f, cell.y * 2));
            }
        }

        private LineRenderer NewLine(string name, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = lineMaterial;
            line.startWidth = line.endWidth = width;
            line.useWorldSpace = true;
            line.loop = true;
            line.enabled = false;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return line;
        }

        private static void SetSquare(LineRenderer line, Vector3 centre)
        {
            line.positionCount = 4;
            for (int i = 0; i < 4; i++)
                line.SetPosition(i, centre + new Vector3(i == 0 || i == 3 ? -1 : 1, 0, i < 2 ? -1 : 1));
        }

        public void Dispose()
        {
            if (root != null) Object.Destroy(root.gameObject);
            Object.Destroy(lineMaterial);
        }
    }
}
