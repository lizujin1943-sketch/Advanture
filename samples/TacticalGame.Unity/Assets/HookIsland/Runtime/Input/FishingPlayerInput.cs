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
        private short moveX,moveZ,aimVertical;
        private bool pendingFire,pendingCancel,pendingReload,skipPointerFrame,entryPerspectivePending=true;
        private Vector3 aim;
        public Vector3 Aim=>aim;
        public bool ThirdPerson=>cameraView?.ThirdPerson==true;
        public bool FirstPerson=>cameraView?.FirstPerson==true;
        public bool CenterAim=>cameraView?.CenterAim==true;
        public bool CameraPointerCaptured=>cameraView?.PointerCaptured==true;
        public void Initialize(Camera camera,FishingCameraView view){viewCamera=camera;cameraView=view;}
        private static bool CameraControlsActive(IslandFishingController room)=>room.Playing && !room.Incapacitated && Application.isFocused &&
            !room.ShopOpen && room.Equipment?.InventoryOpen!=true && room.Construction?.Open!=true && room.Overlay?.MenuOpen!=true && !room.BeingPulled && room.InputProvider==null;
        public void TogglePerspective(IslandFishingController room)
        {
            if(cameraView==null || FirstPerson || !room.Playing || !room.TryGetLocalPlayer(out var player) || player.WaitingToJoin)return;
            var feet=room.Presentation.PlayerPosition(room.LocalSlot);
            var facing=aim-feet;
            if(facing.sqrMagnitude<.001f)facing=new Vector3(player.AimX/1000f-feet.x,0,player.AimZ/1000f-feet.z);
            cameraView.Toggle(facing,feet);skipPointerFrame=true;
            cameraView.SetLookInputActive(CameraControlsActive(room));
        }
        public void Follow(IslandFishingController room,Fisher local)
        {
            if(cameraView==null || local.WaitingToJoin)return;
            EnsureEntryPerspective(room);
            // Poll 可能刚确认一次装备切换或倒地；本帧显示立即跟上确认态，不多推进过渡时间。
            cameraView.UpdateFirearmFraming(room.Playing && room.HoldingFirearm && !room.Incapacitated,0);
            // Follow the same smoothed/predicted avatar that is rendered this frame, including
            // while a UI is open, swimming or being towed. Never feed camera position into authority.
            cameraView.Follow(room.Presentation.PlayerPosition(room.LocalSlot));
        }
        private void EnsureEntryPerspective(IslandFishingController room)
        {
            if(!entryPerspectivePending || !room.Playing || !room.TryGetLocalPlayer(out var player) || player.WaitingToJoin)return;
            if(!CenterAim)TogglePerspective(room);
            entryPerspectivePending=false;
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
            cameraView?.PrepareInput();
            EnsureEntryPerspective(room);
            // 装备及库存使用同一份确认态，避免预测换装被回滚时镜头来回跳动。
            cameraView?.UpdateFirearmFraming(room.Playing && room.HoldingFirearm && !room.Incapacitated,Time.unscaledDeltaTime);
            CaptureCameraInput(room);
            if(!room.Playing || room.Incapacitated || !Application.isFocused || room.ShopOpen || room.Equipment?.InventoryOpen==true || room.BeingPulled || (room.Overlay!=null && room.Overlay.MenuOpen))
            { moveX=moveZ=0;pendingFire=pendingCancel=pendingReload=false;return; }
            Vector2 move=new Vector2((Input.GetKey(KeyCode.D)?1:0)-(Input.GetKey(KeyCode.A)?1:0),
                (Input.GetKey(KeyCode.W)?1:0)-(Input.GetKey(KeyCode.S)?1:0));
            move=cameraView.Movement(move);moveX=(short)(move.x*1000);moveZ=(short)(move.y*1000);
            bool overUi=!CameraPointerCaptured && room.Overlay!=null && room.Overlay.PointerOverHud;
            // Apply zoom before ray sampling; do not move a grid out from under an active drag.
            if(!CenterAim && !overUi && room.Construction?.Dragging!=true && Mathf.Abs(Input.mouseScrollDelta.y)>.01f)
                cameraView.Zoom(Input.mouseScrollDelta.y);
            var pointer=CenterAim?(Vector3)viewCamera.pixelRect.center:Input.mousePosition;
            var ray=viewCamera.ScreenPointToRay(pointer);
            // room.Equipment IDs and their inventory must come from the same confirmed timeline.
            if(room.TryGetLocalPlayer(out var shooter))
            {
                var feet=room.Presentation.PlayerPosition(room.LocalSlot);
                float range=(ActiveToolView.Firearm(room.State,shooter)?.Range??MeleeRules.Equipped(room.State,shooter)?.Range??FishingRules.Range(room.Map,room.State,shooter))/1000f;
                if(room.HoldingFirearm)
                {
                    // 枪械采集真正的上下方向，才能命中海面之上的头部和主眼。
                    // 本地只提交量化方向，不提交 Unity 碰撞体或命中结果；服务器重新计算射线。
                    aimVertical=QuantizeVerticalAim(ray.direction);
                    aim=feet+Vector3.up*(shooter.Swimming.InWater?.35f-feet.y:1.03f)+ray.direction*range;
                }
                else
                {
                    aimVertical=0;
                    aim=cameraView.Aim(ray,feet,range);
                }
            }
            if(!room.HoldingFirearm)aim=FishingAimAssist.Resolve(room,viewCamera,pointer,aim);
            aim.x=Mathf.Clamp(aim.x,-90,90);aim.z=Mathf.Clamp(aim.z,-90,90);
            if(!skipPointerFrame && !overUi && room.Construction?.Open!=true && Input.GetMouseButtonDown(0))
            {if(room.Equipment?.UseSelected()!=true && room.CanCast)
                {pendingFire=true;if(room.HoldingFirearm)room.Presentation?.PresentEmptyFirearm(room.State,room.LocalSlot);}}
            if(!skipPointerFrame && !overUi && room.Construction?.Open!=true && Input.GetMouseButtonDown(1))pendingCancel=true;
            if(room.HoldingFirearm && room.Construction?.Open!=true && Input.GetKeyDown(KeyCode.R) && room.Revival?.OwnsRescuePress!=true)pendingReload=true;
        }
        public HookInput Consume(ulong tick,Func<ulong,HookInput> provider,bool won)
        {
            if(won){ClearCommands();return default;}
            var result=provider!=null?provider(tick):new HookInput(moveX,moveZ,Mathf.RoundToInt(aim.x*1000),Mathf.RoundToInt(aim.z*1000),pendingFire,pendingCancel,reload:pendingReload,aimVertical:aimVertical);
            pendingFire=pendingCancel=pendingReload=false;return result;
        }
        public void ConfirmFirearmShot()=>cameraView?.ConfirmFirearmShot();
        /// <summary>方向的 Y 分量以千分比传输；限制极点，保证水平瞄准方向仍然有定义。</summary>
        public static short QuantizeVerticalAim(Vector3 direction)=>(short)Mathf.Clamp(Mathf.RoundToInt(direction.normalized.y*1000),-995,995);
        public void ClearCommands(){moveX=moveZ=0;pendingFire=pendingCancel=pendingReload=false;}
        public void Reset()
        {ClearCommands();aim=Vector3.zero;aimVertical=0;cameraView?.Reset();cameraInput.Reset();entryPerspectivePending=true;skipPointerFrame=true;}
        public void ReleaseFocus(){ClearCommands();cameraView?.SetLookInputActive(false);cameraInput.Reset();}
        public void EndFrame(IslandFishingController room)
        {
            if(!CameraControlsActive(room)){cameraView?.SetLookInputActive(false);cameraInput.Reset();}
            skipPointerFrame=false;
        }
        public void Dispose(){cameraView?.Dispose();cameraView=null;}
    }
}
