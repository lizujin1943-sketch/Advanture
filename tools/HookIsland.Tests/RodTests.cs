using System;
using System.Collections.Generic;
using System.Linq;
using FrameSync.Abstractions;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.Tests;
internal static partial class Program
{
    // Legacy fixtures keep their fixed-value fish; new equipment scenarios below use the real platform defaults.
    private static IslandMap LegacyPlatformMap(int wood=4,int bone=4)
    {var m=IslandMap.CreatePlatform(wood,bone);return new IslandMap(m.Shore,m.Pier,m.Spawns,m.FishSpots,m.Platform);}
    private static IDeterministicSimulation<HookState> RodModel(int coins=0,int count=1,FishingConfig? config=null)
    {
        var m=IslandMap.CreatePlatform();if(config!=null)m=new IslandMap(m.Shore,m.Pier,m.Spawns,m.FishSpots,m.Platform,config);
        var model=PullModel(m,count);var s=model.Capture();Require(s.Tackle!=null,"Missing equipped rods and shared stock.");
        var p=s.Players.ToArray();for(int i=0;i<count;i++){p[i].X=-1400+i*900;p[i].Z=0;}p[0].Coins=p[0].CoinsEarned=coins;
        model.Restore(new HookState(s.Steps,p,s.Fish,coins,s.Tiles,s.Driftwood,s.Tackle));return model;
    }
    private static byte[] RodCommand(RodAction action,int target,uint batch,uint id)=>new HookInput(0,0,rodAction:action,rodTarget:target,rodBatch:batch,rodRequestId:id).Encode();
    private static void SetRodStamina(IDeterministicSimulation<HookState> model,int points,int delay=60)
    {var s=model.Capture();var p=s.Players.ToArray();p[0].Stamina=points*FishingRules.UnitsPerPoint;p[0].RegenDelay=delay;
     model.Restore(new HookState(s.Steps,p,s.Fish,s.Coins,s.Tiles,s.Driftwood,s.Tackle));}
    private static void AttachRodFish(IDeterministicSimulation<HookState> model,int grams=1000,int species=0,int grade=1,int distance=2000)
    {
        var s=model.Capture();var players=s.Players.ToArray();var fish=s.Fish.ToArray();var p=players[0];
        p.Phase=HookPhase.Returning;p.FishId=0;p.HookX=p.X-distance;p.HookZ=p.Z;p.Distance=distance;
        var f=fish[0];f.Phase=FishPhase.Hooked;f.Owner=0;f.X=p.HookX;f.Z=p.HookZ;f.Y=1250;
        f.WeightGrams=grams;f.Species=species;f.Grade=grade;fish[0]=f;players[0]=p;
        model.Restore(new HookState(s.Steps,players,fish,s.Coins,s.Tiles,s.Driftwood,s.Tackle));
    }
    private static IEnumerable<(string,Action)> RodCases()
    {
        yield return ("rod_shop_accepts_current_platform_and_preserves_shopping_on_restore",()=>{
            var m=RodModel(1000);var before=m.Capture();var players=before.Players.ToArray();
            players[0].X=0;players[0].Z=3200;players[0].WoodEarned=4;
            var tiles=before.Tiles.Concat(new[]{new PlatformTile(0,2,TileKind.Wood,0,4)}).ToArray();
            m.Restore(new HookState(before.Steps,players,before.Fish,before.Coins,tiles,before.Driftwood,before.Tackle));
            ShopStep(m,new HookInput(0,0,shopping:true,rodAction:RodAction.BuyAndEquip,rodTarget:0,rodBatch:before.Tackle!.Batch,rodRequestId:1).Encode());
            var after=m.Capture();
            Require(after.Players[0].LastRodResult==RodResult.Success && after.Players[0].Shopping,"Valid expanded platform near merchant was rejected.");
            var codec=new HookGame();m.Restore(codec.Decode(codec.Encode(after)));
            Require(m.Capture().Players[0].Shopping,"Shopping protection was lost on checkpoint restore.");
        });
        yield return ("rod_grid_rejects_overflowing_coordinates",()=>{
            Require(!new GridRect(int.MaxValue,0,3,1).Fits(4,4),"Wrapped X was accepted as a backpack cell.");
            Require(!new GridRect(0,int.MaxValue,1,3).Fits(4,4),"Wrapped Y was accepted as a backpack cell.");
        });
        yield return ("rod_cannot_spend_teammates_coins",()=>{
            var m=RodModel(1000,2);var before=m.Capture();
            ShopStep(m,default(HookInput).Encode(),RodCommand(RodAction.BuyAndEquip,0,before.Tackle!.Batch,1));
            var s=m.Capture();Require(s.Players[1].LastRodResult==RodResult.InsufficientCoins && s.Coins==1000 && s.Tackle!.Items.Count==2,"A player spent a teammate's earnings.");
        });
        yield return ("rod_four_players_earn_buy_and_catch_with_equipped_rods",()=>{
            var map=IslandMap.CreatePlatform();var r=Runner(4,map);var pilot=new Advanture.HookIsland.RodSmokePilot();
            for(int tick=0;tick<9000 && !pilot.Complete(r.InspectState());tick++)
            {var s=r.InspectState();Step(r,s.Players.Select(p=>pilot.Sample(map,s,p.Slot,r.Tick+1)).ToArray());}
            var final=r.InspectState();
            Require(pilot.Complete(final),"Normal input equipment loop incomplete at "+r.Tick+": "+string.Join(";",final.Players.Select(p=>$"P{p.Slot} catches={p.Catches} earned={p.Coins} casts={p.Casts} equipped={p.EquippedRod} stamina={p.Stamina/30000} pos={p.X},{p.Z}")));
            Require(final.Coins==final.Players.Sum(p=>p.CoinsEarned)-final.Tackle!.Items.Sum(i=>RodCatalog.Get(i.Type).Price),"Income/purchases failed wallet conservation.");
            Console.WriteLine("ROD_ACCEPTANCE_TICKS "+r.Tick);
        });
        yield return ("rod_heavier_fish_and_elites_drain_the_shared_pool",()=>{
            var light=RodModel();var heavy=RodModel();AttachRodFish(light,1000);AttachRodFish(heavy,10000,9,3,4000);
            ShopStep(light);ShopStep(heavy);
            Require(light.Capture().Players[0].Stamina==120*30000-1000,"Ordinary fish drain differs from K × kg / 30.");
            Require(heavy.Capture().Players[0].Stamina==120*30000-150000,"Boss fish drain differs from K × kg × 3 / 30.");
            for(int i=0;i<23;i++)ShopStep(heavy);
            var s=heavy.Capture();Require(s.Players[0].Stamina==0 && s.Players[0].Escapes==1 && s.Coins==0 && s.Players[0].Fishbones==0,"Exhausted fish rewarded or failed to escape.");
            Require(s.Fish[0].Owner==-1 && s.Players[0].FishId==-1,"Escape left dangling ownership.");
        });
        yield return ("rod_landing_rewards_actual_weight_and_grade_once",()=>{
            var m=RodModel();AttachRodFish(m,2300,2,2,1000);
            for(int i=0;i<30;i++)ShopStep(m);
            var s=m.Capture();Require(s.Players[0].Catches==1 && s.Coins==40 && s.Players[0].Fishbones==12,"Weight rounding or elite rewards are wrong.");
            for(int i=0;i<30;i++)ShopStep(m);
            Require(m.Capture().Coins==40 && m.Capture().Players[0].Catches==1,"Landing awarded twice.");
        });
        yield return ("rod_final_drain_wins_tie_with_landing_and_cancel_never_rewards",()=>{
            var m=RodModel();SetRodStamina(m,1);AttachRodFish(m,10000,9,3,1);ShopStep(m);
            Require(m.Capture().Coins==0 && m.Capture().Players[0].Escapes==1,"Zero stamina landed fish on the same tick.");
            m=RodModel();AttachRodFish(m);ShopStep(m,new HookInput(0,0,cancel:true).Encode());
            for(int i=0;i<60;i++)ShopStep(m);
            Require(m.Capture().Coins==0 && m.Capture().Players[0].Catches==0 && m.Capture().Fish[0].Owner==-1,"Release awarded a catch.");
        });
        yield return ("rod_checkpoint_preserves_fish_stamina_and_equipment_receipts",()=>{
            var a=RodModel(1000,config:new FishingConfig(restockSeconds:4));var initial=a.Capture();
            ShopStep(a,RodCommand(RodAction.BuyAndEquip,0,initial.Tackle!.Batch,1));AttachRodFish(a,6500,4,2);
            for(int i=0;i<9;i++)ShopStep(a);
            var b=RodModel(config:new FishingConfig(restockSeconds:4));var codec=new HookGame();b.Restore(codec.Decode(codec.Encode(a.Capture())));
            for(int i=0;i<180;i++){ShopStep(a);ShopStep(b);}
            Require(codec.Encode(a.Capture()).SequenceEqual(codec.Encode(b.Capture())),"Equipment, restock or stamina replay diverged.");
            bool rejected=false;try{RodModel().Restore(a.Capture());}catch(System.IO.InvalidDataException){rejected=true;}
            Require(rejected,"Different fishing configuration accepted a checkpoint.");
        });
        yield return ("rod_idle_unequipped_player_cannot_cast_but_recovers_base_stamina",()=>{
            var m=RodModel();SetRodStamina(m,0,0);ShopStep(m,RodCommand(RodAction.Unequip,0,0,1));
            ShopStep(m,new HookInput(0,0,-50000,0,true).Encode());
            Require(m.Capture().Players[0].Casts==0 && m.Capture().Players[0].Stamina==10000,"Unequipped casting or base recovery failed.");
        });
        yield return ("rod_stock_and_starting_equipment_follow_catalog",()=>{
            var s=RodModel().Capture();Require(s.Tackle!.Stock.Count>=4,"Early merchant needs four rods.");
            Require(RodCatalog.All.Count==31 && RodCatalog.All.Skip(1).GroupBy(r=>r.Tier).All(g=>g.Count()==6),"Missing rod tiers/variants.");
            Require(s.Tackle.Stock.Where(i=>i.Kind==ItemKind.Rod).All(i=>RodCatalog.Get(i.Type).Tier==1),"Early merchant leaked higher tier gear.");
            Require(s.Players[0].Stamina==120*30000 && FishingRules.Equipped(s,s.Players[0])!.Id=="rod_starter","Invalid starting stamina or rod.");
        });
        yield return ("rod_rest_waits_two_seconds_then_restores_five_per_second",()=>{
            var m=RodModel();SetRodStamina(m,0);
            for(int i=0;i<60;i++)ShopStep(m);
            Require(m.Capture().Players[0].Stamina==0,"Recovery happened during wait.");
            for(int i=0;i<30;i++)ShopStep(m);
            Require(m.Capture().Players[0].Stamina==5*30000,"Wrong recovery rate.");
            for(int i=0;i<720;i++)ShopStep(m);
            Require(m.Capture().Players[0].Stamina==120*30000,"Recovery did not cap at maximum.");
        });
        yield return ("rod_active_empty_cast_interrupts_rest_without_spending_stamina",()=>{
            var m=RodModel();SetRodStamina(m,50,0);ShopStep(m,new HookInput(0,0,-50000,0,true).Encode());
            for(int i=0;i<10;i++)ShopStep(m);
            var p=m.Capture().Players[0];Require(p.Stamina==50*30000 && p.RegenDelay==60,"Empty hook restored or spent stamina.");
        });
        yield return ("rod_purchase_race_and_duplicate_receipts_charge_once",()=>{
            var m=RodModel(1000,4);var s=m.Capture();int cost=RodCatalog.Get(s.Tackle!.Stock[0].Type).Price;
            var buy=RodCommand(RodAction.BuyAndEquip,0,s.Tackle.Batch,1);ShopStep(m,buy,buy,buy,buy);
            s=m.Capture();Require(s.Coins==1000-cost && s.Tackle!.Items.Count==5,"Contested stock duplicated or overcharged.");
            Require(s.Players[0].LastRodResult==RodResult.Success && s.Players.Skip(1).All(p=>p.LastRodResult==RodResult.SoldOut),"Missing race receipts.");
            for(int i=0;i<5;i++)ShopStep(m,buy,buy,buy,buy);
            Require(m.Capture().Coins==1000-cost && m.Capture().Tackle!.Items.Count==5,"Duplicate request granted another rod.");
        });
        yield return ("rod_swap_clamps_stamina_without_refilling_and_keeps_old_rod",()=>{
            var m=RodModel(1000);SetRodStamina(m,108);var s=m.Capture();var starter=s.Players[0].EquippedRod;
            ShopStep(m,RodCommand(RodAction.BuyAndEquip,0,s.Tackle!.Batch,1));s=m.Capture();
            Require(s.Players[0].Stamina==108*30000 && s.Tackle!.Items.Single(i=>i.Id==starter).X>=0,"Purchase refilled stamina or destroyed starter.");
            ShopStep(m,RodCommand(RodAction.Unequip,0,0,2));
            Require(m.Capture().Players[0].Stamina==100*30000 && m.Capture().Players[0].EquippedRod==0,"Unequip failed to clamp.");
            ShopStep(m,RodCommand(RodAction.Equip,starter,0,3));
            Require(m.Capture().Players[0].Stamina==100*30000,"Re-equipping refilled stamina.");
        });
        yield return ("rod_restock_invalidates_selected_batch_without_charging",()=>{
            var m=RodModel(1000,config:new FishingConfig(restockSeconds:1));var before=m.Capture();
            for(int i=0;i<30;i++)ShopStep(m);
            ShopStep(m,RodCommand(RodAction.Buy,0,before.Tackle!.Batch,1));var s=m.Capture();
            Require(s.Tackle!.Batch==2 && s.Coins==1000 && s.Players[0].LastRodResult==RodResult.StaleStock,"Old selected item bought from new batch.");
        });
        yield return ("rod_full_storage_purchase_is_atomic",()=>{
            var m=RodModel(10000,config:new FishingConfig(restockSeconds:1));uint request=0;bool full=false;
            for(int n=0;n<80 && !full;n++)
            {
                var s=m.Capture();int index=Enumerable.Range(0,s.Tackle!.Stock.Count).FirstOrDefault(i=>!s.Tackle.Stock[i].Sold && s.Tackle.Stock[i].Kind==ItemKind.Rod);
                if(s.Tackle.Stock.Where(i=>i.Kind==ItemKind.Rod).All(i=>i.Sold)){for(int i=0;i<30;i++)ShopStep(m);continue;}
                int coins=s.Coins,count=s.Tackle.Items.Count;ShopStep(m,RodCommand(RodAction.Buy,index,s.Tackle.Batch,++request));s=m.Capture();
                if(s.Players[0].LastRodResult==RodResult.NoSpace){full=true;Require(s.Coins==coins && s.Tackle!.Items.Count==count && !s.Tackle.Stock[index].Sold,"Full storage consumed stock or currency.");}
            }
            Require(full,"Storage has no real grid limit.");
        });
        yield return ("rod_starter_actual_flight_is_four_metres_per_second",()=>{
            var r=Runner(map:IslandMap.CreatePlatform());
            Step(r,new HookInput(0,0,-50000,-1400,true));
            Require(r.InspectState().Players[0].Distance==133,"Starter hook must travel 4 m/s, with fractional millimetres carried between ticks.");
            for(int i=1;i<30;i++)Step(r);
            Require(r.InspectState().Players[0].Distance==4000,"Starter line must reach exactly 4 metres after one second.");
        });
    }
}
