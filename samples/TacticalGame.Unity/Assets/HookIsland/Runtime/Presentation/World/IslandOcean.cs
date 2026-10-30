using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Presentation only: waves never enter the deterministic game state.</summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(MeshRenderer))]
    public sealed class IslandOcean : MonoBehaviour
    {
        [Header("Water")]
        public Color shallowColor = new Color(.10f, .73f, .66f);
        public Color deepColor = new Color(.025f, .40f, .47f);
        public Color foamColor = new Color(.85f, .98f, .96f);
        [Range(0, .12f)] public float waveHeight = .035f;
        [Range(0, 3)] public float waveSpeed = .75f;
        [Range(.2f, 2)] public float foamWidth = .85f;
        [Range(1, 20)] public float shallowDistance = 6f;
        [Header("Small waves and reflections")]
        [Tooltip("Linear RG slope texture. Repeat wrapping and mipmaps are required.")]
        public Texture2D rippleMap;
        [Range(.05f, 1)] public float rippleScale = .18f;
        [Range(0, 1)] public float rippleStrength = .26f;
        [Range(0, 1)] public float reflectionStrength = .48f;
        [Range(0, .2f)] public float skyReflectionBlur = .08f;
        [Range(0, 2)] public float cloudReflectionStrength = 1.5f;
        [Range(0, 1)] public float cloudReflectionCoverage = .4f;
        [Tooltip("Visual cloud layer height in metres; smaller values bring cloud patches closer together.")]
        [Range(4, 60)] public float cloudReflectionHeight = 12f;
        [Tooltip("Additional smooth wave displacement of reflected clouds, in metres.")]
        [Range(0, 1)] public float cloudReflectionWarp = .18f;
        [Range(0, 1)] public float glintStrength = .35f;
        public Color skyHorizonColor = new Color(.57f, .82f, .82f);
        public Color skyZenithColor = new Color(.19f, .48f, .63f);
        public bool previewInEditor = true;

        private MeshRenderer waterRenderer;
        private MaterialPropertyBlock properties;
        private static readonly int OceanTime = Shader.PropertyToID("_OceanTime");
        private static readonly int IslandSkyAvailable = Shader.PropertyToID("_IslandSkyAvailable");
        private static readonly int[] SkyColors =
        {
            Shader.PropertyToID("_SkyMiddleColor"), Shader.PropertyToID("_HorizonGlowColor"),
            Shader.PropertyToID("_CloudColor"), Shader.PropertyToID("_CloudShadowColor")
        };
        private static readonly int[] SkyValues =
        {
            Shader.PropertyToID("_CloudCoverage"), Shader.PropertyToID("_CloudSoftness"),
            Shader.PropertyToID("_CloudScale"), Shader.PropertyToID("_CloudSpeed"), Shader.PropertyToID("_SunGlow")
        };
#if UNITY_EDITOR
        private double nextEditorFrame;
#endif

        private void OnEnable()
        {
            waterRenderer = GetComponent<MeshRenderer>();
            properties = new MaterialPropertyBlock();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= EditorTick;
            UnityEditor.EditorApplication.update += EditorTick;
#endif
            Apply(Time.realtimeSinceStartup);
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= EditorTick;
#endif
        }

        private void OnValidate()
        {
            waveHeight = Mathf.Clamp(waveHeight, 0, .12f);
            waveSpeed = Mathf.Clamp(waveSpeed, 0, 3);
            foamWidth = Mathf.Clamp(foamWidth, .2f, 2);
            shallowDistance = Mathf.Clamp(shallowDistance, 1, 20);
            rippleScale = Mathf.Clamp(rippleScale, .05f, 1);
            rippleStrength = Mathf.Clamp01(rippleStrength);
            reflectionStrength = Mathf.Clamp01(reflectionStrength);
            skyReflectionBlur = Mathf.Clamp(skyReflectionBlur, 0, .2f);
            cloudReflectionStrength = Mathf.Clamp(cloudReflectionStrength, 0, 2);
            cloudReflectionCoverage = Mathf.Clamp01(cloudReflectionCoverage);
            cloudReflectionHeight = Mathf.Clamp(cloudReflectionHeight, 4, 60);
            cloudReflectionWarp = Mathf.Clamp01(cloudReflectionWarp);
            glintStrength = Mathf.Clamp01(glintStrength);
            Apply(Application.isPlaying ? Time.time : Time.realtimeSinceStartup);
        }

        private void Update()
        {
            if (Application.isPlaying) Apply(Time.time);
        }

        public void Apply(float time)
        {
            if (waterRenderer == null) waterRenderer = GetComponent<MeshRenderer>();
            if (waterRenderer == null) return;
            if (properties == null) properties = new MaterialPropertyBlock();
            waterRenderer.GetPropertyBlock(properties);
            properties.SetFloat(OceanTime, time);
            // Unity converts Color properties for the active linear render pipeline.
            properties.SetColor("_ShallowColor", shallowColor);
            properties.SetColor("_DeepColor", deepColor);
            properties.SetColor("_FoamColor", foamColor);
            properties.SetFloat("_WaveHeight", waveHeight);
            properties.SetFloat("_WaveSpeed", waveSpeed);
            properties.SetFloat("_FoamWidth", foamWidth);
            properties.SetFloat("_ShallowDistance", shallowDistance);
            if (rippleMap != null) properties.SetTexture("_RippleMap", rippleMap);
            else properties.SetTexture("_RippleMap", Texture2D.grayTexture);
            properties.SetFloat("_RippleScale", rippleScale);
            properties.SetFloat("_RippleStrength", rippleStrength);
            properties.SetFloat("_ReflectionStrength", reflectionStrength);
            properties.SetFloat("_SkyReflectionBlur", skyReflectionBlur);
            properties.SetFloat("_CloudReflectionStrength", cloudReflectionStrength);
            properties.SetFloat("_CloudReflectionCoverage", cloudReflectionCoverage);
            properties.SetFloat("_CloudReflectionHeight", cloudReflectionHeight);
            properties.SetFloat("_CloudReflectionWarp", cloudReflectionWarp);
            properties.SetFloat("_GlintStrength", glintStrength);
            // Match the actual skybox, including the distant sea's atmospheric fade.
            var sky = RenderSettings.skybox;
            properties.SetColor("_SkyHorizonColor", sky != null && sky.HasProperty("_SkyHorizonColor")
                ? sky.GetColor("_SkyHorizonColor") : skyHorizonColor);
            properties.SetColor("_SkyZenithColor", sky != null && sky.HasProperty("_SkyZenithColor")
                ? sky.GetColor("_SkyZenithColor") : skyZenithColor);
            bool islandSky = sky != null && sky.shader != null && sky.shader.name == "Hook Island/Cartoon Sky";
            properties.SetFloat(IslandSkyAvailable, islandSky ? 1 : 0);
            if (islandSky)
            {
                foreach (int property in SkyColors) properties.SetColor(property, sky.GetColor(property));
                foreach (int property in SkyValues) properties.SetFloat(property, sky.GetFloat(property));
            }
            waterRenderer.SetPropertyBlock(properties);
        }

#if UNITY_EDITOR
        private void EditorTick()
        {
            // Scene switches can leave an already queued editor callback after OnDisable unsubscribes it.
            if(this==null){UnityEditor.EditorApplication.update-=EditorTick;return;}
            if (Application.isPlaying || !previewInEditor || !isActiveAndEnabled) return;
            double now = UnityEditor.EditorApplication.timeSinceStartup;
            if (now < nextEditorFrame) return;
            nextEditorFrame = now + 1.0 / 20.0;
            Apply((float)now);
            UnityEditor.SceneView.RepaintAll();
        }
#endif
    }
}
