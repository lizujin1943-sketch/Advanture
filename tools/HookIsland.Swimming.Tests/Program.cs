using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;
using FrameSync.Abstractions;

namespace Advanture.HookIsland.SwimmingTests;
internal static partial class Program
{
    private static void Require(bool value,string message){if(!value)throw new Exception(message);}
    private static void Throws<T>(Action a) where T:Exception
    {try{a();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    private static int Main(string[] args)
    {
        int passed=0,total=0;
        foreach(var (name,test) in Cases().Concat(DomainCases()).Concat(GameCases()).Concat(NetworkCases()))
        {
            if(args.Length>0 && !name.Contains(args[0],StringComparison.Ordinal))continue;
            total++;try{test();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){Console.WriteLine("FAIL "+name+": "+e);}
        }
        Console.WriteLine($"{passed}/{total} passed");return total>0 && passed==total?0:1;
    }
    private static IDeterministicSimulation<HookState> Model(int count=1,FishingConfig? config=null)
    {
        var m=IslandMap.CreatePlatform();if(config!=null)m=new IslandMap(m.Shore,m.Pier,m.Spawns,m.FishSpots,m.Platform,config);
        var model=new HookGame().Create();
        model.Initialize(new SimulationInitialization(new SessionDescriptor(new Guid("6e59ebf0-1e01-4399-aab6-43c2cc4bec10"),1,30,
            Enumerable.Range(0,count).Select(i=>new Participant((ushort)i,new Guid(i+1,0,0,new byte[8]))).ToArray()),new ByteString(m.Encode())));
        return model;
    }
    private static void Step(IDeterministicSimulation<HookState> model,params HookInput[] commands)
    {
        var s=model.Capture();
        model.Step(new FrameContext(s.Steps+1,s.Steps+1,30),s.Players.Select(p=>new SimulationInput(
            new Participant(p.Slot,new Guid(p.Slot+1,0,0,new byte[8])),new ByteString((p.Slot<commands.Length?commands[p.Slot]:default).Encode()))).ToArray());
    }
    private static IEnumerable<(string,Action)> Cases()
    {
        yield return ("walking_off_platform_enters_water_and_loses_health",()=>{
            var m=Model();for(int i=0;i<60;i++)Step(m,new HookInput(-1000,0));
            var p=m.Capture().Players[0];Require(p.X< -3000,"Platform edge still clamps the player to land.");
            Require(p.Vitals.Health<100 && p.Vitals.Health>0,"Water did not apply ongoing damage.");
        });
    }
}
