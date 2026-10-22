using System;
using System.Collections.Generic;
using Advanture.HookIsland.Simulation;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Advanture.HookIsland
{
    /// <summary>Renders confirmed tiles and placement hints; never changes gameplay state.</summary>
    internal sealed class PlatformPresentation : IDisposable
    {
        private readonly Transform root, ghost;
        private readonly IReadOnlyDictionary<TileKind, GameObject> prefabs;
        private readonly List<GameObject> tiles = new List<GameObject>();
        private readonly List<PlatformTile> rendered = new List<PlatformTile>();
        private readonly List<LineRenderer> frontier = new List<LineRenderer>();
        private readonly Material previewMaterial, lineMaterial;
        private readonly LineRenderer outline;
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
            previewMaterial = new Material(Shader.Find("Hook Island/Baked Color Surface"));
            previewMaterial.SetFloat("_Ambient", .4f);
            previewMaterial.SetFloat("_SunStrength", .85f);
            lineMaterial = new Material(Shader.Find("Hook Island/Fishing Line"));
            ghost = PlatformGeometry.Part(root, "Placement preview", Vector3.zero,
                new Vector3(1.94f, .04f, 1.94f), previewMaterial);
            outline = NewLine("Placement boundary", .045f);
            HidePreview();
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

        public void ShowPreview(bool buildMode, bool hasPointer, int x, int z, BuildResult result)
        {
            for (int i = 0; i < frontier.Count; i++) frontier[i].enabled = buildMode && i < frontierUsed;
            ghost.gameObject.SetActive(buildMode && hasPointer);
            outline.enabled = buildMode && hasPointer;
            if (!buildMode || !hasPointer) return;
            var color = result == BuildResult.Success ? new Color(.22f, .84f, .52f) : new Color(.95f, .26f, .22f);
            previewMaterial.color = color;
            outline.startColor = outline.endColor = color * 1.2f;
            ghost.position = new Vector3(x * 2, PlatformGeometry.DeckHeight + .03f, z * 2);
            SetSquare(outline, ghost.position + Vector3.up * .04f);
        }

        public void HidePreview() => ShowPreview(false, false, 0, 0, BuildResult.Disabled);

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
            Object.Destroy(previewMaterial);
            Object.Destroy(lineMaterial);
        }
    }
}
