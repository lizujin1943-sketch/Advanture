using UnityEngine;
using UnityEngine.InputSystem;

namespace Advanture.HookIsland
{
    /// <summary>Relative mouse input for one local camera, independently of simulation.</summary>
    public sealed class FishingCameraInput
    {
        private bool tracking;

        public Vector2 ReadLookDelta(bool active)
        {
            var mouse=Mouse.current;
            // Captured pointer positions stay centered. Read device motion instead and
            // discard the first sample after UI/focus changes to avoid a capture jump.
            var delta=active && tracking && mouse!=null?mouse.delta.ReadValue()*.1f:Vector2.zero;
            tracking=active && mouse!=null;
            return delta;
        }
        public void Reset(){tracking=false;}
    }
}
