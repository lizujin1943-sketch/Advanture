using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;
using FrameSync.Abstractions;

namespace Advanture.HookIsland.Tests;
internal static partial class Program
{
    private static IslandMap ShopMap()
    {
        var map=TestMap();
        return new IslandMap(map.Shore,map.Pier,Enumerable.Repeat(new Point(0,-2900),4),new[]{new Point(0,-10500)});
    }
    private static IDeterministicSimulation<HookState> FundedShop(int coins,int count=1)
    {
        var model=new HookGame().Create();
        var session=new SessionDescriptor(Id,1,30,Enumerable.Range(0,count)
            .Select(i=>new Participant((ushort)i,new Guid(i+1,0,0,new byte[8]))).ToArray());
        model.Initialize(new SimulationInitialization(session,new ByteString(ShopMap().Encode())));
        var initial=model.Capture();var players=initial.Players.ToArray();players[0].Coins=coins;
        model.Restore(new HookState(0,players,initial.Fish,coins));
        return model;
    }
    private static byte[] ShopCommand(byte kind,uint request)
    {
        // New wire input: existing movement/aim/flags, followed by item + unique request id.
        var bytes=new byte[HookInput.ByteCount];bytes[13]=kind;BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(14),request);
        return bytes;
    }
    private static void ShopStep(IDeterministicSimulation<HookState> model,params byte[][] commands)
    {
        var state=model.Capture();
        model.Step(new FrameContext(state.Steps+1,state.Steps+1,30),state.Players.Select((p,i)=>
            new SimulationInput(new Participant(p.Slot,new Guid(i+1,0,0,new byte[8])),
                new ByteString(i<commands.Length?commands[i]:default(HookInput).Encode()))).ToArray());
    }
    private static IEnumerable<(string,Action)> ShopCases()
    {
        yield return ("shop_purchase_charges_shared_wallet_only_once",()=>{
            var model=FundedShop(200);
            ShopStep(model,ShopCommand(1,1));
            Require(model.Capture().Coins==150,"Buying the first range upgrade must spend 50 team coins.");
            for(int i=0;i<5;i++)ShopStep(model,ShopCommand(1,1));
            Require(model.Capture().Coins==150,"Repeated purchase id charged the shared wallet again.");
            Require(model.Capture().Players[0].Coins==200,"Spending must preserve lifetime catch contribution.");
        });
        yield return ("shop_simultaneous_buyers_cannot_overspend",()=>{
            var model=FundedShop(50,4);
            var buy=ShopCommand(2,1);ShopStep(model,buy,buy,buy,buy);
            var result=model.Capture();
            Require(result.Coins==0,"Four simultaneous purchases must spend only the available 50 coins.");
            Require(result.Players[0].SpeedLevel==1 && result.Players.Skip(1).All(p=>p.SpeedLevel==0),
                "Only one buyer should receive the upgrade, in stable slot order.");
            Require(result.Players.Skip(1).All(p=>p.LastPurchaseResult==PurchaseResult.InsufficientCoins),
                "Other buyers need an explicit insufficient-coins result.");
        });
        yield return ("shop_remote_request_cannot_buy_from_shore",()=>{
            var model=FundedShop(200);var state=model.Capture();var players=state.Players.ToArray();
            players[0].X=-5000;players[0].Z=-5000;model.Restore(new HookState(0,players,state.Fish,200));
            ShopStep(model,ShopCommand(1,1));
            Require(model.Capture().Coins==200,"Purchases away from the shop must not charge coins.");
            Require(model.Capture().Players[0].LastPurchaseResult==PurchaseResult.TooFar,"Missing distance rejection.");
        });
        yield return ("shop_rejected_request_is_not_retried_after_player_moves",()=>{
            var model=FundedShop(200);var state=model.Capture();var players=state.Players.ToArray();
            players[0].Z=-7000;model.Restore(new HookState(0,players,state.Fish,200));
            ShopStep(model,ShopCommand(1,1));
            state=model.Capture();players=state.Players.ToArray();players[0].Z=-2100;
            model.Restore(new HookState(state.Steps,players,state.Fish,state.Coins));
            ShopStep(model,ShopCommand(1,1));Require(model.Capture().Coins==200,"Stale rejected request was purchased later.");
            ShopStep(model,ShopCommand(1,2));Require(model.Capture().Coins==150,"A new request should succeed at the shop.");
        });
        yield return ("shop_levels_cap_and_insufficient_funds_leave_stats_unchanged",()=>{
            var model=FundedShop(350);
            ShopStep(model,ShopCommand(1,1));ShopStep(model,ShopCommand(1,2));ShopStep(model,ShopCommand(1,3));
            Require(model.Capture().Coins==20 && model.Capture().Players[0].RangeLevel==3,"Three upgrades should spend 330.");
            ShopStep(model,ShopCommand(1,4));
            Require(model.Capture().Coins==20 && model.Capture().Players[0].LastPurchaseResult==PurchaseResult.MaxLevel,"Max level charged coins.");
            ShopStep(model,ShopCommand(2,5));
            var p=model.Capture().Players[0];
            Require(model.Capture().Coins==20 && p.SpeedLevel==0 && p.LastPurchaseResult==PurchaseResult.InsufficientCoins,
                "An unaffordable upgrade changed the wallet or stats.");
        });
        yield return ("shop_purchase_requires_retracted_hook",()=>{
            var model=FundedShop(200);ShopStep(model,new HookInput(0,0,0,-40000,true).Encode());
            ShopStep(model,ShopCommand(1,1));
            Require(model.Capture().Coins==200 && model.Capture().Players[0].LastPurchaseResult==PurchaseResult.HookBusy,
                "An in-flight hook was upgraded.");
        });
        yield return ("shop_upgrades_change_real_hook_speed_and_range",()=>{
            var model=FundedShop(200);
            ShopStep(model,ShopCommand(1,1));ShopStep(model,ShopCommand(2,2));
            ShopStep(model,new HookInput(0,0,0,-40000,true).Encode());
            var p=model.Capture().Players[0];
            Require(p.Distance==400 && p.HookZ==-3300,"Speed upgrade did not change actual flight to 12 m/s.");
            for(int i=0;i<40 && model.Capture().Players[0].Phase==HookPhase.Flying;i++)ShopStep(model);
            p=model.Capture().Players[0];
            Require(p.Distance==12000 && p.HookZ==-14900,"Range upgrade must reach exactly 12 m before returning.");
            Require(model.Capture().Coins==100,"Each first-level upgrade should cost 50.");
        });
        yield return ("shop_snapshot_retains_upgrades_receipts_and_wallet",()=>{
            var a=FundedShop(500);ShopStep(a,ShopCommand(1,1));ShopStep(a,ShopCommand(2,2));
            var codec=new HookGame();var bytes=codec.Encode(a.Capture());
            var b=FundedShop(500);b.Restore(codec.Decode(bytes));
            ShopStep(a,ShopCommand(2,2));ShopStep(b,ShopCommand(2,2));
            Require(a.Capture().Coins==400 && b.Capture().Coins==400,"A restored purchase receipt must prevent recharging.");
            ShopStep(a,ShopCommand(1,3));ShopStep(b,ShopCommand(1,3));
            for(int i=0;i<40;i++)
            {
                var input=new HookInput(0,0,0,-40000,i==0).Encode();ShopStep(a,input);ShopStep(b,input);
            }
            Require(codec.Encode(a.Capture()).SequenceEqual(codec.Encode(b.Capture())),"Restored upgraded hook diverged.");
            var s=a.Capture();bool rejected=false;
            try{codec.Decode(codec.Encode(new HookState(s.Steps,s.Players,s.Fish,s.Coins+50)));}catch(System.IO.InvalidDataException){rejected=true;}
            Require(rejected,"Snapshot granted free upgrade by restoring spent coins.");
        });
        yield return ("shop_input_rejects_old_or_malformed_requests",()=>{
            foreach(var bytes in new[]{new byte[13],ShopCommand(3,1),ShopCommand(0,1),ShopCommand(1,0)})
            {
                bool rejected=false;try{HookInput.Decode(bytes);}catch(System.IO.InvalidDataException){rejected=true;}
                Require(rejected,"Malformed purchase request accepted.");
            }
            var command=new HookInput(0,0,upgrade:HookUpgrade.Speed,purchaseId:42);
            var roundtrip=HookInput.Decode(command.Encode());
            Require(roundtrip.Upgrade==HookUpgrade.Speed && roundtrip.PurchaseId==42,"Purchase input did not round-trip.");
        });
    }
}
