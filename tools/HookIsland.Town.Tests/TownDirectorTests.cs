using System;
using System.Linq;
using Advanture.HookIsland.Simulation;

public static partial class TownGameplayTests
{
    public static int RunDirector()
    {
        int failed=0;
        void Test(string name,Action body){try{body();Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e.Message);}}
        Test("director_reuses_settled_town_slot_for_next_generation",()=>
        {
            var map=IslandMap.CreateTown();var roster=new EldritchSystem(map).Capture().Creatures.ToArray();
            var dead=Living(roster[0]);dead.Phase=EldritchPhase.Dead;dead.Health=0;dead.Timer=1;dead.RewardOwner=1;dead.Rewarded=true;roster[0]=dead;
            var system=new EldritchSystem(map,new EldritchState(roster));system.ActivateTownZone(0,true);
            var player=Model().Capture().Players[0];Position(ref player,new Point(-50000,-29000));
            for(int i=0;i<900 && system.Get(0).Generation<2;i++)system.Advance(TownLayout.InitialTiles(),new[]{player},_=>{});
            var next=system.Get(0);Require(next.Generation==2 && next.Alive,"Paid town corpse never became a new living generation");
            Require(!next.Rewarded && next.RewardOwner==0,"Next generation inherited the previous kill receipt");
        });
        Test("director_income_scales_with_explorers_and_elapsed_time",()=>
        {
            var roster=new EldritchSystem(IslandMap.CreateTown()).Capture().Creatures.ToArray();roster[0]=Living(roster[0]);
            int Income(int count,ulong elapsed)
            {
                var d=new TownSpawnDirector(7381,new TownDirectorState{RandomState=1,ElapsedTicks=elapsed});
                for(int t=0;t<30;t++)d.Advance(true,count,new[]{true,false,false,false,false,false},roster,1,_=>throw new Exception("Spawned above cap"));
                return d.Capture().CreditUnits;
            }
            Require(Income(1,0)==60 && Income(4,0)==168 && Income(1,3600)==90,"Time/player budget calculation lost its integer remainder");
        });
        Test("director_stops_in_safe_area_and_during_boss_without_banking",()=>
        {
            var d=new TownSpawnDirector(7);var roster=new EldritchSystem(IslandMap.CreateTown()).Capture().Creatures;
            for(int t=0;t<1200;t++)
            {
                d.Advance(false,4,new[]{true,false,false,false,false,false},roster,4,_=>throw new Exception("Spawned during boss"));
                d.Advance(true,0,new[]{true,false,false,false,false,false},roster,4,_=>throw new Exception("Spawned without explorers"));
                d.Advance(true,4,new bool[6],roster,4,_=>throw new Exception("Spawned in safe area"));
            }
            Require(d.Capture().Equals(default(TownDirectorState)),"Inactive director accumulated time or money");
        });
        Test("director_caps_bank_and_does_not_charge_blocked_ports",()=>
        {
            var d=new TownSpawnDirector(7,new TownDirectorState{RandomState=1,CreditUnits=2400});
            var roster=new EldritchSystem(IslandMap.CreateTown()).Capture().Creatures;int attempts=0;
            for(int t=0;t<300;t++)d.Advance(true,4,new[]{true,false,false,false,false,false},roster,4,_=>{attempts++;return false;});
            Require(d.Capture().CreditUnits==2400,"Blocked port spent credits or exceeded bank cap");
            Require(attempts>0 && attempts<=10,"Blocked port caused per-frame retry storm");
        });
        Test("director_waits_for_expensive_selected_card_and_spends_once",()=>
        {
            var d=new TownSpawnDirector(7,new TownDirectorState{RandomState=1,PendingCard=6,BurstRemaining=2});
            var roster=new EldritchSystem(IslandMap.CreateTown()).Capture().Creatures;int attempts=0;
            for(int t=0;t<359;t++)d.Advance(true,1,new[]{false,false,true,false,false,false},roster,4,_=>{attempts++;return true;});
            Require(attempts==0 && d.Capture().PendingCard==6,"Expensive guard was replaced by a cheaper card");
            d.Advance(true,1,new[]{false,false,true,false,false,false},roster,4,id=>{Require(id==5,"Wrong card bought");attempts++;return true;});
            Require(attempts==1 && d.Capture().CreditUnits==0 && d.Capture().WaitTicks==45,"Purchase cost or spacing is wrong");
        });
        Test("director_restores_rng_fraction_pending_card_and_cooldown",()=>
        {
            var roster=new EldritchSystem(IslandMap.CreateTown()).Capture().Creatures;
            var a=new TownSpawnDirector(7381);var zones=new[]{false,false,true,true,false,false};
            for(int t=0;t<137;t++)a.Advance(true,2,zones,roster,4,_=>false);
            var b=new TownSpawnDirector(7381,a.Capture());
            var first=new System.Collections.Generic.List<int>();var second=new System.Collections.Generic.List<int>();
            for(int t=0;t<3000;t++)
            {
                a.Advance(true,2,zones,roster,4,id=>{first.Add(id);return true;});
                b.Advance(true,2,zones,roster,4,id=>{second.Add(id);return true;});
                Require(a.Capture().Equals(b.Capture()),"Restored director diverged at frame "+t);
            }
            Require(first.Count>5 && first.SequenceEqual(second) && first.Distinct().Count()>1,"No reproducible weighted selections");
        });
        Test("director_abandons_card_when_its_district_sleeps",()=>
        {
            var d=new TownSpawnDirector(7,new TownDirectorState{RandomState=1,PendingCard=6,BurstRemaining=2,CreditUnits=2400});
            var roster=new EldritchSystem(IslandMap.CreateTown()).Capture().Creatures;int spawned=-1;
            d.Advance(true,1,new[]{true,false,false,false,false,false},roster,4,id=>{spawned=id;return true;});
            Require(spawned>=0 && spawned<3,"Pending card spawned inside a sleeping district");
        });
        Test("director_never_recycles_unpaid_rewards",()=>
        {
            var map=IslandMap.CreateTown();var roster=new EldritchSystem(map).Capture().Creatures.ToArray();
            var dead=Living(roster[0]);dead.Phase=EldritchPhase.Dead;dead.Health=0;dead.Timer=1;dead.RewardOwner=1;roster[0]=dead;
            var system=new EldritchSystem(map,new EldritchState(roster));system.ActivateTownZone(0,true);
            var p=Model().Capture().Players[0];Position(ref p,new Point(-50000,-29000));
            for(int i=0;i<600;i++)system.Advance(TownLayout.InitialTiles(),new[]{p},_=>{});
            Require(system.Get(0).Generation==1 && system.Get(0).Phase==EldritchPhase.Dead && system.Get(0).Timer==1,"Unpaid reward lost on recycling");
        });
        Test("director_cleared_zone_respawns_and_contract_pays_once_after_restore",()=>
        {
            var m=Model();var s=m.Capture();var ps=s.Players.ToArray();Position(ref ps[0],new Point(-56000,19000));
            var roster=s.Eldritch.Creatures.ToArray();var zones=s.Town.Zones.ToArray();zones[1]=new TownZoneState{Started=true};
            foreach(int id in new[]{3,4})
            {var c=Living(roster[id]);c.Phase=EldritchPhase.Dead;c.Health=0;c.Timer=1;c.RewardOwner=1;roster[id]=c;}
            m.Restore(Codec.Decode(Codec.Encode(With(s,ps,new TownState(true,s.Town.Players,zones,contracts:1),new EldritchState(roster)))));
            Step(m);Step(m);s=m.Capture();
            Require(s.Town.Zones[1].Cleared && s.Town.Zones[1].DefeatedAdults==2 && s.Players[0].Coins==438,"First clear or kill/contract transaction is wrong");
            m.Restore(Codec.Decode(Codec.Encode(s)));
            for(int i=0;i<600 && !m.Capture().Eldritch.Creatures.Skip(3).Take(2).Any(c=>c.Generation==2 && c.Alive);i++)Step(m);
            s=m.Capture();Require(s.Eldritch.Creatures.Skip(3).Take(2).Any(c=>c.Generation==2 && c.Alive),"Cleared district stayed permanently quiet");
            Require(s.Players[0].Coins==438 && s.Town.ContractsPaidMask==1,"Repeat spawn paid the first-clear contract again");
            // 再击杀一代：击杀金币照常发放，清理进度封顶、委托不能重复支付。
            roster=s.Eldritch.Creatures.ToArray();int next=Array.FindIndex(roster,c=>c.Id>=3 && c.Id<5 && c.Alive);
            var dead=roster[next];dead.Health=0;dead.Phase=EldritchPhase.Dead;dead.Timer=1;dead.Target=dead.Ally=-1;dead.RewardOwner=1;roster[next]=dead;
            m.Restore(With(s,eldritch:new EldritchState(roster,s.Eldritch.NextSpawn,s.Eldritch.Director)));
            Step(m);Step(m);s=m.Capture();
            Require(s.Town.Zones[1].DefeatedAdults==2 && s.Players[0].Coins==438+EldritchCatalog.Get(dead.Kind).Coins && s.Town.ContractsPaidMask==1,"Repeat kill duplicated first-clear progress or contract");
            m.Restore(Codec.Decode(Codec.Encode(s)));
        });
        Test("director_full_game_snapshot_replay_keeps_each_frame_identical",()=>
        {
            var a=Model(4);var s=a.Capture();var ps=s.Players.ToArray();
            for(int i=0;i<4;i++)Position(ref ps[i],new Point(-50000+i*800,-29000));
            a.Restore(With(s,ps));var b=Model(4);b.Restore(Codec.Decode(Codec.Encode(a.Capture())));bool sawSpawn=false;
            for(int t=0;t<600;t++)
            {
                MultiStep(a);MultiStep(b);var bytes=Codec.Encode(b.Capture());
                if(t%37==0)b.Restore(Codec.Decode(bytes));
                Require(Codec.Encode(a.Capture()).SequenceEqual(Codec.Encode(b.Capture())),"Full replay diverged at "+t);
                sawSpawn|=a.Capture().Eldritch.Creatures.Any(c=>c.Alive);
            }
            Require(sawSpawn && a.Capture().Eldritch.Director.ElapsedTicks>0,"Replay never exercised the director");
        });
        Test("director_boss_attempt_sleeps_enemies_and_freezes_pressure",()=>
        {
            var m=Model();PrepareFinale(m);var s=m.Capture();var roster=s.Eldritch.Creatures.ToArray();roster[0]=Living(roster[0]);
            var zones=s.Town.Zones.ToArray();zones[0]=new TownZoneState{Started=true};
            var d=new TownDirectorState{RandomState=123,ElapsedTicks=1,CreditUnits=600,WaitTicks=10};
            var town=s.Town;m.Restore(With(s,town:new TownState(true,town.Players,zones,goals:7),eldritch:new EldritchState(roster,director:d)));
            Step(m,new HookInput(0,0,town:new TownCommand(TownAction.ReadyBoss,1)));
            for(int i=0;i<120;i++)Step(m);s=m.Capture();
            Require(s.Boss.Active && s.Eldritch.Director.Equals(d),"BOSS fight continued ordinary monster pressure");
            Require(s.Eldritch.Creatures[0].Paused && !s.Eldritch.Creatures.Any(c=>c.Damageable),"Ordinary enemy could still attack during BOSS");
            m.Restore(Codec.Decode(Codec.Encode(s)));
        });
        Test("director_codec_rejects_corrupt_budget_rng_clock_card_and_progress",()=>
        {
            var m=Model();var s=m.Capture();var ps=s.Players.ToArray();Position(ref ps[0],new Point(-50000,-29000));m.Restore(With(s,ps));
            for(int i=0;i<100;i++)Step(m);s=m.Capture();Codec.Decode(Codec.Encode(s));
            void Reject(HookState corrupt)
            {bool rejected=false;try{Codec.Decode(Codec.Encode(corrupt));}catch(System.IO.InvalidDataException){rejected=true;}Require(rejected,"Corrupt director snapshot accepted");}
            foreach(int field in Enumerable.Range(0,7))
            {
                var d=s.Eldritch.Director;
                switch(field){case 0:d.CreditUnits=-1;break;case 1:d.CreditUnits=2401;break;case 2:d.IncomeRemainder=5;break;case 3:d.RandomState=0;break;case 4:d.ElapsedTicks=s.Steps+1;break;case 5:d.PendingCard=16;break;case 6:d.WaitTicks=151;break;}
                Reject(With(s,eldritch:new EldritchState(s.Eldritch.Creatures,s.Eldritch.NextSpawn,d)));
            }
            var zones=s.Town.Zones.ToArray();zones[0].Cleared=true;
            Reject(With(s,town:new TownState(true,s.Town.Players,zones)));
        });
        Test("director_keeps_spacing_and_a_rest_between_bursts",()=>
        {
            var d=new TownSpawnDirector(7,new TownDirectorState{RandomState=1,CreditUnits=2400});
            var roster=new EldritchSystem(IslandMap.CreateTown()).Capture().Creatures;var times=new System.Collections.Generic.List<int>();
            for(int tick=0;tick<=195;tick++)d.Advance(true,1,new[]{true,false,false,false,false,false},roster,4,_=>{times.Add(tick);return true;});
            Require(times.SequenceEqual(new[]{0,45,195}),"Director skipped spacing or post-burst rest");
        });
        Test("director_recycles_paid_children_without_resetting_mothers_quota",()=>
        {
            var map=IslandMap.CreateTown();var roster=new EldritchSystem(map).Capture().Creatures.ToArray();
            var mother=Living(roster[13]);mother.Summons=4;roster[13]=mother;
            var child=Living(roster[15]);child.Phase=EldritchPhase.Dead;child.Health=0;child.Timer=1;child.RewardOwner=1;child.Rewarded=true;roster[15]=child;
            var system=new EldritchSystem(map,new EldritchState(roster));system.ActivateTownZone(5,true);
            var p=Model().Capture().Players[0];Position(ref p,new Point(20000,-38000));
            system.Advance(TownLayout.InitialTiles(),new[]{p},_=>{},allowSpawn:false);
            Require(system.Get(15).Phase==EldritchPhase.Dormant && system.Get(15).Generation==1 && !system.Get(15).Rewarded,"Child slot retained paid death");
            Require(system.Get(13).Summons==4,"Recycling a child reset the living mother's per-generation quota");
        });
        Test("director_service_entrance_freezes_authority_without_local_ui_flags",()=>
        {
            var m=Model();var s=m.Capture();var ps=s.Players.ToArray();
            Position(ref ps[0],TownLayout.Buildings[1].Entrance);var zones=s.Town.Zones.ToArray();zones[0]=new TownZoneState{Started=true};
            m.Restore(With(s,ps,new TownState(true,s.Town.Players,zones)));
            // 正式城镇服务窗口只保留本地 Open，不发送旧海岛商店的 Shopping 位；这里必须按真实输入验证。
            for(int i=0;i<400;i++)Step(m);
            Require(m.Capture().Eldritch.Director.Equals(default(TownDirectorState)),"Armory service area accumulated spawn budget without a shopping input flag");
            Require(m.Capture().Town.Zones[0].Paused,"Service visitor retained the active district indefinitely");
            s=m.Capture();ps=s.Players.ToArray();Position(ref ps[0],new Point(-50000,-29000));m.Restore(With(s,ps));
            for(int i=0;i<120;i++)Step(m);
            Require(m.Capture().Eldritch.Director.ElapsedTicks>0 && !m.Capture().Town.Zones[0].Paused,"Director did not resume after leaving service area");
            m.Restore(Codec.Decode(Codec.Encode(m.Capture())));
        });
        return failed;
    }
}
