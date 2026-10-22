#if UNITY_EDITOR || DEVELOPMENT_BUILD || HOOK_ISLAND_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland
{
    // Automated acceptance input only. It never edits world state or grants resources.
    public static class PlatformSmokePilot
    {
        private static readonly Point[] FishingPositions={new Point(-1400,-2600),new Point(2600,-1400),new Point(1400,2600),new Point(-2600,1400)};
        private static readonly Point[] WoodCells={new Point(-1,-2),new Point(2,-1),new Point(1,2),new Point(-2,1)};
        public static bool Complete(HookState s)=>s.Tiles.Count==9+s.Players.Count*2 && s.Players.All(p=>
            s.Tiles.Any(t=>t.Builder==p.Slot && t.Kind==TileKind.Wood) &&
            s.Tiles.Any(t=>t.Builder==p.Slot && t.Kind==TileKind.Fishbone &&
                IslandMap.DistanceSquared(p.X,p.Z,t.X*2000,t.Z*2000)<150L*150));
        public static HookInput Sample(IslandMap map,HookState s,int slot,ulong requestedTick)
        {
            var p=s.Players[slot];int lead=(int)Math.Min(30,requestedTick>s.Steps?requestedTick-s.Steps-1:0);
            if(s.Players.Any(f=>f.WoodEarned<4 || f.FishbonesEarned<4 || f.Catches<2))
            {
                var move=Move(p,FishingPositions[slot],lead);if(move.MoveX!=0 || move.MoveZ!=0)return move;
                if(p.Phase!=HookPhase.Ready || p.Cooldown>0)return default;
                int range=FishingRules.Range(map,s,p),speed=FishingRules.Speed(s,p);
                if(speed<=0 || range<=0)return default;
                int flightTicks=(range*HookGame.Rate+speed-1)/speed+4;
                if(p.WoodEarned<4)
                {
                    var wood=s.Driftwood[slot];
                    for(int t=0;t<lead;t++)DriftwoodMotion.Advance(ref wood);
                    for(int t=1;t<=flightTicks;t++)
                    {
                        DriftwoodMotion.Advance(ref wood);if(wood.Phase!=DriftwoodPhase.Floating)continue;
                        int d=HookGame.Sqrt(IslandMap.DistanceSquared(p.X,p.Z,wood.X,wood.Z));
                        if(d<=range && Math.Abs(d-t*speed/HookGame.Rate)<180)
                            return new HookInput(0,0,wood.X,wood.Z,true);
                    }
                }
                else if(p.FishbonesEarned<4 || p.Catches<2)
                {
                    if(s.Tackle!=null && p.Stamina<FishingRules.MaxStamina(s,p)*3/4)return default;
                    int id=slot*6;var f=s.Fish[id];
                    for(int t=1;t<=flightTicks;t++)
                    {
                        var future=FishMotion.Predict(f,map.FishSpots[id],lead+t);
                        int d=HookGame.Sqrt(IslandMap.DistanceSquared(p.X,p.Z,future.X,future.Z));
                        if((s.Tackle==null || (long)FishCatalog.DrainPerTick(f)*(d*30/s.Tackle.Config.ReelSpeed+4)<p.Stamina) && future.Phase==FishPhase.Jumping && future.Y>=450 && d<=range && Math.Abs(d-t*speed/HookGame.Rate)<200)
                            return new HookInput(0,0,future.X,future.Z,true);
                    }
                }
                return default;
            }
            if(p.Phase!=HookPhase.Ready)return default;
            if(!PlatformRules.Contains(s.Tiles,-2,-1))return Build(TileKind.Wood,-2,-1,p.LastBuildId+1);
            var woodTile=s.Tiles.FirstOrDefault(t=>t.Builder==slot && t.Kind==TileKind.Wood);
            if(woodTile.Kind==TileKind.None)
            {var cell=WoodCells[slot];return Build(TileKind.Wood,cell.X,cell.Z,p.LastBuildId+1);}
            var bone=s.Tiles.FirstOrDefault(t=>t.Builder==slot && t.Kind==TileKind.Fishbone);
            if(bone.Kind==TileKind.None)
            {
                int x=woodTile.X,z=woodTile.Z;if(x< -1)x--;else if(x>1)x++;else if(z< -1)z--;else z++;
                return Build(TileKind.Fishbone,x,z,p.LastBuildId+1);
            }
            // Walk the actual connected route instead of cutting diagonally across unbuilt water.
            var start=(PlatformRules.CellAt(p.X),PlatformRules.CellAt(p.Z));var target=(bone.X,bone.Z);
            if(start==target)return Move(p,new Point(bone.X*2000,bone.Z*2000),lead);
            var from=new Dictionary<(int,int),(int,int)>();var queue=new Queue<(int,int)>();queue.Enqueue(start);from[start]=start;
            while(queue.Count>0 && !from.ContainsKey(target))
            {
                var c=queue.Dequeue();foreach(var next in new[]{(c.Item1-1,c.Item2),(c.Item1+1,c.Item2),(c.Item1,c.Item2-1),(c.Item1,c.Item2+1)})
                    if(PlatformRules.Contains(s.Tiles,next.Item1,next.Item2) && !from.ContainsKey(next)){from[next]=c;queue.Enqueue(next);}
            }
            if(!from.ContainsKey(target))return default;
            var step=target;while(from[step]!=start)step=from[step];
            // Align to the current cell's centre line before crossing the next edge.
            if(step.Item1!=start.Item1 && Math.Abs(p.Z-start.Item2*2000)>100)return Move(p,new Point(p.X,start.Item2*2000),lead);
            if(step.Item2!=start.Item2 && Math.Abs(p.X-start.Item1*2000)>100)return Move(p,new Point(start.Item1*2000,p.Z),lead);
            return Move(p,new Point(step.Item1*2000,step.Item2*2000),lead);
        }
        private static HookInput Build(TileKind kind,int x,int z,uint id)=>new HookInput(0,0,build:kind,buildX:(short)x,buildZ:(short)z,buildId:id);
        private static HookInput Move(Fisher p,Point target,int lead)
        {
            int dx=target.X-p.X,dz=target.Z-p.Z;
            // Commands remain in flight for several ticks. Ease toward a waypoint instead of
            // alternating full-speed commands across it while waiting for confirmed positions.
            if(Math.Abs(dx)>60)return new HookInput(Velocity(dx,lead),0);
            if(Math.Abs(dz)>60)return new HookInput(0,Velocity(dz,lead));
            return default;
        }
        private static short Velocity(int delta,int lead)=>(short)Math.Max(-1000,Math.Min(1000,delta*10/(lead+3)));
    }
}
#endif
