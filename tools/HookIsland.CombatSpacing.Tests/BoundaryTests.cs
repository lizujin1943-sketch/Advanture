using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.CombatSpacingTests;

internal static partial class Program
{
    private static IEnumerable<(string,Action)> BoundaryCases()
    {
        yield return ("spacing_preserves_active_knockback_direction_and_restorable_origin",()=>{
            var model=Model();var s=model.Capture();var people=s.Players.ToArray();
            people[0].X=-2000;people[0].Z=-1400;people[1].X=-1650;people[1].Z=-1400;
            KnockbackSystem.Start(ref people[0],EnemyGeometry.DirectionScale,0,WeaponCatalog.All[0].FriendlyKnockback);
            model.Restore(Replace(s,people));
            for(int i=0;i<8;i++)
            {
                Step(model);var state=model.Capture();var p=state.Players[0];
                Require(p.Knockback.DirectionX==EnemyGeometry.DirectionScale && p.Knockback.DirectionZ==0,"Spacing changed hit direction");
                if(p.Knockback.Active)Require(p.X==p.Knockback.OriginX+p.Knockback.Distance && p.Z==p.Knockback.OriginZ,"Spacing broke knockback trajectory receipt");
                var encoded=Codec.Encode(state);Require(encoded.SequenceEqual(Codec.Encode(Codec.Decode(encoded))),"Spacing produced an unrestorable knockback state");
            }
        });
        yield return ("hole_boundary_is_swept_for_every_ground_unit",()=>{
            var model=Model();var s=model.Capture();var people=s.Players.ToArray();
            for(int i=0;i<people.Length;i++){people[i].X=2730;people[i].Z=0;}
            // 初始九格不可破坏；在扩建环中保留 (2,0) 的合法缺口。
            var ring=new[]{(2,-1),(3,-1),(3,0),(3,1),(2,1)}.Select(p=>new PlatformTile(p.Item1,p.Item2,TileKind.Wood,0,4));
            people[0].WoodEarned+=20; // 五块合法历史建造消耗；恢复时仍满足材料守恒。
            model.Restore(Replace(s,people,tiles:s.Tiles.Concat(ring)));
            for(int tick=0;tick<10;tick++)
            {
                Step(model);s=model.Capture();
                Require(s.Players.All(p=>!p.Swimming.InWater && PlatformRules.CanStand(s.Tiles,p.X,p.Z)),"Spacing crossed a destroyed platform tile");
            }
            Separate(s.Players.Select(p=>new Point(p.X,p.Z)).ToArray(),660);
        });
        yield return ("world_boundary_keeps_swimmers_inside_deterministic_limits",()=>{
            var model=Model();var s=model.Capture();var people=s.Players.ToArray();
            for(int i=0;i<people.Length;i++){people[i].X=49900;people[i].Z=-7000;people[i].Swimming.InWater=true;}
            model.Restore(Replace(s,people));
            for(int tick=0;tick<10;tick++)Step(model);s=model.Capture();
            Require(s.Players.All(p=>p.Swimming.InWater && Math.Abs((long)p.X)<=SwimmingTerrain.WorldLimit && Math.Abs((long)p.Z)<=SwimmingTerrain.WorldLimit),"Spacing crossed the world wall");
            Separate(s.Players.Select(p=>new Point(p.X,p.Z)).ToArray(),660);
        });
        yield return ("offshore_merchant_wall_cannot_be_crossed_by_crowd",()=>{
            var map=Map(merchants:new MerchantConfig(new[]{new MerchantDefinition(1,2,2,0)}));
            var model=Model(map);var s=model.Capture();var people=s.Players.ToArray();
            for(int i=0;i<people.Length;i++){people[i].X=2990;people[i].Z=0;people[i].Swimming.InWater=true;}
            model.Restore(Replace(s,people));
            for(int tick=0;tick<10;tick++)
            {
                Step(model);s=model.Capture();
                Require(s.Players.All(p=>p.Swimming.InWater && !SwimmingTerrain.Obstacle(map,p.X,p.Z)),"Spacing entered a merchant or pushed a swimmer onto the deck");
            }
            Separate(s.Players.Select(p=>new Point(p.X,p.Z)).ToArray(),660);
        });
        yield return ("stationary_water_monster_is_an_anchor_and_keeps_aim",()=>{
            var map=Map(enemies:new EnemyConfig(piranhaCount:0));var model=Model(map);var s=model.Capture();var people=s.Players.ToArray();
            people[0].X=7500;people[0].Z=-7000;people[0].Swimming.InWater=true;
            var monster=new WaterMonster{Phase=MonsterPhase.Windup,Timer=10,Target=0,X=7000,Z=-7000,Level=1,DirectionX=EnemyGeometry.DirectionScale,Cycle=1};
            model.Restore(Replace(s,people,enemies:new EnemyState(Array.Empty<Piranha>(),monster,Array.Empty<Waterball>())));Step(model);s=model.Capture();
            Require(s.Enemies.Monster.X==7000 && s.Enemies.Monster.Z==-7000 && s.Enemies.Monster.DirectionX==EnemyGeometry.DirectionScale && s.Enemies.Monster.DirectionZ==0,"Spacing moved or rotated the anchored monster");
            Require(IslandMap.DistanceSquared(s.Players[0].X,s.Players[0].Z,7000,-7000)>=1228L*1228,"Swimmer remains inside the water monster");
        });
        yield return ("downed_body_is_fixed_but_does_not_allow_living_player_overlap",()=>{
            var model=Model();var s=model.Capture();var people=s.Players.ToArray();people[0].Vitals.Health=0;
            people[1].X=people[0].X;people[1].Z=people[0].Z;int x=people[0].X,z=people[0].Z;
            model.Restore(Replace(s,people));Step(model);s=model.Capture();
            Require(s.Players[0].Vitals.Downed && s.Players[0].X==x && s.Players[0].Z==z,"Spacing moved a downed rescue target");
            Separate(s.Players.Select(p=>new Point(p.X,p.Z)).ToArray(),660);
        });
        yield return ("climbing_crabs_can_adjust_landing_points_without_changing_shore",()=>{
            var map=Map(new CrabConfig(initialDelayTicks:108000,count:3));var model=Model(map);var s=model.Capture();
            var crabs=Enumerable.Range(0,3).Select(_=>new CrabState{Generation=1,Phase=CrabPhase.Climbing,Timer=20,Health=100,
                X=0,Z=-2400,ShoreZ=-3500,DirectionZ=EnemyGeometry.DirectionScale,Target=-1}).ToArray();
            model.Restore(Replace(s,crabs:crabs));Step(model);s=model.Capture();
            Require(s.Crabs.All(c=>c.Phase==CrabPhase.Climbing && c.ShoreX==0 && c.ShoreZ==-3500),"Spacing rewrote climb origin");
            Separate(s.Crabs.Select(c=>new Point(c.X,c.Z)).ToArray(),800);
        });
    }
}
