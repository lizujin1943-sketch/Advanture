using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.RevivalTests;
internal static partial class Program
{
    private static IEnumerable<(string,Action)> BoundaryCases()
    {
        yield return ("snapshot_rejects_altered_death_price",()=>{
            var m=Model(revival:new RevivalConfig(downedTicks:1));Down(m,1,200);Step(m);
            Throws<InvalidDataException>(()=>Change(m,p=>p[1].Revival.DeathCost=0));
        });
        yield return ("snapshot_rejects_help_owned_by_unavailable_or_displaced_player",()=>{
            var m=Model(enemies:new EnemyConfig(initialDelayTicks:18000));Down(m,1);
            Change(m,p=>{p[0].X=0;p[0].Z=-2000;});Wait(m,10,Help(1));
            Throws<InvalidDataException>(()=>Change(m,p=>p[0].Unavailable=true));
            Throws<InvalidDataException>(()=>Change(m,p=>p[0].Knockback=new KnockbackState{Active=true,OriginX=p[0].X,OriginZ=p[0].Z,DirectionX=1000000}));
        });
        yield return ("final_death_disallows_external_movement_and_unavailable_payment",()=>{
            var m=Model(revival:new RevivalConfig(downedTicks:1),enemies:new EnemyConfig(initialDelayTicks:18000));Down(m,1,200);Step(m);
            Throws<InvalidDataException>(()=>Change(m,p=>p[1].Knockback=new KnockbackState{Active=true,OriginX=p[1].X,OriginZ=p[1].Z,DirectionX=1000000}));
            Change(m,p=>p[1].Unavailable=true);Step(m,default,Pay(m.Capture().Players[1]));
            Require(m.Capture().Players[1].Revival.Dead && m.Capture().Players[1].Coins==200,"Unavailable player paid to respawn.");
        });
        yield return ("snapshot_rejects_duplicate_helper",()=>{
            var m=Model(3);Down(m,1);Down(m,2);Change(m,p=>{p[0].X=300;p[0].Z=-2000;});
            Throws<InvalidDataException>(()=>Change(m,p=>{
                p[1].Revival.Rescuer=p[2].Revival.Rescuer=1;p[1].Revival.RescueTicks=p[2].Revival.RescueTicks=1;}));
        });
        yield return ("stale_death_generation_and_alive_click_never_charge_on_later_death",()=>{
            var m=Model(revival:new RevivalConfig(downedTicks:1));
            Step(m,default,new HookInput(0,0,revival:new RevivalCommand(respawnRequest:1,deathId:1)));
            Down(m,1,200);Step(m);Step(m,default,Pay(m.Capture().Players[1],1));
            Require(m.Capture().Players[1].Revival.Dead && m.Capture().Players[1].Coins==200,"Alive click replayed after death.");
            Step(m,default,Pay(m.Capture().Players[1],2));Change(m,p=>p[1].Vitals.Health=0);Step(m);
            Step(m,default,new HookInput(0,0,revival:new RevivalCommand(respawnRequest:3,deathId:1)));
            Require(m.Capture().Players[1].Revival.LastResult==RespawnResult.StaleDeath && m.Capture().Players[1].Coins==170,"Old-life request charged new death.");
        });
        yield return ("no_safe_landing_does_not_charge_or_restore_resources",()=>{
            var m=Model(revival:new RevivalConfig(downedTicks:1));Down(m,1,200);Step(m);var s=m.Capture();var p=s.Players[1];int total=s.Coins;
            RevivalSystem.Respawn(IslandMap.CreatePlatform(),Array.Empty<PlatformTile>(),s.Players,ref p,Pay(p).Revival,FishingRules.Attributes(s,p),ref total);
            Require(p.Revival.LastResult==RespawnResult.NoLanding && p.Revival.Dead && p.Coins==200 && total==200 && p.Vitals.Health==0,"Failed respawn partially committed.");
        });
        yield return ("new_zero_health_gets_full_countdown_and_dead_actions_stay_blocked",()=>{
            var m=Model(revival:new RevivalConfig(downedTicks:2));Change(m,p=>{p[1].X=0;p[1].Z=-4000;p[1].Swimming.InWater=true;p[1].Vitals.Health=1;p[1].Swimming.DamageRemainder=25000;});
            Step(m);Require(m.Capture().Players[1].Vitals.Downed && m.Capture().Players[1].Revival.DownedTicks==0,"Lethal frame shortened rescue window.");
            Step(m);Step(m);int x=m.Capture().Players[1].X;
            Wait(m,30,default,new HookInput(1000,0,1000,-4000,true));Require(m.Capture().Players[1].Revival.Dead && m.Capture().Players[1].X==x && m.Capture().Players[1].Casts==0,"Dead player moved or cast.");
        });
        yield return ("combat_hit_interrupts_help_and_displaced_target_cannot_receive_help",()=>{
            var m=Model(enemies:new EnemyConfig(piranhaCount:0,waterballSpeed:30000,initialDelayTicks:18000));
            Down(m,1,200);Change(m,p=>{p[0].X=0;p[0].Z=-2400;p[1].X=700;p[1].Z=-2400;});Wait(m,30,Help(1));
            // Three complete 30 Hz flight steps at 30 m/s, before the sweep reaches the helper.
            var s=m.Capture();var ball=new Waterball {Id=1,OriginX=0,OriginZ=-6000,X=0,Z=-3000,DirectionZ=1000000,Distance=3000,KnockbackDistance=s.Tackle!.Config.Enemies.KnockbackDistance};
            m.Restore(new HookState(s.Steps,s.Players,s.Fish,s.Coins,s.Tiles,s.Driftwood,s.Tackle,
                new EnemyState(s.Enemies.Piranhas,s.Enemies.Monster,new[]{ball},serial:1,shots:1)));
            Step(m,Help(1));s=m.Capture();Require(s.Players[0].Vitals.Health==80 && s.Players[0].Knockback.Active && s.Players[1].Revival.RescueTicks==0,"Combat did not interrupt help.");
            // Downed targets also cannot gain help while external displacement is unfinished.
            Change(m,p=>{p[0].Knockback=default;KnockbackSystem.Start(ref p[1],1000000,0,s.Tackle!.Config.Enemies.KnockbackDistance);});
            Step(m,Help(1));Require(m.Capture().Players[1].Revival.RescueTicks==0,"Moving target gained help progress.");
        });
        yield return ("deactivated_helper_cannot_continue_buffered_rescue",()=>{
            var m=Model();Down(m,1);Change(m,p=>{p[0].X=0;p[0].Z=-2000;});Wait(m,30,Help(1));var s=m.Capture();
            m.Step(new FrameSync.Abstractions.FrameContext(s.Steps+1,s.Steps+1,30,new[]{new FrameSync.Abstractions.MemberEvent(1,FrameSync.Abstractions.MemberEventKind.Deactivate,0,1)}),
                new[]{new FrameSync.Abstractions.SimulationInput(new FrameSync.Abstractions.Participant(0,new Guid(1,0,0,new byte[8])),new FrameSync.Abstractions.ByteString(Help(1).Encode()))});
            Require(m.Capture().Players[1].Revival.RescueTicks==0,"Inactive helper retained progress.");
        });
#if HOOK_ISLAND_TESTS
        yield return ("normal_inputs_earn_coins_tow_help_and_pay_to_respawn",()=>{
            foreach(bool enemies in new[]{false,true})
            {
                var m=Model(4,enemies:enemies?new EnemyConfig():null);var map=IslandMap.CreatePlatform();var pilot=new Advanture.HookIsland.RevivalSmokePilot();
                var queue=new Queue<HookInput[]>();for(int i=0;i<4;i++)queue.Enqueue(new HookInput[4]);
                for(int i=0;i<5000 && !pilot.Complete(m.Capture());i++)
                {var s=m.Capture();queue.Enqueue(s.Players.Select(p=>pilot.Sample(map,s,p.Slot,s.Steps+5)).ToArray());Step(m,queue.Dequeue());}
                var end=m.Capture();Require(pilot.Complete(end),"Normal input scenario failed (enemies="+enemies+"): "+string.Join("; ",end.Players.Select(p=>$"P{p.Slot} {p.X},{p.Z} hp={p.Vitals.Health} dead={p.Revival.Dead} earned={p.CoinsEarned} spent={p.Revival.CoinsSpent} towed={p.TimesHooked}")));
                Require(end.Players[1].Revival.CoinsSpent==0 && end.Players[2].CoinsEarned>0 && end.Players[2].Revival.CoinsSpent>0,"Real personal earnings not used.");
            }
        });
#endif
        yield return ("revival_input_roundtrip_rejects_invalid_targets_and_mixed_transactions",()=>{
            var c=Help(3);var d=HookInput.Decode(c.Encode());Require(d.Revival.RescueSlot==3,"Rescue target lost on wire.");
            Throws<InvalidDataException>(()=>new RevivalCommand(rescueSlot:4));
            Throws<InvalidDataException>(()=>new RevivalCommand(rescueSlot:1,respawnRequest:1,deathId:1));
            Throws<InvalidDataException>(()=>new HookInput(0,0,build:TileKind.Wood,buildId:1,revival:new RevivalCommand(rescueSlot:1)).Encode());
        });
    }
}
