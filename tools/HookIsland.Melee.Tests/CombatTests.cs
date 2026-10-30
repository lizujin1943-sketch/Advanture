using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.MeleeTests;
internal static partial class Program
{
    private static IEnumerable<(string,Action)> CombatCases()
    {
        yield return ("piranha_takes_two_hits_and_respawns_without_fishing_rewards",()=>{
            var cfg=new EnemyConfig(piranhaCount:1,monsterEnabled:false,patrolSpeed:30,piranhaRespawnTicks:60);
            var m=Model(enemies:cfg);Equip(m);Pair(m);var s=m.Capture();var f=s.Enemies.Piranhas.ToArray();f[0].X=1000;f[0].Z=-1400;
            m.Restore(Replace(s,enemies:new EnemyState(f,s.Enemies.Monster,s.Enemies.Waterballs)));
            Step(m,new HookInput(0,0,2000,-1400,true));for(int i=1;i<6;i++)Step(m);
            var hit=m.Capture();Require(hit.Enemies.Piranhas[0].DamageTaken==25 && hit.Enemies.Piranhas[0].StunTicks>0,"Piranha did not take damage/stun");m.Restore(hit);
            for(int i=0;i<19;i++)Step(m);Step(m,new HookInput(0,0,2000,-1400,true));for(int i=1;i<6;i++)Step(m);
            var dead=m.Capture();Require(dead.Enemies.Piranhas[0].RespawnTicks>0 && dead.Enemies.Piranhas[0].DamageTaken==50,"Piranha survived two hits");Require(dead.Coins==0 && dead.Enemies.Captures==0 && dead.Players[0].Catches==0,"Melee minted fishing rewards");m.Restore(dead);
            for(int i=0;i<60;i++)Step(m);Require(m.Capture().Enemies.Piranhas[0].DamageTaken==0 && m.Capture().Enemies.Piranhas[0].RespawnTicks==0,"Respawn did not restore health");
        });
        yield return ("pan_interrupts_monster_on_final_windup_tick",()=>{
            var cfg=new EnemyConfig(piranhaCount:0,windupTicks:60,initialDelayTicks:18000);var m=Model(enemies:cfg);Equip(m);var s=m.Capture();var ps=s.Players.ToArray();ps[0].X=0;ps[0].Z=-2670;ps[0].HookX=0;ps[0].HookZ=-2670;
            var monster=s.Enemies.Monster;monster.X=0;monster.Z=-4100;monster.Phase=MonsterPhase.Windup;monster.Timer=6;monster.Target=0;monster.DirectionX=0;monster.DirectionZ=1000000;
            m.Restore(Replace(s,ps,new EnemyState(s.Enemies.Piranhas,monster,s.Enemies.Waterballs)));Step(m,new HookInput(0,0,0,-5000,true));for(int i=1;i<6;i++)Step(m);
            Require(m.Capture().Enemies.Interruptions==1 && m.Capture().Enemies.Shots==0,"Last-tick melee interruption failed");m.Restore(m.Capture());
        });
        yield return ("capturing_or_releasing_a_stunned_piranha_clears_stun_before_respawn",()=>{
            var cfg=new EnemyConfig(piranhaCount:1,monsterEnabled:false,piranhaRespawnTicks:60);
            var m=Model(enemies:cfg);var s=m.Capture();var fish=s.Enemies.Piranhas.ToArray();fish[0].DamageTaken=25;fish[0].StunTicks=10;
            var enemies=new EnemySystem(cfg,new EnemyState(fish,s.Enemies.Monster,s.Enemies.Waterballs));
            Require(enemies.TryHook(0,0) && enemies.CompleteCapture(0,0),"Could not capture injured piranha");
            Require(enemies.GetPiranha(0).StunTicks==0,"Captured piranha retains stun during respawn");
            enemies=new EnemySystem(cfg,new EnemyState(fish,s.Enemies.Monster,s.Enemies.Waterballs));Require(enemies.TryHook(0,0),"Could not hook injured piranha");
            enemies.Release(0,0,new Point(4000,4000));
            for(int i=0;i<120 && enemies.GetPiranha(0).RespawnTicks==0;i++)enemies.Advance(s.Players,_=>{});
            Require(enemies.GetPiranha(0).RespawnTicks>0 && enemies.GetPiranha(0).StunTicks==0,"Escaped piranha retains stun during respawn");
            m.Restore(Replace(s,enemies:enemies.Capture()));
        });
        yield return ("insufficient_stamina_and_water_without_ring_cannot_attack",()=>{
            var m=Model();Equip(m);var s=m.Capture();var ps=s.Players.ToArray();ps[0].Stamina=8*StaminaSystem.UnitsPerPoint-1;m.Restore(Replace(s,ps));Step(m,new HookInput(0,0,2000,-1400,true));Require(m.Capture().Players[0].Melee.Serial==0,"Attacked without stamina");
            s=m.Capture();ps=s.Players.ToArray();ps[0].X=4000;ps[0].HookX=4000;ps[0].Swimming.InWater=true;ps[0].Stamina=50*StaminaSystem.UnitsPerPoint;m.Restore(Replace(s,ps));Step(m,new HookInput(0,0,5000,-1400,true));Require(m.Capture().Players[0].Melee.Serial==0,"Attacked underwater without ring");
        });
        yield return ("protected_downed_and_swimming_friends_are_not_prank_targets",()=>{
            var m=Model();Equip(m);Pair(m);var s=m.Capture();var ps=s.Players.ToArray();ps[1].ProtectionTicks=60;m.Restore(Replace(s,ps));Step(m,new HookInput(0,0,2000,-1400,true));for(int i=1;i<6;i++)Step(m);Require(m.Capture().Players[0].Melee.HitCount==0,"Spawn protection ignored");
            var p=m.Capture().Players[0];p.Vitals.Health=0;Require(!MeleeRules.CanAct(p,false),"Downed player can attack");p.Vitals.Health=100;p.Swimming.InWater=true;Require(!MeleeRules.CanAct(p,false) && MeleeRules.CanAct(p,true),"Ring attack rule ignored");
        });
        yield return ("melee_cannot_overlap_teammate_revival",()=>{
            var m=Model();Equip(m);Pair(m);Step(m,new HookInput(0,0,2000,-1400,true));var s=m.Capture();var target=s.Players[1];target.Vitals.Health=0;
            Require(!RevivalSystem.CanHelp(Map(),s.Tiles,s.Players[0],target),"Swinging player may revive at the same time");
        });
        yield return ("being_hit_cancels_swing_before_it_can_hit_back",()=>{
            var m=Model();Equip(m);Equip(m,1);Pair(m);
            Step(m,new HookInput(0,0,2000,-1400,true),new HookInput(0,0,-2000,-1400,true));
            for(int i=1;i<6;i++)Step(m);
            var s=m.Capture();Require(s.Players[0].Melee.HitCount==1 && s.Players[1].Melee.HitCount==0 && !s.Players[1].Melee.Active,"Interrupted windup still hit back");
            m.Restore(new HookGame().Decode(new HookGame().Encode(s)));
        });
        yield return ("invalid_rescue_intent_does_not_grant_pan_immunity",()=>{
            var m=Model();Equip(m);Pair(m);var fake=new HookInput(0,0,revival:new RevivalCommand(rescueSlot:1));
            Step(m,new HookInput(0,0,2000,-1400,true),fake);for(int i=1;i<6;i++)Step(m,default,fake);
            Require(m.Capture().Players[0].Melee.HitCount==1 && m.Capture().Players[1].Knockback.Active,"Self-rescue intent incorrectly grants immunity");
        });
        yield return ("valid_teammate_rescue_is_protected_from_pan_pranks",()=>{
            var m=Model(3);Equip(m);Pair(m);var s=m.Capture();var ps=s.Players.ToArray();ps[2].X=ps[2].HookX=1000;ps[2].Z=ps[2].HookZ=-900;ps[2].Vitals.Health=0;m.Restore(Replace(s,ps));
            var help=new HookInput(0,0,revival:new RevivalCommand(rescueSlot:2));Step(m,new HookInput(0,0,2000,-1400,true),help);for(int i=1;i<6;i++)Step(m,default,help);
            s=m.Capture();Require(s.Players[0].Melee.HitCount==0 && !s.Players[1].Knockback.Active && s.Players[2].Revival.RescueTicks>0,"Pan disrupted an eligible rescue");
        });
        yield return ("switch_to_pan_is_rejected_while_hook_is_out",()=>{
            var m=Model();Step(m,new HookInput(0,0,-1400,-5400,true));var s=m.Capture();var pan=s.Tackle!.Inventory.Items.First(i=>i.Owner==0 && i.Kind==ItemKind.Weapon);
            Step(m,new HookInput(0,0,inventory:new InventoryCommand(InventoryAction.Equip,pan.Id,requestId:1)));Require(m.Capture().Players[0].LastInventoryResult==InventoryResult.HookBusy && m.Capture().Players[0].EquippedRod!=0,"Rod removed while hook is out");
        });
    }
}
