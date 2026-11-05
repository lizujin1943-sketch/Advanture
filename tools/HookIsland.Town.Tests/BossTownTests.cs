using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;
using FrameSync.Abstractions;

internal static class BossTownTests
{
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    private static Fisher[] People()=>new[]{new Fisher{Slot=0,X=2000,Z=5000,Vitals=new CharacterVitals{Health=100},HookedPlayer=-1,PulledBy=-1,FishId=-1,DriftwoodId=-1}};
    private static BossSystem System(BossState? state=null)=>new BossSystem(new BossConfig(),1,state,true);
    private static BossState Combat()
    {
        var b=System();var p=People();Check(b.Request(0,1,p)==BossRequestResult.Success,"Town beacon did not start boss.");
        var tiles=TownLayout.InitialTiles().ToList();for(ulong t=1;t<=120;t++)b.Advance(t,p,tiles,_=>{});return b.Capture();
    }
    private static BossState With(BossState b,IEnumerable<BossLimbState>? limbs=null,int? animation=null,int? cooldown=null,int? idle=null,int? ink=null,ulong? inkStart=null)=>
        new(b.Phase,b.Health,b.MaxHealth,b.ParticipantCount,b.Attackers,b.Rewarded,b.PhaseTicks,b.Sequence,ink??b.InkTicks,inkStart??b.InkStartTick,limbs??b.Limbs,b.Receipts,b.DestroyedTiles,idle??b.IdleTicks,animation??b.InkWindupTicks,cooldown??b.InkCooldownTicks,b.NoTargetTicks,b.Enraged,b.TownArena,b.InkSector);
    public static int Run()
    {
        var cases=new (string,Action)[]{
            ("town_boss_beacon_and_pose_anchor",()=>{
                var b=Combat();var body=BossGeometry.Proxies(b).First();
                Check(Math.Abs(body.X+8000)<5000 && Math.Abs(body.Z+12000)<5000,"Boss body remained at legacy world center.");
                foreach(var p in TownLayout.ShootingPlatforms)
                {
                    var eye=BossGeometry.Proxies(b).Single(v=>v.Eye);int x=eye.X-p.X,y=eye.Y-1480,z=eye.Z-p.Z,len=HookGame.Sqrt((long)x*x+(long)y*y+(long)z*z);
                    int entry=BossGeometry.RayHit(b,p.X,1480,p.Z,(int)((long)x*1000000/len),(int)((long)y*1000000/len),(int)((long)z*1000000/len),12000,out _,out _);
                    Check(entry<=10000,"Basic pistol cannot hit boss from a stone platform.");
                }
            }),
            ("town_slam_only_removes_its_four_authored_segments",()=>{
                var s=Combat();var limbs=s.Limbs.ToArray();limbs[0]=new BossLimbState(0,300,300,BossAttack.Slam,44,targetSlot:0);
                var b=System(With(s,limbs:limbs,idle:90));var tiles=TownLayout.InitialTiles().ToList();tiles.Add(new PlatformTile(-4,-11,TileKind.Wood));
                b.Advance(121,People(),tiles,_=>{});
                Check(tiles.Count==13,"Town slam must remove exactly four authored segments.");
                Check(tiles.Any(t=>t.X==-4 && t.Z==-11),"Town slam removed non-whitelisted floor.");
                Check(b.Capture().DestroyedTiles.Count==4 && b.Capture().DestroyedTiles.All(t=>TownLayout.Destructible(t.X,t.Z)),"Town loss ledger escaped whitelist.");
            }),
            ("town_ink_has_two_second_windup_and_local_coverage",()=>{
                var s=Combat();var b=System(With(s,cooldown:0,idle:90));var p=People();p[0].X=-8000;p[0].Z=-22000;var tiles=TownLayout.InitialTiles().ToList();
                for(ulong t=121;t<180;t++)b.Advance(t,p,tiles,_=>{});
                Check(b.Capture().InkTicks==0,"Ink landed before two seconds.");b.Advance(180,p,tiles,_=>{});
                Check(b.Capture().InkTicks==180,"Ink did not land at two seconds.");
                int covered=TownLayout.ShootingPlatforms.Count(v=>b.MovementPermille(new Fisher{X=v.X,Z=v.Z})<1000);
                Check(covered>0 && covered<3,"Ink covers all three stone platforms or misses them all.");
            }),
            ("town_stone_tips_are_actual_slam_targets",()=>{
                foreach(var platform in TownLayout.ShootingPlatforms)
                {
                    var b=System(Combat());var p=People();p[0].X=platform.X;p[0].Z=platform.Z;var tiles=TownLayout.InitialTiles().ToList();int hits=0;
                    for(ulong tick=121;tick<=270;tick++)b.Advance(tick,p,tiles,h=>hits+=h.Damage);
                    Check(hits>0,"Stone shooting tip was immune to aimed slam.");
                }
            }),
            ("town_enraged_double_slams_remain_spaced_and_cancellable",()=>{
                var b=System(Combat());b.Strike(0,1500);var p=People();p[0].X=-8000;p[0].Z=-27000;var tiles=TownLayout.InitialTiles().ToList();
                int lastImpact=-1000;bool overlapping=false;
                for(ulong tick=121;tick<1000;tick++)
                {
                    b.Advance(tick,p,tiles,_=>{});var state=b.Capture();overlapping|=state.Limbs.Count(l=>l.Attack!=BossAttack.None)==2;
                    foreach(var l in state.Limbs.Where(l=>l.Attack==BossAttack.Slam && l.AttackTicks==45))
                    {Check((int)tick-lastImpact>=18,"Two arm impacts were closer than 0.6 seconds.");lastImpact=(int)tick;}
                }
                Check(overlapping && lastImpact>0,"Town enrage never exercised overlapping arm windups.");
                b.Strike(0,int.MaxValue);int hits=0;for(ulong tick=1000;tick<1180;tick++)b.Advance(tick,p,tiles,_=>hits++);
                Check(hits==0 && b.Capture().Phase==BossPhase.Defeated && b.Capture().InkTicks==0,"Dead town boss kept pending damage.");
            }),
            ("town_escape_search_respects_ink_speed_and_impending_impact",()=>{
                var baseState=Combat();var p=People()[0];p.X=-8000;p.Z=-22000;
                var limbs=baseState.Limbs.ToArray();limbs[1]=new BossLimbState(1,300,300,BossAttack.Slam,0,targetSlot:0,attackX:0,attackZ:-1000);
                var state=With(baseState,limbs:limbs,ink:180,inkStart:120);
                p.X=-4200; // Within the ink edge: slow movement must reach dry stone before the next pulse.
                Check(BossEscapeRoutes.CanEscape(state,Array.Empty<PlatformTile>(),p),"Stone tip has no slow walking route with full warning.");
                p.X=-8000;
                Check(!BossEscapeRoutes.CanEscape(With(baseState,limbs:limbs,ink:151,inkStart:91),TownLayout.InitialTiles(),p),"Route ignored an imminent ink pulse while escaping a slam.");
                limbs[1]=new BossLimbState(1,300,300,BossAttack.Slam,44,targetSlot:0,attackX:0,attackZ:-1000);
                Check(!BossEscapeRoutes.CanEscape(With(baseState,limbs:limbs),TownLayout.InitialTiles(),p),"Escape search teleported out of next-tick slam.");
            }),
            ("town_outer_ring_attack_roundtrips_actual_locked_direction",()=>{
                var codec=new HookGame(false);var model=codec.Create();var participant=new Participant(0,new Guid("00000000-0000-0000-0000-000000000001"));
                model.Initialize(new SimulationInitialization(new SessionDescriptor(Guid.NewGuid(),1,30,new[]{participant}),new ByteString(IslandMap.CreateTown().Encode())));
                void Step(HookInput command=default)
                {var state=model.Capture();model.Step(new FrameContext(state.Steps+1,state.Steps+1,30),new[]{new SimulationInput(participant,new ByteString(command.Encode()))});}
                HookState Copy(HookState state,Fisher[] players,TownState? town=null,EldritchState? roster=null)=>
                    new(state.Steps,players,state.Fish,state.Coins,state.Tiles,state.Driftwood,state.Tackle,state.Enemies,state.RoomRules,state.Treasure,state.Commissions,state.Ocean,state.Finale,state.Crabs,state.Boss,state.Grenades,roster??state.Eldritch,town??state.Town);
                // Isolate completed district objectives and player placement; start and schedule the boss through production commands.
                var initial=model.Capture();var players=initial.Players.ToArray();var creatures=initial.Eldritch.Creatures.ToArray();var zones=initial.Town.Zones.ToArray();
                foreach(int zone in new[]{2,4,5})
                {
                    zones[zone]=new TownZoneState{Started=true,Cleared=true};
                    for(int i=0;i<TownEncounters.AdultCount;i++)if(TownEncounters.Zone(i)==zone)
                    {var c=creatures[i];c.Phase=EldritchPhase.Dead;c.Generation=1;c.Timer=2700;c.DirectionX=1000000;c.Target=c.Ally=-1;c.RewardOwner=1;c.Rewarded=true;creatures[i]=c;}
                }
                players[0].X=players[0].HookX=2000;players[0].Z=players[0].HookZ=5000;players[0].Swimming=default;
                model.Restore(codec.Decode(codec.Encode(Copy(initial,players,new TownState(true,initial.Town.Players,zones,goals:7),new EldritchState(creatures)))));
                for(int i=0;i<15;i++)Step();Step(new HookInput(0,0,town:new TownCommand(TownAction.ReadyBoss,1)));
                for(int i=0;i<120;i++)Step();Check(model.Capture().Boss.Phase==BossPhase.Combat,"Fixture failed to enter production Town combat.");
                var before=model.Capture();players=before.Players.ToArray();players[0].X=players[0].HookX=-29000;players[0].Z=players[0].HookZ=-11000;players[0].Swimming=default;
                Check(TownLayout.CanStand(before.Tiles,-29000,-11000),"Outer-ring target must stand on actual stone.");model.Restore(Copy(before,players));
                for(int i=0;i<120 && !model.Capture().Boss.Limbs.Any(l=>l.Attack!=BossAttack.None);i++)Step();
                var attacked=model.Capture();var arm=attacked.Boss.Limbs.FirstOrDefault(l=>l.Attack!=BossAttack.None);
                Check(arm.Attack!=BossAttack.None && arm.AttackX== -998 && arm.AttackZ==47,"Production scheduler did not reproduce the legal northward lock.");
                var bytes=codec.Encode(attacked);var restored=codec.Decode(bytes);model.Restore(restored);
                Check(bytes.SequenceEqual(codec.Encode(model.Capture())),"Legal outer-ring lock changed during save/restore.");
            }),
            ("town_escape_rejects_safe_damage_axis_with_future_destroyed_footprint",()=>{
                var state=Combat();var limbs=state.Limbs.ToArray();limbs[3]=new BossLimbState(3,300,300,BossAttack.Slam,44,targetSlot:0,attackX:900,attackZ:-436);
                var pending=With(state,limbs:limbs);var p=People()[0];p.X=3008;p.Z=-18933;var floor=TownLayout.InitialTiles();
                Check(TownLayout.CanStand(floor,p.X,p.Z,200),"Fixture must begin with a complete supported footprint.");
                Check(!BossGeometry.SlamContainsXZ(pending,3,p.X,p.Z,200),"Fixture must avoid direct slam damage.");
                Check(!BossEscapeRoutes.CanEscape(pending,floor,p),"Stationary escape witness ignored the board destroyed on the next tick.");
            }),
            ("town_wipe_waits_ten_seconds_and_clears_loss_for_free_retry",()=>{
                var s=Combat();var limbs=s.Limbs.ToArray();limbs[0]=new BossLimbState(0,300,300,BossAttack.Slam,44,targetSlot:0);
                var b=System(With(s,limbs:limbs,idle:90));var tiles=TownLayout.InitialTiles().ToList();var p=People();b.Advance(121,p,tiles,_=>{});
                p[0].X=50000;p[0].Z=50000;for(ulong t=122;t<421;t++)b.Advance(t,p,tiles,_=>{});
                Check(b.Capture().Phase!=BossPhase.Dormant,"Reset happened before ten-second return window.");b.Advance(421,p,tiles,_=>{});
                Check(b.Capture().Phase==BossPhase.Dormant && b.Capture().DestroyedTiles.Count==0,"Town wipe retained damage ledger.");
                p=People();Check(b.Request(0,2,p)==BossRequestResult.Success && b.Capture().Health==2400,"Free retry failed.");
            })};
        int failed=0;foreach(var (name,test) in cases){try{test();Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e.Message);}}return failed;
    }
}
