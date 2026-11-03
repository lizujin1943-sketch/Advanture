using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Advanture.HookIsland.Simulation;
using FrameSync.Abstractions;

namespace Advanture.HookIsland.CrabTests;
internal static partial class Program
{
    private static readonly Guid SessionId=new("9455fa3b-facd-47ca-baa6-1b561ef74519");
    private static IslandMap Map(CrabConfig? config=null)
    {var m=IslandMap.CreatePlatform();return new IslandMap(m.Shore,m.Pier,m.Spawns,m.FishSpots,m.Platform,new FishingConfig(melee:new MeleeConfig(),crabs:config??new CrabConfig(initialDelayTicks:30)));}
    private static IDeterministicSimulation<HookState> Model(int count=4,CrabConfig? config=null)
    {
        var model=new HookGame(false).Create();model.Initialize(new SimulationInitialization(new SessionDescriptor(SessionId,1,30,
            Enumerable.Range(0,count).Select(i=>new Participant((ushort)i,new Guid(i+1,0,0,new byte[8]))).ToArray()),new ByteString(Map(config).Encode())));return model;
    }
    private static void Step(IDeterministicSimulation<HookState> m,params HookInput[] inputs)
    {var s=m.Capture();m.Step(new FrameContext(s.Steps+1,s.Steps+1,30),s.Players.Select(p=>new SimulationInput(new Participant(p.Slot,new Guid(p.Slot+1,0,0,new byte[8])),new ByteString((p.Slot<inputs.Length?inputs[p.Slot]:default).Encode()))).ToArray());}
    private static HookState Replace(HookState s,CrabState? crab=null,Fisher[]? players=null)=>new(s.Steps,players??s.Players.ToArray(),s.Fish,s.Coins,s.Tiles,s.Driftwood,s.Tackle,s.Enemies,s.RoomRules,s.Treasure,s.Commissions,s.Ocean,s.Finale,crab??s.Crab);
    private static void Equip(IDeterministicSimulation<HookState> m,int slot)
    {var s=m.Capture();var pan=s.Tackle!.Inventory.Items.First(i=>i.Owner==slot && i.Kind==ItemKind.Weapon);var inputs=new HookInput[s.Players.Count];inputs[slot]=new HookInput(0,0,inventory:new InventoryCommand(InventoryAction.Equip,pan.Id,requestId:s.Players[slot].LastInventoryRequest+1));Step(m,inputs);}
    private static CrabState Ready(int x=0,int z=-1400)=>new(){Phase=CrabPhase.Chasing,Generation=1,X=x,Z=z,DirectionX=1000000,Health=100,Target=-1};
    private static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
    private static void Reject(Action action){try{action();}catch(InvalidDataException){return;}throw new Exception("Invalid state accepted");}
    private static void Same(HookState a,HookState b)=>Require(new HookGame().Encode(a).SequenceEqual(new HookGame().Encode(b)),"State bytes diverged");
    private static IEnumerable<(string,Action)> Cases()
    {
        yield return ("spawn_delay_climb_and_real_shore",()=>{
            var m=Model();for(int i=0;i<29;i++)Step(m);Require(m.Capture().Crab.Phase==CrabPhase.Waiting,"Early spawn");
            Step(m);var c=m.Capture().Crab;Require(c.Phase==CrabPhase.Climbing && c.Health==100,"No automatic spawn");
            Require(!PlatformRules.OnPlatform(m.Capture().Tiles,c.ShoreX,c.ShoreZ),"Spawn underwater path is on floor");
            for(int i=0;i<44;i++)Step(m);Require(m.Capture().Crab.Phase==CrabPhase.Climbing,"Climb too fast");Step(m);Require(m.Capture().Crab.Phase==CrabPhase.Chasing,"No landing");
        });
        yield return ("pinch_windup_fixed_direction_multi_hit_once",()=>{
            var map=Map();var c=Ready();var system=new CrabSystem(map,c);var players=new[]{new Fisher{Vitals=new CharacterVitals{Health=100},Slot=0,X=1000,Z=-1400},new Fisher{Vitals=new CharacterVitals{Health=100},Slot=1,X=900,Z=-1100},new Fisher{Vitals=new CharacterVitals{Health=100},Slot=2,X=-1300,Z=-1400}};
            var hits=new List<EnemyHit>();system.Advance(PlatformRules.InitialTiles(),players,hits.Add);
            Require(system.Capture().Phase==CrabPhase.Windup,"Did not wind up");var direction=system.Capture().DirectionX;
            for(int i=0;i<17;i++)system.Advance(PlatformRules.InitialTiles(),players,hits.Add);Require(hits.Count==0,"Early pinch");
            system.Advance(PlatformRules.InitialTiles(),players,hits.Add);Require(hits.Count==2 && hits.All(h=>h.Damage==12 && h.KnockbackDistance==500),"Wrong pinch targets/damage");
            Require(system.Capture().DirectionX==direction,"Attack tracked target");for(int i=0;i<35;i++)system.Advance(PlatformRules.InitialTiles(),players,hits.Add);Require(hits.Count==2,"Repeated pinch damage");
        });
        yield return ("dodge_locked_pinch_and_hook_interrupt_protection",()=>{
            var map=Map();var system=new CrabSystem(map,Ready());var players=new[]{new Fisher{Vitals=new CharacterVitals{Health=100},Slot=0,X=1000,Z=-1400}};int hits=0;
            system.Advance(PlatformRules.InitialTiles(),players,_=>hits++);players[0].X=-1000;
            for(int i=0;i<18;i++)system.Advance(PlatformRules.InitialTiles(),players,_=>hits++);Require(hits==0,"Pinch turned around");
            var c=Ready();c.Phase=CrabPhase.Windup;c.Timer=1;c.AttackSerial=1;system=new CrabSystem(map,c);
            Require(system.Interrupt(),"Last tick interrupt rejected");system.Advance(PlatformRules.InitialTiles(),players,_=>hits++);Require(hits==0 && system.Capture().Timer==30,"Interrupted pinch hit");
            c=system.Capture();c.Phase=CrabPhase.Windup;c.Timer=1;system=new CrabSystem(map,c);Require(!system.Interrupt(),"Protection bypassed");
        });
        yield return ("normal_damage_no_stunlock_and_lethal_cancels_attack",()=>{
            var c=Ready();c.Phase=CrabPhase.Windup;c.Timer=1;c.AttackSerial=1;var s=new CrabSystem(Map(),c);
            for(int i=0;i<3;i++){Require(s.Strike(0,25),"Damage rejected");Require(s.Capture().Phase==CrabPhase.Windup,"Pan stunned crab");}
            Require(s.Strike(1,25),"Lethal rejected");int hits=0;s.Advance(PlatformRules.InitialTiles(),new[]{new Fisher{X=1000,Z=-1400}},_=>hits++);
            Require(hits==0 && s.Capture().Phase==CrabPhase.Dead && s.Capture().Attackers==3,"Lethal attack fired or contributors lost");Require(!s.Strike(2,25),"Corpse accepted new contributor");
        });
        foreach(int count in new[]{1,2,3,4})yield return ($"reward_{count}_attackers_excludes_bystanders_and_survives_restore",()=>{
            var m=Model();var c=Ready();c.Phase=CrabPhase.Dead;c.Timer=1800;c.Health=0;c.Attackers=(byte)((1<<count)-1);
            var initial=m.Capture();var people=initial.Players.ToArray();people[0].Unavailable=true;
            m.Restore(Replace(initial,c,people));Step(m);var reward=m.Capture();
            for(int i=0;i<4;i++){Require(reward.Players[i].Coins==(i<count?CrabConfig.CoinsPerAttacker(count):0),"Reward distribution wrong");Require(reward.Players[i].FishbonesEarned==(i<count?2:0),"Bone reward wrong");Require(reward.Players[i].Combo.Count==0,"Kill advanced combo");}
            Require(reward.Crab.Rewarded,"Missing receipt");var n=Model();n.Restore(new HookGame().Decode(new HookGame().Encode(reward)));
            for(int i=0;i<20;i++){Step(m);Step(n);}Same(m.Capture(),n.Capture());Require(n.Capture().Coins==count*CrabConfig.CoinsPerAttacker(count),"Duplicate reward");
        });
        yield return ("actual_pan_records_damage_and_settles_solo",()=>{
            var m=Model(1,new CrabConfig(initialDelayTicks:30,windupTicks:180));Equip(m,0);var c=Ready(-400,-1400);c.Phase=CrabPhase.Recovering;c.Timer=30;
            m.Restore(Replace(m.Capture(),c));
            for(int i=0;i<110 && !m.Capture().Crab.Rewarded;i++)Step(m,new HookInput(0,0,aimX:2000,aimZ:-1400,fire:true));
            var s=m.Capture();Require(s.Crab.Rewarded && s.Players[0].Coins==100,"Pan kill did not award 100");Require(s.Players[0].Melee.HitCount==4,"Expected four 25 point hits");
        });
        yield return ("checkpoint_every_action_phase_and_bad_receipts",()=>{
            var m=Model(1);for(int i=0;i<160;i++)
            {var n=Model(1);n.Restore(new HookGame().Decode(new HookGame().Encode(m.Capture())));Step(m);Step(n);Same(m.Capture(),n.Capture());}
            var c=m.Capture().Crab;c.Attackers=16;Reject(()=>m.Restore(Replace(m.Capture(),c)));c=m.Capture().Crab;c.Timer=int.MaxValue;Reject(()=>m.Restore(Replace(m.Capture(),c)));
        });
        yield return ("no_targets_retreat_without_reward",()=>{
            var map=Map();var c=Ready();var s=new CrabSystem(map,c);var people=new[]{new Fisher{Vitals=new CharacterVitals{Health=100},Slot=0,X=-1400,Z=-1400,Unavailable=true}};
            for(int i=0;i<350;i++)s.Advance(PlatformRules.InitialTiles(),people,_=>throw new Exception("Attacked absent target"));
            Require(s.Capture().Phase==CrabPhase.Waiting && s.Capture().Attackers==0,"Crab failed to leave");
        });
        yield return ("navigation_avoids_expanded_platform_hole_and_follows_boundary",()=>{
            var tiles=PlatformRules.InitialTiles().ToList();foreach(var p in new[]{(2,-1),(3,-1),(3,0),(3,1)})tiles.Add(new PlatformTile(p.Item1,p.Item2,TileKind.Wood));
            var nav=new CrabNavigation(Map());nav.Refresh(tiles);Require(!nav.Segment(2000,2000,6000,2000),"Route crossed missing tiles");
            var system=new CrabSystem(Map(),Ready(2000,2000));var players=new[]{new Fisher{Vitals=new CharacterVitals{Health=100},Slot=0,X=6000,Z=2000}};
            for(int i=0;i<200;i++){system.Advance(tiles,players,_=>{});var c=system.Capture();Require(nav.CanStand(c.X,c.Z),"Crab walked off platform");}
            Require(system.Capture().AttackSerial>0,"Crab could not route around gap");
            Require(nav.TrySpawn(players,2,out var land,out var sea) && !PlatformRules.OnPlatform(tiles,sea.X,sea.Z),"Spawn uses obsolete boundary");
        });
        yield return ("map_config_round_trip_and_invalid_config",()=>{
            var map=Map();Require(map.Encode().SequenceEqual(IslandMap.Decode(map.Encode()).Encode()),"Map codec differs");
            Reject(()=>new CrabConfig(windupTicks:0));Reject(()=>new FishingConfig(crabs:new CrabConfig()));
            var bytes=map.Encode();bytes[3]=0x48;Reject(()=>IslandMap.Decode(bytes));
        });
    }
    private static int Main(string[] args)
    {int count=0;try{foreach(var (name,test) in Cases().Concat(BoundaryCases()).Concat(NetworkCases()).Where(c=>args.Length==0||args.Any(a=>c.Item1.Contains(a,StringComparison.OrdinalIgnoreCase)))){test();Console.WriteLine("PASS "+name);count++;}Require(count>0,"No matching crab checks");Console.WriteLine($"Passed {count} crab checks");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
