using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Presentation only: waves never enter the deterministic game state.</summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(MeshRenderer))]
    public sealed class IslandOcean : MonoBehaviour
    {
        [Header("Water")]
        public Color shallowColor = new Color(.12f, .76f, .74f);
        public Color deepColor = new Color(.035f, .38f, .56f);
        public Color foamColor = new Color(.83f, 1f, .96f);
        [Range(0, .12f)] public float waveHeight = .035f;
        [Range(0, 3)] public float waveSpeed = .75f;
        [Range(.2f, 2)] public float foamWidth = .85f;
        [Range(1, 20)] public float shallowDistance = 6f;
        public bool previewInEditor = true;

        private MeshRenderer waterRenderer;
        private MaterialPropertyBlock properties;
        private static readonly int OceanTime = Shader.PropertyToID("_OceanTime");
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
