using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;
using FrameSync.Abstractions;

namespace Advanture.HookIsland.Tests;
internal static partial class Program
{
    private static void LandComboFish(IDeterministicSimulation<HookState> model)
    {
        CoopEdit(model,p=>{
            p[0].X=0;p[0].Z=0;p[0].HookX=-2800;p[0].HookZ=0;p[0].Distance=2800;
            p[0].Phase=HookPhase.Returning;p[0].FishId=0;p[0].Stamina=600000;
            p[0].Combo.PendingCast=model.Capture().Tackle!.Config.Combo.Enabled;
        },f=>{
            f[0].X=-2800;f[0].Z=0;f[0].Y=1250;f[0].Owner=0;f[0].Phase=FishPhase.Hooked;
            f[0].Species=0;f[0].WeightGrams=5000;f[0].Grade=1;
        });
        ShopStep(model);
    }
    private static IEnumerable<(string,Action)> ComboCases()
    {
        yield return ("combo_driftwood_overextended_or_knocked_away_breaks_without_material_credit",()=>{
            foreach(bool hit in new[]{false,true})
            {
                var m=TreasureModel(TreasureKind.Small,2,enemyConfig:new EnemyConfig(piranhaCount:0,initialDelayTicks:18000));
                var s=m.Capture();var p=s.Players.ToArray();var wood=s.Driftwood.ToArray();
                p[0].Combo=new ComboState{Count=8,PendingCast=true};p[0].Phase=HookPhase.Returning;p[0].DriftwoodId=0;
                p[0].HookX=hit?-4500:-7000;p[0].HookZ=p[0].Z;
                wood[0].Phase=DriftwoodPhase.Hooked;wood[0].Owner=0;wood[0].X=p[0].HookX;wood[0].Z=p[0].HookZ;
                var enemies=s.Enemies;
                if(hit)
                {
                    var ball=new Waterball{Id=1,OriginX=-3500,OriginZ=-600,X=-3234,Z=-600,DirectionX=1000000,Distance=266,TravelRemainder=20,KnockbackDistance=4000};
                    enemies=new EnemyState(enemies.Piranhas,enemies.Monster,new[]{ball},1,0,1);
                }
                m.Restore(new HookState(s.Steps,p,s.Fish,s.Coins,s.Tiles,wood,s.Tackle,enemies,s.RoomRules,s.Treasure));ShopStep(m);
                var after=m.Capture().Players[0];
                Require(after.Combo.Count==0 && after.DriftwoodId==-1 && after.WoodEarned==0,"Broken driftwood line retained combo/wood: hit="+hit);
            }
        });
        yield return ("combo_curve_caps_reward_not_count_and_bones_are_unchanged",()=>{
            var m=CoopModel(1);int before=m.Capture().Coins;
            var expected=new[]{20,40,62,85,109,134,160,187,215,245,275};
            for(int i=0;i<expected.Length;i++)
            {
                LandComboFish(m);var s=m.Capture();
                Require(s.Coins-before==expected[i] && s.Players[0].Combo.Count==i+1,"Incorrect threshold or capped counter at "+(i+1));
                Require(s.Players[0].FishbonesEarned==(i+1)*10,"Combo multiplied fishbones.");
            }
            CoopEdit(m,p=>p[0].Combo.Count=99);before=m.Capture().Coins;LandComboFish(m);
            Require(m.Capture().Players[0].Combo.Count==100 && m.Capture().Coins-before==30,"100th catch lost its real count or exceeded +50%.");
        });
        yield return ("combo_third_fish_rewards_current_catch",()=>{
            var m=CoopModel(1);int before=m.Capture().Coins;
            for(int i=0;i<3;i++)LandComboFish(m);
            Require(m.Capture().Players[0].Catches==3,"Fixture did not land three fish.");
            Require(m.Capture().Coins-before==62,"Third 20-coin catch must pay 22 (20 + 10%), total 62.");
        });
        yield return ("combo_shared_fish_uses_highest_new_tier_once_then_splits",()=>{
            var m=CoopModel();CoopJoin(m,1,new Point(-2600,700));
            CoopEdit(m,p=>{p[0].Combo.Count=9;p[1].Combo.Count=1;},f=>{f[0].WeightGrams=5000;f[0].Grade=3;});
            var before=m.Capture();for(int i=0;i<40 && m.Capture().Players[0].Catches==0;i++)ShopStep(m);
            var s=m.Capture();Require(s.Coins-before.Coins==90,"60-coin grade-3 fish should become 90 exactly once.");
            Require(s.Players[0].Coins-before.Players[0].Coins==45 && s.Players[1].Coins-before.Players[1].Coins==45,"Bonus not split after whole-fish calculation.");
            Require(s.Players[0].Combo.Count==10 && s.Players[1].Combo.Count==2,"Personal counts were shared or reset by return.");
            for(int i=0;i<30;i++)ShopStep(m);
            Require(m.Capture().Players[0].Combo.Count==10 && m.Capture().Players[1].Combo.Count==2,"Successful empty line return reset the combo.");
        });
        yield return ("combo_failed_material_transaction_cannot_advance_or_pay",()=>{
            var m=CoopModel();CoopJoin(m,1,new Point(-2600,700));CoopEdit(m,p=>{p[0].Combo.Count=9;p[1].Combo.Count=2;});
            var s=m.Capture();var t=s.Tackle!;
            var inv=new InventoryState(2,t.Inventory.Items,int.MaxValue-1,t.Inventory.SpentCoins);
            m.Restore(new HookState(s.Steps,s.Players,s.Fish,s.Coins,s.Tiles,s.Driftwood,new TackleState(t.Config,inv,t.Stock,t.Batch,t.NextRestock),s.Enemies));
            for(int i=0;i<40;i++)ShopStep(m);var after=m.Capture();
            Require(after.Coins==s.Coins && after.Players[0].Combo.Count==9 && after.Players[1].Combo.Count==2 && after.Players.All(p=>p.Catches==0 && p.FishId==0),"Failed inventory credit changed the reward or combo.");
        });
        yield return ("combo_cancel_escape_reverse_steal_and_overrange_reset_only_failed_members",()=>{
            foreach(string failure in new[]{"cancel","stamina","range","steal"})
            {
                var m=CoopModel(failure=="steal"?3:2);CoopJoin(m,1,new Point(-2600,700));
                CoopEdit(m,p=>{p[0].Combo.Count=7;p[1].Combo.Count=4;if(failure=="stamina")p[0].Stamina=1;if(failure=="range")p[0].X=0;});
                if(failure=="steal")CoopJoin(m,2,new Point(-7000,1000));
                else ShopStep(m,(failure=="cancel"?new HookInput(0,0,cancel:true):default).Encode());
                var s=m.Capture();Require(s.Players[0].Combo.Count==0,"Failed line retained bonus: "+failure);
                Require(s.Players[1].Combo.Count==(failure=="steal"?0:4),"Unrelated line changed count: "+failure);
            }
        });
        yield return ("combo_empty_cast_cancel_and_idle_have_distinct_outcomes",()=>{
            foreach(bool cancel in new[]{false,true})
            {
                var m=RodModel();CoopEdit(m,p=>p[0].Combo.Count=7);
                for(int i=0;i<100;i++)ShopStep(m);
                Require(m.Capture().Players[0].Combo.Count==7,"Resting expired the streak.");
                var p=m.Capture().Players[0];ShopStep(m,new HookInput(0,0,p.X+2000,p.Z+2000,true).Encode());
                if(cancel)ShopStep(m,new HookInput(0,0,cancel:true).Encode());
                for(int i=0;i<130;i++)ShopStep(m);
                Require(m.Capture().Players[0].Combo.Count==0 && !m.Capture().Players[0].Combo.PendingCast,"Empty/cancelled cast retained combo.");
            }
        });
        yield return ("combo_teammate_hit_cancel_return_and_checkpoint_preserve_both_counts",()=>{
            var m=RodModel(count:2);CoopEdit(m,p=>{p[0].Combo.Count=9;p[1].Combo.Count=4;p[1].X=1800;});
            var s=m.Capture();m.Restore(new HookState(s.Steps,s.Players,s.Fish,s.Coins,s.Tiles,s.Driftwood,s.Tackle,s.Enemies,new RoomRulesState(true)));
            var friend=m.Capture().Players[1];ShopStep(m,new HookInput(0,0,friend.X,friend.Z,true).Encode());
            for(int i=0;i<20 && m.Capture().Players[0].PlayerHooks==0;i++)ShopStep(m);
            Require(m.Capture().Players[0].PlayerHooks==1,"Fixture did not hit teammate.");
            var codec=new HookGame();m.Restore(codec.Decode(codec.Encode(m.Capture())));
            ShopStep(m,new HookInput(0,0,cancel:true).Encode());for(int i=0;i<60;i++)ShopStep(m);
            Require(m.Capture().Players[0].Combo.Count==9 && m.Capture().Players[1].Combo.Count==4,"Teammate return counted as empty cast or rewarded a count.");
        });
        yield return ("combo_teammate_interrupting_airborne_cast_preserves_victim_streak",()=>{
            var m=RodModel(count:2);CoopEdit(m,p=>{p[0].Combo.Count=9;p[1].Combo.Count=7;p[1].X=1800;});
            var s=m.Capture();m.Restore(new HookState(s.Steps,s.Players,s.Fish,s.Coins,s.Tiles,s.Driftwood,s.Tackle,s.Enemies,new RoomRulesState(true)));
            ShopStep(m,new HookInput(0,0,1800,0,true).Encode(),new HookInput(0,0,1800,2000,true).Encode());
            for(int i=0;i<100;i++)ShopStep(m);s=m.Capture();
            Require(s.Players[0].PlayerHooks==1 && !s.Players[1].Swimming.InWater,"Fixture did not tow airborne caster on land.");
            Require(s.Players[0].Combo.Count==9 && s.Players[1].Combo.Count==7,"Forced return of airborne teammate hook falsely broke victim's streak.");
        });
        yield return ("combo_all_driftwood_sizes_increment_without_multiplying_wood",()=>{
            var m=RodModel();
            for(int id=0;id<3;id++)
            {
                var s=m.Capture();var p=s.Players.ToArray();var wood=s.Driftwood.ToArray();
                p[0].Phase=HookPhase.Returning;p[0].DriftwoodId=id;p[0].HookX=p[0].X;p[0].HookZ=p[0].Z;p[0].Combo.PendingCast=true;
                wood[id].Phase=DriftwoodPhase.Hooked;wood[id].Owner=0;wood[id].X=p[0].X;wood[id].Z=p[0].Z;
                m.Restore(new HookState(s.Steps,p,s.Fish,s.Coins,s.Tiles,wood,s.Tackle));ShopStep(m);
                Require(m.Capture().Players[0].Combo.Count==id+1,"Wood failed to advance once.");
            }
            Require(m.Capture().Players[0].WoodEarned==20,"Wood reward multiplied by combo.");
            LandComboFish(m);Require(m.Capture().Players[0].Combo.Count==4 && m.Capture().Coins==23,"Mixed wood and fish did not share one streak.");
        });
        yield return ("combo_small_and_rare_chests_increment_but_do_not_gain_bonus",()=>{
            foreach(var kind in new[]{TreasureKind.Small,TreasureKind.Rare})
            {
                var m=TreasureModel(kind);var s=m.Capture();var p=s.Players.ToArray();p[0].Combo.Count=9;TreasureRestore(m,p);
                int baseGold=s.Treasure.Chests[0].Coins;TreasureCast(m,0);
                for(int i=0;i<180 && m.Capture().Players[0].TreasuresCollected==0;i++)ShopStep(m);
                Require(m.Capture().Players[0].Combo.Count==10 && m.Capture().Coins==baseGold,"Chest bonus/count differs from design.");
                TreasureCheckpoint(m);for(int i=0;i<30;i++)ShopStep(m);
                Require(m.Capture().Players[0].Combo.Count==10,"Chest return/reset replayed outcome.");
            }
        });
        yield return ("combo_large_chest_landing_receipt_survives_restore_and_later_break",()=>{
            var m=TreasureModel(TreasureKind.Large,3);var p=m.Capture().Players.ToArray();
            p[0].Combo.Count=2;p[1].Combo.Count=9;p[2].Combo.Count=6;TreasureRestore(m,p);
            TreasureCast(m,0);TreasureCast(m,1);var chest=TreasureLand(m);var s=m.Capture();
            Require(s.Players[0].Combo.Count==3 && s.Players[1].Combo.Count==10 && s.Players[2].Combo.Count==6 && s.Coins==0,"Large chest should count on landing for final pullers only.");
            TreasureCheckpoint(m);p=m.Capture().Players.ToArray();p[0].Combo=default;p[2].X=chest.X;p[2].Z=chest.Z;TreasureRestore(m,p);
            TreasureOpen(m,2,chest,1);TreasureOpen(m,2,chest,1);s=m.Capture();
            Require(s.Coins==chest.Coins && s.Players[0].Combo.Count==0 && s.Players[1].Combo.Count==10 && s.Players[2].Combo.Count==6,"Opening re-awarded combo or restored a broken streak.");
        });
        yield return ("combo_water_entry_resets_once_and_offline_resets_idle_streak",()=>{
            var m=CoopModel(1);LandComboFish(m);CoopEdit(m,p=>{p[0].Phase=HookPhase.Ready;p[0].Combo.Count=8;p[0].X=-2790;p[0].Z=-1800;});
            ShopStep(m,new HookInput(-1000,0).Encode());
            Require(m.Capture().Players[0].Swimming.InWater && m.Capture().Players[0].Combo.Count==0,"Ring-protected water entry did not reset.");
            CoopEdit(m,p=>p[0].Combo.Count=3);for(int i=0;i<10;i++)ShopStep(m);
            Require(m.Capture().Players[0].Combo.Count==3,"Continuous water state repeatedly reset new streak.");
            var s=m.Capture();m.Step(new FrameContext(s.Steps+1,s.Steps+1,30,new[]{new MemberEvent(s.Steps+1,MemberEventKind.Deactivate,0,1)}),Array.Empty<SimulationInput>());
            Require(m.Capture().Players[0].Combo.Count==0,"Idle offline player retained combo.");
            s=m.Capture();m.Step(new FrameContext(s.Steps+1,s.Steps+1,30,new[]{new MemberEvent(s.Steps+1,MemberEventKind.Activate,0,2)}),Array.Empty<SimulationInput>());
            Require(m.Capture().Players[0].Combo.Count==0,"Rejoin resurrected combo.");
        });
        yield return ("combo_downing_in_water_clears_existing_streak_without_waiting_for_death",()=>{
            var m=RodModel(count:2);CoopEdit(m,p=>{p[0].X=-3400;p[0].Z=-1800;p[0].Swimming.InWater=true;p[0].Vitals.Health=1;p[0].Combo.Count=8;});
            for(int i=0;i<60 && !m.Capture().Players[0].Vitals.Downed;i++)ShopStep(m);
            Require(m.Capture().Players[0].Vitals.Downed && !m.Capture().Players[0].Revival.Dead && m.Capture().Players[0].Combo.Count==0,"Downing retained combo until death.");
        });
        yield return ("combo_configuration_and_checkpoint_preserve_settled_and_pending_outcomes",()=>{
            var c=new ComboConfig(goldBonusPercent:new[]{0,0,25,60});var m=RodModel(config:new FishingConfig(combo:c));
            LandComboFish(m);LandComboFish(m);LandComboFish(m);Require(m.Capture().Coins==65,"Custom reward curve ignored.");
            var codec=new HookGame();var bytes=codec.Encode(m.Capture());m.Restore(codec.Decode(bytes));for(int i=0;i<30;i++)ShopStep(m);
            Require(m.Capture().Coins==65 && m.Capture().Players[0].Combo.Count==3,"Restore repeated reward or empty return.");
            var p=m.Capture().Players[0];ShopStep(m,new HookInput(0,0,p.X+2000,p.Z+2000,true).Encode());
            bytes=codec.Encode(m.Capture());m.Restore(codec.Decode(bytes));for(int i=0;i<130;i++)ShopStep(m);
            Require(m.Capture().Players[0].Combo.Count==0,"Restore forgot pending empty cast.");
            var basis=IslandMap.CreatePlatform();var custom=new IslandMap(basis.Shore,basis.Pier,basis.Spawns,basis.FishSpots,basis.Platform,new FishingConfig(combo:c));
            Require(IslandMap.Decode(custom.Encode()).Fishing!.Combo.BonusPercent(100)==60 && !basis.Encode().SequenceEqual(custom.Encode()),"Combo configuration missing from map identity.");
            var disabled=RodModel(config:new FishingConfig(combo:new ComboConfig(false)));for(int i=0;i<4;i++)LandComboFish(disabled);
            Require(disabled.Capture().Coins==80 && disabled.Capture().Players[0].Combo.Count==0,"Disabled combo still rewarded/counts.");
            bytes=codec.Encode(m.Capture());bytes[bytes.Length-1]=2;bool rejected=false;try{codec.Decode(bytes);}catch(System.IO.InvalidDataException){rejected=true;}
            Require(rejected,"Invalid pending flag accepted.");
        });
    }
}
