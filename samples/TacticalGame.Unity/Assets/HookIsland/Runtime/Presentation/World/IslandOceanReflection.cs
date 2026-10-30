using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Owns the ocean's optional planar reflection. Never reads or writes gameplay state.</summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(MeshRenderer))]
    public sealed class IslandOceanReflection : MonoBehaviour
    {
        [Range(128, 1024)] public int maximumTextureSize = 512;
        [Range(.01f, .2f)] public float clipOffset = .05f;
        public LayerMask reflectedLayers = ~(1 << 5);

        private static bool renderingReflection;
        private MeshRenderer waterRenderer;
        private MaterialPropertyBlock properties;
        private Camera reflectionCamera;
        private RenderTexture reflectionTexture;
        private static readonly int ReflectionTexture = Shader.PropertyToID("_OceanReflection");
        private static readonly int ReflectionMatrix = Shader.PropertyToID("_OceanReflectionVP");
        private static readonly int ReflectionValid = Shader.PropertyToID("_OceanReflectionValid");

        private void OnEnable()
        {
            waterRenderer = GetComponent<MeshRenderer>();
            properties = new MaterialPropertyBlock();
        }

        private void OnWillRenderObject()
        {
            var source = Camera.current;
            if (!isActiveAndEnabled || renderingReflection || source == null ||
                source.cameraType == CameraType.Reflection || source.cameraType == CameraType.Preview ||
                waterRenderer == null || !waterRenderer.enabled) return;

            EnsureResources();
            reflectionCamera.CopyFrom(source);
            reflectionCamera.enabled = false;
            reflectionCamera.cameraType = CameraType.Reflection;
            reflectionCamera.targetTexture = reflectionTexture;
            reflectionCamera.rect = new Rect(0, 0, 1, 1);
            reflectionCamera.allowHDR = false;
            reflectionCamera.allowMSAA = false;
            reflectionCamera.useOcclusionCulling = false;
            reflectionCamera.depthTextureMode = DepthTextureMode.None;
            reflectionCamera.cullingMask = source.cullingMask & reflectedLayers.value;
            // Only geometry belongs in this texture. Unity draws its skybox with
            // perspective rays even for an orthographic camera; projecting that image
            // onto water makes clouds look like flat stickers. The water shader samples
            // the shared sky directly along reflect(-viewDirection, waterNormal).
            reflectionCamera.clearFlags = CameraClearFlags.SolidColor;
            reflectionCamera.backgroundColor = Color.clear;

            float height = transform.position.y;
            var mirror = Matrix4x4.identity;
            mirror.m11 = -1;
            mirror.m13 = 2 * height;
            var view = source.worldToCameraMatrix * mirror;
            var position = source.transform.position;
            position.y = 2 * height - position.y;
            reflectionCamera.transform.SetPositionAndRotation(position,
                Quaternion.Euler(-source.transform.eulerAngles.x, source.transform.eulerAngles.y, source.transform.eulerAngles.z));
            reflectionCamera.worldToCameraMatrix = view;
            var planePosition = view.MultiplyPoint(new Vector3(0, height + clipOffset, 0));
            var planeNormal = view.MultiplyVector(Vector3.up).normalized;
            var clipPlane = new Vector4(planeNormal.x, planeNormal.y, planeNormal.z, -Vector3.Dot(planePosition, planeNormal));
            reflectionCamera.projectionMatrix = source.CalculateObliqueMatrix(clipPlane);

            bool previousCulling = GL.invertCulling;
            bool previousVisibility = waterRenderer.forceRenderingOff;
            renderingReflection = true;
            try
            {
                waterRenderer.forceRenderingOff = true;
                GL.invertCulling = !previousCulling;
                reflectionCamera.Render();
            }
            finally
            {
                GL.invertCulling = previousCulling;
                waterRenderer.forceRenderingOff = previousVisibility;
                renderingReflection = false;
            }

            waterRenderer.GetPropertyBlock(properties);
            properties.SetTexture(ReflectionTexture, reflectionTexture);
            // Camera.Render already handles the render-target orientation. Texture UVs
            // must match WorldToViewportPoint, without applying Metal's RT flip again.
            properties.SetMatrix(ReflectionMatrix, reflectionCamera.projectionMatrix * view);
            properties.SetFloat(ReflectionValid, 1);
            waterRenderer.SetPropertyBlock(properties);
        }

        private void EnsureResources()
        {
            if (reflectionCamera == null)
            {
                var cameraObject = new GameObject("Ocean reflection (presentation)", typeof(Camera));
                cameraObject.hideFlags = HideFlags.HideAndDontSave;
                reflectionCamera = cameraObject.GetComponent<Camera>();
                reflectionCamera.enabled = false;
            }
            // A square target can reuse the source projection at any viewport aspect.
            // Game/Scene views must not resize and allocate a texture on every render.
            int size = Mathf.Clamp(Mathf.ClosestPowerOfTwo(maximumTextureSize), 128, 1024);
            if (reflectionTexture != null && reflectionTexture.width == size && reflectionTexture.height == size)
            {
                if (!reflectionTexture.IsCreated()) reflectionTexture.Create();
                return;
            }
            ReleaseTexture();
            reflectionTexture = new RenderTexture(size, size, 16, RenderTextureFormat.ARGB32)
            {
                name = "Ocean planar reflection",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                useMipMap = false
            };
            reflectionTexture.Create();
        }

        private void OnDisable()
        {
            if (waterRenderer != null && properties != null)
            {
                waterRenderer.GetPropertyBlock(properties);
                properties.SetFloat(ReflectionValid, 0);
                properties.SetTexture(ReflectionTexture, Texture2D.blackTexture);
                waterRenderer.SetPropertyBlock(properties);
            }
            if (reflectionCamera != null)
            {
                reflectionCamera.targetTexture = null;
                ReleaseOwned(reflectionCamera.gameObject);
                reflectionCamera = null;
            }
            ReleaseTexture();
        }

        private void ReleaseTexture()
        {
            if (reflectionTexture == null) return;
            if (reflectionCamera != null) reflectionCamera.targetTexture = null;
            reflectionTexture.Release();
            ReleaseOwned(reflectionTexture);
            reflectionTexture = null;
        }

        private static void ReleaseOwned(Object owned)
        {
            if (Application.isPlaying) Destroy(owned);
            else DestroyImmediate(owned);
        }
    }
}
