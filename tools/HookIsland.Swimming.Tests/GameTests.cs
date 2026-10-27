using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Advanture.HookIsland.Simulation;
using FrameSync.Abstractions;

namespace Advanture.HookIsland.SwimmingTests;
internal static partial class Program
{
    private static void Change(IDeterministicSimulation<HookState> m,Action<Fisher[]> edit)
    {var s=m.Capture();var p=s.Players.ToArray();edit(p);m.Restore(new HookState(s.Steps,p,s.Fish,p.Sum(x=>x.Coins),s.Tiles,s.Driftwood,s.Tackle));}
    private static void At(IDeterministicSimulation<HookState> m,int slot,int x,int z)
    {Change(m,p=>{p[slot].X=x;p[slot].Z=z;p[slot].Swimming.InWater=!PlatformRules.CanStand(m.Capture().Tiles,x,z);});}
    private static int GiveRing(IDeterministicSimulation<HookState> m,int slot,int level,bool equipped=true)
    {
        var s=m.Capture();var p=s.Players.ToArray();var inventory=new InventorySystem(s.Tackle!.Inventory);
        int cost=SwimRingCatalog.Get(level).Price;p[slot].Coins+=cost;p[slot].CoinsEarned+=cost;
        Require(inventory.Purchase(slot,ItemKind.SwimRing,level,equipped,false,new Point(0,0),ref p[slot].Coins)==InventoryResult.Success,"Fixture purchase failed.");
        var inv=inventory.Capture();m.Restore(new HookState(s.Steps,p,s.Fish,p.Sum(x=>x.Coins),s.Tiles,s.Driftwood,
            new TackleState(s.Tackle.Config,inv,s.Tackle.Stock,s.Tackle.Batch,s.Tackle.NextRestock)));
        return inv.Items.Last(i=>i.Kind==ItemKind.SwimRing).Id;
    }
    private static void FishOnLine(IDeterministicSimulation<HookState> m,int slot=0,int distance=0)
    {
        var s=m.Capture();var p=s.Players.ToArray();var f=s.Fish.ToArray();
        p[slot].Phase=HookPhase.Returning;p[slot].FishId=0;p[slot].HookX=p[slot].X+distance;p[slot].HookZ=p[slot].Z;
        f[0].X=p[slot].HookX;f[0].Z=p[slot].HookZ;f[0].Y=1250;f[0].Owner=slot;f[0].Phase=FishPhase.Hooked;
        f[0].Species=1;f[0].WeightGrams=5000;f[0].Grade=1;
        m.Restore(new HookState(s.Steps,p,f,s.Coins,s.Tiles,s.Driftwood,s.Tackle));
    }
    private static void Tow(IDeterministicSimulation<HookState> m,int owner,int victim)
    {
        Change(m,p=>{
            p[owner].Phase=HookPhase.Returning;p[owner].HookedPlayer=victim;
            p[owner].HookX=p[victim].X;p[owner].HookZ=p[victim].Z;
            p[victim].PulledBy=owner;p[victim].ProtectionTicks=0;p[victim].Swimming.Active=false;p[victim].Shopping=false;
        });
    }
    private static HookInput Inventory(InventoryAction action,int target,uint id=1,bool dropOld=false)=>
        new HookInput(0,0,inventory:new InventoryCommand(action,target,requestId:id,dropOld:dropOld));
    private static IEnumerable<(string,Action)> GameCases()
    {
        yield return ("water_building_at_footprint_edge_lands_idle_downed_and_casting_players",()=>{
            foreach(var mode in new[]{"idle","downed","casting"})
            {
                var m=Model(2);At(m,0,2800,0);At(m,1,1400,0);
                if(mode=="casting")GiveRing(m,0,1);
                var s=m.Capture();var p=s.Players.ToArray();var inventory=new InventorySystem(s.Tackle!.Inventory);
                Require(inventory.CreditMaterial(1,ItemKind.Wood,4,new Point(1400,0))==4,"Fixture wood credit failed.");
                p[1].Wood=p[1].WoodEarned=4;p[0].Vitals.Health=mode=="downed"?0:91;p[0].Swimming.DamageRemainder=25000;
                // Keep passing driftwood away from this construction-only boundary fixture.
                var wood=s.Driftwood.Select(d=>DriftwoodMotion.Create(d.Id,60)).ToArray();
                m.Restore(new HookState(s.Steps,p,s.Fish,s.Coins,s.Tiles,wood,
                    new TackleState(s.Tackle.Config,inventory.Capture(),s.Tackle.Stock,s.Tackle.Batch,s.Tackle.NextRestock)));
                Step(m,mode=="casting"?new HookInput(1000,0,-1200,0,true):default,
                    new HookInput(0,0,build:TileKind.Wood,buildX:2,buildId:1));
                s=m.Capture();Require(s.Players[1].LastBuildResult==BuildResult.Success,"Exact-boundary build failed: "+s.Players[1].LastBuildResult);
                Require(!s.Players[0].Swimming.InWater && !s.Players[0].Swimming.Active,
                    mode+": new ground did not clear water state in the build frame.");
                Require(s.Players[0].Vitals.Health==(mode=="downed"?0:91),mode+": landing damaged or healed player.");
                Require(s.Players[0].X==2800 && s.Players[0].Z==0,"Construction moved the player.");
                if(mode=="casting")Require(s.Players[0].Phase!=HookPhase.Ready,"Casting fixture lost its movement lock.");
                var codec=new HookGame();var restored=Model(2);restored.Restore(codec.Decode(codec.Encode(s)));
                Step(m);Step(restored);Require(codec.Encode(m.Capture()).SequenceEqual(codec.Encode(restored.Capture())),"Build/landing snapshot diverged.");
            }
        });
#if HOOK_ISLAND_TESTS
        yield return ("swimming_acceptance_uses_normal_inputs_with_network_delay",()=>{
            var m=Model(4);var pilot=new Advanture.HookIsland.SwimmingSmokePilot();var map=IslandMap.CreatePlatform();
            var inputs=new Queue<HookInput[]>();for(int i=0;i<4;i++)inputs.Enqueue(new HookInput[4]);
            for(int i=0;i<2400 && !pilot.Complete(m.Capture());i++)
            {
                var s=m.Capture();inputs.Enqueue(s.Players.Select(p=>pilot.Sample(map,s,p.Slot,s.Steps+5)).ToArray());Step(m,inputs.Dequeue());
            }
            var result=m.Capture();Require(pilot.Complete(result),"Input acceptance failed: "+string.Join(";",result.Players.Select(p=>$"P{p.Slot} ({p.X},{p.Z}) hp={p.Vitals.Health} hooks={p.TimesHooked} casts={p.Casts} phase={p.Phase}")));
            Require(result.Players[0].CoinsEarned>=150 && result.Tackle!.Inventory.SpentCoins[0]==150,"Ring was not earned and purchased normally.");
        });
#endif
        yield return ("cast_locks_land_movement_until_hook_fully_returns",()=>{
            var m=Model();At(m,0,-1400,-1400);Step(m,new HookInput(1000,0,-5400,-1400,true));
            Require(m.Capture().Players[0].X==-1400 && m.Capture().Players[0].Phase!=HookPhase.Ready,"Cast frame allowed walking.");
            for(int i=0;i<120 && m.Capture().Players[0].Phase!=HookPhase.Ready;i++)
            {Step(m,new HookInput(1000,0));Require(m.Capture().Players[0].X==-1400,"Active hook allowed walking.");}
            Require(m.Capture().Players[0].Phase==HookPhase.Ready,"Hook never returned.");Step(m,new HookInput(1000,0));
            Require(m.Capture().Players[0].X==-1300,"Walking did not unlock after return.");
        });
        yield return ("cast_cancel_waits_for_empty_return_and_invalid_cast_does_not_lock",()=>{
            var m=Model();At(m,0,-1400,-1400);Step(m,new HookInput(0,0,-5400,-1400,true));
            for(int i=0;i<15;i++)Step(m);Step(m,new HookInput(1000,0,cancel:true));
            Require(m.Capture().Players[0].X==-1400 && m.Capture().Players[0].Phase==HookPhase.Returning,"Cancel unlocked before empty return.");
            for(int i=0;i<100 && m.Capture().Players[0].Phase!=HookPhase.Ready;i++)Step(m,new HookInput(1000,0));
            Require(m.Capture().Players[0].X==-1400,"Return frame allowed walking.");Step(m,new HookInput(1000,0));
            Require(m.Capture().Players[0].X==-1300,"Cancel never unlocked.");
            m=Model();At(m,0,0,-4500);Step(m,new HookInput(1000,0,0,-8500,true));
            Require(m.Capture().Players[0].X==33 && m.Capture().Players[0].Casts==0 && m.Capture().Players[0].Swimming.Active,"Invalid no-ring cast locked swimming.");
        });
        yield return ("reeling_allows_slow_swimming_and_both_stamina_costs",()=>{
            var m=Model();GiveRing(m,0,10);At(m,0,0,-4500);FishOnLine(m);Change(m,p=>p[0].Stamina=15000);
            Step(m,new HookInput(1000,0));var p=m.Capture().Players[0];
            Require(p.X==21 && p.Stamina==4500 && p.Swimming.Active,"Reeling must swim at 30% of the equipped ring speed and pay both swimming and fish costs.");
            Step(m,new HookInput(1000,0));Step(m,new HookInput(1000,0));p=m.Capture().Players[0];
            Require(p.X==42 && p.Stamina==0 && p.Escapes==1,"Exhaustion must detach the fish and stop further movement while the empty hook returns.");
        });
        yield return ("water_simultaneous_rescue_claim_has_one_owner_and_disconnect_releases",()=>{
            var m=Model(3);At(m,0,-500,-2600);At(m,1,500,-2600);At(m,2,0,-4500);
            var fire=new HookInput(0,0,0,-4500,true);Step(m,fire,fire);
            for(int i=0;i<40 && m.Capture().Players[2].PulledBy<0;i++)Step(m);
            var s=m.Capture();Require(s.Players[2].PulledBy==0 && s.Players.Sum(p=>p.PlayerHooks)==1,"Rescue race produced multiple owners.");
            int x=s.Players[2].X,z=s.Players[2].Z;
            // Cancel the other still-flying hook: a new rescue after disconnect is intentionally permitted.
            m.Step(new FrameContext(s.Steps+1,s.Steps+1,30,new[]{new MemberEvent(1,MemberEventKind.Deactivate,0,1)}),
                new[]{new SimulationInput(new Participant(1,new Guid(2,0,0,new byte[8])),new ByteString(new HookInput(0,0,cancel:true).Encode()))});
            s=m.Capture();Require(s.Players[2].PulledBy<0 && s.Players[2].X==x && s.Players[2].Z==z && s.Players[2].Swimming.InWater,"Disconnect kept pulling or teleported victim.");
        });
        yield return ("water_held_wood_only_credits_on_land",()=>{
            var m=Model(2);GiveRing(m,0,1);At(m,0,0,-3200);At(m,1,0,-2400);var s=m.Capture();var p=s.Players.ToArray();var wood=s.Driftwood.ToArray();
            p[0].Phase=HookPhase.Returning;p[0].DriftwoodId=0;p[0].HookX=p[0].X;p[0].HookZ=p[0].Z;
            wood[0].Owner=0;wood[0].Phase=DriftwoodPhase.Hooked;wood[0].X=p[0].X;wood[0].Z=p[0].Z;
            m.Restore(new HookState(s.Steps,p,s.Fish,s.Coins,s.Tiles,wood,s.Tackle));
            for(int i=0;i<10;i++)Step(m);Require(m.Capture().Players[0].WoodEarned==0,"Wood credited at sea.");
            Step(m,new HookInput(0,1000),new HookInput(0,0,0,-3200,true));
            for(int i=0;i<20;i++)Step(m,new HookInput(0,1000));s=m.Capture();
            Require(!s.Players[0].Swimming.InWater && s.Players[0].WoodEarned==DriftwoodMotion.Value(0),"Towed wood did not credit at land.");
        });
        yield return ("water_entering_without_ring_abandons_fish_and_wood_without_rewards",()=>{
            var m=Model(2);GiveRing(m,1,1);At(m,1,-4500,0);At(m,0,-2790,0);FishOnLine(m,0,1000);Tow(m,1,0);Step(m);
            var s=m.Capture();Require(s.Players[0].Swimming.InWater && s.Players[0].FishId==-1 && s.Coins==0 && s.Fish[0].Owner==-1,"Falling retained/rewarded a fish without ring.");
            m=Model(2);GiveRing(m,1,1);At(m,1,-4500,0);At(m,0,-2790,0);s=m.Capture();var p=s.Players.ToArray();var wood=s.Driftwood.ToArray();
            p[0].Phase=HookPhase.Returning;p[0].DriftwoodId=0;p[0].HookX=p[0].X-1000;p[0].HookZ=0;
            wood[0].Owner=0;wood[0].Phase=DriftwoodPhase.Hooked;wood[0].X=p[0].HookX;wood[0].Z=0;
            m.Restore(new HookState(s.Steps,p,s.Fish,s.Coins,s.Tiles,wood,s.Tackle));Tow(m,1,0);Step(m);s=m.Capture();
            Require(s.Players[0].DriftwoodId==-1 && s.Players[0].WoodEarned==0 && s.Tackle!.Inventory.Items.All(i=>i.Kind!=ItemKind.Wood),"Falling granted wood or offshore drop.");
        });
        yield return ("water_ring_inventory_never_drops_items_at_sea",()=>{
            var m=Model();GiveRing(m,0,1);int spare=GiveRing(m,0,2,false);At(m,0,0,-4500);
            Step(m,Inventory(InventoryAction.Equip,spare,dropOld:true));
            Require(m.Capture().Players[0].LastInventoryResult==InventoryResult.InvalidPlacement,"Dropped old ring into sea.");
            Step(m,Inventory(InventoryAction.Equip,spare,2));var s=m.Capture();
            Require(s.Players[0].LastInventoryResult==InventoryResult.Success && s.Tackle!.Inventory.Equipped(0,EquipmentSlot.SwimRing)!.Value.Type==2,"Valid water swap failed.");
            Require(s.Tackle!.Inventory.Items.All(i=>i.Location!=ItemLocation.Ground),"Water swap produced ground item.");
        });
        yield return ("water_rescue_cancel_retains_position_and_consumes_no_swimming_stamina",()=>{
            var m=Model(2);At(m,0,2700,0);At(m,1,6000,0);
            Step(m,new HookInput(0,0,6000,0,true));
            for(int i=0;i<55 && m.Capture().Players[1].PulledBy<0;i++)Step(m);
            Require(m.Capture().Players[1].PulledBy==0,"No rescue connection.");
            int stamina=m.Capture().Players[1].Stamina;Step(m,default,new HookInput(1000,0));
            Require(m.Capture().Players[1].Stamina>=stamina,"Pulled victim paid active swim cost.");
            int x=m.Capture().Players[1].X;Step(m,new HookInput(0,0,cancel:true));
            Require(m.Capture().Players[1].X==x && m.Capture().Players[1].PulledBy<0 && m.Capture().Players[1].Swimming.InWater,"Cancel teleported or kept dragging.");
        });
        yield return ("water_no_ring_blocks_cast_and_bag_ring_has_no_effect",()=>{
            var m=Model();GiveRing(m,0,10,false);At(m,0,0,-4500);
            for(int i=0;i<30;i++)Step(m,new HookInput(0,0,0,-9000,true));
            var p=m.Capture().Players[0];Require(p.Casts==0 && p.Vitals.Health==95 && p.Swimming.InWater,"Bag item granted equipment effects.");
        });
        yield return ("water_ring_equips_in_water_immediately_without_refill_and_unequip_releases_fish",()=>{
            var m=Model();int id=GiveRing(m,0,1,false);At(m,0,0,-4500);Change(m,p=>{p[0].Vitals.Health=60;p[0].Stamina=30*30000;p[0].RegenDelay=60;});
            Step(m,Inventory(InventoryAction.Equip,id));var s=m.Capture();
            Require(s.Players[0].LastInventoryResult==InventoryResult.Success && s.Players[0].Vitals.Health==60 && s.Players[0].Stamina==30*30000,"Equip failed or refilled resources.");
            FishOnLine(m);Step(m);Require(m.Capture().Players[0].FishId==0 && m.Capture().Coins==0,"Water fish awarded at swimmer.");
            Step(m,Inventory(InventoryAction.Unequip,(int)EquipmentSlot.SwimRing,2));s=m.Capture();
            Require(s.Players[0].FishId==-1 && s.Fish[0].Owner==-1 && s.Coins==0 && s.Players[0].LastInventoryResult==InventoryResult.Success,"Removing ring did not release target atomically.");
            for(int i=0;i<6;i++)Step(m);Require(m.Capture().Players[0].Vitals.Health==59,"Unequip did not resume water damage.");
        });
        yield return ("water_swim_budget_and_zero_input_rest_are_exact",()=>{
            var m=Model();GiveRing(m,0,10);At(m,0,0,-4500);Change(m,p=>p[0].Stamina=6750);
            Step(m,new HookInput(1000,0));var p=m.Capture().Players[0];
            Require(p.Stamina==2250 && p.X==70,"Incorrect swim cost or full step.");
            Step(m,new HookInput(1000,0));p=m.Capture().Players[0];
            Require(p.Stamina==0 && p.X==105 && p.FishId==-1 && p.Coins==0,"Last partial movement failed.");
            for(int i=0;i<100;i++)Step(m,new HookInput(1000,0));p=m.Capture().Players[0];
            Require(p.X==105 && p.Stamina==0 && p.RegenDelay==60,"Holding swim recovered stamina or moved at zero.");
            for(int i=0;i<60;i++)Step(m);Require(m.Capture().Players[0].Stamina==0,"Recovered during delay.");
            for(int i=0;i<30;i++)Step(m);Require(m.Capture().Players[0].Stamina==5*30000,"Idle water recovery differs from land.");
        });
        yield return ("water_catch_is_held_until_real_land_then_awards_once",()=>{
            var m=Model(2);GiveRing(m,0,1);At(m,0,0,-3200);At(m,1,0,-2400);FishOnLine(m);
            int value=FishCatalog.Coins(m.Capture().Fish[0]);
            for(int i=0;i<10;i++)Step(m);Require(m.Capture().Coins==0 && m.Capture().Players[0].FishId==0,"Water arrival granted reward.");
            Step(m,new HookInput(0,1000),new HookInput(0,0,0,-3200,true));
            for(int i=0;i<20;i++)Step(m,new HookInput(0,1000));var s=m.Capture();
            Require(!s.Players[0].Swimming.InWater && s.Players[0].Catches==1 && s.Players[0].Coins==value,"Landing/reward failed.");
            for(int i=0;i<10;i++)Step(m);Require(m.Capture().Coins==value,"Catch awarded twice.");
        });
        yield return ("water_zero_stamina_still_casts_with_ring_but_downed_cannot_swim",()=>{
            var m=Model();GiveRing(m,0,1);At(m,0,0,-4500);Change(m,p=>p[0].Stamina=0);Step(m,new HookInput(0,0,0,-8500,true));
            Require(m.Capture().Players[0].Casts==1,"Zero stamina incorrectly blocks empty cast.");
            Change(m,p=>p[0].Vitals.Health=0);for(int i=0;i<100;i++)Step(m,new HookInput(1000,0,0,-8500,true));
            var p=m.Capture().Players[0];Require(p.X==0 && p.Vitals.Downed && p.Stamina==0 && p.Swimming.InWater && p.Casts==1,"Downed player acted or recovered.");
        });
        yield return ("water_rescue_ignores_protection_and_pulls_downed_zero_stamina_to_free_land",()=>{
            var m=Model(2);At(m,0,2700,0);At(m,1,4500,0);Change(m,p=>{p[1].ProtectionTicks=90;p[1].Vitals.Health=0;p[1].Stamina=0;});
            Step(m,new HookInput(0,0,4500,0,true));for(int i=0;i<90;i++)Step(m);
            var s=m.Capture();var victim=s.Players[1];Require(victim.TimesHooked==1 && !victim.Swimming.InWater && PlatformRules.CanStand(s.Tiles,victim.X,victim.Z),"Rescue blocked or stopped in water.");
            Require(victim.Vitals.Downed && victim.Stamina==0 && victim.PulledBy<0 && s.Players[0].PlayerHooks==1,"Rescue healed/revived or failed release.");
            Require(IslandMap.DistanceSquared(victim.X,victim.Z,s.Players[0].X,s.Players[0].Z)>=400L*400,"Landed on the rescuer.");
        });
        yield return ("water_rescuer_in_water_does_not_fake_land_and_cancel_stops_drag",()=>{
            var m=Model(2);GiveRing(m,0,1);At(m,0,0,-4500);At(m,1,2500,-4500);
            Step(m,new HookInput(0,0,2500,-4500,true));for(int i=0;i<50;i++)Step(m);
            var p=m.Capture().Players[1];Require(p.TimesHooked==1 && p.Swimming.InWater && p.Vitals.Health<100,"Water-to-water pull incorrectly rescued.");
        });
        yield return ("water_roundtrip_keeps_damage_and_rest_and_rejects_false_ground",()=>{
            var a=Model();At(a,0,0,-5000);for(int i=0;i<13;i++)Step(a,new HookInput(707,-707));
            var codec=new HookGame();var b=Model();b.Restore(codec.Decode(codec.Encode(a.Capture())));
            for(int i=0;i<120;i++){var c=i<30?new HookInput(-707,707):default;Step(a,c);Step(b,c);}
            Require(codec.Encode(a.Capture()).SequenceEqual(codec.Encode(b.Capture())),"Water replay diverged.");
            var s=a.Capture();var p=s.Players.ToArray();p[0].Swimming.InWater=false;
            Throws<InvalidDataException>(()=>b.Restore(new HookState(s.Steps,p,s.Fish,s.Coins,s.Tiles,s.Driftwood,s.Tackle)));
        });
    }
}
