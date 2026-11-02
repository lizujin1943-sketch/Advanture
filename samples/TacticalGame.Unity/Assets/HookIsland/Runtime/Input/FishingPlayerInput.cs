using System;
using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Local device sampling, camera control and button latching. Never mutates gameplay state.</summary>
    public sealed class FishingPlayerInput : IDisposable
    {
        private Camera viewCamera;
        private FishingCameraView cameraView;
        private readonly FishingCameraInput cameraInput=new FishingCameraInput();
        private short moveX,moveZ;
        private bool pendingFire,pendingCancel,skipPointerFrame,entryPerspectivePending=true;
        private Vector3 aim;
        public Vector3 Aim=>aim;
        public bool ThirdPerson=>cameraView?.ThirdPerson==true;
        public bool CameraPointerCaptured=>cameraView?.PointerCaptured==true;
        public void Initialize(Camera camera,FishingCameraView view){viewCamera=camera;cameraView=view;}
        private static bool CameraControlsActive(IslandFishingController room)=>room.Playing && !room.Incapacitated && Application.isFocused &&
            !room.ShopOpen && room.Equipment?.InventoryOpen!=true && room.Construction?.Open!=true && room.Overlay?.MenuOpen!=true && !room.BeingPulled && room.InputProvider==null;
        public void TogglePerspective(IslandFishingController room)
        {
            if(cameraView==null || !room.Playing || !room.TryGetLocalPlayer(out var player) || player.WaitingToJoin)return;
            var feet=room.Presentation.PlayerPosition(room.LocalSlot);
            var facing=aim-feet;
            if(facing.sqrMagnitude<.001f)facing=new Vector3(player.AimX/1000f-feet.x,0,player.AimZ/1000f-feet.z);
            cameraView.Toggle(facing,feet);skipPointerFrame=true;
            cameraView.SetLookInputActive(CameraControlsActive(room));
        }
        public void Follow(IslandFishingController room,Fisher local,float deltaTime)
        {
            if(cameraView==null || local.WaitingToJoin)return;
            if(entryPerspectivePending && room.Playing)
            {if(!ThirdPerson)TogglePerspective(room);if(ThirdPerson)entryPerspectivePending=false;}
            // Local prediction moves the rendered avatar. Overhead retains its confirmed follow target.
            cameraView.Follow(room.Presentation.PlayerPosition(room.LocalSlot),new Vector3(local.X/1000f,0,local.Z/1000f),deltaTime);
        }
        private void CaptureCameraInput(IslandFishingController room)
        {
            bool active=CameraControlsActive(room);
            if(active && Input.GetKeyDown(KeyCode.V))TogglePerspective(room);
            if(cameraView==null){cameraInput.Reset();return;}
            if(cameraView.SetLookInputActive(active))skipPointerFrame=true;
            bool canLook=cameraView.LookInputActive && cameraView.PointerCaptured;
            var delta=cameraInput.ReadLookDelta(canLook);
            if(canLook && !skipPointerFrame){cameraView.Look(delta);cameraView.Zoom(Input.mouseScrollDelta.y);}
        }
        public void Capture(IslandFishingController room)
        {
            CaptureCameraInput(room);
            if(!room.Playing || room.Incapacitated || !Application.isFocused || room.ShopOpen || room.Equipment?.InventoryOpen==true || room.BeingPulled || (room.Overlay!=null && room.Overlay.MenuOpen))
            { moveX=moveZ=0;pendingFire=pendingCancel=false;return; }
            Vector2 move=new Vector2((Input.GetKey(KeyCode.D)?1:0)-(Input.GetKey(KeyCode.A)?1:0),
                (Input.GetKey(KeyCode.W)?1:0)-(Input.GetKey(KeyCode.S)?1:0));
            move=cameraView.Movement(move);moveX=(short)(move.x*1000);moveZ=(short)(move.y*1000);
            var pointer=ThirdPerson?(Vector3)viewCamera.pixelRect.center:Input.mousePosition;
            var ray=viewCamera.ScreenPointToRay(pointer);
            // room.Equipment IDs and their inventory must come from the same confirmed timeline.
            if(room.TryGetLocalPlayer(out var shooter))aim=cameraView.Aim(ray,room.Presentation.PlayerPosition(room.LocalSlot),(MeleeRules.Equipped(room.State,shooter)?.Range??FishingRules.Range(room.Map,room.State,shooter))/1000f);
            aim=FishingAimAssist.Resolve(room,viewCamera,pointer,aim);
            aim.x=Mathf.Clamp(aim.x,-90,90);aim.z=Mathf.Clamp(aim.z,-90,90);
            bool overUi=!CameraPointerCaptured && room.Overlay!=null && room.Overlay.PointerOverHud;
            if(!skipPointerFrame && !overUi && room.Construction?.Open!=true && Input.GetMouseButtonDown(0))
            {if(room.Equipment?.UseSelected()!=true && room.CanCast)pendingFire=true;}
            if(!skipPointerFrame && !overUi && room.Construction?.Open!=true && Input.GetMouseButtonDown(1))pendingCancel=true;
            float scroll=Input.mouseScrollDelta.y;
            if(!ThirdPerson && !overUi && Mathf.Abs(scroll)>.01f)viewCamera.orthographicSize=Mathf.Clamp(viewCamera.orthographicSize-scroll*.7f,room.Map.Platform==null?12:5,28);
        }
        public HookInput Consume(ulong tick,Func<ulong,HookInput> provider,bool won)
        {
            if(won){ClearCommands();return default;}
            var result=provider!=null?provider(tick):new HookInput(moveX,moveZ,Mathf.RoundToInt(aim.x*1000),Mathf.RoundToInt(aim.z*1000),pendingFire,pendingCancel);
            pendingFire=pendingCancel=false;return result;
        }
        public void ClearCommands(){moveX=moveZ=0;pendingFire=pendingCancel=false;}
        public void Reset()
        {ClearCommands();aim=Vector3.zero;cameraView?.Reset();cameraInput.Reset();entryPerspectivePending=true;skipPointerFrame=true;}
        public void ReleaseFocus(){ClearCommands();cameraView?.SetLookInputActive(false);cameraInput.Reset();}
        public void EndFrame(IslandFishingController room)
        {
            if(!CameraControlsActive(room)){cameraView?.SetLookInputActive(false);cameraInput.Reset();}
            skipPointerFrame=false;
        }
        public void Dispose(){cameraView?.Dispose();cameraView=null;}
    }
}
