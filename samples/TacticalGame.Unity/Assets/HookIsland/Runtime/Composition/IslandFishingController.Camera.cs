using UnityEngine;

namespace Advanture.HookIsland
{
    public sealed partial class IslandFishingController
    {
        [SerializeField,Range(50,100)] private float thirdPersonFieldOfView=75;
        [SerializeField,Range(.5f,2)] private float thirdPersonPivotHeight=1;
        [SerializeField,Range(2,5)] private float thirdPersonDistance=3.6f;
        [SerializeField,Range(0,1)] private float thirdPersonShoulderOffset=.45f;
        [SerializeField,Range(10,70)] private float thirdPersonInitialPitch=40;
        [SerializeField,Range(.1f,10)] private float mouseLookSensitivity=3;
        [SerializeField,Range(.1f,1)] private float verticalLookSensitivityMultiplier=.85f;
        public bool ThirdPerson=>playerInput.ThirdPerson;
        public bool CameraPointerCaptured=>playerInput.CameraPointerCaptured;
        private void LateUpdate()=>playerInput.EndFrame(this);
        private void OnDisable()=>playerInput.Reset();
    }
}
