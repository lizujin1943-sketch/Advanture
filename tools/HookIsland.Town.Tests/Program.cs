using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;

internal static partial class Program
{
    private static void Require(bool value,string message){if(!value)throw new Exception(message);}
    private static IslandMap Town()=>IslandMap.CreateTown();
    private static HashSet<(int,int)>? reachable;
    private static bool Reachable(IslandMap map,Point from,Point to)
    {
        const int step=500;var tiles=Array.Empty<PlatformTile>();
        if(reachable==null)
        {
            reachable=new HashSet<(int,int)>();var checkedPoints=new HashSet<(int,int)>();var queue=new Queue<Point>();queue.Enqueue(from);reachable.Add((from.X,from.Z));
            var dirs=new[]{new Point(step,0),new Point(-step,0),new Point(0,step),new Point(0,-step)};
            while(queue.Count>0)
            {
                var p=queue.Dequeue();foreach(var d in dirs)
                {
                    int x=p.X+d.X,z=p.Z+d.Z;
                    if(!checkedPoints.Add((x,z)) || !SwimmingTerrain.Ground(map,tiles,x,z))continue;
                    reachable.Add((x,z));queue.Enqueue(new Point(x,z));
                }
            }
        }
        foreach(var p in reachable)if(IslandMap.DistanceSquared(p.Item1,p.Item2,to.X,to.Z)<=250000)return true;
        return false;
    }
    private static int Main(string[] args)
    {
        if(args.Contains("--navigation-startup-only"))return TownNavigationTests.RunStartup();
        if(args.Contains("--navigation-only"))return TownNavigationTests.Run();
        if(args.Contains("--town-boundaries"))return TownGameplayTests.RunBoundaries();
        if(args.Contains("--boss-only"))return BossTownTests.Run();
        int navigationFailures=TownNavigationTests.Run();
        var cases=new (string,Action)[]{
            ("town_snapshot_restores_outer_docks",()=>{
                var map=Town();var copy=IslandMap.Decode(map.Encode());
                Require(copy.CanStand(59000,12000),"East cartography dock beyond old 50m bounds lost.");
                Require(copy.CanStand(-55000,-47000),"South starting dock lost.");
                Require(!copy.OnLand(-8000,-12000),"Boss central deep water filled.");
                Require(map.Encode().SequenceEqual(copy.Encode()),"Snapshot not canonical.");
            }),
            ("town_stone_route_survives_all_wood_destroyed",()=>{
                var map=Town();var tiles=Array.Empty<PlatformTile>();
                Require(SwimmingTerrain.Ground(map,tiles,-8000,9000),"North ring is not fixed stone.");
                Require(SwimmingTerrain.Ground(map,tiles,-8000,-33000),"South ring is not fixed stone.");
                Require(!SwimmingTerrain.Ground(map,tiles,-8000,-12000),"Center is not water.");
                Require(SwimmingTerrain.Ground(map,tiles,-8000,-22000),"Pistol stone platform missing.");
            }),
            ("town_bounds_and_obstacles_share_grenade_navigation_rules",()=>{
                var map=Town();var tiles=PlatformRules.InitialTiles(map);var navigation=new CrabNavigation(map);navigation.Refresh(tiles);
                Require(!SwimmingTerrain.Obstacle(map,59000,12000),"Town still clamps at legacy 50m.");
                Require(SwimmingTerrain.Obstacle(map,80001,0),"East map boundary is open.");
                Require(GrenadeRules.Solid(map,-60000,1000,-21000),"Armory building does not block shots.");
                Require(!navigation.CanStand(-60000,-21000),"Crab can walk through the armory.");
                Require(navigation.CanStand(59000,12000),"Navigation rejects east town.");
            }),
            ("town_fixed_team_stone_platforms_extend_past_circular_edge",()=>{
                var map=Town();Require(map.OnLand(8000,-28000),"Southeast assembly platform corner is missing.");
            }),
            ("town_all_services_and_ports_reachable_from_dock_without_wood",()=>{
                var map=Town();var nav=new CrabNavigation(map);nav.Refresh(Array.Empty<PlatformTile>());
                foreach(var b in TownLayout.Buildings)
                {
                    Require(SwimmingTerrain.Ground(map,Array.Empty<PlatformTile>(),b.Entrance.X,b.Entrance.Z),"Building entrance not standable: "+b.Id);
                    Require(Reachable(map,new Point(-55000,-47000),b.Entrance),"Unreachable service: "+b.Id);
                }
                foreach(var p in TownLayout.SpawnPorts)
                {
                    bool ground=nav.CanStand(p.X,p.Z);
                    Require(p.Water?!SwimmingTerrain.Ground(map,Array.Empty<PlatformTile>(),p.X,p.Z):ground,"Port wrong habitat: "+p.ZoneId+"/"+p.Index);
                    if(!p.Water)Require(Reachable(map,new Point(-55000,-47000),new Point(p.X,p.Z)),"Port disconnected: "+p.ZoneId+"/"+p.Index);
                }
                foreach(var p in TownLayout.ShootingPlatforms)Require(Reachable(map,new Point(-55000,-47000),p),"Stone pistol platform disconnected.");
            }),
            ("town_only_paid_or_return_bridge_tiles_create_optional_water_crossings",()=>{
                var map=Town();var wood=TownLayout.InitialTiles();
                Require(!SwimmingTerrain.Ground(map,wood,-31000,14000),"S1 is already free ground.");
                var first=wood.Concat(TownLayout.ShortcutTiles(0)).ToArray();
                Require(SwimmingTerrain.Ground(map,first,-31000,14000),"S1 paid terrain does not restore crossing.");
                Require(!SwimmingTerrain.Ground(map,wood,-2000,28000),"S2 is already free ground.");
                Require(SwimmingTerrain.Ground(map,wood.Concat(TownLayout.ShortcutTiles(1)).ToArray(),-2000,28000),"S2 paid terrain missing.");
                Require(!SwimmingTerrain.Ground(map,wood,-40000,-42000),"Return route is already free ground.");
                Require(SwimmingTerrain.Ground(map,wood.Concat(TownLayout.ReturnBridgeTiles()).ToArray(),-40000,-42000),"Return bridge missing.");
                Require(!SwimmingTerrain.Ground(map,new[]{new PlatformTile(30,30,TileKind.Wood)},60000,60000),"Arbitrary town building becomes ground.");
            }),
            ("town_four_wood_bands_remove_only_their_sixteen_segments",()=>{
                var map=Town();var initial=TownLayout.InitialTiles();
                Require(initial.Length==16 && initial.Select(t=>(t.X,t.Z)).Distinct().Count()==16,"Wood segment identities overlap.");
                foreach(var segment in TownLayout.WoodSegments)
                {
                    Require(TownLayout.OnGround(initial,segment.X,segment.Z),"Initial segment missing.");
                    var removed=initial.Where(t=>t.X!=segment.CellX || t.Z!=segment.CellZ).ToArray();
                    Require(!TownLayout.OnGround(removed,segment.X,segment.Z),"Destroyed segment retains hidden ground: "+segment.BandId+"/"+segment.SegmentId);
                }
                for(int degree=0;degree<360;degree++)
                {
                    int x=-8000+(int)Math.Round(Math.Cos(degree*Math.PI/180)*21000),z=-12000+(int)Math.Round(Math.Sin(degree*Math.PI/180)*21000);
                    Require(SwimmingTerrain.Ground(map,Array.Empty<PlatformTile>(),x,z),"Stone ring interrupted after destruction at "+degree);
                }
            }),
            ("town_navigation_routes_around_shared_cover",()=>{
                var map=Town();var nav=new CrabNavigation(map);nav.Refresh(TownLayout.InitialTiles());
                var player=new Fisher{Slot=0,X=-46000,Z=13000,Vitals=new CharacterVitals{Health=100}};
                Require(!nav.Segment(-56000,13000,player.X,player.Z),"Broken boat has no authoritative navigation footprint.");
                int target=nav.Target(new[]{player},-56000,13000,out var goal);
                Require(target==0,"Town static street graph could not reach target around cover.");
                Require(nav.Segment(-56000,13000,goal.X,goal.Z),"First path step clips cover or water.");
            }),
            ("town_each_district_has_hidden_distant_emergence_from_combat_floor",()=>{
                var map=Town();var nav=new CrabNavigation(map);nav.Refresh(TownLayout.InitialTiles());var positions=new[]{(-50000,-29000),(-56000,19000),(-25000,24000),(31000,9000),(46000,-19000),(20000,-38000)};
                for(int zone=0;zone<6;zone++)
                {
                    var p=positions[zone];
                    Require(TownLayout.SpawnPorts.Any(port=>port.ZoneId==zone && !port.Water && nav.CanStand(port.X,port.Z) && IslandMap.DistanceSquared(p.Item1,p.Item2,port.X,port.Z)>=8000L*8000 &&
                        !GrenadeRules.Visible(map,p.Item1,700,p.Item2,port.X,700,port.Z)),"All ports visible from district combat floor: "+zone);
                }
                var water=TownLayout.SpawnPorts.Single(p=>p.Water);
                Require(!GrenadeRules.Solid(map,water.X,600,water.Z,400) && !SwimmingTerrain.Ground(map,TownLayout.InitialTiles(),water.X,water.Z),"Drowned Bell water port is obstructed or dry.");
                Require(IslandMap.DistanceSquared(46000,-19000,water.X,water.Z)>=8000L*8000 && !GrenadeRules.Visible(map,46000,700,-19000,water.X,700,water.Z),"Drowned Bell has no hidden distant entry.");
            }),
            ("legacy_platform_snapshot_and_build_capacity_unchanged",()=>{
                var map=IslandMap.CreatePlatform();var copy=IslandMap.Decode(map.Encode());
                Require(copy.CanStand(0,0),"Legacy spawn broken.");
                Require(!copy.CanStand(59000,12000),"Town terrain leaks into legacy map.");
                Require(PlatformRules.InitialTiles(copy).Length==9,"Legacy initial tiles changed.");
            })};
        int passed=0;
        foreach(var (name,test) in cases){try{test();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){Console.WriteLine("FAIL "+name+": "+e.Message);}}
        Console.WriteLine($"{passed}/{cases.Length} geometry passed");
        int gameplayFailures=args.Contains("--geometry-only")?0:TownGameplayTests.Run(!args.Contains("--no-network"));
        int bossFailures=args.Contains("--geometry-only")?0:BossTownTests.Run();
        return passed==cases.Length && navigationFailures==0 && gameplayFailures==0 && bossFailures==0?0:1;
    }
}
