using System.Linq;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Service UI owns intentions only. Receipts, unlocks and balances come from confirmed snapshots.</summary>
    public sealed class TownController
    {
        private readonly IslandFishingController room;
        private readonly TownRequestBuffer requests=new TownRequestBuffer();
        private int building;
        private string message;
        public bool Open {get;private set;}
        public bool Pending=>requests.Pending;
        public TownController(IslandFishingController room){this.room=room;}
        private bool Near(int x,int z,int range)=>room.TryGetLocalPlayer(out var p) && IslandMap.DistanceSquared(p.X,p.Z,x,z)<=(long)range*range;
        public int NearestBuilding()
        {foreach(var b in TownLayout.Buildings)if(Near(b.Entrance.X,b.Entrance.Z,3000))return b.Id;return 0;}
        public bool Request(TownAction action,int target=0)
        {
            if(!room.Playing || room.Incapacitated || !room.TryGetLocalPlayer(out var p) || room.State.Town.Players.Count<=p.Slot)return false;
            return requests.TryQueue(room.State.Town.Players[p.Slot].LastRequest,action,target,action==TownAction.RepairBand?room.State.Town.Attempt:0);
        }
        public bool TryInteract()
        {
            if(Open){Close();return true;}
            if(!room.Playing || room.Incapacitated)return false;
            building=NearestBuilding();
            if(building==0 && Near(TownLayout.BeaconX,TownLayout.BeaconZ,2500)){Request(TownAction.ReadyBoss);return true;}
            var power=TownLayout.EncounterAreas[2];
            if(building==0 && Near(power.X,power.Z,5000)){Request(TownAction.RestorePower);return true;}
            if(building==0 && room.State.Boss.Active)
                foreach(var segment in TownLayout.WoodSegments)
                    if(segment.SegmentId==3 && Near(segment.X,segment.Z,4000)){Request(TownAction.RepairBand,segment.BandId);return true;}
            if(building==0 && !room.State.Boss.Active && !room.State.Town.FinaleCompleted && Near(TownLayout.CenterX,TownLayout.CenterZ,26000)){Request(TownAction.ReadyBoss);return true;}
            if(building==0)return false;
            room.Grenades?.CancelAim();room.Equipment?.Close();room.CloseShop();Open=true;return true;
        }
        public HookInput Compose(HookInput input)=>requests.Compose(input);
        public void Synchronize(HookState state)
        {
            if(room.LocalSlot<state.Town.Players.Count && requests.Observe(state.Town.Players[room.LocalSlot],out var result))message=Explain(result);
            if(Open && (room.Incapacitated || NearestBuilding()!=building))Close();
        }
        public void Close()=>Open=false;
        public void Reset(){Close();requests.Reset();message=null;building=0;}
        public static string Explain(TownResult result)
        {
            switch(result)
            {
                case TownResult.Success:return "已确认 · 金币、服务与目标已同步";
                case TownResult.TooFar:return "离开服务范围，操作失败；靠近后重新点击";
                case TownResult.InsufficientCoins:return "金币不足，未扣款";
                case TownResult.GoalLocked:return "先清理对应区域 / 完成三个免费目标";
                case TownResult.Busy:return "请结束攻击、换弹或拉拽后重试";
                case TownResult.NoSpace:return "背包空间不足，未扣款";
                case TownResult.AlreadyCompleted:return "已经完成，无需再次支付";
                case TownResult.Unsafe:return "危险中不能维修，等待全部攻击和墨区结束";
                case TownResult.Cooldown:return "应急弹冷却中（90秒）";
                case TownResult.InvalidTarget:return "条件不符：检查弹药、物品或本次挑战编号";
                case TownResult.PartyNotReady:return "等待全队清醒并逐人就位";
                default:return "当前无法操作："+result;
            }
        }
        private static int Count(int mask){int n=0;while(mask!=0){n+=mask&1;mask>>=1;}return n;}
        private static void Panel(Rect r){var old=GUI.color;GUI.color=new Color(.018f,.07f,.09f,.94f);GUI.DrawTexture(r,Texture2D.whiteTexture);GUI.color=old;}
        public void Draw(float width,float height,GUIStyle body,GUIStyle small,GUIStyle button)
        {
            if(room.State==null || !room.TryGetLocalPlayer(out var p))return;
            bool finite=room.State.Tackle.Config.FiniteAmmunition;
            var s=room.State.Town;var personal=s.Players[p.Slot];
            if(s.Finished){Panel(new Rect(width/2-220,height/2-70,440,140));GUI.Label(new Rect(width/2-200,height/2-45,400,100),"沉钟镇 · 全员归航\n深渊已平息，探索行动完成。",body);return;}
            Panel(new Rect(22,100,330,152));
            GUI.Label(new Rect(34,108,310,55),$"沉钟镇   C电源 {((s.Goals&1)!=0?"完成":"未完")} · E潮闸 {((s.Goals&2)!=0?"完成":"未完")}\nF档案 {((s.Goals&4)!=0?"完成":"未完")} · 就位 {Count(s.ReadyMask)}/{room.State.Players.Count(q=>!q.Unavailable && !q.WaitingToJoin)}",small);
            GUI.Label(new Rect(34,165,310,44),$"尝试 #{s.Attempt}"+(finite?$"  专用弹 {personal.BoundLoaded}+{personal.BoundReserve}\n应急弹 {personal.EmergencyReserve}":"")+$" · 委托 {((s.ContractMask&(1<<p.Slot))!=0?"已接":"未接")}",small);
            GUI.Label(new Rect(34,212,310,34),s.FinaleCompleted?"已击败大章鱼 · 回码头全员确认返航":"A→B→C→D→E→F · 礼拜堂祭火就位",small);
            if((s.MapKnownMask&(1<<p.Slot))!=0)
                GUI.Label(new Rect(24,258,340,75),"地图：西南A码头 → 西B巷道 → 北C电源\n→ 东北D集市 → 东E潮闸 → 东南F档案\n档案北桥通礼拜堂 / 中央G深渊石环",small);
            if(!Open)
            {
                string prompt=Pending?"正在等待服务确认…":message;
                int near=NearestBuilding();
                if(near>0)prompt="[E] "+TownLayout.Buildings[near-1].Name+" · 门口服务";
                else if(Near(TownLayout.EncounterAreas[2].X,TownLayout.EncounterAreas[2].Z,5000))prompt="[E] 恢复电源 · C清场后免费";
                else if(Near(TownLayout.BeaconX,TownLayout.BeaconZ,2500))prompt="[E] 祭火就位 · 三目标完成后开战";
                if(!room.State.Boss.Active && !s.FinaleCompleted && Near(TownLayout.CenterX,TownLayout.CenterZ,26000))
                {prompt="[E] 确认就位 · 全员26米内，一人在祭火旁";}
                foreach(var segment in TownLayout.WoodSegments)
                    if(segment.SegmentId==3 && room.State.Boss.Active && Near(segment.X,segment.Z,4000))
                    {prompt=$"[E] 修复木带 {segment.BandId+1} · 40金币 · 等待安全间隙";break;}
                if(prompt!=null){Panel(new Rect(width/2-255,height-201,510,44));GUI.Label(new Rect(width/2-245,height-197,490,38),prompt,small);}return;
            }
            float x=width/2-280,y=Mathf.Max(80,height/2-260);Panel(new Rect(x,y,560,520));
            GUI.Label(new Rect(x+20,y+15,400,30),TownLayout.Buildings[building-1].Name+$" · {p.Coins} 金币",body);
            if(GUI.Button(new Rect(x+445,y+12,95,30),"关闭 E",button))Close();
            float row=y+58;
            void Text(string text,float h=48){GUI.Label(new Rect(x+20,row,520,h),text,small);row+=h;}
            void Buy(string text,TownAction action,int target=0){GUI.enabled=!Pending;if(GUI.Button(new Rect(x+20,row,520,34),text,button))Request(action,target);GUI.enabled=true;row+=40;}
            switch(building)
            {
                case 1:Text("击败大章鱼后，全体在线清醒成员在码头逐人确认，才结束本局。");Buy("确认返航 · 免费",TownAction.ReturnToHarbor);break;
                case 2:
                    Buy("AK47 · 900金币",TownAction.BuyRifle);
                    if(finite)
                    {
                        for(int type=0;type<AmmunitionCatalog.All.Count;type++)
                        {
                            var ammo=AmmunitionCatalog.Get(type);
                            Buy($"{ammo.Name} {ammo.Quantity}发 · {ammo.Price}金币",TownAction.BuyAmmo,type);
                        }
                        Buy("应急手枪弹 7发 · 免费 / 90秒",TownAction.EmergencyAmmo);
                        Text("应急条件：金币不足30且所有可用弹药耗尽。");
                    }
                    else Text("所有枪械均有无限备用弹匣，按 R 换弹。");
                    break;
                case 3:Text("恢复生命并补满医疗次数。");Buy("诊疗 · 40金币",TownAction.Heal);break;
                case 4:Buy("泳圈 · 150金币",TownAction.BuyRing);break;
                case 5:Buy("手榴弹 · 40金币",TownAction.BuyGrenade);break;
                case 6:Text("清理B区奖励90金币，清理D区奖励120金币。每人接受，确认清场后自动结算。");Buy("接受两项清场委托 · 免费",TownAction.Contract);break;
                case 7:Text("非保底装备半价出售；初始背包、近战与基础手枪不可出售。",40);
                    foreach(var item in room.State.Tackle.Inventory.Items.Where(i=>i.Owner==p.Slot && i.Id>room.State.Players.Count*3 && i.Definition.Price>=2 && i.Kind!=ItemKind.Ammunition && i.Kind!=ItemKind.Food && i.Kind!=ItemKind.Backpack && !InventoryCatalog.IsMaterial(i.Kind)).Take(8))Buy($"出售 {item.Definition.Name} · {item.Definition.Price/2}金币",TownAction.Sell,item.Id);break;
                case 8:Text("标记A–F路线、免费目标与G深渊。沿石路探索，木桥只在解锁后出现。");Buy("揭示地图 · 免费",TownAction.RevealMap);break;
                case 9:Buy("打开捷径 S1 · 60金币",TownAction.ShortcutOne);Buy("打开捷径 S2 · 80金币",TownAction.ShortcutTwo);Buy("开启潮闸 · E清场后免费",TownAction.OpenTideGate);break;
                case 10:
                    Buy("登记驿站复活点 · 免费",TownAction.RegisterInn);
                    if(finite){Buy("应急手枪弹 7发 · 免费 / 90秒",TownAction.EmergencyAmmo);Text("登记后可领取应急弹。");}
                    break;
                case 11:Text("潮务档案：电源、潮闸与档案封印共同约束深渊。旧桥通往礼拜堂，完成三处任务后，全员进入石环26米内逐人就位，一人靠近祭火。",100);Buy("读取封印档案 · F清场后免费",TownAction.ReadArchive);break;
                case 12:Text("沉钟碑文：红色砸击会摧毁木带；黄色横扫沿内环经过。永久外石环可躲避，南侧石尖台可射击与救援。墨区持续侵蚀，攻击主眼与触手。",100);Text((finite?"每次开战获得场内专用手枪弹。":"")+"全灭后木带复位，可重新集结。胜利后回码头返航。",100);Buy("阅读碑文 · 免费",TownAction.ReadClues);break;
            }
            GUI.Label(new Rect(x+20,y+470,520,40),Pending?"等待确认；同一请求不会重复扣款":message??"服务读取确认态；失败后需重新点击。",small);
        }
    }
}
