using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.FirearmTests;
internal static partial class Program
{
    private static IEnumerable<(string,Action)> LongGunCases()
    {
        yield return ("long_guns_purchase_capacity_price_stock_and_duplicate_request",()=>{
            for(int type=1;type<FirearmCatalog.All.Count;type++)
            {
                var gun=FirearmCatalog.Get(type);var model=Model(armed:false);
                Step(model,Buy(FirearmCatalog.OfferOffset+type,1,true));var state=model.Capture();
                Require(Gun(model).Type==type && Gun(model).LoadedAmmo==gun.Capacity,"Purchased wrong gun or empty magazine");
                Require(state.Players[0].Coins==5000-gun.Price && state.Tackle!.Merchants[0].FirearmStock[type]==gun.Stock-1,"Wrong price or stock settlement");
                Wait(model,gun.DrawTicks);Step(model,Buy(FirearmCatalog.OfferOffset+type,1,true));
                Require(model.Capture().Tackle!.Inventory.Items.Count(i=>i.Owner==0 && i.Kind==ItemKind.Firearm)==2,"Duplicate request minted another gun");
                Require(model.Capture().Players[0].Coins==5000-gun.Price,"Duplicate purchase charged twice");
                new HookGame().Decode(new HookGame().Encode(model.Capture()));
            }
        });
        yield return ("long_guns_cadence_empty_magazine_and_checkpoint_reload",()=>{
            for(int type=1;type<FirearmCatalog.All.Count;type++)
            {
                var gun=FirearmCatalog.Get(type);var model=Model(armed:false);
                Step(model,Buy(FirearmCatalog.OfferOffset+type,1,true));Wait(model,gun.DrawTicks);
                var ticks=new List<ulong>();
                for(int tick=0;tick<gun.Capacity*gun.CooldownTicks+2;tick++)
                    if(Step(model,Shoot()).Any(e=>e.Kind==HookGameplayEventKind.FirearmShot))ticks.Add(model.Capture().Steps);
                Require(ticks.Count==gun.Capacity && ticks.Skip(1).Select((t,i)=>t-ticks[i]).All(d=>d==(ulong)gun.CooldownTicks),"Cadence or capacity violated");
                Require(Gun(model).LoadedAmmo==0 && !model.Capture().Players[0].Firearm.Reloading,"Empty magazine auto-reloaded");
                Step(model,new HookInput(0,0,reload:true));Wait(model,gun.ReloadTicks-1);
                Require(Gun(model).LoadedAmmo==0,"Reload completed early");
                var restored=Model(armed:false);var codec=new HookGame();restored.Restore(codec.Decode(codec.Encode(model.Capture())));
                var events=Step(model);Step(restored);
                Require(events.Single(e=>e.Kind==HookGameplayEventKind.FirearmReloaded).Amount==gun.Capacity,"Reload amount incorrect");
                Require(codec.Encode(model.Capture()).SequenceEqual(codec.Encode(restored.Capture())),"Restored reload diverged");
                Require(Gun(model).LoadedAmmo==gun.Capacity && model.Capture().Tackle!.Inventory.ReplenishedAmmunition[gun.AmmoType]==gun.Capacity,"Ammo ledger incorrect");
                Wait(model,3);Require(model.Capture().Tackle!.Inventory.ReplenishedAmmunition[gun.AmmoType]==gun.Capacity,"Reload replayed");
            }
        });
        yield return ("sniper_remote_hits_and_range_limits_use_authoritative_ray",()=>{
            for(int type=2;type<4;type++)foreach(bool beyond in new[]{false,true})
            {
                var gun=FirearmCatalog.Get(type);var config=new EnemyConfig(piranhaCount:1,monsterEnabled:false,patrolSpeed:30);
                var model=Model(config,false);Step(model,Buy(FirearmCatalog.OfferOffset+type,1,true));Wait(model,gun.DrawTicks);
                var state=model.Capture();var fish=state.Enemies.Piranhas.ToArray();var shooter=state.Players[0];
                int distance=beyond?gun.Range+1500:gun.Range-5000;
                fish[0].X=shooter.X-distance*3/5;fish[0].Z=shooter.Z-distance*4/5;
                model.Restore(Replace(state,enemies:new EnemyState(fish,state.Enemies.Monster,state.Enemies.Waterballs)));
                var events=Step(model,new HookInput(0,0,shooter.X-36000,shooter.Z-48000,fire:true));
                var shot=events.Single(e=>e.Kind==HookGameplayEventKind.FirearmShot);
                Require(beyond?shot.TargetId==-1:shot.TargetId==-2 && model.Capture().Enemies.Piranhas[0].RespawnTicks>0,"Sniper range/remote hit incorrect");
            }
        });
        yield return ("long_gun_reload_cancel_and_spread_profiles_are_independent",()=>{
            for(int type=1;type<4;type++)
            {
                var gun=FirearmCatalog.Get(type);var model=Model(armed:false);
                Step(model,Buy(FirearmCatalog.OfferOffset+type,1,true));Wait(model,gun.DrawTicks);Step(model,Shoot());Wait(model,gun.RecoveryTicks);
                Step(model,new HookInput(0,0,reload:true));Wait(model,gun.ReloadTicks-1);
                int rod=model.Capture().Tackle!.Inventory.Items.First(i=>i.Owner==0 && i.Kind==ItemKind.Rod).Id;
                Step(model,new HookInput(0,0,inventory:new InventoryCommand(InventoryAction.Equip,rod,requestId:2)));
                Require(Gun(model).LoadedAmmo==gun.Capacity-1 && model.Capture().Tackle!.Inventory.ReplenishedAmmunition[gun.AmmoType]==0,"Last-frame switch refilled cancelled reload");
            }
            var spread=new FirearmState();FirearmSpread.Advance(ref spread,3000,new[]{0,1,2,3});
            Require(spread.Spreads.Select(s=>s.Angle).SequenceEqual(new[]{6000,5000,6500,7000}),"Weapon movement profiles mixed");
            for(int i=0;i<40;i++)FirearmSpread.Advance(ref spread,0,new[]{0,1,2,3});
            Require(spread.Spreads.Select(s=>s.Angle).SequenceEqual(new[]{500,300,50,80}),"Sniper steady precision did not recover");
        });
    }
}
