using System;
using System.Collections.Generic;

namespace Advanture.HookIsland.Simulation;

/// <summary>单位的平面占位快照；稳定 ID 和碰撞层由玩法适配器提供，固定单位只作为障碍。</summary>
public struct CombatBody
{
    public CombatBody(int id,int x,int z,int radius,int layers,bool fixedBody=false)
    {Id=id;X=x;Z=z;Radius=radius;Layers=layers;Fixed=fixedBody;}
    public readonly int Id,Radius,Layers;
    public readonly bool Fixed;
    public int X,Z;
}

/// <summary>
/// 毫米整数圆盘分离。空间哈希只产生附近候选，按稳定 ID 解算；不依赖物理引擎、随机数或字典遍历顺序。
/// 网格、候选和桶均为可重建缓存，复用容量但不参与存档或回滚状态。
/// </summary>
public sealed class CombatSpacing
{
    private const int CellSize=1000,Passes=12,MaxPush=400;
    private readonly Dictionary<(int,int),List<int>> grid=new Dictionary<(int,int),List<int>>();
    private readonly List<List<int>> buckets=new List<List<int>>();
    private readonly List<int> candidates=new List<int>();
    private int[] seen=Array.Empty<int>();
    private int usedBuckets,stamp;

    /// <summary>原地更新坐标并按 ID 排序；返回窄相位候选数，便于检测是否退化为全量两两检查。</summary>
    public int Resolve(List<CombatBody> bodies,Func<CombatBody,Point,Point,bool> canMove)
    {
        if(bodies==null)throw new ArgumentNullException(nameof(bodies));
        if(canMove==null)throw new ArgumentNullException(nameof(canMove));
        bodies.Sort((a,b)=>a.Id.CompareTo(b.Id));
        for(int i=0;i<bodies.Count;i++)
        {
            var b=bodies[i];
            if(b.Id<0 || b.Radius<1 || b.Radius>12000 || b.Layers<1 || b.Layers>3 ||
                Math.Abs((long)b.X)>100000 || Math.Abs((long)b.Z)>100000 ||
                i>0 && bodies[i-1].Id==b.Id)throw new ArgumentException("Invalid combat body.",nameof(bodies));
        }
        for(int i=0;i<usedBuckets;i++)buckets[i].Clear();
        usedBuckets=0;grid.Clear();
        if(seen.Length<bodies.Count)seen=new int[bodies.Count];
        for(int i=0;i<bodies.Count;i++)Insert(i,bodies[i]);
        int checks=0;
        for(int pass=0;pass<Passes;pass++)
        {
            bool changed=false;
            for(int i=0;i<bodies.Count;i++)
            {
                Query(i,bodies[i]);
                // 桶可因前面的单位移动而改变插入顺序，候选排序确保回放仍按 ID 处理。
                candidates.Sort();
                foreach(int j in candidates)
                {
                    var a=bodies[i];var b=bodies[j];
                    if((a.Layers&b.Layers)==0 || a.Fixed && b.Fixed)continue;
                    checks++;
                    int radius=a.Radius+b.Radius;long square=IslandMap.DistanceSquared(a.X,a.Z,b.X,b.Z);
                    if(square>=(long)radius*radius)continue;
                    int dx,dz;
                    if(square==0)
                    {
                        // 同心时没有几何法线。仅由稳定 ID 选择轴，禁止借用攻击朝向或运行时随机数。
                        uint axis=(unchecked((uint)a.Id*73856093u)^unchecked((uint)b.Id*19349663u))&3;
                        dx=axis==0?EnemyGeometry.DirectionScale:axis==2?-EnemyGeometry.DirectionScale:0;
                        dz=axis==1?EnemyGeometry.DirectionScale:axis==3?-EnemyGeometry.DirectionScale:0;
                    }
                    else EnemyGeometry.Direction(b.X-a.X,b.Z-a.Z,out dx,out dz);
                    int overlap=radius-HookGame.Sqrt(square)+2;
                    int first=b.Fixed?overlap:a.Fixed?0:(overlap+1)/2;
                    int second=a.Fixed?overlap:b.Fixed?0:overlap/2;
                    bool pairChanged=Push(i,-dx,-dz,first,bodies,canMove);
                    pairChanged|=Push(j,dx,dz,second,bodies,canMove);
                    // 一侧被岸边或墙挡住时，将剩余重叠交给另一侧，避免把被挡住的一侧挤出地形。
                    int remaining=Remaining(bodies[i],bodies[j]);
                    if(remaining>0)pairChanged|=Push(i,-dx,-dz,remaining,bodies,canMove);
                    remaining=Remaining(bodies[i],bodies[j]);
                    if(remaining>0)pairChanged|=Push(j,dx,dz,remaining,bodies,canMove);
                    remaining=Remaining(bodies[i],bodies[j]);
                    if(!pairChanged && remaining>0)
                    {
                        // 狭道两壁会同时挡住法线方向，但沿墙仍可能有空间。按稳定次序尝试切向和反切向，
                        // 两次仍走同一个整段地形检查；至少滑动 50 毫米，避免浅重叠在整数舍入后卡死。
                        int slide=Math.Max(50,a.Fixed || b.Fixed?remaining:(remaining+1)/2);
                        pairChanged=Push(i,dz,-dx,slide,bodies,canMove);
                        pairChanged|=Push(j,-dz,dx,slide,bodies,canMove);
                        if(!pairChanged)
                        {
                            pairChanged=Push(i,-dz,dx,slide,bodies,canMove);
                            pairChanged|=Push(j,dz,-dx,slide,bodies,canMove);
                        }
                    }
                    changed|=pairChanged;
                }
            }
            if(!changed)break;
        }
        return checks;
    }

