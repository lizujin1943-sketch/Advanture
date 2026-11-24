using Advanture.HookIsland;
using Advanture.Tactical.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
namespace Advanture.TacticalGame.NetworkView
{
    /// <summary>键鼠采样与帧冻结分开：每个渲染帧更新意图，只有会话请求新逻辑帧时才生成输入。</summary>
    public sealed class NetworkInputSource : MonoBehaviour
    {
        private Vector2 movement;
        private bool captured, sprint, fire, reload, jump, resupply, suppressFireUntilRelease;
        private readonly FishingCameraInput mouseLook=new FishingCameraInput();
        private readonly FirearmTriggerInput trigger=new FirearmTriggerInput();
        private FishingCameraView townView;
        private FirearmCameraSettings firearmSettings;

        public float Yaw { get; private set; }
        public float Pitch { get; private set; }
        public bool HasControl => captured && Cursor.lockState == CursorLockMode.Locked && Application.isFocused;

        public void UseTownCamera(Camera camera,float eyeHeight)
        {
            firearmSettings=new FirearmCameraSettings { eyeHeight=eyeHeight };
            townView=new FishingCameraView(camera,false,firearmSettings:firearmSettings);
        }

        public void InitializeLook(float yaw)
        {
            ClearCommands();
            Yaw=yaw; Pitch=firearmSettings?.initialPitch ?? 0;
            if(townView==null)return;
            townView.UpdateFirearmFraming(true,0);
            townView.SetFacing(Yaw,Pitch);
            mouseLook.Reset();
        }

        public void FollowTownCamera(Vector3 feet,TacticalPlayer player,bool externalInput,float delta)
        {
            if(externalInput)townView.SetFacing(player.Yaw*.01f,player.Pitch*.01f);
            townView.UpdateFirearmFraming(true,delta);
            townView.Follow(feet);
        }

        public void ConfirmShot() => townView?.ConfirmFirearmShot();

        public void CaptureDevice(bool playing, bool allowCapture = true)
        {
            var keyboard = Keyboard.current; var mouse = Mouse.current;
            if (!playing || !Application.isFocused) { Release(); return; }
            if (townView!=null)
            {
                if (!allowCapture || !townView.FirstPerson) { Release(); return; }
                bool changed=townView.SetLookInputActive(true);
                captured=townView.PointerCaptured;
                if(changed)mouseLook.Reset();
                townView.Look(mouseLook.ReadLookDelta(captured),85);
                Yaw=townView.Yaw;Pitch=townView.Pitch;
                trigger.Capture(1,true,captured && !changed,mouse?.leftButton.wasPressedThisFrame==true,mouse?.leftButton.isPressed==true);
            }
            else
            {
                if ((captured && Cursor.lockState != CursorLockMode.Locked) || keyboard?.escapeKey.wasPressedThisFrame==true)
                {Release();return;}
                if (!HasControl)
                {
                    movement=Vector2.zero;
                    if (allowCapture && mouse?.leftButton.wasPressedThisFrame==true)
                    { Cursor.lockState=CursorLockMode.Locked; Cursor.visible=false; captured=true; suppressFireUntilRelease=true; }
                    return;
                }
                Vector2 look=mouse!=null?mouse.delta.ReadValue():Vector2.zero;
                Yaw=Mathf.Repeat(Yaw+look.x*.12f,360);
                Pitch=Mathf.Clamp(Pitch-look.y*.12f,-85,85);
                if(mouse?.leftButton.isPressed!=true)suppressFireUntilRelease=false;
                fire=mouse?.leftButton.isPressed==true && !suppressFireUntilRelease;
            }
            if(!HasControl){ClearCommands();return;}
            movement = keyboard == null ? Vector2.zero : new Vector2((keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0),
                (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0));
            sprint = keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame) reload = true;
            if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame) jump = true;
            if (keyboard != null && keyboard.eKey.wasPressedThisFrame) resupply = true;
        }

        public TacticalInput Sample(ulong tick)
        {
            if (!HasControl) return default; // 失焦/菜单也显式冻结中性输入，后台会话照常推进。
            bool requestReload = reload; reload = false;
            bool requestJump = jump; jump = false;
            bool requestResupply = resupply; resupply = false;
            return InputQuantizer.Create(movement, Yaw, Pitch, sprint, fire: townView!=null?trigger.Consume():fire, reload: requestReload, jump: requestJump, resupply: requestResupply);
        }
        private void ClearCommands()
        {
            movement=Vector2.zero;
            sprint=reload=jump=resupply=fire=false;
            trigger.Interrupt();
        }
        public void Release()
        {
            captured=false;ClearCommands();mouseLook.Reset();
            if(townView!=null)townView.SetLookInputActive(false);
            else {Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}
        }
        public void ResetSession(){Release();townView?.Reset();}
        private void OnApplicationFocus(bool focused) { if (!focused) Release(); }
        private void OnDisable() => Release();
    }
}
