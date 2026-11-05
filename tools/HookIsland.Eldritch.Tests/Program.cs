using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Advanture.HookIsland.Simulation;
using FrameSync.Abstractions;

namespace Advanture.HookIsland.EldritchTests;

internal static partial class Program
{
    private static readonly HookGame Codec=new(false);
    private static readonly Guid SessionId=new("1f015dd1-0067-43f5-b747-332a0fc38544");
    private static IslandMap Map(EldritchConfig? config=null)
    {var b=IslandMap.CreatePlatform();return new(b.Shore,b.Pier,b.Spawns,b.FishSpots,b.Platform,new FishingConfig(character:new CharacterConfig(health:10000),enemies:EnemyConfig.Disabled,melee:new MeleeConfig(),firearms:new FirearmConfig(),fishingRodsEnabled:false,eldritch:config??new EldritchConfig(initialDelayTicks:1,staggerTicks:1,maxAlive:10)));}
    private static IDeterministicSimulation<HookState> Model(IslandMap? map=null)
    {var m=Codec.Create();m.Initialize(new SimulationInitialization(new SessionDescriptor(SessionId,1,30,Enumerable.Range(0,4).Select(i=>new Participant((ushort)i,new Guid(i+1,0,0,new byte[8]))).ToArray()),new ByteString((map??Map()).Encode())));return m;}
    private static HookGameplayEvent[] Step(IDeterministicSimulation<HookState> m,params HookInput[] input)
    {var s=m.Capture();return m.Step(new FrameContext(s.Steps+1,s.Steps+1,30),s.Players.Select(p=>new SimulationInput(new Participant(p.Slot,new Guid(p.Slot+1,0,0,new byte[8])),new ByteString((p.Slot<input.Length?input[p.Slot]:default).Encode()))).ToArray()).Select(x=>HookGameplayEventCodec.Decode(x.Span)).ToArray();}
    private static void Require(bool value,string message){if(!value)throw new Exception(message);}
    private static void Reject(Action action){try{action();}catch(InvalidDataException){return;}catch(EndOfStreamException){return;}throw new Exception("Malformed data accepted");}
    private static void Test(string name,Action test){test();Console.WriteLine("PASS "+name);}
    private static EldritchCreature Creature(EldritchKind kind,EldritchPhase phase=EldritchPhase.Windup)
    {
        var d=EldritchCatalog.Get(kind);int x=d.Habitat==EldritchHabitat.Water?-5000:-1800;
        return new EldritchCreature{Id=(int)kind,Kind=kind,Phase=phase,Generation=1,AttackSerial=1,X=x,Z=-1700,Health=d.Health,DirectionX=1000000,
            Timer=phase==EldritchPhase.Windup?1:phase==EldritchPhase.Recovering?d.RecoveryTicks:0,Target=0,Ally=-1,EffectX=x+d.Range,EffectZ=-1700};
    }
    private static EldritchSystem System(params EldritchCreature[] values)
    {
        var map=Map(new EldritchConfig(initialDelayTicks:108000,maxAlive:10));var roster=new EldritchSystem(map).Capture().Creatures.ToArray();
        foreach(var c in values)roster[c.Id]=c;
        var result=new EldritchSystem(map,new EldritchState(roster));result.ReconcileTerrain(PlatformRules.InitialTiles(map));return result;
    }
    private static Fisher Player(ushort slot,int x,int z)=>new Fisher{Slot=slot,X=x,Z=z,Vitals=new CharacterVitals{Health=10000},PulledBy=-1,HookedPlayer=-1};
    private static List<EnemyHit> Advance(EldritchSystem system,Fisher[] players,int ticks=1)
    {var hits=new List<EnemyHit>();for(int i=0;i<ticks;i++)system.Advance(PlatformRules.InitialTiles(Map()),players,hits.Add,false);return hits;}
    private static HookState With(HookState s,EldritchState eldritch,Fisher[]? players=null,int? coins=null)=>new(s.Steps,players??s.Players.ToArray(),s.Fish,coins??s.Coins,s.Tiles,s.Driftwood,s.Tackle,s.Enemies,s.RoomRules,s.Treasure,s.Commissions,s.Ocean,s.Finale,s.Crabs,s.Boss,s.Grenades,eldritch);
    private static void Main()
    {
        Test("ten_distinct_roles_and_validated_configuration",()=>{
            Require(EldritchCatalog.All.Count==10 && EldritchCatalog.All.Select(d=>d.Ability).Distinct().Count()==10,"Duplicated combat role");
            var map=Map();Require(map.Encode().SequenceEqual(IslandMap.Decode(map.Encode()).Encode()),"Config roundtrip");
            Reject(()=>new EldritchConfig(maxAlive:11));Reject(()=>new EldritchConfig(speciesMask:1024));Reject(()=>new FishingConfig(eldritch:new EldritchConfig()));
        });
        Test("deep_one_claw_can_be_evaded",()=>{
            var c=Creature(EldritchKind.DeepOne);Require(Advance(System(c),new[]{Player(0,-1000,-1700)}).Single().Damage==10,"Claw damage");
            Require(Advance(System(c),new[]{Player(0,1000,-1700)}).Count==0,"Claw tracked escaped player");
        });
        Test("ram_sweep_hits_once_and_stops_at_shore",()=>{
            var c=Creature(EldritchKind.ReefRam);var sys=System(c);var hits=Advance(sys,new[]{Player(0,-600,-1700),Player(1,-600,1000)},21);
            Require(hits.Count==1 && hits[0].Slot==0 && hits[0].Damage==20,"Charge duplicated/tracked hit");
            Require(sys.Get(c.Id).X<=2600,"Charge crossed shore");
        });
        Test("warden_front_armor_rear_recovery_and_blast",()=>{
            var c=Creature(EldritchKind.ShellWarden,EldritchPhase.Hunting);
            Require(System(c).Strike(c.Id,0,40,c.X+1000,c.Z)==10,"Front armor absent");
            Require(System(c).Strike(c.Id,0,40,c.X-1000,c.Z)==40,"Back protected");
            Require(System(c).Strike(c.Id,0,40,c.X+1000,c.Z,true)==40,"Blast incorrectly directional");
            c.Phase=EldritchPhase.Recovering;Require(System(c).Strike(c.Id,0,40,c.X+1000,c.Z)==40,"Recovery not vulnerable");
        });
        Test("eye_beam_locks_line_and_is_interruptible",()=>{
            var c=Creature(EldritchKind.RiftEye);var hits=Advance(System(c),new[]{Player(0,1500,-1700),Player(1,1500,0)});
            Require(hits.Count==1 && hits[0].Slot==0,"Beam did not use fixed line");
            var sys=System(c);Require(sys.Strike(c.Id,0,1,-1800,-3000)>0 && sys.Get(c.Id).Phase==EldritchPhase.Recovering,"Shot did not interrupt");
            Require(Advance(sys,new[]{Player(0,1500,-1700)},30).Count==0,"Interrupted beam replayed");
        });
        Test("bell_pulls_toward_source_with_bounded_range",()=>{
            var hits=Advance(System(Creature(EldritchKind.DrownedBell)),new[]{Player(0,-2000,-1700),Player(1,2000,-1700)});
            Require(hits.Count==1 && hits[0].Knockback && hits[0].DirectionX<0 && hits[0].KnockbackDistance==900,"Incorrect pull");
        });
        Test("blight_locks_ground_and_pulses_four_times",()=>{
            var c=Creature(EldritchKind.BlightSac);c.EffectX=-1800;
            var hits=Advance(System(c),new[]{Player(0,-1800,-1700),Player(1,1500,-1700)},121);
            Require(hits.Count==4 && hits.All(h=>h.Slot==0 && h.Damage==6),"Poison pulse count/locked area");
            var sys=System(c);Advance(sys,new[]{Player(0,-1800,-1700)});sys.Strike(c.Id,0,1000,0,0);
            Require(Advance(sys,new[]{Player(0,-1800,-1700)},90).Count==0,"Dead caster retained poison");
        });
        Test("choir_heals_only_live_locked_ally",()=>{
            var priest=Creature(EldritchKind.ChoirPriest);priest.Ally=0;var ally=Creature(EldritchKind.DeepOne,EldritchPhase.Recovering);ally.Health=20;ally.X=-800;
            var sys=System(priest,ally);Advance(sys,new[]{Player(0,-2000,-1700)});Require(sys.Get(0).Health==44,"Heal missed");
            sys=System(priest,ally);sys.Strike(0,0,1000,0,0);Advance(sys,new[]{Player(0,-2000,-1700)});Require(sys.Get(0).Health==0,"Resurrected dead ally");
        });
        Test("brood_has_fixed_slots_and_lifetime_cap",()=>{
            var mother=Creature(EldritchKind.BroodMother);var sys=System(mother);var players=new[]{Player(0,-1400,-1400)};
            for(int cast=0;cast<3;cast++)
            {
                Advance(sys,players);var state=sys.Capture();var cs=state.Creatures.ToArray();cs[7].Phase=EldritchPhase.Windup;cs[7].Timer=1;
                sys=new EldritchSystem(Map(new EldritchConfig(initialDelayTicks:108000,maxAlive:10)),new EldritchState(cs));
            }
            Require(sys.Get(7).Summons==4 && sys.Capture().Creatures.Count(c=>c.IsMinion && c.Damageable)==4,"Brood cap or slots wrong");
            Require(sys.Capture().Creatures.Where(c=>c.IsMinion && c.Damageable).All(c=>c.Health==30),"Minion health");
        });
        Test("stalker_telegraphs_destination_before_blink",()=>{
            var c=Creature(EldritchKind.RiftStalker);c.EffectX=1800;c.EffectZ=-1700;c.Timer=2;var sys=System(c);
            Require(Advance(sys,new[]{Player(0,-1800,-1700)}).Count==0 && sys.Get(c.Id).X==-1800,"Early blink");
            Require(Advance(sys,new[]{Player(0,-1800,-1700),Player(1,1800,-1700)}).Single().Slot==1 && sys.Get(c.Id).X==1800,"Blink ignored locked destination");
        });
        Test("herald_ring_has_real_inner_and_outer_safe_zones",()=>{
            var c=Creature(EldritchKind.StarHerald);c.X=-6000;c.Z=-6000;
            var hits=Advance(System(c),new[]{Player(0,-5500,-6000),Player(1,-3000,-6000),Player(2,0,-6000)});
            Require(hits.Count==1 && hits[0].Slot==1 && hits[0].Damage==24,"Ring safe zones wrong");
        });
        Test("solid_merchant_blocks_beam_and_pull",()=>{
            var c=Creature(EldritchKind.RiftEye);c.X=-3000;c.Z=1000;c.EffectX=3000;c.EffectZ=1000;
            Require(Advance(System(c),new[]{Player(0,2000,1000)}).Count==0,"Beam passed merchant");
            c=Creature(EldritchKind.DrownedBell);c.X=-3500;c.Z=1000;
            Require(Advance(System(c),new[]{Player(0,1400,1000)}).Count==0,"Pull passed merchant");
        });
        Test("terrain_reconciliation_keeps_death_receipt_and_requeues_live_ground",()=>{
            var live=Creature(EldritchKind.DeepOne,EldritchPhase.Hunting);var sys=System(live);sys.ReconcileTerrain(Array.Empty<PlatformTile>());
            Require(sys.Get(0).Phase==EldritchPhase.Dormant,"Ground creature survived removed deck");
            sys=System(live);sys.Strike(0,0,1000,0,0);sys.ReconcileTerrain(Array.Empty<PlatformTile>());
            Require(sys.Get(0).Phase==EldritchPhase.Dead && sys.Get(0).RewardOwner==1,"Terrain erased reward");
        });
        RunGameTests();RunNetworkTests();
    }
    static partial void RunGameTests();
    static partial void RunNetworkTests();
}
