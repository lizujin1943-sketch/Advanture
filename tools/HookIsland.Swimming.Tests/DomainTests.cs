using System;
using System.Collections.Generic;
using System.IO;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.SwimmingTests;
internal static partial class Program
{
    private static IEnumerable<(string,Action)> DomainCases()
    {
        yield return ("domain_landing_checks_footprint_occupancy_and_path",()=>{
            var map=IslandMap.CreatePlatform();var tiles=PlatformRules.InitialTiles();
            var players=new[]{new Fisher{Slot=0,X=2700,Z=0},new Fisher{Slot=1,X=4200,Z=0}};
            Require(!SwimmingTerrain.Landing(map,tiles,players,1,2800,0),"Occupied deck accepted a swimmer.");
            Require(SwimmingTerrain.RescueLanding(map,tiles,players,players[0],players[1],out var landing),"No landing found beside edge-standing rescuer.");
            var at=SwimmingTerrain.Move(map,tiles,players,1,new Point(4200,0),landing,true,true,out bool water);
            Require(!water && SwimmingTerrain.Landing(map,tiles,players,1,at.X,at.Z),"Rescue endpoint is not valid ground.");
            var stay=SwimmingTerrain.Move(map,tiles,players,1,new Point(4200,0),new Point(3300,0),true,true,out water);
            Require(water && stay.X==3300,"Nearby water was counted as shore.");
            var legacy=new IslandMap(new[]{new Point(-8000,-8000),new Point(8000,-8000),new Point(8000,8000),new Point(-8000,8000)},
                map.Pier,map.Spawns,map.FishSpots);
            var blocked=SwimmingTerrain.Move(legacy,tiles,Array.Empty<Fisher>(),1,new Point(-4000,1050),new Point(4000,1050),false,false,out water);
            Require(blocked.X< -2000,"Path crossed a solid merchant building.");
        });
        yield return ("domain_ring_catalog_and_config_match_ten_levels",()=>{
            var c=new SwimmingConfig();Require(SwimRingCatalog.All.Count==10,"Expected ten ring levels.");
            int[] prices={150,260,450,750,1200,1800,2700,3900,5500,7600};
            for(int i=1;i<=10;i++)
            {
                var r=SwimRingCatalog.Get(i);Require(r.Level==i && r.Price==prices[i-1] && r.Tier==(i+1)/2,"Incorrect price/tier.");
                Require(SwimmingSystem.Speed(c,r)==1100+i*100 && SwimmingSystem.DrainPerTick(c,r)==9500-i*500,"Incorrect speed or drain.");
            }
            Throws<InvalidDataException>(()=>new SwimmingConfig(speed:10001));
            Throws<InvalidDataException>(()=>new SwimmingConfig(staminaMilliPerSecond:0));
            Throws<InvalidDataException>(()=>new SwimmingConfig(healthLossMilliPerSecond:-1));
            using var ms=new MemoryStream();using var w=new BinaryWriter(ms);new SwimmingConfig(750,1400,11250).Write(w);ms.Position=0;
            using var r2=new BinaryReader(ms);var round=SwimmingConfig.Read(r2);Require(round.HealthLossMilliPerSecond==750 && round.Speed==1400 && round.StaminaMilliPerSecond==11250,"Config roundtrip failed.");
        });
        yield return ("domain_damage_is_exact_ignores_defense_and_never_revives",()=>{
            var c=new SwimmingConfig();var water=new SwimmingState{InWater=true};var v=new CharacterVitals{Health=100,Hunger=100};
            for(int i=0;i<30;i++)SwimmingSystem.Damage(ref water,ref v,c,false);
            Require(v.Health==95 && water.DamageRemainder==0,"Damage rate or direct damage incorrect.");
            for(int i=0;i<90;i++)SwimmingSystem.Damage(ref water,ref v,c,true);
            Require(v.Health==95,"Equipped ring failed to stop water damage.");
            water.InWater=false;SwimmingSystem.Damage(ref water,ref v,c,false);Require(v.Health==95,"Land healed/damaged.");
            water.InWater=true;for(int i=0;i<600;i++)SwimmingSystem.Damage(ref water,ref v,c,false);
            Require(v.Downed && water.InWater,"Downed state cleared water state.");
        });
        yield return ("domain_shared_budget_uses_sum_and_last_partial_step",()=>{
            int stamina=5250;int f=SwimmingSystem.Spend(ref stamina,4500,6000);long remainder=0;
            Require(stamina==0 && f==500000,"Fish and swimming must share a single partial-frame budget.");
            Require(SwimmingSystem.Delta(1000,2100,f,ref remainder)==35,"Half stamina granted a full movement step.");
            f=SwimmingSystem.Spend(ref stamina,4500,6000);Require(f==0 && SwimmingSystem.Delta(1000,2100,f,ref remainder)==0,"Empty pool moved.");
            int x=0;remainder=0;for(int i=0;i<30;i++)x+=SwimmingSystem.Delta(-1000,1300,SwimmingSystem.FullFrame,ref remainder);
            Require(x==-1300 && remainder==0,"Fractional displacement is not exact over one second.");
            Require(SwimmingSystem.Active(true,false,false,1000,0) && !SwimmingSystem.Active(true,false,true,1000,0),"External pull did not override active swimming.");
            Require(!SwimmingSystem.CanCast(true,false,false) && SwimmingSystem.CanCast(true,false,true) && !SwimmingSystem.CanCast(true,true,true),"Cast permission incorrect.");
        });
        yield return ("domain_water_snapshot_preserves_fractional_damage_and_rejects_corruption",()=>{
            var s=new SwimmingState{InWater=true,DamageRemainder=12500,MoveRemainderX=-15400000000,MoveRemainderZ=19000};
            using var ms=new MemoryStream();using var w=new BinaryWriter(ms);SwimmingStateCodec.Write(w,s);ms.Position=0;
            using var r=new BinaryReader(ms);var t=SwimmingStateCodec.Read(r);
            Require(t.InWater && t.DamageRemainder==12500 && t.MoveRemainderX==s.MoveRemainderX && t.MoveRemainderZ==19000,"Lost residual state.");
            t.DamageRemainder=30000;Throws<InvalidDataException>(()=>SwimmingStateCodec.Validate(t));
            t.DamageRemainder=0;t.MoveRemainderX=long.MinValue;Throws<InvalidDataException>(()=>SwimmingStateCodec.Validate(t));
        });
    }
}
