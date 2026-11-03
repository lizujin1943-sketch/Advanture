using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using FrameSync.Abstractions;

namespace Advanture.HookIsland.FirearmTests;
internal static partial class Program
{
    private static readonly Guid SessionId=new("213714a2-0922-4db6-9a80-abaa623c9f11");
    private static IslandMap Map(EnemyConfig? enemies=null)
    {var b=IslandMap.CreatePlatform();return new IslandMap(b.Shore,b.Pier,b.Spawns,b.FishSpots,b.Platform,new FishingConfig(enemies:enemies??EnemyConfig.Disabled,melee:new MeleeConfig(),firearms:new FirearmConfig()));}
    private static IDeterministicSimulation<HookState> Model(EnemyConfig? enemies=null,bool armed=true)
    {
        var m=new HookGame(false).Create();m.Initialize(new SimulationInitialization(new SessionDescriptor(SessionId,1,30,new[]{new Participant(0,Guid.NewGuid()),new Participant(1,Guid.NewGuid())}),new ByteString(Map(enemies).Encode())));
        var s=m.Capture();var ps=s.Players.ToArray();foreach(int i in new[]{0,1}){ps[i].Coins=ps[i].CoinsEarned=5000;ps[i].X=ps[i].HookX=i==0?-1000:700;ps[i].Z=ps[i].HookZ=-1400;}
        m.Restore(new HookState(s.Steps,ps,s.Fish,10000,s.Tiles,s.Driftwood,s.Tackle,s.Enemies,s.RoomRules,s.Treasure,s.Commissions,s.Ocean,s.Finale));
        if(armed){Step(m,Buy(FirearmCatalog.OfferOffset,2,true));Wait(m,9);Require(Gun(m).LoadedAmmo==7,"Gun not bought/equipped");}return m;
    }
    private static HookInput Buy(int target,uint request,bool equip=false)=>new(0,0,inventory:new InventoryCommand(equip?InventoryAction.BuyAndEquip:InventoryAction.Buy,target,batch:1,requestId:request));
    private static HookInput Shoot(bool friend=false,short move=0)=>new(move,0,aimX:friend?5000:-1000,aimZ:friend?-1400:-14000,fire:true);
    private static HookGameplayEvent[] Step(IDeterministicSimulation<HookState> m,params HookInput[] inputs)
    {var s=m.Capture();return m.Step(new FrameContext(s.Steps+1,s.Steps+1,30),s.Players.Select(p=>new SimulationInput(new Participant(p.Slot,new Guid(p.Slot+1,0,0,new byte[8])),new ByteString((p.Slot<inputs.Length?inputs[p.Slot]:default).Encode()))).ToArray()).Select(v=>HookGameplayEventCodec.Decode(v.Span)).ToArray();}
    private static void Wait(IDeterministicSimulation<HookState> m,int ticks){for(int i=0;i<ticks;i++)Step(m);}
    // Commerce fixtures exercise the purchased gun, in addition to the new starter pistol.
    private static InventoryItem Gun(IDeterministicSimulation<HookState> m)=>m.Capture().Tackle!.Inventory.Items.Where(i=>i.Kind==ItemKind.Firearm && i.Owner==0).OrderByDescending(i=>i.Id).First();
    private static int Reserve(IDeterministicSimulation<HookState> m)=>m.Capture().Tackle!.Inventory.Items.Where(i=>i.Owner==0&&i.Kind==ItemKind.Ammunition).Sum(i=>i.Count);
    private static HookState Replace(HookState s,Fisher[]? ps=null,EnemyState? enemies=null)=>new(s.Steps,ps??s.Players.ToArray(),s.Fish,s.Coins,s.Tiles,s.Driftwood,s.Tackle,enemies??s.Enemies,s.RoomRules,s.Treasure,s.Commissions,s.Ocean,s.Finale);
    private static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
    private static void Reject(Action action){try{action();}catch(InvalidDataException){return;}throw new Exception("Invalid snapshot accepted");}
    private static IEnumerable<(string,Action)> Cases()
    {
        yield return ("spread_profiles_are_configurable_per_weapon_type",()=>{
            var eagle=FirearmCatalog.Get(0).Spread;
            var curve=new[]{new MovementSpreadNode(100,100),new MovementSpreadNode(1000,3000),new MovementSpreadNode(3000,8000)};
            var other=new FirearmSpreadDefinition(curve,100,30000,9000);
            curve[0]=new MovementSpreadNode(0,9999);
            Require(other.MinimumHalfAngle==100,"Spread profile shares a mutable input curve");
            Require(eagle.StationarySpeedThreshold==50 && eagle.IncreasePerSecond==60000 && eagle.RecoveryPerSecond==20000 && eagle.Sampling==FirearmSpreadSampling.Triangular,"CSV tuning was not loaded");
            Require(FirearmSpread.Target(3000,eagle)==6000 && FirearmSpread.Target(3000,other)==8000 && FirearmSpread.Target(100,other)==100,"Profiles do not have independent speed curves");
            var first=new FirearmSpreadState(0,500);var second=new FirearmSpreadState(1,100);
            first=FirearmSpread.Advance(first,3000,eagle);second=FirearmSpread.Advance(second,3000,other);
            Require(first.Type==0 && second.Type==1 && first.Angle==2500 && second.Angle==1100,"Profiles did not widen at their own rates");
            first=FirearmSpread.Advance(first,0,eagle);second=FirearmSpread.Advance(second,0,other);
            Require(first.Angle==1834 && first.Remainder==20 && second.Angle==800,"Profiles did not recover at their own rates");
            var fallback=new FirearmState {MoveSpeed=3000};Require(fallback.Spread(0)==6000,"Presentation fallback did not read the configured profile");
            var learned=new FirearmState();FirearmSpread.Advance(ref learned,3000,new[]{0});Require(learned.Spread(0)==6000,"Newly acquired profile initialized at stationary precision");
            Require(FirearmSpread.Sample(11,0,1,5,500,eagle.Sampling)==FirearmSpread.Sample(11,0,1,5,500),"Configured sampling changed the approved triangular distribution");
            var slow=new FirearmSpreadDefinition(new[]{new MovementSpreadNode(0,0),new MovementSpreadNode(3000,500)},0,31,61);
            var fractional=new FirearmSpreadState(2,0);
            for(int i=0;i<30;i++)fractional=FirearmSpread.Advance(fractional,3000,slow);
            Require(fractional.Angle==31 && fractional.Remainder==0,"Configurable fractional widening rate was truncated");
            for(int i=0;i<30;i++)fractional=FirearmSpread.Advance(fractional,0,slow);
            Require(fractional.Angle==0 && fractional.Remainder==0,"Fractional recovery did not converge");
        });
        yield return ("spread_profile_validation_and_snapshot_bounds",()=>{
            FirearmSpreadDefinition Profile(MovementSpreadNode[] nodes,int threshold=50,int increase=60000,int recovery=20000,FirearmSpreadSampling sampling=FirearmSpreadSampling.Triangular)=>new(nodes,threshold,increase,recovery,sampling);
            var normal=new[]{new MovementSpreadNode(50,500),new MovementSpreadNode(4500,10000)};
            Reject(()=>Profile(Array.Empty<MovementSpreadNode>()));Reject(()=>Profile(normal,threshold:-1));Reject(()=>Profile(normal,threshold:51));
            Reject(()=>Profile(normal,increase:0));Reject(()=>Profile(normal,recovery:0));Reject(()=>Profile(normal,increase:300001));Reject(()=>Profile(normal,recovery:300001));Reject(()=>Profile(normal,sampling:(FirearmSpreadSampling)1));
            Reject(()=>Profile(new[]{new MovementSpreadNode(50,500),new MovementSpreadNode(50,1000)}));
            Reject(()=>Profile(new[]{new MovementSpreadNode(50,500),new MovementSpreadNode(4500,499)}));
            Reject(()=>Profile(new[]{new MovementSpreadNode(50,-1)}));Reject(()=>Profile(new[]{new MovementSpreadNode(50,10001)}));
            Reject(()=>Profile(new[]{new MovementSpreadNode(50,500),new MovementSpreadNode(1000001,1000)}));
            var m=Model();var codec=new HookGame();
            foreach(var bad in new[]{new FirearmSpreadState(0,499),new FirearmSpreadState(0,10001),new FirearmSpreadState(0,500,30),new FirearmSpreadState(0,500,-30),new FirearmSpreadState(FirearmCatalog.All.Count,500)})
            {var s=m.Capture();var ps=s.Players.ToArray();ps[0].Firearm.SetSpreads(new[]{bad});Reject(()=>codec.Decode(codec.Encode(Replace(s,ps))));}
            var saved=m.Capture();var players=saved.Players.ToArray();players[0].Firearm.SetSpreads(new[]{new FirearmSpreadState(0,2500,-20)});
            Require(codec.Decode(codec.Encode(Replace(saved,players))).Players[0].Firearm.Spreads.Single().Remainder==-20,"Checkpoint discarded widening remainder");
            try{FirearmSpread.Direction(1000,0,10001,out _,out _);throw new Exception("Out-of-domain trigonometry accepted");}catch(ArgumentOutOfRangeException){}
        });
        yield return ("spread_speed_curve_recovery_and_symmetric_deterministic_domain",()=>{
            foreach(var pair in new[]{(0,500),(50,500),(1500,2000),(3000,6000),(4500,10000),(9000,10000)})Require(FirearmSpread.Target(pair.Item1)==pair.Item2,"Wrong speed curve");
            var state=new FirearmState();FirearmSpread.Advance(ref state,0,new[]{0});FirearmSpread.Advance(ref state,3000,new[]{0});Require(state.Spread(0)==2500,"Start moving stays precise");
            for(int i=0;i<3;i++)FirearmSpread.Advance(ref state,3000,new[]{0});for(int i=0;i<9;i++)FirearmSpread.Advance(ref state,0,Array.Empty<int>());Require(state.Spread(0)==500,"Standing recovery did not take 0.3s");
            FirearmSpread.Advance(ref state,4500,new[]{0});for(int i=0;i<5;i++)FirearmSpread.Advance(ref state,4500,new[]{0});for(int i=0;i<15;i++)FirearmSpread.Advance(ref state,0,new[]{0});Require(state.Spread(0)==500,"Sprint recovery did not take 0.5s");
            var samples=Enumerable.Range(1,5000).Select(i=>FirearmSpread.Sample(7381,0,(uint)i,7,10000)).ToArray();Require(samples.Min()< -8000 && samples.Max()>8000 && Math.Abs(samples.Average())<300,"Spread is biased or clipped");
            for(int i=1;i<100;i++){int angle=FirearmSpread.Sample(7381,0,(uint)i,7,10000);FirearmSpread.Direction(1000,0,angle,out int x,out int z);Require(Math.Abs((long)x*x+(long)z*z-1000000000000L)<5000000,"Direction not normalized");}
            Require(FirearmRules.FriendlyDamage(35,0)==18 && FirearmRules.FriendlyDamage(35,100)==9,"Friendly armor rounded twice");
        });
        yield return ("shot_spends_once_emits_authoritative_ray_and_does_not_touch_fishing_combo",()=>{
            var m=Model();var before=m.Capture();var e=Step(m,Shoot(true));var after=m.Capture();Require(after.Players[1].Vitals.Health==82 && !after.Players[1].Knockback.Active,"50% friend damage ignored prank-off");
            Require(Gun(m).LoadedAmmo==6 && after.Players[0].Firearm.ShotSequence==1,"Shot ammo not committed");Require(e.Count(v=>v.Kind==HookGameplayEventKind.FirearmShot)==1 && e.Single(v=>v.Kind==HookGameplayEventKind.FirearmShot).TargetId==1,"Wrong confirmed endpoint/target");
            Require(after.Players[0].Casts==before.Players[0].Casts && after.Players[0].Combo.Equals(before.Players[0].Combo),"Gun affected fishing combo");
            Step(m,Shoot(true));Require(Gun(m).LoadedAmmo==6,"Cooldown spent second bullet");new HookGame().Decode(new HookGame().Encode(m.Capture()));
        });
        yield return ("empty_magazine_no_auto_reload_and_exact_manual_reload_refill",()=>{
            var m=Model();for(int i=0;i<7;i++){Step(m,Shoot());Wait(m,18);}Step(m,Shoot());Require(Gun(m).LoadedAmmo==0 && Reserve(m)==0 && !m.Capture().Players[0].Firearm.Reloading,"Empty shot auto reloads or mints bullets");
            Step(m,new HookInput(0,0,reload:true));Require(Reserve(m)==0 && Gun(m).LoadedAmmo==0 && !IslandSaveSafety.IsSafe(m.Capture()),"Reload filled magazine at start or save accepted transient");
            Wait(m,53);Require(Gun(m).LoadedAmmo==0,"Reload finished early");Step(m);Require(Gun(m).LoadedAmmo==7 && Reserve(m)==0 && IslandSaveSafety.IsSafe(m.Capture()),"Reload did not atomically refill on completion");
            new HookGame().Decode(new HookGame().Encode(m.Capture()));
        });
        yield return ("reload_last_tick_switch_cancels_without_spending_and_failed_switch_preserves",()=>{
            var m=Model();Step(m,Shoot());Wait(m,6);Step(m,new HookInput(0,0,reload:true));Wait(m,52);
            Step(m,new HookInput(0,0,inventory:new InventoryCommand(InventoryAction.Equip,int.MaxValue,requestId:3)));Require(m.Capture().Players[0].Firearm.ReloadTicks==1,"Failed switch cancelled reload");
            int rod=m.Capture().Tackle!.Inventory.Items.First(i=>i.Kind==ItemKind.Rod&&i.Owner==0).Id;
            Step(m,new HookInput(0,0,inventory:new InventoryCommand(InventoryAction.Equip,rod,requestId:4)));Require(!m.Capture().Players[0].Firearm.Reloading && Gun(m).LoadedAmmo==6 && Reserve(m)==0,"Completion-frame switch spent ammo");
        });
        yield return ("reload_last_tick_valid_rescue_wins_and_inventory_actions_are_blocked",()=>{
            var m=Model();Step(m,Shoot());Wait(m,6);var s=m.Capture();var ps=s.Players.ToArray();ps[1].Vitals.Health=0;ps[1].X=ps[1].HookX=400;m.Restore(Replace(s,ps));
            Step(m,new HookInput(0,0,reload:true));Wait(m,52);Step(m,Buy(AmmunitionCatalog.OfferOffset,3));Require(m.Capture().Players[0].LastInventoryResult==InventoryResult.HookBusy,"Purchase during reload succeeded");
            Step(m,new HookInput(0,0,revival:new RevivalCommand(rescueSlot:1)));Require(!m.Capture().Players[0].Firearm.Reloading && Gun(m).LoadedAmmo==6 && Reserve(m)==0 && m.Capture().Players[1].Revival.RescueTicks==1,"Reload/rescue ordering broken");
        });
        yield return ("reload_survives_menu_and_checkpoint_without_replay",()=>{
            var m=Model();Step(m,Shoot());Wait(m,6);Step(m,new HookInput(0,0,reload:true));Wait(m,20);
            var codec=new HookGame();var cp=codec.Decode(codec.Encode(m.Capture()));var restored=Model();restored.Restore(cp);
            for(int i=0;i<40;i++){Step(m,new HookInput(0,0,shopping:true));Step(restored,new HookInput(0,0,shopping:true));}
            Require(codec.Encode(m.Capture()).SequenceEqual(codec.Encode(restored.Capture())) && Gun(m).LoadedAmmo==7 && Reserve(m)==0,"Reload checkpoint/menu diverged");
            var bad=m.Capture().Players.ToArray();bad[0].Firearm.ReloadTicks=1;Reject(()=>restored.Restore(Replace(m.Capture(),bad)));
        });
        yield return ("movement_spread_uses_actual_displacement_and_recovery_preserves_cooldown",()=>{
            var m=Model();Step(m,Shoot(move:1000));var p=m.Capture().Players[0];Require(p.Firearm.MoveSpeed==3000 && p.Firearm.Spread(0)==2500,"Current shot incorrectly used recoil slow");
            for(int frame=1;frame<=7;frame++)
            {
                int before=p.X;Step(m,new HookInput(1000,0));p=m.Capture().Players[0];
                Require(p.X-before==(frame<=6?80:100),$"Wrong movement on recovery frame {frame}: {p.X-before}");
                Require(p.Firearm.RecoveryTicks==Math.Max(0,6-frame),"Recovery action clock shifted");
                Require(p.Firearm.CooldownTicks==18-frame,"Fire cooldown shifted");
                Require(IslandSaveSafety.IsSafe(m.Capture())==(frame>=6),"Recovery save boundary shifted");
            }
            var cp=new HookGame().Decode(new HookGame().Encode(m.Capture()));var n=Model();n.Restore(cp);Step(m,Shoot());Step(n,Shoot());Require(new HookGame().Encode(m.Capture()).SequenceEqual(new HookGame().Encode(n.Capture())),"Restored cooldown rerolled shot");
        });
        yield return ("sixth_recovery_movement_keeps_existing_reload_and_equip_gates",()=>{
            var m=Model();Step(m,Shoot());Wait(m,5);int x=m.Capture().Players[0].X;
            Step(m,new HookInput(1000,0,reload:true));var p=m.Capture().Players[0];
            Require(p.X-x==80 && p.Firearm.ReloadTicks==54 && p.Firearm.RecoveryTicks==0,"Sixth interval delayed reload or lost recovery movement");
            m=Model();Step(m,Shoot());Wait(m,5);int rod=m.Capture().Tackle!.Inventory.Items.First(i=>i.Kind==ItemKind.Rod && i.Owner==0).Id;
            Step(m,new HookInput(0,0,inventory:new InventoryCommand(InventoryAction.Equip,rod,requestId:3)));
            Require(m.Capture().Players[0].EquippedRod==rod && m.Capture().Players[0].LastInventoryResult==InventoryResult.Success,"Sixth interval delayed equipment gate");
        });
        yield return ("piranha_two_shots_kill_awards_coins_once_and_nearest_solids_block",()=>{
            var cfg=new EnemyConfig(piranhaCount:1,monsterEnabled:false,patrolSpeed:30);var m=Model(cfg);var s=m.Capture();var ps=s.Players.ToArray();ps[1].X=ps[1].HookX=2000;ps[1].Z=ps[1].HookZ=2400;
            var f=s.Enemies.Piranhas.ToArray();f[0].X=700;f[0].Z=-1400;m.Restore(Replace(s,ps,new EnemyState(f,s.Enemies.Monster,s.Enemies.Waterballs)));
            Step(m,Shoot(true));Require(m.Capture().Enemies.Piranhas[0].DamageTaken==35 && m.Capture().Players[0].Coins==4700,"Nonlethal shot missed or paid coins");Wait(m,18);
            var events=Step(m,Shoot(true));s=m.Capture();int gold=PiranhaFishing.Coins(cfg,f[0]);
            Require(s.Enemies.Piranhas[0].RespawnTicks>0 && s.Enemies.Captures==0 && s.Players[0].Catches==0 && s.Players[0].FishbonesEarned==0 && s.Players[0].Coins==4700+gold,"Gun kill reward/progress incorrect");
            Require(events.Count(e=>e.Kind==HookGameplayEventKind.CoinsReceived && e.Slot==0 && e.Amount==gold)==1,"Missing confirmed kill income event");
            m.Restore(new HookGame().Decode(new HookGame().Encode(s)));Wait(m,30);Require(m.Capture().Coins==s.Coins && m.Capture().Players[0].Coins==4700+gold,"Restored kill paid twice");
            Require(FirearmRules.RayBox(0,-1400,0,1000000,0,1000,800,12000)==1600,"Shop solid ray incorrect");new HookGame().Decode(new HookGame().Encode(s));
        });
        yield return ("friendly_protection_does_not_prevent_damage_or_downing",()=>{
            var m=Model();var s=m.Capture();var ps=s.Players.ToArray();ps[1].ProtectionTicks=90;ps[1].Vitals.Health=10;m.Restore(Replace(s,ps));Step(m,Shoot(true));
            Require(m.Capture().Players[1].Vitals.Downed && !m.Capture().Players[1].Knockback.Active,"Protected friend did not take damage/down");
        });
        yield return ("water_without_ring_cancels_reload_and_cannot_fire",()=>{
            var m=Model();Step(m,Shoot());Wait(m,6);Step(m,new HookInput(0,0,reload:true));
            // A legal checkpoint cannot retain an action after entering invalid water.
            var s=m.Capture();var ps=s.Players.ToArray();ps[0].X=ps[0].HookX=-4000;ps[0].Swimming.InWater=true;FirearmRules.CancelActions(ref ps[0]);m.Restore(Replace(s,ps));Step(m,Shoot());
            Require(Gun(m).LoadedAmmo==6 && Reserve(m)==0 && !m.Capture().Players[0].Firearm.Reloading,"Underwater gun action continued");
        });
    }
    private static int Main(string[] args)
    {int count=0;try{foreach(var (name,test) in LongGunCases().Concat(LongGunNetworkCases()).Concat(UnlimitedAmmoCases()).Concat(StarterCases()).Concat(Cases()).Concat(BoundaryCases()).Concat(KillRewardCases()).Concat(NetworkCases()).Where(c=>args.Length==0||args.Any(a=>c.Item1.Contains(a,StringComparison.OrdinalIgnoreCase)))){test();Console.WriteLine("PASS "+name);count++;}Require(count>0,"No matching firearm checks");Console.WriteLine($"Passed {count} firearm checks");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
