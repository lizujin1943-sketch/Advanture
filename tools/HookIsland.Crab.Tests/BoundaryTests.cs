using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.CrabTests;
internal static partial class Program
{
    private static IEnumerable<(string,Action)> BoundaryCases()
    {
        yield return ("actual_hook_interrupts_without_damage_or_reward_eligibility",()=>{
            var m=Model(1);var c=Ready();c.Phase=CrabPhase.Windup;c.Timer=18;c.Target=0;c.AttackSerial=1;
            m.Restore(Replace(m.Capture(),c));Step(m,new HookInput(0,0,aimX:0,aimZ:-1400,fire:true));for(int i=0;i<9;i++)Step(m);
            c=m.Capture().Crab;Require(c.Phase==CrabPhase.Recovering && c.InterruptProtection>0,"Real hook did not interrupt");
            Require(c.Health==100 && c.Attackers==0 && m.Capture().Coins==0,"Hook acquired damage rewards");
        });
        yield return ("reward_overflow_stays_pending_without_partial_materials_or_respawn",()=>{
            var m=Model(1);var s=m.Capture();var players=s.Players.ToArray();players[0].Coins=players[0].CoinsEarned=int.MaxValue;
            var c=Ready();c.Phase=CrabPhase.Dead;c.Health=0;c.Timer=1;c.Attackers=1;
            m.Restore(new HookState(s.Steps,players,s.Fish,int.MaxValue,s.Tiles,s.Driftwood,s.Tackle,s.Enemies,s.RoomRules,s.Treasure,s.Commissions,s.Ocean,s.Finale,c));
            for(int i=0;i<10;i++)Step(m);s=m.Capture();Require(!s.Crab.Rewarded && s.Crab.Timer==1 && s.Crab.Phase==CrabPhase.Dead,"Pending credit lost");
            Require(s.Players[0].Coins==int.MaxValue && s.Players[0].FishbonesEarned==0 && s.Tackle!.Inventory.Items.All(i=>i.Kind!=ItemKind.Fishbone),"Partial reward committed");
        });
        yield return ("respawn_clears_contributors_and_new_generation_has_full_health",()=>{
            var map=Map();var c=Ready();c.Phase=CrabPhase.Dead;c.Health=0;c.Timer=1;c.Attackers=3;c.Rewarded=true;
            var s=new CrabSystem(map,c);var players=new[]{new Fisher{Slot=0,X=-1400,Z=-1400,Vitals=new CharacterVitals{Health=100}}};
            s.Advance(PlatformRules.InitialTiles(),players,_=>{});s.Advance(PlatformRules.InitialTiles(),players,_=>{});c=s.Capture();
            Require(c.Generation==2 && c.Health==100 && c.Attackers==0 && !c.Rewarded && c.Phase==CrabPhase.Climbing,"Old reward participants leaked into respawn");
        });
        yield return ("pinch_can_hit_edge_and_shopping_players_but_not_water_or_downed",()=>{
            var map=Map();var c=Ready(1700,-1400);c.Phase=CrabPhase.Windup;c.Timer=1;c.AttackSerial=1;
            var people=new[]{new Fisher{Slot=0,X=2800,Z=-1400,Shopping=true,Vitals=new CharacterVitals{Health=100}},
                new Fisher{Slot=1,X=2700,Z=-1300,Swimming=new SwimmingState{InWater=true},Vitals=new CharacterVitals{Health=100}},
                new Fisher{Slot=2,X=2700,Z=-1500}};
            var hits=new List<EnemyHit>();new CrabSystem(map,c).Advance(PlatformRules.InitialTiles(),people,hits.Add);
            Require(hits.Count==1 && hits[0].Slot==0,"Wrong edge/shopping/water/downed targeting");
        });
        yield return ("formal_map_is_enabled_and_round_trips",()=>{
            var path=Path.Combine("samples","TacticalGame.Unity","Assets","HookIsland","Data","PlatformMap.bytes");
            var bytes=File.ReadAllBytes(path);var map=IslandMap.Decode(bytes);Require(map.Fishing!.Crabs.Enabled && map.Fishing.Crabs.InitialDelayTicks==2700,"Formal crab defaults missing");
            Require(bytes.SequenceEqual(map.Encode()),"Formal map encoding changed");
        });
    }
}
