using System;
using System.Collections.Generic;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Advanture.HookIsland
{
    /// <summary>Pooled, non-colliding placement ghosts. Display only; no inventory or simulation writes.</summary>
    internal sealed class PlatformBuildPreviewPresentation : IDisposable
    {
        private readonly Transform root;
        private readonly Material valid, invalid, border;
        private readonly List<Renderer> ghosts = new List<Renderer>();
        private readonly List<LineRenderer> outlines = new List<LineRenderer>();
        private int visible;
        public int VisibleCount => visible;

        public PlatformBuildPreviewPresentation(Transform parent)
        {
            root = new GameObject("Construction previews").transform;
            root.SetParent(parent, false);
            valid = Surface(new Color(.14f, .78f, .32f));
            invalid = Surface(new Color(.92f, .15f, .12f));
            border = new Material(Shader.Find("Hook Island/Fishing Line"));
        }

        public void ShowDraft(IReadOnlyList<PlatformBuildPreview> cells)
        {
            int used = 0;
            foreach (var cell in cells) Show(used++, cell.Tile.X, cell.Tile.Z, cell.Result == BuildResult.Success);
            Finish(used);
        }

        public void ShowPending(IEnumerable<PlatformTile> tiles)
        {
            int used = 0;
            foreach (var tile in tiles) Show(used++, tile.X, tile.Z, true);
            Finish(used);
        }

        public void ShowHover(bool hasPointer, int x, int z, BuildResult result)
        {
            if (hasPointer) Show(0, x, z, result == BuildResult.Success);
            Finish(hasPointer ? 1 : 0);
        }

        public void Hide() => Finish(0);

        private void Show(int index, int x, int z, bool allowed)
        {
            if (index == ghosts.Count)
            {
                var part = PlatformGeometry.Part(root, "Placement preview", Vector3.zero, new Vector3(1.94f, .04f, 1.94f), valid);
                var renderer = part.GetComponent<Renderer>();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                ghosts.Add(renderer);
                var line = new GameObject("Placement boundary").AddComponent<LineRenderer>();
                line.transform.SetParent(root, false);line.sharedMaterial = border;
                line.startWidth = line.endWidth = .055f;line.useWorldSpace = true;line.loop = true;
                line.positionCount = 4;line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                outlines.Add(line);
            }
            var ghost = ghosts[index];var outline = outlines[index];
            ghost.gameObject.SetActive(true);outline.enabled = true;
            ghost.sharedMaterial = allowed ? valid : invalid;
            var centre = new Vector3(x * 2f, PlatformGeometry.DeckHeight + .035f, z * 2f);
            ghost.transform.position = centre;
            outline.startColor = outline.endColor = allowed ? new Color(.57f, 1, .63f) : new Color(1, .54f, .47f);
            for (int i = 0; i < 4; i++)
                outline.SetPosition(i, centre + new Vector3(i == 0 || i == 3 ? -1 : 1, .045f, i < 2 ? -1 : 1));
        }

        private void Finish(int count)
        {
            for (int i = count; i < visible; i++) { ghosts[i].gameObject.SetActive(false);outlines[i].enabled = false; }
            visible = count;
        }

        private static Material Surface(Color color)
        {
            var material = new Material(Shader.Find("Hook Island/Baked Color Surface"));
            material.color = color;material.SetFloat("_Ambient", 1);material.SetFloat("_SunStrength", 0);
            return material;
        }

        public void Dispose()
        {
            Destroy(root.gameObject);Destroy(valid);Destroy(invalid);Destroy(border);
        }

        private static void Destroy(Object target)
        {
            if (Application.isPlaying) Object.Destroy(target);else Object.DestroyImmediate(target);
        }
    }
}
