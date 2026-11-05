using System;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;

var cases = new (string, Action)[] {
    ("town_delivery_retries_frozen_intent_until_failure_receipt", () => {
        var b=new TownRequestBuffer(); Check(b.TryQueue(40,TownAction.RepairBand,2,7),"queue repair");
        for(int i=0;i<30;i++) {
            var input=HookInput.Decode(b.Compose(new HookInput(300,400,59000,-41000,fire:true,shopping:true,reload:true,aimVertical:420)).Encode());
            Check(input.Town.RequestId==41 && input.Town.Action==TownAction.RepairBand && input.Town.Target==2 && input.Town.Attempt==7,"intent changed");
            Check(input.MoveX==300 && input.MoveZ==400 && input.AimX==59000 && input.AimZ==-41000 && input.AimVertical==420,"aim/movement lost");
            Check(!input.Fire && !input.Reload && !input.Shopping,"incompatible combat leaked");
        }
        Check(!b.Observe(new TownPersonalState{LastRequest=40,LastResult=TownResult.Success},out _),"stale receipt settled");
        Check(!b.TryQueue(40,TownAction.Heal),"pending click replaced intent");
        Check(b.Observe(new TownPersonalState{LastRequest=41,LastResult=TownResult.Unsafe},out var result) && result==TownResult.Unsafe,"failure did not settle");
        Check(!b.Pending && b.Compose(default).Town.IsEmpty,"failure replayed");
        Check(b.TryQueue(41,TownAction.Heal) && b.Command.RequestId==42,"explicit retry lacks new id");
    }),
    ("town_delivery_defers_existing_transactions_without_losing_intent", () => {
        var b=new TownRequestBuffer(); Check(b.TryQueue(3,TownAction.Heal),"queue heal");
        var inputs=new[]{new HookInput(0,0,build:TileKind.Wood,buildX:2,buildId:1),new HookInput(0,0,bossRequest:1),
            new HookInput(0,0,town:new TownCommand(TownAction.ReadClues,2))};
        foreach(var input in inputs)Check(b.Compose(input).Encode().AsSpan().SequenceEqual(input.Encode()) && b.Pending,"transaction overwritten");
        Check(HookInput.Decode(b.Compose(default).Encode()).Town.RequestId==4,"deferred intention lost");
    }),
    ("town_delivery_rejects_invalid_intents_and_never_wraps", () => {
        var b=new TownRequestBuffer();
        Check(!b.TryQueue(uint.MaxValue,TownAction.Heal) && !b.TryQueue(0,TownAction.None) && !b.TryQueue(0,TownAction.Heal,1) && !b.TryQueue(0,TownAction.RepairBand,4,1),"invalid queue accepted");
        Check(!b.Pending && b.TryQueue(0,TownAction.ReadyBoss),"invalid queue poisoned buffer");
        b.Reset(); Check(!b.Pending && b.Compose(default).Town.IsEmpty,"reset did not clear");
    })
};
int failures=0;
foreach(var (name,test) in cases){try{test();Console.WriteLine("PASS "+name);}catch(Exception e){failures++;Console.WriteLine("FAIL "+name+": "+e.Message);}}
return failures==0?0:1;
static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
