using System;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Local shoulder composition. These values never enter room or save state.</summary>
    [Serializable]
    public sealed class FirearmCameraSettings
    {
        [Tooltip("Torso position in the viewport, measured from the bottom-left corner.")]
        public Vector2 torsoViewport = new Vector2(.32f, .30f);
        [Range(.3f, 1.2f)] public float torsoHeight = .6f;
        [Range(50, 90)] public float fieldOfView = 65;
        [Range(.7f, 1.2f)] public float distanceMultiplier = .9f;
        [Range(.1f, .5f)] public float transitionSeconds = .22f;
    }
}
