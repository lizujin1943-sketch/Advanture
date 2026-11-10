using System;
using System.Diagnostics;
using System.Linq;
using Advanture.HookIsland.Simulation;
using FrameSync.Abstractions;

/// <summary>首次寻路的内存回归：避免在第一批敌人开始移动时制造大量临时对象和 GC 停顿。</summary>
internal static class TownNavigationTests
{
    public static int RunStartup()
    {
        try
        {
            // 由验证脚本在独立进程调用，防止先前用例的缓存掩盖“初始化没有准备寻路图”的回归。
            var map=IslandMap.CreateTown();var codec=new HookGame(false);var model=codec.Create();
            var participant=new Participant(0,Guid.Parse("00000000-0000-0000-0000-000000000001"));
            var session=new SessionDescriptor(Guid.Parse("b67c34c6-e06b-46c6-968e-b32bc86c5a22"),1,30,new[]{participant});
            var watch=Stopwatch.StartNew();
            model.Initialize(new SimulationInitialization(session,new ByteString(map.Encode())));
            double initializationMs=watch.Elapsed.TotalMilliseconds;
            var nav=new CrabNavigation(map);nav.Refresh(model.Capture().Tiles);
            var players=new[]{new Fisher{Slot=0,X=-46000,Z=13000,Vitals=new CharacterVitals{Health=100}}};
            long before=GC.GetAllocatedBytesForCurrentThread();watch.Restart();
            int target=nav.Target(players,-56000,13000,out var goal);
            watch.Stop();long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
            Console.WriteLine($"Town initialization: {initializationMs:F3} ms; first enemy query: {watch.Elapsed.TotalMilliseconds:F3} ms / {allocated} bytes");
            if(target!=0 || goal.X!=-56000 || goal.Z!=12000)throw new Exception("初始化后的寻路结果发生变化。");
            if(allocated>2L*1024*1024)throw new Exception("第一批敌人仍在游戏过程中创建整张寻路图，而非复用初始化结果。");
            Console.WriteLine("PASS town_prepares_navigation_before_first_step");return 0;
        }
        catch(Exception e){Console.WriteLine("FAIL town_prepares_navigation_before_first_step: "+e.Message);return 1;}
    }
    public static int Run()
    {
        try
        {
            // 必须在本进程其他寻路用例之前执行，确保没有命中静态图缓存。
            var map=IslandMap.CreateTown();
            var tiles=TownLayout.InitialTiles();
            var nav=new CrabNavigation(map);
            nav.Refresh(tiles);
            var players=new[]{new Fisher{Slot=0,X=-46000,Z=13000,Vitals=new CharacterVitals{Health=100}}};
            long before=GC.GetAllocatedBytesForCurrentThread();
            var clock=Stopwatch.StartNew();
            int target=nav.Target(players,-56000,13000,out var goal);
            clock.Stop();
            long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
            Console.WriteLine($"Town cold navigation: {clock.Elapsed.TotalMilliseconds:F3} ms, {allocated/1048576.0:F3} MiB allocated");
            if(target!=0 || goal.X!=-56000 || goal.Z!=12000)
                throw new Exception("绕过沉船掩体时，目标或第一步方向发生改变。");
            // 用分配量作为稳定门槛；耗时单独报告，避免共享 CI 机器的负载造成偶发失败。
            // 修复前同一查询分配约 1 GiB；图及临时查询缓存应控制在 32 MiB 内。
            if(allocated>32L*1024*1024)
                throw new Exception("首次寻路分配超过 32 MiB，会在游戏主线程造成集中垃圾回收。");
            Console.WriteLine("PASS town_cold_navigation_allocation_budget");
            CheckFixedGroundIndex();
            CheckTerrainSampling();
            return 0;
        }
        catch(Exception e){Console.WriteLine("FAIL town_cold_navigation_allocation_budget: "+e.Message);return 1;}
    }

    private static void CheckFixedGroundIndex()
    {
        var index=new TownGroundIndex();
        // 参考查询不经过分区，能发现边界索引错误、遗漏候选街道及道路端帽被裁掉的问题。
        bool Reference(int x,int z)
        {
            if(!TownLayout.InBounds(x,z))return false;
            foreach(var a in TownLayout.EncounterAreas)if(a.Contains(x,z))return true;
            foreach(var b in TownLayout.Buildings)
                if(Math.Abs((long)x-b.X)<=b.Width/2+1500 && Math.Abs((long)z-b.Z)<=b.Depth/2+1500)return true;
            foreach(var road in TownLayout.Roads)
                if(TownLayout.DistanceToSegmentSquared(x,z,road.Start,road.End)<=(long)road.HalfWidth*road.HalfWidth)return true;
            return false;
        }
        void Check(int x,int z)
        {if(index.Contains(x,z)!=Reference(x,z))throw new Exception($"固定地面索引与全量几何查询不同：{x},{z}");}
        for(int z=-64000;z<=64000;z+=200)for(int x=-80000;x<=80000;x+=200)Check(x,z);
        // 分区边界两侧的毫米坐标，以及道路端点附近；不能只用刚好对齐的网格点验证。
        foreach(var road in TownLayout.Roads)foreach(var p in new[]{road.Start,road.End})
            foreach(int offset in new[]{-road.HalfWidth-1,-road.HalfWidth,road.HalfWidth,road.HalfWidth+1})
            {Check(p.X+offset,p.Z);Check(p.X,p.Z+offset);}
        for(int z=-64000;z<=64000;z+=8000)for(int x=-80000;x<=80000;x+=8000)
            for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++)Check(x+dx,z+dz);
        Console.WriteLine("PASS town_ground_index_matches_full_geometry");
    }

    private static void CheckTerrainSampling()
    {
        var full=TownLayout.InitialTiles();
        var variants=new[]{full,Array.Empty<PlatformTile>(),full.Skip(1).ToArray(),
            full.Concat(TownLayout.ShortcutTiles(0)).Concat(TownLayout.ShortcutTiles(1)).ToArray(),
            full.Concat(TownLayout.ReturnBridgeTiles()).ToArray()};
        foreach(var tiles in variants)
        {
            var terrain=new TownNavigationTerrain(tiles);
            void Check(int x,int z)
            {
                bool expected=TownLayout.CanStand(tiles,x,z,CrabConfig.BodyRadius);
                if(terrain.CanStand(x,z)!=expected || terrain.CanStand(x,z)!=expected)
                    throw new Exception($"寻路缓存改变身体覆盖结果：tiles={tiles.Length}, {x},{z}");
            }
            for(int z=-64000;z<=64000;z+=1000)for(int x=-80000;x<=80000;x+=1000)Check(x,z);
            foreach(var wood in TownLayout.WoodSegments)
                for(int dz=-1200;dz<=1200;dz+=100)for(int dx=-1200;dx<=1200;dx+=100)Check(wood.X+dx,wood.Z+dz);
            // 任意毫米坐标、负坐标和世界边界不能因缓存索引而被取整或越界。
            foreach(var p in new[]{new Point(-80001,0),new Point(80001,0),new Point(0,-64001),new Point(0,64001),
                new Point(-79999,-63999),new Point(79999,63999),new Point(-55001,-47001),new Point(-56000,13000)})Check(p.X,p.Z);
        }
        Console.WriteLine("PASS town_navigation_sampling_preserves_destroyed_and_unlocked_ground");
    }
}
