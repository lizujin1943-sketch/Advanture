using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Advanture.HookIsland.Simulation;
using FrameSync.Abstractions;

namespace Advanture.HookIsland.RevivalTests;
internal static partial class Program
{
    private static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
    private static void Throws<T>(Action f) where T:Exception {try{f();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    private static int Main(string[] args)
    {
        int passed=0,total=0;
        foreach(var (name,test) in Cases().Concat(BoundaryCases()).Concat(ImmediateCases()).Concat(NetworkCases()).Concat(ImmediateNetworkCases()))
        {
            if(args.Length>0 && !name.Contains(args[0],StringComparison.Ordinal))continue;
            total++;try{test();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){Console.WriteLine("FAIL "+name+": "+e);}
        }
        Console.WriteLine($"{passed}/{total} passed");return passed==total && total>0?0:1;
    }
    private static IDeterministicSimulation<HookState> Model(int count=2,RevivalConfig? revival=null,EnemyConfig? enemies=null)
    {
        var map=IslandMap.CreatePlatform();
        if(revival!=null || enemies!=null)map=new IslandMap(map.Shore,map.Pier,map.Spawns,map.FishSpots,map.Platform,new FishingConfig(revival:revival,enemies:enemies));
        var m=new HookGame().Create();
        m.Initialize(new SimulationInitialization(new SessionDescriptor(new Guid("152c77a8-d515-4df2-a72b-1a384f4fa35a"),1,30,
            Enumerable.Range(0,count).Select(i=>new Participant((ushort)i,new Guid(i+1,0,0,new byte[8]))).ToArray()),new ByteString(map.Encode())));return m;
    }
    private static void Step(IDeterministicSimulation<HookState> m,params HookInput[] commands)
    {
        var s=m.Capture();m.Step(new FrameContext(s.Steps+1,s.Steps+1,30),s.Players.Select(p=>new SimulationInput(
            new Participant(p.Slot,new Guid(p.Slot+1,0,0,new byte[8])),new ByteString((p.Slot<commands.Length?commands[p.Slot]:default).Encode()))).ToArray());
    }
    private static void Change(IDeterministicSimulation<HookState> m,Action<Fisher[]> edit)
    {var s=m.Capture();var p=s.Players.ToArray();edit(p);m.Restore(new HookState(s.Steps,p,s.Fish,p.Sum(x=>x.Coins),s.Tiles,s.Driftwood,s.Tackle,s.Enemies));}
    private static void Down(IDeterministicSimulation<HookState> m,int slot,int coins=0,bool water=false)
    {Change(m,p=>{p[slot].Vitals.Health=0;p[slot].Coins=p[slot].CoinsEarned=coins;p[slot].Stamina=0;p[slot].X=slot*700;p[slot].Z=water?-4000:-2000;p[slot].Swimming.InWater=water;});}
    private static HookInput Help(int slot)=>new HookInput(0,0,revival:new RevivalCommand(rescueSlot:slot));
    private static HookInput Pay(Fisher p,uint request=1)=>new HookInput(0,0,revival:new RevivalCommand(respawnRequest:request,deathId:p.Revival.DownedId));
    private static void Wait(IDeterministicSimulation<HookState> m,int ticks,params HookInput[] c){for(int i=0;i<ticks;i++)Step(m,c);}
    private static IEnumerable<(string,Action)> Cases()
    {
        yield return ("fee_uses_simulation_time_and_floor_percentage_without_overflow",()=>{
            var c=new RevivalConfig();Require(c.Cost(8999,201)==30,"Initial fixed/percentage fee wrong.");
            Require(c.Cost(9000,201)==35 && c.Cost(18000,201)==40,"Five-minute growth boundary wrong.");
            Require(c.Cost(ulong.MaxValue,int.MaxValue)==int.MaxValue,"Fee overflowed.");
            Throws<InvalidDataException>(()=>new RevivalConfig(growthIntervalTicks:0));
        });
        yield return ("help_restores_health_for_free_without_refilling_stamina",()=>{
            var m=Model();Down(m,1,200);Change(m,p=>{p[0].X=0;p[0].Z=-2000;});Wait(m,89,Help(1));
            var p=m.Capture().Players[1];Require(p.Vitals.Downed && p.Revival.RescueTicks==89 && p.Coins==200,"Revived too early or charged for downing.");
            Step(m,Help(1));p=m.Capture().Players[1];Require(p.Vitals.Health==30 && p.Stamina==0 && p.Coins==200 && p.Revival.CoinsSpent==0,"Free teammate revival wrong.");
            Step(m,Help(1));Require(m.Capture().Players[1].Vitals.Health==30,"Holding help healed again.");
        });
        yield return ("downed_timeout_becomes_dead_without_charging_and_quote_stays_fixed",()=>{
            var m=Model();Down(m,1,200);Wait(m,1799);Require(!m.Capture().Players[1].Revival.Dead,"Died before 60s.");
            Step(m);var p=m.Capture().Players[1];Require(p.Revival.Dead && p.Revival.Deaths==1 && p.Revival.DeathCost==30 && p.Coins==200,"Final death or quote wrong.");
            Wait(m,120);Require(m.Capture().Players[1].Revival.Deaths==1 && m.Capture().Players[1].Revival.DeathCost==30,"Death repeated or quote changed.");
        });
        yield return ("paid_respawn_is_atomic_personal_and_duplicate_proof_after_restore",()=>{
            var m=Model(revival:new RevivalConfig(downedTicks:1));Down(m,1,200);Step(m);
            var cmd=Pay(m.Capture().Players[1]);Step(m,default,cmd);var s=m.Capture();var p=s.Players[1];
            Require(!p.Revival.Dead && p.Vitals.Health==100 && p.Stamina==120*30000 && !p.Swimming.InWater,"Respawn did not restore resources on land.");
            Require(p.Coins==170 && p.Revival.CoinsSpent==30 && s.Coins==170 && s.Players[0].Coins==0,"Respawn charged incorrect wallet/amount.");
            var copy=Model(revival:new RevivalConfig(downedTicks:1));copy.Restore(new HookGame().Decode(new HookGame().Encode(s)));
            Wait(copy,10,default,cmd);Require(copy.Capture().Players[1].Coins==170 && copy.Capture().Players[1].Revival.CoinsSpent==30,"Restored request charged twice.");
        });
        yield return ("insufficient_coins_never_charge_and_failed_request_is_not_replayed",()=>{
            var m=Model(revival:new RevivalConfig(downedTicks:1));Down(m,1,5);Step(m);var cmd=Pay(m.Capture().Players[1]);Step(m,default,cmd);
            var p=m.Capture().Players[1];Require(p.Revival.Dead && p.Coins==5 && p.Revival.LastResult==RespawnResult.InsufficientCoins,"Partial deduction or false revive.");
            Change(m,a=>{a[1].Coins+=100;a[1].CoinsEarned+=100;});Step(m,default,cmd);
            Require(m.Capture().Players[1].Revival.Dead && m.Capture().Players[1].Coins==105,"Failed request implicitly replayed.");
            Step(m,default,Pay(m.Capture().Players[1],2));Require(m.Capture().Players[1].Coins==95,"Fresh request did not use frozen death quote.");
        });
        yield return ("team_fails_only_when_everyone_dead_and_no_person_can_afford_own_fee",()=>{
            var m=Model(revival:new RevivalConfig(downedTicks:2));Down(m,0,5);Down(m,1,5);Step(m);
            Require(!m.Capture().GameOver,"Downed team declared failed.");Step(m);Require(m.Capture().GameOver,"All dead and broke did not fail.");
            m=Model(revival:new RevivalConfig(downedTicks:1));Down(m,0,11);Down(m,1,0);Step(m);
            Require(!m.Capture().GameOver,"One player can pay exact 11 G but team failed.");Step(m,Pay(m.Capture().Players[0]));
            Require(m.Capture().Players[0].Coins==0 && !m.Capture().Players[0].Vitals.Downed && m.Capture().Players[1].Revival.Dead,"Personal respawn affected teammate.");
        });
        yield return ("help_progress_resets_on_release_motion_or_competing_new_helper",()=>{
            var m=Model(3);Down(m,2);Change(m,p=>{p[0].X=300;p[0].Z=-2000;p[1].X=500;p[1].Z=-2000;});
            Wait(m,40,Help(2),Help(2));Require(m.Capture().Players[2].Revival.RescueTicks==40,"Multiple helpers sped progress.");
            Step(m,default,Help(2));Require(m.Capture().Players[2].Revival.RescueTicks==1,"New helper inherited prior progress.");
            Step(m);Require(m.Capture().Players[2].Revival.RescueTicks==0,"Released help retained progress.");
            Step(m,new HookInput(1000,0,revival:new RevivalCommand(rescueSlot:2)));Require(m.Capture().Players[2].Revival.RescueTicks==0,"Moving helper progressed.");
        });
        yield return ("rescue_completion_wins_same_tick_as_bleedout",()=>{
            var m=Model(revival:new RevivalConfig(downedTicks:90));Down(m,1);Change(m,p=>{p[0].X=0;p[0].Z=-2000;});Wait(m,90,Help(1));
            Require(m.Capture().Players[1].Vitals.Health==30 && m.Capture().Players[1].Revival.Deaths==0,"Bleedout won against completed help.");
        });
        yield return ("water_downed_requires_hook_to_land_before_free_help",()=>{
            var m=Model();Down(m,1,200,true);Change(m,p=>{p[0].X=700;p[0].Z=-2600;});Wait(m,20,Help(1));
            Require(m.Capture().Players[1].Revival.RescueTicks==0,"Revived across water.");Step(m,new HookInput(0,0,700,-4000,true));
            Wait(m,60);var p=m.Capture().Players[1];Require(!p.Swimming.InWater && p.Vitals.Downed && p.TimesHooked==1,"Hook did not land downed target or healed it.");
            Wait(m,90,Help(1));p=m.Capture().Players[1];Require(p.Vitals.Health==30 && p.Coins==200,"Landed teammate could not be rescued for free.");
        });
        yield return ("death_during_tow_releases_both_ends_and_dead_body_cannot_be_hooked",()=>{
            var m=Model(revival:new RevivalConfig(downedTicks:10));Down(m,1,200,true);Change(m,p=>{
                p[0].X=700;p[0].Z=-2600;p[0].Phase=HookPhase.Returning;p[0].HookedPlayer=1;p[0].HookX=700;p[0].HookZ=-4000;
                p[1].PulledBy=0;p[1].Revival.DownedTicks=9;});Step(m);var s=m.Capture();
            Require(s.Players[1].Revival.Dead && s.Players[1].PulledBy<0 && s.Players[0].HookedPlayer<0,"Death retained tow connection.");
            Wait(m,40);var dead=s.Players[1];Step(m,new HookInput(0,0,dead.X,dead.Z,true));Wait(m,90,Help(1));
            Require(m.Capture().Players[1].Revival.Dead && m.Capture().Players[1].TimesHooked==0,"Dead player was hooked or helped.");
        });
        yield return ("snapshot_rejects_forged_rescue_or_death_wallet_and_replays_progress",()=>{
            var m=Model();Down(m,1,200);Change(m,p=>{p[0].X=0;p[0].Z=-2000;});Wait(m,45,Help(1));
            var codec=new HookGame();var b=Model();b.Restore(codec.Decode(codec.Encode(m.Capture())));Wait(m,45,Help(1));Wait(b,45,Help(1));
            Require(codec.Encode(m.Capture()).SequenceEqual(codec.Encode(b.Capture())),"Rescue progress replay diverged.");
            Throws<InvalidDataException>(()=>Change(m,p=>p[1].Revival.CoinsSpent=50));
            Throws<InvalidDataException>(()=>Change(m,p=>{p[1].Revival.Rescuer=8;p[1].Revival.RescueTicks=40;}));
        });
    }
}
