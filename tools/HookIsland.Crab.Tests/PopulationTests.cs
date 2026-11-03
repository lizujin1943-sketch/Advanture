using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.CrabTests;
internal static partial class Program
{
    private static HookState Population(HookState s,IEnumerable<CrabState> crabs)=>new(s.Steps,s.Players,s.Fish,s.Coins,s.Tiles,s.Driftwood,s.Tackle,s.Enemies,s.RoomRules,s.Treasure,s.Commissions,s.Ocean,s.Finale,crabs);
    private static IEnumerable<(string,Action)> PopulationCases()
    {
        yield return ("population_stagger_distinct_shores_and_restore_every_phase",()=>{
            var config=new CrabConfig(initialDelayTicks:30,count:3,spawnStaggerTicks:30,windupTicks:300,damage:1);
            var model=Model(4,config);var twin=Model(4,config);var shores=new List<(int,int)>();
            for(int tick=1;tick<=180;tick++)
            {
                twin.Restore(new HookGame().Decode(new HookGame().Encode(model.Capture())));
                Step(model);Step(twin);Same(model.Capture(),twin.Capture());
                var state=model.Capture();Require(state.Crabs.Count==3,"Population changed");
                if(tick%30==0 && tick<=90)
                {
                    int slot=tick/30-1;var c=state.Crabs[slot];
                    Require(c.Phase==CrabPhase.Climbing && c.Health==100,"Staggered spawn did not occur on its tick");
                    shores.Add((c.ShoreX,c.ShoreZ));
                    for(int j=slot+1;j<3;j++)Require(state.Crabs[j].Phase==CrabPhase.Waiting,"Later crab spawned early");
                }
            }
            Require(shores.Distinct().Count()==3,"Crabs spawned at the same shoreline point");
            var s=model.Capture();Reject(()=>model.Restore(Population(s,s.Crabs.Take(2))));
            Reject(()=>new CrabConfig(count:0));Reject(()=>new CrabConfig(count:9));Reject(()=>new CrabConfig(spawnStaggerTicks:-1));
        });
        yield return ("population_independent_rewards_and_receipts_survive_restore",()=>{
            var config=new CrabConfig(initialDelayTicks:300,count:3);var model=Model(4,config);
            var crabs=new[]{Ready(-500,-1400),Ready(500,-1400),Ready(1800,-1400)};
            for(int i=0;i<2;i++){crabs[i].Phase=CrabPhase.Dead;crabs[i].Health=0;crabs[i].Timer=1800;crabs[i].Attackers=(byte)(i==0?1:3);}
            model.Restore(Population(model.Capture(),crabs));var events=Step(model);var s=model.Capture();
            Require(s.Coins==232 && s.Players[0].Coins==166 && s.Players[1].Coins==66,"Population rewards were merged or overwritten");
            Require(s.Players[0].FishbonesEarned==4 && s.Players[1].FishbonesEarned==2,"Crab material receipts lost");
            Require(s.Crabs[0].Rewarded && s.Crabs[1].Rewarded && !s.Crabs[2].Rewarded,"Receipt applied to another crab");
            Require(events.Where(e=>e.Kind==HookGameplayEventKind.CoinsReceived).Sum(e=>e.Amount)==232,"Confirmed coin feedback missing");
            model.Restore(new HookGame().Decode(new HookGame().Encode(s)));
            Require(!Step(model).Any(e=>e.Kind==HookGameplayEventKind.CoinsReceived) && model.Capture().Coins==232,"Restore paid crab rewards twice");
        });
        yield return ("firearm_hits_nearest_crab_and_awards_each_kill_once",()=>{
            var config=new CrabConfig(initialDelayTicks:300,count:3,windupTicks:300);var model=Model(1,config,true);
            var gun=model.Capture().Tackle!.Inventory.Items.Single(i=>i.Kind==ItemKind.Firearm);
            Step(model,new HookInput(0,0,inventory:new InventoryCommand(InventoryAction.Equip,gun.Id,requestId:1)));
            for(int i=0;i<9;i++)Step(model);
            var crabs=new[]{Ready(-400,-1400),Ready(1200,-1400),Ready(2000,-1400)};
            for(int i=0;i<crabs.Length;i++){crabs[i].Phase=CrabPhase.Recovering;crabs[i].Timer=30;}
            model.Restore(Population(model.Capture(),crabs));var events=Step(model,new HookInput(0,0,aimX:5000,aimZ:-1400,fire:true));
            Require(model.Capture().Crabs[0].Health==65 && model.Capture().Crabs.Skip(1).All(c=>c.Health==100),"Bullet hit multiple or wrong crabs");
            Require(events.Any(e=>e.Kind==HookGameplayEventKind.FirearmShot && e.TargetId==-6 && e.Value==0 && e.Amount==35),"Crab bullet feedback has wrong identity");
            for(int i=0;i<60 && !model.Capture().Crabs[0].Rewarded;i++)
                events=Step(model,new HookInput(0,0,aimX:5000,aimZ:-1400,fire:true));
            var state=model.Capture();Require(state.Crabs[0].Rewarded && state.Players[0].Coins==100,"Gun kill did not award crab coins");
            Require(state.Crabs.Skip(1).All(c=>c.Health==100) && state.Players[0].Firearm.ShotSequence==3,"Gun pierced a live crab");
            Require(events.Count(e=>e.Kind==HookGameplayEventKind.CoinsReceived && e.Amount==100)==1,"Missing or duplicate kill feedback");
            model.Restore(new HookGame().Decode(new HookGame().Encode(state)));
            for(int i=0;i<20;i++)Step(model);
            Require(model.Capture().Coins==100,"Loaded kill paid twice");
        });
    }
}
