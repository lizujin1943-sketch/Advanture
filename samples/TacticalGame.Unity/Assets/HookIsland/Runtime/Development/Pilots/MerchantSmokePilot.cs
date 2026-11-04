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
        private static readonly Point[] fishingStands={new Point(-2600,-1000),new Point(-2300,-1800),new Point(-1700,-2400),new Point(-900,-2700)};
        private readonly int[] visited=new int[4],returnCatches={-1,-1,-1,-1};
        private readonly ulong[,] arrival=new ulong[4,5];
        private readonly Point[] navigationTargets=new Point[4],navigationWaypoints=new Point[4];
        private readonly bool[] hasWaypoint=new bool[4];
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
            // 单格桥无法穿过已经停好的后排：先让前两人靠船，后两人暂回起始岛等待。
            // 折返时后排先离开，前排自然跟随；不瞬移、不扩大交互范围，也不豁免单位碰撞。
            if(state.Players.Count==4 && slot>=2 && !state.Players.Take(2).All(f=>MerchantRules.Near(map,state.Tiles,f,target)))
                return Navigate(map,state,p,map.Spawns[slot],delay);
            // 四人分两排停在船前，全部处于交互范围内，不能继续要求同伴站到同一个点。
            int directionX=Math.Sign(def.CellX),directionZ=Math.Sign(def.CellZ);
            int distance=state.Players.Count<=2?2000:1500+slot/2*900;
            int lateral=state.Players.Count==1?0:(slot%2==0?-400:400);
            var destination=new Point(def.X-directionX*distance+(directionX==0?lateral:0),def.Z-directionZ*distance+(directionZ==0?lateral:0));
            var movement=Navigate(map,state,p,destination,delay);
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
        private HookInput Fish(IslandMap map,HookState state,Fisher p,int delay)
        {
            if(state.Tackle==null)return default;
            // 沿西南岸错开钓位，保持大于 660 毫米的角色直径，钩路也不会穿过同伴。
            var move=Navigate(map,state,p,fishingStands[p.Slot],delay);if(move.MoveX!=0 || move.MoveZ!=0)return move;
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
        private HookInput Navigate(IslandMap map,HookState state,Fisher p,Point target,int delay)
        {
            // 绕行可能先跨过相邻地格；在中间点到达前保持它，避免 CellAt 改变后立刻反向规划。
            var previous=navigationTargets[p.Slot];var waypoint=navigationWaypoints[p.Slot];
            if(hasWaypoint[p.Slot] && previous.X==target.X && previous.Z==target.Z &&
                (Math.Abs(p.X-waypoint.X)>70 || Math.Abs(p.Z-waypoint.Z)>70))return Move(map,state,p,waypoint,delay);
            hasWaypoint[p.Slot]=false;navigationTargets[p.Slot]=target;
            // 一段直线路面能走通时，直接导航到最终站位并绕过同伴。
            // 不把地格中心当成必须到达的终点，否则中心附近正在通行的人会让两边互相等待。
            int samples=Math.Max(1,(Math.Max(Math.Abs(target.X-p.X),Math.Abs(target.Z-p.Z))+49)/50);
            bool direct=true;
            for(int n=1;n<=samples;n++)
            {
                int probeX=p.X+(int)((long)(target.X-p.X)*n/samples),probeZ=p.Z+(int)((long)(target.Z-p.Z)*n/samples);
                if(!SwimmingTerrain.Ground(map,state.Tiles,probeX,probeZ) || SwimmingTerrain.Obstacle(map,probeX,probeZ)){direct=false;break;}
            }
            if(direct)return Move(map,state,p,target,delay);
            var start=(PlatformRules.CellAt(p.X),PlatformRules.CellAt(p.Z));var goal=(PlatformRules.CellAt(target.X),PlatformRules.CellAt(target.Z));
            if(start==goal)return Move(map,state,p,target,delay);
            var parents=new Dictionary<(int,int),(int,int)>();var queue=new Queue<(int,int)>();queue.Enqueue(start);parents[start]=start;
            while(queue.Count>0 && !parents.ContainsKey(goal))
            {
                var cell=queue.Dequeue();foreach(var next in new[]{(cell.Item1-1,cell.Item2),(cell.Item1+1,cell.Item2),(cell.Item1,cell.Item2-1),(cell.Item1,cell.Item2+1)})
                    if(PlatformRules.Contains(state.Tiles,next.Item1,next.Item2) && !parents.ContainsKey(next)){parents[next]=cell;queue.Enqueue(next);}
            }
            if(!parents.ContainsKey(goal))return default;
            var step=goal;while(parents[step]!=start)step=parents[step];
            // 最后一个地格直接走向各自站位；该格中心可能已被先到的同伴占据。
            if(step==goal)return Move(map,state,p,target,delay);
            // 往返都保持同一全局侧道，避免在窄桥上先对齐同一个中心，或折返时互换侧道。
            int lane=state.Players.Count==1?0:(p.Slot%2==0?-400:400);
            // 局部绕行可以暂时离开侧道，不能下一帧又把人拉回去，否则会来回震荡。
            waypoint=step.Item1!=start.Item1?new Point(step.Item1*2000,start.Item2*2000+lane):new Point(start.Item1*2000+lane,step.Item2*2000);
            navigationWaypoints[p.Slot]=waypoint;hasWaypoint[p.Slot]=true;
            return Move(map,state,p,waypoint,delay);
        }
        private static HookInput Move(IslandMap map,HookState state,Fisher p,Point destination,int delay)
        {
            // 地格 BFS 负责绕过海面，局部导航负责避让其他玩家；只产生正式移动输入。
            SmokeNavigation.Toward(map,state,p.Slot,destination,delay,out var move);return move;
        }
    }
}
#endif
