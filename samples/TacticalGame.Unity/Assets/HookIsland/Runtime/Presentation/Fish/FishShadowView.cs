using UnityEngine;
using UnityEngine.Rendering;

namespace Advanture.HookIsland
{
    /// <summary>A soft underwater cue shared by three broad body shapes. Never reveals a species texture.</summary>
    public sealed class FishShadowView
    {
        private static readonly int Profile = Shader.PropertyToID("_Profile");
        private static readonly int Opacity = Shader.PropertyToID("_Opacity");
        private static readonly int Phase = Shader.PropertyToID("_Phase");
        private readonly Transform root;
        private readonly MeshRenderer renderer;
        private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();

        // The presentation owns the parent, shared mesh and material, and releases them together.
        public FishShadowView(Transform parent, int id, Mesh quad, Material material)
        {
            root = new GameObject("Swimming fish shadow " + id).transform;
            root.SetParent(parent, false);
            root.gameObject.AddComponent<MeshFilter>().sharedMesh = quad;
            renderer = root.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            Hide();
        }

        public void Present(FishVisualDefinition fish, float sizeScale, Vector3 position, Vector3 direction, float opacity, float phase)
        {
            root.gameObject.SetActive(opacity > .001f);
            root.position = position;
            if (direction.sqrMagnitude > .000001f) root.rotation = Quaternion.LookRotation(direction);
            float length = fish.baseLengthMetres * sizeScale;
            float width = fish.shadowShape == FishShadowShape.Rounded ? 1.25f : fish.shadowShape == FishShadowShape.Long ? .68f : .85f;
            root.localScale = new Vector3(length * width, 1, length * 1.5f);
            properties.SetFloat(Profile, (int)fish.shadowShape);
            properties.SetFloat(Opacity, Mathf.Clamp01(opacity));
            properties.SetFloat(Phase, phase);
            renderer.SetPropertyBlock(properties);
        }

        public void Hide() => root.gameObject.SetActive(false);

        public static Mesh CreateSharedQuad()
        {
            var mesh = new Mesh { name = "Soft fish shadow quad" };
            mesh.vertices = new[] { new Vector3(-.5f, 0, -.5f), new Vector3(.5f, 0, -.5f), new Vector3(.5f, 0, .5f), new Vector3(-.5f, 0, .5f) };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }
    }
}
