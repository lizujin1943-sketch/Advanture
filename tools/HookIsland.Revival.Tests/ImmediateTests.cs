using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.RevivalTests;
internal static partial class Program
{
    private static HookInput ImmediatePay(uint request=1,uint episode=1)=>new HookInput(0,0,revival:new RevivalCommand(respawnRequest:request,deathId:episode));
    private static IEnumerable<(string,Action)> ImmediateCases()
    {
        yield return ("immediate_solo_lethal_frame_automatically_pays_once_and_restores",()=>{
            var m=Model(1);Down(m,0,200,true);Change(m,p=>{p[0].Vitals.Health=1;p[0].Swimming.DamageTicks=29;});
            Step(m);var s=m.Capture();var p=s.Players[0];
            Require(!p.Vitals.Downed && !p.Revival.Dead && !p.Swimming.InWater && p.Coins==170 && p.Revival.CoinsSpent==30,"Solo lethal frame must respawn on land and charge exactly 30 G without waiting or input.");
            Require(p.Vitals.Health==100 && p.Stamina==120*30000 && p.Revival.Deaths==1,"Solo respawn must use normal restoration and death accounting.");
            var copy=Model(1);copy.Restore(new HookGame().Decode(new HookGame().Encode(s)));Wait(copy,10);
            Require(copy.Capture().Players[0].Coins==170 && copy.Capture().Players[0].Revival.CoinsSpent==30,"Restored automatic respawn charged again.");
        });
        yield return ("immediate_solo_insufficient_funds_fails_without_rescue_window",()=>{
            var m=Model(1);Down(m,0,5);Step(m);var s=m.Capture();
            Require(s.GameOver && s.Players[0].Revival.Dead && s.Players[0].Coins==5 && s.Players[0].Revival.CoinsSpent==0,"Broke solo player must fail immediately without a partial payment.");
            Step(m);Require(m.Capture().Steps==s.Steps,"Failed solo world must freeze.");
        });
        yield return ("immediate_solo_exact_balance_revives_instead_of_failing",()=>{
            var m=Model(1);Down(m,0,11);Step(m);var s=m.Capture();
            Require(!s.GameOver && !s.Players[0].Vitals.Downed && s.Players[0].Coins==0 && s.Players[0].Revival.CoinsSpent==11,"Exact revival fee must succeed automatically.");
        });
        yield return ("immediate_multiplayer_can_pay_while_downed_without_waiting",()=>{
            var m=Model();Down(m,1,200);Step(m);var before=m.Capture().Players[1];
            Require(!before.Revival.Dead && before.Vitals.Downed,"Multiplayer must keep the rescue choice.");
            Step(m,default,ImmediatePay());var s=m.Capture();var p=s.Players[1];
            Require(!p.Vitals.Downed && p.Coins==170 && p.Revival.CoinsSpent==30 && s.Players[0].Coins==0,"Downed player could not buy an immediate personal respawn.");
            Wait(m,10,default,ImmediatePay());Require(m.Capture().Players[1].Coins==170,"Repeated click charged twice.");
        });
        yield return ("immediate_free_rescue_invalidates_old_request_even_after_next_downing",()=>{
            var m=Model();Down(m,1,200);Change(m,p=>{p[0].X=0;p[0].Z=-2000;});Step(m);Wait(m,90,Help(1));
            Require(!m.Capture().Players[1].Vitals.Downed && m.Capture().Players[1].Coins==200,"Free rescue changed.");
            Down(m,1,200);Step(m);Step(m,default,ImmediatePay());
            var p=m.Capture().Players[1];Require(p.Vitals.Downed && p.Coins==200 && p.Revival.LastResult==RespawnResult.StaleDeath,"Delayed request from the previous rescue episode charged a later downing.");
            Step(m,default,ImmediatePay(2,2));Require(!m.Capture().Players[1].Vitals.Downed && m.Capture().Players[1].Coins==170,"Fresh request for this downing failed.");
        });
        yield return ("immediate_insufficient_payment_keeps_free_rescue_available",()=>{
            var m=Model();Down(m,1,5);Change(m,p=>{p[0].X=0;p[0].Z=-2000;});Step(m);Step(m,default,ImmediatePay());
            var p=m.Capture().Players[1];Require(p.Vitals.Downed && !p.Revival.Dead && p.Coins==5 && p.Revival.LastResult==RespawnResult.InsufficientCoins,"Failed early payment killed or charged player.");
            Wait(m,90,Help(1));Require(m.Capture().Players[1].Vitals.Health==30 && m.Capture().Players[1].Coins==5,"Failed early payment blocked free rescue.");
        });
        yield return ("immediate_payment_during_tow_releases_both_ends",()=>{
            var m=Model();Down(m,1,200,true);Change(m,p=>{p[0].X=700;p[0].Z=-2600;});Step(m);
            Step(m,new HookInput(0,0,700,-4000,true));
            for(int n=0;n<15 && m.Capture().Players[1].PulledBy<0;n++)Step(m);
            Require(m.Capture().Players[1].PulledBy==0,"Fixture never began water rescue tow.");
            Step(m,default,ImmediatePay());var s=m.Capture();
            Require(!s.Players[1].Vitals.Downed && s.Players[1].PulledBy<0 && s.Players[0].HookedPlayer<0,"Early paid respawn retained a tow link.");
            new HookGame().Decode(new HookGame().Encode(s));
        });
        yield return ("immediate_quote_and_episode_survive_downed_snapshot_without_repricing",()=>{
            var config=new RevivalConfig(growthIntervalTicks:10,downedTicks:100);
            var m=Model(revival:config);Down(m,1,200);Step(m);var before=m.Capture();
            var copy=Model(revival:config);copy.Restore(new HookGame().Decode(new HookGame().Encode(before)));Wait(copy,15);
            Step(copy,default,ImmediatePay());var p=copy.Capture().Players[1];
            Require(!p.Vitals.Downed && p.Coins==170 && p.Revival.CoinsSpent==30,"Downed snapshot lost its quote or payment identity at a fee growth boundary.");
        });
        yield return ("immediate_solo_ignores_reserved_seats_but_counts_disconnected_teammate",()=>{
            var m=Model(4);Change(m,p=>{for(int i=1;i<4;i++){p[i].WaitingToJoin=true;p[i].Unavailable=true;}});Down(m,0,200);Step(m);
            Require(!m.Capture().Players[0].Vitals.Downed && m.Capture().Players[0].Coins==170,"Empty reserved slots prevented solo automatic revival.");
            m=Model();Change(m,p=>p[1].Unavailable=true);Down(m,0,200);Step(m);
            Require(m.Capture().Players[0].Vitals.Downed && !m.Capture().Players[0].Revival.Dead && m.Capture().Players[0].Coins==200,"A disconnected teammate silently switched multiplayer to automatic spending.");
        });
    }
}
