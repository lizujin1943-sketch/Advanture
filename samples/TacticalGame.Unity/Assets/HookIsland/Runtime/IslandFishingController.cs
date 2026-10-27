using System;
using System.Diagnostics;
using System.Net;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using FrameSync.Sessions;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Unity input/presentation adapter. All shared gameplay runs in the independent simulation.</summary>
    public sealed partial class IslandFishingController : MonoBehaviour
    {
        [SerializeField] private TextAsset mapData;
        [SerializeField] private Camera viewCamera;
        [SerializeField] private Shader lineShader;
        [SerializeField] private PlatformSettings platformSettings;
        private readonly Stopwatch clock=new Stopwatch();
        private IslandMap map;
        private FishingPresentation presentation;
        private GroundItemPresentation groundItems;
        public EnemyPresentation Enemies { get; private set; }
        public FinalePresentation Finale { get; private set; }
        public MerchantPresentation Merchants { get; private set; }
        public TreasureController Treasures { get; private set; }
        public RevivalController Revival { get; private set; }
        public bool Incapacitated=>State?.Won==true || TryGetLocalPlayer(out var p) && State.Tackle!=null && p.Vitals.Downed;
        private FishingOverlay overlay;
        private short moveX,moveZ;
        private bool pendingFire,pendingCancel;
        private string error;
        private Vector3 aim;
        private bool restoreBackground;
        private uint pendingPurchaseId;
        private readonly RoomRulesRequestBuffer roomRulesRequests=new RoomRulesRequestBuffer();
        private HookUpgrade pendingUpgrade;
        private string shopMessage;
        private float shopMessageUntil;
        public IslandHost Host { get; private set; }
        public IslandClient Client { get; private set; }
        public HookState State { get; private set; }
        public IslandMap Map => map;
        public Camera ViewCamera => viewCamera;
        public FishingPresentation Presentation => presentation;
        public FishingEquipmentController Equipment { get; private set; }
        public PlatformConstruction Construction { get; private set; }
        public bool InRoom=>Client!=null;
        public bool Restarting=>Host?.Restarting==true || Client?.Restarting==true;
        public bool Playing=>Failure==null && !Restarting && Client?.Session?.Status==SessionStatus.Running && Client.Session.LocalParticipant?.IsActive==true;
        public bool CanRestart=>Failure==null && State?.RoundFinished==true && Host?.CanRestart==true;
        public bool RestartRound()=>CanRestart && Host.TryRestart();
        public bool RoomRulesPending=>roomRulesRequests.Pending;
        public bool RequestFriendlyPranks(bool enabled)=>Playing && Host!=null && LocalSlot==0 && State!=null && !State.RoundFinished &&
            roomRulesRequests.TryQueue(State.RoomRules,enabled);
        public string Failure=>error??Host?.Failure??Client?.Failure;
        public ushort LocalSlot=>Client!=null?Client.Slot:ushort.MaxValue;
        public Vector3 Aim=>aim;
        public Func<ulong,HookInput> InputProvider { get; set; }
        public bool ShopOpen { get; private set; }
        public int ShopMerchantId { get; private set; }=-1;
        public bool PurchasePending=>pendingPurchaseId!=0;
        public string ShopMessage=>PurchasePending?"正在购买…":Time.unscaledTime<shopMessageUntil?shopMessage:null;
        public int NearestMerchantId
        {
            get
            {
                if(map==null || !TryGetLocalPlayer(out var p) || map.Fishing!=null && !MerchantRules.CanInteract(p))return -1;
                return map.Fishing!=null?MerchantRules.Nearest(map,State.Tiles,p):
                    map.Platform==null && HookUpgrades.IsNearShop(p.X,p.Z)?0:-1;
            }
        }
        public bool NearShop=>NearestMerchantId>=0;
        public bool CanShopAt(int id)=>id>=0 && map!=null && TryGetLocalPlayer(out var p) && (map.Fishing==null || MerchantRules.CanInteract(p)) &&
            (map.Fishing!=null?MerchantRules.Near(map,State.Tiles,p,id):id==0 && map.Platform==null && HookUpgrades.IsNearShop(p.X,p.Z));
        public bool CanCast=>Playing && State?.Won!=true && TryGetLocalPlayer(out var p) && !p.Unavailable && (State.Tackle==null || SwimmingSystem.CanCast(p.Swimming.InWater,p.Vitals.Downed,SwimRingCatalog.Equipped(State.Tackle.Inventory,p.Slot)!=null));
        public bool BeingPulled=>TryGetLocalPlayer(out var p) && p.PulledBy>=0;
        public string Status=>Failure??(Restarting?"正在重新开始 · 等待全队同步…":!InRoom?"创建房间或加入好友":Playing?(map?.Fishing!=null?"捕鱼收入归自己 · 商店库存共享":"共同金币 · 捕鱼收益全队共享"):
            Host!=null && Host.Session==null?$"等待好友 {Host.JoinedCount} / {Host.Capacity} 人":Synchronizing?"正在同步当前海域…":"已连接，等待房主开始…");

        public void Configure(TextAsset data,Camera camera,Shader lines)
        { mapData=data;platformSettings=null;viewCamera=camera;lineShader=lines; }
        public void ConfigurePlatform(PlatformSettings settings,Camera camera,Shader lines)
        { mapData=null;platformSettings=settings;viewCamera=camera;lineShader=lines; }
        private void Awake()
        {
            restoreBackground=Application.runInBackground;Application.runInBackground=true;
            Cursor.lockState=CursorLockMode.None;Cursor.visible=true;clock.Start();
            overlay=GetComponent<FishingOverlay>();
            Construction=GetComponent<PlatformConstruction>();
            Equipment=GetComponent<FishingEquipmentController>();Revival=GetComponent<RevivalController>();
            try
            {
                map=platformSettings!=null?platformSettings.CreateMap():IslandMap.Decode(mapData.bytes);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if(LaunchValue("-island-commission-smoke")=="1")map=CommissionSmokePilot.CreateMap();
                if(LaunchValue("-island-ocean-smoke")=="1")map=OceanSmokePilot.CreateMap();
                if(LaunchValue("-island-finale-smoke")=="1")map=FinaleSmokePilot.CreateMap();
                if(LaunchValue("-island-merchant-smoke")=="1")map=MerchantSmokePilot.CreateMap();
                if(LaunchValue("-island-recovery-smoke")=="1")map=RecoverySmokePilot.CreateMap();
                if(LaunchValue("-island-treasure-smoke")=="1")map=TreasureSmokePilot.CreateMap();
                if(LaunchValue("-island-cooperative-smoke")=="1")map=CooperativeFishingSmokePilot.CreateMap();
                if(LaunchValue("-island-room-rules-smoke")=="1")map=RoomRulesSmokePilot.CreateMap();
#endif
                presentation=new FishingPresentation(transform,viewCamera,lineShader,map);
                cameraView=new FishingCameraView(viewCamera,map.Platform!=null,thirdPersonFieldOfView,mouseLookSensitivity,
                    thirdPersonPivotHeight,verticalLookSensitivityMultiplier,thirdPersonDistance,thirdPersonShoulderOffset);
                if(map.Fishing!=null)groundItems=new GroundItemPresentation(transform);
                if(map.Fishing?.Treasure.Enabled==true)Treasures=new TreasureController(this,viewCamera);
                if(map.Fishing?.Enemies.Enabled==true)Enemies=new EnemyPresentation(transform,viewCamera,lineShader,map.Fishing.Enemies);
                if(map.Fishing?.Finale.Enabled==true)Finale=new FinalePresentation(transform,lineShader,map.Fishing.Finale);
                if(map.Fishing?.Merchants.Definitions.Count>0)Merchants=new MerchantPresentation(transform,map.Fishing);
            }
            catch(Exception e){error="小岛数据无法读取："+e.Message;UnityEngine.Debug.LogException(e);}
        }
        private void Start()
        {
            if(LaunchValue("-island-role")=="host")HostRoom(ParseArgument("-island-port",7778),ParseArgument("-island-players",2));
            else if(LaunchValue("-island-role")=="join")JoinRoom(LaunchValue("-island-address")??"127.0.0.1",ParseArgument("-island-port",7778));
        }
        public void HostRoom(int port,int players)
        {
            LeaveRoom();if(map==null){error="缺少小岛数据。";return;}
            try { Host=new IslandHost(map,port,players);ConnectClient(IPAddress.Loopback,Host.Port,0); }
            catch(Exception e){LeaveRoom();error="创建房间失败："+e.Message;}
        }
        public void JoinRoom(string address,int port)
        {
            LeaveRoom();if(map==null){error="缺少小岛数据。";return;}
            try
            {
                if(!IPAddress.TryParse(address,out var ip) || ip.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork)
                    throw new ArgumentException("请填写房主的 IPv4 地址");
                ConnectClient(ip,port,ushort.MaxValue);
            }
            catch(Exception e){LeaveRoom();error="加入房间失败："+e.Message;}
        }
        private void Update()
        {
            CaptureInput();
            if(!PollConnection()){CloseShop();return;}
            try
            {
                State=Client.Session.InspectState();
                roomRulesRequests.Observe(State.RoomRules);
                ObservePurchase();
                if(Incapacitated){CloseShop();Equipment?.Close();Construction?.Close();}
                groundItems?.Synchronize(State.Tackle?.Inventory);
                Enemies?.Synchronize(State);Treasures?.Synchronize(State);
                Finale?.Synchronize(State);
                Merchants?.Synchronize(State);
                if(ShopOpen && (!Playing || !CanShopAt(ShopMerchantId)))CloseShop();
                presentation.Present(State,Client.Session.InspectPredictedState(),LocalSlot,aim,
                    Playing && CanCast && !overlay.MenuOpen && !ShopOpen && Equipment?.InventoryOpen!=true && !BeingPulled && Construction?.Open!=true && InputProvider==null);
                if(TryGetLocalPlayer(out var local) && !local.WaitingToJoin)
                {
                    // Third person follows the displayed (locally predicted) avatar; overhead keeps its confirmed follow.
                    cameraView.Follow(presentation.PlayerPosition(LocalSlot),new Vector3(local.X/1000f,0,local.Z/1000f),Time.unscaledDeltaTime);
                }
            }
            catch(Exception e)
            {
                error="本次连接已停止："+e.Message;
                CloseShop();
                Client?.Dispose();UnityEngine.Debug.LogException(e);
            }
        }
        private void CaptureInput()
        {
            Revival?.Capture();
            CaptureCameraInput();
            if(!Playing || Incapacitated || !Application.isFocused || ShopOpen || Equipment?.InventoryOpen==true || BeingPulled || (overlay!=null && overlay.MenuOpen))
            { moveX=moveZ=0;pendingFire=pendingCancel=false;return; }
            Vector2 move=new Vector2((Input.GetKey(KeyCode.D)?1:0)-(Input.GetKey(KeyCode.A)?1:0),
                (Input.GetKey(KeyCode.W)?1:0)-(Input.GetKey(KeyCode.S)?1:0));
            move=cameraView.Movement(move);moveX=(short)(move.x*1000);moveZ=(short)(move.y*1000);
            var pointer=ThirdPerson && Construction?.Open!=true?new Vector3(Screen.width*.5f,Screen.height*.5f):Input.mousePosition;
            var ray=viewCamera.ScreenPointToRay(pointer);
            // Equipment IDs and their inventory must come from the same confirmed timeline.
            if(TryGetLocalPlayer(out var shooter))aim=cameraView.Aim(ray,presentation.PlayerPosition(LocalSlot),FishingRules.Range(map,State,shooter)/1000f);
            // A small screen-space assist makes clicking an airborne fish match its XZ hit shape.
            // The synchronized simulation still validates timing, range and collision.
            float nearest=27f*27f;
            if(State!=null && Construction?.Open!=true)foreach(var f in State.Fish)
            {
                if(f.Phase!=FishPhase.Jumping)continue;
                var screen=viewCamera.WorldToScreenPoint(new Vector3(f.X/1000f,f.Y/1000f,f.Z/1000f));
                float d=((Vector2)screen-(Vector2)pointer).sqrMagnitude;
                if(screen.z>0 && d<14f*14f && d<nearest){nearest=d;aim=new Vector3(f.X/1000f,.75f,f.Z/1000f);}
            }
            if(State!=null && Construction?.Open!=true)foreach(var d in State.Driftwood)
            {
                if(d.Phase!=DriftwoodPhase.Floating)continue;
                var screen=viewCamera.WorldToScreenPoint(new Vector3(d.X/1000f,.13f,d.Z/1000f));
                float distanceToPointer=((Vector2)screen-(Vector2)pointer).sqrMagnitude;
                if(screen.z>0 && distanceToPointer<18f*18f && distanceToPointer<nearest)
                {nearest=distanceToPointer;aim=new Vector3(d.X/1000f,.75f,d.Z/1000f);}
            }
            if(State!=null && Construction?.Open!=true)foreach(var chest in State.Treasure.Chests)
            {
                if(chest.Phase==TreasurePhase.Waiting || chest.Phase==TreasurePhase.Landed)continue;
                var screen=viewCamera.WorldToScreenPoint(new Vector3(chest.X/1000f,.4f,chest.Z/1000f));
                float d=((Vector2)screen-(Vector2)pointer).sqrMagnitude;
                if(screen.z>0 && d<18f*18f && d<nearest){nearest=d;aim=new Vector3(chest.X/1000f,.75f,chest.Z/1000f);}
            }
            if(State!=null && Construction?.Open!=true)foreach(var friend in State.Players)
            {
                if(friend.WaitingToJoin || friend.Revival.Dead || friend.Slot==LocalSlot || (!friend.Swimming.InWater && (friend.Unavailable || !State.RoomRules.FriendlyPranksEnabled || friend.Shopping || friend.ProtectionTicks>0)) || friend.PulledBy>=0 || friend.HookedPlayer>=0)continue;
                var screen=viewCamera.WorldToScreenPoint(presentation.PlayerPosition(friend.Slot)+Vector3.up*(map.Platform==null?1.2f:.6f));
                float d=((Vector2)screen-(Vector2)pointer).sqrMagnitude;
                if(screen.z>0 && d<nearest){nearest=d;aim=new Vector3(friend.X/1000f,.75f,friend.Z/1000f);}
            }
            if(State!=null && Construction?.Open!=true)foreach(var f in State.Enemies.Piranhas)
            {
                if(f.Owner>=0 || f.RespawnTicks>0 || f.Escape.Active)continue;
                var position=new Vector3(f.X/1000f,.12f,f.Z/1000f);var screen=viewCamera.WorldToScreenPoint(position);
                float d=((Vector2)screen-(Vector2)pointer).sqrMagnitude;
                if(screen.z>0 && d<18f*18f && d<nearest){nearest=d;aim=new Vector3(position.x,.75f,position.z);}
            }
            if(State!=null && Construction?.Open!=true && map.Fishing?.Enemies.MonsterEnabled==true && State.Enemies.Monster.Phase==MonsterPhase.Windup)
            {
                var enemy=State.Enemies.Monster;var position=new Vector3(enemy.X/1000f,.75f,enemy.Z/1000f);
                var screen=viewCamera.WorldToScreenPoint(position);float d=((Vector2)screen-(Vector2)pointer).sqrMagnitude;
                if(screen.z>0 && d<24f*24f && d<nearest)aim=position;
            }
            if(State?.Finale.Phase==FinalePhase.Surfaced && Construction?.Open!=true)
            {
                var giant=State.Finale;var position=new Vector3(giant.X/1000f,.75f,giant.Z/1000f);
                var screen=viewCamera.WorldToScreenPoint(position);float d=((Vector2)screen-(Vector2)pointer).sqrMagnitude;
                if(screen.z>0 && d<28f*28f && d<nearest)aim=position;
            }
            aim.x=Mathf.Clamp(aim.x,-90,90);aim.z=Mathf.Clamp(aim.z,-90,90);
            bool overUi=!CameraPointerCaptured && overlay!=null && overlay.PointerOverHud;
            if(!skipPointerFrame && !overUi && CanCast && Construction?.Open!=true && Input.GetMouseButtonDown(0))pendingFire=true;
            if(!skipPointerFrame && !overUi && Construction?.Open!=true && Input.GetMouseButtonDown(1))pendingCancel=true;
            float scroll=Input.mouseScrollDelta.y;
            if(!ThirdPerson && !overUi && Mathf.Abs(scroll)>.01f)viewCamera.orthographicSize=Mathf.Clamp(viewCamera.orthographicSize-scroll*.7f,map.Platform==null?12:5,28);
        }
        private HookInput Sample(ulong tick)
        {
            if(State?.Won==true){moveX=moveZ=0;pendingFire=pendingCancel=false;return default;}
            var result=InputProvider!=null?InputProvider(tick):
                new HookInput(moveX,moveZ,Mathf.RoundToInt(aim.x*1000),Mathf.RoundToInt(aim.z*1000),pendingFire,pendingCancel);
            // Input frames may expire before delivery. Repeat one receipt until confirmation; simulation deduplicates it.
            result=new HookInput(result.MoveX,result.MoveZ,result.AimX,result.AimZ,result.Fire,result.Cancel,
                PurchasePending?pendingUpgrade:result.Upgrade,PurchasePending?pendingPurchaseId:result.PurchaseId,ShopOpen || result.Shopping,
                result.Build,result.BuildX,result.BuildZ,result.BuildId,result.RodAction,result.RodTarget,result.RodBatch,result.RodRequestId,result.Inventory,result.Revival,result.RoomRules,result.Treasure);
            if(Equipment!=null)result=Equipment.Compose(result);
            if(Construction!=null)result=Construction.Compose(result);
            if(Revival!=null)result=Revival.Compose(result);
            if(Treasures!=null)result=Treasures.Compose(result);
            result=roomRulesRequests.Compose(result);
            pendingFire=pendingCancel=false;return result;
        }
        public bool TryGetLocalPlayer(out Fisher player)
        {
            if(State!=null && LocalSlot<State.Players.Count){player=State.Players[LocalSlot];return true;}
            player=default;return false;
        }
        public bool OpenShop()
        {
            if(!Playing || Incapacitated || Treasures?.Pending==true || !NearShop || !TryGetLocalPlayer(out var p) || p.PulledBy>=0 || p.HookedPlayer>=0)return false;
            Equipment?.Close();Construction?.Close();ShopMerchantId=NearestMerchantId;ShopOpen=true;
            GetComponent<FishingEquipmentOverlay>()?.ResetShopSelection();if(overlay!=null)overlay.MenuOpen=false;
            moveX=moveZ=0;pendingFire=pendingCancel=false;return true;
        }
        public void CloseShop()
        {
            if(ShopOpen)GetComponent<FishingEquipmentOverlay>()?.ResetShopSelection();
            ShopOpen=false;ShopMerchantId=-1;moveX=moveZ=0;pendingFire=pendingCancel=false;
        }
        public bool RequestUpgrade(HookUpgrade upgrade)
        {
            if(!Playing || !ShopOpen || Treasures?.Pending==true || PurchasePending || !TryGetLocalPlayer(out var p) ||
                (upgrade!=HookUpgrade.Range && upgrade!=HookUpgrade.Speed) || p.LastPurchaseId==uint.MaxValue)return false;
            var result=HookUpgrades.CheckPurchase(p,State.Coins,upgrade);
            if(result!=PurchaseResult.Success){SetShopMessage(PurchaseMessage(result));return false;}
            pendingUpgrade=upgrade;pendingPurchaseId=p.LastPurchaseId+1;
            return true;
        }
        private void ObservePurchase()
        {
            if(!PurchasePending || !TryGetLocalPlayer(out var p) || p.LastPurchaseId<pendingPurchaseId)return;
            SetShopMessage(p.LastPurchaseResult==PurchaseResult.Success?
                (pendingUpgrade==HookUpgrade.Range?$"射程升级成功 · 现在可出钩 {HookUpgrades.Range(p.RangeLevel)/1000} 米":
                    $"速度升级成功 · 现在为 {HookUpgrades.SpeedPerTick(p.SpeedLevel)*HookGame.Rate/1000} 米/秒"):
                PurchaseMessage(p.LastPurchaseResult));
            pendingPurchaseId=0;pendingUpgrade=HookUpgrade.None;
        }
        private void SetShopMessage(string text){shopMessage=text;shopMessageUntil=Time.unscaledTime+5;}
        public static string PurchaseMessage(PurchaseResult result)
        {
            switch(result)
            {
                case PurchaseResult.TooFar:return "靠近商店后才能升级";
                case PurchaseResult.InsufficientCoins:return "全队金币不足，先去钓几条鱼吧";
                case PurchaseResult.MaxLevel:return "这项升级已达到满级";
                case PurchaseResult.HookBusy:return "等钩索收回后再升级";
                default:return "";
            }
        }
        private void OnApplicationFocus(bool focused)
        {if(!focused){moveX=moveZ=0;pendingFire=pendingCancel=false;cameraView?.SetPointerCapture(false);}}
        public void LeaveRoom()
        {
            if(Client!=null)Client.RoundChanged-=ResetLocalRoundState;
            Client?.Dispose();Host?.Dispose();Client=null;Host=null;error=null;
            resumeTicket=null;connectionAddress=null;connectionPort=0;connectionFailed=hostStopped=false;
            ResetLocalRoundState();
        }
        // Runs synchronously from the client before it can sample any input for the new epoch.
        private void ResetLocalRoundState()
        {
            State=null;moveX=moveZ=0;pendingFire=pendingCancel=false;aim=Vector3.zero;presentation?.ClearPlayers();
            cameraView?.Reset();skipPointerFrame=true;
            ShopOpen=false;ShopMerchantId=-1;pendingPurchaseId=0;pendingUpgrade=HookUpgrade.None;shopMessage=null;
            roomRulesRequests.Reset();
            Construction?.ResetSession();Equipment?.ResetSession();Revival?.ResetSession();groundItems?.Clear();Enemies?.Clear();Treasures?.Clear();
            Finale?.Clear();Merchants?.Synchronize(null);
            if(overlay!=null)overlay.MenuOpen=false;
            if(map?.Platform!=null && viewCamera!=null && LocalSlot<map.Spawns.Count)
            {var spawn=map.Spawns[LocalSlot];viewCamera.transform.position=new Vector3(spawn.X/1000f,24,spawn.Z/1000f-20);}
        }
        private void OnDestroy()
        {LeaveRoom();cameraView?.Dispose();presentation?.Dispose();groundItems?.Dispose();Enemies?.Dispose();Treasures?.Dispose();Finale?.Dispose();Merchants?.Dispose();Application.runInBackground=restoreBackground;}
        private void OnApplicationQuit(){Client?.Dispose();Host?.Dispose();}
        public static string LaunchValue(string key)
        {var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,key);return i>=0 && i+1<args.Length?args[i+1]:null;}
        private static int ParseArgument(string key,int fallback)=>int.TryParse(LaunchValue(key),out int value)?value:fallback;
    }
}
