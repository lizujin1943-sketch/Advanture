using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;
using FrameSync.Abstractions;
using FrameSync.Runtime;

namespace Advanture.HookIsland.Tests;
internal static partial class Program
{
    private static readonly Guid Id=Guid.Parse("4bb92556-d270-4dc4-9ba8-e1d22cd00ace");
    internal static IslandMap TestMap()=>new IslandMap(
        new[]{new Point(-10000,-10000),new Point(10000,-10000),new Point(10000,10000),new Point(-10000,10000)},
        new[]{new Point(-1000,-10000),new Point(1000,-10000),new Point(1000,-14000),new Point(-1000,-14000)},
        new[]{new Point(-5000,-5000),new Point(-5000,-5000),new Point(5000,5000),new Point(-5000,5000)},
        new[]{new Point(-5000,-12000)});
    internal static SimulationRunner<HookState> Runner(int count=1,IslandMap? map=null)=>new SimulationRunner<HookState>(
        HookGame.CreateModule(),new SessionDescriptor(Id,1,30,Enumerable.Range(0,count).Select(i=>new Participant((ushort)i,new Guid(i+1,0,0,new byte[8]))).ToArray()),
        new RuntimeLimits(maxParticipants:4,maxInputBytes:HookInput.ByteCount,maxCheckpointBytes:65536),new ByteString((map??TestMap()).Encode()));
    internal static void Step(SimulationRunner<HookState> r,params HookInput[] inputs)=>r.Advance(new InputFrame(Id,1,r.Tick+1,
        Enumerable.Range(0,r.InspectState().Players.Count).Select(i=>new RecordedInput((ushort)i,1,(i<inputs.Length?inputs[i]:default).Encode())).ToArray()));
    internal static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
    private static IEnumerable<(string,Action)> Cases()
    {
        yield return ("movement_stops_at_shore_and_shop",()=>{
            var r=Runner();for(int i=0;i<150;i++)Step(r,new HookInput(-1000,0));
            var p=r.InspectState().Players[0];Require(p.X<-9000 && TestMap().CanStand(p.X,p.Z),"Player did not move to shoreline safely.");
            for(int i=0;i<180;i++)Step(r,new HookInput(707,707));
            p=r.InspectState().Players[0];Require(TestMap().CanStand(p.X,p.Z),"Player crossed shoreline/obstacle.");
        });
        yield return ("fish_lands_then_awards_once_when_two_players_hook",()=>{
            var r=Runner(2);bool attached=false;int landedAt=0;
            for(int i=0;i<210;i++)
            {
                var s=r.InspectState();bool fire=s.Fish[0].Phase==FishPhase.Jumping && !attached && s.Players.All(p=>p.Casts==0);
                var command=new HookInput(0,0,-5000,-12000,fire);
                Step(r,command,command);s=r.InspectState();
                if(s.Fish[0].Phase==FishPhase.Hooked){attached=true;Require(s.Coins==0,"Coins awarded before landing.");}
                if(s.Coins>0 && landedAt==0)landedAt=i;
            }
            var result=r.InspectState();
            Require(attached && landedAt>0,"No complete cast/hook/reel/landing loop.");
            Require(result.Coins==10 && result.Players.Sum(p=>p.Catches)==1,"Duplicate or missing catch reward.");
            Require(result.Players.Sum(p=>p.Coins)==result.Coins,"Team wallet inconsistent.");
        });
        yield return ("miss_and_cancel_return_without_reward",()=>{
            var r=Runner();Step(r,new HookInput(0,0,-25000,-5000,true));
            for(int i=0;i<90;i++)Step(r);
            Require(r.InspectState().Players[0].Phase==HookPhase.Ready && r.InspectState().Coins==0,"Miss did not reset.");
            Step(r,new HookInput(0,0,-25000,-5000,true));Step(r,new HookInput(0,0,cancel:true));
            for(int i=0;i<45;i++)Step(r);
            Require(r.InspectState().Players[0].Phase==HookPhase.Ready && r.InspectState().Coins==0,"Cancel did not reset.");
        });
        yield return ("restore_replays_inflight_fish_and_wallet",()=>{
            var a=Runner();for(int i=0;i<35;i++)Step(a,new HookInput(0,0,-5000,-12000,i==25));
            var checkpoint=a.CaptureCheckpoint();var b=Runner();b.Restore(checkpoint);
            for(int i=0;i<150;i++){var input=new HookInput(333,0,-5000,-12000,i%60==0);Step(a,input);Step(b,input);}
            Require(CheckpointCodec.Hash(a.CaptureCheckpoint())==CheckpointCodec.Hash(b.CaptureCheckpoint()),"Restore diverged.");
        });
        yield return ("invalid_input_and_map_rejected",()=>{
            bool rejected=false;try{new HookInput(1000,1000).Encode();}catch{rejected=true;}Require(rejected,"Diagonal overspeed accepted.");
            var bytes=TestMap().Encode();Require(bytes.SequenceEqual(IslandMap.Decode(bytes).Encode()),"Map encoding changed.");
            rejected=false;try{IslandMap.Decode(bytes.Concat(new byte[]{1}).ToArray());}catch{rejected=true;}Require(rejected,"Trailing map bytes accepted.");
        });
    }
    private static int Main(string[] args)
    {
        int failed=0,total=0;
        var tests=Cases().Concat(ShopCases()).Concat(PlayerHookCases()).Concat(FishDifficultyCases()).Concat(PlatformCases()).Concat(RodCases()).Concat(EscapeCases()).Concat(CooperativeFishingCases()).Concat(CooperativeInputCases()).Concat(RoomRulesCases()).Concat(RoomRulesInputCases()).Concat(TreasureCases()).Concat(TreasureInputCases()).Concat(CommissionCases()).Concat(CommissionInputCases()).Concat(OceanCases()).Concat(OceanInputCases()).Concat(FinaleCases()).Concat(FinaleGameCases()).Concat(FinaleInputCases()).Concat(MerchantCases()).Concat(MerchantGameCases());
        if(!args.Contains("--simulation-only"))tests=tests.Concat(NetworkCases()).Concat(PlatformNetworkCases()).Concat(RodNetworkCases()).Concat(EscapeNetworkCases()).Concat(CooperativeNetworkCases()).Concat(TreasureNetworkCases()).Concat(RoomRulesNetworkCases()).Concat(CommissionNetworkCases()).Concat(OceanNetworkCases()).Concat(FinaleNetworkCases());
        tests=tests.Concat(MerchantInputCases()).Concat(PresenceCases()).Concat(RecoveryProtocolCases()).Concat(ReelingMovementCases());
        if(!args.Contains("--simulation-only"))tests=tests.Concat(ReelingMovementNetworkCases());
        if(!args.Contains("--simulation-only"))tests=tests.Concat(MerchantNetworkCases()).Concat(RecoveryNetworkCases()).Concat(RecoveryLifecycleCases());
        var filter=args.FirstOrDefault(a=>a.StartsWith("--filter="));
        if(filter!=null)tests=tests.Where(t=>t.Item1.Contains(filter.Substring(9),StringComparison.Ordinal));
        foreach(var (name,test) in tests){total++;try{test();Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e.Message);}}
        Console.WriteLine($"{total-failed}/{total} passed");return failed==0 && total>0?0:1;
    }
}
