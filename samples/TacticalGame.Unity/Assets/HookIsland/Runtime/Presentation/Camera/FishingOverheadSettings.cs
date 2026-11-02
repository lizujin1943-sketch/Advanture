using System;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Local overhead framing, independent of simulation and room configuration.</summary>
    [Serializable]
    public sealed class FishingOverheadSettings
    {
        [Tooltip("Lobby/reset focus. During play the camera always follows the local avatar.")]
        public Vector3 focus = new Vector3(0, .45f, 0);
        [Range(40, 70)] public float pitch = 50;
        [Range(4.8f, 18)] public float size = 5.5f;
    }
}
