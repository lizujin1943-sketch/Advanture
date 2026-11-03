using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Advanture.HookIsland.Simulation;
using FrameSync.Abstractions;

namespace Advanture.HookIsland.BossTests;
internal static partial class Program
{
    private static readonly Guid SessionId=new("b6295727-eef9-4b91-9027-e614850e6f0b");
    private static IslandMap Map(BossConfig? boss=null,bool arenaSpawns=false,CrabConfig? crabs=null,bool progression=false)
    {
        var map=IslandMap.CreatePlatform();
        var spawns=arenaSpawns?new[]{new Point(0,8000),new Point(-1500,16000),new Point(0,16000),new Point(1500,16000)}:map.Spawns;
        return new IslandMap(map.Shore,map.Pier,spawns,map.FishSpots,map.Platform,new FishingConfig(melee:new MeleeConfig(),firearms:new FirearmConfig(),boss:boss??new BossConfig(),crabs:crabs,treasure:progression?new TreasureConfig(0,0,1):null,commissions:progression?new CommissionConfig(new[]{new CommissionDefinition(1,1,3)}):null,finale:progression?new FinaleConfig():null));
    }
    private static IDeterministicSimulation<HookState> Model(int count=4,BossConfig? boss=null,bool arenaSpawns=false,CrabConfig? crabs=null,bool progression=false)
    {
        var m=new HookGame(false).Create();m.Initialize(new SimulationInitialization(new SessionDescriptor(SessionId,1,30,
            Enumerable.Range(0,count).Select(i=>new Participant((ushort)i,new Guid(i+1,0,0,new byte[8]))).ToArray()),new ByteString(Map(boss,arenaSpawns,crabs,progression).Encode())));return m;
    }
    private static HookGameplayEvent[] Step(IDeterministicSimulation<HookState> m,params HookInput[] inputs)
    {var s=m.Capture();return m.Step(new FrameContext(s.Steps+1,s.Steps+1,30),s.Players.Select(p=>new SimulationInput(new Participant(p.Slot,new Guid(p.Slot+1,0,0,new byte[8])),new ByteString((p.Slot<inputs.Length?inputs[p.Slot]:default).Encode()))).ToArray()).Select(e=>HookGameplayEventCodec.Decode(e.Span)).ToArray();}
    private static HookState Replace(HookState s,BossState? boss=null,Fisher[]? players=null,IEnumerable<PlatformTile>? tiles=null,TackleState? tackle=null)=>new(s.Steps,players??s.Players.ToArray(),s.Fish,s.Coins,tiles??s.Tiles,s.Driftwood,tackle??s.Tackle,s.Enemies,s.RoomRules,s.Treasure,s.Commissions,s.Ocean,s.Finale,s.Crabs,boss??s.Boss);
    private static BossState With(BossState b,BossPhase? phase=null,int? health=null,int? phaseTicks=null,bool? rewarded=null,IEnumerable<BossLimbState>? limbs=null,int? inkTicks=null,ulong? inkStart=null,int? inkAnimation=null,int? inkCooldown=null,bool? enraged=null,int? idle=null,IEnumerable<BossTileLoss>? loss=null)=>
        new(phase??b.Phase,health??b.Health,b.MaxHealth,b.ParticipantCount,b.Attackers,rewarded??b.Rewarded,phaseTicks??b.PhaseTicks,b.Sequence,inkTicks??b.InkTicks,inkStart??b.InkStartTick,limbs??b.Limbs,b.Receipts,loss??b.DestroyedTiles,idle??b.IdleTicks,inkAnimation??b.InkWindupTicks,inkCooldown??b.InkCooldownTicks,b.NoTargetTicks,enraged??b.Enraged);
    private static Fisher[] People(int count=4)=>Enumerable.Range(0,count).Select(i=>new Fisher{Slot=(ushort)i,X=i*500,Z=8000,Vitals=new CharacterVitals{Health=100},HookedPlayer=-1,PulledBy=-1,FishId=-1,DriftwoodId=-1}).ToArray();
    private static BossSystem Combat(int count=4)
    {var system=new BossSystem(new BossConfig(),count);var people=People(count);Require(system.Request(0,1,people)==BossRequestResult.Success,"No start");var tiles=BossArena.InitialTiles().ToList();for(ulong t=1;t<=120;t++)system.Advance(t,people,tiles,_=>{});return system;}
    private static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
    private static void Reject(Action action){try{action();}catch(InvalidDataException){return;}throw new Exception("Invalid state accepted");}
    private static void Same(HookState a,HookState b)=>Require(new HookGame().Encode(a).SequenceEqual(new HookGame().Encode(b)),"State bytes diverged");
    private static void RoundTrip(HookState state)=>Same(state,new HookGame().Decode(new HookGame().Encode(state)));
    private static IEnumerable<(string,Action)> Cases()
    {
        yield return ("input_vertical_boss_transaction_codec_and_boundaries",()=>{
            var input=new HookInput(0,0,aimX:7000,aimZ:28000,aimVertical:995,bossRequest:9);var decoded=HookInput.Decode(input.Encode());
            Require(decoded.AimVertical==995 && decoded.BossRequest==9,"Input tail lost");
            Reject(()=>new HookInput(0,0,aimVertical:996).Encode());Reject(()=>new HookInput(0,0,bossRequest:1,inventory:new InventoryCommand(InventoryAction.Equip,1,requestId:1)).Encode());
            var treasure=new HookInput(0,0,treasure:new TreasureCommand(1,1,1),aimVertical:500);Require(HookInput.Decode(treasure.Encode()).Treasure.RequestId==1,"Treasure offset shifted");
        });
        yield return ("start_authorization_failed_and_successful_requests_deduplicate",()=>{
            var b=new BossSystem(new BossConfig(),4);var p=People();p[0].Z=0;Require(b.Request(0,1,p)==BossRequestResult.TooFar,"Remote start");p[0].Z=8000;
            Require(b.Request(0,1,p)==BossRequestResult.TooFar && b.Capture().Phase==BossPhase.Dormant,"Failed retry restarted");
            p[0].Swimming.InWater=true;Require(b.Request(0,2,p)==BossRequestResult.Unavailable,"Swimming start");p[0].Swimming.InWater=false;
            Require(b.Request(0,3,p)==BossRequestResult.Success && b.Request(0,3,p)==BossRequestResult.Success,"Start receipt lost");
            Require(b.Request(1,1,p)==BossRequestResult.Busy && b.Capture().Sequence==1,"Concurrent double start");
        });
        yield return ("party_health_frozen_and_supports_independent",()=>{
            foreach(int count in new[]{1,2,3,4})
            {
                var b=Combat(count);var s=b.Capture();Require(s.MaxHealth==2400+1560*(count-1) && s.Limbs[0].MaxHealth==300+135*(count-1),"Wrong scaling");
                var p=People(count);if(count>1)p[count-1].Unavailable=true;b.Advance(121,p,BossArena.InitialTiles().ToList(),_=>{});
                Require(b.Capture().MaxHealth==s.MaxHealth && b.Capture().ParticipantCount==count,"HP rescaled on leave");
                b.Strike(0,100,4);Require(b.Capture().Health==s.Health && b.Capture().Limbs[4].Health==s.Limbs[4].Health-100,"Support shares HP");
            }
        });
        yield return ("baked_3d_head_eye_limb_range_and_upward_ground_rejection",()=>{
            var b=Combat(1).Capture();var eye=BossGeometry.Proxies(b).Single(p=>p.Eye);
            int ox=0,oy=1480,oz=16000;int dx=eye.X-ox,dy=eye.Y-oy,dz=eye.Z-oz,length=HookGame.Sqrt((long)dx*dx+(long)dy*dy+(long)dz*dz);
            int entry=BossGeometry.RayHit(b,ox,oy,oz,(int)((long)dx*1000000/length),(int)((long)dy*1000000/length),(int)((long)dz*1000000/length),12000,out int limb,out bool weak);
            Require(entry<12000 && limb==-1 && weak,$"Eye miss {entry}/{limb}/{weak}");
            Require(BossGeometry.RayHit(b,ox,oy,oz,0,1000000,0,12000,out _,out _)==int.MaxValue,"Vertical sky hits ground");
            Require(BossGeometry.RayHit(b,ox,oy,oz,(int)((long)dx*1000000/length),(int)((long)dy*1000000/length),(int)((long)dz*1000000/length),1000,out _,out _)==int.MaxValue,"Ray exceeded range");
            var arm=BossGeometry.Proxies(b).Where(v=>v.LimbId==1).Last();int hit=BossGeometry.RayHit(b,arm.X,arm.Y,arm.Z-400,0,0,1000000,1000,out limb,out weak);Require(hit<1000 && limb==1,"Baked arm cannot be shot");
            Require(FirearmRules.RayBox3(0,1480,0,0,0,1000000,0,1000,5000,1000,1000,1000,12000)==4000,"Merchant occlusion");
            var system=Combat(1);system.Strike(0,100,eye:true);Require(system.Capture().Health==2250,"Eye multiplier");
        });
        yield return ("limb_death_cancels_pending_attack_never_revives_at_enrage",()=>{
            var b=Combat(1);var s=b.Capture();var arms=s.Limbs.ToArray();arms[1]=new BossLimbState(1,1,300,BossAttack.Slam,44,targetSlot:0);
            b=new BossSystem(new BossConfig(),1,With(s,limbs:arms));var tiles=BossArena.InitialTiles().ToList();int before=tiles.Count,hits=0;
            Require(b.Strike(0,35,1),"Arm strike failed");b.Advance(121,People(1),tiles,_=>hits++);Require(tiles.Count==before && hits==0 && b.Capture().Limbs[1].Attack==BossAttack.None,"Dead arm impacted");
            b.Strike(0,1500);b.Advance(122,People(1),tiles,_=>{});Require(b.Capture().Phase==BossPhase.Enrage && b.Capture().Limbs[1].Health==0,"Enrage revived limb");
        });
        yield return ("front_lanes_concurrency_supports_and_wipe_reset",()=>{
            var b=Combat();var p=People();p[0].X=-16000;p[0].Z=20000;p[1].X=16000;p[1].Z=20000;p[2].X=-7000;p[2].Z=12000;p[3].X=7000;p[3].Z=12000;
            var tiles=BossArena.InitialTiles().ToList();bool attacks=false;
            for(ulong tick=121;tick<500;tick++){b.Advance(tick,p,tiles,_=>{});var s=b.Capture();attacks|=s.Limbs.Any(l=>l.Attack!=BossAttack.None);Require(s.Limbs.Count(l=>l.Attack!=BossAttack.None)<=1 && s.Limbs.Skip(4).All(l=>l.Attack==BossAttack.None),"Wrong normal concurrency");}
            Require(attacks,"No scheduled attacks");b.Strike(0,5000);bool doubleAttack=false;
            for(ulong tick=500;tick<1100;tick++){b.Advance(tick,p,tiles,_=>{});var s=b.Capture();int active=s.Limbs.Count(l=>l.Attack!=BossAttack.None);Require(active<=2,"Too many enraged limbs");doubleAttack|=active==2;}
            Require(doubleAttack,"No independent enrage clocks");for(int i=0;i<p.Length;i++){p[i].X=0;p[i].Z=49000;}
            for(ulong tick=1100;tick<1450;tick++)b.Advance(tick,p,tiles,_=>{});
            Require(b.Capture().Limbs.All(l=>l.Attack==BossAttack.None),"BOSS turned around to attack rear");
            for(int i=0;i<p.Length;i++)p[i].Unavailable=true;for(ulong tick=1450;tick<1750;tick++)b.Advance(tick,p,tiles,_=>{});
            Require(b.Capture().Phase==BossPhase.Dormant && b.Capture().Health==2400 && b.Capture().DestroyedTiles.Count>0,"Wipe did not reset or erased loss ledger");
        });
        yield return ("slam_only_destroys_fixed_strip_sweep_and_ink_preserve_floor",()=>{
            foreach(var attack in new[]{BossAttack.Slam,BossAttack.Sweep})
            {
                var s=Combat(1).Capture();var arms=s.Limbs.ToArray();arms[1]=new BossLimbState(1,300,300,attack,44,targetSlot:0);
                var b=new BossSystem(new BossConfig(),1,With(s,limbs:arms));var tiles=BossArena.InitialTiles().ToList();int initial=tiles.Count;b.Advance(121,People(1),tiles,_=>{});
                Require(attack==BossAttack.Slam?tiles.Count<initial:tiles.Count==initial,"Wrong destruction attack");
                Require(PlatformRules.InitialTiles().All(t=>tiles.Any(v=>v.X==t.X && v.Z==t.Z)),"Destroyed home platform");
                Require(b.Capture().DestroyedTiles.All(t=>BossGeometry.SlamContainsXZ(1,t.X*2000,t.Z*2000)),"Destroyed outside strip");
            }
        });
        yield return ("ink_six_second_dot_slow_and_clear_on_defeat",()=>{
            var s=Combat(1).Capture();var b=new BossSystem(new BossConfig(),1,With(s,inkCooldown:0,idle:90));var p=People(1);var tiles=BossArena.InitialTiles().ToList();int damage=0;
            for(ulong tick=121;tick<=156;tick++)b.Advance(tick,p,tiles,h=>damage+=h.Damage);
            Require(b.Capture().InkTicks==180 && b.MovementPermille(p[0])==650,"Ink did not activate/slow");
            for(ulong tick=157;tick<=336;tick++)b.Advance(tick,p,tiles,h=>damage+=h.Damage);
            Require(damage>=48 && b.Capture().InkTicks==0 && b.MovementPermille(p[0])==1000,"Ink pulses/expiry wrong");
            b.Strike(0,int.MaxValue);b.Advance(337,p,tiles,_=>throw new Exception("Dead boss hit"));Require(b.Capture().InkTicks==0,"Death left ink");
        });
        yield return ("optional_disabled_world_and_map_compatibility",()=>{
            var m=Model(1,BossConfig.Disabled);Require(m.Capture().Tiles.Count==9 && m.Capture().Boss.Limbs.Count==0,"Disabled generated ring");Step(m,new HookInput(0,0,bossRequest:1));Require(m.Capture().Boss.Receipts[0].Result==BossRequestResult.Disabled,"Disabled request receipt");RoundTrip(m.Capture());
            var map=Map();Require(map.Encode().SequenceEqual(IslandMap.Decode(map.Encode()).Encode()),"Map differs");Reject(()=>new BossConfig(bodyHealth:0));
            var old=map.Encode();old[3]=0x4b;Reject(()=>IslandMap.Decode(old));
            Require(new HookGame().Encode(m.Capture())[3]==0x4c,"State version not advanced");
        });
        foreach(var test in StateCases())yield return test;
        foreach(var test in NetworkCases())yield return test;
    }
    private static int Main(string[] args)
    {int count=0;try{foreach(var (name,test) in Cases().Where(c=>args.Length==0 || args.Any(a=>c.Item1.Contains(a,StringComparison.OrdinalIgnoreCase)))){test();Console.WriteLine("PASS "+name);count++;}Require(count>0,"No matching boss checks");Console.WriteLine($"Passed {count} boss checks");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