    private static int Remaining(CombatBody a,CombatBody b)
    {
        int radius=a.Radius+b.Radius;long square=IslandMap.DistanceSquared(a.X,a.Z,b.X,b.Z);
        return square>=(long)radius*radius?0:radius-HookGame.Sqrt(square)+2;
    }
    private bool Push(int index,int dx,int dz,int amount,List<CombatBody> bodies,Func<CombatBody,Point,Point,bool> canMove)
    {
        var b=bodies[index];if(b.Fixed || amount<=0)return false;
        amount=Math.Min(MaxPush,amount);
        int x=(int)((long)dx*amount/EnemyGeometry.DirectionScale),z=(int)((long)dz*amount/EnemyGeometry.DirectionScale);
        if(x==0 && z==0)return false;
        var from=new Point(b.X,b.Z);var to=new Point(b.X+x,b.Z+z);
        if(!canMove(b,from,to))
        {
            // 回调检查整段路径，故二分的是连续可达前缀，不能越过薄墙再落到合法终点。
            int low=0,high=1024;
            while(low+1<high)
            {
                int mid=(low+high)/2;
                var p=new Point(b.X+(int)((long)x*mid/1024),b.Z+(int)((long)z*mid/1024));
                if(canMove(b,from,p))low=mid;else high=mid;
            }
            to=new Point(b.X+(int)((long)x*low/1024),b.Z+(int)((long)z*low/1024));
        }
        if(to.X==b.X && to.Z==b.Z)return false;
        Remove(index,b);b.X=to.X;b.Z=to.Z;bodies[index]=b;Insert(index,b);return true;
    }
    private void Query(int index,CombatBody b)
    {
        candidates.Clear();
        if(stamp==int.MaxValue){Array.Clear(seen,0,seen.Length);stamp=0;}
        stamp++;
        for(int z=Cell(b.Z-b.Radius);z<=Cell(b.Z+b.Radius);z++)
            for(int x=Cell(b.X-b.Radius);x<=Cell(b.X+b.Radius);x++)
                if(grid.TryGetValue((x,z),out var bucket))foreach(int candidate in bucket)
                    if(candidate>index && seen[candidate]!=stamp){seen[candidate]=stamp;candidates.Add(candidate);}
    }
    private void Insert(int index,CombatBody b)
    {
        for(int z=Cell(b.Z-b.Radius);z<=Cell(b.Z+b.Radius);z++)
            for(int x=Cell(b.X-b.Radius);x<=Cell(b.X+b.Radius);x++)
            {
                if(!grid.TryGetValue((x,z),out var bucket))
                {
                    if(usedBuckets==buckets.Count)buckets.Add(new List<int>());
                    bucket=buckets[usedBuckets++];grid.Add((x,z),bucket);
                }
                bucket.Add(index);
            }
    }
    private void Remove(int index,CombatBody b)
    {
        for(int z=Cell(b.Z-b.Radius);z<=Cell(b.Z+b.Radius);z++)
            for(int x=Cell(b.X-b.Radius);x<=Cell(b.X+b.Radius);x++)grid[(x,z)].Remove(index);
    }
    private static int Cell(int coordinate)=>coordinate>=0?coordinate/CellSize:(coordinate-CellSize+1)/CellSize;
}
