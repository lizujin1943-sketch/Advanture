using System.Linq;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Local panel state and reliable command delivery. Does not own equipment or currency.</summary>
    public sealed class FishingEquipmentController : MonoBehaviour
    {
        private IslandFishingController room;
        private readonly RodRequestBuffer requests=new RodRequestBuffer();
        private readonly InventoryRequestBuffer inventoryRequests=new InventoryRequestBuffer();
        private string message;
        private float messageUntil;
        private int previousEscapes;
        public bool InventoryOpen { get; private set; }
        public bool Pending=>requests.Pending || inventoryRequests.Pending;
        public string Message=>Pending?"正在确认交易…":Time.unscaledTime<messageUntil?message:null;
        private void Awake(){room=GetComponent<IslandFishingController>();}
        private void Update()
        {
            if(!room.TryGetLocalPlayer(out var p))return;
            if(requests.Observe(p,out var result))Notify(Explain(result));
            if(inventoryRequests.Observe(p.LastInventoryRequest,p.LastInventoryResult,out var inventoryResult))Notify(Explain(inventoryResult));
            if(p.Escapes>previousEscapes)Notify("耐力耗尽，鱼挣脱了！休息片刻可恢复耐力。");
            previousEscapes=p.Escapes;
            if(!room.Playing || room.Incapacitated){InventoryOpen=false;return;}
            if(Application.isFocused && (Input.GetKeyDown(KeyCode.I) || Input.GetKeyDown(KeyCode.Tab)))ToggleInventory();
            if(Application.isFocused && !InventoryOpen && !room.ShopOpen && room.Construction?.Open!=true &&
                !GetComponent<FishingOverlay>().MenuOpen && Input.GetKeyDown(KeyCode.F))PickupNearest(p);
        }
        public void ToggleInventory()
        {
            if(!room.Playing || room.Incapacitated)return;
            InventoryOpen=!InventoryOpen;room.CloseShop();room.Construction?.Close();
            GetComponent<FishingOverlay>().MenuOpen=false;
        }
        public void Close(){InventoryOpen=false;}
        public bool Request(RodAction action,int target=0,uint batch=0)
        {
            if(!room.Playing || !room.TryGetLocalPlayer(out var p) || Pending || room.Construction?.Pending==true || room.Treasures?.Pending==true)return false;
            return requests.TryQueue(p.LastRodRequest,action,target,batch);
        }
        /// <summary>只排队意图；库存和金币由确认帧更新。挂起时不接收第二个点击。</summary>
        public bool RequestInventory(InventoryCommand intent)
        {
            if(!room.Playing || !room.TryGetLocalPlayer(out var player) || Pending || room.Construction?.Pending==true || room.PurchasePending || room.Treasures?.Pending==true)return false;
            return inventoryRequests.TryQueue(player.LastInventoryRequest,intent);
        }
        private void PickupNearest(Fisher player)
        {
            if(room.State?.Tackle==null || Pending)return;
            var state=room.State.Tackle.Inventory;
            var nearby=GroundPickupSelection.Nearby(state,player);
            if(nearby.Length==0){Notify("附近没有可拾取物品");return;}
            RequestInventory(GroundPickupSelection.Command(state,player,nearby[0]));
        }
        public HookInput Compose(HookInput input)
        {
            if(inventoryRequests.Pending)return new HookInput(input.MoveX,input.MoveZ,input.AimX,input.AimZ,
                shopping:input.Shopping,inventory:inventoryRequests.Command,roomRules:input.RoomRules);
            return requests.Compose(input);
        }
        public void ResetSession(){Close();GetComponent<FishingEquipmentOverlay>()?.ResetSession();requests.Reset();inventoryRequests.Reset();message=null;previousEscapes=0;}
        private void Notify(string text){message=text;messageUntil=Time.unscaledTime+5;}
        public static string Explain(InventoryResult result)
        {
            switch(result)
            {
                case InventoryResult.Success:return "操作已确认 · 背包已同步";
                case InventoryResult.Disabled:return "当前角色无法操作装备";
                case InventoryResult.NotOwned:return "只能操作自己背包中的物品";
                case InventoryResult.InvalidItem:return "物品已被取走或不支持这项操作";
                case InventoryResult.InvalidQuantity:return "请选择有效数量";
                case InventoryResult.InvalidPlacement:return "位置超出背包或不在有效平台上";
                case InventoryResult.NoSpace:return "空间不足，请移动、旋转或腾出格子";
                case InventoryResult.TooFar:return "请靠近商品或地面物品再操作";
                case InventoryResult.InsufficientCoins:return "自己的金币不足";
                case InventoryResult.InventoryNotEmpty:return "先清空背包，再将空包卸到平台上";
                case InventoryResult.HookBusy:return "请先收回钓竿并结束拉拽";
                case InventoryResult.StaleStock:return "商店已换货，请重新选择";
                case InventoryResult.SoldOut:return "商品已被队友买走";
                case InventoryResult.LimitReached:return "本局物品或编号达到上限，操作未提交";
                case InventoryResult.LayoutTooComplex:return "自动整理未找到布局，请先手动整理";
                default:return "当前无法操作";
            }
        }
        public static string Explain(RodResult result)
        {
            switch(result)
            {
                case RodResult.Success:return "已完成 · 装备与金币已同步";
                case RodResult.TooFar:return "请靠近平台商人购买";
                case RodResult.HookBusy:return "请先收回钓竿，结束拉拽后再操作";
                case RodResult.StaleStock:return "商人刚刚换货，请重新选择商品";
                case RodResult.SoldOut:return "这件商品已被好友买走";
                case RodResult.InsufficientCoins:return "自己的金币不足";
                case RodResult.NoSpace:return "背包放不下这支钓竿，请先调整装备";
                case RodResult.NotOwned:return "只能装备自己的钓竿";
                default:return "当前无法操作";
            }
        }
    }
}
