#if UNITY_EDITOR || DEVELOPMENT_BUILD || HOOK_ISLAND_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland
{
    /// <summary>Explicit acceptance input only. Earns resources and uses ordinary movement, build and purchase commands.</summary>
    public sealed class MerchantSmokePilot
    {
        public const int PurchaseMerchantId=2;
        private readonly int[] visited=new int[4],returnCatches={-1,-1,-1,-1};
        private readonly ulong[,] arrival=new ulong[4,5];
        public int VisitedMask {get;private set;}
        public int ReturnedMask {get;private set;}
        public int PurchaseTier {get;private set;}
        public static IslandMap CreateMap()
        {
            var basis=IslandMap.CreatePlatform();
            return new IslandMap(basis.Shore,basis.Pier,basis.Spawns,new[]{new Point(-4200,-4200)},basis.Platform,
                new FishingConfig(merchants:MerchantConfig.Default));
        }
        public bool Complete(HookState state)
        {
            Observe(state);return VisitedMask==15 && PurchaseTier>=2 && ReturnedMask==(1<<state.Players.Count)-1;
        }
        private void Observe(HookState state)
        {
            if(state.Tackle==null)return;
            foreach(var p in state.Players)
            {
                for(int id=1;id<=4;id++)
                {
                    if(!MerchantRules.CanInteract(p) || !MerchantRules.Near(state.Tackle.Config,state.Tiles,p,id))continue;
                    if(arrival[p.Slot,id]==0)arrival[p.Slot,id]=state.Steps;
                    if(state.Steps>=arrival[p.Slot,id]+60)visited[p.Slot]|=1<<(id-1);
                }
                var rod=FishingRules.Equipped(state,p);if(rod!=null)PurchaseTier=Math.Max(PurchaseTier,rod.Tier);
            }
            VisitedMask=15;foreach(var p in state.Players)VisitedMask&=visited[p.Slot];
            if(VisitedMask!=15)return;
            foreach(var p in state.Players)
            {
                if(returnCatches[p.Slot]<0)returnCatches[p.Slot]=p.Catches;
                if(p.Catches>returnCatches[p.Slot] && Math.Abs(p.X)<2800 && Math.Abs(p.Z)<2800)ReturnedMask|=1<<p.Slot;
            }
        }
        private static int MaterialGoal(int slot,int players)=>MerchantConfig.Default.Definitions
            .Where(d=>(d.Id-1)%players==slot).Sum(d=>(Math.Abs(d.CellX)+Math.Abs(d.CellZ)-2)*4);
        public HookInput Sample(IslandMap map,HookState state,int slot,ulong tick)
        {
            if(Complete(state) || state.Tackle==null || map.Fishing==null)return default;
            var p=state.Players[slot];if(p.Phase!=HookPhase.Ready || p.Cooldown>0)return default;
            int delay=(int)Math.Min(30UL,tick>state.Steps?tick-state.Steps-1:0);
            bool Need(Fisher player)=>player.FishbonesEarned<MaterialGoal(player.Slot,state.Players.Count) || player.Slot<2 && player.CoinsEarned<600;
            if(state.Players.Any(Need))return Need(p)?Fish(map,state,p,delay):default;
            foreach(var d in map.Fishing.Merchants.Definitions.Where(d=>(d.Id-1)%state.Players.Count==slot))
            {
                int dx=Math.Sign(d.CellX),dz=Math.Sign(d.CellZ),length=Math.Abs(d.CellX)+Math.Abs(d.CellZ);
                for(int n=2;n<length;n++)
                    if(!PlatformRules.Contains(state.Tiles,dx*n,dz*n))
                        return new HookInput(0,0,build:TileKind.Fishbone,buildX:(short)(dx*n),buildZ:(short)(dz*n),buildId:p.LastBuildId+1);
            }
            if(state.Tiles.Count<33)return default;
            int target=1;
            while(target<=4 && (VisitedMask&(1<<(target-1)))!=0 && (target!=PurchaseMerchantId || state.Players.All(f=>f.LastInventoryRequest>=1)))target++;
            if(target>4)return (ReturnedMask&(1<<slot))==0?Fish(map,state,p,delay):default;
            var def=map.Fishing.Merchants.Definitions[target-1];
            var destination=new Point((def.CellX-Math.Sign(def.CellX))*2000,(def.CellZ-Math.Sign(def.CellZ))*2000);
            var movement=Navigate(state,p,destination,delay);
            if(movement.MoveX!=0 || movement.MoveZ!=0)return movement;
            if(target==PurchaseMerchantId && p.LastInventoryRequest==0 && state.Players.All(f=>MerchantRules.Near(map,state.Tiles,f,PurchaseMerchantId)))
            {
                var shelf=state.Tackle.Merchants[PurchaseMerchantId];if(!shelf.Active)return default;
                var offers=Enumerable.Range(0,shelf.Stock.Count).Where(i=>shelf.Stock[i].Kind==ItemKind.Rod).ToArray();
                int offer=offers.First(i=>RodCatalog.Get(shelf.Stock[i].Type).Tier==2);
                if(slot>=2 && !shelf.Stock[offer].Sold)return new HookInput(0,0,shopping:true);
                return new HookInput(0,0,shopping:true,inventory:new InventoryCommand(InventoryAction.BuyAndEquip,offer,batch:shelf.Batch,requestId:1,merchantId:PurchaseMerchantId));
            }
            return new HookInput(0,0,shopping:true);
        }
        private static HookInput Fish(IslandMap map,HookState state,Fisher p,int delay)
        {
            if(state.Tackle==null)return default;
            var stand=new Point(-2600+p.Slot*300,-1700-p.Slot*300);
            var move=Navigate(state,p,stand,delay);if(move.MoveX!=0 || move.MoveZ!=0)return move;
            int speed=FishingRules.Speed(state,p),range=FishingRules.Range(map,state,p);if(speed==0 || range==0)return default;
            var fish=state.Fish[0];var home=map.FishSpots[0];
            if(fish.Owner>=0)
            {
                var lead=state.Players[fish.Owner];int length=HookGame.Sqrt(IslandMap.DistanceSquared(lead.X,lead.Z,fish.X,fish.Z));
                int frames=HookGame.Sqrt(IslandMap.DistanceSquared(p.X,p.Z,fish.X,fish.Z))*30/speed+delay;
                int reel=CooperativeFishing.Speed(state.Tackle.Config.ReelSpeed,CooperativeFishing.Count(state.Players,lead),state.Tackle.Config.CooperativeSpeedBonusPermille);
                int travel=Math.Min(length,frames*reel/30);
                return new HookInput(0,0,fish.X+(length==0?0:(lead.X-fish.X)*travel/length),fish.Z+(length==0?0:(lead.Z-fish.Z)*travel/length),true);
            }
            if(p.Stamina<FishingRules.MaxStamina(state,p)*9/10)return default;
            var future=FishMotion.Predict(fish,home,delay);
            for(int frames=1;frames<=Math.Min(120,range*30/speed+1);frames++)
            {
                FishMotion.Advance(ref future,home);if(future.Phase!=FishPhase.Jumping || future.Y<450)continue;
                int distance=HookGame.Sqrt(IslandMap.DistanceSquared(p.X,p.Z,future.X,future.Z));
                if(distance<=range && Math.Abs(distance-frames*speed/30)<speed/60+40)return new HookInput(0,0,future.X,future.Z,true);
            }
            return default;
        }
        private static HookInput Navigate(HookState state,Fisher p,Point target,int delay)
        {
            var start=(PlatformRules.CellAt(p.X),PlatformRules.CellAt(p.Z));var goal=(PlatformRules.CellAt(target.X),PlatformRules.CellAt(target.Z));
            if(start==goal)return Move(p,target,delay);
            var parents=new Dictionary<(int,int),(int,int)>();var queue=new Queue<(int,int)>();queue.Enqueue(start);parents[start]=start;
            while(queue.Count>0 && !parents.ContainsKey(goal))
            {
                var cell=queue.Dequeue();foreach(var next in new[]{(cell.Item1-1,cell.Item2),(cell.Item1+1,cell.Item2),(cell.Item1,cell.Item2-1),(cell.Item1,cell.Item2+1)})
                    if(PlatformRules.Contains(state.Tiles,next.Item1,next.Item2) && !parents.ContainsKey(next)){parents[next]=cell;queue.Enqueue(next);}
            }
            if(!parents.ContainsKey(goal))return default;
            var step=goal;while(parents[step]!=start)step=parents[step];
            if(step.Item1!=start.Item1 && Math.Abs(p.Z-start.Item2*2000)>80)return Move(p,new Point(p.X,start.Item2*2000),delay);
            if(step.Item2!=start.Item2 && Math.Abs(p.X-start.Item1*2000)>80)return Move(p,new Point(start.Item1*2000,p.Z),delay);
            return Move(p,new Point(step.Item1*2000,step.Item2*2000),delay);
        }
        private static HookInput Move(Fisher p,Point destination,int delay)
        {
            int dx=destination.X-p.X,dz=destination.Z-p.Z;
            if(Math.Abs(dx)>65)return new HookInput(Approach(dx,delay),0);
            if(Math.Abs(dz)>65)return new HookInput(0,Approach(dz,delay));
            return default;
        }
        private static short Approach(int delta,int delay)=>(short)Math.Max(-1000,Math.Min(1000,delta*10/(2*(delay+1))));
    }
}
#endif
