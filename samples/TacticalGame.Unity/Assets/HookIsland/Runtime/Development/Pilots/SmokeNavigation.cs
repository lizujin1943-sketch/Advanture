#if UNITY_EDITOR || DEVELOPMENT_BUILD || HOOK_ISLAND_TESTS
using System;
using System.Collections.Generic;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland
{
    /// <summary>开发验证的普通输入导航：绕过其他玩家，不向权威状态写坐标。</summary>
    public static class SmokeNavigation
    {
        private const int Clearance=EnemyGeometry.PlayerRadius*2;
        private static readonly Point[] ring={new Point(900,0),new Point(640,640),new Point(0,900),new Point(-640,640),
            new Point(-900,0),new Point(-640,-640),new Point(0,-900),new Point(640,-640)};

        public static bool Toward(IslandMap map,HookState state,int slot,Point target,int delay,out HookInput input)
        {
            input=default;var player=state.Players[slot];var from=new Point(player.X,player.Z);
            if(Math.Abs(target.X-from.X)<=70 && Math.Abs(target.Z-from.Z)<=70)return false;
            if(!Free(map,state,slot,target))
            {
                // 同伴也使用到达容差；名义目标可能稍被其占据，在同一个 70 毫米容差内寻找可站位置。
                long best=long.MaxValue;var goal=target;
                for(int z=-60;z<=60;z+=60)for(int x=-60;x<=60;x+=60)
                {
                    var point=new Point(target.X+x,target.Z+z);
                    if(!Free(map,state,slot,point))continue;
                    long distance=IslandMap.DistanceSquared(from.X,from.Z,point.X,point.Z);
                    if(distance>=best)continue;best=distance;goal=point;
                }
                target=goal;
            }
            var next=target;
            if(!Clear(map,state,slot,from,target))
            {
                // 只有直线被占位挡住时才建立小型可见图。每个玩家周围八个候选，最多 26 个节点。
                var points=new List<Point>{from,target};
                foreach(var other in state.Players)
                    if(other.Slot!=slot && !other.WaitingToJoin && !other.Revival.Dead)
                        foreach(var offset in ring)
                        {
                            var point=new Point(other.X+offset.X,other.Z+offset.Z);
                            if(SwimmingTerrain.Ground(map,state.Tiles,point.X,point.Z) && !SwimmingTerrain.Obstacle(map,point.X,point.Z))points.Add(point);
                        }
                var costs=new int[points.Count];var previous=new int[points.Count];var visited=new bool[points.Count];
                for(int i=0;i<costs.Length;i++){costs[i]=int.MaxValue;previous[i]=-1;}costs[0]=0;
                for(int step=0;step<points.Count;step++)
                {
                    int current=-1;
                    for(int i=0;i<points.Count;i++)if(!visited[i] && costs[i]!=int.MaxValue && (current<0 || costs[i]<costs[current]))current=i;
                    if(current<0 || current==1)break;visited[current]=true;
                    for(int i=1;i<points.Count;i++)
                    {
                        if(visited[i] || !Clear(map,state,slot,points[current],points[i]))continue;
                        int cost=costs[current]+HookGame.Sqrt(IslandMap.DistanceSquared(points[current].X,points[current].Z,points[i].X,points[i].Z));
                        // 对向行走者都优先从各自右侧绕行，避免完全对称时同时横移到同一侧。
                        if(current==0 && (long)(target.X-from.X)*(points[i].Z-from.Z)-(long)(target.Z-from.Z)*(points[i].X-from.X)>0)cost+=5;
                        if(cost>=costs[i])continue;costs[i]=cost;previous[i]=current;
                    }
                }
                if(previous[1]<0)return true;
                int first=1;while(previous[first]>0)first=previous[first];next=points[first];
            }
            int dx=(next.X-from.X)*5/(delay+1),dz=(next.Z-from.Z)*5/(delay+1);
            long square=(long)dx*dx+(long)dz*dz;
            if(square>1000000)
            {
                int length=HookGame.Sqrt(square);if((long)length*length<square)length++;
                dx=(int)((long)dx*1000/length);dz=(int)((long)dz*1000/length);
            }
            input=new HookInput((short)dx,(short)dz);return true;
        }

        private static bool Clear(IslandMap map,HookState state,int slot,Point from,Point to)
        {
            int steps=Math.Max(1,(Math.Max(Math.Abs(to.X-from.X),Math.Abs(to.Z-from.Z))+49)/50);
            for(int step=1;step<=steps;step++)
            {
                int x=from.X+(int)((long)(to.X-from.X)*step/steps),z=from.Z+(int)((long)(to.Z-from.Z)*step/steps);
                if(!SwimmingTerrain.Ground(map,state.Tiles,x,z) || SwimmingTerrain.Obstacle(map,x,z))return false;
                foreach(var other in state.Players)
                {
                    if(other.Slot==slot || other.WaitingToJoin || other.Revival.Dead)continue;
                    long distance=IslandMap.DistanceSquared(x,z,other.X,other.Z);
                    // 起点可能刚好接触另一玩家，允许离开接触，禁止继续挤向对方。
                    if(distance<(long)Clearance*Clearance && distance<IslandMap.DistanceSquared(from.X,from.Z,other.X,other.Z))return false;
                }
            }
            return true;
        }

        private static bool Free(IslandMap map,HookState state,int slot,Point point)
        {
            if(!SwimmingTerrain.Ground(map,state.Tiles,point.X,point.Z) || SwimmingTerrain.Obstacle(map,point.X,point.Z))return false;
            foreach(var other in state.Players)
                if(other.Slot!=slot && !other.WaitingToJoin && !other.Revival.Dead &&
                    IslandMap.DistanceSquared(point.X,point.Z,other.X,other.Z)<(long)Clearance*Clearance)return false;
            return true;
        }
    }
}
#endif
