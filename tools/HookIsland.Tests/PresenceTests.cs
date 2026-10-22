using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Advanture.HookIsland.Simulation;
using FrameSync.Abstractions;

namespace Advanture.HookIsland.Tests;
internal static partial class Program
{
    private static IDeterministicSimulation<HookState> PresenceModel(int count=2,int active=1,EnemyConfig? enemies=null)
    {
        var basis=IslandMap.CreatePlatform();var map=new IslandMap(basis.Shore,basis.Pier,basis.Spawns,basis.FishSpots,basis.Platform,
            new FishingConfig(enemies:enemies??EnemyConfig.Disabled));
        var session=new SessionDescriptor(Id,1,30,Enumerable.Range(0,count).Select(i=>new Participant((ushort)i,new Guid(i+1,0,0,new byte[8]),isActive:i<active)).ToArray());
        var model=new HookGame().Create();model.Initialize(new SimulationInitialization(session,new ByteString(map.Encode())));return model;
    }
    private static void PresenceEdit(IDeterministicSimulation<HookState> model,Action<Fisher[]> edit)
    {
        var s=model.Capture();var players=s.Players.ToArray();edit(players);
        model.Restore(new HookState(s.Steps,players,s.Fish,s.Coins,s.Tiles,s.Driftwood,s.Tackle,s.Enemies,s.RoomRules,s.Treasure,s.Commissions,s.Ocean,s.Finale));
    }
    private static void PresenceEvent(IDeterministicSimulation<HookState> model,int slot,MemberEventKind kind)
    {
        ulong next=model.Capture().Steps+1;
        model.Step(new FrameContext(next,next,30,new[]{new MemberEvent(next,kind,(ushort)slot,kind==MemberEventKind.Rebind?2UL:1UL)}),Array.Empty<SimulationInput>());
    }
    private static IEnumerable<(string,Action)> PresenceCases()
    {
        yield return ("presence_terminal_defeat_does_not_spawn_a_reserved_body",()=>{
            var m=PresenceModel();
            for(int i=0;i<4000 && !m.Capture().GameOver;i++)ShopStep(m,new HookInput(0,-1000).Encode());
            var defeated=m.Capture();Require(defeated.GameOver && defeated.Players[1].WaitingToJoin,"Normal water damage did not produce the defeat fixture.");
            PresenceEvent(m,1,MemberEventKind.Rebind);var after=m.Capture();
            Require(after.GameOver && after.Players[1].WaitingToJoin && after.Players[1].Unavailable,"A terminal rebind spawned a fresh body and reversed defeat.");
            var codec=new HookGame();Require(codec.Encode(defeated).SequenceEqual(codec.Encode(after)),"Terminal resume advanced the frozen world.");
            m.Restore(codec.Decode(codec.Encode(after)));ShopStep(m,new HookInput(1000,0).Encode());
            Require(codec.Encode(after).SequenceEqual(codec.Encode(m.Capture())),"Restoring a terminal spectator resumed gameplay.");
        });
        yield return ("presence_reserved_seat_enters_only_on_confirmed_membership",()=>{
            var m=PresenceModel();var before=m.Capture();
            Require(!before.Players[0].Unavailable && before.Players[1].Unavailable && before.Players[1].WaitingToJoin,"Reserved seat appeared before membership activation.");
            PresenceEvent(m,1,MemberEventKind.Rebind);var p=m.Capture().Players[1];
            Require(!p.WaitingToJoin && !p.Unavailable && p.Coins==0 && p.Vitals.Health==before.Players[1].Vitals.Health,"First activation did not preserve the initial personal state.");
        });
        yield return ("presence_reserved_seats_do_not_block_landings_or_defeat",()=>{
            var m=PresenceModel();var s=m.Capture();var players=s.Players.ToArray();
            Require(SwimmingTerrain.Landing(IslandMap.CreatePlatform(),s.Tiles,players,0,players[1].X,players[1].Z),"Invisible reserved seat blocked landing.");
            players[0].Vitals.Health=0;players[0].Revival.Dead=true;players[0].Revival.DeathCost=1;
            Require(RevivalSystem.GameOver(players),"A reserved seat prevented actual team defeat.");
            Require(!RevivalSystem.GameOver(PresenceModel(active:0).Capture().Players),"An empty world was marked defeated.");
        });
        yield return ("presence_reserved_seat_does_not_recover_or_take_water_damage",()=>{
            var m=PresenceModel();PresenceEdit(m,p=>{p[1].X=0;p[1].Z=-4000;p[1].Swimming.InWater=true;p[1].Stamina=0;});
            var initial=m.Capture().Players[1];for(int i=0;i<60;i++)ShopStep(m);
            var p=m.Capture().Players[1];
            Require(p.Vitals.Health==initial.Vitals.Health && p.Stamina==0 && p.Revival.DownedTicks==0,"Reserved body participated in world timers.");
        });
        yield return ("presence_disconnect_does_not_cancel_confirmed_knockback",()=>{
            var m=PresenceModel(1,1,new EnemyConfig(piranhaCount:0,monsterEnabled:true,initialDelayTicks:18000));
            PresenceEdit(m,p=>{p[0].X=0;p[0].Z=0;KnockbackSystem.Start(ref p[0],1000,0);});
            PresenceEvent(m,0,MemberEventKind.Deactivate);var s=m.Capture();
            Require(s.Players[0].Unavailable && s.Players[0].X>0 && s.Players[0].Knockback.Active,"Disconnect cancelled knockback.");
            var codec=new HookGame();Require(codec.Encode(s).SequenceEqual(codec.Encode(codec.Decode(codec.Encode(s)))),"Offline external movement cannot round-trip.");
            for(int i=0;i<20;i++)ShopStep(m);var after=m.Capture().Players[0];
            Require(after.X==4000 && after.Swimming.InWater && !after.Knockback.Active,"Offline knockback did not finish in water.");
        });
        yield return ("presence_offline_swimmer_takes_damage_and_can_be_hook_rescued",()=>{
            var m=PresenceModel(2,2);PresenceEdit(m,p=>{p[0].X=0;p[0].Z=-3200;p[0].Swimming.InWater=true;p[0].Stamina=0;p[0].RegenDelay=0;p[1].X=0;p[1].Z=-2400;});
            int health=m.Capture().Players[0].Vitals.Health;
            PresenceEvent(m,0,MemberEventKind.Deactivate);for(int i=0;i<30;i++)ShopStep(m);
            var hurt=m.Capture().Players[0];Require(hurt.Vitals.Health<health && hurt.Stamina>0,"Offline swimmer ignored natural damage/recovery.");
            ShopStep(m,default(HookInput).Encode(),new HookInput(0,0,0,-3200,true).Encode());
            for(int i=0;i<35;i++)ShopStep(m);
            var rescued=m.Capture().Players[0];Require(rescued.Unavailable && rescued.TimesHooked==1 && !rescued.Swimming.InWater,"Offline swimmer could not be rescued by teammate.");
            int savedHealth=rescued.Vitals.Health;int savedStamina=rescued.Stamina;
            PresenceEvent(m,0,MemberEventKind.Rebind);var rejoined=m.Capture().Players[0];
            Require(!rejoined.Unavailable && rejoined.X==rescued.X && rejoined.Z==rescued.Z && rejoined.Vitals.Health==savedHealth && rejoined.Stamina==savedStamina+5000,"Rebind reset position or personal survival state instead of one normal recovery tick.");
        });
        yield return ("presence_offline_downed_teammate_can_be_revived",()=>{
            var m=PresenceModel(2,2);PresenceEdit(m,p=>{p[0].X=0;p[0].Z=0;p[0].Vitals.Health=0;p[1].X=800;p[1].Z=0;});
            PresenceEvent(m,0,MemberEventKind.Deactivate);var s=m.Capture();
            var basis=IslandMap.CreatePlatform();var map=new IslandMap(basis.Shore,basis.Pier,basis.Spawns,basis.FishSpots,basis.Platform,s.Tackle!.Config);
            Require(RevivalSystem.CanHelp(map,s.Tiles,s.Players[1],s.Players[0]),"Offline downed target cannot receive help.");
            for(int i=0;i<90;i++)
            {
                ShopStep(m,default(HookInput).Encode(),new HookInput(0,0,revival:new RevivalCommand(0)).Encode());
                var codec=new HookGame();codec.Decode(codec.Encode(m.Capture()));
            }
            var rescued=m.Capture().Players[0];
            Require(rescued.Unavailable && rescued.Vitals.Health==30 && rescued.Revival.DownedTicks==0,"Teammate did not complete the offline rescue.");
        });
        yield return ("presence_enemies_ignore_reserved_seats_but_attack_offline_bodies",()=>{
            var system=new EnemySystem(new EnemyConfig(piranhaCount:1,monsterEnabled:false));var f=system.GetPiranha(0);int hits=0;
            var p=new Fisher {Slot=0,X=f.X,Z=f.Z,Unavailable=true,WaitingToJoin=true,Vitals=new CharacterVitals {Health=100},Swimming=new SwimmingState {InWater=true}};
            system.Advance(new[]{p},_=>hits++);Require(hits==0,"Enemy attacked reserved seat.");
            p.WaitingToJoin=false;system.Advance(new[]{p},_=>hits++);Require(hits==1,"Disconnect made an entered body immune to piranhas.");
        });
        yield return ("presence_incoming_rescue_continues_across_disconnect",()=>{
            var m=PresenceModel(2,2);PresenceEdit(m,p=>{p[0].X=0;p[0].Z=-4400;p[0].Swimming.InWater=true;p[1].X=0;p[1].Z=-2400;});
            ShopStep(m,default(HookInput).Encode(),new HookInput(0,0,0,-4400,true).Encode());
            for(int i=0;i<15 && m.Capture().Players[0].PulledBy<0;i++)ShopStep(m);
            Require(m.Capture().Players[0].PulledBy==1,"Rescue fixture did not hook the swimmer.");
            PresenceEvent(m,0,MemberEventKind.Deactivate);
            Require(m.Capture().Players[0].PulledBy==1,"Disconnect released another player's rescue hook.");
            for(int i=0;i<40;i++)ShopStep(m);
            Require(!m.Capture().Players[0].Swimming.InWater && m.Capture().Players[0].Unavailable,"Existing rescue did not finish after target disconnected.");
        });
        yield return ("presence_reserved_seats_do_not_inflate_finale_requirement",()=>{
            var s=PresenceModel(4,1).Capture();
            Require(FinaleRules.RequiredHooks(new FinaleConfig(),s.Players)==1,"Three unused seats required extra finale hooks.");
            var codec=new HookGame();var bytes=codec.Encode(s);var restored=codec.Decode(bytes);
            Require(restored.Players.Skip(1).All(p=>p.WaitingToJoin && p.Unavailable),"Snapshot lost unused seats.");
            bytes[bytes.Length-1]=2;bool rejected=false;
            try{codec.Decode(bytes);}catch(InvalidDataException){rejected=true;}
            Require(rejected,"Noncanonical presence flag was accepted.");
        });
        yield return ("presence_victory_all_offline_and_rebind_preserve_results",()=>{
            var m=FinaleReadyModel(config:new FinaleConfig(holdTicks:2),hooked:true,completed:2,progress:1);ShopStep(m);
            var won=m.Capture();PresenceEvent(m,0,MemberEventKind.Deactivate);var offline=m.Capture();
            Require(offline.Won && offline.Players[0].Unavailable && offline.Steps==won.Steps,"Victory prevented membership updates or resumed gameplay time.");
            var codec=new HookGame();codec.Decode(codec.Encode(offline));
            PresenceEvent(m,0,MemberEventKind.Rebind);var restored=m.Capture();
            Require(!restored.Players[0].Unavailable && codec.Encode(restored).SequenceEqual(codec.Encode(won)),"Victory rejoin changed the result or personal state.");
        });
    }
}
