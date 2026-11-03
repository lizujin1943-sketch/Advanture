using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.FirearmTests;
internal static partial class Program
{
    private static IEnumerable<(string,Action)> BoundaryCases()
    {
        yield return ("friendly_prank_push_is_400mm_and_roundtrips",()=>{
            var m=Model();var s=m.Capture();m.Restore(new HookState(s.Steps,s.Players,s.Fish,s.Coins,s.Tiles,s.Driftwood,s.Tackle,s.Enemies,new RoomRulesState(true),s.Treasure,s.Commissions,s.Ocean,s.Finale));
            Step(m,Shoot(true));s=m.Capture();Require(s.Players[1].Vitals.Health==82 && s.Players[1].Knockback.Active && s.Players[1].Knockback.TotalDistance==400,"Wrong firearm friendly push");m.Restore(s);Wait(m,2);Require(!m.Capture().Players[1].Knockback.Active && m.Capture().Players[1].X>=1098,"Short push did not finish");m.Restore(m.Capture());
        });
        foreach(bool lethal in new[]{false,true})
            yield return ($"reload_completion_frame_{(lethal?"downing_cancels":"ordinary_damage_preserves")}_ammo",()=>{
                var m=Model();Step(m,default,Buy(FirearmCatalog.OfferOffset,1,true));Wait(m,9);Step(m,Shoot());Wait(m,6);
                if(lethal){var s=m.Capture();var ps=s.Players.ToArray();ps[0].Vitals.Health=10;m.Restore(Replace(s,ps));}
                Step(m,new HookInput(0,0,reload:true));Wait(m,53);Step(m,default,new HookInput(0,0,-1000,-1400,true));
                Require(Gun(m).LoadedAmmo==(lethal?6:7) && Reserve(m)==0,"Same-frame damage/reload transaction wrong");
                Require(m.Capture().Players[0].Vitals.Health==(lethal?0:82),"Friendly damage missing");m.Restore(m.Capture());
            });
        yield return ("gun_quickbar_switch_retains_player_cooldown_and_spread",()=>{
            var m=Model();int id=Gun(m).Id;Step(m,new HookInput(0,0,inventory:new InventoryCommand(InventoryAction.BindQuickSlot,id,amount:6,requestId:3)));
            Step(m,Shoot(move:1000));Wait(m,6);int cooldown=m.Capture().Players[0].Firearm.CooldownTicks;
            Step(m,new HookInput(0,0,inventory:new InventoryCommand(InventoryAction.SelectQuickSlot,1,requestId:4)));
            Require(m.Capture().Players[0].EquippedRod>0 && m.Capture().Players[0].Firearm.CooldownTicks==cooldown-1,"Switch reset gun cooldown");
            Step(m,new HookInput(0,0,inventory:new InventoryCommand(InventoryAction.SelectQuickSlot,6,requestId:5)));
            Require(m.Capture().Players[0].Firearm.DrawTicks==9 && m.Capture().Players[0].SelectedQuickSlot==5,"Quickbar did not draw gun");Step(m,Shoot());Require(Gun(m).LoadedAmmo==6,"Fired during draw");m.Restore(m.Capture());
        });
        yield return ("last_windup_tick_gun_interrupts_monster_and_shop_blocks_bullets",()=>{
            var config=new EnemyConfig(piranhaCount:0,initialDelayTicks:18000);var m=Model(config);var s=m.Capture();var monster=s.Enemies.Monster;
            monster.X=-1000;monster.Z=-4100;monster.Phase=MonsterPhase.Windup;monster.Timer=1;monster.Target=0;monster.DirectionX=0;monster.DirectionZ=1000000;
            m.Restore(Replace(s,enemies:new EnemyState(s.Enemies.Piranhas,monster,s.Enemies.Waterballs)));Step(m,Shoot());Require(m.Capture().Enemies.Interruptions==1 && m.Capture().Enemies.Shots==0,"Gun did not interrupt final windup");m.Restore(m.Capture());
            m=Model();s=m.Capture();var ps=s.Players.ToArray();ps[0].X=ps[0].HookX=0;ps[1].X=ps[1].HookX=0;ps[1].Z=ps[1].HookZ=2400;m.Restore(Replace(s,ps));
            var e=Step(m,new HookInput(0,0,0,8000,true));Require(m.Capture().Players[1].Vitals.Health==100 && e.Single(v=>v.Kind==HookGameplayEventKind.FirearmShot).TargetId==-4,"Shot passed through merchant");
        });
        yield return ("stowed_spread_recovers_and_blocked_movement_does_not_widen",()=>{
            var m=Model();var s=m.Capture();var ps=s.Players.ToArray();ps[0].X=ps[0].HookX=50000;ps[0].Swimming.InWater=true;FirearmRules.CancelActions(ref ps[0]);m.Restore(Replace(s,ps));
            Step(m,new HookInput(1000,0));Require(m.Capture().Players[0].Firearm.MoveSpeed==0 && m.Capture().Players[0].Firearm.Spread(0)==500,"Blocked input widened spread");
            var f=new FirearmState();FirearmSpread.Advance(ref f,4500,new[]{0});for(int i=0;i<15;i++)FirearmSpread.Advance(ref f,0,Array.Empty<int>());Require(f.Spread(0)==500,"Stowed spread stopped recovering");
        });
    }
}
