using UnityEngine;

namespace Advanture.HookIsland
{
    public sealed partial class IslandFishingController
    {
        [SerializeField,Range(50,100)] private float thirdPersonFieldOfView=75;
        [SerializeField,Range(.5f,2)] private float thirdPersonPivotHeight=1;
        [SerializeField,Range(2,5)] private float thirdPersonDistance=3.1f;
        [SerializeField,Range(0,1)] private float thirdPersonShoulderOffset=.6f;
        [SerializeField,Range(.1f,10)] private float mouseLookSensitivity=1.2f;
        [SerializeField,Range(.1f,1)] private float verticalLookSensitivityMultiplier=.75f;
        private FishingCameraView cameraView;
        private bool skipPointerFrame;
        public bool ThirdPerson=>cameraView?.ThirdPerson==true;
        public bool CameraPointerCaptured=>cameraView?.PointerCaptured==true;
        private bool CameraControlsActive=>Playing && !Incapacitated && Application.isFocused &&
            !ShopOpen && Equipment?.InventoryOpen!=true && Construction?.Open!=true && overlay?.MenuOpen!=true && !BeingPulled && InputProvider==null;

        public void TogglePerspective()
        {
            if(cameraView==null || !Playing || !TryGetLocalPlayer(out var player) || player.WaitingToJoin)return;
            var feet=presentation.PlayerPosition(LocalSlot);
            var facing=aim-feet;
            if(facing.sqrMagnitude<.001f)facing=new Vector3(player.AimX/1000f-feet.x,0,player.AimZ/1000f-feet.z);
            cameraView.Toggle(facing,feet);skipPointerFrame=true;
            cameraView.SetPointerCapture(CameraControlsActive);
        }
        private void CaptureCameraInput()
        {
            if(CameraControlsActive && Input.GetKeyDown(KeyCode.V))TogglePerspective();
            if(cameraView==null)return;
            if(cameraView.SetPointerCapture(CameraControlsActive))skipPointerFrame=true;
            if(CameraPointerCaptured && !skipPointerFrame)
            {
                cameraView.Look(FishingCameraInput.ReadLookDelta());
                cameraView.Zoom(Input.mouseScrollDelta.y);
            }
        }
        private void LateUpdate()
        {
            // UI may open later in Update or the connection may fail during PollConnection.
            if(!CameraControlsActive)cameraView?.SetPointerCapture(false);
            skipPointerFrame=false;
        }
        private void OnDisable()
        {
            cameraView?.Reset();
            moveX=moveZ=0;pendingFire=pendingCancel=false;
        }
    }
}
