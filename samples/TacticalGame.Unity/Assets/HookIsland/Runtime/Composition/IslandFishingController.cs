using System;
using System.Diagnostics;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
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
        internal FishingOverlay Overlay=>overlay;
        private readonly FishingPlayerInput playerInput=new FishingPlayerInput();
        private IslandRoomSession roomSession;
        private string error;
        private bool restoreBackground;
        private uint pendingPurchaseId;
        private readonly RoomRulesRequestBuffer roomRulesRequests=new RoomRulesRequestBuffer();
        private HookUpgrade pendingUpgrade;
        private string shopMessage;
        private float shopMessageUntil;
        public IslandHost Host=>roomSession?.Host;
        public IslandClient Client=>roomSession?.Client;
        public HookState State { get; private set; }
        public FishingFeedback Feedback {get;}=new FishingFeedback();
        public IslandMap Map => map;
        public Camera ViewCamera => viewCamera;
        public FishingPresentation Presentation => presentation;
        public FishingEquipmentController Equipment { get; private set; }
        public PlatformConstruction Construction { get; private set; }
        public bool InRoom=>roomSession?.InRoom==true;
        public bool Restarting=>roomSession?.Restarting==true;
        public bool Playing=>error==null && roomSession?.Playing==true;
        public bool CanRestart=>Failure==null && State?.RoundFinished==true && Host?.CanRestart==true;
        public bool RestartRound()=>CanRestart && roomSession.Restart(State.RoundFinished);
        public bool RoomRulesPending=>roomRulesRequests.Pending;
        public bool RequestFriendlyPranks(bool enabled)=>Playing && Host!=null && LocalSlot==0 && State!=null && !State.RoundFinished &&
            roomRulesRequests.TryQueue(State.RoomRules,enabled);
        public string Failure=>error??roomSession?.Failure;
        public ushort LocalSlot=>Client!=null?Client.Slot:ushort.MaxValue;
        public Vector3 Aim=>playerInput.Aim;
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
        public bool HoldingMelee=>TryGetLocalPlayer(out var player) && MeleeRules.Equipped(State,player)!=null;
        public bool CanCast=>Playing && State?.Won!=true && TryGetLocalPlayer(out var p) && !p.Unavailable && (State.Tackle==null || SwimmingSystem.CanCast(p.Swimming.InWater,p.Vitals.Downed,SwimRingCatalog.Equipped(State.Tackle.Inventory,p.Slot)!=null));
        public bool BeingPulled=>TryGetLocalPlayer(out var p) && p.PulledBy>=0;
        public string Status=>Failure??(Restarting?"正在重新开始 · 等待全队同步…":!InRoom?"创建房间或加入好友":Playing?(map?.Fishing!=null?"捕鱼收入归自己 · 商店库存共享":"共同金币 · 捕鱼收益全队共享"):
            Host?.Restoring==true?$"恢复存档 · 等待原队友 {Host.JoinedCount} / {Host.RestoreRequiredCount} 人":
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
                InitializeSaves();
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
                playerInput.Initialize(viewCamera,new FishingCameraView(viewCamera,map.Platform!=null,thirdPersonFieldOfView,mouseLookSensitivity,
                    thirdPersonPivotHeight,verticalLookSensitivityMultiplier,thirdPersonDistance,thirdPersonShoulderOffset,thirdPersonInitialPitch));
                EnsureRoomSession();
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
            if(!EnsureRoomSession()){error="缺少小岛数据。";return;}
            saves.ResetSession();error=null;roomSession.Create(port,players);
        }
        public void JoinRoom(string address,int port)
        {
            if(!EnsureRoomSession()){error="缺少小岛数据。";return;}
            saves.ResetSession();error=null;roomSession.Join(address,port);
        }
        private void Update()
        {
            Revival?.Capture();playerInput.Capture(this);
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
                presentation.Present(State,Client.Session.InspectPredictedState(),LocalSlot,Aim,
                    Playing && CanCast && !HoldingMelee && overlay?.MenuOpen!=true && !ShopOpen && Equipment?.InventoryOpen!=true && !BeingPulled && Construction?.Open!=true && InputProvider==null);
                Feedback.Update(Client,Client.RoundEpoch,Client.Session.ConfirmedTick,State,LocalSlot,Client.ConfirmedEvents,Time.unscaledTime,
                    overlay?.MenuOpen!=true && !ShopOpen && Equipment?.InventoryOpen!=true,PresentConfirmedEvent);
                if(TryGetLocalPlayer(out var local))playerInput.Follow(this,local,Time.unscaledDeltaTime);
            }
            catch(Exception e)
            {
                roomSession.StopClient(e);CloseShop();UnityEngine.Debug.LogException(e);
            }
        }
        private void PresentConfirmedEvent(HookGameplayEvent value)=>presentation.PresentConfirmedEvent(value,LocalSlot);
        private HookInput Sample(ulong tick)
        {
            if(State?.Won==true){playerInput.ClearCommands();return default;}
            var result=playerInput.Consume(tick,InputProvider,false);
            // Input frames may expire before delivery. Repeat one receipt until confirmation; simulation deduplicates it.
            result=new HookInput(result.MoveX,result.MoveZ,result.AimX,result.AimZ,result.Fire,result.Cancel,
                PurchasePending?pendingUpgrade:result.Upgrade,PurchasePending?pendingPurchaseId:result.PurchaseId,ShopOpen || result.Shopping,
                result.Build,result.BuildX,result.BuildZ,result.BuildId,result.RodAction,result.RodTarget,result.RodBatch,result.RodRequestId,result.Inventory,result.Revival,result.RoomRules,result.Treasure);
            if(Equipment!=null)result=Equipment.Compose(result);
            if(Construction!=null)result=Construction.Compose(result);
            if(Revival!=null)result=Revival.Compose(result);
            if(Treasures!=null)result=Treasures.Compose(result);
            result=roomRulesRequests.Compose(result);
            return result;
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
            playerInput.ClearCommands();return true;
        }
        public void CloseShop()
        {
            if(ShopOpen)GetComponent<FishingEquipmentOverlay>()?.ResetShopSelection();
            ShopOpen=false;ShopMerchantId=-1;playerInput.ClearCommands();
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
        private void OnApplicationFocus(bool focused){if(!focused)playerInput.ReleaseFocus();}
        public void LeaveRoom()
        {
            saves?.ResetSession();error=null;
            if(roomSession!=null)roomSession.Leave();else ResetLocalRoundState();
        }
        // Runs synchronously from the client before it can sample any input for the new epoch.
        private void ResetLocalRoundState()
        {
            State=null;playerInput.Reset();Feedback.Reset();presentation?.ClearPlayers();
            ShopOpen=false;ShopMerchantId=-1;pendingPurchaseId=0;pendingUpgrade=HookUpgrade.None;shopMessage=null;
            roomRulesRequests.Reset();
            Construction?.ResetSession();Equipment?.ResetSession();Revival?.ResetSession();groundItems?.Clear();Enemies?.Clear();Treasures?.Clear();
            Finale?.Clear();Merchants?.Synchronize(null);
            if(overlay!=null)overlay.MenuOpen=false;
            if(map?.Platform!=null && viewCamera!=null && LocalSlot<map.Spawns.Count)
            {var spawn=map.Spawns[LocalSlot];viewCamera.transform.position=new Vector3(spawn.X/1000f,24,spawn.Z/1000f-20);}
        }
        private void OnDestroy()
        {LeaveRoom();playerInput.Dispose();presentation?.Dispose();groundItems?.Dispose();Enemies?.Dispose();Treasures?.Dispose();Finale?.Dispose();Merchants?.Dispose();Application.runInBackground=restoreBackground;}
        private void OnApplicationQuit(){roomSession?.Dispose();}
        public static string LaunchValue(string key)
        {var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,key);return i>=0 && i+1<args.Length?args[i+1]:null;}
        private static int ParseArgument(string key,int fallback)=>int.TryParse(LaunchValue(key),out int value)?value:fallback;
    }
}
