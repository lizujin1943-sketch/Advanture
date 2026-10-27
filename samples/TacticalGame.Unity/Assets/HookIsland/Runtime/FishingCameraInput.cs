using UnityEngine;
using UnityEngine.InputSystem;

namespace Advanture.HookIsland
{
    /// <summary>Device input for the local camera; simulation never reads Unity devices.</summary>
    public static class FishingCameraInput
    {
        public static Vector2 ReadLookDelta()
        {
            var mouse=Mouse.current;
            // Read relative device motion even while the cursor is locked to screen centre.
            // Preserve the existing Inspector sensitivity (legacy mouse axes used a 0.1 scale).
            return mouse!=null?mouse.delta.ReadValue()*.1f:Vector2.zero;
        }
    }
}
