#if UNITY_EDITOR || DEVELOPMENT_BUILD || HOOK_ISLAND_TESTS
using System;
using System.Linq;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland
{
    /// <summary>仅供显式开发验收的输入生成器；通过真实捕鱼挣钱和获得材料，不注入资源或写权威状态。</summary>
    public sealed class InventorySmokePilot
    {
        private static readonly Point[] Stands={new Point(-1400,-2600),new Point(2600,-1400),new Point(1400,2600),new Point(-2600,1400)};
        private int groundTarget;
        private static uint Base(int slot)=>(uint)(slot==0?1:0);
        public bool Complete(HookState s)=>s.Tackle!=null && s.Tackle.Inventory.Size(0).Height==5 &&
            s.Players.All(p=>p.LastInventoryRequest>=Base(p.Slot)+4 && s.Tiles.Any(t=>t.Builder==p.Slot && t.Kind==TileKind.Fishbone));
        public HookInput Sample(IslandMap map,HookState s,int slot,ulong requestedTick)
        {
            var p=s.Players[slot];var inventory=s.Tackle!.Inventory;
            int lead=(int)Math.Min(30,requestedTick>s.Steps?requestedTick-s.Steps-1:0);
            var ground=inventory.Items.FirstOrDefault(i=>i.Kind==ItemKind.Fishbone && i.Location==ItemLocation.Ground);
            if(ground.Id!=0)groundTarget=ground.Id;
            bool collecting=s.Players.Any(v=>v.FishbonesEarned<8) || s.Players[0].CoinsEarned<200;
            if(collecting)
            {
                if(p.FishbonesEarned>=8 && (slot!=0 || p.CoinsEarned>=200))return default;
                var move=Move(p,Stands[slot],lead);if(move.MoveX!=0 || move.MoveZ!=0)return move;
                return Fish(map,s,p,lead);
            }
            if(p.Phase!=HookPhase.Ready || p.Cooldown>0)return default;
            var walking=Move(p,new Point(-600+slot*400,0),lead);if(walking.MoveX!=0 || walking.MoveZ!=0)return walking;
            if(inventory.Size(0).Height!=5)
            {
                if(slot!=0)return default;
                int offer=Enumerable.Range(0,s.Tackle.Stock.Count).First(i=>s.Tackle.Stock[i].Kind==ItemKind.Backpack && s.Tackle.Stock[i].Type==2 && !s.Tackle.Stock[i].Sold);
                return Command(new InventoryCommand(InventoryAction.BuyAndEquip,offer,batch:s.Tackle.Batch,requestId:1));
            }
            uint basis=Base(slot),phase=p.LastInventoryRequest-basis;
            var original=inventory.Items.Where(i=>i.Owner==slot && i.Kind==ItemKind.Fishbone && i.Location==ItemLocation.Backpack).OrderBy(i=>i.Id).FirstOrDefault();
            if(phase==0)return Command(new InventoryCommand(InventoryAction.Move,original.Id,x:3,y:3,requestId:basis+1));
            if(phase==1)return Command(new InventoryCommand(InventoryAction.Split,original.Id,amount:2,x:3,y:2,requestId:basis+2));
            if(phase==2)
            {
                var split=inventory.Items.FirstOrDefault(i=>i.Owner==slot && i.Kind==ItemKind.Fishbone && i.X==3 && i.Y==2);
                return Command(new InventoryCommand(InventoryAction.Drop,split.Id,amount:2,requestId:basis+3));
            }
            if(s.Players.Any(v=>v.LastInventoryRequest<Base(v.Slot)+3))return default;
            // 全队争抢同一个材料包。已被拿走时仍提交一次旧意图，让失败回执也走完网络链路。
            if(phase==3)return Command(new InventoryCommand(InventoryAction.Pickup,groundTarget==0?int.MaxValue:groundTarget,requestId:basis+4));
            if(s.Players.Any(v=>v.LastInventoryRequest<Base(v.Slot)+4))return default;
            if(s.Tiles.Any(t=>t.Builder==slot && t.Kind==TileKind.Fishbone))return default;
            foreach(var cell in Enumerable.Range(-2,5).SelectMany(z=>Enumerable.Range(-2,5).Select(x=>new Point(x,z))))
                if(PlatformRules.Check(map,s,p,TileKind.Fishbone,cell.X,cell.Z)==BuildResult.Success)
                    return new HookInput(0,0,build:TileKind.Fishbone,buildX:(short)cell.X,buildZ:(short)cell.Z,buildId:p.LastBuildId+1);
            return default;
        }
        private static HookInput Command(InventoryCommand command)=>new HookInput(0,0,inventory:command);
        private static HookInput Move(Fisher p,Point target,int lead)
        {
            int dx=target.X-p.X,dz=target.Z-p.Z;
            short Velocity(int delta)=>(short)Math.Max(-1000,Math.Min(1000,delta*10/(lead+3)));
            if(Math.Abs(dx)>60)return new HookInput(Velocity(dx),0);
            if(Math.Abs(dz)>60)return new HookInput(0,Velocity(dz));return default;
        }
        private static HookInput Fish(IslandMap map,HookState s,Fisher p,int lead)
        {
            if(p.Phase!=HookPhase.Ready || p.Cooldown>0)return default;
            int speed=FishingRules.Speed(s,p),range=FishingRules.Range(map,s,p);if(speed<=0)return default;
            foreach(var fish in s.Fish)
            {
                if(fish.Owner>=0)continue;var future=FishMotion.Predict(fish,map.FishSpots[fish.Id],lead);
                for(int ticks=1;ticks<=Math.Min(120,range*HookGame.Rate/speed+1);ticks++)
                {
                    FishMotion.Advance(ref future,map.FishSpots[fish.Id]);
                    if(future.Phase!=FishPhase.Jumping || future.Y<450)continue;
                    int d=HookGame.Sqrt(IslandMap.DistanceSquared(p.X,p.Z,future.X,future.Z));
                    if(d<=range && Math.Abs(d-ticks*speed/HookGame.Rate)<speed/HookGame.Rate/2+40 &&
                        (long)FishCatalog.DrainPerTick(fish)*(d*HookGame.Rate/s.Tackle!.Config.ReelSpeed+4)<p.Stamina)
                        return new HookInput(0,0,future.X,future.Z,true);
                }
            }
            return default;
        }
    }
}
#endif
