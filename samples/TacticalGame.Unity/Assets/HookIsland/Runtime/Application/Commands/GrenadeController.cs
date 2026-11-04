using System;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>仅管理本机瞄准和已冻结请求；数量、出手和伤害由确认状态决定。</summary>
    public sealed class GrenadeController : IDisposable
    {
        private readonly IslandFishingController room;
        private readonly GrenadeAimInput press=new GrenadeAimInput();
        private readonly GrenadeRequestBuffer requests=new GrenadeRequestBuffer();
        private readonly GrenadeView view;
        private bool targetValid;
        private int targetX,targetZ;
        private GrenadeState preview;
        private GrenadeResult invalidReason;
        private float messageUntil;
        private string message;
        public bool Aiming=>press.Aiming;
        public bool Pending=>requests.Pending;
        public string Message=>Time.unscaledTime<messageUntil?message:null;
        public bool Busy=>Pending || room.TryGetLocalPlayer(out var p) && p.Grenade.Active;
        public bool BlocksOtherActions=>Aiming || Busy;
        public GrenadeController(IslandFishingController room,Shader lines)
        {this.room=room;view=new GrenadeView(room.transform,lines);}

        public void BeginCapture(bool controlsAllowed)
        {
            bool held=Input.GetMouseButton(0);
            bool switchPressed=Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Alpha3);
            // 与松手同帧打开 UI 时，先取消本地瞄准；不能依赖不同 MonoBehaviour 的 Update 先后顺序。
            bool openingUi=Input.GetKeyDown(KeyCode.I) || Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.Escape) ||
                Input.GetKeyDown(KeyCode.F1) || Input.GetKeyDown(KeyCode.B) || Input.GetKeyDown(KeyCode.E);
            bool allowed=controlsAllowed && !openingUi && room.HoldingGrenade && !Busy && room.Equipment?.Pending!=true && !switchPressed &&
                room.TryGetLocalPlayer(out var p) && !p.Firearm.Busy && p.Phase==HookPhase.Ready && !p.Melee.Active && !p.Knockback.Active;
            if(!allowed || Input.GetMouseButtonDown(1)){press.Interrupt(held);view.HidePreview();return;}
            press.Begin(true,Input.GetMouseButtonDown(0),held);
        }
        /// <summary>镜头后拉后才取屏幕射线；天空不退化成“自动朝前扔”。松手无效时当次直接失败。</summary>
        public void SampleTarget(Camera camera)
        {
            if(!Aiming){view.HidePreview();return;}
            var ray=camera.ScreenPointToRay(Input.mousePosition);
            var feet=room.Presentation.PlayerPosition(room.LocalSlot);
            bool hit=TryPickGround(ray,room.Map,room.State,out var point);
            targetX=Mathf.RoundToInt(point.x*1000);targetZ=Mathf.RoundToInt(point.z*1000);
            invalidReason=hit?GrenadeRules.TryPlan(room.Map,room.State.Tiles,Mathf.RoundToInt(feet.x*1000),Mathf.RoundToInt(feet.z*1000),targetX,targetZ,out preview):GrenadeResult.Blocked;
            targetValid=hit && invalidReason==GrenadeResult.Success;
            if(!hit){point=feet+Vector3.ProjectOnPlane(ray.direction,Vector3.up).normalized*3;point.y=.5f;}
            view.Preview(preview,point,targetValid);
            if(!press.Release(Input.GetMouseButtonUp(0)))return;
            view.HidePreview();
            if(!targetValid){Notify(hit?Explain(invalidReason):"请指向地面或水面");return;}
            if(room.TryGetLocalPlayer(out var p))
            {
                var item=room.State.Tackle?.Inventory.ActiveItem(p.Slot);
                if(item?.Kind==ItemKind.Throwable)requests.TryQueue(p.Grenade.LastRequest,item.Value.Id,targetX,targetZ);
            }
        }
        public static bool TryPickGround(Ray ray,IslandMap map,HookState state,out Vector3 point)
        {
            point=default;
            if(new Plane(Vector3.up,new Vector3(0,.45f,0)).Raycast(ray,out float deckDistance))
            {
                var deck=ray.GetPoint(deckDistance);int x=Mathf.RoundToInt(deck.x*1000),z=Mathf.RoundToInt(deck.z*1000);
                if(GrenadeRules.SurfaceHeight(map,state.Tiles,x,z)>0){point=deck;return Mathf.Abs(deck.x)<=50 && Mathf.Abs(deck.z)<=50;}
            }
            if(!new Plane(Vector3.up,Vector3.zero).Raycast(ray,out float waterDistance))return false;
            point=ray.GetPoint(waterDistance);return Mathf.Abs(point.x)<=50 && Mathf.Abs(point.z)<=50;
        }
        public HookInput Compose(HookInput input)=>requests.Compose(input);
        public void Synchronize(HookState state,Camera camera,bool nearView)
        {
            if(room.TryGetLocalPlayer(out var p) && requests.Observe(p.Grenade,out var result) && result!=GrenadeResult.Success)Notify(Explain(result));
            view.Synchronize(state,room.Presentation,room.LocalSlot,camera,nearView && !Aiming);
        }
        public void Confirm(HookGameplayEvent value)
        {
            view.Confirm(value,room.Presentation.PlayerPosition(room.LocalSlot));
            if(value.Kind==HookGameplayEventKind.GrenadeExploded)
            {
                float distance=Vector3.Distance(new Vector3(value.X/1000f,0,value.Z/1000f),room.Presentation.PlayerPosition(room.LocalSlot));
                if(distance<8)room.ShakeGrenadeCamera(Mathf.Lerp(2.4f,.3f,distance/8));
            }
        }
        public void CancelAim(){press.Interrupt(Input.GetMouseButton(0));view.HidePreview();}
        public void Notify(string text){message=text;messageUntil=Time.unscaledTime+2.5f;}
        public static string Explain(GrenadeResult result)=>result==GrenadeResult.OutOfRange?"落点需在 1.5～18 米内":result==GrenadeResult.Blocked?"投掷路径被挡住":result==GrenadeResult.NotOwned?"没有可用的手榴弹":result==GrenadeResult.Interrupted?"投掷已中断":"当前无法投掷";
        public void Reset(){press.Interrupt();requests.Reset();view.Clear();message=null;}
        public void Dispose()=>view.Dispose();
    }
}
