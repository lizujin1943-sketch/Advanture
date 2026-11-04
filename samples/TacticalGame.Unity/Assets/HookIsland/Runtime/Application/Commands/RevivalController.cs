using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Local hold intent and acknowledged paid-respawn delivery. Never mutates gameplay state.</summary>
    public sealed class RevivalController : MonoBehaviour
    {
        private IslandFishingController room;
        private readonly RevivalRequestBuffer requests=new RevivalRequestBuffer();
        private readonly RescuePressOwnership press=new RescuePressOwnership();
        private int heldTarget=>press.Target;
        public bool Pending=>requests.Pending;
        public bool OwnsRescuePress=>press.OwnedByRescue;
        public string Message { get; private set; }
        private void Awake(){room=GetComponent<IslandFishingController>();}
        public void Capture()
        {
            if(room.TryGetLocalPlayer(out var local) && requests.Observe(local.Revival,out var result))Message=Explain(result);
            if(!room.Playing || room.InputProvider!=null || !Application.isFocused || !Input.GetKey(KeyCode.R) ||
                room.ShopOpen || room.Grenades?.BlocksOtherActions==true || room.Equipment?.InventoryOpen==true || room.Construction?.Open==true ||
                GetComponent<FishingOverlay>().MenuOpen || !room.TryGetLocalPlayer(out local) || local.Vitals.Downed)
            {press.Interrupt(Input.GetKey(KeyCode.R));return;}
            bool valid=heldTarget>=0 && heldTarget<room.State.Players.Count && RevivalSystem.CanHelp(room.Map,room.State.Tiles,local,room.State.Players[heldTarget]);
            bool pressed=Input.GetKeyDown(KeyCode.R);
            press.Capture(pressed,true,pressed?NearestTarget():-1,valid);
        }
        public int NearestTarget()
        {return GetTarget(-1);}
        public int DisplayTarget()
        {return GetTarget(heldTarget);}
        private int GetTarget(int preferred)
        {
            if(room.State?.Tackle==null || !room.TryGetLocalPlayer(out var local))return -1;
            return SelectTarget(room.Map,room.State,local,preferred);
        }
        public static int SelectTarget(IslandMap map,HookState state,Fisher local,int preferred=-1)
        {
            if(preferred>=0 && preferred<state.Players.Count &&
                RevivalSystem.CanHelp(map,state.Tiles,local,state.Players[preferred]))return preferred;
            int target=-1;long best=long.MaxValue;
            foreach(var p in state.Players)
            {
                if(!RevivalSystem.CanHelp(map,state.Tiles,local,p))continue;
                long distance=IslandMap.DistanceSquared(local.X,local.Z,p.X,p.Z);
                if(distance<best){target=p.Slot;best=distance;}
            }
            return target;
        }
        public HookInput Compose(HookInput input)
        {
            if(requests.Pending)return new HookInput(0,0,revival:requests.Command,roomRules:input.RoomRules,
                aimVertical:input.AimVertical);
            if(!input.Grenade.IsEmpty || !input.Treasure.IsEmpty || !input.Revival.IsEmpty || heldTarget<0 || input.Inventory.Action!=InventoryAction.None ||
                input.RodAction!=RodAction.None || input.Build!=TileKind.None || input.Upgrade!=HookUpgrade.None)return input;
            return new HookInput(input.MoveX,input.MoveZ,input.AimX,input.AimZ,false,input.Cancel,shopping:input.Shopping,
                revival:new RevivalCommand(rescueSlot:heldTarget),roomRules:input.RoomRules,
                aimVertical:input.AimVertical);
        }
        public bool RequestRespawn()
        {
            if(!room.Playing || room.State==null || room.State.GameOver || !room.TryGetLocalPlayer(out var p) || !p.Vitals.Downed || !p.Revival.HasQuote ||
                p.Coins<p.Revival.DeathCost || room.Equipment?.Pending==true || room.Construction?.Pending==true || room.PurchasePending || room.Treasures?.Pending==true)return false;
            if(!requests.TryQueue(p.Revival))return false;
            room.CloseShop();room.Equipment?.Close();room.Construction?.Close();Message=null;return true;
        }
        public void ResetSession(){requests.Reset();press.Reset();Message=null;}
        public static string Explain(RespawnResult result)
        {
            switch(result)
            {
                case RespawnResult.Success:return "已复活，费用已结算";
                case RespawnResult.InsufficientCoins:return "金币不足，未扣费";
                case RespawnResult.NoLanding:return "出生区域暂时拥挤，请稍后重试；未扣费";
                case RespawnResult.StaleDeath:return "复活请求已过期，请重新选择";
                case RespawnResult.Unavailable:return "角色暂时离线，未扣费";
                case RespawnResult.GameOver:return "全队已死亡且无人能支付复活费用";
                default:return "当前无需付费复活";
            }
        }
    }
}
