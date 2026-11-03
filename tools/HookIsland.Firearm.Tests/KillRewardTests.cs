using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.FirearmTests;
internal static partial class Program
{
    private static IEnumerable<(string,Action)> KillRewardCases()
    {
        yield return ("piranha_simultaneous_shots_pay_only_the_killer",()=>{
            var config=new EnemyConfig(piranhaCount:1,monsterEnabled:false,patrolSpeed:30);
            var model=Model(config);var state=model.Capture();
            var gun=state.Tackle!.Inventory.Items.Single(i=>i.Owner==1 && i.Kind==ItemKind.Firearm);
            Step(model,default,new HookInput(0,0,inventory:new InventoryCommand(InventoryAction.Equip,gun.Id,requestId:1)));Wait(model,9);
            state=model.Capture();var fish=state.Enemies.Piranhas.ToArray();fish[0].X=0;fish[0].Z=-1400;
            model.Restore(Replace(state,enemies:new EnemyState(fish,state.Enemies.Monster,state.Enemies.Waterballs)));
            var events=Step(model,Shoot(true),new HookInput(0,0,-5000,-1400,true));var dead=model.Capture();int gold=PiranhaFishing.Coins(config,fish[0]);
            Require(dead.Players[0].Coins==4700 && dead.Players[1].Coins==5000+gold,"First attacker was paid instead of the killer");
            Require(dead.Players.All(p=>p.Vitals.Health==100) && dead.Enemies.Piranhas[0].RespawnTicks>0,"Bullet target or death incorrect");
            Require(events.Count(e=>e.Kind==HookGameplayEventKind.CoinsReceived)==1 && events.Any(e=>e.Kind==HookGameplayEventKind.CoinsReceived && e.Slot==1 && e.Amount==gold),"Kill paid twice or used wrong event owner");
            model.Restore(new HookGame().Decode(new HookGame().Encode(dead)));Wait(model,30);
            Require(model.Capture().Coins==dead.Coins,"Restoring credited kill replayed income");
        });
        yield return ("piranha_pending_reward_survives_overflow_and_restore_without_respawn",()=>{
            var config=new EnemyConfig(piranhaCount:1,monsterEnabled:false,patrolSpeed:30,piranhaRespawnTicks:30);
            var model=Model(config);var state=model.Capture();var people=state.Players.ToArray();
            people[0].Coins=int.MaxValue-300;people[0].CoinsEarned=int.MaxValue;people[1].Coins=people[1].CoinsEarned=300;
            people[1].X=people[1].HookX=2000;people[1].Z=people[1].HookZ=2400;
            var fish=state.Enemies.Piranhas.ToArray();fish[0].X=0;fish[0].Z=-1400;
            var enemies=new EnemyState(fish,state.Enemies.Monster,state.Enemies.Waterballs);
            model.Restore(new HookState(state.Steps,people,state.Fish,int.MaxValue,state.Tiles,state.Driftwood,state.Tackle,enemies,state.RoomRules,state.Treasure,state.Commissions,state.Ocean,state.Finale));
            Step(model,Shoot(true));Wait(model,18);var events=Step(model,Shoot(true));state=model.Capture();
            Require(state.Enemies.Piranhas[0].RewardOwner==1 && state.Enemies.Piranhas[0].RespawnTicks==30 && state.Players[0].Coins==int.MaxValue-300,"Overflow lost pending kill reward or changed wallet");
            Require(!events.Any(e=>e.Kind==HookGameplayEventKind.CoinsReceived),"Uncommitted reward emitted feedback");
            model.Restore(new HookGame().Decode(new HookGame().Encode(state)));Wait(model,60);
            Require(model.Capture().Enemies.Piranhas[0].RewardOwner==1 && model.Capture().Enemies.Piranhas[0].RespawnTicks==30,"Pending kill respawned without settling");
            state=model.Capture();fish=state.Enemies.Piranhas.ToArray();fish[0].RewardOwner=3;
            Reject(()=>model.Restore(Replace(state,enemies:new EnemyState(fish,state.Enemies.Monster,state.Enemies.Waterballs))));
        });
    }
}
