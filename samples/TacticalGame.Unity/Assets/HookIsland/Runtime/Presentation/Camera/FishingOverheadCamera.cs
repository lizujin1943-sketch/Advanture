using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Fixed-angle overhead camera locked to the rendered local avatar.</summary>
    public sealed class FishingOverheadCamera
    {
        private const float Distance = 32;
        private const float MinimumAspect = 4f / 3f;
        private readonly Camera camera;
        private readonly Vector3 initialFocus;
        private readonly Quaternion rotation;
        private readonly float initialSize;
        private Vector3 focus;
        private float size;

        public FishingOverheadCamera(Camera camera, FishingOverheadSettings settings)
        {
            this.camera = camera;
            initialFocus = settings.focus;
            float pitch = Mathf.Clamp(settings.pitch, 40, 70);
            rotation = Quaternion.Euler(pitch, 0, 0);
            initialSize = Mathf.Clamp(settings.size, 4.8f, 18);
            Reset();
        }

        // Portrait/narrow windows retain the same minimum horizontal play area.
        private float ProjectionSize => size * Mathf.Max(1, MinimumAspect / Mathf.Max(.1f, camera.aspect));

        public void Follow(Vector3 renderedFeet)
        {
            // Avatar presentation already smooths movement. A second camera filter would
            // make the character drift away from screen centre, especially during prediction.
            focus = renderedFeet;
            Apply();
        }

        public void Zoom(float scroll)
        {
            size = Mathf.Clamp(size - scroll * .7f, 4.8f, 18);
            Apply();
        }

        private void Apply()
        {
            camera.orthographic = true;
            camera.orthographicSize = ProjectionSize;
            camera.nearClipPlane = .1f;
            camera.transform.SetPositionAndRotation(focus - rotation * Vector3.forward * Distance, rotation);
        }

        public void Reset()
        {
            size = initialSize;
            focus = initialFocus;
            Apply();
        }
    }
}
