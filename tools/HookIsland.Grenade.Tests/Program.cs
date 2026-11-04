using System;
using System.IO;
using System.Linq;
using Advanture.HookIsland.Simulation;
using FrameSync.Abstractions;

namespace Advanture.HookIsland.GrenadeTests;

internal static partial class Program
{
    private static readonly HookGame Codec=new(false);
    private static readonly Guid SessionId=new("0a8cffcc-b78a-4218-93c1-812e8bda1194");
    private static IslandMap Map()
    {var b=IslandMap.CreatePlatform();return new(b.Shore,b.Pier,b.Spawns,b.FishSpots,b.Platform,new FishingConfig(enemies:EnemyConfig.Disabled,melee:new MeleeConfig(),firearms:new FirearmConfig(),fishingRodsEnabled:false));}
    private static IDeterministicSimulation<HookState> Model(IslandMap? map=null)
    {var m=Codec.Create();m.Initialize(new SimulationInitialization(new SessionDescriptor(SessionId,1,30,Enumerable.Range(0,4).Select(i=>new Participant((ushort)i,new Guid(i+1,0,0,new byte[8]))).ToArray()),new ByteString((map??Map()).Encode())));return m;}
    private static HookGameplayEvent[] Step(IDeterministicSimulation<HookState> m,params HookInput[] input)
    {var s=m.Capture();return m.Step(new FrameContext(s.Steps+1,s.Steps+1,30),s.Players.Select(p=>new SimulationInput(new Participant(p.Slot,new Guid(p.Slot+1,0,0,new byte[8])),new ByteString((p.Slot<input.Length?input[p.Slot]:default).Encode()))).ToArray()).Select(x=>HookGameplayEventCodec.Decode(x.Span)).ToArray();}
    private static void Wait(IDeterministicSimulation<HookState> m,int n){for(int i=0;i<n;i++)Step(m);}
    private static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
    private static void Reject(Action action){try{action();}catch(InvalidDataException){return;}throw new Exception("Malformed input/state accepted.");}
    private static HookInput Select(uint request=1)=>new(0,0,inventory:new InventoryCommand(InventoryAction.SelectWeapon,(int)EquipmentSlot.Throwable,requestId:request));
    private static HookInput Throw(HookState s,int x,int z,uint request=1,int slot=0)=>new(0,0,grenade:new GrenadeCommand(request,s.Tackle!.Inventory.Equipped(slot,EquipmentSlot.Throwable)!.Value.Id,x,z));
    private static void Test(string name,Action test){test();Console.WriteLine("PASS "+name);}
    private static int Main()
    {
        Test("new_players_have_one_real_grenade_and_fourth_slot",()=>{
            var m=Model();var s=m.Capture();Require(WeaponLoadout.Slots.Count==4,"Missing fourth category");
            foreach(var p in s.Players)Require(s.Tackle!.Inventory.Equipped(p.Slot,EquipmentSlot.Throwable)?.Kind==ItemKind.Throwable,"Missing personal grenade");
            Step(m,Select());Require(m.Capture().Tackle!.Inventory.ActiveItem(0)?.Kind==ItemKind.Throwable,"Cannot select fourth category");
        });
        Test("throw_input_is_canonical_and_exclusive",()=>{
            var command=Throw(Model().Capture(),4500,-1400);var decoded=HookInput.Decode(command.Encode());Require(decoded.Grenade.TargetX==4500 && decoded.Grenade.RequestId==1,"Lost target");
            Reject(()=>new HookInput(0,0,grenade:new GrenadeCommand(0,1,100,100)).Encode());
            Reject(()=>new HookInput(0,0,inventory:new InventoryCommand(InventoryAction.Heal,0,requestId:1),grenade:decoded.Grenade).Encode());
        });
        RunSimulationTests();
        RunNetworkTests();
        return 0;
    }
    static partial void RunSimulationTests();
    static partial void RunNetworkTests();
}
