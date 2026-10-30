using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Advanture.HookIsland.Simulation;
using FrameSync.Abstractions;

namespace Advanture.HookIsland.MeleeTests;
internal static partial class Program
{
    private static readonly Guid SessionId=new("9455fa3b-facd-47ca-baa6-1b561ef74516");
    private static IslandMap Map(EnemyConfig? enemies=null)
    {var b=IslandMap.CreatePlatform();return new IslandMap(b.Shore,b.Pier,b.Spawns,b.FishSpots,b.Platform,new FishingConfig(enemies:enemies??EnemyConfig.Disabled,melee:new MeleeConfig()));}
    private static IDeterministicSimulation<HookState> Model(int count=2,EnemyConfig? enemies=null)
    {
        var m=new HookGame().Create();m.Initialize(new SimulationInitialization(new SessionDescriptor(SessionId,1,30,
            Enumerable.Range(0,count).Select(i=>new Participant((ushort)i,new Guid(i+1,0,0,new byte[8]))).ToArray()),new ByteString(Map(enemies).Encode())));return m;
    }
    private static void Step(IDeterministicSimulation<HookState> m,params HookInput[] inputs)
    {var s=m.Capture();m.Step(new FrameContext(s.Steps+1,s.Steps+1,30),s.Players.Select(p=>new SimulationInput(new Participant(p.Slot,new Guid(p.Slot+1,0,0,new byte[8])),new ByteString((p.Slot<inputs.Length?inputs[p.Slot]:default).Encode()))).ToArray());}
    private static HookState Replace(HookState s,Fisher[]? players=null,EnemyState? enemies=null,RoomRulesState? rules=null)=>new(s.Steps,players??s.Players.ToArray(),s.Fish,s.Coins,s.Tiles,s.Driftwood,s.Tackle,enemies??s.Enemies,rules??s.RoomRules,s.Treasure,s.Commissions,s.Ocean,s.Finale);
    private static void Equip(IDeterministicSimulation<HookState> m,int slot=0,ItemKind kind=ItemKind.Weapon)
    {var s=m.Capture();var item=s.Tackle!.Inventory.Items.First(i=>i.Owner==slot&&i.Kind==kind);var inputs=new HookInput[s.Players.Count];inputs[slot]=new HookInput(0,0,inventory:new InventoryCommand(InventoryAction.Equip,item.Id,requestId:s.Players[slot].LastInventoryRequest+1));Step(m,inputs);Require(m.Capture().Players[slot].LastInventoryResult==InventoryResult.Success,"Equip rejected");}
    private static void Pair(IDeterministicSimulation<HookState> m,int x=1000,int z=0)
    {var s=m.Capture();var p=s.Players.ToArray();p[0].X=0;p[0].Z=-1400;p[0].HookX=0;p[0].HookZ=-1400;p[1].X=x;p[1].Z=-1400+z;p[1].HookX=x;p[1].HookZ=p[1].Z;m.Restore(Replace(s,p));}
    private static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
    private static void Reject(Action action){try{action();}catch(InvalidDataException){return;}throw new Exception("Invalid state accepted");}
    private static IEnumerable<(string,Action)> Cases()
    {
        yield return ("starter_pan_equips_without_masquerading_as_a_rod",()=>{
            var m=Model();Equip(m);var s=m.Capture();Require(s.Players[0].EquippedRod==0,"Pan is being treated as a rod");Require(MeleeRules.Equipped(s,s.Players[0])!=null,"Pan not equipped");m.Restore(s);Equip(m,kind:ItemKind.Rod);Require(m.Capture().Players[0].EquippedRod!=0,"Rod cannot be restored");
        });
        yield return ("front_swing_waits_then_pushes_once_without_friendly_damage",()=>{
            var m=Model();Equip(m);Pair(m);var before=m.Capture().Players[0];var hp=m.Capture().Players[1].Vitals.Health;
            Step(m,new HookInput(0,0,aimX:2000,aimZ:-1400,fire:true));var started=m.Capture();Require(!started.Players[1].Knockback.Active,"Impact before windup");Require(started.Players[0].Stamina==before.Stamina-8*StaminaSystem.UnitsPerPoint,"Wrong stamina cost");
            for(int i=1;i<6;i++)Step(m);Require(m.Capture().Players[1].Knockback.Active,"No forward impact");var hit=m.Capture();Require(hit.Players[1].Vitals.Health==hp,"Friendly fire damaged health");Require(hit.Players[0].Melee.HitCount==1,"No hit receipt");
            for(int i=0;i<25;i++)Step(m,new HookInput(0,0,aimX:2000,aimZ:-1400,fire:true));Require(m.Capture().Players[0].Casts==before.Casts,"Melee counted as fishing");Require(m.Capture().Players[0].Melee.Serial==2,"Cooldown did not throttle repeated input");
        });
        foreach(var (name,x,z) in new[]{("behind",-1000,0),("outside_sector",300,1200),("outside_range",2200,0)})
            yield return ($"swing_misses_{name}",()=>{var m=Model();Equip(m);Pair(m,x,z);Step(m,new HookInput(0,0,aimX:2000,aimZ:-1400,fire:true));for(int i=1;i<8;i++)Step(m);Require(m.Capture().Players[0].Melee.HitCount==0,"Hit outside forward sector");});
        yield return ("pranks_off_prevents_friendly_push",()=>{var m=Model();Equip(m);Pair(m);m.Restore(Replace(m.Capture(),rules:new RoomRulesState(false)));Step(m,new HookInput(0,0,aimX:2000,aimZ:-1400,fire:true));for(int i=1;i<8;i++)Step(m);Require(m.Capture().Players[0].Melee.HitCount==0,"Room prank switch ignored");});
        yield return ("weapon_swap_is_blocked_until_swing_finishes",()=>{
            var m=Model();Equip(m);Pair(m);Step(m,new HookInput(0,0,aimX:2000,aimZ:-1400,fire:true));var s=m.Capture();var rod=s.Tackle!.Items.First(i=>i.Owner==0);Step(m,new HookInput(0,0,inventory:new InventoryCommand(InventoryAction.Equip,rod.Id,requestId:2)));Require(m.Capture().Players[0].LastInventoryResult==InventoryResult.HookBusy,"Swapped during swing");
        });
        yield return ("checkpoint_mid_windup_is_exact_and_cannot_duplicate_impact",()=>{
            var m=Model();Equip(m);Pair(m);Step(m,new HookInput(0,0,aimX:2000,aimZ:-1400,fire:true));Step(m);var snapshot=new HookGame().Decode(new HookGame().Encode(m.Capture()));var n=Model();n.Restore(snapshot);
            for(int i=0;i<30;i++){Step(m);Step(n);}Require(new HookGame().Encode(m.Capture()).SequenceEqual(new HookGame().Encode(n.Capture())),"Restored melee diverged");Require(n.Capture().Players[0].Melee.HitCount==1,"Impact was duplicated");
            var p=snapshot.Players.ToArray();p[0].Melee.RemainingTicks=999;Reject(()=>n.Restore(Replace(snapshot,p)));
        });
    }
    private static int Main(string[] args)
    {int count=0;try{foreach(var (name,test) in Cases().Concat(CombatCases()).Concat(NetworkCases()).Where(c=>args.Length==0||args.Any(a=>c.Item1.Contains(a,StringComparison.OrdinalIgnoreCase)))){test();Console.WriteLine("PASS "+name);count++;}Require(count>0,"No matching melee checks");Console.WriteLine($"Passed {count} melee checks");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
